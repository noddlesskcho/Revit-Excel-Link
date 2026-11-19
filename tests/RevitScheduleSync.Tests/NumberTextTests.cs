using System.Globalization;
using RevitScheduleSync.Sync;
using Xunit;

namespace RevitScheduleSync.Tests
{
    public class NumberTextTests
    {
        private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
        private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

        [Theory]
        [InlineData("900", 900)]
        [InlineData("1.5", 1.5)]
        [InlineData(" -2.25 ", -2.25)]
        [InlineData("1234", 1234)]
        [InlineData("1.2345", 1.2345)]   // four decimals: not a thousands group
        [InlineData("1e3", 1000)]
        [InlineData("0.500", 0.5)]       // a thousands group never follows a leading zero
        [InlineData("-0.125", -0.125)]
        public void Plain_numbers_read_the_same_in_every_locale(string text, double expected)
        {
            Assert.Equal(NumberText.Result.Parsed, NumberText.TryParse(text, English, out var en));
            Assert.Equal(expected, en, 12);
            Assert.Equal(NumberText.Result.Parsed, NumberText.TryParse(text, German, out var de));
            Assert.Equal(expected, de, 12);
        }

        [Theory]
        [InlineData("1.234")]       // 1234 in Germany, 1.234 in the US
        [InlineData("12,345")]      // 12345 in the US, 12.345 in Germany
        [InlineData("1.234.567")]
        [InlineData("-1,000")]
        [InlineData("1.234 mm")]    // a unit after it does not settle the separator
        [InlineData("1,234'")]
        public void Separators_that_could_mean_thousands_are_refused(string text)
        {
            Assert.Equal(NumberText.Result.Ambiguous, NumberText.TryParse(text, English, out _));
            Assert.Equal(NumberText.Result.Ambiguous, NumberText.TryParse(text, German, out _));
        }

        [Fact]
        public void Comma_decimal_is_accepted_only_where_the_locale_uses_it()
        {
            Assert.Equal(NumberText.Result.Parsed, NumberText.TryParse("1,5", German, out var value));
            Assert.Equal(1.5, value, 12);
            Assert.Equal(NumberText.Result.Parsed, NumberText.TryParse("0,125", German, out value));
            Assert.Equal(0.125, value, 12);
            Assert.Equal(NumberText.Result.NotANumber, NumberText.TryParse("1,5", English, out _));
        }

        [Theory]
        [InlineData("")]
        [InlineData("N/A")]
        [InlineData("900 mm")]   // units are left to Revit's own parser
        [InlineData("1.5 mm")]
        [InlineData("0.500 mm")]
        public void Non_numbers_are_not_parsed(string text) =>
            Assert.Equal(NumberText.Result.NotANumber, NumberText.TryParse(text, English, out _));
    }
}
