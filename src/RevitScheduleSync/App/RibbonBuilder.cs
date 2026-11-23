using System;
using Autodesk.Revit.UI;
using RevitScheduleSync.Commands;
using RevitScheduleSync.Logging;

namespace RevitScheduleSync.App
{
    internal static class RibbonBuilder
    {
        /// <summary>
        /// Adds the "ScheduleDock" panel (ScheduleDock and About buttons) to the shared "Pnyx Tools" tab, creating
        /// the tab unless another Pnyx add-in already did. If Revit refuses a new tab (too many add-in tabs), the
        /// panel goes on the Add-Ins tab instead.
        /// </summary>
        public static void Build(UIControlledApplication application, ILogger logger)
        {
            bool ownTab = TryCreateTab(application, logger);
            var panel = ownTab
                ? application.CreateRibbonPanel(PluginInfo.RibbonTabName, PluginInfo.RibbonPanelName)
                : application.CreateRibbonPanel(PluginInfo.ProductName);

            var entry = AddinHost.Entry;
            var buttonData = new PushButtonData(
                PluginInfo.OpenCommandButtonName,
                PluginInfo.OpenCommandButtonText,
                entry.AssemblyPath,
                entry.CommandClass)
            {
                ToolTip = "Export schedules and model data (categories, selection, rooms, spaces, project standards) " +
                          "to Excel and import reviewed changes back into the model.",
                LongDescription =
                    "Every import is analysed and compared (original export / current model / Excel) " +
                    "before any change is written. Nothing is applied without review." +
                    Environment.NewLine + Environment.NewLine + "Version " + PluginInfo.Version,
                AvailabilityClassName = entry.AvailabilityClass,
                LargeImage = RibbonIcons.Create(32),
                Image = RibbonIcons.Create(16)
            };

            panel.AddItem(buttonData);

            panel.AddItem(new PushButtonData(
                PluginInfo.AboutButtonName,
                PluginInfo.AboutButtonText,
                entry.AssemblyPath,
                entry.AboutCommandClass)
            {
                ToolTip = "About " + PluginInfo.ProductName + ": version, updates and support contact (" + PluginInfo.SupportEmail + ").",
                AvailabilityClassName = entry.AlwaysAvailableClass,
                LargeImage = RibbonIcons.CreateAbout(32),
                Image = RibbonIcons.CreateAbout(16)
            });

            logger.Info($"Ribbon panel created on {(ownTab ? "the " + PluginInfo.RibbonTabName : "the Add-Ins")} tab (command: {entry.CommandClass}).");
        }

        private static bool TryCreateTab(UIControlledApplication application, ILogger logger)
        {
            try
            {
                application.CreateRibbonTab(PluginInfo.RibbonTabName);
                return true;
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                // The tab already exists (created by another Pnyx add-in, or earlier in this session); reuse it.
                return true;
            }
            catch (Exception ex)
            {
                logger.Warning("Pnyx Tools ribbon tab not available; using the Add-Ins tab.", ex);
                return false;
            }
        }
    }
}
