// Services/CleanUrl.cs - URL 追踪参数清理（对齐 Web 版 lib/core/clips-store.js cleanUrl + TRACKING_KEYS）
// 24 个追踪参数（UTM 九项 + 渠道统计）；无追踪参数原样返回；无法解析（畸形）原样返回。
// 重建时保留原始 query 子串（不做重编码），保证"无参原样返回"与 Web 行为一致。
using System.Text.RegularExpressions;

namespace ClipboardExe.Services;

public static class CleanUrl
{
    /// <summary>追踪参数（v0.2.0：去追踪参数，保持链接干净）——逐字搬运自 clips-store.js TRACKING_KEYS。</summary>
    private static readonly HashSet<string> TrackingKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "utm_source", "utm_medium", "utm_campaign", "utm_term", "utm_content",
        "utm_id", "utm_source_platform", "utm_creative_format", "utm_marketing_tactic",
        "fbclid", "gclid", "gclsrc", "dclid", "msclkid", "mc_cid", "mc_eid",
        "igshid", "ref", "spm", "scm", "from", "from_sources", "clicktime", "clickid",
    };

    private static readonly Regex HttpRe = new(@"^https?://", RegexOptions.IgnoreCase);

    /// <summary>清理追踪参数；保留协议/主机/路径/其余参数，只剔除 TRACKING_KEYS（对齐 cleanUrl）。</summary>
    public static string Clean(string url)
    {
        // 非 http(s) 或空 → 原样（JS: if (!/^https?:\/\//i.test(url)) return url;）
        if (string.IsNullOrEmpty(url) || !HttpRe.IsMatch(url)) return url;
        var qIdx = url.IndexOf('?');
        // 无查询串 → 原样（JS: if (!u.search) return url;）
        if (qIdx < 0) return url;
        var basePart = url[..(qIdx + 1)];   // 含 '?'（JS: u.search 重置后 toString 保留 base）
        var query = url[(qIdx + 1)..];
        var kept = new List<string>();
        var removed = 0;
        foreach (var pair in query.Split('&'))
        {
            if (pair.Length == 0) continue;
            var eq = pair.IndexOf('=');
            var rawKey = eq < 0 ? pair : pair[..eq];
            // searchParams 的 key 是解码后的（如 %20 → 空格）；小写比对（TRACKING_KEYS 匹配不区分大小写）
            var key = Uri.UnescapeDataString(rawKey).ToLowerInvariant();
            if (TrackingKeys.Contains(key)) removed++;
            else kept.Add(pair);
        }
        // 无追踪参数 → 原样返回（对齐 JS: if (!removed) return url;）
        if (removed == 0) return url;
        // 全删 → base 去掉 '?'（JS: u.search = "" 后 toString 无 '?' 尾巴）
        return kept.Count == 0 ? basePart[..^1] : basePart + string.Join("&", kept);
    }
}
