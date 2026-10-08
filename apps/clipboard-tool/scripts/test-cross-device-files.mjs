// scripts/test-cross-device-files.mjs - 跨设备文件实体同步 E2E（回归门禁）
// 场景：两台设备（各自独立数据目录 + 独立服务实例，账号名相同、userId 不同）共用同一 WebDAV。
// 覆盖：设备A 上传图片/文件条目 → 同步（实体上云）→ 设备B 同账号名同步 → 实体拉回 + 可下载。
// 运行：node scripts/test-cross-device-files.mjs（自起 mock-webdav + 两个实例，跑完自动关闭）
import { spawn } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const ROOT = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const DAV_PORT = 8181;
const DAV = `http://127.0.0.1:${DAV_PORT}/dav/`;
const DAV_DIR = "C:/Temp/clip-xdav";
const DEV_A = { port: 8133, dir: "C:/Temp/clip-devA" };
const DEV_B = { port: 8134, dir: "C:/Temp/clip-devB" };
const ACCOUNT = "CrossDev";
const AUTH = { Authorization: "Basic " + Buffer.from("admin:admin123").toString("base64") };

let pass = 0, fail = 0;
const ok = (n, c, extra = "") => { c ? pass++ : fail++; console.log((c ? "✅" : "❌") + " " + n + (c ? "" : "  << " + extra)); };

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const procs = [];

function startProc(args, env) {
  const p = spawn(process.execPath, args, { cwd: ROOT, env: { ...process.env, ...env }, stdio: "ignore" });
  procs.push(p);
  return p;
}
async function waitUp(url, label) {
  for (let i = 0; i < 60; i++) {
    try { const r = await fetch(url); if (r.status < 500) return true; } catch {}
    await sleep(200);
  }
  throw new Error("服务未起来: " + label);
}
function cleanDir(d) { try { fs.rmSync(d, { recursive: true, force: true }); } catch {} fs.mkdirSync(d, { recursive: true }); }

async function api(dev, method, p, { token, json, form } = {}) {
  const headers = {};
  if (token) headers["Authorization"] = "Bearer " + token;
  let body;
  if (form) body = form;
  else if (json !== undefined) { headers["Content-Type"] = "application/json"; body = JSON.stringify(json); }
  const r = await fetch(`http://127.0.0.1:${dev.port}${p}`, { method, headers, body });
  let d = null;
  try { d = await r.json(); } catch {}
  return { status: r.status, data: d, raw: r };
}

// 1×1 PNG
const PNG = Buffer.from([
  0x89,0x50,0x4e,0x47,0x0d,0x0a,0x1a,0x0a,0x00,0x00,0x00,0x0d,0x49,0x48,0x44,0x52,
  0x00,0x00,0x00,0x01,0x00,0x00,0x00,0x01,0x08,0x06,0x00,0x00,0x00,0x1f,0x15,0xc4,0x89,
  0x00,0x00,0x00,0x0a,0x49,0x44,0x41,0x54,0x78,0x9c,0x63,0x00,0x01,0x00,0x00,0x05,0x00,0x01,
  0x0d,0x0a,0x2d,0xb4,0x00,0x00,0x00,0x00,0x49,0x45,0x4e,0x44,0xae,0x42,0x60,0x82,
]);

async function uploadFile(dev, token, buf, filename, mime) {
  const fd = new FormData();
  fd.append("file", new Blob([new Uint8Array(buf)], { type: mime }), filename);
  const r = await api(dev, "POST", "/api/files", { token, form: fd });
  return r.data && r.data.file;
}

async function main() {
  cleanDir(DAV_DIR); cleanDir(DEV_A.dir); cleanDir(DEV_B.dir);
  startProc(["scripts/mock-webdav.mjs", String(DAV_PORT), DAV_DIR], {});
  startProc(["server.mjs", String(DEV_A.port)], { CAP_STORAGE_DIR: DEV_A.dir });
  startProc(["server.mjs", String(DEV_B.port)], { CAP_STORAGE_DIR: DEV_B.dir });
  await waitUp(DAV, "mock-webdav");
  await waitUp(`http://127.0.0.1:${DEV_A.port}/api/users`, "devA");
  await waitUp(`http://127.0.0.1:${DEV_B.port}/api/users`, "devB");

  // ---- 设备A：建账号 + 图片条目 + 同步上云 ----
  const ua = await api(DEV_A, "POST", "/api/users", { json: { name: ACCOUNT } });
  ok("A 建账号", !!ua.data.token, JSON.stringify(ua.data));
  const tkA = ua.data.token, uidA = ua.data.user.id;

  const fA = await uploadFile(DEV_A, tkA, PNG, "截图.png", "image/png");
  ok("A 上传图片", !!fA && !!fA.fileId, JSON.stringify(fA));
  const clipA = await api(DEV_A, "POST", "/api/clips", { token: tkA, json: {
    type: "file", fileId: fA.fileId, fileName: fA.fileName, fileSize: fA.fileSize, fileMime: fA.fileMime, title: "截图.png",
  }});
  ok("A 建文件条目", clipA.status === 200 || clipA.status === 201, JSON.stringify(clipA.data));

  const cfgA = await api(DEV_A, "POST", "/api/sync/config", { token: tkA, json: { url: DAV, user: "admin", pass: "admin123", syncFiles: true } });
  ok("A 配置 WebDAV(syncFiles=true)", cfgA.status === 200, JSON.stringify(cfgA.data));
  const sA = await api(DEV_A, "POST", "/api/sync/run", { token: tkA });
  ok("A 同步成功", sA.data && sA.data.ok, JSON.stringify(sA.data));

  const rRemote = await fetch(DAV + "workbuddy/剪贴板/files/" + ACCOUNT + "/" + fA.fileId + ".png", { headers: AUTH });
  ok("远端有图片实体", rRemote.status === 200, "HTTP " + rRemote.status);

  // ---- 设备B：同账号名，同步拉回 ----
  const ub = await api(DEV_B, "POST", "/api/users", { json: { name: ACCOUNT } });
  ok("B 建同名账号", !!ub.data.token, JSON.stringify(ub.data));
  const tkB = ub.data.token, uidB = ub.data.user.id;
  ok("B 与 A 的 userId 不同（真实跨设备）", uidA !== uidB, uidA + " vs " + uidB);

  // ---- 场景X（v0.7.6 核心回归）：B 首次同步【未勾 syncFiles】→ 拉回必须无条件生效 ----
  // 旧行为：此处本地无实体 + 下载 404 = 用户报的「另一台设备用不了」。新行为：实体照样拉回。
  await api(DEV_B, "POST", "/api/sync/config", { token: tkB, json: { url: DAV, user: "admin", pass: "admin123", syncFiles: false } });
  await api(DEV_B, "POST", "/api/sync/run", { token: tkB });
  const dirBX = path.join(DEV_B.dir, "files", uidB);
  let hitX = null;
  try { hitX = fs.readdirSync(dirBX).find((f) => f.startsWith(fA.fileId + ".")); } catch {}
  ok("X 未勾实体: 拉回无条件生效", !!hitX, "dir=" + dirBX + " 内容=" + (fs.existsSync(dirBX) ? fs.readdirSync(dirBX).join(",") : "(无目录)"));
  const dlX = await fetch(`http://127.0.0.1:${DEV_B.port}/api/files/${fA.fileId}?token=${tkB}`);
  ok("X 未勾实体: 仍可下载(HTTP 200)", dlX.status === 200, "HTTP " + dlX.status);

  const cfgB = await api(DEV_B, "POST", "/api/sync/config", { token: tkB, json: { url: DAV, user: "admin", pass: "admin123", syncFiles: true } });
  ok("B 配置 WebDAV(syncFiles=true)", cfgB.status === 200, JSON.stringify(cfgB.data));
  const sB = await api(DEV_B, "POST", "/api/sync/run", { token: tkB });
  ok("B 同步成功", sB.data && sB.data.ok, JSON.stringify(sB.data));

  const listB = await api(DEV_B, "GET", "/api/clips", { token: tkB });
  const gotClip = (listB.data.clips || []).find((c) => c.fileId === fA.fileId);
  ok("B 拉回条目", !!gotClip, JSON.stringify((listB.data.clips || []).map((c) => ({ id: c.id, type: c.type, fileId: c.fileId }))));

  // 本地实体是否落盘
  const dirB = path.join(DEV_B.dir, "files", uidB);
  let localHit = null;
  try { localHit = fs.readdirSync(dirB).find((f) => f.startsWith(fA.fileId + ".")); } catch {}
  ok("B 本地实体落盘", !!localHit, "dir=" + dirB + " files=" + (fs.existsSync(dirB) ? fs.readdirSync(dirB).join(",") : "(不存在)"));

  // 下载可用性（<img> / 下载按钮实际走的接口）
  const dl = await fetch(`http://127.0.0.1:${DEV_B.port}/api/files/${fA.fileId}?token=${tkB}`);
  ok("B 下载实体 HTTP 200（勾选后自愈）", dl.status === 200, "HTTP " + dl.status);
  if (dl.status === 200) {
    const buf = Buffer.from(await dl.arrayBuffer());
    ok("B 下载内容字节一致", buf.length === PNG.length && buf.equals(PNG), `len ${buf.length} vs ${PNG.length}`);
  }

  // ---- 反向验证：上传仍受 syncFiles 约束（关掉就不该把实体推上云） ----
  const fB = await uploadFile(DEV_B, tkB, Buffer.from([1, 2, 3, 4, 5]), "note.txt", "text/plain");
  await api(DEV_B, "POST", "/api/clips", { token: tkB, json: {
    type: "file", fileId: fB.fileId, fileName: fB.fileName, fileSize: fB.fileSize, fileMime: fB.fileMime, title: "note.txt",
  }});
  await api(DEV_B, "POST", "/api/sync/config", { token: tkB, json: { url: DAV, user: "admin", pass: "admin123", syncFiles: false } });
  await api(DEV_B, "POST", "/api/sync/run", { token: tkB });
  const rNoUp = await fetch(DAV + "workbuddy/剪贴板/files/" + ACCOUNT + "/" + fB.fileId + ".txt", { headers: AUTH });
  ok("未勾实体: 不上传(远端无该实体)", rNoUp.status === 404, "HTTP " + rNoUp.status);

  // ---- 默认开启：不传 syncFiles 首次配置 → 应为 true ----
  const uc = await api(DEV_A, "POST", "/api/users", { json: { name: "DefaultOn" } });
  await api(DEV_A, "POST", "/api/sync/config", { token: uc.data.token, json: { url: DAV, user: "admin", pass: "admin123" } });
  const cfgC = await api(DEV_A, "GET", "/api/sync/config", { token: uc.data.token });
  ok("新建配置默认 syncFiles=true", cfgC.data.syncFiles === true, JSON.stringify(cfgC.data));

  console.log(`\n跨设备文件实体同步: ${pass} 通过 / ${fail} 失败`);
}

main()
  .catch((e) => { console.error("❌ 异常:", e && e.message); fail++; })
  .finally(() => { for (const p of procs) { try { p.kill(); } catch {} } process.exit(fail ? 1 : 0); });
