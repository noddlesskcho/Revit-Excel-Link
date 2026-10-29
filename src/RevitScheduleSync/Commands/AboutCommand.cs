using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitScheduleSync.App;
using RevitScheduleSync.UI.Views;

namespace RevitScheduleSync.Commands
{
    /// <summary>Shows the About window (version, support contact, updates). Works without an open document.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class AboutCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiApplication = commandData.Application;
            return CommandGuard.Run(nameof(AboutCommand), AddinHost.Logger, () =>
            {
                var revit = uiApplication.Application;
                var window = new AboutWindow($"Revit {revit.VersionNumber} ({revit.SubVersionNumber}, build {revit.VersionBuild})");
                _ = new WindowInteropHelper(window) { Owner = uiApplication.MainWindowHandle };
                window.ShowDialog();
                return Result.Succeeded;
            });
        }
    }

    /// <summary>Keeps a button enabled at all times, including when no document is open.</summary>
    public sealed class AlwaysAvailable : IExternalCommandAvailability
    {
        public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories) => true;
    }
}
