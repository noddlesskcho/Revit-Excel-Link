using System;
using System.Collections.Generic;
using System.Windows.Input;
using RevitScheduleSync.Logging;
using RevitScheduleSync.Models;

namespace RevitScheduleSync.UI.ViewModels
{
    /// <summary>
    /// Export section: Select Schedules › Analyse › Export.
    /// Holds the current step; each step has its own view model and view.
    /// </summary>
    public sealed class ExportViewModel : SectionViewModel
    {
        private readonly Func<IReadOnlyList<ScheduleSummary>, IReadOnlyList<ScheduleAnalysis>> _analyse;
        private readonly Action<string, Exception> _reportError;
        private readonly ILogger _logger;
        private ViewModelBase _currentStep;

        /// <param name="analyse">
        /// Runs the Revit-side analysis. Supplied by the command so this view model stays free of
        /// Revit API types; it is called on the UI thread inside the modal command, i.e. in API context.
        /// </param>
        public ExportViewModel(
            ScheduleScanResult scan,
            Func<IReadOnlyList<ScheduleSummary>, IReadOnlyList<ScheduleAnalysis>> analyse,
            ILogger logger,
            Action<string, Exception> reportError)
            : base("Export")
        {
            _analyse = analyse;
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

            IReadOnlyList<ScheduleAnalysis> results;
            var previousCursor = Mouse.OverrideCursor;
            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                results = _analyse(selected);
            }
            finally
            {
                Mouse.OverrideCursor = previousCursor;
            }

            CurrentStep = new ScheduleAnalysisViewModel(results, BackToSelection, _reportError);
        }

        private void BackToSelection() => CurrentStep = Selection;
    }
}
