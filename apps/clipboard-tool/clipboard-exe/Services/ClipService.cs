// Services/ClipService.cs - 条目业务规则（对齐 Web 版 clips-mutate.js createClip + clips-query.js listClips
// + public/app.js findDuplicateClip / getVisibleClips 拼音搜索）
// 纯函数 + Storage 编排，M2 零 UI；MainWindow 只做编排（边界契约 #1/#2：数据层独立文件）。
using System.Text.RegularExpressions;
using ClipboardExe.Models;

namespace ClipboardExe.Services;

public sealed class ClipService
{
    public const int MaxTitle = 200;           // CONFIG.MAX_TITLE
    public const int MaxContent = 200 * 1024;  // CONFIG.MAX_CONTENT
    public const int MaxHtml = 512 * 1024;     // CONFIG.MAX_HTML
    public const int MaxTags = 10;             // CONFIG.MAX_TAGS
    public const int MaxTagLen = 20;           // CONFIG.MAX_TAG_LEN

    private static readonly Regex LinkRe = new(@"^https?://\S+$", RegexOptions.IgnoreCase);
    private static readonly Regex ExpireRe = new(@"^(\d+)([hd])$");

    private readonly Storage _storage;
    private readonly FileStore _fileStore;

    public ClipService(Storage storage, FileStore fileStore)
    {
        _storage = storage;
        _fileStore = fileStore;
    }

    // ---- 创建（对齐 createClip：type 白名单 → 校验 → cleanUrl → 自动标题）----
    /// <param name="expire">过期选项 '1h'|'1d'|'7d'|'30d'|''(永久)。</param>
    public ClipItem Create(string type, string? title, string? content, string? html, string? url,
                           List<string>? tags, string? expire,
                           string? fileId = null, string? fileName = null, long fileSize = 0, string? fileMime = null)
    {
        var (t, tgs) = SanitizeInput(title, tags);
        var ty = type is "text" or "link" or "file" ? type : "text";
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var clip = new ClipItem
        {
            Id = Guid.NewGuid().ToString("D"),
            Type = ty,
            Title = t,
            Html = SanitizeHtml(html),
            Tags = tgs,
            ExpireAt = ResolveExpire(expire),
            CreatedAt = now,
            UpdatedAt = now,
        };
        if (ty == "text")
        {
            var c = content ?? "";
            if (c.Length == 0) throw new InvalidOperationException("内容不能为空");
            if (c.Length > MaxContent) throw new InvalidOperationException("内容过长");
            clip.Content = c;
        }
        else if (ty == "link")
        {
            var u = url ?? "";
            if (!LinkRe.IsMatch(u)) throw new InvalidOperationException("链接需以 http(s):// 开头");
            clip.Url = CleanUrl.Clean(u); // v0.2.0：自动去追踪参数（UTM/fbclid 等）
            if (string.IsNullOrEmpty(clip.Title)) clip.Title = Truncate(clip.Url, 60); // v0.3.1：用清理后的 url 做标题，避免 utm 残留
        }
        else
        {
            if (string.IsNullOrEmpty(fileId)) throw new InvalidOperationException("缺少文件");
            clip.FileId = fileId;
            clip.FileName = Truncate(string.IsNullOrEmpty(fileName) ? "file" : fileName, 255);
            clip.FileSize = fileSize;
            clip.FileMime = fileMime ?? "";
        }
        var list = _storage.LoadClips();
        list.Add(clip);
        _storage.SaveClips(list);
        return clip;
    }

    // ---- 去重（对齐 app.js findDuplicateClip：link 比 url，其他比 content；url 兜底 "" 防 undefined 参与比对）----
    public static ClipItem? FindDuplicate(string? content, IEnumerable<ClipItem>? clips)
    {
        if (string.IsNullOrEmpty(content) || clips == null) return null;
        foreach (var c in clips)
        {
            var cmp = c.Type == "link" ? (c.Url ?? "") : (c.Content ?? "");
            if (cmp == content) return c;
        }
        return null;
    }

    // ---- 变更（对齐 Web 版 /api/clips/:id 系列：pin / copy / PUT / DELETE / archive）----
    /// <summary>按 id 取活跃区条目（弹窗回显用）。</summary>
    public ClipItem? GetById(string id)
        => _storage.LoadClips().FirstOrDefault(c => c.Id == id);

    /// <summary>置顶切换（对齐 POST /api/clips/:id/pin）。返回新状态。</summary>
    public bool TogglePin(string id)
    {
        var list = _storage.LoadClips();
        var c = list.FirstOrDefault(x => x.Id == id);
        if (c == null) throw new InvalidOperationException("条目不存在");
        c.Pinned = !c.Pinned;
        c.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); // 置顶参与排序（对齐后端 pin 刷新排序）
        _storage.SaveClips(list);
        return c.Pinned;
    }

    /// <summary>复制计数 +1（对齐 POST /api/clips/:id/copy）。返回新计数。</summary>
    public long BumpCopyCount(string id)
    {
        var list = _storage.LoadClips();
        var c = list.FirstOrDefault(x => x.Id == id);
        if (c == null) return 0;
        c.CopyCount++;
        c.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _storage.SaveClips(list);
        return c.CopyCount;
    }

    /// <summary>删除单条（对齐 Web DELETE /api/clips/:id 路由：先活跃区、不存在则归档区；文件实体由调用方按返回 fileId 联动清理）。
    /// v0.7.2 修复：此前仅从活跃区移除且**不记墓碑**——与 Web 路由层 deleteClip/deleteArchivedClip 后 recordTombstoneIfConfigured
    /// 不一致，单卡删除后下次 WebDAV 同步把远端仍存的旧副本合并回本地（无墓碑裁决 → 复活）。现改为跨活跃+归档删除并记墓碑，
    /// 与 BatchDelete 同语义（防远端复活）。返回被删条目的 fileId（文件类供调用方清理实体；无则 null）。</summary>
    public string? Delete(string id)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string? fileId = null;
        var found = false;
        foreach (var (load, save) in new (Func<List<ClipItem>> load, Action<List<ClipItem>> save)[]
        {
            (_storage.LoadClips, _storage.SaveClips),
            (_storage.LoadArchive, _storage.SaveArchive),
        })
        {
            var list = load();
            var kept = new List<ClipItem>(list.Count);
            foreach (var c in list)
            {
                if (c.Id == id)
                {
                    found = true;
                    if (c.Type == "file" && !string.IsNullOrEmpty(c.FileId)) fileId = c.FileId;
                }
                else kept.Add(c);
            }
            if (kept.Count != list.Count) save(kept);
        }
        if (!found) throw new InvalidOperationException("条目不存在");
        // 墓碑：同 id 保留最新 deletedAt（与 BatchDelete 一致，无条件记录——桌面单机形态本地留存无害且便于本地删除传播）
        RecordTombstone(id, now);
        return fileId;
    }

    /// <summary>移入归档（对齐 POST /api/clips/:id/archive：活跃区移除 + 追加归档）。</summary>
    public void Archive(string id)
    {
        var list = _storage.LoadClips();
        var c = list.FirstOrDefault(x => x.Id == id);
        if (c == null) throw new InvalidOperationException("条目不存在");
        list.Remove(c);
        var arch = _storage.LoadArchive();
        if (!arch.Any(x => x.Id == id)) arch.Add(c);
        _storage.SaveArchive(arch);
        _storage.SaveClips(list);
    }

    /// <summary>从归档恢复到活跃区（对齐 Web ↺ 恢复；活跃区已存在则跳过避免重复）。</summary>
    public bool Unarchive(string id)
    {
        var arch = _storage.LoadArchive();
        var c = arch.FirstOrDefault(x => x.Id == id);
        if (c == null) throw new InvalidOperationException("归档中不存在");
        var list = _storage.LoadClips();
        if (list.Any(x => x.Id == id)) return false; // 已存在（理论上归档按 id 唯一，但活跃区可能有重——防御）
        c.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); // 恢复到活跃区参与排序
        list.Add(c);
        arch.Remove(c);
        _storage.SaveClips(list);
        _storage.SaveArchive(arch);
        return true;
    }

    /// <summary>
    /// 编辑保存（对齐 PUT /api/clips/:id）：title/tags/expire 恒更新；content（text/rich）或 url（link）按类型更新；
    /// html 仅富文本条目传入（由调用方决定：content 未变保留原值、变了 textToHtml 重建、清除格式传 ""）。
    /// 规则净化对齐 createClip（sanitizeInput + link 校验 + expire 解析）。
    /// </summary>
    public ClipItem? Update(string id, string? title, List<string>? tags, string? expire,
                            string? content = null, string? url = null, string? html = null)
    {
        var list = _storage.LoadClips();
        var c = list.FirstOrDefault(x => x.Id == id);
        if (c == null) return null;
        var (t, tgs) = SanitizeInput(title, tags);
        c.Title = t;
        c.Tags = tgs;
        c.ExpireAt = ResolveExpire(expire);
        if (c.Type == "link")
        {
            var u = url ?? c.Url ?? "";
            if (!LinkRe.IsMatch(u)) throw new InvalidOperationException("链接需以 http(s):// 开头");
            c.Url = CleanUrl.Clean(u);
            if (string.IsNullOrEmpty(c.Title)) c.Title = Truncate(c.Url, 60);
        }
        else if (c.Type == "text")
        {
            if (content != null)
            {
                if (content.Length == 0) throw new InvalidOperationException("内容不能为空");
                if (content.Length > MaxContent) throw new InvalidOperationException("内容过长");
                c.Content = content;
            }
            if (html != null) c.Html = SanitizeHtml(html);
        }
        c.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _storage.SaveClips(list);
        return c;
    }

    // ---- 导入导出 / 清空（对齐 Web 版 clips-transfer.js exportClips/importClips + clips-mutate.js clearAllClips）----

    /// <summary>导出全部（活跃区 + 归档区合并为扁平 clips[]），包成 {app,version,exportedAt,clips[]}（对齐 exportClips）。</summary>
    public ExportDoc BuildExport()
    {
        var all = new List<ClipItem>(_storage.LoadClips().Count + _storage.LoadArchive().Count);
        all.AddRange(_storage.LoadClips());
        all.AddRange(_storage.LoadArchive());
        return new ExportDoc
        {
            App = "clipboard",
            Version = 1,
            ExportedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Clips = all,
        };
    }

    /// <summary>
    /// 导入合并（对齐 importClips）：同 id 取 updatedAt 新者（更新计数），旧者跳过，新 id 新增；
    /// 不清理本地已有数据（合并语义）；超 1000 条拒绝；保存时自动滚动超限进归档，不丢。
    /// </summary>
    public ImportResult ImportClips(IEnumerable<ClipItem>? items)
    {
        var list = items?.ToList() ?? new List<ClipItem>();
        if (list.Count > Storage.MaxClipsPerUser * 2) throw new InvalidOperationException("导入条目过多");
        var byId = new Dictionary<string, ClipItem>(StringComparer.Ordinal);
        foreach (var c in _storage.LoadClips()) byId[c.Id] = c; // 仅活跃区（对齐 Web：归档不进 byId，避免与导入项重复）
        int added = 0, updated = 0, skipped = 0;
        foreach (var raw in list)
        {
            if (raw == null || string.IsNullOrEmpty(raw.Id)) { skipped++; continue; }
            var clip = SanitizeImported(raw);
            if (byId.TryGetValue(clip.Id, out var ex))
            {
                if (clip.UpdatedAt > ex.UpdatedAt) { byId[clip.Id] = clip; updated++; }
                else skipped++;
            }
            else { byId[clip.Id] = clip; added++; }
        }
        _storage.SaveClips(byId.Values.ToList()); // 内部自动滚动超限进归档，不丢
        return new ImportResult { Added = added, Updated = updated, Skipped = skipped, Total = byId.Count };
    }

    /// <summary>
    /// 全部清空（对齐 clearAllClips）：活跃区 + 归档一并清空；清空不记墓碑（= 想从网上同步，不传播删除）；
    /// 联动 FileStore 清理文件实体。返回清空前的活跃区条目数。
    /// </summary>
    public int ClearAll()
    {
        var list = _storage.LoadClips();
        var fileIds = list
            .Where(c => c.Type == "file" && !string.IsNullOrEmpty(c.FileId))
            .Select(c => c.FileId)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        _storage.SaveClips(new List<ClipItem>());
        _storage.SaveArchive(new List<ClipItem>());
        _storage.SaveTombstones(new List<Tombstone>());
        foreach (var fid in fileIds) _fileStore.Delete(fid);
        return list.Count;
    }

    /// <summary>导入条目净化（对齐 sanitizeImported）：只保留合法字段、校验类型/长度、id 非 UUID 重生成、超长截断。</summary>
    private ClipItem SanitizeImported(ClipItem raw)
    {
        var ty = raw.Type is "text" or "link" or "file" ? raw.Type : "text";
        // v0.6.11：id 必须符合 UUID（非 UUID 重新生成，否则后续查不到此条目）。exe id 为 GUID "D" 格式，Guid.TryParse 覆盖。
        var id = Guid.TryParse(raw.Id, out _) ? raw.Id : Guid.NewGuid().ToString("D");
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var clip = new ClipItem
        {
            Id = id,
            Type = ty,
            Title = Truncate(raw.Title ?? "", MaxTitle),
            Content = Truncate(raw.Content ?? "", MaxContent),
            Html = SanitizeHtml(raw.Html),
            Url = "",
            Tags = SanitizeTags(raw.Tags),
            CopyCount = Math.Max(0, raw.CopyCount),
            Pinned = raw.Pinned,
            ExpireAt = raw.ExpireAt,
            CreatedAt = raw.CreatedAt == 0 ? now : raw.CreatedAt,
            UpdatedAt = raw.UpdatedAt == 0 ? now : raw.UpdatedAt,
        };
        if (ty == "link")
        {
            var url = raw.Url ?? "";
            if (LinkRe.IsMatch(url)) clip.Url = CleanUrl.Clean(url); // 导入同样清理追踪参数
        }
        else if (ty == "file" && !string.IsNullOrEmpty(raw.FileId))
        {
            clip.FileId = raw.FileId;
            clip.FileName = Truncate(raw.FileName ?? "file", 255);
            clip.FileSize = Math.Max(0, raw.FileSize);
            clip.FileMime = raw.FileMime ?? "";
        }
        return clip;
    }

    /// <summary>记墓碑（单条删除用；与 BatchDelete 同语义）：同 id 保留最新 deletedAt，直接追加覆盖。</summary>
    private void RecordTombstone(string id, long deletedAt)
    {
        var existing = _storage.LoadTombstones().Where(t => t.Id != id).ToList();
        existing.Add(new Tombstone { Id = id, DeletedAt = deletedAt });
        _storage.SaveTombstones(existing);
    }

    // ---- 批量编辑（对齐 Web 版 clips-mutate.js batchDeleteClips / batchSetTags）----
    /// <summary>批量删除：跨活跃区+归档；联动 FileStore 清理文件实体；记录墓碑供 M5 WebDAV 同步传播删除。</summary>
    public int BatchDelete(IEnumerable<string?> ids)
    {
        var idSet = new HashSet<string>(
            (ids ?? Enumerable.Empty<string?>())
            .Where(x => !string.IsNullOrEmpty(x))
            .Select(x => x!),
            StringComparer.Ordinal);
        if (idSet.Count == 0) throw new InvalidOperationException("未选择条目");

        var fileIds = new List<string>();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var tombstones = new List<Tombstone>();
        int deleted = 0;

        foreach (var (load, save) in new (Func<List<ClipItem>> load, Action<List<ClipItem>> save)[]
        {
            (_storage.LoadClips, _storage.SaveClips),
            (_storage.LoadArchive, _storage.SaveArchive),
        })
        {
            var list = load();
            var kept = new List<ClipItem>(list.Count);
            foreach (var c in list)
            {
                if (idSet.Contains(c.Id))
                {
                    deleted++;
                    if (c.Type == "file" && !string.IsNullOrEmpty(c.FileId)) fileIds.Add(c.FileId);
                    tombstones.Add(new Tombstone { Id = c.Id, DeletedAt = now });
                }
                else kept.Add(c);
            }
            if (kept.Count != list.Count) save(kept);
        }

        // 墓碑：同 id 保留最新 deletedAt
        var existing = _storage.LoadTombstones().Where(t => !idSet.Contains(t.Id)).ToList();
        existing.AddRange(tombstones);
        _storage.SaveTombstones(existing);

        // 文件实体清理（与单条删除同语义；失败不阻断返回）
        foreach (var fid in fileIds.Distinct(StringComparer.Ordinal)) _fileStore.Delete(fid);

        return deleted;
    }

    /// <summary>批量加/减标签：跨活跃区+归档；mode 为 add 时 tags 去重/截长/上限 MAX_TAGS；刷新 updatedAt 驱动排序。</summary>
    public int BatchSetTags(IEnumerable<string?> ids, IEnumerable<string?>? tags, bool isAdd)
    {
        var idSet = new HashSet<string>(
            (ids ?? Enumerable.Empty<string?>())
            .Where(x => !string.IsNullOrEmpty(x))
            .Select(x => x!),
            StringComparer.Ordinal);
        if (idSet.Count == 0) throw new InvalidOperationException("未选择条目");

        var names = SanitizeTags(tags);
        if (names.Count == 0) throw new InvalidOperationException("标签不能为空");

        int affected = 0;
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var (load, save) in new (Func<List<ClipItem>> load, Action<List<ClipItem>> save)[]
        {
            (_storage.LoadClips, _storage.SaveClips),
            (_storage.LoadArchive, _storage.SaveArchive),
        })
        {
            var list = load();
            var touched = false;
            foreach (var c in list)
            {
                if (!idSet.Contains(c.Id)) continue;
                var cur = new HashSet<string>(c.Tags ?? new List<string>(), StringComparer.Ordinal);
                if (isAdd)
                {
                    foreach (var t in names) cur.Add(t);
                    c.Tags = cur.Take(MaxTags).ToList();
                }
                else
                {
                    foreach (var t in names) cur.Remove(t);
                    c.Tags = cur.ToList();
                }
                c.UpdatedAt = now;
                affected++;
                touched = true;
            }
            if (touched) save(list);
        }
        return affected;
    }

    private List<string> SanitizeTags(IEnumerable<string?>? tags)
    {
        var list = new List<string>();
        if (tags == null) return list;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in tags)
        {
            var t = Truncate((raw ?? "").Trim(), MaxTagLen);
            if (t.Length == 0 || seen.Contains(t)) continue;
            seen.Add(t);
            list.Add(t);
        }
        return list;
    }

    // ---- 查询（对齐 listClips + getVisibleClips 前端拼音过滤）----    /// <summary>q（标题/内容/URL/标签模糊 + 拼音首字母缩写）+ tag（精确）+ type + 含归档；自动过滤已过期。</summary>
    public List<ClipItem> Search(string? q, string? tag = "", string? type = "all", bool includeArchived = false)
    {
        var kw = (q ?? "").Trim().ToLowerInvariant();
        var tg = (tag ?? "").Trim();
        List<ClipItem> list;
        if (includeArchived)
        {
            // 归档合并：只取最近 ARCHIVE_SCAN_LIMIT 条（防超大归档拖慢），标记 archived 供前端只读
            var arch = _storage.LoadArchive();
            var tail = arch.Count > Storage.ArchiveScanLimit ? arch.Skip(arch.Count - Storage.ArchiveScanLimit).ToList() : arch;
            foreach (var c in tail) c.Archived = true;
            list = tail.Concat(_storage.LoadClips()).ToList();
        }
        else
        {
            list = _storage.LoadClips();
        }
        list = list.Where(c => !IsExpired(c)).ToList();
        if (!string.IsNullOrEmpty(type) && type != "all")
            list = list.Where(c => c.Type == type).ToList();
        if (tg.Length > 0)
            list = list.Where(c => c.Tags.Contains(tg)).ToList();
        if (kw.Length > 0)
        {
            list = list.Where(c =>
            {
                var title = c.Title ?? "";
                var content = c.Content ?? "";
                var url = c.Url ?? "";
                var tags = c.Tags ?? new List<string>();
                if (title.ToLowerInvariant().Contains(kw) ||
                    content.ToLowerInvariant().Contains(kw) ||
                    url.ToLowerInvariant().Contains(kw) ||
                    tags.Any(t => t.ToLowerInvariant().Contains(kw))) return true;
                // 拼音首字母缩写匹配（标题+标签）：如 "sf" → 身份
                var py = (Pinyin.InitialsOf(title) + " " + Pinyin.InitialsOf(string.Join(" ", tags))).ToLowerInvariant();
                return py.Contains(kw);
            }).ToList();
        }
        return Storage.SortClips(list);
    }

    // ---- 过期（对齐 clips-store.js isExpired / resolveExpire）----
    /// <summary>过期判断：expireAt 为 null/0 = 永久（0 守卫对齐 Web !!c.expireAt，2026-09-03 差分门禁抓出单边漂移）。</summary>
    public static bool IsExpired(ClipItem c)
    {
        return c.ExpireAt.HasValue && c.ExpireAt.Value > 0
            && c.ExpireAt.Value < DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    /// <summary>解析过期选项：'1h'|'1d'|'7d'|'30d'|''(永久) → 绝对时间戳；非法返回 null。</summary>
    public static long? ResolveExpire(string? opt)
    {
        if (string.IsNullOrEmpty(opt)) return null;
        var m = ExpireRe.Match(opt.Trim());
        if (!m.Success) return null;
        var n = long.Parse(m.Groups[1].Value);
        var unit = m.Groups[2].Value == "h" ? 3_600_000L : 86_400_000L;
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + n * unit;
    }

    // ---- 净化（对齐 clips-store.js sanitizeInput / sanitizeHtml）----
    /// <summary>输入净化（标题/标签）：trim + 截长 + 去重 + 上限（写操作共用）。</summary>
    public static (string Title, List<string> Tags) SanitizeInput(string? title, List<string>? tags)
    {
        var t = Truncate((title ?? "").Trim(), MaxTitle);
        var list = new List<string>();
        if (tags != null)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in tags)
            {
                var tag = Truncate((raw ?? "").Trim(), MaxTagLen);
                if (tag.Length == 0 || seen.Contains(tag)) continue; // 对齐 filter(Boolean) + Set 去重（首次出现保留）
                seen.Add(tag);
                list.Add(tag);
                if (list.Count >= MaxTags) break; // 对齐 slice(0, MAX_TAGS)
            }
        }
        return (t, list);
    }

    /// <summary>html 净化：空串原样；截长到 512KB（对齐 sanitizeHtml）。</summary>
    public static string SanitizeHtml(string? html)
    {
        var h = html ?? "";
        return h.Length == 0 ? "" : Truncate(h, MaxHtml);
    }

    private static string Truncate(string s, int max)
    {
        return s.Length > max ? s[..max] : s;
    }
}
