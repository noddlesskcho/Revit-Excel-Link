<#
.SYNOPSIS
  Installs Revit-Excel Link for the current Windows user (Revit 2024, 2025, 2026 and 2027).

.DESCRIPTION
  Copies RevitExcelLink.bundle (next to this script) to %APPDATA%\Autodesk\ApplicationPlugins.
  Revit loads it on its next start. No administrator rights are needed.
  Older copies of the same add-in (developer installs or the former "Schedule Sync" bundle) are removed first,
  because Revit refuses two add-ins with the same AddInId.

.PARAMETER Uninstall
  Removes the add-in instead.

.PARAMETER Force
  Continues while Revit is running (the change takes effect when Revit starts next).
#>
param(
    [switch]$Uninstall,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$bundleName = 'RevitExcelLink.bundle'
$plugins    = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins'
$target     = Join-Path $plugins $bundleName

function Remove-OldCopies {
    # Former bundle name.
    $old = Join-Path $plugins 'ScheduleSyncForRevit.bundle'
    if (Test-Path $old) { Remove-Item $old -Recurse -Force; Write-Host "Removed old bundle: $old" }

    # Developer installs of this add-in (same AddInId) in the per-user Addins folders.
    foreach ($year in '2024', '2025', '2026', '2027') {
        $addins   = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$year"
        $manifest = Join-Path $addins 'RevitScheduleSync.addin'
        if ((Test-Path $manifest) -and ((Get-Content $manifest -Raw) -match '5afc7788-f203-4ec5-bf1b-d05b2042fe37')) {
            Remove-Item $manifest -Force
            $folder = Join-Path $addins 'RevitScheduleSync'
            if (Test-Path $folder) { Remove-Item $folder -Recurse -Force }
            Write-Host "Removed developer install for Revit $year"
        }
    }
}

if (-not $Force -and (Get-Process -Name 'Revit' -ErrorAction SilentlyContinue)) {
    Write-Warning 'Revit is running. Close Revit, then run this again.'
    exit 1
}

if ($Uninstall) {
    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
    Remove-OldCopies
    Write-Host 'Revit-Excel Link removed.'
    exit 0
}

$source = Join-Path $PSScriptRoot $bundleName
if (-not (Test-Path (Join-Path $source 'PackageContents.xml'))) {
    throw "$bundleName not found next to this script. Unzip the whole package first."
}

Remove-OldCopies
New-Item -ItemType Directory -Force $plugins | Out-Null
if (Test-Path $target) { Remove-Item $target -Recurse -Force }
Copy-Item $source $plugins -Recurse

# Files from a downloaded zip carry the "downloaded from the internet" mark; Revit 2024 (.NET Framework)
# refuses to load add-in DLLs that still have it.
Get-ChildItem $target -Recurse -File | Unblock-File

$version = ([xml](Get-Content (Join-Path $target 'PackageContents.xml') -Raw)).ApplicationPackage.AppVersion
Write-Host ''
Write-Host "Revit-Excel Link $version installed for $env:USERNAME."
Write-Host "Location: $target"
Write-Host 'Start Revit 2024, 2025, 2026 or 2027 and look for the Revit-Excel Link panel on the Add-Ins tab.'
