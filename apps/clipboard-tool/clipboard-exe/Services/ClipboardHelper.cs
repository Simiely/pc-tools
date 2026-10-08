// Services/ClipboardHelper.cs - 健壮剪贴板写入
// 真实根因（已用 --selftest 探针复现 + 上游权威确认 dotnet/wpf #9901）：
//   System.Windows.Clipboard.SetText / SetDataObject(copy:true) 内部 OleSetClipboard→OleFlushClipboard(Flush)，
//   Flush 在剪贴板被外部进程（TeraCopy / Skype / PowerToys / TextInputHost 触摸键盘 / Win+V 历史 / RDP 剪贴板桥）
//   占用时抛 COMException 0x800401D0 (CLIPBRD_E_CANT_OPEN_CLIPBOARD)。WPF 自带重试是同线程 Thread.Sleep，
//   救不了「外部常驻占用」——故上一版「泵 dispatcher + 重试」仍然失败。
// 权威修复（dotnet/wpf #9901 + StackOverflow 多位 maintainer 验证）：
//   改用 System.Windows.Forms.Clipboard.SetDataObject(data, copy, retryTimes, retryDelay) ——
//   它自带重试循环(10×100ms) 且不受 WPF 的 Flush 路径影响，是报告中最稳定可用的写法；copy:true 保留数据直到进程退出
//   （剪贴板工具需要：复制后即使本程序关闭也应可粘贴）。兜底仍尝试 WPF 路径（极少数 WinForms.Clipboard 不可用环境）。
// 注：浏览器端无此问题（navigator.clipboard 走系统服务），桌面端必须处理。
using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SWF = System.Windows.Forms;

namespace ClipboardExe.Services;

public static class ClipboardHelper
{
    private const int RetryTimes = 10;
    private const int RetryDelayMs = 100;

    // ---- 诊断：定位剪贴板占用方（外部进程常驻占用即 0x800401D0 根因） ----
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetOpenClipboardWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowTextLength(IntPtr hWnd);
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    /// <summary>返回当前占用剪贴板的窗口/进程信息；无人占用返回提示。用于复制失败时定位根因。</summary>
    private static string GetClipboardOwnerInfo()
    {
        try
        {
            var hwnd = GetOpenClipboardWindow();
            if (hwnd == IntPtr.Zero) return "（无进程占用剪贴板）";
            var len = GetWindowTextLength(hwnd);
            var sb = new System.Text.StringBuilder(len + 1);
            GetWindowText(hwnd, sb, sb.Capacity);
            GetWindowThreadProcessId(hwnd, out var pid);
            var procName = "";
            try { procName = System.Diagnostics.Process.GetProcessById((int)pid)?.ProcessName ?? ""; }
            catch { /* 进程已退出等 */ }
            return $"占用方 hwnd=0x{hwnd:X} 窗口='{sb}' 进程={procName}(pid={pid})";
        }
        catch (Exception ex) { return "（诊断失败：" + ex.Message + "）"; }
    }

    /// <summary>复制纯文本（对齐 Web navigator.clipboard.writeText；同时写 UnicodeText+Text 保证中文/emoji 无损）。</summary>
    public static void SetText(string text)
    {
        var d = new SWF.DataObject();
        d.SetData(SWF.DataFormats.UnicodeText, text ?? "");
        d.SetData(SWF.DataFormats.Text, text ?? "");
        SetViaWinForms(d, copy: true);
    }

    /// <summary>复制图片（对齐 Web copyImageToClipboard）。BitmapSource → System.Drawing.Bitmap 后走 WinForms 路径（绕开 WPF Flush）。</summary>
    public static void SetImage(BitmapSource image)
    {
        if (image == null) throw new ArgumentNullException(nameof(image));
        using var bmp = BitmapSourceToBitmap(image);
        SetViaWinForms(new SWF.DataObject(SWF.DataFormats.Bitmap, bmp), copy: true);
    }

    /// <summary>复制任意 DataObject（富文本 CopyRich 传入的 HTML+Text 包）。</summary>
    public static void SetDataObject(DataObject data)
        => SetViaWinForms(ToWinForms(data), copy: true);

    // ---- 读取：纯图片剪贴板识别（截图 / 复制图片：剪贴板含图片格式但无文本） ----
    // 对齐 Web paste ②「图片/文件优先」；但必须排除「富文本复制」——Word/网页带格式复制时剪贴板
    // 常同时携带 CF_BITMAP/CF_DIB 位图预览（渲染选区用），若见位图就当图片，会把文本误存成一张图。
    // 故判定 = 有可解码图片格式 && 无文本格式。Win+Shift+S 截图、右键复制图片（Chrome PNG/DIB）、
    // 微信/QQ 复制图片均满足；纯文本 / 富文本复制不满足。
    private static readonly string[] ImageFormats = { DataFormats.Bitmap, DataFormats.Dib, "PNG", "DeviceIndependentBitmap", "image/png" };

    private static bool HasTextFormat(IDataObject d)
    {
        foreach (var f in new[] { DataFormats.UnicodeText, DataFormats.Text, DataFormats.OemText })
        {
            try { if (d.GetDataPresent(f, false)) return true; }
            catch { /* 某些格式 GetDataPresent 抛异常则跳过 */ }
        }
        return false;
    }

    private static bool HasImageFormat(IDataObject d)
    {
        foreach (var f in ImageFormats)
        {
            try { if (d.GetDataPresent(f, false)) return true; }
            catch { /* 同上 */ }
        }
        try { if (Clipboard.ContainsImage()) return true; }
        catch { /* 剪贴板被占用则按无图处理 */ }
        return false;
    }

    /// <summary>判定剪贴板内容是否为纯图片（有图片格式且无文本）。任意 IDataObject 均可（弹窗 Pasting 事件传入）。
    /// 失败/被占用一律 false（宁可漏弹也不误劫持富文本）。</summary>
    public static bool IsImageOnly(IDataObject? d)
    {
        if (d == null) return false;
        try { return !HasTextFormat(d) && HasImageFormat(d); }
        catch { return false; }
    }

    /// <summary>系统剪贴板当前是否为纯图片。</summary>
    public static bool IsImageOnlyClipboard()
    {
        try { return IsImageOnly(Clipboard.GetDataObject()); }
        catch { return false; }
    }

    /// <summary>纯图片剪贴板 → PNG 字节（对齐 Web blobToPng canvas.toBlob('image/png')）。非纯图片返回 null。
    /// 供 watcher 弹窗自动收图 / 弹窗内 Ctrl+V 拦截共用。可传 Pasting 事件的 DataObject（默认读系统剪贴板）。</summary>
    public static byte[]? ReadImageOnlyAsPng(IDataObject? d = null)
    {
        try
        {
            d ??= Clipboard.GetDataObject();
            if (!IsImageOnly(d)) return null;
            var bmp = GetBitmapFrom(d);
            return bmp == null ? null : EncodePng(bmp);
        }
        catch { return null; }
    }

    /// <summary>读取系统剪贴板的文件拖放列表（资源管理器「复制」的文件）；无文件/剪贴板不可用 → null。
    ///
    /// 存在意义（与 ReadImageOnlyAsPng 的 Ctrl+V 兜底同源）：剪贴板只有 FileDrop 而无文本时，
    /// TextBox 的粘贴命令 CanExecute=false → **DataObject.Pasting 事件根本不触发** →
    /// 挂在输入框上的 OnPasting 收不到文件，Ctrl+V 表现为「毫无反应」。
    /// 必须在 PreviewKeyDown 层直接读剪贴板兜底（v0.7.6-P1 修复：此前只给图片做了兜底，漏了文件）。</summary>
    public static string[]? GetFileDropList()
    {
        try
        {
            if (!Clipboard.ContainsFileDropList()) return null;
            var c = Clipboard.GetFileDropList();
            if (c == null || c.Count == 0) return null;
            var a = new string[c.Count];
            c.CopyTo(a, 0);
            return a;
        }
        catch { return null; } // 剪贴板被占用/无权限：静默（Ctrl+V 兜底路径，不该弹错打断输入）
    }

    /// <summary>从 IDataObject 取 BitmapSource（Bitmap/Dib/PNG 均可）。</summary>
    private static BitmapSource? GetBitmapFrom(IDataObject d)
    {
        try
        {
            foreach (var f in ImageFormats)
            {
                try
                {
                    if (d.GetDataPresent(f, true) && d.GetData(f, true) is BitmapSource bmp) return bmp;
                }
                catch { /* 该格式无法转换则试下一个 */ }
            }
        }
        catch { }
        try { return Clipboard.GetImage(); } // 兜底：WPF 自动转换 Dib→Bitmap
        catch { return null; }
    }

    /// <summary>BitmapSource → PNG 字节（先走 BitmapImage 再编码，保证跨 DPI/格式一致）。</summary>
    public static byte[]? EncodePng(BitmapSource? bmp)
    {
        if (bmp == null) return null;
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }
        catch { return null; }
    }

    // ---- 核心：WinForms.Clipboard（自带重试，绕开 WPF Flush） ----
    private static void SetViaWinForms(SWF.DataObject data, bool copy)
    {
        try
        {
            SWF.Clipboard.SetDataObject(data, copy, RetryTimes, RetryDelayMs);
            return;
        }
        catch (Exception ex)
        {
            AppLog.Info($"clipboard write (WinForms) failed: {ex.GetType().Name}(0x{unchecked((uint)ex.HResult):X8}): {ex.Message} | 占用方: {GetClipboardOwnerInfo()}");
        }
        // 兜底：WPF 路径（极少数环境 WinForms.Clipboard 不可用）
        try
        {
            var wpf = new DataObject();
            foreach (var fmt in data.GetFormats())
                wpf.SetData(fmt, data.GetData(fmt));
            Clipboard.SetDataObject(wpf, copy);
        }
        catch (Exception ex)
        {
            AppLog.Info($"clipboard write (WPF fallback) failed: {ex} | 占用方: {GetClipboardOwnerInfo()}");
            throw new InvalidOperationException(
                "系统剪贴板无法写入：可能被剪贴板管理器/系统历史占用，或当前进程无剪贴板访问权限。占用方=" + GetClipboardOwnerInfo(), ex);
        }
    }

    /// <summary>WPF DataObject → WinForms DataObject（逐格式搬运，保留 UnicodeText/Text/Html 等）。</summary>
    private static SWF.DataObject ToWinForms(DataObject wpf)
    {
        var wf = new SWF.DataObject();
        foreach (var fmt in wpf.GetFormats())
            wf.SetData(fmt, wpf.GetData(fmt));
        return wf;
    }

    /// <summary>BitmapSource → System.Drawing.Bitmap（先统一转 Pbgra32 再 BGRA 直拷，无 unsafe）。</summary>
    private static System.Drawing.Bitmap BitmapSourceToBitmap(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
        var bmp = new System.Drawing.Bitmap(
            converted.PixelWidth, converted.PixelHeight,
            System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        var rect = new System.Drawing.Rectangle(0, 0, converted.PixelWidth, converted.PixelHeight);
        var bd = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.WriteOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        converted.CopyPixels(new Int32Rect(0, 0, converted.PixelWidth, converted.PixelHeight),
            bd.Scan0, bd.Height * bd.Stride, bd.Stride);
        bmp.UnlockBits(bd);
        return bmp;
    }
}
