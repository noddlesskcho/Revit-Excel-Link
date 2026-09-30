using System;
using System.Globalization;
using Autodesk.Revit.DB;
using RevitScheduleSync.Models;
using RevitScheduleSync.Models.Export;
using RevitScheduleSync.Models.Import;

namespace RevitScheduleSync.Revit.Parameters
{
    /// <summary>Result of interpreting one Excel cell for a column.</summary>
    internal sealed record ParsedValue(string Normalised, string Display, TargetValue? Target, string? Error)
    {
        public bool IsValid => Error == null;

        public static ParsedValue Invalid(string display, string error) => new(display, display, null, error);
    }

    /// <summary>
    /// Converts Excel cell values back into Revit values. Numbers typed as plain numbers are in the
    /// column's export unit and are converted with UnitUtils.ConvertToInternalUnits; text such as
    /// "3' 6\"" or "900 mm" is parsed with UnitFormatUtils.TryParse using the project units.
    /// Reference (ElementId) values stay as names here; ReferenceResolver turns them into ids.
    /// </summary>
    internal sealed class ExcelValueParser
    {
        private readonly Units _units;

        public ExcelValueParser(Document document)
        {
            _units = document.GetUnits();
        }

        public ParsedValue Parse(ExcelValue value, ImportColumn column)
        {
            return column.Kind switch
            {
                CellKind.Text => ParseText(value),
                CellKind.Number => ParseNumber(value, column),
                CellKind.Integer => ParseInteger(value),
                CellKind.YesNo => ParseYesNo(value),
                CellKind.Reference => ParseReference(value),
                _ => ParsedValue.Invalid(value.Text, "Unsupported column type.")
            };
        }

        private static ParsedValue ParseText(ExcelValue value)
        {
            string text = value.Kind switch
            {
                ExcelValueKind.Blank => string.Empty,
                // Excel turned typed digits into a number (cell was not text-formatted): keep the digits.
                ExcelValueKind.Number => value.Number!.Value.ToString("0.###############", CultureInfo.InvariantCulture),
                _ => value.Text
            };

            return new ParsedValue(text, text, TargetValue.OfText(text), null);
        }

        private ParsedValue ParseNumber(ExcelValue value, ImportColumn column)
        {
            if (value.IsBlank)
            {
                // Revit cannot clear a number parameter through the API; allowed only when it was blank already.
                return new ParsedValue(string.Empty, string.Empty, null, null);
            }

            var unit = string.IsNullOrEmpty(column.ExportUnitTypeId) ? null : new ForgeTypeId(column.ExportUnitTypeId);
            double exportValue;

            if (value.Number.HasValue)
            {
                exportValue = value.Number.Value;
            }
            else if (TryParseDouble(value.Text, out var typed))
            {
                exportValue = typed;
            }
            else if (!string.IsNullOrEmpty(column.SpecTypeId) && unit != null
                     && UnitFormatUtils.TryParse(_units, new ForgeTypeId(column.SpecTypeId), value.Text, out var internalValue))
            {
                // Text with units ("3' 6\"", "900 mm"): Revit parses it into internal units.
                exportValue = UnitUtils.ConvertFromInternalUnits(internalValue, unit);
            }
            else
            {
                return ParsedValue.Invalid(value.Text, "Not a valid number.");
            }

            if (double.IsNaN(exportValue) || double.IsInfinity(exportValue))
            {
                return ParsedValue.Invalid(value.Text, "Not a valid number.");
            }

            double internalUnits = unit == null ? exportValue : UnitUtils.ConvertToInternalUnits(exportValue, unit);
            return new ParsedValue(
                exportValue.ToString("R", CultureInfo.InvariantCulture),
                exportValue.ToString("0.######", CultureInfo.CurrentCulture),
                TargetValue.OfDouble(internalUnits),
                null);
        }

        private static ParsedValue ParseInteger(ExcelValue value)
        {
            if (value.IsBlank)
            {
                return new ParsedValue(string.Empty, string.Empty, null, null);
            }

            double number;
            if (value.Number.HasValue)
            {
                number = value.Number.Value;
            }
            else if (!TryParseDouble(value.Text, out number))
            {
                return ParsedValue.Invalid(value.Text, "Not a valid whole number.");
            }

            if (Math.Abs(number - Math.Round(number)) > 1e-9 || number < int.MinValue || number > int.MaxValue)
            {
                return ParsedValue.Invalid(value.Text, "Not a valid whole number.");
            }

            int integer = (int)Math.Round(number);
            var text = integer.ToString(CultureInfo.InvariantCulture);
            return new ParsedValue(text, text, TargetValue.OfInteger(integer), null);
        }

        private static ParsedValue ParseYesNo(ExcelValue value)
        {
            if (value.IsBlank)
            {
                return new ParsedValue(string.Empty, string.Empty, null, null);
            }

            bool? parsed = value.Kind switch
            {
                ExcelValueKind.Boolean => value.Text == "TRUE",
                ExcelValueKind.Number when value.Number is 0 or 1 => value.Number == 1,
                _ => value.Text.Trim().ToLowerInvariant() switch
                {
                    "yes" or "y" or "true" or "1" => true,
                    "no" or "n" or "false" or "0" => false,
                    _ => null
                }
            };

            return parsed.HasValue
                ? new ParsedValue(parsed.Value ? "1" : "0", parsed.Value ? "Yes" : "No", TargetValue.OfInteger(parsed.Value ? 1 : 0), null)
                : ParsedValue.Invalid(value.Text, "Enter Yes or No.");
        }

        private static ParsedValue ParseReference(ExcelValue value)
        {
            var name = value.IsBlank ? string.Empty : value.Text.Trim();

            // Target is filled in by ReferenceResolver once the name has been matched to an element.
            return new ParsedValue(name, name, null, null);
        }

        private static bool TryParseDouble(string text, out double value) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
            || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
