using System;
using System.Collections.Generic;
using RevitScheduleSync.Models.Export;

namespace RevitScheduleSync.Models.Import
{
    /// <summary>A workbook as read back from disk: metadata plus the user's current cell values.</summary>
    public sealed class ImportWorkbook
    {
        public required string FilePath { get; init; }
        public required int SchemaVersion { get; init; }
        public required string PluginVersion { get; init; }
        public required string RevitVersion { get; init; }
        public required DateTime? ExportedUtc { get; init; }
        public required DocumentFingerprint Fingerprint { get; init; }
        public required IReadOnlyList<ImportSheet> Sheets { get; init; }

        /// <summary>Problems found while reading (renamed sheets, pasted rows, moved columns ...).</summary>
        public required IReadOnlyList<ImportIssue> Issues { get; init; }
    }

    public sealed class ImportSheet
    {
        public required string SheetName { get; init; }
        public required string ScheduleUniqueId { get; init; }
        public required string ScheduleName { get; init; }
        public required SheetMode Mode { get; init; }

        public SheetSource Source { get; init; } = SheetSource.Schedule;

        /// <summary>True when rows added in Excel create new elements (rooms, spaces).</summary>
        public bool AllowNewRows { get; init; }

        /// <summary>Rows added in Excel (blank identity, some values entered).</summary>
        public IReadOnlyList<ImportNewRow> NewRows { get; init; } = [];

        /// <summary>False when the visible sheet was not found in the workbook.</summary>
        public required bool SheetFound { get; init; }

        public required IReadOnlyList<ImportColumn> Columns { get; init; }
        public required IReadOnlyList<ImportRow> Rows { get; init; }
    }

    public sealed class ImportColumn
    {
        public required int FieldId { get; init; }
        public required string Heading { get; init; }

        /// <summary>Revit ScheduleFieldType name (Instance, ElementType, Count, Formula ...).</summary>
        public required string FieldType { get; init; }

        public required ParameterIdentity Parameter { get; init; }
        public required bool IsType { get; init; }
        public required ValueStorage Storage { get; init; }
        public required string SpecTypeId { get; init; }
        public required string ExportUnitTypeId { get; init; }
        public required CellKind Kind { get; init; }
        public required bool IsEditable { get; init; }
        public string? ReadOnlyReason { get; init; }

        public bool RequiredForNewRows { get; init; }

        /// <summary>Where the column was found in the sheet now; null when it is missing.</summary>
        public int? ExcelColumn { get; init; }
    }

    public sealed class ImportRow
    {
        public required string ElementUniqueId { get; init; }
        public required long ElementId { get; init; }
        public string? TypeUniqueId { get; init; }
        public required bool InGroup { get; init; }

        /// <summary>Linked-model row: shown for information, never written.</summary>
        public bool IsLinked { get; init; }

        /// <summary>Row where the element currently sits in the sheet (users may filter/sort); null if the row was deleted.</summary>
        public int? ExcelRow { get; init; }

        /// <summary>Current Excel values by FieldId (only for columns that were found).</summary>
        public required IReadOnlyDictionary<int, ExcelValue> Values { get; init; }

        /// <summary>Original exported values by FieldId (editable columns only).</summary>
        public required IReadOnlyDictionary<int, OriginalValue> Originals { get; init; }
    }

    /// <summary>A row added in Excel: a request to create a new element (rooms and spaces).</summary>
    public sealed class ImportNewRow
    {
        public required int ExcelRow { get; init; }

        /// <summary>Entered values by FieldId (editable columns that were found).</summary>
        public required IReadOnlyDictionary<int, ExcelValue> Values { get; init; }
    }

    public enum ExcelValueKind
    {
        Blank,
        Text,
        Number,
        Boolean,
        Other
    }

    /// <summary>A cell value as Excel stores it, before any interpretation.</summary>
    public readonly record struct ExcelValue(ExcelValueKind Kind, string Text, double? Number)
    {
        public static readonly ExcelValue Blank = new(ExcelValueKind.Blank, string.Empty, null);

        public bool IsBlank => Kind == ExcelValueKind.Blank || (Kind == ExcelValueKind.Text && string.IsNullOrWhiteSpace(Text));
    }

    /// <summary>Value "A" of the three-way comparison, as recorded at export.</summary>
    public sealed record OriginalValue(bool HasValue, string Raw, string Normalised, string Display, string? LockReason);

    public enum IssueSeverity
    {
        Info,
        Warning,
        Error
    }

    public sealed record ImportIssue(IssueSeverity Severity, string? SheetName, string Message);
}
