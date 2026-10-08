// lib/core/clips-transfer.js - 条目域导出/导入（本地文件备份，v0.2.0）
// v0.6.14 从 clips.js 拆出（P2 模块化）：单向依赖 clips-store/store/config，无循环。
import crypto from "node:crypto";
import { CONFIG, ID_RE } from "./config.js";
import { httpError } from "./store.js";
import { loadClips, loadArchive, saveClips, sanitizeHtml } from "./clips-store.js";

/** 导出：全量条目（活跃区 + 归档区，含文件元数据），供下载备份。 */
export function exportClips(userId) {
  return {
    app: "clipboard", version: 1, exportedAt: Date.now(),
    clips: [...loadClips(userId), ...loadArchive(userId)],
  };
}

/** 导入：合并去重（同 id 保留 updatedAt 新者），返回导入/跳过计数。
 * 不清理本地已有数据（合并语义）；文件实体引用保留但文件本体不迁移（下载时缺失会 404 提示）。 */
export function importClips(userId, items) {
  if (!Array.isArray(items)) throw httpError(400, "导入数据格式错误");
  if (items.length > CONFIG.MAX_CLIPS_PER_USER * 2) throw httpError(413, "导入条目过多");
  const byId = new Map();
  for (const c of loadClips(userId)) byId.set(c.id, c);
  let added = 0, updated = 0, skipped = 0;
  for (const raw of items) {
    if (!raw || typeof raw !== "object" || !raw.id) { skipped++; continue; }
    const clip = sanitizeImported(raw);
    const ex = byId.get(clip.id);
    if (ex) { // 同 id：取 updatedAt 新者（更新计数单独统计）
      if ((clip.updatedAt || 0) > (ex.updatedAt || 0)) { byId.set(clip.id, clip); updated++; }
      else skipped++;
    } else { byId.set(clip.id, clip); added++; }
  }
  const merged = [...byId.values()];
  saveClips(userId, merged); // 内部自动滚动超限进归档，不丢
  return { ok: true, added, updated, skipped, total: merged.length };
}

/** 导入条目净化：只保留合法字段、校验类型/长度，防恶意 JSON 注入 */
function sanitizeImported(raw) {
  const type = ["text", "link", "file"].includes(raw.type) ? raw.type : "text";
  // v0.6.11：id 必须符合 UUID 白名单（ID_RE）——否则后续 assertId 全拒（编辑/复制/删除/置顶 400），
  // 导入的条目变成"看得见摸不着"。非 UUID id 重新生成（如外部工具备份/手工构造 JSON 的场景）。
  const rawId = String(raw.id || "");
  const id = ID_RE.test(rawId) ? rawId : crypto.randomUUID();
  const clip = {
    id,
    type,
    title: String(raw.title || "").slice(0, CONFIG.MAX_TITLE),
    content: String(raw.content || "").slice(0, CONFIG.MAX_CONTENT),
    html: sanitizeHtml(raw.html), // 富文本随导入迁移（超 512KB 截断）
    url: "",
    tags: Array.isArray(raw.tags)
      ? [...new Set(raw.tags.map((t) => String(t).slice(0, CONFIG.MAX_TAG_LEN)))].slice(0, CONFIG.MAX_TAGS)
      : [],
    fileId: "", fileName: "", fileSize: 0, fileMime: "",
    copyCount: Math.max(0, Math.floor(Number(raw.copyCount) || 0)),
    pinned: !!raw.pinned,
    expireAt: Number(raw.expireAt) || null,
    createdAt: Number(raw.createdAt) || Date.now(),
    updatedAt: Number(raw.updatedAt) || Date.now(),
  };
  if (type === "link") {
    const url = String(raw.url || "");
    if (/^https?:\/\/\S+$/i.test(url)) clip.url = url;
  } else if (type === "file" && typeof raw.fileId === "string") {
    clip.fileId = raw.fileId;
    clip.fileName = String(raw.fileName || "file").slice(0, 255);
    clip.fileSize = Math.max(0, Number(raw.fileSize) || 0);
    clip.fileMime = String(raw.fileMime || "");
  }
  return clip;
}
