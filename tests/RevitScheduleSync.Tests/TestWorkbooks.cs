using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RevitScheduleSync.Excel;
using RevitScheduleSync.Logging;
using RevitScheduleSync.Models;
using RevitScheduleSync.Models.Export;
using RevitScheduleSync.Services;

namespace RevitScheduleSync.Tests
{
    /// <summary>Builds export models like the Revit side would, without Revit.</summary>
    internal static class TestWorkbooks
    {
        public const int MarkField = 10;
        public const int TypeField = 11;
        public const int WidthField = 12;
        public const int FireExitField = 13;
        public const int CountField = 14;

        public static string NewPath() =>
            Path.Combine(Path.GetTempPath(), "RssTests", Guid.NewGuid().ToString("N"), "export.xlsx");

        public static ExportWorkbook Doors(int rows = 12, string scheduleName = "Door Schedule", int extraColumns = 0)
        {
            var columns = new List<ExportColumn>
            {
                Column(0, MarkField, "Mark", "Instance", BuiltIn("Mark", "ALL_MODEL_MARK"), false, ValueStorage.String,
                    FieldClassification.EditableInstance, CellKind.Text, true),
                Column(1, TypeField, "Type", "Instance", BuiltIn("Type", "ELEM_TYPE_PARAM"), false, ValueStorage.ElementId,
                    FieldClassification.ReadOnlySystem, CellKind.Reference, false, "Family/type identity."),
                Column(2, WidthField, "Width", "ElementType", BuiltIn("Width", "DOOR_WIDTH"), true, ValueStorage.Double,
                    FieldClassification.EditableType, CellKind.Number, true, unit: "autodesk.unit.unit:millimeters-1.0.1", format: "0"),
                Column(3, FireExitField, "Fire Exit", "Instance",
                    new ParameterIdentity { Source = ParameterSource.Shared, Name = "Fire Exit", SharedGuid = Guid.Parse("6f1c2c3e-4d5e-4f60-8a7b-9c0d1e2f3a4b"), ParameterElementId = 77 },
                    false, ValueStorage.Integer, FieldClassification.EditableInstance, CellKind.YesNo, true),
                Column(4, CountField, "Count", "Count", ParameterIdentity.None, false, ValueStorage.None,
                    FieldClassification.ReadOnlyCalculated, CellKind.Integer, false, "Count computed by the schedule.", format: "0")
            };

            for (int i = 0; i < extraColumns; i++)
            {
                int fieldId = 100 + i;
                columns.Add(Column(5 + i, fieldId, $"Text {i}", "Instance",
                    new ParameterIdentity { Source = ParameterSource.Project, Name = $"Text {i}", ParameterElementId = 200 + i },
                    false, ValueStorage.String, FieldClassification.EditableInstance, CellKind.Text, true));
            }

            var exportRows = Enumerable.Range(1, rows).Select(i =>
            {
                var cells = new List<ExportCell>
                {
                    Cell($"D{i:000}", $"D{i:000}"),
                    Cell("Single 900", "Single 900"),
                    Cell(914.4, "914.4"),
                    Cell(i % 2 == 0 ? "Yes" : "No", i % 2 == 0 ? "1" : "0"),
                    Cell(1d, "1")
                };
                cells.AddRange(Enumerable.Range(0, extraColumns).Select(c => Cell($"value {i}-{c}", $"value {i}-{c}")));

                return new ExportRow
                {
                    ElementUniqueId = $"uid-{i:000000}",
                    ElementId = 1000 + i,
                    TypeUniqueId = "type-a",
                    TypeId = 500,
                    CategoryName = "Doors",
                    Cells = cells
                };
            }).ToList();

            return new ExportWorkbook
            {
                Info = new WorkbookInfo
                {
                    SchemaVersion = WorkbookSchema.SchemaVersion,
                    PluginVersion = "test",
                    RevitVersion = "2026",
                    ExportedUtc = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc),
                    Fingerprint = new DocumentFingerprint { CreationGuid = "creation-1", ProjectInfoUniqueId = "pi-1", Title = "Test.rvt" }
                },
                Sheets =
                [
                    new ExportSheet
                    {
                        ScheduleUniqueId = "schedule-1",
                        ScheduleElementId = 9,
                        ScheduleName = scheduleName,
                        CategoryName = "Doors",
                        Mode = SheetMode.Editable,
                        Columns = columns,
                        Rows = exportRows
                    },
                    new ExportSheet
                    {
                        ScheduleUniqueId = "schedule-2",
                        ScheduleElementId = 10,
                        ScheduleName = "Door Schedule - Grouped",
                        CategoryName = "Doors",
                        Mode = SheetMode.Presentation,
                        PresentationRows = [["Type", "Count"], ["Type A", "37"], ["Type B", "18"]]
                    }
                ]
            };
        }

        public static string Write(ExportWorkbook workbook, bool protect = true)
        {
            var path = NewPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            new ClosedXmlWorkbookWriter(NullLogger.Instance).Write(workbook, new ExportOptions { FilePath = path, ProtectReadOnlyCells = protect });
            return path;
        }

        internal static ExportColumn Column(int index, int fieldId, string heading, string fieldType, ParameterIdentity parameter,
            bool isType, ValueStorage storage, FieldClassification classification, CellKind kind, bool editable,
            string? reason = null, string unit = "", string? format = null) => new()
        {
            Field = new ScheduleFieldInfo
            {
                FieldIndex = index,
                FieldId = fieldId,
                Heading = heading,
                FieldName = heading,
                FieldType = fieldType,
                Parameter = parameter,
                IsTypeParameter = isType,
                Storage = storage,
                SpecTypeId = string.Empty,
                SpecLabel = string.Empty,
                IsMeasurable = unit.Length > 0,
                IsYesNo = kind == CellKind.YesNo,
                DisplayUnitTypeId = unit,
                DisplayUnitLabel = unit.Length > 0 ? "Millimeters" : string.Empty,
                HasCustomFormat = false,
                IsHidden = false,
                IsCalculated = false,
                IsFormula = false,
                IsPercentage = false,
                IsCombined = false,
                IsCount = fieldType == "Count",
                IsRelatedElement = false,
                DisplayType = "Standard",
                Classification = classification,
                Reason = reason ?? string.Empty
            },
            Kind = kind,
            IsEditable = editable,
            ReadOnlyReason = reason,
            ExportUnitTypeId = unit,
            ExportUnitLabel = unit.Length > 0 ? "Millimeters" : string.Empty,
            NumberFormat = format
        };

        internal static ParameterIdentity BuiltIn(string name, string builtIn) =>
            new() { Source = ParameterSource.BuiltIn, Name = name, BuiltInName = builtIn, BuiltInValue = -1001 };

        internal static ExportCell Cell(object? value, string normalised) => new()
        {
            Value = value,
            Raw = normalised,
            Normalised = normalised,
            Display = normalised,
            HasValue = value != null,
            IsAvailable = true
        };
    }
}
