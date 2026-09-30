using System;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Microsoft.Win32;

namespace RevitScheduleSync.UI
{
    internal static class SaveDialogs
    {
        /// <summary>Asks for the export workbook path. Returns null when the user cancels.</summary>
        public static string? ChooseExportPath(Document document)
        {
            var title = Path.GetFileNameWithoutExtension(document.Title);
            var invalid = Path.GetInvalidFileNameChars();
            var safeTitle = new string(title.Select(c => invalid.Contains(c) ? '_' : c).ToArray());

            var dialog = new SaveFileDialog
            {
                Title = "Export schedules to Excel",
                Filter = "Excel workbook (*.xlsx)|*.xlsx",
                DefaultExt = ".xlsx",
                AddExtension = true,
                OverwritePrompt = true,
                FileName = $"{safeTitle} - Schedules {DateTime.Now:yyyy-MM-dd HHmm}.xlsx",
                InitialDirectory = GetInitialDirectory(document)
            };

            // No explicit owner: the active window is the modal Schedule Sync window.
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        /// <summary>Asks for the workbook to import. Returns null when the user cancels.</summary>
        public static string? ChooseImportPath()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Import schedules from Excel",
                Filter = "Excel workbook (*.xlsx)|*.xlsx",
                CheckFileExists = true,
                Multiselect = false
            };

            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        /// <summary>Asks where to save an import report copy. Returns null when the user cancels.</summary>
        public static string? ChooseReportPath(string defaultName)
        {
            var dialog = new SaveFileDialog
            {
                Title = "Export import report",
                Filter = "CSV report (*.csv)|*.csv",
                DefaultExt = ".csv",
                AddExtension = true,
                OverwritePrompt = true,
                FileName = defaultName,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        private static string GetInitialDirectory(Document document)
        {
            try
            {
                // Local projects: next to the model. Cloud/server paths are not usable folders.
                if (!string.IsNullOrEmpty(document.PathName) && !document.IsModelInCloud)
                {
                    var folder = Path.GetDirectoryName(document.PathName);
                    if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                    {
                        return folder;
                    }
                }
            }
            catch (Exception)
            {
                // Fall through to Documents.
            }

            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }
    }
}
