#if NETFRAMEWORK
using System;
using System.IO;
using System.Reflection;

namespace RevitScheduleSync
{
    /// <summary>
    /// Revit 2024 (.NET Framework) has no binding redirects for add-ins. ClosedXML's dependencies reference
    /// slightly different versions of each other (System.Memory, System.Buffers, ...), so a load that fails on
    /// the exact version is answered with the copy shipped next to this add-in.
    /// </summary>
    internal static class AssemblyResolver
    {
        private static readonly string Folder = Path.GetDirectoryName(typeof(AssemblyResolver).Assembly.Location)!;

        public static void Register() => AppDomain.CurrentDomain.AssemblyResolve += Resolve;

        public static void Unregister() => AppDomain.CurrentDomain.AssemblyResolve -= Resolve;

        private static Assembly? Resolve(object? sender, ResolveEventArgs args)
        {
            var name = new AssemblyName(args.Name).Name;
            var path = Path.Combine(Folder, name + ".dll");
            if (!File.Exists(path))
            {
                return null;
            }

            foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!loaded.IsDynamic && string.Equals(loaded.Location, path, StringComparison.OrdinalIgnoreCase))
                {
                    return loaded;
                }
            }

            return Assembly.LoadFrom(path);
        }
    }
}
#endif
