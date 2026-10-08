# DEV_README — 开发 / 排查记录

> 目的：把这次「ExplorerBlurMica 底部白条」从现象到修复的完整推导固化下来。
> 以后遇到**同类「注入式皮肤 / 模糊工具首绘漏铺」问题**，可直接复用这里的排查路径与方案层级，不必从头摸索。

---

## 1. 背景与目标

- **现象**：ExplorerBlurMica（`effect=2` Mica）运行时，手动打开资源管理器正常；但**浏览器用 `ShellExecute` 拉起的下载目录窗口，底部状态栏有一条白条**，手动拖一下窗口立即正常。
- **目标**：消除「每次都要手动拖」的麻烦。
- **约束**：用户环境为 Windows 11、PowerShell `FullLanguage`、AutoHotkey 未安装；希望尽量不碰 C++、不碰未公开源码。

## 2. ExplorerBlurMica 运行机理（源码确认）

它是注入 `explorer.exe` 的 **Shell 扩展 DLL**，逐窗口改写渲染：

1. **注入**：`register.cmd` 用 `regsvr32` 注册为 explorer 扩展，每次资源管理器启动加载该 DLL。
2. **子类化窗口**（源码 `WindowListener.h` 证实）：对每个 `CabinetWClass` 窗口用 `SetWindowSubclass` 接管 `WndProc`，并用 minhook hook 一批 API，可在窗口收消息时插手。
3. **监听重绘时机**：盯着 `WM_SIZE` / 窗口创建 / 显示等消息。窗口尺寸布局确定后，处理那些**本来不透明**的子控件（地址栏、WinUI 工具栏、滚动条、状态栏区域），把背景擦透明（`clearBarBg` / `clearAddress` / `clearWinUIBg`）。
4. **底层效果交给 DWM**：Mica / Acrylic / Blur 的模糊 / 材质背景由 Windows **DWM 桌面窗口管理器按窗口当前尺寸**在客户端区绘制。程序只负责「把挡在前面的不透明控件擦掉」，让后面的 DWM 效果露出来。

## 3. 白条根因（首绘竞态）

底部那条是 DirectUI 元素，背景本应被第 3 步的 hook 擦透明、再让 DWM 的 Mica 透出。但**浏览器拉起的窗口，首帧绘制发生在「hook 装好 / HostBackdrop 区域铺到那条」之前** → 露出默认白底。手动拖动触发 `WM_SIZE` → 监听器重跑重铺 → 盖对。

这是**纯首绘竞态**，与具体用哪种 `effect` **无关**。

## 4. 关键发现：公开仓库没有完整实现

- `ExplorerBlurMica/` 下**只有头文件 + minhook + 一个 327 字节的 `dllmain.cpp` 空壳**。
- 所有真正干活的函数声明都带 `extern`（`MyDefWindowProcW`、`MyDrawThemeBackground`、各 Listener 的 `WndProc`、`TranslucentImpl::Startup`……），实现全在作者私有基础库 **MToolBox** 里。
- 实测 `vcxproj` 引用的 `Helper/DirectUIHelper.h`、`HookHelper/*.h` 在 GitHub 上**全部 404**（用 Contents API 取同样 404，确属真缺失而非网络抖动）。
- 搜 `MToolBox` 仓库结果为 0。

**结论**：这是 LGPL 许可证意义上的「开源」，但**公开仓库没有可编译的完整引擎代码**。所以「clone → 改 `.cpp` → 重编」这条路在本仓库上行不通，必须先拿到 MToolBox 源码（向上游要）才行。

> 排查技巧：先用 `git tree?recursive=1` 看文件清单与大小；对 `extern` 声明保持怀疑，确认其实现文件是否在仓库内；用 `Contents API`（`api.github.com`）取文件可区分「真 404」与「raw 域名网络抖动」（raw.githubusercontent.com 偶发连接失败返回 `000`，而 `api.github.com` 通常通畅）。

## 5. 方案层级对比与选型

| 方案 | 机制 | 性能 | 白条 | 实现门槛 | 结论 |
|------|------|------|------|----------|------|
| ① 轮询 | 定时 `EnumWindows` 扫新窗口再 nudge | 有 400ms 轻轮询 | 修掉 | PowerShell ✅ | 可用，但非最优 |
| **② 事件驱动** | `SetWinEventHook(EVENT_OBJECT_CREATE)` 监听新窗口 | **零轮询，CPU 平时 idle** | 修掉（偶尔一帧闪） | **PowerShell ✅** | **本次采用** |
| ③ WH_CBT 钩子 | 全局钩子 DLL 注入，窗口尺寸未定时就拦 | 与②基本持平（略重：in-context 跨进程） | **几乎零闪现** | **需 C++ DLL + 编译工具链** | 进阶可选 |
| ④ 改 DLL 本体 | 首绘前就让 DWM 铺好 | 最优（治本） | 彻底无 | **需未公开的 MToolBox 源码** | 锁死，等上游 |

要点澄清：

- **② 与 ③ 性能基本在同一量级**，③ 并没有「更省性能」；③ 真正强在**时机更早 → 白条几乎零闪现**（视觉收益，非性能收益）。严格说 ③ 的 in-context 全局注入每次窗口创建还有极微跨进程开销，方向是「略重」而非「更轻」。
- 在「不碰 C++ / 不碰未公开源码」约束下，**② 是外部脚本的天花板**。
- `effect=3`（Blur Clear）测试过，**无效**——病根在监听器 attach 时机，不在效果类型本身，所以换效果没用。

## 6. nudge 实现细节（`explorer-blur-fix-event.ps1`）

核心 `Nudge(hWnd)`：

```csharp
RECT r; GetWindowRect(hWnd, out r);
int w = r.Right - r.Left, h = r.Bottom - r.Top;
// 仅高度 +1px（SWP_NOMOVE 保证左上角不动 → 只有下边框向下探 1px 再弹回），宽度不变
SetWindowPos(hWnd, IntPtr.Zero, 0, 0, w, h + 1, SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOMOVE);
SetWindowPos(hWnd, IntPtr.Zero, 0, 0, w, h,     SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOMOVE);
```

- **为什么用「真实尺寸变更」而非只 `PostMessage(WM_SIZE)`**：只有真实尺寸变化才必然触发 `WM_SIZE` 让监听器重跑；纯消息不一定可靠（部分实现只认真实变更）。「+1 再还原」等价于手动拖动，确保重铺必然发生。
- **为什么只动下边框**：用户要求更「专一」——底边向下探 1px 再弹回，左右上不动，视觉上只底边轻微一探一收；功能与整体放大等价（都触发 WM_SIZE → 整片背景重铺）。
- 带 `SWP_NOACTIVATE`（不抢焦点）、`SWP_NOMOVE`（位置不动）、`SWP_NOZORDER`（层级不变）。
- 延迟 450ms 再 nudge：等窗口先画出来，避免抢在首绘前抖动。偶尔还瞥见一帧白可把 450 调小（如 250）。
- 去重集合 `_seen`：每个窗口只 nudge 一次。
- 事件回调里用 `Task.Run` + `Sleep` 异步延迟，避免阻塞 hook 回调线程。

## 7. 性能分析

- **CPU**：事件驱动，平时 idle 零轮询；仅新窗口出现那一次触发一次性重绘（等价于手动拖一下）。
- **内存**：常驻一个 `powershell.exe` 约 30–60MB（因 `Add-Type` 编译了 P/Invoke 代码，固定成本，不随窗口增长）。
- **磁盘 / 网络 / 电池**：无。Mica 背景由 DWM 合成器加速绘制（零感知开销），脚本不增加其持续开销。
- 与方案①轮询相比，② 更省（无持续轮询）。与③相比性能同一量级。

## 8. 可复用的排查路径（以后同类问题照做）

1. **先确认是「配置可调」还是「代码级 bug」**：查官方 README 的 Config 段 + issue 搜索（用 `api.github.com/search/issues`，按「白 / 底 / white / flash / flicker」等中英文关键词）。
2. **读源码定位机制**：`git tree` 看结构 → 找 hook / 重绘 / 子类化相关文件 → 确认是真实现还是 `extern` 声明。
3. **判断能否自己改**：实现是否都在公开仓库？缺失则确认是否私有依赖（搜仓库名 + 实测 404）。
4. **治标（外部脚本 nudge）**：当无法改 DLL 时，`SetWinEventHook(EVENT_OBJECT_CREATE)` + 延迟 + `SetWindowPos` 真实尺寸抖动，是最省事的自动化手段。
5. **治本（改 DLL）**：需拿到私有源码，在窗口首创建 / `WM_SHOWWINDOW` 处强制 `RedrawWindow` 或 `PostMessage(WM_SIZE)`；或用 `WH_CBT` 更早拦截。

## 9. 后续可进阶方向

- **方案③（WH_CBT 全局钩子，C++ DLL）**：时机更早、白条几乎零闪现。需 MSVC/MinGW 编 x64 DLL，注入 `explorer.exe`，过滤只 nudge `CabinetWClass`。风险：全局注入若有 bug 可能影响 explorer 稳定性，工程量与风险高于②。
- **推动上游修复**：向上游提 issue，附复现步骤（浏览器拉起下载目录 + 首绘白条 + 拖一下恢复），推动作者在 `CabinetWClassListener` 首创建时补一次强制重绘。
- **拿到 MToolBox 源码后**：直接改 `TranslucentImpl` 的 attach 时机，从根上让首绘即铺满。
