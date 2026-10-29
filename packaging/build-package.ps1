<#
.SYNOPSIS
  Builds the Revit-Excel Link bundle, the install zip and (when Inno Setup is available) the setup .exe,
  for Revit 2024, 2025, 2026 and 2027.

.DESCRIPTION
  Release builds (no deployment) -> artifacts\RevitExcelLink.bundle -> validated -> packaged.

  Bundle layout (loaded by Revit from %APPDATA%\Autodesk\ApplicationPlugins):
    RevitExcelLink.bundle\
      PackageContents.xml
      Contents\2024\RevitExcelLink.addin, RevitScheduleSync.dll + dependencies (.NET Framework 4.8)
      Contents\2025\RevitExcelLink.addin, RevitScheduleSync.Loader.dll, core\RevitScheduleSync.dll + dependencies
      Contents\2026\RevitExcelLink.addin, RevitScheduleSync.dll + dependencies (.NET 8)
      Contents\2027\RevitExcelLink.addin, RevitScheduleSync.dll + dependencies (.NET 10)
      Contents\Resources\icon-*.png, help.html
      Contents\THIRD-PARTY-NOTICES.txt

  Revit 2026/2027 isolate the add-in themselves (UseRevitContext=False). Revit 2025 has no add-in isolation,
  so it loads the small loader, which loads core\ and ClosedXML into a private AssemblyLoadContext.

  Outputs in artifacts\:
    RevitExcelLink-<version>.zip         bundle + Install.cmd / Uninstall.cmd / README.txt
    RevitExcelLink-Setup-<version>.exe   single-file installer (needs Inno Setup 6: ISCC.exe)

.PARAMETER Install
  Also installs the bundle for the current user (same as running Install.cmd from the zip).

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
$build     = Join-Path $artifacts 'build'
$bundle    = Join-Path $artifacts 'RevitExcelLink.bundle'
$years     = '2024', '2025', '2026', '2027'

# dotnet: prefer the per-user .NET 10 SDK used for development.
$dotnet = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }

$version = ([xml](Get-Content $project -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw 'Version not found in RevitScheduleSync.csproj.' }
Write-Host "Revit-Excel Link $version"

# 1. Clean Release x64 builds, one per Revit version, never deployed to the developer's Revit folders.
if (Test-Path $build) { Remove-Item $build -Recurse -Force }
foreach ($year in $years) {
    & $dotnet build $project -c Release -p:Platform=x64 -p:RevitVersion=$year -p:DeployToRevit=false `
        -o (Join-Path $build $year) --no-incremental --nologo -v:minimal
    if ($LASTEXITCODE -ne 0) { throw "Build failed: Revit $year" }
}
& $dotnet build $loader -c Release -p:Platform=x64 -p:DeployToRevit=false -o (Join-Path $build 'loader') --no-incremental --nologo -v:minimal
if ($LASTEXITCODE -ne 0) { throw 'Build failed: loader' }

$dlls = @($years | ForEach-Object { Join-Path $build "$_\RevitScheduleSync.dll" }) + (Join-Path $build 'loader\RevitScheduleSync.Loader.dll')
foreach ($dll in $dlls) {
    $built = (Get-Item $dll).VersionInfo.ProductVersion
    if (-not $built.StartsWith("$version build ")) { throw "$dll reports version $built, expected $version. Refusing to package a stale build." }
}
$productVersion = (Get-Item $dlls[0]).VersionInfo.ProductVersion.Split('+')[0]
Write-Host "Built: $productVersion"

# 2. Assemble the bundle from scratch.
if (Test-Path $bundle) { Remove-Item $bundle -Recurse -Force }
$resources = Join-Path $bundle 'Contents\Resources'
New-Item -ItemType Directory -Force $resources | Out-Null

function Copy-Core([string]$From, [string]$To) {
    New-Item -ItemType Directory -Force $To | Out-Null
    Get-ChildItem $From | Where-Object { $_.Extension -notin '.addin', '.pdb' } | Copy-Item -Destination $To -Recurse
}
$manifests = @{ '2024' = 'ScheduleSync2024.addin'; '2025' = 'ScheduleSync2025.addin'; '2026' = 'ScheduleSync.addin'; '2027' = 'ScheduleSync.addin' }
foreach ($year in $years) {
    $contents = Join-Path $bundle "Contents\$year"
    if ($year -eq '2025') {
        Copy-Core -From (Join-Path $build $year) -To (Join-Path $contents 'core')
        Copy-Item (Join-Path $build 'loader\RevitScheduleSync.Loader.dll') $contents
    } else {
        Copy-Core -From (Join-Path $build $year) -To $contents
    }
    Copy-Item (Join-Path $packaging $manifests[$year]) (Join-Path $contents 'RevitExcelLink.addin')
}
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
if (-not (Test-Path (Join-Path $bundle 'Contents\2025\core\RevitScheduleSync.deps.json'))) { throw 'Revit 2025 core is missing its deps.json (needed by the loader).' }
if (-not (Test-Path (Join-Path $bundle 'Contents\2024\ClosedXML.dll'))) { throw 'Revit 2024 build is missing ClosedXML.dll.' }

# 4. Zip: bundle + installer scripts, ready to send.
$staging = Join-Path $artifacts 'zip'
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
$folder = Join-Path $staging "RevitExcelLink-$version"
New-Item -ItemType Directory -Force $folder | Out-Null
Copy-Item $bundle $folder -Recurse
Get-ChildItem (Join-Path $packaging 'install') -File | Copy-Item -Destination $folder
$zip = Join-Path $artifacts "RevitExcelLink-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $folder -DestinationPath $zip
$files = (Get-ChildItem $bundle -Recurse -File).Count
Write-Host "Bundle: $bundle ($files files)"
Write-Host "Zip:    $zip ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)"

# 5. Setup .exe (Inno Setup 6).
$iscc = @(
    (Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue).Source,
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if ($iscc) {
    & $iscc /Qp "/DAppVersion=$version" "/DBundleDir=$bundle" "/DOutputDir=$artifacts" (Join-Path $packaging 'installer.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed.' }
    Write-Host "Setup:  $(Join-Path $artifacts "RevitExcelLink-Setup-$version.exe")"
} else {
    Write-Warning 'Inno Setup 6 (ISCC.exe) not found: setup .exe skipped. The zip installer is complete.'
}

# 6. Optional local install.
if ($Install) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $folder 'install.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Install failed.' }
}
