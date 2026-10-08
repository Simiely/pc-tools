// Controls/ModalHost.cs - 弹窗宿主（对齐 Web #modal-root：居中弹窗卡片，一次只挂一个）
//  实现：独立顶层 Window（透明背景、无全屏压暗遮罩），卡片自带阴影、尺寸由内容决定，
//  因此可超出主窗口边界完整显示；位置相对主窗口居中，并夹在屏幕工作区内保证完整可见。
//  非模态：主窗口保持可点击；弹窗失焦（点击空白/主窗口/其它程序）即自动关闭（SuppressDismiss 期间不关，保护内部子对话框）。
//  - Attach(owner)：MainWindow 构造时绑定主窗口
//  - Show(content)：清旧弹窗 → 包 ScrollViewer（限制最大尺寸避免超出屏幕）→ 居中于主窗口的非模态 Window；Close()：收 Window
//  - Confirm(msg, onOk, ...) ：确认框（对齐 askConfirm）
// 拖动：无边框窗口默认不可拖（WPF 无标题栏即无系统拖动）。方案 = 系统级拖动：
//   PreviewMouseLeftButtonDown(隧道，Window 最先收到，子元素 Handled 无法吞噬) 命中非交互区时
//   ReleaseCapture + SendMessage(WM_NCLBUTTONDOWN, HTCAPTION) —— 让系统按"标题栏被按下"进入原生拖动循环。
//   为何不用手动 Left/Top 跟随（v0.7.0 15:52 曾用，用户实测"狂闪"）：
//     ① AllowsTransparency(layered window) + SizeToContent 下逐 MouseMove SetWindowPos 无系统移动合成，
//       每帧全量重绘，渲染跟不上鼠标消息 → 抽搐闪烁（社区共识：MouseMove 里做位置更新 = 卡顿元凶）；
//     ② 双屏混合 DPI：GetPosition(null) 按鼠标所在屏 DPI 换算，Left/Top 按窗口所在屏 DPI 解释，
//       跨屏瞬间基准跳变 → 位置跳动。
//   系统拖动由内核/DWM 合成：内容不逐帧重绘（不闪）、跨 DPI/多屏无坐标换算（不跳）、天然支持 Aero Snap。
//   为何不用 DragMove()：透明窗口下曾实测无效，且受"仅可在 MouseLeftButtonDown 中调用"的 WPF 生命周期限制。
//   命中交互控件（按钮/输入框/下拉/滚动条等）时不拖——放行正常交互；拖动区 = 全部非交互区（整卡空白/标题/文字），
//   光标由 content 根 Cursor=SizeAll 提示（TextBox 隐式样式已设 IBeam，按钮家族已设 Hand）。
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace ClipboardExe.Controls;

public static class ModalHost
{
    private static Window? _owner;
    private static Window? _current;
    private static bool _closing;   // 防 Deactivated 重入 Close 触发 VerifyNotClosing（窗口关闭中再次 Close 会抛异常）
    private static bool _armed;      // Show 后短暂屏蔽 Deactivated 自动关闭，避开打开瞬间的激活抖动（Owner 切换导致的瞬时 Deactivated）

    // v0.7.3 修复：子弹窗（Confirm）栈 —— 之前 Confirm 走 Show 会把当前弹窗（EditDialog）顶掉，
    //   导致点"清除格式"后编辑窗被关闭、无法保存。
    //   修法：Confirm 不再创建新 Window，而是把 Confirm 卡放进 _current 的 Content 顶层 Grid（Overlay），
    //   关闭时只移除该层。EditDialog 等主弹窗全程存活。Overlay 用 Border + 透暗遮罩。
    private static Grid? _overlayHost;     // 注入到 _current.Content 顶层的覆盖层 Grid
    private static readonly Stack<Grid> _overlayStack = new(); // 支持嵌套（理论上用不到）

    // ---- 打开后"激活稳定保护期"：治愈"点击主窗口激活 → 存卡窗闪一下就没" ----
    // 根因：点击后台主窗口激活 → OnActivated 立即弹存卡窗（win.Show 抢激活）→ 用户那一下点击落点仍在
    // 主窗口 → 焦点又切回主窗口 → 弹窗 Deactivated → 自动 Close = 一闪即没，来不及点存入/取消。
    // 关键在 _armed 只在 Loaded 就置 true，保护窗太短，防不住 Loaded 之后紧接的焦点回落。
    // 修法：把保护从"仅 Loaded"扩展为"打开后 GuardWindowMs 内的 Deactivated 一律延迟确认"——
    // 震荡期不立即关，给弹窗重新拿回焦点的机会；稳定失焦(用户真去点了别处)才关。
    private static readonly TimeSpan GuardWindow = TimeSpan.FromMilliseconds(450);
    private static readonly DispatcherTimer _closeGuard = new() { Interval = TimeSpan.FromMilliseconds(160) };
    private static DateTime _openedAt;
    private static bool _closePending;   // 震荡期标记一次延迟关闭，_closeGuard 到点若仍未重新激活才真正关

    static ModalHost()
    {
        // 延迟确认到点：若期间未被重新激活(win.Activated 已取消 pending)，才真正关闭。
        // 静态注册一次即可，避免 Show 多次重复订阅 Tick。
        _closeGuard.Tick += (_, _) =>
        {
            _closeGuard.Stop();
            if (_closePending)
            {
                _closePending = false;
                Close();
            }
        };
    }

    // ---- 系统级拖动（ReleaseCapture + WM_NCLBUTTONDOWN/HTCAPTION）常量与 P/Invoke ----
    // WM_NCLBUTTONDOWN = 在非客户区按下左键；HTCAPTION = 命中区域视为标题栏 → 系统进入原生移动循环。
    // 依据：SO 33139478 高赞（AllowsTransparency 无边框窗拖动标准做法）；SO 3274097 高赞（无边框可拖 = HTCAPTION）。
    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HTCAPTION = 2;

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    /// <summary>拖动排除的交互控件基类（点在这些控件上不拖动窗口：按钮/输入框/下拉/滚动条/列表项/滑块…）。</summary>
    private static readonly Type[] DragExcludeTypes =
    {
        typeof(ButtonBase),   // Button/ToggleButton/RepeatButton（CheckBox/RadioButton 亦属 ToggleButton）
        typeof(TextBoxBase),  // TextBox/RichTextBox
        typeof(ComboBox),
        typeof(ScrollBar),
        typeof(PasswordBox),
        typeof(ListBoxItem),
        typeof(DatePicker),
        typeof(Slider),
    };

    /// <summary>内部子对话框（文件选择等）打开期间置 true，屏蔽失焦自动关闭，避免误关弹窗。</summary>
    public static bool SuppressDismiss { get; set; }

    /// <summary>绑定主窗口（MainWindow 构造时调用一次）。</summary>
    public static void Attach(Window owner) => _owner = owner;

    /// <summary>当前是否有弹窗（对齐 $(".mask") 判断——已有弹窗时不叠加）。</summary>
    public static bool IsOpen => _current != null;

    /// <summary>挂载弹窗：独立顶层 Window（透明背景、不压暗界面）+ 相对主窗口居中 + 非模态（点击空白关闭）。</summary>
    public static void Show(FrameworkElement content)
    {
        if (_owner == null) return;
        if (_current != null) Close();

        var wa = _owner.GetScreenWorkAreaDip(); // owner 所在屏工作区（双屏：主窗在副屏时按副屏钳制，不拽回主屏）
        var win = new Window
        {
            Owner = _owner,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = null, // 透明：不压暗整个界面
            Foreground = (Brush)Application.Current.FindResource("TextBrush"), // 弹窗独立窗口默认前景为黑，强制浅色，避免黑底黑字
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            SizeToContent = SizeToContent.WidthAndHeight,
        };

        // 内容居中；包 ScrollViewer 限制最大尺寸，避免内容过大时超出屏幕（仅弹窗自身可滚动）
        content.HorizontalAlignment = HorizontalAlignment.Center;
        content.VerticalAlignment = VerticalAlignment.Center;
        // 全卡非交互区可拖（系统拖动在 PreviewMouseLeftButtonDown 触发）——SizeAll 光标提示可拖范围；
        // 交互控件自带光标覆盖（TextBox 隐式样式 IBeam、按钮家族 Hand），不会误显示。
        content.Cursor = Cursors.SizeAll;

        // v0.7.3：弹窗根 = Grid 叠层（Content + 顶层 Overlay Host）。
        // 子弹窗（Confirm）不替换主弹窗，而是在 Overlay 顶层叠层显示；保证 EditDialog/PasteDialog 等主弹窗持续存活。
        var root = new Grid();
        root.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxWidth = wa.Width - 24,
            MaxHeight = wa.Height - 24,
            Content = content,
        });
        _overlayHost = new Grid { IsHitTestVisible = false }; // 默认不挡命中（无 Overlay 时不挡拖动/点击）
        root.Children.Add(_overlayHost);
        win.Content = root;

        // 先把窗口摆到主窗口附近（用估算尺寸），避免 Show 瞬间的 0,0 闪烁；Loaded 再用真实尺寸精修
        content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var est = content.DesiredSize;
        (win.Left, win.Top) = PositionFor(est.Width, est.Height);

        _current = win;
        _closing = false;
        _armed = false;
        _openedAt = DateTime.UtcNow;
        _closePending = false;
        _closeGuard.Stop();
        win.Loaded += (_, _) =>
        {
            // 用真实渲染尺寸再夹一次，确保完整可见
            (win.Left, win.Top) = PositionFor(win.ActualWidth, win.ActualHeight);
            KeyboardFocus(content);
            _armed = true; // 装载完成后再允许「点击空白关闭」，避开打开瞬间的激活抖动（一开就关）
        };
        // 非模态：主窗口保持可点击；失焦（点空白/主窗口/其它程序）即关闭。
        //  仅当本窗口仍是当前弹窗(_current==win)、已就绪(_armed)、非关闭中(_closing)、非子对话框期间(SuppressDismiss)才关，
        //  以防打开瞬间 Owner 切换产生的瞬时 Deactivated 把刚开的弹窗立刻关掉，以及关闭过程 WmActivate 重入导致 VerifyNotClosing 崩溃。
        //  打开后 GuardWindow 内失焦 → 不立即关：走 _closePending + 延迟确认，弹窗重新激活(win.Activated)即取消，
        //  治愈"点击主窗口激活 → 存卡窗闪一下就没"(见文件头注释)。
        win.Activated += (_, _) => { _closePending = false; _closeGuard.Stop(); };
        win.Deactivated += (_, _) =>
        {
            if (SuppressDismiss || _closing) return;
            if (_current != win) return;
            if (!_armed) return;
            if ((DateTime.UtcNow - _openedAt) < GuardWindow)
            {
                // 仍在"激活稳定保护期"：本次失焦可能是点击主窗口激活的焦点震荡，延迟确认，给重新激活机会
                _closePending = true;
                _closeGuard.Stop();
                _closeGuard.Start();
                return;
            }
            Close();
        };
        // 拖动：无边框窗口无系统拖动（WindowStyle.None）。Preview 隧道阶段拦截（子元素 Handled 无法吞噬），
        //   命中交互控件（按钮/输入框/下拉/滚动条等）不拖 → return 放行，控件交互正常；
        //   命中非交互区（标题行/卡片空白/文字等，即"激活区域"= 整卡非交互区）→ e.Handled 阻断冒泡 +
        //   ReleaseCapture + SendMessage(WM_NCLBUTTONDOWN, HTCAPTION) 交给系统原生拖动循环。
        //   SendMessage 同步阻塞至 MouseUp（拖动结束）才返回；期间由内核/DWM 合成移动 = 无闪烁、跨 DPI 无跳变。
        win.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Left || e.ClickCount != 1) return;
            if (IsInteractiveHit(e.OriginalSource)) return; // 交互控件：放行（按钮点击/文本框选择文本/滚动…正常）
            e.Handled = true; // 本区域无交互语义（标题/留白/文字），阻断后续冒泡与子元素 Down，防与系统拖动叠加
            var hwnd = new WindowInteropHelper(win).Handle;
            ReleaseCapture(); // 先释放可能存在的鼠标捕获，否则系统收不到 NC 命中后的移动消息
            SendMessage(hwnd, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero); // 系统原生移动循环
        };
        win.Show();
    }

    /// <summary>计算相对主窗口居中、并夹在屏幕工作区内的左上角坐标（工作区取 owner 所在屏，非主屏）。</summary>
    private static (double left, double top) PositionFor(double w, double h)
    {
        var ow = _owner!;
        var wa = ow.GetScreenWorkAreaDip(); // owner 所在屏工作区（DIP）——副屏主窗时不再被 Clamp 拽回主屏
        var owW = ow.ActualWidth > 0 ? ow.ActualWidth : ow.Width;
        var owH = ow.ActualHeight > 0 ? ow.ActualHeight : ow.Height;

        var left = ow.Left + (owW - w) / 2;
        var top = ow.Top + (owH - h) / 2;

        left = System.Math.Clamp(left, wa.Left, System.Math.Max(wa.Left, wa.Right - w));
        top = System.Math.Clamp(top, wa.Top, System.Math.Max(wa.Top, wa.Bottom - h));
        return (left, top);
    }

    public static void Close()
    {
        var w = _current;
        if (w == null || _closing) return;       // 已无弹窗 / 正在关闭中：不重入，避免 VerifyNotClosing
        _closing = true;                          // 标记关闭中（关闭过程 WmActivate 重入 Deactivated 时不再重复 Close）
        _current = null;
        _closePending = false;                    // 清延迟关闭标记，防迟到的 _closeGuard Tick 误关新弹窗
        _closeGuard.Stop();
        _overlayHost = null;                      // v0.7.3：主弹窗关闭时清空 overlay host 引用
        _overlayStack.Clear();                    // v0.7.3：清空子弹窗栈
        try { w.Close(); }
        finally { _closing = false; }
    }

    /// <summary>
    /// v0.7.3 修复：确认框——叠在当前弹窗之上（不替换）。
    /// 之前 v0.7.3 用 Show 创建新 Window，会把 _current（EditDialog 等主弹窗）顶掉，造成"清除格式点击后编辑窗被关"的 bug。
    /// 新版：把 Confirm 卡片放到 _current 的顶层 Grid（_overlayHost）里，主弹窗全程存活。
    /// 暗色遮罩盖住整个主弹窗；点击遮罩不关闭（确认框必须明确选确认/取消，避免误关）。
    /// </summary>
    public static void Confirm(string msg, Action onOk, string okText = "确认", Action? onCancel = null)
    {
        if (_owner == null || _current == null || _overlayHost == null)
        {
            // 无主弹窗（极端边界）走原 Show 路径：把 confirm 当独立弹窗
            ConfirmStandalone(msg, onOk, okText, onCancel);
            return;
        }

        var title = new TextBlock { Text = "确认操作", FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) };
        var body = new TextBlock
        {
            Text = msg,
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(0x84, 0x84, 0x84)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 18),
        };
        var ok = new Button
        {
            Style = (Style)Application.Current.FindResource("BtnPrimary"),
            Content = okText,
            MinWidth = 130,
            Margin = new Thickness(0, 0, 10, 0),
        };
        var cancel = new Button
        {
            Style = (Style)Application.Current.FindResource("BtnClose"),
            Content = "取消",
            MinWidth = 130,
        };
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(ok, 0);
        Grid.SetColumn(cancel, 1);
        ok.Margin = new Thickness(0, 0, 10, 0);
        row.Children.Add(ok);
        row.Children.Add(cancel);

        var sp = new StackPanel { Children = { title, body, row } };
        var card = new Border
        {
            Style = (Style)Application.Current.FindResource("ModalCard"),
            Width = 360,
            Child = sp,
        };

        // 暗色遮罩（半透明）+ 居中卡片，覆盖整个主弹窗。IsHitTestVisible=true 拦截主弹窗的点击/拖动。
        var mask = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x66, 0x00, 0x00, 0x00)),
            IsHitTestVisible = true,
            // 屏蔽主弹窗系统拖动：吃掉 PreviewMouseLeftButtonDown
        };
        mask.PreviewMouseLeftButtonDown += (_, e) => { e.Handled = true; };
        // 居中卡片容器
        var center = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        center.Children.Add(card);
        var overlay = new Grid();
        overlay.Children.Add(mask);
        overlay.Children.Add(center);
        _overlayHost.Children.Add(overlay);
        _overlayHost.IsHitTestVisible = true; // 启用覆盖层命中
        _overlayStack.Push(overlay);

        void CloseOverlay()
        {
            if (_overlayHost == null) return;
            _overlayHost.Children.Remove(overlay);
            if (_overlayStack.Count > 0) _overlayStack.Pop();
            if (_overlayHost.Children.Count == 0) _overlayHost.IsHitTestVisible = false; // 没覆盖层了恢复
        }

        ok.Click += (_, _) => { CloseOverlay(); onOk(); };
        cancel.Click += (_, _) => { CloseOverlay(); onCancel?.Invoke(); };
    }

    /// <summary>v0.7.3 兜底：当前无主弹窗时（极端边界），把 confirm 当独立弹窗。保留 v0.7.3 行为。</summary>
    private static void ConfirmStandalone(string msg, Action onOk, string okText, Action? onCancel)
    {
        var title = new TextBlock { Text = "确认操作", FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) };
        var body = new TextBlock
        {
            Text = msg,
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(0x84, 0x84, 0x84)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 18),
        };
        var ok = new Button
        {
            Style = (Style)Application.Current.FindResource("BtnPrimary"),
            Content = okText,
            MinWidth = 130,
            Margin = new Thickness(0, 0, 10, 0),
        };
        var cancel = new Button
        {
            Style = (Style)Application.Current.FindResource("BtnClose"),
            Content = "取消",
            MinWidth = 130,
        };
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(ok, 0);
        Grid.SetColumn(cancel, 1);
        ok.Margin = new Thickness(0, 0, 10, 0);
        row.Children.Add(ok);
        row.Children.Add(cancel);

        var sp = new StackPanel { Children = { title, body, row } };
        var card = new Border
        {
            Style = (Style)Application.Current.FindResource("ModalCard"),
            Width = 360,
            Child = sp,
        };

        ok.Click += (_, _) => { Close(); onOk(); };
        cancel.Click += (_, _) => { Close(); onCancel?.Invoke(); };
        Show(card);
    }

    /// <summary>把焦点给弹窗内第一个可聚焦控件（对齐 Web 打开弹窗即聚焦输入）。</summary>
    private static void KeyboardFocus(DependencyObject root)
    {
        var first = FindFocusable(root);
        first?.Focus();
        if (first is TextBox tb) tb.CaretIndex = tb.Text.Length;
    }

    private static UIElement? FindFocusable(DependencyObject node)
    {
        if (node is TextBox or Button) return node as UIElement;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var hit = FindFocusable(VisualTreeHelper.GetChild(node, i));
            if (hit != null) return hit;
        }
        return null;
    }

    /// <summary>命中点是否落在交互控件上（沿 visual/logical 树向上找，见 DragExcludeTypes）。返回 true = 不拖动。</summary>
    private static bool IsInteractiveHit(object? original)
    {
        for (DependencyObject? d = original as DependencyObject; d != null; d = NextParent(d))
        {
            foreach (var t in DragExcludeTypes)
                if (t.IsInstanceOfType(d)) return true;
            if (d is Window) break; // 树顶（拖动作用于窗口自身）
        }
        return false;
    }

    private static DependencyObject? NextParent(DependencyObject d)
        => d is FrameworkContentElement fce ? fce.Parent : VisualTreeHelper.GetParent(d);
}
