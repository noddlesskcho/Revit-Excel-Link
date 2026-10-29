using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using RevitScheduleSync.Logging;
using RevitScheduleSync.Models;
using RevitScheduleSync.Models.Export;
using RevitScheduleSync.Revit.Parameters;
using RevitScheduleSync.Revit.Schedules;
using RevitScheduleSync.Revit.Styles;

namespace RevitScheduleSync.Revit.ModelData
{
    /// <summary>
    /// Builds the sheets of a "Model data" export (categories, selection, types, rooms, spaces, project
    /// information, object and line styles, families) as plain ExportSheet data, in the same shape as
    /// schedule sheets, so the writer, reader and import analysis are shared. Read-only.
    /// </summary>
    internal sealed class ModelSheetBuilder
    {
        private const string LockElementReadOnly = "read-only on that element";
        private const string LockInGroup = "element is in a model group";
        private const string LockLinked = "element is in a linked model";
        private const string LockExistingPhase = "the phase of an existing element cannot be changed";

        private static readonly ParameterIdentity ElementIdInfo = Info("ElementId", "Element ID");
        private static readonly ParameterIdentity FamilyInfo = Info("Family", "Family");
        private static readonly ParameterIdentity TypeInfo = Info("Type", "Type");
        private static readonly ParameterIdentity PhaseInfo = Info("Phase", "Phase");
        private static readonly ParameterIdentity CategoryInfo = Info("Category", "Category");
        private static readonly ParameterIdentity SubcategoryInfo = Info("Subcategory", "Subcategory");

        private readonly Document _document;
        private readonly ModelCatalog _catalog;
        private readonly ParameterLocator _parameters;
        private readonly ReferenceResolver _references;
        private readonly ILogger _logger;
        private readonly Units _units;

        public ModelSheetBuilder(Document document, ModelCatalog catalog, ILogger logger)
        {
            _document = document;
            _catalog = catalog;
            _parameters = new ParameterLocator(document);
            _references = new ReferenceResolver(document);
            _logger = logger;
            _units = document.GetUnits();
        }

        public IReadOnlyList<ExportSheet> Build(ModelExportRequest request)
        {
            switch (request.Source)
            {
                case ModelSourceKind.ObjectStyles:
                    return [BuildStyles(lineStyles: false)];
                case ModelSourceKind.LineStyles:
                    return [BuildStyles(lineStyles: true)];
                case ModelSourceKind.Families:
                    return [BuildFamilies()];
                case ModelSourceKind.Rooms:
                case ModelSourceKind.Spaces:
                case ModelSourceKind.ProjectInformation:
                    return [BuildElementSheet(request, ModelCatalog.CategoryIdFor(request.Source))];
                default:
                    return request.CategoryIds.Select(id => BuildElementSheet(request, id)).Where(s => s.Rows.Count > 0).ToList();
            }
        }

        // ---- Element sheets ---------------------------------------------------------------------------

        private ExportSheet BuildElementSheet(ModelExportRequest request, long categoryId)
        {
            bool spatial = request.Source is ModelSourceKind.Rooms or ModelSourceKind.Spaces;
            bool byType = request.ExportByType && !spatial && request.Source != ModelSourceKind.ProjectInformation;
            var category = Category.GetCategory(_document, new ElementId(categoryId));
            string categoryName = category?.Name ?? $"Category {categoryId}";

            var elements = spatial && request.EmptyTemplate
                ? []
                : _catalog.Elements(request.Source, request.Scope, categoryId, byType);

            var choices = _catalog.Parameters(request.Source, request.Scope, [categoryId], byType)
                .ToDictionary(c => c.Key, StringComparer.Ordinal);

            // Rooms and spaces always carry Number and Name (required for new rows), in that order.
            var keys = request.ParameterKeys.ToList();
            if (spatial)
            {
                foreach (var required in new[] { BuiltInParameter.ROOM_NAME, BuiltInParameter.ROOM_NUMBER })
                {
                    var key = $"I|bip:{required}";
                    keys.Remove(key);
                    keys.Insert(0, key);

                    // No room or space in the model yet (empty template): the catalog found no parameters.
                    choices.TryAdd(key, new ParameterChoice
                    {
                        Parameter = new ParameterIdentity
                        {
                            Source = ParameterSource.BuiltIn,
                            Name = LabelUtils.GetLabelFor(required),
                            BuiltInName = required.ToString(),
                            BuiltInValue = (long)required
                        },
                        IsType = false,
                        Storage = ValueStorage.String,
                        IsReadOnly = false,
                        GroupName = "Identity Data"
                    });
                }
            }

            var parameterColumns = keys
                .Where(choices.ContainsKey)
                .Select(k => choices[k])
                .Where(c => !byType || !c.IsType)
                .ToList();

            // Leading information columns.
            var columns = new List<ExportColumn> { InfoColumn(ElementIdInfo, 1) };
            if (!spatial && request.Source != ModelSourceKind.ProjectInformation)
            {
                columns.Add(InfoColumn(FamilyInfo, 2));
                columns.Add(InfoColumn(TypeInfo, 3));
            }

            if (spatial)
            {
                var phases = new FilteredElementCollector(_document).OfClass(typeof(Phase)).Select(p => p.Name).ToList();
                columns.Add(InfoColumn(PhaseInfo, 2) with { IsEditable = true, ListValues = phases, RequiredForNewRows = true });
            }

            int fieldId = 100;
            var headings = new HashSet<string>(columns.Select(c => c.Field.Heading), StringComparer.OrdinalIgnoreCase);
            var sampleParameters = new List<Parameter?>();
            foreach (var choice in parameterColumns)
            {
                var heading = UniqueHeading(choice.Name + (choice.IsType ? " (Type)" : string.Empty), headings);
                var sample = SampleParameter(elements, choice);
                sampleParameters.Add(sample);
                var column = ParameterColumn(fieldId++, heading, choice, sample);
                if (spatial && choice.Parameter.BuiltInName is nameof(BuiltInParameter.ROOM_NUMBER) or nameof(BuiltInParameter.ROOM_NAME))
                {
                    column = column with { RequiredForNewRows = true };
                }

                columns.Add(column);
            }

            var rows = new List<ExportRow>();
            var typeCache = new Dictionary<long, Element?>();
            foreach (var element in elements)
            {
                rows.Add(BuildRow(element, byType ? null : GetType(element, typeCache), columns, parameterColumns, spatial, linkName: null));
            }

            int linkedRows = 0;
            if (request.IncludeLinkedModels && !byType && request.Source is ModelSourceKind.ModelCategories or ModelSourceKind.AnnotationCategories
                    or ModelSourceKind.Rooms or ModelSourceKind.Spaces)
            {
                foreach (var (linkName, linkElements) in LinkedElements(categoryId))
                {
                    foreach (var element in linkElements)
                    {
                        rows.Add(BuildRow(element, GetLinkedType(element), columns, parameterColumns, spatial, linkName));
                        linkedRows++;
                    }
                }
            }

            var notes = new List<string>();
            if (linkedRows > 0)
            {
                notes.Add($"{categoryName}: {linkedRows} row(s) from linked models are read-only.");
            }

            int skipped = request.ParameterKeys.Count(k => !choices.ContainsKey(k));
            if (skipped > 0 && !spatial)
            {
                notes.Add($"{categoryName}: {skipped} selected parameter(s) do not exist in this category.");
            }

            _logger.Info($"Model sheet '{categoryName}': {rows.Count} rows, {columns.Count} columns, {columns.Count(c => c.IsEditable)} editable.");

            var source = request.Source switch
            {
                ModelSourceKind.Rooms => SheetSource.Rooms,
                ModelSourceKind.Spaces => SheetSource.Spaces,
                ModelSourceKind.ProjectInformation => SheetSource.ProjectInformation,
                _ => byType ? SheetSource.CategoryTypes : SheetSource.Category
            };

            return new ExportSheet
            {
                Source = source,
                AllowNewRows = spatial,
                ScheduleUniqueId = $"category:{categoryId}{(byType ? ":types" : string.Empty)}",
                ScheduleElementId = categoryId,
                ScheduleName = byType ? $"{categoryName} Types" : categoryName,
                CategoryName = categoryName,
                Mode = SheetMode.Editable,
                Columns = columns,
                Rows = rows,
                Notes = notes
            };
        }

        private ExportRow BuildRow(
            Element element, Element? type, List<ExportColumn> columns, List<ParameterChoice> parameterColumns, bool spatial, string? linkName)
        {
            bool inGroup = element.GroupId != ElementId.InvalidElementId;
            var cells = new List<ExportCell>(columns.Count);
            int parameterIndex = 0;

            foreach (var column in columns)
            {
                ExportCell cell;
                if (column.Field.Parameter.Source == ParameterSource.Info)
                {
                    cell = InfoCell(element, type, column.Field.Parameter.BuiltInName!);
                    if (column.IsEditable)
                    {
                        cell = cell with { ForceReadOnly = true, LockReason = LockExistingPhase };
                    }
                }
                else
                {
                    var choice = parameterColumns[parameterIndex++];
                    var target = choice.IsType ? type : element;
                    var parameter = target == null ? null : FindParameter(target, choice.Parameter, linkName != null);
                    if (parameter == null)
                    {
                        cell = ExportCell.Unavailable;
                    }
                    else
                    {
                        cell = SafeRead(parameter, column, element);
                        if (column.IsEditable && parameter.IsReadOnly && !cell.ForceReadOnly && cell.IsAvailable)
                        {
                            cell = cell with { ForceReadOnly = true, LockReason = LockElementReadOnly };
                        }
                        else if (column.IsEditable && inGroup && !choice.IsType && cell.IsAvailable && !cell.ForceReadOnly)
                        {
                            cell = cell with { ForceReadOnly = true, LockReason = LockInGroup };
                        }
                    }
                }

                if (linkName != null && cell.IsAvailable)
                {
                    cell = cell with { ForceReadOnly = true, LockReason = LockLinked };
                }

                cells.Add(cell);
            }

            return new ExportRow
            {
                ElementUniqueId = linkName == null ? element.UniqueId : $"link:{linkName}:{element.UniqueId}",
                ElementId = element.Id.Value,
                TypeUniqueId = linkName == null ? type?.UniqueId : null,
                TypeId = type?.Id.Value,
                CategoryName = element.Category?.Name ?? string.Empty,
                InGroup = inGroup,
                DesignOptionName = SafeDesignOptionName(element),
                LinkName = linkName,
                Cells = cells
            };
        }

        private ExportCell InfoCell(Element element, Element? type, string key)
        {
            string text = key switch
            {
                "ElementId" => element.Id.Value.ToString(CultureInfo.InvariantCulture),
                "Family" => element is ElementType t ? t.FamilyName : (type as ElementType)?.FamilyName ?? string.Empty,
                "Type" => element is ElementType ? element.Name : type?.Name ?? string.Empty,
                "Phase" => element.get_Parameter(BuiltInParameter.ROOM_PHASE)?.AsValueString() ?? string.Empty,
                _ => string.Empty
            };

            var cell = ParameterValueReader.Text(text, text, text.Length > 0, text);

            // The Phase column is editable only for new rows: Revit cannot move an existing room or space to
            // another phase, and import ignores this column on existing rows, so lock it there.
            return key == "Phase"
                ? cell with { ForceReadOnly = true, LockReason = "the phase of an existing room or space cannot be changed" }
                : cell;
        }

        private ExportCell SafeRead(Parameter parameter, ExportColumn column, Element element)
        {
            try
            {
                return ParameterValueReader.Read(parameter, column, null);
            }
            catch (Exception ex)
            {
                _logger.Warning($"Could not read '{column.Field.Heading}' on element {element.Id.Value}.", ex);
                return ExportCell.Unavailable;
            }
        }

        /// <summary>Linked documents have their own project parameter elements: match those by name.</summary>
        private Parameter? FindParameter(Element element, ParameterIdentity identity, bool linked)
        {
            if (linked && identity.Source == ParameterSource.Project)
            {
                return element.LookupParameter(identity.Name);
            }

            return linked ? FindInOtherDocument(element, identity) : _parameters.Find(element, identity);
        }

        private static Parameter? FindInOtherDocument(Element element, ParameterIdentity identity) => identity.Source switch
        {
            ParameterSource.BuiltIn when identity.BuiltInValue.HasValue => element.get_Parameter((BuiltInParameter)identity.BuiltInValue.Value),
            ParameterSource.Shared when identity.SharedGuid.HasValue => element.get_Parameter(identity.SharedGuid.Value),
            _ => null
        };

        private Parameter? SampleParameter(IReadOnlyList<Element> elements, ParameterChoice choice)
        {
            var typeCache = new Dictionary<long, Element?>();
            Parameter? empty = null;
            foreach (var element in elements.Take(500))
            {
                var target = choice.IsType ? GetType(element, typeCache) : element;
                var parameter = target == null ? null : _parameters.Find(target, choice.Parameter);
                if (parameter == null)
                {
                    continue;
                }

                // Prefer a parameter with a value: reference drop-downs follow the kind of the referenced element.
                if (parameter.StorageType != StorageType.ElementId || parameter.AsElementId() != ElementId.InvalidElementId)
                {
                    return parameter;
                }

                empty ??= parameter;
            }

            return empty;
        }

        private ExportColumn ParameterColumn(int fieldId, string heading, ParameterChoice choice, Parameter? sample)
        {
            string specTypeId = string.Empty, specLabel = string.Empty, unitId = string.Empty, unitLabel = string.Empty;
            bool measurable = false, yesNo = false, specFailed = false;
            double? accuracy = null;

            // The export unit follows the parameter's data type. Taken from a sample on the exported elements,
            // else from the catalog: without it a length would go to Excel in internal feet with no unit.
            var spec = SpecOf(sample) ?? (string.IsNullOrEmpty(choice.SpecTypeId) ? null : new ForgeTypeId(choice.SpecTypeId));
            if (spec != null)
            {
                try
                {
                    specTypeId = spec.TypeId;
                    yesNo = spec == SpecTypeId.Boolean.YesNo;
                    if (UnitUtils.IsMeasurableSpec(spec))
                    {
                        measurable = true;
                        var format = _units.GetFormatOptions(spec);
                        unitId = format.GetUnitTypeId().TypeId;
                        unitLabel = LabelUtils.GetLabelForUnit(format.GetUnitTypeId());
                        accuracy = format.Accuracy;
                        specLabel = LabelUtils.GetLabelForSpec(spec);
                    }
                }
                catch (Exception)
                {
                    // Data type or unit unavailable: treated as plain value (and locked below if it is a number).
                    specFailed = true;
                }
            }

            // A number whose data type (and so unit) could not be read is exported unconverted. Typing a display
            // value back (e.g. mm) would be stored as feet, so such a column is read-only.
            bool unitUnknown = choice.Storage == ValueStorage.Double && (spec == null || specFailed || (measurable && unitId.Length == 0));

            var field = new ScheduleFieldInfo
            {
                FieldIndex = fieldId - 100,
                FieldId = fieldId,
                Heading = heading,
                FieldName = choice.Name,
                FieldType = choice.IsType ? "ElementType" : "Instance",
                Parameter = choice.Parameter,
                IsTypeParameter = choice.IsType,
                Storage = choice.Storage,
                SpecTypeId = specTypeId,
                SpecLabel = specLabel,
                IsMeasurable = measurable,
                IsYesNo = yesNo,
                DisplayUnitTypeId = unitId,
                DisplayUnitLabel = unitLabel,
                DisplayAccuracy = accuracy,
                HasCustomFormat = false,
                IsHidden = false,
                IsCalculated = false,
                IsFormula = false,
                IsPercentage = false,
                IsCombined = false,
                IsCount = false,
                IsRelatedElement = false,
                DisplayType = string.Empty,
                Classification = choice.IsReadOnly || unitUnknown
                    ? FieldClassification.ReadOnlySystem
                    : choice.IsType ? FieldClassification.EditableType : FieldClassification.EditableInstance,
                Reason = choice.IsReadOnly ? "Revit reports the parameter as read-only."
                    : unitUnknown ? "Unit unknown: the value is in Revit internal units." : string.Empty
            };

            var column = ScheduleExportBuilder.MakeColumn(field, sheetEditable: true);
            if (column.IsEditable && column.Kind == CellKind.Reference && sample != null)
            {
                column = column with { ListValues = _references.CandidateNames(sample) };
            }

            return column;
        }

        private static ForgeTypeId? SpecOf(Parameter? parameter)
        {
            try
            {
                var spec = parameter?.Definition.GetDataType();
                return spec == null || spec.Empty() ? null : spec;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static ExportColumn InfoColumn(ParameterIdentity identity, int fieldId) => new()
        {
            Field = new ScheduleFieldInfo
            {
                FieldIndex = fieldId,
                FieldId = fieldId,
                Heading = identity.Name,
                FieldName = identity.Name,
                FieldType = "Info",
                Parameter = identity,
                IsTypeParameter = false,
                Storage = ValueStorage.String,
                SpecTypeId = string.Empty,
                SpecLabel = string.Empty,
                IsMeasurable = false,
                IsYesNo = false,
                DisplayUnitTypeId = string.Empty,
                DisplayUnitLabel = string.Empty,
                HasCustomFormat = false,
                IsHidden = false,
                IsCalculated = false,
                IsFormula = false,
                IsPercentage = false,
                IsCombined = false,
                IsCount = false,
                IsRelatedElement = false,
                DisplayType = string.Empty,
                Classification = FieldClassification.ReadOnlySystem,
                Reason = "Information column."
            },
            Kind = CellKind.Text,
            IsEditable = false,
            ReadOnlyReason = "Information column: not imported."
        };

        private static ParameterIdentity Info(string key, string name) => new()
        {
            Source = ParameterSource.Info,
            Name = name,
            BuiltInName = key
        };

        private IEnumerable<(string LinkName, IList<Element> Elements)> LinkedElements(long categoryId)
        {
            foreach (var link in new FilteredElementCollector(_document).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
            {
                Document? linkDocument;
                try
                {
                    linkDocument = link.GetLinkDocument();
                }
                catch (Exception)
                {
                    linkDocument = null;
                }

                if (linkDocument == null)
                {
                    continue;
                }

                // Built-in category ids are the same in every document; other categories cannot be matched.
                if (categoryId > 0)
                {
                    continue;
                }

                var elements = new FilteredElementCollector(linkDocument).OfCategoryId(new ElementId(categoryId))
                    .WhereElementIsNotElementType().ToElements();
                yield return (linkDocument.Title, elements);
            }
        }

        private static Element? GetLinkedType(Element element)
        {
            var typeId = element.GetTypeId();
            return typeId == ElementId.InvalidElementId ? null : element.Document.GetElement(typeId);
        }

        private Element? GetType(Element element, Dictionary<long, Element?> cache)
        {
            var typeId = element.GetTypeId();
            if (typeId == ElementId.InvalidElementId)
            {
                return null;
            }

            if (!cache.TryGetValue(typeId.Value, out var type))
            {
                type = _document.GetElement(typeId);
                cache[typeId.Value] = type;
            }

            return type;
        }

        private static string UniqueHeading(string heading, HashSet<string> used)
        {
            var name = heading;
            for (int n = 2; !used.Add(name); n++)
            {
                name = $"{heading} ({n})";
            }

            return name;
        }

        private static string? SafeDesignOptionName(Element element)
        {
            try
            {
                return element.DesignOption?.Name;
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ---- Object and line styles -------------------------------------------------------------------

        private ExportSheet BuildStyles(bool lineStyles)
        {
            var styles = new StyleAccess(_document);
            var properties = StyleAccess.Properties(lineStyles);
            var columns = new List<ExportColumn> { InfoColumn(CategoryInfo, 1), InfoColumn(SubcategoryInfo, 2) };

            int fieldId = 100;
            foreach (var (key, heading, kind) in properties)
            {
                IReadOnlyList<string>? list = key switch
                {
                    StyleAccess.LinePatternProjection or StyleAccess.LinePatternCut => styles.PatternNames(),
                    StyleAccess.Material => styles.MaterialNames(),
                    _ => null
                };
                columns.Add(StyleColumn(fieldId++, key, heading, kind, list));
            }

            var rows = new List<ExportRow>();
            foreach (var (parent, category) in StyleCategories(lineStyles))
            {
                var graphicsStyle = category.GetGraphicsStyle(GraphicsStyleType.Projection);
                if (graphicsStyle == null)
                {
                    continue;
                }

                var cells = new List<ExportCell>
                {
                    ParameterValueReader.Text(parent?.Name ?? category.Name, parent?.Name ?? category.Name, true, parent?.Name ?? category.Name),
                    parent == null ? ExportCell.Unavailable : ParameterValueReader.Text(category.Name, category.Name, true, category.Name)
                };
                cells.AddRange(properties.Select(p => styles.Read(category, p.Key)));

                rows.Add(new ExportRow
                {
                    ElementUniqueId = graphicsStyle.UniqueId,
                    ElementId = graphicsStyle.Id.Value,
                    CategoryName = parent?.Name ?? category.Name,
                    Cells = cells
                });
            }

            var name = lineStyles ? "Line Styles" : "Object Styles";
            _logger.Info($"Style sheet '{name}': {rows.Count} rows.");
            return new ExportSheet
            {
                Source = lineStyles ? SheetSource.LineStyles : SheetSource.ObjectStyles,
                ScheduleUniqueId = lineStyles ? "styles:lines" : "styles:objects",
                ScheduleElementId = 0,
                ScheduleName = name,
                CategoryName = name,
                Mode = SheetMode.Editable,
                Columns = columns,
                Rows = rows
            };
        }

        private IEnumerable<(Category? Parent, Category Category)> StyleCategories(bool lineStyles)
        {
            var lines = Category.GetCategory(_document, BuiltInCategory.OST_Lines);
            if (lineStyles)
            {
                if (lines == null)
                {
                    yield break;
                }

                foreach (var sub in Sorted(lines.SubCategories))
                {
                    yield return (lines, sub);
                }

                yield break;
            }

            var top = _document.Settings.Categories.Cast<Category>()
                .Where(c => c.CategoryType is CategoryType.Model or CategoryType.Annotation && c.Id != lines?.Id)
                .OrderBy(c => c.CategoryType)
                .ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase);
            foreach (var category in top)
            {
                yield return (null, category);
                foreach (var sub in Sorted(category.SubCategories))
                {
                    yield return (category, sub);
                }
            }
        }

        private static IEnumerable<Category> Sorted(CategoryNameMap map) =>
            map.Cast<Category>().OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase);

        private static ExportColumn StyleColumn(int fieldId, string key, string heading, CellKind kind, IReadOnlyList<string>? list) => new()
        {
            Field = new ScheduleFieldInfo
            {
                FieldIndex = fieldId,
                FieldId = fieldId,
                Heading = heading,
                FieldName = heading,
                FieldType = "Style",
                Parameter = StyleAccess.Identity(key, heading),
                IsTypeParameter = false,
                Storage = kind switch { CellKind.Integer => ValueStorage.Integer, CellKind.Reference => ValueStorage.ElementId, _ => ValueStorage.String },
                SpecTypeId = string.Empty,
                SpecLabel = string.Empty,
                IsMeasurable = false,
                IsYesNo = false,
                DisplayUnitTypeId = string.Empty,
                DisplayUnitLabel = string.Empty,
                HasCustomFormat = false,
                IsHidden = false,
                IsCalculated = false,
                IsFormula = false,
                IsPercentage = false,
                IsCombined = false,
                IsCount = false,
                IsRelatedElement = false,
                DisplayType = string.Empty,
                Classification = FieldClassification.EditableInstance,
                Reason = string.Empty
            },
            Kind = kind,
            IsEditable = true,
            NumberFormat = kind == CellKind.Integer ? "0" : null,
            ListValues = list
        };

        // ---- Families -------------------------------------------------------------------------------

        private ExportSheet BuildFamilies()
        {
            var instances = new FilteredElementCollector(_document).WhereElementIsNotElementType()
                .Select(e => e.GetTypeId().Value)
                .Where(id => id > 0)
                .GroupBy(id => id)
                .ToDictionary(g => g.Key, g => g.Count());

            var rows = new List<IReadOnlyList<string>> { new[] { "Category", "Family", "Type", "Kind", "Instances" } };
            var types = new FilteredElementCollector(_document).WhereElementIsElementType().Cast<ElementType>()
                .Where(t => t.Category != null && t.Category.CategoryType is CategoryType.Model or CategoryType.Annotation)
                .OrderBy(t => t.Category!.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(t => t.FamilyName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase);

            foreach (var type in types)
            {
                rows.Add(new[]
                {
                    type.Category!.Name,
                    type.FamilyName,
                    type.Name,
                    type is FamilySymbol ? "Loadable" : "System",
                    (instances.TryGetValue(type.Id.Value, out var n) ? n : 0).ToString(CultureInfo.InvariantCulture)
                });
            }

            _logger.Info($"Families sheet: {rows.Count - 1} types.");
            return new ExportSheet
            {
                Source = SheetSource.Families,
                ScheduleUniqueId = "families",
                ScheduleElementId = 0,
                ScheduleName = "Families",
                CategoryName = "Families",
                Mode = SheetMode.Presentation,
                PresentationRows = rows,
                Notes = ["Families: list for information only (not re-importable)."]
            };
        }
    }
}
