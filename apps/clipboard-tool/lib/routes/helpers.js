// lib/routes/helpers.js - 路由共享工具：响应 + JSON 读取（限长）+ multipart 解析 + 会话中间件
// requireAuth 放这里而不是 index.js，避免各域路由 → index.js 的循环依赖。
import { CONFIG } from "../core/config.js";
import { verifyToken } from "../core/users.js";

/** 会话中间件：解析 Authorization: Bearer <token> → userId；失败抛 401。
 * 支持 url 参数 token（?token=xxx）——<img> 等资源标签无法带 header，图片预览场景专用（v0.4.1）。 */
export function requireAuth(req, url = null) {
  let token = (req.headers["authorization"] || "").replace(/^Bearer\s+/i, "");
  if (!token && url) token = url.searchParams.get("token") || "";
  const userId = verifyToken(token);
  if (!userId) {
    const e = new Error("未登录或会话已失效");
    e.status = 401;
    throw e;
  }
  return userId;
}

/** 统一 JSON 响应（自动加安全响应头） */
export function sendJson(res, status, data) {
  res.writeHead(status, {
    "Content-Type": "application/json; charset=utf-8",
    "X-Content-Type-Options": "nosniff",
    "X-Frame-Options": "DENY",
    "Cache-Control": "no-store",
  });
  res.end(JSON.stringify(data));
}

/** 读取请求体（限长防超长 body），返回 Buffer */
export async function readBody(req, limit = CONFIG.MAX_JSON_BODY) {
  const chunks = [];
  let size = 0;
  for await (const c of req) {
    size += c.length;
    if (size > limit) {
      // v0.6.11：超限立即排空剩余流（req.resume()）——否则连接 keep-alive 复用时代理/客户端等待挂起
      req.resume().on("data", () => {});
      const e = new Error("请求体过大");
      e.status = 413;
      throw e;
    }
    chunks.push(c);
  }
  return Buffer.concat(chunks);
}

/** JSON body 解析（业务层再校验字段） */
export async function jsonBody(req) {
  const buf = await readBody(req);
  if (!buf.length) return {};
  try {
    return JSON.parse(buf.toString("utf8"));
  } catch {
    const e = new Error("JSON 解析失败");
    e.status = 400;
    throw e;
  }
}

/** 从 Content-Type 提取 boundary */
export function boundaryOf(contentType = "") {
  const m = /boundary=([^;]+)/i.exec(contentType);
  return m ? m[1].trim().replace(/^"|"$/g, "") : "";
}

/**
 * 零依赖 multipart/form-data 解析（基于 Buffer 字节定位，兼容二进制内容）
 * 返回 { field: string | { buffer, filename, mime } } —— 有 filename 视为文件字段
 */
export function parseMultipart(body, boundary) {
  const result = {};
  if (!boundary) return result;
  const first = body.indexOf(Buffer.from("--" + boundary));
  if (first < 0) return result;
  let cursor = first + boundary.length + 2; // 跳过 --boundary
  if (body.slice(cursor, cursor + 2).toString() === "\r\n") cursor += 2;
  const parts = [];
  while (true) {
    const idx = body.indexOf(Buffer.from("\r\n--" + boundary), cursor);
    const partEnd = idx < 0 ? body.length : idx;
    parts.push(body.slice(cursor, partEnd));
    if (idx < 0) break;
    cursor = idx + boundary.length + 4;
    if (body.slice(cursor, cursor + 2).toString() === "--") break; // 结尾 --boundary--
    if (body.slice(cursor, cursor + 2).toString() === "\r\n") cursor += 2;
  }
  for (const part of parts) {
    const headerEnd = part.indexOf(Buffer.from("\r\n\r\n"));
    if (headerEnd < 0) continue;
    const header = part.slice(0, headerEnd).toString();
    const data = part.slice(headerEnd + 4);
    const nameM = /name="([^"]+)"/.exec(header);
    if (!nameM) continue;
    const fileM = /filename="([^"]*)"/.exec(header);
    const typeM = /Content-Type:\s*(\S+)/i.exec(header);
    if (fileM) {
      result[nameM[1]] = { buffer: data, filename: fileM[1] || "", mime: (typeM && typeM[1]) || "" };
    } else {
      result[nameM[1]] = data.toString("utf8");
    }
  }
  return result;
}
