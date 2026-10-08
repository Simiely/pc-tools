# AGENTS.md · 项目规则

> 📌 **文档基线**：2026-10-08（commit `81f192f`）本仓库转为**索引仓库**
> **更新索引后，请更新此行**（日期 + 新 commit hash），并在 CHANGELOG 追加。

## 本仓库定位（重要）
- `pc-tools` 是**索引仓库**：**只放文档，不放代码**。
- 各工具的源码 / 构建 / 发行都在**各自的独立仓库**里，本仓库只维护一张索引表。

## 约定
- 🔴 **不要**把任何工具源码、构建产物、发行包提交进本仓库
- 🔴 **不要**归档工具仓库 —— 归档后 **Releases 变只读，就发不了新版 exe 了**
- 新增工具：README「工具一览」按分类加一行 → CHANGELOG 加节
- 表格里的**最新版本 / 最近更新**取自各仓库的 Releases 与 push 时间，改版后记得同步

## 相关
- 同类索引仓库：[`ae-tools`](https://github.com/Simiely/ae-tools) · [`blender-addons`](https://github.com/Simiely/blender-addons) · [`c4d-tools`](https://github.com/Simiely/c4d-tools)
