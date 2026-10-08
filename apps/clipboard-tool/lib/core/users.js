// lib/core/users.js - 用户域：CRUD + scrypt 密码 + 会话 token + 登录限流
// 纯业务逻辑，不碰 HTTP。路由层负责解析 token 并传入 userId。
import crypto from "node:crypto";
import path from "node:path";
import { CONFIG } from "./config.js";
import { readJson, writeJson, httpError, assertId, rmForce } from "./store.js";

// ---------- 会话（v0.3.0 起落盘持久化，重启不掉线） ----------
// v0.6.13 去缓存重构（用户决策）：小体量(单机/低并发)下【文件 sessions.json 即唯一真相】——
// 每次读写直接走文件(readJson/writeJson)，不做内存缓存。收益：无"内存/文件两份数据"的一致性问题，
// 登录态丢失/退出复活等分叉 bug 从根上消失；实测读+解析 774B JSON 单次 ~14μs，性能无感。
// ⚠️ 注意事项：若未来请求量达每秒几十次以上（多用户高并发/常驻服务被机器高频调用），
// 再引入"内存缓存 + TTL 失效(如 1s)"，不要提前加。登录限流表(loginGuard)是临时状态，
// 重启丢失无害，保持内存即可。
const SESSION_TTL_MS = 30 * 24 * 3600 * 1000; // 会话有效期 30 天
const sessionsFile = () => path.join(path.dirname(CONFIG.usersFile), "sessions.json");

/** 读会话表（文件即真相）：坏文件/不存在 → 空表；顺手过滤已过期条目 */
function loadSessions() {
  const data = readJson(sessionsFile(), {});
  const now = Date.now();
  const out = {};
  if (data && typeof data === "object") {
    for (const [tk, s] of Object.entries(data)) {
      if (s && s.userId && (!s.expireAt || s.expireAt > now)) out[tk] = s;
    }
  }
  return out;
}

/** 惰性清理过期会话（返回清理数；文件直接过滤重写，非活跃数据自然收敛） */
export function pruneExpiredSessions() {
  const sessions = loadSessions();
  const now = Date.now();
  let removed = 0;
  for (const [tk, s] of Object.entries(sessions)) {
    if (s.expireAt && s.expireAt <= now) { delete sessions[tk]; removed++; }
  }
  if (removed) writeJson(sessionsFile(), sessions);
  return removed;
}

/** 登录限流表：key(ip:userId) -> { fails, lockedUntil }。
 *  key 带目标用户——只锁「同一 IP 对同一用户」的暴力尝试，避免一人输错全用户连坐（v0.3.1 修复）。
 *  P1-3：追加惰性清理——查询时顺带回收「已过期且失败计数归零」的 key，防长运行内存缓慢增长。 */
const loginGuard = new Map();

function loginKey(ip, userId) {
  return (ip || "?") + ":" + (userId || "?");
}

/** 惰性清理限流表（内存卫生，isLoginBlocked 查询时顺带回收）：
 *  ① 清理已过期的锁定（lockedUntil 过期且失败归零）
 *  ② v0.6.11 真修复：清理「持续失败但未达阈值」的 key（fails>0 且超过 LOGIN_WINDOW_MS 未再失败）——
 *     P1-3 原实现只清①，失败 1~7 次后停手的 IP 会永久残留（假修复） */
function pruneLoginGuard() {
  const now = Date.now();
  for (const [k, g] of loginGuard) {
    if (!g) { loginGuard.delete(k); continue; }
    if (g.lockedUntil && g.lockedUntil <= now && g.fails <= 0) loginGuard.delete(k);
    else if (g.fails > 0 && now - (g.lastFailAt || 0) > CONFIG.LOGIN_WINDOW_MS) loginGuard.delete(k);
  }
}

// ---------- 用户数据 ----------

function loadUsers() {
  const list = readJson(CONFIG.usersFile, []);
  return Array.isArray(list) ? list : [];
}

function saveUsers(list) {
  writeJson(CONFIG.usersFile, list);
}

// ---------- 双名模型（v0.6.13 重构） ----------
// 身份键与展示名分离（业界标准：Steam/Google/WordPress 同款）：
//  - accountName 账号名：创建后不可变，是唯一身份键（登录/WebDAV 寻址/跨设备识别）——改名不涉及
//  - displayName  显示名：可随时改，只影响展示（顶栏/卡片/导出文件名）
// 兼容旧数据：v0.6.13 之前只有 name 字段 → accountName = name、displayName = name（读取时归一，不强制迁移文件）
function acctName(u) { return u.accountName || u.name || ""; } // 身份键（旧数据 = 原 name）
function dispName(u) { return u.displayName || u.name || ""; } // 展示名（旧数据 = 原 name）

export function listUsers() {
  // 对外视图：不含 passHash；hasPass 仅暴露"是否设密"（前端据此决定是否需要密码框）
  return loadUsers().map((u) => ({ id: u.id, name: dispName(u), accountName: acctName(u), color: u.color, createdAt: u.createdAt, hasPass: !!u.passHash }));
}

function findUser(id) {
  return loadUsers().find((u) => u.id === id) || null;
}

function userFile(id) {
  return path.join(CONFIG.usersDir, id + ".json");
}

// ---------- 密码（scrypt 加盐慢哈希） ----------

function hashPassword(password) {
  const salt = crypto.randomBytes(16).toString("hex");
  const hash = crypto.scryptSync(String(password), salt, CONFIG.SCRYPT_KEYLEN).toString("hex");
  return `${salt}:${hash}`;
}

function verifyPassword(password, stored) {
  if (!stored) return true; // 未设密码
  const [salt, hash] = String(stored).split(":");
  if (!salt || !hash) return false;
  // v0.6.11：先校验 hash 为合法 hex 且长度一致——否则 timingSafeEqual 对不等长 Buffer
  // 抛 ERR_CRYPTO_TIMING_SAFE_EQUAL_LENGTH，passHash 损坏（手工编辑/半写坏文件）时登录 500 崩溃。
  if (!/^[0-9a-f]+$/i.test(hash)) return false;
  const calc = crypto.scryptSync(String(password || ""), salt, CONFIG.SCRYPT_KEYLEN).toString("hex");
  const a = Buffer.from(calc, "hex"), b = Buffer.from(hash, "hex");
  if (a.length !== b.length) return false;
  return crypto.timingSafeEqual(a, b);
}

// ---------- 会话 ----------

/** 新建会话 token（文件即真相：writeJson 失败即抛错，文件未动=无会话，无"半成功"状态） */
export function createToken(userId) {
  const token = crypto.randomBytes(CONFIG.TOKEN_BYTES).toString("hex");
  const now = Date.now();
  const sessions = loadSessions();
  sessions[token] = { userId, createdAt: now, expireAt: now + SESSION_TTL_MS };
  writeJson(sessionsFile(), sessions);
  return token;
}

/** 校验 token，返回 userId；无效/过期返回 null（路由层统一在此解析，业务不碰 token） */
export function verifyToken(token) {
  if (!token) return null;
  const sessions = loadSessions();
  const s = sessions[token];
  if (!s) return null;
  if (s.expireAt && s.expireAt <= Date.now()) { // 过期即删（惰性清理：文件直接过滤重写）
    delete sessions[token];
    writeJson(sessionsFile(), sessions);
    return null;
  }
  return s.userId;
}

export function destroyToken(token) {
  const sessions = loadSessions();
  if (delete sessions[token]) writeJson(sessionsFile(), sessions);
}

/** 销毁某用户的全部会话（改密/删号时调用） */
function destroyUserSessions(userId, keepToken = null) {
  const sessions = loadSessions();
  let changed = false;
  for (const [tk, s] of Object.entries(sessions)) {
    if (s.userId === userId && tk !== keepToken) { delete sessions[tk]; changed = true; }
  }
  if (changed) writeJson(sessionsFile(), sessions);
}

// ---------- 限流（防密码爆破；按 ip+目标用户 组合，防连坐） ----------

export function isLoginBlocked(ip, userId = "") {
  const g = loginGuard.get(loginKey(ip, userId));
  if (!g) return false;
  if (g.lockedUntil && g.lockedUntil > Date.now()) return true;
  if (g.lockedUntil) loginGuard.delete(loginKey(ip, userId)); // 仅清理"已过期的锁定"，正在累计失败的保留
  pruneLoginGuard(); // P1-3：顺带回收已过期的空 key（内存卫生，低频调用无压力）
  return false;
}

export function noteLoginFail(ip, userId = "") {
  const key = loginKey(ip, userId);
  const g = loginGuard.get(key) || { fails: 0, lockedUntil: 0, lastFailAt: 0 };
  g.fails += 1;
  g.lastFailAt = Date.now(); // v0.6.11：记录最后失败时间——未达阈值时按窗口过期清理（见 pruneLoginGuard ②）
  if (g.fails >= CONFIG.LOGIN_MAX_FAIL) {
    g.lockedUntil = Date.now() + CONFIG.LOGIN_WINDOW_MS;
    g.fails = 0;
  }
  loginGuard.set(key, g);
}

export function clearLoginFail(ip, userId = "") {
  loginGuard.delete(loginKey(ip, userId));
}

// ---------- 业务操作 ----------

/** 新建用户（v0.6.13 双名模型）：accountName 账号名必填且不可变（身份键，仅限英文+数字）；displayName 显示名可选（默认=账号名，不限字符）。
 *  密码可选（空 = 无密码零摩擦进入）。 */
export function createUser(accountName, displayName = "", password = "") {
  const a = String(accountName || "").trim();
  if (!a) throw httpError(400, "账号名不能为空");
  if (a.length > CONFIG.MAX_USER_NAME) throw httpError(400, `账号名最多 ${CONFIG.MAX_USER_NAME} 字`);
  if (!CONFIG.ACCOUNT_NAME_RE.test(a)) throw httpError(400, "账号名仅限英文和数字");
  const d = (displayName != null && String(displayName).trim()) ? String(displayName).trim().slice(0, CONFIG.MAX_USER_NAME) : a;
  if (password != null && String(password).length > CONFIG.MAX_PASSWORD) throw httpError(400, "密码过长");
  const list = loadUsers();
  if (list.some((u) => acctName(u) === a)) throw httpError(409, "账号名已存在");
  if (list.length >= CONFIG.MAX_USERS) throw httpError(409, "用户数已达上限"); // 走查 P-6：防无限制建号占磁盘
  const user = {
    id: crypto.randomUUID(),
    accountName: a,
    displayName: d,
    color: PALETTE[list.length % PALETTE.length],
    passHash: password ? hashPassword(password) : "",
    createdAt: Date.now(),
  };
  list.push(user);
  saveUsers(list);
  return { id: user.id, name: d, accountName: a, color: user.color, createdAt: user.createdAt };
}

/** 登录验证：密码正确（或未设密码）→ 签发 token */
export function login(id, password = "") {
  const user = findUser(assertId(id, "用户 id"));
  if (!user) throw httpError(404, "用户不存在");
  if (!verifyPassword(password, user.passHash)) throw httpError(401, "密码错误");
  return { token: createToken(user.id), user: { id: user.id, name: dispName(user), accountName: acctName(user), color: user.color } };
}

/** 修改密码：需验证原密码（设密码用户自助改密）。
 * keepToken 为当前会话 token——改密后销毁该用户其他会话（多端安全，第二轮走查 R-4）。
 */
export function changePassword(id, oldPass, newPass, keepToken = null) {
  const uid = assertId(id, "用户 id");
  const list = loadUsers(); // 同一份数组上修改后保存，避免二次加载丢失改动
  const user = list.find((u) => u.id === uid);
  if (!user) throw httpError(404, "用户不存在");
  if (!verifyPassword(oldPass, user.passHash)) throw httpError(401, "原密码错误");
  if (newPass == null || !String(newPass).length) throw httpError(400, "新密码不能为空");
  if (String(newPass).length > CONFIG.MAX_PASSWORD) throw httpError(400, "密码过长");
  user.passHash = hashPassword(String(newPass));
  saveUsers(list);
  destroyUserSessions(uid, keepToken); // 销毁其他会话，保留当前（v0.3.0 落盘版）
  return { ok: true };
}

/** 修改显示名（v0.6.13 双名模型：只改 displayName，不影响 accountName 身份键/WebDAV 寻址）。同名校验 409，不销毁会话。 */
export function renameUser(id, newDisplayName) {
  const uid = assertId(id, "用户 id");
  const n = String(newDisplayName || "").trim();
  if (!n) throw httpError(400, "显示名不能为空");
  if (n.length > CONFIG.MAX_USER_NAME) throw httpError(400, `显示名最多 ${CONFIG.MAX_USER_NAME} 字`);
  const list = loadUsers();
  const user = list.find((u) => u.id === uid);
  if (!user) throw httpError(404, "用户不存在");
  if (list.some((u) => u.id !== uid && dispName(u) === n)) throw httpError(409, "显示名已存在");
  user.displayName = n;
  saveUsers(list);
  return { ok: true, name: n };
}

/** 修改账号名（v0.6.13 双名模型·管理员级一次性操作）：accountName 是身份键（WebDAV 寻址/跨设备识别），
 *  日常改名请用显示名。仅用于创建后修正身份键；调用方负责 WebDAV 快照迁移（记 prevAccountName，下次同步自愈）。 */
export function changeAccountName(id, newAccountName) {
  const uid = assertId(id, "用户 id");
  const n = String(newAccountName || "").trim();
  if (!n) throw httpError(400, "账号名不能为空");
  if (n.length > CONFIG.MAX_USER_NAME) throw httpError(400, `账号名最多 ${CONFIG.MAX_USER_NAME} 字`);
  if (!CONFIG.ACCOUNT_NAME_RE.test(n)) throw httpError(400, "账号名仅限英文和数字");
  const list = loadUsers();
  const user = list.find((u) => u.id === uid);
  if (!user) throw httpError(404, "用户不存在");
  if (list.some((u) => u.id !== uid && acctName(u) === n)) throw httpError(409, "账号名已存在");
  const oldName = acctName(user);
  user.accountName = n;
  saveUsers(list);
  return { ok: true, name: n, oldName };
}

/** 取单用户安全视图（v0.6.13：boot 恢复登录时拉最新展示名等，防 localStorage 缓存过期/多端改名不一致） */
export function getUserPublic(id) {
  const u = findUser(assertId(id, "用户 id"));
  if (!u) throw httpError(404, "用户不存在");
  return { id: u.id, name: dispName(u), accountName: acctName(u), color: u.color, createdAt: u.createdAt, hasPass: !!u.passHash };
}

/** 取用户账号名（v0.6.13 起 WebDAV 按账号名寻址：accountName 不可变 → 快照路径永不因改名变化） */
export function getUserName(id) {
  const u = findUser(assertId(id, "用户 id"));
  if (!u) throw httpError(404, "用户不存在");
  return acctName(u);
}

/**
 * v0.7.x 昵称随 WebDAV 快照同步（对齐 exe：displayName 本地权威、仅未显式设昵称时采纳远端）。
 * 返回是否自定义过昵称 + 当前昵称。判定：displayName ≠ accountName 才算自定义
 * （createUser 默认把 displayName=账号名写入，故不能看是否有键；旧数据仅 name 的 fallback 也满足相等=未自定义）。
 * 未自定义时 upload 不带 nickname；hasExplicit=true 时上传才带、且拉取采纳被拒（本地优先）。
 */
export function getUserDisplayNameEx(id) {
  const u = findUser(assertId(id, "用户 id"));
  if (!u) throw httpError(404, "用户不存在");
  const d = dispName(u), a = acctName(u);
  return { hasExplicit: !!String(d).trim() && d !== a, displayName: d };
}

/** v0.7.x 拉取采纳远端昵称：仅当用户未自定义昵称(displayName==账号名)时，把快照 nickname 写入其 displayName（本地权威优先）。
 *  撞其它用户同名 → 放弃采纳(保留本地)不抛错，避免中断同步；返回是否已采纳。 */
export function adoptRemoteNickname(id, nickname) {
  const uid = assertId(id, "用户 id");
  const n = String(nickname || "").trim();
  if (!n) return false;
  const list = loadUsers();
  const user = list.find((u) => u.id === uid);
  if (!user) throw httpError(404, "用户不存在");
  if (dispName(user) !== acctName(user)) return false; // 已自定义昵称 → 保留本地，不覆盖
  if (list.some((u) => u.id !== uid && dispName(u) === n)) return false; // 撞同名 → 放弃
  user.displayName = n;
  saveUsers(list);
  return true;
}

/** 删除用户：同时清空其条目与文件，并销毁该用户全部会话 token
 * （否则旧 token 仍有效，可重建幽灵数据文件——走查 P-10）
 */
export function deleteUser(id) {
  const uid = assertId(id, "用户 id");
  if (!findUser(uid)) throw httpError(404, "用户不存在");
  saveUsers(loadUsers().filter((u) => u.id !== uid));
  rmForce(userFile(uid));           // 条目文件
  rmForce(userFile(uid).replace(/\.json$/, ".archive.json")); // 归档文件（滚动归档 v0.2.0）
  rmForce(path.join(CONFIG.filesDir, uid)); // 文件实体
  rmForce(path.join(CONFIG.usersDir, uid + ".tombstones.json")); // 墓碑（v0.3.1：补清理，防残留）
  rmForce(path.join(CONFIG.usersDir, uid + ".webdav.json"));    // WebDAV 配置（v0.3.1：补清理，防密码残留）
  destroyUserSessions(uid);         // 销毁该用户全部会话（v0.3.0 落盘版）
  return { ok: true };
}

// v0.6.2：用户头像色板对齐设计指南 mock——金为主 + 砖红点缀 + 暖棕系
const PALETTE = ["#C9A96E", "#D4AF37", "#AE4D4D", "#A9714B", "#B05C3B", "#8B6F47"];
