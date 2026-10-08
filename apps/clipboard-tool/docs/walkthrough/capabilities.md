# capabilities.md · 能力清单(针对性走查基线)

> 日期:2026-08-15 · 基线:v0.6.0(HEAD `6ca91b3`)· 方法:全量代码阅读核对
> 用途:针对性走查(缠绕点验证)素材的 C-ID 引用源

## 页面/入口(C- 前缀)

| C-ID | 能力 | 入口(文件:行号) |
|---|---|---|
| C-01 | 用户选择页(列表/新建/编辑模式/删除用户) | app.js `renderUserSelect` L273 / `setUserEditMode` L231 |
| C-02 | 主页面(顶栏/存入入口/工具栏/类型Tab/标签栏/列表) | app.js `renderMain` L436 |
| C-03 | 存入大弹窗(万能入口:粘贴识别/拖放/文件/标签/过期) | app.js `openPasteModal` L1002 |
| C-04 | 编辑弹窗(含重复提示 dup) | app.js `openEditModal` L1188 |
| C-05 | 密码管理弹窗 | app.js `openPasswordModal` L1251 |
| C-06 | 数据管理弹窗(缩放步长/WebDAV/备份/清空/删号) | app.js `openDataModal` L1281 |
| C-07 | 标签管理弹窗(重命名/删除) | app.js `openTagManageModal` L546 |
| C-08 | JSON 格式化预览弹窗 | app.js `openJsonPreview` L970 |
| C-09 | 图片 hover 预览浮层(缩放/拖拽) | app.js `bindImageHoverPreview` L809 |
| C-10 | 图片大图预览弹窗 | app.js `openImagePreview` L941 |

## 数据域(D- 前缀,后端)

| C-ID | 能力 | 位置 |
|---|---|---|
| D-01 | 条目 CRUD(建/改/删/清空/归档闭环) | lib/core/clips.js `createClip` L334 / `updateClip` / `deleteClip` L406 / `clearAllClips` / `archiveClip` L440 / `unarchiveClip` L453(注:v0.6.13 已删未使用的 `getClip`/`readFileBuffer`,行号以 main-flow.md 为准) |
| D-02 | 复制计数 + 星标 | clips.js `bumpCopy` L419 / `togglePin` L431 |
| D-03 | 列表/搜索/标签过滤(后端 q/tag/archived) | clips.js `listClips` L239 / `listTags` L262 |
| D-04 | 排序(pinned→copyCount→updatedAt，v0.6.16 移除归拢) | clips.js `sortClips` L115 |
| D-05 | 滚动归档(活跃区 500 上限) | clips.js `rollToArchive` L42 / `loadArchive` L25 |
| D-06 | 墓碑机制(删除传播) | clips.js L54-94 `loadTombstones/saveTombstones/recordTombstone/pruneTombstones/clearTombstones` |
| D-07 | 标签重命名/删除(跨归档) | clips.js `renameTag` L272 / `deleteTag` L295 |
| D-08 | 导出/导入(合并去重) | clips.js `exportClips` L453 / `importClips` L462 |
| D-09 | 过期清扫(后台 60s) | clips.js `sweepExpired` L517 |
| D-10 | URL 自动清理(UTM) | clips.js `cleanUrl` L185 |
| D-11 | 用户 CRUD + 密码 scrypt + 会话 token + 限流 | lib/core/users.js `createUser` L171 / `login` L192 / `changePassword` L202 / `deleteUser` L219 / `createToken` L104 / `verifyToken` L114 / `isLoginBlocked` L144 |
| D-12 | 文件上传/下载(黑名单+attachment) | lib/core/files.js `saveFile` L15 / `getFilePath` L41 / `deleteFile` L52 |
| D-13 | WebDAV 配置/测试/一键同步/自动同步 | lib/core/webdav.js `saveSyncConfig` L42 / `testConnection` L110 / `runSync` L240 / `runAutoSync` L285 |
| D-14 | WebDAV 合并算法(墓碑裁决) | webdav.js `mergeSnapshots` L169 |
| D-15 | 会话/清扫/自动同步 3 个后台定时器 | server.mjs L27-41 |
| D-16 | 静态资源内存缓存 + 路由分发 | server.mjs L21-24 / L43-74 |

## 交互/前端域(F- 前缀)

| C-ID | 能力 | 位置 |
|---|---|---|
| F-01 | 前端即时过滤(搜索/标签/类型/拼音) | app.js `renderList` L605 |
| F-02 | 拼音首字母搜索(3755 字表) | app.js `PY_GROUPS` L598 / `strToPy` L602 |
| F-03 | 卡片工厂(类型按钮装配) | app.js `clipCard` L779 + make*Btn L641-720 |
| F-04 | 单击复制/双击编辑 | app.js `handleCardClick` L751 / `card.ondblclick` L804 |
| F-05 | 复制来源抑制(800ms) | app.js `suppressAutoPasteUntil` L13 / L705 / L752 / L1475 |
| F-06 | 富文本双格式复制(🅡) | app.js `copyRich` L114 / `makeRichBtn` L699 |
| F-07 | 富文本暂存 pendingHtml | app.js `pendingHtml` L17 / autoFill L1149-1163 / save L1133-1136 |
| F-08 | 剪贴板监听自动弹窗 | app.js boot `clipboardchange` L1473-1491 |
| F-09 | 会话恢复(LS cur + 显式 token) | app.js boot L1495-1504 |
| F-10 | 重复检测纯函数 | app.js `findDuplicateClip` L213 |
| F-11 | 自定义确认弹窗 | app.js `askConfirm` L82 / `askConfirmP` L97 |
| F-12 | 防连点锁 guard | app.js `guard` L68 |
| F-13 | 删除卡片(makeDeleteBtn) | app.js L678-689 |

## 平台/工程(E- 前缀)

| C-ID | 能力 | 位置 |
|---|---|---|
| E-01 | `__BASE__` 子路径挂载 | app.js L7 |
| E-02 | 静态缓存 + 安全头 | server.mjs L21-24 / helpers.js `sendJson` L21 |
| E-03 | 会话中间件(Header + ?token=) | lib/routes/helpers.js `requireAuth` L8 |
| E-04 | multipart 解析(零依赖) | helpers.js `parseMultipart` L70 |
| E-05 | 路由分段匹配器 | lib/routes/index.js `matchRoute` L35 |
| E-06 | 原子写 JSON + UUID 白名单 | lib/core/store.js `writeJson` L18 / `assertId` L26 |
