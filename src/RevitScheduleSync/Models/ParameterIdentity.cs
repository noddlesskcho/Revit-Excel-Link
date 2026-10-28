using System;

namespace RevitScheduleSync.Models
{
    public enum ParameterSource
    {
        /// <summary>The field has no parameter (count, formula, percentage, combined).</summary>
        None,

        /// <summary>Revit built-in parameter (BuiltInParameter enum value).</summary>
        BuiltIn,

        /// <summary>Shared parameter, identified by its GUID.</summary>
        Shared,

        /// <summary>Non-shared project parameter, identified by its ParameterElement.</summary>
        Project,

        /// <summary>Graphic property of an object or line style (line weight, colour ...), stored in BuiltInName.</summary>
        Style,

        /// <summary>Information column written by the add-in (Element ID, Family, Category ...). Never imported.</summary>
        Info
    }

    /// <summary>
    /// The real identity of the parameter behind a schedule column.
    /// Column headings can be renamed by users, so mapping always uses this instead.
    /// </summary>
    public sealed class ParameterIdentity
    {
        public static readonly ParameterIdentity None = new() { Source = ParameterSource.None, Name = string.Empty };

        public required ParameterSource Source { get; init; }

        /// <summary>Parameter name as Revit defines it (not the column heading).</summary>
        public required string Name { get; init; }

        /// <summary>BuiltInParameter enum name, e.g. "ALL_MODEL_MARK". Built-in only.</summary>
        public string? BuiltInName { get; init; }

        /// <summary>Negative ElementId value of the built-in parameter. Built-in only.</summary>
        public long? BuiltInValue { get; init; }

        /// <summary>Shared parameter GUID. Shared only.</summary>
        public Guid? SharedGuid { get; init; }

        /// <summary>ElementId of the ParameterElement (shared or project). Document-specific: diagnostic only.</summary>
        public long? ParameterElementId { get; init; }

        /// <summary>
        /// Stable key for matching the same parameter across export and import.
        /// Built-in: enum name. Shared: GUID. Project: name (ParameterElement ids can differ between documents,
        /// but the document fingerprint check makes the id usable as a secondary check).
        /// </summary>
        public string Key => Source switch
        {
            ParameterSource.BuiltIn => $"bip:{BuiltInName}",
            ParameterSource.Shared => $"shared:{SharedGuid:D}",
            ParameterSource.Project => $"project:{Name}",
            ParameterSource.Style => $"style:{BuiltInName}",
            ParameterSource.Info => $"info:{BuiltInName}",
            _ => "none"
        };

        public string SourceText => Source switch
        {
            ParameterSource.BuiltIn => "Built-in",
            ParameterSource.Shared => "Shared",
            ParameterSource.Project => "Project",
            ParameterSource.Style => "Style",
            ParameterSource.Info => "Information",
            _ => "–"
        };

        public override string ToString() => Key;
    }
}
