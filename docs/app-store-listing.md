# Autodesk App Store listing — draft for confirmation

Everything below was decided on your behalf and needs your confirmation before submission.
Items marked **[YOU]** must come from you (publisher identity, contact, legal).

## Package

| Item | Value |
|---|---|
| Upload file | `artifacts/ScheduleSyncForRevit-1.0.0.bundle.zip` (built by `packaging/build-package.ps1`) |
| Format | Autodesk bundle (`PackageContents.xml`), Revit 2025 and 2026, Win64 |
| Version | 1.0.0 |
| Install location (by the store installer) | `%ProgramData%\Autodesk\ApplicationPlugins\ScheduleSyncForRevit.bundle` |
| UpgradeCode | `{FBB76A76-F19C-4D34-A639-12D06EF166CE}` — never change it; future versions upgrade this one |
| Publisher fields in `PackageContents.xml` | **[YOU]** Author / company name, support URL, support email (currently placeholders; rebuild after filling) |

## Listing

**App name:** Schedule Sync for Revit
*(Autodesk naming rules: "Revit" may appear only as "… for Revit".)*

**Price:** Free *(V1 has no licensing; can be changed for a later version).*

**Supported products:** Autodesk Revit 2025, Autodesk Revit 2026 · Windows 64-bit · English

**Category (suggested):** Productivity; Data exchange / Import-Export *(pick the closest options the form offers).*

**Short description (≤ 200 characters):**
> Export Revit schedules to Excel, edit values, and bring changes back safely. Every change is compared, reviewed and approved before it touches the model.

**Description:**
> Schedule Sync for Revit turns Excel into a safe editing surface for your Revit schedules.
>
> Export one or many schedules to a single .xlsx workbook — no Microsoft Excel installation needed. Editable values, type values and read-only values are clearly marked, read-only cells can be locked, and units follow your project settings.
>
> When the workbook comes back, nothing is written straight away. Schedule Sync checks that the workbook belongs to the open project, finds every element by its unique id and every value by its real parameter, and compares three versions of each value: what was exported, what is in Revit now, and what is in Excel. Changes made in Revit since the export are never overwritten silently; conflicts and type-parameter changes (with the number of affected instances) wait for your decision.
>
> Review changes per schedule, approve them, and apply them in a single step that one Undo reverses. A report lists what was applied, skipped or refused, and is kept in the import history.

**Key features:**
- Export multiple schedules to one Excel workbook (Excel not required)
- Clear cell colours: editable instance, editable type, read-only; optional cell locking
- Unit-aware numbers, Yes/No drop-downs, named references (Level, Phase …)
- Wrong-project protection
- Three-way comparison with conflict detection
- Type-parameter changes require approval and show affected instances
- Review per schedule, bulk decisions, single-step Undo
- Import report (CSV) and history
- Grouped schedules exported as a read-only presentation copy
- Works fully offline; no data leaves your computer

**What's new (1.0.0):** Initial release.

**Keywords:** schedule, Excel, export, import, parameters, data, round-trip, sync, xlsx, BIM data

**Known limitations (listed on the page):**
- Formula, percentage and related-element columns (e.g. Room: Name) are exported blank.
- Hidden fields and rows from linked models are not exported.
- Instance values of model-group members are read-only.
- Grouped (non-itemised) schedules: presentation export only.
- Revit 2025 and 2026 only.

**Help / documentation:** included in the package (`Contents/Resources/help.html`); the same text can be pasted into the store's documentation field.

**Privacy statement:**
> Schedule Sync for Revit does not collect, transmit or store data outside your computer. It reads and writes only the Revit model you open and the Excel files you choose. Diagnostic logs (30 days) and import reports are stored locally in %LOCALAPPDATA%\RevitScheduleSync.

**Installation / uninstallation text:**
> Install with the Autodesk App Store installer and restart Revit. The command appears under Add-Ins › Schedule Sync. Uninstall from Windows "Apps & features" (or remove the ScheduleSyncForRevit.bundle folder from %ProgramData%\Autodesk\ApplicationPlugins).

## Media

| Asset | Status |
|---|---|
| App icon | `packaging/Resources/icon-512.png` (also 256/128/64/32/16) |
| Screenshots (at least 3 recommended) | **To capture in Revit** on a demo model without client data: 1) Export — schedule browser, 2) Analyse fields, 3) Excel workbook with colours, 4) Import review with tabs, 5) Import report |
| Video | Optional, not prepared |

## Needed from you before submitting **[YOU]**

1. Publisher / company name as it should appear.
2. Support email and support URL (website, or a public page for the app).
3. Confirmation of name, price (Free), description, categories.
4. Accepting Autodesk's publisher agreement on the form — you tick that yourself.
5. A demo model (no client data) for the screenshots, or permission to use the current test model.
