// Services/Format.cs - 展示格式化纯函数（对齐 app.js fmtSize/fmtTime/expLabel/hostOf/looksLikeJson）
// 纯函数无 UI 依赖，SelfTest 可单测；规则逐字对齐 Web 版源码。
using System.Text.Json;

namespace ClipboardExe.Services;

public static class Format
{
    /// <summary>文件大小（对齐 app.js fmtSize：<1024 整数 B；<1MB 一位小数 KB；否则一位小数 MB）。</summary>
    public static string Size(long? n)
    {
        if (n == null || n == 0) return "0B";
        var v = n.Value;
        if (v < 1024) return v + "B";
        if (v < 1048576) return (v / 1024.0).ToString("0.0") + "KB";
        return (v / 1048576.0).ToString("0.0") + "MB";
    }

    /// <summary>完整时间（对齐 app.js fmtTime：2026/08/12 13:33；0 返回空串）。</summary>
    public static string Time(long? ts)
    {
        if (ts == null || ts == 0) return "";
        var d = DateTimeOffset.FromUnixTimeMilliseconds(ts.Value).ToLocalTime();
        return $"{d.Year}/{d.Month:00}/{d.Day:00} {d.Hour:00}:{d.Minute:00}";
    }

    /// <summary>过期倒计时文案（对齐 app.js expLabel；null 返回空串）。</summary>
    public static string ExpLabel(long? ts)
    {
        if (ts == null || ts == 0) return "";
        var left = ts.Value - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (left <= 0) return "已过期";
        if (left < 3600000) return Math.Ceiling(left / 60000.0) + " 分钟后过期";
        if (left < 86400000) return Math.Ceiling(left / 3600000.0) + " 小时后过期";
        return Math.Ceiling(left / 86400000.0) + " 天后过期";
    }

    /// <summary>从 URL 提取域名（对齐 app.js hostOf：new URL 失败回退去协议取首段）。</summary>
    public static string HostOf(string? url)
    {
        var u = url ?? "";
        try
        {
            if (Uri.TryCreate(u, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https"))
                return uri.Host;
        }
        catch { /* 回退 */ }
        return u.Replace("https://", "").Replace("http://", "").Split('/')[0];
    }

    /// <summary>JSON 形检测（对齐 app.js looksLikeJson：首字符 {/[、≤100KB、可解析；不满足返回 false）。</summary>
    public static bool LooksLikeJson(string? s)
    {
        var t = (s ?? "").Trim();
        if (t.Length == 0 || (t[0] != '{' && t[0] != '[')) return false;
        if (t.Length > 100000) return false;
        try { JsonDocument.Parse(t); return true; } catch { return false; }
    }

    /// <summary>美化 JSON（对齐 openJsonPreview：解析成功缩进 2，失败原样）。
    /// 注意：.NET 9 JsonSerializer 缩进默认 CRLF（实测），Web JSON.stringify 是 LF——统一替换为 LF，保证互导字节一致。</summary>
    public static string PrettyJson(string content)
    {
        try
        {
            return JsonSerializer.Serialize(JsonDocument.Parse(content).RootElement,
                       new JsonSerializerOptions { WriteIndented = true })
                   .Replace("\r\n", "\n");
        }
        catch { return content; }
    }

    /// <summary>自动标题：内容首行前 20 字（对齐 savePasteContent autoTitle）。</summary>
    public static string AutoTitle(string? content)
    {
        var first = (content ?? "").Split('\n')[0].Trim();
        return first.Length > 20 ? first[..20] : first;
    }

    /// <summary>文件图标类型判定（对齐 app.js makeFileIcon：.pdf → pdf 红边；.zip/.rar/.7z/.tar/.gz → zip 金边；其余 file）。</summary>
    public static string FileKindFor(string? name)
    {
        var n = (name ?? "").ToLowerInvariant();
        if (n.EndsWith(".pdf")) return "pdf";
        if (System.Text.RegularExpressions.Regex.IsMatch(n, @"\.(zip|rar|7z|tar|gz)$")) return "zip";
        return "file";
    }

    /// <summary>是否图片（M3b-2b：fileMime 起始 image/；对齐 app.js handleCardClick/bindImageHoverPreview/file-chip 判定）。</summary>
    public static bool IsImageMime(string? mime) => (mime ?? "").StartsWith("image/", StringComparison.OrdinalIgnoreCase);
}
