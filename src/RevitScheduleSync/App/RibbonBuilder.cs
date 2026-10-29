using System;
using Autodesk.Revit.UI;
using RevitScheduleSync.Commands;
using RevitScheduleSync.Logging;

namespace RevitScheduleSync.App
{
    internal static class RibbonBuilder
    {
        /// <summary>Adds the "Revit-Excel Link" ribbon tab with an "Excel" panel.</summary>
        public static void Build(UIControlledApplication application, ILogger logger)
        {
            try
            {
                application.CreateRibbonTab(PluginInfo.RibbonTabName);
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                // The tab already exists (created earlier in this session); reuse it.
            }

            var panel = application.CreateRibbonPanel(PluginInfo.RibbonTabName, PluginInfo.RibbonPanelName);

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

            var help = application.CreateRibbonPanel(PluginInfo.RibbonTabName, PluginInfo.InfoPanelName);
            help.AddItem(new PushButtonData(
                PluginInfo.AboutButtonName,
                PluginInfo.AboutButtonText,
                entry.AssemblyPath,
                entry.AboutCommandClass)
            {
                ToolTip = "Version, updates and support contact (" + PluginInfo.SupportEmail + ").",
                AvailabilityClassName = entry.AlwaysAvailableClass,
                LargeImage = RibbonIcons.CreateAbout(32),
                Image = RibbonIcons.CreateAbout(16)
            });

            logger.Info($"Ribbon panels created (command: {entry.CommandClass}).");
        }
    }
}
