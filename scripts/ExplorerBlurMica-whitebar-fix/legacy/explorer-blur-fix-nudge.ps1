# explorer-blur-fix-nudge.ps1
# Auto-nudges newly opened Explorer (CabinetWClass) windows so the
# ExplorerBlurMica bottom white-bar repaints without a manual drag.
#
# How it works:
#   Poll all top-level windows. When a NEW CabinetWClass window appears,
#   wait a short moment (let it paint), then resize it by +1px and back.
#   That sends WM_SIZE, which makes ExplorerBlurMica re-apply the DWM
#   backdrop over the whole client area, including the bottom strip.
#   This reproduces exactly what a manual drag does.
#
# Requires: Windows PowerShell (FullLanguage mode). No admin needed.
# Stop: end the powershell.exe process running this script (Task Manager).

$ErrorActionPreference = 'SilentlyContinue'

Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Text;

public class WinUtil {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    public struct RECT { public int Left, Top, Right, Bottom; }

    public const uint SWP_NOZORDER  = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
}
'@

$nudged  = New-Object 'System.Collections.Generic.HashSet[IntPtr]'
$newOnes = New-Object 'System.Collections.Generic.List[IntPtr]'

$pollMs  = 400   # how often to scan for new explorer windows
$delayMs = 400   # wait after detection so the white bar has painted first

$enumDelegate = [WinUtil+EnumWindowsProc] {
    param($hwnd, $lparam)
    $sb = New-Object System.Text.StringBuilder 256
    [WinUtil]::GetClassName($hwnd, $sb, 256) | Out-Null
    if ($sb.ToString() -eq 'CabinetWClass' -and [WinUtil]::IsWindowVisible($hwnd)) {
        if (-not $script:nudged.Contains($hwnd)) {
            $script:nudged.Add($hwnd)  | Out-Null
            $script:newOnes.Add($hwnd) | Out-Null
        }
    }
    return $true
}

function Nudge($hwnd) {
    $rect = New-Object WinUtil+RECT
    if (-not [WinUtil]::GetWindowRect($hwnd, [ref]$rect)) { return }
    $w = $rect.Right - $rect.Left
    $h = $rect.Bottom - $rect.Top
    if ($w -le 0 -or $h -le 0) { return }
    try {
        # resize +1px then restore -> sends WM_SIZE, mimics a drag-resize
        [WinUtil]::SetWindowPos($hwnd, [IntPtr]::Zero, $rect.Left, $rect.Top,
            $w + 1, $h, [WinUtil]::SWP_NOZORDER -bor [WinUtil]::SWP_NOACTIVATE) | Out-Null
        Start-Sleep -Milliseconds 30
        [WinUtil]::SetWindowPos($hwnd, [IntPtr]::Zero, $rect.Left, $rect.Top,
            $w, $h, [WinUtil]::SWP_NOZORDER -bor [WinUtil]::SWP_NOACTIVATE) | Out-Null
    } catch { }
}

Write-Host "ExplorerBlurMica white-bar auto-nudge started (Ctrl+C to stop)."

while ($true) {
    $newOnes.Clear()
    [WinUtil]::EnumWindows($enumDelegate, [IntPtr]::Zero) | Out-Null

    if ($newOnes.Count -gt 0) {
        Start-Sleep -Milliseconds $delayMs
        foreach ($h in $newOnes) {
            if ([WinUtil]::IsWindowVisible($h)) { Nudge $h }
        }
    }

    # forget windows that have been closed
    foreach ($h in @($nudged)) {
        if (-not [WinUtil]::IsWindowVisible($h)) { $nudged.Remove($h) | Out-Null }
    }

    Start-Sleep -Milliseconds $pollMs
}
