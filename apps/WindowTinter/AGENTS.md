# AGENTS.md · 项目规则

> 给 AI / 未来的你：只记代码里看不出的关键信息。详细问题记录见 [DEV.md](DEV.md)（26 条一坑一篇）。

## 技术栈

- C# WinForms + .NET 6.0（`net6.0-windows`，目标框架必须匹配用户运行时——用户机器只有 .NET 6，SDK 版本可更高）
- 窗口渲染：UpdateLayeredWindow（纯黑位图 × alpha，DWM 硬件合成）——不用 Magnifier API
- 窗口操作：Win32 P/Invoke（SetWindowLong / SetLayeredWindowAttributes / SetWinEventHook / EnumWindows）
- CI：GitHub Actions 打 `v*` tag 自动构建发布 zip；落地页 `simiely.github.io/WindowTinter/`

## 关键坑

1. **跨进程 `SetWindowPos(SWP_FRAMECHANGED)` 会阻塞 UI 线程**（底层同步 SendMessage）——用 `InvalidateRect` 异步替代
2. **WinEvent 回调死循环**：改窗口样式触发 EVENT_OBJECT_LOCATIONCHANGE → 回调再改 → 回环。守卫：Refresh 不变不触发 + OnUpdate 内 alpha 不变不调 SetTargetAlpha
3. **BeginInvoke 闭包变量过期**：执行时加 `IsWindow + GetForegroundWindow` 守卫检查
4. **hwnd 回收复用风险**：操作前必须 `IsWindow(hwnd)`（两次 Win32 调用之间有 TOCTOU 窗口）
5. **`FormClosing` 必须检查 `CloseReason`**——用户关闭/系统关机/任务管理器是三种语义，无条件 `e.Cancel=true` 会阻止关机
6. **托盘常驻程序**：设置变更即时 `Save()`（不能依赖"退出时保存"）；启动时做状态修复（杀进程残留的 WS_EX_LAYERED 用 RedrawWindow 清）
7. **便携工具配置与 exe 同目录**（`Path.GetDirectoryName(Environment.ProcessPath)`）——`%AppData%` 会造成"换目录配置丢失"
8. **资源路径用绝对路径**——开机自启工作目录 ≠ exe 目录，相对路径 `app.ico` 会崩
9. **持久化格式变更必须加迁移代码**（如 .exe 后缀剥离），否则存量用户全挂
10. **Dispose 顺序**：依赖方先停用再释放共享资源（`_appIcon.Dispose()` 必须在 `_tray.Visible=false` 之后）
11. **窗口拾取**：用 `EnumWindows` 按 Z 序遍历 + PtInRect，不用 ShowWindow 显隐（DWM 过渡动画闪烁）
12. **跨完整性级别静默失败**：目标提权而本程序未提权时设透明度无反应——主动探测 TokenElevation 并提示用户

## 约定

- 前台蒙版 + 后台透明（WS_EX_LAYERED alpha）策略；KeepTransparency 模式统一走 SetLayeredWindowAttributes
- 全屏/单窗口透明度开关：关闭全局时把当前值写入每个目标避免跳变
- 发布 zip 打整个 `publish/` 目录（exe + dll + deps.json + runtimeconfig.json + app.ico），不是只挑 exe

## 常用命令

- 构建：`dotnet publish -c Release -r win-x64 --self-contained false`（依赖框架模式）
- 打包：整个 publish/ 目录 + app.ico → zip
- 版本号：csproj 中 Version
