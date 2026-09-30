using System.Collections.Generic;
using System.Linq;

namespace RevitScheduleSync.Models
{
    /// <summary>Field-by-field analysis of one schedule.</summary>
    public sealed class ScheduleAnalysis
    {
        public required ScheduleSummary Schedule { get; init; }

        public required IReadOnlyList<ScheduleFieldInfo> Fields { get; init; }

        /// <summary>Elements inspected to verify parameter presence and writability.</summary>
        public required int SampledElementCount { get; init; }

        /// <summary>Schedule-level notes for the user (grouped layout, duplicates, ...).</summary>
        public required IReadOnlyList<string> Notes { get; init; }

        /// <summary>Set when the schedule could not be analysed at all.</summary>
        public string? Error { get; init; }

        public int EditableInstanceCount => Fields.Count(f => f.Classification == FieldClassification.EditableInstance);

        public int EditableTypeCount => Fields.Count(f => f.Classification == FieldClassification.EditableType);

        public int ReadOnlyCount => Fields.Count(f => f.Classification is
            FieldClassification.ReadOnlyCalculated or FieldClassification.ReadOnlySystem or FieldClassification.ReadOnlyRelated);

        public int UnsupportedCount => Fields.Count(f => f.Classification == FieldClassification.Unsupported);
    }
}
