# CHANGELOG.md · 版本记录

> 项目无正式版本号，按提交里程碑记录。

## 里程碑

- **事件驱动版完成**：`explorer-blur-fix-event.ps1`（SetWinEventHook 监听新窗口，零轮询）+ `run-nudge-event.bat`——替代旧轮询方案
- **旧轮询版归档**：`legacy/explorer-blur-fix-nudge.ps1` + `run-nudge.bat`（400ms 轮询，已弃用）
- **vendor 收录**：ExplorerBlurMica 原版（含 minhook、register/uninstall 脚本）
- **文档完善**：README + DEV_README（现象→修复完整推导，含方案层级对比）+ 使用方法与注意事项
- **在线预览**：README 加 GitHub Pages 预览徽章（index.html 落地页）

## 备注

- 无版本 tag / Release；交付物为 PowerShell 脚本 + 排查文档
- 已知限制：ExplorerBlurMica 公开仓库无完整引擎源码（MToolBox 私有），改 DLL 治本方案锁死等上游
