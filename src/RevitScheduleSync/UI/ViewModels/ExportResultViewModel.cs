using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using RevitScheduleSync.Services;

namespace RevitScheduleSync.UI.ViewModels
{
    /// <summary>Export step 3: what was written, and where.</summary>
    public sealed class ExportResultViewModel : ViewModelBase
    {
        public ExportResultViewModel(ExportResult result, Action back, Action startOver, Action<string, Exception> reportError)
        {
            Result = result;
            OpenFileCommand = new RelayCommand(
                () => Process.Start(new ProcessStartInfo(result.FilePath) { UseShellExecute = true }),
                ex => reportError("The workbook could not be opened. Is an application for .xlsx files installed?", ex));
            OpenFolderCommand = new RelayCommand(
                () => Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{result.FilePath}\"") { UseShellExecute = true }),
                ex => reportError("The folder could not be opened.", ex));
            BackCommand = new RelayCommand(back, ex => reportError("Could not go back.", ex));
            StartOverCommand = new RelayCommand(startOver, ex => reportError("Could not start a new export.", ex));
        }

        public ExportResult Result { get; }

        public string FileName => Path.GetFileName(Result.FilePath);

        public string FolderPath => Path.GetDirectoryName(Result.FilePath) ?? string.Empty;

        public string SummaryText =>
            $"{Result.EditableSheetCount} editable sheet(s) · {Result.PresentationSheetCount} presentation sheet(s) · " +
            $"{Result.RowCount:N0} row(s) · {Result.EditableColumnCount} editable column(s) · {Result.ElapsedMilliseconds:N0} ms";

        public IReadOnlyList<string> Notes => Result.Notes;

        public bool HasNotes => Result.Notes.Count > 0;

        public ICommand OpenFileCommand { get; }

        public ICommand OpenFolderCommand { get; }

        public ICommand BackCommand { get; }

        public ICommand StartOverCommand { get; }
    }
}
