# Schedule Sync for Revit

Repository and assembly name: `RevitScheduleSync`.

Revit 2026 add-in for safe, auditable schedule ↔ Excel synchronisation: export schedules to .xlsx,
edit values in Excel, then analyse, review and apply the changes back to the model.

Status: **V1 (1.0.0)** — export, import analysis (three-way comparison), review, apply (one Undo step),
report and history. See [docs/V1-checklist.md](docs/V1-checklist.md).

## Requirements

- Revit 2026.5 (hosts the .NET 10 runtime), Windows 64-bit
- .NET 10 SDK (10.0.401 or later) to build
- Microsoft Excel is **not** required

## Build and develop

```
dotnet build RevitScheduleSync.sln -c Debug
```

A Debug build copies the add-in to `%APPDATA%\Autodesk\Revit\Addins\2026\` (close Revit first; the build
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
tests/RevitScheduleSync.Tests/   xUnit tests (no Revit)
tools/IconExport/                renders the add-in icon to PNG for the package
packaging/                       bundle manifest, add-in manifest, help, icons, build script
```

## Logs and reports

`%LOCALAPPDATA%\RevitScheduleSync\Logs` (kept 30 days) and `%LOCALAPPDATA%\RevitScheduleSync\Reports`.
