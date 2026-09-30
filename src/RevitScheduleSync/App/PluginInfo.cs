using System.Reflection;

namespace RevitScheduleSync.App
{
    /// <summary>Fixed identifiers and names used across the add-in.</summary>
    internal static class PluginInfo
    {
        public const string ProductName = "Revit Schedule Sync";
        public const string RibbonPanelName = "Schedule Sync";
        public const string OpenCommandButtonName = "RSS_OpenScheduleSync";
        public const string OpenCommandButtonText = "Schedule\nSync";

        public static string Version =>
            typeof(PluginInfo).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion.Split('+')[0]
            ?? typeof(PluginInfo).Assembly.GetName().Version?.ToString()
            ?? "unknown";
    }
}
