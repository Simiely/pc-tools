// Controls/NeuBorder.cs - 新拟态容器（还原 Web 版双阴影浮雕 tokens）
// 凸起（Raised）：右下深影 DropShadowEffect(黑 50%) + 左上浅灰高光 DropShadowEffect(#585858 35%) —— 对齐 --sh-raised 双投影，无描边
// 内嵌（Inset/Press）：四边异色 Rectangle 模拟（WPF 无原生 inset 阴影）：top/left 暗 + bottom/right 亮
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace ClipboardExe.Controls;

public enum NeuShadowKind
{
    Raised,    // 外凸：右下深影 + 左上浅灰高光（--sh-raised）
    RaisedSm,  // 外凸-小（--sh-raised-sm）
    RaisedLg,  // 外凸-大（--sh-raised-lg）
    Inset,     // 内嵌（--sh-inset）
    InsetSm,   // 内嵌-小（--sh-inset-sm）
    Press,     // 按下（--sh-press）
}

/// <summary>新拟态容器：Kind 切换 Raised/Inset/Press 视觉（对齐 Web :root 双阴影 tokens）。</summary>
public class NeuBorder : ContentControl
{
    static NeuBorder()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(NeuBorder), new FrameworkPropertyMetadata(typeof(NeuBorder)));
        // 内容默认拉伸填满控件：对齐 Web .topbar/.tb 的 display:flex 容器行为（自身宽度=父 100%），
        // 否则 HorizontalContentAlignment=Left + DockPanel/StackPanel.HorizontalAlignment=Left
        // 会让卡片只占内容需要宽度，父容器拉宽时卡片纹丝不动（窗口自适应失效）。
        HorizontalContentAlignmentProperty.OverrideMetadata(typeof(NeuBorder),
            new FrameworkPropertyMetadata(HorizontalAlignment.Stretch));
        VerticalContentAlignmentProperty.OverrideMetadata(typeof(NeuBorder),
            new FrameworkPropertyMetadata(VerticalAlignment.Stretch));
    }

    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);
        // 强制 Content 自身 Horizontal/VerticalAlignment=Stretch：DockPanel/StackPanel 默认 Left，
        // 仅靠 ContentPresenter 的 Stretch 不足以让它们占满 NeuBorder。
        if (newContent is FrameworkElement fe)
        {
            fe.HorizontalAlignment = HorizontalAlignment.Stretch;
            fe.VerticalAlignment = VerticalAlignment.Stretch;
        }
    }

    public static readonly DependencyProperty KindProperty =
        DependencyProperty.Register(nameof(Kind), typeof(NeuShadowKind), typeof(NeuBorder),
            new FrameworkPropertyMetadata(NeuShadowKind.Raised, FrameworkPropertyMetadataOptions.AffectsRender, OnKindChanged));

    public NeuShadowKind Kind
    {
        get => (NeuShadowKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(nameof(CornerRadius), typeof(CornerRadius), typeof(NeuBorder),
            new FrameworkPropertyMetadata(default(CornerRadius)));

    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    private static void OnKindChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((NeuBorder)d).ApplyKind();

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        ApplyKind();
        // Body 是圆角 Border，Clip=RectangleGeometry 裁剪 ContentPresenter 子元素到圆角矩形内，
        // 防止子元素（如其他 Border/Button）撑出 Body 圆角外显方形。
        if (GetTemplateChild("Body") is Border body) BorderClip.SetClipToRadius(body, true);
    }

    private void ApplyKind()
    {
        var shHi = GetTemplateChild("ShHi") as Border;   // 左上浅灰高光阴影层
        var shLo = GetTemplateChild("ShLo") as Border;   // 右下深影层
        var top = GetTemplateChild("EdgeTop") as Rectangle;
        var bottom = GetTemplateChild("EdgeBottom") as Rectangle;
        var left = GetTemplateChild("EdgeLeft") as Rectangle;
        var right = GetTemplateChild("EdgeRight") as Rectangle;

        switch (Kind)
        {
            case NeuShadowKind.Raised:
            case NeuShadowKind.RaisedSm:
            case NeuShadowKind.RaisedLg:
                // 双投影：右下黑 rgba(0,0,0,.5) + 左上浅灰 rgba(88,88,88,.35)——对齐 --sh-raised tokens
                var blur = Kind == NeuShadowKind.RaisedLg ? 5 : 3;
                var darkOpacity = Kind == NeuShadowKind.RaisedLg ? 0.52 : Kind == NeuShadowKind.RaisedSm ? 0.45 : 0.5;
                var lightOpacity = Kind == NeuShadowKind.RaisedLg ? 0.38 : Kind == NeuShadowKind.RaisedSm ? 0.3 : 0.35;
                SetShadow(shLo, Colors.Black, blur, 1, darkOpacity, 135);
                SetShadow(shHi, Color.FromRgb(88, 88, 88), blur, 1, lightOpacity, 315);
                SetRect(top, null, Visibility.Collapsed);
                SetRect(left, null, Visibility.Collapsed);
                SetRect(bottom, null, Visibility.Collapsed);
                SetRect(right, null, Visibility.Collapsed);
                break;

            case NeuShadowKind.Inset:
            case NeuShadowKind.InsetSm:
            case NeuShadowKind.Press:
                // 内嵌：top/left 暗 + bottom/right 亮（视觉凹陷，无投影）
                SetShadow(shLo, null, 0, 0, 0, 0);
                SetShadow(shHi, null, 0, 0, 0, 0);
                var dark = Kind == NeuShadowKind.Press ? "#85000000" : "#80000000";
                var light = Kind == NeuShadowKind.Press ? "#59383838" : "#52383838";
                SetRect(top, dark, Visibility.Visible);
                SetRect(left, dark, Visibility.Visible);
                SetRect(bottom, light, Visibility.Visible);
                SetRect(right, light, Visibility.Visible);
                break;
        }
    }

    private static void SetShadow(Border? b, Color? color, double blur, double depth, double opacity, double direction)
    {
        if (b == null) return;
        if (color == null) { b.Effect = null; return; }
        b.Effect = new DropShadowEffect
        {
            Color = color.Value,
            BlurRadius = blur,
            ShadowDepth = depth,
            Opacity = opacity,
            Direction = direction,
        };
    }

    private static void SetRect(Rectangle? r, string? color, Visibility v)
    {
        if (r == null) return;
        if (color == null) r.Fill = null;
        else r.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        r.Visibility = v;
    }
}
