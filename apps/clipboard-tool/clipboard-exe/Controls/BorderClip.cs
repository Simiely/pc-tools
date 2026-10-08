// Controls/BorderClip.cs - 给 Border 加 Clip=RectangleGeometry(RadiusX/Y=CornerRadius),裁剪所有渲染到圆角矩形内。
// 用途：WPF 原生 DropShadowEffect / Effect 永远按元素矩形 bbox 渲染,圆角 Border 的 Effect 阴影外缘呈方形（深色主题下肉眼可见"圆角处见方形"）。
// 标准解法：Clip=RectangleGeometry(0,0,W,H,r,r) 把 Effect 阴影一并裁到圆角矩形内 → 阴影贴边甚至消失，但圆角彻底干净、彻底消除方形外缘。
// 用法：XAML 中给目标 Border 加 `local:BorderClip.ClipToRadius="True"`。Loaded/SizeChanged 自动按 Border.ActualWidth/Height 更新 Clip；CornerRadius 非均匀时取 TopLeft 兜底。
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ClipboardExe.Controls;

public static class BorderClip
{
    public static readonly DependencyProperty ClipToRadiusProperty =
        DependencyProperty.RegisterAttached("ClipToRadius", typeof(bool), typeof(BorderClip),
            new FrameworkPropertyMetadata(false, OnClipToRadiusChanged));

    public static void SetClipToRadius(DependencyObject d, bool v) => d.SetValue(ClipToRadiusProperty, v);
    public static bool GetClipToRadius(DependencyObject d) => (bool)d.GetValue(ClipToRadiusProperty);

    private static void OnClipToRadiusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Border b) return;
        if ((bool)e.NewValue)
        {
            b.Loaded += (_, _) => UpdateClip(b);
            b.SizeChanged += (_, _) => UpdateClip(b);
            UpdateClip(b);
        }
    }

    private static void UpdateClip(Border b)
    {
        if (b.ActualWidth <= 0 || b.ActualHeight <= 0) return;
        var r = b.CornerRadius;
        if (r.TopLeft == 0 && r.TopRight == 0 && r.BottomLeft == 0 && r.BottomRight == 0) { b.Clip = null; return; }
        var radius = r.TopLeft; // 取左上角作为统一圆角(项目内全部用均匀 CornerRadius)
        b.Clip = new RectangleGeometry(new Rect(0, 0, b.ActualWidth, b.ActualHeight), radius, radius);
    }
}