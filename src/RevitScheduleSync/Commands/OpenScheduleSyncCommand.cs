using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitScheduleSync.App;
using RevitScheduleSync.Excel;
using RevitScheduleSync.Models;
using RevitScheduleSync.Revit;
using RevitScheduleSync.Revit.Schedules;
using RevitScheduleSync.Services;
using RevitScheduleSync.UI;
using RevitScheduleSync.UI.ViewModels;
using RevitScheduleSync.UI.Views;

namespace RevitScheduleSync.Commands
{
    /// <summary>
    /// Opens the Schedule Sync window.
    /// Manual transaction mode: the command itself never opens a transaction; later phases
    /// open one explicitly, only in the Apply step, after the user has reviewed changes.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class OpenScheduleSyncCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var logger = AddinHost.Logger;
            var uiApplication = commandData.Application;

            return CommandGuard.Run(nameof(OpenScheduleSyncCommand), logger, () =>
            {
                var document = uiApplication.ActiveUIDocument?.Document;
                if (document == null)
                {
                    TaskDialog.Show(PluginInfo.ProductName, "Open a Revit project before using Schedule Sync.");
                    return Result.Cancelled;
                }

                var session = SessionInfo.Create(uiApplication.Application, document, AddinHost.FileLogger?.LogDirectory);

                // Read schedules up front, in API context. The window is modal, so the model
                // cannot change while the user browses this snapshot.
                var scheduleScan = new ScheduleCatalog(document, logger).Scan();

                var operations = new RevitScheduleOperations(
                    uiApplication.Application, document, new ClosedXmlWorkbookWriter(logger), new ClosedXmlWorkbookReader(logger), logger);
                var viewModel = new MainViewModel(
                    session, scheduleScan, operations, () => SaveDialogs.ChooseExportPath(document), SaveDialogs.ChooseImportPath, new TaskDialogPrompts(), logger);
                var window = new ScheduleSyncWindow(viewModel);

                // Parent the window to Revit so it stays on top of Revit and centres on it.
                _ = new WindowInteropHelper(window) { Owner = uiApplication.MainWindowHandle };

                // Modal on purpose: while ShowDialog runs we are still inside IExternalCommand.Execute,
                // so later phases can call the Revit API from the window without ExternalEvent plumbing.
                window.ShowDialog();
                return Result.Succeeded;
            });
        }
    }
}
