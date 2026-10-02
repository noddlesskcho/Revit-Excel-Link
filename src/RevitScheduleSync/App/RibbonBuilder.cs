using Autodesk.Revit.UI;
using RevitScheduleSync.Commands;
using RevitScheduleSync.Logging;

namespace RevitScheduleSync.App
{
    internal static class RibbonBuilder
    {
        /// <summary>Adds the "Schedule Sync" panel to Revit's built-in Add-Ins tab.</summary>
        public static void Build(UIControlledApplication application, ILogger logger)
        {
            var panel = application.CreateRibbonPanel(PluginInfo.RibbonPanelName);

            var entry = AddinHost.Entry;
            var buttonData = new PushButtonData(
                PluginInfo.OpenCommandButtonName,
                PluginInfo.OpenCommandButtonText,
                entry.AssemblyPath,
                entry.CommandClass)
            {
                ToolTip = "Export Revit schedules to Excel and import reviewed changes back into the model.",
                LongDescription =
                    "Every import is analysed and compared (original export / current model / Excel) " +
                    "before any change is written. Nothing is applied without review.",
                AvailabilityClassName = entry.AvailabilityClass,
                LargeImage = RibbonIcons.Create(32),
                Image = RibbonIcons.Create(16)
            };

            panel.AddItem(buttonData);
            logger.Info($"Ribbon panel created (command: {entry.CommandClass}).");
        }
    }
}
