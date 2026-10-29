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
            var requested = new AssemblyName(args.Name);
            var path = Path.Combine(Folder, requested.Name + ".dll");
            if (!File.Exists(path))
            {
                return null;
            }

            // This handler sees every failed load in Revit's AppDomain. Answer our own assemblies' requests, and
            // other requests only when our copy is not older than the version asked for; anything else would
            // hand another add-in a library it cannot use.
            var requester = args.RequestingAssembly;
            bool fromUs = requester != null && !requester.IsDynamic
                && string.Equals(Path.GetDirectoryName(requester.Location), Folder, StringComparison.OrdinalIgnoreCase);
            if (!fromUs && requested.Version != null)
            {
                var ours = AssemblyName.GetAssemblyName(path).Version;
                if (ours == null || ours < requested.Version)
                {
                    return null;
                }
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
