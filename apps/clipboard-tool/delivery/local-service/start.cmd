@echo off
rem ============================================
rem Clipboard Tool - Local Service Launcher
rem Starts server.mjs on port 8130 with ./.data
rem Requires Node.js >= 22.7
rem ============================================
cd /d "%~dp0..\.."

echo [clipboard] starting local service (v from package.json)...
node server.mjs 8130

echo.
echo [clipboard] service stopped.
pause
