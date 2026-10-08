// Models/Tombstone.cs - 删除墓碑（对齐 Web 版 tombstones.js）
// 用于 WebDAV 同步时传播删除：记录被删条目 id 与时间，防止旧备份把已删条目复活。
using System.Text.Json.Serialization;

namespace ClipboardExe.Models;

public sealed class Tombstone
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("deletedAt")]
    public long DeletedAt { get; set; }
}
