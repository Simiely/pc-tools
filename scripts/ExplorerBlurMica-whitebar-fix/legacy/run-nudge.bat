@echo off
rem Launch the ExplorerBlurMica white-bar auto-nudge silently in the background.
powershell.exe -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0explorer-blur-fix-nudge.ps1"
