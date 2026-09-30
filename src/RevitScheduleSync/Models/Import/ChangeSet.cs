using System.Collections.Generic;
using System.Linq;

namespace RevitScheduleSync.Models.Import
{
    /// <summary>Outcome of the three-way comparison for one cell (element × parameter).</summary>
    public enum ChangeStatus
    {
        /// <summary>A == B == C.</summary>
        NoChange,

        /// <summary>A == B, C differs: safe instance-parameter update candidate.</summary>
        Change,

        /// <summary>As Change, but on a type parameter: affects every instance of the type.</summary>
        TypeChange,

        /// <summary>B differs from A, Excel untouched: Revit changed since export, keep Revit.</summary>
        RevitChanged,

        /// <summary>B and C both differ from A but agree: nothing to write.</summary>
        SameChange,

        /// <summary>B and C both differ from A and from each other, or rows disagree: user must decide.</summary>
        Conflict,

        /// <summary>Excel value changed on a read-only field or locked cell: not applied.</summary>
        ReadOnly,

        /// <summary>Excel value cannot be converted (bad number, Yes/No, unknown reference ...).</summary>
        Invalid,

        /// <summary>Element no longer exists (deleted since export).</summary>
        MissingElement,

        /// <summary>Element exists but no longer has the parameter, or it changed storage type.</summary>
        MissingParameter,

        /// <summary>Cannot be written now (worksharing ownership, out of date with central ...).</summary>
        Blocked,

        /// <summary>Will be applied once elsewhere (same element/type and value in another row or sheet).</summary>
        Duplicate
    }

    /// <summary>Why an item is a conflict; decides which review actions are possible.</summary>
    public enum ConflictKind
    {
        None,

        /// <summary>Revit and Excel both changed the value differently since export: user picks one.</summary>
        ThreeWay,

        /// <summary>Several rows/sheets set different values for the same element or type: fix in Excel.</summary>
        Disagreement,

        /// <summary>The element's type was swapped in Revit since export: type value not applicable.</summary>
        TypeSwapped
    }

    /// <summary>The reviewer's decision for one item. Only Apply / UseExcel ever lead to a write.</summary>
    public enum ReviewDecision
    {
        /// <summary>No action possible (informational or problem item).</summary>
        None,

        /// <summary>Needs an explicit decision (type changes, three-way conflicts). Not applied.</summary>
        Pending,

        Apply,
        Skip,
        KeepRevit,
        UseExcel
    }

    /// <summary>Kind-specific value ready to be written in the Apply phase (internal units, resolved ids).</summary>
    public sealed record TargetValue(ValueStorage Storage, string? Text, int? Integer, double? Double, long? ElementIdValue, bool Clear)
    {
        public static TargetValue OfText(string text) => new(ValueStorage.String, text, null, null, null, false);
        public static TargetValue OfInteger(int value) => new(ValueStorage.Integer, null, value, null, null, false);
        public static TargetValue OfDouble(double internalValue) => new(ValueStorage.Double, null, null, internalValue, null, false);
        public static TargetValue OfElementId(long id) => new(ValueStorage.ElementId, null, null, null, id, false);
    }

    public sealed class ChangeItem
    {
        public required ChangeStatus Status { get; set; }
        public required string SheetName { get; init; }
        public required string ScheduleName { get; init; }
        public required string ElementUniqueId { get; init; }
        public required long ElementId { get; init; }
        public required string ElementLabel { get; init; }
        public required int FieldId { get; init; }
        public required string ParameterName { get; init; }
        public required string ParameterKey { get; init; }
        public required bool IsType { get; init; }
        public string? TypeUniqueId { get; init; }
        public string? TypeName { get; init; }
        public int? ExcelRow { get; init; }

        /// <summary>A, B and C as text for display.</summary>
        public required string Original { get; init; }
        public required string Current { get; init; }
        public required string Excel { get; init; }

        /// <summary>Value to write when the item is applied (Change / TypeChange only).</summary>
        public TargetValue? Target { get; init; }

        /// <summary>Normalised C, used to detect disagreeing duplicates.</summary>
        public string? ExcelNormalised { get; init; }

        /// <summary>Instances of the type (type parameters only).</summary>
        public int? AffectedInstances { get; set; }

        public string Message { get; set; } = string.Empty;

        public ConflictKind ConflictKind { get; set; }

        /// <summary>Review decision (Phase 6). Set by the analyser to a safe default.</summary>
        public ReviewDecision Decision { get; set; }

        public bool IsApplicable => Status is ChangeStatus.Change or ChangeStatus.TypeChange;

        /// <summary>True when the Apply phase will write this item's Excel value.</summary>
        public bool WillWrite => Target != null && (
            (Status is ChangeStatus.Change or ChangeStatus.TypeChange && Decision == ReviewDecision.Apply)
            || (Status == ChangeStatus.Conflict && ConflictKind == ConflictKind.ThreeWay && Decision == ReviewDecision.UseExcel));
    }

    public enum FingerprintMatch
    {
        Match,

        /// <summary>No hard evidence of a different project, but not all identifiers could be compared.</summary>
        Probable,

        /// <summary>Workbook belongs to another project: import blocked.</summary>
        Mismatch
    }

    public sealed record FingerprintResult(FingerprintMatch Match, IReadOnlyList<string> Reasons);

    public sealed class ChangeSet
    {
        public required ImportWorkbook Workbook { get; init; }
        public required FingerprintResult Fingerprint { get; init; }
        public required IReadOnlyList<ChangeItem> Items { get; init; }
        public required IReadOnlyList<ImportIssue> Issues { get; init; }
        public required long ElapsedMilliseconds { get; init; }

        /// <summary>True when nothing may be applied (wrong project).</summary>
        public bool IsBlocked => Fingerprint.Match == FingerprintMatch.Mismatch;

        public int Count(ChangeStatus status) => Items.Count(i => i.Status == status);
    }
}
