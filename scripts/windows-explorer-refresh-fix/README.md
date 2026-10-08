# Windows 资源管理器自动刷新修复工具

下载 / 拷贝文件后，资源管理器不立即显示新文件，必须手动按 F5 或右键刷新？本项目一键修复。

## 问题
Windows 资源管理器在文件变更后不自动刷新视图，表现为「下载完文件，文件夹里看不到，按一下 F5 才出现」。

## 适用环境
- Windows 10 / 11（已在 **Windows 10 IoT Enterprise LTSC 2024, Build 26100 x64** 实测）
- **无需管理员权限**

## 根因（简要）
最常见的元凶是：文件夹视图缓存（Shell Bags / BagMRU）损坏、缩略图/图标缓存损坏、或资源管理器历史（Quick Access）损坏。本工具用标准修复组合一次性处理：重置 Shell Bags + 清除图标/缩略图缓存 + 清除历史 + 写入 `AlwaysRefresh=1` 强制外壳后台刷新。

> 说明：社区里常把这类问题甩锅给「映射网络盘」，但本机实测（ping 1ms、瞬连）已排除该假设。详见 `explorer-refresh-guide.md` 与 `DEVELOPMENT.md`。

## 文件说明
| 文件 | 作用 |
|---|---|
| `reset-explorer-view.ps1` | 核心修复脚本（纯 ASCII，避免控制台乱码） |
| `run-explorer-fix.bat` | 一键启动器，双击即可运行上面的脚本 |
| `explorer-refresh-guide.md` | 完整图文指南（手动分步、管理员 SFC/DISM、ShellExView 排查） |
| `DEVELOPMENT.md` | 开发 / 排障笔记，记录关键问题与可复用经验 |

## 文档索引

| 文档 | 给谁看 | 内容 |
|---|---|---|
| [`AGENTS.md`](./AGENTS.md) | AI / 未来的你 | 技术要点、关键坑（编码/测量）、常用命令 |
| [`DEVELOPMENT.md`](./DEVELOPMENT.md) | 开发者 | 排障笔记：误诊教训、性能影响交叉验证 |
| [`explorer-refresh-guide.md`](./explorer-refresh-guide.md) | 用户 | 完整图文指南 |

## 使用方法
**方式一（推荐，最省事）**
直接双击 `run-explorer-fix.bat`。它会自动以 `-ExecutionPolicy Bypass` 运行脚本，结束后按任意键关闭窗口。运行期间桌面 / 任务栏会闪一下（资源管理器重启），属正常现象。

**方式二（PowerShell 命令）**
```powershell
powershell -ExecutionPolicy Bypass -File "D:\path\to\reset-explorer-view.ps1"
```

**方式三**
右键 `reset-explorer-view.ps1` →「使用 PowerShell 运行」。

## 验证
运行后下载一个文件，应当无需按 F5 就自动出现。若仍须手动刷新，按 `explorer-refresh-guide.md` 的进阶步骤排查（系统文件损坏 SFC/DISM、第三方壳扩展 ShellExView 等）。

## 性能影响
基本无持续影响。修复是「恢复正常的自动刷新 + 清掉损坏缓存」，成本全是一次性的：
- `AlwaysRefresh=1` 是**事件驱动**刷新（文件变了才刷），非死循环，开销可忽略；
- 清缩略图 / 图标缓存后，**首次**打开图片 / 视频多的文件夹会略慢几秒（重建缩略图），之后自动恢复并更快；SSD 上重建是秒级；
- 重置 Shell Bags 仅删后首次并发浏览有短时 CPU 升高，之后正常。

> 注意：**不要频繁反复运行**脚本——频繁清 + 重建缓存会加剧 SSD 写放大 / 磨损，一次就够。实测运行后无性能退化。

## 安全与可逆性
- 仅操作**当前用户（HKCU + 用户配置文件）**的缓存与视图状态，**不删除任何真实文件**。
- 想关闭强制刷新：删除 `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced` 下的 `AlwaysRefresh`（或设为 0），重启资源管理器即可。
- 脚本**运行一次即可**，无需频繁反复执行（原因见上「性能影响」）。

## 仓库
https://github.com/Simiely/windows-explorer-refresh-fix
