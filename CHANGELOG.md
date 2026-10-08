# CHANGELOG.md · 仓库级变更

## 仓库 · 2026-10-08 · **由 monorepo 转为索引仓库**

- **删除全部工具代码**：`apps/`（202 文件）、`scripts/`（62 文件）、`web/`（9 文件）、`tips/`、`releases/`，
  合计 **275 个文件**
- 四件套改写为**索引性质**（README 变为工具索引表；DEVELOPMENT 说明「本仓不放代码」）
- **6 个原仓库解冻**（撤销归档）并剥离「已并入 pc-tools」横幅，恢复**独立开发与发版**：
  `WindowTinter` · `clipboard-tool` · `ExplorerBlurMica-whitebar-fix` ·
  `windows-explorer-refresh-fix` · `edge-multi-account-cookie` · `dirmap`
- **原因**：GitHub 仓库归档后 **releases / tags 亦为只读**，无法发布新版本。
  改由「**各工具仓库独立维护 + 本仓库做索引**」，源码只保留一份。

## 仓库 · 2026-10-08 · （已撤销）

- ~~建库 `pc-tools`（monorepo），并入 6 个公开工具：`apps/WindowTinter`、`apps/clipboard-tool`、
  `scripts/ExplorerBlurMica-whitebar-fix`、`scripts/windows-explorer-refresh-fix`、
  `scripts/edge-multi-account-cookie`、`web/dirmap`；原仓库加横幅并归档~~
  → **该方案已于同日撤销**（见上一条），文件已全部退回各自仓库。
