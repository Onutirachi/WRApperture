@echo off
rem Removes WRApperture from this host. Requests elevation (UAC) and asks what to keep.
rem Leaves the install folder, and "(goto) 2>nul" detaches cmd from this file, so the folder
rem (including this file) can be deleted without "path not found" errors.
cd /d "%TEMP%"
(goto) 2>nul & powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Start-Process powershell.exe -Verb RunAs -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File \"%~dp0Uninstall-WRAppertureHost.ps1\" -Interactive'"
