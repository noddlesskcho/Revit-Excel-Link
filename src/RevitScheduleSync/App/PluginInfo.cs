using System;
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
        public const string InfoPanelName = "Help";
        public const string AboutButtonName = "RSS_About";
        public const string AboutButtonText = "About";

        public const string SupportEmail = "pynxsg@gmail.com";
        public const string RepositoryUrl = "https://github.com/noddlesskcho/Revit-Excel-Link";
        public const string LatestReleaseUrl = RepositoryUrl + "/releases/latest";

        /// <summary>Major.minor.patch, compared with the GitHub release tag by the update check.</summary>
        public static Version NumericVersion
        {
            get
            {
                var v = typeof(PluginInfo).Assembly.GetName().Version ?? new Version(0, 0, 0);
                return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
            }
        }

        public static string Version =>
            typeof(PluginInfo).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion.Split('+')[0]
            ?? typeof(PluginInfo).Assembly.GetName().Version?.ToString()
            ?? "unknown";
    }
}
