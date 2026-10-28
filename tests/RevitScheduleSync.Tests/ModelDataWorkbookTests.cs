using System;
using System.Collections.Generic;
using System.Linq;
using ClosedXML.Excel;
using RevitScheduleSync.Excel;
using RevitScheduleSync.Logging;
using RevitScheduleSync.Models;
using RevitScheduleSync.Models.Export;
using RevitScheduleSync.Models.Import;
using Xunit;

namespace RevitScheduleSync.Tests
{
    /// <summary>Model-data sheets (rooms, linked rows, drop-down lists) through the real writer and reader.</summary>
    public class ModelDataWorkbookTests
    {
        private const int PhaseField = 2;
        private const int NumberField = 100;
        private const int NameField = 101;
        private const int LevelField = 102;

        private static ImportWorkbook Read(string path) => new ClosedXmlWorkbookReader(NullLogger.Instance).Read(path);

        private static ExportWorkbook Rooms(bool withLinkedRow = false)
        {
            var info = new ParameterIdentity { Source = ParameterSource.Info, Name = "Phase", BuiltInName = "Phase" };
            var columns = new List<ExportColumn>
            {
                TestWorkbooks.Column(0, PhaseField, "Phase", "Info", info, false, ValueStorage.String,
                    FieldClassification.ReadOnlySystem, CellKind.Text, true) with { ListValues = ["Existing", "New Construction"], RequiredForNewRows = true },
                TestWorkbooks.Column(1, NumberField, "Number", "Instance", TestWorkbooks.BuiltIn("Number", "ROOM_NUMBER"), false, ValueStorage.String,
                    FieldClassification.EditableInstance, CellKind.Text, true) with { RequiredForNewRows = true },
                TestWorkbooks.Column(2, NameField, "Name", "Instance", TestWorkbooks.BuiltIn("Name", "ROOM_NAME"), false, ValueStorage.String,
                    FieldClassification.EditableInstance, CellKind.Text, true) with { RequiredForNewRows = true },
                TestWorkbooks.Column(3, LevelField, "Level", "Instance", TestWorkbooks.BuiltIn("Level", "ROOM_LEVEL_ID"), false, ValueStorage.ElementId,
                    FieldClassification.EditableInstance, CellKind.Reference, true) with { ListValues = ["Level 1", "Level 2"] }
            };

            ExportRow Row(int i, string? link = null) => new()
            {
                ElementUniqueId = link == null ? $"room-{i}" : $"link:{link}:room-{i}",
                ElementId = 2000 + i,
                CategoryName = "Rooms",
                LinkName = link,
                Cells =
                [
                    TestWorkbooks.Cell("New Construction", "New Construction") with { ForceReadOnly = true, LockReason = "phase" },
                    TestWorkbooks.Cell($"{100 + i}", $"{100 + i}"),
                    TestWorkbooks.Cell($"Office {i}", $"Office {i}"),
                    TestWorkbooks.Cell("Level 1", "Level 1")
                ]
            };

            var rows = new List<ExportRow> { Row(1), Row(2) };
            if (withLinkedRow)
            {
                var linked = Row(9, "Structure.rvt");
                rows.Add(new ExportRow
                {
                    ElementUniqueId = linked.ElementUniqueId,
                    ElementId = linked.ElementId,
                    CategoryName = linked.CategoryName,
                    LinkName = linked.LinkName,
                    Cells = linked.Cells.Select(c => c with { ForceReadOnly = true, LockReason = "element is in a linked model" }).ToList()
                });
            }

            var doors = TestWorkbooks.Doors(rows: 1);
            return new ExportWorkbook
            {
                Info = doors.Info,
                Sheets =
                [
                    new ExportSheet
                    {
                        Source = SheetSource.Rooms,
                        AllowNewRows = true,
                        ScheduleUniqueId = "category:-2000160",
                        ScheduleElementId = -2000160,
                        ScheduleName = "Rooms",
                        CategoryName = "Rooms",
                        Mode = SheetMode.Editable,
                        Columns = columns,
                        Rows = rows
                    }
                ]
            };
        }

        [Fact]
        public void Rows_added_to_a_rooms_sheet_are_read_as_new_elements()
        {
            var path = TestWorkbooks.Write(Rooms());
            using (var book = new XLWorkbook(path))
            {
                var ws = book.Worksheet("Rooms");
                int row = WorkbookSchema.FirstDataRow + 2; // first row below the data
                ws.Cell(row, WorkbookSchema.FirstFieldColumn).Value = "New Construction";
                ws.Cell(row, WorkbookSchema.FirstFieldColumn + 1).Value = "201";
                ws.Cell(row, WorkbookSchema.FirstFieldColumn + 2).Value = "Meeting";
                book.Save();
            }

            var workbook = Read(path);
            var sheet = Assert.Single(workbook.Sheets);
            Assert.Equal(SheetSource.Rooms, sheet.Source);
            Assert.True(sheet.AllowNewRows);

            var added = Assert.Single(sheet.NewRows);
            Assert.Equal("New Construction", added.Values[PhaseField].Text);
            Assert.Equal("201", added.Values[NumberField].Text);
            Assert.Equal("Meeting", added.Values[NameField].Text);
            Assert.True(added.Values[LevelField].IsBlank);
            Assert.True(sheet.Columns.Single(c => c.FieldId == NumberField).RequiredForNewRows);
            Assert.DoesNotContain(workbook.Issues, i => i.Message.Contains("import never creates elements"));
        }

        [Fact]
        public void New_row_cells_are_unlocked_on_the_protected_sheet()
        {
            var path = TestWorkbooks.Write(Rooms());
            using var book = new XLWorkbook(path);
            var ws = book.Worksheet("Rooms");
            int row = WorkbookSchema.FirstDataRow + 2;

            Assert.True(ws.IsProtected);
            Assert.False(ws.Cell(row, WorkbookSchema.FirstFieldColumn + 1).Style.Protection.Locked);
            // Phase of an existing room is locked even though new rows may set it.
            Assert.True(ws.Cell(WorkbookSchema.FirstDataRow, WorkbookSchema.FirstFieldColumn).Style.Protection.Locked);
        }

        [Fact]
        public void Drop_down_lists_are_written_to_a_very_hidden_sheet_and_validated()
        {
            var path = TestWorkbooks.Write(Rooms());
            using var book = new XLWorkbook(path);

            var lists = book.Worksheet(WorkbookSchema.ListsSheetName);
            Assert.Equal(XLWorksheetVisibility.VeryHidden, lists.Visibility);
            var values = lists.Column(1).CellsUsed().Concat(lists.Column(2).CellsUsed()).Select(c => c.GetString()).ToList();
            Assert.Contains("Level 2", values);
            Assert.Contains("New Construction", values);

            var ws = book.Worksheet("Rooms");
            var level = ws.Cell(WorkbookSchema.FirstDataRow, WorkbookSchema.FirstFieldColumn + 3);
            Assert.True(level.HasDataValidation);
            Assert.Equal(XLAllowedValues.List, level.GetDataValidation().AllowedValues);
            Assert.StartsWith("RevitSyncList", level.GetDataValidation().Value.TrimStart('='));
        }

        [Fact]
        public void Linked_rows_are_marked_and_store_no_original_values()
        {
            var path = TestWorkbooks.Write(Rooms(withLinkedRow: true));
            var sheet = Assert.Single(Read(path).Sheets);

            var linked = sheet.Rows.Single(r => r.ElementUniqueId.StartsWith("link:", StringComparison.Ordinal));
            Assert.True(linked.IsLinked);
            Assert.Empty(linked.Originals);
            Assert.All(sheet.Rows.Where(r => !r.IsLinked), r => Assert.NotEmpty(r.Originals));
        }

        [Fact]
        public void Rows_added_to_a_schedule_sheet_are_still_ignored()
        {
            var path = TestWorkbooks.Write(TestWorkbooks.Doors(rows: 2));
            using (var book = new XLWorkbook(path))
            {
                var ws = book.Worksheet("Door Schedule");
                ws.Unprotect();
                ws.Cell(WorkbookSchema.FirstDataRow + 2, WorkbookSchema.FirstFieldColumn).Value = "D999";
                book.Save();
            }

            var workbook = Read(path);
            Assert.Empty(workbook.Sheets[0].NewRows);
            Assert.Contains(workbook.Issues, i => i.Message.Contains("import never creates elements"));
        }
    }
}
