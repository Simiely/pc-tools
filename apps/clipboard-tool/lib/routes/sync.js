// lib/routes/sync.js - WebDAV 备份同步路由：配置 / 测试保存 / 一键同步
import { sendJson, jsonBody } from "./helpers.js";
import { requireAuth } from "./helpers.js";
import * as webdav from "../core/webdav.js";

export const syncRoutes = [
  { p: "/api/sync/config", m: "GET", handler: async (req, res) => {
    const userId = requireAuth(req);
    const cfg = webdav.getSyncConfig(userId);
    sendJson(res, 200, {
      ok: true,
      configured: !!cfg,
      url: cfg ? cfg.url : "",
      user: cfg ? cfg.user : "",
      hasPass: !!(cfg && cfg.pass),
      syncFiles: !!(cfg && cfg.syncFiles),
      autoSync: !!(cfg && cfg.autoSync),
      intervalMin: cfg ? cfg.intervalMin : 720,
      lastSyncError: cfg ? (cfg.lastSyncError || "") : "", // P-104：自动同步上次失败信息（可观测）
    });
  } },
  { p: "/api/sync/config", m: "POST", handler: async (req, res) => {
    const userId = requireAuth(req);
    const body = await jsonBody(req);
    const cfg = { url: body.url, user: body.user, pass: body.pass, syncFiles: body.syncFiles, autoSync: body.autoSync, intervalMin: body.intervalMin };
    // 先测连通，通过才保存（参考项目：测试失败不保存配置）
    await webdav.testConnection(cfg);
    webdav.saveSyncConfig(userId, cfg);
    sendJson(res, 200, { ok: true });
  } },
  { p: "/api/sync/run", m: "POST", handler: async (req, res) => {
    const userId = requireAuth(req);
    const r = await webdav.runSync(userId);
    sendJson(res, 200, r);
  } },
];
