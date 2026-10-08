# 剪贴板 📋

多用户剪贴板管理工具（tools-center 平台工具，Node 零依赖）。暗黑新拟态（Neumorphism）界面，黑金奢华配色（酒红金 · v0.6.2+），首页极简墙布局（v0.6.3+），主页面双行工具栏（v0.6.4+），卡片系统全量重构（v0.6.5+：三区骨架 + 类型专属卡片 + 富文本左右对比分栏），弹窗全量重设计（v0.6.6+：密码/数据管理/存入/编辑 四弹窗），WebDAV 远端统一子目录（v0.6.7+：workbuddy/剪贴板/），富文本复制链路定稿（v0.6.9+：存入内联化 + xmlns 识别 Word 来源，粘贴 Word 保留完整格式），细节审查修复批（v0.6.11+：归档去重防膨胀/损坏数据不崩溃/导入容错/富文本编辑同步），富文本链路修复批（v0.6.12+：Word 私有属性字符串级保真/body 文档级属性保留/卡片分栏回归+取消渲染预览），归档完整备份与可删可恢复 + 一键灰度配色 + 用户名修改（v0.6.13+）。

## 功能

- **单一万能入口**：粘贴自动识别（URL→链接 / 其他→文本）、拖放/选择文件→文件条目、Ctrl+V 粘贴图片/文件；检测到复制内容自动弹出大窗口
- **一键复制 / 双击编辑**：文本/链接复制内容，图片点击直接复制到系统剪贴板（ClipboardItem），其他文件点击下载
- **富文本双格式复制（v0.6.0，卡片分栏 v0.6.12）**：从网页/Word 复制带格式内容 → 存入时自动检测 `text/html`，卡片内容区**左右分栏**（顶部提示「T 普通文本 | ✦ 富文本」）——点左栏复制纯文本，点右栏粘贴到 Word/飞书保留格式；Word 私有属性（mso-* / tab-interval 等）与文档级设置字符串级保真（v0.6.12）；**编辑弹窗「清除格式」**（v0.6.13+）——不需要格式的文本一键转纯文本，复制不再带格式
- **智能排序**：星标置顶 → 使用次数优先 → 最近更新（点击次数越高越靠前，严格按次数排序）
- **拼音首字母搜索**：内置 3755 常用字映射表，`sf` 可搜到"身份"（注：拼音匹配仅前端搜索框输入生效；后端 `?q=` 查询为子串匹配）
- **标签体系**：点选已有标签 + 输入新建，列表标签过滤；**标签多时自动换行全部可见**（v0.6.13+）；筛选的标签下无内容时自动回到全部（v0.6.13+）
- **多用户（双名模型 v0.6.13+）**：账号名（创建后不可变，跨设备迁移/同步的身份键）+ 显示名（可随时改，仅影响展示）；无密码零摩擦进入 / 可选密码锁，会话持久化（重启不掉线，30 天有效）
- **WebDAV 备份同步**：单向全量备份 + 双向合并同步（墓碑机制防删除复活、全部清空不传播删除、定时自动同步默认 12h、可选同步文件实体、地址留空默认局域网服务器）；快照按账号名寻址——**新设备创建相同账号名 → 配置 WebDAV → 同步即拉回全部数据（设备迁移零配置）**
- **滚动归档**：活跃区上限 500 条/用户，超出自动移入归档（零丢失），「含归档」可搜历史；**归档参与 WebDAV 完整备份**（v0.6.13+，远端快照含归档）；编辑弹窗可手动归档、归档卡片可 ↺ 恢复 / ✕ 删除（删除同步传播）
- **本地备份**：一键导出全部（含归档）JSON / 导入合并（同 id 取新），不依赖 WebDAV
- **URL 自动清理**：保存链接自动剔除 UTM 等追踪参数
- **标签管理**：设置里重命名 / 删除标签（跨全部条目含归档同步生效）
- **JSON 格式化预览**：文本条目是 JSON 时一键美化查看 / 复制 / 覆盖保存
- **一键灰度配色（v0.6.13）**：顶栏 ◐ 一键切换彩色 / 无饱和度（灰度）配色，localStorage 记忆保持
- **安全**：UUID 白名单防路径穿越、原子写、登录限流、上传黑名单（拒可执行/脚本类）、下载强制 attachment

## 技术栈

- 后端：Node.js（内置 `http` + `fetch`，零第三方依赖）
- 前端：原生 HTML/JS（Alpine 式轻量、无框架），暗黑新拟态双阴影浮雕风格（设计令牌集中在 `:root`）
- 存储：JSON 文件（原子写），WebDAV 用 HTTP 协议直连
- **exe 桌面版（规划中，`clipboard-exe/`）**：C# WPF net9.0-windows 原生重写，逻辑与 Web 版对齐（排序/合并/清理规则一致）、数据 JSON 互导；框架依赖单文件发布（~1-3MB，需装 .NET 9 Desktop Runtime；方案见 `docs/exe-wpf-plan.md`）

## 运行

```bash
node server.mjs [port]        # 默认 8130
# 数据目录默认 ./.data（可用 CAP_STORAGE_DIR 覆盖）
```

## 测试

```bash
# 冒烟测试（需先启动独立实例，勿对真实数据目录跑）
CAP_STORAGE_DIR=/tmp/clip-test PORT=8131 node server.mjs 8131
TEST_PORT=8131 node scripts/smoke-test.mjs

# WebDAV 端到端（需再起 mock WebDAV）
node scripts/mock-webdav.mjs 8180 /tmp/mock-webdav
node scripts/test-webdav-sync.mjs
node scripts/test-auto-sync.mjs

# 富文本 html 字段单测（v0.6.0，无需起服务，独立数据目录）
node scripts/test-html-field.mjs
```

> Windows 提示：`/tmp/...` 是类 Unix 路径写法；Windows 下请用绝对路径
> （如 `C:/Temp/clip-test`、`C:/Temp/mock-webdav`），数据目录同样传给
> `TEST_DATA_DIR` 保持一致（`TEST_DATA_DIR=C:/Temp/clipboard-test node scripts/test-webdav-sync.mjs`）。

## 目录结构

```
server.mjs          # 入口：HTTP 装配 + 路由 + 静态服务 + 过期清扫 + 自动同步定时器（启动打印 v版本 (commit)）
manifest.json       # tools-center 平台声明
package.json        # 工程元数据：type:module + Node>=22.7 + npm scripts（start/smoke/test:webdav/test:auto-sync/test）
lib/core/           # 纯业务逻辑（store/clips/users/files/webdav + clips 子域拆分）
lib/routes/         # 路由薄层 + 会话中间件
public/index.html   # 单文件前端（暗黑新拟态，设计令牌在 :root）
scripts/            # 测试与工具脚本（mock WebDAV、冒烟、集成、html 字段单测、复杂度测量）
delivery/           # 交付形态（本地服务版 start.cmd / 平台版 / 服务器版 / exe 版，总览见 delivery/README.md）
clipboard-exe/      # exe 桌面版（C# WPF，规划中，方案见 docs/exe-wpf-plan.md）
```

## 文档

- [开发文档 DEVELOPMENT.md](DEVELOPMENT.md) — 架构说明与关键问题记录
- [变更日志 CHANGELOG.md](CHANGELOG.md) — 版本记录
- [AI 项目规则 AGENTS.md](AGENTS.md) — 技术栈 / 关键坑 / 约定
- [交付形态 delivery/README.md](delivery/README.md) — 本地服务版 / 平台版 / 服务器版 / exe 版四形态
- [exe 版开发方案 docs/exe-wpf-plan.md](docs/exe-wpf-plan.md) — WPF 重写方案（还原度确认 / 技术选型 / 里程碑）
- [发布规范 docs/发布规范.md](docs/发布规范.md) — 版本号 / 产物命名 / 打包 / 发布 / 部署全流程

