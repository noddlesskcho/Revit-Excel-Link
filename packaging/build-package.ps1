<#
.SYNOPSIS
  Builds the Autodesk App Store bundle for Revit-Excel Link (Revit 2025 and 2026).

.DESCRIPTION
  Release builds (no deployment) -> artifacts\ScheduleSyncForRevit.bundle -> validated -> zipped.

  Bundle layout (loaded by Revit from an ApplicationPlugins folder):
    ScheduleSyncForRevit.bundle\
      PackageContents.xml
      Contents\2025\ScheduleSync.addin, RevitScheduleSync.Loader.dll, core\RevitScheduleSync.dll + dependencies
      Contents\2026\ScheduleSync.addin, RevitScheduleSync.dll + dependencies
      Contents\Resources\icon-*.png, help.html
      Contents\THIRD-PARTY-NOTICES.txt

  Revit 2026 isolates the add-in itself (UseRevitContext=False). Revit 2025 has no add-in isolation, so it loads
  the small loader, which loads core\ and ClosedXML into a private AssemblyLoadContext.

.PARAMETER Install
  Also copies the bundle to %APPDATA%\Autodesk\ApplicationPlugins for a local test.
  Refuses while a development add-in (%APPDATA%\Autodesk\Revit\Addins\<year>\RevitScheduleSync.addin) exists,
  because both use the same AddInId and Revit would report a duplicate.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File packaging\build-package.ps1
#>
param(
    [switch]$Install
)

$ErrorActionPreference = 'Stop'
$root      = Split-Path -Parent $PSScriptRoot
$packaging = $PSScriptRoot
$project   = Join-Path $root 'src\RevitScheduleSync\RevitScheduleSync.csproj'
$loader    = Join-Path $root 'src\RevitScheduleSync.Loader\RevitScheduleSync.Loader.csproj'
$artifacts = Join-Path $root 'artifacts'
$bundle    = Join-Path $artifacts 'ScheduleSyncForRevit.bundle'

# dotnet: prefer the per-user .NET 10 SDK used for development.
$dotnet = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }

$version = ([xml](Get-Content $project -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw 'Version not found in RevitScheduleSync.csproj.' }
Write-Host "Revit-Excel Link $version"

# 1. Clean Release x64 builds, never deployed to the developer's Revit folders.
#    Platform must be explicit: building a .csproj directly would otherwise output to bin\Release.
foreach ($p in $project, $loader) {
    & $dotnet build $p -c Release -p:Platform=x64 -p:DeployToRevit=false --no-incremental --nologo -v:minimal
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $p" }
}
$core2026  = Join-Path $root 'src\RevitScheduleSync\bin\x64\Release\net10.0-windows'
$core2025  = Join-Path $root 'src\RevitScheduleSync\bin\x64\Release\net8.0-windows'
$loaderBin = Join-Path $root 'src\RevitScheduleSync.Loader\bin\x64\Release\net8.0-windows'
foreach ($dll in (Join-Path $core2026 'RevitScheduleSync.dll'), (Join-Path $core2025 'RevitScheduleSync.dll'), (Join-Path $loaderBin 'RevitScheduleSync.Loader.dll')) {
    $built = (Get-Item $dll).VersionInfo.ProductVersion.Split('+')[0]
    if ($built -ne $version) { throw "$dll reports version $built, expected $version. Refusing to package a stale build." }
}

# 2. Assemble the bundle from scratch.
if (Test-Path $bundle) { Remove-Item $bundle -Recurse -Force }
$contents2026 = Join-Path $bundle 'Contents\2026'
$contents2025 = Join-Path $bundle 'Contents\2025'
$core2025Out  = Join-Path $contents2025 'core'
$resources    = Join-Path $bundle 'Contents\Resources'
New-Item -ItemType Directory -Force $contents2026, $core2025Out, $resources | Out-Null

function Copy-Core([string]$From, [string]$To) {
    Get-ChildItem $From -File | Where-Object { $_.Extension -notin '.addin', '.pdb' } | Copy-Item -Destination $To
}
Copy-Core -From $core2026 -To $contents2026
Copy-Item (Join-Path $packaging 'ScheduleSync.addin') $contents2026
Copy-Core -From $core2025 -To $core2025Out
Copy-Item (Join-Path $loaderBin 'RevitScheduleSync.Loader.dll') $contents2025
Copy-Item (Join-Path $packaging 'ScheduleSync2025.addin') (Join-Path $contents2025 'ScheduleSync.addin')
Get-ChildItem (Join-Path $packaging 'Resources') -File | Copy-Item -Destination $resources
Copy-Item (Join-Path $packaging 'THIRD-PARTY-NOTICES.txt') (Join-Path $bundle 'Contents')

(Get-Content (Join-Path $packaging 'PackageContents.xml') -Raw).Replace('$VERSION$', $version) |
    Set-Content (Join-Path $bundle 'PackageContents.xml') -Encoding UTF8

# 3. Validate: manifest parses, every component's add-in points at a real DLL, required files exist,
#    no Revit API DLLs shipped.
$manifest = [xml](Get-Content (Join-Path $bundle 'PackageContents.xml') -Raw)
foreach ($entry in $manifest.ApplicationPackage.Components.ComponentEntry) {
    $module = Join-Path $bundle $entry.ModuleName
    if (-not (Test-Path $module)) { throw "ModuleName not found: $($entry.ModuleName)" }
    $addin = [xml](Get-Content $module -Raw)
    $dll = Join-Path (Split-Path $module) $addin.RevitAddIns.AddIn.Assembly
    if (-not (Test-Path $dll)) { throw "Add-in assembly not found: $dll" }
}
foreach ($file in @($manifest.ApplicationPackage.Icon, $manifest.ApplicationPackage.HelpFile)) {
    if (-not (Test-Path (Join-Path $bundle $file))) { throw "Missing file referenced by PackageContents.xml: $file" }
}
if (Get-ChildItem (Join-Path $bundle 'Contents') -Recurse -Filter 'RevitAPI*.dll') { throw 'Revit API assemblies must not be shipped.' }
if (-not (Test-Path (Join-Path $core2025Out 'RevitScheduleSync.deps.json'))) { throw 'Revit 2025 core is missing its deps.json (needed by the loader).' }
if ((Get-Content (Join-Path $bundle 'PackageContents.xml') -Raw) -match '\[Publisher name\]|\[Support') {
    Write-Warning 'PackageContents.xml still contains publisher placeholders. Fill them in before submitting.'
}

# 4. Zip (the zip contains the .bundle folder itself).
$zip = Join-Path $artifacts "ScheduleSyncForRevit-$version.bundle.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $bundle -DestinationPath $zip
$files = (Get-ChildItem $bundle -Recurse -File).Count
Write-Host "Bundle: $bundle ($files files)"
Write-Host "Zip:    $zip ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)"

# 5. Optional local install for testing.
if ($Install) {
    foreach ($year in '2025', '2026') {
        $devAddin = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$year\RevitScheduleSync.addin"
        if (Test-Path $devAddin) {
            throw "The development add-in is installed ($devAddin). Remove that file (and the RevitScheduleSync folder next to it) before testing the bundle; both use the same AddInId."
        }
    }
    $plugins = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins'
    New-Item -ItemType Directory -Force $plugins | Out-Null
    $target = Join-Path $plugins 'ScheduleSyncForRevit.bundle'
    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
    Copy-Item $bundle $plugins -Recurse
    Write-Host "Installed for this user: $target (restart Revit)"
}
