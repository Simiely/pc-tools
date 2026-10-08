# DEVELOPMENT.md · 仓库说明与索引

## 项目概览

`pc-tools` 是个人 Windows/PC 工具集的 **monorepo**：所有**公开**桌面工具的唯一源码源。

演进：**2026-10-08 建库** —— 把原先分散的 7 个独立公开仓库整合进来。

## 结构

```
pc-tools/
├─ apps/       独立程序（GUI / 常驻 / 需构建）
├─ scripts/    轻量脚本类
├─ web/        本地 HTML 小工具
├─ tips/       技巧知识库（占位）
└─ releases/   发行包归档（占位）
```

## 迁入记录

| 模块 | 来自原仓库 | 类型 |
|---|---|---|
| `apps/WindowTinter/` | `Simiely/WindowTinter` | C# (WinForms) |
| `apps/clipboard-tool/` | `Simiely/clipboard-tool` | C# (WPF) + Node |
| `apps/MonitorBrightness/` | `Simiely/MonitorBrightness` | Python |
| `scripts/ExplorerBlurMica-whitebar-fix/` | `Simiely/ExplorerBlurMica-whitebar-fix` | 脚本 |
| `scripts/windows-explorer-refresh-fix/` | `Simiely/windows-explorer-refresh-fix` | 脚本 |
| `scripts/edge-multi-account-cookie/` | `Simiely/edge-multi-account-cookie` | JS |
| `web/dirmap/` | `Simiely/dirmap` | HTML/JS |

未并入：`gh-latency`（内容未成形，暂缓）、`MultiSwitch`（私有，不并入）。

## 每次改动的动作清单

| 场景 | 动作 |
|---|---|
| 新增工具 | 按形态放 `apps/`/`scripts/`/`web/` → README「工具总览」加行 → CHANGELOG 加节 → DEVELOPMENT 加章节 |
| 发版 | 工具内版本号升级 + `CHANGELOG.md` 加节 |
| 任何提交后 | 更新 `AGENTS.md` 顶部的**文档基线行**（日期 + 新 commit hash） |
