using System;
using System.Diagnostics;
using Autodesk.Revit.UI;
using RevitScheduleSync.App;
using RevitScheduleSync.Logging;
using RevitScheduleSync.Services;

namespace RevitScheduleSync.Commands
{
    /// <summary>Tells the user once per new release that an update is available.</summary>
    internal static class UpdateNotice
    {
        public static void ShowOnce()
        {
            var update = UpdateChecker.Available;
            if (update == null || !UpdateChecker.ShouldNotify(update))
            {
                return;
            }

            var dialog = new TaskDialog(PluginInfo.ProductName)
            {
                MainInstruction = $"Version {update.Version} is available",
                MainContent = $"You have {PluginInfo.NumericVersion}. Close Revit before installing the new version." +
                              Environment.NewLine + "You can also check later from the About button.",
                CommonButtons = TaskDialogCommonButtons.Close
            };
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Open the download page");

            if (dialog.Show() == TaskDialogResult.CommandLink1)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(update.Url) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    AddinHost.Logger.Warning("Could not open the download page.", ex);
                }
            }
        }
    }
}
