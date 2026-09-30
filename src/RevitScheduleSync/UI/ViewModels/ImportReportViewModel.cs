using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Data;
using System.Windows.Input;
using RevitScheduleSync.Models.Import;
using RevitScheduleSync.Services;

namespace RevitScheduleSync.UI.ViewModels
{
    /// <summary>Import step 3: what Apply did, item by item.</summary>
    public sealed class ImportReportViewModel : ViewModelBase
    {
        public const string AllResults = "All results";

        private string _resultFilter = AllResults;

        public ImportReportViewModel(
            ImportReport report, string? savedPath, Action exportReport, Action openFolder, Action reanalyse, Action done,
            Action<string, Exception> reportError)
        {
            Report = report;
            SavedPath = savedPath;

            Entries = CollectionViewSource.GetDefaultView(report.Entries.Select(e => new ReportEntryViewModel(e)).ToList());
            Entries.Filter = o => o is ReportEntryViewModel e && (_resultFilter == AllResults || e.ResultText == _resultFilter);
            ResultFilters = new[] { AllResults }.Concat(report.Entries.Select(e => ReportEntryViewModel.Text(e.Result)).Distinct()).ToList();

            ExportReportCommand = new RelayCommand(exportReport, ex => reportError("The report could not be exported.", ex));
            OpenFolderCommand = new RelayCommand(openFolder, ex => reportError("The report folder could not be opened.", ex));
            ReanalyseCommand = new RelayCommand(reanalyse, ex => reportError("The workbook could not be analysed.", ex));
            DoneCommand = new RelayCommand(done, ex => reportError("Could not finish.", ex));
        }

        public ImportReport Report { get; }

        public string? SavedPath { get; }

        public ICollectionView Entries { get; }

        public IReadOnlyList<string> ResultFilters { get; }

        public string ResultFilter
        {
            get => _resultFilter;
            set
            {
                if (SetProperty(ref _resultFilter, value ?? AllResults))
                {
                    Entries.Refresh();
                }
            }
        }

        public string Title => Report.RolledBack ? "IMPORT NOT APPLIED" : "IMPORT COMPLETE";

        public string Subtitle =>
            $"{Path.GetFileName(Report.WorkbookPath)} → {Report.DocumentTitle} · {Report.Time:g} · {Report.ElapsedMilliseconds:N0} ms";

        public bool RolledBack => Report.RolledBack;

        public int Applied => Report.Count(ReportResult.Applied);
        public int Skipped => Report.Count(ReportResult.Skipped);
        public int Conflicts => Report.Count(ReportResult.Conflict);
        public int Failed => Report.Count(ReportResult.Failed);
        public int ReadOnlyIgnored => Report.Count(ReportResult.ReadOnlyIgnored);
        public int NotApplicable => Report.Count(ReportResult.NotApplicable);

        public IReadOnlyList<string> RevitWarnings => Report.RevitWarnings;

        public bool HasRevitWarnings => Report.RevitWarnings.Count > 0;

        public string SavedText => SavedPath == null
            ? "The report could not be saved to History (see log). Use Export report to keep a copy."
            : $"Saved to History: {Path.GetFileName(SavedPath)}";

        public ICommand ExportReportCommand { get; }
        public ICommand OpenFolderCommand { get; }
        public ICommand ReanalyseCommand { get; }
        public ICommand DoneCommand { get; }
    }

    public sealed class ReportEntryViewModel
    {
        public ReportEntryViewModel(ImportReportEntry entry)
        {
            Entry = entry;
        }

        public ImportReportEntry Entry { get; }

        public ReportResult Result => Entry.Result;

        public string ResultText => Text(Entry.Result);

        public static string Text(ReportResult result) => result switch
        {
            ReportResult.Applied => "Applied",
            ReportResult.Skipped => "Skipped",
            ReportResult.Conflict => "Conflict",
            ReportResult.Failed => "Failed",
            ReportResult.ReadOnlyIgnored => "Read-only ignored",
            _ => "Not applicable"
        };
    }

    /// <summary>History tab: import reports saved on this computer.</summary>
    public sealed class HistoryViewModel : SectionViewModel
    {
        private readonly ReportStore _store;
        private IReadOnlyList<ReportFileInfo> _reports = [];

        public HistoryViewModel(ReportStore store, Action<string, Exception> reportError)
            : base("History")
        {
            _store = store;
            RefreshCommand = new RelayCommand(Refresh, ex => reportError("History could not be read.", ex));
            OpenCommand = new ParameterCommand(path =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    reportError("The report could not be opened. Is an application for .csv files installed?", ex);
                }
            });
            OpenFolderCommand = new RelayCommand(() => ShellOpen.Folder(_store.Folder), ex => reportError("The folder could not be opened.", ex));
            Refresh();
        }

        public IReadOnlyList<ReportFileInfo> Reports
        {
            get => _reports;
            private set
            {
                SetProperty(ref _reports, value);
                OnPropertyChanged(nameof(IsEmpty));
            }
        }

        public bool IsEmpty => _reports.Count == 0;

        public string FolderText => $"Reports are saved on this computer in {_store.Folder}";

        public ICommand RefreshCommand { get; }
        public ICommand OpenCommand { get; }
        public ICommand OpenFolderCommand { get; }

        public void Refresh() => Reports = _store.List();
    }

    internal static class ShellOpen
    {
        public static void Folder(string folder)
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
    }
}
