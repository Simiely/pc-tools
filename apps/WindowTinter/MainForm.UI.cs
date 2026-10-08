using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace WindowTinter
{
    internal partial class MainForm : Form
    {
        // ════════════════════════════════════════════════════════════
        // 设置界面构建
        // ════════════════════════════════════════════════════════════

        private void BuildUI()
        {
            int y = 10, pad = 10;
            const int GW = 434; // group box width

            AddGroup("状态", pad, ref y, 50, GW, gb =>
            {
                _lblStatus = AddLabel(gb, pad, 20, FontStyle.Bold, 10f);
            });

            _chkEnabled = AddCheck(this, "启用覆盖", pad + 4, y + 2, FontStyle.Bold, _settings.Enabled, ToggleEnabled);
            _chkBackdropPlate = AddCheck(this, "下方垫黑色（下层遮罩）", 140, y + 2, FontStyle.Regular,
                _settings.BackdropBlackPlate, () => { _settings.BackdropBlackPlate = _chkBackdropPlate.Checked; _settings.Save(); foreach (var e in _entries) e.Tracker.RefreshForeground(); });
            y += 28;

            AddGroup("目标窗口", pad, ref y, 260, GW, gb =>
            {
                _pnlTargets = new FlowLayoutPanel
                {
                    Location = new Point(pad, 18), Size = new Size(416, 210),
                    AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false,
                    BackColor = Color.FromArgb(32, 32, 32), ForeColor = Color.White
                };
                _pnlTargets.HandleCreated += (_, _) =>
                    Native.SetWindowTheme(_pnlTargets.Handle, "DarkMode_Explorer", null);
                gb.Controls.Add(_pnlTargets);
            });

            AddButton(this, "+ 添加窗口", pad, y + 2, 95, PickWindow);
            _btnRefind = AddButton(this, "🔄 重新查找", 110, y + 2, 95, RefindAllWindows);
            y += 38;

            AddGroup("窗口控制", pad, ref y, 176, GW, gb =>
            {
                _chkGlobalTransparency = AddCheck(gb, "全局统一透明度（关闭后每个窗口单独配置）", pad, 22, FontStyle.Bold,
                    _settings.GlobalTransparency, ToggleGlobalTransparency);

                gb.Controls.Add(new Label { Text = "窗口透明度:", Location = new Point(pad, 54), AutoSize = true });
                _tbBgAlpha = new JumpTrackBar
                {
                    Location = new Point(90, 52), Size = new Size(260, 24),
                    Minimum = 0, Maximum = 100, TickFrequency = 10,
                    SmallChange = 5, LargeChange = 20,
                    Value = _settings.BackgroundAlpha
                };
                _tbBgAlpha.ValueChanged += (_, _) => SetBgAlpha(_tbBgAlpha.Value);
                gb.Controls.Add(_tbBgAlpha);
                _lblBgAlpha = new Label { Location = new Point(356, 54), AutoSize = true };
                gb.Controls.Add(_lblBgAlpha);

                _chkGlobalCornerRadius = AddCheck(gb, "全局统一圆角（关闭后每个窗口单独配置）", pad, 86, FontStyle.Bold,
                    _settings.GlobalCornerRadius, ToggleGlobalCornerRadius);

                gb.Controls.Add(new Label { Text = "圆角半径:", Location = new Point(pad, 118), AutoSize = true });
                _tbCornerRadius = new JumpTrackBar
                {
                    Location = new Point(90, 116), Size = new Size(260, 24),
                    Minimum = 0, Maximum = 20, TickFrequency = 5,
                    SmallChange = 1, LargeChange = 5,
                    Value = _settings.CornerRadius
                };
                _tbCornerRadius.ValueChanged += (_, _) => SetCornerRadius(_tbCornerRadius.Value);
                gb.Controls.Add(_tbCornerRadius);
                _lblCornerRadius = new Label { Location = new Point(356, 118), AutoSize = true,
                    Text = _settings.CornerRadius == 0 ? "关" : $"{_settings.CornerRadius}px" };
                gb.Controls.Add(_lblCornerRadius);

                gb.Controls.Add(new Label
                {
                    Text = "关闭任一全局开关后，在列表中点击 ○ 选中窗口单独配置",
                    Location = new Point(pad, 152), AutoSize = true,
                    ForeColor = Color.FromArgb(150, 150, 160),
                    Font = new Font("Microsoft YaHei UI", 8.5f)
                });
            });

            // 系统选项
            int rowY = y + 2;
            _chkStartup = AddCheck(this, "开机自启", pad + 4, rowY, FontStyle.Regular, _settings.StartWithWindows,
                () => { _settings.StartWithWindows = _chkStartup.Checked; _settings.ApplyStartWithWindows(); _settings.Save(); });
            _chkMinimizeTray = AddCheck(this, "关闭窗口时最小化到托盘（不勾选则直接退出）", pad + 4, rowY + 24, FontStyle.Regular,
                _settings.MinimizeToTray, () => { _settings.MinimizeToTray = _chkMinimizeTray.Checked; _settings.Save(); });
            rowY += 52;

            AddButton(this, "📂 配置文件夹", pad, rowY, 120, OpenConfigFolder);
            AddButton(this, "ℹ 关于", 134, rowY, 70, ShowAbout);
            AddButton(this, "💾 保存配置", 210, rowY, 110, SaveSettings);
            AddButton(this, "🚪 退出", 326, rowY, 70, () => { _reallyQuit = true; Close(); });

            var link = new LinkLabel
            {
                Text = "20260720 / 世界的风吹向你 / 开源软件",
                Location = new Point(pad, rowY + 38),
                AutoSize = true,
                LinkColor = Color.FromArgb(160, 160, 170),
                ActiveLinkColor = Color.FromArgb(200, 200, 220)
            };
            link.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo("https://github.com/Simiely/WindowTinter") { UseShellExecute = true });
            Controls.Add(link);

            // 恢复已有条目（活跃 + 待激活）
            for (int i = 0; i < _entries.Count; i++)
                AddTargetUI(_entries[i]);
            foreach (var t in _settings.Targets)
                if (!_entries.Any(e => e.Info == t))
                    AddPendingUI(t);

            ApplyDarkTheme();
        }

        // ── 控件辅助方法 ──────────────────────────────────────────

        private void AddGroup(string text, int x, ref int y, int h, int w, Action<GroupBox> build)
        {
            var gb = new GroupBox { Text = text, Location = new Point(x, y), Size = new Size(w, h) };
            // WinForms GroupBox 内部区域由系统主题绘制，BackColor 无效，需自绘填充
            var groupBg = Color.FromArgb(40, 40, 40);
            gb.Paint += (_, e) =>
            {
                using var brush = new SolidBrush(groupBg);
                // 用 Font.Height 动态计算标题高度，避免高 DPI 下填充错位
                int headerH = gb.Font.Height + 3;
                e.Graphics.FillRectangle(brush, 3, headerH, w - 6, h - headerH - 3);
            };
            build(gb);
            Controls.Add(gb);
            y += h + 6;
        }

        private static Label AddLabel(Control parent, int x, int y, FontStyle style, float size = 9f)
        {
            var lbl = new Label { Location = new Point(x, y), AutoSize = true, Font = new Font("Microsoft YaHei UI", size, style) };
            parent.Controls.Add(lbl);
            return lbl;
        }

        private static Button AddButton(Control parent, string text, int x, int y, int w, Action onClick)
        {
            var btn = new Button { Text = text, Location = new Point(x, y), Size = new Size(w, 30) };
            btn.Click += (_, _) => onClick();
            parent.Controls.Add(btn);
            return btn;
        }

        private static CheckBox AddCheck(Control parent, string text, int x, int y, FontStyle style, bool initial, Action onChange)
        {
            var chk = new CheckBox
            {
                Text = text, Location = new Point(x, y), AutoSize = true, Checked = initial
            };
            if (style != FontStyle.Regular)
                chk.Font = new Font("Microsoft YaHei UI", 9.5f, style);
            chk.CheckedChanged += (_, _) => onChange();
            parent.Controls.Add(chk);
            return chk;
        }

        /// <summary>创建目标面板中的 ○/● 选中按钮。尺寸/位置按 _dpiScale 换算（面板为 AutoScaleMode.None，避免重复缩放）。</summary>
        private Button CreateSelectButton(int w, bool selected, bool enabled, EventHandler onClick)
        {
            float s = _dpiScale;
            var btn = new Button
            {
                Text = selected ? "●" : "○",
                Size = new Size((int)(30 * s), (int)(24 * s)),
                Location = new Point(w - (int)(68 * s), (int)(4 * s)),
                FlatStyle = FlatStyle.Flat,
                BackColor = selected ? Color.FromArgb(90, 120, 160) : Color.FromArgb(60, 60, 60),
                ForeColor = Color.FromArgb(224, 224, 224),
                Enabled = enabled
            };
            btn.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);
            btn.Click += onClick;
            return btn;
        }

        /// <summary>创建目标面板中的 × 删除按钮。尺寸/位置按 _dpiScale 换算。</summary>
        private Button CreateRemoveButton(int w, EventHandler onClick)
        {
            float s = _dpiScale;
            var btn = new Button
            {
                Text = "×",
                Size = new Size((int)(28 * s), (int)(24 * s)),
                Location = new Point(w - (int)(34 * s), (int)(4 * s)),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(60, 60, 60),                 ForeColor = Color.FromArgb(224, 224, 224)
            };
            btn.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);
            btn.Click += onClick;
            return btn;
        }

        // ════════════════════════════════════════════════════════════
        // 深色主题
        // ════════════════════════════════════════════════════════════

        private void ApplyDarkTheme()
        {
            var bg = Color.FromArgb(30, 30, 30);
            var fg = Color.FromArgb(224, 224, 224);
            var panelBg = Color.FromArgb(40, 40, 40);

            BackColor = bg;
            ForeColor = fg;
            if (IsHandleCreated)
            {
                int dark = 1;
                Native.DwmSetWindowAttribute(Handle, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, 4);
            }
            ThemeAll(this, bg, fg, panelBg);
        }

        private static void ThemeAll(Control parent, Color bg, Color fg, Color panelBg)
        {
            foreach (Control c in parent.Controls)
            {
                if (c is GroupBox or Panel or FlowLayoutPanel) { c.BackColor = panelBg; c.ForeColor = fg; }
                else if (c is Button btn) { btn.BackColor = Color.FromArgb(60, 60, 60); btn.ForeColor = fg; btn.FlatStyle = FlatStyle.Flat; btn.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80); }
                else if (c is CheckBox or RadioButton) { c.BackColor = bg; c.ForeColor = fg; }
                else if (c is Label lbl) { lbl.BackColor = lbl.Parent is Panel or FlowLayoutPanel ? panelBg : bg; if (lbl.ForeColor == Color.Gray) lbl.ForeColor = Color.FromArgb(140, 140, 140); else lbl.ForeColor = fg; }
                else if (c is TrackBar) { c.BackColor = panelBg; }
                else { c.BackColor = bg; c.ForeColor = fg; }
                if (c.Controls.Count > 0) ThemeAll(c, bg, fg, panelBg);
            }
        }

        // ════════════════════════════════════════════════════════════
        // UI 同步
        // ════════════════════════════════════════════════════════════

        private void UpdateUI()
        {
            int total = _settings.Targets.Count;
            int active = _entries.Count;
            int pending = total - active;
            _lblStatus.Text = !_settings.Enabled ? "⏸ 已暂停"
                : total == 0 ? "○ 等待选择窗口…"
                : pending > 0 ? $"● {active} 个监控中, {pending} 个待激活"
                : $"● 监控中 — {active} 个窗口";

            _chkEnabled.Checked = _settings.Enabled;
            _chkGlobalTransparency.Checked = _settings.GlobalTransparency;
            _chkGlobalCornerRadius.Checked = _settings.GlobalCornerRadius;

            // 非全局模式下，若选中目标已不在列表中则清空
            if (!_settings.GlobalTransparency && _selectedTarget != null && !_settings.Targets.Any(t => t.Equals(_selectedTarget)))
                _selectedTarget = null;

            int b = _settings.GlobalTransparency ? _settings.BackgroundAlpha : (_selectedTarget?.BackgroundAlpha ?? 0);
            if (_tbBgAlpha.Value != b) _tbBgAlpha.Value = b;
            UpdateSliderEnabled();
            _lblBgAlpha.Text = $"{_tbBgAlpha.Value}%";

            int cr = _settings.GlobalCornerRadius ? _settings.CornerRadius : (_selectedTarget?.CornerRadius ?? 0);
            if (_tbCornerRadius.Value != cr) _tbCornerRadius.Value = cr;
            UpdateCornerSliderEnabled();
            _lblCornerRadius.Text = cr == 0 ? "关" : $"{cr}px";
            UpdateSelectButtons();
            RefreshTrayMenu();
        }

        // ════════════════════════════════════════════════════════════
        // 选中目标（非全局模式）
        // ════════════════════════════════════════════════════════════

        private void SelectTarget(TargetInfo info)
        {
            if (_settings.GlobalTransparency && _settings.GlobalCornerRadius) return;
            _selectedTarget = info;
            UpdateSelectButtons();
            if (!_settings.GlobalTransparency)
            {
                int b = info.BackgroundAlpha;
                if (_tbBgAlpha.Value != b) _tbBgAlpha.Value = b;
                _lblBgAlpha.Text = $"{b}%";
                UpdateSliderEnabled();
            }
            if (!_settings.GlobalCornerRadius)
            {
                int cr = info.CornerRadius;
                if (_tbCornerRadius.Value != cr) _tbCornerRadius.Value = cr;
                _lblCornerRadius.Text = cr == 0 ? "关" : $"{cr}px";
                UpdateCornerSliderEnabled();
            }
        }

        private void UpdateSelectButtons()
        {
            bool global = _settings.GlobalTransparency;
            foreach (var kv in _selectButtons)
            {
                bool sel = !global && _selectedTarget != null && _selectedTarget.Equals(kv.Key);
                kv.Value.Text = sel ? "●" : "○";
                kv.Value.Enabled = !global;
                kv.Value.BackColor = sel ? Color.FromArgb(90, 120, 160) : Color.FromArgb(60, 60, 60);
            }
            // 高亮选中面板
            foreach (var e in _entries)
                if (e.UIPanel != null)
                    e.UIPanel.BackColor = (!global && _selectedTarget != null && _selectedTarget.Equals(e.Info))
                        ? Color.FromArgb(50, 70, 95) : Color.FromArgb(40, 40, 40);
            foreach (var kv in _pendingPanels)
                kv.Value.BackColor = (!global && _selectedTarget != null && _selectedTarget.Equals(kv.Key))
                    ? Color.FromArgb(50, 70, 95) : Color.FromArgb(40, 40, 40);
        }

        /// <summary>非全局模式且未选中任何目标时，禁用手动滑块避免"空转"（此时调节无效果）。</summary>
        private void UpdateSliderEnabled()
        {
            bool enabled = _settings.GlobalTransparency || _selectedTarget != null;
            _tbBgAlpha.Enabled = enabled;
        }

        private void UpdateCornerSliderEnabled()
        {
            bool enabled = _settings.GlobalCornerRadius || _selectedTarget != null;
            _tbCornerRadius.Enabled = enabled;
        }

        // ════════════════════════════════════════════════════════════
        // 托盘
        // ════════════════════════════════════════════════════════════

        private void BuildTray()
        {
            _menu = new ContextMenuStrip();
            _tray = new NotifyIcon { Icon = _appIcon, Text = $"暗幕 v{AppVersion}", ContextMenuStrip = _menu, Visible = true };
            _tray.DoubleClick += (_, _) => ToggleWindow();
            RefreshTrayMenu();
        }

        private void RefreshTrayMenu()
        {
            _menu.Items.Clear();
            int total = _settings.Targets.Count;
            int active = _entries.Count;
            int pending = total - active;
            string s = !_settings.Enabled ? "⏸ 已暂停"
                : total == 0 ? "○ 等待选择窗口…"
                : pending > 0 ? $"● {active} 监控中, {pending} 待激活"
                : $"● 监控中 — {active} 窗口";
            _menu.Items.Add(s).Enabled = false;
            _menu.Items.Add("-");
            _menu.Items.Add(IsWindowOpen() ? "最小化到托盘" : "打开设置窗口", null, (_, _) => ToggleWindow());
            _menu.Items.Add(_settings.Enabled ? "⏸ 停用" : "▶ 启用", null, (_, _) => _chkEnabled.Checked = !_chkEnabled.Checked);
            _menu.Items.Add("-");
            _menu.Items.Add("退出", null, (_, _) => { _reallyQuit = true; Close(); });
        }

        /// <summary>窗口是否真正"打开"（可见且非最小化）。用于托盘菜单文案与开合判断，
        /// 这样开机自启以最小化态驻留时，双击托盘会正确地"打开"而非"再最小化"。</summary>
        private bool IsWindowOpen() => Visible && WindowState != FormWindowState.Minimized;

        private void ToggleWindow()
        {
            if (IsWindowOpen())
            {
                Hide();
                ShowInTaskbar = false;
            }
            else
            {
                ShowInTaskbar = true;
                Show();
                WindowState = FormWindowState.Normal;
                BringToFront();
                Activate();
            }
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!_reallyQuit && _settings.MinimizeToTray && e.CloseReason == CloseReason.UserClosing)
            { _settings.Save(); e.Cancel = true; Hide(); ShowInTaskbar = false; }
        }

        // ════════════════════════════════════════════════════════════
        // 核心操作（UI 按钮回调）
        // ════════════════════════════════════════════════════════════

        private void ToggleEnabled()
        {
            // 直接读 checkbox 状态，避免 _settings.Enabled 翻转与 UI 不一致
            bool enable = _chkEnabled.Checked;
            _settings.Enabled = enable;
            _settings.Save();
            if (enable)
            {
                foreach (var t in _settings.Targets)
                {
                    if (_entries.Any(e => e.Info == t)) continue;
                    TryBindTarget(t);
                    if (_entries.Any(e => e.Info == t)) RemovePendingUI(t);
                    else if (!_pendingPanels.ContainsKey(t)) AddPendingUI(t);
                }
                foreach (var e in _entries) ApplyMaskNow(e);
            }
            else
            {
                foreach (var e in _entries) { SetTargetAlpha(e.Tracker.TargetHandle, 255); e.Plate.HidePlate(); }
            }
            UpdateUI();
        }

        private void ToggleGlobalTransparency()
        {
            bool g = _chkGlobalTransparency.Checked;
            _settings.GlobalTransparency = g;
            if (!g)
            {
                // 关闭全局：把当前全局值写入每个目标作为各自起点
                foreach (var t in _settings.Targets)
                {
                    t.BackgroundAlpha = _settings.BackgroundAlpha;
                }
                _selectedTarget = _settings.Targets.FirstOrDefault();
            }
            _settings.Save();
            UpdateSelectButtons();
            UpdateUI();
            foreach (var e in _entries) ApplyMaskNow(e);
        }

        private void ToggleGlobalCornerRadius()
        {
            bool g = _chkGlobalCornerRadius.Checked;
            _settings.GlobalCornerRadius = g;
            if (!g)
            {
                // 关闭全局：把当前全局圆角值写入每个目标作为各自起点
                foreach (var t in _settings.Targets)
                {
                    t.CornerRadius = _settings.CornerRadius;
                }
                _selectedTarget = _settings.Targets.FirstOrDefault();
            }
            _settings.Save();
            UpdateSelectButtons();
            UpdateUI();
            // 刷新所有底板的圆角
            foreach (var e in _entries)
            {
                bool useGlobal = _settings.GlobalCornerRadius;
                e.Plate.CornerRadius = useGlobal ? _settings.CornerRadius : e.Info.CornerRadius;
                e.Tracker.RefreshForeground();
            }
        }

        private void SetBgAlpha(int value)
        {
            value = Math.Clamp(value, 0, 100);
            if (_settings.GlobalTransparency)
                _settings.BackgroundAlpha = value;
            else
            {
                if (_selectedTarget == null) return;
                _selectedTarget.BackgroundAlpha = value;
            }
            foreach (var e in _entries) ApplyMaskNow(e);
            _lblBgAlpha.Text = $"{value}%";
            // 200ms 防抖：停止拖动后再写盘
            _saveDebounceTimer.Stop();
            _saveDebounceTimer.Start();
        }

        private void SetCornerRadius(int value)
        {
            value = Math.Clamp(value, 0, 20);
            if (_settings.GlobalCornerRadius)
            {
                _settings.CornerRadius = value;
                foreach (var e in _entries)
                    SetPlateCornerRadius(e, value);
            }
            else
            {
                if (_selectedTarget == null) return;
                _selectedTarget.CornerRadius = value;
                foreach (var e in _entries)
                {
                    if (e.Info == _selectedTarget)
                    {
                        SetPlateCornerRadius(e, value);
                        break;
                    }
                }
            }
            _lblCornerRadius.Text = value == 0 ? "关" : $"{value}px";
            _saveDebounceTimer.Stop();
            _saveDebounceTimer.Start();
        }

        private static void SetPlateCornerRadius(TargetEntry e, int value)
        {
            e.Plate.CornerRadius = value;
            e.Tracker.RefreshForeground();
        }

        private void PickWindow()
        {
            bool wasVisible = Visible; if (wasVisible) Hide();
            using var picker = new WindowPickerForm();
            var result = picker.ShowDialog();
            if (wasVisible) { Show(); BringToFront(); Activate(); }

            if (result != DialogResult.OK || picker.SelectedHandle == IntPtr.Zero) return;

            Native.GetWindowThreadProcessId(picker.SelectedHandle, out uint pid);
            string procName = "";
            try { procName = Process.GetProcessById((int)pid).ProcessName ?? ""; } catch { }

            string title = "";
            int len = Native.GetWindowTextLength(picker.SelectedHandle);
            if (len > 0)
            {
                var sb = new System.Text.StringBuilder(len + 1);
                Native.GetWindowText(picker.SelectedHandle, sb, len + 1);
                title = sb.ToString();
            }
            var info = new TargetInfo { ProcessName = procName, WindowTitle = title };

            // 以当前全局值作为该窗口独立配置的起点
            info.BackgroundAlpha = _settings.BackgroundAlpha;
            info.CornerRadius = _settings.CornerRadius;

            if (_settings.Targets.Contains(info))
            {
                MessageBox.Show("此窗口已添加。", "WindowTinter", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _settings.Targets.Add(info);

            if (_settings.Enabled)
            {
                TryBindTarget(info);
                var entry = _entries.FirstOrDefault(e => e.Info == info);
                if (entry != null) ApplyMaskNow(entry);
                else AddPendingUI(info);
            }
            else AddPendingUI(info);
            // 非全局模式下，自动选中刚添加的窗口
            if (!_settings.GlobalTransparency || !_settings.GlobalCornerRadius) _selectedTarget = info;
            UpdateUI();
        }

        private void RefindAllWindows()
        {
            UnbindAll();
            foreach (var t in _settings.Targets)
            {
                TryBindTarget(t);
                if (!_entries.Any(e => e.Info == t)) AddPendingUI(t);
            }
            UpdateUI();
        }

        private void OpenConfigFolder()
        {
            var dir = Path.GetDirectoryName(Environment.ProcessPath) ?? ".";
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            Process.Start("explorer.exe", dir);
        }

        private void ShowAbout()
        {
            var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            string v = ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "5.5.2";
            MessageBox.Show(
                $"暗幕 v{v}\n\n" +
                "给任意窗口设置透明度、并在其正下方垫纯黑的常驻小工具。\n" +
                "支持多窗口同时控制。\n\n" +
                "• 配置: 与 exe 同目录 WindowTinter.settings.json\n" +
                "• 图标: app.ico 与 exe 同目录\n\n" +
                "https://github.com/Simiely/WindowTinter",
                "关于");
        }

        private void SaveSettings()
        {
            _settings.Save();
        }

        // ════════════════════════════════════════════════════════════
        // WinEvent
        // ════════════════════════════════════════════════════════════

        private void InstallWinEventHook()
        {
            _winEventProc = WinEventProcCallback;
            _winEventHook = Native.SetWinEventHook(
                Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_OBJECT_LOCATIONCHANGE,
                IntPtr.Zero, _winEventProc, 0, 0,
                Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);
        }

        private void WinEventProcCallback(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (idObject != 0 || idChild != 0) return;

            if (eventType == Native.EVENT_SYSTEM_FOREGROUND)
            {
                try { BeginInvoke(new Action(() => { foreach (var e in _entries) e.Tracker.RefreshForeground(); })); }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
                return;
            }

            // 目标特定事件：在 BeginInvoke 内读取 _entries，避免跨线程访问非安全集合
            if (eventType is Native.EVENT_OBJECT_LOCATIONCHANGE or Native.EVENT_OBJECT_HIDE
                               or Native.EVENT_OBJECT_SHOW or Native.EVENT_OBJECT_DESTROY)
            {
                var targetHwnd = hwnd;
                try { BeginInvoke(new Action(() =>
                {
                    var match = _entries.FirstOrDefault(e => e.Tracker.TargetHandle == targetHwnd);
                    if (match != null) match.Tracker.RefreshNow();
                })); }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
            }
        }
    }
}
