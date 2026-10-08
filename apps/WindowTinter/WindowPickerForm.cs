using System;
using System.Drawing;
using System.Windows.Forms;

namespace WindowTinter
{
    /// <summary>
    /// 窗口拾取器：全屏透明度0.01捕获层 + 十字光标 + 反转边框高亮。
    /// 左键点击窗口选定，右键取消，Esc取消。
    /// </summary>
    internal class WindowPickerForm : Form
    {
        public IntPtr SelectedHandle { get; private set; } = IntPtr.Zero;
        private Native.RECT _highlight;
        private IntPtr _lastHwnd = IntPtr.Zero;
        private DateTime _lastMoveCheck = DateTime.MinValue;

        public WindowPickerForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Bounds = SystemInformation.VirtualScreen;
            TopMost = true;
            Opacity = 0.01;
            Cursor = Cursors.Cross;
            KeyPreview = true;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            TopMost = true;
            BringToFront();
            Activate();
            Cursor.Current = Cursors.Cross;
        }

        /// <summary>
        /// EnumWindows 按 Z 序从顶到底枚举所有顶层窗口。
        /// 无需 show/hide 拾取窗 —— 直接跳过自己，找第一个包含光标的窗口。
        /// </summary>
        private IntPtr GetWindowAtCursor()
        {
            Point pt = Control.MousePosition;
            IntPtr found = IntPtr.Zero;

            Native.EnumWindows((hwnd, _) =>
            {
                if (hwnd == Handle) return true;                   // 跳过拾取窗自己
                if (!Native.IsWindowVisible(hwnd)) return true;     // 跳过不可见
                if (Native.IsIconic(hwnd)) return true;             // 跳过最小化

                Native.GetWindowRect(hwnd, out Native.RECT r);
                if (pt.X >= r.Left && pt.X < r.Right && pt.Y >= r.Top && pt.Y < r.Bottom)
                {
                    found = hwnd;
                    return false; // 找到顶层窗口，停止枚举
                }
                return true;
            }, IntPtr.Zero);

            if (found != IntPtr.Zero)
                found = Native.GetAncestor(found, Native.GA_ROOT);

            return found;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            // 30ms 节流——避免每像素移动都 EnumWindows
            var now = DateTime.UtcNow;
            if ((now - _lastMoveCheck).TotalMilliseconds < 30) return;
            _lastMoveCheck = now;

            IntPtr h = GetWindowAtCursor();
            if (h == _lastHwnd) return;

            ClearHighlight();
            _lastHwnd = h;

            if (h != IntPtr.Zero && Native.GetWindowRect(h, out _highlight))
                DrawHighlight(h, _highlight);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                CancelPicker();
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            ClearHighlight();
            IntPtr h = GetWindowAtCursor();
            if (h != IntPtr.Zero)
            {
                SelectedHandle = h;
                DialogResult = DialogResult.OK;
            }
            else
                DialogResult = DialogResult.Cancel;
            Close();
        }

        // ProcessCmdKey 在 ProcessDialogKey 之前——后者吞掉 Esc 等导航键
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                CancelPicker();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void CancelPicker()
        {
            ClearHighlight();
            DialogResult = DialogResult.Cancel;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            ClearHighlight();
            base.OnFormClosing(e);
        }

        // ── 反转边框绘制 ──

        private void DrawHighlight(IntPtr hwnd, Native.RECT r)
        {
            IntPtr hdc = Native.GetWindowDC(hwnd);
            if (hdc == IntPtr.Zero) return;
            try
            {
                int bw = 3;
                Native.PatBlt(hdc, 0, 0, r.Width, bw, Native.DSTINVERT);
                Native.PatBlt(hdc, 0, r.Height - bw, r.Width, bw, Native.DSTINVERT);
                Native.PatBlt(hdc, 0, 0, bw, r.Height, Native.DSTINVERT);
                Native.PatBlt(hdc, r.Width - bw, 0, bw, r.Height, Native.DSTINVERT);
            }
            finally { Native.ReleaseDC(hwnd, hdc); }
        }

        private void ClearHighlight()
        {
            if (_lastHwnd != IntPtr.Zero && Native.IsWindow(_lastHwnd)
                && Native.GetWindowRect(_lastHwnd, out Native.RECT current))
                DrawHighlight(_lastHwnd, current);
            _lastHwnd = IntPtr.Zero;
        }
    }
}
