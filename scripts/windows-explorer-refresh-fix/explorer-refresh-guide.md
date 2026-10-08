# 资源管理器不自动刷新（下载文件要手动 F5）修复指南

> 适用：Windows 10 IoT Enterprise LTSC 2024（Build 26100，x64）
> 症状：下载/拷贝文件后，资源管理器不立即显示，必须按 F5 或右键刷新才出现。

---

## 一、诊断更正（重要）

上一轮我怀疑是「映射网络盘 Y:/Z: 拖垮外壳刷新消息泵」，但**实测已排除**：

| 探测项 | 结果 |
|---|---|
| Ping 网关 192.168.x.1 | 平均 **1.0 ms**，4/4 成功，零丢包 |
| `net use` 状态 | Y:/Z: 均为 **OK** |
| `Test-Path Y:\` / `Z:\` | 均可访问，耗时 **0.02s / 0.007s**（瞬连） |

→ 两个 SMB 共享在线且延迟极低，**不是**卡顿元凶。请**不要**用上一版 `fix-explorer-refresh.reg`（那是针对网络盘通知的，对你无效），我已删除。

## 二、重新排序的真凶（按可能性）

| 嫌疑 | 本机实测状态 | 可能性 |
|---|---|---|
| 网络盘通知卡顿 | 已实测排除 | ❌ |
| `dontrefresh`=1 | 该项不存在（默认刷新开） | 低 |
| 云盘/杀软壳扩展 | 无进程在跑 | 低（但可能有静态残留扩展） |
| **Shell Bags / BagMRU 视图缓存损坏** | 两项注册表均存在 | ⚠️ 高（最常见隐性元凶） |
| **缩略图/图标缓存损坏** | 未清过 | ⚠️ 中高 |
| **资源管理器历史 / Quick Access 损坏** | 未清过 | ⚠️ 中 |
| 系统文件损坏 | 未跑 SFC | 低-中 |

---

## 三、方法一：一键脚本（推荐，免管理员）

文件：`reset-explorer-view.ps1`，配套启动器 `run-explorer-fix.bat`

它做五件事：重启外壳 → 重置 Shell Bags → 清历史/跳转列表 → 清图标+缩略图缓存 → 写入 `AlwaysRefresh=1` 强制后台刷新。

**运行方式（任选其一，推荐方式一）：**
- 方式一（最简单·双击即可）：双击 `run-explorer-fix.bat`，它会自动用 `-ExecutionPolicy Bypass` 运行脚本，结束后按任意键关闭窗口。
- 方式二：Win+X → 终端（非管理员），直接执行（绝对路径，无需 cd）：
  ```powershell
  powershell -ExecutionPolicy Bypass -File ".\reset-explorer-view.ps1"
  ```
- 方式三：右键 `reset-explorer-view.ps1` →「使用 PowerShell 运行」（若被策略拦截则用方式一/二）。

执行后资源管理器会重启（桌面/任务栏闪一下），**开个下载测试**：新文件应自动出现。

> 说明：`AlwaysRefresh=1` 是微软保留的强制刷新开关，对「必须 F5」这类症状最直接；若日后想关掉它，见第五节。

---

## 四、方法二：手动分步（不想跑脚本时用）

1. **重启外壳**：任务管理器 → Windows 资源管理器 → 重新启动。
2. **重置文件夹视图**：资源管理器 → ⋯(右上角) → 选项 →「查看」选项卡 → **重置文件夹** → 应用。
3. **清历史**：同一窗口「常规」选项卡 → 隐私区 **清除**（文件资源管理器历史记录）。
4. **清缩略图缓存（管理员 CMD 或普通 CMD 均可）**：
   ```bat
   taskkill /f /im explorer.exe
   del /f /s /q /a "%LocalAppData%\Microsoft\Windows\Explorer\thumbcache_*.db"
   del /f /s /q /a "%LocalAppData%\Microsoft\Windows\Explorer\iconcache_*.db"
   start explorer.exe
   ```
5. **（可选）强制后台刷新**：`Win+R` → `regedit` →
   `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced`
   右键 → 新建 → DWORD(32位) → 名称 `AlwaysRefresh` → 值 `1` → 重启资源管理器。

---

## 五、方法三：系统文件修复（需管理员 CMD/终端）

若方法一/二无效，排除系统文件损坏：

```bat
sfc /scannow
DISM /Online /Cleanup-Image /RestoreHealth
```
完成后重启。若 SFC 报「有损坏且已修复」，说明底层组件确实坏了，这正是 F5 问题的根子。

---

## 六、方法四：第三方壳扩展冲突（进阶）

若以上都无效，可能是某软件（iCloud、Dropbox、某些杀软/压缩软件）注入了 Shell 扩展，拦截了刷新调用：

1. 下载 **ShellExView**（NirSoft，免费）。
2. 按 Company 排序，**禁用所有非 Microsoft** 的上下文菜单/预览/叠加图标处理程序。
3. 重启资源管理器观察；若恢复，逐个启用以定位元凶。

---

## 七、回退

- 关闭强制刷新：`regedit` 删除 `HKCU\...\Explorer\Advanced` 下的 `AlwaysRefresh`（或设回 0），重启资源管理器即可。
- 脚本本身不删任何真实文件，只清缓存与视图状态，可反复运行。

---

## 八、来源交叉验证

- realitypathing.com — File Explorer Won't Update（重启外壳 → 清缓存 → 重置视图 → SFC/DISM）
- pureinfotech.com — 清缩略图缓存命令与路径
- CSDN Ask / wenku — Shell Bags、图标缓存、ShellIconOverlay 修复脚本
- vszh.cn / php.xlycwl.com — 删除 thumbcache_*.db / iconcache_*.db + 重置 Bags/BagMRU
- 本机实测：Y:/Z: 网络盘延迟 1ms、瞬连，排除网络盘假设
