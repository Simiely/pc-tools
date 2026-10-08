// Services/WebDavSync.cs - 同步配置 IO + 合并裁决核心（对齐 Web 版 lib/core/webdav.js）
// ① 配置 IO：data/webdav.json 读写 + saveSyncConfig 校验（url 非空 / http(s) / 首次密码非空 / 间隔裁剪）；
// ② 合并算法 mergeSnapshots：纯函数、无 IO、无副作用，与 scripts/test-merge-snapshot.mjs 对拍验收。
// 注：删除墓碑记录由 ClipService.BatchDelete 无条件写入（桌面单机形态，墓碑本地留存无害，且便于本地删除传播）；
//     与 Web「仅已配置同步时记录」语义为超集关系，不影响合并正确性。
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClipboardExe.Models;

namespace ClipboardExe.Services;

public static class WebDavSync
{
    public const int DefaultIntervalMin = 720; // 自动同步默认间隔：12 小时
    public const string DefaultUrl = "http://192.168.2.1:6086"; // 未配置过时的默认 WebDAV 地址
    public const int AutoMin = 30, AutoMax = 24 * 60; // 间隔范围 30 分钟 ~ 24 小时
    private const string ConfigFile = "webdav.json";

    private static readonly JsonSerializerOptions JsonOpt = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    // ---------- 配置 IO（对齐 webdav.js getSyncConfig/saveSyncConfig） ----------

    /// <summary>读同步配置；未配置（无文件/坏文件）返回 null。</summary>
    public static SyncConfig? LoadConfig(string dataDir)
    {
        var f = Path.Combine(dataDir, ConfigFile);
        if (!File.Exists(f)) return null;
        try
        {
            var c = JsonSerializer.Deserialize<SyncConfig>(File.ReadAllText(f), JsonOpt);
            if (c == null) return null;
            if (c.IntervalMin <= 0) c.IntervalMin = DefaultIntervalMin;
            try { MigrateSyncFilesOnce(dataDir, c); } catch { /* 迁移失败不影响读取 */ }
            return c;
        }
        catch { return null; }
    }

    /// <summary>v0.7.6 一次性迁移：存量 `syncFiles:false` 翻 true（对齐 webdav.js migrateSyncFilesDefaults）。
    ///
    /// **为什么必须在 exe 端也做**：`SyncConfig.SyncFiles` 的属性默认值 `= true` 只对
    /// 「JSON 里没有该字段」或新建对象生效——**已落盘的 `data/webdav.json` 里显式写的
    /// `"syncFiles": false` 会在反序列化时覆盖它**，于是 exe 端依旧不备份实体上云，
    /// 云端没有实体 → 他端（含 Web 版）同步后即便"拉回无条件"也拉不到东西。
    /// 这正是本次整条问题链的源头，只改属性默认值等于没改（与 Web 端同一个教训）。
    ///
    /// 只翻「从未迁移过」的配置：迁移后用户再主动关闭会被尊重（有标记就不再自动翻开）。</summary>
    private static void MigrateSyncFilesOnce(string dataDir, SyncConfig c)
    {
        if (c.SyncFilesMigrated) return;
        c.SyncFilesMigrated = true;
        if (!c.SyncFiles) c.SyncFiles = true;
        SaveConfig(dataDir, c); // 写回标记（只此一次）
    }

    /// <summary>写同步配置（UTF-8 无 BOM，camelCase 缩进，与既有数据文件一致）。</summary>
    public static void SaveConfig(string dataDir, SyncConfig cfg)
    {
        var f = Path.Combine(dataDir, ConfigFile);
        Directory.CreateDirectory(dataDir);
        File.WriteAllText(f, JsonSerializer.Serialize(cfg, JsonOpt), new System.Text.UTF8Encoding(false));
    }

    /// <summary>构建并校验配置（对齐 saveSyncConfig：pass 留空复用旧密码）。不写盘，由调用方先 testConnection 通过再 SaveConfig。</summary>
    public static SyncConfig ValidateAndBuild(string url, string user, string pass, bool syncFiles,
        bool autoSync, int intervalMin, SyncConfig? old)
    {
        var cfg = new SyncConfig
        {
            Url = (url ?? "").Trim(),
            User = (user ?? "").Trim(),
            Pass = string.IsNullOrEmpty(pass) ? (old?.Pass ?? "") : pass,
            SyncFiles = syncFiles,
            AutoSync = autoSync,
            IntervalMin = ClampInt(intervalMin, AutoMin, AutoMax, old?.IntervalMin ?? DefaultIntervalMin),
            LastSyncAt = old?.LastSyncAt ?? 0,
            LastSyncError = old?.LastSyncError ?? "",
            AccountName = old?.AccountName ?? "default",
            DisplayName = old?.DisplayName ?? "",
            PendingNameMigrations = old?.PendingNameMigrations ?? new List<string>(),
        };
        if (string.IsNullOrEmpty(cfg.Url)) throw new WebDavException(400, "WebDAV 服务器地址不能为空");
        if (!Regex.IsMatch(cfg.Url, "^https?://", RegexOptions.IgnoreCase))
            throw new WebDavException(400, "地址需以 http(s):// 开头");
        if (string.IsNullOrEmpty(cfg.Pass)) throw new WebDavException(400, "密码不能为空（首次配置）");
        return cfg;
    }

    private static int ClampInt(int v, int min, int max, int def)
        => (v < min || v > max) ? def : v;

    // ---------- 合并算法（对齐 webdav.js mergeSnapshots，纯函数） ----------
    //  - 同 id 条目取 updatedAt 新者
    //  - 墓碑合并取 deletedAt 新者
    //  - 裁决：墓碑 deletedAt > 条目 updatedAt → 删除；条目 updatedAt > 墓碑 deletedAt → 保留（删后又被编辑）
    //  - 清空语义：localTomb=[] 表示「全部清空不传播删除」——此时远端条目直接拉回
    public static (List<ClipItem> Clips, List<Tombstone> Tombstones) MergeSnapshots(
        List<ClipItem> localClips, List<Tombstone> localTomb, Snapshot? remoteSnap)
    {
        var byId = new Dictionary<string, ClipItem>(StringComparer.Ordinal);
        foreach (var c in localClips) byId[c.Id] = c;
        if (remoteSnap?.Clips != null)
        {
            foreach (var c in remoteSnap.Clips)
            {
                if (!byId.TryGetValue(c.Id, out var ex) || c.UpdatedAt > ex.UpdatedAt)
                    byId[c.Id] = c;
            }
        }

        var tombs = new Dictionary<string, long>(StringComparer.Ordinal);
        if (localTomb != null)
            foreach (var t in localTomb) tombs[t.Id] = t.DeletedAt;
        if (remoteSnap?.Tombstones != null)
        {
            foreach (var t in remoteSnap.Tombstones)
            {
                if (!tombs.TryGetValue(t.Id, out var ex))
                    tombs[t.Id] = t.DeletedAt;
                else if (t.DeletedAt > ex)
                    tombs[t.Id] = t.DeletedAt;
            }
        }

        var clips = new List<ClipItem>();
        foreach (var (id, c) in byId)
        {
            var delAt = tombs.TryGetValue(id, out var d) ? d : 0L;
            if (delAt > c.UpdatedAt) continue; // 墓碑裁决：删除
            clips.Add(c);
        }
        var tombstones = tombs.Select(kv => new Tombstone { Id = kv.Key, DeletedAt = kv.Value }).ToList();
        return (clips, tombstones);
    }
}
