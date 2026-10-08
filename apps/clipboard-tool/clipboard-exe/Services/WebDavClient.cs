// Services/WebDavClient.cs - WebDAV HTTP 客户端（对齐 Web 版 lib/core/webdav.js）
// 零依赖：HttpClient 直连 + Basic 认证 + 超时（10s）。支持 MKCOL/GET/PUT，含目录逐级创建、
// 连通测试（写探针+读回校验）、远端快照拉取/上传。URL 方案与 Web 版一致：
//   <根>/workbuddy/剪贴板/ + 按账号名寻址 clipboard-<accountName>.json（v0.6.13 双名模型）。
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClipboardExe.Models;

namespace ClipboardExe.Services;

/// <summary>WebDAV 操作异常（对齐 webdav.js httpError：携带 HTTP 语义 code）。</summary>
public sealed class WebDavException : Exception
{
    public int Code { get; }
    public WebDavException(int code, string message) : base(message) => Code = code;
}

/// <summary>WebDAV HTTP 客户端（对齐 webdav.js 函数级实现）。</summary>
public static class WebDavClient
{
    private static readonly HttpClient Http = new();
    private const int REQ_TIMEOUT_MS = 10_000; // 与前端 fetch 超时一致（防挂起）
    private const string REMOTE_SUBDIR = "workbuddy/剪贴板"; // v0.6.7 起统一子目录（与根其他用途隔离）
    private static readonly JsonSerializerOptions JsonOpt = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    // ---------- URL 构建（对齐 webdav.js dirUrl/dataDirUrl/safeName/snapUrl） ----------

    private static string DirUrl(string url) => url.TrimEnd('/') + "/";

    private static string DataDirUrl(SyncConfig c)
    {
        var b = DirUrl(c.Url);
        foreach (var s in REMOTE_SUBDIR.Split('/'))
            if (!string.IsNullOrEmpty(s)) b += s + "/";
        return b;
    }

    /// <summary>用户名 → 远端路径安全名（剔除 / ? # 等路径破坏字符，保留中文可读）。</summary>
    internal static string SafeName(string name)
    {
        if (string.IsNullOrEmpty(name)) name = "u";
        var s = Regex.Replace(name, @"[\/\\?%#&:=""<>|*]", "_");
        return s.Length > 80 ? s.Substring(0, 80) : s;
    }

    /// <summary>快照文件 URL（v0.6.13 起按账号名寻址：clipboard-&lt;账号名&gt;.json）。</summary>
    internal static string SnapUrl(SyncConfig c, string nameOrId)
        => DataDirUrl(c) + "clipboard-" + SafeName(nameOrId) + ".json";

    /// <summary>对外暴露快照 URL 计算（供同步引擎/测试使用）。</summary>
    public static string SnapUrlFor(SyncConfig c, string name) => SnapUrl(c, name);

    // ---------- 底层 fetch 封装（对齐 davFetch：Basic 认证 + 超时，返回 status/buf/text） ----------

    private static async Task<(int Status, byte[] Buf, string Text)> DavFetch(
        SyncConfig c, string url, HttpMethod method, string? body = null)
    {
        using var cts = new CancellationTokenSource(REQ_TIMEOUT_MS);
        using var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization =
            new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(c.User + ":" + c.Pass)));
        if (body != null)
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        try
        {
            using var resp = await Http.SendAsync(req, cts.Token);
            var buf = await resp.Content.ReadAsByteArrayAsync();
            return ((int)resp.StatusCode, buf, Encoding.UTF8.GetString(buf));
        }
        catch (OperationCanceledException)
        {
            throw new WebDavException(502, "WebDAV 连接失败: 请求超时");
        }
        catch (Exception e)
        {
            throw new WebDavException(502, "WebDAV 连接失败: " + e.Message);
        }
    }

    // ---------- 目录 / 连通 / 快照 I/O（对齐 ensureDir/testConnection/fetchRemoteSnapshot/uploadSnapshot） ----------

    /// <summary>逐级创建「根 → workbuddy/ → workbuddy/剪贴板/」；已存在/不支持均容忍。</summary>
    public static async Task EnsureDir(SyncConfig c)
    {
        var segs = new List<string> { DirUrl(c.Url) };
        foreach (var s in REMOTE_SUBDIR.Split('/'))
            if (!string.IsNullOrEmpty(s)) segs.Add(s + "/");
        var cur = DirUrl(c.Url);
        for (var i = 1; i < segs.Count; i++)
        {
            cur += segs[i];
            var r = await DavFetch(c, cur, new HttpMethod("MKCOL"));
            if (r.Status == 401 || r.Status == 403)
                throw new WebDavException(401, "WebDAV 认证失败（检查用户名/密码）");
            if (!new[] { 201, 204, 200, 301, 405 }.Contains(r.Status))
                throw new WebDavException(502, "WebDAV 目录不可用（HTTP " + r.Status + "）");
        }
    }

    /// <summary>连通测试：确保目录 + 写探针 + 读回校验；失败抛错（配置不保存）。</summary>
    public static async Task TestConnection(SyncConfig c)
    {
        await EnsureDir(c);
        var probe = DataDirUrl(c) + ".clipboard-probe";
        var put = await DavFetch(c, probe, HttpMethod.Put, "ok");
        if (!new[] { 201, 204, 200 }.Contains(put.Status))
            throw new WebDavException(502, "WebDAV 不可写（PUT 返回 " + put.Status + "）");
        var get = await DavFetch(c, probe, HttpMethod.Get);
        if (get.Status != 200 || get.Text != "ok")
            throw new WebDavException(502, "WebDAV 读回校验失败");
    }

    /// <summary>拉远端快照（按 URL）：404 → null；其他错误抛错。</summary>
    public static async Task<Snapshot?> FetchRemoteSnapshot(SyncConfig c, string url)
    {
        var r = await DavFetch(c, url, HttpMethod.Get);
        if (r.Status == 404) return null;
        if (r.Status != 200)
            throw new WebDavException(502, "拉取远端备份失败（HTTP " + r.Status + "）");
        try
        {
            var snap = JsonSerializer.Deserialize<Snapshot>(r.Text, JsonOpt);
            if (snap == null || snap.Clips == null) throw new Exception("格式错误");
            return snap;
        }
        catch
        {
            throw new WebDavException(502, "远端备份文件损坏或格式不兼容");
        }
    }

    // ---------- 远端账号枚举（v0.7.x 本地单账号切换远端账号：PROPFIND 列目录找 clipboard-<账号名>.json） ----------

    /// <summary>列出远端 workbuddy/剪贴板/ 下已存在的账号快照名（clipboard-&lt;名&gt;.json → 名）。
    /// 返回远端真实存在的账号名集合（去重保序）；服务器不支持 PROPFIND(405/501) 时抛 WebDavException 提示。
    /// 供「本地无账号首次同步 → 选远端账号拉回」使用。</summary>
    public static async Task<List<string>> ListRemoteAccountNames(SyncConfig c)
    {
        var url = DataDirUrl(c);
        var names = new List<string>();
        using (var cts = new CancellationTokenSource(REQ_TIMEOUT_MS))
        using (var req = new HttpRequestMessage(new HttpMethod("PROPFIND"), url))
        {
            req.Headers.Authorization =
                new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(c.User + ":" + c.Pass)));
            req.Headers.Add("Depth", "1");
            // 部分服务器需请求体；空体 + 标准 propfind 是普遍兼容做法
            req.Content = new StringContent(
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                "<D:propfind xmlns:D=\"DAV:\"><D:prop><D:displayname/></D:prop></D:propfind>",
                Encoding.UTF8, "application/xml");
            try
            {
                using var resp = await Http.SendAsync(req, cts.Token);
                var code = (int)resp.StatusCode;
                if (code == 401 || code == 403)
                    throw new WebDavException(401, "WebDAV 认证失败（检查用户名/密码）");
                if (code == 405 || code == 501)
                    throw new WebDavException(502, "该 WebDAV 服务器不支持列出账号（PROPFIND 不可用）");
                if (code != 207 && code != 200)
                    throw new WebDavException(502, "列出远端账号失败（HTTP " + code + "）");
                var buf = await resp.Content.ReadAsByteArrayAsync();
                ParseClipboardHrefs(Encoding.UTF8.GetString(buf), names);
            }
            catch (OperationCanceledException)
            {
                throw new WebDavException(502, "WebDAV 连接失败: 请求超时");
            }
            catch (Exception e) when (e is not WebDavException)
            {
                throw new WebDavException(502, "WebDAV 连接失败: " + e.Message);
            }
        }
        return names;
    }

    /// <summary>从 PROPFIND 207 XML 中提取所有形如 clipboard-&lt;名&gt;.json 的 href，解码出账号名（去重保序）。</summary>
    private static void ParseClipboardHrefs(string xml, List<string> into)
    {
        if (string.IsNullOrEmpty(xml)) return;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var hrefPattern = new Regex("<[A-Za-z0-9_]*:?href>([^<]+)</", RegexOptions.Compiled);
        foreach (System.Text.RegularExpressions.Match m in hrefPattern.Matches(xml))
        {
            var href = m.Groups[1].Value.Trim();
            var idx = href.LastIndexOf("clipboard-", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) continue;
            var tail = href.Substring(idx + "clipboard-".Length);
            if (!tail.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
            var name = Uri.UnescapeDataString(tail.Substring(0, tail.Length - ".json".Length));
            if (!string.IsNullOrEmpty(name) && seen.Add(name)) into.Add(name);
        }
    }

    /// <summary>上传快照（按 URL）。</summary>
    public static async Task UploadSnapshot(SyncConfig c, string url, Snapshot snap)
    {
        var r = await DavFetch(c, url, HttpMethod.Put, JsonSerializer.Serialize(snap, JsonOpt));
        if (!new[] { 201, 204, 200 }.Contains(r.Status))
            throw new WebDavException(502, "上传远端备份失败（HTTP " + r.Status + "）");
    }

    // ---------- 文件实体同步（对齐 webdav.js syncFileEntities：files/ 与 files/<账号名>/ 集合 + PUT/GET/DELETE） ----------

    /// <summary>实体根目录 URL（files/）。</summary>
    public static string FilesRootUrlFor(SyncConfig c) => DataDirUrl(c) + "files/";

    /// <summary>账号实体目录 URL（files/&lt;账号名&gt;/，对齐 webdav.js syncFileEntities 的 fBase）。</summary>
    public static string FilesDirUrlFor(SyncConfig c, string name) => DataDirUrl(c) + "files/" + SafeName(name) + "/";

    /// <summary>确保单个集合（目录）存在（MKCOL，容忍已存在/不支持）。</summary>
    public static async Task EnsureOneDir(SyncConfig c, string url)
    {
        var r = await DavFetch(c, url, new HttpMethod("MKCOL"));
        if (r.Status == 401 || r.Status == 403) throw new WebDavException(401, "WebDAV 认证失败（检查用户名/密码）");
        if (!new[] { 201, 204, 200, 301, 405 }.Contains(r.Status))
            throw new WebDavException(502, "WebDAV 目录不可用（HTTP " + r.Status + "）");
    }

    /// <summary>MIME 兜底：null / 空串 / 空白 → application/octet-stream。
    /// 对齐 Web 端 `c.fileMime || "application/octet-stream"`（JS 空串也走 || 兜底）。
    ///
    /// 单独抽成纯函数以便自检覆盖（v0.7.6-P1）：C# 的 `??` **只判 null**，
    /// 条目的 fileMime 为**空字符串**时（MimeFromPath 对未知扩展名返回 ""，很常见）
    /// `mime ?? "application/octet-stream"` 会原样把 "" 传下去 →
    /// `new MediaTypeHeaderValue("")` 抛 "The value cannot be an empty string. (Parameter 'mediaType')"
    /// → 整个同步被这一个文件中断，用户只看到「同步失败」。</summary>
    public static string MimeOrDefault(string? mime)
        => string.IsNullOrWhiteSpace(mime) ? "application/octet-stream" : mime;

    /// <summary>上传文件实体（PUT 二进制；对齐 syncFileEntities 的 PUT 分支）。</summary>
    public static async Task UploadFile(SyncConfig c, string url, byte[] data, string? mime)
    {
        using var content = new ByteArrayContent(data);
        // v0.7.6-P1 修复：`mime ?? "..."` 只判 null，条目的 fileMime 为**空字符串**时原样传入 →
        // MediaTypeHeaderValue("") 抛 "The value cannot be an empty string. (Parameter 'mediaType')"，
        // 整个同步被这一次异常中断（用户侧只看到「同步失败」）。
        // 空/空白一律兜底 octet-stream（对齐 Web 端 `c.fileMime || "application/octet-stream"` 的 JS 语义）。
        content.Headers.ContentType = new MediaTypeHeaderValue(MimeOrDefault(mime));
        using var cts = new CancellationTokenSource(REQ_TIMEOUT_MS);
        using var req = new HttpRequestMessage(HttpMethod.Put, url)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(c.User + ":" + c.Pass))) },
            Content = content,
        };
        using var resp = await Http.SendAsync(req, cts.Token);
        if (!new[] { 201, 204, 200 }.Contains((int)resp.StatusCode))
            throw new WebDavException(502, "文件实体上传失败（HTTP " + (int)resp.StatusCode + "）");
    }

    /// <summary>下载文件实体；404 → null（对齐 syncFileEntities 的 GET 分支）。</summary>
    public static async Task<byte[]?> DownloadFile(SyncConfig c, string url)
    {
        var r = await DavFetch(c, url, HttpMethod.Get);
        if (r.Status == 404) return null;
        if (r.Status != 200) throw new WebDavException(502, "文件实体下载失败（HTTP " + r.Status + "）");
        return r.Buf;
    }

    /// <summary>删除远端文件/快照（迁移清理用，忽略错误）。</summary>
    public static async Task DeleteFile(SyncConfig c, string url)
    {
        try { await DavFetch(c, url, HttpMethod.Delete); } catch { /* 忽略 */ }
    }
}
