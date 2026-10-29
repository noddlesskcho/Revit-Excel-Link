# Revit-Excel Link

Repository and assembly name: `RevitScheduleSync`.

Add-in for Autodesk Revit **2024, 2025, 2026 and 2027** for safe, auditable Revit ↔ Excel synchronisation:
export schedules and model data to .xlsx, edit values in Excel, then analyse, review and apply the changes back
to the model.

## Download and install

Get the latest installer from [Releases](https://github.com/noddlesskcho/Revit-Excel-Link/releases/latest):

- `RevitExcelLink-Setup-<version>.exe`: single-file installer, per user, no administrator rights.
- `RevitExcelLink-<version>.zip`: unzip, then double-click `Install.cmd` (`Uninstall.cmd` removes it).

Close Revit before installing. Both install the same bundle to
`%APPDATA%\Autodesk\ApplicationPlugins\RevitExcelLink.bundle`; Revit picks the build for its own version.
The **Revit-Excel Link** panel is on the Add-Ins tab. The window footer and button tooltip show the version
with its build date and time.

Features: export, import analysis (three-way comparison), review, apply (one Undo step), report and history.
See [docs/V1-checklist.md](docs/V1-checklist.md).

### Model data

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

| Revit | Add-in build | Loading |
|---|---|---|
| 2024 | .NET Framework 4.8 | loaded directly |
| 2025 | .NET 8 | through `RevitScheduleSync.Loader`, which isolates ClosedXML in a private load context (Revit 2025 has no add-in isolation; Dynamo for Revit 2025 ships an older DocumentFormat.OpenXml) |
| 2026 | .NET 8 (also runs on 2026 updates that host .NET 10) | isolated by Revit (`UseRevitContext=False`) |
| 2027 | .NET 10 | isolated by Revit (`UseRevitContext=False`) |

- Windows 64-bit. Microsoft Excel is **not** required.
- To build: .NET 10 SDK (10.0.401 or later). Revit itself is not needed: the Revit API comes from the
  `Nice3point.Revit.Api` reference packages.

## Build and develop

One build per Revit version (default 2026):

```
dotnet build src/RevitScheduleSync -c Debug -p:RevitVersion=2024
dotnet build src/RevitScheduleSync.Loader -c Debug
```

A Debug build copies the add-in to `%APPDATA%\Autodesk\Revit\Addins\<version>\` (close Revit first; the build
warns "NOT deployed" if Revit holds the files). Skip with `-p:DeployToRevit=false`. Revit 2025 needs both the core
(`-p:RevitVersion=2025`) and the loader.

## Tests

Revit-free logic (comparison, workbook layer, reports, review scoping, 20,000-row performance):

```
dotnet test tests/RevitScheduleSync.Tests -c Release -p:DeployToRevit=false
```

## Installers

```
powershell -ExecutionPolicy Bypass -File packaging\build-package.ps1
```

Builds all four versions and produces in `artifacts\`:

- `RevitExcelLink.bundle` (Autodesk bundle: `PackageContents.xml` + `Contents\2024` … `Contents\2027`)
- `RevitExcelLink-<version>.zip` (bundle + `Install.cmd`, `Uninstall.cmd`, `README.txt`)
- `RevitExcelLink-Setup-<version>.exe` when Inno Setup 6 is installed (`packaging\installer.iss`)

`-Install` also installs the bundle for the current user. Installing removes older developer installs of this
add-in (`Addins\<version>\RevitScheduleSync.addin`), because both use the same AddInId.

### GitHub release

Bump `<Version>` in `src/RevitScheduleSync/RevitScheduleSync.csproj` (and the loader), commit, then push a tag:

```
git tag v1.2.0
git push origin v1.2.0
```

The **Build and release** workflow tests, builds both installers and attaches them to a new GitHub release.

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
├── Logging/         ILogger, FileLogger
└── Compatibility/   .NET Framework 4.8 stand-ins (Revit 2024 build)
src/RevitScheduleSync.Loader/    Revit 2025 entry point: loads the core into a private AssemblyLoadContext
tests/RevitScheduleSync.Tests/   xUnit tests (no Revit)
tools/IconExport/                renders the add-in icon to PNG for the package
packaging/                       bundle manifest, add-in manifests, installers, help, icons, build script
.github/workflows/release.yml    CI: tag push builds installers and publishes a GitHub release
```

## Logs and reports

`%LOCALAPPDATA%\RevitScheduleSync\Logs` (kept 30 days) and `%LOCALAPPDATA%\RevitScheduleSync\Reports`.
