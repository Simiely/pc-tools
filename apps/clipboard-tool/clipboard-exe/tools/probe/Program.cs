// tools/probe/Program.cs - probe v3：在「真实 WPF 窗口 + 真实消息泵」下验证 v0.7.0 的确定性判定
//
// 为什么必须有这一版：v2 是在控制台（无 Dispatcher、无真实窗口）里测的，而产品是 WPF 窗口 + STA + 真实 HWND。
// GetClipboardOwner 归属在真实 GUI 下是否同样成立，搜索给不出确定答案，只能实测。
//
// 被测对象：直接编译生产源文件 ClipboardNative.cs / ClipboardWatcher.cs / ClipboardHelper.cs（见 csproj），
//   测的就是产品跑的那份代码，不是复刻品。
//
// 验证的四个断言（对应"弹两次窗"根因的两条修复）：
//   S1 本程序写入 → watcher 不触发（owner PID 判定，替代 800ms 时间窗）
//   S2 外部写入   → watcher 触发 1 次
//   S3 外部连续写入相同内容两次 → 触发 2 次（序列号递增 = 真实新复制，不该被吞）
//   S4 本程序写入后等 3 秒（远超原 800ms 窗、也超实测 1943ms 事件延迟）→ 仍 0 次（判定与时间无关）
//
// 注意：会占用并覆盖系统剪贴板约 10 秒，结束时自动恢复原文本。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using ClipboardExe.Services;

namespace ClipProbe;

internal static class Program
{
    private static int _events;
    private static readonly List<string> _trail = new();

    [STAThread]
    private static int Main()
    {
        Console.WriteLine("=== clipprobe v3：真实 WPF 窗口下的自写判定 / 序列号判重 ===");
        Console.WriteLine("占用剪贴板约 10 秒，结束后自动恢复。\n");

        string backup = "";
        try { backup = System.Windows.Clipboard.GetText() ?? ""; }
        catch { /* 无文本 */ }
        Console.WriteLine($"[备份] 原剪贴板文本长度 = {backup.Length}");

        var app = new Application();
        var win = new Window { Width = 320, Height = 180, Title = "clipprobe-v3", Topmost = false };
        ClipboardWatcher? watcher = null;

        win.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(win).Handle;
            // Paused = false：沙箱内无法真正激活窗口，这里强制开启捕获以测 watcher 判定本身
            watcher = new ClipboardWatcher(hwnd) { Paused = false };
            watcher.ClipboardChanged += () =>
            {
                _events++;
                _trail.Add($"      事件 #{_events} @ {DateTime.Now:HH:mm:ss.fff} seq={ClipboardNative.SequenceNumber}");
            };
            watcher.Attach();
        };

        win.Loaded += (_, _) => { _ = RunAsync(win, app); };

        app.Run(win);

        // 恢复剪贴板
        try
        {
            if (backup.Length > 0) ClipboardHelper.SetText(backup);
            var now = "";
            try { now = System.Windows.Clipboard.GetText() ?? ""; } catch { }
            Console.WriteLine($"\n[恢复] 剪贴板已写回，长度={backup.Length}，校验长度={now.Length}");
        }
        catch (Exception ex) { Console.WriteLine("[恢复] 失败: " + ex.Message); }

        return 0;
    }

    private static async Task RunAsync(Window win, Application app)
    {
        var results = new List<(string Name, bool Ok, string Detail)>();

        try
        {
            await Task.Delay(300); // 等 watcher 就绪

            // ---- S0：核心风险点直查 —— 真实 GUI 下，生产写入路径是否把 owner 落在本进程 ----
            ClipboardHelper.SetText("PROBE-OWNER-CHECK");
            await Task.Delay(200);
            var ownedBySelf = ClipboardNative.IsOwnedByThisProcess;
            results.Add(("S0 生产路径写入后 owner 属本进程", ownedBySelf,
                $"IsOwnedByThisProcess={ownedBySelf}（本进程 PID={Environment.ProcessId}）"));

            // ---- S1：本程序写入 → 不应触发 ----
            var before = _events;
            ClipboardHelper.SetText("SELF-1");
            await Task.Delay(1000);
            results.Add(("S1 本程序写入→watcher 不触发", _events == before, $"事件数 {before} → {_events}"));

            // ---- S2：外部写入 → 应触发 1 次 ----
            before = _events;
            ExternalCopy("EXTERNAL-1");
            await Task.Delay(1000);
            results.Add(("S2 外部写入→触发 1 次", _events == before + 1, $"事件数 {before} → {_events}"));

            // ---- S3：外部连续写入相同内容两次 → 应触发 2 次（序列号递增 = 真实新复制）----
            before = _events;
            var seqA = ClipboardNative.SequenceNumber;
            ExternalCopy("SAME-CONTENT");
            await Task.Delay(600);
            var seqB = ClipboardNative.SequenceNumber;
            ExternalCopy("SAME-CONTENT");
            await Task.Delay(600);
            var seqC = ClipboardNative.SequenceNumber;
            var seqGrew = seqB > seqA && seqC > seqB;
            results.Add(("S3 相同内容连复两次→触发 2 次", _events == before + 2 && seqGrew,
                $"事件数 {before} → {_events}；seq {seqA}→{seqB}→{seqC}（递增={seqGrew}）"));

            // ---- S4：延迟无关性 —— 本程序写入后等 3 秒（远超原 800ms 窗与实测 1943ms 延迟）----
            before = _events;
            ClipboardHelper.SetText("SELF-DELAY-CHECK");
            await Task.Delay(3000);
            results.Add(("S4 本程序写入后等 3 秒→仍 0 次（与时间无关）", _events == before,
                $"事件数 {before} → {_events}"));

            // ---- S5：交替序列 —— 外部 / 自写 / 外部 ----
            before = _events;
            ExternalCopy("MIX-EXT-1"); await Task.Delay(700);
            ClipboardHelper.SetText("MIX-SELF"); await Task.Delay(700);
            ExternalCopy("MIX-EXT-2"); await Task.Delay(700);
            results.Add(("S5 外部→自写→外部→共触发 2 次（自写被拦）", _events == before + 2,
                $"事件数 {before} → {_events}"));

            foreach (var t in _trail) Console.WriteLine(t);
        }
        catch (Exception ex)
        {
            Console.WriteLine("探测异常: " + ex);
        }

        Console.WriteLine("\n=== 结果 ===");
        foreach (var (name, ok, detail) in results)
            Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}\n      {detail}");

        var allPass = results.All(r => r.Ok);
        Console.WriteLine($"\n总计：{results.Count(r => r.Ok)}/{results.Count} 通过 → {(allPass ? "方案 A+A2 成立" : "存在失败项，需复查")}");

        win.Close();
        app.Shutdown();
    }

    /// <summary>模拟外部程序写入剪贴板（clip.exe 无窗口，owner=0，代表"别人复制的"）。</summary>
    private static void ExternalCopy(string text)
    {
        var psi = new ProcessStartInfo("clip.exe")
        {
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi)!;
        p.StandardInput.Write(text);
        p.StandardInput.Close();
        p.WaitForExit();
    }
}
