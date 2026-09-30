using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using RevitScheduleSync.Logging;
using RevitScheduleSync.Models;
using RevitScheduleSync.Services;

namespace RevitScheduleSync.UI.ViewModels
{
    public sealed class MainViewModel : ViewModelBase
    {
        private readonly ILogger _logger;
        private SectionViewModel _selectedSection;

        public MainViewModel(
            SessionInfo session,
            ScheduleScanResult scheduleScan,
            IScheduleOperations operations,
            Func<string?> chooseSavePath,
            Func<string?> chooseImportPath,
            ILogger logger)
        {
            Session = session;
            _logger = logger;

            Export = new ExportViewModel(scheduleScan, operations, chooseSavePath, logger, ReportError);
            Sections =
            [
                Export,
                new ImportViewModel(operations, chooseImportPath, logger, ReportError),
                WorkflowSectionViewModel.CreateHistory()
            ];
            _selectedSection = Export;

            OpenLogFolderCommand = new RelayCommand(
                OpenLogFolder,
                ex => ReportError("Could not open the log folder.", ex),
                () => !string.IsNullOrEmpty(Session.LogDirectory));
            CloseCommand = new RelayCommand(
                () => RequestClose?.Invoke(this, EventArgs.Empty),
                ex => ReportError("Could not close the window.", ex));
        }

        /// <summary>Raised when the view model wants its window closed.</summary>
        public event EventHandler? RequestClose;

        public SessionInfo Session { get; }

        public ExportViewModel Export { get; }

        public IReadOnlyList<SectionViewModel> Sections { get; }

        public SectionViewModel SelectedSection
        {
            get => _selectedSection;
            set
            {
                if (value != null && SetProperty(ref _selectedSection, value))
                {
                    _logger.Debug($"Section selected: {value.Title}");
                }
            }
        }

        public string WorksharingText => Session.IsWorkshared ? "Workshared" : "Not workshared";

        public ICommand OpenLogFolderCommand { get; }

        public ICommand CloseCommand { get; }

        private void OpenLogFolder()
        {
            var folder = Session.LogDirectory!;
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }

        private void ReportError(string summary, Exception ex)
        {
            _logger.Error(summary, ex);
            ErrorReporter.Show(_logger, summary, ex);
        }
    }
}
