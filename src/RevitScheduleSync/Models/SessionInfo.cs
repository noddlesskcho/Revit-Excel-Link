using System;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using RevitScheduleSync.App;
using RevitScheduleSync.Excel;

namespace RevitScheduleSync.Models
{
    /// <summary>
    /// Plain snapshot of document and environment facts for display.
    /// Holds no Revit objects, so the UI never touches the Revit API directly.
    /// </summary>
    public sealed class SessionInfo
    {
        public required string DocumentTitle { get; init; }
        public required string DocumentLocation { get; init; }
        public required bool IsWorkshared { get; init; }
        public required string RevitVersion { get; init; }
        public required string PluginVersion { get; init; }
        public required string ExcelLibraryVersion { get; init; }
        public required string RuntimeVersion { get; init; }
        public string? LogDirectory { get; init; }

        internal static SessionInfo Create(Application application, Document document, string? logDirectory)
        {
            return new SessionInfo
            {
                DocumentTitle = document.Title,
                DocumentLocation = string.IsNullOrEmpty(document.PathName) ? "Not saved yet" : document.PathName,
                IsWorkshared = document.IsWorkshared,
                RevitVersion = $"{application.SubVersionNumber} (build {application.VersionBuild})",
                PluginVersion = PluginInfo.Version,
                ExcelLibraryVersion = ExcelLibraryInfo.GetVersion(),
                RuntimeVersion = Environment.Version.ToString(),
                LogDirectory = logDirectory
            };
        }
    }
}
