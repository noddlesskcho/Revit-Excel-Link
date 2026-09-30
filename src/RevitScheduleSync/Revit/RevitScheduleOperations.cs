using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using RevitScheduleSync.App;
using RevitScheduleSync.Excel;
using RevitScheduleSync.Logging;
using RevitScheduleSync.Models;
using RevitScheduleSync.Models.Export;
using RevitScheduleSync.Models.Import;
using RevitScheduleSync.Revit.Documents;
using RevitScheduleSync.Revit.Import;
using RevitScheduleSync.Revit.Schedules;
using RevitScheduleSync.Services;

namespace RevitScheduleSync.Revit
{
    /// <summary>IScheduleOperations for the active Revit document. Read-only: no transactions.</summary>
    internal sealed class RevitScheduleOperations : IScheduleOperations
    {
        private readonly Application _application;
        private readonly Document _document;
        private readonly IWorkbookWriter _writer;
        private readonly IWorkbookReader _reader;
        private readonly ILogger _logger;
        private readonly ScheduleAnalyser _analyser;

        public RevitScheduleOperations(
            Application application, Document document, IWorkbookWriter writer, IWorkbookReader reader, ILogger logger)
        {
            _application = application;
            _document = document;
            _writer = writer;
            _reader = reader;
            _logger = logger;
            _analyser = new ScheduleAnalyser(document, logger);
        }

        public ChangeSet AnalyseImport(string workbookPath)
        {
            _logger.Info($"Import analysis started: '{System.IO.Path.GetFileName(workbookPath)}'.");

            // 1. Read the workbook (no Revit API).
            var workbook = _reader.Read(workbookPath);

            // 2. Compare with the model (read-only Revit API, no transaction).
            return new ImportAnalyser(_document, _logger).Analyse(workbook);
        }

        public IReadOnlyList<ScheduleAnalysis> Analyse(IReadOnlyList<ScheduleSummary> schedules) =>
            _analyser.AnalyseAll(schedules);

        public ExportResult Export(IReadOnlyList<ScheduleAnalysis> analyses, ExportOptions options)
        {
            var stopwatch = Stopwatch.StartNew();
            _logger.Info($"Export started: {analyses.Count} schedule(s).");

            // 1. Read everything from Revit (API context) into plain data.
            var builder = new ScheduleExportBuilder(_document, _logger);
            var sheets = analyses.Where(a => a.Error == null).Select(builder.Build).ToList();

            var workbook = new ExportWorkbook
            {
                Info = new WorkbookInfo
                {
                    SchemaVersion = WorkbookSchema.SchemaVersion,
                    PluginVersion = PluginInfo.Version,
                    RevitVersion = $"{_application.VersionNumber} ({_application.SubVersionNumber}, build {_application.VersionBuild})",
                    ExportedUtc = DateTime.UtcNow,
                    Fingerprint = DocumentFingerprintReader.Read(_document, _logger)
                },
                Sheets = sheets
            };

            // 2. Write the workbook (no Revit API calls from here on).
            _writer.Write(workbook, options);

            stopwatch.Stop();
            var result = new ExportResult
            {
                FilePath = options.FilePath,
                EditableSheetCount = sheets.Count(s => s.Mode == SheetMode.Editable),
                PresentationSheetCount = sheets.Count(s => s.Mode == SheetMode.Presentation),
                RowCount = sheets.Sum(s => s.Mode == SheetMode.Editable ? s.Rows.Count : s.PresentationRows.Count),
                EditableColumnCount = sheets.Sum(s => s.Columns.Count(c => c.IsEditable)),
                ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
                Notes = sheets.SelectMany(s => s.Notes).ToList()
            };

            // File name only in the log: the full path may contain user or project names.
            _logger.Info($"Export finished: {sheets.Count} sheet(s), {result.RowCount} rows, " +
                         $"{stopwatch.ElapsedMilliseconds} ms, file '{System.IO.Path.GetFileName(options.FilePath)}'.");
            return result;
        }
    }
}
