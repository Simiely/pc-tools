// lib/core/webdav.js - WebDAV 备份与同步
// 设计参考 edge-multi-account-cookie 的 WebDAV 方案，语义完全对齐：
//   1) 单独删除 → 记墓碑 → 同步传播删除（防旧备份把已删条目复活）
//   2) 全部清空 → 不记墓碑（清空即"想从网上同步"，下次同步从远端拉回恢复）
//   3) 双向取最新：同 id 条目按 updatedAt 取新者；墓碑 deletedAt > updatedAt → 删除，
//      另一侧删后又编辑（updatedAt > deletedAt）→ 保留
//   4) 本地无数据时跳过上传（防把清空后的空备份覆盖远端）
//   5) 远端固定保留最新 1 份快照：<目录>/workbuddy/剪贴板/clipboard-<uid>.json（v0.6.7 起统一子目录；
//      v0.6.13 起快照 = 活跃区 ∪ 归档区（归档条目带 archived 标记），WebDAV 完整备份）
//   6) 实体同步开关（syncFiles）：勾选 → 文件实体也备份/恢复（<目录>/workbuddy/剪贴板/files/<uid>/<fileId>.<ext>）；
//      不勾选 → 快照只含条目元数据（含 fileId 引用，恢复后文件实体缺失时下载 404）
//   7) 定时自动同步（autoSync + intervalMin）：服务进程存活期间按间隔自动双向同步，默认 12 小时
//   8) 按账号名寻址（v0.6.13 双名模型）：快照 = clipboard-<accountName>.json、实体 = files/<accountName>/——
//      accountName 创建后不可变（唯一身份键），显示名 displayName 随便改不影响寻址 → 无改名迁移复杂度。
//      设备迁移零配置：新部署创建相同账号名 → 配置 WebDAV → 同步即拉回全部数据（账号名即身份）。
//      同部署内账号名唯一（409 校验），不同部署同名=同一数据（单机自用场景即"迁移"语义）
//   9) 旧格式兼容（v0.6.13 之前按 userId 存快照）：同步时检测 clipboard-<userId>.json 存在 →
//      并入合并后上传当前名并删除旧路径（一次性迁移，自愈幂等）
import fs from "node:fs";
import path from "node:path";
import { CONFIG, EXT_BY_MIME } from "./config.js";
import { readJson, writeJson, httpError } from "./store.js";
import { loadClips, saveClips, loadArchive, saveArchive, loadTombstones, saveTombstones, pruneTombstones, recordTombstone } from "./clips.js";
import { getFilePath } from "./files.js";
import { getUserName, getUserDisplayNameEx, adoptRemoteNickname } from "./users.js";

const REQ_TIMEOUT_MS = 10_000; // WebDAV 请求超时（与前端 fetch 超时一致，防挂起）
const DEFAULT_INTERVAL_MIN = 720; // 自动同步默认间隔：12 小时
const AUTO_MIN = 30, AUTO_MAX = 24 * 60; // 间隔范围 30 分钟 ~ 24 小时

// v0.6.7：远端统一存放于「配置目录/workbuddy/剪贴板/」子目录下（与 WebDAV 根的其他用途隔离）
const REMOTE_SUBDIR = "workbuddy/剪贴板";

// ---------- 配置存储（每个用户独立） ----------

function cfgFile(userId) {
  return path.join(CONFIG.usersDir, userId + ".webdav.json");
}

/** 读同步配置；未配置返回 null */
export function getSyncConfig(userId) {
  return readJson(cfgFile(userId), null);
}

function clampInt(v, min, max, def) {
  const n = parseInt(v, 10);
  if (isNaN(n)) return def;
  return Math.min(max, Math.max(min, n));
}

/** 保存配置（调用方先测试连通）；pass 留空 = 复用旧密码（前端密码框留空时）。
 *  syncFiles=是否同步文件实体；autoSync=是否定时自动同步；intervalMin=间隔分钟（默认 720=12h）。 */
export function saveSyncConfig(userId, { url, user, pass, syncFiles, autoSync, intervalMin }) {
  const old = getSyncConfig(userId) || {};
  // v0.7.6：syncFiles 默认 true（此前默认 false —— 绝大多数用户根本不知道有这个开关，
  // 结果「条目同步过去了、图片/附件在另一台设备下载 404」。未显式传参时：首次配置=开，后续沿用旧值）
  const syncFilesVal = syncFiles === undefined || syncFiles === null
    ? (old.syncFiles === undefined ? true : !!old.syncFiles)
    : !!syncFiles;
  const cfg = {
    url: String(url || "").trim(),
    user: String(user || "").trim(),
    pass: pass ? String(pass) : (old.pass || ""),
    syncFiles: syncFilesVal,
    autoSync: !!autoSync,
    intervalMin: clampInt(intervalMin, AUTO_MIN, AUTO_MAX, old.intervalMin || DEFAULT_INTERVAL_MIN),
    lastSyncAt: old.lastSyncAt || 0,
  };
  if (!cfg.url) throw httpError(400, "WebDAV 服务器地址不能为空");
  if (!/^https?:\/\//i.test(cfg.url)) throw httpError(400, "地址需以 http(s):// 开头");
  if (!cfg.pass) throw httpError(400, "密码不能为空（首次配置）");
  writeJson(cfgFile(userId), cfg);
  return { ok: true };
}

/** 局部更新配置（如同步后刷新 lastSyncAt，不触发校验） */
function updateConfig(userId, patch) {
  const cfg = getSyncConfig(userId);
  if (!cfg) return;
  writeJson(cfgFile(userId), { ...cfg, ...patch });
}

/** v0.6.14：删除条目后的墓碑记录（业务规则下沉——自路由层迁入同步域）。
 *  语义不变：仅「已配置 WebDAV 的用户」记墓碑（未配置同步则无传播需求，不写文件）；
 *  条件判断收敛在 core，路由层只做无条件编排调用。 */
export function recordTombstoneIfConfigured(userId, clipId) {
  if (!clipId) return;
  if (!getSyncConfig(userId)) return;
  recordTombstone(userId, clipId);
}

/** v0.6.13 双名模型·账号名修改后调用：记入待迁移账号名列表，下次同步逐个把旧账号名快照并入并迁移。
 *  数组而非单值（P-1 修复）——连续两次改账号名（间隙未同步）时旧名迁移标记不被覆盖丢失；
 *  旧配置 prevAccountName 兼容转数组。 */
export function noteAccountRename(userId, oldName) {
  const cfg = getSyncConfig(userId);
  if (!cfg || !oldName) return;
  const list = cfg.pendingNameMigrations || (cfg.prevAccountName ? [cfg.prevAccountName] : []);
  if (!list.includes(oldName)) list.push(oldName);
  writeJson(cfgFile(userId), { ...cfg, pendingNameMigrations: list, prevAccountName: undefined });
}

// ---------- WebDAV 客户端（Node fetch 直连，零依赖） ----------

function authHeader(cfg) {
  return "Basic " + Buffer.from(cfg.user + ":" + cfg.pass).toString("base64");
}

/** 目录 URL（去尾斜杠 + 加斜杠） */
function dirUrl(url) {
  return url.replace(/\/+$/, "") + "/";
}

/** 数据根目录 URL：配置目录 + workbuddy/剪贴板/（v0.6.7） */
function dataDirUrl(cfg) {
  return dirUrl(cfg.url) + REMOTE_SUBDIR.split("/").filter(Boolean).map(s => s + "/").join("");
}

/** 用户名 → 远端路径安全名（v0.6.13 按用户名寻址：剔除 / ? # 等路径破坏字符，保留中文可读——与 REMOTE_SUBDIR 中文行为一致） */
function safeName(name) {
  return String(name || "u").replace(/[\/\\?%#&:="<>|*]/g, "_").slice(0, 80) || "u";
}

/** 快照文件 URL（v0.6.13 起按用户名寻址：clipboard-<用户名>.json——设备迁移时新部署建同名用户即可拉回数据；
 *  传入 userId(UUID) 时保持旧格式 clipboard-<uuid>.json，供旧数据兼容迁移检测） */
function snapUrl(cfg, nameOrId) {
  return dataDirUrl(cfg) + "clipboard-" + safeName(nameOrId) + ".json";
}

/** fetch 封装：Basic 认证 + 超时，返回 { status, buf, text }（buf 供二进制文件实体，text 供 JSON） */
async function davFetch(url, opts = {}) {
  const ctrl = new AbortController();
  const timer = setTimeout(() => ctrl.abort(), REQ_TIMEOUT_MS);
  try {
    const r = await fetch(url, { ...opts, signal: ctrl.signal });
    const buf = Buffer.from(await r.arrayBuffer().catch(() => new ArrayBuffer(0)));
    return { status: r.status, buf, text: buf.toString("utf8") };
  } catch (e) {
    const err = httpError(502, "WebDAV 连接失败: " + (e.name === "AbortError" ? "请求超时" : e.message));
    throw err;
  } finally {
    clearTimeout(timer);
  }
}

/** 确保目录存在（MKCOL；已存在/不支持均容忍）。v0.6.7：逐级创建「根 → workbuddy/ → workbuddy/剪贴板/」 */
async function ensureDir(cfg) {
  const segs = [dirUrl(cfg.url), ...REMOTE_SUBDIR.split("/").filter(Boolean).map(s => s + "/")];
  let cur = dirUrl(cfg.url);
  for (let i = 1; i < segs.length; i++) {
    cur = cur + segs[i];
    const r = await davFetch(cur, { method: "MKCOL", headers: { Authorization: authHeader(cfg) } });
    if (r.status === 401 || r.status === 403) throw httpError(401, "WebDAV 认证失败（检查用户名/密码）");
    if (![201, 204, 200, 301, 405].includes(r.status)) {
      throw httpError(502, "WebDAV 目录不可用（HTTP " + r.status + "）");
    }
  }
}

/** 连通测试：确保目录（含子目录）+ 写探针文件 + 读回校验。失败抛错（配置不保存） */
export async function testConnection(cfg) {
  await ensureDir(cfg);
  const probe = dataDirUrl(cfg) + ".clipboard-probe";
  const put = await davFetch(probe, {
    method: "PUT",
    headers: { Authorization: authHeader(cfg), "Content-Type": "text/plain" },
    body: "ok",
  });
  if (![201, 204, 200].includes(put.status)) {
    throw httpError(502, "WebDAV 不可写（PUT 返回 " + put.status + "）");
  }
  const get = await davFetch(probe, { headers: { Authorization: authHeader(cfg) } });
  if (get.status !== 200 || get.text !== "ok") {
    throw httpError(502, "WebDAV 读回校验失败");
  }
  return { ok: true };
}

/** 拉远端快照（按 URL，v0.6.13：支持读取多个迁移源）：404 → null；其他错误抛错 */
async function fetchRemoteSnapshot(cfg, url) {
  const r = await davFetch(url, { headers: { Authorization: authHeader(cfg) } });
  if (r.status === 404) return null;
  if (r.status !== 200) throw httpError(502, "拉取远端备份失败（HTTP " + r.status + "）");
  try {
    const snap = JSON.parse(r.text);
    if (!snap || !Array.isArray(snap.clips)) throw new Error("格式错误");
    return snap;
  } catch {
    throw httpError(502, "远端备份文件损坏或格式不兼容");
  }
}

/** 上传快照（按 URL） */
async function uploadSnapshot(cfg, url, snap) {
  const r = await davFetch(url, {
    method: "PUT",
    headers: { Authorization: authHeader(cfg), "Content-Type": "application/json" },
    body: JSON.stringify(snap),
  });
  if (![201, 204, 200].includes(r.status)) {
    throw httpError(502, "上传远端备份失败（HTTP " + r.status + "）");
  }
}

// ---------- 合并算法（双向取最新 + 墓碑裁决） ----------

/**
 * 合并本地与远端快照：
 *  - 同 id 条目取 updatedAt 新者
 *  - 墓碑合并取 deletedAt 新者
 *  - 裁决：墓碑 deletedAt > 条目 updatedAt → 删除；条目 updatedAt > 墓碑 deletedAt → 保留（删后又被编辑）
 *  - 清空语义：localTomb=[] 表示"全部清空不传播删除"——此时远端条目直接拉回
 * 纯函数：无 IO/无副作用，入参出参即全部行为——单元测试见 scripts/test-merge-snapshot.mjs（node 直跑，无需服务）
 * @param {Array} localClips 本地条目
 * @param {Array} localTomb 本地墓碑 [{id,deletedAt}]
 * @param {Object|null} remoteSnap 远端快照 {clips, tombstones} 或 null
 * @returns {{clips:Array, tombstones:Array}}
 */
export function mergeSnapshots(localClips, localTomb, remoteSnap) {
  const byId = new Map();
  for (const c of localClips) byId.set(c.id, c);
  if (remoteSnap && Array.isArray(remoteSnap.clips)) {
    for (const c of remoteSnap.clips) {
      const ex = byId.get(c.id);
      if (!ex || (c.updatedAt || 0) > (ex.updatedAt || 0)) byId.set(c.id, c);
    }
  }
  const tombs = new Map();
  for (const t of localTomb || []) tombs.set(t.id, t.deletedAt);
  if (remoteSnap && Array.isArray(remoteSnap.tombstones)) {
    for (const t of remoteSnap.tombstones) {
      const ex = tombs.get(t.id);
      if (!ex || t.deletedAt > ex) tombs.set(t.id, t.deletedAt);
    }
  }
  const clips = [];
  for (const [id, c] of byId) {
    const delAt = tombs.get(id);
    if (delAt && delAt > (c.updatedAt || 0)) continue; // 墓碑裁决：删除
    clips.push(c);
  }
  const tombstones = [...tombs.entries()].map(([id, deletedAt]) => ({ id, deletedAt }));
  return { clips, tombstones };
}

// ---------- 实体同步（syncFiles 开关：勾选 → 文件实体也备份/恢复） ----------

/** 文件条目的远端/本地扩展名（mime 优先，其次原始文件名，兜底 .bin——与 saveFile 的 bin 兜底一致，
 *  v0.6.11 修复：此前兜底空串，无扩展名+未知 mime 的文件远端路径 <fileId>、本地存 <fileId>.bin，
 *  恢复写盘无扩展名 → getFilePath 前缀匹配失败 → 下载 404） */
function extFor(c) {
  const mime = EXT_BY_MIME[c.fileMime];
  if (mime) return "." + mime;
  const m = /\.([a-z0-9]{1,8})$/i.exec(c.fileName || "");
  return m ? "." + m[1].toLowerCase() : ".bin";
}

/** 确保单级目录存在（MKCOL；已存在/不支持均容忍）。v0.6.13：files/ 与 files/<uid>/ 实体目录 */
async function ensureOneDir(cfg, url) {
  const r = await davFetch(url, { method: "MKCOL", headers: { Authorization: authHeader(cfg) } });
  if (r.status === 401 || r.status === 403) throw httpError(401, "WebDAV 认证失败（检查用户名/密码）");
  if (![201, 204, 200, 301, 405].includes(r.status)) {
    throw httpError(502, "WebDAV 目录不可用（HTTP " + r.status + "）");
  }
}

/**
 * 实体同步：对合并后的每个文件条目——
 *  - 本地有实体 → PUT 上传到 <目录>/files/<safeName>/<fileId><ext>（v0.6.13 按账号名寻址，
 *    与快照一致——设备迁移建同名用户即可从远端 files/<name>/ 拉回实体）
 *  - 本地缺失（恢复场景）→ GET 远端写回本地 files 目录（远端也没有则跳过，条目保留）
 *  - P-2 修复（v0.6.13）：改账号名后实体目录不随快照迁移——本地缺失且新名 404 时，
 *    从历史账号名目录（pendingNames）尝试拉取，写本地 + 上传新名 + 删除旧文件（自愈迁移，不依赖目录列表）
 *
 * v0.7.6 语义修正（关键）：`syncFiles` 只控制「是否备份实体上云」，**不再控制拉回**。
 * 旧行为：开关同时管上传与拉回 → 未勾选时其它设备同步后条目可见、实体永远缺失，
 * 下载 404 / 图片裂图且无任何提示（用户侧表现为「同步了但另一台设备用不了」）。
 * 新行为：拉回无条件执行——只要本地缺实体就尝试从远端取；上传仍受开关约束
 * （不想把大文件备份上云的用户可关，但不影响新设备/他端恢复）。
 */
async function syncFileEntities(cfg, name, userId, clips, pendingNames = []) {
  const fBase = dataDirUrl(cfg) + "files/" + safeName(name) + "/";
  // v0.6.13：先确保 files/ 与 files/<name>/ 目录存在——严格 WebDAV 服务器对「PUT 到不存在的目录」返回 409
  // v0.7.6：仅「需要上传」时才建目录——只拉回的场景不该在远端凭空建空目录
  if (cfg.syncFiles) {
    await ensureOneDir(cfg, dataDirUrl(cfg) + "files/");
    await ensureOneDir(cfg, fBase);
  }
  for (const c of clips) {
    if (c.type !== "file" || !c.fileId) continue;
    const ext = extFor(c);
    const remote = fBase + c.fileId + ext;
    let local = null;
    try { local = getFilePath(userId, c.fileId); } catch { local = null; }
    if (local) {
      if (!cfg.syncFiles) continue; // 未勾选：不备份实体上云（v0.7.6）
      const buf = fs.readFileSync(local);
      const r = await davFetch(remote, {
        method: "PUT",
        headers: { Authorization: authHeader(cfg), "Content-Type": c.fileMime || "application/octet-stream" },
        body: buf,
      });
      if (![201, 204, 200].includes(r.status)) throw httpError(502, "文件实体上传失败：" + c.fileName + "（HTTP " + r.status + "）");
    } else {
      let got = null;
      const r = await davFetch(remote, { headers: { Authorization: authHeader(cfg) } });
      if (r.status === 200) got = r;
      // P-2：新名 404 → 从历史账号名目录尝试（改账号名后他端旧实体迁移自愈）
      if (!got && pendingNames.length) {
        for (const pn of pendingNames) {
          if (pn === name) continue;
          const oldRemote = dataDirUrl(cfg) + "files/" + safeName(pn) + "/" + c.fileId + ext;
          const o = await davFetch(oldRemote, { headers: { Authorization: authHeader(cfg) } });
          if (o.status === 200) {
            got = o;
            // 迁移:上传到新名 + 删除旧文件
            const pu = await davFetch(remote, {
              method: "PUT",
              headers: { Authorization: authHeader(cfg), "Content-Type": c.fileMime || "application/octet-stream" },
              body: o.buf,
            });
            if ([201, 204, 200].includes(pu.status)) {
              await davFetch(oldRemote, { method: "DELETE", headers: { Authorization: authHeader(cfg) } }).catch(() => {});
            }
            break;
          }
        }
      }
      if (got) {
        const dir = path.join(CONFIG.filesDir, userId);
        fs.mkdirSync(dir, { recursive: true });
        fs.writeFileSync(path.join(dir, c.fileId + ext), got.buf);
      } // 404 → 远端也无实体，跳过（条目保留，下载时提示）
    }
  }
}

// ---------- 一键同步（先拉远端合并进本地，再上传合并后全量） ----------

const syncInFlight = new Set(); // v0.6.13：per-user 同步进行中——手动「一键同步」与定时 autoSync 可能同时触发，
// 两个 runSync 并发会重复拉取/上传且 lastSyncAt 抖动（merge 幂等无坏数据风险，但浪费且日志混乱）。重入抛 409 走前端统一错误分支。

/** 收集远端数据源：当前账号名快照 + 旧格式 clipboard-<userId>.json（v0.6.13 前数据）+ 账号名修改前的旧名快照。
 *  账号名迁移源可能多个（连续改名的旧名逐个迁移；P-1：删除成功才移除，失败保留下次重试）。 */
async function collectRemoteSources(cfg, name, userId) {
  const curUrl = snapUrl(cfg, name);
  const legacyUrl = snapUrl(cfg, userId); // 旧格式（v0.6.13 前按 userId 存）
  const srcs = [[curUrl, await fetchRemoteSnapshot(cfg, curUrl)]];
  const legacySnap = await fetchRemoteSnapshot(cfg, legacyUrl);
  if (legacySnap) srcs.push([legacyUrl, legacySnap]);
  const pendingNames = (cfg.pendingNameMigrations || []).filter((n) => n && n !== name);
  for (const pn of pendingNames) {
    const ps = await fetchRemoteSnapshot(cfg, snapUrl(cfg, pn));
    if (ps) srcs.push([snapUrl(cfg, pn), ps]);
  }
  return { srcs, pendingNames, curUrl };
}

/** 依次并入各远端源（mergeSnapshots 幂等，同 id 取 updatedAt 新者） */
function mergeRemoteAll(localClips, localTomb, srcs) {
  let merged = mergeSnapshots(localClips, localTomb, null);
  for (const [, snap] of srcs) if (snap) merged = mergeSnapshots(merged.clips, merged.tombstones, snap);
  return merged;
}

/** 写回本地（拉回远端/合并结果）——按 archived 标记分拣：归档先替换（防止 saveClips 滚动追加覆盖），活跃再写（内部自动滚动） */
function writeBackLocal(userId, merged) {
  const active = merged.clips.filter((c) => !c.archived);
  const archived = merged.clips.filter((c) => c.archived);
  saveArchive(userId, archived);
  saveClips(userId, active);
  saveTombstones(userId, merged.tombstones);
}

/** 迁移清理：合并上传成功后删除旧路径快照（数据已并入当前名）。旧格式 legacy 删失败下次 404 跳过；
 *  账号名迁移源删除成功才从 pending 移除（P-1：删除失败保留，下次同步重试迁移） */
async function cleanupMigrations(cfg, curUrl, srcs, pendingNames) {
  for (const [url] of srcs) {
    if (url === curUrl) continue;
    await davFetch(url, { method: "DELETE", headers: { Authorization: authHeader(cfg) } }).catch(() => {});
  }
  const stillPending = [];
  for (const pn of pendingNames) {
    const dr = await davFetch(snapUrl(cfg, pn), { method: "DELETE", headers: { Authorization: authHeader(cfg) } }).catch(() => ({ status: 0 }));
    if (![200, 204, 404].includes(dr.status)) stillPending.push(pn); // 删除失败：保留待下次重试
  }
  return stillPending;
}

export async function runSync(userId) {
  if (syncInFlight.has(userId)) throw httpError(409, "同步进行中，请稍候");
  syncInFlight.add(userId);
  try {
    const cfg = getSyncConfig(userId);
    if (!cfg) throw httpError(400, "未配置 WebDAV，请先测试保存");
    const name = getUserName(userId); // v0.6.13 双名模型：账号名（不可变身份键）——显示名随便改，快照路径不变
    const dispEx = getUserDisplayNameEx(userId); // v0.7.x：是否显式设过昵称 + 当前昵称
    await ensureDir(cfg);

    // ① 拉源 → ② 合并 → ③ 写回（v0.6.14 拆阶段，各步骤独立小函数便于定位/单测）
    const { srcs, pendingNames, curUrl } = await collectRemoteSources(cfg, name, userId);
    // v0.6.13：快照 = 活跃区 ∪ 归档区（归档条目带 archived 标记）——WebDAV 完整备份，归档不丢
    const localClips = [...loadClips(userId), ...loadArchive(userId).map((c) => ({ ...c, archived: true }))];
    const localTomb = pruneTombstones(userId); // 先清过期墓碑（防无限增长）
    const merged = mergeRemoteAll(localClips, localTomb, srcs);
    writeBackLocal(userId, merged);

    // ②b 昵称采纳（对齐 exe 本地权威语义）：仅当本用户未自定义昵称(displayName==账号名)时，采纳远端快照 nickname 写入其 displayName
    //（srcs 当前名快照优先；撞其它用户同名则放弃，不中断同步）——采纳成功后刷新 dispEx，本轮上传即可带上新昵称
    if (!dispEx.hasExplicit) {
      for (const [, snap] of srcs) {
        if (snap && String(snap.nickname || "").trim() && adoptRemoteNickname(userId, snap.nickname)) {
          dispEx.hasExplicit = true; dispEx.displayName = snap.nickname;
          break;
        }
      }
    }

    // ④ 实体同步（v0.7.6 无条件执行）：本地有 → 当 syncFiles 勾选时上传到 files/<accountName>/；
    // 本地缺（恢复）→ 无条件从 files/<accountName>/ 拉回（保证他端同步后一定可用）；
    // P-2：本地缺且新名 404 → 从历史账号名目录迁移（改账号名后他端旧实体自愈）
    await syncFileEntities(cfg, name, userId, merged.clips, pendingNames);

    // ⑤ 上传保护：合并前本地无数据（全新设备 / 清空后想从网上同步）→ 跳过上传，防空备份覆盖远端。
    // 此时远端数据已拉回本地，下次同步本地非空即可正常双向收敛。
    const hadLocal = localClips.length > 0 || localTomb.length > 0;
    if (hadLocal) {
      await uploadSnapshot(cfg, curUrl, {
        app: "clipboard", version: 1,
        syncedAt: Date.now(),
        // v0.7.x：昵称随快照上传(仅显式设过昵称才带字段)——同账号其它端(exe/Web)同步时可见、可采纳，对齐 exe 上传 Nickname
        ...(dispEx.hasExplicit ? { nickname: dispEx.displayName } : {}),
        clips: merged.clips,
        tombstones: merged.tombstones,
      });
    }

    // ⑥ 迁移清理 + 收尾（成功即清失败标记 P-104）
    const stillPending = await cleanupMigrations(cfg, curUrl, srcs, pendingNames);
    updateConfig(userId, {
      lastSyncAt: Date.now(), lastSyncError: "",
      ...(pendingNames.length ? { pendingNameMigrations: stillPending } : {}),
    });

    const remoteExisted = srcs.some(([, s]) => !!s);
    const migrated = srcs.some(([u]) => u !== curUrl);
    return {
      ok: true,
      remoteExisted,
      migrated, // v0.6.13：本次是否发生旧数据迁移（仅旧格式 userId 路径）
      uploaded: hadLocal,
      clips: merged.clips.length,
      tombstones: merged.tombstones.length,
    };
  } finally {
    syncInFlight.delete(userId);
  }
}

// ---------- v0.7.6 一次性迁移：存量 syncFiles=false → true ----------

/**
 * 把存量配置里 `syncFiles:false` 翻成 true（仅一次，用 `v0706SyncFiles` 标记防重复）。
 *
 * 背景：v0.7.6 之前 `saveSyncConfig` 恒写 `syncFiles: !!syncFiles`，未勾选就是 false，
 * 而前端复选框默认不勾 → **几乎每个用户的配置里都是 false，且并非其主动选择**。
 * 后果是文件/图片实体从未备份上云，换设备同步后条目可见但实体缺失、下载 404。
 * v0.7.6 已把新配置默认改为 true，但存量数据不会自动变——本函数补上这一刀。
 *
 * 只翻「从没迁移过」的配置：迁移后用户再主动关闭（写 false）会被尊重，不会被再次翻开。
 * @returns {number} 实际被翻开的配置数（= 0 表示无需迁移）
 */
export function migrateSyncFilesDefaults() {
  let flipped = 0;
  try {
    if (!fs.existsSync(CONFIG.usersDir)) return 0;
    for (const f of fs.readdirSync(CONFIG.usersDir)) {
      if (!f.endsWith(".webdav.json")) continue;
      const p = path.join(CONFIG.usersDir, f);
      let cfg;
      try { cfg = readJson(p); } catch { continue; }
      if (!cfg || typeof cfg !== "object" || cfg.v0706SyncFiles) continue;
      cfg.v0706SyncFiles = true;
      if (cfg.syncFiles === false) { cfg.syncFiles = true; flipped++; }
      writeJson(p, cfg);
    }
  } catch { /* 迁移失败不影响服务启动 */ }
  return flipped;
}

// ---------- 定时自动同步（服务进程存活期间按间隔执行；失败静默） ----------

const autoSyncInProgress = new Set(); // 防同一用户重入

export async function runAutoSync() {
  if (!fs.existsSync(CONFIG.usersDir)) return;
  for (const f of fs.readdirSync(CONFIG.usersDir)) {
    if (!f.endsWith(".webdav.json")) continue;
    const userId = f.slice(0, -".webdav.json".length);
    const cfg = getSyncConfig(userId);
    if (!cfg || !cfg.autoSync || autoSyncInProgress.has(userId)) continue;
    // v0.6.13：手动同步进行中 → 本轮跳过（否则 runSync 抛 409 会被记成 lastSyncError 污染展示）
    if (syncInFlight.has(userId)) continue;
    const due = (cfg.lastSyncAt || 0) + (cfg.intervalMin || DEFAULT_INTERVAL_MIN) * 60_000;
    if (Date.now() < due) continue;
  autoSyncInProgress.add(userId);
  try { await runSync(userId); }
  catch (e) {
    // P-104：自动同步失败不再静默——记 lastSyncError 供前端展示（不更新 lastSyncAt，保持原有"每周期重试"节奏）
    updateConfig(userId, { lastSyncError: (e && e.message) || "同步失败" });
  }
  finally { autoSyncInProgress.delete(userId); }
  }
}
