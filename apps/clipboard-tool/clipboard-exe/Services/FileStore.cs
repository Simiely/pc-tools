// Services/FileStore.cs - 文件实体存储（对齐 Web 版 lib/core/files.js saveFile/getFilePath/deleteFile 语义）
// 单机形态：data/files/<fileId>.<ext>（Web 版 files/<uid>/ 的单用户简化，无 uid 层）。
// 安全对齐 files.js：
//   - 10MB 上限（CONFIG.MAX_FILE）→ "文件超过 10MB 上限"
//   - 类型黑名单（BLOCKED_MIME / BLOCKED_EXT，拒绝可执行/脚本）→ "不支持上传该类型"
//   - 随机 fileId (UUID) + 扩展名映射（EXT_BY_MIME，未知按原始扩展名兜底，白名单外回退 bin）防路径穿越
//   - getFilePath 按 fileId 前缀在目录内查找（杜绝目录穿越）；deleteFile 不存在静默
using System.IO;
using System.Text.RegularExpressions;

namespace ClipboardExe.Services;

public sealed class FileStore
{
    /// <summary>单文件上传上限 10MB（CONFIG.MAX_FILE）。</summary>
    public const long MaxFileSize = 10 * 1024 * 1024;

    private static readonly HashSet<string> BlockedMime = new(StringComparer.Ordinal)
    {
        "text/html", "image/svg+xml",
        "application/x-executable", "application/x-msdownload",
        "application/x-msdos-program", "application/vnd.microsoft.portable-executable",
        "application/x-sh", "application/x-shellscript",
        "application/x-javascript", "text/javascript",
        "application/x-httpd-php",
    };

    private static readonly HashSet<string> BlockedExt = new(StringComparer.Ordinal)
    {
        "html", "htm", "svg", "exe", "bat", "cmd", "sh", "com",
        "js", "mjs", "vbs", "ps1", "php", "jsp", "apk",
    };

    private static readonly Dictionary<string, string> ExtByMime = new(StringComparer.Ordinal)
    {
        ["image/png"] = "png", ["image/jpeg"] = "jpg", ["image/gif"] = "gif", ["image/webp"] = "webp",
        ["application/pdf"] = "pdf", ["application/zip"] = "zip", ["application/json"] = "json",
        ["text/plain"] = "txt", ["text/csv"] = "csv", ["text/markdown"] = "md",
    };

    /// <summary>存储扩展名白名单（来自文件名时校验，防路径穿越；EXT_SAFE_RE）。</summary>
    private static readonly Regex ExtSafeRe = new(@"^[a-z0-9]{1,8}$");

    private readonly string _filesDir;

    public FileStore(string dataDir) => _filesDir = Path.Combine(dataDir, "files");

    public string FilesDir => _filesDir;

    /// <summary>保存文件实体，返回元数据（随机 fileId；原始名仅作展示，截 255）。</summary>
    public (string FileId, string FileName, long FileSize, string FileMime) Save(byte[] buffer, string originalName, string mime)
    {
        if (buffer == null || buffer.Length == 0) throw new InvalidOperationException("文件为空");
        if (buffer.Length > MaxFileSize) throw new InvalidOperationException("文件超过 10MB 上限");
        var m = (mime ?? "").ToLowerInvariant();
        var extFromName = Path.GetExtension(originalName ?? "").TrimStart('.').ToLowerInvariant(); // 对齐 split(".").pop()（取最后一段）
        if (BlockedMime.Contains(m) || BlockedExt.Contains(extFromName))
            throw new InvalidOperationException("不支持上传该类型: " + (m.Length > 0 ? m : (extFromName.Length > 0 ? extFromName : "未知")));
        var fileId = Guid.NewGuid().ToString("D");
        var ext = ExtByMime.TryGetValue(m, out var e) ? e : (ExtSafeRe.IsMatch(extFromName) ? extFromName : "bin");
        Directory.CreateDirectory(_filesDir);
        File.WriteAllBytes(Path.Combine(_filesDir, fileId + "." + ext), buffer);
        var name = originalName ?? "file";
        if (name.Length > 255) name = name[..255]; // 对齐 slice(0, 255)
        return (fileId, name, buffer.Length, m);
    }

    /// <summary>读取文件实体（返回磁盘路径，供下载/复制）。按 fileId 前缀查找，防目录穿越。</summary>
    public string GetPath(string fileId)
    {
        if (!Directory.Exists(_filesDir))
            throw new FileNotFoundException("文件不存在");
        var match = Directory.GetFiles(_filesDir).FirstOrDefault(f => Path.GetFileName(f).StartsWith(fileId + ".", StringComparison.Ordinal));
        if (match == null) throw new FileNotFoundException("文件不存在");
        return match;
    }

    public byte[] ReadAllBytes(string fileId) => File.ReadAllBytes(GetPath(fileId));

    /// <summary>扩展名推导（对齐 webdav.js extFor：mime 优先，其次文件名后缀，兜底 bin）。供 WebDAV 实体同步命名远端/本地文件。</summary>
    public static string ExtFor(string? mime, string? fileName)
    {
        if (!string.IsNullOrEmpty(mime) && ExtByMime.TryGetValue(mime!, out var e)) return e;
        var m = Regex.Match(fileName ?? "", @"\.([a-z0-9]{1,8})$", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.ToLowerInvariant() : "bin";
    }

    /// <summary>写回文件实体（WebDAV 恢复场景：远端 → 本地 files/&lt;fileId&gt;.&lt;ext&gt;）。</summary>
    public void WriteRaw(string fileId, string ext, byte[] data)
    {
        Directory.CreateDirectory(_filesDir);
        File.WriteAllBytes(Path.Combine(_filesDir, fileId + "." + ext), data);
    }

    /// <summary>物理删除文件实体（不存在/无效静默，对齐 deleteFile）。</summary>
    public void Delete(string fileId)
    {
        try { File.Delete(GetPath(fileId)); }
        catch { /* 不存在或无效：静默 */ }
    }

    /// <summary>文件实体是否存在（GetPath 成功即存在）。</summary>
    public bool Exists(string fileId)
    {
        try { GetPath(fileId); return true; }
        catch { return false; }
    }

    /// <summary>按文件名推断 MIME（对齐浏览器 File.type 的粗映射；未知返回 ""——对齐空 mime 按扩展名兜底）。
    /// 仅覆盖常见类型 + 图片（图片判定供 UI 分流）。</summary>
    public static string MimeFromPath(string path)
    {
        var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        return ext switch
        {
            "png" => "image/png", "jpg" or "jpeg" => "image/jpeg", "gif" => "image/gif", "webp" => "image/webp",
            "pdf" => "application/pdf", "zip" => "application/zip", "rar" => "application/x-rar-compressed",
            "7z" => "application/x-7z-compressed", "tar" => "application/x-tar", "gz" => "application/gzip",
            "txt" => "text/plain", "csv" => "text/csv", "md" => "text/markdown", "json" => "application/json",
            _ => "",
        };
    }
}
