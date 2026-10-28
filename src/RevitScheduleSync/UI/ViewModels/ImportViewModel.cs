using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Input;
using RevitScheduleSync.Logging;
using RevitScheduleSync.Models.Import;
using RevitScheduleSync.Services;

namespace RevitScheduleSync.UI.ViewModels
{
    /// <summary>
    /// Import section: Select Workbook › Analyse/Compare/Validate › Review › Apply › Report.
    /// Only Apply writes to the model, after an explicit confirmation.
    /// </summary>
    public sealed class ImportViewModel : SectionViewModel
    {
        private readonly IScheduleOperations _operations;
        private readonly ImportDialogs _dialogs;
        private readonly IUserPrompts _prompts;
        private readonly ReportStore _reports;
        private readonly Action _historyChanged;
        private readonly Action<string, Exception> _reportError;
        private readonly ILogger _logger;
        private ViewModelBase _currentStep;

        public ImportViewModel(
            IScheduleOperations operations, ImportDialogs dialogs, IUserPrompts prompts, ReportStore reports,
            Action historyChanged, ILogger logger, Action<string, Exception> reportError)
            : base("Import")
        {
            _operations = operations;
            _dialogs = dialogs;
            _prompts = prompts;
            _reports = reports;
            _historyChanged = historyChanged;
            _logger = logger;
            _reportError = reportError;
            Start = new ImportStartViewModel(ChooseAndAnalyse, reportError);
            _currentStep = Start;
        }

        public ImportStartViewModel Start { get; }

        public ViewModelBase CurrentStep
        {
            get => _currentStep;
            private set => SetProperty(ref _currentStep, value);
        }

        private void ChooseAndAnalyse()
        {
            var path = _dialogs.ChooseWorkbook();
            if (string.IsNullOrEmpty(path))
            {
                _logger.Debug("Import cancelled at file selection.");
                return;
            }

            Analyse(path);
        }

        private void Analyse(string path)
        {
            var changes = WithWaitCursor(() => _operations.AnalyseImport(path));
            CurrentStep = new ChangeReviewViewModel(
                changes,
                _prompts,
                back: () => CurrentStep = Start,
                reanalyse: () => Analyse(path),
                apply: Apply,
                _reportError);
        }

        private void Apply(ChangeReviewViewModel review)
        {
            var changes = review.Changes;
            var toWrite = changes.Items.Where(i => i.WillWrite).ToList();
            if (toWrite.Count == 0)
            {
                return;
            }

            if (!_prompts.Confirm($"Apply {toWrite.Count} change(s) to the model?", BuildConfirmation(changes), "Apply changes", "Cancel"))
            {
                _logger.Info("Apply cancelled by user at confirmation.");
                return;
            }

            var report = WithWaitCursor(() => _operations.Apply(changes));
            var saved = _reports.Save(report);
            _historyChanged();

            CurrentStep = new ImportReportViewModel(
                report,
                saved,
                exportReport: () => ExportReport(report),
                openFolder: () => ShellOpen.Folder(_reports.Folder),
                reanalyse: () => Analyse(changes.Workbook.FilePath),
                done: () => CurrentStep = Start,
                _reportError);
        }

        private void ExportReport(ImportReport report)
        {
            var path = _dialogs.ChooseReportPath(
                $"Revit-Excel Link report {report.Time:yyyy-MM-dd HHmm} - {Path.GetFileNameWithoutExtension(report.DocumentTitle)}.csv");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {
                ReportStore.Write(report, path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new ScheduleSyncException($"The report could not be saved to \"{Path.GetFileName(path)}\". Is it open in Excel?", ex);
            }
        }

        private static string BuildConfirmation(ChangeSet changes)
        {
            var write = changes.Items.Where(i => i.WillWrite).ToList();
            var types = write.Where(i => i.IsType).ToList();
            int pending = changes.Items.Count(i => i.Decision == ReviewDecision.Pending);

            var text = new StringBuilder()
                .AppendLine($"Instance values: {write.Count(i => !i.IsType && i.Status != ChangeStatus.Create)}")
                .AppendLine($"New rooms / spaces: {write.Count(i => i.Status == ChangeStatus.Create)}")
                .AppendLine($"Type values: {types.Count} (affecting {types.Sum(i => i.AffectedInstances ?? 0):N0} instances)")
                .AppendLine($"Conflicts resolved with the Excel value: {write.Count(i => i.Status == ChangeStatus.Conflict)}");
            if (pending > 0)
            {
                text.AppendLine().AppendLine($"{pending} item(s) still need a decision and will NOT be applied.");
            }

            text.AppendLine().Append("All changes are made in one step: Undo (Ctrl+Z) reverses the whole import.");
            return text.ToString();
        }

        private static T WithWaitCursor<T>(Func<T> work)
        {
            var previous = Mouse.OverrideCursor;
            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                return work();
            }
            finally
            {
                Mouse.OverrideCursor = previous;
            }
        }
    }

    /// <summary>File dialogs used by the import workflow (injected so view models stay testable).</summary>
    public sealed record ImportDialogs(Func<string?> ChooseWorkbook, Func<string, string?> ChooseReportPath);

    /// <summary>Import step 1: choose the workbook.</summary>
    public sealed class ImportStartViewModel : ViewModelBase
    {
        public ImportStartViewModel(Action chooseWorkbook, Action<string, Exception> reportError)
        {
            ChooseWorkbookCommand = new RelayCommand(chooseWorkbook, ex => reportError("The workbook could not be analysed.", ex));
        }

        public ICommand ChooseWorkbookCommand { get; }
    }
}
