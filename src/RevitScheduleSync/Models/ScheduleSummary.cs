namespace RevitScheduleSync.Models
{
    /// <summary>What kind of schedule this is, as far as Revit tells us.</summary>
    public enum ScheduleKind
    {
        Regular,
        MultiCategory,
        KeySchedule,
        MaterialTakeoff,
        NoteBlock,
        SheetList,
        ViewList
    }

    /// <summary>How far Schedule Sync can work with a schedule in V1.</summary>
    public enum ScheduleSupport
    {
        /// <summary>Itemised: one row per element, round-trip editing planned.</summary>
        EditableSync,

        /// <summary>Grouped (not itemised): read-only presentation export only.</summary>
        PresentationOnly,

        /// <summary>Listed for information; cannot be selected in V1.</summary>
        Unsupported
    }

    /// <summary>
    /// Plain snapshot of one ViewSchedule for the schedule browser.
    /// Holds no Revit objects: safe to keep in view models.
    /// </summary>
    public sealed class ScheduleSummary
    {
        public required string UniqueId { get; init; }
        public required long ElementIdValue { get; init; }
        public required string Name { get; init; }
        public required string CategoryName { get; init; }
        public required ScheduleKind Kind { get; init; }
        public required bool IsItemized { get; init; }
        public required int FieldCount { get; init; }
        public required int HiddenFieldCount { get; init; }

        /// <summary>Elements returned by a view collector on the schedule; null when not computed.</summary>
        public int? EstimatedElementCount { get; init; }

        public required bool IncludesLinkedFiles { get; init; }
        public required bool IsFilteredBySheet { get; init; }
        public required ScheduleSupport Support { get; init; }

        /// <summary>User-facing explanation of the support level.</summary>
        public required string SupportNote { get; init; }
    }
}
