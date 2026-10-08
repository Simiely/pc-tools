# 剪贴板 · 暗黑新拟态 UI 迁移手册

> 目标：把 `public/index.html` 的现有视觉（粉色系描边风格）迁移为**暗黑新拟态**（双阴影浮雕风格）
> 设计稿：`neumorph-design-preview.html`（已评审定稿，阴影偏移统一 1px）
> 方法：**模块化逐层替换**——先换设计令牌(层0)，再换基础组件(层1)，再换页面布局(层2)，再换卡片组件(层3)，最后换弹窗/浮层(层4)。每层独立可回归，避免一次性大改出错。
> 约束：**类名/结构/JS 全部不动**，只替换 `<style>` 块与内联样式色值——JS 生成的所有类名保持原样，零 JS 改动。

---

## 迁移总则

1. **只改 `public/index.html` 的 `<style>` 段**，`app.js` 一行不动
2. 所有视觉参数（色板/阴影/圆角）收敛到 `:root` 设计令牌，组件不写死
3. 新拟态铁律：**无 1px 边框**（轮廓由双阴影定义）、**凸起=外阴影、凹陷=内阴影**、**按压=阴影反转**
4. 每层改完验证：`node --check` + 浏览器打开 `http://127.0.0.1:8130/` 目检该层组件

---

## 层 0 · 设计令牌（:root）

替换整个 `:root` 块。旧变量 → 新变量映射：

| 旧变量 | 旧值 | 新变量 | 新值 | 用途 |
|---|---|---|---|---|
| `--bg` | `#17121a` | `--bg` | `#1c1f26` | 页面背景 |
| `--surface` | `#221b28` | `--elev` | `#232730` | 凸起面板/卡片 |
| `--surface2` | `#2c2333` | `--elev-hi` | `#272c35` | 面板 hover |
| — | — | `--inset` | `#191c22` | 凹陷区（输入框/轨道） |
| `--border` | `#3a3042` | `--border` | `#2c313b` | 高光边（双阴影用） |
| `--text` | `#f2edf4` | `--text` | `#d3d8e2` | 主文字 |
| `--muted` | `#a89ab4` | `--muted` | `#9aa0b0` | 次级文字 |
| `--dim` | `#7d6f88` | `--dim` | `#8a90a0` | 弱化文字 |
| `--pink` | `#ff9292` | `--accent` | `#7f9dff` | 主强调（原粉→蓝紫） |
| `--pink-dim` | `#b36060` | `--accent2` | `#a3b9ff` | 强调浅色 |
| `--green` | `#86efac` | `--green` | `#8fd6a8` | 文本类型徽章 |
| `--amber` | `#fcd34d` | `--amber` | `#e8b45a` | 星标/警示 |
| `--blue` | `#7dd3fc` | `--blue` | `#7aa2ff` | 链接类型徽章 |
| `--danger` | `#f87171` | `--red` | `#e07a7a` | 危险/删除 |

**新增圆角令牌**：`--r-md:14px; --r-lg:18px; --r-pill:99px`

**新增阴影令牌（定稿 v5，偏移 1px）**：
```css
--sh-raised:  1px 1px 3px rgba(0,0,0,.5),  -1px -1px 3px rgba(44,49,59,.46);
--sh-raised-sm:1px 1px 3px rgba(0,0,0,.45), -1px -1px 3px rgba(44,49,59,.42);
--sh-raised-lg:1px 1px 5px rgba(0,0,0,.52), -1px -1px 5px rgba(44,49,59,.48);
--sh-inset:  inset 1px 1px 4px rgba(0,0,0,.5),  inset -1px -1px 4px rgba(44,49,59,.44);
--sh-inset-sm:inset 1px 1px 3px rgba(0,0,0,.45), inset -1px -1px 3px rgba(44,49,59,.4);
--sh-press:  inset 1px 1px 4px rgba(0,0,0,.52), inset -1px -1px 4px rgba(44,49,59,.48);
```

**语义映射**（关键：程序中所有 `var(--pink)` 引用 → 换 `--accent`；`var(--surface)` → `--elev`；`var(--surface2)` → `--inset`；`var(--danger)` → `--red`）

---

## 层 1 · 基础组件

### 1.1 全局元素

| 选择器 | 旧 | 新 |
|---|---|---|
| `button` | `background:transparent` | 不变（按钮用 .btn 类） |
| `input,textarea,select` | `background:var(--surface2); border:1px solid var(--border); border-radius:8px` | `background:var(--inset); border:none; border-radius:12px; box-shadow:var(--sh-inset-sm)` |
| `input:focus` | `border-color:var(--pink)` | `box-shadow:var(--sh-inset)` |

### 1.2 按钮 .btn（四态）

| 状态 | 旧 | 新 |
|---|---|---|
| 默认 | `border:1px solid var(--border); border-radius:8px` | `background:var(--elev); box-shadow:var(--sh-raised-sm); border-radius:12px; color:var(--muted)` |
| hover | `border-color:var(--pink); color:var(--pink)` | `background:var(--elev-hi); color:var(--text)` |
| active(新增) | 无 | `box-shadow:var(--sh-press); transform:translateY(1px)` |
| `.primary` | `background:var(--pink); color:#24101a` | `background:var(--accent); color:#10131a; font-weight:600` |
| `.primary:hover` | `background:#ffb0b0` | `background:var(--accent2)` |
| `.ghost` | `color:var(--muted)` | `box-shadow:none; background:transparent`（hover → `color:var(--accent2)`） |
| `.danger:hover` | `border-color:var(--danger); color:var(--danger)` | `color:var(--red)` |

### 1.3 徽章 .badge 及其类型色

| 选择器 | 旧 | 新 |
|---|---|---|
| `.badge` | `background:var(--surface2); color:var(--muted)` | `background:var(--elev); box-shadow:var(--sh-raised-sm)` |
| `.badge.link` | `color:var(--blue)` | `color:var(--blue)`（值换新） |
| `.badge.file` | `color:var(--amber)` | `color:var(--amber)`（值换新） |
| `.badge.text` | `color:var(--green)` | `color:var(--green)`（值换新） |
| `.badge.exp` | `color:var(--pink)` | `color:var(--red)` |

### 1.4 标签 .tag

| 状态 | 旧 | 新 |
|---|---|---|
| 默认 | `border:1px solid var(--border); border-radius:99px` | `background:var(--elev); box-shadow:var(--sh-raised-sm); border-radius:99px` |
| `.on` | `background:var(--pink); color:#24101a` | `background:var(--accent); color:#10131a; font-weight:600` |

---

## 层 2 · 页面布局

### 2.1 容器 .view
`max-width:720px` → `max-width:960px`（新拟态需呼吸感；其余不变）

### 2.2 用户选择页

| 选择器 | 旧 | 新 |
|---|---|---|
| `.logo` | 文字 emoji | `width:74px;height:74px;border-radius:24px;display:flex;align-items:center;justify-content:center;font-size:34px;background:var(--elev);box-shadow:var(--sh-raised)`（emoji 居中显示） |
| `.user-card` | `background:var(--surface); border:1px solid var(--border); border-radius:14px` | `background:var(--elev); box-shadow:var(--sh-raised); border-radius:var(--r-lg)` |
| `.user-card:hover` | `border-color:var(--pink); transform:translateY(-2px)` | `background:var(--elev-hi); transform:translateY(-2px); box-shadow:var(--sh-raised-lg)` |
| `.user-card .avatar` | `background:u.color(内联)` | 不变（保留内联背景色）；加 `box-shadow:var(--sh-inset-sm)` 凹陷感 |
| `.user-card .del-user-btn` | `border:1px solid var(--danger)` | `background:var(--elev); box-shadow:var(--sh-raised-sm); color:var(--red)` |
| `.add-user` | `border:1px dashed var(--border)` | `background:var(--inset); box-shadow:var(--sh-inset-sm); color:var(--accent2)`（虚线→凹陷块） |

### 2.3 主页面顶栏 .topbar
`margin-bottom:18px` → `padding:14px 20px; border-radius:var(--r-lg); background:var(--elev); box-shadow:var(--sh-raised)`（整条浮雕面板）

### 2.4 存入入口 .paste-trigger
`border:1px dashed var(--border); background:var(--surface)` → `background:var(--elev); box-shadow:var(--sh-raised); border-radius:var(--r-md)`（hover → `color:var(--accent2); background:var(--elev-hi)`）

### 2.5 工具栏 .toolbar

| 元素 | 旧 | 新 |
|---|---|---|
| `input[type=search]` | 描边方框 | `border-radius:var(--r-pill); padding-left:36px`（胶囊凹陷，配合内嵌阴影） |
| `select` | `background:var(--surface2)` | `background:var(--inset); box-shadow:var(--sh-inset-sm); appearance:none` + 下拉箭头 SVG |
| `.opt`(含归档) | checkbox | checkbox 保留（可后续换 switch） |

### 2.6 类型 Tab .typetab（分段控件化）

| 选择器 | 旧 | 新 |
|---|---|---|
| `.typetab` | 无容器背景 | `display:inline-flex; gap:6px; padding:6px; border-radius:14px; background:var(--inset); box-shadow:var(--sh-inset-sm)` |
| `.typetab .tt` | `border:1px solid var(--border); border-radius:99px` | `padding:6px 18px; border-radius:10px`（无边框） |
| `.typetab .tt.on` | `background:var(--pink); color:#24101a` | `background:var(--elev); box-shadow:var(--sh-raised-sm); color:var(--accent2); font-weight:600` |

### 2.7 标签栏 .tagbar / 标签管理按钮
- `.tagbar` 不变（间距可 `gap:8px`）
- `.tag-mgmt-btn`（JS 生成，CSS 未定义）：迁移时补一条 `.tagbar .tag-mgmt-btn{margin-left:auto}` 样式（原靠内联 `style.marginLeft`？——见附录 A 核实）

---

## 层 3 · 卡片组件 .clip-card

| 选择器 | 旧 | 新 |
|---|---|---|
| `.clip-card` | `background:var(--surface); border:1px solid var(--border); border-radius:14px; padding:12px 14px` | `background:var(--elev); box-shadow:var(--sh-raised); border-radius:var(--r-lg); padding:14px 16px; display:flex; flex-direction:column; gap:6px` |
| `.clip-card:hover` | `border-color:var(--pink)` | `background:var(--elev-hi); transform:translateY(-2px); box-shadow:var(--sh-raised-lg)` |
| `.clip-card.pinned` | `border-color:var(--amber)` | `box-shadow:1px 1px 3px rgba(0,0,0,.5),-1px -1px 3px rgba(44,49,59,.46),0 0 0 1.5px rgba(232,180,90,.55)` |
| `.clip-card .preview` | 纯文字 | `background:var(--inset); box-shadow:var(--sh-inset-sm); border-radius:10px; padding:8px 10px` |
| `.preview.img-thumb` | `background:var(--surface2)` | `background:var(--inset)`（padding:8px） |
| `.clip-card .meta` | 不变 | 不变（间距 gap:10px → 8px 可选） |
| `.clip-card .ops` | 不变 | 不变（gap:4px → 5px 可选） |
| 操作按钮(JS make*Btn) | 无专门类(继承.btn.sm) | 迁移时补 `.clip-card .ops .btn{padding:3px 8px;border-radius:8px;background:var(--elev);box-shadow:var(--sh-raised-sm)}`（原为继承 .btn 描边） |

---

## 层 4 · 弹窗 / 浮层

### 4.1 遮罩 .mask
`background:rgba(0,0,0,.55)` → `background:rgba(10,12,16,.66); backdrop-filter:blur(3px)`

### 4.2 弹窗 .modal
`background:var(--surface); border:1px solid var(--border); border-radius:16px; max-width:440px` → `background:var(--elev); box-shadow:var(--sh-raised-lg); border-radius:var(--r-lg); max-width:450px; padding:24px`

### 4.3 弹窗内元素

| 选择器 | 旧 | 新 |
|---|---|---|
| `.modal input/textarea` | 继承全局 | 继承层1（inset 凹陷） |
| `.modal .form-row` | `display:flex;gap:8px` | 不变 + `.form-row .btn{flex:1}` |
| `.dup-tip` | `background:var(--surface2); border-radius:8px` | `background:var(--inset); box-shadow:var(--sh-inset-sm); color:var(--amber)` |
| `.dropzone` | `border:1px dashed var(--border)` | `background:var(--inset); box-shadow:var(--sh-inset-sm); border-radius:12px` |
| `.file-chip` | `background:var(--surface2)` | `background:var(--inset); box-shadow:var(--sh-inset-sm)` |
| `.tabs/.tab` | 描边 | 同 .typetab 分段控件方案 |

### 4.4 浮层 / 提示

| 选择器 | 旧 | 新 |
|---|---|---|
| `.img-hover-preview` | `background:var(--surface); border:1px solid var(--border)` | `background:var(--elev); box-shadow:var(--sh-raised-lg)` |
| `.copied-flash` | `background:var(--pink); color:#24101a` | `background:var(--accent); color:#10131a` |
| `.toast-err` | `background:var(--danger)` | `background:var(--red)` |
| `.empty` | 不变 | 不变 |

---

## 附录 A · JS 内联样式迁移清单（index.html 之外需注意的点）

以下样式在 `app.js` 中**硬编码**，迁移时逐一核对（新拟态下是否仍适用）：

| 位置(行号) | 内联样式 | 迁移动作 |
|---|---|---|
| renderMain archLbl | `display:flex;align-items:center;gap:4px;color:var(--muted)` | **必须改色值引用**：`var(--muted)` 在 :root 已换值，自动生效——无需改 JS |
| tag-mgmt 行 | `display:flex;gap:6px;align-items:center;margin-bottom:6px` | 结构样式，保留 |
| openImagePreview 大图 | `max-width:100%;max-height:62vh;border-radius:8px` | 保留（圆角可 8→10，可选） |
| openJsonPreview | `min-height:320px;...` | 保留（code 区可加 inset 背景，可选） |
| edit-all-btn | `position:absolute;top:6px;right:4px` | 保留 |

> **关键结论**：JS 中所有 `var(--xxx)` 内联样式都通过 :root 变量间接引用，**层0 换令牌后自动生效**，无需逐条改 JS。只有 `border-color:var(--pink)` 这类旧变量引用，靠层0 的"变量值替换"即全局解决。

---

## 附录 B · 迁移顺序与验证

```
第1步 层0 :root 令牌        → 全局底色/文字色变化，结构不变
第2步 层1 基础组件           → 按钮/输入/徽章/标签浮雕化
第3步 层2 页面布局           → 顶栏/选择页/工具栏/分段Tab
第4步 层3 卡片               → 卡片浮雕 + 预览凹陷
第5步 层4 弹窗/浮层          → modal/mask/提示
第6步 全量回归               → 冒烟34 + WebDAV19 + 自动同步3 + 浏览器目检
```

每步后执行：
```bash
node --check server.mjs && node --check public/app.js   # 语法
# 浏览器打开 http://127.0.0.1:8130/ 目检对应组件
```

---

## 附录 C · 变量名对照速查（写 CSS 时直接替换）

| 旧(代码中出现) | 新令牌 | 说明 |
|---|---|---|
| `var(--pink)` | `var(--accent)` | 主强调色 |
| `var(--surface)` | `var(--elev)` | 凸起面板 |
| `var(--surface2)` | `var(--inset)` 或 `var(--elev-hi)` | 凹陷(输入) / hover(面板) |
| `var(--danger)` | `var(--red)` | 危险 |
| `#24101a`(on-pink) | `#10131a` | 强调色上的深字 |
| `var(--border)` | `--border` 新值 | 双阴影高光 |
