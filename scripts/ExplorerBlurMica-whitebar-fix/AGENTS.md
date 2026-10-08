# AGENTS.md · 项目规则

> 给 AI / 未来的你：只记代码里看不出的关键信息。详细排查推导见 [DEVELOPMENT.md](DEVELOPMENT.md)。

## 项目是什么

修复 ExplorerBlurMica（注入式资源管理器模糊工具）在浏览器 `ShellExecute` 拉起的下载目录窗口底部白条问题。核心是 `explorer-blur-fix-event.ps1`（事件驱动 PowerShell）+ `run-nudge-event.bat` 启动器。用户环境：Windows 11、PowerShell FullLanguage、无 AutoHotkey、不碰 C++。

## 技术要点

- **白条根因是首绘竞态**：浏览器拉起的窗口首帧绘制发生在 hook 装好/背景铺到之前，露出默认白底；手动拖动触发 WM_SIZE → 监听器重跑重铺 → 盖对。与 effect 类型无关（换 effect=3 无效）
- **方案**：`SetWinEventHook(EVENT_OBJECT_CREATE)` 事件驱动监听新窗口 → nudge（GetWindowRect + SetWindowPos 触发重绘）——零轮询、CPU 平时 idle，是外部脚本（无 C++）场景的天花板
- 旧方案 `legacy/explorer-blur-fix-nudge.ps1` 为 400ms 轮询，已弃用
- vendor/ 是 ExplorerBlurMica 原版（含 minhook，LGPL）

## 关键坑

1. **公开仓库没有完整实现**：ExplorerBlurMica 只有头文件 + 327 字节 `dllmain.cpp` 空壳，干活函数全 `extern`，实现在私有库 MToolBox——「clone → 改 .cpp → 重编」此路不通，锁死等上游
2. **排查技巧**：对 `extern` 声明保持怀疑，确认实现文件是否在仓库内；用 Contents API（api.github.com）取文件区分「真 404」与「raw 域名网络抖动」
3. **WH_CBT 钩子 vs 事件驱动**：性能基本同量级，WH_CBT 只是时机更早（白条几乎零闪现），但需 C++ DLL；不碰 C++ 时事件驱动是天花板
4. **轮询 vs 事件驱动**：定时轮询有 400ms 轻开销且窗口出现有延迟；事件驱动零轮询

## 常用命令

```bash
# 事件驱动版（推荐）
run-nudge-event.bat
# 旧版轮询（legacy/）
run-nudge.bat
```

## 文档

- [DEVELOPMENT.md](DEVELOPMENT.md)：现象到修复的完整推导（机理、方案层级对比、nudge 实现）
