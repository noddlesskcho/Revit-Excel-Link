using RevitScheduleSync.Logging;

namespace RevitScheduleSync.App
{
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

        internal static void Initialize(FileLogger logger) => FileLogger = logger;

        internal static void Shutdown() => FileLogger = null;
    }
}
