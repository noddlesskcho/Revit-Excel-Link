using System;
using System.Collections.Generic;

namespace RevitScheduleSync.Models.Export
{
    /// <summary>
    /// Everything needed to write one workbook, as plain data.
    /// Built on the Revit side, written by the Excel layer; neither side knows the other's types.
    /// </summary>
    public sealed class ExportWorkbook
    {
        public required WorkbookInfo Info { get; init; }

        public required IReadOnlyList<ExportSheet> Sheets { get; init; }
    }

    public sealed class WorkbookInfo
    {
        public required int SchemaVersion { get; init; }
        public required string PluginVersion { get; init; }
        public required string RevitVersion { get; init; }
        public required DateTime ExportedUtc { get; init; }
        public required DocumentFingerprint Fingerprint { get; init; }
    }

    public enum SheetMode
    {
        /// <summary>Itemised schedule: one row per element, editable columns can be re-imported.</summary>
        Editable,

        /// <summary>Grouped schedule: text copy of the schedule body, never re-imported.</summary>
        Presentation
    }

    public sealed class ExportSheet
    {
        public required string ScheduleUniqueId { get; init; }
        public required long ScheduleElementId { get; init; }
        public required string ScheduleName { get; init; }
        public required string CategoryName { get; init; }
        public required SheetMode Mode { get; init; }

        /// <summary>Editable mode: exported (visible) columns in schedule order.</summary>
        public IReadOnlyList<ExportColumn> Columns { get; init; } = [];

        /// <summary>Editable mode: one row per element; Cells align with Columns.</summary>
        public IReadOnlyList<ExportRow> Rows { get; init; } = [];

        /// <summary>Presentation mode: schedule body text, row by row, as Revit displays it.</summary>
        public IReadOnlyList<IReadOnlyList<string>> PresentationRows { get; init; } = [];

        /// <summary>User-facing notes written to the export report (not to the sheet).</summary>
        public IReadOnlyList<string> Notes { get; init; } = [];
    }

    /// <summary>How a column's values are written to and read back from Excel.</summary>
    public enum CellKind
    {
        Text,
        Number,
        Integer,
        YesNo,

        /// <summary>ElementId-backed parameter, written as the referenced element's display name.</summary>
        Reference
    }

    public sealed record ExportColumn
    {
        public required ScheduleFieldInfo Field { get; init; }
        public required CellKind Kind { get; init; }

        /// <summary>True when the user may edit this column in Excel and the value can be re-imported.</summary>
        public required bool IsEditable { get; init; }

        /// <summary>Why the column is locked, when it is.</summary>
        public string? ReadOnlyReason { get; init; }

        /// <summary>Unit the numbers in Excel are expressed in (may differ from the schedule's
        /// composite display unit, e.g. feet-and-inches is exported as decimal feet).</summary>
        public string ExportUnitTypeId { get; init; } = string.Empty;

        public string ExportUnitLabel { get; init; } = string.Empty;

        /// <summary>Excel number format, or null for General/text.</summary>
        public string? NumberFormat { get; init; }
    }

    public sealed class ExportRow
    {
        public required string ElementUniqueId { get; init; }
        public required long ElementId { get; init; }
        public string? TypeUniqueId { get; init; }
        public long? TypeId { get; init; }
        public required string CategoryName { get; init; }

        /// <summary>Element is a member of a model group: its instance values are locked in V1.</summary>
        public bool InGroup { get; init; }

        /// <summary>Design option name when the element is not in the main model.</summary>
        public string? DesignOptionName { get; init; }

        public required IReadOnlyList<ExportCell> Cells { get; init; }
    }

    /// <summary>One exported value plus the original values needed for three-way comparison on import.</summary>
    public sealed record ExportCell
    {
        public static readonly ExportCell Unavailable = new()
        {
            Value = null,
            Raw = string.Empty,
            Normalised = string.Empty,
            Display = string.Empty,
            HasValue = false,
            IsAvailable = false
        };

        /// <summary>Value written to the Excel cell: string, double or null (blank).</summary>
        public required object? Value { get; init; }

        /// <summary>Revit value as stored (internal units, ElementId value, 0/1 ...), invariant culture.</summary>
        public required string Raw { get; init; }

        /// <summary>Comparable form of what was written to Excel, invariant culture.</summary>
        public required string Normalised { get; init; }

        /// <summary>Revit's formatted display string.</summary>
        public required string Display { get; init; }

        /// <summary>Parameter.HasValue: distinguishes "never set" from an empty value.</summary>
        public required bool HasValue { get; init; }

        /// <summary>False when the element does not carry the parameter (cell is locked and blank).</summary>
        public required bool IsAvailable { get; init; }

        /// <summary>Lock this single cell even in an editable column.</summary>
        public bool ForceReadOnly { get; init; }

        /// <summary>Why the cell was locked (read-only on this element, model group, too long ...).</summary>
        public string? LockReason { get; init; }
    }
}
