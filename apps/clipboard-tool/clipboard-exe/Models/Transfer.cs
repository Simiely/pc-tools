// Models/Transfer.cs - 导入导出数据包（对齐 Web 版 lib/core/clips-transfer.js exportClips 同构）
//  - ExportDoc：{app,version,exportedAt,clips[]}（app 用 "clipboard" 与 Web 实际导出一致，互导零返工）
//  - ImportResult：{added,updated,skipped,total}（对齐 importClips 返回）
using System.Text.Json.Serialization;

namespace ClipboardExe.Models;

/// <summary>导出包（含归档，合并为扁平 clips[]）。对齐 Web 版 exportClips。</summary>
public sealed class ExportDoc
{
    /// <summary>格式标记。Web 实际导出为 "clipboard"（非文档旧版的 "clipboard-tool"）——以运行中的 Web 源码为准，保证互导。</summary>
    [JsonPropertyName("app")]
    public string App { get; set; } = "clipboard";

    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("exportedAt")]
    public long ExportedAt { get; set; }

    /// <summary>条目数组；可为 null（反序列化缺失时用于判定"不是剪贴板备份文件"）。</summary>
    [JsonPropertyName("clips")]
    public List<ClipItem>? Clips { get; set; }
}

/// <summary>导入合并结果（对齐 Web 版 importClips 返回 {added,updated,skipped,total}）。</summary>
public sealed class ImportResult
{
    public int Added { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public int Total { get; set; }
}
