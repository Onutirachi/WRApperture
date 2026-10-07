@echo off
rem Installs the WRApperture client for the current user (no admin needed) and opens it.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-WRAppertureClient.ps1" -Launch
pause
