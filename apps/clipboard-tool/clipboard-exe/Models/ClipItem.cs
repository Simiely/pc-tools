// Models/ClipItem.cs - 条目模型（对齐 Web 版 lib/core/clips-store.js publicClip 字段）
// 序列化 camelCase + 显式 JsonPropertyName，与 Web 导出 JSON 同构（M4 互导零返工）。
// archived 是 publicClip 输出标记（条目来自归档区时前端只读），存储不落盘 → JsonIgnore。
using System.Text.Json.Serialization;

namespace ClipboardExe.Models;

public sealed class ClipItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>text | link | file（createClip 白名单，非法回退 text）。</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";

    /// <summary>富文本（可选；与 content 并存，前端双按钮复制）。</summary>
    [JsonPropertyName("html")]
    public string Html { get; set; } = "";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    [JsonPropertyName("fileId")]
    public string FileId { get; set; } = "";

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = "";

    [JsonPropertyName("fileSize")]
    public long FileSize { get; set; }

    [JsonPropertyName("fileMime")]
    public string FileMime { get; set; } = "";

    [JsonPropertyName("copyCount")]
    public long CopyCount { get; set; }

    [JsonPropertyName("pinned")]
    public bool Pinned { get; set; }

    /// <summary>只读输出标记（非存储字段，对齐 publicClip 的 archived 推导）。</summary>
    [JsonIgnore]
    public bool Archived { get; set; }

    /// <summary>过期绝对时间戳（ms）；null = 永久（对齐 isExpired/resolveExpire）。</summary>
    [JsonPropertyName("expireAt")]
    public long? ExpireAt { get; set; }

    /// <summary>创建时间戳（ms，对齐 Date.now()）。</summary>
    [JsonPropertyName("createdAt")]
    public long CreatedAt { get; set; }

    /// <summary>最近更新时间戳（ms，排序/归档依据）。</summary>
    [JsonPropertyName("updatedAt")]
    public long UpdatedAt { get; set; }
}
