# DEVELOPMENT.md · 开发文档

## 项目概览

clipboard-tool 是 tools-center 平台的一个**多用户剪贴板管理工具**：粘贴自动识别（URL→链接 / 其他→文本 / 图片文件→文件条目）、一键复制、双击编辑、标签体系、拼音首字母搜索、智能排序（置顶→复制次数→最近更新）、可选密码锁、WebDAV 备份同步。Node 22+ 零依赖，JSON 文件存储，双模式运行（平台托管 / 独立运行）。

**UI 演进**（v0.6.2+）：暗黑新拟态 + **酒红金配色**（对齐 dark-design-style-guide 主页示意 mock：中性黑灰底 `#1A1A1A/#2F2F2F` + 金主强调 `#C9A96E` + 砖红点缀 `#AE4D4D`；设计令牌全在 `index.html :root`，改主题只动令牌 + `users.js PALETTE`，app.js 禁止硬编码颜色）。v0.6.3 起首页（用户选择页）为**极简墙布局**（印章品牌区 + 紧凑卡网格 + 底部编辑胶囊）；v0.6.4 起主页面为**双行工具栏**（行1 搜索+存入右置 / 行2 类型+标签+操作，`renderTagbar` 渲染进 `.tagbar-wrap`）；**v0.6.5 卡片系统全量重构**——三区骨架（顶部徽章行+状态徽章 / 内容区类型专属 / 底部 meta 行+操作按钮）、统一等高 190px（文本区内部滚动）、富文本卡左右对比分栏（左普通文本/右白名单渲染格式，`safeRichNodes` 防 XSS）、26px 图标按钮、右上角只放删除、密码弹窗方案 22（下划线输入+浮动标签+👁显隐）。详见 CHANGELOG。

## 架构说明

```
server.mjs (入口薄层:静态服务 + 路由分发 + 过期清扫 60s + 自动同步 60s)
  ├── lib/core/        纯业务逻辑（不碰 HTTP；.js 扩展名，由 package.json type:module 声明 ESM）
  │   ├── config.js    全局配置单一来源（端口/输入边界/上传黑名单/UUID 白名单）
  │   ├── store.js     JSON 原子读写 + assertId + httpError + rmForce
  │   ├── clips.js     条目域聚合入口（v0.6.14 拆分子域后 re-export，对外路径不变）
  │   │   ├── clips-store.js     底层存取 + 滚动归档 + 共享内部函数（唯一数据底座）
  │   │   ├── clips-mutate.js    CRUD + 归档/恢复 + 复制计数/置顶 + 过期清扫
  │   │   ├── clips-query.js     列表/搜索/标签统计/标签管理
  │   │   ├── clips-transfer.js  导出/导入
  │   │   └── tombstones.js      墓碑（WebDAV 同步配套，90 天 TTL）
  │   ├── users.js     用户域：scrypt 密码 + 会话 token（v0.6.13 起文件即真相，零内存缓存）+ 登录限流
  │   ├── files.js     文件域：上传边界 + 归属校验 + 物理存取
  │   └── webdav.js    WebDAV 备份同步：双向合并 + 墓碑裁决 + 实体同步 + 定时自动同步
  │                   （v0.6.14：runSync 拆阶段函数；recordTombstoneIfConfigured 墓碑规则下沉）
  ├── lib/routes/      路由薄层（分段匹配零正则 + 会话中间件；只编排不决策）
  │   ├── index.js     路由注册表 + matchRoute/withParams
  │   ├── helpers.js   sendJson / jsonBody / multipart 解析 / requireAuth
  │   └── clips.js · users.js · files.js · sync.js
  └── public/index.html  单文件前端（v0.6.3 极简墙首页 + 主页面 + 弹窗体系；CSS 在 index.html，逻辑在 app.js）
```

**数据流**：前端 `fetch(BASE + "/api/..")`（Bearer token）→ server.mjs 路由匹配 → lib/routes 薄层（鉴权/读 body）→ lib/core 业务层（JSON 文件读写）→ 返回 `{ ok, ... }`。

**数据布局**（全部在 `CAP_STORAGE_DIR` 内）：
- `users.json`：用户列表（含 passHash）
- `sessions.json`：会话表（v0.3.0 落盘，token → { userId, createdAt, expireAt }，30 天过期）
- `users/<uid>.json`：每人活跃条目数组（上限 500 条，超出滚动进归档）
- `users/<uid>.archive.json`：每人归档（v0.2.0 滚动归档，最旧条目按 createdAt 移入，只读展示）
- `users/<uid>.tombstones.json`：每人墓碑（删除传播记录，90 天 TTL）
- `users/<uid>.webdav.json`：每人 WebDAV 配置（含明文密码）
- `files/<uid>/<fileId>.<ext>`：文件实体（随机名防路径穿越）

**排序（服务端单一实现）**：① pinned 置顶 → ② copyCount 降序 → ③ updatedAt 降序。严格按点击次数排序（v0.6.16 移除标签/内容归拢——归拢会把高次数卡片挤到后面，破坏次数排序）。读取时计算，任何修改即时反映。

## exe 桌面版（clipboard-exe/，WPF 重写）

> 第四交付形态，C# WPF net9.0-windows 原生重写（**非壳方案、非旧 WinForms 路线**——旧路线已废弃不参考）。
> 方案全文见 `docs/exe-wpf-plan.md`；状态：**M1 骨架 → M5c 全部完成**（2026-09-04，M5b 同步引擎 + M5c 配置 UI/定时同步 已通过 mock WebDAV 端到端验收：上传→合并→恢复→文件实体 + UI 刷新全链路），`--selftest` 223 断言全过；里程碑明细与 E2E 验收记录见 `docs/exe-wpf-plan.md` 里程碑表格。

- **还原铁律**：规则类逻辑（sortClips / rollToArchive / isExpired / resolveExpire / cleanUrl / mergeSnapshots / 墓碑）与 Web 版逐字一致，**唯一新增功能 = 置顶按钮**；数据 JSON 与 Web 版互导。改 Web 版排序/合并/清理规则时必须同步改 clipboard-exe，反之亦然。
- **发布**：框架依赖单文件（~1-3MB），需 .NET 9 Desktop Runtime（用户确认装运行库是小问题）；不采用 NativeAOT（WPF 数据绑定与裁剪兼容差）。
- **数据目录**：exe 同目录 `data/`（便携式）：`clips.json`（等价 Web 活跃区）、`settings.json`（置顶等偏好）、`clipboard-exe.log`（首行版本指纹）、`files/`（M3+）。
- **工程结构**：`Themes/`（Colors 设计令牌 = Web `:root` 逐项照搬 / Styles / Generic）+ `Controls/NeuBorder`（新拟态双阴影）+ `Services/`（AppLog / Settings / TrayIconService）+ `MainWindow`（深色主窗体 + 置顶）。
- **M1 验证**：构建 0 警告 0 错误；Release 单文件 173KB；启动不崩溃；日志首行 `clipboard v0.7.0 (dev)`。

## 关键问题与方案

### 问题：数据莫名被清空——多实例共存 + 测试直连主服务（2026-08-27 排查，环境问题）

**TL;DR**：同一天两次"数据全空"（billpotter 11 条消失，`archive.json`/`tombstones.json`/`webdav.json` 被删）。最终判定为**环境问题而非代码 bug**：机器上积累了 **20+ 个不同版本、不同数据目录的遗留 `server.mjs` 实例**（端口 8132-8139，08/26 起反复启动从未关闭），叠加冒烟测试默认直连 8130 主服务污染了真实 `users.json`（残留测试用户）。代码本身经全量测试 + 隔离副本复现（富文本编辑/清除格式/删除标签/存入富文本）验证**无可复现的清空路径**；数据经 WebDAV 远端快照完整恢复。

- 现象：两次"编辑/存入后列表全空"，本地条目文件被清、附属文件（archive/tombstones/webdav）消失、`files/` 实体目录被清空
- 排查链（可复用）：①全盘搜用户数据文件 → 确认仅一份数据（排除"两份数据"）②直连 WebDAV 远端对比快照 + `syncedAt` 时间戳 → **`syncedAt` 未更新证明"同步时本地已空、跳过了上传"**（关键判据）③墓碑表与活跃条目重叠检查 → 排除墓碑裁决误删 ④隔离副本复现用户操作 → 不触发清空 ⑤进程清查 → 发现 20+ 遗留实例
- 根因：环境混乱——不同版本实例共存（旧版本代码可能缺"本地空跳过上传"等保护）、测试直连主服务污染数据目录、实例长期不清理
- 修复/预防：①杀光全部遗留实例（仅保留 8130 主服务 + 8190 诊断页）②删除测试残留用户与孤儿文件 ③**冒烟测试默认端口 8130→8131**（必须指向独立数据目录实例，禁止连 8130）④WebDAV 远端快照是最终兜底（本次救回 11 条）
- 教训：**测试永远指向独立数据目录实例**；长跑环境定期清查遗留进程；`syncedAt` 时间戳是判断"本地是否曾空"的可靠证据

### 问题：搜索/标签过滤双轨——搜索词非空时增删改，清空搜索词后全量从界面"消失"（v0.6.14 修复）

**TL;DR**：`loadClips()` 曾把 `state.filter.q/tag` 拼进 `/api/clips` 走后端过滤并**覆盖 `state.clips` 为过滤集**，而 `renderList()` 又对 `state.clips` 前端过滤（搜索输入/标签点击只调 `renderList` 不调 `loadClips`）——一份数据被两处过滤。搜索词非空时发生任何增删改 → `refreshList→loadClips(q=词)` 把 `state.clips` 替换成过滤集 → 清空搜索框后 `renderList` 只能从过滤集里筛，**全量数据在界面上消失，须刷新页面恢复**。

- 根因：`loadClips` 后端过滤是早期设计遗留，v0.6.6+ 改前端即时过滤（注释明说"零网络请求"）时未删干净，双轨并存
- 修复：**前端单轨化**——`loadClips()` 恒拉全量（仅 `archived` 影响数据源范围），搜索/标签/类型/拼音全部由 `renderList` 本地过滤；标签筛选自愈（筛选标签全量已无 → 自动回全部）改为前端判断。后端 `listClips` 的 `q/tag` 参数保留（API 兼容，闲置）
- 教训：**同一份 state 只允许一个过滤器**；改"即时过滤"类交互时先查数据源是否还被旧参数污染

### 优化：批量编辑（多选 + 批量删除/加标签/减标签，v0.6.15）

**TL;DR**：工具行「编辑」进入多选模式，卡片整卡点击切换选择（透明覆盖层 + 左上角勾选框），底部悬浮条做全选/批量删除/批量加减标签。后端单入口 `POST /api/clips/batch` 按 `action` 分发。

- 关键设计：`getVisibleClips()` 抽纯函数——`renderList` 与「全选当前页」共用同一份过滤逻辑，避免两处各写一份导致"全选"与"渲染"范围不一致
- **关键坑（必守）**：`batchSetTags` 加减标签后**必须刷新 `updatedAt`**——`updatedAt` 是 WebDAV `mergeSnapshots` 的合并 key，不刷新则批量改标签**不会同步到远端**，也不会反映到排序（编辑过的条目不浮到前面）。对照单条 `updateClip` 同样刷新，批量必须对齐
- 删除语义：`batchDeleteClips` 跨活跃区+归档，与单条 `deleteClip`/`deleteArchivedClip` 完全一致——记墓碑（由 `recordTombstoneIfConfigured` 判断是否已配置 WebDAV）+ 路由层联动 `deleteFile` 清理文件实体
- 交互：进入批量模式用透明覆盖层（`z-index:5`）盖住整卡承载点击，勾选框 `z-index:6` 在上——这样卡片内所有复制/编辑/图片预览行为天然失效，无需逐个禁用；退出批量（完成/删除/登出/会话失效）统一走 `resetBatchMode()`

### 问题：AI 沙箱里 `fs.rmSync` 报 `[safe-delete] 操作失败`（v0.3.1 容错 / 2026-08-14 澄清根源）

**TL;DR**：WorkBuddy（AI 沙箱）通过 `NODE_OPTIONS=--require=genie-safe-delete.cjs` 注入安全删除 shim，把 `fs.rmSync`/`fs.rm` 猴补丁为"送回收站"（genie-trash 二进制），该二进制在 Windows 11 无交互上下文返回 exit 1 + "Some operations were aborted"，但文件实际已删除。删除一律用自写 `rmForce()`（逐个 try/catch 容错）。

- 现象：删除用户数据/文件实体时 `fs.rmSync(path, { recursive: true, force: true })` 抛 `[safe-delete] 操作失败: Some operations were aborted`
- 根因（2026-08-14 实测澄清，非 Node 22 原生行为）：`fs.rmSync.name === "wrappedRmSync"`——被 WorkBuddy 注入的 genie-safe-delete.cjs shim 包裹；shim 把"回收站操作被系统中止"（E_ABORT/DE_OPCANCELLED，无交互桌面下 IFileOperation 常见）误判为删除失败并抛错，但删除实际完成
- 边界：**仅 AI 沙箱环境触发**（用户手动 `node xxx.js` 时 NODE_OPTIONS 为空，rmSync 原生正常）；且非全局——用户主目录/项目目录抛错，`C:\Temp` 等部分路径正常（与回收站/受保护目录相关）
- 解决：`lib/core/store.js` 实现 `rmForce()`——stat 判断类型后手动 `readdir + 递归` 或 `unlink`，每步 try/catch 吞掉（"抛错但实际已删"），以"文件是否存在"判定结果
- 预防：项目内所有删除路径（删用户/删文件/过期清扫）统一走 `rmForce`；排查删除类报错先看 NODE_OPTIONS 是否有 genie-safe-delete，别归咎于 Node 本身

### 问题：平台反代挂在 `/tool/<id>/` 子路径，前端路径写死会 404

**TL;DR**：前端一律 `window.__BASE__ + "/api/.."`；独立运行时平台不注入 `__BASE__`，兜底 `window.__BASE__ || ""`。

- 问题：工具在平台里被反代到 `/tool/clipboard/`，写死 `/api/clips` 会打到平台自身 API
- 根因：tools-center proxy.js 只向 HTML 注入 `<script>window.__BASE__="/tool/<id>";</script>`
- 解决：页面加载先 `const BASE = (window.__BASE__ || "").replace(/\/+$/, "")`；所有请求走 `api(path)` / `apiBlob(path)` 封装
- 预防：新增前端请求时统一走 api 封装，不要散用 fetch

### 问题：删除与"全部清空"的同步语义必须相反（WebDAV）

**TL;DR**：单独删除 → 记墓碑（deletedAt）→ 下次同步传播删除（防旧备份把已删条目复活）；全部清空 → 不记墓碑（= 用户"想从网上同步"，下次同步从远端拉回恢复）。

- 问题：若删除也传播、清空也传播，用户清空后想从备份恢复却把远端也清空
- 根因：两种操作的意图完全不同——删除是"这个条目不要了"，清空是"本地重来"
- 解决：`deleteClip` 记墓碑；`clearAllClips` 清空墓碑。合并裁决：墓碑 `deletedAt > 条目 updatedAt` → 删除；条目 `updatedAt > 墓碑 deletedAt` → 保留（删后又被编辑）
- 预防：改同步语义前先想清楚"用户意图"，墓碑是防复活的关键，90 天 TTL 防无限增长

### 问题：清空后上传会把远端备份覆盖成空（数据丢失风险）

**TL;DR**：合并前本地无数据（新设备 / 清空后）→ 跳过上传，只拉回远端——防空备份覆盖远端。

- 问题：清空本地后一键同步，远端快照被空数组覆盖，另一台设备恢复时数据全丢
- 根因：上传逻辑没区分"本地没数据"与"本地想清空"
- 解决：`runSync` 中 `hadLocal = localClips.length > 0 || localTomb.length > 0`；为 false 时跳过 `uploadSnapshot`，远端数据已拉回本地，下次同步本地非空自然收敛
- 预防：任何"双向合并"都必须有"空侧不覆盖"保护

### 问题：标签/内容归拢破坏点击次数排序（v0.6.16 移除）

**TL;DR**：`sortClips` 按 copyCount 排好后，`groupByTags`/`groupSimilar` 会把共享标签/相似内容的条目拉到一起，导致高次数卡片被挤到低次数卡片后面，用户反馈"卡片没有按点击次数排序"。修复：**移除归拢，严格按 pinned → copyCount → updatedAt 排序**。

- 现象：点击次数 3 的卡片排在点击次数 4 的前面（归拢把共享标签/相似内容的条目提前）
- 根因：排序管道 `sortClips → groupByTags → groupSimilar` 中，后两步基于"归拢"重排，破坏前一步的全局次数序
- 解决：`listClips` 直接 `sortClips(list).map(publicClip)`，删除 `groupByTags`/`groupSimilar` 死代码
- 教训：排序需求必须保证"主排序键全局单调"；任何后续重排都会破坏它

### 问题：会话 token 纯内存，服务重启全部掉线

**TL;DR**：token 存 `sessions` Map（内存态），重启即失效需重新进入——多用户工具可接受，但需在文档/UI 说清。

- 问题：服务重启后所有用户被踢回选用户页
- 根因：设计取舍——token 不落盘，避免额外存储与过期管理
- 解决：前端 localStorage 存 `cur`（id/token/name/color），boot 时带 token 恢复会话；401 时 `handleSessionLost()` 自动回选用户页
- 预防：如未来要持久会话，改 users.js 的 createToken/verifyToken，落盘 + 过期扫描，其余层不动

### 问题：文件下载被浏览器执行（XSS 风险）

**TL;DR**：下载一律 `Content-Disposition: attachment` + `application/octet-stream` + nosniff——SVG/HTML 也不会被执行。

- 问题：上传 HTML/SVG 后浏览器可能直接渲染执行脚本
- 根因：静态服务默认按扩展名猜 Content-Type 内联展示
- 解决：上传黑名单（BLOCKED_MIME/BLOCKED_EXT 双保险）拒可执行/脚本类；下载强制 attachment + `X-Content-Type-Options: nosniff`；存储用随机文件名（`<fileId>.<ext>`）防路径穿越
- 预防：下载侧永远是最后防线——attachment 别改 inline；新增上传类型先过黑名单

### 问题：零依赖下解析 multipart/form-data

**TL;DR**：基于 Buffer 字节定位手写解析（`parseMultipart`）——`--boundary` 分割 + 头部/数据切分 + filename 判定文件字段，兼容二进制内容。

- 问题：平台无第三方依赖约束下不能引 formidable 等库
- 根因：Node 内置没有 multipart 解析器
- 解决：`helpers.js parseMultipart`——找 `\r\n--boundary` 切 parts，每 part 切 `\r\n\r\n` 头/体，`filename=` 存在视为文件字段（保留 buffer/mime），否则 utf8 文本
- 预防：文件上传走 `/api/files`（multipart），新增文件类字段沿用此解析器

### 问题：会话 token 纯内存，服务重启全部掉线（v0.3.0 持久化）

**TL;DR**：会话表落盘 `sessions.json`（内存 Map 做缓存 + 文件做持久层），token 带 30 天过期，重启不掉线；改密/删号仍联动销毁。

- 问题：服务重启后所有用户被踢回选用户页，多用户工具体验割裂
- 根因：`sessions` Map 纯内存态（原设计取舍）
- 解决：`ensureSessionsLoaded()` 惰性从文件加载 → 内存缓存；`createToken`/`destroyToken`/`destroyUserSessions` 变更即 `persistSessions()`；`verifyToken` 惰性删过期；server 60s 周期 `pruneExpiredSessions()`
- 预防：token 校验是高频路径（每个 API 请求），**必须保持内存缓存 + 文件兜底**，不要每次请求读文件；过期清理惰性即可，不要每请求写盘

### 问题：滚动归档把新存入条目立即移走（v0.3.1 修复）

**TL;DR**：rollToArchive 按 copyCount 排序保留前 N，新条目 copyCount=0 被当低价值滚进归档——改为按 updatedAt 保留最近更新的。

- 问题：用户存满 500+ 条（带复制次数）后，每存一条新内容立即从列表消失
- 根因：`rollToArchive` 用 `sortClips()`（置顶→复制次数→更新时间）保留前 N，新条目排末尾
- 解决：改按 `updatedAt` 降序保留前 N（刚存入/刚复制/刚编辑的 updatedAt 最新，绝不进归档）
- 预防：归档判定优先级=「用户最近在意的」，不要用"价值排序"（新数据天然低价值）

### 问题：登录限流全局连坐（v0.3.1 修复）

**TL;DR**：限流 key 从 IP 改为 ip:userId，只锁同一 IP 对同一用户的尝试。

- 问题：本机所有请求都是 127.0.0.1，A 输错 8 次后 B/C 全部被锁 60s
- 根因：`loginGuard` 按 `remoteAddress` 记
- 解决：`loginKey(ip, userId)` 组合 key；路由层传 body.id 作为维度
- 预防：限流维度 = 攻击者的"目标资源"，不能只按来源

### 问题：会话失效后列表空白（v0.3.1 修复）

**TL;DR**：handleSessionLost 重置 filter 缺 type/archived 字段 → renderList 里 type=undefined 过滤掉全部条目——改用 resetFilter()。

- 问题：被踢/被删号后重进任何用户，列表全空
- 根因：`state.filter = { q:"", tag:"" }` 缺字段，与 resetFilter 不一致
- 解决：handleSessionLost 调 resetFilter()
- 预防：filter 重置只有 resetFilter 一个入口，禁止手写对象

### 问题：Windows 沙箱下 unlinkSync 抛错但实际已删除（v0.3.1 修复）

**TL;DR**：safe-delete 拦截导致 unlinkSync/rmdirSync 抛 `[safe-delete]` 错误，但文件实际已删——rmForce 逐个 try/catch 吞掉，避免批量清理中断。

- 问题：deleteUser 中途 500，后续 tombstones/webdav 清理不执行
- 根因：rmForce 的 unlinkSync 在沙箱 safe-delete 拦截下抛错（文件实际已删）
- 解决：rmForce 每步 try/catch 吞错
- 预防：删除类工具函数一律容错，删除结果以"文件是否存在"为准而非"是否抛错"

### 问题：条目无限增长，JSON 文件越写越大（v0.2.0 滚动归档）

**TL;DR**：活跃区设上限（500 条），超出后按排序价值保留前 N、最旧移入归档文件——不删数据，单文件可控，搜索默认只查活跃区。

- 问题：用户不设过期则条目永久堆积，users/<uid>.json 无限膨胀，读列表/排序/搜索变慢
- 根因：只有"手动删/过期"两条清理途径，都没有默认保障
- 方案对比：截断删除（丢数据）vs 分片拆分（同步协议重写、读盘量不减）vs **滚动归档**（零丢失 + 改动小）——选归档
- 解决：`saveClips` → `rollToArchive`（排序价值保留前 MAX，其余按 createdAt 升序追加进 `<uid>.archive.json`）；`listClips` 加 `archived` 参数合并归档（标记只读）；清空/删用户/过期清扫全部覆盖归档
- 预防：**同步快照只含活跃区**（归档是本地扩展，协议零改动）；新增容量相关功能先想"丢不丢数据"

### 问题：Set 没有 slice 方法导致导入崩溃（v0.2.0 修复）

**TL;DR**：`sanitizeImported` 里 `new Set(...).slice()` 语法错误（Set 无 slice），导入即崩——改为先展开数组再 slice。

- 问题：POST /api/import 任何数据都报 `Set.slice is not a function`
- 根因：写 `new Set(...).slice(0, N)` 时误以为 Set 是数组
- 解决：`[...new Set(...)].slice(0, N)`
- 预防：Set 转数组必须先展开；导入类新代码写完后用真实 JSON 跑一次端到端

### 问题：点击卡片复制内容也会弹出"存入"大窗口（v0.1.3 修复）

**TL;DR**：卡片复制写剪贴板会触发 `clipboardchange` 监听 → 自动弹存入窗；用"来源抑制时间戳"区分——卡片点击复制时 800ms 内不自动弹。

- 问题：点卡片复制文本/图片后，刚复制完就弹出"存入内容"大窗口，体验割裂
- 根因：`copyText`/`copyImageToClipboard` 写入系统剪贴板 → `clipboardchange` 事件触发 → boot 里的监听器误以为是"外部复制内容"→ `openPasteModal(true)`
- 解决：全局 `suppressAutoPasteUntil` 时间戳——`card.onclick` 入口先置 `Date.now() + 800`，`clipboardchange` 回调开头检查 `Date.now() < suppressAutoPasteUntil` 则跳过自动弹窗
- 预防：所有"监听剪贴板变化做自动化"的逻辑，必须先排除"本应用自己写剪贴板"的路径（来源抑制），否则自我触发

### 优化：内容去重确认（前端）

**TL;DR**：存入时若文本/链接内容已存在，弹确认"仍要存入？"——不打断文件条目，防误存重复。

- 问题：同一内容反复粘贴存入，列表冗余
- 根因：无重复感知
- 解决：openPasteModal 保存前 `state.clips.some(c => url===content || content===content)` 命中则 askConfirmP 确认
- 预防：重复检测只针对文本/链接（文件有 fileId 天然唯一），不打断文件流程

### 问题：富文本复制到 Word 字体变宋体 / 无格式（v0.6.9 定稿 / 2026-08-16 全链路实测）

**TL;DR**：三条铁律叠加——①浏览器写剪贴板**强制剥 `<style>` 块/html/xmlns，只留 inline style**（Chromium 122+ `ClipboardWellFormedHtmlSanitizationWrite`，实测 `clipboard.write` 完整文档读回仅 98B）；②**Word 识别"来自 Word"靠 `xmlns:w="urn:schemas-microsoft-com:office:word"` 标记**（Microsoft roosterjs `isWordDesktopDocument.ts` / CKEditor paste-from-office），片段不包装则 Word 判定"来自网页"→ 默认合并格式（宋体）；③`execCommand + setData` **不受** Chromium 122+ sanitize 影响（W3C clipboard-apis#193），带 xmlns 完整文档原样进 CF_HTML → Word 保留格式。**解法：存入时 `normalizeRichHtml` 把 style 块内联到元素；复制时 `buildWordDoc` 包装 xmlns:o/w/m + StartFragment；`execCommandRich` 主路径（holder 纯文本承载，绝不 innerHTML 解析完整文档——DOM 会剥 html/head/xmlns）**。

- 现象：富文本卡右栏复制 → Word 粘贴 → 字体变宋体/无格式；诊断页（独立标签页 execCommand + 原始 Word html）却正常
- 根因链：存入 html 样式在 `<style>` 块（浏览器写剪贴板剥块）→ 无样式；且无 xmlns 标记 → Word 判"来自网页" → 合并格式；`clipboard.write` 走 Chromium sanitize 压缩（读回 98B）更糟
- 排查工具：`public/diag.html`（`/diag.html` 路由）——实测 iframe 内 clipboard/execCommand/paste 可用性 + 端到端模拟（写后读回自证），**排障先跑它，不靠猜**
- 解决：`normalizeRichHtml`（存入内联化）/ `buildWordDoc`（复制包装 xmlns）/ `execCommandRich`（setData 原始完整文档，holder 纯文本）
- 环境边界：**预览 iframe 无剪贴板权限时 execCommand/clipboard 可能失败**——富文本复制请在独立浏览器标签页使用；Word 2405+（2024-05）默认"从其他程序粘贴=合并格式"（微软官方行为变更），可改 文件→选项→高级→剪切复制粘贴→从其他程序粘贴→保留源格式
- 预防：富文本链路改动后跑 diag.html 第 7 项（execCommand 复制原始 Word html → 读回含 xmlns 与 inline style）验证；复制类问题先确认粘贴目标（WorkBuddy 输入框/记事本=纯文本，必无格式）

### 问题：富文本复制到 Word 仍"只还原一部分"——CSSOM 剥私有属性 + body 属性丢失（v0.6.12 / 2026-08-26 最小单元链路诊断定位）

**TL;DR**：上一轮（v0.6.9）解决的是「style 块被剥 + 无 xmlns 识别」；这轮暴露两处**静默丢属性**：①`normalizeRichHtml` 用 CSSOM `rule.style.cssText` 收集 style 块规则——**CSSOM 只序列化浏览器认识的属性**，`tab-interval/text-justify-trim/mso-*` 全被丢弃、`word-wrap` 被规范化为 `overflow-wrap`；②Word 文档级设置（`tab-interval:21.0pt;word-wrap:break-word;text-justify-trim:punctuation`）写在 **`<body>` 标签**上，而旧实现返回 `doc.body.innerHTML`——**body 自身属性不在 innerHTML 里**。两处叠加 → 基础格式（标准 CSS 内联成功）在、mso 细节格式缺 = "只还原一部分"。

- 现象：Word 复制 → 工具存入 → 复制 → Word 粘贴，基础格式（字号/颜色/加粗）还原但 tab 制表位/文档级设置缺失；诊断页 C4（浏览器渲染）效果正确
- 根因链：`rule.style.cssText` 过 CSSOM → 私有属性丢（实测 `tab-interval:36pt;word-wrap:break-word;text-justify-trim:punctuation;mso-fareast-font-family:宋体;color:red` 只回 `overflow-wrap: break-word; color: red`）；`doc.body.innerHTML` 不含 body 属性
- 排查工具：`.verify/rich-chain-diag.html`（8190 静态服务）——6 步链路每步统计 style 块/内联属性/CSS 属性集合前后对比 + 真实剪贴板写读/粘贴回读；**注意 `clipboard.read()` 返回剥壳片段（无 body 壳），body 属性缺失是 read() 形式所致、非 CF_HTML 缺失**——验证 CF_HTML 用真实 Ctrl+V 粘贴回读（paste 事件 getData）或 copy 事件内 setData 回读
- 解决：`normalizeRichHtml` ①style 块改 `st.textContent` 正则字符串级解析（声明原样保留，跳过 `@` 规则）②返回 `<body 全部属性>innerHTML</body>`（遍历 `body.attributes`）③双保险——body style 内联到段落元素（p/div/h1-h6/li），因 CF_HTML 规范（MS Learn aa767917）粘贴应用主要解析 StartFragment/EndFragment 之间的 Fragment，body 属性在 Fragment 外的 context 里；`buildWordDoc` 兼容 `<body attrs>…</body>` 片段（属性并入外层 body，避免嵌套）
- 验证：真实 Word 复制 html（42550 字）全链路——S2 lost[无]、C3a/C3b 无 CSS 属性丢失、真实 Ctrl+V 粘贴回读 4997 字（mso×92/o:p×5/xmlns/body 属性全保留）；开关组合实验（诊断页「还原开关实验台」）确认「不勾 head XML + 其余全开」= 当前输出 = Word 正确还原组合
- 边界：旧条目（修复前存入）html 字段仍是残缺数据，需**重新存入**才走新逻辑；Word 对 HTML 导入的 CSS 支持有清单边界（MS Learn aa338201：CORE/EXT/FULL 必还原，mso-* 私有仅 Word 来源路径生效）
- 预防：改 `normalizeRichHtml`/`buildWordDoc` 后跑 `rich-chain-diag.html` 全链路 + 真实粘贴回读；断言检查「mso 属性数」「body 属性存在」

## 开发记录

- 2026-08-27（v0.6.13 去缓存）：**会话去内存缓存——文件 sessions.json 即唯一真相**。用户决策：小体量(单机/低并发)下每请求读文件无感（实测读+解析 774B JSON 单次 ~14μs），换来模型最简——无"内存/文件两份数据"的一致性问题（登录态丢/退出复活/回滚逻辑全部根除）。⚠️ **注意事项：若未来请求量达每秒几十次以上（多用户高并发/常驻服务被机器高频调用），再引入"内存缓存 + TTL 失效(如 1s)"，不要提前加**；登录限流表(loginGuard)是临时状态（重启丢失无害），保持内存即可
- 2026-08-26（v0.6.13 终批）：富文本「清除格式」按钮（编辑弹窗格式文本条目一键转纯文本，保存时 html='' 后端 sanitizeHtml 空串即清空；按钮 primary 金色与保存同款，标记态 warn 红）/ 标签栏换行（flex-wrap:wrap，多标签全部可见可点）/ 标签筛选自愈（loadClips 内：标签筛选下结果为空且无搜索词 → 自动清 filter.tag 回全部，防"内容消失"假象）/ P-1 连续改账号名迁移标记丢失（prevAccountName 单值覆盖 → `pendingNameMigrations` 数组逐个迁移，删除成功才移除）/ P-2 改账号名实体目录不迁移（`syncFileEntities` 加 pendingNames 参数：本地缺实体且新名 404 → 从历史账号名目录拉取→写本地+传新名+删旧）/ 账号名仅限英文数字（`ACCOUNT_NAME_RE`，身份键字符集约束，显示名不受限）/ 账号名修改通道（`POST /api/users/:id/account-name` 管理员级一次性操作，记 pendingNameMigrations 下次同步自动迁移）
- 2026-08-26（v0.6.13）：**双名模型重构**（user = accountName 账号名不可变=身份键 + displayName 显示名可变=仅展示，Stack Overflow/Google AppEngine 权威依据；旧数据 `accountName||name` 读取归一不迁移文件）+ **WebDAV 按账号名寻址**（快照 `clipboard-<accountName>.json`、实体 `files/<accountName>/`——设备迁移 = 新部署建同名账号→同步拉回；删 syncedName 改名迁移复杂度，runSync 回归单源合并，仅留旧格式 `clipboard-<userId>.json` 一次性兼容迁移）/ 改名持久化治本重构（LS.cur 只存会话凭据 {id,token}，展示信息以后端 users.json 为唯一权威源，删两处补丁）/ 原子写 Windows 兼容（writeJson rename EPERM 重试 3 次 + 删目标兜底 + 清 tmp）/ 进程级兜底（uncaughtException/unhandledRejection 记录不退出）+ 同步并发锁（runSync per-user in-flight，409「同步进行中」）+ 死代码清理（getClip/readFileBuffer/makeRichBtn）
- 2026-08-26（v0.6.13 架构微调）：`mergeSnapshots` 纯函数化确认（无副作用已导出）+ 单元测试 `scripts/test-merge-snapshot.mjs`（17 断言：双端取新/墓碑裁决/删后又编辑保留/墓碑取最新/远端 null/归档标记/空快照容错）；新增 `docs/main-flow.md` 主线 SSOT（三视角方法论的主线视角，AGENTS 约定区挂引用）——前端拆 module **有意不做**：经典 script 无法 import module export，全量 module 化/命名空间化成本>收益（已有 playwright 回归覆盖纯函数可单测的边际收益），超 2500 行或出现耦合症状再评估
- 2026-08-26（v0.6.13）：WebDAV 归档完整备份（快照=活跃∪归档，归档带 archived 标记；拉回分拣写回先 saveArchive 后 saveClips）+ 归档条目可删除（deleteArchivedClip + 墓碑传播）/ 编辑弹窗「归档」按钮（archiveClip）+ 归档可恢复（unarchiveClip，updatedAt 刷新防滚动）/ 文件实体目录修复（syncFileEntities 先 MKCOL files/<uid>，严格服务器 PUT 到不存在目录 409）/ 修改显示名入口（renameUser，双名模型前为改用户名）/ 一键无饱和度配色（RGB 三元组变量 + html.mono 覆盖全灰）/ WebDAV 地址留空默认局域网 / 同步文案简化 / UI 背景调深（--elev #1F1F1F）+ 编辑弹窗标签区间距 + 图片预览触发区收窄
- 2026-08-26（v0.6.12）：富文本复制链路修复批——S2 内联化弃用 CSSOM cssText 改字符串级（保 Word 私有属性）/ body 标签属性保留 + buildWordDoc 兼容 body 片段 / body 样式双保险内联段落元素（CF_HTML Fragment）/ 卡片富文本回归左右分栏 + 取消渲染预览与编辑实时预览；最小单元链路诊断页（rich-chain-diag.html）定位
- 2026-08-16（v0.6.11）：细节审查修复批——归档去重防膨胀（rollToArchive 按 id 去重，实测修复 300→600 翻倍）/ verifyPassword 损坏数据不崩溃（hex+长度校验）/ 导入非 UUID id 重生成 / 富文本编辑 html 同步（textToHtml 重建）/ 登录限流表真修复（lastFailAt 窗口回收，P1-3 假修复）/ WebDAV 实体扩展名兜底 .bin / readBody 超限排空 / diag.html 缓存 / 同步间隔 30min 支持
- 2026-08-16（v0.6.9）：富文本复制链路定稿——normalizeRichHtml/buildWordDoc/execCommandRich 三函数统一，删除全部历史缠绕函数；隔离诊断页 diag.html 实测定位
- 2026-08-16（v0.6.8）：富文本链路重构 + 场景走查报告（docs/walkthrough/）+ 诊断页
- 2026-08-16（v0.6.7）：WebDAV 远端统一子目录 workbuddy/剪贴板/
- 2026-08-16（v0.6.6）：弹窗四件套重设计（密码/数据管理/存入/编辑）+ 重复检测单弹窗 + 窄屏适配 + 标签/开关黑金化
- 2026-08-09（init）：多用户剪贴板工具——万能入口/智能排序/拼音搜索/标签/WebDAV 备份同步；提交 `a3b7042`
- 2026-08-09（chore）：添加 API 部署脚本（github.com 被墙时走 Git Data API）；提交 `36cd53b`
- 2026-08-12（docs）：按单项目规范补齐四件套（AGENTS / DEVELOPMENT / CHANGELOG），README 校对
- 2026-08-12（v0.1.3）：修复卡片复制触发自动弹窗（suppressAutoPasteUntil 来源抑制）
- 2026-08-12（v0.2.0）：P0 四件套——星标收藏 / 滚动归档 / 导出导入 / URL 清理
- 2026-08-12（v0.3.0）：P1 三项——会话持久化（sessions.json 落盘）/ 标签管理（重命名删除）/ JSON 格式化预览
- 2026-08-12（v0.3.1）：修复模拟执行检查 7 问题——归档保留策略/限流 key/filter 重置/删用户残留/rmForce 容错/空标签/标题 utm
- 2026-08-12（v0.4.0）：前端 JS 拆 public/app.js（index.html 1251→125 行）+ 图片卡片缩略图/预览按钮/hover 浮层
- 2026-08-12（v0.4.2）：图片预览完善（默认100%/缩放/拖拽/步长可调）+ 触发逻辑重构（先比对再弹窗）+ 弹窗/入口重构（密码/数据管理/标签管理/一键同步/复制提示定位/完整时间）
- 2026-08-12（v0.4.3）：架构评估 v2 三项落地——hover 状态显式化（previewState）/ openDataModal 拆 WebDAV 区（renderWebdavSection）/ 重复检测抽纯函数（findDuplicateClip）
- 2026-08-12（v0.4.4）：函数粒度拆分（架构评估 v3 量化落地）——clipCard CC49→15（make*Btn 工厂 + bindImageHoverPreview）/ openPasteModal CC46→22（savePasteContent + autoFillPasteModal），app.js 无 CC>22
- 2026-08-12（v0.4.5）：逻辑核验加固（状态图验证方法论）——3 处异步补 guard（logoutBtn/archChk/用户卡 enterUser）+ findDuplicateClip 字段兜底
- 2026-08-12~13（v0.5.0-设计稿）：主页面整体 UI 重构设计稿（design-preview.html）——常用优先分区直达/等高网格/渐进式披露卡片/磨砂玻璃深色系/5 磨砂弹窗/极窄顶栏 ⋯ 菜单。**未落地**。⚠️ **2026-08-14 已废弃**：UI 重构确定不做，设计稿已删除（v0.5.2）
- 2026-08-14（v0.5.1-P0 修正批）：补 package.json（type:module + engines + npm scripts）；测试脚本参数化（TEST_PORT/TEST_DATA_DIR）；mock-webdav safeJoin 路径修复（path.resolve 统一两侧，相对路径不再 400）；safe-delete 根源澄清（WorkBuddy shim 注入，非 Node 原生）；README .data 纠偏
- 2026-08-14（v0.5.2）：取消 UI 重构，删除 design-preview.html 设计稿
- 2026-08-15（v0.6.0）：**暗黑新拟态 UI 迁移**（依据《UI迁移手册.md》：双阴影浮雕/去边框/分段控件/新拟态开关/主色粉→蓝紫；修复 transform 劫持 fixed 定位的图片预览浮层"飘远" bug）+ **富文本双格式复制**（条目 `html` 字段 ≤512KB、`copyRich` 双 MIME 写剪贴板、卡片 🅡 按钮；决策不上数据库——JSON 规模足够且保零依赖）+ 新增 `cc-measure.mjs`/`test-html-field.mjs`

## 测试

- `scripts/smoke-test.mjs`：API 冒烟 34 项（用户/条目/文件/搜索/隔离/安全/改密）——需独立数据目录实例
- `scripts/test-webdav-sync.mjs`：WebDAV 端到端 19 项（墓碑传播/清空恢复/实体同步）——需 mock WebDAV 8180
- `scripts/test-auto-sync.mjs`：自动同步（间隔到期触发）
- `scripts/mock-webdav.mjs`：极简 WebDAV 服务器（MKCOL/PUT/GET/DELETE + Basic admin:admin123）
- `scripts/test-html-field.mjs`：富文本 html 字段单测 10 项（新建/截断/更新/清空/导出/导入）——无需起服务
- `scripts/cc-measure.mjs`：圈复杂度/认知复杂度/LOC 测量（AST 级 tokenizer）

**注意**：webdav 测试固定用户名非幂等，重跑前清空测试数据目录（见 AGENTS.md 关键坑）。
