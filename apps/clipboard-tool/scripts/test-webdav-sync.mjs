// scripts/test-webdav-sync.mjs - WebDAV 端到端集成测试（需先起隔离实例 + mock-webdav 8180）
// 参数：TEST_PORT（被测实例端口，默认 8131）、TEST_DATA_DIR（被测实例数据目录，默认 C:/Temp/clipboard-test）
const PORT = process.env.TEST_PORT || "8131";
const DATA_DIR = process.env.TEST_DATA_DIR || "C:/Temp/clipboard-test";
const BASE = "http://127.0.0.1:" + PORT;
const DAV = "http://127.0.0.1:8180/dav/";
async function req(method, path, { token, json } = {}) {
  const headers = {};
  if (token) headers["Authorization"] = "Bearer " + token;
  let body;
  if (json !== undefined) { headers["Content-Type"] = "application/json"; body = JSON.stringify(json); }
  const r = await fetch(BASE + path, { method, headers, body });
  let d = null;
  try { d = await r.json(); } catch {}
  return { status: r.status, data: d };
}
let pass = 0, fail = 0;
const ok = (n, c) => { c ? pass++ : fail++; console.log((c ? "✅" : "❌") + " " + n); };
const AUTH = { Authorization: "Basic " + Buffer.from("admin:admin123").toString("base64") };
async function davGet(p) {
  const r = await fetch(DAV + p, { headers: AUTH });
  return r.json();
}

const u = await req("POST", "/api/users", { json: { name: "WebDAVTest" } });
const tk = u.data.token, uid = u.data.user.id;

// 1. 建两条 + 配置 WebDAV（测试保存）
const c1 = await req("POST", "/api/clips", { token: tk, json: { type: "text", content: "备份内容A", title: "A" } });
const c2 = await req("POST", "/api/clips", { token: tk, json: { type: "text", content: "备份内容B", title: "B" } });
const cfg = await req("POST", "/api/sync/config", { token: tk, json: { url: DAV, user: "admin", pass: "admin123" } });
ok("测试保存配置", cfg.status === 200 && cfg.data.ok);
const cfgBad = await req("POST", "/api/sync/config", { token: tk, json: { url: DAV, user: "admin", pass: "wrong" } });
ok("错误密码测试失败(401)", cfgBad.status === 401);
const cfg2 = await req("POST", "/api/sync/config", { token: tk, json: { url: DAV, user: "admin", pass: "admin123" } });
ok("重新保存正确配置", cfg2.status === 200);
const cfgGet = await req("GET", "/api/sync/config", { token: tk });
ok("配置读取(不含密码)", cfgGet.data.configured && cfgGet.data.url === DAV && cfgGet.data.hasPass && !("pass" in cfgGet.data));

// 2. 一键同步 → 上传
const s1 = await req("POST", "/api/sync/run", { token: tk });
ok("首次同步上传", s1.data.uploaded === true && s1.data.clips === 2 && s1.data.remoteExisted === false);

// 3. 单独删除 A → 墓碑 → 再同步 → 远端传播删除
await req("DELETE", "/api/clips/" + c1.data.clip.id, { token: tk });
const s2 = await req("POST", "/api/sync/run", { token: tk });
ok("删除后同步(墓碑传播)", s2.data.tombstones === 1 && s2.data.clips === 1);
const snap = await davGet("workbuddy/剪贴板/clipboard-WebDAVTest.json");
ok("远端快照无 A", !snap.clips.some(c => c.id === c1.data.clip.id));
ok("远端快照墓碑含 A", snap.tombstones.some(t => t.id === c1.data.clip.id));

// 4. 本地列表只剩 B
const list = await req("GET", "/api/clips", { token: tk });
ok("本地只剩 B", list.data.clips.length === 1 && list.data.clips[0].id === c2.data.clip.id);

// 5. 全部清空（不记墓碑）→ 清空后同步从远端拉回 B
const clr = await req("DELETE", "/api/clips", { token: tk });
ok("全部清空", clr.data.cleared === 1);
const s3 = await req("POST", "/api/sync/run", { token: tk });
ok("清空后同步:跳过上传+拉回远端", s3.data.uploaded === false && s3.data.remoteExisted === true && s3.data.clips === 1);
const list2 = await req("GET", "/api/clips", { token: tk });
ok("清空后从远端恢复 B", list2.data.clips.length === 1 && list2.data.clips[0].id === c2.data.clip.id);

// 6. 远端快照未被覆盖成空（uploaded=false 保护）
const snap2 = await davGet("workbuddy/剪贴板/clipboard-WebDAVTest.json");
ok("远端快照未被清空覆盖", snap2.clips.length >= 1);

// 7. 墓碑文件落盘确认
const fs = await import("node:fs");
const tombs = JSON.parse(fs.readFileSync(DATA_DIR + "/users/" + uid + ".tombstones.json", "utf8"));
ok("墓碑文件落盘(含A)", tombs.some(t => t.id === c1.data.clip.id));

// 8. 实体同步：上传文件条目 → 勾选 syncFiles → 同步 → 远端 files/ 有实体
const fd = new FormData();
fd.append("file", new Blob([new Uint8Array([137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3])], { type: "image/png" }), "pic.png");
const up = await req("POST", "/api/files", { token: tk, json: null, form: fd });
// 需要支持 form 上传：改造 req 支持 form
async function reqForm(path, form) {
  const r = await fetch(BASE + path, { method: "POST", headers: { Authorization: "Bearer " + tk }, body: form });
  return r.json();
}
const up2 = await reqForm("/api/files", fd);
const fileClip = await req("POST", "/api/clips", { token: tk, json: { type: "file", fileId: up2.file.fileId, fileName: up2.file.fileName, fileSize: up2.file.fileSize, fileMime: up2.file.fileMime, title: "pic.png" } });
await req("POST", "/api/sync/config", { token: tk, json: { url: DAV, user: "admin", pass: "admin123", syncFiles: true, autoSync: false } });
const s4 = await req("POST", "/api/sync/run", { token: tk });
const fRemote = await fetch(DAV + "workbuddy/剪贴板/files/WebDAVTest/" + up2.file.fileId + ".png", { headers: AUTH });
ok("实体同步:远端有文件", fRemote.status === 200 && new Uint8Array(await fRemote.arrayBuffer()).length > 0);

// 9. 删除本地文件实体 → 再同步 → 从远端拉回
const localDir = DATA_DIR + "/files/" + uid;
const localFile = fs.readdirSync(localDir).find(f => f.startsWith(up2.file.fileId + "."));
fs.unlinkSync(localDir + "/" + localFile);
ok("本地文件已删(模拟丢失)", !fs.existsSync(localDir + "/" + localFile));
await req("POST", "/api/sync/run", { token: tk });
const restored = fs.readdirSync(localDir).find(f => f.startsWith(up2.file.fileId + "."));
ok("实体同步:远端拉回本地", !!restored);

// 10. 不勾选实体（默认）→ 同步 → 新文件条目不再传实体
const fd2 = new FormData();
fd2.append("file", new Blob([new Uint8Array([1, 2, 3, 4])], { type: "text/plain" }), "note.txt");
const up3 = await reqForm("/api/files", fd2);
await req("POST", "/api/clips", { token: tk, json: { type: "file", fileId: up3.file.fileId, fileName: up3.file.fileName, fileSize: up3.file.fileSize, fileMime: up3.file.fileMime, title: "note.txt" } });
await req("POST", "/api/sync/config", { token: tk, json: { url: DAV, user: "admin", pass: "admin123", syncFiles: false } });
await req("POST", "/api/sync/run", { token: tk });
const fNoRemote = await fetch(DAV + "workbuddy/剪贴板/files/WebDAVTest/" + up3.file.fileId + ".txt", { headers: AUTH });
ok("不勾选实体:远端无文件实体", fNoRemote.status === 404);
// 但快照里仍有 file 条目元数据
const snap3 = await davGet("workbuddy/剪贴板/clipboard-WebDAVTest.json");
ok("不勾选实体:快照仍含文件条目元数据", snap3.clips.some(c => c.type === "file" && c.fileId === up3.file.fileId));

console.log("WebDAV 集成测试: " + pass + " 通过 / " + fail + " 失败");
process.exit(fail ? 1 : 0);
