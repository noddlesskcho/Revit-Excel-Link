using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using RevitScheduleSync.Excel;
using RevitScheduleSync.Logging;
using RevitScheduleSync.Models.Import;
using RevitScheduleSync.Services;
using Xunit;
using Xunit.Abstractions;

namespace RevitScheduleSync.Tests
{
    public class ReportStoreTests
    {
        private static ImportReport Report() => new()
        {
            Time = new DateTime(2026, 10, 1, 9, 30, 0),
            WorkbookPath = @"C:\work\doors.xlsx",
            DocumentTitle = "Tower, \"Block A\".rvt",
            Entries =
            [
                new ImportReportEntry(ReportResult.Applied, "Door Schedule", 1001, "Doors D01", "Mark", false, "D01", "D01-NEW", "Applied."),
                new ImportReportEntry(ReportResult.Skipped, "Door Schedule", 1002, "Doors D02", "Comments", false, "", "=HYPERLINK(\"x\")", "Skipped by reviewer."),
                new ImportReportEntry(ReportResult.Failed, "Door Schedule", 1003, "Doors D03", "Level", false, "L01", "L99", "Revit did not accept the value.")
            ],
            RevitWarnings = ["Elements have duplicate \"Mark\" values."],
            RolledBack = false,
            ElapsedMilliseconds = 12
        };

        [Fact]
        public void Saved_report_is_listed_in_history_with_summary()
        {
            var folder = Path.Combine(Path.GetTempPath(), "RssTests", Guid.NewGuid().ToString("N"));
            var store = new ReportStore(NullLogger.Instance, folder);

            var path = store.Save(Report());
            var listed = Assert.Single(store.List());

            Assert.Equal(path, listed.Path);
            Assert.Equal("Tower, \"Block A\".rvt", listed.Project);
            Assert.Equal("doors.xlsx", listed.Workbook);
            Assert.Contains("Applied 1", listed.Summary);
            Assert.Contains("Failed 1", listed.Summary);
        }

        [Fact]
        public void Csv_escapes_quotes_commas_and_formula_prefixes()
        {
            var path = Path.Combine(Path.GetTempPath(), "RssTests", Guid.NewGuid().ToString("N") + ".csv");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            ReportStore.Write(Report(), path);
            var text = File.ReadAllText(path);

            Assert.Contains("\"Tower, \"\"Block A\"\".rvt\"", text);
            Assert.Contains("\"'=HYPERLINK(\"\"x\"\")\"", text); // never evaluated as a formula by Excel
            Assert.DoesNotContain(",=HYPERLINK", text);
        }
    }

    /// <summary>
    /// Section 26: tens of thousands of rows. Measures the Excel layer, which dominates export/import time
    /// outside Revit's own element reading. Thresholds are generous; timings are written to the test output.
    /// </summary>
    public class PerformanceTests
    {
        private readonly ITestOutputHelper _output;

        public PerformanceTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void Twenty_thousand_rows_write_and_read_back()
        {
            const int rows = 20_000;
            var model = TestWorkbooks.Doors(rows: rows, extraColumns: 6);

            var write = Stopwatch.StartNew();
            var path = TestWorkbooks.Write(model);
            write.Stop();

            var read = Stopwatch.StartNew();
            var workbook = new ClosedXmlWorkbookReader(NullLogger.Instance).Read(path);
            read.Stop();

            _output.WriteLine($"{rows:N0} rows x {model.Sheets[0].Columns.Count} columns: " +
                              $"write {write.ElapsedMilliseconds:N0} ms, read {read.ElapsedMilliseconds:N0} ms, " +
                              $"file {new FileInfo(path).Length / 1024:N0} KB");

            Assert.Equal(rows, workbook.Sheets[0].Rows.Count(r => r.ExcelRow.HasValue));
            Assert.Equal($"value {rows}-5", workbook.Sheets[0].Rows.Last().Values[105].Text);
            Assert.True(write.Elapsed < TimeSpan.FromSeconds(60), $"Write took {write.Elapsed}");
            Assert.True(read.Elapsed < TimeSpan.FromSeconds(60), $"Read took {read.Elapsed}");
        }
    }
}
