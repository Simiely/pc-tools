# pc-tools · Windows / PC 工具索引

> 🔖 **本仓库是索引仓库（导航中心），不放任何代码。**
> 每个工具的**开发、构建、发行**都在它**自己的独立仓库**里进行 —— 点下表链接直达。

## 工具一览

### 🖥️ 常驻程序

| 工具 | 说明 | 技术栈 | 最新版本 | 最近更新 |
|---|---|---|---|---|
| **[`WindowTinter`](https://github.com/Simiely/WindowTinter)** | 给任意窗口叠深色半透明蒙版；切到后台自动变半透明 | C# · WinForms | [`v6.1.0`](https://github.com/Simiely/WindowTinter/releases/latest) | 2026-08-06 |
| **[`clipboard-tool`](https://github.com/Simiely/clipboard-tool)** | 多用户剪贴板管理：粘贴即存 / 拼音搜索 / 标签 / WebDAV 同步 | C# · WPF + Node | [`v0.7.6`](https://github.com/Simiely/clipboard-tool/releases/latest) | 2026-09-13 |
| **[`MonitorBrightness`](https://github.com/Simiely/MonitorBrightness)** | 外接显示器亮度定时调节（DDC/CI 硬件调光，免常驻单文件 exe） | Python · tkinter | [`v1.6.4`](https://github.com/Simiely/MonitorBrightness/releases/latest) | 2026-10-08 |
| **[`gh-latency`](https://github.com/Simiely/gh-latency)** | GitHub 延迟监视器 | Python · GUI | [`v1.0.0`](https://github.com/Simiely/gh-latency/releases/latest) | 2026-10-08 |
| **[`gh-fetcher`](https://github.com/Simiely/gh-fetcher)** | GitHub 仓库 / Release 浏览下载器：镜像链路由 + 字节校验回退，专治直连抽风 | Python · tkinter | [`v1.0.0`](https://github.com/Simiely/gh-fetcher/releases/latest) | 2026-10-08 |

### 🔧 系统修复

| 工具 | 说明 | 技术栈 | 最新版本 | 最近更新 |
|---|---|---|---|---|
| **[`ExplorerBlurMica-whitebar-fix`](https://github.com/Simiely/ExplorerBlurMica-whitebar-fix)** | 修 ExplorerBlurMica 拉起下载目录时的底部白条 | 脚本 | [`v1.0.0`](https://github.com/Simiely/ExplorerBlurMica-whitebar-fix/releases/latest) | 2026-07-12 |
| **[`windows-explorer-refresh-fix`](https://github.com/Simiely/windows-explorer-refresh-fix)** | 修下载后资源管理器不自动刷新（要按 F5） | 脚本 | [`v1.0.0`](https://github.com/Simiely/windows-explorer-refresh-fix/releases/latest) | 2026-07-11 |
| **[`edge-multi-account-cookie`](https://github.com/Simiely/edge-multi-account-cookie)** | Edge 多账号 Cookie 切换 | JavaScript | [`v2.11.11`](https://github.com/Simiely/edge-multi-account-cookie/releases/latest) | 2026-08-24 |

### 🧰 本地小工具

| 工具 | 说明 | 技术栈 | 最新版本 | 最近更新 |
|---|---|---|---|---|
| **[`dirmap`](https://github.com/Simiely/dirmap)** | 把文件夹生成可编辑表格（动态列 / 标签 / 排序） | HTML · JS | — | 2026-10-08 |
| **[`TopoGun3-Chinese-Localization`](https://github.com/Simiely/TopoGun3-Chinese-Localization)** | TopoGun 3 简体中文汉化包（GLSL 着色器 + PowerShell 安装脚本） | GLSL · PS1 | — | 2026-08-03 |

### 🎬 3D / 渲染生态（PC 端）

| 工具 | 说明 | 技术栈 | 最新版本 | 最近更新 |
|---|---|---|---|---|
| **[`blender-render-console`](https://github.com/Simiely/blender-render-console)** | 无头调 Blender 渲染控制台：实时进度 / 预计结束时间 / 崩溃自动续跑 | Python · tkinter | [`v0.8.0`](https://github.com/Simiely/blender-render-console/releases/latest) | 2026-10-06 |
| **[`oc-plugin-activator`](https://github.com/Simiely/oc-plugin-activator)** | 一键清空 OctaneRender 缓存 + 部署资源（Cinema 4D / OC 用） | Python · tkinter → exe | [`v1.0`](https://github.com/Simiely/oc-plugin-activator/releases/latest) | 2026-06-23 |

> 它们名字带 `blender-` / `oc-`，但**都是跑在 PC 上的独立程序，不是宿主软件的插件** ——
> Blender 插件见 [`blender-addons`](https://github.com/Simiely/blender-addons)，
> C4D 插件见 [`c4d-tools`](https://github.com/Simiely/c4d-tools)。

## 说明

- **一个工具一个仓库**：源码、Issue、Releases（exe 下载）都在各自仓库；
- 本仓库只负责**索引与导航**，新增工具＝在这里加一行；
- **为什么 PC 工具不做 monorepo、也不归档**：工具靠 **GitHub Releases 分发 exe**、**发版频繁**（单个工具 10~12 个 Release），而仓库一旦归档就**完全只读、Releases 也发不了**，所以统一为「各仓库独立 + 本仓做索引」。
  （**插件类不同**：插件小、发版少 → 由总管仓库装代码、成员仓归档，见 [`ae-tools`](https://github.com/Simiely/ae-tools) · [`blender-addons`](https://github.com/Simiely/blender-addons) · [`c4d-tools`](https://github.com/Simiely/c4d-tools)。）
- 文档规范遵循 [knowledge-base 单项目规范](https://github.com/Simiely/knowledge-base)。

## 相关仓库

- 其它领域的总管仓库：[`ae-tools`](https://github.com/Simiely/ae-tools)（After Effects）· [`blender-addons`](https://github.com/Simiely/blender-addons)（Blender 插件）· [`c4d-tools`](https://github.com/Simiely/c4d-tools)（Cinema 4D 插件）
