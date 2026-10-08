// scripts/refactor-entry.mjs - 常驻大输入框 → 小入口 + 大弹窗（检测到复制自动弹出）
// 一次性重构脚本：改完即弃，保留备查
import fs from "node:fs";

const htmlPath = "public/index.html";
let html = fs.readFileSync(htmlPath, "utf8");

// ---------- 1) renderMain 内 paste-box 区块 → 小入口条 ----------
const s1 = "  // 万能入口（单一入口交互改造）";
const e1 = "  // 工具栏：搜索";
const i1 = html.indexOf(s1);
const j1 = html.indexOf(e1);
if (i1 < 0 || j1 < i1) throw new Error("区块1定位失败 " + i1 + " " + j1);
const smallEntry = [
  "  // 小入口：常态只占一行，点击或检测到复制内容时自动弹出大窗口（openPasteModal）",
  '  const trigger = el("div", "paste-trigger", "📥 存入内容 — 点击打开，复制内容后自动弹出");',
  "  trigger.onclick = () => openPasteModal();",
  "  v.append(trigger);",
  "",
].join("\n");
html = html.slice(0, i1) + smallEntry + html.slice(j1);

// ---------- 2) 插入 openPasteModal 函数（编辑弹窗之前） ----------
const s2 = "// ---------- 编辑弹窗 ----------";
const i2 = html.indexOf(s2);
if (i2 < 0) throw new Error("区块2定位失败");
const pasteModal = `// ---------- 存入大弹窗（万能入口：检测到复制内容自动弹出 / 点小入口手动打开） ----------
function openPasteModal(auto = false) {
  if ($(".paste-modal")) return; // 已打开不重复弹（连续复制时用户在弹窗内自行粘贴）
  const root = $("#modal-root");
  const m = el("div", "mask");
  const modal = el("div", "modal paste-modal");
  modal.style.maxWidth = "min(92vw, 660px)";
  modal.append(el("h3", "", "存入内容"));
  const pb = el("div", "paste-box");
  const ta = el("textarea"); ta.placeholder = "粘贴文本、链接，或拖文件到这里，一键存入…";
  ta.style.minHeight = "130px";
  const typeBadge = el("div", "typebadge");
  function updateBadge() {
    const content = ta.value.trim();
    if (pickedFile) { typeBadge.textContent = "将存为：文件"; return; }
    if (!content) { typeBadge.textContent = ""; return; }
    typeBadge.textContent = "将存为：" + (/^https?:\\/\\/\\S+$/i.test(content) ? "链接" : "文本");
  }
  ta.addEventListener("input", updateBadge);
  // Ctrl+Enter 快速存入（Enter 仍是换行）
  ta.addEventListener("keydown", (e) => {
    if (e.key === "Enter" && (e.ctrlKey || e.metaKey)) { e.preventDefault(); save.click(); }
  });
  // 粘贴识别：图片（截图/网页图）/ 文件（资源管理器复制的文件）→ 文件流程；纯文本照常
  ta.addEventListener("paste", (e) => {
    const cd = e.clipboardData;
    if (!cd) return;
    if (cd.items) {
      for (const item of cd.items) {
        if (item.type && item.type.startsWith("image/")) {
          e.preventDefault();
          const f = item.getAsFile();
          if (f) { pick(f); flash("已接收图片，点存入即可"); return; }
        }
      }
    }
    if (cd.files && cd.files.length) {
      e.preventDefault();
      pick(cd.files[0]);
      flash(cd.files.length > 1 ? "已接收第一个文件，其余请再次粘贴" : "已接收文件，点存入即可");
    }
  });
  const chipBox = el("div");
  let pickedFile = null;
  function pick(f) {
    if (f.size > 10 * 1024 * 1024) return errToast("文件超过 10MB 上限");
    pickedFile = f;
    chipBox.replaceChildren();
    chipBox.append(el("div", "file-chip", "📎 " + f.name + " · " + fmtSize(f.size)));
    updateBadge();
  }
  const fileBtn = el("button", "btn sm", "选择文件");
  fileBtn.onclick = () => { const fi = el("input"); fi.type = "file"; fi.onchange = () => { if (fi.files[0]) pick(fi.files[0]); }; fi.click(); };
  pb.ondragover = (e) => { e.preventDefault(); pb.classList.add("drag"); };
  pb.ondragleave = () => pb.classList.remove("drag");
  pb.ondrop = (e) => { e.preventDefault(); pb.classList.remove("drag"); if (e.dataTransfer.files[0]) pick(e.dataTransfer.files[0]); };

  // 高级选项：别名 / 标签选择器 / 过期
  const advBtn = el("button", "btn sm ghost", "高级 ▾");
  let advOpen = false;
  const advBox = el("div", "adv-box hidden");
  const advTitle = el("input"); advTitle.placeholder = "别名（可留空）";
  const advTagsWrap = el("div");
  const advTagsSel = [];
  const advExp = el("select");
  for (const [v, l] of [["", "永久"], ["1h", "1 小时后过期"], ["1d", "1 天后过期"], ["7d", "7 天后过期"], ["30d", "30 天后过期"]]) {
    const o = el("option", "", l); o.value = v; advExp.append(o);
  }
  advBtn.onclick = () => {
    advOpen = !advOpen;
    advBox.classList.toggle("hidden", !advOpen);
    advBtn.textContent = advOpen ? "高级 ▴" : "高级 ▾";
    if (advOpen) renderTagPicker(advTagsWrap, advTagsSel, state.tags, (s) => { advTagsSel.length = 0; advTagsSel.push(...s); });
  };
  advBox.append(advTitle, advTagsWrap, advExp);

  const pr = el("div", "paste-row");
  const save = el("button", "btn primary", "存入");
  const cancel = el("button", "btn ghost", "关闭");
  save.onclick = guard(save, async () => {
    const content = ta.value.trim();
    if (!content && !pickedFile) return errToast("先粘贴内容或选择文件");
    const adv = advOpen ? { title: advTitle.value, tags: [...advTagsSel], expire: advExp.value } : {};
    // 重复检测：文本/链接内容已存在时先确认（不打断文件条目）
    if (!pickedFile && state.clips.some(c => (c.type === "link" ? c.url === content : c.content === content))) {
      const go = await askConfirmP("已存在相同内容，仍要存入？", "仍要存入");
      if (!go) return;
    }
    try {
      if (pickedFile) {
        const fd = new FormData(); fd.append("file", pickedFile);
        const r = await api("/api/files", { method: "POST", body: fd });
        if (!r) return;
        await api("/api/clips", { method: "POST", json: { type: "file", fileId: r.file.fileId, fileName: r.file.fileName, fileSize: r.file.fileSize, fileMime: r.file.fileMime, title: adv.title || pickedFile.name, ...adv } });
      } else if (/^https?:\\/\\/\\S+$/i.test(content)) {
        await api("/api/clips", { method: "POST", json: { type: "link", url: content, ...adv } });
      } else {
        // 自动标题：未填别名时取首行前 20 字
        const autoTitle = content.split("\\n")[0].trim().slice(0, 20);
        await api("/api/clips", { method: "POST", json: { type: "text", title: adv.title || autoTitle, content, ...adv } });
      }
      m.remove(); // 成功关弹窗，刷新列表
      await loadClips(); renderTagbar(); renderList();
      flash("已存入");
    } catch (e) { errToast(e.message); }
  });
  cancel.onclick = () => m.remove();
  pr.append(advBtn, fileBtn, save, cancel);
  pb.append(ta, typeBadge, chipBox, advBox, pr);
  modal.append(pb);
  m.append(modal); root.append(m);
  ta.focus();

  // 打开时（点击手势内）自动填入剪贴板：文本优先，其次图片；文件读不到则按场景提示
  (async () => {
    try {
      if (navigator.clipboard && navigator.clipboard.readText) {
        const t = await navigator.clipboard.readText().catch(() => "");
        if (t && !ta.value && !pickedFile) { ta.value = t; updateBadge(); flash("已填入剪贴板内容"); return; }
      }
      if (navigator.clipboard && navigator.clipboard.read) {
        const items = await navigator.clipboard.read().catch(() => []);
        for (const item of items) {
          for (const type of item.types) {
            if (type.startsWith("image/")) {
              const blob = await item.getType(type);
              const f = new File([blob], "paste-image." + (type.split("/")[1] || "png"), { type });
              pick(f); flash("已接收图片，点存入即可"); return;
            }
          }
        }
      }
      if (auto) errToast("剪贴板不是文本/图片（文件请直接 Ctrl+V 粘贴或拖入输入区）");
    } catch {}
  })();
}

`;
html = html.slice(0, i2) + pasteModal + html.slice(i2);

// ---------- 3) boot 剪贴板监听：提示条 → 直接弹大窗口 ----------
const s3 = "  // ⑦ 剪贴板监听（2025 新 API，支持才绑定）：检测到新复制内容 → 显示\"点击填入\"提示条（图片/文本区分文案）";
const e3 = "      });\n    } catch {}\n  }";
const i3 = html.indexOf(s3);
if (i3 < 0) throw new Error("区块3定位失败");
const newListen = `  // ⑦ 剪贴板监听（2025 新 API，支持才绑定）：检测到新复制内容 → 直接弹出存入大窗口并自动填入
  if (navigator.clipboard && typeof navigator.clipboard.addEventListener === "function") {
    try {
      navigator.clipboard.addEventListener("clipboardchange", () => {
        if (!state.current) return;    // 未登录不弹
        if ($(".paste-modal")) return; // 弹窗已开不重复弹（用户在弹窗内自行粘贴）
        openPasteModal(true);          // 检测到复制 → 自动弹出大窗口并填入
      });
    } catch {}
  }`;
html = html.slice(0, i3) + newListen + html.slice(i3 + e3.length);

// ---------- 4) CSS：小入口条 + 弹窗内 paste-box 无下边距 ----------
const s4 = ".cb-hint:hover{border-color:var(--pink);background:var(--surface)}";
const i4 = html.indexOf(s4);
if (i4 < 0) throw new Error("区块4定位失败");
const cssAdd = s4 + "\n.paste-trigger{display:flex;align-items:center;justify-content:center;gap:6px;background:var(--surface);border:1px dashed var(--border);border-radius:12px;padding:10px;margin-bottom:16px;font-size:13px;color:var(--muted);cursor:pointer;transition:.15s}\n.paste-trigger:hover{border-color:var(--pink);color:var(--pink)}\n.paste-modal .paste-box{margin-bottom:0}";
html = html.slice(0, i4) + cssAdd + html.slice(i4 + s4.length);

fs.writeFileSync(htmlPath, html);
console.log("重构完成");
