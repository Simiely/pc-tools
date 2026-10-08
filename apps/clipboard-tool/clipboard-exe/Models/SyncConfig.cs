// Models/SyncConfig.cs - WebDAV 同步配置 + 远端快照模型（对齐 Web 版 lib/core/webdav.js）
// 单机形态：配置单份，存 data/webdav.json；账号名（accountName）为不可变身份键，决定远端快照寻址。
using System.Text.Json.Serialization;

namespace ClipboardExe.Models;

/// <summary>WebDAV 同步配置（对齐 webdav.js saveSyncConfig 字段）。</summary>
public sealed class SyncConfig
{
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("user")] public string User { get; set; } = "";
    [JsonPropertyName("pass")] public string Pass { get; set; } = "";
    // v0.7.6：默认 true（对齐 Web 端 saveSyncConfig）。此前默认 false —— 用户根本不知道有这个开关，
    // 结果「条目同步过去了、图片/附件在另一台设备下载失败」。
    // 注意：JSON 反序列化时若文件里显式写了 "syncFiles": false，会覆盖此默认值（尊重用户既有选择）。
    [JsonPropertyName("syncFiles")] public bool SyncFiles { get; set; } = true;
    [JsonPropertyName("autoSync")] public bool AutoSync { get; set; }
    [JsonPropertyName("intervalMin")] public int IntervalMin { get; set; }
    [JsonPropertyName("lastSyncAt")] public long LastSyncAt { get; set; }
    [JsonPropertyName("lastSyncError")] public string LastSyncError { get; set; } = "";
    // v0.7.6：一次性迁移标记（对齐 Web 端 webdav.json 的 v0706SyncFiles）。
    // 置 true 表示「本份配置已经过 syncFiles 默认值迁移」，此后用户主动关闭不会被再次翻开。
    [JsonPropertyName("v0706SyncFiles")] public bool SyncFilesMigrated { get; set; }
    [JsonPropertyName("accountName")] public string AccountName { get; set; } = "default";
    // v0.7.x：账号昵称（displayName，可随时改，随快照 nickname 同步到同账号其它设备，仅影响展示）
    // 服务器为纯 WebDAV 盘（无用户库）时，昵称 = 快照顶层 nickname 字段 + 本地 SyncConfig 记忆；本地权威、上传写远端、首次换机采纳远端。
    [JsonPropertyName("displayName")] public string DisplayName { get; set; } = "";
    [JsonPropertyName("pendingNameMigrations")]
    public List<string> PendingNameMigrations { get; set; } = new();
}

/// <summary>远端快照（对齐 webdav.js runSync 上传格式 {app,version,syncedAt,clips,tombstones}）。
/// v0.7.x 追加可选 nickname：账号昵称（displayName）随快照在纯 WebDAV 盘上跨设备传播；无该字段 = 旧格式（用账号名兜底）。</summary>
public sealed class Snapshot
{
    [JsonPropertyName("app")] public string App { get; set; } = "clipboard";
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("syncedAt")] public long SyncedAt { get; set; }
    [JsonPropertyName("nickname")] public string Nickname { get; set; } = "";
    [JsonPropertyName("clips")] public List<ClipItem> Clips { get; set; } = new();
    [JsonPropertyName("tombstones")] public List<Tombstone> Tombstones { get; set; } = new();
}
