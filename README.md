# pc-tools

个人 **Windows / PC 工具集**（monorepo）—— 所有公开桌面工具的**唯一源码源**。
仿 [**ae-tools**](https://github.com/Simiely/ae-tools) / [**c4d-tools**](https://github.com/Simiely/c4d-tools) 的标准维护：一个仓库管全部、四件套文档、按形态分目录。

## 目录结构

```
pc-tools/
├─ README.md · AGENTS.md · DEVELOPMENT.md · CHANGELOG.md   四件套文档
├─ apps/        独立程序（有 GUI / 常驻 / 需构建）
├─ scripts/     轻量脚本类（一次性修复 / 小工具）
├─ web/         本地 HTML 小工具
├─ tips/        使用技巧知识库
└─ releases/    发行包归档
```

## 工具总览

| 工具 | 位置 | 技术 | 说明 | 原独立仓库 |
|---|---|---|---|---|
| 窗口深色蒙版 WindowTinter | [`apps/WindowTinter/`](./apps/WindowTinter) | C# (WinForms) | 给任意窗口叠深色半透明蒙版，切后台自动半透明 | `Simiely/WindowTinter` |
| 多用户剪贴板 clipboard-tool | [`apps/clipboard-tool/`](./apps/clipboard-tool) | C# (WPF) + Node | 粘贴即存 / 拼音搜索 / 标签 / WebDAV 同步 | `Simiely/clipboard-tool` |
| 资源管理器白条修复 | [`scripts/ExplorerBlurMica-whitebar-fix/`](./scripts/ExplorerBlurMica-whitebar-fix) | 脚本 | 修 ExplorerBlurMica 拉起下载目录时的底部白条 | `Simiely/ExplorerBlurMica-whitebar-fix` |
| 资源管理器刷新修复 | [`scripts/windows-explorer-refresh-fix/`](./scripts/windows-explorer-refresh-fix) | 脚本 | 修下载后资源管理器不自动刷新（要按 F5） | `Simiely/windows-explorer-refresh-fix` |
| Edge 多账号 Cookie 切换 | [`scripts/edge-multi-account-cookie/`](./scripts/edge-multi-account-cookie) | JS | Edge 多账号 cookie 切换 | `Simiely/edge-multi-account-cookie` |
| 目录映射表 dirmap | [`web/dirmap/`](./web/dirmap) | HTML/JS | 把文件夹生成可编辑表格（动态列 / 标签 / 排序） | `Simiely/dirmap` |

## 构建 / 运行

各工具技术栈不同（C# / Python / JS / HTML），具体构建与运行方式见**各自目录内的 README**。

## 文档规范

按 [knowledge-base](https://github.com/Simiely/knowledge-base) 的单项目规范维护四件套。

## 相关仓库

本仓库为下列工具的**唯一维护处**，原独立仓库内容均已并入、原仓库归档只读。
（例外：`MonitorBrightness` 仍在原仓库 [`Simiely/MonitorBrightness`](https://github.com/Simiely/MonitorBrightness) 活跃开发中、暂未纳入本仓；`gh-latency` 内容未成形、`MultiSwitch` 属私有，均**未并入**。）
