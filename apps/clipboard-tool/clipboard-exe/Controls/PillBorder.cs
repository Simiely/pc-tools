// Controls/PillBorder.cs - Pill 圆角附加属性
// WPF 的 Border.CornerRadius 在大数值（如 99 的 pill）下不会自动钳制：
// 高度窄（如 40px）时，上下角的水平半径仍为 99，垂直半径被限到 20，
// 渲染成椭圆（oval）而不是真正的 pill（两端半圆 + 上下直线）。
// 此附加属性监听 SizeChanged 自动把 CornerRadius 钳制为 min(设定值, w/2, h/2)。
// 用法：<Border PillBorder.Pill="99" .../> 替代 CornerRadius=99 + ClipToBounds。
using System.Windows;
using System.Windows.Controls;

namespace ClipboardExe.Controls;

public static class PillBorder
{
    public static readonly DependencyProperty PillProperty =
        DependencyProperty.RegisterAttached("Pill", typeof(double), typeof(PillBorder),
            new PropertyMetadata(0.0, OnPillChanged));

    public static void SetPill(DependencyObject d, double v) => d.SetValue(PillProperty, v);
    public static double GetPill(DependencyObject d) => (double)d.GetValue(PillProperty);

    private static readonly SizeChangedEventHandler _sizeHandler = Apply!;
    private static readonly RoutedEventHandler _loadedHandler = OnLoaded!;

    private static void OnPillChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Border b) return;
        // 用具名 handler 才能正确 -=
        b.SizeChanged -= _sizeHandler;
        b.SizeChanged += _sizeHandler;
        b.Loaded -= _loadedHandler;
        b.Loaded += _loadedHandler;
        Apply(b, null);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Border b) Apply(b, null);
    }

    private static void Apply(object? sender, SizeChangedEventArgs? e)
    {
        if (sender is not Border b) return;
        var max = GetPill(b);
        if (max <= 0) return;
        if (double.IsNaN(b.ActualWidth) || b.ActualWidth <= 0 ||
            double.IsNaN(b.ActualHeight) || b.ActualHeight <= 0) return;
        // 真正的 pill：四角半径 = min(max, w/2, h/2)，让两端呈半圆，上下直线
        var r = Math.Min(max, Math.Min(b.ActualWidth / 2.0, b.ActualHeight / 2.0));
        b.CornerRadius = new CornerRadius(r);
    }
}