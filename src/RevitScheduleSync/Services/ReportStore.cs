using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using RevitScheduleSync.Logging;
using RevitScheduleSync.Models.Import;

namespace RevitScheduleSync.Services
{
    /// <summary>Summary of a saved report, for the History list.</summary>
    public sealed record ReportFileInfo(string Path, DateTime Time, string Project, string Workbook, string Summary);

    /// <summary>
    /// Saves import reports as CSV (UTF-8, opens in Excel) under
    /// %LOCALAPPDATA%\RevitScheduleSync\Reports and lists them for the History tab.
    /// Reports contain model values (marks, comments ...) and stay on this computer.
    /// </summary>
    public sealed class ReportStore
    {
        private const string SummaryPrefix = "# Summary";
        private readonly ILogger _logger;

        public ReportStore(ILogger logger, string? folder = null)
        {
            _logger = logger;
            Folder = folder ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RevitScheduleSync", "Reports");
        }

        public string Folder { get; }

        public static string SummaryText(ImportReport report) =>
            $"Applied {report.Count(ReportResult.Applied)}, Skipped {report.Count(ReportResult.Skipped)}, " +
            $"Conflicts {report.Count(ReportResult.Conflict)}, Failed {report.Count(ReportResult.Failed)}, " +
            $"Read-only ignored {report.Count(ReportResult.ReadOnlyIgnored)}, Not applicable {report.Count(ReportResult.NotApplicable)}";

        /// <summary>Saves the report to the History folder. Returns the path, or null if saving failed (logged).</summary>
        public string? Save(ImportReport report)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                var name = $"{report.Time:yyyyMMdd-HHmmss} {Sanitize(Path.GetFileNameWithoutExtension(report.DocumentTitle))}.csv";
                var path = Path.Combine(Folder, name);
                Write(report, path);
                return path;
            }
            catch (Exception ex)
            {
                _logger.Warning("Import report could not be saved to history.", ex);
                return null;
            }
        }

        /// <summary>Writes the report as CSV to a path the user chose.</summary>
        public static void Write(ImportReport report, string path)
        {
            var csv = new StringBuilder();
            csv.AppendLine("# Schedule Sync for Revit - import report");
            csv.AppendLine(Row("# Time", report.Time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)));
            csv.AppendLine(Row("# Project", report.DocumentTitle));
            csv.AppendLine(Row("# Workbook", report.WorkbookPath));
            csv.AppendLine(Row(SummaryPrefix, SummaryText(report) + (report.RolledBack ? " (rolled back: nothing changed)" : string.Empty)));
            foreach (var warning in report.RevitWarnings)
            {
                csv.AppendLine(Row("# Revit warning", warning));
            }

            csv.AppendLine();
            csv.AppendLine(Row("Result", "Schedule", "Element ID", "Element", "Parameter", "Parameter Type", "Previous value", "New value", "Message"));
            foreach (var e in report.Entries)
            {
                csv.AppendLine(Row(e.Result.ToString(), e.Schedule, e.ElementId.ToString(CultureInfo.InvariantCulture), e.Element,
                    e.Parameter, e.IsType ? "Type" : "Instance", e.PreviousValue, e.NewValue, e.Message));
            }

            File.WriteAllText(path, csv.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }

        public IReadOnlyList<ReportFileInfo> List()
        {
            if (!Directory.Exists(Folder))
            {
                return [];
            }

            var result = new List<ReportFileInfo>();
            foreach (var file in Directory.EnumerateFiles(Folder, "*.csv").OrderByDescending(f => f))
            {
                try
                {
                    var header = File.ReadLines(file).Take(6).ToList();
                    string Value(string key) => header.FirstOrDefault(l => l.StartsWith(key, StringComparison.Ordinal)) is { } line
                        ? line[(key.Length + 1)..].Trim('"').Replace("\"\"", "\"")
                        : string.Empty;

                    result.Add(new ReportFileInfo(file, File.GetLastWriteTime(file), Value("# Project"),
                        Path.GetFileName(Value("# Workbook")), Value(SummaryPrefix)));
                }
                catch (Exception ex)
                {
                    _logger.Debug($"Report '{Path.GetFileName(file)}' skipped in history: {ex.GetType().Name}");
                }
            }

            return result;
        }

        private static string Row(params string[] values) => string.Join(",", values.Select(Escape));

        private static string Escape(string value)
        {
            // Leading =, +, -, @ would make Excel treat the value as a formula.
            if (value.Length > 0 && "=+-@".Contains(value[0]))
            {
                value = "'" + value;
            }

            return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 || value.StartsWith(' ')
                ? "\"" + value.Replace("\"", "\"\"") + "\""
                : value;
        }

        private static string Sanitize(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
            return cleaned.Length == 0 ? "project" : cleaned[..Math.Min(cleaned.Length, 60)];
        }
    }
}
