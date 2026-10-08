# Clipboard exe · UI 构建规范

> 目标：WPF 端 UI 与 Web 版（`public/index.html` CSS）**1:1 效果映射**，规范化构建、复用统一格式。
> 原则：**以 Web 版 CSS 为唯一规范源**，改 UI 先查 Web 版对应类，再映射到 WPF 资源，禁止随意自创样式。

## 铁律（可 grep 验证）

1. **颜色/圆角一律取令牌**（`Themes/Colors.xaml`），禁止硬编码。
   - 验证：`grep -nE 'CornerRadius="[0-9]|#" Themes/*.xaml *.xaml` 应为空（Colors.xaml 除外）。
2. **阴影一律走 NeuBorder**（`Kind=Raised/RaisedSm/RaisedLg/Inset/InsetSm/Press`），禁止散落 DropShadowEffect。
   - 例外：tt 按钮"凸起选中"等极小元素允许单投影（TtBtn.on）。
3. **样式复用 BasedOn 链**，命名对齐 Web 类（`.btn.ghost.sm` → `BtnGhostSm`）。
4. **内容必须放进圆角 Border 内部** + `ClipToBounds=True`（WPF Border 圆角不裁剪子内容，直角根源）。
5. **间距/尺寸用 Web 版对应值**（padding/gap 对齐 index.html，如 `.topbar` padding 12 20、`.tb` padding 12 14）。

## Web 类 → WPF 资源映射表

### 层1 · 元素

| Web 类 | WPF 资源 | 关键效果 |
|---|---|---|
| `.btn` | `BtnBase` | elev 底 + RadiusBtn(12)；hover elev-hi；disabled 半透明 |
| `.btn.primary` | `BtnPrimary` | 金色 Accent 底 + 深字 600 |
| `.btn.ghost` | `BtnGhost` | 透明无阴影，hover 砖红 |
| `.btn.ghost.sm` | `BtnGhostSm` | 小号（padding 11,5 / 12px / RadiusBtnSm 10） |
| `.btn.sm.danger` | `BtnDanger` | hover 红（批量删除等） |
| `.store-btn` | `StoreBtn` | **金色 pill**（RadiusPill）+ hover Amber |
| who 区置顶 | `BtnPin` | ToggleButton，开=金色（唯一新增，用户要求） |
| `input/textarea` | `TextBox`（implicit） | inset 底 + RadiusBtn(12) + focus 金边 |
| `.search` | `SearchBox` | **pill 内嵌** + placeholder（Tag 驱动）+ focus 金边 |
| `.tag` / `.tag.on` | `TagChip`（`TagChipOn` 触发） | 胶囊（RadiusPill）+ elev/raised-sm；on=金底深字 600 |
| `.badge` / `.link/.file/.text/.exp` | `Badge`（`BadgeLink` 等变体） | 胶囊小徽章，类型着色 |
| `.opt input[type=checkbox]` | `OptSwitch` | **新拟态开关**：内凹轨道+凸起滑块；选中=金轨道+金滑块+滑动 |
| `.typetab` / `.tt` / `.tt.on` | `Typetab` / `TtBtn` | inset 轨道 14 + 凸起选中（elev+raised-sm+accent2 600） |
| `.file-chip` | `FileChip`（M3） | 文件选择 chip |
| `.paste-badge` / `.paste-clear` | `PasteBadge` / `PasteClear`（M3） | 存入弹窗类型徽章/清空钮 |

### 层2 · 容器

| Web 类 | WPF 资源 | 关键效果 |
|---|---|---|
| `--sh-raised*` / `--sh-inset*` / `--sh-press` | `NeuBorder`（Kind） | **双投影浮雕/内嵌/按下，唯一阴影来源** |
| `.topbar` | NeuBorder Raised + RadiusLg + Elev 底 | 顶栏卡片 |
| `.tb` | NeuBorder Raised + RadiusMd + Elev 底 | 工具条卡片 |
| 滚动条 | `ScrollBar`（implicit） | Border 色 thumb 全圆胶囊 |

### 层3 · 卡片（M3 接入时建 `CardControl`）

| Web 类 | 关键效果 |
|---|---|
| `.clip-card` | NeuBorder Raised + RadiusLg + 等高 190；pinned 金描边；archived 降透明 |
| `.clip-card .pv/.code/.linkbody/.imgwrap/.filebody` | 各类型内容区（inset 内嵌） |
| `.rich-split` | 富文本左右分栏（虚线金边） |
| `.ops .b` | 26px 图标钮（RadiusIcon 8 + inset） |

### 层4 · 弹窗（已落地：ModalHost 静态宿主 + 五个 Dialog）

| Web 类 | 关键效果 |
|---|---|
| `.mask` | 半透明遮罩 + blur |
| `.modal` | NeuBorder RaisedLg + RadiusLg + max-width 450 |

## 新增效果流程

1. 在 `public/index.html` 找到 Web 版对应 CSS 类与效果。
2. 若 WPF 已有资源可复用 → 直接用（BasedOn / 令牌）。
3. 若没有 → 在 Styles.xaml 对应层节新增资源，命名对齐 Web 类，效果值从令牌取。
4. 更新本映射表。

## 当前状态

- ✅ **M1→M5c 全部落地**（2026-09-04）：令牌（Colors：颜色/圆角 6 级/半透明金/开关阴影）、按钮家族（BtnBase/Primary/Ghost/GhostSm/Danger，active=translateY(1px) 对齐 Web）、StoreBtn、BtnPin、SearchBox（内容入圆角 Border + ClipToBounds）、TagChip（on/off 态）、Badge 家族（ContentControl 封装 + Link/File/Text/Exp 变体）、OptSwitch（新拟态开关：内凹轨道+凸起滑块+选中金晕）、Typetab/TtBtn、NeuBorder（双投影）、ScrollBar。
- ✅ 表内控件均已按实际形态落地（命名差异说明）：FileChip/PasteBadge/PasteClear 内联于 `Controls/PasteDialog.xaml`；卡片 = `Controls/CardView.xaml`（含 rich-split 富文本分栏、文件/图片卡体、批量勾选框）；弹窗 = `Controls/ModalHost.cs` 静态宿主（唯一拖动实现点：HTCAPTION 系统级）+ `PasteDialog`/`EditDialog`/`DataDialog`/`JsonDialog`/`TagPicker` 五个 Dialog（无独立 ModalBase 类，`ModalCard` 样式承载卡片外观）。
- 🔎 本表是「Web 类 → WPF 资源」映射索引，新增效果时仍按下方流程四步走，完成后更新本表。
