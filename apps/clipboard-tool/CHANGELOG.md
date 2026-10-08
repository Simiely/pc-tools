# CHANGELOG.md

## v0.7.6 (2026-09-13) — 修复「同步后另一台设备的图片/附件无法下载」+ 实体同步默认开启

> 用户实测反馈：**图片在本机可以正常复制，但同步之后，另外的设备无法下载和使用**。新增跨设备 E2E 复现后定位为**产品语义缺陷**（不是传输/编码 bug）——`syncFiles`「同步文件实体」**默认关闭**且藏在数据管理弹窗里，而该开关**同时管着上传和拉回**：没勾 → 条目（文字/标题/标签）照常同步、卡片在另一台设备可见，但文件实体既不上云也不拉取 → 下载 404、图片裂图，且前端只提示笼统的「下载失败」，用户完全看不出原因。

### 🐛 语义修正：拉回无条件，只有上传受开关控制（核心）
- `lib/core/webdav.js` `syncFileEntities`：`cfg.syncFiles` 从此只决定「本地有实体时是否 PUT 上云」；**本地缺失实体时无条件 GET 远端拉回**。未勾选场景新设备也能恢复，不再出现「卡片在、文件死」。
- 配套：`runSync` ④ 去掉 `if (cfg.syncFiles)` 守卫（改为无条件调用）；`ensureOneDir`（`files/` + `files/<账号名>/` 的 MKCOL）仅在需要上传时执行——只拉回的场景不该在远端凭空建空目录。
- exe 端同步改（双版本规则一致铁律）：`Services/SyncEngine.cs` `SyncFileEntities` 同样拆分上传/拉回语义，`RunSync` 去掉守卫。
- AssemblyInfo `0.7.5 → 0.7.6`。

### 🧪 exe 侧验证（.NET 9 SDK 9.0.317 装好后补验，全部通过）
- **构建**：`dotnet build -c Release` **0 错误**（162 个 CA1416 警告为 TrayIconService/TrayDarkMenu 既有，非本次引入）。
- **`--selftest`**：**ALL PASS**（含 `M5 配置：round-trip SyncFiles/AutoSync/Interval/Account` 等关键项，零回归）。
- **双版本差分门禁**：`npm run test:diff` **41 fixtures（dedup 11 + sort 10 + cleanUrl 15 + expiry 5）全部对拍一致**，difftest 构建 0 错误——本次改动跨端规则无漂移。
- **发布产物自检**：`PublishSingleFile` 单文件 `Clipboard.exe` 839.7 KB，发布版跑 `--selftest` 同样 **ALL PASS**。

### ✅ 默认开启
- `saveSyncConfig`（webdav.js）：未显式传 `syncFiles` 时——首次配置 = **true**，后续沿用旧值；显式传 `false` 仍尊重。
- **存量一次性迁移**（`webdav.js migrateSyncFilesDefaults` + `server.mjs` 启动调用）：旧版 `!!syncFiles` 把默认 false **写死进了每一份 `webdav.json`**，改代码不会改变已落盘的值——存量用户仍是不备份状态。启动时扫描 `*.webdav.json`，把 `syncFiles:false` 翻成 true 并打 `v0619SyncFiles` 标记；**只翻「从未迁移过」的配置，迁移后用户再主动关闭会被尊重**（不会被再次翻开）。启动时若发生翻转会打印一行日志。
- exe：`Models/SyncConfig.cs` `SyncFiles` 属性默认值 `false → true`；`DataDialog` 加载既有配置 `?? false → ?? true`。
- 前端 `app.js`：WebDAV 配置弹窗「同步文件实体」复选框默认勾选；读取时 `r.syncFiles !== false`（缺省视为开，存量显式 false 仍尊重）。

### 💬 提示与文案
- `apiBlob` 404 分支：由「下载失败」改为**可操作提示**——「文件实体缺失：本机与云端都没有该文件，请在存有该文件的设备上开启『同步文件实体』后同步一次」；其它状态码带 HTTP 码。
- `downloadFile` 增加空 blob 守卫（`!blob.size` → 抛「文件实体为空」，避免静默下载 0 字节文件）。
- 两端复选框文案补上关闭后果：「…（关闭后不备份上云，但本机仍会尝试从云端拉回缺失的实体）」。

### 🧪 测试
- **新增 `scripts/test-cross-device-files.mjs`**（跨设备实体同步回归门禁）：自起 mock-webdav(8181) + 两个独立实例(8133/8134)，同账号名、不同 userId 模拟真实两台设备。18 断言覆盖：A 上传图片→上云 / B 未勾 syncFiles **仍能拉回且下载 200**（核心回归）/ 勾选后自愈+字节一致 / **未勾时不上传**（反向验证上传仍受控）/ 新建配置默认 `syncFiles=true`。
- 回归：smoke **42/42**、WebDAV 集成 **19/19**、merge-snapshot **17/17**、auto-sync **3/3** 全绿零回归。

### 📦 发布
- **三形态全发**：`clipboard-tool-v0.7.6-exe.zip`（597.2 KB，DEFLATE，单文件 `Clipboard.exe` + 使用说明）、`clipboard-tool-v0.7.6-bat.zip`（430.7 KB）、`clipboard-tool-v0.7.6-platform.zip`（480.6 KB，后两者 STORE 平铺）。
- Release：<https://github.com/Simiely/clipboard-tool/releases/tag/v0.7.6>

### 🔧 P1 补丁（同版本内 exe 资产重发）：同步失败 mediaType + Ctrl+V 识别不了文件

> 首发 exe 后真机立刻暴露两个问题，**均为 exe 侧**（bat / platform 未受影响）。已修复并**替换 Release 同名资产**（旧包下载次数 0）。

**① 同步失败 `The value cannot be an empty string.(Parameter 'mediaType')`**
- 根因：`WebDavClient.UploadFile` 里 `new MediaTypeHeaderValue(mime ?? "application/octet-stream")`。
  **C# 的 `??` 只判 null、不判空字符串**——而 `FileStore.MimeFromPath` 对未知扩展名返回 `""`（很常见），
  条目 `fileMime` 为空串时原样传入 → 构造 HTTP 头抛异常 → **整个同步被这一个文件中断**，用户只看到「同步失败」。
- 修复：抽出纯函数 `WebDavClient.MimeOrDefault(mime)`，空/空白 → `application/octet-stream`
  （对齐 Web 端 `c.fileMime || "application/octet-stream"`——JS 的 `||` 对空串也兜底，故 Web 端从未踩此坑）。
- **为何现在才暴露**：本版把「同步文件实体」改为默认开启后，上传分支第一次真正被执行
  （此前 `if (cfg.SyncFiles)` 默认 false，从不上传）——**被新默认值唤醒的潜伏 bug**。

**② Ctrl+V 识别不了剪贴板里的文件**
- 根因：与代码注释中已记载的图片坑**完全同源**——剪贴板只有 FileDrop、没有文本时，
  `TextBox` 粘贴命令 `CanExecute=false` → **`DataObject.Pasting` 事件根本不触发** →
  挂在输入框上的 `OnPasting` 收不到文件。当时只给**图片**做了 `PreviewKeyDown` 兜底，**漏了文件**。
- 修复：①新增 `ClipboardHelper.GetFileDropList()`（try/catch，剪贴板被占用静默）；
  ②`Input_KeyDown` 的 Ctrl+V 分支补文件兜底（优先级同 `OnPasting`：纯图片 → 文件）；
  ③handler 挂到**控件根** `PreviewKeyDown`（隧道，根先收到），焦点不在输入框时也能识别，
  用 `if (e.Handled) return;` 防同一按键被根与输入框各处理一次。

### 🔧 P2 补丁：复制文件后程序没反应（与 Web 体验对齐）

> 用户反馈「Ctrl+V 无法识别文件，跟 Web 的体验不一样」。上一轮只补了**按键兜底**，没找对根因——
> **Web 版根本不需要按 Ctrl+V**：它在弹窗打开时就把剪贴板的**文本 / 图片 / 文件**一并自动填好。
> exe 侧有**两个断点都只认文本和图片、都漏了文件**（图片当初补过，文件没人管）：

- **断点① `MainWindow.TryAutoPrompt`**：`if (text.Length == 0 && !IsImageOnlyClipboard()) return;`
  → 资源管理器复制文件（无文本无图、只有 FileDrop）**直接 return，压根不弹窗**，用户感觉"程序没反应"。
  现已加 `&& ClipboardHelper.GetFileDropList() == null` 放行文件。
- **断点② `PasteDialog` 自动填入**：只读 `Clipboard.GetText()` 和纯图片 → 就算手动点「＋」开弹窗，
  复制的文件也不会被填进去，是个空窗。现已补文件分支（优先级同 Web：文本 > 图片 > 文件）。
- 附加：弹窗 `Loaded` 时主动聚焦输入框——WPF 键盘事件沿焦点树路由，焦点不在弹窗内时
  上一轮加的根级 `PreviewKeyDown` 收不到，按键兜底形同虚设。
- 多文件：对齐 Web（只取第一个，`e.clipboardData.files[0]`），并 toast 提示"剪贴板有 N 个文件，已接收第一个"。

**现在的链路（与 Web 一致）**：资源管理器复制文件 → 监听自动弹窗 → **直接显示文件 chip** → 点存入即可，全程无需按键。

**验证**：`dotnet build` **0 错误**；`--selftest` **243 断言 ALL PASS**；发布版 exe 同样 243 ALL PASS。
**属 GUI 交互层，自动化不覆盖，需真机确认**（复制文件 → 应自动弹窗并显示文件 chip）。

### 🔧 P3 补丁（发版前代码审核）：exe 端存量配置迁移缺失
**这是本版最该被拦下的一处**——发版前全量审核才发现，功能测试与 243 条自检都覆盖不到它。

- **漏了什么**：v0.7.6 给 `SyncConfig.SyncFiles` 加了属性默认值 `= true`，并给 **Web 端**配了一次性迁移（存量 `syncFiles:false` 翻 true）。但 **exe 端只改了属性默认值，没做迁移**。
- **为什么致命**：`= true` 只对「JSON 里没有该字段」或新建对象生效。**已落盘的 `data/webdav.json` 里显式写的 `"syncFiles": false` 会在反序列化时覆盖它** → exe 端依旧不上传实体 → 云端没有实体 → 他端同步后照样下载失败。**等于本次核心修复在 exe 端完全没生效**，且是静默失效：UI 上复选框受属性默认值影响可能显示勾选，实际读的是落盘的 false。
- **修法**：`WebDavSync.LoadConfig` 内加 `MigrateSyncFilesOnce()`（对齐 Web 端 `migrateSyncFilesDefaults`），用 `v0706SyncFiles` 标记防重复——迁移后用户再主动关闭会被尊重，不会被反复翻开。
- **顺带统一**：Web 端迁移标记字段名 `v0619SyncFiles` 是笔误（本版是 v0.7.6，不是 v0.6.19），改 `v0706SyncFiles`，与 exe 端一致。
- **防回归**：新增 4 条自检断言（首次读取翻 true / 写入标记 / 迁移后用户主动关闭不被再翻开 / 迁移不破坏 Url·User·Pass）。

**验证**：`dotnet build` **0 错误**；`--selftest` **247 断言 ALL PASS**（+4）。

### 📋 发版前审核：已识别但**不在本版修**（避免发版前扩大改动面）
1. **单文件实体失败会中断整次同步**（两端一致都是 `throw`）。
   本次把 `syncFiles` 默认开启后，上传分支首次大规模执行——任何一个坏文件（未知扩展名/超限/权限）都会让**整次同步挂掉**，且此时条目已合并写本地、但快照上传被跳过 → 他端同步不到。
   建议：改成「单文件失败只记警告、不中断」，最后汇总「同步完成，但 N 个附件失败」。属行为变更，需两端一致改 + 补测试，留到下版。
2. **exe 端 `PendingNameMigrations` 存了但从不消费**（`SyncConfig` 有字段，没有任何代码读它）。
   Web 端用它做「改账号名后从旧名目录拉回实体」的自愈迁移；exe 端缺这段回退 → 改过账号名时旧实体可能永久拉不回。属历史遗留缺口，非本版引入。

### 🔧 顺带修好两个「一直是死的」测试脚本（与本问题无关的历史债）
- `scripts/test-auto-sync.mjs` ①找用户只认中文名「WebDAV测试」，而 `test-webdav-sync.mjs` 建的是「WebDAVTest」→ 恒报「未找到测试用户」；现兼容两者并支持 `TEST_USER` 覆盖，失败时打印现有用户名。②远端快照路径仍拼 `userId`（v0.6.13 起已按**账号名**寻址）→ 恒 404 导致 `.json()` 崩溃；改取 `accountName`。修复后 **3/3 通过**（此前 `npm test` 这条从未真跑通过）。

## v0.7.5 (2026-09-08) — 开机自启 + 开机静默托盘 + 图标去「方角」改超椭圆 squircle

> 本版是 exe 桌面版的两个增强：①顶栏加 **⚡ 开机自启**开关（写注册表 Run，开机 `/startup` 静默驻留托盘、不弹主窗），方案对齐 WindowTinter proto-shadow（README 明写"勾选开机自启后开机仅驻留托盘、不弹主窗"）；②修复图标「方角」问题——源图是整张方形不透明画布，旧脚本纯缩放导致开始菜单/任务栏图标四角直边粉色，改为**超椭圆 alpha 裁剪**（同 WindowTinter `DEV.md §17 超椭圆图标`：n=4 大尺寸 squircle / n=8 小尺寸近圆）。bat/platform 形态代码未变，不重打 zip（仅 exe zip 重打）。

### 🚀 开机自启 + 开机静默托盘
- `Services/AutoStart.cs`（新）：写/删 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 的 `ClipboardTool="<exe路径>" /startup`。`Enable/Disable` 幂等；`IsEnabled` 读注册表实际态（非 settings.json），保证按钮态与系统真实一致。
- `Settings.cs` + `LaunchAtStartup`；`MainWindow.xaml` 顶栏加 **⚡ 开机自启** 幽灵按钮（on=金色 0xD4AF37 / off=灰 0x848484）。
- `MainWindow.AutoStartOps.cs`（新 partial）：按钮点击 toggle `AutoStart.Enable/Disable` + 写 Settings，刷新按钮态，Toast 反馈。
- `MainWindow` 加 `startHidden` 参数 → 静默模式下 `Minimized + ShowInTaskbar=false` 创建窗口（保留句柄，ClipboardWatcher 才能在 `OnSourceInitialized` 挂上）；托盘双击唤出 `ShowMainFromTray` 恢复 `ShowInTaskbar=true`。
- `App.xaml.cs`：识别 `/startup`（及 `/silent` `/minimized` `/background` `/tray`）参数 → `startSilent`；**`ShutdownMode` 改 `OnExplicitShutdown`**（关键坑：WPF 默认 `OnLastWindowClose` 会在无可见主窗的静默模式下误退进程，托盘"退出"成为唯一退出路径）。

### 🎨 图标去方角 — 超椭圆 squircle（对齐 WindowTinter DEV.md §17）
- 根因：源图 `D:\download\生成液态玻璃风格icon.png` 是 RGB 2048×2048 整张不透明方形画布，粉色背景直填四角；旧 `_make_icon.py` 纯 LANCZOS 缩放 → ICO 各帧四角直边粉色（用户原话"图标是方的"）。
- 方案（同 WindowTinter proto-shadow `DEV.md §17` Python+Pillow 思路）：
  - 每帧缩放后**按目标像素网格**生成超椭圆 alpha 蒙版 `|x/(1-inset)|^n + |y/(1-inset)|^n ≤ 1`
  - 大尺寸 48/64/128/256 用 **n=4**（squircle，Win11 现代应用图标同款轮廓）
  - 小尺寸 16/24/32 用 **n=8**（近圆——小尺寸 squircle 的圆角只有 1px 视觉退化，n=8 让小图标仍显圆滑）
  - inset 4% 留边距；蒙版 1px 3×3 模糊抗锯齿
- `Assets/_make_icon.py` 重写（RGB→RGBA）；`Assets/app.ico` 重生成（105KB→120KB，7 帧全 RGBA）。校验：每帧 mask 与 alpha **0 内部漏空 / 0 外部漏色**。
- `ClipboardExe.csproj` ApplicationIcon 上方注释更新描述新算法。

### 🧪 selftest
- `--selftest` 在发布 exe 上跑通 **ALL PASS**（238 项断言零回归，含拼音/URL 清理/去重/归档/同步/墓碑/M5 配置等）。注：首次偶发 round-trip 3 FAIL 系 selftest 临时目录残留（`%TEMP%\clipboard-selftest-{pid}`）所致，复跑稳定 ALL PASS，非代码回归。

### 📝 文档基线
- `docs/发布规范.md` 发布记录表追加 v0.7.5 行。

## v0.7.4 (2026-09-08) — 应用图标接入液态玻璃风格「COPY」剪贴板 + 托盘图标同步换皮 + exe 合并为单文件

> 本版是 exe 桌面版的**视觉层与打包形态**变更：把项目长期占位的 `SystemIcons.Application` 托盘/开始菜单图标换成真实的液态玻璃风格「COPY」剪贴板（7 帧 ico），并把发布形式从 5 文件 framework-dependent 改为 **PublishSingleFile 单文件 framework-dependent**。bat/platform 形态代码未变，不重打 zip。

### 🎨 应用图标接入（Assets/app.ico，7 帧）
- 源：`D:\download\生成液态玻璃风格icon.png`（2048×2048，粉色 COPY 剪贴板），通过 `Assets/_make_icon.py`（Pillow）生成 16/24/32/48/64/128/256 共 7 帧 ico，PNG-in-ICO 编码（256 帧为 PNG 流）。
- `ClipboardExe.csproj` 新增 `<ApplicationIcon>Assets\app.ico</ApplicationIcon>` → 图标编进 exe Win32 资源（开始菜单/任务栏图标）。
- 同步 `<Resource Include="Assets\app.ico">` 把它以 WPF Resource 嵌入，运行时通过 `pack://application:,,,/Assets/app.ico` 加载给托盘 NotifyIcon（与 Win32 图标共用同一份二进制，零运行时复制成本）。
- `_make_icon.py` 与 `Assets/app_sizes.png`（检视图）`.gitignore` 走 csproj 的 `<None Remove>` 排除，不进构建产物。
- `bin/Release/net9.0-windows/win-x64/Clipboard.dll` 字节串包含 "0.7.4"（AssemblyInformationalVersion），PE 资源目录可解析出 7 帧 ICONDIR。

### 🖼 托盘图标同步换皮
- `Services/TrayIconService.cs` 构造时 `Icon = LoadAppIcon()`，从 WPF pack 资源加载与 exe 同款的 .ico。Windows 托盘 NotifyIcon 自动按 16×16/32×32 挑最匹配的帧渲染，dpi ≥ 100% 时再放大，锐利不糊。
- `LoadAppIcon()` 兜底：资源不可用时返回 `SystemIcons.Application`，托盘不空白（任务栏图标另由 `<ApplicationIcon>` 保证）。
- 解决：v0.7.3 之前用占位 `SystemIcons.Application`，托盘右键菜单虽已暗色（`TrayDarkRenderer`），但图标是 Windows 默认通用图标，与应用品牌脱节。

### 📦 exe 合并为单文件（PublishSingleFile=true，framework-dependent）
- 发布命令：`dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=embedded`
- 产物：`Clipboard.exe` 唯一文件（805 KB；.NET 9 DLL/JSON 全内嵌 + 自解压到 `%LOCALAPPDATA%\Temp\.net\Clipboard\<hash>\`），pdb 通过 `DebugType=embedded` 嵌进 exe（崩溃栈可直接定位，无额外文件）。
- **运行要求不变**：仍需装 .NET 9 Desktop Runtime x64（不是 self-contained，runtime 不内嵌以控制体积）。 `.NET 9 SDK` 不够，发布说明明确要求 Desktop Runtime。
- `App.OnStartup` 的 `RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly` 强制软件渲染保留（PublishSingleFile 下 DropShadowEffect 视觉树会静默崩溃 → 白屏；剪贴板工具对渲染性能无要求）。
- 安装目录约定从「5 文件同目录」改为「1 个 exe」即可——`Clipboard.dll` / `Clipboard.deps.json` / `Clipboard.runtimeconfig.json` 不再产出。`data/` 子目录（含 clips.json / archive.json / files/）保留原路径不变，便于跨版本迁移。

### 🧪 selftest
- `--selftest` 在新发布的单文件 exe 上跑通，**ALL PASS**（含拼音字库/URL 清理/去重/排序/归档/同步/墓碑/M5 配置等共 238 项断言），与 v0.7.3 同基线。

### 📝 文档基线
- `docs/发布规范.md` 更新 exe 形态描述：「exe framework-dependent 单文件 PublishSingleFile，唯一产物 `Clipboard.exe`，运行要求 .NET 9 Desktop Runtime x64」。发布 zip 内仅 exe + 使用说明。
- `docs/发布规范.md` v0.7.x 发布记录新增 v0.7.4。
- AGENTS.md 基线更新至 2026-09-08 · commit <v0.7.4 commit>。
- `使用说明.txt` 同步：写明图标 + 单文件形态。

> 详细开发记录见下方「开发线」小节。bat/platform 形态未触，仍以 v0.7.3 同号 zip（已发）随本期重新确认；本版仅重新发布 exe 单文件形态（zip 内 exe 替换为 v0.7.4 单文件版）。

## v0.7.3 (2026-09-08) — exe 图片 hover 预览彻底重写（无重复/无白边/等比/可复制）+ 编辑/存入交互闭环 + 同步并发锁硬化

> 本版是 exe 桌面版一次较集中的**图片预览浮层重写 + 编辑/存入交互闭环**发布，并顺带按走查结论硬化同步并发锁。bat/平台形态代码未变（自 v0.7.1 无改动），仅随本版重打 zip 保持三形态同版。

### 🖼 exe 图片 hover 预览彻底重写（对齐 Web `.img-hover-preview`，方案A）
用户连续多版反馈「标题重复 N 遍 + 左右白边」。根因：旧版多 TextBlock 叠层/ScrollViewer Stretch 导致并排重复与白边；且 WPF Popup 只在 `IsOpen` 时自动测量一次，缩放改内部内容不重测视窗 → 缩放后尺寸不更新。本版**整段重写**：
- **单 TextBlock cap**：全链只 1 个 TextBlock，从结构上杜绝「重复 N 遍」；cap 改为**黑字 + 白色字形细描边**（`DropShadowEffect{White, BlurRadius=1, ShadowDepth=0}`——WPF 无 `TextBlock.Stroke` 的单元素最稳等价，不叠层），去掉半透黑圆角矩形底
- **box 显式宽高 = 图等比显示尺寸**（`origW×scale × origH×scale`），不再依赖 Popup 自动测量 → 缩放必然同步、无白边无裁切；图片 `Stretch=Uniform` 填满 box（box 比例=原图 → 完整）
- **垂直锚定边开时定一次不翻转**：浮层上方/下方定一次，缩放只在固定一侧屏内钳制 → 缩放**不上下跳动**
- **点击预览图即复制**：浮层 `MouseLeftButtonUp` → 复制该图片到系统剪贴板并关浮层（Popup 独立窗格不透传卡片）
- 卡片图区滚轮缩放 + 浮层自身滚轮缩放均可用（50%~300% 步进 15%），浮层/卡片间迁移 220ms 延迟关闭防闪关

### 🖋 exe 编辑/存入交互闭环
- **重复编辑窗「＋ 新建」直达干净存入窗**：不再被迫改那条已有内容，可一键开空白存入窗手动录入（`skipAutoFill` 跳过剪贴板 autoFill，避免被判定重复的文本又填回 → 死循环）
- **存入窗「＋ 新建」按钮**：一键清掉自动识别的剪贴板文本/已选文件/图片，回到空白手动输入态
- **富文本卡「清除格式」**：对齐 Web v0.6.13，格式文本卡可一键标记清除（二次确认防误），保存后 html 置空转纯文本；富文本卡编辑正文 → `TextToHtml` 重建 html（此前 exe 漏传 html 致正文/预览不一致，契约对齐 Web）
- **确认框改弹窗内 Overlay 叠层**（不新建 Window）：修复「点清除格式/归档后编辑窗被顶掉、无法保存」的 bug——Confirm 卡片叠在当前弹窗顶层 Grid，暗色遮罩拦截点击/拖动，主弹窗全程存活

### 🔒 exe 同步并发锁硬化（走查 W-02）
`SyncEngine.InFlight`（static HashSet）的 Add/Remove **加锁**——手动同步 + 定时 autoSync 都经 `SyncController` 的 `Task.Run` 走线程池并发触发，裸 HashSet 非线程安全理论可双同步/异常。加对象锁（`InFlightLock`）细粒度保证互斥，`--selftest` 仍 ALL PASS。

> 详细开发记录见下方「开发线」小节。安装与运行要求同 v0.7.2（exe 需 .NET 9 Desktop Runtime x64）。发布 zip：exe(DEFLATE) + bat + platform 三形态。

## v0.7.2 (2026-09-07) — 修复 exe 单卡删除后同步复活（墓碑缺口）+ 墓碑 TTL 对齐 90 天

> 用户实测：exe 删除某张卡片 → 点同步 → 卡片又回来了。根因：exe 的**单卡删除** `ClipService.Delete` 与 `BatchDelete` 语义不一致——只从活跃区移除、**不记墓碑**；而服务端(Web/bat/platform)单删是 `deleteClip`/`deleteArchivedClip` → `recordTombstoneIfConfigured`。同步时本地无该墓碑裁决，远端上次同步留下的旧副本被 `mergeSnapshots` 按 updatedAt 并入 → 复活。已修复并使 exe 与 Web 双版本规则一致（AGENTS「还原铁律」准入门槛）。

### 🐛 exe 修复
- **单卡删除补墓碑**（`ClipService.Delete`）：改为跨活跃区+归档区删除（归档卡 ✕ 此前静默无效，现真正删除）并**无条件记墓碑**（`deletedAt=now`），与 `BatchDelete` 同语义；返回被删文件 id 供 `MainWindow` 联动清理文件实体（对齐 Web 路由层 `deleteFile`）。归档卡单删、文件卡单删的实体清理也一并正确。
- **墓碑 TTL 30 天 → 90 天**（`SyncEngine.TombstoneExpireMs`）：对齐 Web `lib/core/tombstones.js TOMB_TTL_MS=90d` 与 `docs` 记录——TTL 过短会在墓碑先于远端旧副本被清理时二次复活。
- 自检新增 6 断言：单删记墓碑 / 删除不存在抛错 / **M5 ⑨ 删后同步不复活回归用例**（本地墓碑 + 远端旧副本 → 墓碑裁决删除）等，`--selftest` **ALL PASS**（含既有 M5 ①②③④⑤⑥⑦⑧）。

### ✅ bat / 平台版核查结论（无需改动）
- bat(本地服务版 start-server.cmd) 与 平台版共用根 `lib/core` + `server.mjs` 一处代码，单删已正确：路由 `DELETE /api/clips/:id` → `deleteClip`(404 回退 `deleteArchivedClip`) → `recordTombstoneIfConfigured`（已配置 WebDAV 才记，P-102 语义）；批量删同。墓碑 TTL 已 90 天。**该 bug 在 bat/平台侧不存在**，仅 exe 单删是缺口。

> 详细开发记录见下方「开发线」小节。安装与运行要求同 v0.7.1（exe 需 .NET 9 Desktop Runtime x64；bat/platform 双击 start-server.cmd 或平台托管）。

## v0.7.1 (2026-09-05) — exe 桌面版 + 本地服务版(bat) + 平台版 三形态统一发布（账号昵称随同步传播）

> 本版是 exe 线首次带完整 WebDAV 同步生态的正式版本，并同步三端。账号「昵称」成为可同步、跨端一致的第一等概念。

### ✨ 账号昵称随快照同步（三端统一）
- **exe 桌面版**：快照顶层新增可选 `nickname` 字段承载昵称；数据管理新增「账号昵称」编辑（仅已绑定账号可用）；顶栏状态点按 **昵称 → 账号名 → 「本地」** 优先级显示。本地权威、上传带昵称、首次换机采纳远端。
- **本地服务版(bat) / 平台版**（共用 server.mjs 一处代码）：`users.js` 新增 `getUserDisplayNameEx` / `adoptRemoteNickname`；`webdav.js runSync` 上传快照带该用户昵称、拉取时本地未自定义昵称则采纳远端（`displayName ≠ accountName` 才算自定义，规避 createUser 默认写死 displayName 的坑）。与 exe 语义双向收敛。
- 三者连同一块 WebDAV 盘、同一账号时，任一端改昵称 → 同步 → 他端采纳一致。

### 🎨 exe 桌面体验
- **顶栏折叠**：折叠时整条顶栏 Collapsed 收起（仅留工具栏展开按钮 ⌄），不再占细条空间
- **托盘右键菜单**：WinForms ContextMenuStrip 固定暗色（`Services/TrayDarkMenu.cs`，ToolStripProfessionalRenderer，对齐 Win11 深色观感）
- **标签栏溢出**：TagBar 包进横向 ScrollViewer（滚轮/触控平移），不再与右下角操作按钮互相遮挡

### 🔄 exe WebDAV 同步体验
- **首次同步选远端账号**：本地无绑定账号时点击同步 → 弹窗枚举远端 `clipboard-<账号名>.json`（PROPFIND depth:1）→ 单选拉回，免登录式手填
- **同步后顶栏显示远端账号身份**（绑定后不再停留「本地」）
- 批量删除确认提示「N 条被过滤隐藏仍会删除」；批量打标弹窗加橙色警告

### 🐛 exe 修复
- ToolTip 全局 CornerRadius 由 RadiusPill(短文案呈叶子形) 改为固定 6 + ClipToBounds
- 走查批：折叠退出按钮定位 / 移除冗余 x:Name / 批量隐藏选中提示

> 详细开发记录见下方各「开发线」小节。安装与运行要求同 v0.7.0（exe 需 .NET 9 Desktop Runtime x64；bat/platform 双击 start-server.cmd 或平台托管）。

## Web 服务端(bat 本地服务版 / 平台版共用 server.mjs)：同步带昵称适配 (2026-09-04, 开发线, 未发版)

### ✨ 快照 `nickname` 字段双向传播（对齐 exe 端昵称语义）
- **背景**：exe 端已把昵称存进快照 `nickname` + 本地记忆。bat 版/平台版跑同一套 `server.mjs`（users.js/webdav.js），用户有 displayName(昵称)，但同步上传快照时**未带昵称** → 与 exe 连同一盘时昵称无法互通。本次补齐两端一致。
- ✅ **改动**（一处代码，bat+平台两版同时生效）：
  - `lib/core/users.js`：加 `getUserDisplayNameEx(userId)`（返回 `{hasExplicit, displayName}`）与 `adoptRemoteNickname(userId, nick)`。
  - **hasExplicit 判定 = displayName ≠ accountName**（不是看字段是否存在！createUser 默认把 displayName=账号名写入，故"有键"≠"自定义"；旧数据仅 name 的 fallback 也满足相等=未自定义）。
  - `adoptRemoteNickname`：仅当 displayName==accountName（未自定义昵称）时采纳远端 nickname 写入；撞其它用户同名则放弃不抛错。
  - `lib/core/webdav.js runSync`：上传快照 `...(hasExplicit ? { nickname: displayName } : {})`（未自定义不带字段）；拉取后若未自定义则采纳远端 nickname（当前名快照优先），采纳成功后刷新 hasExplicit 使本轮上传即带。
- **与 exe 语义对齐**：本地权威（已自定义昵称 → 不被远端覆盖）→ 上传带自己的昵称；未自定义 → 采纳远端 → 上传补带。两端双向收敛。
- 验证：`--input-type=module` 集成脚本 5/5（无昵称上传无 nickname / 带昵称上传有 / 无昵称采纳到 users / 采纳后上传补带 / 已自定义不被覆盖）；既有 `scripts/test-webdav-sync.mjs` **19 通过 0 失败**（零回归）。

## exe 版：账号昵称（数据管理可改 · 随快照同步 · 顶栏显示昵称） (2026-09-04, 开发线, 未发版)

### ✨ 昵称承载：快照顶层 `nickname` 字段（纯 WebDAV 盘无用户库的现实方案）
- **背景**：用户要顶栏显示「昵称」而非「账号名」。确认 exe 连的 192.168.2.1:6086 是**纯 WebDAV 盘**（无本项目用户库/API），昵称无处可读 → 只能作为一份数据存在盘上。方案定为**快照顶层可选 `nickname` 字段**：昵称 = 该账号快照的一个附加字段 + 本地 SyncConfig 记忆，本地权威、上传写远端、首次换机采纳远端。
- ✅ **改动**：
  - `Models/SyncConfig.cs`：`Snapshot` 加 `Nickname`；`SyncConfig` 加 `DisplayName`（均向后兼容，旧数据无此字段）。
  - `Services/SyncEngine.cs`：①b 拉取后「本地未设昵称但远端带 nickname → 采纳写 cfg + SaveConfig」；上传快照时 `Nickname = cfg.DisplayName`。
  - `Services/WebDavSync.cs` `ValidateAndBuild`：透传 `DisplayName`（old?.DisplayName）。
  - `Controls/DataDialog.xaml(.cs)`：WebDAV 区加「账号昵称」输入框——仅已绑定账号可用（无账号禁用并提示先绑定）；保存配置/立即同步时把框值写入 displayName（清空 = 清掉昵称显示回账号名）。
  - `MainWindow.xaml.cs` `RefreshAccountBadge`：显示优先级 = 昵称(DisplayName) → 账号名(AccountName) → 「本地」。
- **多端一致性**：同账号任一 exe 改昵称 → 同步上传 nickname → 其它用同账号的设备拉取时（本地无昵称）采纳 → 显示一致。本地已设昵称则保留本地（避免远端旧值覆盖新改）。
- 构建 0 错误；`--selftest` ALL PASS。产物 23:28:24。真机验证：数据管理设昵称 → 同步 → 顶栏显示昵称；换机拉取看是否采纳。

## exe 版：同步后顶栏显示远端账号昵称 (2026-09-04, 开发线, 未发版)

### ✨ 顶栏状态点：纯本地 =「本地」/ 已绑定 =「账号昵称」
- **背景**：顶栏右侧状态点写死「● 本地」，同步绑定远端账号后仍显示「本地」，与真实云端身份不符。用户期望同步成功后就显示该远端账号昵称。
- ✅ **改动**：
  - `MainWindow.xaml`：状态点 `<TextBlock Text="本地">` 加 `x:Name="AccountBadge"`。
  - `Services/SyncController.cs`：`Config` 改私有 setter + backing field，配置变化(首次绑定/切账号/同步后重载)统一触发 `public event Action? ConfigChanged`（服务层不碰 UI，仅发通知）。
  - `MainWindow.xaml.cs`：ctor 订阅 `_sync.ConfigChanged += RefreshAccountBadge` + 启动调一次；新增 `RefreshAccountBadge()`——`Config==null` 或无账号名 → 徽章「本地」(ToolTip「本地数据(未绑定远端账号)」)；已绑定 → 徽章显示 `AccountName`(ToolTip「已同步账号「xx」· url」)。
- **覆盖路径**：启动(若已绑定即显示昵称) / 工具栏同步成功 / SyncPickerDialog 首次选号成功 / 自动同步成功 / DataDialog 保存配置 —— 均经 `ConfigChanged` 收敛到同一刷新点。
- 构建 0 错误；`--selftest` ALL PASS(清残留临时目录后，偶发归档目录污染非代码问题)。产物 22:49:07。真机验证需先同步绑定账号看顶栏文字变化。

## exe 版：标签栏溢出防遮挡 + 首次同步选远端账号 (2026-09-04, 开发线, 未发版)

### 🐛 多标签时右下角操作组被遮挡
- **问题**：工具条行2 Grid `[类型tab | 标签栏 | 操作组]`，中间标签栏是横向 StackPanel 不裁剪，标签一多溢出绘制盖到最右操作组(置顶/编辑/归档/列数/同步)上。
- ✅ **改动**（`MainWindow.xaml`）：标签栏外层包 `ScrollViewer`(`TagBarScroller`, Grid.Column=1, `HorizontalScrollBarVisibility=Auto`, 纵向 Disabled, `PanningMode=HorizontalOnly`)——标签少照常一行、标签多自行收缩横向滚动，操作组固定不再被盖。
- `MainWindow.TagBarOps.cs`：新增 `TagBarScroller_MouseWheel`(鼠标滚轮转横向滚动，无溢出忽略) + `using System.Windows.Input`。
- 复用项目既有 6px 细 ScrollBar 样式(溢出才出现)。构建 0 错误；selftest EXIT 0。真机造 15+ 标签验证。

### ✨ 同步：本地无账号时「枚举远端账号 → 选一个拉回」（免登录式手填账号）
- **背景**：本地仍为单账号；WebDAV 服务器 `workbuddy/剪贴板/` 下可共存多份 `clipboard-<账号名>.json`。原工具条「同步」在未配置时只提示去「数据管理」手填(像登录页)。用户希望点同步即可从远端已有账号里挑。
- ✅ **改动**：
  - `Services/WebDavClient.cs`：新增 `ListRemoteAccountNames(cfg)` —— `PROPFIND depth:1` 列目录，解析 `clipboard-<名>.json` 得账号名(去重保序)；服务器不支持 PROPFIND(405/501) 抛语义错误提示。辅助 `ParseClipboardHrefs`。
  - 新增 `Controls/SyncPickerDialog.xaml(.cs)`：首次同步弹窗——填/复用服务器凭据 →「连接并列出账号」(先 TestConnection 再枚举)→ 单选远端账号 →「同步所选账号」。所选账号名作为本机 `accountName` 持久化(`SaveConfig`+`RunNow` 内写盘)，后续同步不再询问。
  - `MainWindow.SyncOps.cs` `SyncBtn_Click`：本地无账号(`Config==null`)→ 打开 `SyncPickerDialog`；有账号 → 维持直接同步(现状不动)。
- **语义**：首次本地空 + 选账号 → `RunSync` 合并结果即远端数据(`hadLocal=false` 不上传) =「拉回覆盖成该账号数据」；二次以后本地已绑定该账号走正常双向。本轮仅明文快照，加密账号未处理(用户已拍板后续)。
- 构建 0 错误；`--selftest` EXIT 0。真机验证需真实 WebDAV 服务器(PROPFIND 支持)。

## exe 版托盘右键菜单固定暗色 (2026-09-04, 开发线, 未发版)

### 🎨 托盘右键菜单固定暗色（对齐主窗口暗色 UI / Win11 深色观感）

- **问题**：托盘右键菜单用 WinForms `NotifyIcon.ContextMenuStrip`，其默认 Professional 渲染是浅色白底黑字，与主窗口固定暗色(#1A1A1A)界面割裂，深色观感差。
- ✅ **改动**：
  - 新增 `Services/TrayDarkMenu.cs`：`TrayDarkColorTable`(继承 `ProfessionalColorTable`) 对齐主窗暗色令牌(底 `Elev #1F1F1F` / 文字 `#DADADA` / 边框 `#3D3D3D` / hover 金 16% `#33C9A96E`)；`TrayDarkRenderer`(继承 `ToolStripProfessionalRenderer`) 兜底文字前景色(hover=白、禁用=Dim `#6E6E6E`) + 去默认浅描边改暗边框。
  - `Services/TrayIconService.cs`：`ContextMenuStrip` 挂 `Renderer = new TrayDarkRenderer()`、`ShowImageMargin=false`(去掉左侧浅色空渐变列)、Segoe UI 9pt。
- **决策**：用户拍板**固定暗色**(不随系统切)——与主窗口固定暗色 UI 一致。采用自定义 Renderer(非实验性 `SetColorMode` WFO5001、不影响 WPF 主程序)。
- 构建 0 错误；`--selftest` EXIT 0；纯托盘菜单渲染层，不动业务。托盘菜单须真机右键验证视觉效果。

## exe 版走查问题修复 (2026-09-04, 开发线, 未发版)

### 🔧 走查问题清零（scenario-walkthrough v3 两轮 + 修复闭环）

- **P-E2**：移除 `TopBar` 内 `TopBrand`/`TopRightOps` 两个不再被代码独立控制的冗余 `x:Name`（折叠态已改由父 `TopBar` Collapsed 统一控制），纯代码卫生，无行为变化。
- **P-E3**：批量操作"看不见的勾选"提示（勾选集按 id 跨过滤持久，S-13 对齐 Web "跨过滤保持"）：
  - 批量**删除**确认：当勾选含被过滤隐藏项时，文案追加 `\n⚠ 其中 M 条当前被搜索/标签过滤隐藏，仍会被删除`。
  - 批量**标签**弹窗：打开时若有隐藏勾选，在标签选择区上方插一条橙色警告行 `⚠ N 条已选中，其中 M 条…仍会被操作`。
  - 新增 `HiddenSelectionCount()`（BatchOps.cs）：统计勾选集中不在当前 `_visibleIds` 的条数。
- **P-E1**（拍板）：折叠态"退出/数据管理"藏二级展开入口 → 用户确认**保持现状**（单机托盘工具以托盘退出为主，数据管理低频可展开）。
- 构建 0 错误；`--selftest` EXIT 0 无回归；纯 UI + 提示文案层，不动批量/数据业务规则。

## exe 版顶栏折叠优化 (2026-09-04, 开发线, 未发版)

### ✨ 顶栏整体可折叠 + 置顶下沉（彻底节省卡片墙纵向空间）

- **问题**：顶栏（📋剪贴板 + ●本地 + ◐/密码占位 + 数据管理 + 退出）常显占一行，窗口本就矮（MinHeight 480），挤占卡片墙可视区。首版"收成细条"后用户仍觉残留背景卡占空间 → 改为折叠时整条彻底消失。
- ✅ **改动**（`MainWindow.xaml` + `MainWindow.xaml.cs`）：
  - Row0 顶栏 `NeuBorder`(`TopBar`) **折叠时整条 `Collapsed`**（Row0 Auto 高度归 0，搜索/工具条整体上移，不留背景卡细条）；展开态恢复完整顶栏（标题 + 本地 + 数据管理 + 退出）。
  - **展开钮 `TopExpandBtn` 从顶栏移出 → 工具条(Row1)行1搜索框左侧**小图标钮（BtnGhostSm、Padding 6,4、Content `⌄`/`⌃` 按态切）；行1 Grid 2 列改 3 列（Auto钮 | *搜索 | Auto存入）。折叠后它是唯一入口，ToolTip 说明。
  - **置顶钮 `PinBtn` 下沉到工具条(Row1)行2操作组最左**（编辑前，`Margin 0,0,8,0`）——顶栏折叠后置顶仍需**常驻可点**。
  - 折叠态默认**收起**（`_headerExpanded=false`，非持久化、重启回默认收起）。
- **验收**：默认启动顶栏整体消失，仅搜索框左侧一个极小"⌄"图标钮；点"⌄"恢复完整顶栏（含数据管理/退出），点"⌃"再收起；📌 置顶在工具条行2常驻可点。
- 构建 0 错误；`--selftest` 数据层 223 断言全绿零回归；纯 UI 布局层不动业务规则。

## exe 版交互修复批 (2026-09-04, 开发线, 未发版)

### 🐛 修复①：已有实例（尤其收进托盘/最小化）后再双击 exe 完全无反应

- **问题**：程序已在跑，再双击启动 exe 无任何反应；尤其点 X / 最小化收进托盘（`Hide()`）后，主窗口不可见，双击毫无动静——用户误判"打不开/崩了"，且最小化后找不到窗口。
- 🔍 **根因**（`App.xaml.cs`）：单实例已有实例分支用 `Process.MainWindowHandle` + `ShowWindowAsync(SW_RESTORE)`。主窗口经 `OnClosing` 收进托盘后是 `Hide()`（非销毁）→ WPF 顶层不可见时 `MainWindowHandle` 返回 **0** → 循环里 `if (h == IntPtr.Zero) continue` → 什么都不做即静默退出。plan §7 原设计是"PostMessage WM_SHOW_MAIN 唤醒"，实现却退化成依赖可见主窗口句柄，对托盘态失效。
- ✅ **修复**：改用**命名 AutoReset `EventWaitHandle`**（`ClipboardTool_Wake_7e2c1a9f`）+ 第一实例后台线程 `while(WaitOne())` → `Dispatcher.BeginInvoke` 调 `MainWindow.WakeMainFromSecondInstance()`（`Show + Normal + Activate + SetForegroundWindow`，AttachThreadInput 绕过前台锁）。第二实例 `!createdNew` → `SignalExistingInstance()`（`OpenExisting().Set()`）后退出。不依赖窗口句柄 → 托盘态/最小化态都可靠唤醒；AutoReset+循环支持**多次双击多次唤起**。清理：`OnExit` 中断线程 + `Dispose` 事件。

### 🐛 修复②：主窗口激活时存卡窗"闪没" / 激活后完全不弹（含一轮回归修正）

- **问题**：主窗口在后台、剪贴板残留内容时点击主窗口激活 → 存卡窗飞快闪一下就消失，点不进存入/取消。
- 🔍 **根因**：`MainWindow.OnActivated` **同步** `PromptFromActivation` → `ModalHost.Show(存卡)`。点击非活动窗口激活时，Win32 先送 `WM_MOUSEACTIVATE`(→`OnActivated` 弹窗 Show 抢激活)、**随后才投递 `WM_LBUTTONDOWN`** 到主窗口(落点不在弹窗上) → 焦点抢回主窗口 → 弹窗 `Deactivated`(旧 `_armed` 只在 Loaded 置 true，防不住 Loaded 后的紧接回落) → 立即 `Close()` = 一闪没。
- ⚠️ **首轮错误修法（已废弃，勿复现）**：`OnActivated` 延迟 + "稳定期内收到主窗口 mouse/key/wheel 则放弃本次自动弹"。但 Win32 消息序决定**点击激活自带的 mouse-down 也必然取消** → 把最常用的"点击激活"一并杀死 → **激活后完全不弹**(用户实测回归)。
- ✅ **正确修复**（`MainWindow.xaml.cs` + `Controls/ModalHost.cs`）：
  - `MainWindow`：`OnActivated` → DispatcherTimer **延迟 200ms 再弹**，等点击的 down/up 排空、主窗口稳定激活后弹窗 Show 才能稳定持焦；**绝不因用户后续输入取消**。`ActivationTimer_Tick` 加 `IsActive && !ModalHost.IsOpen` 保护（此间切走则作罢）。
  - `ModalHost`：打开后 `GuardWindow(450ms)` 内的 `Deactivated` 不立即关——走 `_closePending` + 160ms 延迟确认，期间 `win.Activated` 即取消；稳定失焦才关。兜底残余焦点竞争。
- **三角验收目标**：点击主窗口激活 → 存卡窗**稳定出现可点**；Alt-Tab 切回 → 自动弹可点；点空白/其它程序 → 正常关弹窗。

### ✅ 验证
- Release 构建 **0 错误**（警告为既有 CA1416/CS8632 平台注解噪音，非新引入）；`--selftest` **ALL PASS**（数据层 223 断言零回归）；差分门禁 `npm run test:diff` **41 fixtures 全对拍一致**。改动均在 UI 交互/单实例层，不动业务规则，故数据层/差分测试全绿符合预期。
- ⚠️ 两处均为 GUI 行为，自动化测试不覆盖真实交互，**需用户真机验收**后再发布：①托盘态再双击 exe 应唤起主窗口；②后台态点主窗口激活不应再闪存卡窗，Alt-Tab 切回自动弹仍正常可点。

## v0.6.18 (2026-09-02) — 平台版 / 本地服务版（bat）发布

### 修复：手动 Ctrl+V 打开存入弹窗时内容未自动填入

- 🐛 **问题**：主页面空白处已登录按 Ctrl+V → 存入弹窗弹出但内容空白（用户反馈）。
- 🔍 **根因**：v0.6.17 的 Ctrl+V 由 keydown 拦截 + `e.preventDefault()` 后走 `navigator.clipboard.readText()`（Async Clipboard API）。该 API 在 **iframe / 平台挂载等未授权 `clipboard-read`**（Permissions-Policy）环境静默返回空；且失败提示仅复制自动弹窗路径（`auto=true`）显示，手动路径无任何提示 → "弹窗开、内容空"。
- ✅ **修复**（`public/app.js`）：Ctrl+V 改走 **document 级 `paste` 事件捕获**——`e.clipboardData` 在粘贴手势内**免权限**直接可用。守卫（已登录/无弹窗/焦点不在输入类/主页面）→ `preventDefault` → 同步提取 `text/html`/图片/文件 → `openPasteModal(false, {text, html, file})` 直接填入。
- 🧩 `openPasteModal(auto, pasteData=null)` / `autoFillPasteModal(..., pasteData)` 新增 pasteData 分支（文件优先→pick；文本→直接填 textarea 并 flash「已填入剪贴板内容」）；无 pasteData（复制自动弹/点按钮）仍走原 Async read 尽力路径，行为不变。
- ✍️ 输入框内 Ctrl+V 依旧保留原生粘贴（弹窗 textarea 内图片/html 由原 paste handler 处理）；空格定位搜索框逻辑不变。

## v0.7.0 (2026-09-01, exe 版开发中)

### exe 桌面版启动（C# WPF 原生重写，含置顶按钮）

- 🟢 **新增第四交付形态**：exe 桌面版（`clipboard-exe/`，C# WPF net9.0-windows 原生重写，非壳方案、非旧 WinForms 路线）。方案见 `docs/exe-wpf-plan.md`
- 🟢 **还原铁律**：规则类逻辑（sortClips / rollToArchive / isExpired / resolveExpire / cleanUrl / mergeSnapshots / 墓碑）与 Web 版逐字一致；**唯一新增功能 = 窗口置顶按钮**（Window.Topmost）；数据 JSON 与 Web 版互导
- 🟢 **发布形态**：框架依赖单文件（~1-3MB），.NET 9 Desktop Runtime 另行安装（用户确认小问题）；**不采用 NativeAOT**（WPF 数据绑定与裁剪兼容差，准确优先）
- ✅ **M1 骨架**（2026-08-31）：工程 + 设计令牌主题（Colors/Styles/Generic + NeuBorder 新拟态）+ 深色主窗体 + 托盘 + 单实例 + 置顶按钮 + 三档自适应（`LayoutRules.MaxWidthFor` 纯函数）+ 沉浸式深色标题栏（`WindowExtensions` 扩展）+ 软件渲染修复（单文件发布白屏）+ 文档契约 §5.4（数据层独立文件 / MainWindow 只编排 / Tag 不扩散 / 布局规则走纯函数）
- ✅ **M2 数据层**（2026-09-01）：`Models/ClipItem.cs`（17 字段 camelCase，对齐 publicClip 实际代码，修正原规划 16 字段与 `expiresAt` 笔误）+ `Services/Storage.cs`（原子写 tmp+rename / `SortClips` LINQ 稳定链 / `RollToArchive` 500 上限 + id 去重防膨胀 / `data/clips.json`+`archive.json` 双文件）+ `Services/Pinyin.cs`（GB2312 一级字库 3755 字 23 组逐字搬运）+ `Services/CleanUrl.cs`（24 追踪参数三态）+ `Services/ClipService.cs`（Create 类型判定/校验/自动标题、FindDuplicate link 比 url、Search 含拼音缩写、ResolveExpire、Sanitize）
- ✅ **`--selftest` 自检开关**（App.OnStartup 顶部，Mutex 之前，跑完退出不启动主窗）：**42 项断言全过**——排序对拍 / 拼音（总数 3755、23 组、已知字）/ CleanUrl 三态 / 去重 / 归档防膨胀（520→keep 500 + 二次保存不翻倍）/ round-trip 写读无损 + camelCase / ResolveExpire / SanitizeInput / IsExpired / Search
- 🟢 **M2 红线**：MainWindow.xaml/.cs 零改动（数据层全独立文件）；UI 外观不动；`Tag="on"` 不扩散
- 🟢 **M3b-2a 文件线**（2026-09-01，M3b-2 拆 2a/2b 后单轮交付）：`Services/FileStore.cs`（文件实体 saveFile/getPath/deleteFile 对齐 Web `lib/core/files.js` 语义：10MB 上限、MIME/扩展名黑名单、EXT_BY_MIME 扩展名映射、不安全扩展名回退 bin、前缀查找防穿越、Delete 静默、MimeFromPath 扩展名推断）+ `PasteDialog` 文件 chip（📎 fname·fsize ✕，对齐 `.file-chip`） + 📁 选择文件 + 粘贴/拖放拦截 + 10MB 拒收 + 文件图标卡（PDF 红边 / ZIP 金边 / FILE 中性 + 折叠角 + 内虚线框，对齐 `makeFileIcon` L1173）+ ↓ 下载（SaveFileDialog 原名落盘）+ 删除联动清文件实体（对齐 Web 路由层 `deleteFile`）。**图片粘贴 2a 暂拒收，toast 提示 2b 支持**（图片卡体/hover 预览延后 M3b-2b）。`--selftest` **126 断言全过**（99 + 27 增量：FileStore 存取/黑名单/10MB/扩展名映射/Delete 静默 + FileKindFor 6 态 + Create file 字段）；UIAutomation 冒烟：文件图标卡渲染 + 删除 → 文件实体联动清理
- 🟢 **M3b-2a-1 UI 圆角规范修正**（2026-09-01，M3b-2a 内的二次修正）：全面排查并替换 **12 处硬编码 `CornerRadius`**（Styles.xaml 6 处 + EditDialog/PasteDialog 图标块 3 处 + CardView 折叠角/PasteDialog 文件 chip/Toast 3 处），全部改用 `Colors.xaml` 圆角令牌；`Colors.xaml` 增 3 令牌 `RadiusFold`(3,3,0,0 不对称折叠角) / `RadiusIconLg`(11, 36px 图标块) / `RadiusIconSm`(6, 20px 序号块)；`Styles.xaml` 增全局 `ToolTip` 样式（消除系统默认矩形白底）。构建 **0 错误 0 警告**（既有 CA1416/Settings 已消）；`--selftest` **126 断言仍全过**（零回归）；UI 截图肉眼确认主界面 + 存入弹窗所有可见元素均为圆角矩形（搜索框/存入按钮/标签栏 chips/列数按钮/卡片体/卡片底部 ☆↓✎✕ 按钮/PDF 文件卡/折叠角/文本框/徽章/清空链接 等）；`grep CornerRadius="[0-9]"` 命中 0 处、`grep new CornerRadius(` 仅余 `PillBorder.cs:49`（控件内部封装动态半径，合规）。
- 🟢 **M3b-3 批量编辑条**（2026-09-01，拆 3a 数据层 / 3b UI 层）：
  - 数据层（M3b-3a）：`Models/Tombstone.cs` + `Storage` 增 `tombstones.json` 读写；`ClipService` 增 `BatchDelete`（跨活跃区+归档 / 联动 `FileStore.Delete` 清文件实体 / 合并墓碑）与 `BatchSetTags`（跨区加/减标签 / `SanitizeTags` 去重截长 / `MAX_TAGS=10` 上限 / 刷新 `updatedAt`），逐字对齐 Web `lib/core/clips-mutate.js`。
  - UI 层（M3b-3b）：`MainWindow` 启用「编辑」按钮 → 底部悬浮 `BatchBar`（已选 N / 全选当前页 / ＋加标签 / －减标签 / 🗑删除 / 完成）；卡片 `CardView` 批量模式左上角 `.sel-chk` 覆盖层 + 整卡单击切换选择 + 选中态金描边；全选当前页基于当前 `Search` 可见集；批量标签弹窗复用 `TagPicker`（add=全局标签含新建 / remove=已选条目标签并集）；批量删除确认 → `ClipService.BatchDelete` → 退出批量 → 刷新。
  - `--selftest` **159 断言全过**（144 + 12 条批量编辑业务断言增量）；构建 0 错误；UIAutomation 冒烟：进入批量模式 → 卡片显勾选框 → 全选当前页 → 8 条全部选中金描边 → 退出批量状态清除。
- ✅ **M4a 数据管理 · 导入导出 + 清空**（2026-09-01，M4 拆 M4a/M4b/M4c 后单轮交付）：`Models/Transfer.cs`（`ExportDoc`/`ImportResult`，`app:"clipboard"` 对齐 Web `clips-transfer.js` 真实导出头，保证 exe↔Web 零返工）+ `Storage` 增 `Serialize`/`Deserialize`（选项含 WriteIndented+CamelCase+无 BOM，与 Web 导出同构）+ `ClipService` 增 `BuildExport`（活跃区+归档合并）/ `ImportClips`（byId 仅活跃区、同 id 取 updatedAt 新者=updated/旧者=skipped/新增=added、非 UUID id 重生成、`MAX*2` 超量拒收）/ `ClearAll`（活跃+归档+墓碑一并清空、不传播删除、联动 `FileStore.Delete` 清文件实体，对齐 `clearAllClips`）/ `SanitizeImported`（类型/标题/内容截断/标签净化/URL 清洗，逐字对齐 `sanitizeImported`）+ `Controls/DataDialog.xaml`(+cs)（复用 `ModalHost.Show`/`Confirm` + `JsonDialog` 结构：导出全部（含归档）/ 导入合并… / 全部清空（含归档））+ 顶栏「数据管理」按钮启用（`DataBtn_Click` → `ModalHost.Show(new DataDialog(...))`）。`--selftest` **177 断言全过**（159 + M4a 增量 18：Export 含活跃+归档 / round-trip 一致 / Import updated·skipped·added·total 语义 / 非 UUID id 重生成 / 超 1000 拒收 / 非法备份拒收 / ClearAll 返回 N+三区全空）；构建 0 错误 0 警告；UIAutomation 冒烟：数据管理 → DataDialog 渲染 + 全部清空 → Confirm 弹窗渲染（截图生成、日志无异常、clickby invoked）
- ✅ **M4b 富文本分栏**（2026-09-02，M4 拆 M4a/M4b/M4c 后第二轮交付）：`Services/RichText.cs`（`BuildWordDoc` 字符串级移植 + CF_HTML 编码 `EncodeCfHtml` + `CopyRich` 双格式写入剪贴板 `Text`+`Html`）+ `CardView` 文本卡有 `html` 时渲染左右双栏（顶行「T 普通文本」/「✦ 富文本」，左栏复制纯文本、右栏复制带格式，分栏 click stopPropagation + 双击编辑守卫）+ `MainWindow` 事件接线（800ms 来源抑制 / `BumpCopyCount` / 鼠标位置 toast）。`--selftest` **183 断言全过**（177 + M4b 增量 6）；构建 0 错误；UI 截图肉眼确认 split 分栏正确渲染、纯文本卡仍单栏。
- ✅ **M4c JSON 覆盖保存**（2026-09-02，M4 拆 M4a/M4b/M4c 后第三轮交付）：`Controls/JsonDialog.xaml`(+cs) 由只读预览升级为「覆盖保存 / 复制美化结果 / 关闭」三按钮（`Save_Click` 校验 JSON 合法后 `SaveRequested?.Invoke(_clip, JsonBox.Text)` 回传**编辑后**文本）+ `MainWindow.OpenJsonRequested` 订阅 `SaveRequested`：带 `html` 且内容变更时 `RichText.TextToHtml(新content)` 同步重建 `html`，`ClipService.Update(id, c.Title, c.Tags, null, 新content, null, newHtml)` 写入（**标题/标签原样保留**）+ `Services/RichText.cs` 增 `TextToHtml`（转义 <>& / 双换行分段 / 单换行转 br / URL 转链接）。`--selftest` **196 断言全过**（183 + M4c 增量 13：TextToHtml 5 + 覆盖保存契约 8，含「标题原样保留 / 标签原样保留 / content 更新 / html 重建 / 无 html 条目不凭空生成 html 且标题标签仍保留」）；构建 0 错误 0 警告；因无头环境 ItemsControl 不生成卡片容器（仅静态 14 按钮可达），GUI 冒烟改为数据层契约自检覆盖。
- ✅ **M5a WebDAV 数据层 + 合并算法**（2026-09-03，M5 拆 M5a/M5b/M5c 后首轮交付）：`Models/SyncConfig.cs`（`SyncConfig` 配置 + `Snapshot` 远端快照模型，字段对齐 webdav.js saveSyncConfig）+ `Services/WebDavClient.cs`（HttpClient 直连 Basic 认证 + 10s 超时，MKCOL/GET/PUT，逐级 EnsureDir + TestConnection 写探针读回校验 + Fetch/Upload 远端快照，URL 方案按账号名寻址 `workbuddy/剪贴板/clipboard-<accountName>.json`）+ `Services/WebDavSync.cs`（`MergeSnapshots` 纯函数逐字移植 webdav.js L214 + 配置 IO Load/Save/ValidateAndBuild 校验 url 非空 · http(s) · 首次密码非空 · 间隔裁剪 [30,1440] 默认 720）+ `SelfTest` 增 27 断言（合并 8 用例对拍 test-merge-snapshot.mjs 17 条 + 配置 IO 10 条）。`--selftest` **223 断言全过**（196 + 27）；构建 0 错误 0 警告。
- ✅ **弹窗拖动修复**(2026-09-03):弹窗家族(`PasteDialog`/`EditDialog`/`DataDialog`/`JsonDialog`/`TagPicker`)无边框拖动改系统级 HTCAPTION(`ReleaseCapture + SendMessage WM_NCLBUTTONDOWN=0xA1, HTCAPTION=2`),根治 `AllowsTransparency` 下逐 `MouseMove` 手动 `Left/Top` 的狂闪 + 双屏混合 DPI 跨屏跳变 — 系统 DWM 合成移动,不闪、不跳、跨 DPI 无损、天然支持 Aero Snap(SO 3274097/33139478 高赞规范)。激活区扩到整卡非交互区(content 根 `Cursor=SizeAll`);命中 `ButtonBase`/`TextBoxBase`/`ComboBox`/`PasswordBox`/`CheckBox`/`ScrollBar`/`Hyperlink`/`Selector`/`ItemsControl` 等交互控件直接 `e.Handled=true` 不启动系统拖动,保留点按/选字/下拉/滚动。显式样式输入框(`EditInput`/`EditPassword`/`TagPickInput`)补 `Cursor=IBeam` 防 SizeAll 继承误导;按钮家族(`BtnBase`/`DarkCombo`/`OpsIconBtn`/`TagPickChip`/`MetaTag`)已自带 Hand 无需改。`Controls/ModalHost.cs` 拖动唯一实现点(其他 Dialog 不再独立写拖动)。`--selftest` **223 断言仍全过**(本修复纯 UI 交互层,不动业务规则);**差分门禁真两端跑通**(2026-09-03 修正:`tests/fixtures/rules.json` 文件名是 `rules.json` 不是 `diff-rules.json`,脚本名才含 `diff-rules`,易混淆;之前几次"差分门禁全绿"实际只跑了 node 端单端 PASS,exe 端因路径文件名错从未真跑,本次才真正 41 fixtures 双端对拍全绿);构建 0 错误 0 警告;**真机 GUI 冒烟**:3 次拖动位移 `(150,40)`/`(100,30)`/`(-250,-25)` 与鼠标增量精确一致零累积误差,快速拖动 8 步高频稳定,命中内容区交互控件窗口不动=排除生效;**拖动中 6 帧 `CopyFromScreen` 连拍 + 静止对照 1 张 = 7 张全部弹窗内容(标题/芯片/TextBox)完整清晰零残影零错位零空白帧 — 闪烁客观证据闭环**。`tools/shot/Program.cs` 新增 drag(屏幕坐标按住拖动,模拟真实拖动轨迹)/ wins(列进程窗口 rect,客观比对位移)/ region(`CopyFromScreen` 屏幕区域抓图,count 连拍;`CopyFromScreen` 不依赖 PrintWindow,对透明 layered 弹窗同样有效)三个子命令。修正认知:`tools/shot/Program.cs DoCrop` 的 x/y/w/h 是**窗口客户区相对坐标**,传屏幕绝对坐标越界会抛 GDI+ "Out of memory"(bitmap Clone 越界 Rectangle 经典报错),透明窗口截图首选 `region` 走桌面合成而非 PrintWindow

- 🟢 **徽章/图标「叶子形」根治 + 圆角令牌补全**（2026-09-03，UI 样式层，纯样式不改业务规则）：首页卡片收藏后置顶状态徽章呈「叶子/梭形」而非圆角矩形（用户实测反馈）。根因：`StatusSt`/`Badge`/`MetaTag` 等 ~16px 高元素沿用 `RadiusPill`(99)——WPF 将 CornerRadius clamp 到元素高度一半(≈8)，左右两端近半圆 + 顶底直线 = 扁胶囊（叶子形）；Web 端 CSS 同几何行为。修复：`Colors.xaml` 增 `RadiusSt`(6) 状态徽章令牌 + `CardEdgeBrush`(#33FFFFFF)；`RadiusIconSm` 6→8、`RadiusIcon` 8→10（20px/26px 图标钮）；`Styles.xaml` `Badge`+`StatusSt`+`MetaTag` 全家族 RadiusPill→RadiusSt，`BtnPin` 补 `ClipToBounds=True`（IsChecked 金底溢圆角裁进圆角内），`ModalCard` 增 1px `CardEdgeBrush` 描边（仅弹窗卡片，首页卡 `CardView` 仍 Transparent）。构建 0 错误 0 警告；`publish/` 17:58 重发布（exe 时间戳 17:58:15 > 源码末改 17:57:57，`.old` 保留可回滚）；真机 GUI 验收待用户确认后随 commit 落库
- ✅ **M5b 同步引擎**（2026-09-04，M5 拆 M5a/M5b/M5c 后第二轮交付，单账号桌面形态收窄实现）：`Services/SyncEngine.cs`（`RunSync` ≈ Web `runSync` 单账号形态：withInFlight(account) → EnsureDir → 远端快照 `clipboard-<accountName>.json`（默认账号 → `clipboard-default.json`，对齐 Web 按账号名寻址）→ FetchRemoteSnapshot → **本地快照 = 活跃区 + 归档区**（归档 clip 带 `Archived=true`）→ `MergeSnapshots`（复用 M5a 纯函数）→ **无条件写回本地**（写序：先 `SaveArchive` 防滚动覆盖 → `SaveClips` → `SaveTombstones`）→（`cfg.SyncFiles` 启用时）`SyncFileEntities` 增量上传 → **hadLocal 上传保护**（仅当合并前本地已有 clip/墓碑才 UploadSnapshot，防空备份覆盖远端——对齐 Web v0.6.13 教训）→ 刷新 `lastSyncAt/lastSyncError` + 返回 `SyncResult{Ok/RemoteExisted/Uploaded/Clips/Tombstones/Error}`）。`--selftest` **223 断言仍全过**（本里程碑纯新增文件，不动既有规则）。
- ✅ **M5c 配置 UI + 定时同步**（2026-09-04，M5 第三轮交付）：`Services/SyncController.cs`（DispatcherTimer 定时同步，间隔取配置）+ `Services/WebDavSync.cs` 配置读写扩展 + `Controls/DataDialog.xaml` 增 WebDAV 配置区块（URL/账号/密码/同步间隔/同步文件开关 + 测试连接 + 立即同步 + 配置落盘 `data/webdav.json`，明文密码存储对齐 Web 行为）+ 工具栏 `SyncBtn` 手动同步入口 + 同步状态（`lastSyncAt`/`lastSyncError`）展示。构建 0 错误 0 警告。
- ✅ **M5b/M5c 端到端验收**（2026-09-04，mock WebDAV 8180 + 隔离部署 `C:/Temp/clip-e2e-debug/`）：
  - **上传链路**：8 条严格 schema fixture 首同步 → toast「同步完成 · 共 8 条」；远端 `clipboard-default.json` 8 条唯一；本地与 pristine diff 为空（同步零脏写）；`lastSyncAt` 刷新、无错误。
  - **恢复链路**：本地置空 → 重启 → 同步 → 远端 8 条全量拉回；修复后 UI **无重启即时刷新**可见卡片。
  - **syncFiles 链路**：3 个文件实体（pdf/zip/png）上传远端 `files/default/file-pdf-001.pdf` 等，GET 内容逐字节一致。
  - 修复 2 处新缺陷：① **同步成功后卡片墙不刷新**——工具条 `SyncBtn` 成功路径补 `RefreshWall()`、DataDialog「立即同步」成功路径补 `_onChanged()`；② **远端文件实体名缺 `.`**——`FileStore.ExtFor` 返回不带点（如 `pdf`）而 Web `extFor` 返回带点（`.pdf`），`SyncEngine` 拼接远端 URI 时补 `"."`，防 Web↔exe 跨端互拉 404。
- 🐛 **修复：剪贴板图片无法识别（3 断点 + 防误伤）**（2026-09-04，用户反馈；Debug 真机链路验证：纯图片 SetImage → 自动弹存入窗 → toast「已接收图片」→ 存入 `type=file/mime=image/png` → `files/*.png` 落盘，全链路通）：
  - ① **纯图片复制不弹窗**：`MainWindow.TryAutoPrompt` 只认 `Clipboard.GetText()`，截图/右键复制图片（无文本）直接 return → 补 `IsImageOnlyClipboard()` 判定，图片也走 `OpenPasteDialog`。
  - ② **弹窗打开不接图**：`PasteDialog` 构造器新增 `else if IsImageOnlyClipboard()` → `ReadImageOnlyAsPng` → `PickBytes` 自动收成图片 chip（文本优先级不变，富文本不抢）。
  - ③ **弹窗内 Ctrl+V 无反应**：纯图片剪贴板下 WPF TextBox 粘贴命令 `CanPaste=false` → `DataObject.Pasting` 事件不触发 → `Input_KeyDown` 加 Ctrl+V 兜底：`ReadImageOnlyAsPng` 非空则吞键收图。
  - 防误伤：`OnPasting` 判定从 `GetDataPresent(Bitmap)` 收紧为 `IsImageOnly`（有图且无文本）——Word/网页富文本复制带位图预览 + 文本，不再被误存成图。
  - 代码收敛：PNG 编码/图片判定统一进 `Services/ClipboardHelper.cs`（`IsImageOnly`/`IsImageOnlyClipboard`/`ReadImageOnlyAsPng`/`EncodePng`，删除 PasteDialog 私有 `EncodePng`）。
- 🟢 **M3b-2b 图片线**（2026-09-01，M3b-2 拆 2a/2b 后单轮交付；建于 2a 实体存储之上）：`Services/Format.cs` 增 `IsImageMime` 统一入口（`mime?.StartsWith("image/")` 供卡片/单击复制/弹窗分流，对齐 Web 多处判定）+ `CardView` 图片卡体（`Image.Stretch=UniformToFill` cover 撑满 + `Border.ClipToBounds` + 解码失败自动降级文件卡 + `TypeBadge` 切紫色徽章「图片」）+ 点击图片卡中部 → `CopyImageRequested` 事件 → `MainWindow.CopyImageToClipboard`（`FileStore.ReadAllBytes` → `BitmapImage Freeze` → `System.Windows.Clipboard.SetImage` + `watcher.Suppress(800)` 来源抑制 + BumpCopyCount + flash「图片已复制，可直接粘贴」+ 失败 toast） + hover 悬浮预览（`DispatcherTimer 260ms` 延迟 + Popup + `CustomPopupPlacementCallback` 视口钳制 8px + 优先顶部 4px 间隙 + 空间不够落底部 + 滚轮缩放 50%~300% 步进 15% + 重新解码按比例设宽高 + cap 显示 fname·fsize·百分比）对齐 Web `bindImageHoverPreview` L1199 + 单击复制对齐 `handleCardClick` L1047-1055 + 复制逻辑对齐 `copyImageToClipboard` L1324 + `PasteDialog` 图片 chip 接收（`DataFormats.Bitmap` 剪贴板截图优先 + `EncodePng` BitmapSource→PNG + `PickBytes` 直接接收字节 + `Root_Drop` Bitmap 拖放，去掉 2a 拒收占位）+ `Colors.xaml` 增 2 令牌 `PurpleBrush`(#B39DDB)/`PurpleSoftBrush`(rgba purple .15) + `Styles.xaml` 增 `BadgeImage` 样式。`--selftest` **144 断言全过**（126 + 18 增量：FileStore 图片存读/扩展名映射 jpg·gif·webp/bmp 兜底/10MB 图片拒收/IsImageMime 4 态/MimeFromPath/Create file mime 字段）；构建 0 错误 0 警告；UIAutomation 冒烟：图片卡渲染正确（紫「图片」徽章 + 120×80 金色 PNG cover + #合同 chip + ↓ 下载 + ☆ 收藏 + × 删除）、点击中部 → `copyCount 0→1` 文件卡确认复制触发、`clickby ↓` Invoke 验证下载按钮；**`tools/shot/Program.cs` 同步修复 `SetWindowPos` bug**（缺 `SWP_NOMOVE|SWP_NOSIZE` 会把窗口 resize 到 0×0 被 WPF 钳到最小尺寸 624×441，导致点击坐标完全错位）
- 🟢 **WPF UI 细节精修**（2026-09-01）：
  - 全局滚动条：宽度 9→2px、Thumb 改 `RadiusScroll` 小圆角、去“叶子/胶囊”形状，所有滚动条统一极细默认风。
  - 圆角去胶囊：`BtnClose`/`TypeBadge`/`TagChip` 等窄高元素由 `RadiusPill`(99) 改为固定圆角矩形（12/8/8），消除叶子形。
  - 下拉菜单：按 Web `.edit-row select` / `.paste-adv select` 分别实现 r-9/126px/elev 底（编辑）与 r-10/138px/inset 底（存入）；新增 `Combo.cs` 附加属性让 `DarkCombo` 圆角可覆盖；修复 `DisplayMemberPath` + `ItemTemplate` 同时设置导致的 `InvalidOperationException` 崩溃；下拉项默认显示「永久」中文、选中态金底深字。
  - 操作按钮：编辑/存入弹窗主按钮占满剩余宽度居中；归档/取消固定小按钮。
  - 卡片墙：修复 `ItemWidth` 重复扣减 Gap 导致的右侧大片留白，卡片之间真正应用 16px 间隙；卡片右侧内边距 16→10、文本/链接 inset 右内边距 11→6，给滚动条“只留一点点”。
  - 清理：`.gitignore` 排除 `tools/shot/bin/` / `obj/` / 临时截图与日志；删除仓库中已跟踪的截图工具构建产物与临时截图。
  - `--selftest` **144 断言全过**；构建 0 错误。

## v0.6.17 (2026-09-02) — 平台版 / 本地服务版（bat）发布

> 📌 本版为**平台版 + bat 版**发版（v0.6.16 之后的快捷键调整）；**exe 桌面版仍在开发**（见上方 v0.7.0 节），不随本版发布。

### 快捷键调整：空格=存入 → Ctrl+V=存入 / 空格=聚焦搜索框（对齐 exe）

- 🟢 **平台版快捷键调整（2026-09-02）**：主页面快捷键由「空格=存入」改为「**Ctrl+V=存入** / **空格=聚焦搜索框**」。
  - Ctrl+V：仅当无弹窗、焦点不在输入区（INPUT/TEXTAREA/SELECT/contentEditable）时打开存入弹窗并自动填入剪贴板；输入区内 Ctrl+V 保留原生粘贴。
  - 空格：仅当焦点不在输入/交互元素（INPUT/TEXTAREA/SELECT/BUTTON/A/contentEditable）时把光标快速定位到搜索框并全选（直接输入即新检索）；不吞掉卡片回车与搜索框内空格、不触发页面滚动。
  - 守卫保持：未登录 / 已有弹窗 / 非主页面（用户选择页）不触发；Esc 仍关闭弹窗。源码 `public/app.js` `document.keydown` 全局监听（原 v0.6.15 空格=存入逻辑整段替换）。
- 📦 产物：`clipboard-tool-v0.6.17-bat.zip`（本地服务版，双击 `delivery/server/start-server.cmd`）+ `clipboard-tool-v0.6.17-platform.zip`（平台版，解压到 tools/ 挂载）

## v0.6.16 (2026-08-31)

### 排序修复：严格按点击次数排序（移除标签/内容归拢）

- 🔴 **修复卡片不按点击次数排序**（clips-query.js / clips-store.js）：`listClips` 排序管道原为 `sortClips → groupByTags → groupSimilar`——`sortClips` 按 copyCount 排好后，标签/内容归拢会把共享标签或相似内容的条目拉到一起，导致点击次数高的卡片被挤到低次数卡片后面（实测 3 次点击的卡片排在 4 次点击的前面）
- 🟢 **排序策略定稿**：① pinned 置顶 → ② copyCount 降序 → ③ updatedAt 降序。`listClips` 直接 `sortClips(list).map(publicClip)`，点击次数越高越靠前，严格全局单调
- 🟢 **删除死代码**：`groupByTags` / `groupSimilar` 及其 ngram 倒排索引逻辑整体移除（仅此一处调用）
- ✅ 验证：排序行为验证（pinned 置顶 / copyCount 降序 / 同次数按 updatedAt）+ 冒烟测试无回归

## v0.6.15 (2026-08-28)

### 批量编辑：多选 + 批量删除 / 批量加标签 / 批量减标签

- 🟢 **批量编辑模式**（app.js/index.html）：工具行新增「编辑」按钮进入多选——卡片左上角出现勾选框、整卡点击=切换选择（透明覆盖层，禁用复制/双击编辑/图片 hover 预览）；底部悬浮批量条显示「已选 N 项」+「全选当前页 / 取消全选」+「＋加标签 / －减标签 / 🗑删除 / 完成」
- 🟢 **全选当前页**：抽取 `getVisibleClips()` 纯函数（`renderList` 与「全选当前页」共用同一份可见集定义），全选=当前搜索/标签/类型过滤出的可见条目；已全选则再点取消全选
- 🟢 **后端批量接口**（`POST /api/clips/batch`）：单入口按 `action` 分发 `delete / addTags / removeTags`——`batchDeleteClips` 跨活跃区+归档删除（记墓碑 + 联动文件实体清理，与单条删除同语义）；`batchSetTags` 跨区加/减标签，**刷新 `updatedAt`**（WebDAV 合并的 key，不刷新则改动不同步远端、也不反映到排序）；空 ids / 未知 action 返回 400
- ✅ 验证：冒烟 42/42（新增批量加/减标签、批量删除、空选择/未知操作 400 共 8 条）· merge 17/17 · html 10/10 · playwright 端到端 13/13（进批量→点卡选中→全选→加标签→减标签→删除→退出）
- 🟢 **favicon**（index.html）：📋 emoji 内嵌 SVG data-URI 设为网站图标（`<link rel="icon">`，尖括号编码兼容严格解析器；零额外文件，平台托管/独立运行均生效）
- 🟢 **宽屏自适应**（index.html）：容器 `.view`/`.wall` 基础 960px → `≥1280px` 放宽 1440px → `≥1920px`(4K) 放宽 1920px——卡片墙 auto-fill 多列铺开，充分利用宽屏宽度；1024 以下保持原宽度不回归
- 🟢 **空格快捷键存入**（app.js）：主页面按**空格** = 点击「存入」按钮（打开存入弹窗）。守卫：未登录不触发 / 已有弹窗不叠加 / 焦点在输入框（搜索框打字空格）不拦截 / 仅主页面生效；`preventDefault` 阻止空格滚动页面；Esc 仍关闭弹窗。验证 6/6（弹窗出现/Esc关闭/输入框不误触/不叠加/不滚动）

## v0.6.14 (2026-08-27)

### 架构整理：前端过滤单轨化（bug 修复）+ 墓碑规则下沉 + 模块化拆分

- 🔴 **前端过滤单轨化修复**（app.js `loadClips`）：搜索/标签过滤统一走前端 `renderList`——`loadClips` 去掉 `q/tag` 参数恒拉全量（仅保留 `archived`）。修复真 bug：搜索词非空时发生增删改 → `refreshList` 把 `state.clips` 覆盖成过滤集 → 清空搜索词后全量从界面"消失"（须刷新页面恢复）。根因：后端过滤（历史遗留）与前端过滤（设计意图）双轨并存，一份 state 被两处过滤。后端 `listClips` 的 q/tag 参数保留（API 兼容）
- 🟢 **墓碑规则下沉到 core**（webdav.js `recordTombstoneIfConfigured` + routes/clips.js）："已配置 WebDAV 才记墓碑"原是业务规则写在路由层（`if (r.tombstone && getSyncConfig)`），下沉为 webdav 域内部判断，路由层只做无条件编排调用——消除唯一一处业务规则泄漏到 HTTP 层
- 🟢 **clips.js 585 行拆 5 文件**（clips-store/mutate/query/transfer/tombstones）：按"底层存取 / 写操作 / 查询 / 导入导出 / 墓碑"拆分，依赖方向单向（上层 → clips-store → store/config）零循环；`clips.js` 变纯聚合 re-export，**对外 import 路径与行为全不变**（webdav.js/测试脚本零改动）。renameTag/deleteTag/clearAll/sweep 原直写 `writeJson` 改为 `saveClips/saveArchive`（语义一致，超限滚动更正确）
- 🟢 **webdav.js runSync 拆阶段函数**：拉源/合并/写回/实体/上传/迁移清理六阶段拆独立小函数（`collectRemoteSources`/`mergeRemoteAll`/`writeBackLocal`/`cleanupMigrations`），`mergeSnapshots` 导出路径不变（17 单测不受影响）
- ✅ 验证：冒烟 34/34 · WebDAV 集成 19/19 · merge 17/17 · html 10/10 · P0 UI 场景 playwright 走查（搜索→编辑→清空搜索词全量恢复）全过
- 📌 说明：webdav.js 的 P1（墓碑下沉）与 P3（拆阶段）为同文件改动，合并为一个 commit（`be7c358`），便于整体回滚 webdav 域

### 环境清理 + 测试加固（2026-08-27 同日）

- 🔴 **数据清空事件排查结论**（2026-08-27 上午，两次「数据全空」）：最终判定为**环境问题**——机器上有 20+ 个不同版本/不同数据目录的遗留 `server.mjs` 实例（端口 8132/8133/8134/8135/8136/8137/8138/8139，08/26 起反复启动）长期未关，叠加冒烟测试默认连 8130 主服务污染 `users.json`（残留 u815113b/u5830b/u665694b 测试用户），多重混乱导致数据被清。**代码本身经全量测试 + 副本复现（富文本编辑/清除格式/删除标签/存入富文本）验证无可复现清空 bug**；数据经 WebDAV 远端快照完整恢复
- 🟢 **环境清理**：杀光全部遗留实例（仅保留 8130 主服务 + 8190 诊断页）；删除测试残留用户（u815113b/u5830b/u665694b）与孤儿测试文件（11111111）
- 🟢 **冒烟测试默认端口加固**（smoke-test.mjs）：默认端口 **8130 → 8131**，防止测试直连主服务污染真实数据（本次事件教训）；注释补血泪教训说明
- 🟢 **版本指纹**（server.mjs + package.json）：启动日志打印 `clipboard v0.6.14 (git commit)`——实例身份可追溯（多实例混跑事故的直接对策）；`package.json version` 0.5.1 → 0.6.14（此前与 CHANGELOG 脱节）
- 🟢 **平台版接入 tools-center**（delivery/platform/）：`tool.json`（id=clipboard / capabilities storage / dataFiles 声明）+ 部署说明——平台版 zip 解压到服务器 `tools/` 挂载目录即托管；数据落平台注入的 CAP_STORAGE_DIR（随平台 data/ 挂载持久化）
- 🟢 **发布规范 docs/发布规范.md**：专门发版规则——版本号（vX.Y.Z 语义化 + package.json 同步）/ 四形态产物命名 / 平台版 zip 结构约定 / 发版 Checklist（测试→打包→tag→gh release→服务器部署→验证）/ 回滚策略 / 历史记录

## v0.6.13 (2026-08-26)

### WebDAV 归档完整备份 + 归档可手动删除 + UI 微调

- 🟢 **会话去内存缓存（2026-08-27，users.js）**：文件 `sessions.json` 即唯一真相——createToken/verifyToken/destroyToken/pruneExpiredSessions 全部直接读写文件，删除 sessions Map 缓存与回滚逻辑。小体量（单机/低并发）下每请求读文件无感（实测读+解析 774B JSON 单次 ~14μs），换来模型最简：无"内存/文件两份数据"一致性问题（登录态丢/退出复活从根上消失）。⚠️ 注意事项：请求量达每秒几十次以上时再引入"内存缓存+TTL 1s"，勿提前加；loginGuard 限流表为临时状态保持内存
- 🟢 **富文本「清除格式」按钮**（app.js/index.html）：不需要富文本的条目，编辑弹窗（格式文本类型）新增「清除格式」按钮——确认后标记，保存时 `html` 置空转纯文本（后端 `sanitizeHtml` 空串即清空）；按钮变警告态「已标记清除 · 保存生效（点此取消）」，再点可取消；保存后卡片变普通文本（📝）、复制不再带格式。验证：富文本卡→清除→保存→html 清空 content 保留✅ / 再编辑类型变「文本」按钮不显示✅ / 冒烟 34/34

- 🟢 **WebDAV 快照纳入归档**（webdav.js）：快照 = 活跃区 ∪ 归档区（归档条目带 `archived` 标记）——此前归档只存本地不参与同步，现为完整备份，归档数据远端不丢
  - 拉回分拣写回：先 `saveArchive`（归档组替换）再 `saveClips`（活跃组，内部滚动自动追加归档）——顺序关键：先替换防滚动覆盖
  - 墓碑对归档删除同样传播（远端旧快照无归档标记时按活跃处理，滚动自动收敛）
- 🟢 **归档条目可删除**（clips.js `deleteArchivedClip` + 路由 + 前端）：归档卡片显示 ✕ 删除按钮，删除记墓碑（已配置 WebDAV 时）传播到远端，手动清理用不到的归档
- 🟢 **UI：主界面背景调深**（index.html）：`--elev #2F2F2F→#1F1F1F`、`--elev-hi #3A3A3A→#2A2A2A`，面板与背景更融合，整体观感贴近登录页深色沉浸
- 🟢 **UI：编辑弹窗标签区间距修复**（index.html）：`.edit-modal .tag-pick` 补 `margin-top:10px`（此前与上方别名/过期行零间距紧贴）+ 标签输入框改透明下划线式（与存入弹窗一致，去内阴影）
- 🟢 **UI：图片悬浮预览触发区收窄**（app.js）：open 从整卡 `mouseenter` 改为图片区域 `.imgwrap` `mouseenter`——此前鼠标悬停卡片按钮/标题/状态区也弹预览（用户反馈修正）；open 加防重（浮层挂卡内，移入浮层不再重建）；离开整卡才关闭（浮层不中途消失）
- 🟢 **编辑弹窗「归档」按钮**（clips.js `archiveClip` + 路由 `POST /api/clips/:id/archive` + 前端）：普通卡片编辑弹窗可一键移入归档区（确认提示「归档后可含归档查看、可随时恢复」）；归档参与 WebDAV 同步，用不到可删除
- 🟢 **归档可恢复**（clips.js `unarchiveClip` + 路由 `POST /api/clips/:id/restore` + 前端）：归档卡片右上角「↺ 恢复」移回活跃区，`updatedAt` 刷新为当前（防刚恢复被滚动立刻滚走）；编辑弹窗「归档」按钮改为与「取消」同尺寸同风格（88px 小按钮）
- 🔴 **WebDAV 文件实体上传失败修复**（webdav.js）：`ensureDir` 只建 `workbuddy/剪贴板/`，未建 `files/<uid>/` 实体目录——严格 WebDAV 服务器对「PUT 到不存在的目录」返回 409 → 同步报「文件实体上传失败:<文件名>」。现上传前先 MKCOL 确保 `files/` 与 `files/<uid>/` 存在；错误信息补 HTTP 状态码便于诊断
- 🟢 **一键无饱和度配色**（index.html/app.js）：顶栏 ◐ 按钮切换彩色/灰度配色——全部彩色令牌（金/砖红/绿/亮金/蓝/暖红 + 34 处硬编码 rgba）变量化（`--gold`/`--amber-rgb` 等 RGB 三元组），`html.mono` 一处覆盖全灰；localStorage 记忆，刷新保持
- 🟢 **修改用户名入口**（users.js `renameUser` + 路由 `POST /api/users/:id/name` + 数据管理左栏）：用户名输入框 + 保存，同名校验 409、长度校验，改名不销毁会话；改名后重建界面同步顶栏展示
- 🟢 **架构：mergeSnapshots 纯函数单元测试 + 主线总览文档**：合并裁决（墓碑/updatedAt 4 分支）补 `scripts/test-merge-snapshot.mjs`（17 项断言，node 直跑无需服务，测试即契约）；新增 `docs/main-flow.md` 主线 SSOT（一条剪贴板的生命周期一图流 + 各步入口函数 + 存储布局 + 支线索引），AGENTS 约定区挂引用
- 🟢 **健壮性：进程级异常兜底**（server.mjs）：`uncaughtException`/`unhandledRejection` 记录但不退出——路由层 try/catch 之外的最后防线（异步回调/定时器未预期错误不再导致进程崩溃、全用户掉线）
- 🟢 **并发：runSync per-user in-flight 锁**（webdav.js）：手动「一键同步」与定时 autoSync 同时触发时第二个抛 409「同步进行中」（此前并发会重复拉取/上传且 lastSyncAt 抖动）；autoSync 遇手动进行中跳过（不污染 lastSyncError）
- 🟢 **清理：死代码 3 处**（clips.js `getClip` / files.js `readFileBuffer` / app.js 废弃 `makeRichBtn` 防回滚占位）——全库确认无引用后删除；顺带修正 capabilities.md 过时行号引用
- 🟢 **双名模型重构（v0.6.13 账号名/显示名分离）**：`accountName` 账号名（创建后不可变，唯一身份键——WebDAV 寻址/跨设备识别）+ `displayName` 显示名（可随时改，仅影响展示）。业界标准（Steam/Google/WordPress 同款，Stack Overflow/Google AppEngine 官方文档确认）。**效果：改显示名不再影响 WebDAV 快照路径 → 删除 syncedName 改名迁移复杂度（runSync 回归单源合并）**；设备迁移 = 新部署创建相同账号名 → 同步即拉回。旧数据兼容：v0.6.13 前只有 name → accountName=name、displayName=name（读取归一不迁移文件）。前端：新建弹窗双输入 / 用户卡片显示名+账号名小字 / 数据管理账号名只读+显示名可改
- 🟢 **账号名修改（管理员级一次性操作）**：`POST /api/users/:id/account-name`——身份键修正通道（日常改名用显示名）；已配置 WebDAV 时记 `prevAccountName`，下次同步自动把旧账号名快照并入并迁移（幂等收敛）；前端数据管理账号名行「修改」按钮（确认弹窗提示迁移）。隔离验证 7/7（改账号名→同步→远端新名有数据+旧名删除+二次同步收敛）；真实执行：用户账号名「世界的风吹向你」→「billpotter」，11 条数据本地远端完全一致零丢失
- 🟢 **账号名仅限英文+数字（v0.6.13）**：`config.js ACCOUNT_NAME_RE`——账号名是身份键（WebDAV 文件名/跨设备识别），限 `[A-Za-z0-9]` 规避路径与编码兼容问题；显示名不受限（可中文）。createUser/changeAccountName 双入口校验 400「账号名仅限英文和数字」；前端新建/修改弹窗提示。存量中文账号名不强制迁移（仅约束新建/修改）。测试脚本中文用户名同步改英文（smoke 小明/小红→uRN、WebDAV测试→WebDAVTest）
- 🟢 **WebDAV 按账号名寻址（v0.6.13 设备迁移零配置）**：快照 `clipboard-<账号名>.json`、实体 `files/<账号名>/`——新部署创建相同账号名 → 配置 WebDAV → 同步即拉回全部数据（账号名即身份）。旧格式 `clipboard-<userId>.json` 首次同步自动并入并迁移（一次性自愈）。`runSync` 返回 `migrated` 标记
- 🔴 **原子写 Windows 兼容修复**（store.js `writeJson`）：Windows 上目标文件被瞬时锁定（Defender/索引扫描等外部进程）时 `renameSync` 抛 EPERM——此前直接抛错导致「写 sessions.json 失败」报错。现短重试 3 次（锁定通常 <200ms）+ 仍失败删除目标后重命名兜底（单实例数据可重建，接受极小窗口）；全失败清理残留 tmp 再抛 500（原文件未动不丢数据）。实测 300 次连续原子写零失败、无残留 tmp
- 🔴 **修改用户名持久化——治本重构**（users.js `getUserPublic` + 路由 `GET /api/users/me` + app.js）：改名保存后**强制刷新回落旧名**——根因是**设计缺陷**：`LS.cur` 缓存了 name/color（展示信息），boot 恢复登录直接 `state.current = saved` 用缓存对象——展示信息被当成持久化数据，缓存一旦过期就回落。**治本（消除缓存冗余源，而非补丁）**：①登录只存会话凭据 `{id, token}`（3 处统一，name/color 不再落缓存）②恢复登录 = 凭据恢复 + 必然从 `/api/users/me` 拉最新 name/color 填充（后端 users.json 是唯一权威源；旧缓存残留 name 仅作拉取失败兜底）③删掉"改名后补写 LS""boot 写回 LS"两处补丁——缓存里没有展示信息，永不"过期"。效果：改名/多端刷新永远一致，逻辑单源通顺
- 验证：归档同步端到端（505 条→5 归档→同步远端含 archived 标记→删归档→墓碑传播远端移除）全绿；图片预览三态验证（图片区触发✅/按钮区不触发✅/移入保持✅）；归档按钮端到端（按钮存在✅/活跃 8→7✅/含归档可见✅）；恢复闭环（按钮尺寸 88x41 一致✅/恢复 1→0✅/回活跃区✅）；文件实体同步（远端 files/<uid> 目录自动创建✅/实体上传成功✅）；配色切换（按钮✅/accent #C9A96E→#A8A8A8✅/刷新记忆✅/恢复彩色✅）；改名闭环（262→262x→262 顶栏同步✅）；**改名持久化治本版（登录 LS 纯凭据✅/刷新权威拉取✅/改名→刷新→保持 5/5✅）**；合并裁决单测 17/17 ✅；**同步并发锁（并发第二个 409✅/锁释放后成功✅）**；冒烟 34/34 零回归

## v0.6.12 (2026-08-26)

### 富文本复制链路修复批(最小单元链路诊断定位:Word 私有属性/body 属性保真 + 卡片分栏回归)

**排查方法**：新建最小单元链路诊断页（6 步：S1 捕获→S2 normalizeRichHtml→S3 存储→C2 buildWordDoc→C3 剪贴板写读→C4 还原渲染）+ 真实剪贴板写读 / 粘贴回读验证，逐段对比定位，不靠猜。

- 🔴 **S2 内联化丢 Word 私有属性修复**（`normalizeRichHtml`）：旧实现用 CSSOM `rule.style.cssText` 收集 `<style>` 块规则——CSSOM 只序列化浏览器认识的属性，`tab-interval` / `text-justify-trim` / `mso-*` 等 Word 私有属性全被丢弃、`word-wrap` 被规范化为 `overflow-wrap` → Word/WPS 粘贴还原不全（诊断页实测 cssText 只剩 2/6 属性）。改为 `style.textContent` 字符串级正则解析（跳过 `@` 规则），声明原样保留，元素匹配仍用 `el.matches`
- 🔴 **body 标签属性丢失修复**（`normalizeRichHtml`）：Word 文档级设置（`tab-interval:21.0pt;word-wrap:break-word;text-justify-trim:punctuation`）写在 `<body>` 标签上，旧实现返回 `doc.body.innerHTML`——body 自身属性不在 innerHTML 里，必然丢。现遍历 `body.attributes` 全部保留；`buildWordDoc` 兼容 `<body attrs>…</body>` 片段（属性并入外层 body，避免嵌套）
- 🟢 **body 文档级属性双保险**（`normalizeRichHtml`）：依据 CF_HTML 规范（MS Learn aa767917，粘贴应用主要解析 StartFragment/EndFragment 之间的 Fragment，body 属性在 Fragment 外的 context 里，部分应用不读），把 body 的 style 同时内联到段落元素（p/div/h1-h6/li），保证 Fragment 内也有文档级属性
- 🟢 **卡片富文本回归左右分栏 + 取消渲染预览**（app.js/index.html）：卡片内容区恢复左右分栏（左=普通文本 / 右=富文本，点击各复各的），顶部一行提示「T 普通文本 | ✦ 富文本」；删除富文本格式渲染预览（`richPreviewNodes`/`.rich-pv` 样式/分栏渲染分支）——预览渲染与真实 Word 差异大，取消；编辑弹窗富文本取消实时预览（单 textarea，保存后格式仍保留）；富文本复制按钮 🅡 转入备用（分栏即入口）
- 验证：最小单元诊断页 5 样例全链路无损 + 真实 Word 复制 html（42550 字）全链路——S2 lost[无]、C3a/C3b 无 CSS 属性丢失、真实 Ctrl+V 粘贴回读 4997 字（mso×92 / o:p×5 / xmlns / body 属性全保留）；开关组合实验确认当前输出 = Word 正确还原组合；冒烟 34/34 零回归

## v0.6.11 (2026-08-16)

### 细节审查修复批（代码审查 + 隔离实验验证，冒烟 34/34 · WebDAV 19/19 · 自动同步 3/3 · html 字段 10/10）

**排查方法**：全量代码审查（server.mjs + lib/core + lib/routes + public/app.js）+ 隔离脚本实测复现/验证，不靠猜。

- 🔴 **归档重复膨胀修复**（`rollToArchive`，clips.js）：活跃区超 500 条时，每次 `saveClips` 都把同一批最旧条目重复滚入归档——实测 800 条连续两次保存归档 300→600 翻倍（WebDAV 同步/高频操作触发）。现按 id 去重再追加，归档稳定不膨胀；新条目（updatedAt 新）仍正常留在活跃区
- 🔴 **`verifyPassword` 崩溃修复**（users.js）：passHash 损坏（手工编辑/半写坏文件，非合法 hex）时 `timingSafeEqual` 对不等长 Buffer 抛 `ERR_CRYPTO_TIMING_SAFE_EQUAL_LENGTH` → 登录 500。现先校验 hex + 长度一致，损坏数据登录安全返回 401
- 🟡 **导入非 UUID id 条目不可操作修复**（`sanitizeImported`，clips.js）：导入的条目 id 只截断不校验，非 UUID id 后续 `assertId` 全拒——编辑/复制/删除/置顶全 400（外部工具备份/手工 JSON 场景）。现校验 `ID_RE`，不合法则重新生成 UUID
- 🟡 **富文本编辑左右不一致修复**（前端 app.js）：富文本条目编辑纯文本后 `html` 字段不同步——卡片右栏预览与「复制带格式」拿到的还是旧内容。保存时 content 变了 → 按新文本重建 html（`textToHtml`）；JSON 预览「覆盖保存」同样处理
- 🟡 **登录限流表真修复**（`pruneLoginGuard`，users.js）：P1-3 原实现只清理「锁定过期且失败归零」的 key，**持续失败未达阈值（fails 1~7）且停手的 IP 永久残留**（假修复）。补 `lastFailAt` 时间戳，超过 `LOGIN_WINDOW_MS` 未再失败即回收
- 🟡 **WebDAV 实体同步扩展名兜底修复**（`extFor`，webdav.js）：无扩展名+未知 MIME 文件兜底由空串改 `.bin`（与 `saveFile` 的 bin 兜底一致）——此前远端路径 `<fileId>`、本地存 `<fileId>.bin`，恢复写盘无扩展名 → `getFilePath` 前缀匹配失败 → 下载 404
- 🟢 **`readBody` 超限流挂起修复**（helpers.js）：413 后 `req.resume()` 排空剩余流，防 keep-alive 复用挂起
- 🟢 **diag.html 并入静态缓存**（server.mjs）：此前每次请求 readFileSync，与 P1-4 缓存策略不一致
- 🟢 **WebDAV 自动同步间隔漂移修复**（前端 app.js）：间隔选项补 30 分钟（后端允许最小 30min，此前前端只有 1h 起，保存 30min 读回被 round 成 1h、再存变 60min）；读回/保存均精确到 0.5h
- 🟢 **下载 Blob URL 延迟释放**（前端 app.js）：`a.click()` 后立即 `revokeObjectURL` 部分浏览器下载中断，延迟 2s 释放
- 零行为回归（四套测试全绿）

## v0.6.9 (2026-08-16)

### 富文本复制链路重构定稿（数据流统一，清除历史缠绕）

**排查方法**：隔离诊断页 `public/diag.html`（`/diag.html` 路由）实测 iframe 内各 API 可用性 + 端到端模拟，不靠猜测。

**根因链（全链路）**：
1. 浏览器写剪贴板**强制剥 `<style>` 块/html/xmlns，只留 inline style**（Chromium 122+ `ClipboardWellFormedHtmlSanitizationWrite`，实测 `clipboard.write` 读回仅 98B）→ 样式必须内联
2. **Word 识别"来自 Word"靠 `xmlns:w="urn:schemas-microsoft-com:office:word"` 标记**（Microsoft roosterjs `isWordDesktopDocument.ts` / CKEditor paste-from-office）→ 片段不包装则 Word 判定"来自网页"，默认合并格式（宋体）
3. `execCommand + setData` 不受 Chromium 122+ sanitize 影响（W3C clipboard-apis#193），带 xmlns 的完整文档原样进 CF_HTML → Word 识别来源并保留格式（诊断实测通过）

**重构**（`public/app.js`，4 个干净函数，删除全部历史函数 `wrapWordDoc`/`inlineStyles`/`ensureWellFormedHtml`/`execCommandCopyRich`/`writeRichClipboard`）：
- **`normalizeRichHtml(html)`**（存入时统一）：DOMParser → `<style>` 块规则内联到元素 → 移除 style 块 → 干净内联片段存库
- **`buildWordDoc(html)`**（复制时统一）：片段 → 带 xmlns:o/w/m + StartFragment 的完整 Word 文档
- **`execCommandRich(rich, plain)`**：holder 纯文本承载选区（绝不 innerHTML 解析完整文档），setData 注入原始完整文档
- **`copyRich(html, text)`**：execCommand 主路径 + clipboard.write 兜底（都走 buildWordDoc）

**相关修复（v0.6.8 起累计）**：存入弹窗 paste 事件捕获 text/html、autoFill read() 单次调用、类型徽章「✦ 将存为：格式文本」自证、CSS 类名冲突 `.rich-pv`→`.re-pv`（消除卡片右栏污染）、编辑弹窗五类型、存入弹窗重写。
- 零行为回归（冒烟 34/34）

## v0.6.8 (2026-08-16)

### 富文本复制链路重写与实测闭环（场景走查 + 隔离诊断）

**根因终审**：Word 粘贴 html 时默认「合并格式」→ 显示无格式；选「保留源格式」即完整还原——复制/存入链路功能正常（隔离诊断页实测：paste 取 Word mso HTML 43408B、execCommand 复制读回含 inline style）。非代码缺陷。

- **copyRich 重写**（权威依据 MDN/web.dev/W3C#193）：`ensureWellFormedHtml` → Clipboard API 主路径 → execCommand 兜底（contenteditable+focus+setData 双格式），删除历史 wrap/顺序补丁
- **存入弹窗链路重写**：paste 事件捕获 `text/html`（手动粘贴可靠，无需 read 权限）；autoFill `read()` 只调一次（此前重复调用）；read 权限失败时提示手动粘贴保留格式
- **徽章自证**：存入弹窗类型徽章新增 `✦ 将存为：格式文本`（蓝色，pendingHtml 捕获成功可见）
- **CSS 类名冲突修复**：编辑弹窗富文本预览 `.rich-pv` → `.re-pv`（原全局类污染卡片右栏样式：虚线边框/角标/内边距）
- **隔离诊断页** `public/diag.html`（`/diag.html` 路由）：实测 iframe 内 Clipboard API/execCommand/paste 可用性，排障工具
- **场景走查报告** `docs/walkthrough/富文本复制链路走查.md`：R-1/R-2 实测降级 ⚪（环境支持全 API）
- 零行为回归（冒烟 34/34）

## v0.6.7 (2026-08-16)

### WebDAV 远端存储路径统一子目录（workbuddy/剪贴板/）

- **快照文件**：`<配置目录>/workbuddy/剪贴板/clipboard-<uid>.json`（原：配置目录根）
- **文件实体**：`<配置目录>/workbuddy/剪贴板/files/<uid>/<fileId><ext>`（勾选 syncFiles 时）
- `ensureDir` 改为**逐级 MKCOL**（根 → workbuddy/ → workbuddy/剪贴板/，WebDAV MKCOL 不支持递归）
- 连通测试探针文件同步移到子目录内
- 与 WebDAV 根的其他用途隔离，目录结构清晰；零行为回归（冒烟 34/34）

## v0.6.5 (2026-08-15)

### 卡片系统全量重构（方案 18 落地：三区骨架 + 类型专属内容）

- **统一骨架**：顶部徽章行（类型徽章 + 标题 + **状态徽章**）/ 弹性内容区 / 底部 meta 行（复制次数 + 标签 + 时间 + 操作按钮沉底常显）
- **状态徽章**（从操作区/meta 收敛到顶部行）：★ 置顶（金描边卡 + 金色徽章）/ ⏳ 过期（红）/ 归档（灰 + 整卡降透明）
- **类型专属内容区**（`makeCardBody`）：
  - 文本：2 行摘要（line-clamp）
  - JSON：**代码窗**（红/黄/绿三圆点装饰条 + 文件名 + 等宽缩进 + 键名金/字符串绿着色，安全重建不注入原始文本）
  - 链接：等宽 URL + 金色「↗ 打开链接」主按钮（原操作区「打开」移除）
  - 富文本：**左右对比双栏**——左=普通文本（纯文本样式）/ 右=富文本（白名单安全渲染格式：标题/粗体/列表金点/链接），点击各复各的
  - 图片：**cover 撑满**内容区（hover 放大镜浮层保留）
  - 文件：**类型图标卡**（PDF 红边 / ZIP 金边 / 其他中性，垂直居中）
- 归档卡不再显示「归档」操作区徽章（并入顶部状态徽章）
- **操作按钮对齐方案 18**：26px 方形图标钮（☆★ 置顶 / ✎ 编辑 / ✕ 删除 / ↓ 下载 / {} JSON），内凹底常显、hover 金、删除 hover 红；主操作统一金色胶囊（链接「↗ 打开链接」）
- **置顶实时生效**：点击后整体刷新——顶部 ★ 置顶徽章即时出现/消失 + 后端 pinned 优先排序生效（卡片跳到最前/归位）
- **操作按钮分组**：右上角只放 ✕ 删除；☆ 收藏（置顶）/ ✎ 编辑 / ↓ 下载 / {} JSON 在底部 meta 行右侧
- **图片复制计数修复**：`handleCardClick` 图片分支此前复制成功不计数（漏调 /copy 接口），且 `bumpCopyCount` 的 `c.type==="file"` 拦截会连带跳过图片——现图片复制同样 +1（普通文件下载仍不计数）
- **卡片高度策略**：恢复统一等高 `height:190px`（方案 18）；文本内容在等高内 flex 撑满 + **内部滚动**（`overflow-y:auto`，细滚动条 hover 卡片才显现）——长文本可滚动查看全文，不截断也不撑高卡片，混排零参差
- **富文本左右栏复制修复**：左右栏 onclick 中 `guard(...)` 漏末尾 `()`（guard 返回事件处理器但从未调用，点击只 stopPropagation、复制逻辑不执行）——补 `()` 后左=复制纯文本、右=复制带格式均生效；废弃的 `makeRichBtn` 同步修复
- **密码弹窗重设计（方案 22 极简聚焦）**：下划线式输入 + 浮动标签（focus/有内容时上移金色）+ 👁 显隐切换；头部「🔑 图标 + 标题 + 用户名胶囊」+ 底部「仅存哈希提示 + 关闭」；金色保存大按钮
- **数据管理弹窗重设计（方案 25 双栏工作台）**：左栏=设置与同步（缩放步长 + WebDAV 跨设备配置：url/user/pass + 实体同步/自动同步开关 + 保存配置/一键同步 + 状态区 P-104 失败可见）；右栏=备份与风险（导出/导入 + 红色危险区：全部清空/删除账号）；底部「数据仅存本地 JSON」+ 关闭；`renderWebdavSection` 适配 dm- 类双栏容器
- 新增 `hostOf` / `makeJsonPreview` / `makeFileIcon` / `makeLinkBody`；删除废弃的 `makeCardPreview` / `makeOpenBtn`
- CSS 全面重构（`.clip-card` 三区、`.status/.st.*`、`.pv/.code/.imgwrap/.filebody/.linkbody/.main-btn`、`.ops .b`），hover 金描边统一
- 零后端改动；冒烟 34/34 通过

## v0.6.4 (2026-08-15)

### 主页面版式改版 · 双行工具栏（方案 13 落地）

- 🧰 **工具条重设计**（`renderMain` + index.html 主页面 CSS）：
  - **行1**：大搜索胶囊 + **右侧金色小按钮「＋存入」**（存入入口收缩为小按钮，点击打开存入大弹窗；粘贴检测自动弹窗不受影响）
  - **行2**：类型分段（全部/文本/链接/文件）+ 标签栏（含管理按钮）+ 右侧操作（**含归档开关 / 列数选择 / 「↻ 同步」**）
- 顶栏精简：一键同步从顶栏移入工具行 ops；密码 / 数据管理 / 退出保留
- 删除旧的 `.paste-trigger` 大条与 `.toolbar` 样式；`.tagbar`/`.typetab` 改为行内无 margin
- `renderTagbar` 增加容器参数（渲染进 `.tagbar-wrap`；无参自动查找，兜底 `#view`），`refreshList` 等调用点同步
- **零功能变更**：搜索防抖 / 类型过滤 / 标签过滤管理 / 含归档 / 列数偏好 / 一键同步 / 存入弹窗全部保留
- 验证：语法通过 · 冒烟 34/34 ✅

## v0.6.3 (2026-08-15)

### 首页改版 · 极简墙布局（方案 C 落地）

- 🏠 **用户选择页整体重设计**（`renderUserSelect` + index.html 用户区 CSS）：
  - 品牌区：印章式「剪」logo（内嵌方块+金色）+ 衬线标题「剪贴板」（Noto Serif SC 栈）+ 副标 `SELECT A PERSON · 选择身份进入`
  - 用户网格：紧凑卡（44px 头像 + 名字 + 计数），`auto-fill minmax(150px)` 高密度铺排；hover 金色描边 + 凸起阴影，active 内凹
  - 新建用户：虚线占位卡（+ 图标），hover 金描边
  - 底部操作：「编辑」胶囊按钮（on 态内凹金字），编辑模式删除按钮淡入显示
  - 脚注：`LOCAL JSON · 数据隔离`
- **零逻辑改动**：功能/事件/编辑模式/新建弹窗全保留，仅换 DOM 结构 + 样式；`setUserEditMode`/boot 空白点击委托选择器未变
- 新增令牌：`--ease` 指数缓动；`.serif` 衬线工具类
- 验证：语法通过 · 冒烟 34/34 ✅

## v0.6.2 (2026-08-15)

### 酒红金配色方案（暗黑新拟态 · 奢华暗黑调 · 校准版）

- 🎨 **对齐 dark-design-style-guide 主页示意 mock**（`--mbg:#1A1A1A / --msurf:#2F2F2F / --mtext:#DADADA / --mmuted:#848484 / --mac:#C9A96E / --mac2:#AE4D4D / --mat:#101014`）
- 设计令牌重设：背景 `#1A1A1A` / 面板 `#2F2F2F` / 内嵌 `#141414` / 文字 `#DADADA` / 次要 `#848484`（**中性黑灰底**，非酒红暖调）
- 主强调 `#C9A96E` 金（按钮/选中/选区/标签 active，**深字 `#101014` 配**）、点缀 `#AE4D4D` 砖红（链接徽章/危险 hover/链接文字 hover）
- 阴影高光：**中性灰** `rgba(88,88,88,.3-.4)`（对齐中性底，非暖棕）
- 功能色：绿 `#9FBF8F`（文本徽章）/ 金 `#D4AF37`（文件徽章）/ **砖红 `#AE4D4D`（链接徽章，与 mac2 统一）**/ 暖红 `#E08A7A`（危险）
- 用户头像色板：金为主 + 砖红点缀 + 暖棕系（`users.js PALETTE`）
- **零逻辑改动**：仅 `index.html` 令牌与 `users.js` 色板，app.js 无硬编码颜色、行为不变

> 调整记录：首版酒红主强调 → 二版金主红点缀 → 三版对齐 mock 中性黑灰底（金主 + 砖红点缀）

## v0.6.1 (2026-08-15)

### 针对性走查修复批（P-101~P-104）

- 🔴 **P-101 删除失败误报"已删除"修复**：`makeDeleteBtn` 增加 `if (r)` 返回值守卫——API 失败（网络/会话失效）时仅 errToast，不再无条件 `flash("已删除")` 误导用户；与删用户/删标签/置顶/JSON 覆盖的检查风格对齐
- 🟡 **P-102 墓碑仅在配置 WebDAV 后记录**：`deleteClip` 改为返回 `{ ok, fileId, tombstone }` 不落盘，路由层 `getSyncConfig` 存在才调 `recordTombstone`——未配置同步的用户删除不再产生 `<uid>.tombstones.json` 文件（同步语义不变，测试覆盖保持）
- 🟡 **P-104 自动同步失败可观测**：`runAutoSync` 失败时把 `lastSyncError` 写入 `<uid>.webdav.json`（不更新 lastSyncAt，保持原"每周期重试"节奏），`GET /api/sync/config` 返回该字段，前端数据管理弹窗展示「⚠ 上次自动同步失败:xx」
- ⚪ **P-103 拼音搜索范围文档注明**：README 功能列表注明"拼音匹配仅前端搜索框输入生效；后端 `?q=` 为子串匹配"（现状确认，非缺陷）

### 验证
- 冒烟 34/34 ✅ · WebDAV 集成 19/19 ✅ · 自动同步 3/3 ✅
- P-102 实证：未配置 WebDAV 的用户删除后无墓碑文件、条目正常删除
- P-104 实证：mock 远端停机 → 自动同步失败 → `lastSyncError="WebDAV 连接失败: fetch failed"` 落盘并由 GET 接口返回，lastSyncAt 保持原值

## v0.6.0 (2026-08-15)

### 暗黑新拟态 UI 迁移 + 富文本双格式复制

**🎨 UI 迁移（暗黑新拟态 Neumorphism，依据《UI迁移手册.md》）**
- 设计令牌全换：背景 `#1c1f26` / 面板 `#232730` / 内嵌 `#191c22`，主强调色粉 `#ff9292` → 蓝紫 `#7f9dff`
- 风格核心：**双阴影浮雕**（凸起=外阴影、凹陷=内阴影、按压=阴影反转），全界面去 1px 描边；阴影偏移统一 1px（贴合感），圆角 14-18px
- 组件改造：按钮四态、输入框凹陷内嵌、类型 Tab 改分段控件（凹陷轨道+凸起选中）、checkbox 伪装新拟态开关、搜索框胶囊+放大镜图标
- 布局对齐：容器 720→960px、hero 紧凑化、卡片间距 16px、顶栏整条浮雕面板
- **关键修复**：`.clip-card` hover/active 去掉 `transform`——卡片内部挂 `position:fixed` 的图片预览浮层，transform 会创建 containing block 使 fixed 相对卡片而非视口，导致浮层定位错乱（浮层"飘远"根因）
- 图片 hover 预览浮层与卡片间隙 8px → 4px（贴合卡片）

**📋 富文本双格式复制（content 纯文本 + html 富文本并存）**
- 后端：条目新增可选 `html` 字段（`MAX_HTML: 512KB`）；创建/编辑/导入净化全覆盖，导出/WebDAV 天然携带
- 前端：存入时检测剪贴板 `text/html` → 有富文本来源则存双版本；卡片显示 **🅡 富文本复制按钮**（仅富文本条目）
- 复制：`copyRich()` 剪贴板写入双 MIME（`text/html` + `text/plain`）——粘贴到 Word/飞书保留格式、记事本得纯文本；execCommand 降级兜底
- 决策：**不上数据库**（JSON 足够：100 用户×500 条×512KB 上限；上库破坏零依赖 + WebDAV 快照协议；`node:sqlite` 仍实验性）

**🛠 工具与测试**
- 新增 `scripts/cc-measure.mjs`：AST 级圈复杂度/认知复杂度/LOC 测量工具
- 新增 `scripts/test-html-field.mjs`：富文本 html 字段单测（新建/截断/更新/清空/导出/导入）
- 验证：html 字段单测 10/10、冒烟 34/34、WebDAV 19/19、自动同步 3/3 全绿

## v0.5.2 (2026-08-14)

### 取消 UI 重构，删除设计稿（产品决策）

- ❌ **删除 `design-preview.html` 设计稿**：v0.5.0 的主页面 UI 重构方案（磨砂玻璃/等高网格/渐进披露卡片）**确定不做**，设计稿文件已从仓库移除（`git rm`）
- 影响：前端维持 v0.4.x 现有界面（index.html + app.js 不变）；CHANGELOG v0.5.0-设计稿 章节保留仅作历史记录（标注已废弃）
- 无代码改动，纯清理

## v0.5.1 (2026-08-14)

### P0 修正批：工程卫生 + 文档纠偏（优化计划落地）

- 🟡 **补 package.json**（P0-1）：`"type": "module"`（显式声明 ESM，消除 Node 22 模块语法探测开销）+ `engines.node >=22.7` + npm scripts（`start` / `smoke` / `test:webdav` / `test:auto-sync` / `test` 一键三套）
- 🟡 **测试脚本参数化**（P0-2）：`test-webdav-sync.mjs` / `test-auto-sync.mjs` 的端口与数据目录改读 `TEST_PORT`（默认 8131）/ `TEST_DATA_DIR`（默认 `C:/Temp/clipboard-test`），与 smoke-test 风格统一，不再硬编码
- 🔴 **mock-webdav safeJoin 路径修复**（P0-3）：`path.join`（相对路径无盘符）与 `path.resolve`（补盘符）不一致导致相对路径下 `startsWith` 恒 false、所有请求 400 bad path——两侧统一 `path.resolve` 归一化，且修复前缀匹配边界（`dataDir + path.sep` 精确判定），`../` 逃逸仍拦截
- 🟡 **safe-delete 根源澄清**（P0-4）：实测确认 `fs.rmSync.name === "wrappedRmSync"` 是 **WorkBuddy AI 沙箱注入的 genie-safe-delete shim**（NODE_OPTIONS 猴补丁送回收站），非 Node 22 原生行为；用户手动运行正常。AGENTS/DEVELOPMENT 关键坑重写，rmForce 容错设计不变
- 🟡 **README 纠偏**（P0-4）：数据目录 `./data` → `./.data`；测试命令补 Windows 绝对路径说明

### 验证
- 冒烟 34/34 无回归；WebDAV 集成 19/19 无回归；自动同步 3/3 无回归；mock-webdav 相对/绝对路径 + 逃逸用例全部通过

## v0.5.0-设计稿 (2026-08-12~13) — ⚠️ 已废弃（2026-08-14 决定不做，design-preview.html 已删除）

### 主页面整体 UI 重构设计稿（未落地代码，先评审）

- 🟡 **design-preview.html 设计稿**（纯静态可预览）：
  - **范式**：常用优先·分区直达（置顶/最近分区，0 输入点击即复制，搜索兜底）——参考 Chrome 剪贴板历史 / Win+V
  - **布局**：等高网格（放弃瀑布流参差）→ 大屏 auto-fit 多列自适应 → 窄屏双列/单列
  - **卡片 v3**：渐进式披露（默认干净，hover 浮现操作图标）；类型色点替代文字标签；置顶仅星标徽章不整卡高亮；操作按钮按类型匹配程序（链接=打开/图片=下载/全=编辑+删除）
  - **磨砂玻璃**：深色为主（背景 #101014 + 光斑 opacity .07-.14 + 玻璃填充 85-92%），粉色仅点缀
  - **弹窗体系**：存入/编辑(含重复提示)/密码/数据管理/标签管理 5 个磨砂弹窗，底部切换条演示；过期改 chips 分段选择（统一设计语言）
  - **极窄顶栏**：<520px emoji-only 按钮 + ⋯ 收起菜单（低频收菜单、高频保留）
- ⚠️ **状态**：设计稿已生成待评审，**未合入正式代码**（index.html CSS 与 app.js 未动）

## v0.4.5 (2026-08-12)

### 逻辑核验加固（状态图验证方法论：防连点覆盖补齐）

- 🔴 **3 处异步操作补 guard 防连点**（核验 P2-1）：退出按钮 / 归档勾选 / 用户选择页卡片——消除快速连点导致的并发竞态（重复 DELETE 会话、归档加载竞态、并发建会话）
- 🟡 **findDuplicateClip 加字段兜底**（核验 P2-2）：link 条目的 url 与 text 条目的 content 均 `|| ""` 兜底——防 undefined 参与比对（防御性）

### 验证
- 冒烟测试 34/34 无回归；app.js 语法校验通过

## v0.4.4 (2026-08-12)

### 函数粒度拆分（架构评估 v3 量化落地：圈复杂度大幅下降）

- 🟡 **clipCard 拆分（CC 49 → 15）**：7 个操作按钮抽独立工厂函数（makePinBtn/makeOpenBtn/makeDownloadBtn/makeEditBtn/makeDeleteBtn/makeJsonBtn），预览区/meta 区/单击动作各抽独立函数（makeCardPreview/makeCardMeta/handleCardClick），hover 预览抽 bindImageHoverPreview（CC 14）——216 行 → 27 行组装
- 🟡 **openPasteModal 拆分（CC 46 → 22）**：保存流程抽 savePasteContent（CC 10，含重复检测）、自动填入抽 autoFillPasteModal（CC 16）——151 行 → 103 行编排
- 🟢 全部拆分后 app.js 无 CC>22 函数（boot 25 为启动编排，属可接受）

### 验证
- 冒烟测试 34/34 无回归；app.js 81021 字节完整加载；各拆分函数语法校验通过

## v0.4.3 (2026-08-12)

### 架构评估 v2 三项落地（主线 A / 支线 A- / 模块化 B+）

- 🟡 **hover 预览状态显式化**：`scale/hoverTimer/hoverBox` 收敛为单一 `previewState` 状态对象（open/scale/timer/box/drag），zoom 抽独立函数，滚轮/拖拽/开关统一走 open/close/applyScale——防布尔失控（FSM 标准）
- 🟡 **openDataModal 拆分**：WebDAV 配置区抽独立 `renderWebdavSection()`（弹窗 156 行 → 100 行）——单一职责"函数>20 行即拆分信号"
- 🟡 **重复检测抽纯函数**：`findDuplicateClip(content, clips)` 无 DOM 副作用，clipboardchange 与存入按钮统一调用——比对逻辑可单测（6 项用例通过）
- 🟢 注释/文档同步（CHANGELOG / DEVELOPMENT / 架构评估报告-v2 标记已修复）

### 验证
- 冒烟测试 34/34 无回归；app.js 完整加载 + 语法校验通过

## v0.4.2 (2026-08-12)

### 图片预览完善 + 交互重构

- 🟡 **图片预览默认 100%**：缩放范围 50%~300%，滚轮步长默认 15%（设置可调 1%~50%）
- 🟡 **浮层跟随卡片 + 框体随缩放同步变大** + 放大后拖拽平移查看（替代滚动条）
- 🔴 **弹窗触发逻辑重构**（先比对再弹窗）：clipboardchange 先读剪贴板比对仓库——重复直接弹编辑页带「已有相同内容」提示，不重复才弹存入窗；卡片点击复制不弹（来源抑制）
- 🟡 **存入弹窗优化**：高级选项（别名/标签/过期）默认全展开；选图片后隐藏文本区（可 ✕ 取消）
- 🟡 **标签管理独立入口**：标签栏最右「管理」按钮 → 独立弹窗（从设置挪出）
- 🟡 **顶栏重构**：「设置」拆「密码」+「数据管理」；删「切换」（与退出重复）；新增顶栏「一键同步」
- 🟡 **复制提示跟随鼠标位置**（点击坐标）；过渡只作用于透明度（修复"提示飞过来"）
- 🟡 **卡片时间完整格式**（年月日+时分）；图片卡片删冗余「预览」按钮

## v0.4.0 (2026-08-12)

### 前端 JS 拆分 + 图片卡片体验

- 🔴 **前端 JS 拆分独立文件**（架构评估落地）：`index.html` 1251 行 → 125 行（HTML+CSS），JS 移入 `public/app.js`（1129 行）；`server.mjs` 加 `/app.js` 静态路由——单文件超维护甜蜜点问题解决
- 🟡 **图片卡片缩略图**：图片类型卡片显示小缩略图（懒加载 `loading="lazy"`），替代文件名文本预览
- 🟡 **图片预览按钮**：图片卡片 ops 区新增「预览」按钮 → 打开大图弹窗（下载按钮保留）
- 🟡 **图片 hover 预览浮层**：鼠标移入图片卡片 260ms 后弹出 320px 大图浮层（含文件名+大小）；移出消失；自动避让屏幕边缘

### 验证
- app.js 68362 字节完整加载 + 语法校验通过；上传图片端到端（上传→条目→缩略图 URL 可访问）
- 冒烟测试 34/34 无回归（前端拆分不影响功能）

## v0.3.1 (2026-08-12)

### 修复：模拟执行检查发现的 7 个问题

- 🔴 **滚动归档不再移走新条目**：`rollToArchive` 由「按 copyCount 排序保留」改为「按 updatedAt 保留最近更新的前 N 条」——刚存入/刚复制/刚编辑的条目绝不进归档（此前新条目 copyCount=0 会被立即滚走，用户存完看不到）
- 🔴 **登录限流不再连坐**：限流 key 由 IP 改为 `ip:userId`——只锁「同一 IP 对同一用户」的暴力尝试，一个用户输错密码不再锁住所有用户
- 🔴 **会话失效后列表不再空白**：`handleSessionLost` 改用完整 `resetFilter()`（此前重置 filter 缺 type/archived 字段，重进用户后列表被 type=undefined 过滤成空白）
- 🟡 **删除用户清理配置残留**：`deleteUser` 补删 `<uid>.tombstones.json` 和 `<uid>.webdav.json`（此前残留，webdav 配置含密码明文有泄漏风险）
- 🟡 **rmForce 容错加固**：unlinkSync/rmdirSync 在沙箱 safe-delete 拦截下"抛错但实际已删除"——逐个 try/catch 吞掉，保证批量清理不中断（此前 deleteUser 中途 500 导致后续清理不执行）
- 🟡 **标签重命名空目标拒绝**：`renameTag` 校验 `!to` 返回 400（此前空目标会静默删除标签）
- 🟢 **链接自动标题用清理后 URL**：`createClip` link 标题改取 `clip.url`（此前用原始 URL，utm 追踪参数残留进标题）
- 🟢 **归档不进 WebDAV 快照的说明**：README + 前端「含归档」开关 title 提示（归档只存本地，不参与 WebDAV 同步）

### 验证
- 7 项复现用例全部通过（新条目保留/限流不连坐/无配置残留/空标签 400/标题无 utm/rmForce 容错）
- 冒烟测试 34/34 无回归；墓碑合并边界单测通过

## v0.3.0 (2026-08-12)

### P1 功能三项 + 会话持久化

- 🔴 **会话持久化**：token 从内存 Map 落盘到 `sessions.json`（`CAP_STORAGE_DIR`），**服务重启不再掉线**；token 带 30 天有效期，60s 后台惰性清理过期会话；改密/删号/登出联动清理（含保留当前会话语义不变）
- 🟡 **标签管理增强**：设置弹窗新增「标签管理」区——重命名 / 删除标签，跨活跃区+归档全部条目同步生效，重命名同名合并去重（`PUT/DELETE /api/tags/:name`）
- 🟡 **JSON 格式化预览**：文本条目内容可解析为 JSON 时，卡片出现 `{}` 按钮——弹窗美化展示（2 空格缩进），可复制美化结果 / 覆盖保存回条目
- 🟡 设置弹窗加滚动（`max-height:80vh` + overflow-y:auto），内容变多不溢出

### 修复
- `pruneExpiredSessions` 未导出导致 server.mjs 启动报 SyntaxError——补 export

### 验证
- 标签重命名/删除 API 端到端（中文标签 URL 编码）；会话落盘文件生成；**重启后 token 仍有效**；冒烟测试 34/34 无回归

## v0.2.0 (2026-08-12)

### P0 功能四件套（本地体验 + 数据安全）

- 🔴 **星标收藏（Pin）**：卡片 ★/☆ 一键置顶，排序升级为「pinned 置顶 → 复制次数 → 标签相近 → 内容相近」；置顶卡片琥珀色描边高亮（`/api/clips/:id/pin`）
- 🔴 **滚动归档**：活跃区上限 500 条/用户（`MAX_CLIPS_PER_USER` 可配），超出后按排序价值保留前 500、最旧条目按 createdAt 升序移入 `users/<uid>.archive.json`——**零丢失**；工具栏「含归档」勾选可搜历史（`?archived=1`，只读展示）
  - 清空 = 活跃区+归档一并清空；删除用户连带删归档；过期清扫覆盖活跃区+归档区
  - 与 WebDAV 语义协调：清空不传播删除的规则不变，归档不进同步快照
- 🔴 **数据导出/导入**：设置弹窗「本地备份」区——导出全部（含归档）为 JSON 下载；导入合并（同 id 取 updatedAt 新者，新增/更新/跳过分开计数），不清理本地既有数据（`/api/export` + `/api/import`）
- 🔴 **URL 自动清理**：保存链接时自动剔除 UTM 及常见追踪参数（utm_* / fbclid / gclid / msclkid / mc_* 等 21 个），无追踪参数原样保留（`cleanUrl()`，新增/编辑均生效）

### 修复
- `sanitizeImported` 的 `new Set(...).slice()` 语法错误（Set 无 slice，导入会崩）——改为 `[...new Set(...)].slice()`

### 验证
- cleanUrl 单测 7/7 通过；滚动归档脚本验证（505 条 → 活跃 500 + 归档 5、导出 505、清空、导入去重、重复跳过）；API 端到端（导出/导入/归档查询/星标排序）；冒烟测试 34/34 无回归

## v0.1.3 (2026-08-12)

### 修复：点击卡片复制不再自动弹出"存入"窗口

- 🔴 **问题**：点卡片复制文本/图片后，`clipboardchange` 监听误判为"外部复制内容"，刚复制完就自动弹出存入大窗口
- 🔴 **根因**：本应用 `copyText`/`copyImageToClipboard` 写系统剪贴板 → 触发 `clipboardchange` → boot 监听器 `openPasteModal(true)`
- 🟢 **修复**：新增 `suppressAutoPasteUntil` 来源抑制时间戳——`card.onclick` 入口置 800ms 窗口，`clipboardchange` 回调在窗口期内跳过自动弹窗
- 🟢 验证：语法校验通过，页面实时加载新代码（server 每次请求读 index.html，无需重启）

## v0.1.2 (2026-08-12)

### 文档四件套补齐（docs）

- 按 knowledge-base 单项目规范补齐四件套：`AGENTS.md`（技术栈/关键坑/约定/常用命令 + 文档基线）、`DEVELOPMENT.md`（架构说明 + 一坑一篇问题记录）、`CHANGELOG.md`（本文件）
- README.md 校对（功能清单与实现核对，补充文档索引）
- 文档基线：2026-08-12（commit 见 AGENTS.md 顶部）

## v0.1.1 (2026-08-09)

### 部署工具（chore `36cd53b`）

- 新增 `scripts/deploy-via-api.mjs`：github.com 被墙时通过 GitHub Git Data API（api.github.com 可直连）推送本地仓库——blobs → tree → commit → ref（main 不存在则创建，已存在强推）

## v0.1.0 (2026-08-09)

### 初始化：多用户剪贴板工具（init `a3b7042`）

**功能**：
- **单一万能入口**：粘贴自动识别（URL→链接 / 其他→文本 / 拖放/选择文件→文件条目 / Ctrl+V 粘贴图片文件）；检测到复制内容自动弹出大窗口（`clipboardchange` 监听）
- **一键复制 / 双击编辑**：文本/链接复制内容，图片点击直接复制到系统剪贴板（ClipboardItem，降级转 PNG / 预览），其他文件点击下载
- **智能排序**：复制次数优先 → 标签相近归拢 → 内容相似归拢（10 字符片段倒排索引，毫秒级）
- **拼音首字母搜索**：内置 3755 常用字映射表，`sf` 可搜到"身份"
- **标签体系**：点选已有标签 + 输入新建，列表标签过滤
- **多用户**：无密码零摩擦进入 / 可选密码锁，会话 token 内存态（服务重启需重登）
- **WebDAV 备份同步**：单向全量备份 + 双向合并同步（墓碑机制防删除复活、全部清空不传播删除、定时自动同步默认 12h、可选同步文件实体）
- **安全**：UUID 白名单防路径穿越、原子写、登录限流（8 次/60s/IP）、上传黑名单（拒可执行/脚本类）、下载强制 attachment

**技术**：Node 22+ 零依赖（内置 http/fetch/crypto）；前端单文件原生 JS；JSON 文件存储（原子写）；WebDAV HTTP 直连
