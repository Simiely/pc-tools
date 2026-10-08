# 本地服务版（Local Service）

> 当前在产形态。Node.js 零依赖 Web 服务，浏览器访问，多用户本地/局域网使用。

## 快速启动

方式一：双击 `start.cmd`（本目录内），或命令行执行：

```bash
node server.mjs 8130
```

方式二（自定义数据目录/端口，如测试隔离）：

```bash
# Windows PowerShell（避免 Git Bash 路径转换坑）
$env:CAP_STORAGE_DIR = "C:/Temp/clip-test"; node server.mjs 8131
```

## 启动日志（版本指纹）

服务启动时打印版本与 commit，实例身份可追溯（2026-08-27 事故后新增）：

```
clipboard v0.7.6 (256af5c) running on 8130 (data: D:\...\clipboard-tool\.data)
```

排查多实例问题时，先看每个实例日志的 `v版本 (commit)`，确认是不是同一代码版本、同一数据目录。

## 关键参数

| 参数 | 默认 | 说明 |
|---|---|---|
| 端口 | `8130`（argv[2] 或 PORT） | 主服务固定 8130；测试用 8131+ |
| 数据目录 | `./.data` | `CAP_STORAGE_DIR` 可覆盖（平台托管时由平台注入） |
| Node 版本 | ≥ 22.7 | 零第三方依赖 |

## 数据目录布局

```
.data/
├── users.json            用户列表（含 passHash）
├── sessions.json         会话表（30 天 TTL，文件即真相）
├── users/<uid>.json      活跃条目（上限 500，超限滚动进归档）
├── users/<uid>.archive.json      归档
├── users/<uid>.tombstones.json   墓碑（90 天 TTL，WebDAV 传播删除）
├── users/<uid>.webdav.json       WebDAV 配置（含密码）
└── files/<uid>/          文件实体
```

## 运维要点（血泪教训）

1. **同一时刻只允许一个实例用同一个数据目录**——多实例混跑是 2026-08-27 数据清空事故的元凶之一
2. 启动前检查：`netstat -ano | grep 8130` 确认无残留实例；`Get-Process node` 清理遗留进程
3. 定期检查是否有"端口 8132-8139"的遗留测试实例（历史事故：20+ 个实例长期未关）
4. 备份三保险：本地 JSON + WebDAV 快照 + 定期导出（数据管理弹窗「导出全部」）
