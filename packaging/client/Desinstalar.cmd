@echo off
rem Removes the WRApperture client for the current user and asks whether to delete user data.
rem Leaves the install folder, and "(goto) 2>nul" detaches cmd from this file, so the folder
rem (including this file) can be deleted without "path not found" errors.
cd /d "%TEMP%"
(goto) 2>nul & powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Uninstall-WRAppertureClient.ps1" -Interactive
