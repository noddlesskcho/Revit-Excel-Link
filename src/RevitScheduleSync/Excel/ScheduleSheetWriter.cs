using System;
using System.Linq;
using System.Text;
using ClosedXML.Excel;
using RevitScheduleSync.Logging;
using RevitScheduleSync.Models;
using RevitScheduleSync.Models.Export;

namespace RevitScheduleSync.Excel
{
    /// <summary>Writes the user-facing schedule sheets. Styling is deliberately subtle.</summary>
    internal static class ScheduleSheetWriter
    {
        private const int WidthSampleRows = 200;
        private const double MinColumnWidth = 8;
        private const double MaxColumnWidth = 50;

        private static readonly XLColor HeaderInstance = XLColor.FromHtml("#E6F4EA");
        private static readonly XLColor HeaderType = XLColor.FromHtml("#DCE9F9");
        private static readonly XLColor HeaderReadOnly = XLColor.FromHtml("#E9EBEE");
        private static readonly XLColor CellType = XLColor.FromHtml("#F1F6FD");
        private static readonly XLColor CellReadOnly = XLColor.FromHtml("#F5F6F8");
        private static readonly XLColor TextReadOnly = XLColor.FromHtml("#6B7280");
        private static readonly XLColor Border = XLColor.FromHtml("#C9CED6");
        private static readonly XLColor Banner = XLColor.FromHtml("#FFF3DC");

        public static void WriteEditable(IXLWorksheet ws, ExportSheet sheet, bool protect, ILogger logger)
        {
            int lastColumn = WorkbookSchema.FirstFieldColumn + sheet.Columns.Count - 1;
            int lastRow = WorkbookSchema.FirstDataRow + sheet.Rows.Count - 1;

            WriteTitle(ws, sheet, "Itemised — edit white (instance) and blue (type) cells, then import.");
            WriteLegend(ws);

            // Identity column: hidden and locked. Survives sorting and filtering, unlike row numbers.
            ws.Cell(WorkbookSchema.HeaderRow, WorkbookSchema.IdentityColumn).Value = WorkbookSchema.IdentityHeader;
            for (int r = 0; r < sheet.Rows.Count; r++)
            {
                ws.Cell(WorkbookSchema.FirstDataRow + r, WorkbookSchema.IdentityColumn).Value = sheet.Rows[r].ElementUniqueId;
            }

            ws.Column(WorkbookSchema.IdentityColumn).Hide();

            for (int c = 0; c < sheet.Columns.Count; c++)
            {
                var column = sheet.Columns[c];
                int excelColumn = WorkbookSchema.FirstFieldColumn + c;

                var header = ws.Cell(WorkbookSchema.HeaderRow, excelColumn);
                header.Value = column.Field.Heading;
                header.CreateComment().AddText(BuildHeaderComment(column));
                StyleHeader(header, column);

                for (int r = 0; r < sheet.Rows.Count; r++)
                {
                    var cell = sheet.Rows[r].Cells[c];
                    var target = ws.Cell(WorkbookSchema.FirstDataRow + r, excelColumn);
                    target.Value = cell.Value switch
                    {
                        double d => d,
                        string s => s,
                        _ => Blank.Value
                    };

                    bool cellEditable = column.IsEditable && cell.IsAvailable && !cell.ForceReadOnly;
                    if (cellEditable)
                    {
                        target.Style.Protection.Locked = false;
                        if (column.Field.IsTypeParameter)
                        {
                            target.Style.Fill.BackgroundColor = CellType;
                        }
                    }
                    else
                    {
                        target.Style.Fill.BackgroundColor = CellReadOnly;
                        target.Style.Font.FontColor = TextReadOnly;
                    }
                }

                if (sheet.Rows.Count > 0)
                {
                    var data = ws.Range(WorkbookSchema.FirstDataRow, excelColumn, lastRow, excelColumn);
                    if (column.NumberFormat != null)
                    {
                        data.Style.NumberFormat.Format = column.NumberFormat;
                    }
                    else if (column.Kind is CellKind.Text or CellKind.Reference or CellKind.YesNo)
                    {
                        // Text format stops Excel turning "001" or "1-2" into numbers/dates when edited.
                        data.Style.NumberFormat.Format = "@";
                    }

                    if (column.IsEditable)
                    {
                        AddValidation(data, column);
                    }
                }
            }

            if (sheet.Columns.Count > 0)
            {
                ws.Range(WorkbookSchema.HeaderRow, WorkbookSchema.IdentityColumn, Math.Max(lastRow, WorkbookSchema.HeaderRow), lastColumn)
                    .SetAutoFilter();
                SetColumnWidths(ws, lastColumn, lastRow, logger);
            }

            ws.SheetView.FreezeRows(WorkbookSchema.HeaderRow);

            if (protect)
            {
                // No password: protection prevents accidents, it is not security. Excel does not allow
                // sorting ranges with locked cells on a protected sheet; filtering still works.
                ws.Protect()
                    .AllowElement(XLSheetProtectionElements.SelectEverything)
                    .AllowElement(XLSheetProtectionElements.AutoFilter)
                    .AllowElement(XLSheetProtectionElements.FormatColumns)
                    .AllowElement(XLSheetProtectionElements.FormatRows);
            }
        }

        public static void WritePresentation(IXLWorksheet ws, ExportSheet sheet, bool protect)
        {
            WriteTitle(ws, sheet, null);

            var banner = ws.Cell(WorkbookSchema.LegendRow, WorkbookSchema.FirstFieldColumn);
            banner.Value = "Presentation Export — Re-import not supported";
            banner.Style.Fill.BackgroundColor = Banner;
            banner.Style.Font.Bold = true;

            int startRow = WorkbookSchema.HeaderRow;
            int maxColumns = sheet.PresentationRows.Count == 0 ? 0 : sheet.PresentationRows.Max(r => r.Count);

            for (int r = 0; r < sheet.PresentationRows.Count; r++)
            {
                var row = sheet.PresentationRows[r];
                for (int c = 0; c < row.Count; c++)
                {
                    if (row[c].Length > 0)
                    {
                        ws.Cell(startRow + r, WorkbookSchema.FirstFieldColumn + c).Value = row[c];
                    }
                }
            }

            // The first body row of a Revit schedule holds the column headings.
            if (maxColumns > 0 && sheet.PresentationRows.Count > 0)
            {
                var headings = ws.Range(startRow, WorkbookSchema.FirstFieldColumn, startRow, WorkbookSchema.FirstFieldColumn + maxColumns - 1);
                headings.Style.Font.Bold = true;
                headings.Style.Fill.BackgroundColor = HeaderReadOnly;
                headings.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
                headings.Style.Border.BottomBorderColor = Border;

                for (int c = 0; c < maxColumns; c++)
                {
                    ws.Column(WorkbookSchema.FirstFieldColumn + c).Width = 18;
                }
            }

            ws.Column(WorkbookSchema.IdentityColumn).Width = 2;
            ws.SheetView.FreezeRows(startRow);

            if (protect)
            {
                ws.Protect()
                    .AllowElement(XLSheetProtectionElements.SelectEverything)
                    .AllowElement(XLSheetProtectionElements.FormatColumns)
                    .AllowElement(XLSheetProtectionElements.FormatRows);
            }
        }

        private static void WriteTitle(IXLWorksheet ws, ExportSheet sheet, string? subtitle)
        {
            var title = ws.Cell(WorkbookSchema.TitleRow, WorkbookSchema.FirstFieldColumn);
            title.Value = sheet.ScheduleName;
            title.Style.Font.Bold = true;
            title.Style.Font.FontSize = 13;

            if (subtitle != null)
            {
                var note = ws.Cell(WorkbookSchema.TitleRow, WorkbookSchema.FirstFieldColumn + 1);
                note.Value = $"{sheet.CategoryName} · {subtitle}";
                note.Style.Font.FontColor = TextReadOnly;
            }
        }

        private static void WriteLegend(IXLWorksheet ws)
        {
            int c = WorkbookSchema.FirstFieldColumn;
            Legend(ws.Cell(WorkbookSchema.LegendRow, c), "Editable", XLColor.White);
            Legend(ws.Cell(WorkbookSchema.LegendRow, c + 1), "Editable (type)", CellType);
            Legend(ws.Cell(WorkbookSchema.LegendRow, c + 2), "Read-only", CellReadOnly);
            ws.Cell(WorkbookSchema.LegendRow, c + 1).CreateComment()
                .AddText("Type parameter: one change applies to every element of that type.");
        }

        private static void Legend(IXLCell cell, string text, XLColor fill)
        {
            cell.Value = text;
            cell.Style.Fill.BackgroundColor = fill;
            cell.Style.Font.FontSize = 9;
            cell.Style.Font.FontColor = TextReadOnly;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.OutsideBorderColor = Border;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        private static void StyleHeader(IXLCell header, ExportColumn column)
        {
            header.Style.Font.Bold = true;
            header.Style.Alignment.WrapText = true;
            header.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
            header.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            header.Style.Border.BottomBorderColor = Border;
            header.Style.Fill.BackgroundColor = !column.IsEditable
                ? HeaderReadOnly
                : column.Field.IsTypeParameter ? HeaderType : HeaderInstance;
            if (!column.IsEditable)
            {
                header.Style.Font.FontColor = TextReadOnly;
            }
        }

        private static string BuildHeaderComment(ExportColumn column)
        {
            var field = column.Field;
            var text = new StringBuilder();
            text.AppendLine(field.Parameter.Source == ParameterSource.None ? field.FieldName : field.Parameter.Name);

            if (column.IsEditable)
            {
                text.AppendLine(field.IsTypeParameter
                    ? "Editable type parameter: changes every element of the type."
                    : "Editable instance parameter.");
            }
            else
            {
                text.AppendLine("Read-only: " + (column.ReadOnlyReason ?? "not editable."));
            }

            if (!string.IsNullOrEmpty(column.ExportUnitLabel))
            {
                text.AppendLine($"Unit: {column.ExportUnitLabel}");
            }

            if (column.Kind == CellKind.YesNo)
            {
                text.AppendLine("Values: Yes / No");
            }

            if (column.Kind == CellKind.Reference && column.IsEditable)
            {
                text.AppendLine("Type the exact name of an existing element.");
            }

            return text.ToString().TrimEnd();
        }

        private static void AddValidation(IXLRange data, ExportColumn column)
        {
            IXLDataValidation? validation = null;
            switch (column.Kind)
            {
                case CellKind.YesNo:
                    validation = data.CreateDataValidation();
                    validation.List("\"Yes,No\"", true);
                    validation.ErrorMessage = "Enter Yes or No, or leave the cell empty.";
                    break;
                case CellKind.Integer:
                    validation = data.CreateDataValidation();
                    validation.WholeNumber.Between(int.MinValue, int.MaxValue);
                    validation.ErrorMessage = "Enter a whole number, or leave the cell empty.";
                    break;
                case CellKind.Number:
                    validation = data.CreateDataValidation();
                    validation.Decimal.Between(-1e12, 1e12);
                    validation.ErrorMessage = string.IsNullOrEmpty(column.ExportUnitLabel)
                        ? "Enter a number, or leave the cell empty."
                        : $"Enter a number in {column.ExportUnitLabel}, or leave the cell empty.";
                    break;
            }

            if (validation != null)
            {
                validation.IgnoreBlanks = true;
                validation.ShowErrorMessage = true;
                validation.ErrorStyle = XLErrorStyle.Stop;
                validation.ErrorTitle = "Revit Schedule Sync";
            }
        }

        private static void SetColumnWidths(IXLWorksheet ws, int lastColumn, int lastRow, ILogger logger)
        {
            int sampleEnd = Math.Max(WorkbookSchema.HeaderRow, Math.Min(lastRow, WorkbookSchema.HeaderRow + WidthSampleRows));
            for (int c = WorkbookSchema.FirstFieldColumn; c <= lastColumn; c++)
            {
                var column = ws.Column(c);
                try
                {
                    // Measure a sample only: measuring every row is slow on large schedules.
                    column.AdjustToContents(WorkbookSchema.HeaderRow, sampleEnd);
                    column.Width = Math.Clamp(column.Width + 2, MinColumnWidth, MaxColumnWidth);
                }
                catch (Exception ex)
                {
                    logger.Debug($"Column width measurement failed ({ex.GetType().Name}); using default.");
                    column.Width = 16;
                }
            }
        }
    }
}
