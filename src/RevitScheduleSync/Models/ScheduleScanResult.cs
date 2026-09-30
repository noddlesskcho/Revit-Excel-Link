using System.Collections.Generic;

namespace RevitScheduleSync.Models
{
    /// <summary>Result of reading all schedules from a document.</summary>
    public sealed class ScheduleScanResult
    {
        public required IReadOnlyList<ScheduleSummary> Schedules { get; init; }

        /// <summary>Schedules left out of the browser entirely, keyed by reason (e.g. "Schedule templates").</summary>
        public required IReadOnlyDictionary<string, int> ExcludedCounts { get; init; }

        public required long ElapsedMilliseconds { get; init; }
    }
}
