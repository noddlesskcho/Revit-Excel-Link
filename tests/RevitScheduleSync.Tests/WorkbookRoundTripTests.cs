using System.IO;
using System.Linq;
using ClosedXML.Excel;
using RevitScheduleSync.Excel;
using RevitScheduleSync.Logging;
using RevitScheduleSync.Models.Import;
using RevitScheduleSync.Services;
using Xunit;

namespace RevitScheduleSync.Tests
{
    public class WorkbookRoundTripTests
    {
        private static ImportWorkbook Read(string path) => new ClosedXmlWorkbookReader(NullLogger.Instance).Read(path);

        private static void Edit(string path, System.Action<IXLWorksheet> edit)
        {
            using var book = new XLWorkbook(path);
            var sheet = book.Worksheet("Door Schedule");
            sheet.Unprotect();
            edit(sheet);
            book.Save();
        }

        private static ImportRow Row(ImportWorkbook workbook, int number) =>
            workbook.Sheets[0].Rows.Single(r => r.ElementUniqueId == $"uid-{number:000000}");

        [Fact]
        public void Unedited_workbook_reads_back_exported_values_and_metadata()
        {
            var path = TestWorkbooks.Write(TestWorkbooks.Doors());
            var workbook = Read(path);

            Assert.Equal(WorkbookSchema.SchemaVersion, workbook.SchemaVersion);
            Assert.Equal("creation-1", workbook.Fingerprint.CreationGuid);
            Assert.Single(workbook.Sheets); // presentation sheet is never imported
            Assert.Contains(workbook.Issues, i => i.Message.Contains("presentation"));

            var row = Row(workbook, 1);
            Assert.Equal("D001", row.Values[TestWorkbooks.MarkField].Text);
            Assert.Equal(914.4, row.Values[TestWorkbooks.WidthField].Number);
            Assert.Equal("No", row.Values[TestWorkbooks.FireExitField].Text);
            Assert.Equal("D001", row.Originals[TestWorkbooks.MarkField].Normalised);
            Assert.Equal("914.4", row.Originals[TestWorkbooks.WidthField].Normalised);
            Assert.False(row.Originals.ContainsKey(TestWorkbooks.TypeField)); // read-only column: no original stored

            var width = workbook.Sheets[0].Columns.Single(c => c.FieldId == TestWorkbooks.WidthField);
            Assert.True(width.IsType);
            Assert.Equal("bip:DOOR_WIDTH", width.Parameter.Key);
            Assert.Equal("autodesk.unit.unit:millimeters-1.0.1", width.ExportUnitTypeId);
        }

        [Fact]
        public void Sorting_rows_in_excel_keeps_element_identity()
        {
            var path = TestWorkbooks.Write(TestWorkbooks.Doors(rows: 5));
            Edit(path, ws => ws.Range(WorkbookSchema.FirstDataRow, 1, WorkbookSchema.FirstDataRow + 4, 6).Sort(2, XLSortOrder.Descending));

            var workbook = Read(path);
            var row = Row(workbook, 1);
            Assert.Equal(WorkbookSchema.FirstDataRow + 4, row.ExcelRow); // D001 is now last
            Assert.Equal("D001", row.Values[TestWorkbooks.MarkField].Text);
        }

        [Fact]
        public void Inserted_column_is_found_by_heading()
        {
            var path = TestWorkbooks.Write(TestWorkbooks.Doors(rows: 3));
            Edit(path, ws => ws.Column(3).InsertColumnsBefore(1));

            var workbook = Read(path);
            Assert.Equal(TestWorkbooks.Doors().Sheets[0].Columns.Count, workbook.Sheets[0].Columns.Count(c => c.ExcelColumn.HasValue));
            Assert.Equal(914.4, Row(workbook, 2).Values[TestWorkbooks.WidthField].Number);
            Assert.Contains(workbook.Issues, i => i.Message.Contains("moved"));
        }

        [Fact]
        public void Deleted_added_and_copied_rows_are_reported_not_imported()
        {
            var path = TestWorkbooks.Write(TestWorkbooks.Doors(rows: 6));
            Edit(path, ws =>
            {
                ws.Row(WorkbookSchema.FirstDataRow + 5).Delete();                          // delete uid-6
                ws.Cell(40, 2).Value = "NEW DOOR";                                           // added row, no identity
                ws.Row(WorkbookSchema.FirstDataRow).CopyTo(ws.Row(41));                      // copy of uid-1
            });

            var workbook = Read(path);
            Assert.Null(Row(workbook, 6).ExcelRow);
            Assert.Null(Row(workbook, 1).ExcelRow); // both copies ignored
            Assert.NotNull(Row(workbook, 2).ExcelRow);
            Assert.Contains(workbook.Issues, i => i.Message.Contains("added in Excel"));
            Assert.Contains(workbook.Issues, i => i.Severity == IssueSeverity.Error && i.Message.Contains("more than once"));
            Assert.Contains(workbook.Issues, i => i.Message.Contains("no longer in the sheet"));
        }

        [Fact]
        public void Renamed_sheet_is_reported_and_skipped()
        {
            var path = TestWorkbooks.Write(TestWorkbooks.Doors(rows: 3));
            using (var book = new XLWorkbook(path))
            {
                book.Worksheet("Door Schedule").Name = "My doors";
                book.Save();
            }

            var workbook = Read(path);
            Assert.False(workbook.Sheets[0].SheetFound);
            Assert.Contains(workbook.Issues, i => i.Severity == IssueSeverity.Error && i.SheetName == "Door Schedule");
        }

        [Fact]
        public void Newer_schema_version_is_refused()
        {
            var path = TestWorkbooks.Write(TestWorkbooks.Doors(rows: 2));
            using (var book = new XLWorkbook(path))
            {
                var metadata = book.Worksheet(WorkbookSchema.MetadataSheetName);
                var cell = metadata.Column(1).CellsUsed().First(c => c.GetString() == "SchemaVersion").CellRight();
                cell.Value = "99";
                book.Save();
            }

            var error = Assert.Throws<ScheduleSyncException>(() => Read(path));
            Assert.Contains("newer version", error.Message);
        }

        [Fact]
        public void Damaged_metadata_is_refused()
        {
            var path = TestWorkbooks.Write(TestWorkbooks.Doors(rows: 2));
            using (var book = new XLWorkbook(path))
            {
                var metadata = book.Worksheet(WorkbookSchema.MetadataSheetName);
                metadata.Column(1).CellsUsed().First(c => c.GetString() == WorkbookSchema.SectionColumns).Value = "deleted";
                book.Save();
            }

            Assert.Throws<ScheduleSyncException>(() => Read(path));
        }

        [Fact]
        public void Ordinary_workbook_is_refused()
        {
            var path = TestWorkbooks.NewPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var book = new XLWorkbook())
            {
                book.AddWorksheet("Sheet1").Cell(1, 1).Value = "hello";
                book.SaveAs(path);
            }

            var error = Assert.Throws<ScheduleSyncException>(() => Read(path));
            Assert.Contains("not created by Revit Schedule Sync", error.Message);
        }

        [Fact]
        public void Exporting_over_an_open_file_gives_a_clear_message()
        {
            var path = TestWorkbooks.Write(TestWorkbooks.Doors(rows: 2));
            using var hold = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

            var error = Assert.Throws<ScheduleSyncException>(() =>
                new ClosedXmlWorkbookWriter(NullLogger.Instance).Write(TestWorkbooks.Doors(rows: 2),
                    new ExportOptions { FilePath = path }));
            Assert.Contains("open in another program", error.Message);
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp.xlsx"));
        }

        [Fact]
        public void Protection_locks_read_only_cells_and_hides_metadata()
        {
            var path = TestWorkbooks.Write(TestWorkbooks.Doors(rows: 2));
            using var book = new XLWorkbook(path);
            var sheet = book.Worksheet("Door Schedule");

            Assert.True(sheet.IsProtected);
            Assert.True(sheet.Column(WorkbookSchema.IdentityColumn).IsHidden);
            Assert.False(sheet.Cell(WorkbookSchema.FirstDataRow, 2).Style.Protection.Locked); // Mark: editable
            Assert.True(sheet.Cell(WorkbookSchema.FirstDataRow, 3).Style.Protection.Locked);  // Type: read-only
            Assert.Equal(XLWorksheetVisibility.VeryHidden, book.Worksheet(WorkbookSchema.MetadataSheetName).Visibility);
        }

        [Fact]
        public void Invalid_sheet_name_characters_are_replaced()
        {
            var path = TestWorkbooks.Write(TestWorkbooks.Doors(rows: 1, scheduleName: "Doors: Level 1/2 [Draft]?"));
            using var book = new XLWorkbook(path);
            Assert.Contains(book.Worksheets, w => w.Name == "Doors- Level 1-2 -Draft--");
        }
    }
}
