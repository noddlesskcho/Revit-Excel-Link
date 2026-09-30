using System.Collections.Generic;
using RevitScheduleSync.Models;
using RevitScheduleSync.Models.Import;

namespace RevitScheduleSync.Services
{
    /// <summary>
    /// Revit-side operations the UI can request. Implemented against the active document;
    /// keeps view models free of Revit API types (and testable outside Revit).
    /// All calls must happen on the Revit main thread (the modal window's event handlers).
    /// </summary>
    public interface IScheduleOperations
    {
        IReadOnlyList<ScheduleAnalysis> Analyse(IReadOnlyList<ScheduleSummary> schedules);

        ExportResult Export(IReadOnlyList<ScheduleAnalysis> analyses, ExportOptions options);

        /// <summary>Reads a workbook and compares it with the model. Read-only: never writes to the model.</summary>
        ChangeSet AnalyseImport(string workbookPath);
    }

    public sealed class ExportOptions
    {
        public required string FilePath { get; init; }

        /// <summary>Protect each sheet (no password) so only editable cells can be changed.</summary>
        public bool ProtectReadOnlyCells { get; init; } = true;

        /// <summary>Debug: leave the metadata sheet visible instead of very hidden.</summary>
        public bool ShowMetadataSheet { get; init; }
    }

    public sealed class ExportResult
    {
        public required string FilePath { get; init; }
        public required int EditableSheetCount { get; init; }
        public required int PresentationSheetCount { get; init; }
        public required int RowCount { get; init; }
        public required int EditableColumnCount { get; init; }
        public required long ElapsedMilliseconds { get; init; }
        public required IReadOnlyList<string> Notes { get; init; }
    }
}
