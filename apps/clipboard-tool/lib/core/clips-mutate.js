// lib/core/clips-mutate.js - 条目域写操作：CRUD + 归档/恢复 + 复制计数/置顶 + 过期清扫
// v0.6.14 从 clips.js 拆出（P2 模块化）：单向依赖 clips-store/tombstones/store/config，无循环。
import crypto from "node:crypto";
import fs from "node:fs";
import { CONFIG } from "./config.js";
import { httpError, assertId } from "./store.js";
import {
  loadClips, saveClips, loadArchive, saveArchive,
  publicClip, sanitizeHtml, sanitizeInput, isExpired, resolveExpire, cleanUrl,
} from "./clips-store.js";
import { clearTombstones } from "./tombstones.js";

/** 新增：type = text（content 必填）| link（url 必填）| file（fileId 必填） */
export function createClip(userId, body) {
  const { title, tags } = sanitizeInput(body);
  const type = ["text", "link", "file"].includes(body.type) ? body.type : "text";
  const clip = {
    id: crypto.randomUUID(),
    type,
    title,
    content: "",
    html: sanitizeHtml(body.html), // 富文本（可选；与 content 并存，前端双按钮复制）
    url: "",
    tags,
    fileId: "", fileName: "", fileSize: 0, fileMime: "",
    copyCount: 0,
    pinned: false,
    expireAt: resolveExpire(body.expire) || null,
    createdAt: Date.now(),
    updatedAt: Date.now(),
  };
  if (type === "text") {
    const content = String(body.content || "");
    if (!content) throw httpError(400, "内容不能为空");
    if (content.length > CONFIG.MAX_CONTENT) throw httpError(400, "内容过长");
    clip.content = content;
  } else if (type === "link") {
    const url = String(body.url || "");
    if (!/^https?:\/\/\S+$/i.test(url)) throw httpError(400, "链接需以 http(s):// 开头");
    clip.url = cleanUrl(url); // v0.2.0：自动去追踪参数（UTM/fbclid 等）
    if (!title) clip.title = clip.url.slice(0, 60); // v0.3.1：用清理后的 url 做标题，避免 utm 残留
  } else {
    if (!body.fileId || typeof body.fileId !== "string") throw httpError(400, "缺少文件");
    clip.fileId = body.fileId;
    clip.fileName = String(body.fileName || "file").slice(0, 255);
    clip.fileSize = Number(body.fileSize) || 0;
    clip.fileMime = String(body.fileMime || "");
  }
  const list = loadClips(userId);
  list.push(clip);
  saveClips(userId, list);
  return publicClip(clip);
}

/** 编辑：可改标题/内容/链接/标签/过期；文件条目不可换文件（先删再建） */
export function updateClip(userId, id, body) {
  const clipId = assertId(id, "条目 id");
  const list = loadClips(userId);
  const clip = list.find((c) => c.id === clipId);
  if (!clip) throw httpError(404, "条目不存在");
  const { title, tags } = sanitizeInput(body);
  if ("title" in body) clip.title = title;
  if ("tags" in body) clip.tags = tags;
  if ("expire" in body) clip.expireAt = resolveExpire(body.expire);
  if (clip.type === "text" && "content" in body) {
    const content = String(body.content || "");
    if (!content) throw httpError(400, "内容不能为空");
    if (content.length > CONFIG.MAX_CONTENT) throw httpError(400, "内容过长");
    clip.content = content;
  }
  if ("html" in body) clip.html = sanitizeHtml(body.html); // 富文本可独立更新（清空传空串）
  if (clip.type === "link" && "url" in body) {
    const url = String(body.url || "");
    if (!/^https?:\/\/\S+$/i.test(url)) throw httpError(400, "链接需以 http(s):// 开头");
    clip.url = cleanUrl(url); // v0.2.0：自动去追踪参数
  }
  clip.updatedAt = Date.now();
  saveClips(userId, list);
  return publicClip(clip);
}

/** 删除：返回是否删除（文件实体清理由调用方通过 fileId 处理，路由层联动 files 模块）。
 *  P-102 修复：墓碑不再在此无条件记录——改为返回 tombstone 数据，由同步域（recordTombstoneIfConfigured）
 *  判断「已配置 WebDAV」才记录（未配置同步的用户删除不产生墓碑文件）。
 *  语义不变：单独删除 → 记墓碑 → 下次同步传播删除（防旧备份复活）。 */
export function deleteClip(userId, id) {
  const clipId = assertId(id, "条目 id");
  const list = loadClips(userId);
  const idx = list.findIndex((c) => c.id === clipId);
  if (idx < 0) throw httpError(404, "条目不存在");
  const [removed] = list.splice(idx, 1);
  saveClips(userId, list);
  return { ok: true, fileId: removed.type === "file" ? removed.fileId : null, tombstone: { id: clipId, deletedAt: Date.now() } };
}

/** 全部清空：删除该用户所有条目（含归档）+ 清空墓碑（清空 = 想从网上同步，不传播删除）。
 *  返回需清理的文件实体 id 列表（路由层联动 files 模块）。 */
export function clearAllClips(userId) {
  const list = loadClips(userId);
  const fileIds = list.filter((c) => c.type === "file" && c.fileId).map((c) => c.fileId);
  saveClips(userId, []);
  saveArchive(userId, []); // 归档一并清空（清空 = 全部不要）
  clearTombstones(userId);
  return { ok: true, cleared: list.length, fileIds };
}

/** 删除归档条目（v0.6.13：归档参与 WebDAV 完整备份，用户可手动清理用不到的归档）。
 *  与 deleteClip 同语义：返回 tombstone 供同步域判断「已配置 WebDAV」才记录（删除传播到远端）。 */
export function deleteArchivedClip(userId, id) {
  const clipId = assertId(id, "条目 id");
  const arch = loadArchive(userId);
  const idx = arch.findIndex((c) => c.id === clipId);
  if (idx < 0) throw httpError(404, "条目不存在");
  const [removed] = arch.splice(idx, 1);
  saveArchive(userId, arch);
  return { ok: true, fileId: removed.type === "file" ? removed.fileId : null, tombstone: { id: clipId, deletedAt: Date.now() } };
}

/** 手动归档（v0.6.13）：活跃区条目移入归档区（编辑弹窗「归档」按钮）。原样移入（与滚动归档一致），返回 ok。 */
export function archiveClip(userId, id) {
  const clipId = assertId(id, "条目 id");
  const list = loadClips(userId);
  const idx = list.findIndex((c) => c.id === clipId);
  if (idx < 0) throw httpError(404, "条目不存在");
  const [moved] = list.splice(idx, 1);
  saveClips(userId, list);
  const arch = loadArchive(userId);
  if (!arch.some((c) => c.id === clipId)) { arch.push(moved); saveArchive(userId, arch); }
  return { ok: true };
}

/** 恢复归档（v0.6.13）：归档区条目移回活跃区。updatedAt 刷新为当前（防刚恢复被滚动立刻滚走）。 */
export function unarchiveClip(userId, id) {
  const clipId = assertId(id, "条目 id");
  const arch = loadArchive(userId);
  const idx = arch.findIndex((c) => c.id === clipId);
  if (idx < 0) throw httpError(404, "条目不存在");
  const [moved] = arch.splice(idx, 1);
  saveArchive(userId, arch);
  moved.updatedAt = Date.now();
  const list = loadClips(userId);
  list.push(moved);
  saveClips(userId, list);
  return { ok: true };
}

/** 复制计数 +1（一键复制的统计，驱动排序）。过期条目与已删除同等对待（第三轮 F-3；getClip 已删，语义并入 bumpCopy 的过期/不存在容错） */
export function bumpCopy(userId, id) {
  const clipId = assertId(id, "条目 id");
  const list = loadClips(userId);
  const clip = list.find((c) => c.id === clipId);
  if (!clip || isExpired(clip)) throw httpError(404, "条目不存在");
  clip.copyCount = (clip.copyCount || 0) + 1;
  clip.updatedAt = Date.now();
  saveClips(userId, list);
  return { ok: true, copyCount: clip.copyCount };
}

/** 星标切换：pinned 置顶优先于复制计数排序。过期条目等同不存在 */
export function togglePin(userId, id) {
  const clipId = assertId(id, "条目 id");
  const list = loadClips(userId);
  const clip = list.find((c) => c.id === clipId);
  if (!clip || isExpired(clip)) throw httpError(404, "条目不存在");
  clip.pinned = !clip.pinned;
  clip.updatedAt = Date.now();
  saveClips(userId, list);
  return { ok: true, pinned: clip.pinned };
}

/** 批量删除（v0.6.15 批量编辑）：ids 跨活跃区+归档删除，返回需清理的文件实体 id 与墓碑数据。
 *  与单条 deleteClip/deleteArchivedClip 同语义：墓碑由同步域（recordTombstoneIfConfigured）判断
 *  「已配置 WebDAV」才记录；文件实体清理由路由层联动 files 模块。ids 空或非法 id 直接 400（防御）。 */
export function batchDeleteClips(userId, ids) {
  const idSet = new Set([...new Set(ids)].map((id) => assertId(id, "条目 id")));
  const fileIds = [];
  const tombstones = [];
  let deleted = 0;
  for (const [list, save] of [
    [loadClips(userId), saveClips],
    [loadArchive(userId), saveArchive],
  ]) {
    const kept = [];
    for (const c of list) {
      if (idSet.has(c.id)) {
        deleted++;
        if (c.type === "file" && c.fileId) fileIds.push(c.fileId);
        tombstones.push({ id: c.id, deletedAt: Date.now() });
      } else kept.push(c);
    }
    if (kept.length !== list.length) save(userId, kept);
  }
  return { ok: true, deleted, fileIds, tombstones };
}

/** 批量加/减标签（v0.6.15 批量编辑）：ids 跨活跃区+归档操作，mode: "add" | "remove"。
 *  与单条 updateClip 同约束：标签去重/截长/上限 MAX_TAGS；刷新 updatedAt（WebDAV 合并的 key，
 *  不刷新则改动不会同步到远端、也不会反映到排序）。返回受影响条目数。 */
export function batchSetTags(userId, ids, tags, mode) {
  const idSet = new Set([...new Set(ids)].map((id) => assertId(id, "条目 id")));
  const names = [...new Set((Array.isArray(tags) ? tags : [])
    .map((t) => String(t).trim().slice(0, CONFIG.MAX_TAG_LEN)).filter(Boolean))];
  if (!names.length) throw httpError(400, "标签不能为空");
  if (mode !== "add" && mode !== "remove") throw httpError(400, "未知操作");
  let affected = 0;
  for (const [list, save] of [
    [loadClips(userId), saveClips],
    [loadArchive(userId), saveArchive],
  ]) {
    for (const c of list) {
      if (!idSet.has(c.id)) continue;
      const cur = new Set(c.tags || []);
      if (mode === "add") for (const t of names) cur.add(t);
      else for (const t of names) cur.delete(t);
      c.tags = [...cur].slice(0, CONFIG.MAX_TAGS);
      c.updatedAt = Date.now(); // 刷新 updatedAt——驱动 WebDAV 同步与列表排序
      affected++;
    }
    save(userId, list);
  }
  return { ok: true, affected };
}

/** 后台过期清扫：删除过期条目（活跃区 + 归档区）并返回需清理的文件 id 列表（60s 周期，路由启动时挂载）。
 * 只处理 <uid>.json（活跃）与 <uid>.archive.json（归档）；.tombstones/.webdav 等附属文件不在此列。 */
export function sweepExpired() {
  const filesToDelete = [];
  if (!fs.existsSync(CONFIG.usersDir)) return filesToDelete;
  for (const f of fs.readdirSync(CONFIG.usersDir)) {
    if (!f.endsWith(".json")) continue;
    const isArchive = f.endsWith(".archive.json");
    const suffixLen = isArchive ? ".archive.json".length : ".json".length;
    const userId = f.slice(0, -suffixLen);
    if (!userId || /[.]/.test(userId)) continue; // 跳过 tombstones/webdav 等带后缀文件
    const list = isArchive ? loadArchive(userId) : loadClips(userId);
    const before = list.length;
    const kept = [];
    for (const c of list) {
      if (isExpired(c)) {
        if (c.type === "file" && c.fileId) filesToDelete.push({ userId, fileId: c.fileId });
      } else kept.push(c);
    }
    if (kept.length !== before) {
      if (isArchive) saveArchive(userId, kept);
      else saveClips(userId, kept);
    }
  }
  return filesToDelete;
}
