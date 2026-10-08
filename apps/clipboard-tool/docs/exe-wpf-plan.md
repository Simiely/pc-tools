# Clipboard Tool · WPF exe 版开发方案

> 状态：草案 v1.0（2026-08-31）
> 目标：在 Web 版 v0.6.16（bat 形态）基础上，**原生重写**一个 Windows exe 桌面版。
> 约束：界面/交互最大程度还原 Web 版暗黑新拟态；新增**窗口置顶按钮**；数据格式与 Web 版互导。
> 前置：旧 WinForms 重写路线已废弃（错误路线，不参考、不恢复）。

---

## 0. 决策摘要

| 决策点 | 结论 | 依据 |
|---|---|---|
| 技术栈 | **C# WPF, net9.0-windows** | 稳定成熟首选（20 年生态）；深色主题为成熟能力（非实验 API）；数据绑定天然适合卡片墙 |
| 工程位置 | **主仓库子目录 `clipboard-exe/`（用户已确认，入主仓库方便统一管理）** | 版本/发布/文档与主项目同步 |
| 数据位置 | exe 同目录 `data/`（便携式） | 整个文件夹拷走即迁移，与 Web 版 JSON 互导 |
| 功能范围 | **MVP 单机核心 + WebDAV 同步** | 用户确认全量 |
| UI 还原 | Web 版设计令牌 → WPF ResourceDictionary + 自定义控件模板 | 像素级配色对齐（见 §3） |
| 发布形态 | **框架依赖单文件（~1-3MB），.NET 9 Desktop Runtime 另行安装（用户确认"小问题"）；不采用 NativeAOT** | 准确优先：NativeAOT 对 WPF 数据绑定有裁剪风险（微软官方 + 2026 深度分析一致警告，见 §9），装运行库零风险且体积最小 |
| 置顶按钮 | 工具栏 ★ 按钮 → `Window.Topmost` | 原生属性，一行切换（唯一新增功能，其余纯迁移） |

---

## 1. 形态与运行

- **形态**：Windows 桌面应用，托盘常驻，单实例，原生剪贴板监听（不依赖窗口焦点）。
- **运行**：`Clipboard.exe`（数据目录 exe 同目录 `data/`）。
- **依赖**：.NET 9 Desktop Runtime x64（本机 9.0.5 已装实测）；Windows 10/11。

## 1.5 还原度确认（准确迁移承诺）

> 用户核心要求：**完全还原 Web 版，纯迁移，不放飞，准确度优先**。以下为逐项确认清单（已精读 Web 版源码核对）。

### 1.5.1 规则类逻辑——逐字搬运，禁止改动

| Web 版实现 | 迁移规则（已核对源码） |
|---|---|
| `sortClips`（clips-store.js L79） | pinned 优先 → copyCount 降序 → updatedAt 降序；单一实现 |
| `rollToArchive`（L45） | 活跃区超 500 条 → 按 updatedAt 降序保留前 500，溢出按 **createdAt 升序**追加归档，**按 id 去重防膨胀**（v0.6.11 修复） |
| `isExpired` / `resolveExpire`（L88/93） | expireAt 非空且 < now；`'1h'\|'1d'\|'7d'\|'30d'` → 绝对时间戳 |
| `cleanUrl`（L113） | 24 个 TRACKING_KEYS（UTM + fbclid/gclid/msclkid 等），无追踪参数原样返回，畸形 URL 原样返回 |
| `sanitizeInput`（L134） | title 截 200；tags 去重/trim/截 20/上限 10 |
| `sanitizeHtml`（L72） | html 截 512KB |
| 去重（app.js `findDuplicateClip`） | 与最近一条相同内容 → 静默刷新置顶，不弹窗不产生重复 |
| 墓碑（tombstones.js） | 90 天 TTL；**仅已配置 WebDAV 时记录**（`recordTombstoneIfConfigured` 判断，未配置同步的用户删除不产生墓碑）；全部清空**不记墓碑**（清空=想从网上同步） |
| `sweepExpired`（clips-mutate.js L226） | 60s 周期；活跃+归档都扫；跳过 `.tombstones/.webdav` 等后缀文件；联动删除文件实体 |
| 配置常量（config.js） | MAX_TITLE 200 / MAX_CONTENT 200KB / MAX_HTML 512KB / MAX_TAGS 10 / MAX_TAG_LEN 20 / MAX_CLIPS_PER_USER 500 / ARCHIVE_SCAN_LIMIT 5000 / SWEEP_INTERVAL 60s / ID_RE（UUID 白名单）/ BLOCKED_MIME / BLOCKED_EXT / EXT_BY_MIME —— 原样迁移 |
| `mergeSnapshots`（webdav.js L214） | 墓碑优先 → 无墓碑按 updatedAt 取新 → 仅一侧保留；**对拍测试门禁**：移植后与 `scripts/test-merge-snapshot.mjs` 产出相同结果 |

### 1.5.2 数据格式——原样对齐

- 活跃区 `users/<userId>.json` + 归档 `users/<userId>.archive.json` + 墓碑 `users/<userId>.tombstones.json` + 文件实体 `files/<uid>/<fileId>.<ext>` + WebDAV 配置。
- 条目 17 字段（`publicClip` 视图）：`id/type/title/content/html/url/tags/fileId/fileName/fileSize/fileMime/copyCount/pinned/archived/expireAt/createdAt/updatedAt`。
- 导入导出包：`{app:"clipboard-tool", version, exportedAt, clips[]}`；同 id 取新、非 UUID 重生成（v0.6.11 语义）。
- exe 单机版数据目录：`data/clips.json`（等价活跃区，单用户无 users 层）+ `data/files/` + `data/webdav.json`（同步配置）+ `data/settings.json`（置顶等偏好）。

### 1.5.3 交互还原——逐函数对照 app.js（已精读源码核对）

> 用户红线：**界面 + 交互逻辑全部一模一样，不乱改**。以下为已核对的关键交互行为清单（触发条件/反馈/防叠加/守卫），WPF 实现逐条对齐：

| 交互 | Web 版行为（源码核对） | WPF 还原要求 |
|---|---|---|
| 存入弹窗 | ① 已打开不重复弹（`if ($(".paste-modal")) return`）；② 打开时自动填剪贴板（文本优先，其次图片，文件读不到按场景提示）；③ 类型徽章实时识别（文件/链接/格式文本/文本，`pendingHtml` 非空显示「✦ 格式文本」）；④ 粘贴手势内先捕获 text/html → 图片/文件优先 → 纯文本刷新徽章；⑤ **Ctrl+Enter 快速存入**（Enter 仍换行）；⑥ 文件超 10MB 拒收 toast；⑦ 高级选项默认展开（别名/标签/过期 1h/1d/7d/30d/永久） | 弹窗不叠加、自动填剪贴板、类型徽章动态更新、Ctrl+Enter 快捷键、文件上限 10MB、默认展开高级选项 |
| 重复检测 | 输入 300ms 防抖 → 命中重复 → **关闭存入窗，切换为该条目编辑弹窗（「已有相同内容」常驻）**，一次只一个窗；归档重复只 flash 提示；`force`（点「仍要存入」）跳过拦截 | 300ms 防抖、跳转编辑窗语义、归档只读提示、强制存入通道 |
| 卡片 | ① 单击复制（链接复制 URL，文本复制 content，图片复制 ClipboardItem、失败则打开预览，其他文件下载）；② 复制成功 toast 跟随鼠标位置；③ **双击编辑**（排除 ops/富文本分栏点击；归档只读）；④ 复制后来源抑制 800ms（本次点击引起的写剪贴板不触发自动弹窗）；⑤ 复制计数本地即时 +1 不刷新（P-5） | 点击复制 + 鼠标位置 toast、双击编辑守卫、800ms 来源抑制、计数即时更新 |
| 卡片头部 | 类型徽章（文本/链接/文件）+ 标题（无标题兜底：链接=域名、文件=文件名、文本=内容前 30 字）+ 状态徽章（★置顶 / ⏳过期倒计时 / 归档）+ 右上角操作（归档卡显示 ↺恢复 + ✕删除） | 徽章兜底规则一致、过期倒计时文案一致（`expLabel`） |
| 卡片底部 | meta 信息 + 操作行（☆置顶 / ✎编辑 / ↓下载 / {}JSON——按类型条件出现：文件才有下载、非归档才有编辑、文本 JSON 才有 {}） | 按钮按类型条件出现，不增不减 |
| 富文本分栏 | 文本条目有 html → 内容区左右分栏（左「T 普通文本」复制纯文本 / 右「✦ 富文本」复制 HTML 格式），顶部提示行 | 分栏复制双格式（DataFormats.Text + Html） |
| JSON 预览 | 文本内容是 JSON → 卡片出代码窗（金键名/绿字符串着色，等宽缩进，点开弹窗美化/复制/覆盖保存） | 代码窗着色规则一致 |
| 图片交互 | hover 悬浮预览：默认 100%、滚轮缩放 50%~300%、浮层挂卡片内不中途消失、wheel 绑卡片；点击复制 ClipboardItem | 悬浮预览 + 缩放范围一致 |
| 批量编辑 | 「编辑」进入多选 → 卡片左上角勾选框 + 透明覆盖层（禁用复制/双击/预览）→ 底部悬浮条（已选 N 项 / 全选当前页(已全选则取消) / ＋加标签 / －减标签 / 🗑删除(确认弹窗) / 完成）；删除后自动退出批量模式 | 覆盖层禁用交互、全选=当前可见集、删除确认文案一致、删除后退出模式 |
| 搜索 | 输入 100ms 防抖本地过滤，一边输入一边筛，无网络请求 | 100ms 防抖即时过滤 |
| 类型 tabs | 全部/文本/链接/文件 前端过滤 | 一致 |
| 灰度配色 | 顶栏 ◐ 一键彩色↔无饱和度，localStorage 记忆 | 主题切换 + 持久化（`data/settings.json`） |
| 快捷键 | 主页面 **Ctrl+V=存入**（自动填入剪贴板；焦点在输入区时保留原生粘贴）/ **空格=聚焦搜索框并全选**（焦点在输入/交互元素时不拦截、不滚动页面）；Esc 关闭弹窗 | 与 Web 版逐字一致（`OnPreviewKeyDown`：Ctrl+V→OpenPasteDialog；Space→SearchBox.Focus+SelectAll；守卫：未登录/已有弹窗/焦点在文本框不触发） |
| 置顶按钮 ★ | **新增（唯一不同）**：工具栏切换按钮 → Window.Topmost，状态持久化，按钮高亮（金=开） | 与 Web 版 UI 风格一致的按钮 |

**实现守则**：交互细节（防叠加/守卫/防抖/来源抑制/toast 位置）是"还原度"的一部分，**逐条按上表实现**，不做"更合理"的改动；任何与 Web 版行为的不确定点，以 app.js 源码为准（禁止自由发挥）。

### 1.5.4 验收门禁（准确度红线）

1. 排序对拍：任意数据集下 exe 排序 == Web `sortClips` 排序。
2. merge 对拍：`test-merge-snapshot.mjs` 场景全部一致。
3. 互导验证：Web 导出 JSON → exe 导入 → 再导出 → 与原文一致（17 字段无损）。
4. 冒烟：对齐 `scripts/smoke-test.mjs` 的核心断言清单。

## 2. 技术栈选型（检索验证）

| 项 | 选择 | 依据 |
|---|---|---|
| 语言/框架 | C# WPF (net9.0-windows) | 2026 桌面框架选型共识：短期落地、稳定优先 → WPF 首选；深色主题模板化成熟（对比 WinForms 的 `SetColorMode(Dark)` 为 .NET 9 实验 API，目标 .NET 11 才定型——旧路线技术根源） |
| UI 控件库 | **WPF 原生控件 + 自定义模板**（轻量引入 HandyControl 可选） | HandyControl 提供 Card/Badge/Growl/Dialog/NotifyIcon/暗色主题（支持 net9.0，NuGet 实测）；但默认风格非新拟态，核心靠自定义 ResourceDictionary |
| 剪贴板监听 | Win32 `AddClipboardFormatListener` + `HwndSource.AddHook` | 官方推荐 API（微软文档/Meziantou 博客/StackOverflow 多源一致），WPF 需在 `OnSourceInitialized` 注册，处理 `WM_CLIPBOARDUPDATE(0x031D)`，100ms 防抖避免自触发 |
| 置顶 | `Window.Topmost` | WPF 原生属性 |
| 存储 | JSON（对齐 Web 版 clips.json 17 字段） | 数据格式与 Web 版兼容互导，原子写 |
| 单文件发布 | `dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true` | 产出 ~1-3MB；可选 `-p:PublishAot=true` NativeAOT 自包含更小更快（WPF NativeAOT .NET 8+ 支持） |

## 3. UI 还原方案（对齐 Web 版暗黑新拟态）

### 3.1 设计令牌映射（源：public/index.html `:root`，逐项照搬）

| CSS 变量 | 值 | WPF 资源键 | 用途 |
|---|---|---|---|
| `--bg` | #1A1A1A | `BgBrush` | 窗口背景 |
| `--elev` | #1F1F1F | `ElevBrush` | 面板/工具栏 |
| `--elev-hi` | #2A2A2A | `ElevHiBrush` | 卡片悬浮/凸起 |
| `--inset` | #141414 | `InsetBrush` | 输入框/凹陷区 |
| `--border` | #3D3D3D | `BorderBrush` | 边框/滚动条 |
| `--text` | #DADADA | `TextBrush` | 正文 |
| `--muted` | #848484 | `MutedBrush` | 次要文字 |
| `--dim` | #6E6E6E | `DimBrush` | 弱化 |
| `--accent` | #C9A96E | `AccentBrush` | 金 · 主强调（按钮/选中/标签 active，深字配） |
| `--accent2` | #AE4D4D | `Accent2Brush` | 砖红 · 点缀 |
| `--green` | #9FBF8F | `GreenBrush` | 文本徽章 |
| `--amber` | #D4AF37 | `AmberBrush` | 文件徽章 |
| `--red` | #E08A7A | `RedBrush` | 危险/错误 |
| `--r-md / --r-lg / --r-pill` | 14 / 18 / 99px | `RadiusMd/Lg/Pill` (CornerRadius) | 圆角 |
| `--ease` | cubic-bezier(.22,.61,.36,1) | 动画 EasingFunction | 过渡 |

> 灰度模式（`html.mono` 一键全灰）作为 V2 选项：所有彩色令牌统一灰化，可用 `MonoMode=true` 的 Brush 切换实现。

### 3.2 新拟态阴影实现（核心难点）

Web 版用 CSS 双阴影（深色投影 + 浅色高光）实现凸起/内嵌：

| CSS token | 语义 | WPF 实现方案 |
|---|---|---|
| `--sh-raised`（外凸） | 1px 深影 + -1px 浅高光 | `DropShadowEffect`（BlurRadius 3, ShadowDepth 1, 黑 50%）+ 外层 Border 亮边（#585858 35% 1px） |
| `--sh-inset`（内嵌） | inset 双向 | 双层嵌套 Border：外层暗边 + 内层亮边（WPF 无内嵌阴影，用 4 条边 Line/Thickness 模拟）或自定义 `InsetShadowBorder` 控件 |
| `--sh-press`（按下） | inset 增强 | 同 inset，加深透明度；按钮按下时切换模板状态 |

实现建议：封装 `NeuBorder`（依赖属性 `ShadowKind: Raised/RaisedSm/Inset/Press`），内部组合 Border + Effect，供全局复用。这是还原新拟态手感的关键件，工作量集中但收益最高。

### 3.3 布局结构（对齐 Web 版 renderMain）

```
MainWindow (dark bg, 深色标题栏)
├─ 顶部工具栏：搜索框(inset) | 类型过滤 | ★置顶按钮 | 批量编辑 | 数据管理 | 托盘按钮
├─ 标签栏：全部 + 各标签 chips（滚动）
├─ 卡片墙：ItemsControl + WrapPanel（类型化卡片，虚拟化）
│    ├─ 文本卡：摘要 + {}JSON 按钮 + 富文本分栏(T/✦)
│    ├─ 链接卡：host 徽章 + URL
│    └─ 图片卡：缩略图 + 悬浮预览
├─ 底部悬浮批量条：全选/加标签/减标签/删除/完成
└─ 状态栏：条目数 / 同步状态
```

### 3.4 深色标题栏与系统集成

- 沉浸式深色标题栏：`DWMWA_USE_IMMERSIVE_DARK_MODE`（DWM API，Win10 1809+，成熟做法）。
- 托盘：`NotifyIcon`（WPF 需 WinForms 互操作或 HandyControl NotifyIcon）。

## 4. 功能范围与映射（Web 版 → exe 版）

### 4.1 核心交互（MVP，对齐 Web 版操作逻辑）

| Web 版（app.js 函数） | exe 版实现 | 说明 |
|---|---|---|
| `openPasteModal` / `savePasteContent` | `PasteDialog.xaml` | 确认式存入：类型徽章 + 内容可编辑 + 标题 + 标签 chips + 富文本提示 |
| `clipCard` / `makeCardMeta` / `makeCardBody` | `CardControl.xaml`（DataTemplate） | 类型化卡片，整卡点击=复制 |
| `makeRichSplit` / `copyRich` | `CardControl` 富文本分栏 | 点击分别复制纯文本/富文本（HTML 格式保留） |
| `makePinBtn` / `bumpCopyCount` | 卡片 ★ 按钮 + 复制计数 | 置顶排序参与项 |
| `makeJsonBtn` / `openJsonPreview` | `JsonPreviewDialog.xaml` | JSON 美化/复制/覆盖保存 |
| `openEditModal` | `EditDialog.xaml` | 标题/内容/链接/标签/清除格式/归档/删除 |
| `openImagePreview` / `copyImageToClipboard` / `downloadFile` | 图片卡片交互 | 缩略图/悬浮预览/复制/下载 |
| `bindImageHoverPreview` | 图片悬浮预览 | 对齐 Web 行为 |
| `setBatchMode` / `renderBatchBar` / `openBatchTagModal` | 批量编辑条 | 多选 + 全选当前页 + 批量加/减标签/删除 |
| `pyInitial` / `strToPy`（PY_GROUPS 3755 字） | `Pinyin.cs` | 拼音首字母搜索（数据原样搬） |
| `cleanUrl`（24 追踪参数） | `CleanUrl.cs` | URL 自动清理（逻辑直译） |
| `findDuplicateClip` | 去重逻辑 | 与最近一条相同 → 静默刷新置顶不弹窗 |
| `normalizeRichHtml` / `buildWordDoc` | `RichText.cs` | 富文本规范化 + Word HTML 生成 |
| `flash` / `errToast` / `askConfirm` | Toast / 确认弹窗 | 顶部居中深色卡片 toast（用户偏好） |
| `expLabel` / `fmtTime` / `fmtSize` | 格式化工具 | 过期标签/时间/大小 |
| **新增** | **工具栏 ★ 置顶按钮** | `Window.Topmost` 切换 + 按钮高亮态 + 状态记忆 |

### 4.2 数据与同步（含 WebDAV）

| Web 版（lib/core） | exe 版实现 | 说明 |
|---|---|---|
| `clips-store.js` 原子写 | `Storage.cs` | 原子写（临时文件+rename），排序 pinned→copyCount→updatedAt（对齐 v0.6.16） |
| 标签/归档/墓碑 | `Storage.cs` | 跨活跃+归档生效 |
| 导入导出 | `Storage.cs` Import/Export | Web 版 `{app,version,exportedAt,clips[]}` 格式互导，同 id 取新、非 UUID 重生成 |
| `webdav.js getSyncConfig/saveSyncConfig` | `WebDavSettings`（JSON 配置） | 每账号 URL/user/pass/syncFiles/autoSync/intervalMin |
| `webdav.js testConnection` | `WebDavClient.TestConnection` | PROPFIND/MKCOL 验证 |
| `webdav.js mergeSnapshots` | `WebDavSync.MergeSnapshots` | **合并裁决核心**（见 §6） |
| `webdav.js syncFileEntities/collectRemoteSources/mergeRemoteAll` | `WebDavSync` | 文件实体双向同步 |
| `webdav.js recordTombstoneIfConfigured` | 墓碑记录 | 删除写墓碑，防远端复活 |
| `runAutoSync`（60s 检查） | 后台定时器 | 按各账号 intervalMin 自动同步 |

### 4.3 明确不做（单机形态）

- 多用户/会话/token（Web 版局域网多用户场景，exe 单机不需要）。
- 权限体系、平台托管相关（CAP_STORAGE_DIR 注入等）。

## 5. 架构设计

### 5.1 项目结构

```
clipboard-exe/                        (全新，与旧 WinForms 路线无关)
  ClipboardExe.csproj                 net9.0-windows; WPF
  Program.cs                          入口：单实例互斥 + 全局异常兜底 + 版本指纹 + 日志
  App.xaml / App.xaml.cs              ResourceDictionary 合并 + 启动装配
  Themes/
    Colors.xaml                       设计令牌 Brush（§3.1 全量映射）
    Shadows.xaml                      NeuBorder 控件 + 阴影资源
    Styles.xaml                       按钮/输入框/标签 chips/滚动条样式（对齐 Web 四态）
  MainWindow.xaml / .cs               主窗体：工具栏/标签栏/卡片墙/批量条/状态栏/置顶
  ViewModels/
    MainViewModel.cs                  过滤状态 q/tag/type/archived + 可见列表 + 批量选择
    (轻量 MVVM：代码量可控，不必引入 Prism)
  Controls/
    NeuBorder.cs                      新拟态容器（Raised/Inset/Press）
    CardControl.cs                    类型化卡片（DataTemplate 或自定义控件）
  Services/
    Storage.cs                        JSON 数据层：原子写/排序/标签/归档/导入导出/清空
    ClipService.cs                    业务逻辑：搜索(拼音)/去重/类型识别/过期
    Pinyin.cs                         3755 字首字母映射（数据搬运）
    CleanUrl.cs                       URL 追踪参数清理（24 个）
    RichText.cs                       富文本规范化 + Word HTML
    ClipboardWatcher.cs               原生剪贴板监听 + 确认式捕获
    WebDavClient.cs                   WebDAV HTTP 客户端（PROPFIND/GET/PUT/MKCOL）
    WebDavSync.cs                     快照上下行 + mergeSnapshots + 墓碑 + 定时同步
    TrayIconService.cs                托盘常驻/退出/前台捕获开关
  Dialogs/
    PasteDialog.xaml                   存入确认弹窗
    EditDialog.xaml                    编辑弹窗
    JsonPreviewDialog.xaml             JSON 格式化预览
    DataDialog.xaml                    导入/导出/清空 + WebDAV 配置
    InputDialog.xaml                   批量标签输入
  Models/
    ClipItem.cs                        条目模型（17 字段对齐 Web publicClip）
    SyncConfig.cs                      WebDAV 配置模型
```

### 5.2 类依赖（单向，无循环）

```
MainWindow → ViewModels → Services(Storage/ClipService/WebDav*) → Models
MainWindow → Dialogs → Services
ClipboardWatcher → Storage/ClipService（捕获→确认弹窗→落库）
```

### 5.3 MVVM 边界

- ViewModels 只做状态与命令，不碰原生 API。
- 剪贴板/托盘/置顶等系统能力在 Services/Code-behind，ViewModel 通过事件或命令桥接。
- 保持 UI 与业务解耦（2026 框架选型共识的通用避坑准则）。

### 5.4 M2 数据层边界契约（2026-09-01 架构评估后新增 · 防膨胀硬规则）

> 背景：M1 架构评估结论——主线/支线/模块化清晰，唯一风险是 M2 数据层接入后 MainWindow 膨胀
> （Web 版 clipCard CC49 教训）。以下为开工前定死的边界，M2 起逐条遵守。

1. **数据层全部独立文件进 Services/Models**：ClipItem/Storage/ClipService/Pinyin/CleanUrl/RichText/
   ClipboardWatcher 等一律独立类，MainWindow 不得直接持有任何业务逻辑（含排序/过滤/去重/过期计算）。
2. **MainWindow 只做 UI 编排**：事件处理器 = 调 Service → 刷新 UI；不自研算法，不碰文件系统与剪贴板。
3. **`Tag="on"` 字符串约定不扩散**：M1 仅类型分段一个用例（纯视觉），属 Web `.tt.on` 的直接映射，保留。
   M2 一旦出现选中态联动（如类型分段 + 列表过滤、批量多选），立即升级为真绑定（SelectedIndex/Command/
   ObservableCollection），禁止把字符串约定复制到新控件。
4. **布局规则走 LayoutRules 纯函数**（M1 已抽）：`MaxWidthFor` 已就位；M2 卡片墙列数（Web `.list`
   auto-fill）在此加规则函数，不在事件里写内联计算。

> 搬移产物记录（行为等价，已构建验证）：DWM 深色标题栏 → `Controls/WindowExtensions.cs`（M2 弹窗复用，2026-09-03 由 Services/ 归位）；
> 自适应三档 → `Services/LayoutRules.cs`；MainWindow 关注点由 5 → 3（生命周期/置顶/退出编排）。
> 弹窗/提示宿主（ModalHost/ToastService）同为 UI 基础设施，2026-09-03 由 Services/ 归位到 `Controls/`。

## 6. 数据兼容与 WebDAV 移植要点

### 6.1 条目格式（对齐 Web 版 publicClip，17 字段 camelCase；2026-09-01 以 clips-store.js 实际代码复核修正）

```
{
  id,                          // UUID（Guid "D" 格式）
  type,                        // text | link | file（白名单，非法回退 text）
  title,                       // 输入 trim+截长 200；link 缺省时用 cleanUrl 后 url 前 60 字符（v0.3.1）
  content,                     // text 必填（非空、≤200KB）；其余默认 ""
  html,                        // 富文本（可选，≤512KB；与 content 并存，默认 ""）
  url,                         // link 必填且匹配 /^https?:\/\/\S+$/i；已 cleanUrl 剔除 24 个追踪参数
  tags[],                      // trim+截长 20+Set 去重+上限 10
  fileId, fileName, fileSize, fileMime,  // file 类型（fileName 默认 "file"、≤255）
  copyCount,                   // 复制计数（排序第二优先级）
  pinned,                      // 星标（排序第一优先级）
  expireAt?,                   // 过期绝对时间戳 ms；null = 永久（字段名 expireAt，非 expiresAt）
  createdAt, updatedAt,        // 时间戳 ms（对齐 Date.now()；updatedAt 驱动排序/归档/同步）
  archived                     // 只读输出标记（条目来自归档区时 true），不落盘
}
```

- 导入：同 id 取新；非 UUID 重新生成；来源标记（Web 版导出可直接导入）。
- 导出：`{app:"clipboard-tool", version, exportedAt, clips[]}` 与 Web 版一致。
- 持久化：`data/clips.json`（主数据，`{app,version,clips[]}` 与导出同构）+ `data/archive.json`（滚动归档，Web `users/<id>.archive.json` 的直接对应）。
- 原子写：临时文件 + rename（对齐 writeJson：`file + ".tmp-" + pid`）。

### 6.2 mergeSnapshots 合并裁决（核心移植点，webdav.js L214）

按 Web 版语义逐条移植：
1. 本地条目 ↔ 远端快照按 id 对齐。
2. 墓碑优先：本地墓碑存在 → 远端条目删除；远端墓碑 → 本地删除。
3. 无墓碑：按 `updatedAt` 取新，同刻取远端（或按既定规则）。
4. 仅本地/仅远端 → 保留 + 标记待上传/待下载。
5. 文件实体（files/）按 fileId 引用，缺失则下载补齐。

> 建议：移植后对拍 Web 版 merge 测试脚本（scripts/test-merge-snapshot.mjs）产出相同结果，作为验收门禁。

## 7. 关键技术实现

| 技术点 | 实现 |
|---|---|
| 剪贴板监听 | `OnSourceInitialized` → `HwndSource.AddHook` + `AddClipboardFormatListener(hwnd)`；`WM_CLIPBOARDUPDATE` → 读取 `Clipboard.GetDataObject()` 识别 text/link/image；100ms 防抖；窗口失活/最小化暂停捕获（隐私优先，对齐 Web 版前台捕获语义） |
| 置顶按钮 ★ | 工具栏 ToggleButton → `this.Topmost = !this.Topmost` → 状态持久化到 `data/settings.json` → 按钮高亮（金 = 开） |
| 富文本复制 | 剪贴板同时写 `DataFormats.Text` + `DataFormats.Html`（Word/网页格式保真）；`buildWordDoc` 移植生成 Word HTML 片段 |
| 单实例 | Mutex + 已有实例唤醒（`PostMessage` WM_SHOW_MAIN） |
| 托盘 | 点 X/最小化 → 托盘；托盘菜单：显示/前台捕获开关/退出（退出才真退，停止捕获） |
| 深色标题栏 | `DWMWA_USE_IMMERSIVE_DARK_MODE = 1`（DWM） |
| 图片存储 | `data/files/` PNG，fileId 引用（对齐 Web 版） |
| 日志 | `data/clipboard-exe.log` 首行版本指纹（对齐 Web 版"实例身份可追溯"约定） |

## 8. 里程碑

| 里程碑 | 内容 | 产出/验收 |
|---|---|---|
| ✅ **M1 骨架**（2026-08-31 完成） | WPF 工程 + 设计令牌主题（Colors/Styles/Generic + NeuBorder）+ 深色主窗体 + 托盘 + 单实例 + 置顶按钮 | ✅ 构建 0 警告 0 错误；Release 单文件 173KB；启动不崩溃；日志首行版本指纹 `clipboard v0.7.0 (dev)`；settings.json 置顶持久化生成 |
| ✅ **M2 数据层**（2026-09-01 完成） | ClipItem(17 字段)/Storage(原子写+滚动归档+排序)/Pinyin(3755 字)/CleanUrl(24 参数)/ClipService(Create/去重/搜索/过期/净化) + `--selftest` 自检 | ✅ 构建 0 错误（34 个 CA1416 为 TrayIconService 既有）；`--selftest` **42 项断言全过**（排序/拼音/清理三态/去重/归档防膨胀/round-trip/过期）；数据落 `data/clips.json`+`archive.json`，MainWindow 零改动 |
| ✅ **M3a MVP 交互 · 文本/链接闭环**（2026-09-01 完成，**M3 拆 M3a/M3b 后单轮交付**） | 剪贴板监听（WM_CLIPBOARDUPDATE+100ms 防抖+800ms 来源抑制+失活/最小化暂停）+ 存入弹窗（类型徽章/Ctrl+Enter/去重→跳转编辑）+ 编辑弹窗（标题/内容/标签/过期/归档/删除）+ 卡片墙（文本/链接+状态徽章+☆置顶/✎编辑/{}JSON 只读/✕删除）+ 搜索 100ms 防抖 + 类型过滤真绑定（RadioButton）+ 空状态双文案 + 自适应列数（自动）+ 只读 JSON 预览 | ✅ 构建 0 错误（36 警告全为 TrayIconService 既有 CA1416 + Settings CS8618）；`--selftest` **85 断言全过**（M2 42 + M3a 增量 43，含 PrettyJson LF 统一/弹窗宿主/TagPicker/Cells）；冒烟截图（1366×601）非白屏、4 列自适应、置顶/过期/类型徽章全部正确渲染、链接卡 URL+主按钮可用 |
| ✅ **M3b-1 杂项先行 · 标签栏/归档/列数**（2026-09-01 完成，M3b 拆 M3b-1/2/3 后单轮交付） | 完整标签栏 chips 聚合（`Search("", includeArchived:true)` 去重排序，含归档条目标签）+ 标签过滤 toggle（同标签再点取消，与类型过滤/搜索叠加）+ 归档开关（"归档·关/开" 双态文案+金色反馈，点开显示归档卡）+ 归档卡 ↺ 恢复（Unarchive：归档取回活跃区、updatedAt 刷新参与排序、活跃区已存在防御 false）+ 列数偏好持久化（Settings.MaxColumns 0=自动/1~4 锁定，ColumnsFor 第二参数，按钮循环 自动→1→2→3→4→自动 即时落盘） | ✅ 构建 0 错误；`--selftest` **99 断言全过**（M2 42 + M3a 43 + M3b-1 增量 14：Unarchive 7 + ColumnsFor maxColumns 7）；UIAutomation 冒烟：归档·开 显示归档卡（↺ 按钮存在）、点击 ↺ 后 x1 从 archive.json 移回 clips.json（闭环）、列数循环持久化 MaxColumns 0↔1↔4、标签过滤 50→42→50 按钮数 |
| ✅ **M3b-2a 文件线**（2026-09-01 完成，M3b-2 拆 2a/2b 后单轮交付） | `data/files` 实体存储（FileStore：保存/读取/删除 + 10MB 上限 + MIME/扩展名黑名单 + EXT_BY_MIME 扩展名映射 + 不安全扩展名回退 bin + 前缀查找防穿越，对齐 files.js）+ 存入弹窗文件 chip（📎 fname·fsize ✕，对齐 .file-chip；选中后隐藏 textarea + 徽章切「将存为：文件」）+ 📁 选择文件 + 粘贴/拖放（DataObject.Pasting + Drop）+ 10MB 拒收 + 文件图标卡（PDF 红边 / ZIP 金边 / FILE 中性 + 折叠角 + 内虚线框 + fname·fsize·mime 首段，对齐 makeFileIcon L1173）+ ↓ 下载（SaveFileDialog 原名落盘）+ 删除联动清文件实体（对齐 Web 路由层 deleteFile） | ✅ 构建 0 错误（36 警告全为既有 CA1416/Settings）；`--selftest` **126 断言全过**（99 + M3b-2a 增量 27：FileStore 存取/黑名单/10MB/扩展名映射/Delete 静默 + FileKindFor 6 态 + Create file 字段）；UIAutomation 冒烟：文件图标卡渲染（PDF 红边/ZIP 金边/折叠角/内虚线全对齐）、↓ 下载按钮存在、删除 → 确认 → clips.json 移除 + `data/files/` 实体联动清理（zip 实体消失，PDF 保留） |
| ✅ **M3b-2a-1 UI 圆角规范修正**（2026-09-01 完成，M3b-2a 内的二次修正） | 全面排查并替换 12 处硬编码 `CornerRadius`（Styles.xaml 6 处 + EditDialog/PasteDialog 图标块 3 处 + CardView/PasteDialog/Toast 3 处），全部改用 `Colors.xaml` 圆角令牌；`Colors.xaml` 增 3 令牌 `RadiusFold`(3,3,0,0 折叠角不对称) / `RadiusIconLg`(11, 36px 图标块) / `RadiusIconSm`(6, 20px 序号块)；`Styles.xaml` 增全局 `ToolTip` 样式（消除系统默认白底矩形） | ✅ 构建 0 错误 0 警告（既有 CA1416/Settings 已消）；`--selftest` **126 断言仍全过**（零回归）；UI 截图肉眼确认主界面 + 存入弹窗所有可见元素均为圆角矩形（搜索框/存入按钮/标签栏 chips/列数按钮/卡片体/卡片底部 ☆↓✎✕ 按钮/PDF 文件卡/折叠角/文本框/徽章/清空链接 等）；`grep CornerRadius="[0-9]"` 命中 0 处、`grep new CornerRadius(` 仅余 `PillBorder.cs:49`（控件内部封装动态半径，合规） |
| ✅ **M3b-2b 图片线**（2026-09-01 完成，M3b-2 拆 2a/2b 后单轮交付；建于 M3b-2a 实体存储之上） | `Format.IsImageMime` 统一入口（`mime?.StartsWith("image/")`，对齐 Web 多处判定） + `CardView` 图片卡体（imgwrap cover 撑满：`Image.Stretch=UniformToFill` + `Border.ClipToBounds` + 解码失败自动降级文件卡） + 卡片 `TypeBadge` 切紫色徽章「图片」 + 点击图片卡中部 → `CopyImageRequested` 事件 → `MainWindow.CopyImageToClipboard`（`FileStore.ReadAllBytes` → `BitmapImage Freeze` → `System.Windows.Clipboard.SetImage` + watcher.Suppress(800) 来源抑制 + BumpCopyCount + flash「图片已复制，可直接粘贴」+ 失败 toast） + hover 悬浮预览（`DispatcherTimer 260ms` 延迟 + Popup + `CustomPopupPlacementCallback` 视口钳制 8px + 优先顶部 4px 间隙 + 空间不够落底部 + 滚轮缩放 50%~300% 步进 15% + 重新解码按比例设宽高 + cap 显示 fname·fsize·百分比）对齐 Web `bindImageHoverPreview` L1199 + 单击复制对齐 `handleCardClick` L1047-1055 + 复制逻辑对齐 `copyImageToClipboard` L1324 + `PasteDialog` 图片 chip 接收（`DataFormats.Bitmap` 剪贴板截图优先 + `EncodePng` BitmapSource→PNG + `PickBytes` 直接接收字节 + `Root_Drop` Bitmap 拖放，去掉 2a 拒收占位） | ✅ 构建 0 错误 0 警告；`--selftest` **144 断言全过**（126 + M3b-2b 增量 18：FileStore 图片存读/扩展名映射 jpg·gif·webp/bmp 兜底/10MB 图片拒收/IsImageMime 4 态/MimeFromPath/Create file mime 字段）；UIAutomation 冒烟：图片卡渲染正确（紫色「图片」徽章 + 120×80 金色 PNG cover 撑满 + #合同 chip + ↓ 下载 + ☆ 收藏 + × 删除 + 列数·2 工具栏）、点击图片卡中部 → `copyCount 0→1` 文件级确认 CopyImageToClipboard 触发、`clickby ↓` Invoke 成功复用 M3b-2a 下载链路；`shot` 工具 `SetWindowPos` bug 同步修复（缺 `SWP_NOMOVE|SWP_NOSIZE` 导致点击时窗口被 resize 到 0×0） |
| ✅ **M3b-3 批量编辑条**（2026-09-01 完成） | 多选/全选 + 透明覆盖层 + 底部悬浮 BatchBar（已选 N / 全选当前页 / ＋加标签 / －减标签 / 🗑删除 / 完成）+ 批量标签（TagPicker 加/减）+ 批量删除（BatchDelete 跨区+文件实体联动+墓碑） | ✅ `--selftest` 159 断言全过（144+12 批量业务）；构建 0 错误；UIAutomation 冒烟批量闭环 |
| ✅ **M4a 导入导出 + 清空**（2026-09-01 完成，M4 拆 M4a/M4b/M4c） | `Transfer.cs`(ExportDoc/ImportResult，`app:"clipboard"` 对齐 Web) + `Storage.Serialize/Deserialize` + `ClipService.BuildExport/ImportClips/ClearAll/SanitizeImported`（逐字对齐 clips-transfer.js/clips-mutate.js）+ `DataDialog`（导出/导入合并/全部清空）+ 顶栏「数据管理」启用 | ✅ `--selftest` 177 断言全过（159+18）；构建 0 错误 0 警告；UIAutomation 冒烟 DataDialog+Confirm 渲染 |
| ✅ **M4b 富文本分栏**（2026-09-02 完成） | `Services/RichText.cs`（`BuildWordDoc` 字符串级移植：片段 → 带 `xmlns:o/w/m` 的完整 Word 文档 + 已有完整文档原样返回；`EncodeCfHtml` 输出 CF_HTML 字节偏移；`CopyRich` 双格式写入剪贴板 `DataFormats.Text` + `DataFormats.Html`）+ `CardView` 文本卡有 `html` 时渲染左右双栏（顶部提示行「T 普通文本」/「✦ 富文本」，两栏都显示纯文本内容，左栏单击复制纯文本 / 右栏单击复制富文本，分栏区 stopPropagation + 双击编辑守卫）+ `MainWindow` 事件接线（来源抑制 800ms + `BumpCopyCount` + 鼠标位置 toast） | ✅ `--selftest` **183 断言全过**（177 + M4b 增量 6：BuildWordDoc 空串/片段包命名空间/StartFragment·EndFragment 标记/body 属性保留/已是完整文档原样返回）；构建 0 错误（20 个 CA1416 为 TrayIconService 既有）；UI 截图肉眼确认：富文本卡正确渲染 split 分栏与提示行，普通文本卡仍保持原单栏体 |
| ✅ **M4c JSON 覆盖保存**（2026-09-02 完成） | `JsonDialog` 三按钮（覆盖保存/复制美化结果/关闭：`Save_Click` 校验 JSON 合法后 `SaveRequested(_clip, JsonBox.Text)` 回传编辑后文本）+ `MainWindow.OpenJsonRequested` 订阅 `Update(id, c.Title, c.Tags, null, 新content, null, newHtml)`（标题/标签原样保留，带 html 且变更时 `TextToHtml` 重建）+ `RichText.TextToHtml` | ✅ `--selftest` **196 断言全过**（183 + M4c 增量 13：TextToHtml 5 + 覆盖保存契约 8）；构建 0 错误 0 警告；无头环境改数据层契约自检覆盖 |
| ✅ **M5a WebDAV 数据层 + 合并算法**（2026-09-03 完成） | `Models/SyncConfig.cs`(`SyncConfig`/`Snapshot`) + `Services/WebDavClient.cs`(Basic 认证 + MKCOL/GET/PUT + EnsureDir/TestConnection/Fetch/Upload) + `Services/WebDavSync.cs`(`MergeSnapshots` 逐字移植 webdav.js L214 + 配置 IO 校验) | ✅ `--selftest` **223 断言全过**（196 + 合并对拍 17 + 配置 10）；构建 0 错误；合并与 scripts/test-merge-snapshot.mjs 对拍一致 |
| ✅ **M5b 同步引擎**（2026-09-04 完成，单账号桌面形态收窄实现） | `Services/SyncEngine.cs`（对齐 Web `runSync` 的单账号形态：`RunSync` = InFlight 重入防护 → `EnsureDir` → `FetchRemoteSnapshot`(SnapUrlFor) → 本地快照 = 活跃∪归档(带 archived 标记) → `MergeSnapshots` 双向取最新+墓碑裁决 → 写回本地(归档先替换防滚动覆盖→活跃→墓碑) → `syncFiles` 勾选时 `SyncFileEntities`(文件实体双向：本地有 → PUT 上传 `<files>/<账号>/<fileId>.<ext>`，本地缺 → GET 拉回 `WriteRaw`，404 跳过) → **hadLocal 上传保护**(合并前本地空 → 跳过上传，防空备份覆盖远端) → 刷新 `lastSyncAt`/`lastSyncError` 落盘；`SyncResult`(Ok/RemoteExisted/Uploaded/Clips/Tombstones/Error) | ✅ 构建 0 错误；**真实 mock WebDAV(8180,Basic auth)端到端验收**：①上传场景——空远端首同步 → mock 落 `剪贴板/clipboard-default.json` 快照 8 条(text/link/file 全对、中文无损)，本地与 pristine 零差异，lastSyncAt 刷新 ②恢复场景——本地清空(模拟新设备)再同步 → 从远端拉回 8 条、no-upload 保护生效(未用空本地覆盖远端) ③`syncFiles=true` → 3 文件实体上传 mock `files/default/file-pdf-001.pdf` 等、字节内容 GET 回读一致；**验收修复 2 处**：远端文件实体名缺 `.`(对齐 Web `extFor` 返回带点 `.pdf`；Exe `ExtFor` 不带点 → 拼路径补 `"."`，防 Web↔exe 跨端互拉 404)、同步成功后 UI 卡片墙不刷新(见 M5c) |
| ✅ **M5c 配置 UI + 定时同步**（2026-09-04 完成） | `Services/SyncController.cs`(编排下沉：持配置 + `Tick` 1 分钟轮询到点才跑 + `RunNow`) + `MainWindow.SyncOps.cs`(工具栏「同步」按钮触发 + `InitAutoSync` 接线 DispatcherTimer) + `DataDialog` WebDAV 配置页(地址/用户/密码留空沿用/`syncFiles` 勾选/`autoSync`+`intervalMin`/测试连接并保存(探针写读回)/立即同步/上次同步状态卡，配置落 `data/webdav.json`) | ✅ 构建 0 错误；E2E 验收：TestSave → 探针 PUT `.clipboard-probe` + webdav.json 持久化(url/user/store 全对)、对话框状态卡「上次同步」刷新、工具栏一键同步全链路(见 M5b)；**验收修复**：同步完成后卡片墙不刷新——`SyncNow_Click`(DataDialog) 与 `SyncBtn_Click`(工具栏) 成功分支补 `_onChanged()`/`RefreshWall()`(数据已落盘，仅缺重绑)，修复后本地空→同步恢复远端 8 条 → UI 免重启即时显示卡片 |
| 发布 | 单文件发布 + 版本指纹 + GitHub Release 附 zip（参考发布规范） | Clipboard.exe ~1-3MB |

## 9. 构建与发布

### 9.1 开发期（M1-M5 全程）

```cmd
dotnet run --project clipboard-exe          # 开发调试（JIT，行为与发布一致）
dotnet build clipboard-exe
```

### 9.2 发布（框架依赖单文件，运行库另行安装）

> 用户最终决策（2026-08-31）：**单文件太大就另行安装运行库，是小问题**。准确性 > 体积 → 不采用 NativeAOT。

```cmd
:: 正式发布方式：框架依赖单文件（~1-3MB）
dotnet publish clipboard-exe -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
:: 产出: clipboard-exe/bin/Release/net9.0-windows/win-x64/publish/Clipboard.exe
```

**运行要求（用户机器需装一次 .NET 9 Desktop Runtime）**：
- https://dotnet.microsoft.com/download/dotnet/9.0 → "Desktop Runtime 9.x x64"
- 本机已装 9.0.5，直接可跑；分发他人时随 README 附运行库说明

**为什么不采用 NativeAOT**（检索验证，微软官方 + 2026 深度分析一致）：
- WPF 数据绑定（XAML 反射查找属性、DataTemplate 动态创建）与 trimming 兼容性差（"WPF gets along poorly with trimming"），裁剪后可能运行时报错；
- 本应用数据绑定简单、成功率高于复杂 WPF，但"准确优先"原则下不值得为省 ~20MB 冒运行风险；
- 若未来要免依赖分发，可再评估自包含单文件 JIT（~70-90MB，零风险）或 NativeAOT + TrimmerRootAssembly 专项验证（本次不做）。

### 9.3 分发

- zip 打包 `Clipboard.exe` + `README.txt`（含运行库安装说明），数据目录 `data/` 不打包（运行时生成）。
- 版本指纹：`Program.cs` 内 AppVersion/GitCommit（对齐 Web 版"实例身份可追溯"约定）。

## 10. 风险与对策

| 风险 | 对策 |
|---|---|
| 新拟态内嵌阴影 WPF 无原生支持 | 封装 NeuBorder（§3.2），双层 Border 模拟，样板集中一处 |
| WebDAV 合并裁决移植偏差 | 对拍测试门禁（§1.5.4），逐条对齐 mergeSnapshots 语义 |
| 富文本格式保真（Word mso） | 移植 normalizeRichHtml 字符串级逻辑 + 双格式写入剪贴板 |
| 剪贴板监听与输入法/Office 冲突 | 低干扰：仅前台捕获 + 防抖 + 复制前短暂延迟 |
| 双版本维护成本 | 数据格式互导解耦；exe 逻辑直译保持与 Web 版规则一致（排序/合并/清理），变更时同步改两端 |
| **NativeAOT 裁剪导致 WPF 数据绑定运行错误** | ✅ **已排除**：用户确认装运行库是小问题，采用框架依赖单文件发布（§9.2），不冒裁剪风险 |
| WinForms 深色实验 API 教训重演 | WPF 深色为主题级成熟能力，无此风险 |

## 11. 待确认问题

1. ~~工程位置~~：✅ 已确认 —— `clipboard-exe/` 入主仓库。
2. ~~发布形态~~：✅ 已确认 —— **框架依赖单文件（~1-3MB），.NET 9 Desktop Runtime 另行安装（用户确认小问题），不采用 NativeAOT**（§9.2）。
3. ~~富文本/图片捕获的完整对齐度~~：✅ **M3a 修正（2026-09-01 用户确认"量太大先修正"）**——M3 拆 M3a/M3b：M3a 只做文本/链接线闭环（存入弹窗遇图片/文件 toast 提示 M3b 支持），文件/图片卡 + hover 预览 + 下载 + 标签栏 + 归档恢复 + 批量编辑全部延后 M3b；富文本分栏复制（makeRichSplit）后置 M4。总范围不减，红线不变。M3b 再拆 M3b-1（标签栏/归档/列数持久化，✅ 已完成）/ M3b-2（文件/图片线）/ M3b-3（批量编辑条），单轮量更小。
4. 托盘"前台捕获开关"默认开还是关（Web 版语义：窗口激活才捕获）→ M3a 实现为：窗口失活/最小化暂停捕获（对齐 Web 前台语义），托盘开关暂不新增。
5. ~~M3b-2 拆分~~：✅ **M3b-2 再拆 M3b-2a/2b（2026-09-01 用户确认"量太大先修正"）**——M3b-2a 文件线（FileStore 实体存储 + 弹窗文件 chip + 10MB 拒收 + 文件图标卡 + 下载 + 删除联动；图片粘贴 2a 暂拒收 toast 提示）；M3b-2b 图片线（图片卡体 + hover 预览 + 复制/下载）。依赖：2b 建立在 2a 实体存储之上，顺序不可颠倒。
6. ~~UI 圆角散落硬编码违例~~：✅ **M3b-2a-1 修复（2026-09-01 用户反馈"UI 上有很多不是圆角矩形的"）**——全面排查 12 处硬编码 `CornerRadius`（Styles.xaml 6 处 + EditDialog/PasteDialog 图标块 3 处 + CardView 折叠角/PasteDialog 文件 chip/Toast 3 处）全部改用 `Colors.xaml` 圆角令牌；新增 3 令牌 `RadiusFold`(折叠角不对称 3,3,0,0) / `RadiusIconLg`(36px 图标块 11) / `RadiusIconSm`(20px 序号块 6)；`Styles.xaml` 增全局 `ToolTip` 样式覆盖系统默认矩形白底。**原则确认**：圆角一律取令牌（不硬编码散落）、内容必须进圆角 Border + `ClipToBounds`、颜色/画刷一律取令牌、不为改而改——复用既有 UI 规范（NeuBorder/PillBorder/Styles/Colors）。
7. ✅ **M3b-2b 图片线落地（2026-09-01）**：建于 2a 实体存储之上；`Format.IsImageMime` 统一入口供卡片/单击复制/弹窗分流；`CardView` 图片卡体 `Image.Stretch=UniformToFill` cover + `Border.ClipToBounds` + 解码失败降级文件卡 + TypeBadge 切紫徽章「图片」；点击中部 → `CopyImageRequested` → `MainWindow.CopyImageToClipboard` → `Clipboard.SetImage` + 来源抑制 + BumpCopyCount + flash；hover 预览 `DispatcherTimer 260ms` 延迟 + Popup + `CustomPopupPlacementCallback` 视口钳制 8px + 滚轮 50%~300% 步进 15% + cap 百分比；`PasteDialog` 接收 `DataFormats.Bitmap` 截图 + `EncodePng` + `PickBytes` + Bitmap 拖放。`--selftest` **144 断言全过**（126 + 18 增量），构建 0 错误 0 警告。**冒烟限制**：WPF 不暴露 Win32 子窗口，shot click 必须先修 `SetWindowPos` 缺 flag bug 才能可靠点击（已在 `tools/shot/Program.cs` 同步修，缺 `SWP_NOMOVE|SWP_NOSIZE` 会把窗口 resize 到 0×0 被 WPF 钳到最小尺寸）；`shot clickby ↓` UIAutomation Invoke 验证下载按钮；点击图片卡中部后通过查 `clips.json` `copyCount 0→1` 文件级确认 `CopyImageToClipboard` 触发（toast 时机难精确抓帧但 selftest 已覆盖核心路径）。**原则确认**：图片判定一处入口（`Format.IsImageMime`，不再每处 `mime.StartsWith("image/")`）；hover 浮层用 Popup 不加新窗口（避免 z-order 复杂）；图片实体复用 FileStore 不另开存储；缩放重置用 `HorizontalOffset += 0.001` 触发 Popup 重新 placement（回调只在重新定位时调用）

## 12. 文档同步计划（随开发进度更新）

| 文档 | 内容 | 时点 |
|---|---|---|
| README.md | 交付形态补 exe 版说明 + 技术栈补 WPF + 目录结构补 clipboard-exe/ + 文档链接补本方案 | 已更新 |
| delivery/README.md | 三形态 → 四形态对比表（exe 版入表） | 已更新 |
| AGENTS.md | 技术栈加 WPF 行 + 文档基线提及 exe 路线 | 已更新 |
| CHANGELOG.md | 新增 v0.7.0 规划条目（exe 版启动） | 已更新 |
| DEVELOPMENT.md / 发布规范 | M1 落地后补 exe 构建/发布流程 | M1 完成时 |

---
*方案基于检索验证：Electron/Tauri/WebView2 壳方案对比、WPF 2026 选型共识、HandyControl 能力、AddClipboardFormatListener WPF 用法（微软文档/Meziantou/StackOverflow）、WPF 单文件/NativeAOT 发布（微软官方文档 + 2026 深度分析）。*
