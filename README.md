# Revit-Excel Link

Repository and assembly name: `RevitScheduleSync`.

Revit 2025 / 2026 add-in for safe, auditable schedule ↔ Excel synchronisation: export schedules to .xlsx,
edit values in Excel, then analyse, review and apply the changes back to the model.

Status: **V1 (1.0.0)** — export, import analysis (three-way comparison), review, apply (one Undo step),
report and history. See [docs/V1-checklist.md](docs/V1-checklist.md).

### Model data (in development, branch `feature/model-data-link`)

The **Model data** section exports model data that is not in a schedule. Every option is available to every
user; nothing is licence-gated. Re-import uses the same **Import** section and review as schedules.

| Source | Rows | Import |
|---|---|---|
| Model categories / Annotation categories | Elements of the chosen categories (whole model or active view), one sheet per category | Instance and type parameters |
| Current selection | Elements selected in Revit when the window opened, one sheet per category | Instance and type parameters |
| One row per type (option) | Element types of the chosen categories | Type parameters, per type |
| Rooms / Spaces | Rooms or MEP spaces; optional empty template | Updates existing rows; **rows added in Excel create unplaced rooms or spaces** (Number, Name and Phase required) |
| Project information | The project information element | Its parameters |
| Object styles / Line styles | Categories and subcategories | Line weights, line colour (`R,G,B`), line patterns, material |
| Families | Family and type list with instance counts | Information only |

- Parameters are picked per export (Available ↔ Selected, filter Instance / Type / Read-only, reorder).
- Element-reference columns (levels, phases, materials, scope boxes, view templates, family types) get an Excel
  drop-down list. The lists live on the very hidden sheet `_RevitSyncLists`; import still resolves names against
  the model, never against the list.
- *Include linked models* adds read-only rows for the same built-in categories from loaded links.
- Element ID, Family and Type columns are information only and are never imported.
- Profiles (JSON, default folder `%APPDATA%\RevitScheduleSync\Profiles`) save the source, scope, categories (by
  name) and parameters, so a BIM manager can share standard exports from a network folder.
- Workbook schema version 2 adds `Source`, `AllowNewRows`, `RequiredForNewRows` and `LinkName` to the metadata
  sheet. Schema 1 workbooks still import.

## Requirements

- Revit 2026.5 (.NET 10) and/or Revit 2025 (.NET 8), Windows 64-bit. The core is built for both;
  Revit 2025 loads it through `RevitScheduleSync.Loader`, which isolates ClosedXML in a private load context
  (Revit 2025 has no add-in isolation; Dynamo for Revit 2025 ships an older DocumentFormat.OpenXml).
- .NET 10 SDK (10.0.401 or later) to build
- Microsoft Excel is **not** required

## Build and develop

```
dotnet build RevitScheduleSync.sln -c Debug
```

A Debug build copies the add-in to `%APPDATA%\Autodesk\Revit\Addins\2025\` and `\2026\` (close Revit first; the build
warns "NOT deployed" if Revit holds the files). Skip with `-p:DeployToRevit=false`.

## Tests

Revit-free logic (comparison, workbook layer, reports, review scoping, 20,000-row performance):

```
dotnet test RevitScheduleSync.sln -c Release
```

## Release package (Autodesk App Store bundle)

```
powershell -ExecutionPolicy Bypass -File packaging\build-package.ps1
```

Produces `artifacts\ScheduleSyncForRevit.bundle` and `artifacts\ScheduleSyncForRevit-<version>.bundle.zip`.
`-Install` copies the bundle to `%APPDATA%\Autodesk\ApplicationPlugins` for a local test (remove the development
add-in first: both use the same AddInId). Listing draft: [docs/app-store-listing.md](docs/app-store-listing.md).

## Source layout

```
src/RevitScheduleSync/
├── App/             IExternalApplication, ribbon, add-in host
├── Commands/        external command, availability, exception boundary
├── UI/              WPF window, view models, views, theme
├── Models/          plain data (no Revit objects): schedules, export, import, reports
├── Revit/           Schedules (discovery, analysis, export), Parameters, Documents, Import, Transactions
├── Excel/           workbook layout (WorkbookSchema), ClosedXML writer and reader
├── Sync/            pure comparison logic (three-way, fingerprint)
├── Services/        prompts, errors, report store, operations interface
└── Logging/         ILogger, FileLogger
src/RevitScheduleSync.Loader/    Revit 2025 entry point: loads the core into a private AssemblyLoadContext
tests/RevitScheduleSync.Tests/   xUnit tests (no Revit)
tools/IconExport/                renders the add-in icon to PNG for the package
packaging/                       bundle manifest, add-in manifest, help, icons, build script
```

## Logs and reports

`%LOCALAPPDATA%\RevitScheduleSync\Logs` (kept 30 days) and `%LOCALAPPDATA%\RevitScheduleSync\Reports`.
