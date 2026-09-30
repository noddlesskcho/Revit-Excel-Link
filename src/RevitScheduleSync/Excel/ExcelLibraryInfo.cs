using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using ClosedXML.Excel;

namespace RevitScheduleSync.Excel
{
    /// <summary>
    /// Phase 0 deployment check: touching a ClosedXML type proves that ClosedXML and
    /// DocumentFormat.OpenXml resolve inside Revit's add-in load context.
    /// Workbook reading/writing arrives in Phase 3 behind its own interface.
    /// </summary>
    internal static class ExcelLibraryInfo
    {
        public static string GetVersion()
        {
            try
            {
                return ReadClosedXmlVersion();
            }
            catch (Exception ex)
            {
                return $"NOT LOADED ({ex.GetType().Name})";
            }
        }

        // Separate, non-inlined method: a missing assembly fails when this method is JIT-compiled,
        // which then happens inside the caller's try block instead of before it.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string ReadClosedXmlVersion()
        {
            var assembly = typeof(XLWorkbook).Assembly;
            return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                       .InformationalVersion.Split('+')[0]
                   ?? assembly.GetName().Version?.ToString()
                   ?? "loaded";
        }
    }
}
