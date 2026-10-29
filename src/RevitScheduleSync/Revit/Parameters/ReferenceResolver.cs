using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;

namespace RevitScheduleSync.Revit.Parameters
{
    /// <summary>
    /// Resolves the name typed in Excel for an ElementId-backed parameter (Level, Phase, Material ...)
    /// to an element id, and lists the names offered in Excel drop-downs.
    /// Candidates are elements of the same class and category as the parameter's current value; when the
    /// parameter is empty, the kind is taken from its built-in name or its data type.
    /// Types can also be named "Family: Type", so type names shared by several families stay usable.
    /// Raw numeric ids from Excel are never accepted. Ambiguous names are rejected.
    /// </summary>
    internal sealed class ReferenceResolver
    {
        private static readonly Regex FamilySeparator = new(@"\s*:\s*", RegexOptions.CultureInvariant);

        private readonly Document _document;
        private readonly Dictionary<string, NameIndex> _cache = new(StringComparer.Ordinal);

        public ReferenceResolver(Document document)
        {
            _document = document;
        }

        public (ElementId? Id, string? Error) Resolve(Parameter parameter, string name)
        {
            var candidates = GetCandidates(parameter);
            if (candidates == null)
            {
                return (null, "The parameter is empty and the kind of element it refers to is unknown. Set it in Revit first.");
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                return (null, "A name is required; clearing a reference is not supported.");
            }

            var trimmed = name.Trim();
            var matches = candidates.Index.Lookup[trimmed].ToList();
            if (matches.Count == 0 && trimmed.Contains(':'))
            {
                // "Family:Type" or "Family : Type" typed by hand.
                matches = candidates.Index.Lookup[FamilySeparator.Replace(trimmed, ": ")].ToList();
            }

            return matches.Count switch
            {
                1 => (matches[0], null),
                0 => (null, $"No {candidates.Description} named \"{trimmed}\" exists in the model."),
                _ => (null, $"{matches.Count} elements named \"{trimmed}\" match; the reference is ambiguous. " +
                            "For a type, enter it as \"Family: Type\".")
            };
        }

        /// <summary>Names offered in the Excel drop-down for this parameter, or null when the kind is unknown.
        /// Each element is offered once: by its plain name when that is unique, else as "Family: Type".</summary>
        public IReadOnlyList<string>? CandidateNames(Parameter parameter) =>
            GetCandidates(parameter)?.Index.Offered;

        private sealed record Candidates(NameIndex Index, string Description);

        /// <summary>Every accepted name (plain and "Family: Type") with its element ids, and the drop-down names.</summary>
        private sealed record NameIndex(ILookup<string, ElementId> Lookup, IReadOnlyList<string> Offered);

        private Candidates? GetCandidates(Parameter parameter)
        {
            var currentId = parameter.AsElementId();
            var current = currentId == ElementId.InvalidElementId ? null : _document.GetElement(currentId);
            if (current != null)
            {
                return new Candidates(BySample(current), Describe(current));
            }

            // Empty parameter: decide the kind from the definition.
            var builtIn = parameter.Definition is InternalDefinition internalDefinition
                ? internalDefinition.BuiltInParameter
                : BuiltInParameter.INVALID;
            var name = builtIn == BuiltInParameter.INVALID ? string.Empty : builtIn.ToString();

            if (name.Contains("LEVEL", StringComparison.Ordinal) || name == "WALL_HEIGHT_TYPE")
            {
                return new Candidates(ByClass(typeof(Level)), "level");
            }

            if (name.Contains("PHASE", StringComparison.Ordinal))
            {
                return new Candidates(ByClass(typeof(Phase)), "phase");
            }

            if (name.Contains("MATERIAL", StringComparison.Ordinal))
            {
                return new Candidates(ByClass(typeof(Material)), "material");
            }

            if (name is "VIEWER_VOLUME_OF_INTEREST_CROP" or "DATUM_VOLUME_OF_INTEREST")
            {
                return new Candidates(ByCategory(BuiltInCategory.OST_VolumeOfInterest, types: false), "scope box");
            }

            if (name == "VIEW_TEMPLATE")
            {
                return new Candidates(ViewTemplates(), "view template");
            }

            try
            {
                var dataType = parameter.Definition.GetDataType();
                if (dataType == SpecTypeId.Reference.Material)
                {
                    return new Candidates(ByClass(typeof(Material)), "material");
                }

                if (Category.IsBuiltInCategory(dataType))
                {
                    // Family-type parameters: the value is a type of that category.
                    return new Candidates(ByCategory(Category.GetBuiltInCategory(dataType), types: true), "type");
                }
            }
            catch (Exception)
            {
                // Some definitions have no data type.
            }

            return null;
        }

        private NameIndex BySample(Element sample)
        {
            long categoryId = sample.Category?.Id.Value ?? 0;
            var key = $"sample:{sample.GetType().FullName}:{categoryId}";
            if (_cache.TryGetValue(key, out var index))
            {
                return index;
            }

            IEnumerable<Element> elements;
            try
            {
                elements = new FilteredElementCollector(_document).OfClass(sample.GetType()).ToElements();
            }
            catch (Exception)
            {
                // Some API classes cannot be used with OfClass; fall back to the category.
                elements = sample.Category == null
                    ? []
                    : new FilteredElementCollector(_document).OfCategoryId(sample.Category.Id).ToElements()
                        .Where(e => e.GetType() == sample.GetType());
            }

            index = Index(elements.Where(e => (e.Category?.Id.Value ?? 0) == categoryId));
            _cache[key] = index;
            return index;
        }

        private NameIndex ByClass(Type type)
        {
            var key = $"class:{type.FullName}";
            if (!_cache.TryGetValue(key, out var index))
            {
                index = Index(new FilteredElementCollector(_document).OfClass(type).ToElements());
                _cache[key] = index;
            }

            return index;
        }

        private NameIndex ByCategory(BuiltInCategory category, bool types)
        {
            var key = $"category:{(long)category}:{types}";
            if (!_cache.TryGetValue(key, out var index))
            {
                var collector = new FilteredElementCollector(_document).OfCategory(category);
                var elements = types ? collector.WhereElementIsElementType() : collector.WhereElementIsNotElementType();
                index = Index(elements.ToElements());
                _cache[key] = index;
            }

            return index;
        }

        private NameIndex ViewTemplates()
        {
            const string key = "view-templates";
            if (!_cache.TryGetValue(key, out var index))
            {
                index = Index(new FilteredElementCollector(_document).OfClass(typeof(View)).Cast<View>().Where(v => v.IsTemplate));
                _cache[key] = index;
            }

            return index;
        }

        private static NameIndex Index(IEnumerable<Element> elements)
        {
            var items = elements
                .Select(e => (e.Id, Plain: e.Name, Qualified: e is ElementType t && !string.IsNullOrEmpty(t.FamilyName) ? $"{t.FamilyName}: {t.Name}" : null))
                .ToList();

            var lookup = items
                .SelectMany(i => i.Qualified == null || i.Qualified == i.Plain
                    ? new[] { (Key: i.Plain, i.Id) }
                    : new[] { (Key: i.Plain, i.Id), (Key: i.Qualified, i.Id) })
                .ToLookup(k => k.Key, k => k.Id, StringComparer.Ordinal);

            var offered = items
                .Select(i => lookup[i.Plain].Count() == 1 ? i.Plain : i.Qualified)
                .Where(n => n != null && lookup[n].Count() == 1)
                .Select(n => n!)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            return new NameIndex(lookup, offered);
        }

        private static string Describe(Element element) =>
            element.Category?.Name.ToLowerInvariant() ?? element.GetType().Name.ToLowerInvariant();
    }
}
