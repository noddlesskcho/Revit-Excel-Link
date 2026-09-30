namespace RevitScheduleSync.Models
{
    /// <summary>
    /// Identifiers of the Revit document a workbook was exported from.
    /// Used on import to block workbooks that belong to another project.
    /// No single value is reliable on its own (e.g. projects created from the same template can share
    /// element UniqueIds), so several are stored and compared together in the import phase.
    /// </summary>
    public sealed class DocumentFingerprint
    {
        /// <summary>Document.CreationGUID: generated when the document was first created.</summary>
        public string? CreationGuid { get; init; }

        /// <summary>UniqueId of the ProjectInformation element.</summary>
        public string? ProjectInfoUniqueId { get; init; }

        public string ProjectNumber { get; init; } = string.Empty;

        public string ProjectName { get; init; } = string.Empty;

        /// <summary>Document title (file name without folder). Display only, never used alone for matching.</summary>
        public string Title { get; init; } = string.Empty;

        public bool IsWorkshared { get; init; }

        /// <summary>File name of the central model only; the folder is deliberately not stored.</summary>
        public string? CentralModelName { get; init; }

        /// <summary>Document.WorksharingCentralGUID; only available for Revit Server central models.</summary>
        public string? CentralGuid { get; init; }

        public string? CloudProjectGuid { get; init; }

        public string? CloudModelGuid { get; init; }
    }
}
