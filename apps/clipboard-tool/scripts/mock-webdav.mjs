// scripts/mock-webdav.mjs - 极简 WebDAV 服务器（仅测试用：MKCOL/PUT/GET/DELETE + Basic 认证）
// 用法：node scripts/mock-webdav.mjs <port> [<dataDir>]
// 认证：user=admin pass=admin123（与测试配置一致）；数据存内存 + 落盘 dataDir
// 注意（Windows）：dataDir 请用绝对路径（如 C:/Temp/mock-webdav），
//   path.join 对相对路径（如 /tmp/...）产出无盘符路径，与 path.resolve 比较会恒 false（P0-3 修复：两侧统一 resolve）。
import http from "node:http";
import fs from "node:fs";
import path from "node:path";

const PORT = parseInt(process.argv[2] || "8180", 10);
const DATA = path.resolve(process.argv[3] || path.join(process.cwd(), ".data", "mock-webdav"));
fs.mkdirSync(DATA, { recursive: true });

function authOk(req) {
  const h = req.headers["authorization"] || "";
  const expect = "Basic " + Buffer.from("admin:admin123").toString("base64");
  return h === expect;
}

/** 路径拼接 + 防穿越：两侧统一 path.resolve 归一化（相对/绝对路径均一致判定），
 *  结果必须在 dataDir 内，否则返回 null（400 bad path）。 */
function safeJoin(dir, rel) {
  const p = path.resolve(path.join(dir, rel.replace(/^\//, "")));
  if (!p.startsWith(path.resolve(dir) + path.sep) && p !== path.resolve(dir)) return null;
  return p;
}

const server = http.createServer((req, res) => {
  if (!authOk(req)) { res.writeHead(401); res.end("auth required"); return; }
  const rel = decodeURIComponent(new URL(req.url, "http://x").pathname);
  const fp = safeJoin(DATA, rel);
  if (!fp) { res.writeHead(400); res.end("bad path"); return; }
  const method = req.method;
  if (method === "MKCOL") {
    fs.mkdirSync(fp, { recursive: true });
    res.writeHead(201); res.end();
  } else if (method === "PUT") {
    const chunks = [];
    req.on("data", (c) => chunks.push(c));
    req.on("end", () => {
      fs.mkdirSync(path.dirname(fp), { recursive: true });
      fs.writeFileSync(fp, Buffer.concat(chunks));
      res.writeHead(201); res.end();
    });
  } else if (method === "GET") {
    if (!fs.existsSync(fp)) { res.writeHead(404); res.end("not found"); return; }
    const data = fs.readFileSync(fp);
    res.writeHead(200, { "Content-Type": "application/json" });
    res.end(data);
  } else if (method === "DELETE") {
    if (!fs.existsSync(fp)) { res.writeHead(404); res.end(); return; }
    fs.unlinkSync(fp);
    res.writeHead(204); res.end();
  } else {
    res.writeHead(405); res.end("method not allowed");
  }
});

server.listen(PORT, () => console.log(`mock-webdav on ${PORT}, data: ${DATA}`));
