# ============================================================
# reset-explorer-view.ps1
# Fix: File Explorer does not auto-refresh after a file is
# downloaded/copied (you must press F5 manually).
#
# Scope : current user only (HKCU + user profile), no admin needed.
# What it does:
#   1. Stop and restart Explorer (the Windows shell)
#   2. Reset folder view cache (Shell Bags / BagMRU) - top suspect
#   3. Clear Explorer history and jump lists (Recent / RunMRU / TypedPaths)
#   4. Clear icon cache and thumbnail cache (iconcache_*.db / thumbcache_*.db)
#   5. Write AlwaysRefresh=1 to force the shell to keep refreshing in background
#
# How to run: double-click run-explorer-fix.bat, or in PowerShell:
#   powershell -ExecutionPolicy Bypass -File ".\reset-explorer-view.ps1"
# ============================================================

$ErrorActionPreference = 'SilentlyContinue'

function Step($n, $msg) {
    Write-Host ("[{0}/5] {1}" -f $n, $msg) -ForegroundColor Cyan
}

Step 1 "Stopping Explorer process (the shell)..."
Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

Step 2 "Resetting folder view cache (Shell Bags / BagMRU)..."
Remove-Item -Path 'HKCU:\Software\Microsoft\Windows\Shell\Bags' -Recurse -Force
Remove-Item -Path 'HKCU:\Software\Microsoft\Windows\Shell\BagMRU' -Recurse -Force

Step 3 "Clearing Explorer history and jump lists..."
Remove-Item -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\RunMRU' -Recurse -Force
Remove-Item -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\TypedPaths' -Recurse -Force
Remove-Item -Path "$env:APPDATA\Microsoft\Windows\Recent\AutomaticDestinations\*" -Force
Remove-Item -Path "$env:APPDATA\Microsoft\Windows\Recent\CustomDestinations\*" -Force
Remove-Item -Path "$env:USERPROFILE\Recent\*" -Force

Step 4 "Clearing icon cache and thumbnail cache..."
$tc = "$env:LOCALAPPDATA\Microsoft\Windows\Explorer"
Remove-Item -Path "$tc\iconcache_*.db" -Force
Remove-Item -Path "$tc\thumbcache_*.db" -Force
Remove-Item -Path "$tc\cloudcache.db" -Force
Remove-Item -Path "$env:LOCALAPPDATA\IconCache.db" -Force

Step 5 "Enabling background refresh (AlwaysRefresh=1) and restarting Explorer..."
New-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced' `
    -Name 'AlwaysRefresh' -Value 1 -PropertyType DWORD -Force | Out-Null

Start-Process explorer

Write-Host ""
Write-Host "Done. Explorer has been restarted." -ForegroundColor Green
Write-Host "Test by downloading a file now: it should appear WITHOUT pressing F5." -ForegroundColor Green
Write-Host "If it still requires a manual refresh, open explorer-refresh-guide.md for advanced steps (SFC/DISM, ShellExView)." -ForegroundColor Yellow
