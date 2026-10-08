// lib/core/clips-store.js - 条目域底层：文件存取 + 滚动归档 + 共享内部函数
// v0.6.14 从 clips.js 拆出（P2 模块化）：本文件是所有条目上层操作的唯一数据底座，
// 只依赖 store/config，不 import 任何上层模块（单向依赖，无循环）。
import path from "node:path";
import { CONFIG } from "./config.js";
import { readJson, writeJson } from "./store.js";

function clipsFile(userId) {
  return path.join(CONFIG.usersDir, userId + ".json");
}

function archiveFile(userId) {
  return path.join(CONFIG.usersDir, userId + ".archive.json");
}

/** 读用户条目原始数组（同步模块也使用） */
export function loadClips(userId) {
  const list = readJson(clipsFile(userId), []);
  return Array.isArray(list) ? list : [];
}

/** 读归档数组（滚动归档：活跃区超限后最旧条目移入，只读居多） */
export function loadArchive(userId) {
  const list = readJson(archiveFile(userId), []);
  return Array.isArray(list) ? list : [];
}

/** 写归档数组（直接替换，不做滚动——v0.6.13：WebDAV 同步分拣写回用） */
export function saveArchive(userId, list) {
  writeJson(archiveFile(userId), Array.isArray(list) ? list : []);
}

/** 写用户条目数组（同步模块也使用）；超上限时自动滚动最旧条目进归档 */
export function saveClips(userId, list) {
  const trimmed = rollToArchive(userId, list);
  writeJson(clipsFile(userId), trimmed);
}

/**
 * 滚动归档：活跃区超过 MAX_CLIPS_PER_USER 时，保留"最近更新"的前 N 条
 * （按 updatedAt 降序——刚存入/刚复制/刚编辑的条目绝不进归档），
 * 其余按 createdAt 升序追加进归档（零丢失）。清空（空数组）不触发滚动。
 * 注意：不按 copyCount 排序——否则新条目（copyCount=0）会被当成低价值立即滚走（v0.3.1 修复）。
 */
export function rollToArchive(userId, list) {
  const MAX = CONFIG.MAX_CLIPS_PER_USER;
  if (!MAX || list.length <= MAX) return list;
  const byRecent = [...list].sort((a, b) => (b.updatedAt || 0) - (a.updatedAt || 0));
  const keep = byRecent.slice(0, MAX);
  const overflow = byRecent.slice(MAX).sort((a, b) => (a.createdAt || 0) - (b.createdAt || 0));
  // v0.6.11：按 id 去重再追加——此前每次 saveClips 都把同一批最旧条目重复滚入归档，
  // 实测 800 条连续两次保存归档 300→600 翻倍膨胀（WebDAV 同步/保存高频触发）。
  const arch = loadArchive(userId);
  const existing = new Set(arch.map((c) => c.id));
  for (const c of overflow) if (!existing.has(c.id)) { arch.push(c); existing.add(c.id); }
  writeJson(archiveFile(userId), arch);
  return keep;
}

/** 条目对外视图（前端渲染用） */
export function publicClip(c) {
  return {
    id: c.id, type: c.type, title: c.title, content: c.content,
    html: c.html || "", url: c.url, tags: c.tags, fileId: c.fileId, fileName: c.fileName,
    fileSize: c.fileSize, fileMime: c.fileMime,
    copyCount: c.copyCount, pinned: !!c.pinned, archived: !!c.archived, expireAt: c.expireAt,
    createdAt: c.createdAt, updatedAt: c.updatedAt,
  };
}

/** html 字段净化：白名单字符串 + 长度上限（512KB）。富文本随条目存储，前端按钮二选一复制 */
export function sanitizeHtml(raw) {
  const h = String(raw || "");
  if (!h) return "";
  return h.slice(0, CONFIG.MAX_HTML);
}

/** 排序：星标优先 → 复制次数降序 → 最近更新（单一实现，勿在前端重复） */
export function sortClips(list) {
  return [...list].sort((a, b) => {
    if (!!b.pinned !== !!a.pinned) return (b.pinned ? 1 : 0) - (a.pinned ? 1 : 0);
    if ((b.copyCount || 0) !== (a.copyCount || 0)) return (b.copyCount || 0) - (a.copyCount || 0);
    return (b.updatedAt || 0) - (a.updatedAt || 0);
  });
}

/** 过期判断：expireAt 为 null/0 = 永久 */
export function isExpired(c) {
  return !!c.expireAt && c.expireAt < Date.now();
}

/** 解析过期选项：'1h'|'1d'|'7d'|'30d'|''(永久) → 绝对时间戳 */
export function resolveExpire(opt) {
  if (!opt) return null;
  const m = /^(\d+)([hd])$/.exec(String(opt).trim());
  if (!m) return null;
  const n = parseInt(m[1], 10);
  const unit = m[2] === "h" ? 3_600_000 : 86_400_000;
  return Date.now() + n * unit;
}

// ---------- URL 自动清理（v0.2.0：去追踪参数，保持链接干净） ----------
/** 常见追踪参数（UTM + 渠道统计），保存链接时自动剔除 */
const TRACKING_KEYS = new Set([
  "utm_source", "utm_medium", "utm_campaign", "utm_term", "utm_content",
  "utm_id", "utm_source_platform", "utm_creative_format", "utm_marketing_tactic",
  "fbclid", "gclid", "gclsrc", "dclid", "msclkid", "mc_cid", "mc_eid",
  "igshid", "ref", "spm", "scm", "from", "from_sources", "clicktime", "clickid",
]);

/** 清理 URL 追踪参数：保留协议/主机/路径/其余参数，只剔除 TRACKING_KEYS。
 * 无追踪参数时原样返回；无法解析（畸形 URL）也原样返回。 */
export function cleanUrl(url) {
  if (!/^https?:\/\//i.test(url)) return url;
  try {
    const u = new URL(url);
    if (!u.search) return url;
    const kept = [];
    let removed = 0;
    for (const [k, v] of u.searchParams) {
      if (TRACKING_KEYS.has(k.toLowerCase())) removed++;
      else kept.push([k, v]);
    }
    if (!removed) return url;
    u.search = "";
    for (const [k, v] of kept) u.searchParams.append(k, v);
    return u.toString();
  } catch {
    return url;
  }
}

/** 输入净化（标题/标签）：写操作共用 */
export function sanitizeInput(body) {
  const title = String(body.title || "").trim().slice(0, CONFIG.MAX_TITLE);
  const tags = Array.isArray(body.tags)
    ? [...new Set(body.tags.map((t) => String(t).trim().slice(0, CONFIG.MAX_TAG_LEN)).filter(Boolean))].slice(0, CONFIG.MAX_TAGS)
    : [];
  return { title, tags };
}
