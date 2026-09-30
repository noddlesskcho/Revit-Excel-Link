using System;
using System.Windows.Input;
using RevitScheduleSync.Logging;
using RevitScheduleSync.Models.Import;
using RevitScheduleSync.Services;

namespace RevitScheduleSync.UI.ViewModels
{
    /// <summary>
    /// Import section: Select Workbook › Analyse › Compare › Validate › (Apply, Report in later phases).
    /// Analysis never writes to the model.
    /// </summary>
    public sealed class ImportViewModel : SectionViewModel
    {
        private readonly IScheduleOperations _operations;
        private readonly Func<string?> _chooseImportPath;
        private readonly Action<string, Exception> _reportError;
        private readonly ILogger _logger;
        private ViewModelBase _currentStep;

        public ImportViewModel(
            IScheduleOperations operations, Func<string?> chooseImportPath, ILogger logger, Action<string, Exception> reportError)
            : base("Import")
        {
            _operations = operations;
            _chooseImportPath = chooseImportPath;
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
            var path = _chooseImportPath();
            if (string.IsNullOrEmpty(path))
            {
                _logger.Debug("Import cancelled at file selection.");
                return;
            }

            Analyse(path);
        }

        private void Analyse(string path)
        {
            var previous = Mouse.OverrideCursor;
            Mouse.OverrideCursor = Cursors.Wait;
            ChangeSet changes;
            try
            {
                changes = _operations.AnalyseImport(path);
            }
            finally
            {
                Mouse.OverrideCursor = previous;
            }

            CurrentStep = new ImportAnalysisViewModel(
                changes,
                back: () => CurrentStep = Start,
                reanalyse: () => Analyse(path),
                _reportError);
        }
    }

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
