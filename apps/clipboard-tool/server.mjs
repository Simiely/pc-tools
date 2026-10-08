// server.mjs - 剪贴板工具入口（薄层）：HTTP 装配 + 路由分发 + 静态服务 + 过期清扫
// 平台托管时端口从 argv[2] 传入；数据目录由平台注入 CAP_STORAGE_DIR。
import http from "node:http";
import fs from "node:fs";
import path from "node:path";
import { execSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import { CONFIG } from "./lib/core/config.js";
import { matchRoute, withParams } from "./lib/routes/index.js";
import { sweepExpired } from "./lib/core/clips.js";
import { deleteFile } from "./lib/core/files.js";
import { runAutoSync, migrateSyncFilesDefaults } from "./lib/core/webdav.js";
import { pruneExpiredSessions } from "./lib/core/users.js";
import { sendJson } from "./lib/routes/helpers.js";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
// v0.6.14 版本指纹：启动日志打印 v版本 (git commit)——实例身份可追溯，
// 排查多实例混跑（2026-08-27 数据清空事故）时先看日志确认代码版本。
let VERSION = "dev";
try { VERSION = JSON.parse(fs.readFileSync(path.join(__dirname, "package.json"), "utf8")).version || "dev"; } catch {}
let GIT_SHORT = "";
try { GIT_SHORT = execSync("git rev-parse --short HEAD", { cwd: __dirname, encoding: "utf8", stdio: ["ignore", "pipe", "ignore"] }).trim().slice(0, 7); } catch {}
const INDEX_HTML = path.join(__dirname, "public", "index.html");
const APP_JS = path.join(__dirname, "public", "app.js"); // v0.4.0：前端 JS 拆独立文件

// P1-4：静态资源启动时读入内存缓存，避免每次请求 readFileSync（高频路径）
// 开发期改 index.html/app.js 需重启服务生效（平台托管/独立运行均为常规操作）。
const STATIC = {
  html: { type: "text/html; charset=utf-8", body: fs.readFileSync(INDEX_HTML) },
  js: { type: "text/javascript; charset=utf-8", body: fs.readFileSync(APP_JS) },
  // v0.6.11：diag.html 并入缓存——此前每次请求 readFileSync，与静态策略不一致
  diag: { type: "text/html; charset=utf-8", body: fs.readFileSync(path.join(__dirname, "public", "diag.html")) },
};

// v0.7.6 一次性迁移：存量「同步文件实体」由 false 翻 true（旧默认值导致实体从未备份上云，
// 换设备后条目可见但下载 404）。仅对从未迁移过的配置生效，之后用户主动关闭会被尊重。
try {
  const flipped = migrateSyncFilesDefaults();
  if (flipped) console.log(`[v0.7.6] 已为 ${flipped} 个用户开启「同步文件实体」（旧默认值导致文件/图片未备份上云）`);
} catch { /* 迁移失败不影响启动 */ }

// 后台过期清扫：60s 周期，删除过期条目并联动清理文件实体
setInterval(() => {
  try {
    for (const { userId, fileId } of sweepExpired()) deleteFile(userId, fileId);
  } catch { /* 清扫失败不影响服务 */ }
}, CONFIG.SWEEP_INTERVAL_MS);

// 后台会话过期清理：60s 周期，惰性清理过期 token（v0.3.0 会话持久化）
setInterval(() => {
  try { pruneExpiredSessions(); } catch { /* 清理失败不影响服务 */ }
}, CONFIG.SWEEP_INTERVAL_MS);

// 后台定时自动同步：60s 周期，检查所有启用自动同步的用户是否到期（间隔各自配置，默认 12h）
setInterval(() => {
  try { runAutoSync(); } catch { /* 自动同步失败不影响服务 */ }
}, 60_000);

const server = http.createServer(async (req, res) => {
  const url = new URL(req.url, "http://127.0.0.1");
  try {
    // 健康检查（平台托管要求）
    if (url.pathname === "/health") {
      return sendJson(res, 200, { ok: true, name: "clipboard" });
    }
    // 静态页面（P1-4：内存缓存，启动时读入）
    if (url.pathname === "/" || url.pathname === "/index.html") {
      res.writeHead(200, {
        "Content-Type": STATIC.html.type,
        "X-Content-Type-Options": "nosniff",
        "X-Frame-Options": "DENY",
      });
      return res.end(STATIC.html.body);
    }
    // 前端 JS（v0.4.0 拆分独立文件；P1-4：内存缓存）
    if (url.pathname === "/app.js") {
      res.writeHead(200, {
        "Content-Type": STATIC.js.type,
        "X-Content-Type-Options": "nosniff",
      });
      return res.end(STATIC.js.body);
    }
    // 剪贴板环境诊断页（v0.6.8：隔离测试 iframe 内 API 可用性，不走查不猜；v0.6.11：静态缓存）
    if (url.pathname === "/diag.html") {
      res.writeHead(200, { "Content-Type": STATIC.diag.type, "X-Content-Type-Options": "nosniff" });
      return res.end(STATIC.diag.body);
    }
    // API 路由
    const r = matchRoute(url, req.method);
    if (!r) return sendJson(res, 404, { ok: false, error: "not found" });
    await r.handler(req, res, withParams(url, r.params));
  } catch (e) {
    sendJson(res, e.status || 500, { ok: false, error: e.message || "internal error" });
  }
});

server.listen(CONFIG.PORT, "127.0.0.1", () => {
  console.log(`clipboard v${VERSION} (${GIT_SHORT || "no-git"}) running on ${CONFIG.PORT} (data: ${path.dirname(CONFIG.usersFile)})`);
});

// 进程级异常兜底（v0.6.13）：任何未捕获异常/拒绝 → 记录但不退出进程。
// 路由层已有 try/catch，这里是兜最后一层（异步回调/定时器/未预期错误）。
// 本工具数据全部落盘 JSON（原子写、无事务态），进程存活比崩溃重启对用户更友好；
// 若反复崩溃（如磁盘故障），由平台健康检查/重启策略兜底。
process.on("uncaughtException", (err) => {
  console.error("[uncaughtException]", err && err.stack ? err.stack : String(err));
});
process.on("unhandledRejection", (err) => {
  console.error("[unhandledRejection]", err && err.stack ? err.stack : String(err));
});
