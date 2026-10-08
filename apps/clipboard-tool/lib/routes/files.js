// lib/routes/files.js - 文件域路由：multipart 上传 + 下载（attachment 强制下载防执行）
import fs from "node:fs";
import path from "node:path";
import { sendJson, readBody, parseMultipart, boundaryOf } from "./helpers.js";
import * as files from "../core/files.js";
import { CONFIG } from "../core/config.js";
import { requireAuth } from "./helpers.js";

// v0.4.1：图片扩展名 → 渲染 mime（<img> 预览用）；非图片保持 octet-stream + attachment 防执行
// 注意：svg 不在列表——SVG 可含脚本，inline 渲染有 XSS 风险，保持 attachment 下载（防执行优先）
const IMAGE_EXT = new Set(["png", "jpg", "jpeg", "gif", "webp", "bmp", "ico", "avif"]);
const EXT_TO_MIME = {
  png: "image/png", jpg: "image/jpeg", jpeg: "image/jpeg", gif: "image/gif",
  webp: "image/webp", bmp: "image/bmp", ico: "image/x-icon", avif: "image/avif",
};

export const fileRoutes = [
  { p: "/api/files", m: "POST", handler: async (req, res) => {
    const userId = requireAuth(req);
    const contentType = req.headers["content-type"] || "";
    if (!contentType.includes("multipart/form-data")) {
      return sendJson(res, 415, { ok: false, error: "需 multipart/form-data" });
    }
    const body = await readBody(req, CONFIG.MAX_FILE + 64 * 1024);
    const fields = parseMultipart(body, boundaryOf(contentType));
    const file = fields.file;
    if (!file || !file.buffer) return sendJson(res, 400, { ok: false, error: "缺少 file 字段" });
    const meta = files.saveFile(userId, {
      buffer: file.buffer,
      originalName: file.filename,
      mime: file.mime || fields.mime || "",
    });
    sendJson(res, 201, { ok: true, file: meta });
  } },
  { p: "/api/files/:fileId", m: "GET", handler: async (req, res, url) => {
    const userId = requireAuth(req, url); // v0.4.1：支持 ?token= （<img> 预览场景无法带 header）
    const fp = files.getFilePath(userId, url.params.fileId);
    const stat = fs.statSync(fp);
    // 图片类型 → 返回真实 mime + inline（供 <img> 缩略图/预览渲染）；
    // 其他类型 → 一律 attachment + octet-stream（即使 SVG/HTML 也不会被浏览器执行，防 XSS）。
    // v0.4.1 修复：此前统一 octet-stream + nosniff，浏览器禁止嗅探导致图片 <img> 不渲染。
    const ext = path.extname(fp).slice(1).toLowerCase();
    const isImage = IMAGE_EXT.has(ext);
    const mime = EXT_TO_MIME[ext] || "application/octet-stream";
    res.writeHead(200, {
      "Content-Type": isImage ? mime : "application/octet-stream",
      "Content-Disposition": isImage ? "inline" : `attachment; filename="file${path.extname(fp)}"`,
      "Content-Length": stat.size,
      "X-Content-Type-Options": isImage ? "nosniff" : "nosniff",
      "Cache-Control": "private, max-age=60",
    });
    fs.createReadStream(fp).pipe(res);
  } },
];
