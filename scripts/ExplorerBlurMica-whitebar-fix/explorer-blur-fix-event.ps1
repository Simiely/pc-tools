# explorer-blur-fix-event.ps1
# Plan 2: event-driven nudge for the ExplorerBlurMica bottom white bar.
# Detects new Explorer (CabinetWClass) windows via SetWinEventHook(EVENT_OBJECT_CREATE,
# WINEVENT_OUTOFCONTEXT) and nudges each one once after a short delay, forcing
# ExplorerBlurMica to repaint the DWM backdrop. Zero polling, zero idle CPU.
# NOTE: must be run by double-clicking run-nudge-event.bat on the user's own machine.
# Do NOT run inside the WorkBuddy PowerShell tool (Add-Type is sandbox-blocked there).

Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

public class WinEventNudge {
    public delegate void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmod, WinEventProc proc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    public static extern void PostQuitMessage(int nExitCode);

    [DllImport("user32.dll")]
    public static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    public static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern bool DispatchMessage(ref MSG lpMsg);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public int x; public int y; }

    public const uint EVENT_OBJECT_CREATE = 0x8000;
    public const uint WINEVENT_OUTOFCONTEXT = 0x0002;
    public const uint WINEVENT_SKIPOWNPROCESS = 0x0008;
    public const uint GA_ROOT = 2;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_NOMOVE = 0x0002;

    private static HashSet<IntPtr> _seen = new HashSet<IntPtr>();

    public static void Nudge(IntPtr hWnd) {
        RECT r;
        if (!GetWindowRect(hWnd, out r)) return;
        int w = r.Right - r.Left;
        int h = r.Bottom - r.Top;
        if (w <= 0 || h <= 0) return;
        // Grow the bottom edge by 1px (height +1, width unchanged) then restore.
        // SWP_NOMOVE keeps the top-left fixed, so only the bottom border dips 1px and
        // springs back, forcing a real WM_SIZE (same as a manual drag) to repaint the bar.
        SetWindowPos(hWnd, IntPtr.Zero, 0, 0, w, h + 1, SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOMOVE);
        SetWindowPos(hWnd, IntPtr.Zero, 0, 0, w, h,     SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOMOVE);
    }

    public static WinEventProc Proc = (hHook, eventType, hwnd, idObject, idChild, dwEventThread, dwmsEventTime) => {
        try {
            if (idObject != 0 || idChild != 0) return;          // only the window object itself
            IntPtr root = GetAncestor(hwnd, GA_ROOT);
            if (root != hwnd) return;                            // only top-level windows
            if (!IsWindowVisible(hwnd)) return;
            StringBuilder sb = new StringBuilder(256);
            if (GetClassName(hwnd, sb, 256) == 0) return;
            if (sb.ToString() != "CabinetWClass") return;        // Explorer only
            bool isNew;
            lock (_seen) { isNew = _seen.Add(hwnd); }
            if (!isNew) return;                                   // nudge each window once
            IntPtr target = hwnd;
            Task.Run(() => {
                System.Threading.Thread.Sleep(450);              // let it paint first
                try { Nudge(target); } catch { }
            });
        } catch { }
    };

    public static IntPtr Install() {
        return SetWinEventHook(EVENT_OBJECT_CREATE, EVENT_OBJECT_CREATE, IntPtr.Zero, Proc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
    }

    public static void RunLoop() {
        MSG msg;
        while (GetMessage(out msg, IntPtr.Zero, 0, 0) != 0) {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
    }
}
'@

$hook = [WinEventNudge]::Install()
if ($hook -eq [IntPtr]::Zero) {
    # Hook install failed; do not hang.
    exit 1
}

# Graceful exit on Ctrl+C when a visible console is available.
$handler = [System.ConsoleCancelEventHandler] {
    param($s, $e)
    $e.Cancel = $true
    [WinEventNudge]::PostQuitMessage(0)
}
[Console]::CancelKeyPress.Add($handler)

# Message pump: keeps the process alive and dispatches hook callbacks.
[WinEventNudge]::RunLoop()
