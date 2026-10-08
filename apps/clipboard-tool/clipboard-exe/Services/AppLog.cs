// Services/AppLog.cs - 运行日志（data/clipboard-exe.log，首行版本指纹，对齐 Web 版"实例身份可追溯"约定）
using System.IO;

namespace ClipboardExe.Services;

public static class AppLog
{
    private static string _logFile = "";
    private static readonly object Sync = new();

    /// <summary>初始化：写入版本指纹首行（每次启动重写，保留当次运行完整记录）。失败不阻塞启动。</summary>
    public static void Init(string dataDir, string versionLine)
    {
        try
        {
            Directory.CreateDirectory(dataDir);
            _logFile = Path.Combine(dataDir, "clipboard-exe.log");
            lock (Sync) File.WriteAllText(_logFile, versionLine + Environment.NewLine);
        }
        catch { /* 日志失败不影响运行 */ }
    }

    public static void Info(string msg)
    {
        if (string.IsNullOrEmpty(_logFile)) return;
        try
        {
            lock (Sync)
                File.AppendAllText(_logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}{Environment.NewLine}");
        }
        catch { /* 日志失败不影响运行 */ }
    }
}
