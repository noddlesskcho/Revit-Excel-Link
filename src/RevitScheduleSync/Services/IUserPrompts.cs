using System;
using Autodesk.Revit.UI;
using RevitScheduleSync.App;

namespace RevitScheduleSync.Services
{
    /// <summary>Yes/no confirmations requested by view models (kept out of them so they stay testable).</summary>
    public interface IUserPrompts
    {
        /// <returns>True when the user confirms.</returns>
        bool Confirm(string instruction, string content, string confirmText, string cancelText);
    }

    /// <summary>Revit TaskDialog confirmations. Must be called on the Revit main thread.</summary>
    internal sealed class TaskDialogPrompts : IUserPrompts
    {
        public bool Confirm(string instruction, string content, string confirmText, string cancelText)
        {
            var dialog = new TaskDialog(PluginInfo.ProductName)
            {
                MainIcon = TaskDialogIcon.TaskDialogIconWarning,
                MainInstruction = instruction,
                MainContent = content,
                AllowCancellation = true
            };
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, confirmText);
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, cancelText);

            // Must be set after the command links exist: Revit throws "Corresponding button not found" otherwise.
            // Default = cancel, so pressing Enter never writes to the model.
            dialog.DefaultButton = TaskDialogResult.CommandLink2;

            return dialog.Show() == TaskDialogResult.CommandLink1;
        }
    }
}
