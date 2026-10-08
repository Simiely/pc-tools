// scripts/test-auto-sync.mjs - 定时自动同步测试（模块直测 runAutoSync 到期判定）
// 前置：隔离实例 + mock-webdav 8180 运行中；test-webdav-sync.mjs 已跑（有 WebDAV 配置用户）
// 参数：TEST_PORT（被测实例端口，默认 8131）、TEST_DATA_DIR（被测实例数据目录，默认 C:/Temp/clipboard-test）
import fs from "node:fs";

const PORT = process.env.TEST_PORT || "8131";
const DATA_DIR = process.env.TEST_DATA_DIR || "C:/Temp/clipboard-test";
const BASE = "http://127.0.0.1:" + PORT;
const DAV = "http://127.0.0.1:8180/dav/";
const AUTH = { Authorization: "Basic " + Buffer.from("admin:admin123").toString("base64") };
let pass = 0, fail = 0;
const ok = (n, c) => { c ? pass++ : fail++; console.log((c ? "✅" : "❌") + " " + n); };

// 动态 import 模块（先设 CAP_STORAGE_DIR，ESM 静态 import 会提前，必须动态）
process.env.CAP_STORAGE_DIR = DATA_DIR;
const webdav = await import("../lib/core/webdav.js");

// 找 WebDAV 测试用户（test-webdav-sync 创建）
const users = await (await fetch(BASE + "/api/users")).json();
// v0.7.6 修复：此前只认「WebDAV测试」，而 test-webdav-sync.mjs 建的是「WebDAVTest」——
// 名字不匹配导致本脚本恒报「未找到测试用户」，`npm test` 这条一直是死的。现兼容两种 + 支持 TEST_USER 覆盖。
const NAMES = [process.env.TEST_USER, "WebDAVTest", "WebDAV测试"].filter(Boolean);
const u = users.users.find(x => NAMES.includes(x.name));
if (!u) { console.log("❌ 未找到测试用户（先跑 test-webdav-sync.mjs）；现有用户：" + users.users.map(x => x.name).join(",")); process.exit(1); }
const uid = u.id;
// v0.7.6 修复：v0.6.13 起远端快照按【账号名】寻址（clipboard-<accountName>.json），
// 本脚本仍拼 userId（旧格式）→ 恒 404 → .json() 崩溃。这里统一取账号名。
const snapName = u.accountName || u.name;

// 1. 配置 autoSync=true、intervalMin=1 分钟、lastSyncAt=0（立即到期）
webdav.saveSyncConfig(uid, { url: DAV, user: "admin", pass: "admin123", syncFiles: false, autoSync: true, intervalMin: 1 });
const cfgFile = DATA_DIR + "/users/" + uid + ".webdav.json";
const cfg1 = JSON.parse(fs.readFileSync(cfgFile, "utf8"));
cfg1.lastSyncAt = 0; // 强制到期
fs.writeFileSync(cfgFile, JSON.stringify(cfg1, null, 2));
const t0 = Date.now();

// 2. runAutoSync → 应触发（上传新快照，syncedAt 更新）
await webdav.runAutoSync();
const after1 = JSON.parse(fs.readFileSync(cfgFile, "utf8"));
ok("到期触发自动同步(lastSyncAt 更新)", after1.lastSyncAt >= t0);

// 3. 立即再跑 → 未到期 → 不触发（快照 syncedAt 不变）
const snapBefore = await (await fetch(DAV + "workbuddy/剪贴板/clipboard-" + snapName + ".json", { headers: AUTH })).json();
await new Promise(r => setTimeout(r, 200));
await webdav.runAutoSync();
const snapAfter = await (await fetch(DAV + "workbuddy/剪贴板/clipboard-" + snapName + ".json", { headers: AUTH })).json();
ok("未到期跳过(快照 syncedAt 不变)", snapBefore.syncedAt === snapAfter.syncedAt);

// 4. 关闭自动同步后不触发（把 lastSyncAt 归零也不会跑）
cfg1.autoSync = false;
cfg1.lastSyncAt = 0;
fs.writeFileSync(cfgFile, JSON.stringify(cfg1, null, 2));
await webdav.runAutoSync();
const cfg2 = JSON.parse(fs.readFileSync(cfgFile, "utf8"));
ok("autoSync=false 不触发", cfg2.lastSyncAt === 0);

// 5. 恢复配置避免影响后续
webdav.saveSyncConfig(uid, { url: DAV, user: "admin", pass: "admin123", syncFiles: false, autoSync: false, intervalMin: 720 });

console.log("自动同步测试: " + pass + " 通过 / " + fail + " 失败");
process.exit(fail ? 1 : 0);
