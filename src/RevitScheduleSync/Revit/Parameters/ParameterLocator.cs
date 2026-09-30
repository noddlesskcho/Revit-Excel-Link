using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using RevitScheduleSync.Models;

namespace RevitScheduleSync.Revit.Parameters
{
    /// <summary>
    /// Turns a schedule field's ParameterId into a ParameterIdentity, and finds that exact
    /// parameter on an element. Never looks parameters up by display name.
    /// Caches ParameterElement lookups per document.
    /// </summary>
    internal sealed class ParameterLocator
    {
        private readonly Document _document;
        private readonly Dictionary<long, ParameterIdentity> _identityCache = new();
        private readonly Dictionary<long, Definition?> _definitionCache = new();

        public ParameterLocator(Document document)
        {
            _document = document;
        }

        public ParameterIdentity Identify(ElementId parameterId)
        {
            if (parameterId == ElementId.InvalidElementId)
            {
                return ParameterIdentity.None;
            }

            if (_identityCache.TryGetValue(parameterId.Value, out var cached))
            {
                return cached;
            }

            var identity = CreateIdentity(parameterId);
            _identityCache[parameterId.Value] = identity;
            return identity;
        }

        /// <summary>Finds the parameter on the element, or null if the element does not have it.</summary>
        public Parameter? Find(Element element, ParameterIdentity identity)
        {
            switch (identity.Source)
            {
                case ParameterSource.BuiltIn when identity.BuiltInValue.HasValue:
                    return element.get_Parameter((BuiltInParameter)identity.BuiltInValue.Value);

                case ParameterSource.Shared when identity.SharedGuid.HasValue:
                    return element.get_Parameter(identity.SharedGuid.Value);

                case ParameterSource.Project when identity.ParameterElementId.HasValue:
                    var definition = GetDefinition(identity.ParameterElementId.Value);
                    return definition == null ? null : element.get_Parameter(definition);

                default:
                    return null;
            }
        }

        private ParameterIdentity CreateIdentity(ElementId parameterId)
        {
            if (ParameterUtils.IsBuiltInParameter(parameterId))
            {
                var builtIn = (BuiltInParameter)parameterId.Value;
                return new ParameterIdentity
                {
                    Source = ParameterSource.BuiltIn,
                    Name = GetBuiltInLabel(builtIn),
                    BuiltInName = builtIn.ToString(),
                    BuiltInValue = parameterId.Value
                };
            }

            switch (_document.GetElement(parameterId))
            {
                case SharedParameterElement shared:
                    return new ParameterIdentity
                    {
                        Source = ParameterSource.Shared,
                        Name = shared.Name,
                        SharedGuid = shared.GuidValue,
                        ParameterElementId = parameterId.Value
                    };

                case ParameterElement project:
                    return new ParameterIdentity
                    {
                        Source = ParameterSource.Project,
                        Name = project.GetDefinition()?.Name ?? project.Name,
                        ParameterElementId = parameterId.Value
                    };

                default:
                    // Positive id that is not a ParameterElement: unknown kind of field parameter.
                    return new ParameterIdentity
                    {
                        Source = ParameterSource.None,
                        Name = $"Unknown parameter ({parameterId.Value})",
                        ParameterElementId = parameterId.Value
                    };
            }
        }

        private Definition? GetDefinition(long parameterElementId)
        {
            if (!_definitionCache.TryGetValue(parameterElementId, out var definition))
            {
                definition = (_document.GetElement(new ElementId(parameterElementId)) as ParameterElement)?.GetDefinition();
                _definitionCache[parameterElementId] = definition;
            }

            return definition;
        }

        private static string GetBuiltInLabel(BuiltInParameter builtIn)
        {
            try
            {
                return LabelUtils.GetLabelFor(builtIn);
            }
            catch (Exception)
            {
                // Some internal built-ins have no UI label.
                return builtIn.ToString();
            }
        }
    }
}
