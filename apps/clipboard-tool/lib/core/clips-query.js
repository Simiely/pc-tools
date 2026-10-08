// lib/core/clips-query.js - 条目域查询：列表/搜索/标签统计/标签管理
// v0.6.14 从 clips.js 拆出（P2 模块化）：单向依赖 clips-store/store/config，无循环。
import { CONFIG } from "./config.js";
import { httpError } from "./store.js";
import {
  loadClips, loadArchive, saveClips, saveArchive,
  sortClips, publicClip, isExpired,
} from "./clips-store.js";

/** 列表：可选搜索词 q（标题/内容/URL/标签模糊）+ 标签 tag（精确过滤）+ 含归档 archived，自动过滤已过期。
 * 排序策略（用户确认）：① pinned 置顶 → ② copyCount 降序 → ③ updatedAt 降序。
 * 严格按点击次数排序（v0.6.16 移除标签/内容归拢——归拢会把高次数卡片挤到后面）。
 * 全部线性级，读取时计算（155 条约 5ms），任何修改即时反映，无需持久化排序。
 * 归档条目标记 archived=true（前端只读展示，不参与编辑/删除）。
 * 注：q/tag 后端过滤能力保留（v0.6.14 前端已单轨化改用前端过滤，此处为 API 兼容）。 */
export function listClips(userId, { q = "", tag = "", archived = false } = {}) {
  const kw = String(q || "").trim().toLowerCase();
  const tg = String(tag || "").trim();
  let list = loadClips(userId);
  if (archived) {
    // 归档合并：只取最近 ARCHIVE_SCAN_LIMIT 条（防超大归档拖慢），标记 archived 供前端只读
    const arch = loadArchive(userId).slice(-CONFIG.ARCHIVE_SCAN_LIMIT);
    list = [...arch.map((c) => ({ ...c, archived: true })), ...list];
  }
  list = list.filter((c) => !isExpired(c));
  if (kw) {
    list = list.filter((c) =>
      (c.title || "").toLowerCase().includes(kw) ||
      (c.content || "").toLowerCase().includes(kw) ||
      (c.url || "").toLowerCase().includes(kw) ||
      (c.tags || []).some((t) => t.toLowerCase().includes(kw))
    );
  }
  if (tg) list = list.filter((c) => (c.tags || []).includes(tg));
  return sortClips(list).map(publicClip);
}

/** 标签统计（前端标签过滤条） */
export function listTags(userId) {
  const counts = {};
  for (const c of loadClips(userId)) {
    if (isExpired(c)) continue;
    for (const t of c.tags || []) counts[t] = (counts[t] || 0) + 1;
  }
  return Object.entries(counts).map(([tag, count]) => ({ tag, count })).sort((a, b) => b.count - a.count);
}

/** 标签重命名：跨活跃区+归档全部条目替换（重名目标合并去重），返回受影响条目数 */
export function renameTag(userId, oldTag, newTag) {
  const from = String(oldTag || "").trim();
  const to = String(newTag || "").trim().slice(0, CONFIG.MAX_TAG_LEN);
  if (!from || !to) throw httpError(400, "标签名无效"); // v0.3.1：目标为空会静默删除标签，必须拒绝
  if (from === to) throw httpError(400, "标签名无效");
  let affected = 0;
  for (const [list, save] of [
    [loadClips(userId), saveClips],
    [loadArchive(userId), saveArchive],
  ]) {
    for (const c of list) {
      if ((c.tags || []).includes(from)) {
        c.tags = [...new Set(c.tags.map((t) => (t === from ? to : t)).filter(Boolean))];
        affected++;
      }
    }
    save(userId, list);
  }
  if (!affected) throw httpError(404, "标签不存在");
  return { ok: true, affected };
}

/** 标签删除：从活跃区+归档全部条目移除，返回受影响条目数 */
export function deleteTag(userId, tag) {
  const name = String(tag || "").trim();
  if (!name) throw httpError(400, "标签名无效");
  let affected = 0;
  for (const [list, save] of [
    [loadClips(userId), saveClips],
    [loadArchive(userId), saveArchive],
  ]) {
    for (const c of list) {
      if ((c.tags || []).includes(name)) {
        c.tags = (c.tags || []).filter((t) => t !== name);
        affected++;
      }
    }
    save(userId, list);
  }
  if (!affected) throw httpError(404, "标签不存在");
  return { ok: true, affected };
}
