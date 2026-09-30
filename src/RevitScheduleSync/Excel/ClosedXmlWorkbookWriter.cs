using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using RevitScheduleSync.Logging;
using RevitScheduleSync.Models.Export;
using RevitScheduleSync.Services;

namespace RevitScheduleSync.Excel
{
    /// <summary>
    /// Writes the workbook with ClosedXML. Excel does not need to be installed.
    /// Saves to a temporary file first and then replaces the target, so a failed export never
    /// leaves a half-written workbook behind.
    /// </summary>
    public sealed class ClosedXmlWorkbookWriter : IWorkbookWriter
    {
        private readonly ILogger _logger;

        public ClosedXmlWorkbookWriter(ILogger logger)
        {
            _logger = logger;
        }

        public void Write(ExportWorkbook workbook, ExportOptions options)
        {
            var target = ValidateTarget(options.FilePath);
            EnsureMetadataFits(workbook);

            // Fail fast, before building the workbook, when Excel (or anything else) holds the file open.
            if (IsLocked(target))
            {
                throw new ScheduleSyncException(FileInUseMessage(target));
            }

            var names = new SheetNameAllocator();
            var sheetNames = workbook.Sheets.Select(s => names.Allocate(s.ScheduleName)).ToList();

            using var book = new XLWorkbook();
            book.Properties.Title = $"Schedule Sync for Revit export — {workbook.Info.Fingerprint.Title}";
            book.Properties.Comments = "Edit only the white and blue cells. Do not rename sheets or remove the hidden column A.";

            for (int i = 0; i < workbook.Sheets.Count; i++)
            {
                var sheet = workbook.Sheets[i];
                var worksheet = book.Worksheets.Add(sheetNames[i]);
                if (sheet.Mode == SheetMode.Editable)
                {
                    ScheduleSheetWriter.WriteEditable(worksheet, sheet, options.ProtectReadOnlyCells, _logger);
                }
                else
                {
                    ScheduleSheetWriter.WritePresentation(worksheet, sheet, options.ProtectReadOnlyCells);
                }
            }

            var metadata = book.Worksheets.Add(WorkbookSchema.MetadataSheetName);
            MetadataSheetWriter.Write(metadata, workbook, sheetNames);
            metadata.Visibility = options.ShowMetadataSheet ? XLWorksheetVisibility.Visible : XLWorksheetVisibility.VeryHidden;

            if (book.Worksheets.Count > 1)
            {
                book.Worksheet(1).SetTabActive();
            }

            Save(book, target);
        }

        private void Save(XLWorkbook book, string target)
        {
            var folder = Path.GetDirectoryName(target)!;
            var temp = Path.Combine(folder, $".{Path.GetFileNameWithoutExtension(target)}.{Guid.NewGuid():N}.tmp.xlsx");

            try
            {
                book.SaveAs(temp);
                File.Move(temp, target, overwrite: true);
                _logger.Info($"Workbook saved: '{Path.GetFileName(target)}' ({new FileInfo(target).Length / 1024} KB).");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                TryDelete(temp);
                _logger.Warning("Workbook could not be saved.", ex);

                // Replacing a file that another program holds open fails with either exception type.
                if (IsLocked(target))
                {
                    throw new ScheduleSyncException(FileInUseMessage(target), ex);
                }

                throw new ScheduleSyncException(ex is UnauthorizedAccessException
                    ? $"You do not have permission to write to \"{folder}\". Choose another folder."
                    : $"The file \"{Path.GetFileName(target)}\" could not be written: {ex.Message}", ex);
            }
        }

        private static string ValidateTarget(string path)
        {
            string full;
            try
            {
                full = Path.GetFullPath(path);
            }
            catch (Exception ex)
            {
                throw new ScheduleSyncException("The chosen file path is not valid.", ex);
            }

            if (!string.Equals(Path.GetExtension(full), ".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                throw new ScheduleSyncException("The workbook must be saved as an .xlsx file.");
            }

            if (!Directory.Exists(Path.GetDirectoryName(full)))
            {
                throw new ScheduleSyncException("The chosen folder does not exist.");
            }

            return full;
        }

        private static void EnsureMetadataFits(ExportWorkbook workbook)
        {
            long rows = 20
                        + workbook.Sheets.Count
                        + workbook.Sheets.Sum(s => (long)s.Columns.Count + s.Rows.Count
                                                   + (long)s.Rows.Count * s.Columns.Count(c => c.IsEditable));
            if (rows > WorkbookSchema.MaxMetadataRows)
            {
                throw new ScheduleSyncException(
                    "The selected schedules are too large for one workbook. Export fewer schedules at a time.");
            }
        }

        private static string FileInUseMessage(string target) =>
            $"The file \"{Path.GetFileName(target)}\" is open in another program (probably Excel). " +
            "Close it and export again, or choose a different file name.";

        /// <summary>True when the file exists and cannot be opened exclusively.</summary>
        private static bool IsLocked(string path)
        {
            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                return false;
            }
            catch (IOException)
            {
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                // Read-only file or no permission: not "in use"; the save path reports it.
                return false;
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Best effort only.
            }
        }

        /// <summary>Excel sheet names: max 31 chars, no []:*?/\, unique (case-insensitive), not reserved.</summary>
        private sealed class SheetNameAllocator
        {
            private readonly HashSet<string> _used = new(StringComparer.OrdinalIgnoreCase)
            {
                WorkbookSchema.MetadataSheetName,
                "History"
            };

            public string Allocate(string scheduleName)
            {
                var cleaned = new string(scheduleName.Select(ch => "[]:*?/\\".Contains(ch) ? '-' : ch).ToArray())
                    .Trim().Trim('\'');
                if (cleaned.Length == 0)
                {
                    cleaned = "Schedule";
                }

                var name = Truncate(cleaned, 31);
                for (int n = 2; _used.Contains(name); n++)
                {
                    var suffix = $" ({n})";
                    name = Truncate(cleaned, 31 - suffix.Length) + suffix;
                }

                _used.Add(name);
                return name;
            }

            private static string Truncate(string text, int length) => text.Length <= length ? text : text[..length].TrimEnd();
        }
    }
}
