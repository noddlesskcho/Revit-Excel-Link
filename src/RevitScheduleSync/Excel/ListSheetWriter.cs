using System;
using System.Collections.Generic;
using System.Linq;
using ClosedXML.Excel;
using RevitScheduleSync.Models.Export;

namespace RevitScheduleSync.Excel
{
    /// <summary>
    /// Writes the very hidden sheet that holds drop-down value lists. Each distinct list is written once,
    /// in its own column, and exposed as a workbook-level named range that data validations refer to.
    /// The lists are a typing aid only: import resolves names against the model, never against this sheet.
    /// </summary>
    internal sealed class ListSheetWriter
    {
        /// <summary>Excel data validation lists are limited in practice; longer lists are not offered as drop-downs.</summary>
        public const int MaxListLength = 5000;

        private readonly IXLWorksheet _sheet;
        private readonly Dictionary<string, string> _rangeByList = new(StringComparer.Ordinal);
        private int _nextColumn = 1;

        private ListSheetWriter(IXLWorksheet sheet)
        {
            _sheet = sheet;
            _sheet.Style.NumberFormat.Format = "@";
        }

        /// <summary>Creates the lists sheet, or returns null when no column has a list.</summary>
        public static ListSheetWriter? Create(XLWorkbook book, IReadOnlyList<ExportSheet> sheets)
        {
            bool any = sheets.Any(s => s.Columns.Any(c => c.IsEditable && c.ListValues is { Count: > 0 }));
            return any ? new ListSheetWriter(book.Worksheets.Add(WorkbookSchema.ListsSheetName)) : null;
        }

        /// <summary>Returns the name of the named range holding these values, writing them on first use.</summary>
        public string? RangeFor(IReadOnlyList<string> values)
        {
            if (values.Count == 0 || values.Count > MaxListLength)
            {
                return null;
            }

            var key = string.Join("\u001f", values);
            if (_rangeByList.TryGetValue(key, out var existing))
            {
                return existing;
            }

            int column = _nextColumn++;
            for (int i = 0; i < values.Count; i++)
            {
                _sheet.Cell(i + 1, column).SetValue(values[i]);
            }

            var name = $"RevitSyncList{column}";
            _sheet.Workbook.DefinedNames.Add(name, _sheet.Range(1, column, values.Count, column));
            _rangeByList[key] = name;
            return name;
        }

        public void Finish(bool visible) =>
            _sheet.Visibility = visible ? XLWorksheetVisibility.Visible : XLWorksheetVisibility.VeryHidden;
    }
}
