using System.Globalization;
using System.Text.RegularExpressions;

namespace RevitScheduleSync.Sync
{
    /// <summary>
    /// Reads a number typed as text in Excel (a text-formatted cell). Numbers in number cells never come here.
    /// "1.234" and "12,345" mean a thousand and more in one locale and a decimal in another, so they are refused
    /// instead of guessed: a wrong guess would change the value by a factor of 1000.
    /// </summary>
    public static class NumberText
    {
        // One to three digits, then groups of exactly three digits after a '.' or ','.
        private static readonly Regex AmbiguousGrouping = new(@"^[+-]?\d{1,3}([.,]\d{3})+$", RegexOptions.CultureInvariant);

        public enum Result
        {
            Parsed,
            Ambiguous,
            NotANumber
        }

        public static Result TryParse(string text, CultureInfo culture, out double value)
        {
            value = 0;
            var trimmed = text.Trim();
            if (AmbiguousGrouping.IsMatch(trimmed))
            {
                return Result.Ambiguous;
            }

            // A comma is a decimal separator only where the locale says so; everything else is read the invariant way.
            bool commaDecimal = culture.NumberFormat.NumberDecimalSeparator == ","
                                && trimmed.IndexOf(',') >= 0 && trimmed.IndexOf('.') < 0;
            return double.TryParse(trimmed, NumberStyles.Float, commaDecimal ? culture : CultureInfo.InvariantCulture, out value)
                ? Result.Parsed
                : Result.NotANumber;
        }
    }
}
