// lib/routes/clips.js - 条目域路由：列表/搜索/标签 / 新增 / 编辑 / 删除 / 复制计数
import { sendJson, jsonBody } from "./helpers.js";
import * as clips from "../core/clips.js";
import { deleteFile } from "../core/files.js";
import { recordTombstoneIfConfigured } from "../core/webdav.js";
import { requireAuth } from "./helpers.js";
import { httpError } from "../core/store.js";

export const clipRoutes = [
  { p: "/api/clips", m: "GET", handler: async (req, res, url) => {
    const userId = requireAuth(req);
    const q = url.searchParams.get("q") || "";
    const tag = url.searchParams.get("tag") || "";
    const archived = url.searchParams.get("archived") === "1"; // 含归档查询（v0.2.0）
    sendJson(res, 200, { ok: true, clips: clips.listClips(userId, { q, tag, archived }) });
  } },
  { p: "/api/tags", m: "GET", handler: async (req, res) => {
    const userId = requireAuth(req);
    sendJson(res, 200, { ok: true, tags: clips.listTags(userId) });
  } },
  // 标签管理（P1-6）：重命名 / 删除，跨活跃区+归档全部条目生效
  { p: "/api/tags/:name", m: "PUT", handler: async (req, res, url) => {
    const userId = requireAuth(req);
    const body = await jsonBody(req);
    const r = clips.renameTag(userId, url.params.name, body.name);
    sendJson(res, 200, r);
  } },
  { p: "/api/tags/:name", m: "DELETE", handler: async (req, res, url) => {
    const userId = requireAuth(req);
    const r = clips.deleteTag(userId, url.params.name);
    sendJson(res, 200, r);
  } },
  { p: "/api/clips", m: "POST", handler: async (req, res) => {
    const userId = requireAuth(req);
    const body = await jsonBody(req);
    const clip = clips.createClip(userId, body);
    sendJson(res, 201, { ok: true, clip });
  } },
  { p: "/api/clips/:id", m: "PUT", handler: async (req, res, url) => {
    const userId = requireAuth(req);
    const body = await jsonBody(req);
    const clip = clips.updateClip(userId, url.params.id, body);
    sendJson(res, 200, { ok: true, clip });
  } },
  { p: "/api/clips/:id", m: "DELETE", handler: async (req, res, url) => {
    const userId = requireAuth(req);
    // v0.6.13：归档条目也可删除（WebDAV 完整备份后用户可手动清理用不到的归档）——活跃区没有则查归档
    let r;
    try {
      r = clips.deleteClip(userId, url.params.id);
    } catch (e) {
      if (e.status !== 404) throw e;
      r = clips.deleteArchivedClip(userId, url.params.id);
    }
    if (r.fileId) deleteFile(userId, r.fileId); // 联动清理文件实体
    // v0.6.14：墓碑记录下沉到 core（recordTombstoneIfConfigured 内部判断是否已配置 WebDAV）
    if (r.tombstone) recordTombstoneIfConfigured(userId, r.tombstone.id);
    sendJson(res, 200, r);
  } },
  // 手动归档（v0.6.13：编辑弹窗「归档」按钮）——活跃区移入归档区，非删除不记墓碑（归档仍参与 WebDAV 同步）
  { p: "/api/clips/:id/archive", m: "POST", handler: async (req, res, url) => {
    const userId = requireAuth(req);
    sendJson(res, 200, clips.archiveClip(userId, url.params.id));
  } },
  // 恢复归档（v0.6.13：归档卡片「↺ 恢复」）——归档区移回活跃区，updatedAt 刷新防被滚动
  { p: "/api/clips/:id/restore", m: "POST", handler: async (req, res, url) => {
    const userId = requireAuth(req);
    sendJson(res, 200, clips.unarchiveClip(userId, url.params.id));
  } },
  // 全部清空：不记墓碑（清空 = 想从网上同步，下次 WebDAV 同步从远端拉回恢复）
  { p: "/api/clips", m: "DELETE", handler: async (req, res) => {
    const userId = requireAuth(req);
    const r = clips.clearAllClips(userId);
    if (r.fileIds.length) {
      for (const fid of r.fileIds) deleteFile(userId, fid); // 逐个清理文件实体
    }
    sendJson(res, 200, r);
  } },
  { p: "/api/clips/:id/copy", m: "POST", handler: async (req, res, url) => {
    const userId = requireAuth(req);
    const r = clips.bumpCopy(userId, url.params.id);
    sendJson(res, 200, r);
  } },
  { p: "/api/clips/:id/pin", m: "POST", handler: async (req, res, url) => {
    const userId = requireAuth(req);
    const r = clips.togglePin(userId, url.params.id);
    sendJson(res, 200, r);
  } },
  // 批量编辑（v0.6.15）：多选后批量删除 / 批量加标签 / 批量减标签。单入口按 action 分发，
  // 删除与单条同语义（记墓碑 + 联动文件实体清理），标签操作刷新 updatedAt（WebDAV 合并 key）。
  { p: "/api/clips/batch", m: "POST", handler: async (req, res) => {
    const userId = requireAuth(req);
    const body = await jsonBody(req);
    const ids = Array.isArray(body.ids) ? body.ids.filter((x) => typeof x === "string") : [];
    if (!ids.length) throw httpError(400, "未选择条目");
    if (body.action === "delete") {
      const r = clips.batchDeleteClips(userId, ids);
      for (const fid of r.fileIds) deleteFile(userId, fid);
      for (const t of r.tombstones) recordTombstoneIfConfigured(userId, t.id);
      return sendJson(res, 200, r);
    }
    if (body.action === "addTags" || body.action === "removeTags") {
      const r = clips.batchSetTags(userId, ids, body.tags, body.action === "addTags" ? "add" : "remove");
      return sendJson(res, 200, r);
    }
    throw httpError(400, "未知操作");
  } },
  // 导出 / 导入（本地文件备份，v0.2.0）
  { p: "/api/export", m: "GET", handler: async (req, res) => {
    const userId = requireAuth(req);
    sendJson(res, 200, { ok: true, data: clips.exportClips(userId) });
  } },
  { p: "/api/import", m: "POST", handler: async (req, res) => {
    const userId = requireAuth(req);
    const body = await jsonBody(req);
    const payload = body && body.data ? body.data : body; // 兼容 { data } 包装与裸 { clips }
    const r = clips.importClips(userId, payload && payload.clips);
    sendJson(res, 200, r);
  } },
];
