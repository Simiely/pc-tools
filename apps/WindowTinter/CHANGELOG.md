# CHANGELOG.md · 版本记录

> 按版本从新到旧。完整问题记录见 [DEV.md](DEV.md)。

## v5.5.2 — 版本号统一

- 版本号升至 5.5.2（csproj + 代码 fallback，关于页与 Release 一致）

## v3.9.0 — 文档与仓库清理

- 版本号推进 3.9.0；README/DEV.md 文档更新；清理游离文件；确保仓库可直接 dotnet build

## v3.7.0 — 全局/单窗口透明度 + 稳定化

- 全局/单窗口透明度开关 + 目标选中按钮；动态标题子串匹配回退；GDI 位图缓存（HBITMAP 复用）；提权检测气泡；UI 遮挡修复

## v3.6.2 — UI 重构 + CI/CD

- UI 重构（按钮下移 + 状态栏整合）+ GitHub Actions CI/CD（tag 自动发布）+ GitHub Pages 落地页 + 多项稳定性修复

## v3.2.2 — 配置迁移 + 拾取器零闪烁

- 配置路径迁移（%AppData% → exe 同目录）+ 启动透明度恢复 + 后台点击激活 + 拾取器零闪烁方案（EnumWindows）+ 提示窗修复

## v3.0.1 — 全面审计修复

- BeginInvoke 闪白修复 + .exe 兼容迁移 + CloseReason 修复 + TargetInfo 值语义 + 设置即时持久化 + app.ico 绝对路径 + KeepTransparency + 超椭圆图标 + 深色滚动条

## v2.6 — 后台透明 + 死循环修复

- 后台透明 + 独立滑块 + WinEvent 死循环修复 + 全面审计

## v1.0 ~ v2.5 — 早期演进

- v1.0 蒙版+反色+热键+单窗口 → v1.1 删热键加多窗口 → v2.0 删反色（Magnifier 弯路）→ v2.3 恢复蒙版+前景检查 → v2.5 CreateHandle + 100ms Timer + ApplyMaskNow

## 备注

- 版本日志原在 DEV.md「架构演进」章节，本文件为提炼版
