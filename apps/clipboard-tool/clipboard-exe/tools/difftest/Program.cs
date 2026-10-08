// tools/difftest/Program.cs - exe 侧规则 runner:读 shared fixtures(JSON),
// 用生产代码 Storage.SortClips / ClipService.FindDuplicate / CleanUrl.Clean / ClipService.IsExpired 计算,
// 逐 fixture 输出一行 JSON 到 stdout。
// 输出协议(供 tests/diff-rules.mjs 对拍):
//   {"t":"s","n":"<sort fixture name>","ids":[...]}         排序结果 = id 序列
//   {"t":"d","n":"<dedup fixture name>","id":"<id>|null"}   去重结果 = 命中条目 id 或 null
//   {"t":"u","n":"<cleanUrl fixture name>","v":"<cleaned>"}  URL 清理结果
//   {"t":"e","n":"<expiry fixture name>","v":true|false}     过期判定(isExpired)
using System.Text.Json;
using ClipboardExe.Models;
using ClipboardExe.Services;

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: difftest <fixtures.json>");
    return 2;
}

var file = args[0];
if (!File.Exists(file))
{
    Console.Error.WriteLine("fixtures not found: " + file);
    return 2;
}

using var doc = JsonDocument.Parse(File.ReadAllText(file));
var root = doc.RootElement;

// ---- 去重 ----
if (root.TryGetProperty("dedup", out var dedupArr))
{
    foreach (var fx in dedupArr.EnumerateArray())
    {
        var name = fx.GetProperty("name").GetString()!;
        var probe = fx.TryGetProperty("probe", out var p) && p.ValueKind != JsonValueKind.Null
            ? p.GetString()
            : null;
        var clips = ReadClips(fx, "clips");
        var hit = ClipService.FindDuplicate(probe, clips);
        Console.WriteLine(JsonSerializer.Serialize(new { t = "d", n = name, id = hit?.Id }));
    }
}

// ---- 排序 ----
if (root.TryGetProperty("sort", out var sortArr))
{
    foreach (var fx in sortArr.EnumerateArray())
    {
        var name = fx.GetProperty("name").GetString()!;
        var clips = ReadClips(fx, "clips");
        var sorted = Storage.SortClips(clips);
        Console.WriteLine(JsonSerializer.Serialize(new { t = "s", n = name, ids = sorted.Select(c => c.Id).ToArray() }));
    }
}

// ---- URL 清理 ----
if (root.TryGetProperty("cleanUrl", out var cleanArr))
{
    foreach (var fx in cleanArr.EnumerateArray())
    {
        var name = fx.GetProperty("name").GetString()!;
        var input = fx.GetProperty("input").GetString() ?? "";
        var cleaned = CleanUrl.Clean(input);
        Console.WriteLine(JsonSerializer.Serialize(new { t = "u", n = name, v = cleaned }));
    }
}

// ---- 过期判定（isExpired；fixture 用远离时钟的定值，无竞态）----
if (root.TryGetProperty("expiry", out var expiryArr))
{
    foreach (var fx in expiryArr.EnumerateArray())
    {
        var name = fx.GetProperty("name").GetString()!;
        var clip = ReadOneClip(fx, "clip");
        var expired = ClipService.IsExpired(clip);
        Console.WriteLine(JsonSerializer.Serialize(new { t = "e", n = name, v = expired }));
    }
}

return 0;

static List<ClipItem> ReadClips(JsonElement fx, string key)
{
    var list = new List<ClipItem>();
    if (!fx.TryGetProperty(key, out var arr)) return list;
    foreach (var el in arr.EnumerateArray())
    {
        // 序列化回原始 JSON 文本再走生产反序列化(ClipItem 显式 JsonPropertyName,与发布数据同路径)
        var item = JsonSerializer.Deserialize<ClipItem>(el.GetRawText());
        if (item != null) list.Add(item);
    }
    return list;
}

static ClipItem ReadOneClip(JsonElement fx, string key)
{
    if (fx.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.Object)
    {
        var item = JsonSerializer.Deserialize<ClipItem>(el.GetRawText());
        if (item != null) return item;
    }
    return new ClipItem();
}
