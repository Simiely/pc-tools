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

## 说明

- **一个工具一个仓库**：源码、Issue、Releases（exe 下载）都在各自仓库；
- 本仓库只负责**索引与导航**，新增工具＝在这里加一行；
- 为什么不做 monorepo / 不归档：工具大多通过 **GitHub Releases 分发 exe**，而仓库一旦归档就**完全只读、Releases 也发不了**。所以统一改为「各仓库独立 + 本仓做索引」。
- 文档规范遵循 [knowledge-base 单项目规范](https://github.com/Simiely/knowledge-base)。

## 相关仓库

- 其它领域的工具索引：[`ae-tools`](https://github.com/Simiely/ae-tools)（After Effects）· [`blender-addons`](https://github.com/Simiely/blender-addons)（Blender）· [`c4d-tools`](https://github.com/Simiely/c4d-tools)（Cinema 4D）
