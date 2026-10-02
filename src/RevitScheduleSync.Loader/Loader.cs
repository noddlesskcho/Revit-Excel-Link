using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitScheduleSync.Loader
{
    /// <summary>
    /// Revit 2025 entry point (FullClassName in the manifest). Loads the add-in core from the "core" subfolder
    /// into a private load context and forwards OnStartup/OnShutdown to it.
    /// </summary>
    public sealed class LoaderApplication : IExternalApplication
    {
        private const string CoreAssembly = "RevitScheduleSync";
        private IExternalApplication? _core;

        /// <summary>The core assembly, loaded in the private context. Used by the proxy command classes.</summary>
        internal static Assembly? Core { get; private set; }

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                var folder = Path.GetDirectoryName(typeof(LoaderApplication).Assembly.Location)!;
                var corePath = Path.Combine(folder, "core", CoreAssembly + ".dll");
                if (!File.Exists(corePath))
                {
                    LoaderLog.Write($"Core assembly not found: {corePath}");
                    return Result.Failed;
                }

                var context = new IsolatedLoadContext(corePath);
                Core = context.LoadFromAssemblyPath(corePath);

                // Route the ribbon button and availability check through this assembly's proxies,
                // so Revit never loads RevitScheduleSync.dll a second time in its own context.
                Core.GetType("RevitScheduleSync.App.AddinHost", throwOnError: true)!
                    .GetMethod("UseEntryPoints", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!
                    .Invoke(null, [typeof(LoaderApplication).Assembly.Location, typeof(LoaderCommand).FullName!, typeof(LoaderAvailability).FullName!]);

                // IExternalApplication comes from RevitAPIUI, which the private context shares with Revit.
                _core = (IExternalApplication)Activator.CreateInstance(
                    Core.GetType("RevitScheduleSync.App.ScheduleSyncApplication", throwOnError: true)!)!;
                return _core.OnStartup(application);
            }
            catch (Exception ex)
            {
                LoaderLog.Write("Startup failed: " + ex);
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            try
            {
                return _core?.OnShutdown(application) ?? Result.Succeeded;
            }
            catch (Exception ex)
            {
                LoaderLog.Write("Shutdown failed: " + ex);
                return Result.Succeeded;
            }
        }

        internal static T CreateCore<T>(string typeName) where T : class =>
            (T)Activator.CreateInstance((Core ?? throw new InvalidOperationException("Schedule Sync did not start.")).GetType(typeName, throwOnError: true)!)!;
    }

    /// <summary>Ribbon command proxy. Transaction mode must match the core command (manual).</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class LoaderCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                return LoaderApplication.CreateCore<IExternalCommand>("RevitScheduleSync.Commands.OpenScheduleSyncCommand")
                    .Execute(commandData, ref message, elements);
            }
            catch (Exception ex)
            {
                // The core command has its own error handling; this only covers a failed load.
                LoaderLog.Write("Command failed to start: " + ex);
                message = "Schedule Sync could not start. See %LOCALAPPDATA%\\RevitScheduleSync\\Logs\\loader.log.";
                return Result.Failed;
            }
        }
    }

    /// <summary>Availability proxy: enabled only for project documents (decided by the core).</summary>
    public sealed class LoaderAvailability : IExternalCommandAvailability
    {
        private static IExternalCommandAvailability? _inner;

        public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories)
        {
            try
            {
                _inner ??= LoaderApplication.CreateCore<IExternalCommandAvailability>("RevitScheduleSync.Commands.ProjectDocumentAvailability");
                return _inner.IsCommandAvailable(applicationData, selectedCategories);
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Private load context: resolves the core and its NuGet dependencies from core\*.deps.json.
    /// Revit API, WPF and the .NET framework are not listed there and fall back to Revit's own context,
    /// so Revit types (IExternalApplication, Document ...) stay shared.
    /// </summary>
    internal sealed class IsolatedLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public IsolatedLoadContext(string mainAssemblyPath)
            : base("ScheduleSyncForRevit", isCollectible: false)
        {
            _resolver = new AssemblyDependencyResolver(mainAssemblyPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var name = assemblyName.Name ?? string.Empty;
            if (name.StartsWith("RevitAPI", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("AdWindows", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("UIFramework", StringComparison.OrdinalIgnoreCase))
            {
                return null; // always Revit's copy
            }

            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path == null ? null : LoadFromAssemblyPath(path);
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return path == null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
        }
    }

    /// <summary>Minimal log for failures before the core (and its logger) is running. Never throws.</summary>
    internal static class LoaderLog
    {
        public static void Write(string message)
        {
            try
            {
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RevitScheduleSync", "Logs");
                Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, "loader.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
            catch
            {
                // Logging must never affect Revit.
            }
        }
    }
}
