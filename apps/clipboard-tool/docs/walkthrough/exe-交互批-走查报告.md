# exe 桌面版 · 交互修复批场景走查报告（2026-09-04）

> **对象**：clipboard-exe（WPF 桌面单机剪贴板工具）
> **范围**：本次待 commit 的交互批改动（git diff 触及 7 文件）
> **方法**：scenario-walkthrough v3 变更驱动定向走查 —— git diff → 能力清单(C) → 素材剧本(S) → 深度/快速分级走查 → 报告
> **证据**：全部代码自读验证（MainWindow.xaml/.cs、App.xaml.cs、ModalHost.cs、Themes/Styles.xaml），文件:行号标注
> **注意**：本仓库 docs/walkthrough 既有素材/报告均属 **Web 版**(app.js)，本报告为 **exe 桌面版** 独立首份

---

## 一、能力清单（本次走查聚焦 C-ID，基于 git diff 变更面）

| C-ID | 能力 | 位置(文件:行号) |
|---|---|---|
| C-01 | 顶栏整体可折叠(折叠=整条 Collapsed / 展开=完整顶栏) | MainWindow.xaml L30-60 / L514 |
| C-02 | 展开钮 TopExpandBtn(工具条行1搜索框左侧,小图标⌄/⌃) | MainWindow.xaml L74-77 / L515 |
| C-03 | 置顶钮 PinBtn 下沉工具条行2操作组最左(常驻) | MainWindow.xaml L110-112 / L477 |
| C-04 | 工具条行1 3列布局(展开钮|搜索|存入) | MainWindow.xaml L68-85 |
| C-05 | 全局 ToolTip 圆角矩形样式 | Themes/Styles.xaml L971-1000 |
| C-06 | 激活自动弹窗(OnActivated→200ms延迟→TryAutoPrompt) | MainWindow.xaml.cs L415-436 / L106-133 |
| C-07 | ModalHost 激活稳定保护期(GuardWindow 450ms + 延迟关 160ms) | Controls/ModalHost.cs L44-46 / L156-170 |
| C-08 | 单实例唤醒(命名 EventWaitHandle + 后台线程 → WakeMainFromSecondInstance) | App.xaml.cs L78-97 / L145-165 |
| C-09 | 托盘/二次唤起置前(ForceForeground) | MainWindow.xaml.cs L547-572 |

## 二、覆盖矩阵（单机桌面 → 无后端/并发/离线/多角色，L5/L6/L7 整列 N/A）

```
          | 正常 | 空态/边界 | 误操作 | 状态推进 | 并发 | 离线 | 权限 | 故障注入
顶栏折叠  | S-01 |  S-03     | S-03   |  S-02    |  N/A | N/A | N/A |  N/A
置顶下沉  | S-04 |           |        |          |      |     |     |
ToolTip   | S-05 |           |        |          |      |     |     |
激活弹窗  | S-06 |  S-08     |        | S-06     |      |     |     |  S-07
单实例唤醒| S-09 |  S-10     |        | S-09     |      |     |     |
```

L4-L9 覆盖：L4(S-02/S-06)✅ ｜ L8(S-03/S-10)✅ ｜ L9(S-07)✅ ｜ L5/L6/L7=单机不适用(理由注明)

## 三、素材剧本 + 走查结果

### S-01 | L1 单链直通（启动→默认收起顶栏）｜ 深度 ✅ 通
- 操作：启动 exe → 期望：顶栏(Row0)默认收起、整条消失，搜索框顶到工具条顶部。
- 证据：`MainWindow.xaml.cs` L44 `_headerExpanded`(默认 false) → L82 构造末尾 `ApplyTopBarState()` → L514 `TopBar.Visibility = _headerExpanded?Visible:Collapsed`。
- 通过：默认收起逻辑无缺。

### S-02 | L4 状态机往返（展开⇄收起切换）｜ 深度 ✅ 通
- 操作：点"⌄"(展开顶栏) → 顶栏出现(含 数据管理/退出/本地) → 点"⌃"(收起) → 顶栏整条消失。
- 证据：`ToggleTopBar_Click` L502 翻转 `_headerExpanded`；`ApplyTopBarState` L515-518 切 Content 与 ToolTip；TopExpandBtn 在工具条内(非 TopBar 内) L74，折叠态仍可见。
- **关键验证**：TopExpandBtn 挂在工具条行1(XAML L74)，**不在被 Collapsed 的 TopBar 内部** → 折叠后仍可点。✅
- **置顶不受影响**：PinBtn 在工具条行2(XAML L110)，TopBar Collapsed 不影响。✅

### S-03 | L8 边界/误操作（折叠态下"退出/数据管理"入口）｜ 深度 🟡 UX 权衡
- 操作：折叠态(默认) → 找"退出/数据管理"按钮。
- 结果：这两钮在 TopBar 内(顶栏 L54-57)，折叠态被 Collapsed → **不可见**，须先点"⌄"展开才能用。
- 判定：**有意的设计取舍**(用户要求折叠省空间、展开才有管理项)，非 bug。但需用户确认：单机托盘工具，"退出"是否也走托盘菜单为主？主窗"退出"钮在折叠态需 2 步(展开→退出)。**建议拍板**：可接受 or 需在托盘/工具条保留一个常驻"退出"直达入口。

### S-04 | L1 置顶钮常驻可点 ｜ 深度 ✅ 通
- 操作：折叠态点"📌 置顶" → 窗口置顶；再点取消。
- 证据：PinBtn 工具条行2 XAML L110；`PinBtn_Click` L477 设 `Topmost` + `_settings.AlwaysOnTop` 持久化 + Save。
- 通过：不依赖顶栏可见性，折叠/展开态均可点。持久化逻辑完整(L72-73 启动恢复)。

### S-05 | L1 ToolTip 圆角矩形 ｜ 深度 ✅ 通
- 操作：hover 任意按钮 → 期望深色**圆角矩形**(非直角、非梭形)。
- 证据：`Styles.xaml` L991 CornerRadius 改绝对 `6`(弃 RadiusPill=99)+ClipToBounds，L988-995 模板圆角 Border。
- 通过：**该改动已清 obj/bin 强编**，产物 15:55，BAML 必新(见下方"验证")。

### S-06 | L4 激活→自动弹存卡窗 时序链 ｜ 深度 ✅ 通
- 操作：主窗在后台、剪贴板残留内容 → 点击主窗激活 → 期望存卡窗稳定出现可点(不闪没)。
- 证据：`OnActivated` L415-428 → 200ms DispatcherTimer → `ActivationTimer_Tick` L431-436(`IsActive && !ModalHost.IsOpen`) → `PromptFromActivation` → `TryAutoPrompt` L121-133。
- **seq 去重防线**：`TryTakeClipboardSeq` L113-119，剪贴板未变则不重复弹 → 不会因反复失活/激活误弹盖操作。✅
- **防"闪没"**：ModalHost `GuardWindow` 450ms 内 Deactivated 走 `_closePending`+160ms 延迟(S-08 细看)。

### S-07 | L9 故障注入（激活时序竞争：_closeGuard 延迟关 vs 弹窗重激活）｜ 深度 ✅ 通
- 注入：弹窗打开瞬间用户点回主窗 → 弹窗 Deactivated → _closePending 置 true + 160ms guard → 用户又点回弹窗 → win.Activated 取消 pending。
- 证据：`ModalHost.cs` L156 `win.Activated += 取消`；L160-170 Deactivated 分 GuardWindow 内(延迟)/外(立即)。
- 判定：WPF Dispatcher 单线程串行，guard Tick 与 Activated 不会真并发，先到先判定，互斥清晰(_closePending 被两处 Stop/清)。**无竞态缺陷**。✅

### S-08 | L8 边界（快速连续开关两个弹窗）｜ 深度 ✅ 通
- 操作：弹窗A打开→关→立即开弹窗B。
- 证据：`Show` L141-143 每次重置 `_openedAt/_closePending/_closeGuard.Stop`；`Close` L208-211 也清 pending+Stop guard → B 打开时 A 的残留 guard 已被清。**防御到位**。✅

### S-09 | L1 单实例第二实例唤醒 ｜ 深度 ✅ 通
- 操作：程序运行中(含托盘态/最小化态) → 再双击 exe → 期望主窗恢复前置。
- 证据：App.xaml.cs L78-97 第一实例持命名 `EventWaitHandle`+后台线程 `WaitOne()`→`Dispatcher.BeginInvoke`→L86 `_main.WakeMainFromSecondInstance()`；第二实例 L145-165 `SignalExistingInstance()`→`OpenExisting().Set()`。
- **关键**：主窗 Hide 托盘后 `MainWindowHandle=0` 的老问题——新法不依赖句柄，事件直接驱动第一实例自 Show。**托盘/最小化态都可靠**。✅
- Dispatcher 上下文正确(L83 在 App 实例内访问 `this.Dispatcher`=UI 线程 Dispatcher)。✅

### S-10 | L8 边界（极早启动竞争：第二实例在第一实例主窗未装配时唤醒）｜ 深度 ✅ 通
- 操作：程序刚启动(ms 内)第二实例双击唤醒。
- 证据：App.xaml.cs L86-94 `if(_main!=null) return`，否则 DispatcherTimer 50ms 轮询重试到 `_main` 装配。
- 判定：正常装配数毫秒完成，timer 兜底充分。✅

## 四、问题清单

| 编号 | 级别 | 问题 | 位置 | 受影响链路 | 风险分 L×I | 影响 | 状态 |
|---|---|---|---|---|---|---|---|
| P-E1 | 🟡 UX | 折叠态"退出/数据管理"藏进二级展开入口，需 2 步才能到达 | MainWindow.xaml L54-57(顶栏内) | S-03 | 2×2=4(<8 可选) | 单机托盘工具，主窗退出钮通常非首选(托盘退出为主) | **已拍板·保持现状**(2026-09-04 用户确认接受) |
| P-E2 | ⚪ 极低 | TopBrand/TopRightOps 保留 x:Name 但 cs 不再独立控制可见性(由父 TopBar 统一控制)，属遗留字段 | MainWindow.xaml L35/L42 | 无(纯代码卫生) | 1×1=1 | 仅轻微冗余 | **已修复**(2026-09-04 移除 x:Name) |

**🔴 真缺陷：0 个**　🟡 UX：1 个(需拍板)　⚪ 极低：1 个(可选清理)

## 五、覆盖矩阵结论

- exe 桌面版**首次**走查覆盖：L1/L2/L4/L8/L9 均有素材且验证通过。
- L5(并发)/L6(离线)/L7(权限) = 单机无后端应用，**结构上不适用**(整列 N/A，理由：无多端共享状态/无网络链路/无角色)。
- 盲区无(变更面 9 个 C-ID 全被素材触及)；回归面(git diff 未触及 批量/标签/搜索/服务 文件)已隔离确认。

## 六、走查验证的良好点（程序扎实之处）

1. **seq 去重防线**：激活/剪贴板事件/反复切窗共用 `_lastHandledSeq` 收敛，杜绝重复弹窗盖操作。
2. **时序保护成体系**：激活 200ms 延迟 + ModalHost 450ms GuardWindow + 160ms 延迟关 + Activated 取消 —— 三层互锁，代码注释对根因(Win32 消息序)解释透彻，且**明记"禁用按用户输入取消"的防回归铁律**(曾踩过回归)。
3. **单实例唤醒重设计**：弃 `MainWindowHandle`(托盘态=0 缺陷)改命名事件驱动，连极早启动竞争都有 50ms 轮询兜底。
4. **XAML 责任清晰**：展开钮/置顶钮都下沉到工具条(不被折叠隐藏)，符合"常驻控件不得入随栏隐藏区"的防回归规则。
5. **ToolTip 一处 Style 全局生效**，低高度禁用 RadiusPill=99 的坑已沉淀(与 Colors.xaml RadiusSt 设计一致)。
6. 回归面隔离：git diff 未触碰数据层/批量/搜索/标签/服务，构建 0 错误 + selftest 223 断言全绿。

## 七、结论三问

1. **链路逻辑通顺吗？** 通顺。折叠状态机、激活弹窗时序、单实例唤醒三条新链全部自洽，无代码级断链。
2. **是补丁叠补丁吗？** 不是。每处都有单点归属：折叠=ApplyTopBarState 单一函数；弹窗时序=ModalHost 统一宿主；唤醒=App 单实例层。且文档记录了"错误修法→正确修法"的完整演进，非堆叠。
3. **会无法操作/出 bug 吗？** 代码层面无 🔴。唯一 UX 权衡 P-E1(折叠态退出入口)需你拍板；其余依赖**真机 GUI 验收**(自动化不覆盖真实鼠标/激活时序)。

## 八、建议处理（按风险分排序）

| 优先级 | 项 | 建议 |
|---|---|---|
| 待拍板 | P-E1 | 确认折叠态"退出/数据管理"藏展开入口可接受？若否，在托盘/工具条加常驻直达 |
| 可选 | P-E2 | 后续清理冗余 x:Name(不影响功能) |
| 必做 | — | **真机 GUI 验收**：①折叠态仅见搜索行左小"⌄"、展开/收起往返正常 ②📌置顶折叠态可点且持久化 ③hover 各按钮 ToolTip 为圆角矩形 ④托盘态再双击 exe 唤起主窗 ⑤后台点击主窗激活存卡窗稳定出现不闪没 ⑥点空白关弹窗仍正常 |

---

# 第二轮 · 高复杂度增量走查（用户要求加案例复杂度，2026-09-04 16:4x）

> 目标：第1轮只覆盖 diff 主链的 L1/L2/L4；本轮**上 L4 组合长链 / 真值表 / L8 边界 / 探索盲区**，逼出单链走查漏掉的"状态交叉"问题。
> 补充证据：MainWindow.BatchOps.cs、public/app.js(Web 版对齐源)、UpdateColumnWidth、OnPreviewKeyDown、ModalHost 全量。

## 本轮素材剧本 + 结果

### S-11 | L4 组合长链（批量 × 折叠顶栏往返）｜ 深度 ✅ 通
- 操作：进批量模式勾选若干卡 → 点"⌃/⌄"折叠/展开顶栏(往返 2 次) → 检查勾选态与计数 → 退出批量。
- 证据：折叠只走 `ApplyTopBarState`(L511-519 仅切 Visibility/Content/ToolTip)，**不调 RefreshWall、不清 `_batchSel`**；折叠/展开改变 WallPanel 宽 → 仅 `UpdateColumnWidth`(L399 设 ItemWidth，非破坏、不碰批量数据)。
- 通过：批量勾选在折叠切换后**视觉与数据均保持**，退出批量正常清空(L31)。✅

### S-12 | 真值表（激活弹窗触发 × 批量/折叠/弹窗状态）｜ 深度 ✅ 通
- 条件：`_everActivated ∧ IsActive ∧ ¬ModalHost.IsOpen`(ActivationTimer_Tick L434)。
- 全组合核对：
  - 折叠态激活 → 折叠不耦合弹窗逻辑(_headerExpanded 仅在 L44/504/514-516) → 照常弹 ✅
  - 批量模式激活 → SetBatchMode/RefreshWall 不碰 _activationTimer → 照常弹，存卡窗(ModalHost 居中)盖在底部批量条上，关窗后批量态仍在 ✅
  - ModalHost.IsOpen=true(弹窗开着)再切回 → `!IsOpen` 不成立 → 不重复弹 ✅（防叠弹窗）
  - 用户此间切走 → `IsActive` false → 作罢 ✅
- 通过：无"批量/折叠下误弹或漏弹"矛盾组合。✅

### S-13 | L8 边界（批量勾选跨过滤持久 → 看不见的勾选被批量删除？）｜ 深度 🟢 确认与 Web 对齐(非缺陷)
- 操作：批量勾选 A → 搜索/切 tab 把 A 过滤出可见集(_visibleIds 变，A 仍留 `_batchSel`) → 计数仍含 A → 点"🗑 删除"确认。
- 风险假设：A 不可见却被删 → 误删?
- 对账：Web 版 `app.js` **L20 `batchSel = new Set(); // 已选条目 id 集合（按 id 存，跨过滤/重排保持）`**——**"跨过滤/重排保持" 是有意设计**；exe 行为与 Web 完全一致。且删除/批量确认弹窗都会显示 `{_batchSel.Count}` 总数(含隐藏项)，语义"勾选即作用"透明。
- 判定：**🟢 与源语义一致，非缺陷**。勾选按 id 持久是特性(过滤只是隐藏，不取消已明确勾选的操作意图)。—— 若担心误删，可加"可见 N + 含隐藏 M"提示，属增强非修 bug。

### S-14 | L9 故障注入（剪贴板访问异常容错）｜ 快速 ✅ 通
- 注入：Clipboard.GetText 抛 COM/拒绝 / 纯图片剪贴板 / 空文本。
- 证据：`TryAutoPrompt` L125-129 try/catch + `IsImageOnlyClipboard()` 分支 + seq 去重；`OpenPasteDialog` L143-145 同样 try/catch。
- 通过：剪贴板异常或不可用(如 selftest/无桌面)均不崩。✅

### S-15 | 引用面隔离（折叠控件与业务解耦验证）｜ 深度 ✅ 通（强良好点）
- grep 全 cs：`_headerExpanded/TopBar/TopExpandBtn/TopBrand/TopRightOps` **只出现在 MainWindow.xaml.cs 折叠逻辑内**(L44/504-516) 与 XAML 宿主，**无任何业务代码依赖顶栏可见性**。
- 通过：折叠是**纯净 UI 布局操作**，与批量/搜索/服务/快捷键完全解耦 → 排除"折叠误隐藏业务依赖控件"类回归。✅

### S-16 | 探索/边界（托盘隐藏主窗 × 弹窗持焦残留 / 全局快捷键 × 批量）｜ 快速 🟡 极低(需真机)
- 场景 A：存卡窗持焦时经托盘隐藏主窗(罕见) → WPF owned window 不随 owner.Hide 自动隐藏，弹窗可能短暂残屏。风险极低：托盘隐藏入口(点 X/最小化)都需先点主窗→抢弹窗焦点→Deactivated 关闭，真正持焦隐藏不可达(托盘仅 显示/退出)。
- 场景 B：批量模式下 Ctrl+V 仍开存卡窗 / 空格跳搜索框 → 因批量靠鼠标点选无键盘输入冲突，可接受。
- 判定：🟡 极低风险，放真机验收项兜底，不代码修。

## 本轮问题清单（增量）

| 编号 | 级别 | 问题 | 位置 | 受影响链路 | 风险分 L×I | 影响 | 状态 |
|---|---|---|---|---|---|---|---|
| P-E3 | 🟡 UX(增强) | S-13 删除确认只报总数 N，不含"其中 M 条当前被过滤隐藏"细分——用户可能忘掉被过滤掉的勾选 | BatchDelBtn_Click(删除确认文案) / OpenBatchTagModal(hint) | S-13 | 2×3=6(<8 可选) | 与 Web 语义一致，非误删缺陷；可增强提示 | **已修复**(2026-09-04 删除确认加"\n⚠ 其中 M 条…仍会被删除"；批量标签弹窗 open 时加警告行) |
| P-E4 | ⚪ 极低 | S-16 托盘隐藏+弹窗持焦的理论残留窗口(实际不可达) | ModalHost owner.Hide | S-16 | 1×2=2 | 纯理论，真机确认即可 | 待真机确认 |

**本轮🔴 = 0**（增量问题仅 2 个可选/极低项，均非阻断）

## 本轮结论三问（迭代停止判断）

1. **收敛了吗？** 是。第1轮 0🔴 + 本轮 0🔴；本轮 9 条素材(S-11~S-16 含真值表/组合/对齐)全部通过或仅🟡可选 → **新🔴=0，新发现递减，符合收敛**。
2. **是补丁叠补丁吗？** 否。真值表与组合核对确认每处状态有单一归属。
3. **可以停或继续？** 代码层面已达收敛。剩余全部是 **真机 GUI 项**(自动弹时序/托盘唤起/折叠往返/勾选跨过滤)，非读码能定。→ **建议进入真机验收收敛**，如验收发现问题再针对性回归，不继续无限加复杂度空转。

## 本轮累计建议（两轮合并）
- **已修复**(2026-09-04)：P-E2(冗余x:Name 移除)、P-E3(批量删除确认 + 批量标签弹窗加"含 M 条被过滤隐藏"提示，构建 0 错误 + selftest 通过)。
- **已拍板**：P-E1(折叠态退出入口)→ 用户确认保持现状。
- **待真机确认**：P-E4(托盘×弹窗理论残留)、下方真机验收清单。
- 三轮封顶：代码层面问题已清零(仅剩 P-E4 理论项 + GUI 真机项)，若真机验收零问题即可 commit+push。

## 九、真机验收清单（GUI 交互自动化不覆盖，须人工）

- [ ] 启动默认顶栏整体消失，搜索行左侧一个极小"⌄"图标钮
- [ ] 点"⌄"→完整顶栏展开(📋剪贴板/本地/数据管理/退出)，点"⌃"→收起
- [ ] 折叠/展开态 📌 置顶都常驻可点，重启后置顶状态恢复
- [ ] hover 编辑/归档/列数/同步/置顶/⌄/＋存入 → ToolTip 深色圆角矩形(非直角/梭形)
- [ ] 程序在跑(含收进托盘后)再双击 exe → 主窗恢复前置，多次双击每次唤起
- [ ] 后台态点击主窗激活 + 剪贴板有内容 → 存卡窗稳定出现可点(不闪没)；Alt-Tab 切回同样
- [ ] 点弹窗外空白/主窗其它区域 → 弹窗正常关闭
