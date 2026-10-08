$ErrorActionPreference = 'Continue'
$pub = 'D:\workbuddy\2026-09-02-15-29-37\clipboard-tool\clipboard-exe\publish'

# clean slate
Get-Process Clipboard -ErrorAction SilentlyContinue | Stop-Process -Force -Confirm:$false
Start-Sleep -Milliseconds 800
if (Test-Path "$pub\data") { $bak = "$env:TEMP\verify-paste-data-bak-" + (Get-Date -Format 'yyyyMMddHHmmss'); Move-Item "$pub\data" $bak -Force; "data backed up to $bak" | Out-File "$env:TEMP\vt1.txt" -Encoding utf8 }

# start app
Start-Process -FilePath "$pub\Clipboard.exe" -WorkingDirectory $pub
Start-Sleep -Seconds 3
$p = Get-Process Clipboard | Select-Object -First 1
if (-not $p) { 'FAIL: process not running' | Out-File "$env:TEMP\vt1.txt" -Encoding utf8; exit }
'PID=' + $p.Id + ' Handle=' + $p.MainWindowHandle | Out-File "$env:TEMP\vt1.txt" -Encoding utf8

# simulate external copy while app is foreground (Paused=false -> watcher fires)
$text1 = 'ext-copy-' + (Get-Date -Format 'HHmmssfff')
Set-Clipboard -Value $text1
Start-Sleep -Milliseconds 900   # > 100ms debounce + margin
'After copy1: log=' | Out-File "$env:TEMP\vt1.txt" -Append -Encoding utf8
Get-Content "$pub\data\clipboard-exe.log" | Out-File "$env:TEMP\vt1.txt" -Append -Encoding utf8

# wait past the 300ms dup window + more: confirm NO second dialog opened
Start-Sleep -Milliseconds 1200
'--- after 2.1s total ---' | Out-File "$env:TEMP\vt1.txt" -Append -Encoding utf8
Get-Content "$pub\data\clipboard-exe.log" | Out-File "$env:TEMP\vt1.txt" -Append -Encoding utf8
