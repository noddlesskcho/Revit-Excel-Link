using System;
using System.Linq;

namespace RevitScheduleSync
{
    /// <summary>Generic enum helpers that also work on .NET Framework 4.8 (Revit 2024).</summary>
    internal static class Enums
    {
        public static T Parse<T>(string value)
            where T : struct, Enum => (T)Enum.Parse(typeof(T), value);

        public static T[] Values<T>()
            where T : struct, Enum => Enum.GetValues(typeof(T)).Cast<T>().ToArray();
    }
}
