// Services/ClipboardNative.cs - 剪贴板原生判定（确定性；替代「时间窗 + 内容比对」启发式）
// 为什么需要它（v0.7.0 修复前的两个真实缺陷，均已用 tools/probe 实测复现）：
//   1) 800ms 抑制窗：Suppress 在 SetText 返回之后调用，而写入若走 WPF 兜底路径耗时 ~978ms、
//      事件延迟 ~1943ms → 1943 − 978 = 965ms > 800ms，抑制窗必然失效，表现为「复制后弹两次窗」。
//      根因是时间窗依赖事件到达时序，而时序不可控。
//   2) 内容字符串比对（_lastPrompted）：实测相同内容再次写入，系统序列号照样递增（14566→14576），
//      即"用户连续复制同一段文字"是真实的新复制，内容比对却把它判为重复并吞掉弹窗。
// 权威依据（MS Learn）：
//   - GetClipboardSequenceNumber：系统为 window station 维护的序列号，剪贴板内容改变或清空时递增。
//     → 用「序列号是否变化」判定是不是一次新复制，与内容是否相同无关（A2）。
//   - GetClipboardOwner：返回最后一次 EmptyClipboard 之后放入数据的窗口 HWND。
//     → 配 GetWindowThreadProcessId 比对自身 PID，确定性判定"是不是本程序自己写的"（A）。
// 实测（tools/probe，走生产 ClipboardHelper 写入路径）：本进程写入后 owner 属本进程 5/5 全对；
// 外部 clip.exe 写入 owner = 0。两条判定都不依赖时间：事件 2ms 到达还是 2s 到达，结论一致。
using System;
using System.Runtime.InteropServices;

namespace ClipboardExe.Services;

public static class ClipboardNative
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardOwner();

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    // 本进程 PID（进程级常量，取一次即可；Environment.ProcessId 比 Process.GetCurrentProcess() 无分配）
    private static readonly uint SelfPid = (uint)Environment.ProcessId;

    /// <summary>当前剪贴板序列号：内容改变或清空时由系统递增（window station 级，跨进程单调）。</summary>
    public static uint SequenceNumber => GetClipboardSequenceNumber();

    /// <summary>最后写入剪贴板的窗口是否属于本进程 —— 即"这次变化是本程序自己造成的"。
    /// owner = 0（无窗口进程如 clip.exe 写入、或延迟渲染尚未落主）或属于其他进程 → false。</summary>
    public static bool IsOwnedByThisProcess
    {
        get
        {
            var owner = GetClipboardOwner();
            if (owner == IntPtr.Zero) return false;
            return GetWindowThreadProcessId(owner, out var pid) != 0 && pid == SelfPid;
        }
    }
}
