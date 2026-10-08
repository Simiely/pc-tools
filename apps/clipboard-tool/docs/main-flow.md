# main-flow.md · 主线总览（SSOT）—— 一条剪贴板的生命周期

> **角色**：本项目「主线」单一事实源（SSOT）。回答"系统做什么、数据怎么流动"。
> **方法**：docs-ssot-convergence 三视角 —— 本文 = 主线（用户旅程）；支线（机制怎么工作）见 AGENTS.md / DEVELOPMENT.md / walkthrough 各篇；模块化（怎么实现）见 DEVELOPMENT.md。
> **维护**：改代码后若入口函数/流程变化，同步更新本文；行号会漂移，以函数名为准（行号为 2026-08-26 v0.6.13 快照）。

## 一图流

```
┌─ 系统剪贴板（外部）────────────────────────────────────────────┐
│  复制任意内容：文本 / 富文本(Word·网页) / 链接 / 图片 / 文件          │
└──────────────────────────┬───────────────────────────────────┘
                           │ clipboardchange 事件 / 手动点「＋」
                           ▼
   ① 存入弹窗  openPasteModal(app.js L1247)
      ├─ 自动填入：autoFillPasteModal(L1444) —— 文本优先，其次图片
      │    富文本捕获 text/html → pendingHtml（S1 捕获）
      ├─ 重复检测：checkDuplicate(L1339) —— 内容相似 ≥10 字符 → 提示
      └─ 保存三分支：savePasteContent(L1409)
            文件 → POST /api/files（实体落盘）
            链接 → cleanUrl 去追踪参数
            文本+富文本 → normalizeRichHtml 内联化（Word 私有属性保真）
                           ▼
   ② 后端存储  clips.js
      createClip(L334) → saveClips(L36)
      活跃区 >500 条自动滚动最旧进归档（archive.json）
                           ▼
   ③ 卡片墙渲染  renderMain(app.js L507)
      类型专属卡：clipCard(L929) / makeCardBody(L975)
      图片 hover 预览：bindImageHoverPreview(L1043)
                           ▼
   ④ 用户操作（点卡片/按钮）
      ├─ 左栏复制纯文本：copyText(L102)
      ├─ 右栏复制富文本：copyRich(L199) → buildWordDoc 包装 CF_HTML → execCommandRich
      ├─ 编辑：openEditModal(L1503) —— 类型专属内容区 + 标签 + 归档
      ├─ 归档/恢复/删除：archiveClip / unarchiveClip / deleteClip
      └─ 数据管理：openDataModal(L1694) —— 含 WebDAV 配置 renderWebdavSection(L1831)
                           ▼
   ⑤ WebDAV 同步  webdav.js runSync(L269)（手动/定时 autoSync）
      拉远端 → mergeSnapshots 合并裁决 → 分拣写回 → 上传
      快照 = 活跃区 ∪ 归档区（完整备份）
```

## 分步详解

| 步 | 做什么 | 触发 | 入口（函数 · 文件） |
|---|---|---|---|
| ① 存入 | 捕获剪贴板内容 → 检测类型 → 存库 | 系统复制自动弹窗 / 手动点「＋」 | `openPasteModal` · app.js L1247<br>`autoFillPasteModal` L1444<br>`savePasteContent` L1409 |
| ② 存储 | 建条目；超 500 滚动归档 | ① 保存提交 | `createClip` / `saveClips` · clips.js L334/L36 |
| ③ 展示 | 类型专属卡片 + 操作按钮 | 登录 / 刷新 / 数据变更 | `renderMain` L507 · `clipCard` L929 · `makeCardBody` L975 |
| ④ 操作 | 复制 / 编辑 / 归档 / 恢复 / 删除 | 用户点击 | `copyText` L102 · `copyRich` L199 · `openEditModal` L1503<br>`archiveClip`/`unarchiveClip`/`deleteClip` · clips.js L440/L453/L406 |
| ⑤ 同步 | 双向合并 + 完整备份 | 一键同步 / 定时 / 配置保存 | `runSync` · webdav.js L269 |

## 用户模型与寻址（双名模型 v0.6.13）

```
user = { accountName(不可变身份键), displayName(可变展示名), ... }
```

- **身份键 = accountName**（创建后不可变，仅限英文数字）：WebDAV 远端快照/实体目录寻址、跨设备识别——设备迁移 = 新部署创建相同 accountName → 配置 WebDAV → 同步即拉回全部数据
- **展示 = displayName**（可随时改，仅影响界面/导出文件名）：改显示名**零路径影响**（不触发任何迁移）
- 本地文件（clips/archive/tombstones/webdav.json/files）仍按 `userId`(UUID) 存——**只有远端快照按 accountName 寻址**
- 账号名修改属管理员级一次性操作（`POST /api/users/:id/account-name`）：记 `pendingNameMigrations` 数组，下次同步逐个迁移旧名快照/实体（删除成功才移除，连续改名不丢）

## 关键数据（后端存储布局）

| 数据 | 位置 | 说明 |
|---|---|---|
| 活跃条目 | `store/<uid>/clips.json` | 上限 500，超限滚动进归档 |
| 归档条目 | `store/<uid>/archive.json` | v0.6.13 起参与 WebDAV 完整备份 |
| 墓碑 | `store/<uid>/tombstones.json` | 单独删除的传播记录（90 天 TTL） |
| 文件实体 | `store/<uid>/files/<fileId>.<ext>` | 图片/文件类型条目 |
| 同步配置 | `store/<uid>/webdav.json` | 远端地址 + 凭据（pass 加密） |
| 用户 | `store/users.json` | 含密码哈希/限流表 |
| **远端快照** | `<WebDAV>/workbuddy/剪贴板/clipboard-<accountName>.json` | 按账号名寻址；旧格式 `clipboard-<userId>.json` 首次同步自动并入迁移 |
| **远端实体** | `<WebDAV>/workbuddy/剪贴板/files/<accountName>/` | 按账号名寻址；改名后自动迁移到新名目录 |

## 支线索引（机制细节不在此重复，见指定文档）

| 机制 | 权威文档 |
|---|---|
| WebDAV 同步七铁律（墓碑/清空不传播/双向取最新/上传保护/归档纳入/实体/自动同步） | `webdav.js` 头部注释 + AGENTS.md「WebDAV 同步」 |
| 合并裁决（mergeSnapshots 4 分支） | `scripts/test-merge-snapshot.mjs`（单元测试即契约） |
| 富文本链路（捕获→内联化→存储→包装→写剪贴板） | `docs/walkthrough/富文本复制链路走查.md` |
| 滚动归档 / 归档闭环（归档↔恢复↔删除） | AGENTS.md「滚动归档」 |
| 能力清单（C-ID 表）与走查剧本（S-ID 表） | `docs/walkthrough/capabilities.md` / `scenarios.md` |
