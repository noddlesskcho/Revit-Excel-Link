# Revit Schedule Sync

Revit 2026.5 add-in for safe, auditable Revit schedule ↔ Excel synchronisation.

Status: **Phase 0 — project foundation** (ribbon button, WPF window, logging, error boundary).
No schedule extraction or Excel logic yet.

## Requirements

- Revit 2026.5 (hosts the .NET 10 runtime)
- .NET 10 SDK (10.0.401 or later)
- Microsoft Excel is **not** required

## Build

```
dotnet build RevitScheduleSync.sln -c Debug
```

A Debug build copies the add-in into the per-user Revit add-ins folder automatically.
Close Revit before building, otherwise the copy fails with a warning (DLL locked).

- Skip deployment: `-p:DeployToRevit=false`
- Revit installed elsewhere: `-p:RevitInstallDir="D:\Autodesk\Revit 2026\\"`

## Deployed layout

```
%APPDATA%\Autodesk\Revit\Addins\2026\
├── RevitScheduleSync.addin
└── RevitScheduleSync\
    ├── RevitScheduleSync.dll
    ├── RevitScheduleSync.deps.json
    ├── ClosedXML.dll, DocumentFormat.OpenXml*.dll, ... (dependencies)
```

The manifest sets `UseRevitContext=False`, so Revit 2026 loads the add-in and its
dependencies into an isolated assembly load context.

## Logs

`%LOCALAPPDATA%\RevitScheduleSync\Logs\ScheduleSync-yyyyMMdd.log` (kept 30 days).
The window footer has an "Open log folder" link.

## Source layout

```
src/RevitScheduleSync/
├── App/          IExternalApplication, ribbon, add-in host
├── Commands/     external commands, availability, exception boundary
├── UI/           WPF window, view models, theme
├── Models/       plain data objects (no Revit objects)
├── Excel/        Excel layer (ClosedXML)          — grows in Phase 3
├── Services/     error reporting, user-facing exceptions
└── Logging/      ILogger, FileLogger
```

Planned folders added in later phases: `Revit/Schedules`, `Revit/Elements`,
`Revit/Parameters`, `Revit/Transactions`, `Sync`, `Validation`.
