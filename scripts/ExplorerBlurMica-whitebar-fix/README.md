# ExplorerBlurMica 底部白条修复

[![在线预览](https://img.shields.io/badge/在线预览-点击访问-E55D6B)](https://simiely.github.io/ExplorerBlurMica-whitebar-fix/)

## 项目简介

[ExplorerBlurMica](https://github.com/Maplespe/ExplorerBlurMica) 是一款为 Windows 11 资源管理器（`explorer.exe`）注入 **Mica / Acrylic** 等材质背景的开源工具。

本项目配套提供一段**自动修复脚本**，解决它的一个已知渲染问题：

> 当通过浏览器（Chrome / Edge 等）点击「打开所在文件夹」拉起下载目录时，资源管理器窗口**底部状态栏会出现一条白条**，手动拖动窗口一下才恢复正常。

本仓库包含：

- 自动 nudge 脚本（**方案②：事件驱动，零轮询**）—— 彻底免去「手动拖动」的麻烦
- 完整的中文使用说明
- 开发 / 排查记录 `DEVELOPMENT.md`：记录根因分析与多方案对比，以后遇到同类「注入式皮肤首绘漏铺」问题可直接复用

## 许可与归属

- **ExplorerBlurMica** 是 [Maplespe/ExplorerBlurMica](https://github.com/Maplespe/ExplorerBlurMica) 的开源项目，采用 **LGPL-3.0 / GPL-3.0** 许可。本仓库 `vendor/ExplorerBlurMica/` 内仅作**个人备份与配套分发**，版权归原作者所有，源代码以上游为准。
- 本仓库中 `explorer-blur-fix-*.ps1`、`run-nudge-*.bat` 及文档为作者原创，采用 **MIT License**（见根目录 [`LICENSE`](LICENSE)），可自由使用、修改、再分发。

## 问题现象

1. 资源管理器手动打开时正常；
2. 浏览器打开下载目录（`ShellExecute` 拉起）时，窗口底部有一条白条；
3. 拉动窗口一下，白条立即消失。

## 修复原理（简述）

`explorer-blur-fix-event.ps1` 通过 `SetWinEventHook(EVENT_OBJECT_CREATE)` 监听新建的 `CabinetWClass`（资源管理器）窗口，创建后延迟约 450ms，用 `SetWindowPos` 将窗口**高度 +1px（仅下边框向下探 1px 再弹回）**，触发一次 `WM_SIZE`，强制 ExplorerBlurMica 重铺 DWM 背景，自动消除白条。

特点：**零轮询**（事件驱动，常驻进程 CPU 平时 idle）、每个窗口只处理一次、安全可逆（不改 DLL / 不改配置 / 不碰系统）。

## 使用方法

1. 安装 ExplorerBlurMica（见 `vendor/ExplorerBlurMica/README.md`）并保持 `config.ini` 的 `effect=2`；
2. 双击 `run-nudge-event.bat` 启动修复脚本（窗口一闪后隐藏，无界面）；
3. 之后新开的资源管理器窗口（含浏览器拉起的下载目录）会被自动修复；
4. **停止**：任务管理器结束那个 `powershell.exe` 进程即可；
5. **开机自启（可选）**：把 `run-nudge-event.bat` 的快捷方式放进「运行」→ 输入 `shell:startup` 打开的启动文件夹。

> 详细说明（原理、参数、调优、注意事项）见 [`使用方法与注意事项.md`](使用方法与注意事项.md)。

## 注意事项

- **必须在自己机器双击 `.bat` 运行**；不要在受限的 PowerShell 环境（如某些工具内置终端）里跑，那里 `Add-Type` 可能被拦截。`.bat` 已带 `-ExecutionPolicy Bypass` 兜底。
- 只对**之后新开的**窗口生效；已打开的旧窗口需关掉重开（或手动拖一下）。
- 这是「自动复刻你拖动一下」，不是根治——白条从源头未消失，只是被自动即时修掉，你不用再动手。
- 常驻一个 `powershell.exe`（约 30–60MB 内存），CPU 平时 idle；结束进程即完全还原，零残留。

## 文件结构

```
ExplorerBlurMica-whitebar-fix/
├── LICENSE                        # 本仓库原创代码的许可（MIT）
├── README.md                      # 本文件
├── DEVELOPMENT.md                  # 开发 / 排查记录（根因、方案对比、可复用经验）
├── explorer-blur-fix-event.ps1    # 修复脚本（推荐，事件驱动）
├── run-nudge-event.bat            # 静默启动器
├── 使用方法与注意事项.md           # 详细中文使用说明
├── legacy/                        # 旧版（轮询实现，已弃用但保留参考）
│   ├── explorer-blur-fix-nudge.ps1
│   └── run-nudge.bat
└── vendor/
    └── ExplorerBlurMica/          # 上游程序（LGPL，版权归 Maplespe）
        ├── ExplorerBlurMica.dll
        ├── config.ini
        ├── register.cmd
        ├── uninstall.cmd
        ├── LICENSE                # GPL-3.0
        ├── COPYING.LESSER         # LGPL-3.0
        └── README.md              # 归属与来源说明
```

## 相关链接

- 上游项目：<https://github.com/Maplespe/ExplorerBlurMica>
- 开发记录（根因 / 方案层级 / 排查路径）：[`DEVELOPMENT.md`](DEVELOPMENT.md)

## 文档索引

| 文档 | 给谁看 | 内容 |
|---|---|---|
| [`AGENTS.md`](./AGENTS.md) | AI / 未来的你 | 技术要点、关键坑（首绘竞态/事件驱动/MToolBox）、常用命令 |
| [`DEVELOPMENT.md`](./DEVELOPMENT.md) | 开发者 | 现象到修复完整推导：机理、方案层级对比、nudge 实现 |
| [`CHANGELOG.md`](./CHANGELOG.md) | 所有人 | 版本变更记录 |
| [`使用方法与注意事项.md`](./使用方法与注意事项.md) | 用户 | 使用说明 |
