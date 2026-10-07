@echo off
rem Requests elevation (UAC) and runs the host installer from this folder.
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Start-Process powershell.exe -Verb RunAs -ArgumentList '-NoProfile -ExecutionPolicy Bypass -NoExit -File \"%~dp0Install-WRAppertureHost.ps1\" -Interactive'"
