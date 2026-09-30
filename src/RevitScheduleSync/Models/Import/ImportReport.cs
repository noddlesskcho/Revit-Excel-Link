using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitScheduleSync.Models.Import
{
    public enum ReportResult
    {
        Applied,
        Skipped,
        Conflict,
        Failed,
        ReadOnlyIgnored,

        /// <summary>Invalid value, missing element/parameter or blocked by worksharing: nothing attempted.</summary>
        NotApplicable
    }

    /// <summary>One line of the import report.</summary>
    public sealed record ImportReportEntry(
        ReportResult Result,
        string Schedule,
        long ElementId,
        string Element,
        string Parameter,
        bool IsType,
        string PreviousValue,
        string NewValue,
        string Message);

    /// <summary>What an import did. Kept in memory for the result screen and saved as CSV for History.</summary>
    public sealed class ImportReport
    {
        public required DateTime Time { get; init; }
        public required string WorkbookPath { get; init; }
        public required string DocumentTitle { get; init; }
        public required IReadOnlyList<ImportReportEntry> Entries { get; init; }

        /// <summary>Revit warnings raised while committing (e.g. duplicate Mark values).</summary>
        public required IReadOnlyList<string> RevitWarnings { get; init; }

        /// <summary>True when Revit rejected the transaction and nothing was changed.</summary>
        public required bool RolledBack { get; init; }

        public required long ElapsedMilliseconds { get; init; }

        public int Count(ReportResult result) => Entries.Count(e => e.Result == result);
    }
}
