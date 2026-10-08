# 暗幕 开发笔记 — DEV_README (v5.5.2)

## 项目概况

- **语言/框架**：C# 10 / .NET 6.0-windows / WinForms
- **入口**：`Program.cs` — `MainForm`（partial 类，`MainForm.UI.cs` 含界面代码）
- **构建**：`dotnet build -c Release` / 单文件发布 `dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true`

---

## 关键问题纪要

### 1. DWM 阴影导致 `GetWindowRect` 偏大

**现象**：黑底板比窗口实际可见区大一圈（微信等含 DWM 阴影的程序尤为明显）。

**根因**：`GetWindowRect` 返回的矩形**包含 DWM 阴影边**（4~8 px），而阴影是透明区域，底板在此处未被窗口遮挡，直接暴露。

**解决**：改用 `DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, ...)` 获取排除阴影后的真实可见矩形。失败时回退 `GetWindowRect`。

**涉及文件**：
- `Native.cs` — `DwmGetWindowAttribute` P/Invoke + `GetVisibleWindowRect()` 辅助方法
- `Program.cs:244-245` — `CreateEntry` 的 `OnUpdate` 中传给 `BlackPlate.AlignBehind` 前调用 `GetVisibleWindowRect`

### 2. 黑底板的 Z 序必须严格黏在目标正后方

**原理**：`SetLayeredWindowAttributes` 的 `LWA_ALPHA` 是整窗均匀半透明，露出的像素来自 Z 序中**紧贴它正后方**的窗口。如果有其它窗口插进中间，半透明透出的就不再是黑底板。

**解决**：`BlackPlate.AlignBehind` 使用 `SetWindowPos(Handle, targetHandle, ...)` 将底板插到目标正后方。现有 `WinEvent` + 250ms 轮询在每次目标移动/显示变化时重新对齐。

**涉及文件**：
- `BlackPlate.cs:50-52` — `SetWindowPos(Handle, targetHandle, ...)`

### 3. `TargetInfo` 作为 Dictionary Key 的哈希稳定性

**风险**：`TargetInfo` 被用作 `Dictionary<,>` 和 `HashSet<>` 的 key。如果 `ProcessName` 或 `WindowTitle` 在入字典后被修改，哈希值变化导致条目永久"丢失"。

**解决**：`ProcessName` 和 `WindowTitle` 设为 `{ get; init; }`（仅构造时可写）。`BackgroundAlpha` 仍为 `{ get; set; }`（不在哈希计算中）。

**涉及文件**：
- `Settings.cs:13-14`
- `Program.cs:880-892` — `PickWindow` 改用对象初始化器构造

### 4. `MainForm` 拆分为 Partial 文件

**动机**：`MainForm` 约 1000 行，包含 UI 构建、事件驱动、窗口管理、权限检测、托盘、WinEvent 钩子。

**拆分方案**：
- `Program.cs` — 核心状态、生命周期（OnLoad/OnShown/OnActivated）、条目管理（CreateEntry/SetTargetAlpha/TryBindTarget/AddTargetUI/AddPendingUI/RemoveEntry/UnbindAll）、提权检测、Quit、Main
- `MainForm.UI.cs` — BuildUI、辅助方法（AddGroup/AddCheck/AddButton/CreateSelectButton/CreateRemoveButton）、深色主题、UI 同步、选中目标、托盘、操作回调（ToggleEnabled/PickWindow/RefindAllWindows/SetBgAlpha）、WinEvent

**注意事项**：`partial class MainForm` 仅需在一个文件中声明基类 `: Form`，其它 partial 文件可省略。

### 5. 界面滚动条深色模式

调用 `Native.SetWindowTheme(hwnd, "DarkMode_Explorer", null)` 使 `FlowLayoutPanel` 的滚动条跟随系统暗色模式。

### 6. 分段去抖（Slider Debounce）

**问题**：拖动透明度滑块时 `ValueChanged` 频繁触发，每次调用 `_settings.Save()` 写盘（JSON 序列化 + 原子文件写入）。

**解决**：引入 200ms `Timer`，仅在停止拖动 200ms 后执行一次 `Save()`。

**涉及文件**：
- `Program.cs:147-148` — `_saveDebounceTimer` 初始化
- `MainForm.UI.cs:367-368` — `SetBgAlpha` 中的 `Stop()`+`Start()`

### 7. 提权检测

目标窗口以管理员运行时，非提权的本程序无法修改其 `WS_EX_LAYERED` 样式（`SetWindowLongPtr` 静默失败）。`TryBindTarget` 中检测目标是否提权，若是则弹出托盘提示。

**涉及文件**：
- `Program.cs:288-340` — `IsTargetElevated` / `IsCurrentProcessElevated`
- `Program.cs:375-385` — 绑定时的提权检查

### 8. WinEvent 跨线程安全

`WinEventProcCallback` 运行在系统线程池，不能直接访问 WinForms 控件。所有 UI 操作通过 `BeginInvoke` 封送到 UI 线程。

**涉及文件**：
- `MainForm.UI.cs:466-491` — WinEvent 回调

### 9. 窗口拾取器（WindowPickerForm）

全屏半透明表单 + 十字光标 + 反转边框高亮。左键选中、右键/Esc 取消。

- `EnumWindows` 按 Z 序从顶到底枚举
- 30ms 鼠标移动节流
- `PatBlt(DSTINVERT)` 异或方式绘制边框（两次调用自动消除）

### 10. 启动恢复透明度

程序启动时遍历配置的目标窗口，无条件恢复透明度到 255（`SetTargetAlpha(h, 255)`），以清理上次强制杀进程可能残留的透明效果。

**涉及文件**：
- `Program.cs:192-200` — `RestoreAllTargets()`

### 11. BlackPlate 圆角裁剪：SetWindowRgn vs 逐像素 Alpha

**背景**：v5.1.0 重构移除了上方蒙版（MaskOverlay），改为"目标窗口自身半透明 + 正下方垫纯黑底板"架构。底板是矩形，遮盖了目标窗口的 DWM 圆角。

**v1 尝试（失败）——逐像素 Alpha**：将底板位图改为 `Format32bppArgb`，用 `GraphicsPath` 画圆角 + `AC_SRC_ALPHA` 逐像素合成。

**坑 1：`GetHbitmap()` 丢失 Alpha 通道**。`Bitmap.GetHbitmap()` 从 `Format32bppArgb` 位图生成的 GDI 位图会丢弃 Alpha 数据。`AC_SRC_ALPHA` 合成时所有像素 Alpha=0，四角变成白色。

**坑 2：`UpdateLayeredWindow` 的 `ptDst` 与 `SetWindowPos` 双重定位**。当同时通过 `SetWindowPos` 和 `UpdateLayeredWindow(pptDst=...)` 设置位置且使用了 `AC_SRC_ALPHA` 时，DWM 合成路径变化，导致窗口上/左边框偏移几个像素。

**最终方案（成功）——`SetWindowRgn` + `CreateRoundRectRgn`**：
- 位图保持原 `Format32bppRgb` + `Clear(Color.Black)` + `AlphaFormat=0`（零改动）
- 圆角通过 `CreateRoundRectRgn` 创建区域句柄，`SetWindowRgn` 从窗口管理器层面裁剪形状
- 按 `(w, h, clamped)` 三元组缓存 HRGN 避免每帧重建
- `SetWindowRgn` 调用后区域所有权转移给系统，无需手动释放

**关键性质**：底板圆角半径只需 **≥ 目标窗口 DWM 圆角半径**。因为底板在目标正后方，目标 DWM 裁掉的透明角会透到底板层。底板裁角 < 目标半径 → 黑色漏出；= 目标半径 → 完美对齐；> 目标半径 → 多裁的区域被目标自身遮挡，视觉无感知。

**涉及文件**：
- `BlackPlate.cs` — `ApplyWindowRegion()`
- `Native.cs` — `CreateRoundRectRgn` / `CreateRectRgn` / `SetWindowRgn` P/Invoke
- `Program.cs` — `JumpTrackBar.ClipVisual()`

### 12. TrackBar 底部视觉溢出：Region 裁剪

**现象**：WinForms TrackBar 原生控件的视觉轨道（填充背景条）延伸到控件底部边界之外约 8-12px，遮挡下方的提示文字。

**根因**：TrackBar 是对原生 `msctls_trackbar32` 的封装，其内部 track 绘制区域比 WinForms 声明的 ClientSize 大，底部有多余的填充/焦点矩形。

**解决**：在 `JumpTrackBar` 中通过 `Region` 从窗口管理器层面裁掉底部 8px 的渲染区域：

```csharp
int clipH = Math.Max(Height - 8, 12);
this.Region = new Region(new Rectangle(0, 0, Width, clipH));
```

**涉及文件**：
- `Program.cs:12-31` — `JumpTrackBar` 类

### 13. 全局/局部双开关：透明度与圆角独立控制

**设计**：v5.3.0 引入两个独立的全局开关——`GlobalTransparency`（透明度）和 `GlobalCornerRadius`（圆角）。各自可独立开启/关闭：

- 开启（默认）：所有窗口共用全局滑块值
- 关闭：进入逐窗口配置模式，每个目标前显示 ○/● 选中按钮，上方滑块只修改选中目标

**关键逻辑**：
- 关闭全局开关时，把当前全局值写入每个目标作为各自起点（避免跳变）
- 非全局模式下未选中目标时，对应的滑块自动禁用
- `SelectTarget()` 同步两个参数组的滑块值

**涉及文件**：
- `Settings.cs` — `GlobalCornerRadius` + `TargetInfo.CornerRadius`
- `MainForm.UI.cs` — `ToggleGlobalCornerRadius()` / `SetCornerRadius()` / `SelectTarget()`

---

## 发布流程

```bash
# 1. 版本号写入 .csproj
# 2. 构建 + 单文件发布
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true

# 3. 标签 + Release
git tag v5.5.2
git push origin v5.5.2
gh release create v5.5.2 bin/Release/net6.0-windows/win-x64/publish/WindowTinter.exe --notes "xxx"
```

---
