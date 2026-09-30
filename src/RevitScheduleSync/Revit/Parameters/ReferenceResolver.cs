using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace RevitScheduleSync.Revit.Parameters
{
    /// <summary>
    /// Resolves the name typed in Excel for an ElementId-backed parameter (Level, Phase, Material ...)
    /// to an element id. Candidates are elements of the same class and category as the parameter's
    /// current value. Raw numeric ids from Excel are never accepted. Ambiguous names are rejected.
    /// </summary>
    internal sealed class ReferenceResolver
    {
        private readonly Document _document;
        private readonly Dictionary<(Type, long), ILookup<string, ElementId>> _cache = new();

        public ReferenceResolver(Document document)
        {
            _document = document;
        }

        public (ElementId? Id, string? Error) Resolve(Parameter parameter, string name)
        {
            var currentId = parameter.AsElementId();
            var current = currentId == ElementId.InvalidElementId ? null : _document.GetElement(currentId);
            if (current == null)
            {
                return (null, "The parameter is currently empty, so the kind of element it refers to is unknown. Set it in Revit first.");
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                return (null, "A name is required; clearing a reference is not supported.");
            }

            var candidates = GetCandidates(current)[name.Trim()].ToList();
            return candidates.Count switch
            {
                1 => (candidates[0], null),
                0 => (null, $"No {Describe(current)} named \"{name.Trim()}\" exists in the model."),
                _ => (null, $"{candidates.Count} elements named \"{name.Trim()}\" match; the reference is ambiguous.")
            };
        }

        private ILookup<string, ElementId> GetCandidates(Element sample)
        {
            long categoryId = sample.Category?.Id.Value ?? 0;
            var key = (sample.GetType(), categoryId);
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

        private static string Describe(Element element) =>
            element.Category?.Name.ToLowerInvariant() ?? element.GetType().Name.ToLowerInvariant();
    }
}
