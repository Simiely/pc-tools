# DEV.md — 开发笔记

> 本项目从 v1 到 v3.9.0 的完整踩坑记录。以后做「窗口覆盖 / 蒙版 / 暗化」类工具时直接参考。

---

## 1. 渲染方案：UpdateLayeredWindow

### 为什么用 UpdateLayeredWindow 而不是 Magnifier API

| 方案 | 原理 | 问题 |
|---|---|---|
| **UpdateLayeredWindow** | 纯黑位图 × alpha，DWM 硬件合成 | 蒙版在 TOPMOST 段，物理盖住上层窗口 |
| **Magnifier API** | DWM 捕获屏幕像素 + 颜色矩阵 | 同样是 TOPMOST 真窗口，且挡交互；PowerToys 也不这么用 |
| **WGC + Direct2D** | 只抓目标窗口帧，理论上不盖上层 | 需要 Win10 1903+ 和大量 WinRT/D3D11 基础设施，SharpDX 依赖 |

**结论**：UpdateLayeredWindow 是最简单可靠的方案。Z 序问题通过"前台蒙版 + 后台透明"策略解决。

---

## 2. 后台透明：SetLayeredWindowAttributes

```csharp
// 后台：加 WS_EX_LAYERED，设 alpha
SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_LAYERED);
SetLayeredWindowAttributes(hwnd, 0, alpha, LWA_ALPHA);
InvalidateRect(hwnd, IntPtr.Zero, true);  // 强制立即重绘

// 恢复：去掉 WS_EX_LAYERED
SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA);
SetWindowLong(hwnd, GWL_EXSTYLE, ex & ~WS_EX_LAYERED);
InvalidateRect(hwnd, IntPtr.Zero, true);
```

### 关键教训

- **不要用 `SetWindowPos(SWP_FRAMECHANGED)`** — 底层是同步 `SendMessage(WM_NCCALCSIZE)` 到目标进程，目标消息泵忙时 UI 线程卡死
- **用 `InvalidateRect` 替代** — 异步、不阻塞、效果等同
- **不要用 `ThreadPool` 做跨进程窗口操作** — 多个线程同时 `SetWindowLong` 同一个 hwnd 会造成竞态
- **某些应用不支持 `WS_EX_LAYERED`** — 设置透明度可能无效果或异常，`try/catch` 兜底
- **hwnd 回收复用风险** — 窗口销毁后 hwnd 可能被 OS 回收给新窗口。`SetWindowLong(hwnd, ...)` 前需先 `IsWindow(hwnd)` 检查，不能仅靠入口守卫（两次 Win32 调用之间有 TOCTOU 窗口）

---

## 3. WinEvent 死循环（最严重的 Bug）

### 现象

目标切后台 → 程序卡死，窗口拖不动。

### 根因链

```
SetTargetAlpha → SetWindowLong(改 WS_EX_LAYERED) + InvalidateRect
  → 触发 EVENT_OBJECT_LOCATIONCHANGE（目标窗口样式变了）
  → WinEventProcCallback → BeginInvoke(RefreshNow)
  → Refresh → OnUpdate → SetTargetAlpha → 🔄 死循环
```

### 最终守卫策略

```
Refresh()       → rect/visible 不变则不触发 OnUpdate（阻断位置变化引起的回环）
RefreshForeground() → 无视守卫，强制触发 OnUpdate（前台切换必须更新）
OnUpdate 内部   → _lastBgAlpha 与目标值比较，不变则不调 SetTargetAlpha（阻断重复修改引起的回环）
```

---

## 4. 前后台切换闪白 — BeginInvoke 时序竞态

### 现象

目标从后台切前台（或反过来）时，偶尔闪一下白色。

### 根因

OnUpdate 中先做蒙版/透明操作，再做互补操作——中间有一帧缝隙：

```
切后台：HideMask() 先执行 → 窗口全亮 → SetTargetAlpha() 后执行 → 变暗  ← 闪白
切前台：SetTargetAlpha(255) 先执行 → 窗口全亮 → AlignTo() 后执行 → 盖蒙版  ← 闪白
```

### 修复

用 `BeginInvoke` 让两个操作分隔一帧，先做的效果先稳定下来：

```csharp
// 后台：先设透明度，延迟一帧再撤蒙版
SetTargetAlpha(hwnd, targetAlpha);
BeginInvoke(() => { if (IsWindow(hwnd) && GetForegroundWindow() != hwnd) HideMask(); });

// 前台：先盖蒙版，延迟一帧再恢复透明度
AlignTo(r);
BeginInvoke(() => { if (IsWindow(hwnd) && GetForegroundWindow() == hwnd) SetTargetAlpha(hwnd, 255); });
```

### 教训

BeginInvoke 闭包捕获的变量在执行时可能已过时。必须加守卫检查（*IsWindow + GetForegroundWindow*），否则前台 BeginInvoke 可能在目标已切后台时误恢复全不透明。

---

## 5. .exe 后缀兼容性 — 待激活失败

### 现象

从 v2.x 升级后，已配置的目标窗口打开后一直显示"待激活"，不自动绑定。

### 根因

v2.x 存储 ProcessName 带 `.exe` 后缀（"notepad.exe"），v3.x 改为不带后缀。`FindByTitleAndProcess` 在匹配时去掉了 `.exe` 剥离逻辑——导致 `Process.GetProcessById().ProcessName`（"notepad"）≠ 存储的 "notepad.exe"。

### 修复

1. `FindByTitleAndProcess` 恢复 `.exe` 后缀剥离（向后兼容旧配置）
2. `Settings.Load()` 新增迁移：首次加载时遍历全部 Targets，去掉 `.exe` 后缀并覆写配置文件

**教训**：修改持久化格式时必须加迁移代码，不然存量用户全挂。

---

## 6. FormClosing 阻止系统关机

### 现象

开启"最小化到托盘"后，Windows 关机/注销被阻止。

### 根因

`OnFormClosing` 中 `e.Cancel = true; Hide()` 无条件拦截了所有关闭请求，包括 `CloseReason.WindowsShutDown`。

### 修复

```csharp
if (!_reallyQuit && _settings.MinimizeToTray && e.CloseReason == CloseReason.UserClosing)
    { _settings.Save(); e.Cancel = true; Hide(); }
```

**教训**：`FormClosing` 处理中必须检查 `CloseReason`。用户关闭、系统关机、任务管理器结束是三种不同的语义。

---

## 7. settings.json 持久化一致性

### 问题

多个设置项（滑块、复选框）的值变更仅在关闭程序时才保存到 JSON。如果进程崩溃或被强制结束，所有未保存的改动丢失。

### 修复

每个值变更的回调中直接调用 `_settings.Save()`：背景透明度、蒙版透明度、启用开关、开机自启、最小化到托盘、保持透明度——全部即时持久化。

**教训**：常驻托盘程序无法依赖"正常退出→保存"路径，必须即时写盘。

---

## 8. TargetInfo 值语义重构

### 问题

整个代码库用 `==` 比较 `TargetInfo` 对象，依赖引用相等。反序列化后重新创建的对象，即使内容相同也无法匹配。

### 修复

`TargetInfo` 实现 `IEquatable<TargetInfo>`，重写 `Equals` / `GetHashCode` / `==` / `!=`，基于 `ProcessName` + `WindowTitle` 不区分大小写值语义比较。

---

## 9. app.ico 绝对路径 — 开机自启崩溃

### 现象

设置开机自启后，重启电脑程序启动即崩溃。

### 根因

Windows 从注册表 `Run` 键启动程序时，工作目录是 `C:\Users\<用户名>` 而非 exe 所在目录。`new Icon("app.ico")` 相对路径找不到文件 → `FileNotFoundException` → 构造函数崩。

### 修复

```csharp
var iconPath = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath) ?? ".", "app.ico");
_appIcon = new Icon(iconPath);
```

---

## 10. _appIcon Dispose 顺序 — 退出崩溃

### 现象

点"退出"时程序崩：`ObjectDisposedException: Cannot access a disposed object. Object name: 'Icon'.`

### 根因

`Quit()` 中先 `_appIcon.Dispose()`，然后 `_tray.Visible = false` 触发 `NotifyIcon.UpdateIcon` → 访问已销毁的 `Icon.Handle`。

### 修复

`_appIcon.Dispose()` 放在 `_tray.Visible = false` 之后、所有清理之后执行。

**教训**：Dispose 共享资源的顺序至关重要——依赖方必须先停止使用，然后才能释放。`NotifyIcon` 对 `Icon` 的引用一直持续到其自身清理完成。

---

## 11. 窗口拾取器闪烁修复

### 现象

点击"+ 添加窗口"后，全屏和任务栏出现快速闪烁。

### 根因

每次鼠标移动都执行 `ShowWindow(SW_HIDE)` + `ShowWindow(SW_SHOWNOACTIVATE)`，DWM 对每次显隐产生过渡动画，高频率形成闪烁。

### 修复

`WindowFromPoint` 的 MSDN 文档明确写：**忽略已禁用（WS_DISABLED）的窗口**。用 `Enabled = false` 临时禁用拾取窗：

```csharp
Enabled = false;
IntPtr h = Native.WindowFromPoint(pt);
Enabled = true;
```

**教训**：任何时候需要让 `WindowFromPoint` 忽略自己的窗口，用 `WS_DISABLED`（`Enabled = false`），不要用 `ShowWindow`。后者触发 DWM 合成管线，性能差且视觉干扰严重。

---

## 12. 首次渲染不稳定

### 现象

启动时蒙版"有时候有、有时候没有"，拖动目标窗口才出现。

### 根因

`BeginInvoke` 在 Form 生命周期 `Shown` 阶段不可靠——消息泵可能同步执行。`WM_TIMER` 是可靠替代：

```csharp
private void OnShown(object _, EventArgs __)
{
    foreach (var e in _entries) e.Tracker.RefreshNow();
    _onShownTimer = new Timer { Interval = 100 };
    _onShownTimer.Tick += (s, args) => { _onShownTimer.Stop(); _onShownTimer.Dispose(); ApplyMaskNow(); };
    _onShownTimer.Start();
}
```

---

## 13. 退出清理：FormClosed vs FormClosing

### 修复

将 `Quit()` 挂载到 `FormClosed`——无论哪种退出方式，窗口关闭后必然触发。`Quit` 中先隐藏托盘再释放资源，确保 `NotifyIcon` → `Icon` 依赖链安全释放。

---

## 14. 窗口关闭再打开的重绑定

### 根因

旧条目 handle 失效但未清理。`_entries.Any(e => e.Info == t)` 返回 true（旧条目还在），跳过绑定。

### 修复

1. 定时器 tick 先清理 `!IsWindow(TargetHandle)` 的旧条目
2. 将已销毁条目放回待激活列表，而非直接丢弃

---

## 15. 跨线程访问 _entries

### 修复

`WinEventProcCallback` 中将 `_entries.FirstOrDefault()` 移入 `BeginInvoke` 内。`ObjectDisposedException` + `InvalidOperationException` 双捕获防止窗口关闭时崩溃。

---

## 16. KeepTransparency 模式

### 设计

"窗口保持透明度"开关——开启后不叠加蒙版，前台后台统一走 `SetLayeredWindowAttributes`，用后台透明度滑块的设定值。对不想要蒙版、只需要恒定透明度的场景。

### OnUpdate 路径

```csharp
if (_settings.KeepTransparency)
{
    HideMask();
    SetTargetAlpha(hwnd, (byte)((100 - _settings.BackgroundAlpha) * 255 / 100));
    return;
}
// 正常蒙版模式 ...
```

---

## 17. 超椭圆图标

Python + Pillow + NumPy 处理，1920×1920 源图 → 超椭圆裁剪（n=4 大尺寸 / n=8 小尺寸） → 6 分辨率 ICO（16/32/48/64/128/256）。`SetWindowTheme("DarkMode_Explorer")` 应用深色滚动条。

---

## 18. WinEvent 异常分类捕获

```csharp
try { BeginInvoke(...); }
catch (ObjectDisposedException) { }  // 表单已关闭
catch (InvalidOperationException) { }  // BeginInvoke 在非 UI 线程被调用
```

只捕获明确预期的异常，其余异常不静默。

---

## 19. 启动时恢复透明度 — 强制杀进程残留

### 现象

程序被任务管理器强制结束后重新启动，之前监控的目标窗口仍处于半透明状态（`WS_EX_LAYERED` 未清理干净）。

### 根因

`Quit()` 中的恢复逻辑依赖正常退出流程（`FormClosed` → 遍历所有目标 → 去掉 `WS_EX_LAYERED` → `InvalidateRect`）。强制结束进程时此流程不执行，目标窗口的扩展样式残留。

### 修复

启动时遍历 `WindowTinter.settings.json` 中所有目标，调用 `RedrawWindow` 强制刷新：

```csharp
// 去掉可能残留的 WS_EX_LAYERED
SetWindowLong(hwnd, GWL_EXSTYLE, ex & ~WS_EX_LAYERED);
RedrawWindow(hwnd, IntPtr.Zero, IntPtr.Zero,
    RDW_INVALIDATE | RDW_ERASE | RDW_FRAME | RDW_ALLCHILDREN);
```

用 `RedrawWindow` 替代 `SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA)` + `InvalidateRect` 的组合。MSDN 指出后者在某些情况下不能可靠清除分层属性。

**教训**：托盘常驻程序的退出是不可靠的——杀进程、崩溃、断电都可能跳过清理。**必须在启动时做状态修复**，不能仅依赖"退出→清理"路径。

---

## 20. 配置文件路径迁移 — %AppData% → exe 同目录

### 现象

v3.0.1 及之前版本，`WindowTinter.settings.json` 保存在 `%AppData%\WindowTinter\` 下。用户下载新版 exe 到新目录后，旧配置不被识别。

### 修复

```csharp
var configPath = Path.Combine(
    Path.GetDirectoryName(Environment.ProcessPath) ?? ".",
    "WindowTinter.settings.json");
```

配置与 exe 同目录，下载新版解压即用，无需手动迁移配置。

**教训**：便携式工具（无需安装、解压即用）的配置应与 exe 同目录。`%AppData%` 适合安装版软件，便携版会造成"换了目录配置丢失"的困惑。

---

## 21. UI 重构 — 按钮下移 + 状态栏整合

### 变更

v3.6.x 对主界面进行了重新布局：

- **"+ 添加窗口"按钮**从顶部工具栏移至目标窗口列表下方——拾取后直接看到新增条目，操作流更自然
- **"启用覆盖"和"保持透明度"**从独立复选框整合到目标条目内联状态栏——每个窗口独立的开关状态一目了然
- **移除"查看日志"按钮**——DebugLog 仅供开发阶段使用，用户不需要暴露

**教训**：UI 布局应从用户操作流出发设计，而非从功能模块出发。拾取→查看→启用是一个连续的视觉流，按钮应遵循这个顺序。

---

## 22. 后台点击激活目标 — MaskOverlay 双模式

### 设计

新增后台点击捕获能力：当蒙版盖在目标窗口上（`WS_EX_TRANSPARENT` 穿透），用户点击蒙版区域 → 转发激活目标窗口。

### 实现

```csharp
// MaskOverlay 新增 MouseUp 事件处理
protected override void OnMouseUp(MouseEventArgs e)
{
    // 获取蒙版下方的目标窗口
    var hwndBelow = GetWindowBelowMaskAtPoint(Cursor.Position);
    SetForegroundWindow(hwndBelow);
}
```

蒙版窗口需要临时取消 `WS_EX_TRANSPARENT`（极短时间）以接收鼠标事件，然后在 `MouseUp` 后恢复穿透。时序：`MouseDown` 去掉 `WS_EX_TRANSPARENT` → `MouseUp` 激活目标 → 恢复 `WS_EX_TRANSPARENT`。

**教训**：`WS_EX_TRANSPARENT` 和接收鼠标事件是互斥的。要在蒙版上实现"点击穿透到下层窗口"的 UX，需要用极短时间窗口切换样式——关键是时序必须在同一个鼠标事件周期内完成。

---

## 23. 窗口拾取器零闪烁方案 — EnumWindows Z 序

### 演进

拾取器经过三轮方案迭代：

| 方案 | 方法 | 问题 |
|---|---|---|
| A | `ShowWindow(HIDE)` + `WindowFromPoint` | 每次鼠标移动显隐一次，DWM 合成管线触发过渡动画，高频产生闪烁 |
| B | `Enabled = false` + `WindowFromPoint` | MSDN 说 `WindowFromPoint` 忽略 `WS_DISABLED` 窗口，但在某些 Windows 版本无效 |
| **C（最终）** | `EnumWindows` 按 Z 序从顶到底遍历 | 不碰拾取窗本身，零闪烁、零显隐 |

方案 C 实现：

```csharp
Native.EnumWindows((h, _) => {
    if (h == pickerHwnd || !IsWindowVisible(h)) return true;
    GetWindowRect(h, out var r);
    if (PtInRect(ref r, pt)) { result = h; return false; }  // 找到即停
    return true;
}, IntPtr.Zero);
```

**教训**：`WindowFromPoint` 不是获取"鼠标下可见窗口"的唯一方式。当自身窗口干扰时，`EnumWindows` + 手动 `PtInRect` 更可靠。Z 序遍历天然保证返回最顶层可见窗口，且不需要修改自身窗口状态。

---

## 24. 提示窗 BackColor 含 alpha 崩溃

### 现象

拾取提示窗在设置 `BackColor = Color.FromArgb(200, ...)` 时抛 `ArgumentException`。

### 根因

WinForms 的 `Form.BackColor` 不支持 alpha 通道。带 alpha 的颜色值会被底层 GDI+ 拒绝。

### 修复

```csharp
// 错误
BackColor = Color.FromArgb(200, 30, 30, 30);

// 正确：纯色 BackColor + Form.Opacity 控制整体透明度
BackColor = Color.FromArgb(30, 30, 30);
Opacity = 0.85;
```

---

## 25. GitHub Actions CI/CD + GitHub Pages

### CI 自动发布

打 `v*` tag 时自动触发构建，产出单文件自包含 exe 并打包为 `WindowTinter.zip`，发布到 GitHub Release。

```yaml
# .github/workflows/build.yml
on:
  push:
    tags: ['v*']
jobs:
  build:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '6.0.x' }
      - run: dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
      - uses: softprops/action-gh-release@v1
        with:
          files: WindowTinter-release.zip
```

### GitHub Pages 落地页

产品落地页 (`index.html`) 基于 hub-world 全屏滚动模板，部署在 `https://simiely.github.io/WindowTinter/`。Pages 从 main 分支根目录自动构建，每次 push 自动更新。

---

## 26. 近期迭代踩坑（v3.7.0 → v3.9.0）

以下为本次迭代新增功能与排障经验，单独成节便于日后检索。

### 26.1 目标框架必须匹配运行环境（net9 vs net6）

**现象**：用 `net9.0-windows` 编译出的 exe，在只装了 .NET 6 运行时的机器上「双击无法运行 / 打不开」。
**根因**：`<TargetFramework>` 决定 exe 依赖的共享运行时版本。net9 产物需要 .NET 9 运行时；用户机器只有 .NET 6 → 启动即失败（无报错弹窗，只有事件日志）。
**结论 / 教训**：交付前务必确认目标机运行时版本。`dotnet --list-runtimes` 看本机装了哪些。本项目用户环境是 .NET 6，所以始终用 `net6.0-windows`。SDK 版本（如 9.0.300）可以高于目标框架——用高版本 SDK 编译 net6 完全没问题，关键是目标框架别超运行时。

### 26.2 依赖框架发布必须带齐附属文件

**现象**：只把单个 `WindowTinter.exe` 发给用户 → 用户说「程序坏了 / 打不开」。
**根因**：`dotnet publish` 依赖框架（framework-dependent）模式产出的不是一个 exe，而是一堆文件：`WindowTinter.exe` + `WindowTinter.dll` + `WindowTinter.deps.json` + `WindowTinter.runtimeconfig.json` + 一堆 `*.dll`。**只发 exe 会缺失依赖清单与程序集**，运行时找不到入口而失败。此外 `app.ico` 也必须同目录（否则图标加载失败，见第 9 / 20 节）。
**结论**：发布压缩包时把整个 `publish/` 目录内容（含 app.ico）一起打进 zip，而不是只挑 exe。自包含（`--self-contained`）会把运行时也打进单文件，包体大但免装运行时——本项目用户机器已有 .NET 6，故采用依赖框架 + 完整文件发布。

### 26.3 图标要用真实资源，勿用占位图

**现象**：编译时用了脚本生成的占位 `app.ico`，与产品真实图标不一致，用户一眼看出「图标不对」。
**正确做法**：仓库里本就有真实 `app.ico`（多分辨率 ICO），直接用。csproj 里 `<ApplicationIcon>app.ico</ApplicationIcon>` + `<None Update="app.ico"><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></None>` 即可随 exe 输出。
**取仓库原始资源**：受限环境 `raw.githubusercontent.com` 被 TLS 拦截时，改走 GitHub REST：`GET api.github.com/repos/{owner}/{repo}/git/blobs/{sha}`，请求头 `Accept: application/vnd.github.v3.raw` 直接拿文件字节（见 26.10）。

### 26.4 UI 文字被遮挡：新增控件要重算 GroupBox 高度与 ClientSize

**现象**：在「透明度」分组里新增一行注释 Label（放 y=104），被同组的背景透明度滑块（y=80~120）遮住一部分，文字显示不全。
**根因**：GroupBox 内部区域是固定高度画的（见第 21 节 / `AddGroup`），新增控件超出原高度却没调大 GroupBox `Size` 和 `ClientSize`，于是被裁切。
**修复 / 教训**：每增加一行控件，记得同步把所在 GroupBox 的 `Size.Height` 调大、必要时把窗体 `ClientSize.Height` 调大，留出底部边距。WinForms 不会自动为你扩展父容器。

### 26.5 离线 NuGet 还原（沙箱无外网时）

**场景**：CI/沙箱机无法访问 nuget.org，或缺少对应目标框架的引用包时，`dotnet restore` 报错。
**办法**：临时放一个 `nuget.config`：

```xml
<configuration>
  <packageSources><clear /></packageSources>
  <fallbackPackageFolders><clear />
    <add key="local" value="C:\Users\2504\.nuget\packages" />
  </fallbackPackageFolders>
</configuration>
```

本地 NuGet 缓存 `C:\Users\<用户>\.nuget\packages` 里通常有历史拉取的 net6.0 引用包（`Microsoft.NETCore.App.Ref`、`Microsoft.WindowsDesktop.App.Ref` 等），`fallbackPackageFolders` 让还原从这里取。
**⚠️ 切勿把此 nuget.config 提交到仓库**：`<clear/>` 会**移除 nuget.org 源**，普通用户 clone 后 `dotnet build` 会因无包源而失败。它只是本地 / 沙箱的临时手段，构建完即删。

### 26.6 动态标题窗口匹配：精确匹配 + 「标题包含」回退

**问题**：`FindByTitleAndProcess` 原要求标题**精确相等**。浏览器、带实时时间的播放器等标题会动态变化 → 标题一变就掉回「待激活」。
**修复**：先按「进程名 + 精确标题」匹配；匹配不到时，回退到「标题包含（子串）」匹配（同进程名下任一窗口标题含目标串即命中）。这样动态标题窗口能稳定保持绑定。
**代价**：子串匹配可能误命中同进程的多窗口，但本工具监控粒度本就是「进程 + 标题」，可接受。

### 26.7 GDI 位图缓存：HBITMAP 复用

**问题**：`MaskOverlay.RenderLayered` 每次渲染都 `Bitmap.GetHbitmap()` 新建位图、用完 `DeleteObject` 释放。移动蒙版时每帧新建/释放，GDI 句柄抖动明显。
**修复**：缓存 `_hBmp` 字段，**仅在蒙版尺寸变化时才重建**位图；`Dispose` 里统一 `DeleteObject(_hBmp)` 释放。句柄数稳定，移动更顺。

### 26.8 提权检测（管理员目标 vs 普通权限工具）

**现象**：目标程序以管理员身份运行，而本工具以普通权限运行时，给它设透明度**静默失败**（跨完整性级别改窗口样式被系统拒绝，且无异常抛出）。
**修复**：新增 `Native` P/Invoke：`OpenProcess(PROCESS_QUERY_INFORMATION)` → `OpenProcessToken(hProc, TOKEN_QUERY, out hToken)` → `GetTokenInformation(hToken, TokenElevation=20, out TOKEN_ELEVATION, ...)` 读出目标是否提权；本进程同样取一次自身是否提权。当「目标提权 && 本程序未提权」时弹托盘气泡提示：「请右键以管理员身份运行本程序」。
**教训**：跨完整性级别（IL）的窗口样式修改会被系统静默拒绝，UI 上看就是「设了透明度没反应」。这类问题要主动探测并提示用户，而不是让用户瞎猜。

### 26.9 全局 / 单窗口透明度功能设计

**需求**：一个总开关「全局统一透明度」。

- **开启（默认）**：所有监控窗口共用上方「蒙版 / 后台」两套滑块值（`Settings.Alpha` / `Settings.BackgroundAlpha`）。
- **关闭**：进入「单窗口配置」模式——每个目标前出现「○ / ●」选中按钮，选中后上方双滑块只改该窗口；不同程序可各自设定暗度。每个 `TargetInfo` 增加 `Alpha` / `BackgroundAlpha` 字段。

**关键一致性处理**：

- 关闭全局开关时，把当前全局值**写入每个目标**作为各自起点，避免突然跳变；
- 非全局模式且未选中任何目标时，**禁用手动滑块**（避免「空转」调了却没效果），`UpdateSliderEnabled()` 处理；
- 切换「选中目标」时把该目标的值刷进滑块（`SelectTarget`）。

**OnUpdate 统一取值**：`int maskA = GlobalTransparency ? Settings.Alpha : info.Alpha;` 一处分支贯穿前台蒙版 / 后台透明 / 保持透明度三条路径。

### 26.10 沙箱网络限制：api.github.com 可用，raw.githubusercontent.com 被拦

**现象**：`git clone` / `curl` 直连 `raw.githubusercontent.com` 被 TLS 拦截失败；但 `api.github.com` 各接口（含 blob 原始内容）正常。
**应对**：

- 取仓库原始文件 → 用 `GET api.github.com/repos/{owner}/{repo}/git/blobs/{sha}` + `Accept: application/vnd.github.v3.raw`；
- 推代码 → `git` 走 `https://<token>@github.com/...` 正常（ls-remote / clone / push 均通）。

**教训**：同一域名家族下不同子域的 TLS 策略可能不同，一条路不通就换 REST API。

---

## 架构演进

| 版本 | 变化 |
|---|---|
| v1.0 | 蒙版+反色+热键+单窗口 |
| v1.1 | 删热键、加多窗口 |
| v2.0 | 删反色，Magnifier 弯路 |
| v2.3 | 恢复蒙版、前景检查 |
| v2.5 | CreateHandle + 100ms Timer + ApplyMaskNow |
| v2.6 | 后台透明 + 独立滑块 + 死循环修复 + 全面审计 |
| v3.0.1 | BeginInvoke 闪白修复 + .exe 兼容迁移 + CloseReason 修复 + TargetInfo 值语义 + 设置即时持久化 + app.ico 绝对路径 + KeepTransparency + 超椭圆图标 + 深色滚动条 + 8 轮审计 |
| v3.2.2 | 配置路径迁移（exe 同目录）+ 启动透明度恢复 + 后台点击激活 + 拾取器零闪烁方案 + 提示窗修复 + DebugLog 清理 |
| v3.6.2 | UI 重构（按钮下移+状态栏整合）+ GitHub Actions CI/CD + GitHub Pages 落地页 + 多项稳定性修复 |
| v3.7.0 | 全局/单窗口透明度开关 + 目标选中按钮 + 动态标题子串匹配 + GDI 位图缓存 + 提权检测气泡 + UI 遮挡修复 |
| v3.9.0 | 版本号推进 3.9.0 + README/DEV.md 文档更新 + 清理仓库游离文件 + 确保仓库可直接 dotnet build |

---

## 代码审计清单（通用参考）

做窗口操作类工具时必查：

- [ ] 跨进程 `SetWindowPos` 是否带 `SWP_FRAMECHANGED`（会阻塞 UI 线程）
- [ ] WinEvent 回调是否会使目标窗口状态变化再触发新事件（回环）
- [ ] 变更守卫是否用 `||` 连接了恒真条件
- [ ] BeginInvoke 闭包变量过期风险（加 IsWindow/GetForegroundWindow 守卫）
- [ ] `SetWinEventHook` 范围是否覆盖了所有需要的事件类型
- [ ] WinEvent 线程是否有跨线程访问 UI 集合
- [ ] `FormClosing` 是否检查 `CloseReason`
- [ ] 持久化格式变更是否有迁移代码
- [ ] `Dispose` 顺序：依赖方先停用，再释放共享资源
- [ ] 资源路径是否用绝对路径（开机自启工作目录 ≠ exe 目录）
- [ ] 设置变更是否即时持久化（托盘程序无法依赖"退出时保存"）
- [ ] `SetWindowLong` 改 `WS_EX_LAYERED` 后是否有 `InvalidateRect` 刷新
- [ ] hwnd 操作前是否有 TOCTOU 防护（`IsWindow` 在操作前再查一次）

---

## 参考

- [MSDN: Layered Windows](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features#layered-windows)
- [MSDN: WS_EX_TRANSPARENT](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles)
- [MSDN: WindowFromPoint](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-windowfrompoint)
- [PowerToys Always On Top](https://github.com/microsoft/PowerToys/tree/main/src/modules/alwaysontop)
