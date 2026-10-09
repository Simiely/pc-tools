# CHANGELOG.md · 仓库级变更

## 仓库 · 2026-10-09 · 索引新增 TopoGun3-Chinese-Localization

- 索引「🧰 本地小工具」新增 [`TopoGun3-Chinese-Localization`](https://github.com/Simiely/TopoGun3-Chinese-Localization)
  —— TopoGun 3 简体中文汉化包（PC 端安装）
- 「相关仓库」区补充新索引入口：[`design-tools`](https://github.com/Simiely/design-tools) · [`mobile-apps`](https://github.com/Simiely/mobile-apps) · [`tech-guides`](https://github.com/Simiely/tech-guides)

## 仓库 · 2026-10-09 · 索引新增 gh-fetcher

- 索引新增 [`gh-fetcher`](https://github.com/Simiely/gh-fetcher)（v1.0.0）—— GitHub 仓库 / Release 浏览下载器
- 「不归档」的理由**明确限定为 PC 工具**（插件类仓库已改为「总管装代码 + 成员仓归档」）
- 「相关仓库」措辞：「工具索引」→「总管仓库」

---

## 仓库 · 2026-10-08 · 索引新增「oc-plugin-activator」

- 索引新增 [`oc-plugin-activator`](https://github.com/Simiely/oc-plugin-activator)（v1.0）
  —— OctaneRender 缓存清理 / 资源部署的 **Windows 工具**（不是 C4D 插件），因此归入本索引
- 「Blender 生态（PC 端）」分类扩为「**3D / 渲染生态（PC 端）**」，容纳两个宿主软件的外围 PC 工具

---

## 仓库 · 2026-10-08 · 索引新增「Blender 生态（PC 端）」

- 索引表新增一行 [`blender-render-console`](https://github.com/Simiely/blender-render-console)（v0.8.0）。
  它名字带 `blender-`，但**是跑在 PC 上的独立程序、不是 Blender 插件**（插件见 `blender-addons`），
  因此归入本索引，而非插件集

---

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
