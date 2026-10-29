# Review ledger: Revit-Excel Link

Target: `C:\Users\ISS\Documents\Codex\2026-09-09\0000\work\Revit-Excel-Link`
Scope: repository, excluding `bin/`, `obj/`, `artifacts/` and third-party code.
Reviewers: `revit-units-audit`, `revit-excel-sharp-edges` (plus carried-over `/code-review` items marked CR).
Max rounds: 5. Mode: report first, fix only after approval.

| id | severity | file:line | summary | trigger/evidence | status | round found | round closed | notes |
|---|---|---|---|---|---|---|---|---|
| RL-01 | major | src/RevitScheduleSync/Excel/ClosedXmlWorkbookReader.cs:92 | Workbook `FileStream` is never disposed, so the .xlsx stays locked after import analysis | Harness on the shipped Revit 2024 ClosedXML: after `XLWorkbook.Dispose()` the stream is still open (`CanRead=True`); exclusive open and rename both fail with "being used by another process". Excel saves by rename, so the user cannot save the workbook after analysing it, until .NET garbage-collects the handle | fixed | 1 | 2 | Workbook copied into a MemoryStream and the file closed at once (`ClosedXmlWorkbookReader.Open`). Evidence: same harness with the fix, file renamed while the workbook was loaded: OK. Builds and 43/43 tests pass |
| RL-02 | major | src/RevitScheduleSync/Revit/ModelData/ModelSheetBuilder.cs:279 | Rooms/Spaces sheets: the Phase column is editable on existing rows, but import ignores it | Phase info column is `IsEditable = true` (needed for new rows), and existing-row Phase cells are not locked. `ImportAnalyser.cs:154` skips Info columns for existing rows, so a user who changes Room 101's phase from "New Construction" to "Existing" sees no error, no review entry, and no change | fixed | 1 | 2 | Existing-row Phase cells are ForceReadOnly with reason "the phase of an existing room or space cannot be changed" (`ModelSheetBuilder.InfoCell`); new-row area stays editable. Evidence: build. Unverified in Revit |
| RL-03 | major (CR) | src/RevitScheduleSync/Compatibility/AssemblyResolver.cs:23 | Revit 2024: AppDomain-wide resolver answers any failed load whose name matches a DLL in our folder, ignoring the requested version | Another add-in asking for System.Text.Json 9.0 gets our 8.0.0.5 and fails with MissingMethodException | fixed | 1 | 2 | Resolver answers requests from assemblies in our folder, otherwise only when our copy is not older than the requested version (`AssemblyResolver.Resolve`). Evidence: Revit 2024 build. Unverified in Revit |
| RL-04 | major (CR) | src/RevitScheduleSync/App/RibbonBuilder.cs:17 | `CreateRibbonTab` errors other than ArgumentException (Revit's custom-tab limit) fail startup | Too many add-in tabs: InvalidOperationException, OnStartup returns Failed, no buttons at all | fixed | 1 | 2 | `TryCreateTab`: any other failure logs a warning and puts both buttons in a "Revit-Excel Link" panel on the Add-Ins tab. Evidence: build. Unverified in Revit |
| RL-05 | major | src/RevitScheduleSync/Revit/ModelData/ModelSheetBuilder.cs:342 | Model-data number column could lose its unit and export raw feet | Units audit finding 1: no sample parameter in the first 500 elements | fixed | 1 | 1 | Catalog spec fallback (`ParameterChoice.SpecTypeId`); unit-less numbers locked. Evidence: 2024–2027 builds clean, 43/43 tests. Unverified in Revit |
| RL-06 | minor | src/RevitScheduleSync/Excel/ScheduleSheetWriter.cs:60 | Unit only in the header comment; composite units become decimal (3' 6" shows 3.5) | Units audit finding 2 | fixed | 1 | 4 | Header text is "Heading [unit]" (`ExportColumn.HeaderText`, symbol via `FormatOptions.GetValidSymbols`), written to both the sheet and the metadata heading so heading matching still works; old workbooks keep their stored heading. Evidence: new test Header_shows_the_unit_and_plain_headers_have_none, existing Inserted_column_is_found_by_heading passes with the unit header; 58/58 tests, 2024-2027 builds clean. Unit symbol unverified in Revit (tests use the label) |
| RL-07 | minor | src/RevitScheduleSync/Revit/Parameters/ExcelValueParser.cs:161 | Numbers typed as text: CurrentCulture then InvariantCulture | Units audit finding 3: de-DE "1.234" meant 1234 is read as 1.234 | fixed | 1 | 4 | `Sync/NumberText`: "1.234", "12,345", "1.234.567" refused as ambiguous with a message; decimal comma only in comma locales; otherwise invariant. Evidence: NumberTextTests (en-US and de-DE), 58/58 tests |
| RL-08 | minor | src/RevitScheduleSync/Revit/ModelData/ModelSheetBuilder.cs:304 | Linked-model rows read project parameters by `LookupParameter(name)` | Two parameters with the same name in the link: the first match is shown | open | 1 | | Linked rows are read-only, so display only |
| RL-09 | minor (CR) | packaging/installer.iss:63 | Setup refuses whenever any Revit.exe process exists, with no override | Seen on this PC: hidden PID 38080 blocked the install | open | 1 | | |
| RL-10 | minor (CR) | src/RevitScheduleSync/RevitScheduleSync.csproj:28 | CI build stamp is UTC without a time-zone marker | 1.2.2 release shows 02:28 for a 10:28 local build | open | 1 | | |
| RL-11 | minor (CR) | src/Directory.Build.props:9 | Debug solution build deploys the Revit 2025 loader without its core | Developer-only | open | 1 | | |
| RL-12 | minor | src/RevitScheduleSync/Revit/Parameters/ReferenceResolver.cs:150 | Type references are matched by type name only, so common type names shared by several families cannot be set | A Generic Model "Family Type" parameter: types named "Standard" in two families are excluded from the drop-down (`CandidateNames`) and rejected on import as "2 elements named "Standard" match". Loud, but the value cannot be edited from Excel at all | fixed | 3 | 3 | Types are indexed by plain name and by "Family: Type" (`ReferenceResolver.Index`); the drop-down offers the plain name when unique, else "Family: Type"; "Family:Type" / "Family : Type" typed by hand are accepted; the ambiguity message suggests the qualified form. Traced: A:Standard, B:Standard, C:Wide offer "A: Standard", "B: Standard", "Wide"; "Standard" is rejected as ambiguous. Evidence: 2024-2027 builds clean, 43/43 tests. Unverified in Revit |
| RL-13 | minor | src/RevitScheduleSync/Revit/Parameters/ExcelValueParser.cs:52 | Dates or numbers pasted into a text column (paste keeps the source format) are imported as serial digits | Paste "2026-10-29" from another workbook into a Comments cell: the cell becomes a date number, `ParseText` turns it into "46324" and writes that to the text parameter. The review list shows "46324", so it is visible before Apply | rejected | 3 | 3 | Date case does not occur: harness on the shipped ClosedXML, a number formatted yyyy-mm-dd reads back as `XLDataType.DateTime` and the reader already uses the displayed text ("2026-10-29", `ClosedXmlWorkbookReader.ReadCell`). Remaining case: a formatted number pasted into a text column is stored as its plain value (1,234.50 becomes "1234.5"), shown in review; not changed |

## Round 2

Re-reviewed the four fixes and their callers. No new findings:
- RL-01: a failed read still reports the same errors (IOException, UnauthorizedAccess, damaged file); the copy uses FileShare.Delete so Excel can also replace the file during the copy. Memory use is one extra copy of the .xlsx while it is analysed.
- RL-02: new rows are read from the unlocked new-row area, not from InfoCell, so they keep an editable Phase.
- RL-03: ClosedXML's own dependency chain is requested by assemblies in our folder, so it still resolves; `AssemblyName.GetAssemblyName` reads metadata only (no load, no recursion).
- RL-04: Revit's own "tab exists" case still reuses the tab.

Open critical/major: 0. Converged after 2 rounds.

## Round 3 (standalone sharp-edges pass, 2026-10-29)

Probed beyond rounds 1-2: reference resolution (levels, phases, materials, scope boxes, view templates, family types), worksharing checkout and update status before writing (`ImportAnalyser.cs:445`), model-group members locked at export, warnings deleted but listed in the report (`ImportFailuresPreprocessor`), profile JSON save/load errors reported through the command error handler, new-row limits, text columns formatted as text ("@"), style colour and line-weight validation, update check behind a proxy. New: RL-12, RL-13 (minor). Open critical/major: 0.

## Probed in round 1 (no finding)

- Transactions: one Transaction with a SubTransaction per value, rollback on error, `IFailuresPreprocessor`, retry without failing elements (`ChangeApplier.cs:116`). `Parameter.Set` return value checked.
- Parameter access: `ParameterLocator` uses BuiltInParameter, shared GUID or parameter element id; StorageType checked before every getter and setter; `IsReadOnly` checked again at apply time; value re-read and compared before writing.
- Duplicate rows and conflicting type values: detected (`ClosedXmlWorkbookReader.cs:171`, `ImportAnalyser.cs:345`).
- Moved, renamed or deleted columns: matched by heading, reported (`ClosedXmlWorkbookReader.cs:232`).
- Empty, formula and error cells: blank/number/text/boolean handled; errors become "Not a valid number". Booleans read as TRUE/FALSE independent of locale.
- Export to a file open in Excel: detected, clear message (`ClosedXmlWorkbookWriter.cs:97`).
- Threading: windows are modal inside `IExternalCommand.Execute`; the update check runs on a background task and never calls the Revit API.
- Obsolete APIs: no `ElementId.IntegerValue` (only `ScheduleFieldId.IntegerValue`), no `DisplayUnitType`.
- Units: see `UNITS.md`.
