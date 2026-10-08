using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WindowTinter
{
    /// <summary>点击滑轨直接跳到鼠标位置的自定义 TrackBar。自带底部视觉裁剪。</summary>
    internal class JumpTrackBar : TrackBar
    {
        public JumpTrackBar()
        {
            HandleCreated += (_, _) => ClipVisual();
            Resize += (_, _) => ClipVisual();
        }

        /// <summary>裁掉 TrackBar 原生控件底部的视觉溢出（轨道背景比声明区域大）。</summary>
        private void ClipVisual()
        {
            if (Width > 0 && Height > 0)
            {
                int clipH = Math.Max(Height - 8, 12);
                this.Region?.Dispose();
                this.Region = new Region(new Rectangle(0, 0, Width, clipH));
            }
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_LBUTTONDOWN = 0x0201;
            if (m.Msg == WM_LBUTTONDOWN)
            {
                int x = unchecked((short)((int)m.LParam & 0xFFFF));
                int channelW = Width - 24;
                if (channelW > 0)
                {
                    int newVal = (x - 12) * (Maximum - Minimum) / channelW + Minimum;
                    newVal = Math.Clamp(newVal, Minimum, Maximum);
                    Value = newVal; // 先跳到鼠标位置，再让默认 WndProc 处理拖拽
                }
            }
            base.WndProc(ref m);
        }
    }

    internal partial class MainForm : Form
    {
        // ── 核心状态 ──────────────────────────────────────────────

        private readonly Settings _settings;
        private readonly List<TargetEntry> _entries = new();
        private readonly Dictionary<TargetInfo, Panel> _pendingPanels = new();
        private readonly Dictionary<TargetInfo, Button> _selectButtons = new();
        private TargetInfo _selectedTarget;   // 非全局模式下，滑块当前编辑的目标

        private NotifyIcon _tray;
        private ContextMenuStrip _menu;
        private IntPtr _winEventHook;
        private Native.WinEventProc _winEventProc;
        private bool _reallyQuit;
        private Timer _autoBindTimer;
        private Timer _onShownTimer;
        private Timer _saveDebounceTimer;
        private Icon _appIcon;
        private float _dpiScale = 1f;   // 系统 DPI 缩放比（DeviceDpi/96）；用于手动换算运行时动态添加的面板尺寸

        private static readonly string AppVersion = GetAppVersion();
        private static string GetAppVersion()
        {
            var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            return ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "5.5.2";
        }

        // ── UI 控件 ────────────────────────────────────────────────

        private Label _lblStatus;
        private FlowLayoutPanel _pnlTargets;
        private Button _btnRefind;
        private CheckBox _chkEnabled;
        private TrackBar _tbBgAlpha;
        private Label _lblBgAlpha;
        private CheckBox _chkStartup;
        private CheckBox _chkBackdropPlate;
        private CheckBox _chkMinimizeTray;
        private CheckBox _chkGlobalTransparency;
        private CheckBox _chkGlobalCornerRadius;
        private TrackBar _tbCornerRadius;
        private Label _lblCornerRadius;

        // ── 窗口 ───────────────────────────────────────────────────

        public MainForm(bool startHidden = false)
        {
            // Win11 高 DPI：以 96 DPI 为设计基准，运行时由 AutoScaleMode.Dpi 按屏幕真实 DPI 自动缩放全部静态控件。
            // 关键：控件必须在 CreateControl（即本构造函数）阶段建好，自动缩放才会作用于它们——
            // 因此 BuildUI() 放在这里而非 OnLoad。BlackPlate 是物理像素分层窗，单独用 AutoScaleMode.None，不受影响。
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);

            _settings = Settings.Load();

            Text = $"暗幕 v{AppVersion}";
            var iconPath = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath) ?? ".", "app.ico");
            try { _appIcon = File.Exists(iconPath) ? new Icon(iconPath) : null; }
            catch { _appIcon = null; }
            Icon = _appIcon; // 图标缺失/损坏时退化为系统默认图标，避免启动崩溃
            ClientSize = new Size(470, 695);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            // 开机自启：直接最小化到托盘（WindowState 在句柄创建前设置，窗口以最小化态创建，无闪烁），
            // 且不显示任务栏按钮——仅托盘图标驻留。用户双击托盘即可打开设置窗口。
            if (startHidden)
            {
                WindowState = FormWindowState.Minimized;
                ShowInTaskbar = false;
            }
            Load += OnLoad;
            Shown += OnShown;
            Activated += OnActivated;
            FormClosing += OnFormClosing;
            FormClosed += (_, _) => Quit();

            BuildUI();
        }

        private void OnLoad(object sender, EventArgs e)
        {
            // 句柄已创建，DeviceDpi 为真实屏幕 DPI（进程已 PerMonitorV2 感知）。
            // 静态控件已由 AutoScaleMode.Dpi 自动缩放；运行时动态添加的面板按此因子换算尺寸。
            _dpiScale = Math.Max(DeviceDpi / 96f, 1f);

            // 启动时清除上次强制退出可能残留的透明效果
            RestoreAllTargets();

            BuildTray();
            InstallWinEventHook();

            // 3 秒一次检查是否有目标窗口新启动但未绑定
            _autoBindTimer = new Timer { Interval = 3000 };
            _autoBindTimer.Tick += (_, _) =>
            {
                if (!_settings.Enabled) return;

                // 清理窗口已销毁的条目——放回待激活列表
                bool anyChange = false;
                for (int i = _entries.Count - 1; i >= 0; i--)
                {
                    var e = _entries[i];
                    // 窗口已销毁 → 放回待激活列表
                    if (!Native.IsWindow(e.Tracker.TargetHandle))
                    {
                        e.Plate.HidePlate(); e.Tracker.Dispose(); e.Plate.Dispose();
                        _entries.RemoveAt(i);
                        if (e.UIPanel != null) _pnlTargets.Controls.Remove(e.UIPanel);
                        _selectButtons.Remove(e.Info);
                        AddPendingUI(e.Info); // 放回待激活
                        anyChange = true;
                    }
                }

                // 查找未绑定目标（包括待激活目标）
                foreach (var t in _settings.Targets)
                {
                    if (_entries.Any(e => e.Info == t)) continue;
                    TryBindTarget(t);
                    var entry = _entries.FirstOrDefault(e => e.Info == t);
                    if (entry != null)
                    {
                        RemovePendingUI(t);
                        ApplyMaskNow(entry);
                        anyChange = true;
                    }
                }
                if (anyChange) BeginInvoke(() => UpdateUI());
            };
            _autoBindTimer.Start();

            // 滑块去抖：拖动停止 200ms 后写盘
            _saveDebounceTimer = new Timer { Interval = 200 };
            _saveDebounceTimer.Tick += (_, _) => { _saveDebounceTimer.Stop(); _settings.Save(); };

            if (_settings.Enabled)
            {
                foreach (var t in _settings.Targets)
                    TryBindTarget(t);
                // 移除已成功绑定的待激活面板
                foreach (var entry in _entries)
                    RemovePendingUI(entry.Info);
            }

            // 非全局模式下，默认选中第一个目标
            if (!_settings.GlobalTransparency && _selectedTarget == null)
                _selectedTarget = _settings.Targets.FirstOrDefault();

            _settings.ApplyStartWithWindows();
            UpdateUI();
        }

        private void OnShown(object _, EventArgs __)
        {
            foreach (var e in _entries) e.Tracker.RefreshNow();
            // 用一次性定时器推迟 100ms，确保消息泵完整运转后 UpdateLayeredWindow 稳定
            _onShownTimer = new Timer { Interval = 100 };
            _onShownTimer.Tick += (s, args) => { _onShownTimer.Stop(); _onShownTimer.Dispose(); foreach (var e in _entries) ApplyMaskNow(e); };
            _onShownTimer.Start();
        }

        /// <summary>点击 WindowTinter 自身时 WinEvent SKIPOWNPROCESS 会跳过前台事件，
        /// 通过 Activated 补发 RefreshForeground 让目标切到透明状态。
        /// _inActivated 防 BeginInvoke 嵌套重入（Activated 可能连续触发）。</summary>
        private bool _inActivated;
        private void OnActivated(object _, EventArgs __)
        {
            if (_inActivated) return;
            _inActivated = true;
            BeginInvoke(new Action(() =>
            {
                try { foreach (var e in _entries) e.Tracker.RefreshForeground(); }
                finally { _inActivated = false; }
            }));
        }

        /// <summary>启动时遍历所有已配置目标，恢复透明度——处理上次强制杀进程残留。</summary>
        private void RestoreAllTargets()
        {
            foreach (var t in _settings.Targets)
            {
                var h = TargetTracker.FindByTitleAndProcess(t.WindowTitle, t.ProcessName);
                if (h != IntPtr.Zero)
                    SetTargetAlpha(h, 255);
            }
        }

        // ════════════════════════════════════════════════════════════
        // 目标条目管理
        // ════════════════════════════════════════════════════════════

        private class TargetEntry
        {
            public TargetInfo Info;
            public TargetTracker Tracker;
            public BlackPlate Plate;
            public Panel UIPanel;
        }

        /// <summary>创建条目并挂载 OnUpdate——所有蒙版显示逻辑的唯一入口。</summary>
        private TargetEntry CreateEntry(TargetInfo info)
        {
            var tracker = new TargetTracker();
            var plate = new BlackPlate();
            plate.CornerRadius = _settings.GlobalCornerRadius ? _settings.CornerRadius : info.CornerRadius;

            byte _lastBgAlpha = 255;

            tracker.OnUpdate += (r, visible) =>
            {
                // 全局模式用全局透明度，否则用该目标自己的配置
                int bgPct = _settings.GlobalTransparency ? _settings.BackgroundAlpha : info.BackgroundAlpha;

                if (!_settings.Enabled || !visible)
                {
                    plate.HidePlate();
                    if (_lastBgAlpha != 255) { SetTargetAlpha(tracker.TargetHandle, 255); _lastBgAlpha = 255; }
                    return;
                }

                // 目标设半透明（前后台统一），正后方按需钉纯黑底板
                byte targetAlpha = (byte)((100 - bgPct) * 255 / 100);
                if (_lastBgAlpha != targetAlpha)
                {
                    SetTargetAlpha(tracker.TargetHandle, targetAlpha);
                    _lastBgAlpha = targetAlpha;
                }
                if (_settings.BackdropBlackPlate)
                {
                    // 用 DWM 扩展框架边界（去阴影）对齐底板，避免微信等程序遮罩外溢
                    var visibleRect = Native.GetVisibleWindowRect(tracker.TargetHandle);
                    plate.AlignBehind(tracker.TargetHandle, visibleRect);
                }
                else plate.HidePlate();
            };

            return new TargetEntry { Info = info, Tracker = tracker, Plate = plate };
        }

        private static void SetTargetAlpha(IntPtr hwnd, byte alpha)
        {
            if (hwnd == IntPtr.Zero || !Native.IsWindow(hwnd)) return;
            try
            {
                int ex = (int)Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE);
                bool hasLayered = (ex & Native.WS_EX_LAYERED) != 0;

                if (alpha >= 255)
                {
                    if (hasLayered)
                    {
                        Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, (IntPtr)(ex & ~Native.WS_EX_LAYERED));
                        // MSDN: 移除 WS_EX_LAYERED 后用 RedrawWindow 而非 InvalidateRect
                        Native.RedrawWindow(hwnd, IntPtr.Zero, IntPtr.Zero,
                            Native.RDW_INVALIDATE | Native.RDW_ERASE | Native.RDW_FRAME | Native.RDW_ALLCHILDREN);
                    }
                }
                else
                {
                    if (!hasLayered)
                        Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, (IntPtr)(ex | Native.WS_EX_LAYERED));
                    Native.SetLayeredWindowAttributes(hwnd, 0, alpha, Native.LWA_ALPHA);
                    Native.InvalidateRect(hwnd, IntPtr.Zero, true);
                }
            }
            catch (Exception ex) { Debug.WriteLine($"SetTargetAlpha failed for 0x{hwnd:X}: {ex.Message}"); }
        }

        // ── 提权检测：目标窗口以管理员运行、本程序非提权时无法修改其透明度 ──

        private bool _elevationWarned;

        private static bool? IsTargetElevated(IntPtr hwnd)
        {
            try
            {
                Native.GetWindowThreadProcessId(hwnd, out uint pid);
                IntPtr hProc = Native.OpenProcess(Native.PROCESS_QUERY_INFORMATION, false, pid);
                if (hProc == IntPtr.Zero) return null;
                try
                {
                    if (!Native.OpenProcessToken(hProc, Native.TOKEN_QUERY, out IntPtr hToken)) return null;
                    try
                    {
                        if (Native.GetTokenInformation(hToken, 20 /*TokenElevation*/,
                                out Native.TOKEN_ELEVATION te,
                                (uint)Marshal.SizeOf<Native.TOKEN_ELEVATION>(), out uint _))
                            return te.TokenIsElevated != 0;
                    }
                    finally { Native.CloseHandle(hToken); }
                }
                finally { Native.CloseHandle(hProc); }
            }
            catch { }
            return null;
        }

        private static bool IsCurrentProcessElevated()
        {
            try
            {
                using var p = Process.GetCurrentProcess();
                IntPtr hProc = Native.OpenProcess(Native.PROCESS_QUERY_INFORMATION, false, (uint)p.Id);
                if (hProc == IntPtr.Zero) return false;
                try
                {
                    if (!Native.OpenProcessToken(hProc, Native.TOKEN_QUERY, out IntPtr hToken)) return false;
                    try
                    {
                        if (Native.GetTokenInformation(hToken, 20 /*TokenElevation*/,
                                out Native.TOKEN_ELEVATION te,
                                (uint)Marshal.SizeOf<Native.TOKEN_ELEVATION>(), out uint _))
                            return te.TokenIsElevated != 0;
                    }
                    finally { Native.CloseHandle(hToken); }
                }
                finally { Native.CloseHandle(hProc); }
            }
            catch { }
            return false;
        }

        /// <summary>触发刷新——走 OnUpdate 完整路径（透明度 + 下方垫黑）。</summary>
        private void ApplyMaskNow(TargetEntry e)
        {
            e.Tracker.RefreshForeground();
        }

        private void TryBindTarget(TargetInfo info)
        {
            var boundHandles = new HashSet<IntPtr>(_entries.Select(e => e.Tracker.TargetHandle));
            var h = TargetTracker.FindByTitleAndProcess(info.WindowTitle, info.ProcessName, boundHandles);
            if (h == IntPtr.Zero) return;

            // 已绑定到同一个 handle → 跳过
            if (_entries.Any(e => e.Tracker.TargetHandle == h)) return;

            // 有该目标的旧条目（窗口关闭后 reopen）→ 更新 handle 复用
            var stale = _entries.FirstOrDefault(e => e.Info == info && !Native.IsWindow(e.Tracker.TargetHandle));
            if (stale != null)
            {
                stale.Tracker.TargetHandle = h;
                stale.Tracker.RefreshNow();
                return;
            }

            // 新绑定
            var entry = CreateEntry(info);
            entry.Tracker.TargetHandle = h;
            entry.Tracker.RefreshNow();
            _entries.Add(entry);

            AddTargetUI(entry);

            // 提权提示：目标以管理员身份运行而本程序未提权时，改透明度会静默失败
            if (!_elevationWarned && !IsCurrentProcessElevated())
            {
                bool? elevated = IsTargetElevated(h);
                if (elevated == true)
                {
                    _elevationWarned = true;
                    _tray?.ShowBalloonTip(5000, "无法修改此窗口透明度",
                        "目标窗口以管理员身份运行，而本程序不是。请右键“以管理员身份运行”本程序。",
                        ToolTipIcon.Warning);
                }
            }
        }

        private void AddTargetUI(TargetEntry entry)
        {
            int w = _pnlTargets.ClientSize.Width - (int)(6 * _dpiScale);
            bool sel = !_settings.GlobalTransparency && _selectedTarget != null && _selectedTarget.Equals(entry.Info);
            var pnl = new Panel { Size = new Size(w, (int)(32 * _dpiScale)), Margin = new Padding(0, 0, 0, (int)(3 * _dpiScale)),
                BackColor = sel ? Color.FromArgb(50, 70, 95) : Color.FromArgb(40, 40, 40),
                Cursor = Cursors.Hand };
            pnl.Click += (_, _) => SelectTarget(entry.Info);

            var lbl = new Label
            {
                Text = $"  {entry.Info}", AutoSize = true,
                Location = new Point((int)(4 * _dpiScale), (int)(8 * _dpiScale)), MaximumSize = new Size((int)(270 * _dpiScale), (int)(20 * _dpiScale)),
                ForeColor = Color.FromArgb(224, 224, 224),
                Cursor = Cursors.Hand
            };
            lbl.Click += (_, _) => SelectTarget(entry.Info);
            pnl.Controls.Add(lbl);

            bool btnEnabled = !_settings.GlobalTransparency;
            var btnSelect = CreateSelectButton(w, sel, btnEnabled, (_, _) => SelectTarget(entry.Info));
            pnl.Controls.Add(btnSelect);
            _selectButtons[entry.Info] = btnSelect;

            var btnRemove = CreateRemoveButton(w, (_, _) => RemoveEntry(entry, pnl));
            pnl.Controls.Add(btnRemove);

            entry.UIPanel = pnl;
            _pnlTargets.Controls.Add(pnl);
            // 将新面板排在倒数第二（在最后添加的控件之前）
            if (_pnlTargets.Controls.Count >= 2)
                _pnlTargets.Controls.SetChildIndex(pnl, _pnlTargets.Controls.Count - 2);
        }

        /// <summary>为尚未启动的目标窗口创建灰色"待激活"面板。</summary>
        private void AddPendingUI(TargetInfo info)
        {
            if (_pendingPanels.ContainsKey(info)) return;
            int w = _pnlTargets.ClientSize.Width - (int)(6 * _dpiScale);
            bool sel = !_settings.GlobalTransparency && _selectedTarget != null && _selectedTarget.Equals(info);
            var pnl = new Panel { Size = new Size(w, (int)(32 * _dpiScale)), Margin = new Padding(0, 0, 0, (int)(3 * _dpiScale)),
                BackColor = sel ? Color.FromArgb(50, 70, 95) : Color.FromArgb(40, 40, 40),
                Cursor = Cursors.Hand };
            pnl.Click += (_, _) => SelectTarget(info);

            var lbl = new Label
            {
                Text = $"  ⏳ 待激活 — {info}",
                AutoSize = true,                 Location = new Point((int)(4 * _dpiScale), (int)(8 * _dpiScale)),
                MaximumSize = new Size((int)(250 * _dpiScale), (int)(20 * _dpiScale)),
                ForeColor = Color.FromArgb(120, 120, 120),
                Cursor = Cursors.Hand
            };
            lbl.Click += (_, _) => SelectTarget(info);
            pnl.Controls.Add(lbl);

            bool btnEnabled = !_settings.GlobalTransparency;
            var btnSelect = CreateSelectButton(w, sel, btnEnabled, (_, _) => SelectTarget(info));
            pnl.Controls.Add(btnSelect);
            _selectButtons[info] = btnSelect;

            var btnRemove = CreateRemoveButton(w, (_, _) => RemovePendingTarget(info, pnl));
            pnl.Controls.Add(btnRemove);

            _pendingPanels[info] = pnl;
            _pnlTargets.Controls.Add(pnl);
            if (_pnlTargets.Controls.Count >= 2)
                _pnlTargets.Controls.SetChildIndex(pnl, _pnlTargets.Controls.Count - 2);
        }

        private void RemovePendingUI(TargetInfo info)
        {
            if (_pendingPanels.TryGetValue(info, out var pnl))
            {
                _pnlTargets.Controls.Remove(pnl);
                _pendingPanels.Remove(info);
            }
        }

        private void RemovePendingTarget(TargetInfo info, Panel pnl)
        {
            RemovePendingUI(info);
            _selectButtons.Remove(info);
            if (_selectedTarget != null && _selectedTarget.Equals(info)) _selectedTarget = null;
            // 也清理可能已绑定的条目
            var entry = _entries.FirstOrDefault(e => e.Info == info);
            if (entry != null) RemoveEntry(entry, entry.UIPanel);
            _settings.Targets.Remove(info);
            _settings.Save();
            UpdateUI();
        }

        private void RemoveEntry(TargetEntry entry, Panel pnl)
        {
            _entries.Remove(entry);
            _pnlTargets.Controls.Remove(pnl);
            _selectButtons.Remove(entry.Info);
            if (_selectedTarget != null && _selectedTarget.Equals(entry.Info)) _selectedTarget = null;
            SetTargetAlpha(entry.Tracker.TargetHandle, 255);
            entry.Plate.HidePlate();
            entry.Tracker.Dispose();
            entry.Plate.Dispose();
            _settings.Targets.Remove(entry.Info);
            _settings.Save();
            UpdateUI();
        }

        private void UnbindAll()
        {
            foreach (var e in _entries) { SetTargetAlpha(e.Tracker.TargetHandle, 255); e.Plate.HidePlate(); e.Tracker.Dispose(); e.Plate.Dispose(); }
            _entries.Clear();
            _pnlTargets.Controls.Clear();
            _pendingPanels.Clear();
            _selectButtons.Clear();
        }

        private void Quit()
        {
            _settings.Save();
            _reallyQuit = true;
            _autoBindTimer?.Stop(); _autoBindTimer?.Dispose();
            _onShownTimer?.Stop(); _onShownTimer?.Dispose();
            _saveDebounceTimer?.Stop(); _saveDebounceTimer?.Dispose();
            _tray.Visible = false;  // 先隐藏托盘（内部会访问 Icon.Handle）
            if (_winEventHook != IntPtr.Zero) { Native.UnhookWinEvent(_winEventHook); _winEventHook = IntPtr.Zero; }
            foreach (var e in _entries)
            {
                try { SetTargetAlpha(e.Tracker.TargetHandle, 255); e.Plate.HidePlate(); e.Plate.Dispose(); e.Tracker.Dispose(); }
                catch { /* 单条清理失败不阻塞后续 */ }
            }
            _pendingPanels.Clear();
            _appIcon?.Dispose();  // 最后释放——NotifyIcon 已不再引用它
        }

        // ════════════════════════════════════════════════════════════
        // 入口
        // ════════════════════════════════════════════════════════════

        [STAThread]
        static void Main()
        {
            // PerMonitorV2 必须在 EnableVisualStyles 之前设置，否则静默失效（进程退化为系统 DPI 感知，
            // DeviceDpi 永远返回 96，主界面无法按缩放比缩放）。csproj 的 ApplicationHighDpiMode 也会注入相同声明作为兜底。
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 开机自启（注册表 Run 项带 /startup 参数）时只驻留托盘、不弹主窗口；
            // 手动双击 exe 不带参数，则正常显示主窗口。
            bool startHidden = Environment.GetCommandLineArgs().Skip(1)
                .Any(a => a.Equals("/startup", StringComparison.OrdinalIgnoreCase)
                       || a.Equals("/silent", StringComparison.OrdinalIgnoreCase)
                       || a.Equals("/minimized", StringComparison.OrdinalIgnoreCase)
                       || a.Equals("/background", StringComparison.OrdinalIgnoreCase)
                       || a.Equals("/tray", StringComparison.OrdinalIgnoreCase));
            Application.Run(new MainForm(startHidden));
        }
    }
}
