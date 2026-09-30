using System.Collections.Generic;
using System.Globalization;
using ClosedXML.Excel;
using RevitScheduleSync.Models.Export;

namespace RevitScheduleSync.Excel
{
    /// <summary>
    /// Writes the hidden metadata sheet. Every value is written as text (invariant culture)
    /// so Excel never reinterprets GUIDs, ids or numbers.
    /// </summary>
    internal static class MetadataSheetWriter
    {
        public static void Write(IXLWorksheet ws, ExportWorkbook workbook, IReadOnlyList<string> sheetNames)
        {
            // Text format everywhere so Excel keeps ids and numbers exactly as written if someone opens it.
            // Set before filling: applying a sheet style after hundreds of thousands of cells is slow.
            ws.Style.NumberFormat.Format = "@";

            int row = 1;
            var info = workbook.Info;
            var fp = info.Fingerprint;

            row = Section(ws, row, WorkbookSchema.SectionWorkbook, ["Key", "Value"],
            [
                ["SchemaVersion", Text(info.SchemaVersion)],
                ["PluginVersion", info.PluginVersion],
                ["RevitVersion", info.RevitVersion],
                ["ExportedUtc", info.ExportedUtc.ToString("O", CultureInfo.InvariantCulture)],
                ["Doc.CreationGuid", fp.CreationGuid ?? string.Empty],
                ["Doc.ProjectInfoUniqueId", fp.ProjectInfoUniqueId ?? string.Empty],
                ["Doc.ProjectNumber", fp.ProjectNumber],
                ["Doc.ProjectName", fp.ProjectName],
                ["Doc.Title", fp.Title],
                ["Doc.IsWorkshared", fp.IsWorkshared ? "true" : "false"],
                ["Doc.CentralModelName", fp.CentralModelName ?? string.Empty],
                ["Doc.CentralGuid", fp.CentralGuid ?? string.Empty],
                ["Doc.CloudProjectGuid", fp.CloudProjectGuid ?? string.Empty],
                ["Doc.CloudModelGuid", fp.CloudModelGuid ?? string.Empty]
            ]);

            var sheets = new List<string[]>();
            var columns = new List<string[]>();
            var rows = new List<string[]>();
            var values = new List<string[]>();

            for (int s = 0; s < workbook.Sheets.Count; s++)
            {
                var sheet = workbook.Sheets[s];
                var name = sheetNames[s];
                bool editable = sheet.Mode == SheetMode.Editable;
                int lastDataRow = WorkbookSchema.FirstDataRow + (editable ? sheet.Rows.Count : sheet.PresentationRows.Count) - 1;

                sheets.Add(
                [
                    name, sheet.ScheduleUniqueId, Text(sheet.ScheduleElementId), sheet.ScheduleName, sheet.CategoryName,
                    sheet.Mode.ToString(), Text(WorkbookSchema.HeaderRow), Text(WorkbookSchema.FirstDataRow),
                    Text(lastDataRow), editable ? Text(WorkbookSchema.IdentityColumn) : string.Empty
                ]);

                if (!editable)
                {
                    continue;
                }

                for (int c = 0; c < sheet.Columns.Count; c++)
                {
                    var column = sheet.Columns[c];
                    var f = column.Field;
                    var p = f.Parameter;
                    columns.Add(
                    [
                        name, Text(WorkbookSchema.FirstFieldColumn + c), Text(f.FieldId), Text(f.FieldIndex), f.Heading,
                        f.FieldType, p.Key, p.Source.ToString(), p.Name, p.BuiltInName ?? string.Empty,
                        p.BuiltInValue.HasValue ? Text(p.BuiltInValue.Value) : string.Empty,
                        p.SharedGuid?.ToString("D") ?? string.Empty,
                        p.ParameterElementId.HasValue ? Text(p.ParameterElementId.Value) : string.Empty,
                        f.IsTypeParameter ? "true" : "false", f.Storage.ToString(), f.SpecTypeId, f.DisplayUnitTypeId,
                        column.ExportUnitTypeId, column.Kind.ToString(), f.Classification.ToString(),
                        column.IsEditable ? "true" : "false", column.ReadOnlyReason ?? string.Empty
                    ]);
                }

                for (int r = 0; r < sheet.Rows.Count; r++)
                {
                    var exportRow = sheet.Rows[r];
                    rows.Add(
                    [
                        name, exportRow.ElementUniqueId, Text(exportRow.ElementId), exportRow.TypeUniqueId ?? string.Empty,
                        exportRow.TypeId.HasValue ? Text(exportRow.TypeId.Value) : string.Empty, exportRow.CategoryName,
                        Text(WorkbookSchema.FirstDataRow + r), exportRow.InGroup ? "true" : "false",
                        exportRow.DesignOptionName ?? string.Empty
                    ]);

                    // Original values of cells in editable columns: "A" in the three-way comparison on import.
                    // Locked cells are included (with their reason) so an edit to them can be reported, not applied.
                    for (int c = 0; c < sheet.Columns.Count; c++)
                    {
                        if (!sheet.Columns[c].IsEditable)
                        {
                            continue;
                        }

                        var cell = exportRow.Cells[c];
                        if (!cell.IsAvailable)
                        {
                            continue;
                        }

                        // Compact form (large schedules): HasValue only when false; Raw and Display only when
                        // they differ from Normalised. The reader falls back to Normalised when they are empty.
                        values.Add(
                        [
                            name, exportRow.ElementUniqueId, Text(sheet.Columns[c].Field.FieldId),
                            cell.HasValue ? string.Empty : "false",
                            cell.Raw == cell.Normalised ? string.Empty : cell.Raw,
                            cell.Normalised,
                            cell.Display == cell.Normalised ? string.Empty : cell.Display,
                            cell.ForceReadOnly ? cell.LockReason ?? "locked" : string.Empty
                        ]);
                    }
                }
            }

            row = Section(ws, row, WorkbookSchema.SectionSheets, WorkbookSchema.SheetsHeader, sheets);
            row = Section(ws, row, WorkbookSchema.SectionColumns, WorkbookSchema.ColumnsHeader, columns);
            row = Section(ws, row, WorkbookSchema.SectionRows, WorkbookSchema.RowsHeader, rows);
            Section(ws, row, WorkbookSchema.SectionValues, WorkbookSchema.ValuesHeader, values);
        }

        /// <summary>Marker row, header row, data rows, then one empty row. Returns the next free row.</summary>
        private static int Section(IXLWorksheet ws, int row, string marker, IReadOnlyList<string> header, IReadOnlyList<string[]> data)
        {
            ws.Cell(row++, 1).Value = marker;
            WriteRow(ws, row++, header);
            foreach (var values in data)
            {
                WriteRow(ws, row++, values);
            }

            return row + 1;
        }

        private static void WriteRow(IXLWorksheet ws, int row, IReadOnlyList<string> values)
        {
            for (int c = 0; c < values.Count; c++)
            {
                if (values[c].Length > 0)
                {
                    ws.Cell(row, c + 1).SetValue(values[c]);
                }
            }
        }

        private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
