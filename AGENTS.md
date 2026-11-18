# AGENTS.md · 项目规则

> 📌 **文档基线**：2026-11-18 接入 repo-kit 机器层（`link-only`，kit 2.8.0 / spec 2.1.3）
> **更新索引后，请更新此行**（日期 + 新 commit hash），并在 CHANGELOG 追加。

## 本仓库定位（重要）
- `pc-tools` 是**索引仓库**：**只放文档，不放代码**。
- 各工具的源码 / 构建 / 发行都在**各自的独立仓库**里，本仓库只维护一张索引表。
- **唯一的例外**：`tools/repo-kit/` 是规范分发的校验器（由 `tools/update-repo-kit.ps1` 安装、`schema/kit.lock.json` 锁哈希），不是任何工具的源码，不属于上面那条禁令。

## 约定
- 🔴 **不要**把任何工具源码、构建产物、发行包提交进本仓库
- 🔴 **不要**归档工具仓库 —— 归档后 **Releases 变只读，就发不了新版 exe 了**
- 新增工具：README「工具一览」按分类加一行 → CHANGELOG 加节
- 表格里的**最新版本 / 最近更新**取自各仓库的 Releases 与 push 时间，改版后记得同步

## 机器层（只校验，不产生内容）
- 检查：`python tools/repo-kit/check_repo.py --root .`（链接 / 锚点 / 版本锁之外的不变量）
- 锁校验：`pwsh tools/update-repo-kit.ps1 -Check`
- 配置在 `schema/taxonomy.json`（`link-only`：不读 frontmatter、不生成任何文件）
- 新增/改表后**不需要**动这里；只有升级 kit（`update-repo-kit.ps1 -Source <kit源>`）才会变

## 相关
- 同领域总管仓库：[`ae-tools`](https://github.com/Simiely/ae-tools) · [`blender-addons`](https://github.com/Simiely/blender-addons) · [`c4d-tools`](https://github.com/Simiely/c4d-tools)
