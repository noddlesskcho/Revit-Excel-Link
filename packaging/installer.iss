; Inno Setup script: single-file installer for Revit-Excel Link (Revit 2024-2027).
; Built by build-package.ps1 (or CI) after the bundle is assembled:
;   ISCC.exe /DAppVersion=1.2.0 /DBundleDir=<artifacts>\RevitExcelLink.bundle /DOutputDir=<artifacts> installer.iss
; Installs per user (no administrator rights) into %APPDATA%\Autodesk\ApplicationPlugins, where Revit
; picks up the bundle for every supported version.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef BundleDir
  #define BundleDir "..\artifacts\RevitExcelLink.bundle"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif

[Setup]
AppId={{FBB76A76-F19C-4D34-A639-12D06EF166CE}
AppName=Revit-Excel Link
AppVersion={#AppVersion}
AppVerName=Revit-Excel Link {#AppVersion}
AppPublisher=noddlesskcho
AppPublisherURL=https://github.com/noddlesskcho/Revit-Excel-Link
AppSupportURL=https://github.com/noddlesskcho/Revit-Excel-Link/issues
DefaultDirName={userappdata}\Autodesk\ApplicationPlugins\RevitExcelLink.bundle
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=RevitExcelLink-Setup-{#AppVersion}
UninstallDisplayName=Revit-Excel Link
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
CloseApplicationsFilter=*.dll

[Messages]
WelcomeLabel2=This installs [name/ver] for Autodesk Revit 2024, 2025, 2026 and 2027.%n%nClose Revit before you continue.

[InstallDelete]
; Former bundle name and developer installs of the same add-in (same AddInId: Revit refuses duplicates).
Type: filesandordirs; Name: "{userappdata}\Autodesk\ApplicationPlugins\ScheduleSyncForRevit.bundle"
Type: filesandordirs; Name: "{app}\Contents"
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2024\RevitScheduleSync.addin"
Type: filesandordirs; Name: "{userappdata}\Autodesk\Revit\Addins\2024\RevitScheduleSync"
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2025\RevitScheduleSync.addin"
Type: filesandordirs; Name: "{userappdata}\Autodesk\Revit\Addins\2025\RevitScheduleSync"
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2026\RevitScheduleSync.addin"
Type: filesandordirs; Name: "{userappdata}\Autodesk\Revit\Addins\2026\RevitScheduleSync"
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2027\RevitScheduleSync.addin"
Type: filesandordirs; Name: "{userappdata}\Autodesk\Revit\Addins\2027\RevitScheduleSync"

[Files]
Source: "{#BundleDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Code]
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  // tasklist exits 0 either way; findstr exits 0 only when Revit.exe is listed.
  if Exec(ExpandConstant('{cmd}'), '/C tasklist /FI "IMAGENAME eq Revit.exe" | findstr /I "Revit.exe" >nul',
          '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0) then
  begin
    MsgBox('Revit is running. Close Revit, then run this installer again.', mbError, MB_OK);
    Result := False;
  end;
end;
