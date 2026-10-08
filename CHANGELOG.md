# CHANGELOG.md · 仓库级变更

## 仓库 · 2026-10-08

**建库**：建 Windows/PC 工具 monorepo **`pc-tools`**，成为这些公开工具的唯一源码源。

并入 7 个原独立仓库（按形态分目录）：

| 模块 | 来自原仓库 |
|---|---|
| `apps/WindowTinter/` | `Simiely/WindowTinter` |
| `apps/clipboard-tool/` | `Simiely/clipboard-tool` |
| `apps/MonitorBrightness/` | `Simiely/MonitorBrightness` |
| `scripts/ExplorerBlurMica-whitebar-fix/` | `Simiely/ExplorerBlurMica-whitebar-fix` |
| `scripts/windows-explorer-refresh-fix/` | `Simiely/windows-explorer-refresh-fix` |
| `scripts/edge-multi-account-cookie/` | `Simiely/edge-multi-account-cookie` |
| `web/dirmap/` | `Simiely/dirmap` |

- 原仓库内容全部搬入，**不含**其 `.github/workflows`（不启用 CI/Pages）
- 7 个原仓库随后**归档只读**，README 顶部指向本仓
- `gh-latency` 暂缓、`MultiSwitch` 私有，均未并入
- 各工具按上表顺序逐个并入，**每个工具一个提交**
