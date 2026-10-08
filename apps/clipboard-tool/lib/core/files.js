// lib/core/files.js - 文件域：上传边界 + 归属校验 + 物理存取
// 安全：大小上限、类型黑名单（仅拒可执行/脚本）、随机文件名（防路径穿越）、下载强制 attachment。
import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import { CONFIG, BLOCKED_MIME, BLOCKED_EXT, EXT_BY_MIME, EXT_SAFE_RE } from "./config.js";
import { httpError, assertId, rmForce } from "./store.js";

function userFilesDir(userId) {
  return path.join(CONFIG.filesDir, userId);
}

/** 保存上传文件：返回元数据（文件名随机生成，原始名仅作展示）。
 * 类型策略：黑名单（拒绝可执行/脚本）；空 MIME 或未知类型按原始扩展名兜底——允许 .json 等浏览器不给类型的文件。 */
export function saveFile(userId, { buffer, originalName = "file", mime = "" }) {
  const uid = assertId(userId, "用户 id");
  if (!Buffer.isBuffer(buffer) || buffer.length === 0) throw httpError(400, "文件为空");
  if (buffer.length > CONFIG.MAX_FILE) throw httpError(413, `文件超过 ${CONFIG.MAX_FILE / 1024 / 1024}MB 上限`);
  const m = String(mime || "").toLowerCase();
  const extFromName = String(originalName || "").split(".").pop().toLowerCase();
  if (BLOCKED_MIME.has(m) || BLOCKED_EXT.has(extFromName)) {
    throw httpError(415, `不支持上传该类型: ${m || extFromName || "未知"}`);
  }
  const fileId = crypto.randomUUID();
  const ext = EXT_BY_MIME[m] || (EXT_SAFE_RE.test(extFromName) ? extFromName : "bin");
  const name = `${fileId}.${ext}`;
  const dir = userFilesDir(uid);
  fs.mkdirSync(dir, { recursive: true });
  fs.writeFileSync(path.join(dir, name), buffer);
  return {
    fileId,
    fileName: String(originalName || "file").slice(0, 255),
    fileSize: buffer.length,
    fileMime: m,
  };
}

/** 读取用户文件流路径：校验归属 + 存在性（供下载）。
 * 磁盘文件名 = <fileId>.<安全扩展名>，API 参数为纯 fileId（已按 UUID 白名单校验），
 * 按前缀在用户目录内查找，杜绝目录穿越。 */
export function getFilePath(userId, fileId) {
  const uid = assertId(userId, "用户 id");
  const fid = assertId(fileId, "文件 id");
  const dir = userFilesDir(uid);
  if (!fs.existsSync(dir)) throw httpError(404, "文件不存在");
  const match = fs.readdirSync(dir).find((f) => f.startsWith(fid + "."));
  if (!match) throw httpError(404, "文件不存在");
  return path.join(dir, match);
}

/** 物理删除用户文件（删除条目/过期清扫/删用户时调用；不存在的静默） */
export function deleteFile(userId, fileId) {
  try {
    const p = getFilePath(userId, fileId);
    rmForce(p);
  } catch { /* 不存在或无效：静默 */ }
}
