using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace RevitScheduleSync.Revit.Parameters
{
    /// <summary>
    /// Resolves the name typed in Excel for an ElementId-backed parameter (Level, Phase, Material ...)
    /// to an element id, and lists the names offered in Excel drop-downs.
    /// Candidates are elements of the same class and category as the parameter's current value; when the
    /// parameter is empty, the kind is taken from its built-in name or its data type.
    /// Raw numeric ids from Excel are never accepted. Ambiguous names are rejected.
    /// </summary>
    internal sealed class ReferenceResolver
    {
        private readonly Document _document;
        private readonly Dictionary<string, ILookup<string, ElementId>> _cache = new(StringComparer.Ordinal);

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

            var matches = candidates.Lookup[name.Trim()].ToList();
            return matches.Count switch
            {
                1 => (matches[0], null),
                0 => (null, $"No {candidates.Description} named \"{name.Trim()}\" exists in the model."),
                _ => (null, $"{matches.Count} elements named \"{name.Trim()}\" match; the reference is ambiguous.")
            };
        }

        /// <summary>Names offered in the Excel drop-down for this parameter, or null when the kind is unknown.
        /// Names shared by several elements are left out: they could not be resolved on import.</summary>
        public IReadOnlyList<string>? CandidateNames(Parameter parameter)
        {
            var candidates = GetCandidates(parameter);
            return candidates?.Lookup
                .Where(g => g.Count() == 1)
                .Select(g => g.Key)
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private sealed record Candidates(ILookup<string, ElementId> Lookup, string Description);

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

        private ILookup<string, ElementId> BySample(Element sample)
        {
            long categoryId = sample.Category?.Id.Value ?? 0;
            var key = $"sample:{sample.GetType().FullName}:{categoryId}";
            if (_cache.TryGetValue(key, out var lookup))
            {
                return lookup;
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

            lookup = elements
                .Where(e => (e.Category?.Id.Value ?? 0) == categoryId)
                .ToLookup(e => e.Name, e => e.Id, StringComparer.Ordinal);
            _cache[key] = lookup;
            return lookup;
        }

        private ILookup<string, ElementId> ByClass(Type type)
        {
            var key = $"class:{type.FullName}";
            if (!_cache.TryGetValue(key, out var lookup))
            {
                lookup = new FilteredElementCollector(_document).OfClass(type).ToElements()
                    .ToLookup(e => e.Name, e => e.Id, StringComparer.Ordinal);
                _cache[key] = lookup;
            }

            return lookup;
        }

        private ILookup<string, ElementId> ByCategory(BuiltInCategory category, bool types)
        {
            var key = $"category:{(long)category}:{types}";
            if (!_cache.TryGetValue(key, out var lookup))
            {
                var collector = new FilteredElementCollector(_document).OfCategory(category);
                var elements = types ? collector.WhereElementIsElementType() : collector.WhereElementIsNotElementType();
                lookup = elements.ToElements().ToLookup(e => e.Name, e => e.Id, StringComparer.Ordinal);
                _cache[key] = lookup;
            }

            return lookup;
        }

        private ILookup<string, ElementId> ViewTemplates()
        {
            const string key = "view-templates";
            if (!_cache.TryGetValue(key, out var lookup))
            {
                lookup = new FilteredElementCollector(_document).OfClass(typeof(View)).Cast<View>()
                    .Where(v => v.IsTemplate)
                    .ToLookup(v => v.Name, v => v.Id, StringComparer.Ordinal);
                _cache[key] = lookup;
            }

            return lookup;
        }

        private static string Describe(Element element) =>
            element.Category?.Name.ToLowerInvariant() ?? element.GetType().Name.ToLowerInvariant();
    }
}
