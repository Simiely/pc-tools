// Services/TrayDarkMenu.cs - 托盘右键菜单固定暗色渲染器
// 背景：主程序是固定暗色 UI（BgBrush #1A1A1A / ElevBrush #1F1F1F / TextBrush #DADADA / 金 hover），
// 但托盘用的是 WinForms NotifyIcon + ContextMenuStrip，其默认 Professional 渲染是浅色（白底黑字），
// 与主窗口暗色界面割裂、也不符合 Win11 深色观感。托盘右键菜单不随系统主题切（无需），固定暗色即可与主程序统一。
// 实现：继承 ProfessionalColorTable + ToolStripProfessionalRenderer，把全部状态色对齐主窗暗色令牌；
// 不动 WPF 主程序、无实验性 API(WFO5001)、不依赖 SetColorMode。
using System.Drawing;
using System.Windows.Forms;

namespace ClipboardExe.Services;

/// <summary>托盘右键菜单暗色配色表（对齐 Themes/Colors.xaml 令牌：面板 Elev#1F1F1F / 文字 #DADADA / 边框 #3D3D3D / 金 hover）。</summary>
internal sealed class TrayDarkColorTable : ProfessionalColorTable
{
    // 主窗令牌（Colors.xaml 同值）
    private static readonly Color Bg = Color.FromArgb(0x1A, 0x1A, 0x1A);       // 窗口背景
    private static readonly Color Elev = Color.FromArgb(0x1F, 0x1F, 0x1F);     // 面板/卡片底
    private static readonly Color ElevHi = Color.FromArgb(0x2A, 0x2A, 0x2A);   // 悬浮/凸起
    private static readonly Color Border = Color.FromArgb(0x3D, 0x3D, 0x3D);   // 边框/分隔
    private static readonly Color Text = Color.FromArgb(0xDA, 0xDA, 0xDA);     // 正文
    private static readonly Color Accent = Color.FromArgb(0xC9, 0xA9, 0x6E);   // 金主强调
    private static readonly Color Hover = Color.FromArgb(0x33, 0xC9, 0xA9, 0x6E); // 金 16% 半透明 hover 底(对齐 AccentSoftBrush)

    public override Color ToolStripDropDownBackground => Elev;
    public override Color ImageMarginGradientBegin => Elev;
    public override Color ImageMarginGradientMiddle => Elev;
    public override Color ImageMarginGradientEnd => Elev;
    public override Color MenuBorder => Border;
    public override Color MenuItemBorder => Border;

    // 菜单项常规：透明底 + 亮字（文字色经 TextColorTable 由下方自定义 ToolStripRenderer 绘制更可控，
    // 这里背景统一透明即可——避免 MenuItemSelected 与 MenuItem 背景不一致造成的底色块）
    public override Color MenuItemSelected => Hover;
    public override Color MenuItemSelectedGradientBegin => Hover;
    public override Color MenuItemSelectedGradientEnd => Hover;
    public override Color MenuItemPressedGradientBegin => ElevHi;
    public override Color MenuItemPressedGradientMiddle => ElevHi;
    public override Color MenuItemPressedGradientEnd => ElevHi;

    public override Color MenuStripGradientBegin => Elev;
    public override Color MenuStripGradientEnd => Elev;
    public override Color StatusStripGradientBegin => Elev;
    public override Color StatusStripGradientEnd => Elev;

    // 分隔线（浅色加深到 Border 档，避免白线突兀）
    public override Color SeparatorDark => Border;
    public override Color SeparatorLight => Elev;

    public override Color ToolStripBorder => Border;
    public override Color ToolStripGradientBegin => Elev;
    public override Color ToolStripGradientMiddle => Elev;
    public override Color ToolStripGradientEnd => Elev;
}

/// <summary>暗色 ToolStrip 渲染器：在 ColorTable 基础上额外兜底 文字/高亮 前景色（ToolStrip 默认文字不受 ColorTable 控，需自绘兜底）。</summary>
internal sealed class TrayDarkRenderer : ToolStripProfessionalRenderer
{
    private static readonly Color Text = Color.FromArgb(0xDA, 0xDA, 0xDA);   // 正文(亮)
    private static readonly Color HoverText = Color.FromArgb(0xFF, 0xFF, 0xFF); // hover 文字(更亮,保证对比)
    private static readonly Color Disabled = Color.FromArgb(0x6E, 0x6E, 0x6E);  // 禁用(对齐 DimBrush)

    public TrayDarkRenderer() : base(new TrayDarkColorTable()) { }

    // 注意：ContextMenuStrip 下拉里的 ToolStripMenuItem 文本绘制统一走 OnRenderItemText；
    // 不另覆 OnRenderMenuItemText，避免同一项文本被两条路径各画一次造成重影。
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        if (!e.Item.Enabled) { e.TextColor = Disabled; }
        else if (e.Item.Selected || e.Item.Pressed) { e.TextColor = HoverText; }
        else { e.TextColor = Text; }
        base.OnRenderItemText(e);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        // 去掉默认的浅色描边，改成细暗边框(用 ColorTable 的 ToolStripBorder)
        e.Graphics.DrawRectangle(new Pen(new TrayDarkColorTable().ToolStripBorder),
            new Rectangle(0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1));
    }
}
