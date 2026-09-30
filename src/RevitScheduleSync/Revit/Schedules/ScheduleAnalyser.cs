using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.DB;
using RevitScheduleSync.Logging;
using RevitScheduleSync.Models;
using RevitScheduleSync.Revit.Parameters;

namespace RevitScheduleSync.Revit.Schedules
{
    /// <summary>
    /// Analyses every field of a schedule: real parameter identity, instance/type, storage,
    /// spec and display unit, and whether Schedule Sync may edit it.
    /// Read-only (no transaction). Must run in Revit API context.
    ///
    /// Writability is verified on a sample of the schedule's elements, because
    /// Parameter.IsReadOnly is per element. The import step re-checks every element anyway.
    /// </summary>
    internal sealed class ScheduleAnalyser
    {
        private const int SampleLimit = 25;

        /// <summary>
        /// Family / type identity fields. Writing them swaps or renames types, which is not a
        /// value edit, so V1 protects them even where Revit would allow it.
        /// </summary>
        private static readonly HashSet<BuiltInParameter> ProtectedIdentityParameters =
        [
            BuiltInParameter.ELEM_TYPE_PARAM,
            BuiltInParameter.ELEM_FAMILY_PARAM,
            BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM,
            BuiltInParameter.SYMBOL_NAME_PARAM,
            BuiltInParameter.SYMBOL_FAMILY_NAME_PARAM,
            BuiltInParameter.SYMBOL_FAMILY_AND_TYPE_NAMES_PARAM,
            BuiltInParameter.ALL_MODEL_FAMILY_NAME,
            BuiltInParameter.ALL_MODEL_TYPE_NAME
        ];

        private readonly Document _document;
        private readonly ParameterLocator _parameters;
        private readonly ILogger _logger;

        public ScheduleAnalyser(Document document, ILogger logger)
        {
            _document = document;
            _parameters = new ParameterLocator(document);
            _logger = logger;
        }

        public IReadOnlyList<ScheduleAnalysis> AnalyseAll(IEnumerable<ScheduleSummary> schedules)
        {
            var stopwatch = Stopwatch.StartNew();
            var results = schedules.Select(Analyse).ToList();
            _logger.Info($"Analysed {results.Count} schedule(s) in {stopwatch.ElapsedMilliseconds} ms.");
            return results;
        }

        public ScheduleAnalysis Analyse(ScheduleSummary summary)
        {
            try
            {
                // Resolve by UniqueId: the snapshot may be stale if the schedule was deleted meanwhile.
                if (_document.GetElement(summary.UniqueId) is not ViewSchedule schedule)
                {
                    return Failed(summary, "The schedule no longer exists in this project.");
                }

                var sample = new SampleSet(_document, schedule);
                var definition = schedule.Definition;
                var fields = new List<ScheduleFieldInfo>(definition.GetFieldCount());

                for (int i = 0; i < definition.GetFieldCount(); i++)
                {
                    fields.Add(AnalyseField(definition.GetField(i), sample));
                }

                var analysis = new ScheduleAnalysis
                {
                    Schedule = summary,
                    Fields = fields,
                    SampledElementCount = sample.Instances.Count,
                    Notes = BuildNotes(summary, fields, sample)
                };

                _logger.Info(
                    $"Schedule '{summary.Name}' ({summary.UniqueId}): {fields.Count} fields, " +
                    $"{analysis.EditableInstanceCount} instance-editable, {analysis.EditableTypeCount} type-editable, " +
                    $"{analysis.ReadOnlyCount} read-only, {analysis.UnsupportedCount} unsupported; sample {sample.Instances.Count}.");
                return analysis;
            }
            catch (Exception ex)
            {
                _logger.Error($"Analysis failed for schedule '{summary.Name}' ({summary.UniqueId}).", ex);
                return Failed(summary, "This schedule could not be analysed. See the log for details.");
            }
        }

        private ScheduleFieldInfo AnalyseField(ScheduleField field, SampleSet sample)
        {
            var fieldType = field.FieldType;
            var identity = _parameters.Identify(field.ParameterId);
            bool isType = fieldType == ScheduleFieldType.ElementType;
            var units = ReadUnits(field);

            // Classification by field type first: only plain instance/type parameter fields can be edited.
            FieldClassification classification;
            string reason;
            int found = 0, writable = 0;
            var storage = ValueStorage.None;
            string specTypeId = units.SpecTypeId;

            switch (fieldType)
            {
                case ScheduleFieldType.Count:
                case ScheduleFieldType.HostCount:
                    (classification, reason) = (FieldClassification.ReadOnlyCalculated, "Count computed by the schedule.");
                    break;
                case ScheduleFieldType.Formula:
                    (classification, reason) = (FieldClassification.ReadOnlyCalculated, "Calculated value (formula).");
                    break;
                case ScheduleFieldType.Percentage:
                    (classification, reason) = (FieldClassification.ReadOnlyCalculated, "Calculated value (percentage).");
                    break;
                case ScheduleFieldType.CombinedParameter:
                    (classification, reason) = (FieldClassification.ReadOnlyCalculated, "Combined parameter: text built from several parameters.");
                    break;
                case ScheduleFieldType.ViewBased:
                    (classification, reason) = (FieldClassification.ReadOnlySystem, "Value depends on view settings (e.g. room area).");
                    break;
                case ScheduleFieldType.Instance:
                case ScheduleFieldType.ElementType:
                    (classification, reason, found, writable, storage, specTypeId) =
                        ClassifyParameterField(field, identity, isType, sample, units.SpecTypeId);
                    break;
                case ScheduleFieldType.CustomField:
                    (classification, reason) = (FieldClassification.Unsupported, "Custom (add-in) field.");
                    break;
                default:
                    (classification, reason) = (FieldClassification.ReadOnlyRelated,
                        $"Value comes from a related element ({DescribeRelated(fieldType)}).");
                    break;
            }

            return new ScheduleFieldInfo
            {
                FieldIndex = field.FieldIndex,
                FieldId = field.FieldId.IntegerValue,
                Heading = field.ColumnHeading ?? string.Empty,
                FieldName = SafeGetName(field),
                FieldType = fieldType.ToString(),
                Parameter = identity,
                IsTypeParameter = isType,
                Storage = storage,
                SpecTypeId = specTypeId,
                SpecLabel = GetSpecLabel(specTypeId),
                IsMeasurable = units.IsMeasurable,
                IsYesNo = specTypeId == SpecTypeId.Boolean.YesNo.TypeId,
                DisplayUnitTypeId = units.UnitTypeId,
                DisplayUnitLabel = units.UnitLabel,
                DisplayAccuracy = units.Accuracy,
                HasCustomFormat = units.HasCustomFormat,
                IsHidden = field.IsHidden,
                IsCalculated = field.IsCalculatedField,
                IsFormula = fieldType == ScheduleFieldType.Formula,
                IsPercentage = fieldType == ScheduleFieldType.Percentage,
                IsCombined = field.IsCombinedParameterField,
                IsCount = fieldType is ScheduleFieldType.Count or ScheduleFieldType.HostCount,
                IsRelatedElement = classification == FieldClassification.ReadOnlyRelated,
                DisplayType = field.DisplayType.ToString(),
                Classification = classification,
                Reason = reason,
                SampleFound = found,
                SampleWritable = writable
            };
        }

        private (FieldClassification, string, int Found, int Writable, ValueStorage, string SpecTypeId) ClassifyParameterField(
            ScheduleField field, ParameterIdentity identity, bool isType, SampleSet sample, string fieldSpec)
        {
            if (identity.Source == ParameterSource.None)
            {
                return (FieldClassification.Unsupported, "The parameter behind this column could not be identified.", 0, 0, ValueStorage.None, fieldSpec);
            }

            if (identity.BuiltInValue.HasValue
                && ProtectedIdentityParameters.Contains((BuiltInParameter)identity.BuiltInValue.Value))
            {
                var storage = ProbeStorage(identity, isType, sample);
                return (FieldClassification.ReadOnlySystem,
                    "Family/type identity. Changing it would swap or rename types: read-only in V1.", 0, 0, storage, fieldSpec);
            }

            var targets = isType ? sample.Types : sample.Instances;
            if (targets.Count == 0)
            {
                return (FieldClassification.Unsupported,
                    "The schedule lists no elements, so the parameter could not be verified.", 0, 0, ValueStorage.None, fieldSpec);
            }

            int found = 0, writable = 0;
            Parameter? first = null;
            foreach (var element in targets)
            {
                var parameter = _parameters.Find(element, identity);
                if (parameter == null)
                {
                    continue;
                }

                found++;
                first ??= parameter;
                if (!parameter.IsReadOnly)
                {
                    writable++;
                }
            }

            if (first == null)
            {
                return (FieldClassification.Unsupported,
                    $"Parameter not found on the {targets.Count} sampled {(isType ? "types" : "elements")}.", 0, 0, ValueStorage.None, fieldSpec);
            }

            var storageType = ToStorage(first.StorageType);
            var spec = string.IsNullOrEmpty(fieldSpec) ? SafeDataType(first) : fieldSpec;

            if (writable == 0)
            {
                return (FieldClassification.ReadOnlySystem, "Revit reports this parameter as read-only.", found, writable, storageType, spec);
            }

            var classification = isType ? FieldClassification.EditableType : FieldClassification.EditableInstance;
            var reason = isType
                ? "Type parameter: one change affects every instance of the type."
                : "Instance parameter.";

            if (writable < found)
            {
                reason += $" Read-only on {found - writable} of {found} sampled; checked per element on import.";
            }

            if (storageType == ValueStorage.ElementId)
            {
                reason += " Holds a reference to another element: values need reference mapping.";
            }

            return (classification, reason, found, writable, storageType, spec);
        }

        private ValueStorage ProbeStorage(ParameterIdentity identity, bool isType, SampleSet sample)
        {
            var targets = isType ? sample.Types : sample.Instances;
            var parameter = targets.Select(e => _parameters.Find(e, identity)).FirstOrDefault(p => p != null);
            return parameter == null ? ValueStorage.None : ToStorage(parameter.StorageType);
        }

        private UnitInfo ReadUnits(ScheduleField field)
        {
            ForgeTypeId spec;
            try
            {
                spec = field.GetSpecTypeId();
            }
            catch (Exception)
            {
                return UnitInfo.NotMeasurable(string.Empty);
            }

            if (spec == null || spec.Empty() || !UnitUtils.IsMeasurableSpec(spec))
            {
                return UnitInfo.NotMeasurable(spec == null || spec.Empty() ? string.Empty : spec.TypeId);
            }

            // Field-level override wins; otherwise the project's Units settings apply.
            var fieldFormat = field.GetFormatOptions();
            var format = fieldFormat.UseDefault ? _document.GetUnits().GetFormatOptions(spec) : fieldFormat;
            var unit = format.GetUnitTypeId();

            return new UnitInfo(
                spec.TypeId,
                true,
                unit.TypeId,
                SafeUnitLabel(unit),
                format.Accuracy,
                !fieldFormat.UseDefault);
        }

        private static IReadOnlyList<string> BuildNotes(ScheduleSummary summary, List<ScheduleFieldInfo> fields, SampleSet sample)
        {
            var notes = new List<string>();

            if (summary.Support == ScheduleSupport.PresentationOnly)
            {
                notes.Add("Grouped schedule: exported for presentation only. No column will be re-imported.");
            }

            if (summary.IncludesLinkedFiles)
            {
                notes.Add("Includes linked files: rows from linked models will be read-only.");
            }

            if (sample.Instances.Count == 0)
            {
                notes.Add("The schedule currently lists no elements.");
            }

            // The same parameter twice in one schedule would give two Excel columns for one value.
            var duplicates = fields
                .Where(f => f.Parameter.Source != ParameterSource.None)
                .GroupBy(f => (f.Parameter.Key, f.IsTypeParameter))
                .Where(g => g.Count() > 1);
            foreach (var group in duplicates)
            {
                notes.Add($"Parameter '{group.First().Parameter.Name}' appears in {group.Count()} columns " +
                          $"({string.Join(", ", group.Select(f => f.Heading))}). Conflicting edits will be blocked on import.");
            }

            var renamed = fields.Count(f => f.Parameter.Source != ParameterSource.None
                                           && !string.Equals(f.Heading, f.FieldName, StringComparison.Ordinal));
            if (renamed > 0)
            {
                notes.Add($"{renamed} column heading(s) differ from the parameter name. Mapping uses the parameter, not the heading.");
            }

            return notes;
        }

        private static ScheduleAnalysis Failed(ScheduleSummary summary, string error) => new()
        {
            Schedule = summary,
            Fields = [],
            SampledElementCount = 0,
            Notes = [],
            Error = error
        };

        private static string DescribeRelated(ScheduleFieldType fieldType) => fieldType switch
        {
            ScheduleFieldType.Room => "room",
            ScheduleFieldType.FromRoom => "from room",
            ScheduleFieldType.ToRoom => "to room",
            ScheduleFieldType.Space => "space",
            ScheduleFieldType.Material or ScheduleFieldType.StructuralMaterial or ScheduleFieldType.MaterialQuantity => "material",
            ScheduleFieldType.ProjectInfo => "project information",
            ScheduleFieldType.RevitLinkInstance or ScheduleFieldType.RevitLinkType => "linked model",
            ScheduleFieldType.Analytical => "analytical element",
            ScheduleFieldType.PhysicalInstance or ScheduleFieldType.PhysicalType => "physical element",
            ScheduleFieldType.Sheets => "sheet",
            ScheduleFieldType.Views => "view",
            ScheduleFieldType.Revision => "revision",
            _ => fieldType.ToString()
        };

        private static ValueStorage ToStorage(StorageType storageType) => storageType switch
        {
            StorageType.Integer => ValueStorage.Integer,
            StorageType.Double => ValueStorage.Double,
            StorageType.String => ValueStorage.String,
            StorageType.ElementId => ValueStorage.ElementId,
            _ => ValueStorage.None
        };

        private static string SafeGetName(ScheduleField field)
        {
            try
            {
                return field.GetName();
            }
            catch (Exception)
            {
                return field.ColumnHeading ?? string.Empty;
            }
        }

        private static string SafeDataType(Parameter parameter)
        {
            try
            {
                var type = parameter.Definition?.GetDataType();
                return type == null || type.Empty() ? string.Empty : type.TypeId;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static string GetSpecLabel(string specTypeId)
        {
            if (string.IsNullOrEmpty(specTypeId))
            {
                return string.Empty;
            }

            try
            {
                var id = new ForgeTypeId(specTypeId);
                return SpecUtils.IsSpec(id) ? LabelUtils.GetLabelForSpec(id) : string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static string SafeUnitLabel(ForgeTypeId unit)
        {
            try
            {
                return LabelUtils.GetLabelForUnit(unit);
            }
            catch (Exception)
            {
                return unit.TypeId;
            }
        }

        private readonly record struct UnitInfo(
            string SpecTypeId, bool IsMeasurable, string UnitTypeId, string UnitLabel, double? Accuracy, bool HasCustomFormat)
        {
            public static UnitInfo NotMeasurable(string specTypeId) => new(specTypeId, false, string.Empty, string.Empty, null, false);
        }

        /// <summary>
        /// Up to SampleLimit elements from the schedule plus their distinct types.
        /// Collected once per schedule and shared by all its fields.
        /// </summary>
        private sealed class SampleSet
        {
            public SampleSet(Document document, ViewSchedule schedule)
            {
                Instances = new FilteredElementCollector(document, schedule.Id)
                    .WhereElementIsNotElementType()
                    .Take(SampleLimit)
                    .ToList();

                Types = Instances
                    .Select(e => e.GetTypeId())
                    .Where(id => id != ElementId.InvalidElementId)
                    .Distinct()
                    .Select(id => document.GetElement(id))
                    .OfType<Element>()
                    .ToList();
            }

            public List<Element> Instances { get; }

            public List<Element> Types { get; }
        }
    }
}
