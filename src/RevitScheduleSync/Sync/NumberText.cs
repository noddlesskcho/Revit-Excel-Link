using System.Globalization;
using System.Text.RegularExpressions;

namespace RevitScheduleSync.Sync
{
    /// <summary>
    /// Reads a number typed as text in Excel (a text-formatted cell). Numbers in number cells never come here.
    /// "1.234" and "12,345" mean a thousand and more in one locale and a decimal in another, so they are refused
    /// instead of guessed: a wrong guess would change the value by a factor of 1000. The same applies when a unit
    /// follows ("1.234 mm"), because Revit would read that with the project's own separators.
    /// "0.500" is not refused: a thousands group never follows a leading zero.
    /// </summary>
    public static class NumberText
    {
        // One to three digits (not starting with 0), then groups of exactly three digits after a '.' or ',',
        // then the end of the text or a unit.
        private static readonly Regex AmbiguousGrouping = new(@"^[+-]?[1-9]\d{0,2}([.,]\d{3})+(?![\d.,])", RegexOptions.CultureInvariant);

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
