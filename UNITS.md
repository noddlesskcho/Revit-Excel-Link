# Units in Revit-Excel Link

How numeric values cross the Revit ↔ Excel boundary, and the vocabulary used in `// {unit}` code comments.

## Vocabulary

| Tag | Meaning |
|---|---|
| `{internal}` | Revit internal units for the parameter's spec: `{ft}`, `{ft2}`, `{ft3}`, `{rad}`, or the spec's own internal unit (flow, force, ...) |
| `{ft}` `{ft2}` `{ft3}` `{rad}` | Revit internal length, area, volume, angle |
| `{mm}` `{m}` `{m2}` `{m3}` `{deg}` | Display units |
| `{xl:exportUnit}` | Number stored in an Excel cell, in the column's `ExportUnitTypeId` |
| `{str:display}` | Formatted display text (`AsValueString`, `GetCellText`): never parsed back as a number for calculation |
| `{1}` | Dimensionless (integers, Yes/No, line weight 1–16) |
| `{count}` | Counts (family instance counts) |

## The rule

Every Double column carries an `ExportUnitTypeId` in the hidden metadata sheet (`_RevitSyncMetadata`, columns section).

- Export: `AsDouble() {internal}` → `UnitUtils.ConvertFromInternalUnits(v, ExportUnitTypeId)` → cell `{xl:exportUnit}`
  (`ParameterValueReader.Read`).
- Import: cell `{xl:exportUnit}` → `UnitUtils.ConvertToInternalUnits(v, ExportUnitTypeId)` → `Parameter.Set(double) {internal}`
  (`ExcelValueParser.ParseNumber`, `ChangeApplier`).
- Text with units in a number cell (`3' 6"`, `900 mm`) → `UnitFormatUtils.TryParse` with project units `{internal}` → converted to
  `{xl:exportUnit}` for comparison, then back to `{internal}`.
- The current Revit value at import is read again with the same `ExportUnitTypeId`, so the three-way comparison
  (export / model / Excel) compares like with like. Numbers compare with a relative tolerance of 1e-9 (`ValueComparer`).
- An empty `ExportUnitTypeId` means "no conversion": the cell holds the internal value. Correct for non-measurable
  specs (plain Number); see the audit findings for the cases where a measurable value ends up here.
- Excel number formats (decimals from the display accuracy) only round what is shown; cells keep full precision.

No code converts with literals (`304.8`, `0.3048`, `1000`, `Math.PI`, `180`); every conversion goes through `UnitUtils`.
No geometry (XYZ, locations, coordinates) is exported.

## Export unit per column

| Source | ExportUnitTypeId |
|---|---|
| Schedule field, measurable | Field format override if set, else project `Units.GetFormatOptions(spec)` (`ScheduleAnalyser.GetUnits`) |
| Model data parameter, measurable | Project `Units.GetFormatOptions(spec)`; spec from a sample parameter on the exported elements, else from the catalog (`ParameterChoice.SpecTypeId`). A number whose spec or unit cannot be read is exported unconverted and its column is read-only ("Unit unknown") (`ModelSheetBuilder.ParameterColumn`) |
| Composite display units | Replaced by a decimal unit (`ScheduleExportBuilder.CompositeUnitReplacements`): Feet-fractional-inches → Feet, Fractional inches → Inches, Metres-centimetres → Metres, Degrees-minutes → Degrees, Stationing → Feet / US survey feet / Metres. Excel shows 4 decimals |
| Integer, Yes/No, Text, Reference | none |
| Object styles / line styles | none: line weight `{1}`, colour `R,G,B` text, pattern and material names |
| Families sheet | none: names and `{count}` |
| Info columns (Element ID, Family, Type, Phase, Category) | none: text, never imported (Phase is resolved by name) |

The header text shows the unit: `Length [mm]` (symbol when the unit has one, else its label), also stored as the column heading in the metadata so moved columns are still found. Workbooks from 1.2.2 and earlier have headings without the unit and still import. The header comment and the validation message also name the unit.

## Files with unit boundaries

- `src/RevitScheduleSync/Revit/Parameters/ParameterValueReader.cs`: Revit → Excel conversion
- `src/RevitScheduleSync/Revit/Parameters/ExcelValueParser.cs`: Excel → Revit conversion
- `src/RevitScheduleSync/Revit/Transactions/ChangeApplier.cs`: `Parameter.Set(double)`
- `src/RevitScheduleSync/Revit/Schedules/ScheduleAnalyser.cs`: schedule field units
- `src/RevitScheduleSync/Revit/Schedules/ScheduleExportBuilder.cs`: export unit, composite replacements, number formats
- `src/RevitScheduleSync/Revit/ModelData/ModelSheetBuilder.cs`: model data units
- `src/RevitScheduleSync/Revit/Styles/StyleAccess.cs`: style properties (no units)
- `src/RevitScheduleSync/Excel/ScheduleSheetWriter.cs`: unit label and number validation
- `src/RevitScheduleSync/Sync/ValueComparer.cs`: numeric tolerance

## Audit 2026-10-29: open findings

1. ~~**High**: model data column with no sample parameter gets no export unit~~. Fixed: catalog spec fallback, unit-less numbers locked.
2. ~~**Medium**: unit only in the header comment~~. Fixed: unit in the header text (`ExportColumn.HeaderText`).
3. ~~**Medium**: text-cell numbers use CurrentCulture then InvariantCulture~~. Fixed: `Sync/NumberText` refuses "1.234" / "12,345" (could be thousands or decimals) with a clear message, reads a decimal comma only where the locale uses one, and everything else the invariant way.

## Re-audit 2026-10-29 (after commits b96b67e, c21d96a)

No new findings. Changes since the first audit touch no numeric values (workbook read into memory, Phase cells locked,
assembly resolver, ribbon fallback, "Family: Type" reference names); the model-data unit fallback (finding 1) is in place.

Checked and correct:
- Project units changed between export and import (e.g. mm to m): import converts with the `ExportUnitTypeId` stored
  in the workbook, and reads the current Revit value with the same unit, so the old export unit is honoured.
- Schedule field with its own format (`UseDefault = false`): display text and export unit both come from the field's
  format.
- Excel keeps 15 significant digits: a value exported at full precision (e.g. 2999.9999999999995 mm) and re-saved by
  Excel as 3000 differs by about 2e-16 relative, inside `ValueComparer`'s 1e-9 tolerance, so it is not a change.
- `ValueComparer` parses stored normalised numbers with InvariantCulture only, so the comparison does not depend on
  the Windows locale.
- New rooms/spaces: entered values are parsed with the column's export unit, like edits to existing rows.
- Combined-parameter schedule fields (`ScheduleExportBuilder.ReadCombined`) export display text
  (`AsValueString` with project units, not the field's own format). The column is read-only and the text is never
  parsed, so this affects presentation only.

Findings 2 and 3 fixed after this re-audit; covered by `NumberTextTests` and `WorkbookRoundTripTests.Header_shows_the_unit_and_plain_headers_have_none` (58/58 pass).
