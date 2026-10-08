# 平台版（tools-center 平台托管）

> 状态：✅ **可用**（v0.7.6 起）。接入 tools-center 平台（Simiely/tools-center），作为平台工具之一由平台统一托管。

## 接入原理

tools-center 平台（Docker 镜像 `ghcr.io/simiely/tools-center:main`）扫描 `tools/*/tool.json`，按声明启动每个工具：

- **工具目录**：平台挂载卷 `tools/`（如宿主 `/mnt/usb2/Configs/tools-center/tools`），**新增工具 = 放一个子目录 + tool.json**
- **tool.json**：本目录 `tool.json` 声明 id/name/cmd/port/health/capabilities/dataFiles
- **数据目录**：`capabilities: ["storage"]` → 平台注入 `CAP_STORAGE_DIR`（工具专属目录，随平台 `data/` 挂载持久化）
- **版本指纹**：启动日志打印 `clipboard v0.7.6 (commit)`，实例身份可追溯

## 部署步骤

1. **构建平台版 zip**（发版规则见 `../../docs/发布规范.md`），或直接拷贝程序文件：

```
tools/clipboard/           ← 目标：服务器 tools/ 挂载目录下
├── tool.json              ← 平台接入声明（本目录）
├── server.mjs
├── lib/  public/  scripts/
└── package.json  manifest.json
```

2. **拷贝到服务器**：将整个 `clipboard/` 目录放进 `tools/` 挂载目录（与 note-demo、wb-credits-demo 并列）

3. **平台重扫**：平台支持在线重扫（管理 API 创建/删除工具自动重扫）；手动方式为重启平台容器：

```bash
docker restart tools-center
```

4. **验证**：
   - 平台首页出现「剪贴板」卡片（📋）
   - 点开访问 `http://<平台>:<平台端口>/tool/clipboard/`（平台反代）
   - 平台健康检查 `/health` 通过

## 数据说明

- 数据落在平台注入的 `CAP_STORAGE_DIR`（平台 `data/` 挂载卷），**随平台备份/迁移**
- `dataFiles` 声明（users.json / sessions.json / users / files）供平台存储管理识别与升级保留
- 用户侧 WebDAV 备份照常可用（数据管理 → WebDAV 同步）

## 与本地服务版差异

| 项 | 本地服务版 | 平台版 |
|---|---|---|
| 启动 | `start.cmd` 双击 | 平台自动拉起 + 健康检查 + 崩溃重启 |
| 数据目录 | `./.data` | 平台注入 `CAP_STORAGE_DIR` |
| 访问 | `http://127.0.0.1:8130` | 平台反代 `/tool/clipboard/` |
| 部署 | 任意 Windows/Linux | 服务器 Docker 挂载 |
