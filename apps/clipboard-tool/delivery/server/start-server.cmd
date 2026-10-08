@echo off
setlocal
rem ============================================================
rem Clipboard Tool - Server Launcher
rem Double-click to start, or run from cmd:
rem   start-server.cmd [port] [dataDir]
rem   port    default 8130
rem   dataDir default <project>\.data  (or set CAP_STORAGE_DIR first)
rem Requires Node.js >= 22.7 (zero third-party deps)
rem ============================================================

set "PORT=8130"
if not "%~1"=="" set "PORT=%~1"

rem ---- locate project root (script lives in delivery/server) ----
cd /d "%~dp0..\.."
set "ROOT=%CD%"

rem ---- Node.js check ----
where node >nul 2>nul
if errorlevel 1 (
  echo [ERROR] Node.js not found. Install Node.js 22.7+ first:
  echo         https://nodejs.org/
  pause
  exit /b 1
)
for /f "tokens=1,2 delims=." %%a in ('node --version') do set "NODE_MAJOR=%%a"
echo [info] Node found: %NODE_MAJOR% (need ^>=22)

rem ---- data dir ---- (default ./.data; override with CAP_STORAGE_DIR or arg2)
if "%~2"=="" (
  if not defined CAP_STORAGE_DIR set "CAP_STORAGE_DIR=%ROOT%\.data"
) else (
  set "CAP_STORAGE_DIR=%~2"
)
echo [info] Data dir: %CAP_STORAGE_DIR%

rem ---- data dir sharing warning (2026-08-27 multi-instance lesson) ----
if "%CAP_STORAGE_DIR%"=="%ROOT%\.data" if not "%PORT%"=="8130" (
  echo [WARN] Non-default port with DEFAULT data dir ^(%CAP_STORAGE_DIR%^).
  echo        If another clipboard instance ^(e.g. 8130^) is already using this
  echo        data dir, STOP now - two instances sharing one data dir can
  echo        corrupt/clear data. Use a separate dir for the 2nd instance.
  echo.
  choice /c YN /m "Continue anyway"
  if errorlevel 2 exit /b 1
)

rem ---- port conflict guard ----
netstat -ano | findstr /R /C:":%PORT% " | findstr "LISTENING" >nul 2>nul
if not errorlevel 1 (
  echo [ERROR] Port %PORT% already in use - another clipboard instance running?
  echo         Check with:  netstat -ano ^| findstr :%PORT%
  echo         Stop the other instance first, then retry.
  pause
  exit /b 1
)

echo [info] Starting clipboard server on port %PORT% ...
node server.mjs %PORT%

echo.
echo [info] Server stopped.
pause
