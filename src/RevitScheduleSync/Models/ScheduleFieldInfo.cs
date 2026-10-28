namespace RevitScheduleSync.Models
{
    /// <summary>How Revit-Excel Link may treat a schedule column.</summary>
    public enum FieldClassification
    {
        /// <summary>Instance parameter that Revit reports as writable.</summary>
        EditableInstance,

        /// <summary>Type parameter that Revit reports as writable. One change affects every instance of the type.</summary>
        EditableType,

        /// <summary>Count, formula, percentage or combined parameter field. Computed by the schedule.</summary>
        ReadOnlyCalculated,

        /// <summary>Parameter Revit reports as read-only, or a field V1 deliberately protects.</summary>
        ReadOnlySystem,

        /// <summary>Value comes from a related element (room, material, link, project info ...).</summary>
        ReadOnlyRelated,

        /// <summary>Field that V1 cannot interpret safely.</summary>
        Unsupported
    }

    /// <summary>Mirror of Revit's StorageType, so models stay free of Revit API types.</summary>
    public enum ValueStorage
    {
        None,
        Integer,
        Double,
        String,
        ElementId
    }

    /// <summary>Analysis result for one schedule field (column). Plain data, no Revit objects.</summary>
    public sealed class ScheduleFieldInfo
    {
        public required int FieldIndex { get; init; }

        /// <summary>ScheduleFieldId.IntegerValue: stable for this field within this schedule.</summary>
        public required int FieldId { get; init; }

        /// <summary>Column heading as shown in the schedule (user-editable).</summary>
        public required string Heading { get; init; }

        /// <summary>Field name from Revit (ScheduleField.GetName()).</summary>
        public required string FieldName { get; init; }

        /// <summary>Revit ScheduleFieldType name, e.g. "Instance", "ElementType", "Formula", "Room".</summary>
        public required string FieldType { get; init; }

        public required ParameterIdentity Parameter { get; init; }

        /// <summary>True for type parameters (ScheduleFieldType.ElementType).</summary>
        public required bool IsTypeParameter { get; init; }

        public required ValueStorage Storage { get; init; }

        /// <summary>ForgeTypeId.TypeId of the spec, empty when none.</summary>
        public required string SpecTypeId { get; init; }

        public required string SpecLabel { get; init; }

        public required bool IsMeasurable { get; init; }

        public required bool IsYesNo { get; init; }

        /// <summary>ForgeTypeId.TypeId of the unit the schedule displays; empty when not measurable.</summary>
        public required string DisplayUnitTypeId { get; init; }

        public required string DisplayUnitLabel { get; init; }

        /// <summary>Rounding accuracy in display units; null when not measurable.</summary>
        public double? DisplayAccuracy { get; init; }

        /// <summary>True when the field overrides the project unit settings.</summary>
        public required bool HasCustomFormat { get; init; }

        public required bool IsHidden { get; init; }
        public required bool IsCalculated { get; init; }
        public required bool IsFormula { get; init; }
        public required bool IsPercentage { get; init; }
        public required bool IsCombined { get; init; }
        public required bool IsCount { get; init; }
        public required bool IsRelatedElement { get; init; }

        /// <summary>Totals / Min / Max display: affects grouped rows only.</summary>
        public required string DisplayType { get; init; }

        public required FieldClassification Classification { get; init; }

        /// <summary>User-facing reason for the classification.</summary>
        public required string Reason { get; init; }

        /// <summary>Sampled elements (or types) that carry the parameter / of which writable.</summary>
        public int SampleFound { get; init; }
        public int SampleWritable { get; init; }

        /// <summary>ElementId-backed values must go through a reference-resolution layer, never raw ids.</summary>
        public bool RequiresReferenceMapping => Storage == ValueStorage.ElementId;

        public bool IsEditable =>
            Classification is FieldClassification.EditableInstance or FieldClassification.EditableType;
    }
}
