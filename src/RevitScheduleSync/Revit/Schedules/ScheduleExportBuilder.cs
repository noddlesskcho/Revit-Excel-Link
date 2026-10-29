using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using RevitScheduleSync.Logging;
using RevitScheduleSync.Models;
using RevitScheduleSync.Models.Export;
using RevitScheduleSync.Revit.Parameters;

namespace RevitScheduleSync.Revit.Schedules
{
    /// <summary>
    /// Turns analysed schedules into ExportSheet data. Read-only: never opens a transaction and
    /// never modifies the ScheduleDefinition. Must run in Revit API context.
    /// </summary>
    internal sealed class ScheduleExportBuilder
    {
        /// <summary>
        /// Composite display units (feet-and-inches, degrees-minutes-seconds ...) cannot be typed as a
        /// single number in Excel, so their columns are exported in the matching decimal unit.
        /// </summary>
        private static readonly Dictionary<string, ForgeTypeId> CompositeUnitReplacements = new()
        {
            [UnitTypeId.FeetFractionalInches.TypeId] = UnitTypeId.Feet,
            [UnitTypeId.FractionalInches.TypeId] = UnitTypeId.Inches,
            [UnitTypeId.MetersCentimeters.TypeId] = UnitTypeId.Meters,
            [UnitTypeId.DegreesMinutes.TypeId] = UnitTypeId.Degrees,
            [UnitTypeId.StationingFeet.TypeId] = UnitTypeId.Feet,
            [UnitTypeId.StationingSurveyFeet.TypeId] = UnitTypeId.UsSurveyFeet,
            [UnitTypeId.StationingMeters.TypeId] = UnitTypeId.Meters
        };

        private const string LockElementReadOnly = "read-only on that element";
        private const string LockInGroup = "element is in a model group";

        private readonly Document _document;
        private readonly ParameterLocator _parameters;
        private readonly ILogger _logger;

        public ScheduleExportBuilder(Document document, ILogger logger)
        {
            _document = document;
            _parameters = new ParameterLocator(document);
            _logger = logger;
        }

        public ExportSheet Build(ScheduleAnalysis analysis)
        {
            var summary = analysis.Schedule;
            if (_document.GetElement(summary.UniqueId) is not ViewSchedule schedule)
            {
                throw new InvalidOperationException($"Schedule '{summary.Name}' no longer exists.");
            }

            return summary.Support == ScheduleSupport.EditableSync
                ? BuildEditable(schedule, analysis)
                : BuildPresentation(schedule, analysis);
        }

        private ExportSheet BuildEditable(ViewSchedule schedule, ScheduleAnalysis analysis)
        {
            var definition = schedule.Definition;
            var fields = analysis.Fields;

            // Revit field objects, matched by FieldId (not just index) to be sure they belong to the analysis.
            var scheduleFields = fields
                .Select(f => definition.GetField(f.FieldIndex))
                .ToList();
            for (int i = 0; i < fields.Count; i++)
            {
                if (scheduleFields[i].FieldId.IntegerValue != fields[i].FieldId)
                {
                    throw new InvalidOperationException($"Schedule '{schedule.Name}' changed since it was analysed.");
                }
            }

            // All fields get a column definition (hidden ones are needed for sorting); only visible ones are exported.
            var allColumns = fields.Select(f => MakeColumn(f, sheetEditable: true)).ToList();
            var formats = scheduleFields.Select(SafeFormatOptions).ToList();
            var exportedIndexes = Enumerable.Range(0, fields.Count).Where(i => !fields[i].IsHidden).ToList();

            var elements = new FilteredElementCollector(_document, schedule.Id)
                .WhereElementIsNotElementType()
                .ToElements();

            var typeCache = new Dictionary<long, Element?>();
            var rows = new List<(Element Element, Element? Type, ExportCell[] Cells)>(elements.Count);

            foreach (var element in elements)
            {
                var type = GetType(element, typeCache);
                bool inGroup = element.GroupId != ElementId.InvalidElementId;
                var cells = new ExportCell[fields.Count];
                for (int i = 0; i < fields.Count; i++)
                {
                    var cell = ReadCell(element, type, fields[i], allColumns[i], scheduleFields[i], formats[i]);

                    // Model group members: changing an instance value on one member is easy to get wrong
                    // (group edit rules), so V1 keeps them read-only. Type values are unaffected.
                    if (inGroup && allColumns[i].IsEditable && !fields[i].IsTypeParameter
                        && cell.IsAvailable && !cell.ForceReadOnly)
                    {
                        cell = cell with { ForceReadOnly = true, LockReason = LockInGroup };
                    }

                    cells[i] = cell;
                }

                rows.Add((element, type, cells));
            }

            SortLikeSchedule(definition, fields, rows);

            var exportRows = rows.Select(r => new ExportRow
            {
                ElementUniqueId = r.Element.UniqueId,
                ElementId = r.Element.Id.Value,
                TypeUniqueId = r.Type?.UniqueId,
                TypeId = r.Type?.Id.Value,
                CategoryName = r.Element.Category?.Name ?? string.Empty,
                InGroup = r.Element.GroupId != ElementId.InvalidElementId,
                DesignOptionName = SafeDesignOptionName(r.Element),
                Cells = exportedIndexes.Select(i => r.Cells[i]).ToArray()
            }).ToList();

            var columns = LockDuplicateColumns(exportedIndexes.Select(i => allColumns[i]).ToList());
            var notes = new List<string>();
            AddLockNotes(analysis.Schedule.Name, columns, exportRows, notes);
            if (analysis.Schedule.IncludesLinkedFiles)
            {
                notes.Add($"{analysis.Schedule.Name}: rows from linked models are not exported.");
            }

            int blankColumns = columns.Count(c => c.Field.IsFormula || c.Field.IsPercentage || c.Field.IsRelatedElement);
            if (blankColumns > 0)
            {
                notes.Add($"{analysis.Schedule.Name}: {blankColumns} calculated or related-element column(s) " +
                          "cannot be read per element and are exported blank.");
            }

            int hidden = fields.Count - exportedIndexes.Count;
            if (hidden > 0)
            {
                notes.Add($"{analysis.Schedule.Name}: {hidden} hidden field(s) not exported.");
            }

            _logger.Info($"Export sheet '{schedule.Name}': {exportRows.Count} rows, {columns.Count} columns, " +
                         $"{columns.Count(c => c.IsEditable)} editable.");

            return new ExportSheet
            {
                ScheduleUniqueId = schedule.UniqueId,
                ScheduleElementId = schedule.Id.Value,
                ScheduleName = schedule.Name,
                CategoryName = analysis.Schedule.CategoryName,
                Mode = SheetMode.Editable,
                Columns = columns,
                Rows = exportRows,
                Notes = notes
            };
        }

        /// <summary>
        /// Grouped schedules: copy the schedule body text exactly as Revit displays it.
        /// Rows cannot be mapped reliably to elements, so nothing here is ever re-imported.
        /// </summary>
        private ExportSheet BuildPresentation(ViewSchedule schedule, ScheduleAnalysis analysis)
        {
            var body = schedule.GetTableData().GetSectionData(SectionType.Body);
            var rows = new List<IReadOnlyList<string>>();

            for (int r = body.FirstRowNumber; r <= body.LastRowNumber; r++)
            {
                var row = new List<string>();
                for (int c = body.FirstColumnNumber; c <= body.LastColumnNumber; c++)
                {
                    var text = schedule.GetCellText(SectionType.Body, r, c) ?? string.Empty;
                    row.Add(text.Length > ParameterValueReader.MaxCellText ? text[..ParameterValueReader.MaxCellText] : text);
                }

                rows.Add(row);
            }

            _logger.Info($"Presentation sheet '{schedule.Name}': {rows.Count} rows.");

            return new ExportSheet
            {
                ScheduleUniqueId = schedule.UniqueId,
                ScheduleElementId = schedule.Id.Value,
                ScheduleName = schedule.Name,
                CategoryName = analysis.Schedule.CategoryName,
                Mode = SheetMode.Presentation,
                PresentationRows = rows,
                Notes = [$"{schedule.Name}: grouped schedule exported for presentation only (not re-importable)."]
            };
        }

        private ExportCell ReadCell(
            Element element, Element? type, ScheduleFieldInfo field, ExportColumn column,
            ScheduleField scheduleField, FormatOptions? format)
        {
            try
            {
                switch (field.FieldType)
                {
                    case nameof(ScheduleFieldType.Instance):
                    case nameof(ScheduleFieldType.ElementType):
                    case nameof(ScheduleFieldType.ViewBased):
                    {
                        var target = field.IsTypeParameter ? type : element;
                        var parameter = target == null ? null : _parameters.Find(target, field.Parameter);
                        if (parameter == null)
                        {
                            return ExportCell.Unavailable;
                        }

                        var cell = ParameterValueReader.Read(parameter, column, format);

                        // The column was classified on a sample; Revit decides writability per element.
                        return column.IsEditable && parameter.IsReadOnly && !cell.ForceReadOnly
                            ? cell with { ForceReadOnly = true, LockReason = LockElementReadOnly }
                            : cell;
                    }

                    case nameof(ScheduleFieldType.Count):
                        // Itemised schedule: every row is exactly one element.
                        return new ExportCell
                        {
                            Value = 1d, Raw = "1", Normalised = "1", Display = "1", HasValue = true, IsAvailable = true
                        };

                    case nameof(ScheduleFieldType.CombinedParameter):
                        return ReadCombined(element, type, scheduleField);

                    default:
                        // Formula, percentage, related element: no per-element API; exported blank.
                        return ExportCell.Unavailable;
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Could not read field '{field.Heading}' on element {element.Id.Value}.", ex);
                return ExportCell.Unavailable;
            }
        }

        /// <summary>Rebuilds a combined-parameter value from its parts (prefix + value + suffix, separated).</summary>
        private ExportCell ReadCombined(Element element, Element? type, ScheduleField scheduleField)
        {
            var parts = scheduleField.GetCombinedParameters();
            var text = new StringBuilder();

            for (int i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                var identity = _parameters.Identify(part.ParamId);
                var parameter = _parameters.Find(element, identity) ?? (type == null ? null : _parameters.Find(type, identity));
                var value = parameter == null
                    ? string.Empty
                    : parameter.StorageType == StorageType.String
                        ? parameter.AsString() ?? string.Empty
                        : parameter.AsValueString() ?? string.Empty;

                text.Append(part.Prefix).Append(value).Append(part.Suffix);
                if (i < parts.Count - 1)
                {
                    text.Append(part.Separator);
                }
            }

            var combined = text.ToString();
            return ParameterValueReader.Text(combined, combined, combined.Length > 0, combined);
        }

        /// <summary>
        /// The same parameter shown in two columns would give two competing Excel values for one Revit value.
        /// Only the first column stays editable; later ones are locked and point to it.
        /// </summary>
        private static List<ExportColumn> LockDuplicateColumns(List<ExportColumn> columns)
        {
            var firstHeading = new Dictionary<(string Key, bool IsType), string>();
            for (int i = 0; i < columns.Count; i++)
            {
                var column = columns[i];
                if (!column.IsEditable)
                {
                    continue;
                }

                var key = (column.Field.Parameter.Key, column.Field.IsTypeParameter);
                if (firstHeading.TryGetValue(key, out var heading))
                {
                    columns[i] = column with
                    {
                        IsEditable = false,
                        ReadOnlyReason = $"Same parameter as column \"{heading}\": edit it there."
                    };
                }
                else
                {
                    firstHeading[key] = column.Field.Heading;
                }
            }

            return columns;
        }

        /// <summary>Summarises per-cell locks inside editable columns for the export report.</summary>
        private static void AddLockNotes(string scheduleName, IReadOnlyList<ExportColumn> columns, IReadOnlyList<ExportRow> rows, List<string> notes)
        {
            var counts = new Dictionary<string, int>();
            foreach (var row in rows)
            {
                for (int c = 0; c < columns.Count; c++)
                {
                    var reason = row.Cells[c].LockReason;
                    if (columns[c].IsEditable && reason != null)
                    {
                        counts[reason] = counts.TryGetValue(reason, out var n) ? n + 1 : 1;
                    }
                }
            }

            foreach (var (reason, count) in counts)
            {
                notes.Add($"{scheduleName}: {count} cell(s) locked in editable columns ({reason}).");
            }

            int duplicates = columns.Count(c => c.ReadOnlyReason?.StartsWith("Same parameter as column", StringComparison.Ordinal) == true);
            if (duplicates > 0)
            {
                notes.Add($"{scheduleName}: {duplicates} duplicate parameter column(s) locked; edit the first occurrence.");
            }

            int optionRows = rows.Count(r => r.DesignOptionName != null);
            if (optionRows > 0)
            {
                notes.Add($"{scheduleName}: {optionRows} row(s) belong to a design option.");
            }
        }

        private static string? SafeDesignOptionName(Element element)
        {
            try
            {
                return element.DesignOption?.Name;
            }
            catch (Exception)
            {
                return null;
            }
        }

        internal static ExportColumn MakeColumn(ScheduleFieldInfo field, bool sheetEditable)
        {
            var kind = field.Storage switch
            {
                ValueStorage.String => CellKind.Text,
                ValueStorage.Integer when field.IsYesNo => CellKind.YesNo,
                ValueStorage.Integer when field.SpecTypeId == SpecTypeId.Int.Integer.TypeId => CellKind.Integer,
                ValueStorage.Integer => CellKind.Text,
                ValueStorage.Double => CellKind.Number,
                ValueStorage.ElementId => CellKind.Reference,
                _ when field.IsCount => CellKind.Integer,
                _ => CellKind.Text
            };

            bool editable = sheetEditable && field.IsEditable;
            string? reason = field.IsEditable ? null : field.Reason;

            // Integers shown as text (e.g. enumerated built-ins) cannot be typed back safely yet.
            if (editable && field.Storage == ValueStorage.Integer && kind == CellKind.Text)
            {
                editable = false;
                reason = "Enumerated value: read-only in V1.";
            }

            string unitId = string.Empty, unitLabel = string.Empty, unitSymbol = string.Empty;
            string? numberFormat = kind == CellKind.Integer ? "0" : null;

            if (kind == CellKind.Number && field.IsMeasurable && !string.IsNullOrEmpty(field.DisplayUnitTypeId))
            {
                bool replaced = CompositeUnitReplacements.TryGetValue(field.DisplayUnitTypeId, out var decimalUnit);
                var unit = replaced ? decimalUnit! : new ForgeTypeId(field.DisplayUnitTypeId);
                unitId = unit.TypeId;
                unitLabel = SafeUnitLabel(unit);
                unitSymbol = SafeUnitSymbol(unit);
                numberFormat = DecimalFormat(replaced ? 4 : DecimalsFor(field.DisplayAccuracy));
            }

            return new ExportColumn
            {
                Field = field,
                Kind = kind,
                IsEditable = editable,
                ReadOnlyReason = reason,
                ExportUnitTypeId = unitId,
                ExportUnitLabel = unitLabel,
                ExportUnitSymbol = unitSymbol,
                NumberFormat = numberFormat
            };
        }

        private static void SortLikeSchedule(
            ScheduleDefinition definition,
            IReadOnlyList<ScheduleFieldInfo> fields,
            List<(Element Element, Element? Type, ExportCell[] Cells)> rows)
        {
            var keys = new List<(int FieldIndex, bool Descending)>();
            for (int i = 0; i < definition.GetSortGroupFieldCount(); i++)
            {
                var sortField = definition.GetSortGroupField(i);
                int index = fields.ToList().FindIndex(f => f.FieldId == sortField.FieldId.IntegerValue);
                if (index >= 0)
                {
                    keys.Add((index, sortField.SortOrder == ScheduleSortOrder.Descending));
                }
            }

            rows.Sort((a, b) =>
            {
                foreach (var (fieldIndex, descending) in keys)
                {
                    int result = CompareCells(a.Cells[fieldIndex], b.Cells[fieldIndex]);
                    if (result != 0)
                    {
                        return descending ? -result : result;
                    }
                }

                return a.Element.Id.Value.CompareTo(b.Element.Id.Value);
            });
        }

        private static int CompareCells(ExportCell a, ExportCell b)
        {
            if (a.Value is double x && b.Value is double y)
            {
                return x.CompareTo(y);
            }

            return NaturalCompare(a.Value?.ToString() ?? string.Empty, b.Value?.ToString() ?? string.Empty);
        }

        /// <summary>Case-insensitive compare that orders embedded numbers numerically ("D2" before "D10").</summary>
        private static int NaturalCompare(string a, string b)
        {
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                {
                    int si = i, sj = j;
                    while (i < a.Length && char.IsDigit(a[i])) i++;
                    while (j < b.Length && char.IsDigit(b[j])) j++;
                    var na = a[si..i].TrimStart('0');
                    var nb = b[sj..j].TrimStart('0');
                    int byLength = na.Length.CompareTo(nb.Length);
                    if (byLength != 0) return byLength;
                    int byDigits = string.CompareOrdinal(na, nb);
                    if (byDigits != 0) return byDigits;
                }
                else
                {
                    int c = string.Compare(a[i].ToString(), b[j].ToString(), CultureInfo.CurrentCulture, CompareOptions.IgnoreCase);
                    if (c != 0) return c;
                    i++;
                    j++;
                }
            }

            return (a.Length - i).CompareTo(b.Length - j);
        }

        private Element? GetType(Element element, Dictionary<long, Element?> cache)
        {
            var typeId = element.GetTypeId();
            if (typeId == ElementId.InvalidElementId)
            {
                return null;
            }

            if (!cache.TryGetValue(typeId.Value, out var type))
            {
                type = _document.GetElement(typeId);
                cache[typeId.Value] = type;
            }

            return type;
        }

        private static FormatOptions? SafeFormatOptions(ScheduleField field)
        {
            try
            {
                return field.GetFormatOptions();
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static int DecimalsFor(double? accuracy)
        {
            if (accuracy is not > 0)
            {
                return 2;
            }

            return Math.Min(Math.Max((int)Math.Ceiling(-Math.Log10(accuracy.Value) - 1e-9), 0), 6);
        }

        private static string DecimalFormat(int decimals) => decimals == 0 ? "0" : "0." + new string('0', decimals);

        /// <summary>The unit's first symbol ("mm"), or empty when it has none.</summary>
        private static string SafeUnitSymbol(ForgeTypeId unit)
        {
            try
            {
                foreach (var symbol in FormatOptions.GetValidSymbols(unit))
                {
                    if (!symbol.Empty())
                    {
                        return LabelUtils.GetLabelForSymbol(symbol);
                    }
                }
            }
            catch (Exception)
            {
                // Units without symbols: the label is used instead.
            }

            return string.Empty;
        }

        private static string SafeUnitLabel(ForgeTypeId unit)
        {
            try
            {
                return LabelUtils.GetLabelForUnit(unit);
            }
            catch (Exception)
            {
                return unit.TypeId;
            }
        }
    }
}
