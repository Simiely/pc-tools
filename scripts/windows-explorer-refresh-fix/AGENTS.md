# AGENTS.md · 项目规则

> 给 AI / 未来的你：只记代码里看不出的关键信息。详细排障笔记见 [DEVELOPMENT.md](DEVELOPMENT.md)，图文指南见 [explorer-refresh-guide.md](explorer-refresh-guide.md)。

## 项目是什么

一键修复 Windows 资源管理器「下载/拷贝文件后不自动刷新、必须手动 F5」的问题。核心是 `reset-explorer-view.ps1`（PowerShell）+ `run-explorer-fix.bat` 启动器。Windows 10/11 实测可用（LTSC 2024 Build 26100），**无需管理员权限**。

## 技术要点

- 修复组合：重置 Shell Bags/BagMRU → 清缩略图/图标缓存 → 清历史/Quick Access → 写 `AlwaysRefresh=1` → 重启 Explorer
- 只操作 HKCU + 用户配置文件，不删真实文件，可逆（删 AlwaysRefresh 即恢复）
- `AlwaysRefresh=1` 是事件驱动刷新（文件变了才刷），非死循环，开销≈0

## 关键坑

1. **PowerShell 5.1 按系统 ANSI 代码页（中文 Windows = GBK）解析 `.ps1`**，无 UTF-8 BOM 的中文会乱码；`chcp 65001` 只改控制台输出代码页救不了——脚本内输出全部用**纯英文 ASCII**
2. **`.bat` 启动器用相对路径** `%~dp0script.ps1`，不写死绝对路径（泄露目录结构 + 换机器失效）
3. **排障先测量再下结论**：别信"网络盘拖垮刷新"的通说，先 ping / Test-Path 计时验证（实测已排除该假设）
4. 不要频繁反复运行脚本——频繁清 + 重建缓存加剧 SSD 写放大，一次就够

## 常用命令

```bash
# 一键修复（推荐）
run-explorer-fix.bat
# 或 PowerShell
powershell -ExecutionPolicy Bypass -File "path\to\reset-explorer-view.ps1"
```

## 文档

- [DEVELOPMENT.md](DEVELOPMENT.md)：开发/排障笔记（编码坑、误诊教训、性能影响交叉验证）
- [explorer-refresh-guide.md](explorer-refresh-guide.md)：完整图文指南（手动分步、SFC/DISM、ShellExView）
