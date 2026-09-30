namespace RevitScheduleSync.Excel
{
    /// <summary>
    /// Workbook layout shared by the writer (Phase 3) and the reader (Phase 5).
    /// Changing anything here that affects existing files requires a SchemaVersion bump.
    /// </summary>
    public static class WorkbookSchema
    {
        public const int SchemaVersion = 1;

        public const string MetadataSheetName = "_RevitSyncMetadata";

        // Schedule sheet layout (1-based Excel rows/columns).
        public const int TitleRow = 1;
        public const int LegendRow = 2;
        public const int HeaderRow = 4;
        public const int FirstDataRow = 5;

        /// <summary>Hidden, locked column holding Element.UniqueId: keeps row identity when users filter or sort.</summary>
        public const int IdentityColumn = 1;
        public const int FirstFieldColumn = 2;
        public const string IdentityHeader = "_ElementUniqueId";

        // Metadata sheet: sections start with a marker row "[Name]", followed by a header row, then data
        // rows until an empty row.
        public const string SectionWorkbook = "[Workbook]";
        public const string SectionSheets = "[Sheets]";
        public const string SectionColumns = "[Columns]";
        public const string SectionRows = "[Rows]";
        public const string SectionValues = "[Values]";

        /// <summary>Guard against hitting Excel's 1,048,576 row limit in the metadata sheet.</summary>
        public const int MaxMetadataRows = 1_000_000;

        public static readonly string[] SheetsHeader =
        [
            "SheetName", "ScheduleUniqueId", "ScheduleElementId", "ScheduleName", "Category", "Mode",
            "HeaderRow", "FirstDataRow", "LastDataRow", "IdentityColumn"
        ];

        public static readonly string[] ColumnsHeader =
        [
            "SheetName", "ExcelColumn", "FieldId", "FieldIndex", "Heading", "FieldType", "ParameterKey",
            "ParameterSource", "ParameterName", "BuiltInName", "BuiltInValue", "SharedGuid", "ParameterElementId",
            "IsType", "Storage", "SpecTypeId", "DisplayUnitTypeId", "ExportUnitTypeId", "CellKind",
            "Classification", "Editable", "ReadOnlyReason"
        ];

        public static readonly string[] RowsHeader =
        [
            "SheetName", "ElementUniqueId", "ElementId", "TypeUniqueId", "TypeId", "Category", "ExportedRow",
            "InGroup", "DesignOption"
        ];

        public static readonly string[] ValuesHeader =
        [
            "SheetName", "ElementUniqueId", "FieldId", "HasValue", "Raw", "Normalised", "Display", "LockReason"
        ];
    }
}
