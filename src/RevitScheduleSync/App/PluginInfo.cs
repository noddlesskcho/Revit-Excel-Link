using System.Reflection;

namespace RevitScheduleSync.App
{
    /// <summary>Fixed identifiers and names used across the add-in.</summary>
    internal static class PluginInfo
    {
        public const string ProductName = "Revit-Excel Link";
        public const string RibbonTabName = "Revit-Excel Link";
        public const string RibbonPanelName = "Excel";
        public const string OpenCommandButtonName = "RSS_OpenScheduleSync";
        public const string OpenCommandButtonText = "Excel\nLink";

        public static string Version =>
            typeof(PluginInfo).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion.Split('+')[0]
            ?? typeof(PluginInfo).Assembly.GetName().Version?.ToString()
            ?? "unknown";
    }
}
