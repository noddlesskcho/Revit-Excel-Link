using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using RevitScheduleSync.Models;
using RevitScheduleSync.Models.Export;
using RevitScheduleSync.Models.Import;
// Hosts with a global "using System.Drawing" (CORENET X Validation Tool) would make Color ambiguous.
using Color = Autodesk.Revit.DB.Color;

namespace RevitScheduleSync.Revit.Styles
{
    /// <summary>
    /// Reads and writes the graphic properties of object styles and line styles. A style row is identified by
    /// the UniqueId of the category's projection GraphicsStyle (categories themselves have no UniqueId).
    /// </summary>
    internal sealed class StyleAccess
    {
        public const string LineWeightProjection = "LineWeightProjection";
        public const string LineWeightCut = "LineWeightCut";
        public const string LineColor = "LineColor";
        public const string LinePatternProjection = "LinePatternProjection";
        public const string LinePatternCut = "LinePatternCut";
        public const string Material = "Material";

        /// <summary>Text written for "no material" / "no pattern" so the cell is never confused with "not set".</summary>
        public const string NoneText = "<None>";
        public const string SolidText = "Solid";

        private readonly Document _document;
        private Dictionary<string, ElementId>? _patterns;
        private Dictionary<string, ElementId>? _materials;

        public StyleAccess(Document document)
        {
            _document = document;
        }

        public static IReadOnlyList<(string Key, string Heading, CellKind Kind)> Properties(bool lineStyles) => lineStyles
            ?
            [
                (LineWeightProjection, "Line Weight", CellKind.Integer),
                (LineColor, "Line Color (R,G,B)", CellKind.Text),
                (LinePatternProjection, "Line Pattern", CellKind.Reference)
            ]
            :
            [
                (LineWeightProjection, "Line Weight Projection", CellKind.Integer),
                (LineWeightCut, "Line Weight Cut", CellKind.Integer),
                (LineColor, "Line Color (R,G,B)", CellKind.Text),
                (LinePatternProjection, "Line Pattern Projection", CellKind.Reference),
                (LinePatternCut, "Line Pattern Cut", CellKind.Reference),
                (Material, "Material", CellKind.Reference)
            ];

        public static ParameterIdentity Identity(string key, string heading) => new()
        {
            Source = ParameterSource.Style,
            Name = heading,
            BuiltInName = key
        };

        public Category? CategoryOf(string graphicsStyleUniqueId) =>
            (_document.GetElement(graphicsStyleUniqueId) as GraphicsStyle)?.GraphicsStyleCategory;

        public IReadOnlyList<string> PatternNames() =>
            new[] { SolidText }.Concat(Patterns().Keys.Where(k => k != SolidText).OrderBy(k => k, StringComparer.CurrentCultureIgnoreCase)).ToList();

        public IReadOnlyList<string> MaterialNames() =>
            new[] { NoneText }.Concat(Materials().Keys.OrderBy(k => k, StringComparer.CurrentCultureIgnoreCase)).ToList();

        /// <summary>True when the property applies to this category (cut values only exist for cuttable categories).</summary>
        public static bool Applies(Category category, string key) => key switch
        {
            LineWeightCut or LinePatternCut => category.IsCuttable,
            Material => category.CategoryType == CategoryType.Model,
            _ => true
        };

        public ExportCell Read(Category category, string key)
        {
            if (!Applies(category, key))
            {
                return ExportCell.Unavailable;
            }

            switch (key)
            {
                case LineWeightProjection:
                case LineWeightCut:
                {
                    var weight = category.GetLineWeight(key == LineWeightCut ? GraphicsStyleType.Cut : GraphicsStyleType.Projection);
                    if (weight == null)
                    {
                        return ExportCell.Unavailable;
                    }

                    var text = weight.Value.ToString(CultureInfo.InvariantCulture);
                    return Cell((double)weight.Value, text);
                }

                case LineColor:
                {
                    var color = category.LineColor;
                    var text = color == null || !color.IsValid ? string.Empty : FormatColor(color);
                    return Cell(text.Length == 0 ? null : text, text);
                }

                case LinePatternProjection:
                case LinePatternCut:
                {
                    var id = category.GetLinePatternId(key == LinePatternCut ? GraphicsStyleType.Cut : GraphicsStyleType.Projection);
                    var text = PatternName(id);
                    return Cell(text, text);
                }

                case Material:
                {
                    var material = category.Material;
                    var text = material == null ? NoneText : material.Name;
                    return Cell(text, text);
                }

                default:
                    return ExportCell.Unavailable;
            }
        }

        /// <summary>Turns the Excel text into a value that can be written; returns an error text when it cannot.</summary>
        public (TargetValue? Target, string? Error) Resolve(string key, string normalised)
        {
            switch (key)
            {
                case LineWeightProjection:
                case LineWeightCut:
                    return int.TryParse(normalised, NumberStyles.Integer, CultureInfo.InvariantCulture, out var weight) && weight is >= 1 and <= 16
                        ? (TargetValue.OfInteger(weight), null)
                        : (null, "Line weight must be a whole number from 1 to 16.");

                case LineColor:
                    return TryParseColor(normalised, out _)
                        ? (TargetValue.OfText(normalised), null)
                        : (null, "Enter the colour as R,G,B with values from 0 to 255, e.g. 255,0,0.");

                case LinePatternProjection:
                case LinePatternCut:
                    return Patterns().TryGetValue(normalised.Trim(), out var pattern)
                        ? (TargetValue.OfElementId(pattern.Value), null)
                        : (null, $"No line pattern named \"{normalised.Trim()}\" exists in the model.");

                case Material:
                    if (normalised.Trim().Length == 0 || normalised.Trim() == NoneText)
                    {
                        return (TargetValue.OfElementId(ElementId.InvalidElementId.Value), null);
                    }

                    return Materials().TryGetValue(normalised.Trim(), out var material)
                        ? (TargetValue.OfElementId(material.Value), null)
                        : (null, $"No material named \"{normalised.Trim()}\" exists in the model.");

                default:
                    return (null, "Unknown style property.");
            }
        }

        /// <summary>Writes one property. Must run inside a transaction. Returns null on success, else the reason.</summary>
        public string? Write(Category category, string key, TargetValue value)
        {
            switch (key)
            {
                case LineWeightProjection:
                    category.SetLineWeight(value.Integer!.Value, GraphicsStyleType.Projection);
                    return null;
                case LineWeightCut:
                    category.SetLineWeight(value.Integer!.Value, GraphicsStyleType.Cut);
                    return null;
                case LineColor:
                    if (!TryParseColor(value.Text ?? string.Empty, out var color))
                    {
                        return "Invalid colour.";
                    }

                    category.LineColor = color;
                    return null;
                case LinePatternProjection:
                    category.SetLinePatternId(new ElementId(value.ElementIdValue!.Value), GraphicsStyleType.Projection);
                    return null;
                case LinePatternCut:
                    category.SetLinePatternId(new ElementId(value.ElementIdValue!.Value), GraphicsStyleType.Cut);
                    return null;
                case Material:
                    var id = new ElementId(value.ElementIdValue!.Value);
                    category.Material = id == ElementId.InvalidElementId ? null : _document.GetElement(id) as Autodesk.Revit.DB.Material;
                    return null;
                default:
                    return "Unknown style property.";
            }
        }

        public static string FormatColor(Color color) =>
            string.Format(CultureInfo.InvariantCulture, "{0},{1},{2}", color.Red, color.Green, color.Blue);

        public static bool TryParseColor(string text, out Color color)
        {
            color = null!;
            var parts = text.Split(',', ';').Select(p => p.Trim()).ToArray();
            if (parts.Length != 3)
            {
                return false;
            }

            var values = new byte[3];
            for (int i = 0; i < 3; i++)
            {
                if (!byte.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out values[i]))
                {
                    return false;
                }
            }

            color = new Color(values[0], values[1], values[2]);
            return true;
        }

        private string PatternName(ElementId id)
        {
            if (id == LinePatternElement.GetSolidPatternId())
            {
                return SolidText;
            }

            return id == ElementId.InvalidElementId ? SolidText : _document.GetElement(id)?.Name ?? SolidText;
        }

        private Dictionary<string, ElementId> Patterns()
        {
            if (_patterns == null)
            {
                _patterns = new Dictionary<string, ElementId>(StringComparer.Ordinal) { [SolidText] = LinePatternElement.GetSolidPatternId() };
                foreach (var pattern in new FilteredElementCollector(_document).OfClass(typeof(LinePatternElement)))
                {
                    _patterns.TryAdd(pattern.Name, pattern.Id);
                }
            }

            return _patterns;
        }

        private Dictionary<string, ElementId> Materials()
        {
            if (_materials == null)
            {
                _materials = new Dictionary<string, ElementId>(StringComparer.Ordinal);
                foreach (var material in new FilteredElementCollector(_document).OfClass(typeof(Autodesk.Revit.DB.Material)))
                {
                    _materials.TryAdd(material.Name, material.Id);
                }
            }

            return _materials;
        }

        private static ExportCell Cell(object? value, string text) => new()
        {
            Value = value,
            Raw = text,
            Normalised = text,
            Display = text,
            HasValue = text.Length > 0,
            IsAvailable = true
        };
    }
}
