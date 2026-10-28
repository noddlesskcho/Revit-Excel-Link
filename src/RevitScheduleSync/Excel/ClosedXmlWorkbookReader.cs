using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using RevitScheduleSync.Logging;
using RevitScheduleSync.Models;
using RevitScheduleSync.Models.Export;
using RevitScheduleSync.Models.Import;
using RevitScheduleSync.Services;

namespace RevitScheduleSync.Excel
{
    /// <summary>
    /// Reads a workbook written by ClosedXmlWorkbookWriter. Rows are matched through the hidden
    /// UniqueId column (not row numbers), columns through their heading at the recorded position,
    /// so filtering, sorting and moving columns in Excel do not break the mapping.
    /// </summary>
    public sealed class ClosedXmlWorkbookReader : IWorkbookReader
    {
        private readonly ILogger _logger;

        public ClosedXmlWorkbookReader(ILogger logger)
        {
            _logger = logger;
        }

        public ImportWorkbook Read(string path)
        {
            using var book = Open(path);

            if (!book.Worksheets.TryGetWorksheet(WorkbookSchema.MetadataSheetName, out var metadataSheet))
            {
                throw new ScheduleSyncException(
                    "This workbook was not created by Revit-Excel Link, or its hidden metadata sheet was removed. " +
                    "Only workbooks exported by Revit-Excel Link can be imported.");
            }

            Metadata metadata;
            try
            {
                metadata = Metadata.Parse(metadataSheet);
            }
            catch (Exception ex) when (ex is not ScheduleSyncException)
            {
                _logger.Warning("Metadata could not be parsed.", ex);
                throw new ScheduleSyncException(
                    "The workbook's metadata is damaged, so it cannot be matched safely to the model. Export a new workbook.", ex);
            }

            var issues = new List<ImportIssue>();
            var sheets = metadata.Sheets
                .Where(s => s.Mode == SheetMode.Editable)
                .Select(s => ReadSheet(book, s, metadata, issues))
                .ToList();

            int presentation = metadata.Sheets.Count(s => s.Mode == SheetMode.Presentation);
            if (presentation > 0)
            {
                issues.Add(new ImportIssue(IssueSeverity.Info, null,
                    $"{presentation} grouped (presentation) sheet(s) ignored: presentation exports are never re-imported."));
            }

            _logger.Info($"Workbook read: '{Path.GetFileName(path)}', schema {metadata.SchemaVersion}, " +
                         $"{sheets.Count} editable sheet(s), {sheets.Sum(s => s.Rows.Count)} rows, {issues.Count} issue(s).");

            return new ImportWorkbook
            {
                FilePath = path,
                SchemaVersion = metadata.SchemaVersion,
                PluginVersion = metadata.Get("PluginVersion"),
                RevitVersion = metadata.Get("RevitVersion"),
                ExportedUtc = DateTime.TryParse(metadata.Get("ExportedUtc"), CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var exported) ? exported : null,
                Fingerprint = metadata.Fingerprint(),
                Sheets = sheets,
                Issues = issues
            };
        }

        private XLWorkbook Open(string path)
        {
            if (!File.Exists(path))
            {
                throw new ScheduleSyncException($"The file \"{Path.GetFileName(path)}\" does not exist.");
            }

            try
            {
                // ReadWrite sharing: the workbook may be open in Excel. Unsaved Excel edits are not visible here.
                var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                return new XLWorkbook(stream);
            }
            catch (IOException ex)
            {
                _logger.Warning("Workbook could not be opened (IO).", ex);
                throw new ScheduleSyncException(
                    $"The file \"{Path.GetFileName(path)}\" could not be opened. Close it in other programs and try again.", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new ScheduleSyncException($"You do not have permission to read \"{Path.GetFileName(path)}\".", ex);
            }
            catch (Exception ex)
            {
                _logger.Warning("Workbook could not be parsed.", ex);
                throw new ScheduleSyncException(
                    $"\"{Path.GetFileName(path)}\" is not a valid .xlsx workbook, or it is damaged.", ex);
            }
        }

        private static ImportSheet ReadSheet(XLWorkbook book, SheetInfo info, Metadata metadata, List<ImportIssue> issues)
        {
            var columnsMeta = metadata.Columns.Where(c => c.SheetName == info.SheetName).ToList();
            var rowsMeta = metadata.Rows.Where(r => r.SheetName == info.SheetName).ToList();
            var originals = metadata.Values
                .Where(v => v.SheetName == info.SheetName)
                .GroupBy(v => v.ElementUniqueId)
                .ToDictionary(g => g.Key, g => (IReadOnlyDictionary<int, OriginalValue>)g.ToDictionary(v => v.FieldId, v => v.Value));

            if (!book.Worksheets.TryGetWorksheet(info.SheetName, out var ws)
                || ws.Cell(info.HeaderRow, WorkbookSchema.IdentityColumn).GetString() != WorkbookSchema.IdentityHeader)
            {
                issues.Add(new ImportIssue(IssueSeverity.Error, info.SheetName,
                    $"Sheet \"{info.SheetName}\" was not found or its hidden identity column was removed. Its rows are skipped."));
                return Build(info, false, columnsMeta.Select(c => c.ToImportColumn(null)).ToList(),
                    rowsMeta.Select(r => r.ToImportRow(null, new Dictionary<int, ExcelValue>(), originals)).ToList());
            }

            var columns = LocateColumns(ws, info, columnsMeta, issues);

            // Map UniqueId -> current sheet row; users may have filtered, sorted or deleted rows.
            var known = rowsMeta.Select(r => r.ElementUniqueId).ToHashSet(StringComparer.Ordinal);
            var rowOf = new Dictionary<string, int>(StringComparer.Ordinal);
            var duplicates = new HashSet<string>(StringComparer.Ordinal);
            var newRows = new List<ImportNewRow>();
            int addedRows = 0, unknownRows = 0;
            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;

            for (int r = info.FirstDataRow; r <= lastRow; r++)
            {
                var uid = ws.Cell(r, WorkbookSchema.IdentityColumn).GetString().Trim();
                if (uid.Length == 0)
                {
                    if (columns.Any(c => c.IsEditable && c.ExcelColumn.HasValue && !ws.Cell(r, c.ExcelColumn.Value).IsEmpty()))
                    {
                        if (info.AllowNewRows)
                        {
                            var entered = new Dictionary<int, ExcelValue>();
                            foreach (var column in columns.Where(c => c.IsEditable && c.ExcelColumn.HasValue))
                            {
                                entered[column.FieldId] = ReadCell(ws.Cell(r, column.ExcelColumn!.Value));
                            }

                            newRows.Add(new ImportNewRow { ExcelRow = r, Values = entered });
                        }
                        else
                        {
                            addedRows++;
                        }
                    }

                    continue;
                }

                if (!known.Contains(uid))
                {
                    unknownRows++;
                }
                else if (!rowOf.TryAdd(uid, r))
                {
                    duplicates.Add(uid);
                }
            }

            if (addedRows > 0)
            {
                issues.Add(new ImportIssue(IssueSeverity.Warning, info.SheetName,
                    $"{addedRows} row(s) added in Excel are ignored: import never creates elements."));
            }

            if (unknownRows > 0)
            {
                issues.Add(new ImportIssue(IssueSeverity.Warning, info.SheetName,
                    $"{unknownRows} row(s) have an identity that was not exported in this sheet; they are ignored."));
            }

            if (duplicates.Count > 0)
            {
                issues.Add(new ImportIssue(IssueSeverity.Error, info.SheetName,
                    $"{duplicates.Count} element row(s) appear more than once (copied rows?). All copies are ignored."));
            }

            int deleted = rowsMeta.Count(r => !rowOf.ContainsKey(r.ElementUniqueId));
            if (deleted > 0)
            {
                issues.Add(new ImportIssue(IssueSeverity.Info, info.SheetName,
                    $"{deleted} exported row(s) are no longer in the sheet; they are ignored (nothing is deleted in Revit)."));
            }

            var rows = new List<ImportRow>(rowsMeta.Count);
            foreach (var meta in rowsMeta)
            {
                int? excelRow = rowOf.TryGetValue(meta.ElementUniqueId, out var found) && !duplicates.Contains(meta.ElementUniqueId)
                    ? found
                    : null;

                var values = new Dictionary<int, ExcelValue>();
                if (excelRow.HasValue)
                {
                    foreach (var column in columns.Where(c => c.ExcelColumn.HasValue))
                    {
                        values[column.FieldId] = ReadCell(ws.Cell(excelRow.Value, column.ExcelColumn!.Value));
                    }
                }

                rows.Add(meta.ToImportRow(excelRow, values, originals));
            }

            if (newRows.Count > 0)
            {
                issues.Add(new ImportIssue(IssueSeverity.Info, info.SheetName,
                    $"{newRows.Count} new row(s) found: each one is reviewed as a new element to create."));
            }

            return Build(info, true, columns, rows, newRows);
        }

        /// <summary>
        /// Uses the recorded column when its heading still matches; otherwise looks for a unique heading
        /// elsewhere in the header row (column moved). Missing columns are reported and skipped.
        /// </summary>
        private static List<ImportColumn> LocateColumns(IXLWorksheet ws, SheetInfo info, List<ColumnInfo> columnsMeta, List<ImportIssue> issues)
        {
            var headerCells = ws.Row(info.HeaderRow).CellsUsed().ToList();
            var result = new List<ImportColumn>();

            foreach (var meta in columnsMeta)
            {
                int? position = null;
                if (ws.Cell(info.HeaderRow, meta.ExcelColumn).GetString() == meta.Heading)
                {
                    position = meta.ExcelColumn;
                }
                else
                {
                    var matches = headerCells.Where(c => c.GetString() == meta.Heading).ToList();
                    if (matches.Count == 1)
                    {
                        position = matches[0].Address.ColumnNumber;
                        issues.Add(new ImportIssue(IssueSeverity.Info, info.SheetName,
                            $"Column \"{meta.Heading}\" was moved in Excel; it was found by its heading."));
                    }
                    else if (meta.Editable)
                    {
                        issues.Add(new ImportIssue(IssueSeverity.Warning, info.SheetName,
                            $"Column \"{meta.Heading}\" was not found (renamed or deleted in Excel); its values are ignored."));
                    }
                }

                result.Add(meta.ToImportColumn(position));
            }

            return result;
        }

        private static ExcelValue ReadCell(IXLCell cell)
        {
            var value = cell.Value;
            return value.Type switch
            {
                XLDataType.Blank => ExcelValue.Blank,
                XLDataType.Text => new ExcelValue(ExcelValueKind.Text, value.GetText(), null),
                XLDataType.Number => new ExcelValue(ExcelValueKind.Number,
                    value.GetNumber().ToString("R", CultureInfo.InvariantCulture), value.GetNumber()),
                XLDataType.Boolean => new ExcelValue(ExcelValueKind.Boolean, value.GetBoolean() ? "TRUE" : "FALSE", null),
                _ => new ExcelValue(ExcelValueKind.Other, cell.GetFormattedString(), null)
            };
        }

        private static ImportSheet Build(
            SheetInfo info, bool found, List<ImportColumn> columns, List<ImportRow> rows, List<ImportNewRow>? newRows = null) => new()
        {
            SheetName = info.SheetName,
            ScheduleUniqueId = info.ScheduleUniqueId,
            ScheduleName = info.ScheduleName,
            Mode = info.Mode,
            Source = info.Source,
            AllowNewRows = info.AllowNewRows,
            NewRows = newRows ?? [],
            SheetFound = found,
            Columns = columns,
            Rows = rows
        };

        // ---- Metadata sheet parsing ------------------------------------------------------------

        private sealed record SheetInfo(
            string SheetName, string ScheduleUniqueId, string ScheduleName, SheetMode Mode, int HeaderRow, int FirstDataRow,
            SheetSource Source, bool AllowNewRows);

        private sealed record ColumnInfo(
            string SheetName, int ExcelColumn, int FieldId, string Heading, string FieldType, ParameterIdentity Parameter, bool IsType,
            ValueStorage Storage, string SpecTypeId, string ExportUnitTypeId, CellKind Kind, bool Editable, string? ReadOnlyReason,
            bool RequiredForNewRows)
        {
            public ImportColumn ToImportColumn(int? position) => new()
            {
                FieldId = FieldId,
                Heading = Heading,
                FieldType = FieldType,
                Parameter = Parameter,
                IsType = IsType,
                Storage = Storage,
                SpecTypeId = SpecTypeId,
                ExportUnitTypeId = ExportUnitTypeId,
                Kind = Kind,
                IsEditable = Editable,
                ReadOnlyReason = ReadOnlyReason,
                RequiredForNewRows = RequiredForNewRows,
                ExcelColumn = position
            };
        }

        private sealed record RowInfo(string SheetName, string ElementUniqueId, long ElementId, string? TypeUniqueId, bool InGroup, bool IsLinked)
        {
            public ImportRow ToImportRow(
                int? excelRow, Dictionary<int, ExcelValue> values,
                Dictionary<string, IReadOnlyDictionary<int, OriginalValue>> originals) => new()
            {
                ElementUniqueId = ElementUniqueId,
                ElementId = ElementId,
                TypeUniqueId = TypeUniqueId,
                InGroup = InGroup,
                IsLinked = IsLinked,
                ExcelRow = excelRow,
                Values = values,
                Originals = originals.TryGetValue(ElementUniqueId, out var o) ? o : new Dictionary<int, OriginalValue>()
            };
        }

        private sealed record ValueInfo(string SheetName, string ElementUniqueId, int FieldId, OriginalValue Value);

        private sealed class Metadata
        {
            private readonly Dictionary<string, string> _workbook = new(StringComparer.Ordinal);

            public int SchemaVersion { get; private set; }
            public List<SheetInfo> Sheets { get; } = [];
            public List<ColumnInfo> Columns { get; } = [];
            public List<RowInfo> Rows { get; } = [];
            public List<ValueInfo> Values { get; } = [];

            public string Get(string key) => _workbook.TryGetValue(key, out var v) ? v : string.Empty;

            public DocumentFingerprint Fingerprint() => new()
            {
                CreationGuid = NullIfEmpty(Get("Doc.CreationGuid")),
                ProjectInfoUniqueId = NullIfEmpty(Get("Doc.ProjectInfoUniqueId")),
                ProjectNumber = Get("Doc.ProjectNumber"),
                ProjectName = Get("Doc.ProjectName"),
                Title = Get("Doc.Title"),
                IsWorkshared = Get("Doc.IsWorkshared") == "true",
                CentralModelName = NullIfEmpty(Get("Doc.CentralModelName")),
                CentralGuid = NullIfEmpty(Get("Doc.CentralGuid")),
                CloudProjectGuid = NullIfEmpty(Get("Doc.CloudProjectGuid")),
                CloudModelGuid = NullIfEmpty(Get("Doc.CloudModelGuid"))
            };

            public static Metadata Parse(IXLWorksheet ws)
            {
                var sections = ReadSections(ws);
                var metadata = new Metadata();

                if (!sections.TryGetValue(WorkbookSchema.SectionWorkbook, out var workbook))
                {
                    throw new ScheduleSyncException("The workbook's metadata is incomplete (no [Workbook] section). Export a new workbook.");
                }

                foreach (var row in workbook)
                {
                    metadata._workbook[row.Get("Key")] = row.Get("Value");
                }

                if (!int.TryParse(metadata.Get("SchemaVersion"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var version) || version < 1)
                {
                    throw new ScheduleSyncException("The workbook's metadata has no valid schema version. Export a new workbook.");
                }

                if (version > WorkbookSchema.SchemaVersion)
                {
                    throw new ScheduleSyncException(
                        $"This workbook was created by a newer version of Revit-Excel Link (format {version}). Update the add-in to import it.");
                }

                metadata.SchemaVersion = version;

                foreach (var row in Section(sections, WorkbookSchema.SectionSheets))
                {
                    metadata.Sheets.Add(new SheetInfo(
                        row.Get("SheetName"), row.Get("ScheduleUniqueId"), row.Get("ScheduleName"),
                        Enum.Parse<SheetMode>(row.Get("Mode")), Int(row.Get("HeaderRow")), Int(row.Get("FirstDataRow")),
                        // Schema 1 workbooks have no Source column: they only contain schedules.
                        Enum.TryParse<SheetSource>(row.Get("Source"), out var sheetSource) ? sheetSource : SheetSource.Schedule,
                        row.Get("AllowNewRows") == "true"));
                }

                foreach (var row in Section(sections, WorkbookSchema.SectionColumns))
                {
                    var source = Enum.Parse<ParameterSource>(row.Get("ParameterSource"));
                    var identity = source == ParameterSource.None
                        ? ParameterIdentity.None
                        : new ParameterIdentity
                        {
                            Source = source,
                            Name = row.Get("ParameterName"),
                            BuiltInName = NullIfEmpty(row.Get("BuiltInName")),
                            BuiltInValue = LongOrNull(row.Get("BuiltInValue")),
                            SharedGuid = Guid.TryParse(row.Get("SharedGuid"), out var g) ? g : null,
                            ParameterElementId = LongOrNull(row.Get("ParameterElementId"))
                        };

                    metadata.Columns.Add(new ColumnInfo(
                        row.Get("SheetName"), Int(row.Get("ExcelColumn")), Int(row.Get("FieldId")), row.Get("Heading"), row.Get("FieldType"),
                        identity, row.Get("IsType") == "true", Enum.Parse<ValueStorage>(row.Get("Storage")),
                        row.Get("SpecTypeId"), row.Get("ExportUnitTypeId"), Enum.Parse<CellKind>(row.Get("CellKind")),
                        row.Get("Editable") == "true", NullIfEmpty(row.Get("ReadOnlyReason")),
                        row.Get("RequiredForNewRows") == "true"));
                }

                foreach (var row in Section(sections, WorkbookSchema.SectionRows))
                {
                    metadata.Rows.Add(new RowInfo(
                        row.Get("SheetName"), row.Get("ElementUniqueId"), long.Parse(row.Get("ElementId"), CultureInfo.InvariantCulture),
                        NullIfEmpty(row.Get("TypeUniqueId")), row.Get("InGroup") == "true", row.Get("LinkName").Length > 0));
                }

                foreach (var row in Section(sections, WorkbookSchema.SectionValues))
                {
                    metadata.Values.Add(new ValueInfo(
                        row.Get("SheetName"), row.Get("ElementUniqueId"), Int(row.Get("FieldId")),
                        OriginalFrom(row)));
                }

                return metadata;
            }

            /// <summary>Section = marker row "[Name]", header row, data rows until the first empty row.</summary>
            private static Dictionary<string, List<SectionRow>> ReadSections(IXLWorksheet ws)
            {
                var sections = new Dictionary<string, List<SectionRow>>(StringComparer.Ordinal);
                int last = ws.LastRowUsed()?.RowNumber() ?? 0;
                int lastColumn = ws.LastColumnUsed()?.ColumnNumber() ?? 0;

                for (int r = 1; r <= last; r++)
                {
                    var marker = ws.Cell(r, 1).GetString();
                    if (!marker.StartsWith('[') || !marker.EndsWith(']'))
                    {
                        continue;
                    }

                    var header = Enumerable.Range(1, lastColumn).Select(c => ws.Cell(r + 1, c).GetString()).ToList();
                    var index = header.Select((name, i) => (name, i)).Where(x => x.name.Length > 0)
                        .ToDictionary(x => x.name, x => x.i + 1, StringComparer.Ordinal);
                    var rows = new List<SectionRow>();

                    int dataRow = r + 2;
                    for (; dataRow <= last; dataRow++)
                    {
                        if (ws.Row(dataRow).IsEmpty())
                        {
                            break;
                        }

                        rows.Add(new SectionRow(ws.Row(dataRow), index));
                    }

                    sections[marker] = rows;
                    r = dataRow;
                }

                return sections;
            }

            private static List<SectionRow> Section(Dictionary<string, List<SectionRow>> sections, string name) =>
                sections.TryGetValue(name, out var rows)
                    ? rows
                    : throw new ScheduleSyncException($"The workbook's metadata is incomplete (no {name} section). Export a new workbook.");

            /// <summary>
            /// Accepts both value row forms: full ("true"/"false", all columns filled) and compact
            /// (HasValue written only when false; Raw/Display empty when equal to Normalised).
            /// </summary>
            private static OriginalValue OriginalFrom(SectionRow row)
            {
                var normalised = row.Get("Normalised");
                var raw = row.Get("Raw");
                var display = row.Get("Display");
                return new OriginalValue(
                    row.Get("HasValue") != "false",
                    raw.Length == 0 ? normalised : raw,
                    normalised,
                    display.Length == 0 ? normalised : display,
                    NullIfEmpty(row.Get("LockReason")));
            }

            private static int Int(string text) => int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);

            private static long? LongOrNull(string text) =>
                long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

            private static string? NullIfEmpty(string text) => text.Length == 0 ? null : text;
        }

        private sealed class SectionRow
        {
            private readonly IXLRow _row;
            private readonly Dictionary<string, int> _index;

            public SectionRow(IXLRow row, Dictionary<string, int> index)
            {
                _row = row;
                _index = index;
            }

            public string Get(string column) => _index.TryGetValue(column, out var c) ? _row.Cell(c).GetString() : string.Empty;
        }
    }
}
