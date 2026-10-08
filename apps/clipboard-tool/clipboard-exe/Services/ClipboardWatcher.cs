// Services/ClipboardWatcher.cs - 原生剪贴板监听（对齐 Web 版 navigator.clipboard "clipboardchange" 语义）
//  - AddClipboardFormatListener + HwndSource.AddHook 处理 WM_CLIPBOARDUPDATE(0x031D)
//  - 100ms 防抖：一次真实复制可能连发多条 WM，去重后只触发一次（DispatcherTimer 重启式防抖）
//  - 自写判定：WM 到达瞬间采样剪贴板属主，属本进程则忽略（替代原 800ms 抑制窗——与事件延迟无关，见 ClipboardNative）
//  - Paused：窗口失活/最小化时暂停捕获（对齐 Web 版"仅前台页面派发"语义）
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

namespace ClipboardExe.Services;

public sealed class ClipboardWatcher : IDisposable
{
    private const int WmClipboardUpdate = 0x031D;

    [DllImport("user32.dll")]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    private readonly IntPtr _hwnd;
    private HwndSource? _source;
    private readonly DispatcherTimer _debounce;
    private bool _pendingExternal; // 本轮 WM 中存在"非本程序写入"的剪贴板变化（WM 到达瞬间采样，见 WndProc）
    private bool _disposed;

    /// <summary>窗口失活/最小化时置 true → 暂停捕获（对齐 Web 前台语义）。</summary>
    public bool Paused { get; set; }

    /// <summary>剪贴板有新内容（已过防抖/抑制/暂停检查）。</summary>
    public event Action? ClipboardChanged;

    public ClipboardWatcher(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            if (Paused) { _pendingExternal = false; return; } // 后台期不捕获；唤醒后由激活路径（序列号判重）兜住
            if (!_pendingExternal) return;                    // 本程序自己写入 → 不是用户的新复制
            _pendingExternal = false;
            ClipboardChanged?.Invoke();
        };
    }

    /// <summary>注册监听（OnSourceInitialized 后调用）。</summary>
    public void Attach()
    {
        AddClipboardFormatListener(_hwnd);
        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndProc);
        AppLog.Info("clipboard-watcher: attached");
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmClipboardUpdate)
        {
            // 归属采样必须在这里做，不能拖到 100ms 后的 tick：WM_CLIPBOARDUPDATE 是投递消息，
            // 到达时剪贴板已变更、属主即最后写入者；拖后采样可能已被下一次写入覆盖而误判。
            if (!ClipboardNative.IsOwnedByThisProcess) _pendingExternal = true;

            // 防抖：重启计时器，100ms 内无新消息才触发（一次真实复制连发多条 WM 只处理一次）
            _debounce.Stop();
            _debounce.Start();
            handled = false; // 不吞消息，让其他监听者也可见
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { RemoveClipboardFormatListener(_hwnd); } catch { /* 注销失败不影响退出 */ }
        try { _source?.RemoveHook(WndProc); } catch { /* 同上 */ }
        _debounce.Stop();
        AppLog.Info("clipboard-watcher: disposed");
    }
}
