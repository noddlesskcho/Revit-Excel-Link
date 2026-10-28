using System.Collections.Generic;
using RevitScheduleSync.Models.Export;

namespace RevitScheduleSync.Models
{
    /// <summary>What the "Model data" export reads from the model.</summary>
    public enum ModelSourceKind
    {
        ModelCategories,
        AnnotationCategories,

        /// <summary>Elements currently selected in Revit, one sheet per category.</summary>
        Selection,
        Rooms,
        Spaces,
        ProjectInformation,
        ObjectStyles,
        LineStyles,
        Families
    }

    /// <summary>Which elements of the chosen categories are exported.</summary>
    public enum ModelScope
    {
        WholeModel,
        ActiveView
    }

    /// <summary>One category offered for export (plain data).</summary>
    public sealed class CategoryChoice
    {
        public required long CategoryId { get; init; }
        public required string Name { get; init; }

        /// <summary>Instances in the requested scope (host model only).</summary>
        public required int Count { get; init; }
    }

    /// <summary>One parameter offered for export, found on instances or types of the chosen categories.</summary>
    public sealed class ParameterChoice
    {
        public required ParameterIdentity Parameter { get; init; }
        public required bool IsType { get; init; }
        public required ValueStorage Storage { get; init; }
        public required bool IsReadOnly { get; init; }
        public required string GroupName { get; init; }

        /// <summary>Stable key across sessions (profiles store it).</summary>
        public string Key => $"{(IsType ? "T" : "I")}|{Parameter.Key}";

        public string Name => Parameter.Name;

        public override string ToString() => Name;
    }

    /// <summary>Everything needed to build the sheets of a model-data export.</summary>
    public sealed class ModelExportRequest
    {
        public required ModelSourceKind Source { get; init; }
        public ModelScope Scope { get; init; } = ModelScope.WholeModel;

        /// <summary>Categories to export (category sources only).</summary>
        public IReadOnlyList<long> CategoryIds { get; init; } = [];

        /// <summary>Parameters to export, in column order (keys of <see cref="ParameterChoice"/>).</summary>
        public IReadOnlyList<string> ParameterKeys { get; init; } = [];

        /// <summary>Add read-only rows for the same categories from loaded linked models.</summary>
        public bool IncludeLinkedModels { get; init; }

        /// <summary>One row per element type instead of per instance (type parameters only).</summary>
        public bool ExportByType { get; init; }

        /// <summary>Rooms and spaces: export only the columns, so new rows can be typed in.</summary>
        public bool EmptyTemplate { get; init; }
    }

    /// <summary>Saved selection of a "Model data" export, reusable across projects (JSON).</summary>
    public sealed class ModelExportProfile
    {
        public int Version { get; set; } = 1;
        public string Name { get; set; } = string.Empty;
        public ModelSourceKind Source { get; set; }
        public ModelScope Scope { get; set; }

        /// <summary>Category names (ids differ between projects for non-built-in categories).</summary>
        public List<string> Categories { get; set; } = [];

        public List<string> ParameterKeys { get; set; } = [];
        public bool IncludeLinkedModels { get; set; }
        public bool ExportByType { get; set; }
    }
}
