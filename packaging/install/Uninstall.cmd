@echo off
rem Removes Revit-Excel Link for the current user. Close Revit first.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" -Uninstall
pause
