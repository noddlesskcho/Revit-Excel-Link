using System;
using Autodesk.Revit.UI;
using RevitScheduleSync.Logging;

namespace RevitScheduleSync.App
{
    /// <summary>
    /// Revit entry point (referenced by FullClassName in RevitScheduleSync.addin).
    /// </summary>
    public sealed class ScheduleSyncApplication : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            var logger = new FileLogger(FileLogger.DefaultLogDirectory);
            AddinHost.Initialize(logger);

            try
            {
                var revit = application.ControlledApplication;
                logger.Info(
                    $"{PluginInfo.ProductName} {PluginInfo.Version} starting. " +
                    $"Revit {revit.VersionNumber} ({revit.SubVersionNumber}, build {revit.VersionBuild}), " +
                    $".NET {Environment.Version}.");

                logger.DeleteOldLogs();
                RibbonBuilder.Build(application, logger);

                logger.Info("Startup complete.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                // Failing here only disables this add-in; Revit itself keeps running.
                logger.Error("Startup failed.", ex);
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            AddinHost.Logger.Info($"{PluginInfo.ProductName} shutting down.");
            AddinHost.Shutdown();
            return Result.Succeeded;
        }
    }
}
