using System;
using System.Globalization;
using RevitScheduleSync.Models.Export;

namespace RevitScheduleSync.Sync
{
    /// <summary>
    /// Compares normalised values (the invariant strings stored at export and produced on import).
    /// Numbers compare with a small relative tolerance so unit round-trips do not show as changes.
    /// </summary>
    public static class ValueComparer
    {
        private const double RelativeTolerance = 1e-9;

        public static bool AreEqual(CellKind kind, string a, string b)
        {
            if (kind is CellKind.Number or CellKind.Integer)
            {
                bool hasA = TryNumber(a, out var x);
                bool hasB = TryNumber(b, out var y);
                if (hasA && hasB)
                {
                    return Math.Abs(x - y) <= RelativeTolerance * Math.Max(1.0, Math.Max(Math.Abs(x), Math.Abs(y)));
                }

                return !hasA && !hasB && a == b;
            }

            if (kind == CellKind.Reference)
            {
                return string.Equals(a.Trim(), b.Trim(), StringComparison.Ordinal);
            }

            return string.Equals(a, b, StringComparison.Ordinal);
        }

        /// <summary>Three-way classification of one value (A original, B current Revit, C Excel).</summary>
        public static ThreeWay Classify(CellKind kind, string a, string b, string c)
        {
            bool ab = AreEqual(kind, a, b);
            bool ac = AreEqual(kind, a, c);

            if (ab && ac) return ThreeWay.NoChange;
            if (ab) return ThreeWay.ExcelChange;
            if (ac) return ThreeWay.RevitChange;
            return AreEqual(kind, b, c) ? ThreeWay.SameChange : ThreeWay.Conflict;
        }

        private static bool TryNumber(string text, out double value) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    public enum ThreeWay
    {
        NoChange,
        ExcelChange,
        RevitChange,
        SameChange,
        Conflict
    }
}
