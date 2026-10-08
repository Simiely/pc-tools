// Controls/CardView.xaml.cs - 卡片装配与交互（对齐 app.js clipCard / handleCardClick / makeCardBody / bindImageHoverPreview）
// 只做「展示 + 用户手势 → 事件转发」，业务编排（ClipService 变更 / ClipboardWatcher 抑制 / 弹窗）全部抛给 MainWindow：
//   CopyBumped        复制成功（MainWindow: MarkSelfWrite + BumpCopyCount 持久化；本卡仅本地 +1 显示，不重排——对齐 Web bumpCopyCount）
//   EditRequested     双击编辑 / ✎（归档只读不触发）
//   TogglePinRequested / DeleteRequested / OpenJsonRequested / OpenLinkRequested / TagFilterRequested
//   DownloadRequested 文件卡：单击复制=下载（M3b-2a）
//   CopyImageRequested 图片卡：单击复制到系统剪贴板（M3b-2b，对齐 Web copyImageToClipboard：失败降级 toast）
//   ImageBytesRequested 图片卡：构造时未注入加载器时异步获取字节（M3b-2b，MainWindow 从 FileStore 读取）
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ClipboardExe.Models;
using ClipboardExe.Services;

namespace ClipboardExe.Controls;

public partial class CardView : UserControl
{
    private ClipItem? _clip;
    private long _copyCount;          // 本地显示计数（复制成功后 +1，不改存储对象——防与 BumpCopyCount 双写）
    private readonly Func<string, byte[]>? _imageLoader; // M3b-2b：fileId → 图片字节；MainWindow 注入 FileStore 读取
    private byte[]? _imageBytes;      // M3b-2b：当前图片卡字节（已读则缓存，避免 hover 预览再读一次）
    private Popup? _imgPreviewPopup;  // M3b-2b：hover 浮层 Popup（唯一改 IsOpen 出口）
    private DispatcherTimer? _imgPreviewTimer; // M3b-2b：260ms 延迟打开（对齐 Web setTimeout 防快速划过误弹）
    private DispatcherTimer? _imgCloseTimer;  // 浮层/卡片间迁移的延迟关闭（鼠标移入浮层不闪关）
    private bool _inFloater;         // 鼠标当前在浮层上（避免 imgwrap MouseLeave 误关正在停留的浮层）
    private TextBlock? _capText; // 浮层标题 TextBlock（缩放时同步文字 + MaxWidth 跟随图宽）。单一引用——绝不用多 TextBlock 叠层（那会并排重复，叠层技巧在 WPF 实战中不可靠）。
    private double _imgPreviewScale = 1.0; // M3b-2b：当前缩放（默认 100%，每次开重置）
    private double _imgPreviewStep = 0.15; // M3b-2b：滚轮缩放步长（对齐 Web LS.get("zoomStep", 0.15)；未来从 Settings 读取）
    private Border? _previewBg;    // 浮层 box(Border) 引用：缩放时同步显式宽高（对齐 Web box.style.width）
    private Image? _previewImg;    // 浮层图引用：缩放时同步宽高
    private bool _previewAnchorAbove = true; // 浮层垂直锚定边（true=卡片上方/false=下方）：开浮层定一次，缩放不再翻转 → 杜绝上下跳动

    public event Action<ClipItem>? EditRequested;
    /// <summary>统一复制请求（文本/链接）：卡只发请求，由 MainWindow 经 ClipboardHelper 写入并反馈（审计：单一写入入口）。</summary>
    public event Action<ClipItem, double, double>? CopyRequested;
    public event Action<ClipItem>? TogglePinRequested;
    public event Action<ClipItem>? DeleteRequested;
    public event Action<ClipItem>? OpenJsonRequested;
    public event Action<ClipItem>? OpenLinkRequested;
    public event Action<ClipItem>? DownloadRequested; // 文件卡：单击复制=下载（M3b-2a）
    public event Action<ClipItem>? CopyImageRequested; // M3b-2b：图片卡：单击复制=复制图片到系统剪贴板
    public event Action<string>? TagFilterRequested;
    /// <summary>归档卡 ↺ 恢复（M3b-1：归档只读但可恢复到活跃区）。</summary>
    public event Action<ClipItem>? RestoreRequested;
    /// <summary>富文本分栏左栏：复制纯文本（对齐 Web makeRichSplit 左 half：copyText(content)）。</summary>
    public event Action<ClipItem, double, double>? CopyPlainRequested;
    /// <summary>富文本分栏右栏：复制带格式（对齐 Web makeRichSplit 右 half：copyRich(html, content)）。</summary>
    public event Action<ClipItem, double, double>? CopyRichRequested;

    /// <summary>批量模式：进入/退出时由 MainWindow 设置；控制右上 ✕→勾选框原位替换与单击整卡切换选择。</summary>
    public bool BatchMode { get; set; }
    private bool _selected;
    /// <summary>批量模式下单击整卡切换选择时触发（传出条目 id）。</summary>
    public event Action<string>? SelectionToggled;

    /// <summary>当前条目（供调用方读取）。</summary>
    public ClipItem? Item => _clip;

    public CardView() : this(null) { }

    /// <summary>
    /// 构造卡片。图片卡可注入 imageLoader 用于构造时同步读字节（避免 hover 预览再 IO）。
    /// 文件卡不依赖；MainWindow 注入 FileStore.ReadAllBytes(fileId)。
    /// </summary>
    public CardView(Func<string, byte[]>? imageLoader)
    {
        InitializeComponent();
        _imageLoader = imageLoader;
    }

    /// <summary>装配条目（对齐 clipCard：类型徽章 / 标题兜底 / 状态徽章 / body / meta / ops）。</summary>
    public void SetClip(ClipItem c)
    {
        _clip = c;
        _copyCount = c.CopyCount;
        _imageBytes = null; // 每次重装配清缓存（防换条目串图）
        CloseImagePreview(); // 关闭任何遗留浮层
        CardBorder.Tag = c.Pinned ? "pin" : null;

        // —— row1：类型徽章（.badge elev 底彩字：text 绿 / link 砖红 / file 金 / image 紫红） ——
        var isImage = c.Type == "file" && Format.IsImageMime(c.FileMime);
        TypeBadge.Style = c.Type switch
        {
            "link" => (Style)FindResource("BadgeLink"),
            "file" => (Style)FindResource(isImage ? "BadgeImage" : "BadgeFile"),
            _ => (Style)FindResource("BadgeText"),
        };
        TypeBadge.Content = c.Type == "link" ? "链接" : isImage ? "图片" : c.Type == "file" ? "文件" : "文本";

        // —— row1：标题（对齐 title || link→hostOf / file→fileName / text→content 前 30 字） ——
        var title = c.Title;
        if (string.IsNullOrEmpty(title))
        {
            title = c.Type == "link" ? Format.HostOf(c.Url)
                  : c.Type == "file" ? c.FileName
                  : Truncate(c.Content, 30);
        }
        TitleText.Text = title;
        TitleText.ToolTip = title;

        // —— row1：状态徽章（★ 置顶 / ⏳ 过期 / 归档，顺序对齐 app.js clipCard） ——
        StatusPanel.Children.Clear();
        if (c.Pinned) StatusPanel.Children.Add(MakeSt("★ 置顶", "StatusStPin"));
        if (c.ExpireAt.HasValue) StatusPanel.Children.Add(MakeSt("⏳ " + Format.ExpLabel(c.ExpireAt), "StatusStExp"));
        if (c.Archived) StatusPanel.Children.Add(MakeSt("归档", "StatusStArch"));

        // —— row1 右上：归档卡 ↺ 恢复 + ✕ 删除；非归档卡仅 ✕ 删除（对齐 Web .ops.top）
        //    批量模式：BuildTopOps 把 ✕ 原位替换为 26×26 勾选框（同几何同 Margin → 布局零变化） ——
        BuildTopOps();

        // —— body：类型专属内容区（图片→imgwrap / 文件→图标卡 / 链接→URL+按钮 / 文本→摘要） ——
        if (isImage)
        {
            var body = BuildImageBody(c);
            if (body != null) BodyHost.Content = body;
            else BodyHost.Content = BuildFileBody(c); // 图片字节读取/解码失败 → 降级为文件卡
        }
        // 文本卡：有 html → 富文本分栏（左纯文本/右富文本）；否则普通摘要
        else BodyHost.Content = c.Type == "file" ? BuildFileBody(c)
            : c.Type == "link" ? BuildLinkBody(c)
            : (string.IsNullOrEmpty(c.Html) ? BuildTextBody(c) : BuildRichSplit(c));

        // —— foot：meta（复制 N 次 / #tag / 时间）+ ops ——
        MetaPanel.Children.Clear();
        var cnt = new TextBlock { Text = "复制 " + c.CopyCount + " 次", FontSize = 11, Foreground = DimBrush, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        MetaPanel.Children.Add(cnt);
        foreach (var t in c.Tags ?? new List<string>())
        {
            var tag = new Button { Style = (Style)FindResource("MetaTag"), Content = "#" + t, Margin = new Thickness(0, 0, 8, 0), Tag = c };
            tag.Click += (_, _) => TagFilterRequested?.Invoke(t);
            MetaPanel.Children.Add(tag);
        }
        MetaPanel.Children.Add(new TextBlock { Text = Format.Time(c.UpdatedAt), FontSize = 11, Foreground = DimBrush, VerticalAlignment = VerticalAlignment.Center });

        OpsPanel.Children.Clear();
        if (!c.Archived)
        {
            var pin = MakeOpBtn(c.Pinned ? "★" : "☆", c.Pinned ? "取消置顶" : "置顶", del: false,
                () => TogglePinRequested?.Invoke(c));
            if (c.Pinned) pin.Foreground = AmberBrush; // .ops .b.on：置顶态金色
            OpsPanel.Children.Add(pin);
        }
        // 文件/图片共用 ↓ 下载（图片也允许下载，对齐 Web file → ↓ 下载无条件含归档）
        if (c.Type == "file")
            OpsPanel.Children.Add(MakeOpBtn("↓", "下载", del: false, () => DownloadRequested?.Invoke(c)));
        if (!c.Archived)
            OpsPanel.Children.Add(MakeOpBtn("✎", "编辑", del: false, () => EditRequested?.Invoke(c)));
        if (c.Type == "text" && Format.LooksLikeJson(c.Content))
            OpsPanel.Children.Add(MakeOpBtn("{}", "JSON 格式化预览", del: false, () => OpenJsonRequested?.Invoke(c)));

        OpsPanel.Visibility = OpsPanel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- 批量选择（M3b-3b：BatchMode 下整卡单击切换选择 + 右上 ✕→勾选框原位替换 + 选中金描边） ----

    /// <summary>进入/退出批量模式（MainWindow 调用）。退出时清除选中态。</summary>
    public void SetBatchMode(bool on)
    {
        BatchMode = on;
        if (!on) _selected = false;
        RefreshBatchVisual();
    }

    /// <summary>设置选中态（不触发 SelectionToggled；MainWindow 在 ToggleSelect 时反向同步单卡视觉用）。</summary>
    public void SetSelected(bool sel)
    {
        _selected = sel;
        RefreshBatchVisual();
    }

    private void RefreshBatchVisual()
    {
        bool show = BatchMode;
        // 批量模式视觉 = 仅把右上 ✕ 原位替换成 26×26 勾选框（用户拍板：多选框占删除按钮位）：
        //   StatusPanel(★置顶/⏳过期/归档徽章)/标题/Row0 行高全部保持 → 消除旧实现"隐藏徽章 + 标题让位 30px"
        //   导致的 Row0 行高收缩、卡片内容压缩上移（用户反馈）。布局普通/批量零差异。
        BuildTopOps();
        UpdateSelChkVisual();
        // 批量下隐藏 foot 单卡操作区（☆置顶/✎编辑/↓下载/{}JSON）：避免批量选择态误触弹编辑窗/下载/置顶
        // （实测:批量下点 ✎ 会弹出 600×491 单卡编辑窗,与批量选择心智冲突）；meta 信息(复制次数/标签/时间)保留
        OpsPanel.Visibility = show || OpsPanel.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        bool sel = show && _selected;
        // 选中金描边：仅当选中且非 pinned 时强制本地值；其余清除本地值交还 hover/pin 触发控制（避免覆盖 hover 金环）
        if (CardBorder.Tag?.ToString() == "pin")
            CardBorder.ClearValue(Border.BorderBrushProperty);
        else if (sel)
            CardBorder.BorderBrush = (Brush)FindResource("AccentBrush");
        else
            CardBorder.ClearValue(Border.BorderBrushProperty);
    }

    /// <summary>右上操作区（普通模式：↺恢复/✕删除；批量模式：✕ 原位换勾选框——同 26×26/同 Margin(0,0,5,0)，状态徽章与标题不动）。</summary>
    private Button? _selChkBtn;

    private void BuildTopOps()
    {
        var c = _clip;
        TopOpsPanel.Children.Clear();
        _selChkBtn = null;
        if (c == null) return;
        if (BatchMode)
        {
            _selChkBtn = MakeSelChk();
            TopOpsPanel.Children.Add(_selChkBtn);
        }
        else
        {
            if (c.Archived) TopOpsPanel.Children.Add(MakeOpBtn("↺", "恢复到活跃区", del: false, () => RestoreRequested?.Invoke(c)));
            TopOpsPanel.Children.Add(MakeOpBtn("✕", "删除", del: true, () => DeleteRequested?.Invoke(c)));
        }
    }

    private Button MakeSelChk()
    {
        var b = new Button
        {
            Style = (Style)FindResource("SelChkBtn"),
            Margin = new Thickness(0, 0, 5, 0), // 与 MakeOpBtn 的 ✕ 间距一致（原位替换，横向布局不动）
            ToolTip = "选择（点击卡片任意处亦可切换）",
        };
        b.Click += (_, _) => { if (_clip != null) SelectionToggled?.Invoke(_clip.Id); };
        return b;
    }

    /// <summary>勾选框选中态：未选 Transparent+Muted 描边 / 选中 Accent 金底 + ✓ 深字（对齐 Web sel-chk 选中视觉）。</summary>
    private void UpdateSelChkVisual()
    {
        if (_selChkBtn == null) return;
        bool sel = BatchMode && _selected;
        _selChkBtn.Background = sel ? (Brush)FindResource("AccentBrush") : Brushes.Transparent;
        _selChkBtn.BorderBrush = sel ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("MutedBrush");
        _selChkBtn.Content = sel
            ? new TextBlock { Text = "✓", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("AccentTextBrush") }
            : null;
    }

    // ---- body 构建 ----

    /// <summary>文本卡 body：普通换行摘要（InsetPanel 底，muted 12px，line-height 1.6）。
    /// 不做内嵌滚动——卡片不响应滚轮，滚轮始终作用于整页列表（用户拍板：卡片不适配滚轮，文本自然换行、
    /// 超出卡片高度部分由 ClipToBounds 裁切即可；要看全文双击编辑或复制）。</summary>
    private FrameworkElement BuildTextBody(ClipItem c)
    {
        var text = new TextBlock
        {
            Text = c.Content ?? "",
            FontSize = 12,
            Foreground = MutedBrush,
            TextWrapping = TextWrapping.Wrap, // 对齐 white-space:pre-wrap（保留换行 + 自动折行）
            LineHeight = 19.2,                // 12 * 1.6
        };
        return new Border
        {
            Child = text,
            Style = (Style)FindResource("InsetPanel"),
            Padding = new Thickness(11, 9, 6, 9),
            ClipToBounds = true, // 超高文本裁在卡内，不外溢 foot 行；滚轮永远滚整页
        };
    }

    /// <summary>富文本分栏（对齐 app.js makeRichSplit v0.6.12：左右都显示文本，顶部一行提示区分；
    /// 左栏单击复制纯文本 / 右栏单击复制富文本。stopPropagation 后由事件转发给 MainWindow 做剪贴板 I/O）。</summary>
    private FrameworkElement BuildRichSplit(ClipItem c)
    {
        var split = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 2, 0, 0) };

        // 顶部提示行：T 普通文本 | ✦ 富文本
        var tip = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        tip.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        tip.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var tipL = new TextBlock { Text = "T 普通文本", FontSize = 11, Foreground = MutedBrush, HorizontalAlignment = HorizontalAlignment.Center };
        var tipR = new TextBlock { Text = "✦ 富文本", FontSize = 11, Foreground = AmberBrush, HorizontalAlignment = HorizontalAlignment.Center };
        Grid.SetColumn(tipL, 0); Grid.SetColumn(tipR, 1);
        tip.Children.Add(tipL); tip.Children.Add(tipR);
        split.Children.Add(tip);

        // 左右两栏 + 中间分隔线
        var cols = new Grid();
        cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var divider = new Border { Width = 1, Background = FileBoxBorderBrush, Margin = new Thickness(4, 0, 4, 0), IsHitTestVisible = false };
        cols.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = MakeRichHalf(c.Content ?? "", "单击复制纯文本");
        left.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true; // 阻止冒泡到卡片单击（避免重复复制 / 触发默认文本复制）
            var p = e.GetPosition(null);
            CopyPlainRequested?.Invoke(c, p.X, p.Y);
        };
        var right = MakeRichHalf(c.Content ?? "", "单击复制带格式（粘贴到 Word/飞书保留样式）");
        right.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            var p = e.GetPosition(null);
            CopyRichRequested?.Invoke(c, p.X, p.Y);
        };

        Grid.SetColumn(left, 0); Grid.SetColumn(divider, 1); Grid.SetColumn(right, 2);
        cols.Children.Add(left); cols.Children.Add(divider); cols.Children.Add(right);
        split.Children.Add(cols);
        split.Name = "RichSplit"; // 守卫标记：双击/单击排除分栏区
        return split;
    }

    /// <summary>分栏单栏（.half + .plain-pv：InsetPanel 内嵌文本，hover 高亮，hand 光标）。</summary>
    private Border MakeRichHalf(string text, string tip)
    {
        var pv = new TextBlock
        {
            Text = text,
            FontSize = 12,
            Foreground = MutedBrush,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 19.2,
        };
        var border = new Border
        {
            Child = pv,
            Style = (Style)FindResource("InsetPanel"),
            Padding = new Thickness(8, 6, 8, 6),
            Cursor = Cursors.Hand,
            ToolTip = tip,
        };
        border.MouseEnter += (_, _) => border.Background = HoverBrush;
        border.MouseLeave += (_, _) => border.Background = InsetBrush;
        return border;
    }

    /// <summary>图片卡 body（M3b-2b：imgwrap cover 撑满对齐 app.js makeCardBody 图片分支；图片字节读取/解码失败返回 null 由调用方降级为文件卡）。</summary>
    private FrameworkElement? BuildImageBody(ClipItem c)
    {
        if (string.IsNullOrEmpty(c.FileId) || _imageLoader == null) return null;
        byte[] bytes;
        try { bytes = _imageLoader(c.FileId); }
        catch { return null; }
        if (bytes == null || bytes.Length == 0) return null;

        BitmapImage bmp;
        try
        {
            using var ms = new MemoryStream(bytes);
            bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad; // 立即加载并释放流
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze(); // 跨线程可用
        }
        catch { return null; }

        _imageBytes = bytes; // 缓存供 hover 预览 / 单击复制复用

        var img = new Image
        {
            Source = bmp,
            Stretch = Stretch.UniformToFill, // 对齐 app.js imgwrap cover 撑满内容区
            StretchDirection = StretchDirection.Both,
            ClipToBounds = true,
        };
        // imgwrap（InsetPanel 底 r-md 圆角 + ClipToBounds 圆角裁剪；对齐 .imgwrap 视觉）
        var wrap = new Border
        {
            Style = (Style)FindResource("InsetPanel"),
            CornerRadius = (CornerRadius)FindResource("RadiusMd"),
            Child = img,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 2, 0, 0),
        };
        wrap.Name = "ImgWrap"; // hover 触发区标识（mouseenter 仅此处）
        // 绑定 mouseenter：仅图片区域触发（对齐 Web v0.6.13 触发区收窄）
        wrap.MouseEnter += ImgWrap_MouseEnter;
        wrap.MouseLeave += ImgWrap_MouseLeavePreview;
        // 滚轮缩放绑在图片区（非浮层）：预览开着时鼠标停在卡片图片上滚轮即缩放（对齐 Web 缩放绑 card）；
        // 预览关时不 Handled → 放行页面滚动。浮层自身也另挂缩放（滚轮在浮层上时同样可缩放）。
        wrap.PreviewMouseWheel += (_, e) =>
        {
            if (_imgPreviewPopup == null || !_imgPreviewPopup.IsOpen) return; // 预览未开：放行页面
            if (!_inFloater) ZoomPreview(e);  // 鼠标在卡片图区：缩放预览并消费滚轮
        };
        return wrap;
    }

    /// <summary>文件卡 body（对齐 app.js makeFileIcon：.fic 图标卡 PDF 红边/ZIP 金边/FILE 中性 + 折叠角 + 内虚线 + fname·fsize·mime 首段）。</summary>
    private FrameworkElement BuildFileBody(ClipItem c)
    {
        var kind = Format.FileKindFor(c.FileName); // "pdf" | "zip" | "file"
        var fic = BuildFic(kind);
        var fname = new TextBlock
        {
            Text = c.FileName,
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            Foreground = TextBrush,
            TextTrimming = TextTrimming.CharacterEllipsis, // 对齐 .finfo .fname：单行省略
        };
        var fsize = new TextBlock
        {
            Text = Format.Size(c.FileSize) + " · " + (c.FileMime ?? "").Split('/')[0], // 对齐 fmtSize + " · " + mime 首段
            FontSize = 11,
            Foreground = DimBrush,
            Margin = new Thickness(0, 3, 0, 0),
        };
        var finfo = new StackPanel { Children = { fname, fsize } };
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center, // 对齐 .filebody：flex 1 + align-items center
            Margin = new Thickness(0, 2, 0, 0),
            Children = { fic, finfo },
        };
    }

    /// <summary>文件图标（对齐 .fic：46x56 r-11 inset 底 + 折叠角 + PDF/ZIP 彩边 + 内虚线框）。</summary>
    private FrameworkElement BuildFic(string kind)
    {
        var label = new TextBlock
        {
            Text = kind switch { "pdf" => "PDF", "zip" => "ZIP", _ => "FILE" },
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        // 折叠角（.fic .fold：top -1 left 6 18x5 elev-hi 顶圆角 3 / 底 0）
        var fold = new Border
        {
            Width = 18,
            Height = 5,
            Background = ElevHiBrush,
            CornerRadius = (CornerRadius)FindResource("RadiusFold"),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(6, -1, 0, 0),
        };
        var grid = new Grid { Children = { fold, label } };

        var box = new Border
        {
            Width = 46,
            Height = 56,
            Background = InsetBrush,
            CornerRadius = (CornerRadius)FindResource("RadiusIconLg"),
            BorderThickness = new Thickness(1),
            Child = grid,
            Margin = new Thickness(0, 0, 14, 0), // gap 14（.filebody gap）
        };
        if (kind == "pdf")
        {
            box.BorderBrush = RedSoftBorder;   // rgba(red,.35)
            label.Foreground = RedBrush;
            grid.Children.Insert(0, MakeFicDash(RedSoftBorder)); // 内虚线框（pdf/zip 特有）
        }
        else if (kind == "zip")
        {
            box.BorderBrush = GoldSoftBorder;  // rgba(gold,.35)
            label.Foreground = AmberBrush;     // 对齐 .fic.zip color: var(--accent)（金）
            grid.Children.Insert(0, MakeFicDash(GoldSoftBorder));
        }
        else
        {
            box.BorderBrush = FileBoxBorderBrush; // 对齐 .fic：1px var(--border)
            label.Foreground = MutedBrush;
        }
        return box;
    }

    /// <summary>fic 内虚线框（.fic.pdf::after / .fic.zip::after：inset 4px dashed opacity .35）。</summary>
    private static FrameworkElement MakeFicDash(Brush stroke)
    {
        return new System.Windows.Shapes.Rectangle
        {
            Margin = new Thickness(4),
            RadiusX = 6,
            RadiusY = 6,
            Stroke = stroke,
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 3, 2 },
            Opacity = 0.35,
            IsHitTestVisible = false,
        };
    }

    /// <summary>链接卡 body：砖红 URL（monospace 单行省略）+ ↗ 打开链接金色主按钮（.main-btn：accent 底深字，margin-top:auto）。</summary>
    private FrameworkElement BuildLinkBody(ClipItem c)
    {
        var url = new TextBlock
        {
            Text = c.Url ?? "",
            FontSize = 11.5,
            FontFamily = new FontFamily("Consolas, ui-monospace"),
            Foreground = BlueBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var urlBox = new Border
        {
            Child = url,
            Style = (Style)FindResource("InsetPanel"),
            Padding = new Thickness(11, 8, 6, 8),
        };
        var open = new Button
        {
            Content = "↗ 打开链接",
            Style = (Style)FindResource("MainLinkBtn"),
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        open.Click += (_, _) => OpenLinkRequested?.Invoke(c);
        return new StackPanel { Children = { urlBox, open } };
    }

    // ---- 图片 hover 预览（M3b-2b，对齐 app.js bindImageHoverPreview：260ms 延迟 / 仅 imgwrap mouseenter / 浮层 Popup / 50%~300% 缩放 / 视口钳制） ----

    private void ImgWrap_MouseEnter(object sender, MouseEventArgs e)
    {
        if (BatchMode) return; // 批量模式不弹预览（避免误触 + 干净的选择交互）
        _imgCloseTimer?.Stop(); // 从浮层回到卡片：取消待关
        _imgCloseTimer = null;
        if (_imageBytes == null) return;
        if (_imgPreviewPopup != null && _imgPreviewPopup.IsOpen) return; // 浮层已在：保持
        if (_imgPreviewTimer != null) _imgPreviewTimer.Stop();
        _imgPreviewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) }; // 对齐 Web 260ms 延迟防快速划过误弹
        _imgPreviewTimer.Tick += (_, _) =>
        {
            _imgPreviewTimer?.Stop();
            OpenImagePreview();
        };
        _imgPreviewTimer.Start();
    }

    private void ImgWrap_MouseLeavePreview(object sender, MouseEventArgs e)
    {
        // 延迟关闭：鼠标移向浮层（卡片上方/下方，独立 Popup 窗口）会先触发本卡 MouseLeave。
        // 若即刻 Close 则浮层一旦打开就闪关、无法停留滚轮缩放（用户反馈"放大没做进去"的根因之一）。
        // 延迟 220ms：期间鼠标进入浮层（bg.MouseEnter 取消）则不关；真正离开才关。
        _imgPreviewTimer?.Stop();
        if (_imgPreviewPopup != null && _imgPreviewPopup.IsOpen && _inFloater) return; // 鼠标已在浮层
        if (_imgCloseTimer != null) _imgCloseTimer.Stop();
        _imgCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        _imgCloseTimer.Tick += (_, _) =>
        {
            _imgCloseTimer?.Stop();
            _imgCloseTimer = null;
            if (_inFloater) return; // 已进入浮层：不关
            CloseImagePreview();
        };
        _imgCloseTimer.Start();
    }

    /// <summary>
    /// 打开图片预览浮层（方案A·对齐 Web .img-hover-preview，彻底重写）：
    /// ① 浮层 box(bg) = 圆角 Border，白底 + BorderClip 圆角裁剪(图四角裁进圆角) + 阴影
    /// ② box 尺寸由代码显式设 = 图等比显示尺寸 origW*scale × origH*scale（不依赖 Popup 自动测量 → 缩放后尺寸必然同步，无白条/裁切）
    /// ③ 图 = box 内单格 Grid 唯一尺寸元素，Stretch=Uniform 填满 box（box 比例=原图，图完整无裁切）
    /// ④ 底部 cap = 单 TextBlock 直接叠图底（黑字 + 白色 DropShadowEffect 细描边，无圆角矩形底），MaxWidth≤图宽(超长省略号)
    /// ⑤ 缩放：更新 box 显式宽高 + img 宽高 + cap 文字 + 重新 placement；垂直锚定边开时定一次不翻转（缩放不上下跳）
    /// ⑥ 点击预览图 = 复制该图片（Copy→CopyImageRequested），并关浮层
    /// 关键安全：全链只 1 个 TextBlock → "重复 N 遍"从结构上不可能。
    /// </summary>
    private void OpenImagePreview()
    {
        if (_imageBytes == null || _clip == null) return;
        if (_imgPreviewPopup != null && _imgPreviewPopup.IsOpen) return; // 防重：浮层已开不再重建
        _imgPreviewScale = 1.0; // 每次重开重置 100%

        BitmapImage bmp;
        try
        {
            using var ms = new MemoryStream(_imageBytes);
            bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
        }
        catch { return; }

        double origW = bmp.PixelWidth;
        double origH = bmp.PixelHeight;
        if (origW <= 0 || origH <= 0) return;

        // 0) 视口钳制：原图超大时初始等比缩到当前屏工作区能容纳的最大尺寸（对齐 Web min(img, inner-16)）
        var win = Window.GetWindow(CardBorder);
        var wa0 = win != null ? win.GetScreenWorkAreaDip() : SystemParameters.WorkArea;
        const double vpPad = 16;
        const double capReserveH = 0; // cap 叠图上不额外占高
        double maxW = Math.Max(64, wa0.Width - vpPad * 2);
        double maxH = Math.Max(64, wa0.Height - vpPad * 2 - capReserveH);
        double fitScale = Math.Min(maxW / origW, maxH / origH);
        if (fitScale < 1.0) _imgPreviewScale = fitScale;

        // 图等比显示尺寸（box = 图，精确 1:1）
        double dispW = origW * _imgPreviewScale;
        double dispH = origH * _imgPreviewScale;

        // 1) box：圆角白底容器（显式宽高 = 图尺寸；BorderClip 把内部(图/cap)裁进圆角 4 角）
        var bg = new Border
        {
            Background = Brushes.White,
            CornerRadius = (CornerRadius)FindResource("RadiusMd"),
            Padding = new Thickness(0),
            Width = dispW,
            Height = dispH,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, BlurRadius = 12, ShadowDepth = 4, Opacity = 0.35 },
        };
        ctl:BorderClip.SetClipToRadius(bg, true);

        // 2) 单格 Grid：Image 撑满(Uniform，box 比例=原图→完整无裁) + cap 叠底
        var stack = new Grid();

        var img = new Image
        {
            Source = bmp,
            Stretch = System.Windows.Media.Stretch.Uniform, // 填满 box（box 比例=原图 → 图完整）
            StretchDirection = StretchDirection.Both,
        };
        img.Width = dispW;
        img.Height = dispH;
        stack.Children.Add(img);

        // 3) cap = 单 TextBlock 直接叠图底（无圆角矩形底）：黑字 + 白色 DropShadowEffect(ShadowDepth=0,BlurRadius=1)
        //    渲染成"字形外围细白描边"（WPF 无 TextBlock.Stroke，单元素最稳等价——不叠层不重复）。
        //    IsHitTestVisible=false 让点击穿透到 bg → 触发复制。
        _capText = new TextBlock
        {
            Text = (_clip.FileName ?? "图片") + " · " + Format.Size(_clip.FileSize) + " · " + Math.Round(_imgPreviewScale * 100) + "%",
            FontSize = 11,
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI, ui-sans-serif"),
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.Black,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 8),
            MaxWidth = Math.Max(40, dispW - 8),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.White,
                BlurRadius = 1,      // 1px 细白描边
                ShadowDepth = 0,     // 0 偏移 → 向四周均匀扩散成描边
                Opacity = 1,
            },
        };
        stack.Children.Add(_capText);

        bg.Child = stack;
        _previewBg = bg;
        _previewImg = img;
        // 滚轮缩放：PreviewMouseWheel 隧道，浮层上滚轮先于内部
        bg.PreviewMouseWheel += ImgPreview_Wheel;
        // 点击预览图 = 复制该图片（图片卡走 Copy→CopyImageRequested 复制到系统剪贴板），并关闭浮层。
        // Popup 是独立窗格，点击不会透传给卡片单击，故在此显式接管。
        bg.MouseLeftButtonUp += (_, e) =>
        {
            if (_clip == null) return;
            var pos = e.GetPosition(null); // 屏幕坐标（对齐 e.clientX/Y）
            CloseImagePreview();           // 先关浮层，避免复制 toast 盖在浮层上
            Copy(_clip, pos.X, pos.Y);     // 图片卡 → CopyImageRequested；文本/链接同理按类型复制
            e.Handled = true;
        };
        // 浮层停留态：鼠标进入浮层 → 取消关闭
        bg.MouseEnter += (_, _) =>
        {
            _inFloater = true;
            _imgCloseTimer?.Stop();
            _imgCloseTimer = null;
        };
        bg.MouseLeave += (_, _) =>
        {
            _inFloater = false;
            if (_imgCloseTimer != null) _imgCloseTimer.Stop();
            _imgCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
            _imgCloseTimer.Tick += (_, _) =>
            {
                _imgCloseTimer?.Stop();
                _imgCloseTimer = null;
                CloseImagePreview();
            };
            _imgCloseTimer.Start();
        };
        // 浮层挂到 CardBorder。开浮层定一次垂直锚定边（避免缩放过程在上下方翻转跳动）：
        // 卡片中心在上半屏(下方空间大) → 放下方；在下半屏(上方空间大) → 放上方。整浮层生命周期不换边。
        var cardRect0 = CardBorder.PointToScreen(new Point(0, 0));
        var win0 = Window.GetWindow(CardBorder);
        var wa0b = win0 != null ? win0.GetScreenWorkAreaDip() : SystemParameters.WorkArea;
        var cardCenterY = cardRect0.Y + CardBorder.ActualHeight / 2;
        double roomAbove = cardCenterY - wa0b.Top;
        double roomBelow = wa0b.Bottom - cardCenterY;
        _previewAnchorAbove = roomBelow < roomAbove; // 下方空间不足且上方更宽裕 → 放上方；否则放下方
        _imgPreviewPopup = new Popup
        {
            PlacementTarget = CardBorder,
            Placement = PlacementMode.Custom,
            AllowsTransparency = true,
            StaysOpen = true,
            Child = bg,
        };
        _imgPreviewPopup.CustomPopupPlacementCallback = (popupSize, targetSize, offset) =>
            RepositionPreview(popupSize, targetSize);
        _imgPreviewPopup.IsOpen = true;
    }

    /// <summary>浮层定位（对齐 Web reposition：跟随卡片水平正中；垂直固定锚定边不翻转）。视口取卡片所在屏工作区。
    /// 锚定边 _previewAnchorAbove 在开浮层时定一次：缩放宽高变化仅做该边内的屏内钳制，绝不上下翻转换边 → 缩放不跳。</summary>
    private CustomPopupPlacement[] RepositionPreview(Size popupSize, Size targetSize)
    {
        var cardRect = CardBorder.PointToScreen(new Point(0, 0)); // 卡片屏幕坐标（DIP，虚拟屏空间）
        var win = Window.GetWindow(CardBorder);
        var wa = win != null ? win.GetScreenWorkAreaDip() : SystemParameters.WorkArea; // 卡片所在屏工作区
        var bw = popupSize.Width;
        var bh = popupSize.Height;
        // 水平：卡片居中，钳制 [wa.Left+8, wa.Right - bw - 8]
        var left = cardRect.X + (targetSize.Width - bw) / 2;
        left = Math.Max(wa.Left + 8, Math.Min(left, wa.Right - bw - 8));
        // 垂直：按开浮层时定的锚定边定位（不翻转）。
        double top;
        if (_previewAnchorAbove)
        {
            // 放卡片上方，间隙 4px；过高则钳到屏顶 +8（允许略微盖住卡片，但绝不换边）
            top = Math.Max(wa.Top + 8, cardRect.Y - bh - 4);
        }
        else
        {
            // 放卡片下方，间隙 4px；过高则钳到屏底 - bh - 8（允许略微上扩盖卡，绝不换边）
            top = Math.Min(wa.Bottom - bh - 8, cardRect.Y + targetSize.Height + 4);
            if (top < wa.Top + 8) top = wa.Top + 8; // 卡片极贴屏底时再兜底，不落出屏
        }
        // Popup 的 CustomPopupPlacement 返回相对 PlacementTarget 的偏移（Point(0,0) = PlacementTarget 左上）
        var relX = left - cardRect.X;
        var relY = top - cardRect.Y;
        return new[] { new CustomPopupPlacement(new Point(relX, relY), PopupPrimaryAxis.Horizontal) };
    }

    /// <summary>浮层滚轮缩放（PreviewMouseWheel 隧道到浮层自身）。</summary>
    private void ImgPreview_Wheel(object sender, MouseWheelEventArgs e) => ZoomPreview(e);

    /// <summary>滚轮缩放统一入口：delta>0 放大 / <0 缩小，钳制 50%~300%（对齐 Web zoom：Math.min(3,max(.5,scale±step))）。
    /// 卡片图区与浮层共用。预览打开期间滚轮一律消费（Handled），绝不漏给页面滚动——即使已到 50%/300% 钳制边界，
    /// 继续滚也只是不缩放、不滚页面（用户拍板：预览放大时滚轮永远属于预览，界面滚动应被完全屏蔽）。</summary>
    private void ZoomPreview(MouseWheelEventArgs e)
    {
        if (_imgPreviewPopup == null || !_imgPreviewPopup.IsOpen) return; // 预览未开：放行（由调用点保证仅预览开时调用）
        e.Handled = true; // 预览打开即吞下滚轮，杜绝透传到主窗页面滚动
        var before = _imgPreviewScale;
        _imgPreviewScale = Math.Min(3.0, Math.Max(0.5, _imgPreviewScale + (e.Delta > 0 ? _imgPreviewStep : -_imgPreviewStep)));
        if (_imgPreviewScale != before) ApplyPreviewScale();
    }

    private void ApplyPreviewScale()
    {
        if (_imageBytes == null || _clip == null) return;
        if (_previewBg == null || _previewImg == null) return;
        try
        {
            using var ms = new MemoryStream(_imageBytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            // 图等比显示尺寸 = box 尺寸（对齐 Web img.style.width=natural*scale + box.style.width=img+pad）
            double dispW = bmp.PixelWidth * _imgPreviewScale;
            double dispH = bmp.PixelHeight * _imgPreviewScale;
            // 更新 box 显式宽高 → Popup 视窗跟随（无白条/裁切）。这是方案A关键：不再依赖 Popup 自动测量
            _previewBg.Width = dispW;
            _previewBg.Height = dispH;
            _previewImg.Source = bmp;
            _previewImg.Width = dispW;
            _previewImg.Height = dispH;
            // 同步 cap 文字 + MaxWidth（≤图宽；无 chip 底，TextBlock 即 cap）
            if (_capText != null)
            {
                _capText.Text = (_clip.FileName ?? "图片") + " · " + Format.Size(_clip.FileSize) + " · " + Math.Round(_imgPreviewScale * 100) + "%";
                _capText.MaxWidth = Math.Max(40, dispW - 8);
            }
            // 视口钳制：触发重新 placement（缩放后 box 变大/变小需重新定位居中）
            if (_imgPreviewPopup != null && _imgPreviewPopup.IsOpen)
            {
                _imgPreviewPopup.HorizontalOffset += 0.001; // 触发重新 placement
                _imgPreviewPopup.HorizontalOffset -= 0.001;
            }
        }
        catch { /* 缩放失败不抛——预览仍可用 */ }
    }

    private void CloseImagePreview()
    {
        _imgPreviewTimer?.Stop();
        _imgPreviewTimer = null;
        _imgCloseTimer?.Stop();
        _imgCloseTimer = null;
        _inFloater = false;
        _capText = null; // 释放单 TextBlock 引用，下次重建时填充
        _previewBg = null;
        _previewImg = null;
        if (_imgPreviewPopup != null)
        {
            _imgPreviewPopup.IsOpen = false;
            if (_imgPreviewPopup.Child is Border bg) bg.PreviewMouseWheel -= ImgPreview_Wheel;
            _imgPreviewPopup.Child = null;
            _imgPreviewPopup = null;
        }
    }

    // ---- 手势：单击复制 / 双击编辑（守卫排除按钮区与归档） ----

    private void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_clip == null) return;
        var src = e.OriginalSource as DependencyObject;
        if (BatchMode)
        {
            if (src != null && IsInsideInteractive(src)) return; // ops/标签/主按钮自己处理（如 ✕ 删除）
            e.Handled = true;
            SelectionToggled?.Invoke(_clip.Id); // 批量模式：单击整卡切换选择（对齐 Web 勾选）
            return;
        }
        if (src != null && IsInsideInteractive(src)) return; // ops/标签/主按钮自己处理
        if (IsInsideRichSplit(src)) return; // 富文本分栏自己处理复制（左/右栏 stopPropagation）
        var pos = e.GetPosition(null); // 屏幕坐标（对齐 e.clientX/Y）
        Copy(_clip, pos.X, pos.Y);
    }

    private void Card_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (BatchMode) return; // 批量模式禁用双击编辑（避免与选择切换冲突）
        if (_clip == null || _clip.Archived) return; // 归档只读（对齐 ondblclick 守卫）
        var src = e.OriginalSource as DependencyObject;
        if (src != null && IsInsideInteractive(src)) return;
        if (IsInsideRichSplit(src)) return; // 分栏点击不触发编辑（对齐 Web '.rich-split' 守卫）
        EditRequested?.Invoke(_clip);
    }

    /// <summary>复制内容（对齐 handleCardClick：text 复制 content / link 复制 url / file 非图片 → 下载；
    /// 图片 → 复制到系统剪贴板（失败 toast；对齐 Web copyImageToClipboard + 失败降级 errToast）。</summary>
    private void Copy(ClipItem c, double x, double y)
    {
        if (c.Type == "file")
        {
            if (Format.IsImageMime(c.FileMime))
            {
                CopyImageRequested?.Invoke(c); // M3b-2b：MainWindow 复制到系统剪贴板
                return;
            }
            DownloadRequested?.Invoke(c); // 对齐 handleCardClick：文件（非图片）单击 = 下载
            return;
        }
        // 文本/链接：卡只发复制请求，写入与反馈统一由 MainWindow 处理（审计：单一入口，避免双路径）
        CopyRequested?.Invoke(c, x, y);
    }

    private void UpdateCopyLabel()
    {
        if (MetaPanel.Children.Count == 0) return;
        if (MetaPanel.Children[0] is TextBlock t) t.Text = "复制 " + _copyCount + " 次";
    }

    /// <summary>复制成功后由 MainWindow 回调，更新本卡「复制 N 次」本地计数（对齐 Web bumpCopyCount 的本地 +1）。</summary>
    public void MarkCopied() { _copyCount++; UpdateCopyLabel(); }

    /// <summary>是否点在富文本分栏内（.rich-split）——分栏左右栏自己处理复制，排除卡片默认复制/编辑（对齐 Web e.target.closest(".rich-split")）。</summary>
    private static bool IsInsideRichSplit(DependencyObject? src)
    {
        while (src != null)
        {
            if (src is FrameworkElement fe && fe.Name == "RichSplit") return true;
            src = VisualTreeHelper.GetParent(src);
        }
        return false;
    }

    /// <summary>是否点在内层交互件上（Button / 内容可点击区）——卡片单击/双击守卫（对齐 e.target.closest(".ops")）。</summary>
    private static bool IsInsideInteractive(DependencyObject? src)
    {
        while (src != null)
        {
            if (src is Button or ToggleButton) return true;
            src = VisualTreeHelper.GetParent(src);
        }
        return false;
    }

    // ---- 小构件 ----

    private ContentControl MakeSt(string text, string styleKey) => new()
    {
        Style = (Style)FindResource(styleKey),
        Content = text,
        Margin = new Thickness(0, 0, 5, 0),
    };

    private Button MakeOpBtn(string glyph, string tip, bool del, Action onClick)
    {
        var b = new Button
        {
            Content = glyph,
            ToolTip = tip,
            Style = (Style)FindResource(del ? "OpsIconBtnDel" : "OpsIconBtn"),
            Margin = new Thickness(0, 0, 5, 0),
        };
        b.Click += (_, _) => onClick();
        return b;
    }

    private static string Truncate(string? s, int max)
    {
        s ??= "";
        return s.Length > max ? s[..max] : s;
    }

    // ---- 画刷缓存（避免每次装配 new SolidColorBrush） ----
    private static readonly Brush DimBrush = BrushFrom(0x6E6E6E);
    private static readonly Brush MutedBrush = BrushFrom(0x848484);
    private static readonly Brush TextBrush = BrushFrom(0xDADADA);
    private static readonly Brush BlueBrush = BrushFrom(0xAE4D4D);
    private static readonly Brush AmberBrush = BrushFrom(0xD4AF37);
    private static readonly Brush RedBrush = BrushFrom(0xE08A7A);
    private static readonly Brush InsetBrush = BrushFrom(0x141414);
    /// <summary>分栏 hover 高亮（比 InsetPanel 略亮）。</summary>
    private static readonly Brush HoverBrush = BrushFrom(0x1E1E1E);
    private static readonly Brush ElevHiBrush = BrushFrom(0x2A2A2A);
    private static readonly Brush FileBoxBorderBrush = BrushFrom(0x3D3D3D);
    /// <summary>rgba(red,.35) 文件卡 PDF 边。</summary>
    private static readonly Brush RedSoftBorder = BrushFromAlpha(0xE08A7A, 0x59);
    /// <summary>rgba(gold,.35) 文件卡 ZIP 边。</summary>
    private static readonly Brush GoldSoftBorder = BrushFromAlpha(0xC9A96E, 0x59);

    private static Brush BrushFrom(int rgb)
        => new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));

    private static Brush BrushFromAlpha(int rgb, byte alpha)
        => new SolidColorBrush(Color.FromArgb(alpha, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
}