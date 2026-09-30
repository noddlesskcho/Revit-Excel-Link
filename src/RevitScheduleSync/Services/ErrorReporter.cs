using System;
using Autodesk.Revit.UI;
using RevitScheduleSync.App;
using RevitScheduleSync.Logging;

namespace RevitScheduleSync.Services
{
    /// <summary>
    /// Shows human-readable errors. Technical detail (stack trace) goes to the log only.
    /// Uses TaskDialog, so it must be called on the Revit main thread (command or modal window).
    /// </summary>
    internal static class ErrorReporter
    {
        public static void Show(ILogger logger, string summary, Exception exception)
        {
            try
            {
                var detail = exception is ScheduleSyncException known
                    ? known.Message
                    : "An unexpected error occurred. Revit and your model are not affected.";

                var dialog = new TaskDialog(PluginInfo.ProductName)
                {
                    MainIcon = TaskDialogIcon.TaskDialogIconWarning,
                    MainInstruction = summary,
                    MainContent = detail,
                    ExpandedContent = $"{exception.GetType().Name}: {exception.Message}",
                    FooterText = $"Log folder: {AddinHost.FileLogger?.LogDirectory ?? "(not available)"}"
                };
                dialog.Show();
            }
            catch (Exception dialogError)
            {
                logger.Error("Could not show error dialog.", dialogError);
            }
        }
    }
}
