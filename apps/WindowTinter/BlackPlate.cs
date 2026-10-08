using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace WindowTinter
{
    /// <summary>
    /// 下层纯黑底板：紧贴目标窗口正后方的一张不透明纯黑分层窗口。
    /// 仅在目标被设成半透明（后台透明 / 保持透明度模式）时显示，
    /// 让目标透出来的不是桌面或其它窗口，而是纯黑 —— 即「被压暗」。
    /// 与 MaskOverlay 关键区别：不置顶（无 WS_EX_TOPMOST）、不鼠标穿透（无 WS_EX_TRANSPARENT），
    /// 且窗体类名即 WindowTinter.BlackPlate（不同名），便于单独识别 / 清理。
    /// 
    /// 圆角通过 SetWindowRgn + CreateRoundRectRgn 实现（不依赖逐像素 Alpha），
    /// 避免了 GetHbitmap 丢失 Alpha 通道导致的合成问题。
    /// </summary>
    internal class BlackPlate : Form
    {
        private Bitmap _cachedBmp;
        private int _cachedW, _cachedH, _cachedCornerRadius;
        private IntPtr _hBmp = IntPtr.Zero;   // 由 _cachedBmp 派生的 GDI 位图句柄，跨帧缓存以减少句柄抖动

        // 窗口区域缓存（SetWindowRgn 传入后由系统管理，我们不再持有句柄）
        private int _rgnW, _rgnH, _rgnRadius;

        /// <summary>圆角半径（0=关，矩形；1~10=圆角px）。</summary>
        public int CornerRadius { get; set; } = 0;

        public BlackPlate()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            Text = "WindowTinter.BlackPlate";
            TopMost = false;
            // 本窗口完全由 SetWindowPos / UpdateLayeredWindow 以「物理像素」直接定位与渲染，
            // 不参与 WinForms 的 DPI 自动缩放。若保留默认 AutoScaleMode，在跨不同缩放比的显示器时
            // 会被 WM_DPICHANGED 错误重缩放，导致底板偏移 / 尺寸不对（100% 正常、缩放屏异常的根因之一）。
            AutoScaleMode = AutoScaleMode.None;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                // 仅分层，不置顶、不穿透——它要待在目标正后方，由目标遮挡并接收点击
                cp.ExStyle |= Native.WS_EX_LAYERED;
                return cp;
            }
        }

        /// <summary>
        /// 屏蔽 WinForms 在 DPI 变化时的自动重缩放。
        /// 底板是纯 Win32 分层窗口，坐标始终为物理像素、由 AlignBehind 自行管理；
        /// 若交由 WinForms 处理 WM_DPICHANGED，它会把我们用 SetWindowPos 设定的物理像素坐标
        /// 按新旧 DPI 比例重新缩放，导致缩放屏下底板偏移 / 尺寸错误（即「100% 正常、缩放屏异常」）。
        /// 因此直接吞掉该消息即可——窗口无需任何子控件/字体缩放。
        /// </summary>
        private const int WM_DPICHANGED = 0x02E0;
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_DPICHANGED) return;
            base.WndProc(ref m);
        }

        /// <summary>把底板钉到目标正后方（hWndInsertAfter = 目标句柄），并渲染不透明纯黑。</summary>
        public void AlignBehind(IntPtr targetHandle, Native.RECT r)
        {
            int w = r.Width, h = r.Height;
            if (w <= 0 || h <= 0) { HidePlate(); return; }

            if (!IsHandleCreated) CreateHandle();

            // 关键：用目标句柄作为 hWndInsertAfter，使底板在 Z 序中紧挨目标之下。
            // 这样目标一旦半透明，透出的就是正后方的纯黑，而非桌面 / 其它窗口。
            Native.SetWindowPos(Handle, targetHandle,
                r.Left, r.Top, w, h,
                Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);

            // 设置窗口区域（圆角裁剪），仅在尺寸/半径变化时重建 HRGN
            ApplyWindowRegion(w, h);

            RenderSolidBlack(r.Left, r.Top, w, h);
        }

        /// <summary>隐藏底板（不走 Control.Visible 状态机，避免额外重绘）。</summary>
        public void HidePlate()
        {
            if (IsHandleCreated)
                Native.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0,
                    Native.SWP_HIDEWINDOW | Native.SWP_NOACTIVATE);
        }

        /// <summary>
        /// 通过 SetWindowRgn 裁剪窗口形状。
        /// radius=0 时恢复为全矩形，radius>0 时设为圆角矩形。
        /// SetWindowRgn 调用后区域句柄由系统管理，不缓存也不手动释放。
        /// </summary>
        private void ApplyWindowRegion(int w, int h)
        {
            int r = CornerRadius;
            int clamped = r > 0 ? Math.Min(r, Math.Min(w / 2, h / 2)) : 0;

            // 尺寸/半径未变 → 跳过（避免每帧重建 HRGN）
            if (w == _rgnW && h == _rgnH && clamped == _rgnRadius)
                return;

            IntPtr hrgn = clamped > 0
                ? Native.CreateRoundRectRgn(0, 0, w + 1, h + 1, clamped, clamped)
                : Native.CreateRectRgn(0, 0, w, h);

            if (hrgn != IntPtr.Zero)
            {
                Native.SetWindowRgn(Handle, hrgn, true);
                // SetWindowRgn 接管了区域句柄所有权，不可再 DeleteObject
                (_rgnW, _rgnH, _rgnRadius) = (w, h, clamped);
            }
        }

        private void RenderSolidBlack(int x, int y, int w, int h)
        {
            if (_cachedBmp == null || w != _cachedW || h != _cachedH || CornerRadius != _cachedCornerRadius)
            {
                // 尺寸/圆角变化：旧 GDI 位图随之失效，先释放再重建
                if (_hBmp != IntPtr.Zero) { Native.DeleteObject(_hBmp); _hBmp = IntPtr.Zero; }
                _cachedBmp?.Dispose();
                _cachedBmp = new Bitmap(w, h, PixelFormat.Format32bppRgb);
                using (var g = Graphics.FromImage(_cachedBmp))
                    g.Clear(Color.Black);
                _cachedW = w; _cachedH = h;
                _cachedCornerRadius = CornerRadius;
            }

            IntPtr hdcScreen = Native.GetDC(IntPtr.Zero);
            if (hdcScreen == IntPtr.Zero) return;

            IntPtr hdcMem = Native.CreateCompatibleDC(hdcScreen);
            if (hdcMem == IntPtr.Zero) { Native.ReleaseDC(IntPtr.Zero, hdcScreen); return; }

            // 缓存 GDI 位图：仅在尺寸/圆角变化时重建，避免每帧 GetHbitmap/DeleteObject 抖动
            if (_hBmp == IntPtr.Zero)
                _hBmp = _cachedBmp.GetHbitmap();
            IntPtr hOld = IntPtr.Zero;
            try
            {
                hOld = Native.SelectObject(hdcMem, _hBmp);

                var ptDst = new Point(x, y);
                var ptSrc = new Point(0, 0);
                var sz = new Size(w, h);
                var blend = new Native.BLENDFUNCTION
                {
                    BlendOp = 0, BlendFlags = 0,
                    SourceConstantAlpha = 255, // 不透明纯黑
                    AlphaFormat = 0            // 全局不透明（圆角由 SetWindowRgn 裁剪，不走逐像素 Alpha）
                };

                Native.UpdateLayeredWindow(Handle, hdcScreen,
                    ref ptDst, ref sz, hdcMem, ref ptSrc,
                    0, ref blend, Native.ULW_ALPHA);
            }
            finally
            {
                if (hOld != IntPtr.Zero) Native.SelectObject(hdcMem, hOld);
                Native.DeleteDC(hdcMem);
                Native.ReleaseDC(IntPtr.Zero, hdcScreen);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_hBmp != IntPtr.Zero) { Native.DeleteObject(_hBmp); _hBmp = IntPtr.Zero; }
                _cachedBmp?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
