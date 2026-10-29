using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RevitScheduleSync.Logging;
using RevitScheduleSync.Models;
using RevitScheduleSync.Revit.Parameters;

namespace RevitScheduleSync.Revit.ModelData
{
    /// <summary>
    /// Lists what the "Model data" export can offer: categories with element counts, and the parameters
    /// found on their instances and types. Read-only. Must run in Revit API context.
    /// </summary>
    internal sealed class ModelCatalog
    {
        /// <summary>Elements (and types) sampled per category to discover parameters.</summary>
        private const int ParameterSample = 300;

        private readonly Document _document;
        private readonly ElementId? _activeViewId;
        private readonly IReadOnlyCollection<ElementId> _selection;
        private readonly ParameterLocator _parameters;
        private readonly ILogger _logger;

        public ModelCatalog(Document document, ElementId? activeViewId, IReadOnlyCollection<ElementId> selection, ILogger logger)
        {
            _document = document;
            _activeViewId = activeViewId;
            _selection = selection;
            _parameters = new ParameterLocator(document);
            _logger = logger;
        }

        public bool HasActiveView => _activeViewId != null;

        public int SelectionCount => _selection.Count;

        /// <summary>Categories that have elements in the scope, for category and selection sources.</summary>
        public IReadOnlyList<CategoryChoice> Categories(ModelSourceKind source, ModelScope scope)
        {
            IEnumerable<Element> elements = source == ModelSourceKind.Selection
                ? _selection.Select(_document.GetElement).Where(e => e != null)!
                : Collector(scope).WhereElementIsNotElementType().ToElements();

            var wanted = source switch
            {
                ModelSourceKind.AnnotationCategories => CategoryType.Annotation,
                _ => CategoryType.Model
            };

            return elements
                .Where(e => e.Category != null && (source == ModelSourceKind.Selection || e.Category.CategoryType == wanted))
                .Where(e => source == ModelSourceKind.Selection || !IsExcludedCategory(e.Category!))
                .GroupBy(e => e.Category!.Id.Value)
                .Select(g => new CategoryChoice { CategoryId = g.Key, Name = g.First().Category!.Name, Count = g.Count() })
                .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        /// <summary>Parameters found on the elements (and their types) of the given categories.</summary>
        public IReadOnlyList<ParameterChoice> Parameters(ModelSourceKind source, ModelScope scope, IReadOnlyList<long> categoryIds, bool byType)
        {
            var found = new Dictionary<string, ParameterChoice>(StringComparer.Ordinal);
            var writable = new HashSet<string>(StringComparer.Ordinal);

            foreach (var element in SampleElements(source, scope, categoryIds, byType))
            {
                Collect(element, isType: false, found, writable);
                if (!byType && element.GetTypeId() is var typeId && typeId != ElementId.InvalidElementId
                    && _document.GetElement(typeId) is { } type)
                {
                    Collect(type, isType: true, found, writable);
                }
            }

            return found.Values
                .Select(c => writable.Contains(c.Key) == !c.IsReadOnly ? c : new ParameterChoice
                {
                    Parameter = c.Parameter, IsType = c.IsType, Storage = c.Storage, IsReadOnly = !writable.Contains(c.Key), GroupName = c.GroupName,
                    SpecTypeId = c.SpecTypeId
                })
                .OrderBy(c => c.IsType)
                .ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        /// <summary>Elements of the source, in scope. Category sources use the category ids; others use their own category.</summary>
        public IReadOnlyList<Element> Elements(ModelSourceKind source, ModelScope scope, long categoryId, bool byType)
        {
            if (source == ModelSourceKind.ProjectInformation)
            {
                return [_document.ProjectInformation];
            }

            if (source == ModelSourceKind.Selection && !byType)
            {
                return _selection.Select(_document.GetElement)
                    .Where(e => e?.Category != null && e.Category.Id.Value == categoryId)
                    .ToList()!;
            }

            var category = new ElementId(categoryId);
            if (byType)
            {
                return new FilteredElementCollector(_document).OfCategoryId(category).WhereElementIsElementType().ToElements().ToList();
            }

            return Collector(scope).OfCategoryId(category).WhereElementIsNotElementType().ToElements().ToList();
        }

        public static long CategoryIdFor(ModelSourceKind source) => source switch
        {
            ModelSourceKind.Rooms => (long)BuiltInCategory.OST_Rooms,
            ModelSourceKind.Spaces => (long)BuiltInCategory.OST_MEPSpaces,
            ModelSourceKind.ProjectInformation => (long)BuiltInCategory.OST_ProjectInformation,
            _ => 0
        };

        private IEnumerable<Element> SampleElements(ModelSourceKind source, ModelScope scope, IReadOnlyList<long> categoryIds, bool byType)
        {
            var ids = source is ModelSourceKind.Rooms or ModelSourceKind.Spaces or ModelSourceKind.ProjectInformation
                ? [CategoryIdFor(source)]
                : categoryIds;

            foreach (var categoryId in ids)
            {
                foreach (var element in Elements(source, scope, categoryId, byType).Take(ParameterSample))
                {
                    yield return element;
                }
            }
        }

        private void Collect(Element element, bool isType, Dictionary<string, ParameterChoice> found, HashSet<string> writable)
        {
            foreach (Parameter parameter in element.Parameters)
            {
                try
                {
                    if (parameter.StorageType == StorageType.None || parameter.Definition == null)
                    {
                        continue;
                    }

                    var identity = _parameters.Identify(parameter.Id);
                    if (identity.Source == ParameterSource.None)
                    {
                        continue;
                    }

                    var choice = new ParameterChoice
                    {
                        Parameter = identity,
                        IsType = isType,
                        Storage = ToStorage(parameter.StorageType),
                        IsReadOnly = parameter.IsReadOnly,
                        GroupName = GroupLabel(parameter),
                        SpecTypeId = SpecOf(parameter)
                    };

                    found.TryAdd(choice.Key, choice);
                    if (!parameter.IsReadOnly)
                    {
                        writable.Add(choice.Key);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug($"Parameter skipped on element {element.Id.Value}: {ex.GetType().Name}");
                }
            }
        }

        private static string SpecOf(Parameter parameter)
        {
            try
            {
                return parameter.Definition.GetDataType().TypeId;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private FilteredElementCollector Collector(ModelScope scope) =>
            scope == ModelScope.ActiveView && _activeViewId != null
                ? new FilteredElementCollector(_document, _activeViewId)
                : new FilteredElementCollector(_document);

        /// <summary>Categories handled by their own source, or that hold no editable data.</summary>
        private static bool IsExcludedCategory(Category category)
        {
            var id = category.Id.Value;
            return id == (long)BuiltInCategory.OST_ProjectInformation
                   || id == (long)BuiltInCategory.OST_Cameras
                   || id == (long)BuiltInCategory.OST_Viewers
                   || id == (long)BuiltInCategory.OST_Matchline
                   || id == (long)BuiltInCategory.OST_RvtLinks
                   || id == (long)BuiltInCategory.OST_IOSModelGroups
                   || id < 0 && id > -2000000;
        }

        private static string GroupLabel(Parameter parameter)
        {
            try
            {
                return LabelUtils.GetLabelForGroup(parameter.Definition.GetGroupTypeId());
            }
            catch (Exception)
            {
                return "Other";
            }
        }

        internal static ValueStorage ToStorage(StorageType storageType) => storageType switch
        {
            StorageType.Integer => ValueStorage.Integer,
            StorageType.Double => ValueStorage.Double,
            StorageType.String => ValueStorage.String,
            StorageType.ElementId => ValueStorage.ElementId,
            _ => ValueStorage.None
        };
    }
}
