# Revit-Excel Link — V1 checklist

Status of the V1 definition of done (spec section 32) and the test cases (section 31).

- **Automated**: covered by `tests/RevitScheduleSync.Tests` (no Revit needed). Run:
  `dotnet test tests\RevitScheduleSync.Tests\RevitScheduleSync.Tests.csproj -c Release`
- **Manual**: needs Revit 2026.5. Procedures were given at the end of each phase.
- **Confirmed** means the user reported the manual test as passing.

## Definition of done (section 32)

| Requirement | How it is met | Verified |
|---|---|---|
| Plugin loads in Revit 2026.5 | Isolated add-in context (`UseRevitContext=False`), ribbon button | Confirmed (Phase 0) |
| Plugin loads in Revit 2025 (added after V1 scope) | .NET 8 build + loader with private load context; isolation verified outside Revit with Dynamo's OpenXml 2.12.3 preloaded | **Test in Revit 2025** |
| Select multiple itemised schedules | Schedule browser with search, select all/clear | Confirmed (Phase 1) |
| Export to .xlsx | ClosedXML writer, temp file + replace | Automated; manual (Phase 3) |
| Excel not required | ClosedXML only, no Office Interop/COM | By design |
| Row identity by `Element.UniqueId` | Hidden locked column A + `[Rows]` metadata | Automated (sorting, deleted/added/copied rows) |
| Parameter identity preserved | Built-in name / shared GUID / project parameter; never display names | Automated (metadata round-trip); manual (Phase 2) |
| Instance parameters round-trip | Export → review → apply | **Re-test**: Apply failed at its confirmation dialog until commit 2a4d827 (2026-10-01) |
| Supported type parameters round-trip | Type change approval with affected instance count | **Re-test** (same dialog fix) |
| Units handled correctly | `ConvertFromInternalUnits` on export, `ConvertToInternalUnits` / `UnitFormatUtils.TryParse` on import, tolerance compare | Automated (tolerance); manual (Phase 3 step 6) |
| Read-only parameters cannot be written | Locked cells; analyser + applier re-check `IsReadOnly`; family/type identity protected | Automated (locking); manual |
| Changes analysed before import | Import analysis without any transaction | Manual (Phase 5) |
| Three-way comparison | `ValueComparer.Classify` | Automated (all five cases, spec example) |
| Conflicts shown | Conflict status, decisions, "rows disagree" conflicts | Automated (logic); manual (Phase 6) |
| Review before Apply | Review screen + confirmation dialog | Manual (Phase 6/7) |
| Only approved changes written | `ChangeItem.WillWrite`; undecided items are never written | Manual (Phase 7) |
| Useful import report | Report screen, CSV export, History tab | Automated (CSV, history); manual (Phase 7) |
| Grouped schedules never produce unsafe updates | Presentation export only; ignored on import | Automated (presentation sheet ignored) |
| No crash on common input errors | Command guard, per-command error handling, friendly messages | Automated (bad/foreign/newer/locked workbooks) |

## Test cases (section 31)

| # | Case | Coverage |
|---|---|---|
| 1 | Export and import without changes | Manual: 0 changes expected |
| 2 | Change one text instance parameter | Automated (three-way); manual (Phase 5 step 2, Phase 7 step 2) |
| 3 | Change ten parameters | Manual (Phase 5 step 3) |
| 4 | Change numeric length | Automated (tolerance); manual (Phase 5 step 4) |
| 5 | Change Yes/No | Manual (Phase 5 step 5, invalid value); valid Yes/No change manual |
| 6 | Blank to value | Automated (three-way); manual (Phase 5 step 6) |
| 7 | Value to blank | Automated (three-way); manual (Phase 5 step 6) |
| 8 | Modify read-only Excel field | Automated (cells locked); manual (Phase 5 step 7) |
| 9 | Delete element after export | Manual (Phase 5 step 8) |
| 10 | Rename schedule after export | Manual (Phase 5 step 9) |
| 11 | Change Revit parameter after export | Automated (RevitChange); manual (Phase 5 step 10) |
| 12 | Same Excel parameter changed differently | Manual (Phase 5 step 11, "rows disagree") |
| 13 | Three-way conflict | Automated (D101 / D101A / D101B); manual (Phase 5 step 12, Phase 6) |
| 14 | Modify type parameter | Manual (Phase 6 step 3–4, Phase 7 step 2) |
| 15 | Conflicting type values | Manual (Phase 5 step 11) |
| 16 | Import into wrong project | Automated (fingerprint rules); manual (Phase 5 step 13) |
| 17 | Corrupt workbook metadata | Automated (damaged, newer version, not a Revit-Excel Link workbook) |
| 18 | Grouped schedule | Automated (presentation sheet ignored); manual (Phase 3 step 5) |
| 19 | Very large schedule | Automated: 20,000 rows × 11 columns, write ≈ 5 s, read ≈ 5 s (Release, excluding Revit reading). **Revit-side timing still to measure** on a real large model |
| 20 | Workshared model | **Still to test** in a workshared model (ownership → Blocked / Failed, others applied) |

## Known limitations (V1)

- Formula, percentage and related-element columns (e.g. Room: Name) are exported blank: the Revit API does not link schedule rows to elements.
- Hidden schedule fields are not exported.
- Rows from linked models are not exported.
- Instance values of model-group members are locked.
- Enumerated integer parameters are read-only.
- Clearing a number, Yes/No or reference value is not supported (Revit API limitation); blank is accepted only when it was blank at export.
- Protected sheets can be filtered but not sorted (Excel rule); switch off "Lock read-only cells" to sort.
- Grouped schedules: presentation export only.
