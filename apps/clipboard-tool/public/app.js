
"use strict";
const $ = (s, el=document) => el.querySelector(s);
const el = (tag, cls, txt) => { const n = document.createElement(tag); if (cls) n.className = cls; if (txt != null) n.textContent = txt; return n; };

// 子路径挂载：平台反代注入 window.__BASE__ = /tool/<id>；独立运行兜底空串
const BASE = (window.__BASE__ || "").replace(/\/+$/, "");

// ---------- 状态（单一 state 对象，页面切换只改这里） ----------
const state = { users: [], current: null, clips: [], tags: [], filter: { q: "", tag: "", type: "all", archived: false }, cols: "auto" };
const LS = { get:(k,d)=>{ try{ return JSON.parse(localStorage.getItem(k)) ?? d; }catch{ return d; } }, set:(k,v)=>localStorage.setItem(k, JSON.stringify(v)), del:(k)=>localStorage.removeItem(k) };
// 卡片复制来源抑制：点击卡片复制内容/图片时设置时间戳，clipboardchange 在窗口期内不自动弹存入窗（避免"复制即弹"）
let suppressAutoPasteUntil = 0;
// 用户选择页编辑模式状态（v0.3.1：删除用户后保持编辑状态，点击空白处退出）
let userEditMode = false;
// 待存入的富文本 html（从剪贴板 text/html 读取，存入弹窗关闭时清空；无富文本来源则为空）
let pendingHtml = "";
// 批量编辑模式（v0.6.15）：编辑按钮进入多选——全选当前页 / 批量删除 / 批量加标签 / 批量减标签
let batchMode = false;
const batchSel = new Set(); // 已选条目 id 集合（按 id 存，跨过滤/重排保持）

// ---------- API（统一带 token，10s 超时防永久挂起——第三轮 F-1） ----------
const REQ_TIMEOUT = 10000;
async function api(path, opts = {}) {
  const headers = { ...(opts.headers || {}) };
  // opts.token 显式覆盖（boot 恢复会话时 state.current 尚未设置，必须显式传 token——修复"每次刷新被踢出登录"）
  if (opts.token || state.current) headers["Authorization"] = "Bearer " + (opts.token || state.current.token);
  if (opts.json !== undefined) { headers["Content-Type"] = "application/json"; opts.body = JSON.stringify(opts.json); }
  const ctrl = new AbortController();
  const timer = setTimeout(() => ctrl.abort(), REQ_TIMEOUT);
  let r;
  try {
    r = await fetch(BASE + path, { ...opts, headers, signal: ctrl.signal });
  } catch (e) {
    clearTimeout(timer);
    throw new Error("请求超时或网络异常");
  }
  clearTimeout(timer);
  const data = await r.json().catch(() => ({}));
  if (r.status === 401 && state.current) handleSessionLost(); // 第二轮 R-2：会话失效自动回选用户页
  if (!r.ok) throw new Error(data.error || ("请求失败 " + r.status));
  return data;
}
async function apiBlob(path) {
  const headers = {};
  if (state.current) headers["Authorization"] = "Bearer " + state.current.token;
  const ctrl = new AbortController();
  const timer = setTimeout(() => ctrl.abort(), REQ_TIMEOUT);
  let r;
  try {
    r = await fetch(BASE + path, { headers, signal: ctrl.signal });
  } catch (e) {
    clearTimeout(timer);
    throw new Error("请求超时或网络异常");
  }
  clearTimeout(timer);
  if (r.status === 401 && state.current) handleSessionLost();
  // v0.7.6：404 = 本地缺少文件实体（多为WebDAV 同步时未备份/远端也无），给出可操作提示而非笼统「下载失败」
  if (!r.ok) {
    if (r.status === 404) throw new Error("文件实体缺失：本机与云端都没有该文件，请在存有该文件的设备上开启「同步文件实体」后同步一次");
    throw new Error("下载失败（HTTP " + r.status + "）");
  }
  return r.blob();
}
/** 会话失效：清理本地状态回选用户页（被删号/被踢/服务重启后 token 失效） */
function handleSessionLost() {
  LS.del("cur"); state.current = null; state.clips = []; state.tags = [];
  userEditMode = false; // 回用户选择页不残留编辑模式
  resetFilter(); // v0.3.1 修复：必须用完整 resetFilter（含 type/archived），否则重进列表被 type=undefined 过滤成空白
  render();
}
/** 进入用户时重置过滤条件（第三轮 F-2：切换用户后搜索词/标签不得残留；type 保留"全部"——否则类型 Tab 无选中且列表过滤为空） */
function resetFilter() { state.filter.q = ""; state.filter.tag = ""; state.filter.type = "all"; state.filter.archived = false; }
/** 防连点锁 + 操作中视觉反馈（UI 走查 U-1：请求期间按钮禁用并置灰，用户知道正在处理） */
function guard(btn, fn) {
  return (e) => {
    if (btn._busy) return;
    btn._busy = true;
    btn.disabled = true; btn.classList.add("busy");
    const done = () => { btn._busy = false; btn.disabled = false; btn.classList.remove("busy"); };
    try {
      const r = fn(e);
      if (r && typeof r.finally === "function") r.finally(done);
      else done();
    } catch { done(); }
  };
}
/** 自定义确认弹窗（不依赖原生 confirm——预览沙箱下 confirm 行为不可靠，第二轮 R-3） */
function askConfirm(msg, onOk, okText = "确认", onCancel = null) {
  const root = $("#modal-root");
  const m = el("div", "mask");
  const modal = el("div", "modal");
  modal.append(el("h3", "", "确认操作"));
  modal.append(el("p", "", msg));
  const row = el("div", "form-row");
  const ok = el("button", "btn primary", okText); ok.style.flex = "1";
  const cancel = el("button", "btn ghost", "取消");
  row.append(ok, cancel); modal.append(row);
  ok.onclick = () => { m.remove(); onOk(); };
  cancel.onclick = () => { m.remove(); if (onCancel) onCancel(); };
  m.append(modal); root.append(m);
}
/** Promise 版确认：确认 resolve(true)，取消 resolve(false） */
function askConfirmP(msg, okText = "确认") {
  return new Promise((res) => askConfirm(msg, () => res(true), okText, () => res(false)));
}

// ---------- 一键复制（Clipboard API + execCommand 兜底；同步手势内完成，防 Safari 拒绝） ----------
function copyText(text) {
  if (navigator.clipboard && window.isSecureContext) {
    return navigator.clipboard.writeText(text).then(() => true, () => legacyCopy(text));
  }
  return Promise.resolve(legacyCopy(text));
}

// ---------- 富文本复制（v0.6.9 重构定稿：数据流统一——存入 normalize / 复制 buildWordDoc + execCommand） ----------
/** 统一标准化（存入时）：DOMParser 解析 → <style> 块规则内联到元素 → 移除 style 块 → 干净内联片段。
 *  浏览器写剪贴板强制剥 style 块只留 inline style（Chromium 122+），存入前内联化，复制时格式才完整。
 *  纯函数，失败回退原 html。
 *  v0.6.12 修复：style 块规则改用原始文本 textContent 正则解析——CSSOM 的 rule.style.cssText
 *  只序列化浏览器认识的属性，会把 Word 私有属性（tab-interval / text-justify-trim / mso-* 等）
 *  丢弃、把 word-wrap 规范化成 overflow-wrap，导致 Word/WPS 粘贴还原不全（最小单元诊断页定位）。 */
function normalizeRichHtml(html) {
  try {
    const doc = new DOMParser().parseFromString(html, "text/html");
    const rules = [];
    for (const st of doc.querySelectorAll("style")) {
      const text = st.textContent || "";
      for (const m of text.matchAll(/([^{}]+)\{([^{}]*)\}/g)) {
        const sel = m[1].trim();
        if (!sel || sel.startsWith("@")) continue; // 跳过 @font-face/@media/@page 等
        const decl = m[2].trim();
        if (decl) rules.push([sel, decl]);
      }
    }
    if (rules.length) {
      for (const el of doc.querySelectorAll("*")) {
        const merged = rules.filter(([sel]) => { try { return el.matches(sel); } catch { return false; } }).map(([, css]) => css).join(";");
        if (merged) {
          const prev = el.getAttribute("style");
          el.setAttribute("style", merged + (prev ? ";" + prev : ""));
        }
      }
    }
    doc.querySelectorAll("style").forEach(s => s.remove());
    // v0.6.12:body 自身属性(Word 文档级设置,如 tab-interval/word-wrap/text-justify-trim/lang)
    // 不在 innerHTML 里,必须保留在 <body> 标签上,否则 Word/WPS 粘贴还原丢文档级格式
    const attrs = [...doc.body.attributes].map(a => a.name + '="' + String(a.value).replace(/"/g, "&quot;") + '"').join(" ");
    // 双保险(CF_HTML 规范:粘贴应用主要解析 StartFragment/EndFragment 之间的 Fragment,body 属性在 Fragment 外
    // 的 context 里,部分应用可能不读)——把 body 的 style 也内联到段落元素上,保证 Fragment 内也有文档级属性
    const bodyStyle = doc.body.getAttribute("style");
    if (bodyStyle) {
      const paraSel = "p,div,h1,h2,h3,h4,h5,h6,li";
      for (const el of doc.body.querySelectorAll(paraSel)) {
        const prev = el.getAttribute("style");
        el.setAttribute("style", bodyStyle + (prev ? ";" + prev : ""));
      }
    }
    return (attrs ? "<body " + attrs + ">" : "<body>") + doc.body.innerHTML + "</body>";
  } catch { return html; }
}
/** 复制包装：片段 → 带 Word 命名空间的完整文档（Word 识别"来自 Word"靠 xmlns:o/w/m，见 Microsoft roosterjs
 *  isWordDesktopDocument.ts / CKEditor paste-from-office；setData 不受 Chromium 122+ sanitize 影响，原样进 CF_HTML） */
function buildWordDoc(html) {
  const s = String(html || "").trim();
  if (!s) return "";
  if (/xmlns:w\s*=/.test(s) || /<html[\s>]/i.test(s)) return s; // 已是 Word/完整文档
  // v0.6.12:body 片段(normalizeRichHtml 保留的文档级属性 tab-interval 等)→ 属性并入外层 body,避免嵌套
  const m = s.match(/^<body([^>]*)>([\s\S]*)<\/body>$/i);
  if (m) {
    return '<html xmlns:o="urn:schemas-microsoft-com:office:office" xmlns:w="urn:schemas-microsoft-com:office:word" xmlns:m="http://schemas.microsoft.com/office/2004/12/omml"><head><meta charset="utf-8"></head><body' + m[1] + '><!--StartFragment -->' + m[2] + '<!--EndFragment --></body></html>';
  }
  return '<html xmlns:o="urn:schemas-microsoft-com:office:office" xmlns:w="urn:schemas-microsoft-com:office:word" xmlns:m="http://schemas.microsoft.com/office/2004/12/omml"><head><meta charset="utf-8"></head><body><!--StartFragment -->' + s + '<!--EndFragment --></body></html>';
}
/** execCommand 复制（holder 纯文本承载选区，setData 注入原始完整文档——不解析 html，避免 DOM 剥 xmlns/style） */
function execCommandRich(rich, plain) {
  try {
    const holder = document.createElement("div");
    holder.textContent = plain; // 纯文本承载：绝不 innerHTML 解析完整文档
    holder.style.position = "fixed"; holder.style.top = "-9999px"; holder.style.left = "-9999px";
    document.body.appendChild(holder);
    holder.focus();
    const range = document.createRange();
    range.selectNodeContents(holder);
    const sel = window.getSelection();
    sel.removeAllRanges(); sel.addRange(range);
    const listener = (e) => {
      e.clipboardData.setData("text/html", rich); // 完整 Word 文档（含 xmlns，Word 识别来源的关键）
      e.clipboardData.setData("text/plain", plain);
      e.preventDefault();
    };
    document.addEventListener("copy", listener);
    const ok = document.execCommand("copy");
    document.removeEventListener("copy", listener);
    sel.removeAllRanges();
    document.body.removeChild(holder);
    return ok;
  } catch { return false; }
}
/**
 * 富文本复制（重构定稿，无历史缠绕）：
 *  主路径 execCommand + setData(buildWordDoc(html)) —— 诊断第7项实测 Word 粘贴格式正确；
 *  兜底 clipboard.write（Chromium 122+ sanitize 压缩，仅 execCommand 不可用时）。
 *  iframe 环境（预览面板）execCommand 常失败 → 明确提示独立浏览器。
 */
async function copyRich(html, text) {
  const plain = text || "";
  const rich = buildWordDoc(html); // 统一：片段 → 带 xmlns:o/w/m 的完整 Word 文档
  if (execCommandRich(rich, plain)) return true;
  if (navigator.clipboard && window.isSecureContext && typeof ClipboardItem !== "undefined") {
    try {
      await navigator.clipboard.write([
        new ClipboardItem({
          "text/html": new Blob([rich], { type: "text/html" }),
          "text/plain": new Blob([plain], { type: "text/plain" }),
        }),
      ]);
      return true;
    } catch { return false; }
  }
  return false;
}

/** 纯文本 → 简单富文本 HTML（换行转段落，URL 转可点链接；仅"有富文本来源"时用，手动输入不生成） */
function textToHtml(text) {
  const esc = String(text || "").replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
  return "<div>" + esc.split(/\n{2,}/).map(p =>
    "<p>" + p.replace(/\n/g, "<br>").replace(/(https?:\/\/\S+)/g, '<a href="$1">$1</a>') + "</p>"
  ).join("") + "</div>";
}
function legacyCopy(text) {
  try {
    const ta = document.createElement("textarea");
    ta.value = text;
    ta.setAttribute("readonly", "");
    ta.style.position = "fixed"; ta.style.top = "-9999px"; ta.style.left = "-9999px";
    document.body.appendChild(ta);
    ta.focus({ preventScroll: true }); ta.select(); ta.setSelectionRange(0, text.length);
    const ok = document.execCommand("copy");
    document.body.removeChild(ta);
    return ok;
  } catch { return false; }
}

function flash(msg, x, y) {
  const f = $("#flash");
  f.textContent = msg;
  if (typeof x === "number" && typeof y === "number") {
    // v0.4.2：复制成功类提示跟随鼠标点击位置显示
    f.style.left = x + "px";
    f.style.top = y + "px";
    f.classList.add("at-pos");
  } else {
    f.style.left = "";
    f.style.top = "";
    f.classList.remove("at-pos");
  }
  f.classList.add("show");
  clearTimeout(f._t);
  f._t = setTimeout(() => f.classList.remove("show"), 1400);
}
function errToast(msg) {
  const t = el("div", "toast-err", msg);
  document.body.appendChild(t);
  setTimeout(() => t.remove(), 2600);
}

function esc(s) { return String(s ?? ""); }
function fmtSize(n) { if (!n) return "0B"; if (n < 1024) return n + "B"; if (n < 1048576) return (n/1024).toFixed(1) + "KB"; return (n/1048576).toFixed(1) + "MB"; }
// v0.4.2：完整时间格式（年月日 + 时分），如 2026/08/12 13:33
function fmtTime(ts) {
  if (!ts) return "";
  const d = new Date(ts);
  const p = (n) => String(n).padStart(2, "0");
  return `${d.getFullYear()}/${p(d.getMonth() + 1)}/${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}`;
}
function expLabel(ts) { if (!ts) return ""; const left = ts - Date.now(); if (left <= 0) return "已过期"; if (left < 3600000) return Math.ceil(left/60000) + " 分钟后过期"; if (left < 86400000) return Math.ceil(left/3600000) + " 小时后过期"; return Math.ceil(left/86400000) + " 天后过期"; }

// ---------- 重复检测纯函数（v0.4.3：架构评估 v2 #3——比对逻辑可单测，无 DOM 副作用） ----------
/**
 * 在条目列表中查找与给定内容重复的条目（link 比 url，其他比 content）。
 * @param {string} content 待比对内容（已 trim）
 * @param {Array} clips 条目列表（含 archived 标记）
 * @returns {object|null} 重复条目（含 archived）或 null
 */
function findDuplicateClip(content, clips) {
  if (!content || !Array.isArray(clips)) return null;
  // v0.4.5：link 条目 url 兜底 ""（逻辑核验 P2-2 防御性——防 undefined 参与比对）
  return clips.find(c => (c.type === "link" ? (c.url || "") === content : (c.content || "") === content)) || null;
}

// ---------- 视图切换 ----------
function render() {
  resetBatchMode(); // v0.6.15：整页重建（进入用户/登出/会话失效）必然退出批量编辑
  $("#view").replaceChildren();
  if (state.current) { userEditMode = false; renderMain(); } // 进主页面必然退出编辑模式
  else renderUserSelect();
}

// ---------- 用户选择页 ----------
/** 用户选择页编辑模式开关（v0.3.1 重构）：
 *  - 进入：所有卡片右上角加 ✕ 删除按钮；退出：全部收起
 *  - 删除用户后保持编辑状态（userEditMode 全局标志，render 重建后自动恢复）
 *  - 点击页面空白处退出（见 boot 的事件委托） */
function setUserEditMode(on) {
  userEditMode = !!on;
  const grid = document.querySelector(".user-grid");
  const editAllBtn = document.querySelector(".edit-all-btn");
  if (editAllBtn) {
    editAllBtn.textContent = on ? "完成" : "编辑";
    editAllBtn.classList.toggle("on", on);
  }
  if (!grid) return;
  grid.classList.toggle("editing", on);
  grid.querySelectorAll(".user-card").forEach((card) => {
    const delBtn = card.querySelector(".del-user-btn");
    if (on && !delBtn) {
      const idx = [...grid.querySelectorAll(".user-card")].indexOf(card);
      const u = state.users[idx];
      const d = el("button", "del-user-btn", "✕");
      d.title = "删除该用户";
      d.onclick = (e) => {
        e.stopPropagation(); // 关键：在按钮层阻止冒泡，避免触发卡片 onclick 进入用户
        if (!u) return;
        askConfirm(`删除用户「${u.name}」？其全部条目与文件将被永久清除，不可恢复！`, guard(d, async () => {
          // 删除需本人身份：无密码直接登录拿 token；有密码先验证
          let token = "";
          if (u.hasPass) {
            token = await promptUserPassword(u, "验证密码以删除账号");
            if (!token) return;
          } else {
            const r = await api("/api/session", { method: "POST", json: { id: u.id, password: "" } }).catch(e2 => errToast(e2.message));
            if (!r) return;
            token = r.token;
          }
          const r = await api("/api/users/" + u.id, { method: "DELETE", token }).catch(e2 => errToast(e2.message));
          if (r) { flash("用户已删除"); await loadUsers(); render(); /* userEditMode 保持 true → 重建后仍处于编辑模式 */ }
        }), "永久删除");
      };
      card.append(d);
    } else if (!on && delBtn) {
      delBtn.remove();
    }
  });
}

async function renderUserSelect() {
  const v = $("#view");
  const wall = el("div", "wall");
  // 品牌区（v0.6.3 极简墙：印章 logo + 衬线标题 + 副标）
  const brand = el("div", "brand");
  brand.append(el("div", "mark", "剪"), el("h1", "serif", "剪贴板"), el("div", "sub", "SELECT A PERSON · 选择身份进入"));
  wall.append(brand);
  // 用户网格
  const grid = el("div", "user-grid");
  if (!state.users.length) grid.append(el("div", "empty-state", "还没有用户，点下方新建一个"));
  for (const u of state.users) {
    const card = el("div", "user-card");
    const av = el("div", "avatar", (u.name || "?").slice(0, 1).toUpperCase());
    av.style.background = u.color;
    card.append(av, el("div", "name", u.name));
    // v0.6.13 双名模型：小字显示账号名（身份键——跨设备迁移/同步时填它）
    if (u.accountName && u.accountName !== u.name) {
      const acct = el("div", "acct", u.accountName);
      acct.style.cssText = "font-size:11px;color:var(--muted);margin-top:2px";
      card.append(acct);
    }
    card.append(el("div", "cnt", ""));
    // v0.4.5：补 guard 防连点（逻辑核验 P2-1——连点避免并发创建会话）
    card.onclick = guard(card, () => enterUser(u));
    grid.append(card);
  }
  // 新建用户：虚线占位卡（极简墙风格）
  const addBtn = el("div", "add-user");
  addBtn.append(el("span", "plus", "＋"), el("span", "", "新建用户"));
  addBtn.onclick = () => openUserModal();
  grid.append(addBtn);
  wall.append(grid);
  // 底部操作：编辑胶囊（点空白处退出见 boot 委托）
  const actions = el("div", "user-actions");
  const editAllBtn = el("button", "btn ghost edit-all-btn", "编辑");
  editAllBtn.onclick = (e) => { e.stopPropagation(); setUserEditMode(!userEditMode); };
  actions.append(editAllBtn);
  wall.append(actions);
  wall.append(el("div", "user-foot", "LOCAL JSON · 数据隔离"));
  v.append(wall);
  // 渲染后恢复编辑模式（删除用户后 render 重建时保持）
  if (userEditMode) setUserEditMode(true);
}
/** 密码验证弹窗（复用 openPassModal 样式）：返回 token 或 null */
function promptUserPassword(u, title) {
  return new Promise((resolve) => {
    const root = $("#modal-root");
    const m = el("div", "mask");
    const modal = el("div", "modal");
    modal.append(el("h3", "", title));
    const inp = el("input"); inp.type = "password"; inp.placeholder = `${u.name} 的密码`;
    modal.append(el("label", "", "密码"), inp);
    const row = el("div", "form-row");
    const ok = el("button", "btn primary", "验证"); ok.style.flex = "1";
    const cancel = el("button", "btn ghost", "取消");
    row.append(ok, cancel); modal.append(row);
    ok.onclick = guard(ok, async () => {
      const r = await api("/api/session", { method: "POST", json: { id: u.id, password: inp.value } }).catch(e2 => errToast(e2.message));
      if (r) { m.remove(); resolve(r.token); }
    });
    cancel.onclick = () => { m.remove(); resolve(null); };
    inp.onkeydown = e => { if (e.key === "Enter") ok.click(); };
    m.append(modal); root.append(m); inp.focus();
  });
}
async function enterUser(u) {
  if (u.hasPass) { openPassModal(u); return; }
  const r = await api("/api/session", { method: "POST", json: { id: u.id, password: "" } }).catch(e => { errToast(e.message); return null; });
  if (!r) return;
  state.current = { ...r.user, token: r.token };
  LS.set("cur", { id: r.user.id, token: r.token }); // v0.6.13 治本：仅存会话凭据——展示信息（name/color）以后端为唯一权威源，恢复登录时拉取
  resetFilter(); // 第三轮 F-2
  await loadClips(); render();
}
function openPassModal(u) {
  const root = $("#modal-root");
  root.innerHTML = "";
  const m = el("div", "mask");
  const modal = el("div", "modal");
  modal.append(el("h3", "", `输入 ${u.name} 的密码`));
  const inp = el("input"); inp.type = "password"; inp.placeholder = "密码";
  modal.append(el("label", "", "密码"), inp);
  const row = el("div", "form-row");
  const ok = el("button", "btn primary", "进入"); ok.style.flex = "1";
  const cancel = el("button", "btn ghost", "取消");
  row.append(ok, cancel); modal.append(row);
  ok.onclick = guard(ok, async () => {
    const r = await api("/api/session", { method: "POST", json: { id: u.id, password: inp.value } }).catch(e => { errToast(e.message); return null; });
    if (!r) return;
    state.current = { ...r.user, token: r.token };
    LS.set("cur", { id: r.user.id, token: r.token }); // v0.6.13 治本：仅存会话凭据——展示信息（name/color）以后端为唯一权威源，恢复登录时拉取
    resetFilter(); // 第三轮 F-2
    m.remove(); await loadClips(); render();
  });
  cancel.onclick = () => m.remove();
  inp.onkeydown = e => { if (e.key === "Enter") ok.click(); };
  m.append(modal); root.append(m); inp.focus();
}
function openUserModal() {
  const root = $("#modal-root");
  root.innerHTML = "";
  const m = el("div", "mask");
  const modal = el("div", "modal");
  modal.append(el("h3", "", "新建用户"));
  // v0.6.13 双名模型：账号名=身份键（创建后不可改，登录/WebDAV 迁移识别用）；显示名=展示名（可随时改）
  const acct = el("input"); acct.placeholder = "账号名（仅英文数字，创建后不可修改，跨设备迁移用）";
  const disp = el("input"); disp.placeholder = "显示名（可留空=账号名，可随时修改）";
  const pass = el("input"); pass.type = "password"; pass.placeholder = "密码（可留空）";
  modal.append(el("label", "", "账号名"), acct, el("label", "", "显示名"), disp, el("label", "", "密码（可选）"), pass);
  const row = el("div", "form-row");
  const ok = el("button", "btn primary", "创建并进入"); ok.style.flex = "1";
  const cancel = el("button", "btn ghost", "取消");
  row.append(ok, cancel); modal.append(row);
  ok.onclick = guard(ok, async () => {
    const r = await api("/api/users", { method: "POST", json: { name: acct.value, displayName: disp.value, password: pass.value } }).catch(e => { errToast(e.message); return null; });
    if (!r) return;
    state.current = { ...r.user, token: r.token };
    LS.set("cur", { id: r.user.id, token: r.token }); // v0.6.13 治本：仅存会话凭据——展示信息（name/color）以后端为唯一权威源，恢复登录时拉取
    resetFilter(); // 第三轮 F-2
    m.remove(); await loadClips(); render();
  });
  cancel.onclick = () => m.remove();
  acct.onkeydown = e => { if (e.key === "Enter") ok.click(); };
  m.append(modal); root.append(m); acct.focus();
}

// ---------- 数据加载 ----------
async function loadClips() {
  // v0.6.14 单轨化：过滤统一走前端 renderList（搜索/标签/类型/拼音全本地筛，零网络请求），
  // 这里恒拉全量（仅 archived 影响数据源范围）。此前 q/tag 也走后端过滤并覆盖 state.clips，
  // 与 renderList 前端过滤双轨并存——搜索词非空时发生增删改 → state.clips 被替换成过滤集，
  // 清空搜索词后全量从界面"消失"（须刷新页面恢复）。修复：loadClips 只负责拉数据，不过滤。
  const params = [];
  if (state.filter.archived) params.push("archived=1"); // 含归档查询（滚动归档 v0.2.0）
  const [a, b] = await Promise.all([
    api("/api/clips" + (params.length ? "?" + params.join("&") : "")),
    api("/api/tags"),
  ]);
  state.clips = a.clips; state.tags = b.tags;
  // v0.6.13: 标签筛选下全量已无该标签（该标签最后一张被编辑取消/删除）→ 自动清除筛选回到全部，
  // 避免"内容全部消失"的假象；有搜索词时不干预（空是搜索无结果，不是标签问题）
  if (state.filter.tag && !state.filter.q && !state.clips.some((c) => (c.tags || []).includes(state.filter.tag))) {
    const gone = state.filter.tag;
    state.filter.tag = "";
    flash("「" + gone + "」下已无内容，已回到全部");
  }
}
async function loadUsers() {
  const r = await api("/api/users");
  state.users = r.users;
}

// ---------- 标签选择器（交互改造：系统已有标签 chips 点选 + 输入框新建） ----------
function renderTagPicker(container, selected, allTags, onChange) {
  const render = () => {
    container.replaceChildren();
    const box = el("div", "tag-pick");
    // 展示列表 = 已有标签 ∪ 选中的新标签（新建后立即出现在选择区，保存后后端提取、刷新后仍在）
    const merged = allTags.map(t => t.tag);
    for (const s of selected) if (!merged.includes(s)) merged.push(s);
    for (const name of merged) {
      const chip = el("span", "tag" + (selected.includes(name) ? " on" : ""), name);
      chip.onclick = () => {
        const i = selected.indexOf(name);
        if (i >= 0) selected.splice(i, 1); else selected.push(name);
        onChange([...selected]);
        render(); // 立即重渲染，刷新选中高亮（修复"点击无选取反馈"）
      };
      box.append(chip);
    }
    const input = el("input"); input.placeholder = "新标签，回车添加"; input.maxLength = 20;
    input.onkeydown = (e) => {
      if (e.key === "Enter" && input.value.trim()) {
        const t = input.value.trim();
        if (!selected.includes(t)) { selected.push(t); onChange([...selected]); }
        input.value = "";
        render();
      }
    };
    box.append(input);
    container.append(box);
  };
  render();
}

// ---------- 主页面（v0.6.4 · 双行工具栏版式） ----------
function renderMain() {
  const v = $("#view");
  v.replaceChildren(); // 修复 U-7：renderMain 自清空——保存后直接调用时不再叠出第二套界面（用户看到 2 个输入框的根因）
  // 顶栏（v0.6.4：一键同步移入工具行 ops，顶栏更轻）
  const tb = el("div", "topbar");
  tb.append(el("span", "t-logo", "📋"), el("h1", "", "剪贴板"));
  const who = el("div", "who");
  const dot = el("span", "dot"); dot.style.background = state.current.color;
  // v0.6.13：一键切换无饱和度配色（localStorage 记忆，html.mono class 驱动 CSS 变量灰化）
  const monoBtn = el("button", "btn sm ghost", "◐");
  monoBtn.title = document.documentElement.classList.contains("mono") ? "当前：无饱和度配色 · 点击恢复彩色" : "切换为无饱和度（灰度）配色";
  monoBtn.onclick = () => {
    const on = document.documentElement.classList.toggle("mono");
    LS.set("mono", on ? "1" : "");
    monoBtn.title = on ? "当前：无饱和度配色 · 点击恢复彩色" : "切换为无饱和度（灰度）配色";
    flash(on ? "已切换为无饱和度配色" : "已恢复彩色配色");
  };
  const pwBtn = el("button", "btn sm ghost", "密码");
  const dataBtn = el("button", "btn sm ghost", "数据管理");
  const logoutBtn = el("button", "btn sm ghost", "退出");
  // v0.4.2：设置拆为「密码」+「数据管理」两个入口
  pwBtn.onclick = () => openPasswordModal();
  dataBtn.onclick = () => openDataModal();
  // v0.4.5：补 guard 防连点（逻辑核验 P2-1）；v0.6.13 走查 P-A：不再吞错——服务端会话销毁失败要提示，
  // 避免"假退出"（本地清了但服务端会话还在，重启后 token 复活）
  logoutBtn.onclick = guard(logoutBtn, async () => {
    const r = await api("/api/session", { method: "DELETE" }).catch(e => { errToast("退出失败：" + e.message); return null; });
    if (r === null) return; // 服务端销毁失败：保留本地登录态，让用户重试
    LS.del("cur"); state.current = null; userEditMode = false;
    await loadUsers(); render();
  });
  who.append(dot, el("span", "", state.current.name), monoBtn, pwBtn, dataBtn, logoutBtn);
  tb.append(who); v.append(tb);

  // 工具条（v0.6.4 双行：行1 搜索+存入 / 行2 类型+标签+操作）
  const tool = el("div", "tb");
  // —— 行1：搜索 + 存入小按钮（右置，点开大弹窗）
  const row1 = el("div", "row1");
  const search = el("input"); search.type = "search"; search.className = "search";
  search.placeholder = "搜索内容 / 标题 / 标签…（空格定位光标）"; search.value = state.filter.q;
  search.title = "搜索内容 / 标题 / 标签 — 主页面按「空格」快速定位光标到此";
  // 一边输入一边筛选：本地即时过滤（100ms 微防抖只防极速输入时的 DOM 重建，无网络请求）
  let searchTimer = null;
  search.oninput = () => {
    clearTimeout(searchTimer);
    searchTimer = setTimeout(() => {
      state.filter.q = search.value;
      renderList();
    }, 100);
  };
  const storeBtn = el("button", "store-btn", "");
  storeBtn.append(el("span", "plus", "＋"), el("span", "", "存入"));
  storeBtn.title = "存入内容 — 粘贴 / 拖文件 / 按 Ctrl+V 打开（自动填入剪贴板）";
  storeBtn.onclick = () => openPasteModal(); // v0.6.4：存入入口收缩为右置小按钮
  row1.append(search, storeBtn);
  tool.append(row1);
  // —— 行2：类型分段 + 标签栏 + 右侧操作（含归档 / 列数 / 一键同步）
  const row2 = el("div", "row2");
  // ⑥ 类型分类 Tab：全部 / 文本 / 链接 / 文件（前端过滤，数据量小无需后端）
  const TYPE_TABS = [["all", "全部"], ["text", "文本"], ["link", "链接"], ["file", "文件"]];
  const typetab = el("div", "typetab");
  const renderTypeTab = () => {
    typetab.querySelectorAll(".tt").forEach((n, i) => n.classList.toggle("on", TYPE_TABS[i][0] === state.filter.type));
  };
  for (const [v, l] of TYPE_TABS) {
    const tt = el("span", "tt" + (state.filter.type === v ? " on" : ""), l);
    tt.onclick = () => { state.filter.type = v; renderTypeTab(); renderList(); };
    typetab.append(tt);
  }
  row2.append(typetab);
  // 标签栏容器（renderTagbar 渲染到此，v0.6.4）
  const tagbarWrap = el("div", "tagbar-wrap");
  row2.append(tagbarWrap);
  // 右侧操作
  const ops = el("div", "ops");
  // 批量编辑入口（v0.6.15）：进入/退出多选模式——全选当前页 / 批量删除 / 批量加标签 / 批量减标签
  const editBtn = el("button", "btn sm ghost batch-edit-btn", "编辑");
  editBtn.title = "批量编辑：多选条目，批量删除 / 加标签 / 减标签";
  editBtn.onclick = () => setBatchMode(!batchMode);
  // 含归档开关：归档条目是只读历史（滚动归档 v0.2.0），勾选才从后端拉取合并
  const archLbl = el("label", "opt"); archLbl.title = "含归档：查看历史归档条目（归档参与 WebDAV 同步）";
  const archChk = el("input"); archChk.type = "checkbox"; archChk.checked = !!state.filter.archived;
  // v0.4.5：补 guard 防连点（逻辑核验 P2-1——快速切换勾选避免并发加载竞态）
  archChk.onchange = guard(archChk, async () => {
    state.filter.archived = archChk.checked;
    await loadClips(); renderTagbar(); renderList();
  });
  archLbl.append(archChk, "含归档");
  // 列数选择（1~4 列或自适应，记住偏好）
  const colsSel = el("select");
  colsSel.title = "每行显示列数";
  for (const [v, l] of [["auto", "自适应"], ["1", "1 列"], ["2", "2 列"], ["3", "3 列"], ["4", "4 列"]]) {
    const o = el("option", "", l); o.value = v; colsSel.append(o);
  }
  colsSel.value = state.cols;
  colsSel.onchange = () => {
    state.cols = colsSel.value;
    LS.set("cols", state.cols);
    renderList(); // 仅重渲染列表，不动其他区域
  };
  // 一键同步（v0.4.2 顶栏 → v0.6.4 移入工具行）
  const syncBtn = el("button", "btn sm ghost", "↻ 同步");
  syncBtn.title = "WebDAV 一键同步";
  syncBtn.onclick = guard(syncBtn, async () => {
    try {
      const r = await api("/api/sync/run", { method: "POST" });
      await loadClips(); renderTagbar(); renderList();
      flash("同步完成：远端" + (r.remoteExisted ? "有备份" : "无备份") + (r.uploaded ? " · 已上传" : " · 本地空跳过上传"));
    } catch (e) { errToast(e.message); }
  });
  ops.append(editBtn, archLbl, colsSel, syncBtn);
  row2.append(ops);
  tool.append(row2);
  v.append(tool);

  renderTagbar(tagbarWrap);
  renderList(v);
}

/** 标签栏：渲染到指定容器（v0.6.4 起渲染进 .tagbar-wrap；无参时自动查找该容器，兜底 #view） */
function renderTagbar(container) {
  if (!container) container = $(".tagbar-wrap") || $("#view");
  const old = $(".tagbar", container); if (old) old.remove();
  const bar = el("div", "tagbar");
  const all = el("span", "tag" + (state.filter.tag ? " off" : " on"), "全部");
  all.onclick = () => { state.filter.tag = ""; renderTagbar(); renderList(); }; // 前端即时过滤
  bar.append(all);
  for (const t of state.tags) {
    const c = el("span", "tag" + (state.filter.tag === t.tag ? " on" : ""), t.tag + " · " + t.count);
    c.onclick = () => { state.filter.tag = state.filter.tag === t.tag ? "" : t.tag; renderTagbar(); renderList(); };
    bar.append(c);
  }
  // v0.4.2：标签管理入口——标签行最右侧的「管理」按钮，点击弹出独立管理窗口
  const mgmtBtn = el("button", "btn sm ghost tag-mgmt-btn", "管理");
  mgmtBtn.title = "标签管理（重命名 / 删除）";
  mgmtBtn.style.marginLeft = "auto"; // 推到最右
  mgmtBtn.onclick = () => openTagManageModal();
  bar.append(mgmtBtn);
  container.append(bar);
}

// ---------- 标签管理弹窗（v0.4.2：独立窗口，由标签栏「管理」按钮打开） ----------
// 重命名 / 删除跨活跃区+归档全部条目生效；操作后刷新标签栏与列表
function openTagManageModal() {
  const root = $("#modal-root");
  root.innerHTML = "";
  const m = el("div", "mask");
  const modal = el("div", "modal");
  modal.append(el("h3", "", "标签管理"));
  const list = el("div");
  const refresh = async () => {
    list.replaceChildren();
    const tags = (await api("/api/tags").catch(() => ({ tags: [] }))).tags;
    if (!tags.length) { list.append(el("div", "empty", "还没有标签")); return; }
    for (const t of tags) {
      const row = el("div", "tag-mgmt-row");
      row.style.cssText = "display:flex;gap:6px;align-items:center;margin-bottom:6px";
      const nm = el("span", "tag", t.tag + " · " + t.count);
      nm.style.flex = "1"; nm.style.overflow = "hidden"; nm.style.textOverflow = "ellipsis";
      const rn = el("button", "btn sm ghost", "改名");
      rn.onclick = () => {
        const inp = el("input"); inp.value = t.tag; inp.maxLength = 20;
        row.replaceChildren(inp);
        inp.onkeydown = async (e) => {
          if (e.key !== "Enter" || !inp.value.trim() || inp.value.trim() === t.tag) { if (e.key === "Enter") refresh(); return; }
          const r = await api("/api/tags/" + encodeURIComponent(t.tag), { method: "PUT", json: { name: inp.value.trim() } }).catch(e2 => errToast(e2.message));
          if (r) { flash("已改名（" + r.affected + " 条）"); refresh(); refreshList(); }
        };
        inp.onblur = () => refresh();
        inp.focus();
      };
      const del = el("button", "btn sm ghost danger", "删除");
      del.onclick = () => {
        askConfirm("删除标签「" + t.tag + "」？相关条目不会删除，仅移除该标签。", guard(del, async () => {
          const r = await api("/api/tags/" + encodeURIComponent(t.tag), { method: "DELETE" }).catch(e2 => errToast(e2.message));
          if (r) { flash("已删除（" + r.affected + " 条受影响）"); refresh(); refreshList(); }
        }), "删除标签");
      };
      row.append(nm, rn, del);
      list.append(row);
    }
  };
  modal.append(list);
  const row = el("div", "form-row");
  const close = el("button", "btn ghost", "关闭"); close.style.flex = "1";
  row.append(close); modal.append(row);
  close.onclick = () => m.remove();
  m.append(modal); root.append(m);
  refresh();
}
/** 删除/编辑等低频操作后重载（与后端排序/计数保持一致；高频过滤走前端 renderList） */
async function refreshList() { await loadClips(); renderTagbar(); renderList($("#view")); }

/** 拼音首字母缩写搜索（零依赖）：GB2312 一级汉字 3755 字 → 首字母映射表（pypinyin 生成）。
 *  例："身份"→"sf"；搜索词 "sf" 可命中标题/标签含"身份"的条目。多音字取常用读音，近似即可。 */
const PY_GROUPS = {"A":"啊阿埃挨哎唉哀皑癌蔼矮艾碍爱隘鞍氨安俺按暗岸胺案肮昂盎凹敖熬翱袄傲奥懊澳","B":"芭捌扒叭吧笆八疤巴拔跋靶把耙坝霸罢爸白柏百摆佰败拜稗斑班搬扳般颁板版扮拌伴瓣半办绊邦帮梆榜膀绑棒磅蚌镑傍谤苞胞包褒剥薄雹保堡饱宝抱报暴豹鲍爆杯碑悲卑北辈背贝钡倍狈备惫焙被奔苯本笨崩绷甭泵蹦迸逼鼻比鄙笔彼碧蓖蔽毕毙毖币庇痹闭敝弊必壁臂避陛鞭边编贬扁便变卞辨辩辫遍标彪膘表鳖憋别瘪彬斌濒滨宾摈兵冰柄丙秉饼炳病并玻菠播拨钵波博勃搏铂箔伯帛舶脖膊渤驳捕卜哺补埠不布步簿部怖","C":"擦猜裁材才财睬踩采彩菜蔡餐参蚕残惭惨灿苍舱仓沧藏操糙槽曹草厕策侧册测层蹭插叉茬茶查碴搽察岔差诧拆柴豺搀掺蝉馋谗缠铲产阐颤昌猖场尝常偿肠厂敞畅唱倡超抄钞朝嘲潮巢吵炒车扯撤掣彻澈郴臣辰尘晨忱沉陈趁衬撑称城橙成呈乘程惩澄诚承逞骋秤吃痴持池迟弛驰耻齿侈尺赤翅斥炽充冲虫崇宠抽酬畴踌稠愁筹仇绸瞅丑臭初出橱厨躇锄雏滁除楚础储矗搐触处揣川穿椽传船喘串疮窗幢床闯创吹炊捶锤垂春椿醇唇淳纯蠢戳绰疵茨磁雌辞慈瓷词此刺赐次聪葱囱匆从丛凑粗醋簇促蹿篡窜摧崔催脆瘁粹淬翠村存寸磋撮搓措挫错伺畜曾椎","D":"搭达答瘩打大呆歹傣戴带殆代贷袋待逮怠耽担丹单郸掸胆旦氮但惮淡诞弹蛋当挡党荡档刀捣蹈倒岛祷导到稻悼道盗德得的蹬灯登等瞪凳邓堤低滴迪敌笛狄涤翟嫡抵底地蒂第帝弟递缔颠掂滇碘点典靛垫电佃甸店惦奠淀殿碉叼雕凋刁掉吊钓调跌爹碟蝶迭谍叠丁盯叮钉顶鼎锭定订丢东冬董懂动栋侗恫冻洞兜抖斗陡豆逗痘都督毒犊独读堵睹赌杜镀肚度渡妒端短锻段断缎堆兑队对墩吨蹲敦顿囤钝盾遁掇哆多夺垛躲朵跺舵剁惰堕","E":"蛾峨鹅俄额讹娥恶厄扼遏鄂饿恩而儿耳尔饵洱二贰","F":"发罚筏伐乏阀法珐藩帆番翻樊矾钒繁凡烦反返范贩犯饭泛坊芳方肪房防妨仿访纺放菲非啡飞肥匪诽吠肺废沸费芬酚吩氛分纷坟焚汾粉奋份忿愤粪丰封枫蜂峰锋风疯烽逢冯缝讽奉凤佛否夫敷肤孵扶拂辐幅氟符伏俘服浮涪福袱弗甫抚辅俯釜斧腑府腐赴副覆赋复傅付阜父腹负富讣附妇缚咐","G":"噶嘎该改概钙盖溉干甘杆柑竿肝赶感秆敢赣冈刚钢缸肛纲岗港杠篙皋高膏羔糕搞镐稿告哥歌搁戈鸽胳疙割革葛格阁隔铬个各给根跟耕更庚羹埂耿梗工攻功恭龚供躬公宫弓巩汞拱贡共钩勾沟苟狗垢构购够辜菇咕箍估沽孤姑鼓古蛊骨谷股故顾固雇刮瓜剐寡挂褂乖拐怪棺关官冠观管馆罐惯灌贯光广逛瑰规圭硅归龟闺轨鬼诡癸桂柜跪贵刽辊滚棍锅郭国果裹过咯傀炔","H":"蛤哈骸孩海氦亥害骇酣憨邯韩含涵寒函喊罕翰撼捍旱憾悍焊汗汉夯杭航壕嚎豪毫郝好耗号浩呵喝荷菏核禾和何合盒貉阂河涸赫褐鹤贺嘿黑痕很狠恨哼亨横衡恒轰哄烘虹鸿洪宏弘红喉侯猴吼厚候后呼乎忽瑚壶葫胡蝴狐糊湖弧虎唬护互沪户花哗华猾滑画划化话槐徊怀淮坏欢环桓还缓换患唤痪豢焕涣宦幻荒慌黄磺蝗簧皇凰惶煌晃幌恍谎灰挥辉徽恢蛔回毁悔慧卉惠晦贿秽会烩汇讳诲绘荤昏婚魂浑混豁活伙火获或惑霍货祸","J":"击圾基机畸稽积箕肌饥迹激讥鸡姬绩缉吉极棘辑籍集及急疾汲即嫉级挤几脊己蓟技冀季伎祭剂悸济寄寂计记既忌际妓继纪嘉枷夹佳家加荚颊贾甲钾假稼价架驾嫁歼监坚尖笺间煎兼肩艰奸缄茧检柬碱硷拣捡简俭剪减荐鉴践贱见键箭件健舰剑饯渐溅涧建僵姜将浆江疆蒋桨奖讲匠酱降蕉椒礁焦胶交郊浇骄娇嚼搅铰矫侥脚狡角饺缴绞剿教酵轿较叫窖揭接皆秸街阶截劫节桔杰捷睫竭洁结解姐戒藉芥界借介疥诫届巾筋斤金今津襟紧锦仅谨进靳晋禁近烬浸尽劲荆兢茎睛晶鲸京惊精粳经井警景颈静境敬镜径痉靖竟竞净炯窘揪究纠玖韭久灸九酒厩救旧臼舅咎就疚鞠拘狙疽居驹菊局咀矩举沮聚拒据巨具距踞锯俱句惧炬剧捐鹃娟倦眷卷绢撅攫抉掘倔爵觉决诀绝均菌钧军君峻俊竣浚郡骏茄","K":"槛喀咖卡开揩楷凯慨刊堪勘坎砍看康慷糠扛抗亢炕考拷烤靠坷苛柯棵磕颗科壳咳可渴克刻客课肯啃垦恳坑吭空恐孔控抠口扣寇枯哭窟苦酷库裤夸垮挎跨胯块筷侩快宽款匡筐狂框矿眶旷况亏盔岿窥葵奎魁馈愧溃坤昆捆困括扩廓阔","L":"垃拉喇蜡腊辣啦莱来赖蓝婪栏拦篮阑兰澜谰揽览懒缆烂滥琅榔狼廊郎朗浪捞劳牢老佬姥酪烙涝勒乐雷镭蕾磊累儡垒擂肋类泪棱楞冷厘梨犁黎篱狸离漓理李里鲤礼莉荔吏栗丽厉励砾历利傈例俐痢立粒沥隶力璃哩俩联莲连镰廉怜涟帘敛脸链恋炼练粮凉梁粱良两辆量晾亮谅撩聊僚疗燎寥辽潦了撂镣廖料列裂烈劣猎琳林磷霖临邻鳞淋凛赁吝拎玲菱零龄铃伶羚凌灵陵岭领另令溜琉榴硫馏留刘瘤流柳六龙聋咙笼窿隆垄拢陇楼娄搂篓漏陋芦卢颅庐炉掳卤虏鲁麓碌露路赂鹿潞禄录陆戮驴吕铝侣旅履屡缕虑氯律率滤绿峦挛孪滦卵乱掠略抡轮伦仑沦纶论萝螺罗逻锣箩骡裸落洛骆络","M":"妈麻玛码蚂马骂嘛吗埋买麦卖迈脉瞒馒蛮满蔓曼慢漫谩芒茫盲氓忙莽猫茅锚毛矛铆卯茂冒帽貌贸么玫枚梅酶霉煤没眉媒镁每美昧寐妹媚门闷们萌蒙檬盟锰猛梦孟眯醚靡糜迷谜弥米秘觅泌蜜密幂棉眠绵冕免勉娩缅面苗描瞄藐秒渺庙妙蔑灭民抿皿敏悯闽明螟鸣铭名命谬摸摹蘑模膜磨摩魔抹末莫墨默沫漠寞陌谋牟某拇牡亩姆母墓暮幕募慕木目睦牧穆","N":"拿哪呐钠那娜纳氖乃奶耐奈南男难囊挠脑恼闹淖呢馁内嫩能妮霓倪泥尼拟你匿腻逆溺蔫拈年碾撵捻念娘酿鸟尿捏聂孽啮镊镍涅您柠狞凝宁拧泞牛扭钮纽脓浓农弄奴努怒女暖虐疟挪懦糯诺辗","O":"哦欧鸥殴藕呕偶沤","P":"辟泊脯啪趴爬帕怕琶拍排牌徘湃派攀潘盘磐盼畔判叛乓庞旁耪胖抛咆刨炮袍跑泡呸胚培裴赔陪配佩沛喷盆砰抨烹澎彭蓬棚硼篷膨朋鹏捧碰坯砒霹批披劈琵毗啤脾疲皮匹痞僻屁譬篇偏片骗飘漂瓢票撇瞥拼频贫品聘乒坪苹萍平凭瓶评屏坡泼颇婆破魄迫粕剖扑铺仆莆葡菩蒲埔朴圃普浦谱曝瀑","Q":"期欺栖戚妻七凄漆柒沏其棋奇歧畦崎脐齐旗祈祁骑起岂乞企启契砌器气迄弃汽泣讫掐恰洽牵扦钎铅千迁签仟谦乾黔钱钳前潜遣浅谴堑嵌欠歉枪呛腔羌墙蔷强抢橇锹敲悄桥瞧乔侨巧鞘撬翘峭俏窍切且怯窃钦侵亲秦琴勤芹擒禽寝沁青轻氢倾卿清擎晴氰情顷请庆琼穷秋丘邱球求囚酋泅趋区蛆曲躯屈驱渠取娶龋趣去圈颧权醛泉全痊拳犬券劝缺瘸却鹊榷确雀裙群","R":"然燃冉染瓤壤攘嚷让饶扰绕惹热壬仁人忍韧任认刃妊纫扔仍日戎茸蓉荣融熔溶容绒冗揉柔肉茹蠕儒孺如辱乳汝入褥软阮蕊瑞锐闰润若弱","S":"匙撒洒萨腮鳃塞赛三叁伞散桑嗓丧搔骚扫嫂瑟色涩森僧莎砂杀刹沙纱傻啥煞筛晒珊苫杉山删煽衫闪陕擅赡膳善汕扇缮墒伤商赏晌上尚裳梢捎稍烧芍勺韶少哨邵绍奢赊蛇舌舍赦摄射慑涉社设砷申呻伸身深娠绅神沈审婶甚肾慎渗声生甥牲升绳省盛剩胜圣师失狮施湿诗尸虱十石拾时什食蚀实识史矢使屎驶始式示士世柿事拭誓逝势是嗜噬适仕侍释饰氏市恃室视试收手首守寿授售受瘦兽蔬枢梳殊抒输叔舒淑疏书赎孰熟薯暑曙署蜀黍鼠属术述树束戍竖墅庶数漱恕刷耍摔衰甩帅栓拴霜双爽谁水睡税吮瞬顺舜说硕朔烁斯撕嘶思私司丝死肆寺嗣四似饲巳松耸怂颂送宋讼诵搜艘擞嗽苏酥俗素速粟僳塑溯宿诉肃酸蒜算虽隋随绥髓碎岁穗遂隧祟孙损笋蓑梭唆缩琐索锁所厦","T":"塌他它她塔獭挞蹋踏胎苔抬台泰酞太态汰坍摊贪瘫滩坛檀痰潭谭谈坦毯袒碳探叹炭汤塘搪堂棠膛唐糖倘躺淌趟烫掏涛滔绦萄桃逃淘陶讨套特藤腾疼誊梯剔踢锑提题蹄啼体替嚏惕涕剃屉天添填田甜恬舔腆挑条迢眺跳贴铁帖厅听烃汀廷停亭庭挺艇通桐酮瞳同铜彤童桶捅筒统痛偷投头透凸秃突图徒途涂屠土吐兔湍团推颓腿蜕褪退吞屯臀拖托脱鸵陀驮驼椭妥拓唾","W":"挖哇蛙洼娃瓦袜歪外豌弯湾玩顽丸烷完碗挽晚皖惋宛婉万腕汪王亡枉网往旺望忘妄威巍微危韦违桅围唯惟为潍维苇萎委伟伪尾纬未蔚味畏胃喂魏位渭谓尉慰卫瘟温蚊文闻纹吻稳紊问嗡翁瓮挝蜗涡窝我斡卧握沃巫呜钨乌污诬屋无芜梧吾吴毋武五捂午舞伍侮坞戊雾晤物勿务悟误","X":"昔熙析西硒矽晰嘻吸锡牺稀息希悉膝夕惜熄烯溪汐犀檄袭席习媳喜铣洗系隙戏细瞎虾匣霞辖暇峡侠狭下夏吓掀锨先仙鲜纤咸贤衔舷闲涎弦嫌显险现献县腺馅羡宪陷限线相厢镶香箱襄湘乡翔祥详想响享项巷橡像向象萧硝霄削哮嚣销消宵淆晓小孝校肖啸笑效楔些歇蝎鞋协挟携邪斜胁谐写械卸蟹懈泄泻谢屑薪芯锌欣辛新忻心信衅星腥猩惺兴刑型形邢行醒幸杏性姓兄凶胸匈汹雄熊休修羞朽嗅锈秀袖绣墟戌需虚嘘须徐许蓄酗叙旭序恤絮婿绪续轩喧宣悬旋玄选癣眩绚靴薛学穴雪血勋熏循旬询寻驯巡殉汛训讯逊迅吁","Y":"压押鸦鸭呀丫芽牙蚜崖衙涯雅哑亚讶焉咽阉烟淹盐严研蜒岩延言颜阎炎沿奄掩眼衍演艳堰燕厌砚雁唁彦焰宴谚验殃央鸯秧杨扬佯疡羊洋阳氧仰痒养样漾邀腰妖瑶摇尧遥窑谣姚咬舀药要耀椰噎耶爷野冶也页掖业叶曳腋夜液一壹医揖铱依伊衣颐夷遗移仪胰疑沂宜姨彝椅蚁倚已乙矣以艺抑易邑屹亿役臆逸肄疫亦裔意毅忆义益溢诣议谊译异翼翌绎茵荫因殷音阴姻吟银淫寅饮尹引隐印英樱婴鹰应缨莹萤营荧蝇迎赢盈影颖硬映哟拥佣臃痈庸雍踊蛹咏泳涌永恿勇用幽优悠忧尤由邮铀犹油游酉有友右佑釉诱又幼迂淤于盂榆虞愚舆余俞逾鱼愉渝渔隅予娱雨与屿禹宇语羽玉域芋郁遇喻峪御愈欲狱育誉浴寓裕预豫驭鸳渊冤元垣袁原援辕园员圆猿源缘远苑愿怨院曰约越跃钥岳粤月悦阅耘云郧匀陨允运蕴酝晕韵孕轧","Z":"长匝砸杂栽哉灾宰载再在咱攒暂赞赃脏葬遭糟凿藻枣早澡蚤躁噪造皂灶燥责择则泽贼怎增憎赠扎喳渣札铡闸眨栅榨咋乍炸诈摘斋宅窄债寨瞻毡詹粘沾盏斩崭展蘸栈占战站湛绽樟章彰漳张掌涨杖丈帐账仗胀瘴障招昭找沼赵照罩兆肇召遮折哲蛰辙者锗蔗这浙珍斟真甄砧臻贞针侦枕疹诊震振镇阵蒸挣睁征狰争怔整拯正政帧症郑证芝枝支吱蜘知肢脂汁之织职直植殖执值侄址指止趾只旨纸志挚掷至致置帜峙制智秩稚质炙痔滞治窒中盅忠钟衷终种肿重仲众舟周州洲诌粥轴肘帚咒皱宙昼骤珠株蛛朱猪诸诛逐竹烛煮拄瞩嘱主著柱助蛀贮铸筑住注祝驻抓爪拽专砖转撰赚篆桩庄装妆撞壮状锥追赘坠缀谆准捉拙卓桌琢茁酌啄着灼浊兹咨资姿滋淄孜紫仔籽滓子自渍字鬃棕踪宗综总纵邹走奏揍租足卒族祖诅阻组钻纂嘴醉最罪尊遵昨左佐柞做作坐座"};
const PY_MAP = new Map();
for (const [letter, chars] of Object.entries(PY_GROUPS)) for (const ch of chars) PY_MAP.set(ch, letter);
function pyInitial(ch) { return PY_MAP.get(String(ch)) || ""; }
function strToPy(s) { let r = ""; for (const ch of String(s || "")) r += pyInitial(ch); return r; }

/** 前端过滤（搜索/标签/类型全本地筛——数据量小、零网络请求；删除/编辑后仍走 loadClips 保证与后端一致）。
 *  抽独立纯函数：renderList 与批量「全选当前页」共用同一份可见集定义。 */
function getVisibleClips() {
  const kw = state.filter.q.trim().toLowerCase();
  const tg = state.filter.tag;
  const ty = state.filter.type;
  let filtered = state.clips;
  if (ty !== "all") filtered = filtered.filter(c => c.type === ty);
  if (tg) filtered = filtered.filter(c => (c.tags || []).includes(tg));
  if (kw) {
    filtered = filtered.filter(c => {
      const title = c.title || "";
      const content = c.content || "";
      const url = c.url || "";
      const tags = c.tags || [];
      if (title.toLowerCase().includes(kw) || content.toLowerCase().includes(kw) ||
          url.toLowerCase().includes(kw) || tags.some(t => t.toLowerCase().includes(kw))) return true;
      // 拼音首字母缩写匹配（标题+标签）：如 "sf" → 身份
      const py = (strToPy(title) + " " + strToPy(tags.join(" "))).toLowerCase();
      return py.includes(kw);
    });
  }
  return filtered;
}

/** 前端即时过滤（搜索/标签/类型全本地筛——数据量小、零网络请求；删除/编辑后仍走 loadClips 保证与后端一致） */
function renderList(v = $("#view")) {
  const old = $(".list", v); if (old) old.remove();
  const list = el("div", "list");
  list.dataset.cols = state.cols; // 列数选择（1~4 或 auto）
  const kw = state.filter.q.trim().toLowerCase();
  const tg = state.filter.tag;
  const ty = state.filter.type;
  const filtered = getVisibleClips();
  if (!filtered.length) {
    // UI 走查 U-2：区分"没有条目"与"搜索/过滤无结果"，避免误导
    const msg = (kw || tg || ty !== "all")
      ? "没有匹配的内容 — 试试调整搜索词、标签或类型"
      : "还没有内容 — 顶部粘贴框 Ctrl+V 即存，或拖文件进来";
    list.append(el("div", "empty", msg));
  }
  for (const c of filtered) list.append(clipCard(c));
  v.append(list);
  renderBatchBar(); // v0.6.15：批量操作条（悬浮底部，仅批量模式渲染）
}

// ---------- 批量编辑（v0.6.15：编辑按钮进入多选——全选当前页 / 批量删除 / 批量加标签 / 批量减标签） ----------
/** 进入/退出批量编辑模式。进入：清空选择 + 重建列表（卡片带勾选层）；退出：清选择 + 收条。 */
function setBatchMode(on) {
  batchMode = !!on;
  if (!batchMode) batchSel.clear();
  const btn = $(".batch-edit-btn");
  if (btn) { btn.textContent = on ? "完成" : "编辑"; btn.classList.toggle("on", on); }
  renderList();
}
/** 整页重建（进入用户/登出/会话失效）时强制退出批量模式，并移除悬浮条 */
function resetBatchMode() {
  batchMode = false; batchSel.clear();
  const bar = $("#batch-bar"); if (bar) bar.remove();
}
/** 切换单条选择（按 id；不重建列表，仅刷卡片选中态与计数，避免滚动位置丢失） */
function toggleSelect(id) {
  if (batchSel.has(id)) batchSel.delete(id); else batchSel.add(id);
  syncBatchUI();
}
/** 当前可见集是否全部选中 */
function allVisibleSelected() {
  const visible = getVisibleClips();
  return visible.length > 0 && visible.every((c) => batchSel.has(c.id));
}
/** 全选当前页：全选（或已全选则取消全选）当前过滤出的可见条目 */
function selectAllVisible() {
  const visible = getVisibleClips();
  if (allVisibleSelected()) { for (const c of visible) batchSel.delete(c.id); }
  else { for (const c of visible) batchSel.add(c.id); }
  syncBatchUI();
}
/** 同步已渲染卡片的选中态 + 批量条计数/全选标签/按钮可用态 */
function syncBatchUI() {
  document.querySelectorAll(".clip-card").forEach((card) => {
    const id = card.dataset.clipId;
    if (id === undefined) return;
    card.classList.toggle("selected", batchSel.has(id));
    const chk = card.querySelector(".sel-chk");
    if (chk) chk.classList.toggle("on", batchSel.has(id));
  });
  const bar = $("#batch-bar");
  if (!bar) return;
  const cnt = bar.querySelector(".batch-count");
  if (cnt) cnt.textContent = "已选 " + batchSel.size + " 项";
  const sa = bar.querySelector(".batch-select-all");
  if (sa) sa.textContent = allVisibleSelected() ? "取消全选" : "全选当前页";
  const empty = batchSel.size === 0;
  bar.querySelectorAll("[data-need-sel]").forEach((b) => { b.disabled = empty; });
}
/** 渲染悬浮批量操作条（仅批量模式；每次 renderList 末尾调用，重建以刷新状态） */
function renderBatchBar() {
  const old = $("#batch-bar"); if (old) old.remove();
  if (!batchMode) return;
  const bar = el("div", "batch-bar");
  bar.id = "batch-bar"; // id 供 $() 定位（el() 只设 class）
  const cnt = el("span", "batch-count", "已选 " + batchSel.size + " 项");
  const sa = el("button", "btn sm ghost batch-select-all", allVisibleSelected() ? "取消全选" : "全选当前页");
  sa.onclick = () => selectAllVisible();
  const add = el("button", "btn sm", "＋ 加标签");
  add.dataset.needSel = "1";
  add.onclick = () => openBatchTagModal("add");
  const rm = el("button", "btn sm", "－ 减标签");
  rm.dataset.needSel = "1";
  rm.onclick = () => openBatchTagModal("remove");
  const del = el("button", "btn sm danger", "🗑 删除");
  del.dataset.needSel = "1";
  del.onclick = () => {
    if (!batchSel.size) return;
    askConfirm("删除已选的 " + batchSel.size + " 条内容？此操作不可恢复（已配置 WebDAV 备份则下次同步也会从远端删除）。", guard(del, async () => {
      const r = await api("/api/clips/batch", { method: "POST", json: { action: "delete", ids: [...batchSel] } }).catch((e) => errToast(e.message));
      if (!r) return;
      flash("已批量删除 " + r.deleted + " 条");
      setBatchMode(false); // 删除后退出批量模式（选择已无意义）
      await refreshList();
    }), "批量删除");
  };
  const done = el("button", "btn sm primary", "完成");
  done.onclick = () => setBatchMode(false);
  bar.append(cnt, sa, add, rm, del, done);
  document.body.append(bar);
  const empty = batchSel.size === 0;
  bar.querySelectorAll("[data-need-sel]").forEach((b) => { b.disabled = empty; });
}
/** 批量加/减标签弹窗：add=选要加的标签（含新建）；remove=在已选条目的标签并集中挑要移除的 */
function openBatchTagModal(mode) {
  if (!batchSel.size) return errToast("请先选择条目");
  const isAdd = mode === "add";
  const root = $("#modal-root");
  const m = el("div", "mask");
  const modal = el("div", "modal");
  modal.append(el("h3", "", isAdd ? "批量加标签" : "批量减标签"),
    el("div", "sub", "对已选 " + batchSel.size + " 条" + (isAdd ? "添加标签" : "移除标签")));
  const pickSel = [];
  const wrap = el("div");
  if (isAdd) {
    renderTagPicker(wrap, pickSel, state.tags, (s) => { pickSel.length = 0; pickSel.push(...s); });
  } else {
    const union = new Set();
    for (const c of state.clips) if (batchSel.has(c.id)) for (const t of c.tags || []) union.add(t);
    if (!union.size) {
      const row = el("div", "form-row");
      const close = el("button", "btn ghost", "关闭"); close.style.flex = "1";
      close.onclick = () => m.remove();
      row.append(close);
      modal.append(el("div", "empty", "所选条目没有任何标签"), row);
      m.append(modal); root.append(m);
      return;
    }
    renderTagPicker(wrap, pickSel, [...union].map((t) => ({ tag: t })), (s) => { pickSel.length = 0; pickSel.push(...s); });
  }
  modal.append(wrap);
  const row = el("div", "form-row");
  const ok = el("button", "btn primary", "确定"); ok.style.flex = "1";
  const cancel = el("button", "btn ghost", "取消"); cancel.style.flex = "1";
  row.append(ok, cancel); modal.append(row);
  ok.onclick = guard(ok, async () => {
    if (!pickSel.length) return errToast("请选择标签");
    const r = await api("/api/clips/batch", { method: "POST", json: { action: isAdd ? "addTags" : "removeTags", ids: [...batchSel], tags: pickSel } }).catch((e) => errToast(e.message));
    if (!r) return;
    m.remove();
    flash((isAdd ? "已批量加标签（" : "已批量减标签（") + r.affected + " 条）");
    await refreshList();
  });
  cancel.onclick = () => m.remove();
  m.append(modal); root.append(m);
}

// ---------- 卡片工厂（v0.4.3 拆分：clipCard 从 CC49 降到组装级，各事件按钮独立小函数）
// v0.6.5 按钮样式对齐方案18：26px 方形图标钮（.ops .b），常显、hover 金、删除 hover 红 ----------
/** 星标按钮（非归档卡片）；点击切换置顶并更新卡片样式 */
function makePinBtn(c, card) {
  if (c.archived) return null;
  const btn = el("button", "b" + (c.pinned ? " on" : ""), c.pinned ? "★" : "☆");
  btn.title = c.pinned ? "取消置顶" : "置顶";
  btn.onclick = (e) => {
    e.stopPropagation();
    guard(btn, async () => {
      const r = await api("/api/clips/" + c.id + "/pin", { method: "POST" }).catch(e2 => errToast(e2.message));
      if (!r) return;
      c.pinned = r.pinned;
      // v0.6.5：置顶后整体刷新——顶部状态徽章实时重建（★ 置顶出现/消失）+ 后端 pinned 优先排序生效（卡片跳到最前/归位）
      flash(r.pinned ? "已置顶" : "已取消置顶");
      refreshList();
    })();
  };
  return btn;
}
/** 文件卡「下载」按钮（图标 ↓） */
function makeDownloadBtn(c) {
  const btn = el("button", "b", "↓");
  btn.title = "下载 " + (c.fileName || "");
  btn.onclick = (e) => { e.stopPropagation(); downloadFile(c); };
  return btn;
}
/** 「编辑」按钮（图标 ✎） */
function makeEditBtn(c) {
  const btn = el("button", "b", "✎");
  btn.title = "编辑";
  btn.onclick = (e) => { e.stopPropagation(); openEditModal(c); };
  return btn;
}
/** 「删除」按钮（图标 ✕，带确认） */
function makeDeleteBtn(c) {
  const btn = el("button", "b del", "✕");
  btn.title = "删除";
  btn.onclick = (e) => {
    e.stopPropagation();
    askConfirm("删除这条内容？", guard(btn, async () => {
      // P-101 修复：必须检查返回值——失败时 errToast 已提示，不得再报"已删除"（此前无条件 flash 误导用户）
      const r = await api("/api/clips/" + c.id, { method: "DELETE" }).catch(e2 => errToast(e2.message));
      if (!r) return;
      flash("已删除"); // UI 走查 U-5：删除成功也有反馈
      refreshList();
    }), "删除");
  };
  return btn;
}
/** 恢复归档按钮（v0.6.13：归档卡片 ↺，移回活跃区） */
function makeRestoreBtn(c) {
  const btn = el("button", "b", "↺");
  btn.title = "恢复（移回活跃区）";
  btn.onclick = (e) => {
    e.stopPropagation();
    guard(btn, async () => {
      const r = await api("/api/clips/" + c.id + "/restore", { method: "POST" }).catch(e2 => errToast(e2.message));
      if (!r) return;
      flash("已恢复");
      refreshList();
    })();
  };
  return btn;
}
/** JSON 格式化预览按钮（文本可解析为 JSON 时） */
function makeJsonBtn(c) {
  const btn = el("button", "b", "{}");
  btn.title = "JSON 格式化预览";
  btn.onclick = (e) => { e.stopPropagation(); openJsonPreview(c); };
  return btn;
}

/** 富文本分栏（v0.6.12 改版：取消格式渲染预览——左右都显示文本，顶部一行提示区分；左栏复制纯文本、右栏复制富文本） */
function makeRichSplit(c) {
  const split = el("div", "rich-split");
  // 顶部提示行：一边普通文本 / 一边富文本
  const tip = el("div", "rich-tip");
  tip.append(el("span", "t", "T 普通文本"), el("span", "f", "✦ 富文本"));
  split.append(tip);
  // 左右两栏容器
  const cols = el("div", "rich-cols");
  // 左栏：普通文本（点击复制纯文本）
  const left = el("div", "half plain");
  left.title = "单击复制纯文本";
  left.append(el("div", "plain-pv", c.content || ""));
  left.onclick = (e) => {
    e.stopPropagation();
    guard(left, async () => {
      suppressAutoPasteUntil = Date.now() + 800; // 来源抑制：本次复制不触发自动弹窗
      const ok = await copyText(c.content || "");
      if (ok) {
        flash("纯文本已复制", e.clientX, e.clientY);
        bumpCopyCount(c, left);
      } else errToast("复制失败，请手动选择复制");
    })();
  };
  // 右栏：富文本（显示文本，点击复制带格式；不再渲染格式预览）
  const right = el("div", "half rich");
  right.title = "单击复制带格式（粘贴到 Word/飞书保留样式）";
  right.append(el("div", "plain-pv", c.content || ""));
  right.onclick = (e) => {
    e.stopPropagation();
    guard(right, async () => {
      suppressAutoPasteUntil = Date.now() + 800;
      const ok = await copyRich(c.html || "", c.content || "");
      if (ok) {
        flash("富文本已复制（含格式）", e.clientX, e.clientY);
        bumpCopyCount(c, right);
      } else errToast("富文本复制失败——请用独立浏览器标签页打开后重试（预览面板剪贴板权限受限）");
    })();
  };
  cols.append(left, right);
  split.append(cols);
  return split;
}

/** 复制成功后本地 +1 计数（与 handleCardClick 的计数逻辑共用） */
function bumpCopyCount(c, root) {
  // 仅跳过「普通文件下载」（非图片）；图片复制到剪贴板也算一次复制（P-5 计数口径）
  const isImage = c.type === "file" && (c.fileMime || "").startsWith("image/");
  if (c.type === "file" && !isImage) return;
  api("/api/clips/" + c.id + "/copy", { method: "POST" }).then(() => {
    c.copyCount = (c.copyCount || 0) + 1;
    const span = $(".copycnt", root.closest(".clip-card"));
    if (span) span.textContent = "复制 " + c.copyCount + " 次";
  }).catch(() => {});
}
/** 卡片 meta 区：复制次数 / 标签（点击过滤）/ 时间（过期徽章已在顶部状态行，v0.6.5） */
function makeCardMeta(c) {
  const meta = el("div", "meta");
  meta.append(el("span", "copycnt", "复制 " + (c.copyCount || 0) + " 次"));
  for (const t of c.tags || []) {
    const tg = el("span", "badge", "#" + t);
    tg.style.cursor = "pointer";
    tg.onclick = (e) => { e.stopPropagation(); state.filter.tag = t; renderTagbar(); renderList(); };
    meta.append(tg);
  }
  meta.append(el("span", "", fmtTime(c.updatedAt)));
  return meta;
}
/** 单击卡片：文本/链接复制；图片复制到剪贴板（失败降级预览）；其他文件下载 */
async function handleCardClick(c, card, e) {
  suppressAutoPasteUntil = Date.now() + 800; // 来源抑制：本次点击引起的写剪贴板不触发自动弹窗
  const px = e ? e.clientX : undefined, py = e ? e.clientY : undefined;
  if (c.type === "file") {
    if ((c.fileMime || "").startsWith("image/")) {
      try {
        const ok = await copyImageToClipboard(c);
        if (ok) { flash("图片已复制，可直接粘贴", px, py); bumpCopyCount(c, card); } // P-5：图片复制也计数（此前漏记）
        else { errToast("此浏览器不支持复制图片，已打开预览"); openImagePreview(c); }
      } catch { errToast("图片加载失败"); }
    } else downloadFile(c);
    return;
  }
  const text = c.type === "link" ? c.url : c.content;
  const ok = await copyText(text);
  if (ok) {
    flash("已复制", px, py);
    if (c.type !== "file") {
      api("/api/clips/" + c.id + "/copy", { method: "POST" }).then(() => {
        // 本地即时更新计数（走查 P-5：不刷新也看到 +1）
        c.copyCount = (c.copyCount || 0) + 1;
        const span = $(".copycnt", card);
        if (span) span.textContent = "复制 " + c.copyCount + " 次";
      }).catch(() => {});
    }
  } else errToast("复制失败，请手动选择复制");
}

function clipCard(c) {
  const card = el("div", "clip-card");
  card.dataset.clipId = c.id; // v0.6.15：批量选择按 id 映射 DOM 卡片（syncBatchUI 用）
  if (c.pinned) card.classList.add("pinned"); // 星标卡片高亮描边
  if (c.archived) card.classList.add("archived"); // 归档：降透明（v0.6.5 状态视觉化）
  // 顶部徽章行（方案18：类型徽章 + 标题 + 状态徽章）
  const row1 = el("div", "row1");
  const typeBadge = el("span", "badge " + (c.type === "link" ? "link" : c.type === "file" ? "file" : "text"), c.type === "link" ? "链接" : c.type === "file" ? "文件" : "文本");
  const title = el("span", "title", c.title || (c.type === "link" ? hostOf(c.url) : c.type === "file" ? c.fileName : (c.content || "").slice(0, 30)));
  const status = el("span", "status");
  if (c.pinned) status.append(el("span", "st pin", "★ 置顶"));
  if (c.expireAt) status.append(el("span", "st exp", "⏳ " + expLabel(c.expireAt)));
  if (c.archived) status.append(el("span", "st arch", "归档"));
  // 右上角操作组：✕ 删除（v0.6.13：归档也可手动删除——WebDAV 完整备份后清理用不到的归档；↺ 恢复归档）
  const topOps = el("div", "ops top");
  if (c.archived) topOps.append(makeRestoreBtn(c));
  topOps.append(makeDeleteBtn(c));
  row1.append(typeBadge, title, status, topOps);
  // 底部 meta 行：信息 + 其余操作（☆ 收藏 / ✎ 编辑 / ↓ 下载 / {} JSON）
  const foot = el("div", "foot");
  foot.append(makeCardMeta(c));
  const footOps = el("div", "ops");
  const pin = makePinBtn(c, card); if (pin) footOps.append(pin);
  if (c.type === "file") footOps.append(makeDownloadBtn(c));
  if (!c.archived) footOps.append(makeEditBtn(c));
  if (c.type === "text" && looksLikeJson(c.content)) footOps.append(makeJsonBtn(c));
  // v0.6.12：富文本复制入口在内容区左右分栏（左纯文本/右富文本）；链接打开为内容区金色主按钮——ops 不重复添加
  if (footOps.children.length) foot.append(footOps);
  card.append(row1, makeCardBody(c), foot);

  // 批量模式（v0.6.15）：整卡=选择切换（透明覆盖层），左上角勾选框；禁用复制/双击编辑/图片 hover 预览
  if (batchMode) {
    card.classList.add("batching");
    card.classList.toggle("selected", batchSel.has(c.id));
    const chk = el("div", "sel-chk" + (batchSel.has(c.id) ? " on" : ""), "✓");
    chk.onclick = (e) => { e.stopPropagation(); toggleSelect(c.id); };
    const ov = el("div", "batch-overlay");
    ov.onclick = () => toggleSelect(c.id);
    card.append(chk, ov);
    return card;
  }

  // 图片卡片 hover 预览（v0.4.3 状态显式化 + 独立函数）：默认 100%，滚轮缩放（50%~300%）
  //  - 状态收敛为单一对象 previewState（open/scale/timer/box/drag），不再散落闭包变量（防布尔失控，架构评估 v2 #1）
  //  - 浮层挂卡片内部（mouseleave 不中途消失）；wheel 绑卡片（卡片/浮层内滚动都生效）
  if (c.type === "file" && (c.fileMime || "").startsWith("image/")) bindImageHoverPreview(c, card);

  // 单击复制 / 双击编辑（v0.4.2：复制成功提示跟随鼠标点击位置）
  card.onclick = (e) => handleCardClick(c, card, e);
  card.ondblclick = (e) => { if (e.target.closest(".ops") || e.target.closest(".rich-split") || c.archived) return; openEditModal(c); }; // 归档只读；分栏点击已 stopPropagation 但双击仍拦
  return card;
}

/** 从 URL 提取域名（链接卡金色标题用） */
function hostOf(url) {
  try { return new URL(url).hostname; } catch { return (url || "").replace(/^https?:\/\//, "").split("/")[0]; }
}

/** 卡片内容区（方案18 各类型专属） */
function makeCardBody(c) {
  // 富文本：左右分栏（v0.6.12：去格式渲染预览，保留双栏复制入口 + 顶部提示行）
  if (c.type === "text" && c.html) return makeRichSplit(c);
  // JSON：代码窗（金色键名/绿色字符串，等宽缩进）
  if (c.type === "text" && looksLikeJson(c.content)) return makeJsonPreview(c);
  // 图片：cover 撑满内容区
  if (c.type === "file" && (c.fileMime || "").startsWith("image/")) {
    const preview = el("div", "imgwrap");
    const img = el("img");
    img.loading = "lazy";
    img.alt = c.fileName || "图片";
    img.src = BASE + "/api/files/" + c.fileId + "?token=" + encodeURIComponent(state.current?.token || "");
    preview.append(img);
    return preview;
  }
  // 文件：类型图标卡（PDF 红边 / ZIP 金边 / 其他中性）
  if (c.type === "file") return makeFileIcon(c);
  // 链接：金色域名标题 + URL + 「↗ 打开链接」主按钮
  if (c.type === "link") return makeLinkBody(c);
  // 文本：2 行摘要
  return el("div", "pv", c.content || "");
}

/** JSON 代码窗预览（安全：键/值着色基于解析结果重建，不注入原始文本） */
function makeJsonPreview(c) {
  const box = el("div", "code");
  let parsed = null;
  try { parsed = JSON.parse(c.content); } catch { /* 保持原样 */ }
  box.append(el("div", "dots", ""), el("span", "fname", c.title || "config.json"));
  const pre = el("pre");
  if (parsed !== null) {
    const json = JSON.stringify(parsed, null, 2);
    // 简易着色：键名金色、字符串绿色（基于转义后的文本做行内替换，安全）
    const esc = json.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
    pre.innerHTML = esc.replace(/("(?:\\u[a-fA-F0-9]{4}|\\[^u]|[^\\"])*")(\s*:)/g, '<span class="k">$1</span>$2')
                      .replace(/("(?:\\u[a-fA-F0-9]{4}|\\[^u]|[^\\"])*")(\s*[,}\]])/g, '<span class="s">$1</span>$2');
  } else pre.textContent = c.content;
  box.append(pre);
  return box;
}

/** 文件类型图标卡（PDF/ZIP 着色） */
function makeFileIcon(c) {
  const body = el("div", "filebody");
  const name = (c.fileName || "").toLowerCase();
  const isPdf = name.endsWith(".pdf");
  const isZip = /\.(zip|rar|7z|tar|gz)$/.test(name);
  const fic = el("div", "fic " + (isPdf ? "pdf" : isZip ? "zip" : ""));
  fic.textContent = (isPdf ? "PDF" : isZip ? "ZIP" : "FILE");
  fic.append(el("span", "fold", ""));
  const finfo = el("div", "finfo");
  finfo.append(el("div", "fname", c.fileName || "文件"), el("div", "fsize", fmtSize(c.fileSize) + " · " + (c.fileMime || "").split("/")[0]));
  body.append(fic, finfo);
  return body;
}

/** 链接卡内容：金色域名 + URL + 打开主按钮 */
function makeLinkBody(c) {
  const body = el("div", "linkbody");
  const url = el("div", "url", c.url);
  url.title = c.url;
  const openBtn = el("button", "main-btn", "↗ 打开链接");
  openBtn.onclick = (e) => { e.stopPropagation(); window.open(c.url, "_blank", "noopener"); };
  body.append(url, openBtn);
  return body;
}

// ---------- 图片 hover 预览绑定（v0.4.3：从 clipCard 抽出，CC 独立） ----------
function bindImageHoverPreview(c, card) {
  /** 预览状态机：open=浮层是否可见；scale=缩放比例；timer=延迟句柄；box=浮层元素；drag=拖拽进行中 */
  const previewState = { open: false, scale: 1.0, timer: null, box: null, drag: false };
  // 缩放应用：按自然尺寸×比例设宽高，框体同步调整（唯一改 scale 生效的出口）
  const applyScale = () => {
    if (!previewState.open) return;
    const img = previewState.box.querySelector("img");
    const cap = previewState.box.querySelector(".img-cap");
    img.style.width = Math.round(img.naturalWidth * previewState.scale) + "px";
    img.style.height = "auto";
    img.style.maxWidth = "none";
    if (cap) cap.textContent = c.fileName + " · " + fmtSize(c.fileSize) + " · " + Math.round(previewState.scale * 100) + "%";
    reposition();
  };
  // 缩放步进：delta>0 放大，<0 缩小；钳制 50%~300%；读配置步长（默认 15%）
  const zoom = (delta) => {
    const step = LS.get("zoomStep", 0.15) || 0.15;
    const before = previewState.scale;
    previewState.scale = Math.min(3.0, Math.max(0.5, previewState.scale + (delta > 0 ? step : -step)));
    if (previewState.scale !== before) applyScale();
  };
  // 框体尺寸 + 定位：跟随图片大小（受视口限制），显示在卡片正上方（间隙 4px，贴卡片防"飘远"）
  const reposition = () => {
    if (!previewState.open) return;
    const img = previewState.box.querySelector("img");
    const cap = previewState.box.querySelector(".img-cap");
    const pad = 20; // box padding 10*2
    const bw = Math.min(img.offsetWidth + pad, window.innerWidth - 16);
    const bh = Math.min(img.offsetHeight + (cap ? cap.offsetHeight : 20) + pad, window.innerHeight - 16);
    previewState.box.style.width = bw + "px";
    const r = card.getBoundingClientRect();
    let left = r.left + r.width / 2 - bw / 2;
    left = Math.max(8, Math.min(left, window.innerWidth - bw - 8));
    const top = (r.top - bh - 4 >= 8) ? (r.top - bh - 4) : Math.min(r.bottom + 4, window.innerHeight - bh - 8);
    previewState.box.style.left = left + "px";
    previewState.box.style.top = top + "px";
  };
  // 打开浮层（260ms 延迟防快速划过误弹）
  // v0.6.13：触发区收窄到图片区域 imgwrap——此前绑整卡 mouseenter，鼠标到按钮/标题区也会弹预览（用户反馈修正）
  const imgwrap = card.querySelector(".imgwrap");
  const open = () => {
    if (previewState.open) return; // 防重：浮层已开不再重建（浮层挂卡内，鼠标移到浮层会再触发 mouseenter）
    clearTimeout(previewState.timer);
    previewState.timer = setTimeout(() => {
      const box = el("div", "img-hover-preview");
      const wrap = el("div", "img-hover-wrap");
      const img = el("img");
      img.src = BASE + "/api/files/" + c.fileId + "?token=" + encodeURIComponent(state.current?.token || "");
      img.onerror = () => close();
      const cap = el("div", "img-cap");
      cap.textContent = c.fileName + " · " + fmtSize(c.fileSize) + " · 100%";
      previewState.scale = 1.0; // 每次重开重置 100%
      previewState.box = box;
      previewState.open = true;
      wrap.append(img);
      box.append(wrap, cap);
      card.appendChild(box); // 挂卡片内部，防止 mouseleave 中途移除浮层
      img.onload = () => applyScale();
    }, 260);
  };
  // 关闭浮层（移除元素 + 重置状态）
  const close = () => {
    clearTimeout(previewState.timer);
    if (previewState.box) { previewState.box.remove(); previewState.box = null; }
    previewState.open = false;
    previewState.drag = false;
  };
  // 滚轮缩放（绑卡片：卡片+浮层内滚动都生效；浮层未开不拦截页面滚动）
  card.addEventListener("wheel", (e) => {
    if (!previewState.open) return;
    e.preventDefault();
    zoom(e.deltaY < 0 ? 1 : -1);
  }, { passive: false });
  // 拖拽平移：按住鼠标拖动查看放大图片的不同位置（v0.4.2）
  card.addEventListener("mousedown", (e) => {
    if (!previewState.open || e.button !== 0) return;
    const wrap = previewState.box.querySelector(".img-hover-wrap");
    if (!wrap || !wrap.contains(e.target)) return; // 仅浮层图片区可拖
    e.preventDefault();
    previewState.drag = true;
    const startX = e.clientX, startY = e.clientY;
    const sl = wrap.scrollLeft, st = wrap.scrollTop;
    const move = (ev) => { if (!previewState.drag) return; wrap.scrollLeft = sl - (ev.clientX - startX); wrap.scrollTop = st - (ev.clientY - startY); };
    const up = () => { previewState.drag = false; document.removeEventListener("mousemove", move); document.removeEventListener("mouseup", up); };
    document.addEventListener("mousemove", move);
    document.addEventListener("mouseup", up);
  });
  // 触发：仅图片区域 mouseenter 打开（按钮/标题/状态区不再误触）；离开整卡才关闭（浮层在卡内不中途消失）
  if (imgwrap) imgwrap.addEventListener("mouseenter", open);
  card.addEventListener("mouseleave", close);
}

// ---------- 文件下载 / 图片复制到剪贴板 / 图片预览 ----------
async function downloadFile(c) {
  try {
    const blob = await apiBlob("/api/files/" + c.fileId);
    if (!blob || !blob.size) throw new Error("文件实体为空");
    const u = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = u; a.download = c.fileName || "file";
    document.body.appendChild(a); a.click(); a.remove();
    // v0.6.11：延迟释放——a.click() 是同步触发下载，但部分浏览器需在下个 tick 才能开始读取，
    // 立即 revokeObjectURL 会导致下载中断/空文件
    setTimeout(() => URL.revokeObjectURL(u), 2000);
  } catch (ex) { errToast(ex.message); }
}
/** 图片转 PNG（ClipboardItem 最可靠格式；canvas 同源 blob 可用） */
function blobToPng(blob) {
  return new Promise((resolve) => {
    const img = new Image();
    const url = URL.createObjectURL(blob);
    img.onload = () => {
      try {
        const cv = document.createElement("canvas");
        cv.width = img.naturalWidth; cv.height = img.naturalHeight;
        cv.getContext("2d").drawImage(img, 0, 0);
        URL.revokeObjectURL(url);
        cv.toBlob((b) => resolve(b), "image/png");
      } catch { URL.revokeObjectURL(url); resolve(null); }
    };
    img.onerror = () => { URL.revokeObjectURL(url); resolve(null); };
    img.src = url;
  });
}
/** 复制图片到系统剪贴板（资料依据：Chrome/Edge 76+ 支持 ClipboardItem 写图片，image/png 最可靠；
 *  需安全上下文 + 点击手势；先用原格式，不支持则转 PNG，再失败返回 false 由调用方降级） */
async function copyImageToClipboard(c) {
  const tryWrite = async (b, t) => {
    if (!navigator.clipboard || !navigator.clipboard.write || typeof ClipboardItem === "undefined") return false;
    if (typeof ClipboardItem.supports === "function" && !ClipboardItem.supports(t)) return false;
    try { await navigator.clipboard.write([new ClipboardItem({ [t]: b })]); return true; } catch { return false; }
  };
  const blob = await apiBlob("/api/files/" + c.fileId);
  const type = blob.type || "image/png";
  if (await tryWrite(blob, type)) return true;
  const png = await blobToPng(blob);
  if (png && await tryWrite(png, "image/png")) return true;
  return false;
}
async function openImagePreview(c) {
  const root = $("#modal-root");
  root.innerHTML = "";
  const m = el("div", "mask");
  const modal = el("div", "modal");
  modal.style.maxWidth = "min(92vw, 760px)";
  const img = el("img");
  img.style.cssText = "max-width:100%;max-height:62vh;border-radius:8px;display:block;margin:0 auto 12px;object-fit:contain";
  modal.append(el("h3", "", c.fileName + " · " + fmtSize(c.fileSize)), img);
  const row = el("div", "form-row");
  const dl = el("button", "btn primary", "下载"); dl.style.flex = "1";
  const close = el("button", "btn ghost", "关闭"); close.style.flex = "1";
  row.append(dl, close); modal.append(row);
  dl.onclick = () => downloadFile(c);
  close.onclick = () => m.remove();
  m.append(modal); root.append(m);
  try {
    const blob = await apiBlob("/api/files/" + c.fileId);
    img.src = URL.createObjectURL(blob);
  } catch (ex) { errToast("预览失败: " + ex.message); }
}

// ---------- JSON 格式化预览（P1-7）：检测 + 美化弹窗（复制 / 覆盖保存） ----------
function looksLikeJson(s) {
  const t = String(s || "").trim();
  if (!t || (t[0] !== "{" && t[0] !== "[")) return false;
  if (t.length > 100000) return false; // 超大文本不格式化（防卡顿）
  try { JSON.parse(t); return true; } catch { return false; }
}
function openJsonPreview(c) {
  const root = $("#modal-root");
  root.innerHTML = "";
  const m = el("div", "mask");
  const modal = el("div", "modal");
  modal.style.maxWidth = "min(92vw, 760px)";
  modal.append(el("h3", "", "JSON 预览 · " + (c.title || "未命名")));
  const ta = el("textarea");
  ta.style.cssText = "min-height:320px;max-height:50vh;font-family:ui-monospace,Consolas,monospace;font-size:12px;line-height:1.5;white-space:pre;overflow:auto";
  ta.readOnly = true;
  let formatted = "";
  try { formatted = JSON.stringify(JSON.parse(c.content), null, 2); } catch { formatted = c.content; }
  ta.value = formatted;
  modal.append(ta);
  const row = el("div", "form-row");
  const copy = el("button", "btn sm", "复制美化结果"); copy.style.flex = "1";
  const save = el("button", "btn sm primary", "覆盖保存"); save.style.flex = "1";
  const close = el("button", "btn sm ghost", "关闭"); close.style.flex = "1";
  row.append(copy, save, close); modal.append(row);
  copy.onclick = guard(copy, async () => {
    const ok = await copyText(formatted);
    if (ok) flash("已复制美化 JSON"); else errToast("复制失败");
  });
  save.onclick = guard(save, async () => {
    // v0.6.11：带 html 的条目覆盖保存同步重建 html（防 content/html 不一致，同编辑弹窗修复）
    const json = { content: formatted };
    if (c.html && formatted !== (c.content || "")) json.html = textToHtml(formatted);
    const r = await api("/api/clips/" + c.id, { method: "PUT", json }).catch(e2 => errToast(e2.message));
    if (r) { c.content = formatted; m.remove(); flash("已覆盖保存"); refreshList(); }
  });
  close.onclick = () => m.remove();
  m.append(modal); root.append(m);
}

// ---------- 存入大弹窗（万能入口：检测到复制内容自动弹出 / 点小入口手动打开） ----------
// v0.6.8 链路重写：html 捕获统一（paste 事件 = 手动可靠来源 / autoFill read = 自动尽力来源），
// 类型徽章实时自证「✦ 将存为：格式文本」，消除历史补丁叠加。
function openPasteModal(auto = false, pasteData = null) {
  if ($(".paste-modal")) return; // 已打开不重复弹（连续复制时用户在弹窗内自行粘贴）
  const root = $("#modal-root");
  const m = el("div", "mask");
  const modal = el("div", "modal paste-modal");
  const pb = el("div", "paste-body");
  // 头部
  const head = el("div", "p-head");
  head.append(el("div", "p-ic", "📥"), el("h3", "", "存入内容"));
  pb.append(head);
  // 类型徽章（实时识别：文件/链接/格式文本/文本；pendingHtml 非空时显示「✦ 格式文本」自证捕获成功）
  const typeBadge = el("div", "paste-badge text", "将存为：文本");
  function updateBadge() {
    const content = ta.value.trim();
    typeBadge.className = "paste-badge ";
    if (pickedFile) { typeBadge.textContent = "将存为：文件"; typeBadge.classList.add("file"); return; }
    if (!content) { typeBadge.textContent = "将存为：文本"; typeBadge.classList.add("text"); return; }
    if (/^https?:\/\/\S+$/i.test(content)) { typeBadge.textContent = "将存为：链接"; typeBadge.classList.add("link"); return; }
    if (pendingHtml) { typeBadge.textContent = "✦ 将存为：格式文本"; typeBadge.classList.add("rich"); return; }
    typeBadge.textContent = "将存为：文本"; typeBadge.classList.add("text");
  }
  // 徽章行：类型徽章 + 右侧一键清空（v0.6.6：自动读取剪贴板后可随时清空重输）
  const badgeRow = el("div", "paste-badge-row");
  const clearBtn = el("button", "paste-clear", "🗑 清空");
  clearBtn.title = "清空输入与已选文件";
  clearBtn.onclick = () => {
    ta.value = ""; pickedFile = null; pendingHtml = ""; chipBox.replaceChildren();
    syncTextareaVisibility(); updateBadge();
    flash("已清空"); ta.focus();
  };
  badgeRow.append(typeBadge, clearBtn);
  pb.append(badgeRow);
  // 输入区
  const ta = el("textarea"); ta.placeholder = "粘贴文本、链接，或拖文件到这里，一键存入…";
  ta.style.minHeight = "150px";
  ta.addEventListener("input", () => { updateBadge(); checkDuplicate(); });
  // Ctrl+Enter 快速存入（Enter 仍是换行）
  ta.addEventListener("keydown", (e) => {
    if (e.key === "Enter" && (e.ctrlKey || e.metaKey)) { e.preventDefault(); save.click(); }
  });
  // 粘贴处理（v0.6.8 重写：①捕获 text/html → ②图片/文件优先 → ③纯文本刷新徽章）
  ta.addEventListener("paste", (e) => {
    const cd = e.clipboardData;
    if (!cd) return;
    // ① 捕获 text/html：Word/网页复制的手动粘贴，clipboardData 在手势内直接提供，无需 read() 权限
    try {
      const h = cd.getData("text/html");
      if (h && h.length < 512 * 1024) pendingHtml = h;
    } catch {}
    // ② 图片/文件优先（截图、资源管理器复制文件）
    let file = null;
    if (cd.items) {
      for (const it of cd.items) {
        if (it.type && it.type.startsWith("image/")) { file = it.getAsFile(); break; }
      }
    }
    if (!file && cd.files && cd.files.length) file = cd.files[0];
    if (file) {
      e.preventDefault();
      pick(file);
      flash(file.type.startsWith("image/") ? "已接收图片，点存入即可" : "已接收文件，点存入即可");
      return;
    }
    // ③ 纯文本/富文本粘贴：刷新徽章（html 已捕获则显示「✦ 格式文本」）
    updateBadge();
  });
  const chipBox = el("div");
  let pickedFile = null;
  // v0.4.1：选中图片/文件后隐藏文本输入区（存入图片不需要文字），未选文件时显示
  // v0.6.6：类型徽章始终显示（updateBadge 会切到「将存为：文件」），只隐藏 textarea
  function syncTextareaVisibility() {
    const isFile = !!pickedFile;
    ta.classList.toggle("hidden", isFile);
  }
  function pick(f) {
    if (f.size > 10 * 1024 * 1024) return errToast("文件超过 10MB 上限");
    pickedFile = f;
    chipBox.replaceChildren();
    const chip = el("div", "file-chip");
    chip.append(el("span", "", "📎"), el("span", "fname", f.name), el("span", "fsize", fmtSize(f.size)));
    const rm = el("button", "rm", "✕");
    rm.title = "取消选择";
    rm.onclick = (e) => { e.stopPropagation(); pickedFile = null; chipBox.replaceChildren(); syncTextareaVisibility(); updateBadge(); };
    chip.append(rm);
    chipBox.append(chip);
    syncTextareaVisibility();
    updateBadge();
  }
  // ===== 重复检测（v0.6.6：检测到重复→直接切换为该条目的编辑弹窗「已有相同内容」，一次只弹一个窗） =====
  let dupTimer = null;
  let dupJumped = false; // 已跳转编辑窗，防后续 input 重复触发
  pb.append(ta); // v0.6.8：移除存入弹窗富文本实时预览（效果不符，编辑弹窗仍保留）
  function checkDuplicate() {
    if (dupJumped) return; // 已切换过一次，不再触发
    clearTimeout(dupTimer);
    dupTimer = setTimeout(() => {
      if (pickedFile) return;
      const content = ta.value.trim();
      if (!content) return;
      const dup = findDuplicateClip(content, state.clips);
      if (!dup) return;
      // v0.6.6：命中重复 → 关闭存入窗，直接打开该条目编辑弹窗（「已有相同内容」常驻），一次只一个窗
      dupJumped = true;
      m.remove();
      if (!dup.archived) openEditModal(dup, true);
      else flash("已有相同内容（归档只读）");
    }, 300);
  }
  // 拖放
  const fileBtn = el("button", "btn btn-file", "📁 选择文件");
  fileBtn.onclick = () => { const fi = el("input"); fi.type = "file"; fi.onchange = () => { if (fi.files[0]) pick(fi.files[0]); }; fi.click(); };
  pb.ondragover = (e) => { e.preventDefault(); pb.classList.add("drag"); };
  pb.ondragleave = () => pb.classList.remove("drag");
  pb.ondrop = (e) => { e.preventDefault(); pb.classList.remove("drag"); if (e.dataTransfer.files[0]) pick(e.dataTransfer.files[0]); };
  pb.append(chipBox);
  // 高级选项：别名 / 标签选择器 / 过期（v0.4.1：默认全部展开，不再折叠）
  const advBox = el("div", "paste-adv");
  const advRow = el("div", "adv-row");
  const advTitle = el("input"); advTitle.type = "text"; advTitle.placeholder = "别名（可留空，默认取首行）";
  const advExp = el("select");
  for (const [v, l] of [["", "永久"], ["1h", "1 小时后过期"], ["1d", "1 天后过期"], ["7d", "7 天后过期"], ["30d", "30 天后过期"]]) {
    const o = el("option", "", l); o.value = v; advExp.append(o);
  }
  advRow.append(advTitle, advExp);
  const advTagsWrap = el("div");
  const advTagsSel = [];
  renderTagPicker(advTagsWrap, advTagsSel, state.tags, (s) => { advTagsSel.length = 0; advTagsSel.push(...s); });
  advBox.append(advRow, advTagsWrap);
  pb.append(advBox);

  // 操作行
  const actions = el("div", "paste-actions");
  const save = el("button", "btn primary", "存入");
  const cancel = el("button", "btn btn-close", "关闭");
  // v0.4.3：保存流程抽独立函数 savePasteContent（openPasteModal CC 46→拆）
  save.onclick = guard(save, async () => {
    const content = ta.value.trim();
    if (!content && !pickedFile) return errToast("先粘贴内容或选择文件");
    const adv = { title: advTitle.value, tags: [...advTagsSel], expire: advExp.value }; // v0.4.1：高级选项恒生效
    const okSave = await savePasteContent({ content, pickedFile, adv, m }); // v0.6.6：重复已在输入时拦截（切编辑窗），此处兜底
    if (okSave) { await loadClips(); renderTagbar(); renderList(); flash("已存入"); }
  });
  cancel.onclick = () => m.remove();
  actions.append(fileBtn, save, cancel);
  pb.append(actions, el("div", "paste-hint", "Ctrl + Enter 快速存入 · 粘贴图片/文件自动识别"));
  modal.append(pb);
  m.append(modal); root.append(m);
  ta.focus();

  // 打开时自动填入剪贴板：优先用 pasteData（Ctrl+V 捕获，clipboardData 免权限直接可用）；
  // 无 pasteData（复制自动弹/点按钮打开）→ 走 Async Clipboard read 尽力读取
  // v0.4.3：抽独立函数 autoFillPasteModal（openPasteModal CC 46→拆）
  autoFillPasteModal(ta, typeBadge, pick, auto, () => updateBadge(), pasteData).then(() => {
    checkDuplicate(); // v0.6.6:自动填入(直接赋值不触发 input)后立即补一次重复检测
  });
}

// ---------- 存入保存流程（v0.4.3：从 openPasteModal 拆出——文件/链接/文本三分支） ----------
/**
 * 保存条目到后端；返回 true=已存入 / false=已拦截或失败。
 * 重复检测：文本/链接内容已存在 → 关存入窗，打开该条目的编辑页（标注「已有相同内容」）。
 * v0.6.6：force=true（用户在存入窗点了「仍要存入」）→ 跳过重复拦截，直接存入。
 */
async function savePasteContent({ content, pickedFile, adv, m, force = false }) {
  if (!pickedFile && !force) {
    const dup = findDuplicateClip(content, state.clips); // v0.4.3：统一走纯函数
    if (dup) {
      m.remove(); // 关闭存入弹窗
      if (!dup.archived) openEditModal(dup, true); // 归档条目只读，仅提示不打开编辑
      else flash("已有相同内容");
      return false;
    }
  }
  try {
    if (pickedFile) {
      const fd = new FormData(); fd.append("file", pickedFile);
      const r = await api("/api/files", { method: "POST", body: fd });
      if (!r) return false;
      await api("/api/clips", { method: "POST", json: { type: "file", fileId: r.file.fileId, fileName: r.file.fileName, fileSize: r.file.fileSize, fileMime: r.file.fileMime, title: adv.title || pickedFile.name, ...adv } });
    } else if (/^https?:\/\/\S+$/i.test(content)) {
      await api("/api/clips", { method: "POST", json: { type: "link", url: content, ...adv } });
    } else {
      // 自动标题：未填别名时取首行前 20 字
      const autoTitle = content.split("\n")[0].trim().slice(0, 20);
      // 富文本：仅当剪贴板有 text/html 来源时携带（无来源则为纯文本条目，卡片只显示 1 个复制按钮）
      // v0.6.9：存入前样式内联化——浏览器写入剪贴板强制剥 <style> 块只留 inline style（Chromium 122+ sanitization），
      // 提前把 style 块规则内联到元素，复制时格式才完整保留（Word 粘贴字体/颜色正确）
      const html = pendingHtml ? normalizeRichHtml(pendingHtml) : ""; // v0.6.9：统一标准化（内联化）
      await api("/api/clips", { method: "POST", json: { type: "text", title: adv.title || autoTitle, content, ...(html ? { html } : {}), ...adv } });
    }
    pendingHtml = ""; // 存入成功清空富文本暂存
    m.remove(); // 成功关弹窗
    return true;
  } catch (e) { errToast(e.message); return false; }
}

// ---------- 自动填入剪贴板（v0.4.3：从 openPasteModal 拆出——文本优先，其次图片） ----------
// v0.6.8 重写：read() 只调一次；html → 文本 → 图片 顺序清晰；read 权限失败时提示手动粘贴（走查 R-1/R-2 闭环）
// v0.6.18：新增 pasteData 分支——手动 Ctrl+V 走 paste 捕获（clipboardData 免权限直接可用），
//          不再依赖 navigator.clipboard.readText()（iframe/平台挂载未授权时读回空 → 弹窗内容空白）
async function autoFillPasteModal(ta, typeBadge, pick, auto, updateBadge, pasteData = null) {
  try {
    pendingHtml = ""; // 每次打开重置，避免残留上次的富文本
    // ① 手动 Ctrl+V 路径：paste 事件 clipboardData 直接提供，无需 read 权限
    if (pasteData) {
      if (pasteData.html) pendingHtml = pasteData.html; // 富文本来源（Word/网页复制）
      if (pasteData.file) { // 图片/文件优先（截图、资源管理器复制文件）
        pick(pasteData.file);
        flash(pasteData.file.type.startsWith("image/") ? "已接收图片，点存入即可" : "已接收文件，点存入即可");
        return;
      }
      const t = String(pasteData.text || "").trim();
      if (t && !ta.value) { ta.value = t; updateBadge(); flash("已填入剪贴板内容"); return; }
      return; // 剪贴板既无文本也无文件 → 留空窗，用户自行输入/拖入
    }
    // ◆ 无 pasteData（复制自动弹 / 点按钮打开）：一次 read() 拿全部剪贴板项（权限允许时；iframe/未授权会 catch 为空数组）
    let items = [];
    let readDenied = false;
    if (navigator.clipboard && navigator.clipboard.read) {
      items = await navigator.clipboard.read().catch(() => { readDenied = true; return []; });
    }
    // ① 捕获 text/html（自动弹出场景；读取结果为浏览器 sanitize 后的 HTML）
    for (const item of items) {
      if (item.types.includes("text/html")) {
        try {
          const blob = await item.getType("text/html");
          const html = await blob.text();
          if (html && html.length < 512 * 1024) pendingHtml = html;
        } catch {}
        break;
      }
    }
    // ② 文本优先填入
    if (navigator.clipboard && navigator.clipboard.readText) {
      const t = await navigator.clipboard.readText().catch(() => "");
      if (t && !ta.value) {
        ta.value = t; updateBadge();
        // 走查 R-1/R-2：read 权限被拒（预览 iframe）→ 格式可能丢失，引导手动粘贴走可靠路径
        if (readDenied && auto) flash("已填入文本——预览面板无法读取格式，从 Word/网页复制请按 Ctrl+V 粘贴以保留格式");
        else flash("已填入剪贴板内容");
        return;
      }
    }
    // ③ 图片拾取（无文本时；与文本一致，手动打开也自动拾取）
    for (const item of items) {
      for (const type of item.types) {
        if (type.startsWith("image/")) {
          try {
            const blob = await item.getType(type);
            const f = new File([blob], "paste-image." + (type.split("/")[1] || "png"), { type });
            pick(f); flash("已接收图片，点存入即可"); return;
          } catch {}
        }
      }
    }
    updateBadge(); // 无文本无图：确保徽章反映当前状态（html 已捕获则显示格式文本）
    if (auto && !ta.value && !pickedFile) errToast("剪贴板不是文本/图片（文件请直接 Ctrl+V 粘贴或拖入输入区）");
  } catch {}
}

// ---------- 编辑弹窗 ----------
// v0.4.2：第二参数 dup=true 时标题显示「已有相同内容」常驻标记（由重复检测触发，替代一闪而过的 toast）
// v0.6.6 方案32 v2：五类型差异化——文本/链接=textarea，富文本=纯文本编辑（v0.6.12 起取消实时预览），图片=缩略图卡，文件=只读图标卡
/** 编辑弹窗内删除条目（带确认；图片/文件卡上的 ✕ 用） */
function confirmDeleteHere(c, modalEl) {
  askConfirm("删除这条内容？", guard({}, async () => {
    const r = await api("/api/clips/" + c.id, { method: "DELETE" }).catch(e2 => errToast(e2.message));
    if (!r) return;
    modalEl.remove(); flash("已删除"); refreshList();
  }), "删除");
}
function openEditModal(c, dup = false) {
  const root = $("#modal-root");
  root.innerHTML = "";
  const m = el("div", "mask");
  const modal = el("div", "modal edit-modal");
  // 头部：类型图标 + 标题 + 类型徽章
  const isImage = c.type === "file" && (c.fileMime || "").startsWith("image/");
  const icon = c.type === "link" ? "🔗" : isImage ? "🖼️" : c.type === "file" ? "📄" : (c.html ? "✦" : "📝");
  const typeCls = c.type === "link" ? "link" : isImage ? "image" : c.type === "file" ? "file" : (c.html ? "rich" : "text");
  const typeTxt = c.type === "link" ? "链接" : isImage ? "图片" : c.type === "file" ? "文件" : (c.html ? "格式文本" : "文本");
  const head = el("div", "edit-head");
  head.append(el("div", "ic", icon), el("h3", "", "编辑" + (c.title ? " · " + c.title : "")), el("span", "edit-type " + typeCls, typeTxt));
  modal.append(head);
  if (dup) {
    // v0.4.2：重复提示常驻（两行），引导改标题/标签便于检索
    const dupTip = el("div", "edit-dup");
    dupTip.append(el("div", "t1", "⚠️ 已有相同内容"), el("div", "t2", "可修改标题或标签，方便下次检索"));
    modal.append(dupTip);
  }
  const imgUrl = () => BASE + "/api/files/" + c.fileId + "?token=" + encodeURIComponent(state.current?.token || "");
  // ===== ① 内容区（类型专属） =====
  const sec1 = el("div", "edit-sec");
  const t1 = el("div", "edit-sec-t"); t1.append(el("span", "n", "1"), el("span", "", c.type === "link" ? "链接" : isImage ? "图片" : c.type === "file" ? "文件" : "内容"));
  let contentInput = null, urlInput = null;
  let clearFormat = false; // v0.6.13：富文本清除格式标记（保存时 html 置空转纯文本）
  if (c.type === "text" && c.html) {
    // 富文本：纯文本编辑（v0.6.12：取消实时预览——保存后格式仍保留，编辑的是纯文本正文）
    t1.append(el("span", "hint", "编辑正文，保存后保留原格式"));
    // v0.6.13：清除格式——不需要富文本时一键转纯文本（保存后 html 置空，卡片变普通文本、复制不再带格式）
    // 样式用 primary（金色强调，与「保存」按钮同款醒目色，用户指定）
    const clearBtn = el("button", "btn sm primary", "清除格式");
    clearBtn.style.flexShrink = "0";
    clearBtn.title = "不需要保留格式时，点击后保存即转为纯文本";
    clearBtn.onclick = (e) => {
      e.stopPropagation();
      if (!clearFormat) {
        askConfirm("清除格式后该条目将变为纯文本，复制时不再保留格式（字体/颜色/对齐等）。继续？", () => {
          clearFormat = true;
          clearBtn.classList.add("warn");
          clearBtn.textContent = "已标记清除 · 保存生效（点此取消）";
        }, "清除格式");
      } else {
        clearFormat = false;
        clearBtn.classList.remove("warn");
        clearBtn.textContent = "清除格式";
      }
    };
    t1.append(clearBtn);
    sec1.append(t1);
    contentInput = el("textarea"); contentInput.value = c.content; contentInput.style.minHeight = "130px";
    sec1.append(contentInput);
  } else if (c.type === "link") {
    // 链接：文本式编辑（与文本一致，v0.6.6）
    t1.append(el("span", "hint", "与文本一致，直接编辑"));
    sec1.append(t1);
    urlInput = el("textarea"); urlInput.value = c.url; urlInput.style.minHeight = "90px";
    sec1.append(urlInput, el("div", "edit-file-note", "保存后按链接类型存储，点击卡片可复制此地址"));
  } else if (isImage) {
    // 图片：缩略图卡（hover 放大）
    t1.append(el("span", "hint", "hover 放大预览"));
    sec1.append(t1);
    const card = el("div", "img-card");
    const thumb = el("div", "thumb");
    const img = el("img"); img.src = imgUrl(); img.alt = c.fileName || "图片";
    const zoom = el("div", "zoom", el("i", "", "🔍"));
    thumb.append(img, zoom);
    const info = el("div", "img-info");
    info.append(el("span", "fname", c.fileName || "图片"), el("span", "fmeta", fmtSize(c.fileSize) + " · " + ((c.fileMime || "").split("/")[1] || "").toUpperCase()));
    const act = el("div", "act");
    const dl = el("button", "b", "↓"); dl.title = "下载"; dl.onclick = (e) => { e.stopPropagation(); downloadFile(c); };
    const del = el("button", "b del", "✕"); del.title = "删除";
    del.onclick = (e) => { e.stopPropagation(); confirmDeleteHere(c, m); };
    act.append(dl, del);
    info.append(act);
    card.append(thumb, info);
    sec1.append(card);
  } else if (c.type === "file") {
    // 文件：只读图标卡 + 下载/删除
    t1.append(el("span", "hint", "只读"));
    sec1.append(t1);
    const body = el("div", "filebody");
    const name = (c.fileName || "").toLowerCase();
    const isPdf = name.endsWith(".pdf");
    const isZip = /\.(zip|rar|7z|tar|gz)$/.test(name);
    const fic = el("div", "fic " + (isPdf ? "pdf" : isZip ? "zip" : ""));
    fic.textContent = (isPdf ? "PDF" : isZip ? "ZIP" : "FILE");
    fic.append(el("span", "fold", ""));
    const finfo = el("div", "finfo");
    finfo.append(el("div", "fname", c.fileName || "文件"), el("div", "fsize", fmtSize(c.fileSize) + " · " + (c.fileMime || "")));
    body.append(fic, finfo);
    const acts = el("div", "img-info");
    const act = el("div", "act");
    const dl = el("button", "b", "↓"); dl.title = "下载"; dl.onclick = (e) => { e.stopPropagation(); downloadFile(c); };
    const del = el("button", "b del", "✕"); del.title = "删除";
    del.onclick = (e) => { e.stopPropagation(); confirmDeleteHere(c, m); };
    act.append(dl, del);
    acts.append(act);
    body.append(acts);
    sec1.append(body, el("div", "edit-file-note", "文件内容不可在线编辑——需要替换请删除后重新存入"));
  } else {
    // 文本：textarea
    t1.append(el("span", "hint", "纯文本"));
    sec1.append(t1);
    contentInput = el("textarea"); contentInput.value = c.content; contentInput.style.minHeight = "110px";
    sec1.append(contentInput);
  }
  modal.append(sec1);
  // ===== ② 元数据区 =====
  const sec2 = el("div", "edit-sec");
  const t2 = el("div", "edit-sec-t"); t2.append(el("span", "n", "2"), el("span", "", "元数据"));
  sec2.append(t2);
  const title = el("input"); title.type = "text"; title.value = c.title || ""; title.placeholder = "别名";
  const expSel = el("select");
  for (const [v, l] of [["", "永久"], ["1h", "1 小时后"], ["1d", "1 天后"], ["7d", "7 天后"], ["30d", "30 天后"]]) {
    const o = el("option", "", l); o.value = v; expSel.append(o);
  }
  expSel.value = c.expireAt ? (c.expireAt - Date.now() < 7200000 ? "1h" : c.expireAt - Date.now() < 172800000 ? "1d" : c.expireAt - Date.now() < 604800000 ? "7d" : "30d") : "";
  const row = el("div", "edit-row");
  row.append(title, expSel);
  sec2.append(row);
  const editTagsSel = [...(c.tags || [])];
  const tagWrap = el("div");
  renderTagPicker(tagWrap, editTagsSel, state.tags, (s) => { editTagsSel.length = 0; editTagsSel.push(...s); });
  sec2.append(tagWrap);
  modal.append(sec2);
  // ===== 保存 / 归档 / 取消 =====
  const actions = el("div", "paste-actions");
  // v0.6.13：普通卡片编辑可「归档」（活跃区移入归档区；归档参与 WebDAV 同步、可「含归档」查看）
  if (!c.archived) {
    const archBtn = el("button", "btn btn-close", "归档"); // v0.6.13：与「取消」同尺寸同风格(小按钮)
    archBtn.title = "移入归档区——「含归档」可查看，可恢复或删除";
    archBtn.onclick = guard(archBtn, async () => {
      if (!await askConfirmP("将该条目移入归档？归档后可「含归档」查看，可随时恢复。", "归档")) return;
      const r = await api("/api/clips/" + c.id + "/archive", { method: "POST" }).catch((e) => { errToast(e.message); return null; });
      if (r) { m.remove(); flash("已归档"); refreshList(); }
    });
    actions.append(archBtn);
  }
  const ok = el("button", "btn primary", "保存");
  const cancel = el("button", "btn btn-close", "取消");
  actions.append(ok, cancel);
  modal.append(actions);
  ok.onclick = guard(ok, async () => {
    try {
      const json = { title: title.value, tags: [...editTagsSel], expire: expSel.value };
      if (contentInput) json.content = contentInput.value;
      if (urlInput) json.url = urlInput.value;
      // v0.6.11：富文本条目编辑纯文本后 html 必须同步——此前只改 content，html 还是旧内容，
      // 卡片右栏预览与「复制带格式」拿到的都是旧文本（左右不一致 bug）。content 变了 → 按新文本重建 html。
      // v0.6.13：清除格式 → html 置空转纯文本（后端 sanitizeHtml 空串即清空）
      if (c.type === "text" && c.html && contentInput) {
        json.html = clearFormat ? "" : (contentInput.value !== (c.content || "") ? textToHtml(contentInput.value) : c.html);
      }
      await api("/api/clips/" + c.id, { method: "PUT", json }).catch(e => { errToast(e.message); return null; });
      m.remove(); flash("已保存"); refreshList();
    } catch (e) { errToast(e.message); }
  });
  cancel.onclick = () => m.remove();
  m.append(modal); root.append(m);
  title.focus(); // UI 走查 U-4：编辑弹窗聚焦标题框
  title.onkeydown = (e) => { if (e.key === "Enter" && !contentInput) ok.click(); }; // 文本 tab 内容区不拦截回车（可换行）
}

// ---------- 密码管理（v0.4.2：从设置拆出独立入口） ----------
function openPasswordModal() {
  const root = $("#modal-root");
  root.innerHTML = "";
  const m = el("div", "mask");
  const modal = el("div", "modal pw-modal"); // 方案22 极简聚焦
  // 头部：小锁图标 + 标题 + 用户名胶囊
  const head = el("div", "head");
  head.append(el("div", "lock", "🔑"), el("h3", "", "修改密码"), el("span", "who", state.current.name));
  modal.append(head, el("div", "sub", "原密码验证身份 · 新密码至少 4 位"));

  // 密码输入字段（下划线式 + 浮动标签 + 👁 显隐切换）
  const mkField = (label) => {
    const field = el("div", "field");
    const input = el("input");
    input.type = "password";
    input.placeholder = " "; // 触发 :not(:placeholder-shown) 浮动标签
    const pl = el("span", "pl", label);
    const eye = el("button", "eye", "👁");
    eye.type = "button";
    eye.onclick = () => { input.type = input.type === "password" ? "text" : "password"; eye.textContent = input.type === "password" ? "👁" : "🙈"; };
    field.append(input, pl, eye);
    return { field, input };
  };
  const oldP = mkField("原密码（未设置可留空）");
  const newP = mkField("新密码");
  modal.append(oldP.field, newP.field);

  // 保存
  const save = el("button", "save", "保存新密码");
  save.onclick = guard(save, async () => {
    try {
      await api("/api/users/" + state.current.id + "/password", {
        method: "POST", json: { oldPassword: oldP.input.value, newPassword: newP.input.value },
      });
      flash("密码已更新"); oldP.input.value = ""; newP.input.value = "";
    } catch (e) { errToast(e.message); }
  });
  modal.append(save);

  // 底部：安全提示 + 关闭
  const foot = el("div", "foot");
  const close = el("button", "close", "关闭");
  close.onclick = () => m.remove();
  foot.append(el("span", "hint", "仅存哈希 · 不泄露原文"), close);
  modal.append(foot);
  m.append(modal); root.append(m);
}

// ---------- 数据管理（v0.4.2：从设置拆出独立入口——预览/备份/清空/删号；v0.6.5 方案25 双栏工作台） ----------
function openDataModal() {
  const root = $("#modal-root");
  root.innerHTML = "";
  const m = el("div", "mask");
  const modal = el("div", "modal dm-modal");
  // 头部
  const head = el("div", "dm-head");
  head.append(el("div", "ic", "🗂️"), el("h3", "", "数据管理"), el("span", "who", state.current.name));
  modal.append(head);

  const body = el("div", "dm-body");
  // ===== 左栏：设置与同步 =====
  const left = el("div", "dm-col");
  const lt = el("div", "dm-col-t"); lt.append(el("span", "n", "1"), el("span", "", "设置与同步"));
  left.append(lt);
  // 图片缩放步长
  const zoomRow = el("div", "", ""); zoomRow.style.cssText = "display:flex;gap:8px;align-items:center;margin-bottom:10px";
  const zoomLbl = el("span", "", "图片缩放步长"); zoomLbl.style.cssText = "font-size:11.5px;color:var(--muted);flex:1";
  const zoomStep = el("input"); zoomStep.type = "number"; zoomStep.min = "1"; zoomStep.max = "50"; zoomStep.step = "1";
  zoomStep.style.cssText = "width:76px;margin:0;flex-shrink:0";
  zoomStep.value = Math.round((LS.get("zoomStep", 0.15) || 0.15) * 100);
  const zoomSave = el("button", "dm-btn ghost", "保存"); zoomSave.style.cssText = "flex:0 0 auto;width:auto;padding:8px 14px;margin:0;border-radius:8px";
  zoomSave.onclick = guard(zoomSave, () => {
    let v = Math.max(1, Math.min(50, Math.round(Number(zoomStep.value) || 10)));
    zoomStep.value = v;
    LS.set("zoomStep", v / 100);
    flash("缩放步长已设为 " + v + "%");
  });
  zoomRow.append(zoomLbl, zoomStep, zoomSave);
  left.append(zoomRow);
  // v0.6.13 双名模型：账号名只读默认（身份键不可随意改，管理员级修改走确认弹窗），显示名可改（不影响 WebDAV 寻址/登录）
  const acctRow = el("div", ""); acctRow.style.cssText = "display:flex;gap:8px;align-items:center;margin-bottom:10px";
  const acctLbl = el("span", "", "账号名"); acctLbl.style.cssText = "font-size:11.5px;color:var(--muted);flex:1";
  const acctVal = el("span", "", state.current.accountName || state.current.name); acctVal.style.cssText = "font-size:12.5px;flex:1;text-align:right;color:var(--dim)";
  const acctBtn = el("button", "dm-btn ghost", "修改"); acctBtn.style.cssText = "flex:0 0 auto;width:auto;padding:8px 14px;margin:0;border-radius:8px";
  acctBtn.title = "管理员级操作：账号名是身份键（WebDAV 快照/跨设备迁移识别用），改动后下次同步自动迁移远端数据；日常改名请用「显示名」";
  acctBtn.onclick = guard(acctBtn, async () => {
    const cur = state.current.accountName || state.current.name;
    const newName = prompt("修改账号名（身份键，仅限英文和数字，不可随意改）\n\n改动后下次同步会自动把远端数据迁移到新账号名下。\n日常改名请用「显示名」。\n当前账号名: " + cur, cur);
    if (!newName || newName.trim() === cur) return;
    const r = await api("/api/users/" + state.current.id + "/account-name", { method: "POST", json: { name: newName } }).catch((e) => errToast(e.message));
    if (!r) return;
    state.current.accountName = r.name;
    flash("账号名已更新，下次同步自动迁移远端快照");
    m.remove(); render();
  });
  acctRow.append(acctLbl, acctVal, acctBtn);
  left.append(acctRow);
  const nameRow = el("div", ""); nameRow.style.cssText = "display:flex;gap:8px;align-items:center;margin-bottom:10px";
  const nameLbl = el("span", "", "显示名"); nameLbl.style.cssText = "font-size:11.5px;color:var(--muted);flex:1";
  const nameInput = el("input"); nameInput.value = state.current.name; nameInput.maxLength = 20;
  nameInput.style.cssText = "width:130px;margin:0;flex-shrink:0";
  const nameSave = el("button", "dm-btn ghost", "保存"); nameSave.style.cssText = "flex:0 0 auto;width:auto;padding:8px 14px;margin:0;border-radius:8px";
  nameSave.onclick = guard(nameSave, async () => {
    const r = await api("/api/users/" + state.current.id + "/name", { method: "POST", json: { name: nameInput.value } }).catch((e) => errToast(e.message));
    if (!r) return;
    state.current.name = r.name; // 同步本地状态（顶栏 who / 头部展示用）——后端已改，刷新时 /api/users/me 亦取最新，无需写缓存
    flash("显示名已更新");
    m.remove(); render();
  });
  nameRow.append(nameLbl, nameInput, nameSave);
  left.append(nameRow);
  // WebDAV 配置区（渲染进左栏）
  left.append(el("div", "dm-divider", "WebDAV 跨设备同步"));
  renderWebdavSection(left);
  body.append(left);

  // ===== 右栏：备份与风险 =====
  const right = el("div", "dm-col");
  const rt = el("div", "dm-col-t"); rt.append(el("span", "n", "2"), el("span", "", "备份与风险"));
  right.append(rt);
  // 导出 / 导入（本地文件备份，v0.2.0——不依赖 WebDAV 的换机/归档方案）
  const expBtn = el("button", "dm-btn gold", "导出全部");
  const impBtn = el("button", "dm-btn ghost", "导入合并");
  const bakStatus = el("div", "dm-status");
  const bakRow = el("div", "", ""); bakRow.style.cssText = "display:flex;gap:10px";
  bakRow.append(expBtn, impBtn);
  right.append(bakRow, bakStatus);
  expBtn.onclick = guard(expBtn, async () => {
    bakStatus.textContent = "导出中…";
    try {
      const r = await api("/api/export");
      const name = "clipboard-" + (state.current.name || "backup") + "-" + new Date().toISOString().slice(0, 10) + ".json";
      const blob = new Blob([JSON.stringify(r.data, null, 2)], { type: "application/json" });
      const u = URL.createObjectURL(blob);
      const a = document.createElement("a"); a.href = u; a.download = name;
      document.body.appendChild(a); a.click(); a.remove();
      URL.revokeObjectURL(u);
      bakStatus.textContent = "已导出 " + r.data.clips.length + " 条（含归档）";
      flash("导出成功");
    } catch (e) { bakStatus.textContent = "❌ " + e.message; }
  });
  impBtn.onclick = () => {
    const fi = el("input"); fi.type = "file"; fi.accept = ".json,application/json";
    fi.onchange = async () => {
      const file = fi.files[0]; if (!file) return;
      bakStatus.textContent = "导入中…";
      try {
        const text = await file.text();
        const parsed = JSON.parse(text);
        const payload = parsed && parsed.clips ? parsed : (parsed && parsed.data && parsed.data.clips ? parsed.data : null);
        if (!payload) throw new Error("不是剪贴板备份文件");
        const r = await api("/api/import", { json: { data: payload } });
        bakStatus.textContent = "导入完成：新增 " + r.added + " 条，更新 " + (r.updated || 0) + " 条，跳过 " + r.skipped + " 条，共 " + r.total + " 条";
        flash("导入完成");
        await loadClips(); renderTagbar(); renderList();
      } catch (e) { bakStatus.textContent = "❌ " + e.message; }
    };
    fi.click();
  };

  // 危险操作区
  const dangerZone = el("div", "dm-danger-zone");
  dangerZone.append(el("div", "dz-t", "⚠ 危险操作 · 不可恢复"));
  // 全部清空：清空不记墓碑（= 想从网上同步，下次 WebDAV 同步从远端恢复）
  const clrBtn = el("button", "dm-btn danger", "全部清空");
  clrBtn.onclick = () => {
    askConfirm("确定全部清空？该用户所有条目将被清除；若已配置 WebDAV 备份，下次「一键同步」会从远端恢复。", guard(clrBtn, async () => {
      try {
        const r = await api("/api/clips", { method: "DELETE" });
        await loadClips(); renderTagbar(); renderList();
        flash(r.cleared ? "已全部清空（" + r.cleared + " 条）" : "已清空（本来就没有内容）");
      } catch (e) { errToast(e.message); }
    }), "全部清空");
  };
  dangerZone.append(clrBtn, el("div", "dm-note", "清空不传播删除——已做 WebDAV 备份则下次同步从远端恢复"));
  // 删除账号
  const delBtn = el("button", "dm-btn danger", "删除我的账号");
  delBtn.onclick = () => {
    askConfirm("确定删除账号？该用户所有数据将被永久清除！", guard(delBtn, async () => {
      try {
        await api("/api/users/" + state.current.id, { method: "DELETE" });
        LS.del("cur"); state.current = null;
        m.remove(); await loadUsers(); render(); flash("账号已删除");
      } catch (e) { errToast(e.message); }
    }), "永久删除");
  };
  dangerZone.append(delBtn, el("div", "dm-note", "条目与文件一并永久清除"));
  right.append(dangerZone);
  body.append(right);
  modal.append(body);

  // 底部
  const foot = el("div", "dm-foot");
  const close = el("button", "", "关闭"); close.className = "close"; close.style.cssText = "border:none;background:none;color:var(--dim);font-size:12px;cursor:pointer;padding:6px 14px;border-radius:99px";
  close.onclick = () => m.remove();
  foot.append(el("span", "hint", "数据仅存本地 JSON · 密码只存哈希"), close);
  modal.append(foot);
  m.append(modal); root.append(m);
}

// ---------- WebDAV 配置区（v0.4.3：从 openDataModal 拆出独立函数——单一职责，架构评估 v2 #2） ----------
// 参考 edge-multi-account-cookie 设计：墓碑同步/清空不传播/双向取最新
// v0.6.5：适配方案25 双栏工作台（渲染进左栏容器，使用 dm- 类）
function renderWebdavSection(container) {
  // v0.6.13：同步全部数据（含归档），无需额外说明文字（用户要求 UI 简洁）；地址留空默认局域网服务器
  const DAV_DEFAULT_URL = "http://192.168.2.1:6086";
  const davUrl = el("input"); davUrl.placeholder = "服务器地址（留空默认 " + DAV_DEFAULT_URL + "）";
  const davUser = el("input"); davUser.placeholder = "用户名";
  const davPass = el("input"); davPass.type = "password"; davPass.placeholder = "密码（留空复用已保存）";
  container.append(davUrl, davUser, davPass);
  // 实体同步 + 自动同步选项
  const davFiles = el("input"); davFiles.type = "checkbox";
  davFiles.checked = true; // v0.7.6：默认开启（此前默认关闭 → 另一台设备图片/附件下载 404）
  // v0.7.6：文案补上「关闭的后果」——旧文案只说「也备份」，用户看不出关掉会导致他端用不了
  const davFilesLbl = el("label", "dm-opt", "");
  davFilesLbl.append(davFiles, " 同步文件实体（图片/附件；关闭后不备份上云，但本机仍会尝试从云端拉回缺失的实体）");
  const davAuto = el("input"); davAuto.type = "checkbox";
  const davInt = el("select");
  // v0.6.11：选项含 30 分钟——此前只有 1/6/12/24 小时，后端允许最小 30 分钟，
  // 用户保存 30 分钟后重开设置被 round 成 1 小时再保存变成 60 分钟（间隔漂移 bug）
  for (const [h, l] of [[0.5, "30 分钟"], [1, "1 小时"], [6, "6 小时"], [12, "12 小时"], [24, "24 小时"]]) {
    const o = el("option", "", l); o.value = h; davInt.append(o);
  }
  davInt.value = 12; // 默认 12 小时
  const davAutoLbl = el("label", "dm-opt", ""); davAutoLbl.append(davAuto, " 自动同步 每", davInt);
  container.append(davFilesLbl, davAutoLbl);
  // 保存配置：紧跟输入区（填完信息→保存落盘），最后才是一键同步
  const testSave = el("button", "dm-btn gold", "保存配置");
  container.append(testSave);
  const davSync = el("button", "dm-btn ghost", "一键同步");
  davSync.style.marginTop = "10px"; // 与「保存配置」保持合理间隙
  const davStatus = el("div", "dm-status");
  container.append(davSync, davStatus);
  (async () => {
    try {
      const r = await api("/api/sync/config");
      if (r.configured) {
        davUrl.value = r.url; davUser.value = r.user;
        davFiles.checked = r.syncFiles !== false; // v0.7.6：缺省/未配置视为开启（存量 false 尊重其选择）
        davAuto.checked = !!r.autoSync;
        // v0.6.11：精确读回间隔（分钟→小时，30 分钟=0.5）——旧实现 Math.round 把 30 分钟变 1 小时
        davInt.value = String((r.intervalMin || 720) / 60);
        // P-104：自动同步上次失败不再静默——状态区展示失败原因（成功则正常显示）
        davStatus.textContent = "已配置：" + r.url + (r.autoSync ? " · 每 " + davInt.value + " 小时自动同步" : "");
        if (r.lastSyncError) { davStatus.textContent += " · ⚠ 上次自动同步失败：" + r.lastSyncError; davStatus.classList.add("err"); }
        else davStatus.classList.add("ok");
      }
    } catch {}
  })();
  // 保存配置（已移至输入区下方，此处仅绑定事件）
  testSave.onclick = guard(testSave, async () => {
    // v0.6.13：地址留空默认填入局域网服务器地址
    if (!davUrl.value.trim()) davUrl.value = DAV_DEFAULT_URL;
    davStatus.textContent = "测试中…";
    try {
      await api("/api/sync/config", { method: "POST", json: { url: davUrl.value, user: davUser.value, pass: davPass.value, syncFiles: davFiles.checked, autoSync: davAuto.checked, intervalMin: Math.round(parseFloat(davInt.value) * 60) } });
      davStatus.textContent = "已保存：" + davUrl.value + (davAuto.checked ? " · 每 " + davInt.value + " 小时自动同步" : "") + (davFiles.checked ? " · 含文件实体" : "");
      davStatus.classList.remove("err"); davStatus.classList.add("ok");
      flash("WebDAV 配置已保存");
    } catch (e) { davStatus.textContent = "❌ " + e.message; davStatus.classList.add("err"); }
  });
  davSync.onclick = guard(davSync, async () => {
    davStatus.textContent = "同步中…";
    try {
      const r = await api("/api/sync/run", { method: "POST" });
      davStatus.textContent = "同步完成：远端" + (r.remoteExisted ? "有备份" : "无备份") + (r.uploaded ? " · 已上传" : " · 本地空跳过上传") + "，共 " + r.clips + " 条 / " + r.tombstones + " 墓碑";
      davStatus.classList.remove("err"); davStatus.classList.add("ok");
      await loadClips(); renderTagbar(); renderList();
      flash("同步完成");
    } catch (e) { davStatus.textContent = "❌ " + e.message; davStatus.classList.add("err"); }
  });
}

// ---------- 启动 ----------
// UI 走查 U-3：全局弹窗交互——点遮罩关闭 + Esc 关闭（事件委托，所有弹窗生效）
$("#modal-root").addEventListener("click", (e) => {
  if (e.target.classList && e.target.classList.contains("mask")) e.target.remove();
});
document.addEventListener("keydown", (e) => {
  if (e.key !== "Escape") return;
  const masks = document.querySelectorAll("#modal-root .mask");
  const last = masks[masks.length - 1];
  if (last) last.remove();
});
// v0.6.18 快捷键调整（Ctrl+V 改走 paste 捕获——clipboardData 在手势内直接可用，无需 Async Clipboard read 权限；
// 原 keydown + readText 方案在 iframe/平台挂载等未授权 clipboard-read 的环境下读回空，导致弹窗开了但内容没自动填入）：
//  - Ctrl+V：主页面无弹窗、焦点不在输入区时 → 打开「存入」弹窗，用 paste 事件数据直接填入（文本/富文本/图片/文件）
//  - 空格：主页面无弹窗、焦点不在输入/交互元素时 → 光标快速定位到搜索框（打字即检索）；空格本身不落入搜索框
// 守卫：已登录 / 无弹窗 / 焦点不在文本类或交互元素（避免吞掉搜索框/卡片上的粘贴与空格）/ 仅主页面
document.addEventListener("paste", (e) => {
  if (!state.current) return;                       // 未登录
  if ($(".mask")) return;                            // 已有弹窗不覆盖
  const t = e.target;
  if (t && (t.tagName === "INPUT" || t.tagName === "TEXTAREA" || t.tagName === "SELECT" || t.isContentEditable)) return; // 输入场景保留原生粘贴
  if (!$(".store-btn")) return;                      // 不在主页面
  const cd = e.clipboardData;
  if (!cd) return;
  e.preventDefault();                                // 接管（焦点在不可编辑区域，默认粘贴本就无动作）
  // 同步提取（DataTransfer 无需权限）：图片/文件优先，其次 html 与文本
  let file = null;
  if (cd.items) { for (const it of cd.items) { if (it.type && it.type.startsWith("image/")) { file = it.getAsFile(); break; } } }
  if (!file && cd.files && cd.files.length) file = cd.files[0];
  let html = "";
  try { const h = cd.getData("text/html"); if (h && h.length < 512 * 1024) html = h; } catch {}
  openPasteModal(false, { text: cd.getData("text/plain") || "", html, file });
});
document.addEventListener("keydown", (e) => {
  // —— 空格 → 快速定位光标到搜索框 ——
  if (e.code !== "Space") return;
  if (!state.current) return;                         // 未登录
  if ($(".mask")) return;                            // 已有弹窗不覆盖
  const t = e.target;
  if (t && (t.tagName === "INPUT" || t.tagName === "TEXTAREA" || t.tagName === "SELECT" || t.tagName === "BUTTON" || t.tagName === "A" || t.isContentEditable)) return; // 输入/交互元素保留原生行为（空格不误触、不吞卡片回车）
  if (!$(".store-btn")) return;                      // 不在主页面（用户选择页无存入按钮）
  e.preventDefault();                                // 阻止空格滚动
  const sb = $(".search");
  if (sb) { sb.focus(); sb.select(); }               // 聚焦并全选，直接输入即新检索
});
async function boot() {
  state.cols = LS.get("cols", "auto") || "auto"; // 恢复列数偏好
  if (LS.get("mono")) document.documentElement.classList.add("mono"); // v0.6.13：恢复无饱和度配色记忆
  // 用户选择页：点击空白处退出编辑模式（v0.3.1；#view 常驻，事件委托只绑一次）
  $("#view").addEventListener("click", (e) => {
    if (!userEditMode) return;                       // 非编辑模式忽略
    if (e.target.closest(".user-card")) return;      // 点卡片本体 → 正常进入用户
    if (e.target.closest(".del-user-btn")) return;   // 点删除按钮 → 由按钮处理
    if (e.target.closest(".edit-all-btn")) return;   // 点编辑按钮 → 由按钮处理
    if (e.target.closest(".add-user")) return;       // 点新建用户 → 由按钮处理
    setUserEditMode(false);                          // 其余空白处 → 退出编辑模式
  });
  // ⑦ 剪贴板监听（2025 新 API，支持才绑定）：检测到新复制内容 → 直接弹出存入大窗口并自动填入
  if (navigator.clipboard && typeof navigator.clipboard.addEventListener === "function") {
    try {
      // v0.4.2/v0.4.3 触发逻辑（顺序：先比对，再决定弹什么；比对抽纯函数 findDuplicateClip 可单测）：
      //  1. 复制触发 → 先读剪贴板文本 → 与仓库比对（findDuplicateClip）
      //  2. 重复 → 直接弹带「已有相同内容」提示的编辑页（不弹存入窗）
      //  3. 不重复 → 正常弹存入窗并自动填入
      //  4. 卡片点击复制 → suppressAutoPasteUntil 窗口期内不弹（来源抑制）
      navigator.clipboard.addEventListener("clipboardchange", async () => {
        if (!state.current) return;    // 未登录不弹
        if (Date.now() < suppressAutoPasteUntil) return; // 来源抑制：卡片复制引起的变化不自动弹窗
        if ($(".mask")) return;        // 已有任何弹窗（编辑/设置等）时不覆盖
        let t = "";
        try {
          if (navigator.clipboard && navigator.clipboard.readText) t = (await navigator.clipboard.readText().catch(() => "")) || "";
        } catch { t = ""; }
        const content = String(t).trim();
        // 第一步：比对数据（纯函数，无副作用）
        const dup = content ? findDuplicateClip(content, state.clips) : null;
        if (dup) { // 重复 → 弹带提示的编辑页
          if (!dup.archived) openEditModal(dup, true);
          else flash("已有相同内容");
          return;
        }
        // 不重复 → 正常弹存入窗（内部自动填入剪贴板文本/图片）
        openPasteModal(true);
      });
    } catch {}
  }
  // 恢复上次登录（token 失效则回选用户页；显式传 token——否则校验请求不带鉴权永远 401）
  // v0.6.13 治本：LS.cur 仅作「会话凭据缓存」{id, token}；展示信息（name/color）以
  // 后端为唯一权威源——恢复登录时必然从 /api/users/me 拉最新值填充（改名/多端刷新一致，
  // 缓存永不"过期"回落）。旧缓存里若残留 name 仅作拉取失败时的兜底展示，不做权威。
  const saved = LS.get("cur", null);
  if (saved && saved.token) {
    try {
      const r = await api("/api/clips", { token: saved.token });
      state.current = { id: saved.id, token: saved.token, name: saved.name || "" };
      state.clips = r.clips;
      const t = await api("/api/tags", { token: saved.token });
      state.tags = t.tags;
      try {
        const me = await api("/api/users/me", { token: saved.token });
        if (me && me.user) { state.current.name = me.user.name; state.current.accountName = me.user.accountName; state.current.color = me.user.color; }
      } catch { /* 权威拉取失败：保留兜底旧名展示，不阻塞登录 */ }
    } catch { LS.del("cur"); }
  }
  if (!state.current) {
    try { await loadUsers(); } catch (e) { errToast("无法连接服务: " + e.message); } // 走查 P-9：网络异常不白屏
  }
  render();
}
boot();

