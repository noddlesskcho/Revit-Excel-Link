using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using RevitScheduleSync.Logging;
using RevitScheduleSync.Models;
using RevitScheduleSync.Services;

namespace RevitScheduleSync.UI.ViewModels
{
    /// <summary>
    /// Export section: Select Schedules › Analyse › Export.
    /// Holds the current step; each step has its own view model and view.
    /// </summary>
    public sealed class ExportViewModel : SectionViewModel
    {
        private readonly IScheduleOperations _operations;
        private readonly Func<string?> _chooseSavePath;
        private readonly Action<string, Exception> _reportError;
        private readonly ILogger _logger;
        private ViewModelBase _currentStep;
        private ScheduleAnalysisViewModel? _analysis;

        /// <param name="operations">Revit-side work; called on the UI thread inside the modal command (API context).</param>
        /// <param name="chooseSavePath">Asks the user for the target .xlsx path; null when cancelled.</param>
        public ExportViewModel(
            ScheduleScanResult scan,
            IScheduleOperations operations,
            Func<string?> chooseSavePath,
            ILogger logger,
            Action<string, Exception> reportError)
            : base("Schedules")
        {
            _operations = operations;
            _chooseSavePath = chooseSavePath;
            _logger = logger;
            _reportError = reportError;
            Selection = new ScheduleSelectionViewModel(scan, logger, reportError, RunAnalysis);
            _currentStep = Selection;
        }

        public ScheduleSelectionViewModel Selection { get; }

        public ViewModelBase CurrentStep
        {
            get => _currentStep;
            private set => SetProperty(ref _currentStep, value);
        }

        private void RunAnalysis()
        {
            var selected = Selection.SelectedSchedules;
            _logger.Info($"Analyse requested for {selected.Count} schedule(s).");

            var results = WithWaitCursor(() => _operations.Analyse(selected));
            _analysis = new ScheduleAnalysisViewModel(results, () => CurrentStep = Selection, RunExport, _reportError);
            CurrentStep = _analysis;
        }

        private void RunExport(ScheduleAnalysisViewModel analysis)
        {
            var path = _chooseSavePath();
            if (string.IsNullOrEmpty(path))
            {
                _logger.Debug("Export cancelled at file selection.");
                return;
            }

            var options = new ExportOptions
            {
                FilePath = path,
                ProtectReadOnlyCells = analysis.ProtectReadOnlyCells,
                ShowMetadataSheet = analysis.ShowMetadataSheet
            };

            var analyses = analysis.Schedules.Select(s => s.Analysis).Where(a => a.Error == null).ToList();
            var result = WithWaitCursor(() => _operations.Export(analyses, options));

            CurrentStep = new ExportResultViewModel(
                result,
                back: () => CurrentStep = _analysis!,
                startOver: () => CurrentStep = Selection,
                _reportError);
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
}
