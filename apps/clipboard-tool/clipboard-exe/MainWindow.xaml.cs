using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ClipboardExe.Controls;
using ClipboardExe.Models;
using ClipboardExe.Services;

namespace ClipboardExe;

/// <summary>
/// 主窗体（M3a 编排层）：
///  - ★ 置顶按钮（M1）：Window.Topmost 切换 + 状态持久化
///  - 剪贴板监听：WM_CLIPBOARDUPDATE → 防抖 → 去重 → 弹存入/编辑窗（对齐 Web clipboardchange）
///  - 卡片墙：搜索 100ms 防抖 + 类型 RadioButton 过滤 + 标签过滤（轻量 tagbar）+ 自适应列数（ColumnsFor）
///  - 弹窗编排：存入/编辑/JSON 预览/确认 → ModalHost；数据变更走 ClipService（规则净化对齐 Web）
/// </summary>
public partial class MainWindow : Window
{
    private readonly Settings _settings;
    private readonly TrayIconService? _tray;
    private readonly Storage _storage;
    private readonly FileStore _fileStore; // M3b-2a：文件实体（data/files/）
    private readonly ClipService _svc;
    private ClipboardWatcher? _watcher; // OnSourceInitialized 后创建（需窗口句柄就绪）
    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private bool _reallyExit;
    private bool _everActivated;        // 首次激活（启动）不自动弹窗，仅后续切回才检测
    private uint? _lastHandledSeq;      // 已处理过的剪贴板序列号（激活/剪贴板事件共用；null = 尚未处理过任何内容）
    // 激活后延迟弹窗：等"点击激活"那一记 mouse down/up 排空、窗口稳定激活后再弹，避免弹窗在
    // Show 抢焦点后被紧接的鼠标落点抢回 → 闪没/点不到。注意：此延迟只"推迟弹"，绝不以用户后续输入取消
    // （Win32：点击非活动窗口时 WM_MOUSEACTIVATE→OnActivated 在 WM_LBUTTONDOWN 之前投递，若见 mouse-down
    // 就取消，会把最常用的"点击激活"一并取消 → 激活后完全不弹，是回归）。
    private readonly DispatcherTimer _activationTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };

    // 顶栏折叠态：默认收起（折叠 = 只留右侧「⌄ 展开管理」细条，省卡片墙纵向空间；展开 = 显示数据管理/退出/本地等）。
    // 置顶钮已下沉到工具条操作组常驻，不随顶栏隐藏。非持久化——重启回到默认收起。
    private bool _headerExpanded;

    private const double Gap = 16; // .list gap:16px（卡片右/下外边距，MakeCard 使用）

    public MainWindow(Settings settings, TrayIconService? tray, bool startHidden = false)
    {
        InitializeComponent();
        // 标题栏版本标识：读取程序集信息版本(AssemblyInformationalVersion, csproj <Version> 生成, 如 0.7.4)
        Title = "剪贴板 v" + (Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "0");
        _settings = settings;
        _tray = tray;
        if (_tray != null) _tray.ShowMainRequested += ShowMainFromTray;

        // 开机自启静默（/startup）：以最小化 + 无任务栏按钮创建，等同"托盘待命"——
        // 句柄照常创建（watcher/定时器/托盘都起来），但用户看不到也不被打扰；
        // 因窗口从未激活，watcher 保持 Paused（前台语义，不抢收剪贴板）。托盘双击/二次双击唤出即恢复。
        // 参考 WindowTinter：窗口以最小化态创建，无闪烁。
        if (startHidden)
        {
            WindowState = WindowState.Minimized;
            ShowInTaskbar = false;
        }

        // 服务装配（数据目录 exe 同目录 data/）
        _storage = new Storage(App.DataDir);
        _fileStore = new FileStore(App.DataDir);
        _svc = new ClipService(_storage, _fileStore);

        // M5c：启动定时自动同步（1 分钟轮询，到点才跑；编排逻辑在 SyncController；接线见 MainWindow.SyncOps.InitAutoSync）
        _sync = new SyncController(_storage, _fileStore, App.DataDir);
        _sync.ConfigChanged += RefreshAccountBadge; // 配置变化（首次绑定/切账号/同步后）→ 刷新顶栏账号徽章
        InitAutoSync();

        // 弹窗宿主 + Toast 初始化（独立顶层 Window，可超出主窗口边界）
        ModalHost.Attach(this);
        ToastService.Init();

        // 搜索防抖（对齐 Web 100ms 微防抖：只防极速输入时的 DOM 重建）
        _searchTimer.Tick += (_, _) => { _searchTimer.Stop(); RefreshWall(); };

        // 置顶状态恢复
        PinBtn.IsChecked = _settings.AlwaysOnTop;
        Topmost = _settings.AlwaysOnTop;
        // 列数/归档按钮初值（M3b-1）
        UpdateColsBtnText();
        ArchBtn.Content = "归档·关";
        ArchBtn.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x84, 0x84, 0x84));
        // 开机自启按钮初值（以注册表 Run 实际态为准——用户可能被任务管理器禁用/手动改注册表）
        RefreshAutoStartBtn();

        // 激活后延迟弹窗：Tick 由 OnActivated 触发(见 OnActivated 注释)；这里只注册一次。
        _activationTimer.Tick += ActivationTimer_Tick;

        ApplyTopBarState(); // 顶栏初始折叠态（默认收起省空间；置顶钮已在工具条常驻，不受影响）
        RefreshAccountBadge(); // 启动即按是否已绑定账号刷新徽章（纯本地=「本地」，已同步=远端账号昵称）

        Loaded += (_, _) => RefreshWall();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        this.EnableImmersiveDarkTitleBar();
        // 剪贴板监听：句柄就绪后创建 + 订阅 + 注册（Paused 初始 true，等待首次激活——对齐 Web 前台语义）
        _watcher = new ClipboardWatcher(new System.Windows.Interop.WindowInteropHelper(this).Handle) { Paused = true };
        _watcher.ClipboardChanged += OnClipboardChanged;
        _watcher.Attach();
    }

    // ---- 剪贴板自动提示（对齐 Web clipboardchange + 激活检测）：有内容→存卡；已有内容→编辑弹窗 ----
    //  两条触发路径（watcher 剪贴板事件 / OnActivated 窗口激活）统一走 TryAutoPrompt，用剪贴板序列号去重，
    //  避免"一次复制 + 切回窗口/关弹窗再激活"被两条路径各弹一次（连续弹两次窗）。
    private void OnClipboardChanged()
    {
        if (_watcher == null) return;
        TryAutoPrompt();
    }

    private void PromptFromActivation()
    {
        TryAutoPrompt();
    }

    /// <summary>自上次处理后剪贴板是否又变了一次（序列号判定）。返回 false = 同一份内容被重复触发，不重复弹窗。
    /// 用序列号而非内容比对：实测相同内容再次复制，序列号照样递增——那是真实的新复制，不该被吞掉。</summary>
    private bool TryTakeClipboardSeq()
    {
        var seq = ClipboardNative.SequenceNumber;
        if (_lastHandledSeq == seq) return false;
        _lastHandledSeq = seq;
        return true;
    }

    private void TryAutoPrompt()
    {
        if (ModalHost.IsOpen) return; // 已开弹窗不覆盖
        string text;
        try { text = (Clipboard.GetText() ?? "").Trim(); }
        catch { return; }
        // 纯图片剪贴板（截图 Win+Shift+S / 右键复制图片 / 微信QQ复制图片：无文本但有 Bitmap/DIB/PNG）
        // 也应弹存卡窗——此前只认文本，图片复制永远走不到 OpenPasteDialog（图片识别断点①）。
        // v0.7.6-P2：**文件剪贴板**（资源管理器复制文件，只有 FileDrop 无文本无图）此前同样被这里挡掉 ——
        // 这是「复制文件后程序毫无反应、跟 Web 体验不一样」的第一道断点（断点①的文件版）。
        // 补齐后：复制文件 → 自动弹存卡窗 → PasteDialog.autoFill 的文件分支自动填入（全程无需 Ctrl+V）。
        if (text.Length == 0 && !ClipboardHelper.IsImageOnlyClipboard()
            && ClipboardHelper.GetFileDropList() == null) return;
        if (!TryTakeClipboardSeq()) return; // 剪贴板未再变化（激活/事件/反复切回窗口都在这里收敛）
        // 去重预查统一收敛到 OpenPasteDialog：剪贴板文本已存在 → 只弹编辑窗；否则开存卡窗（autoFill）
        OpenPasteDialog();
    }

    // ---- 弹窗编排 ----

    private void OpenPasteDialog(bool skipAutoFill = false)
    {
        // 预查（对齐 Web findDuplicateClip 语义 + 用户期望"先检索再弹，只出一扇"）：
        //   剪贴板文本已存在 → 直接弹编辑窗（不再"存卡窗 → autoFill → 300ms 检测 → 跳编辑窗"两扇窗连续出现）。
        //   剪贴板为空/非文本（手动输入、拖文件/图片）→ 不预查，正常开存卡窗；用户在窗内改文本撞重仍由
        //   PasteDialog 输入级兜底跳转（单扇窗内过渡，可接受且合理）。
        //   skipAutoFill=true（v0.7.3 dup 编辑窗「＋ 新建」直达）：不清剪贴板 autoFill → 不做预查，直接空白手动输入。
        if (!skipAutoFill)
        {
            string pre;
            try { pre = (Clipboard.GetText() ?? "").Trim(); }
            catch { pre = ""; }
            if (pre.Length > 0)
            {
                var dup = ClipService.FindDuplicate(pre, _svc.Search(""));
                if (dup != null)
                {
                    if (!dup.Archived) { OpenEditDialog(dup, dup: true); return; }
                    ToastService.Flash("已有相同内容");
                    return;
                }
            }
        }

        var dlg = new PasteDialog(_svc, _fileStore, () => _svc.Search(""), GetAllTags, skipAutoFill);
        dlg.DuplicateFound += c =>
        {
            if (!c.Archived) OpenEditDialog(c, dup: true);
            else ToastService.Flash("已有相同内容");
        };
        dlg.Saved += RefreshWall;
        ModalHost.Show(dlg);
    }

    private void OpenEditDialog(ClipItem c, bool dup)
    {
        var dlg = new EditDialog(_svc, c, GetAllTags, dup);
        dlg.Saved += RefreshWall;
        dlg.Archived += RefreshWall;
        // v0.7.3：dup 编辑窗「＋ 新建」→ 关掉当前 dup 窗，打开干净存入窗（skipAutoFill：忽略剪贴板，手动录入全新内容）
        dlg.NewRequested += () =>
        {
            ModalHost.Close(); // 关掉本 dup 编辑窗
            OpenPasteDialog(skipAutoFill: true); // 干净存入窗：忽略剪贴板 → 无预查 dup，直接空白手动输入
        };
        ModalHost.Show(dlg);
    }

    /// <summary>系统已有标签（聚合全部条目去重，对齐 /api/tags）。</summary>
    private List<string> GetAllTags()
        => _svc.Search("").SelectMany(c => c.Tags ?? new List<string>())
            .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();

    // ---- 卡片墙渲染（搜索/类型/标签过滤 + 空状态双文案 + 自适应列数） ----

    private void RefreshWall()
    {
        var clips = _svc.Search(_q, _tagFilter, _typeFilter, includeArchived: _includeArchived);
        WallPanel.Children.Clear();
        _cards.Clear();
        _visibleIds = clips.Select(c => c.Id).ToList();
        foreach (var c in clips)
        {
            var card = MakeCard(c);
            card.BatchMode = _batchMode; // 批量模式：右上 ✕ 原位替换勾选框 + 单击整卡切换选择
            card.SetSelected(_batchMode && _batchSel.Contains(c.Id)); // 重建后恢复选中态
            card.SelectionToggled += OnCardSelectionToggled;
            _cards[c.Id] = card;
            WallPanel.Children.Add(card);
        }

        var hasFilter = _q.Length > 0 || _tagFilter.Length > 0 || _typeFilter != "all" || _includeArchived;
        EmptyHint.Text = hasFilter
            ? "没有匹配的内容 — 试试调整搜索词、标签或类型"
            : "还没有内容 — 顶部粘贴框 Ctrl+V 即存，或拖文件进来";
        EmptyHint.Visibility = clips.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        RenderTagBar();   // 标签集合随活跃+归档变化同步刷新
        UpdateColumnWidth(); // 重建后同步列宽
        SyncBatchUI();    // 批量计数/全选按钮文案随可见集变化
    }

    /// <summary>卡片装配 + 事件接线（对齐 clipCard 各按钮 → MainWindow 编排）。</summary>
    private CardView MakeCard(ClipItem c)
    {
        var card = new CardView(fileId =>
        {
            try { return _fileStore.ReadAllBytes(fileId); }
            catch { return Array.Empty<byte>(); }
        });
        card.SetClip(c);
        // 卡片间距：WrapPanel 无 gap 概念，Web .list{gap:16px} 靠右/下外边距实现。
        // ItemWidth 已在 UpdateColumnWidth 里减掉一个 Gap，此处补上对应的右/下 Margin，
        // 否则每张卡紧贴排列（左侧视觉连成一片），Gap 只变成行尾空白。
        card.Margin = new Thickness(0, 0, Gap, Gap);
        card.EditRequested += cc => OpenEditDialog(cc, dup: false);
        card.TogglePinRequested += cc =>
        {
            try
            {
                var pinned = _svc.TogglePin(cc.Id);
                ToastService.Flash(pinned ? "已置顶" : "已取消置顶");
                RefreshWall(); // 置顶重排（对齐 Web refreshList）
            }
            catch (Exception ex) { ToastService.Error(ex.Message); }
        };
        card.DeleteRequested += cc =>
        {
            ModalHost.Confirm("删除这条内容？", () =>
            {
                try
                {
                    var delFileId = _svc.Delete(cc.Id); // v0.7.2：跨活跃+归档删除并记墓碑（防同步复活）；返回文件 id 供实体清理
                    if (!string.IsNullOrEmpty(delFileId)) _fileStore.Delete(delFileId); // 联动清理文件实体（对齐 Web 路由层 deleteFile）
                    ToastService.Flash("已删除");
                    RefreshWall();
                }
                catch (Exception ex) { ToastService.Error(ex.Message); }
            }, "删除");
        };
        card.DownloadRequested += cc =>
        {
            try
            {
                var dlg = new Microsoft.Win32.SaveFileDialog { FileName = cc.FileName ?? "file", Filter = "所有文件 (*.*)|*.*" };
                ModalHost.SuppressDismiss = true; // 子对话框期间屏蔽失焦自动关闭
                var ok = dlg.ShowDialog(this) == true;
                ModalHost.SuppressDismiss = false;
                if (ok)
                {
                    File.WriteAllBytes(dlg.FileName, _fileStore.ReadAllBytes(cc.FileId)); // 对齐 downloadFile：attachment 原名落盘
                    ToastService.Flash("已下载");
                }
            }
            catch (Exception ex) { ToastService.Error("下载失败: " + ex.Message); }
        };
        card.CopyImageRequested += cc => CopyImageToClipboard(cc); // M3b-2b：图片卡单击复制到系统剪贴板
        card.CopyRequested += (cc, x, y) =>
        {
            var text = (cc.Type == "link" ? cc.Url : cc.Content) ?? "";
            if (string.IsNullOrEmpty(text)) { ToastService.Error("没有可复制的内容"); return; }
            if (TryCopy(() => ClipboardHelper.SetText(text)))
            {
                card.MarkCopied();
                MarkSelfWriteAndBump(cc);
                ToastService.Flash("已复制", x, y);
            }
        };
        card.OpenJsonRequested += cc =>
        {
            var dlg = new JsonDialog(cc);
            // 复制 JSON 也属本程序写剪贴板：记下序列号，关窗后激活不再误弹存入窗
            dlg.Copied += () => MarkSelfWrite();
            dlg.SaveRequested += (c, newContent) =>
            {
                try
                {
                    // v0.6.11：带 html 的条目覆盖保存同步重建 html，防 content/html 不一致；标题/标签原样保留
                    var newHtml = !string.IsNullOrEmpty(c.Html) && newContent != (c.Content ?? "")
                        ? RichText.TextToHtml(newContent) : null;
                    if (_svc.Update(c.Id, c.Title, c.Tags, null, newContent, null, newHtml) != null)
                    {
                        ToastService.Flash("已覆盖保存");
                        RefreshWall();
                        ModalHost.Close();
                    }
                    else ToastService.Error("条目不存在");
                }
                catch (Exception ex) { ToastService.Error(ex.Message); }
            };
            ModalHost.Show(dlg);
        };
        card.OpenLinkRequested += cc =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(cc.Url) { UseShellExecute = true });
            }
            catch { ToastService.Error("无法打开链接"); }
        };
        card.CopyPlainRequested += (cc, x, y) =>
        {
            if (TryCopy(() => ClipboardHelper.SetText(cc.Content ?? "")))
            {
                card.MarkCopied();
                MarkSelfWriteAndBump(cc);
                ToastService.Flash("已复制", x, y);
            }
        };
        card.CopyRichRequested += (cc, x, y) =>
        {
            bool ok;
            try { ok = RichText.CopyRich(cc.Html, cc.Content); }
            catch (Exception ex) { AppLog.Info("rich copy failed: " + ex); ok = false; }
            if (ok)
            {
                card.MarkCopied();
                MarkSelfWriteAndBump(cc);
                ToastService.Flash("富文本已复制（含格式）", x, y);
            }
            else ToastService.Error("富文本复制失败，请用独立窗口重试");
        };
        card.TagFilterRequested += tag =>
        {
            _tagFilter = (_tagFilter == tag) ? "" : tag; // 同一标签再点 → 清除（对齐 Web .tagchip toggle）
            RenderTagBar();
            RefreshWall();
        };
        card.RestoreRequested += cc =>
        {
            try
            {
                if (_svc.Unarchive(cc.Id)) ToastService.Flash("已恢复");
                else ToastService.Flash("活跃区已存在同 id，未恢复");
                RefreshWall(); // 恢复后归档区减少 + 活跃区可能新增（refreshWall 包含归档过滤）
                RenderTagBar(); // 标签集合可能变化（恢复条目带新标签）
            }
            catch (Exception ex) { ToastService.Error(ex.Message); }
        };
        return card;
    }

    /// <summary>复制图片到系统剪贴板（M3b-2b，对齐 Web copyImageToClipboard：解码文件为 BitmapSource 后走 ClipboardHelper；
    /// 成功 flash + 计数 + 来源抑制；失败降级 errToast，不打开预览（WPF 已有 toast 反馈）。</summary>
    private void CopyImageToClipboard(ClipItem c)
    {
        if (string.IsNullOrEmpty(c.FileId)) { ToastService.Error("图片缺失"); return; }
        var bytes = _fileStore.ReadAllBytes(c.FileId);
        if (bytes == null || bytes.Length == 0) { ToastService.Error("图片为空"); return; }
        BitmapSource? bmp = null;
        try
        {
            using var ms = new MemoryStream(bytes);
            var bi = new BitmapImage();
            bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.StreamSource = ms; bi.EndInit(); bi.Freeze();
            bmp = bi;
        }
        catch (Exception ex) { AppLog.Info("decode image failed: " + ex.Message); ToastService.Error("图片解码失败"); return; }
        if (bmp != null && TryCopy(() => ClipboardHelper.SetImage(bmp)))
        {
            MarkSelfWriteAndBump(c);
            ToastService.FlashAtMouse("图片已复制，可直接粘贴");
        }
    }

    /// <summary>统一复制写入：成功返回 true；失败记录异常（含剪贴板占用方诊断）并弹错误 toast（消除 5 处重复处理）。</summary>
    private static bool TryCopy(Action write, string? context = null)
    {
        try { write(); return true; }
        catch (Exception ex)
        {
            AppLog.Info($"copy failed{(context != null ? " (" + context + ")" : "")}: {ex.GetType().Name}(0x{unchecked((uint)ex.HResult):X8}): {ex.Message}");
            ToastService.Error("复制失败，请手动选择复制");
            return false;
        }
    }

    /// <summary>复制成功后：标记为"本程序自己写入"+ 持久化复制计数（对齐 Web bumpCopyCount）。
    /// 两条自动弹窗路径各自用确定性判定收敛，都不依赖时间窗：
    ///   - 剪贴板事件路径：watcher 在 WM 到达瞬间用属主 PID 判定属本进程 → 忽略；
    ///   - 激活路径（OnActivated → TryAutoPrompt）：用剪贴板序列号判重，写入后记下新序列号 → 切回不再误弹。</summary>
    private void MarkSelfWriteAndBump(ClipItem c)
    {
        MarkSelfWrite();
        try { _svc.BumpCopyCount(c.Id); } catch { /* 计数失败不影响 */ }
    }

    /// <summary>记录"本程序刚写入剪贴板"：把当前序列号记为已处理，激活路径据此不再弹窗（对齐 Web 语义）。</summary>
    private void MarkSelfWrite() => _lastHandledSeq = ClipboardNative.SequenceNumber;

    /// <summary>列宽自适应（对齐 CSS auto-fill minmax(280px,1fr)；仅改 ItemWidth 不重建卡片——拖动窗口不闪烁）。
    /// maxColumns 来自 Settings：0 = 自动 4 上限，1~4 = 用户锁定（M3b-1 接入）。</summary>
    private void UpdateColumnWidth()
    {
        var w = WallPanel.ActualWidth;
        if (w <= 0) return;
        var cols = LayoutRules.ColumnsFor(w, _settings.MaxColumns);
        // ItemWidth 只负责「卡片 + 右间隙」的槽位宽度，不能再减 Gap：
        // 卡片自身已有 Margin.Right=Gap（见 MakeCard），若这里再减一次，
        // 每行右端会白白空出 (cols+1)*Gap —— 看起来像「给滚动条留了一大条空位」。
        // Floor 防止 cols 个槽位因小数累加超出 w 而误换行。
        WallPanel.ItemWidth = Math.Floor(w / cols);
    }

    private void WallPanel_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateColumnWidth();

    // ---- 窗口生命周期：置顶 / 前台语义 / 退出 ----

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        if (_watcher != null) _watcher.Paused = false; // 前台激活恢复捕获
        if (!_everActivated) { _everActivated = true; return; } // 首次启动不自动弹窗
        // 切回窗口时：剪贴板有内容则弹存卡/编辑窗（对齐 Web 前台语义）。
        // 延迟 200ms 再弹，等"点击激活"那一记 mouse down/up 排空、主窗口稳定激活，弹窗 Show 才能稳定持焦
        // （若在 OnActivated 同步弹，弹窗 Show 抢激活后紧接的鼠标落点会把焦点抢回 → 弹窗 Deactivated → 闪没）。
        // 注意：绝不能因为"随后收到用户输入"就取消本次延迟——Win32 点击非活动窗口时
        // WM_MOUSEACTIVATE(→OnActivated) 在 WM_LBUTTONDOWN(→PreviewMouseDown) 之前投递，
        // 若用 mouse-down 取消，会把最常用的"点击激活"一并取消 → 激活后完全不弹(回归)。
        _activationTimer.Stop();
        _activationTimer.Start();
    }

    /// <summary>激活延迟到点：主窗口已稳定激活才弹；若此间又失活(用户切走)则本次作罢。</summary>
    private void ActivationTimer_Tick(object? sender, EventArgs e)
    {
        _activationTimer.Stop();
        if (_everActivated && IsActive && !ModalHost.IsOpen)
            PromptFromActivation();
    }
    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        if (_watcher != null) _watcher.Paused = true; // 失活暂停（对齐 Web 前台语义）
    }

    // ---- 全局快捷键（仅在主窗口已激活、无弹窗、且焦点不在文本框内时生效） ----
    //  - Ctrl+V：快速打开存入编辑器并自动填入剪贴板内容（PasteDialog 打开即 autoFill）
    //  - 空格：把输入光标快速定位到搜索框（打字即可搜索）；空格本身不落入搜索框
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        // 已有弹窗时不拦截（交给弹窗自身处理，避免重复开窗或吞掉弹窗内的空格/粘贴）
        if (ModalHost.IsOpen) return;
        // 焦点已在文本框（搜索框/存入框等）：保留原生行为（空格正常输入、Ctrl+V 正常粘贴）
        if (Keyboard.FocusedElement is TextBox or PasswordBox or ComboBox) return;

        if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            e.Handled = true;
            OpenPasteDialog(); // 打开即自动填入剪贴板内容
            return;
        }

        if (e.Key == Key.Space)
        {
            e.Handled = true; // 空格不落入搜索框
            SearchBox.Focus();
            SearchBox.SelectAll(); // 全选既有查询，直接输入即新检索（对齐 Web 平台版）
        }
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized && _watcher != null) _watcher.Paused = true;
    }

    /// <summary>★ 置顶切换：金=开，状态持久化。</summary>
    private void PinBtn_Click(object sender, RoutedEventArgs e)
    {
        Topmost = PinBtn.IsChecked == true;
        _settings.AlwaysOnTop = Topmost;
        _settings.Save();
        AppLog.Info("always-on-top: " + Topmost);
    }

    /// <summary>宽屏自适应（规则在 LayoutRules.MaxWidthFor，纯函数可单测）。
    /// 阈值基于客户区宽度（ActualWidth ≈ Web viewport），而非窗口总宽 e.NewSize.Width（含 DWM 边框 ~16px），
    /// 避免临界 1280 早一档切换。兜底走 e.NewSize.Width - 16（边框经验值）。</summary>
    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var w = ActualWidth > 0 ? ActualWidth : e.NewSize.Width - 16;
        if (w <= 0) return;
        ViewGrid.MaxWidth = LayoutRules.MaxWidthFor(w);
    }

    private void StoreBtn_Click(object sender, RoutedEventArgs e) => OpenPasteDialog();

    private void ExitBtn_Click(object sender, RoutedEventArgs e) => ReallyExit();

    /// <summary>顶栏折叠钮：收起/展开整条顶栏（标题 + 本地 + 占位 + 数据管理 + 退出）。
    /// 置顶钮(PinBtn)在工具条操作组常驻，不在此栏内、不随折叠隐藏。折叠 = 整条 TopBar Collapsed
    /// (Row0 归 0)，搜索/工具条整体上移、彻底不留细条；展开 = 恢复完整顶栏。实现见 ApplyTopBarState。</summary>
    private void ToggleTopBar_Click(object sender, RoutedEventArgs e)
    {
        _headerExpanded = !_headerExpanded;
        ApplyTopBarState();
    }

    /// <summary>应用顶栏折叠/展开态到可视元素。
    /// 折叠 = 整条 TopBar(Row0) Collapsed → 高度归 0，工具条/搜索整体上移，不留背景卡细条（彻底省空间）。
    /// 展开钮 TopExpandBtn 在工具条行1搜索框左侧作小图标钮常驻（不随顶栏隐藏），Content 按态切 ⌄/⌃。</summary>
    private void ApplyTopBarState()
    {
        if (TopBar == null) return; // InitializeComponent 前不会走（构造末尾才调）
        TopBar.Visibility = _headerExpanded ? Visibility.Visible : Visibility.Collapsed;
        TopExpandBtn.Content = _headerExpanded ? "⌃" : "⌄";
        TopExpandBtn.ToolTip = _headerExpanded
            ? "收起顶栏，卡片墙上移多出空间"
            : "展开管理区（数据管理 / 退出 / 本地状态）";
    }

    /// <summary>顶栏状态徽章显示优先级：账号昵称(DisplayName)非空 → 昵称；否则账号名(AccountName)非空 → 账号名；否则「本地」。
    /// 由 SyncController.ConfigChanged 订阅 + 启动时刷新；改昵称/切账号/同步后采纳远端昵称都会走到。</summary>
    private void RefreshAccountBadge()
    {
        if (AccountBadge == null) return;
        var cfg = _sync.Config;
        var nick = cfg?.DisplayName?.Trim();
        var acct = cfg?.AccountName?.Trim();
        if (!string.IsNullOrEmpty(nick))
        {
            AccountBadge.Text = nick;
            AccountBadge.ToolTip = string.IsNullOrEmpty(acct)
                ? $"账号昵称：{nick}"
                : $"账号昵称：{nick}（账号 {acct}） · {cfg!.Url}";
        }
        else if (!string.IsNullOrEmpty(acct))
        {
            AccountBadge.Text = acct;
            AccountBadge.ToolTip = $"已同步账号「{acct}」 · {cfg!.Url}（未设昵称，点数据管理可设置）";
        }
        else
        {
            AccountBadge.Text = "本地";
            AccountBadge.ToolTip = "本地数据（未绑定远端账号）";
        }
    }


    public void ReallyExit()
    {
        _reallyExit = true;
        AppLog.Info("exit");
        Application.Current.Shutdown();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        // 点 X / 最小化 → 收进托盘（不退出、停止可见），托盘「退出」才真退
        if (!_reallyExit && _tray != null)
        {
            e.Cancel = true;
            Hide();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _watcher?.Dispose();
    }

    /// <summary>从托盘/第二实例唤起：显示并恢复前置。托盘菜单/托盘双击/第二实例唤醒共用。</summary>
    public void ShowMainFromTray()
    {
        ShowInTaskbar = true; // startHidden 静默启动设过 false；唤出时恢复任务栏按钮
        Show();
        WindowState = WindowState.Normal;
        Activate();
        ForceForeground(); // 置前：绕过 Windows 前台锁，让窗口真正跳到最前（托盘/二次唤起常受前台限制）
    }

    /// <summary>第二实例双击唤起入口（区别于托盘右键——同样显示+置前）。</summary>
    public void WakeMainFromSecondInstance() => ShowMainFromTray();

    /// <summary>可靠置前：先 attach 前台再 SetForegroundWindow（Windows 前台锁限制下也能抢到焦点）。</summary>
    private void ForceForeground()
    {
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                _ = AttachThreadInput(GetCurrentThreadId(), GetWindowThreadProcessId(hwnd, IntPtr.Zero), true);
                SetForegroundWindow(hwnd);
                _ = AttachThreadInput(GetCurrentThreadId(), GetWindowThreadProcessId(hwnd, IntPtr.Zero), false);
            }
        }
        catch { /* 置前失败不致命：窗口至少已显示 */ }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr lpdwProcessId);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    // 同步逻辑已抽到 MainWindow.SyncOps.cs（partial class）
}
