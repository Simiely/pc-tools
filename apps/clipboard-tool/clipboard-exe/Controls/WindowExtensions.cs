// Controls/WindowExtensions.cs - 窗口级平台行为扩展（M1 抽离：防 MainWindow 膨胀，M2 弹窗可复用）
// 沉浸式深色标题栏（Win10 1809+，DWM）——从 MainWindow.xaml.cs 等价搬移，行为不变。
// 窗口所在屏工作区（DIP）——修复双屏下 SystemParameters.WorkArea 只认主屏导致的弹窗错屏/钳制错误。
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace ClipboardExe.Controls;

public static class WindowExtensions
{
    /// <summary>开启沉浸式深色标题栏（Win10 1809+）。旧系统静默忽略。</summary>
    public static void EnableImmersiveDarkTitleBar(this Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            var useDark = 1;
            _ = DwmSetWindowAttribute(hwnd, 20, ref useDark, sizeof(int));
        }
        catch { /* 旧系统忽略 */ }
    }

    /// <summary>窗口当前所在显示器的工作区（WPF DIP 单位，含任务栏扣除）。
    /// 依据：WPF 无内置"窗口所在屏"API（SystemParameters.WorkArea 仅主屏），标准做法 = Screen.FromHandle
    /// 取句柄所在屏（SO 254258 高赞）+ GetMonitorInfo/Screen.WorkingArea 的物理像素经
    /// CompositionTarget.TransformFromDevice 转 DIP（多屏混合 DPI 时系统按该屏缩放矩阵换算）。
    /// 退化（无句柄/转换失败）回退主屏 WorkArea，保证永不抛异常。</summary>
    public static Rect GetScreenWorkAreaDip(this Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return SystemParameters.WorkArea;
            var wa = System.Windows.Forms.Screen.FromHandle(hwnd).WorkingArea; // 物理像素
            var m = HwndSource.FromHwnd(hwnd)?.CompositionTarget?.TransformFromDevice;
            if (m == null || m.Value == Matrix.Identity)
                return new Rect(wa.Left, wa.Top, wa.Width, wa.Height); // DPI 100%：像素=DIP
            var tl = m.Value.Transform(new Point(wa.Left, wa.Top));
            var br = m.Value.Transform(new Point(wa.Right, wa.Bottom));
            return new Rect(tl, br);
        }
        catch { return SystemParameters.WorkArea; }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}
