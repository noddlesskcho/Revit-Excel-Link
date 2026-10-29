using RevitScheduleSync.Commands;
using RevitScheduleSync.Logging;

namespace RevitScheduleSync.App
{
    /// <summary>Assembly and classes Revit must call for the ribbon buttons and their availability checks.</summary>
    internal sealed record EntryPoints(
        string AssemblyPath,
        string CommandClass,
        string AvailabilityClass,
        string AboutCommandClass,
        string AlwaysAvailableClass);

    /// <summary>
    /// The single shared entry point for add-in services.
    /// Revit creates IExternalCommand instances itself (parameterless constructor), so commands
    /// cannot receive services through a constructor. Everything below the command boundary
    /// receives its dependencies explicitly; only command classes read from here.
    /// </summary>
    internal static class AddinHost
    {
        public static FileLogger? FileLogger { get; private set; }

        public static ILogger Logger => (ILogger?)FileLogger ?? NullLogger.Instance;

        /// <summary>
        /// Revit 2024/2026/2027: this assembly's own command classes. Revit 2025: the loader's proxy classes, so Revit
        /// never loads a second copy of this assembly outside the private load context.
        /// </summary>
        public static EntryPoints Entry { get; private set; } = new(
            typeof(OpenScheduleSyncCommand).Assembly.Location,
            typeof(OpenScheduleSyncCommand).FullName!,
            typeof(ProjectDocumentAvailability).FullName!,
            typeof(AboutCommand).FullName!,
            typeof(AlwaysAvailable).FullName!);

        internal static void Initialize(FileLogger logger) => FileLogger = logger;

        internal static void Shutdown() => FileLogger = null;

        /// <summary>Called by RevitScheduleSync.Loader (via reflection) before OnStartup.</summary>
        internal static void UseEntryPoints(
            string assemblyPath, string commandClass, string availabilityClass, string aboutCommandClass, string alwaysAvailableClass) =>
            Entry = new EntryPoints(assemblyPath, commandClass, availabilityClass, aboutCommandClass, alwaysAvailableClass);
    }
}
