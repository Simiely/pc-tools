// Controls/ToastService.cs - 轻提示（对齐 Web 版 #flash .copied-flash 与 .toast-err）
//  - Flash(msg) / Flash(msg, x, y)：金色胶囊（accent 底 + 深字 600），跟随鼠标上方（Placement=Mouse 锚定），1400ms
//  - FlashAtMouse(msg)：显式跟随鼠标（图片复制等无需调用方传坐标）
//  - Error(msg)：红底白字，同样跟随鼠标上方（与成功提示一致），2600ms
// 实现：Popup（独立 Win32 窗口）。统一 Placement=Mouse：由 WPF 按当前光标 + 所在显示器 DPI 内部锚定，
//      规避 GetPosition+Absolute 在高分屏缩放 / 多显示器下坐标错配导致不跟随鼠标的问题。
//      注：所有提示（成功/失败）均跟随鼠标 —— 用户诉求「所有提示信息都跟随鼠标」。
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

// UI 基础设施（弹窗/提示宿主）归位到 Controls：展示层不是"服务"。AppLog 日志仍用 Services。
using ClipboardExe.Services;

namespace ClipboardExe.Controls;

public static class ToastService
{
    private static Popup? _popup;
    private static Border? _body;
    private static TextBlock? _text;
    private static readonly DispatcherTimer Timer = new() { Interval = TimeSpan.FromMilliseconds(1400) };

    private static bool _isError;

    /// <summary>初始化（MainWindow 构造时调用一次；Popup 依赖 Application 存在）。</summary>
    public static void Init()
    {
        if (_popup != null) return;
        _text = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center };
        _body = new Border
        {
            Child = _text,
            Padding = new Thickness(18, 8, 18, 8),
            CornerRadius = (CornerRadius)Application.Current.Resources["RadiusBtn"],
            Effect = CreateShadow(),
        };
        _popup = new Popup
        {
            Child = _body,
            Placement = PlacementMode.Absolute,
            AllowsTransparency = true,
            IsHitTestVisible = false,
            StaysOpen = true,
        };
        Timer.Tick += (_, _) => Close();
    }

    /// <summary>金色提示（跟随鼠标上方，WPF Placement=Mouse 锚定，规避高分屏/多屏坐标错配）。1400ms 自动关闭。</summary>
    public static void Flash(string msg, double? x = null, double? y = null)
    {
        Show(msg, x, y, isError: false, 1400, atMouse: true);
    }

    /// <summary>金色提示：显式跟随鼠标（无需调用方传坐标，如图片复制）。</summary>
    public static void FlashAtMouse(string msg) => Show(msg, null, null, isError: false, 1400, atMouse: true);

    /// <summary>红色错误提示：与成功提示一致，跟随鼠标上方。2600ms 自动关闭。</summary>
    public static void Error(string msg) => Show(msg, null, null, isError: true, 2600, atMouse: true);

    // 统一跟随鼠标：x/y 保留仅为兼容既有调用点（坐标由 Placement=Mouse 内部锚定，不再参与计算）。
    private static void Show(string msg, double? x, double? y, bool isError, int ms, bool atMouse = true)
    {
        if (_popup == null || _body == null || _text == null) return;
        _isError = isError;
        _popup.IsOpen = false; // 强制干净重开，确保 Placement 切换生效（避免上次淡出中残留旧坐标）
        _text.Text = msg;
        _body.Background = isError ? new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x7A)) // --red
                                   : new SolidColorBrush(Color.FromRgb(0xC9, 0xA9, 0x6E)); // --accent 金
        _text.Foreground = isError ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x14));

        // 先测量内容，Popup 无窗口不能直接拿 ActualWidth
        _body.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var w = _body.DesiredSize.Width;
        var h = _body.DesiredSize.Height;

        // 所有提示（成功/失败）统一跟随鼠标：Placement=Mouse 由 WPF 按当前光标 + 所在显示器 DPI 内部锚定，
        // 规避 GetPosition+Absolute 在高分屏缩放 / 多显示器下坐标错配导致不跟随鼠标的问题。
        _popup.Placement = PlacementMode.Mouse;
        _popup.HorizontalOffset = -w / 2;   // 水平居中对齐光标
        _popup.VerticalOffset = -(h + 10);  // 光标上方 10px

        _body.Opacity = 0;
        _popup.IsOpen = true;
        // 淡入（对齐 .copied-flash transition opacity .25s；软件渲染下动画仍安全）
        _body.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)) { EasingFunction = new QuadraticEase() });
        Timer.Interval = TimeSpan.FromMilliseconds(ms);
        Timer.Stop();
        Timer.Start();
        AppLog.Info((isError ? "toast-err: " : "flash: ") + msg);
    }

    private static void Close()
    {
        if (_popup == null || _body == null) return;
        var fade = new DoubleAnimation(_body.Opacity, 0, TimeSpan.FromMilliseconds(140));
        fade.Completed += (_, _) => _popup.IsOpen = false;
        _body.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private static System.Windows.Media.Effects.DropShadowEffect CreateShadow() => new()
    {
        Color = Colors.Black, BlurRadius = 14, ShadowDepth = 2, Opacity = 0.5, Direction = 270,
    };
}
