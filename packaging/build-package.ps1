<#
.SYNOPSIS
  Builds the Autodesk App Store bundle for Schedule Sync for Revit.

.DESCRIPTION
  Release build (no deployment) -> artifacts\ScheduleSyncForRevit.bundle -> validated -> zipped.

  Bundle layout (loaded by Revit from an ApplicationPlugins folder):
    ScheduleSyncForRevit.bundle\
      PackageContents.xml
      Contents\2026\ScheduleSync.addin, RevitScheduleSync.dll + dependencies
      Contents\Resources\icon-*.png, help.html

.PARAMETER Install
  Also copies the bundle to %APPDATA%\Autodesk\ApplicationPlugins for a local test.
  Refuses while the development add-in (%APPDATA%\Autodesk\Revit\Addins\2026\RevitScheduleSync.addin) exists,
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
$artifacts = Join-Path $root 'artifacts'
$bundle    = Join-Path $artifacts 'ScheduleSyncForRevit.bundle'

# dotnet: prefer the per-user .NET 10 SDK used for development.
$dotnet = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }

$version = ([xml](Get-Content $project -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw 'Version not found in RevitScheduleSync.csproj.' }
Write-Host "Schedule Sync for Revit $version"

# 1. Clean Release x64 build, never deployed to the developer's Revit folder.
#    Platform must be explicit: building the .csproj directly would otherwise output to bin\Release.
& $dotnet build $project -c Release -p:Platform=x64 -p:DeployToRevit=false --no-incremental --nologo -v:minimal
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$bin = Join-Path $root 'src\RevitScheduleSync\bin\x64\Release'
$built = (Get-Item (Join-Path $bin 'RevitScheduleSync.dll')).VersionInfo.ProductVersion.Split('+')[0]
if ($built -ne $version) { throw "Built DLL reports version $built, expected $version. Refusing to package a stale build." }

# 2. Assemble the bundle from scratch.
if (Test-Path $bundle) { Remove-Item $bundle -Recurse -Force }
$contents  = Join-Path $bundle 'Contents\2026'
$resources = Join-Path $bundle 'Contents\Resources'
New-Item -ItemType Directory -Force $contents, $resources | Out-Null

Get-ChildItem $bin -File | Where-Object { $_.Extension -notin '.addin', '.pdb' } |
    Copy-Item -Destination $contents
Copy-Item (Join-Path $packaging 'ScheduleSync.addin') $contents
Copy-Item (Join-Path $packaging 'Resources\*') $resources
Copy-Item (Join-Path $packaging 'THIRD-PARTY-NOTICES.txt') (Join-Path $bundle 'Contents')

(Get-Content (Join-Path $packaging 'PackageContents.xml') -Raw).Replace('$VERSION$', $version) |
    Set-Content (Join-Path $bundle 'PackageContents.xml') -Encoding UTF8

# 3. Validate: manifest parses, referenced files exist, add-in points at a real DLL, no Revit API DLLs shipped.
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
if (Get-ChildItem $contents -Filter 'RevitAPI*.dll') { throw 'Revit API assemblies must not be shipped.' }
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
    $devAddin = Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2026\RevitScheduleSync.addin'
    if (Test-Path $devAddin) {
        throw "The development add-in is installed ($devAddin). Remove that file (and the RevitScheduleSync folder next to it) before testing the bundle; both use the same AddInId."
    }
    $plugins = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins'
    New-Item -ItemType Directory -Force $plugins | Out-Null
    $target = Join-Path $plugins 'ScheduleSyncForRevit.bundle'
    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
    Copy-Item $bundle $plugins -Recurse
    Write-Host "Installed for this user: $target (restart Revit)"
}
