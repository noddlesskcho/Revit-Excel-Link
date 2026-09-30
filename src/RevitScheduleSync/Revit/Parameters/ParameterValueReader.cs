using System;
using System.Globalization;
using Autodesk.Revit.DB;
using RevitScheduleSync.Models.Export;

namespace RevitScheduleSync.Revit.Parameters
{
    /// <summary>
    /// Converts a Revit parameter value into the value written to Excel plus the original
    /// raw / normalised / display forms kept for three-way comparison on import.
    /// Numbers are converted from Revit internal units into the column's export unit with
    /// UnitUtils.ConvertFromInternalUnits and kept at full precision (Excel formatting rounds for display).
    /// </summary>
    internal static class ParameterValueReader
    {
        /// <summary>Excel's maximum characters per cell.</summary>
        public const int MaxCellText = 32767;

        public static ExportCell Read(Parameter parameter, ExportColumn column, FormatOptions? fieldFormat) =>
            Read(parameter, column.Kind, column.ExportUnitTypeId, fieldFormat);

        /// <summary>Same conversion, from the column description stored in the workbook (used by import).</summary>
        public static ExportCell Read(Parameter parameter, CellKind kind, string exportUnitTypeId, FormatOptions? fieldFormat)
        {
            bool hasValue = parameter.HasValue;
            string display = SafeDisplay(parameter, fieldFormat);

            switch (parameter.StorageType)
            {
                case StorageType.String:
                {
                    var text = parameter.AsString() ?? string.Empty;
                    return Text(text, text, hasValue, display);
                }

                case StorageType.Integer:
                {
                    int value = parameter.AsInteger();
                    string raw = value.ToString(CultureInfo.InvariantCulture);

                    if (kind == CellKind.YesNo)
                    {
                        if (!hasValue)
                        {
                            return Cell(null, raw, string.Empty, display, false);
                        }

                        return Cell(value != 0 ? "Yes" : "No", raw, value != 0 ? "1" : "0", display, true);
                    }

                    if (kind == CellKind.Integer)
                    {
                        return hasValue
                            ? Cell((double)value, raw, raw, display, true)
                            : Cell(null, raw, string.Empty, display, false);
                    }

                    // Enumerated integer (locked column): show Revit's text, keep the number as raw.
                    return Cell(display, raw, display, display, hasValue);
                }

                case StorageType.Double:
                {
                    double internalValue = parameter.AsDouble();
                    string raw = internalValue.ToString("R", CultureInfo.InvariantCulture);
                    if (!hasValue)
                    {
                        return Cell(null, raw, string.Empty, display, false);
                    }

                    double exported = string.IsNullOrEmpty(exportUnitTypeId)
                        ? internalValue
                        : UnitUtils.ConvertFromInternalUnits(internalValue, new ForgeTypeId(exportUnitTypeId));
                    return Cell(exported, raw, exported.ToString("R", CultureInfo.InvariantCulture), display, true);
                }

                case StorageType.ElementId:
                {
                    var id = parameter.AsElementId();
                    string raw = id.Value.ToString(CultureInfo.InvariantCulture);
                    bool set = hasValue && id != ElementId.InvalidElementId;

                    // Export the referenced element's name, never the numeric id.
                    return set
                        ? Cell(display, raw, display, display, true)
                        : Cell(null, raw, string.Empty, display, false);
                }

                default:
                    return ExportCell.Unavailable;
            }
        }

        /// <summary>Builds a text cell; values too long for Excel are truncated and the cell locked.</summary>
        public static ExportCell Text(string text, string raw, bool hasValue, string display)
        {
            if (text.Length > MaxCellText)
            {
                var cut = text[..MaxCellText];
                return new ExportCell
                {
                    Value = cut,
                    Raw = cut,
                    Normalised = cut,
                    Display = cut,
                    HasValue = hasValue,
                    IsAvailable = true,
                    ForceReadOnly = true,
                    LockReason = "value too long for Excel"
                };
            }

            return Cell(text.Length == 0 ? null : text, raw, text, display, hasValue);
        }

        private static ExportCell Cell(object? value, string raw, string normalised, string display, bool hasValue) => new()
        {
            Value = value,
            Raw = raw,
            Normalised = normalised,
            Display = display.Length > MaxCellText ? display[..MaxCellText] : display,
            HasValue = hasValue,
            IsAvailable = true
        };

        private static string SafeDisplay(Parameter parameter, FormatOptions? fieldFormat)
        {
            try
            {
                return (fieldFormat != null && !fieldFormat.UseDefault
                           ? parameter.AsValueString(fieldFormat)
                           : parameter.AsValueString())
                       ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }
}
