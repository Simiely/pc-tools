// Services/AutoStart.cs - 开机自启动管理（注册表 Run 项）
//
// 实现参考 WindowTinter 的 Settings.ApplyStartWithWindows：把自身写入
// HKCU\Software\Microsoft\Windows\CurrentVersion\Run，值带 /startup 参数——
// 程序开机被拉起时据该参数判定"开机自启"→ 只驻留托盘、不显示主窗口（见 App.OnStartup）。
//
// 用注册表 Run（而非任务计划程序/启动文件夹）的理由：
//  - 轻量、无需提权（HKCU）、用户可在 任务管理器→启动 页直接开关，体验与主流工具一致。
//  - 值带 /startup 参数，程序自己就能区分"开机自启"和"手动双击"。
using System.Diagnostics;
using Microsoft.Win32;

namespace ClipboardExe.Services;

/// <summary>开机自启（HKCU Run 项）读写。值名固定，幂等 Enable/Disable。</summary>
public static class AutoStart
{
    /// <summary>注册表 Run 值名（任务管理器"启动"页显示的应用名取自 exe 文件描述/名称）。</summary>
    public const string RunValueName = "ClipboardTool";

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>开机自启的启动参数（App.OnStartup 据此静默进托盘）。</summary>
    public const string StartupArg = "/startup";

    /// <summary>当前是否已设开机自启（读注册表实际值，不以 settings.json 为准——用户可手动改/被任务管理器禁用）。</summary>
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            if (key == null) return false;
            return key.GetValue(RunValueName) is string s && s.Length > 0;
        }
        catch { return false; }
    }

    /// <summary>开启开机自启：写 Run 值 = &quot;exe路径&quot; /startup。返回是否成功。</summary>
    public static bool Enable()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return false;
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key == null) return false;
            // 引号包裹 exe 路径防路径含空格；/startup 供程序静默进托盘
            key.SetValue(RunValueName, $"\"{exe}\" {StartupArg}");
            return true;
        }
        catch (Exception ex) { Debug.WriteLine("AutoStart.Enable failed: " + ex.Message); return false; }
    }

    /// <summary>关闭开机自启：删除 Run 值（不存在则忽略）。返回是否成功。</summary>
    public static bool Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key == null) return true; // 键不存在 = 本来就未自启，视为成功
            key.DeleteValue(RunValueName, throwOnMissingValue: false);
            return true;
        }
        catch (Exception ex) { Debug.WriteLine("AutoStart.Disable failed: " + ex.Message); return false; }
    }
}
