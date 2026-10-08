// Services/Settings.cs - 应用偏好持久化（data/settings.json）：置顶状态 + 列数偏好
// ⚠️ Load() 用显式字段拷贝（new Settings(...) { A = loaded.A, B = loaded.B, ... }），
// 加新字段必须同步扩展拷贝逻辑——反序列化做不到把 nested JsonElement 拷回强类型（camelCase 命名也帮不上）。
using System.IO;
using System.Text.Json;

namespace ClipboardExe.Services;

public sealed class Settings
{
    /// <summary>窗口置顶（★ 按钮状态）。</summary>
    public bool AlwaysOnTop { get; set; }

    /// <summary>列数偏好：0=自适应（默认），1~4=用户锁定（M3b-1 接入）。</summary>
    public int MaxColumns { get; set; }

    /// <summary>开机自启意图（上次用户在顶栏按钮选的态）。实际是否生效以注册表 Run 值为准，
    /// App 启动时用 AutoStart.IsEnabled() 反映真实态（用户可被任务管理器禁用/手动改注册表）。</summary>
    public bool LaunchAtStartup { get; set; }

    private readonly string _file;

    /// <summary>JsonSerializer.Deserialize 需要 public 无参 ctor（私有带参 ctor 不算）。
    /// 缺它 → 反序列化抛异常 → Load 的 catch 静默吞 → AlwaysOnTop 永远默认 false → 置顶状态无法恢复。</summary>
    public Settings() { }

    private Settings(string dataDir)
    {
        _file = Path.Combine(dataDir, "settings.json");
    }

    public static Settings Load(string dataDir)
    {
        var s = new Settings(dataDir);
        try
        {
            if (File.Exists(s._file))
            {
                var loaded = JsonSerializer.Deserialize<Settings>(File.ReadAllText(s._file));
                if (loaded != null) return new Settings(dataDir)
                {
                    AlwaysOnTop = loaded.AlwaysOnTop,
                    MaxColumns = loaded.MaxColumns,
                    LaunchAtStartup = loaded.LaunchAtStartup,
                };
            }
        }
        catch { /* 损坏则用默认 */ }
        return s;
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_file);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_file, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* 保存失败不影响运行 */ }
    }
}
