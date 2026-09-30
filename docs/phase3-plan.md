# Phase 3 — Excel export: design notes (not yet implemented)

Phases 0–2 were tested in Revit and passed. Phase 3 design is below; no Phase 3 code exists yet.

## Revit API members checked in RevitAPI.xml (2026)
- `Document.CreationGUID` (2024+), `Document.WorksharingCentralGUID` (throws unless the central model is on Revit Server — wrap it),
  `Document.GetWorksharingCentralModelPath()`, `ModelPathUtils.ConvertModelPathToUserVisiblePath`,
  `Document.IsModelInCloud`, `Document.GetCloudModelPath()` + `ModelPath.GetProjectGUID()/GetModelGUID()`, `Document.ProjectInformation`
- `Parameter.HasValue`, `Parameter.AsValueString()` and `AsValueString(FormatOptions)` (use the field's format options when it overrides units)
- `ScheduleDefinition.GetSortGroupFieldCount()/GetSortGroupField(i)` → `FieldId`, `SortOrder`
- `TableSectionData.First/LastRowNumber`, `First/LastColumnNumber`, `GetCellText(row, col)`
- `TableCellCombinedParameterData.ParamId/Prefix/Suffix/Separator`
- Not confirmed: `ViewSchedule.GetCellText(SectionType, row, col)` (the docs mention it but the member was not found; check before use)

## Decisions
- **Document fingerprint:** store CreationGUID, ProjectInformation UniqueId, project number and name, title, workshared flag,
  the central model *file name only* (never the directory), the central GUID and the cloud project/model GUIDs. The compare rules come in Phase 5.
- **Itemised sheets:** one row per element, from `FilteredElementCollector(doc, schedule.Id)`.
  - Rows are sorted by the schedule's sort fields.
  - Visible fields only.
  - Column A is hidden and locked and holds Element.UniqueId, so the file still imports correctly after users filter or sort it.
  - Rows 1–2 hold the title and legend. Row 4 is the header, and data starts at row 5.
- **Grouped sheets:** a read-only text copy of the schedule body.
  - Banner: "Presentation Export — Re-import not supported".
  - No row identity is stored.
- **Values:**
  - Measured values are exported with `UnitUtils.ConvertFromInternalUnits` into the displayed unit, at full precision. The number format follows the column's accuracy.
  - Units that mix feet and inches, or degrees-minutes-seconds, are exported as decimal feet, inches or degrees.
  - Yes/No is exported as "Yes"/"No", with a list validation on the column.
  - ElementId values are exported as their display text; import will look up the element by that text.
  - Integers that are not plain Integer spec are locked (enumerated values).
- **Calculated fields:** Count is 1 per row; combined parameters are built from their parts; formula, percentage and related-element fields are left blank with a header comment. There is no API that links a schedule row to its element.
- **Metadata:** one very-hidden sheet `_RevitSyncMetadata` with sections `[Workbook]`, `[Sheets]`, `[Columns]`, `[Rows]` and `[Values]`.
  - `[Values]` holds raw, normalised and display values for editable cells.
  - An option shows the sheet for debugging.
  - Schema version 1.
- **Protection:** sheets are protected without a password, and editable cells are unlocked. Users can filter but not sort; this can be switched off with an option.
- **Saving:** write to a temporary file, then move it over the target. If the target is open in Excel, show a clear message.
- **Code structure:**
  - `Services/IScheduleOperations` (Scan/Analyse/Export), implemented on the Revit side, so the view models stay Revit-free.
  - `Excel/IWorkbookWriter` + `ClosedXmlWorkbookWriter`.
  - `Revit/Documents/DocumentFingerprintReader`.
  - `Revit/Parameters/ParameterValueReader`.
  - A step 3 result screen.
