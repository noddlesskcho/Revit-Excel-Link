using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using RevitScheduleSync.Logging;
using RevitScheduleSync.Models;
using RevitScheduleSync.Models.Export;
using RevitScheduleSync.Models.Import;
using RevitScheduleSync.Revit.Documents;
using RevitScheduleSync.Revit.Parameters;
using RevitScheduleSync.Revit.Styles;
using RevitScheduleSync.Sync;

namespace RevitScheduleSync.Revit.Import
{
    /// <summary>
    /// Builds the change set for a workbook: fingerprint check, element and parameter resolution,
    /// value validation and the three-way comparison (A exported, B current Revit, C Excel).
    /// Strictly read-only: no transaction is started and nothing is written to the model.
    /// </summary>
    internal sealed class ImportAnalyser
    {
        private readonly Document _document;
        private readonly ILogger _logger;
        private readonly ParameterLocator _parameters;
        private readonly ExcelValueParser _parser;
        private readonly ReferenceResolver _references;
        private readonly StyleAccess _styles;
        private readonly Dictionary<long, Dictionary<long, int>> _instancesPerTypeByCategory = new();

        public ImportAnalyser(Document document, ILogger logger)
        {
            _document = document;
            _logger = logger;
            _parameters = new ParameterLocator(document);
            _parser = new ExcelValueParser(document);
            _references = new ReferenceResolver(document);
            _styles = new StyleAccess(document);
        }

        public ChangeSet Analyse(ImportWorkbook workbook)
        {
            var stopwatch = Stopwatch.StartNew();
            var issues = new List<ImportIssue>(workbook.Issues);
            var fingerprint = FingerprintComparer.Compare(workbook.Fingerprint, DocumentFingerprintReader.Read(_document, _logger));

            if (fingerprint.Match == FingerprintMatch.Mismatch)
            {
                _logger.Warning($"Import blocked: fingerprint mismatch ({string.Join(" ", fingerprint.Reasons)})");
                return new ChangeSet
                {
                    Workbook = workbook,
                    Fingerprint = fingerprint,
                    Items = [],
                    Issues = issues,
                    ElapsedMilliseconds = stopwatch.ElapsedMilliseconds
                };
            }

            var items = new List<ChangeItem>();
            foreach (var sheet in workbook.Sheets.Where(s => s.SheetFound))
            {
                if (sheet.Source != SheetSource.Schedule)
                {
                    // Element sheets are matched row by row; there is no schedule to check.
                }
                else if (_document.GetElement(sheet.ScheduleUniqueId) is not ViewSchedule schedule)
                {
                    issues.Add(new ImportIssue(IssueSeverity.Info, sheet.SheetName,
                        $"Schedule \"{sheet.ScheduleName}\" no longer exists. Rows are still matched to elements by their ids."));
                }
                else if (schedule.Name != sheet.ScheduleName)
                {
                    issues.Add(new ImportIssue(IssueSeverity.Info, sheet.SheetName,
                        $"Schedule was renamed to \"{schedule.Name}\" since export. Rows are matched by element ids, not names."));
                }

                foreach (var row in sheet.Rows.Where(r => r.ExcelRow.HasValue && !r.IsLinked))
                {
                    if (sheet.Source is SheetSource.ObjectStyles or SheetSource.LineStyles)
                    {
                        AnalyseStyleRow(sheet, row, items);
                    }
                    else
                    {
                        AnalyseRow(sheet, row, items);
                    }
                }

                foreach (var newRow in sheet.NewRows)
                {
                    items.Add(AnalyseNewRow(sheet, newRow));
                }
            }

            ResolveDuplicates(items);
            CountAffectedInstances(items);
            if (_document.IsWorkshared)
            {
                CheckWorksharing(items);
            }

            SetDefaultDecisions(items);

            stopwatch.Stop();
            var set = new ChangeSet
            {
                Workbook = workbook,
                Fingerprint = fingerprint,
                Items = items,
                Issues = issues,
                ElapsedMilliseconds = stopwatch.ElapsedMilliseconds
            };

            _logger.Info(
                $"Import analysis: {items.Count} cells compared in {stopwatch.ElapsedMilliseconds} ms; " +
                string.Join(", ", Enums.Values<ChangeStatus>().Select(s => (s, n: set.Count(s))).Where(x => x.n > 0).Select(x => $"{x.s} {x.n}")));
            return set;
        }

        private void AnalyseRow(ImportSheet sheet, ImportRow row, List<ChangeItem> items)
        {
            var element = _document.GetElement(row.ElementUniqueId);
            if (element == null)
            {
                items.Add(new ChangeItem
                {
                    Status = ChangeStatus.MissingElement,
                    SheetName = sheet.SheetName,
                    ScheduleName = sheet.ScheduleName,
                    ElementUniqueId = row.ElementUniqueId,
                    ElementId = row.ElementId,
                    ElementLabel = $"Element {row.ElementId}",
                    FieldId = -1,
                    ParameterName = "—",
                    ParameterKey = string.Empty,
                    IsType = false,
                    ExcelRow = row.ExcelRow,
                    Original = string.Empty,
                    Current = string.Empty,
                    Excel = string.Empty,
                    Message = "Deleted / missing element: the element no longer exists in the model. Row skipped."
                });
                return;
            }

            var type = element.GetTypeId() is var typeId && typeId != ElementId.InvalidElementId ? _document.GetElement(typeId) : null;
            bool typeSwapped = row.TypeUniqueId != null && type != null && type.UniqueId != row.TypeUniqueId;
            string label = Label(element);

            // Information columns (Element ID, Family, Phase of existing rows ...) are never imported.
            foreach (var column in sheet.Columns.Where(c => c.ExcelColumn.HasValue && c.Parameter.Source != ParameterSource.Info))
            {
                if (!row.Values.TryGetValue(column.FieldId, out var excel))
                {
                    continue;
                }

                try
                {
                    var item = column.IsEditable
                        ? AnalyseEditable(sheet, row, column, excel, element, type, typeSwapped, label)
                        : AnalyseReadOnly(sheet, row, column, excel, element, type, label);
                    if (item != null)
                    {
                        items.Add(item);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Could not analyse '{column.Heading}' for element {element.Id.Value}.", ex);
                    items.Add(NewItem(sheet, row, column, element, type, label, ChangeStatus.Invalid,
                        string.Empty, string.Empty, excel.Text, "Could not be analysed. See the log for details."));
                }
            }
        }

        private ChangeItem? AnalyseEditable(
            ImportSheet sheet, ImportRow row, ImportColumn column, ExcelValue excel,
            Element element, Element? type, bool typeSwapped, string label)
        {
            if (!row.Originals.TryGetValue(column.FieldId, out var original))
            {
                // Parameter was not on this element at export (cell was locked and blank).
                return excel.IsBlank
                    ? null
                    : NewItem(sheet, row, column, element, type, label, ChangeStatus.ReadOnly, string.Empty, string.Empty, excel.Text,
                        "Read-only — change not applied (the element had no such parameter at export).");
            }

            var parsed = _parser.Parse(excel, column);
            string a = original.Normalised;

            var target = column.IsType ? type : element;
            var parameter = target == null ? null : _parameters.Find(target, column.Parameter);
            if (parameter == null || ToStorage(parameter.StorageType) != column.Storage)
            {
                return ValueComparer.AreEqual(column.Kind, a, parsed.Normalised)
                    ? null
                    : NewItem(sheet, row, column, element, type, label, ChangeStatus.MissingParameter, original.Display, string.Empty,
                        parsed.Display, parameter == null
                            ? "The parameter no longer exists on this element."
                            : "The parameter changed data type since export.");
            }

            var current = ParameterValueReader.Read(parameter, column.Kind, column.ExportUnitTypeId, null);
            string b = current.Normalised;

            if (!parsed.IsValid)
            {
                return NewItem(sheet, row, column, element, type, label, ChangeStatus.Invalid, original.Display, current.Display,
                    parsed.Display, parsed.Error!, parsed.Normalised);
            }

            string c = parsed.Normalised;
            var threeWay = ValueComparer.Classify(column.Kind, a, b, c);
            var (status, message) = threeWay switch
            {
                ThreeWay.NoChange => (ChangeStatus.NoChange, string.Empty),
                ThreeWay.RevitChange => (ChangeStatus.RevitChanged, "Changed in Revit since export; Excel unchanged. Revit value kept."),
                ThreeWay.SameChange => (ChangeStatus.SameChange, "Revit already has this value. Nothing to write."),
                ThreeWay.Conflict => (ChangeStatus.Conflict, "Changed in both Revit and Excel to different values. Decide which to keep."),
                _ => (column.IsType ? ChangeStatus.TypeChange : ChangeStatus.Change, string.Empty)
            };

            TargetValue? targetValue = parsed.Target;
            var conflictKind = ConflictKind.None;
            if (threeWay == ThreeWay.ExcelChange)
            {
                (status, message, targetValue) = ValidateCandidate(column, row, original, parameter, parsed, typeSwapped, status);
                if (status == ChangeStatus.Conflict)
                {
                    conflictKind = ConflictKind.TypeSwapped;
                }
            }
            else if (threeWay == ThreeWay.Conflict)
            {
                // The reviewer may choose "Use Excel", so check now that the Excel value could be written.
                var (checkedStatus, reason, checkedTarget) =
                    ValidateCandidate(column, row, original, parameter, parsed, typeSwapped, ChangeStatus.Conflict);
                conflictKind = ConflictKind.ThreeWay;
                targetValue = checkedStatus == ChangeStatus.Conflict && checkedTarget != null && !(column.IsType && typeSwapped)
                    ? checkedTarget
                    : null;
                if (targetValue == null)
                {
                    message += $" The Excel value cannot be written ({reason.TrimEnd('.')}), so only \"Keep Revit\" is possible.";
                }
            }

            var item = NewItem(sheet, row, column, element, type, label, status, original.Display, current.Display,
                parsed.Display, message, c, targetValue, b);
            item.ConflictKind = conflictKind;
            return item;
        }

        /// <summary>Checks that an Excel change can actually be written; downgrades it otherwise.</summary>
        private (ChangeStatus, string, TargetValue?) ValidateCandidate(
            ImportColumn column, ImportRow row, OriginalValue original, Parameter parameter,
            ParsedValue parsed, bool typeSwapped, ChangeStatus status)
        {
            if (original.LockReason != null)
            {
                return (ChangeStatus.ReadOnly, $"Read-only — change not applied ({original.LockReason}).", null);
            }

            if (row.InGroup && !column.IsType)
            {
                return (ChangeStatus.ReadOnly, "Read-only — change not applied (element is in a model group).", null);
            }

            if (parameter.IsReadOnly)
            {
                return (ChangeStatus.ReadOnly, "Read-only — change not applied (Revit reports the parameter as read-only).", null);
            }

            if (column.IsType && typeSwapped)
            {
                return (ChangeStatus.Conflict, "The element's type was changed in Revit since export. Type value not applied.", null);
            }

            if (column.Kind == CellKind.Reference)
            {
                var (id, error) = _references.Resolve(parameter, parsed.Normalised);
                return id == null
                    ? (ChangeStatus.Invalid, error!, null)
                    : (status, string.Empty, TargetValue.OfElementId(id.Value));
            }

            if (parsed.Target == null)
            {
                return (ChangeStatus.Invalid, "Revit cannot clear this value. Enter a value or undo the edit.", null);
            }

            return (status, string.Empty, parsed.Target);
        }

        /// <summary>
        /// Read-only columns: report an Excel edit (never apply it). Compares the Excel value with the
        /// value the export would produce now; calculated columns were exported blank (or 1 for Count).
        /// </summary>
        private ChangeItem? AnalyseReadOnly(
            ImportSheet sheet, ImportRow row, ImportColumn column, ExcelValue excel, Element element, Element? type, string label)
        {
            string reason = column.ReadOnlyReason ?? "read-only field";
            string excelText = excel.Kind == ExcelValueKind.Number
                ? excel.Number!.Value.ToString("R", CultureInfo.InvariantCulture)
                : excel.Text;

            if (column.Parameter.Source == ParameterSource.None)
            {
                bool changed = column.FieldType == nameof(ScheduleFieldType.Count)
                    ? !(excel.Number is 1d)
                    : column.FieldType == nameof(ScheduleFieldType.CombinedParameter) ? false : !excel.IsBlank;
                return changed
                    ? NewItem(sheet, row, column, element, type, label, ChangeStatus.ReadOnly, string.Empty, string.Empty, excel.Text,
                        $"Read-only — change not applied ({reason})")
                    : null;
            }

            var target = column.IsType ? type : element;
            var parameter = target == null ? null : _parameters.Find(target, column.Parameter);
            if (parameter == null)
            {
                return null;
            }

            var current = ParameterValueReader.Read(parameter, column.Kind, column.ExportUnitTypeId, null);
            if (ValueComparer.AreEqual(column.Kind, current.Normalised, excelText)
                || (excel.IsBlank && current.Normalised.Length == 0))
            {
                return null;
            }

            return NewItem(sheet, row, column, element, type, label, ChangeStatus.ReadOnly, string.Empty, current.Display, excel.Text,
                $"Read-only — change not applied ({reason})");
        }

        /// <summary>
        /// The same element (instance parameter) or the same type (type parameter) may be changed from several
        /// rows or sheets. Identical values are applied once; different values become a conflict. Never picks one.
        /// </summary>
        private static void ResolveDuplicates(List<ChangeItem> items)
        {
            var groups = items
                .Where(i => i.IsApplicable)
                .GroupBy(i => (Owner: i.IsType ? i.TypeUniqueId ?? i.ElementUniqueId : i.ElementUniqueId, i.ParameterKey, i.IsType));

            foreach (var group in groups)
            {
                var list = group.ToList();
                if (list.Count < 2)
                {
                    continue;
                }

                var values = list.Select(i => i.ExcelNormalised ?? string.Empty).Distinct(StringComparer.Ordinal).ToList();
                if (values.Count > 1)
                {
                    var where = string.Join(", ", list.Select(i => $"{i.SheetName} row {i.ExcelRow}: {i.Excel}"));
                    foreach (var item in list)
                    {
                        item.Status = ChangeStatus.Conflict;
                        item.ConflictKind = ConflictKind.Disagreement;
                        item.Message = (item.IsType ? "Rows set different values for the same type parameter: " : "Different values for the same element: ") + where;
                    }
                }
                else
                {
                    var first = list[0];
                    foreach (var item in list.Skip(1))
                    {
                        item.Status = ChangeStatus.Duplicate;
                        item.Message = $"Same change as {first.SheetName} row {first.ExcelRow}; applied once.";
                    }
                }
            }
        }

        /// <summary>
        /// Safe defaults: simple instance changes are proposed for Apply; type changes and three-way conflicts
        /// need an explicit decision; conflicts that cannot be resolved here are skipped.
        /// </summary>
        private static void SetDefaultDecisions(List<ChangeItem> items)
        {
            foreach (var item in items)
            {
                item.Decision = item.Status switch
                {
                    ChangeStatus.Change => ReviewDecision.Apply,
                    ChangeStatus.Create => ReviewDecision.Apply,
                    ChangeStatus.TypeChange => ReviewDecision.Pending,
                    ChangeStatus.Conflict when item.ConflictKind == ConflictKind.ThreeWay =>
                        item.Target != null ? ReviewDecision.Pending : ReviewDecision.KeepRevit,
                    ChangeStatus.Conflict => ReviewDecision.Skip,
                    _ => ReviewDecision.None
                };
            }
        }

        /// <summary>For type changes: how many instances of the type exist (one pass per category).</summary>
        private void CountAffectedInstances(List<ChangeItem> items)
        {
            foreach (var item in items.Where(i => i.Status == ChangeStatus.TypeChange && i.TypeUniqueId != null))
            {
                var type = _document.GetElement(item.TypeUniqueId);
                if (type?.Category == null)
                {
                    continue;
                }

                long categoryId = type.Category.Id.Value;
                if (!_instancesPerTypeByCategory.TryGetValue(categoryId, out var counts))
                {
                    counts = new FilteredElementCollector(_document)
                        .OfCategoryId(type.Category.Id)
                        .WhereElementIsNotElementType()
                        .GroupBy(e => e.GetTypeId().Value)
                        .ToDictionary(g => g.Key, g => g.Count());
                    _instancesPerTypeByCategory[categoryId] = counts;
                }

                item.AffectedInstances = counts.TryGetValue(type.Id.Value, out var n) ? n : 0;
            }
        }

        /// <summary>Workshared models: elements owned by others or out of date with central cannot be written.</summary>
        private void CheckWorksharing(List<ChangeItem> items)
        {
            // Includes conflicts the reviewer could resolve with "Use Excel".
            foreach (var item in items.Where(i => i.IsApplicable || (i.Status == ChangeStatus.Conflict && i.Target != null)))
            {
                var target = item.IsType && item.TypeUniqueId != null
                    ? _document.GetElement(item.TypeUniqueId)
                    : _document.GetElement(item.ElementUniqueId);
                if (target == null)
                {
                    continue;
                }

                try
                {
                    var status = WorksharingUtils.GetCheckoutStatus(_document, target.Id, out var owner);
                    if (status == CheckoutStatus.OwnedByOtherUser)
                    {
                        item.Status = ChangeStatus.Blocked;
                        item.Message = $"Owned by {owner}. Ask them to relinquish it, then import again.";
                        continue;
                    }

                    var updates = WorksharingUtils.GetModelUpdatesStatus(_document, target.Id);
                    if (updates is ModelUpdatesStatus.UpdatedInCentral or ModelUpdatesStatus.DeletedInCentral)
                    {
                        item.Status = ChangeStatus.Blocked;
                        item.Message = updates == ModelUpdatesStatus.DeletedInCentral
                            ? "Deleted in the central model."
                            : "Changed in the central model. Use Reload Latest, then import again.";
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Worksharing status unavailable for element {target.Id.Value}.", ex);
                }
            }
        }

        /// <summary>Object and line styles: the row is a category, the columns are graphic properties.</summary>
        private void AnalyseStyleRow(ImportSheet sheet, ImportRow row, List<ChangeItem> items)
        {
            var category = _styles.CategoryOf(row.ElementUniqueId);
            string label = category == null
                ? $"Style {row.ElementId}"
                : category.Parent == null ? category.Name : $"{category.Parent.Name} › {category.Name}";

            foreach (var column in sheet.Columns.Where(c => c.ExcelColumn.HasValue && c.IsEditable && c.Parameter.Source == ParameterSource.Style))
            {
                if (!row.Values.TryGetValue(column.FieldId, out var excel) || !row.Originals.TryGetValue(column.FieldId, out var original))
                {
                    continue;
                }

                var key = column.Parameter.BuiltInName ?? string.Empty;
                var parsed = _parser.Parse(excel, column);
                if (category == null)
                {
                    if (!ValueComparer.AreEqual(column.Kind, original.Normalised, parsed.Normalised))
                    {
                        items.Add(StyleItem(sheet, row, column, label, ChangeStatus.MissingElement, original.Display, string.Empty, parsed.Display,
                            "The style no longer exists in the model."));
                    }

                    continue;
                }

                var current = _styles.Read(category, key);
                var threeWay = ValueComparer.Classify(column.Kind, original.Normalised, current.Normalised, parsed.Normalised);
                if (threeWay == ThreeWay.NoChange)
                {
                    continue;
                }

                if (threeWay != ThreeWay.ExcelChange)
                {
                    var status = threeWay switch
                    {
                        ThreeWay.RevitChange => ChangeStatus.RevitChanged,
                        ThreeWay.SameChange => ChangeStatus.SameChange,
                        _ => ChangeStatus.Conflict
                    };
                    var item = StyleItem(sheet, row, column, label, status, original.Display, current.Display, parsed.Display,
                        status == ChangeStatus.Conflict ? "Changed in both Revit and Excel. Revit value kept." : "Revit value kept.");
                    item.ConflictKind = status == ChangeStatus.Conflict ? ConflictKind.Disagreement : ConflictKind.None;
                    items.Add(item);
                    continue;
                }

                var (target, error) = _styles.Resolve(key, parsed.Normalised);
                items.Add(target == null
                    ? StyleItem(sheet, row, column, label, ChangeStatus.Invalid, original.Display, current.Display, parsed.Display, error!)
                    : StyleItem(sheet, row, column, label, ChangeStatus.Change, original.Display, current.Display, parsed.Display,
                        string.Empty, parsed.Normalised, target, current.Normalised));
            }
        }

        private static ChangeItem StyleItem(
            ImportSheet sheet, ImportRow row, ImportColumn column, string label, ChangeStatus status,
            string original, string current, string excel, string message,
            string? excelNormalised = null, TargetValue? target = null, string? currentNormalised = null) => new()
        {
            Status = status,
            SheetName = sheet.SheetName,
            ScheduleName = sheet.ScheduleName,
            ElementUniqueId = row.ElementUniqueId,
            ElementId = row.ElementId,
            ElementLabel = label,
            FieldId = column.FieldId,
            ParameterName = column.Heading,
            ParameterKey = column.Parameter.Key,
            Parameter = column.Parameter,
            Kind = column.Kind,
            IsType = false,
            ExcelRow = row.ExcelRow,
            Original = original,
            Current = current,
            Excel = excel,
            ExcelNormalised = excelNormalised,
            CurrentNormalised = currentNormalised,
            Target = status == ChangeStatus.Change ? target : null,
            Message = message
        };

        /// <summary>
        /// A row added in Excel to a rooms or spaces sheet. The values are checked here; references
        /// (levels, materials ...) are resolved when the element exists, in the Apply step.
        /// </summary>
        private ChangeItem AnalyseNewRow(ImportSheet sheet, ImportNewRow newRow)
        {
            var values = new List<NewElementValue>();
            var problems = new List<string>();
            string phaseName = string.Empty, number = string.Empty, name = string.Empty;

            foreach (var column in sheet.Columns.Where(c => c.IsEditable && c.ExcelColumn.HasValue))
            {
                if (!newRow.Values.TryGetValue(column.FieldId, out var excel) || excel.IsBlank)
                {
                    if (column.RequiredForNewRows)
                    {
                        problems.Add($"\"{column.Heading}\" is required.");
                    }

                    continue;
                }

                var parsed = _parser.Parse(excel, column);
                if (!parsed.IsValid)
                {
                    problems.Add($"\"{column.Heading}\": {parsed.Error}");
                    continue;
                }

                if (column.Parameter.Source == ParameterSource.Info)
                {
                    if (column.Parameter.BuiltInName == "Phase")
                    {
                        phaseName = parsed.Normalised;
                    }

                    continue;
                }

                if (column.Parameter.BuiltInName == nameof(BuiltInParameter.ROOM_NUMBER))
                {
                    number = parsed.Normalised;
                }
                else if (column.Parameter.BuiltInName == nameof(BuiltInParameter.ROOM_NAME))
                {
                    name = parsed.Normalised;
                }

                values.Add(new NewElementValue(column.Parameter, column.Heading, column.Kind, column.ExportUnitTypeId,
                    parsed.Display, parsed.Normalised, parsed.Target));
            }

            if (phaseName.Length > 0 && !new FilteredElementCollector(_document).OfClass(typeof(Phase)).Any(p => p.Name == phaseName))
            {
                problems.Add($"Phase \"{phaseName}\" does not exist in the model.");
            }

            bool rooms = sheet.Source == SheetSource.Rooms;
            string kind = rooms ? "room" : "space";
            string label = $"New {kind} {number} {name}".TrimEnd();
            return new ChangeItem
            {
                Status = problems.Count == 0 ? ChangeStatus.Create : ChangeStatus.Invalid,
                SheetName = sheet.SheetName,
                ScheduleName = sheet.ScheduleName,
                ElementUniqueId = string.Empty,
                ElementId = -1,
                ElementLabel = label,
                FieldId = -1,
                ParameterName = $"New {kind}",
                ParameterKey = $"new:{newRow.ExcelRow}",
                IsType = false,
                ExcelRow = newRow.ExcelRow,
                Original = string.Empty,
                Current = string.Empty,
                Excel = string.Join(" · ", values.Select(v => $"{v.Heading}: {v.Excel}")),
                NewElement = problems.Count == 0
                    ? new NewElementRequest { Source = sheet.Source, PhaseName = phaseName, Values = values }
                    : null,
                Message = problems.Count == 0
                    ? $"Creates an unplaced {kind} in phase \"{phaseName}\". Place it in a plan view afterwards."
                    : "Not created: " + string.Join(" ", problems)
            };
        }

        private static ChangeItem NewItem(
            ImportSheet sheet, ImportRow row, ImportColumn column, Element element, Element? type, string label,
            ChangeStatus status, string original, string current, string excel, string message,
            string? excelNormalised = null, TargetValue? target = null, string? currentNormalised = null) => new()
        {
            Status = status,
            SheetName = sheet.SheetName,
            ScheduleName = sheet.ScheduleName,
            ElementUniqueId = row.ElementUniqueId,
            ElementId = element.Id.Value,
            ElementLabel = label,
            FieldId = column.FieldId,
            ParameterName = column.Heading,
            ParameterKey = column.Parameter.Key,
            Parameter = column.Parameter,
            Kind = column.Kind,
            ExportUnitTypeId = column.ExportUnitTypeId,
            CurrentNormalised = currentNormalised,
            IsType = column.IsType,
            TypeUniqueId = type?.UniqueId,
            TypeName = type?.Name,
            ExcelRow = row.ExcelRow,
            Original = original,
            Current = current,
            Excel = excel,
            ExcelNormalised = excelNormalised,
            Target = status is ChangeStatus.Change or ChangeStatus.TypeChange or ChangeStatus.Conflict ? target : null,
            Message = message
        };

        private static string Label(Element element)
        {
            var category = element.Category?.Name ?? "Element";
            var mark = element.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();
            return string.IsNullOrWhiteSpace(mark) ? $"{category}: {element.Name}" : $"{category} {mark}";
        }

        private static ValueStorage ToStorage(StorageType storageType) => storageType switch
        {
            StorageType.Integer => ValueStorage.Integer,
            StorageType.Double => ValueStorage.Double,
            StorageType.String => ValueStorage.String,
            StorageType.ElementId => ValueStorage.ElementId,
            _ => ValueStorage.None
        };
    }
}
