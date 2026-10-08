using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Windows.Forms;

namespace WindowTinter
{
    /// <summary>
    /// 跟踪目标窗口的位置和可见性，通过 WinEvent + 250ms 兜底轮询驱动。
    /// </summary>
    internal class TargetTracker : IDisposable
    {
        public IntPtr TargetHandle { get; set; } = IntPtr.Zero;

        /// <summary>目标窗口状态变化时触发：RECT（屏幕坐标）、是否可见。</summary>
        public event Action<Native.RECT, bool> OnUpdate;

        private readonly Timer _timer;

        // 变更守卫
        private bool _hasLast;
        private Native.RECT _lastRect;
        private bool _lastVisible;

        public TargetTracker()
        {
            _timer = new Timer { Interval = 250 };
            _timer.Tick += (_, _) => Refresh();
            _timer.Start();
        }

        public void RefreshNow() => Refresh();

        private void Refresh()
        {
            if (TargetHandle == IntPtr.Zero || !Native.IsWindow(TargetHandle))
            {
                if (_hasLast) { _hasLast = false; OnUpdate?.Invoke(default, false); }
                return;
            }

            bool visible = Native.IsWindowVisible(TargetHandle) && !Native.IsIconic(TargetHandle);
            if (!Native.GetWindowRect(TargetHandle, out Native.RECT r))
                return; // 窗口已销毁，跳过

            bool changed = !_hasLast
                || r.Left != _lastRect.Left || r.Top != _lastRect.Top
                || r.Right != _lastRect.Right || r.Bottom != _lastRect.Bottom
                || visible != _lastVisible;

            if (!changed) return;

            (_lastRect, _lastVisible, _hasLast) = (r, visible, true);
            OnUpdate?.Invoke(r, visible);
        }

        /// <summary>前台切换专用：无视 rect/visible 变更守卫，强制触发 OnUpdate。</summary>
        public void RefreshForeground()
        {
            if (TargetHandle == IntPtr.Zero || !Native.IsWindow(TargetHandle)) return;
            if (!_hasLast) { Refresh(); return; }
            OnUpdate?.Invoke(_lastRect, _lastVisible);
        }

        // ── 静态查找方法 ──────────────────────────────────────────

        private const int MIN_TARGET_SIZE = 100;

        public static bool IsAcceptableTarget(IntPtr hwnd)
        {
            if (!Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd)) return false;
            Native.GetWindowRect(hwnd, out Native.RECT r);
            return r.Width >= MIN_TARGET_SIZE && r.Height >= MIN_TARGET_SIZE;
        }

        /// <summary>
        /// 按标题+进程名查找目标窗口。
        /// 先精确匹配（保持原有行为），找不到时退回“标题包含”匹配，
        /// 以兼容浏览器/编辑器等运行时标题会动态变化的程序。
        /// 公开签名保持不变，调用方无感知。
        /// </summary>
        public static IntPtr FindByTitleAndProcess(string title, string processName, HashSet<IntPtr> excludeHandles = null)
        {
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(processName))
                return IntPtr.Zero;

            IntPtr found = FindByPredicate(title, processName, excludeHandles, exact: true);
            if (found == IntPtr.Zero)
                found = FindByPredicate(title, processName, excludeHandles, exact: false);
            return found;
        }

        private static IntPtr FindByPredicate(string title, string processName, HashSet<IntPtr> excludeHandles, bool exact)
        {
            string proc = processName.ToLowerInvariant();
            if (proc.EndsWith(".exe")) proc = proc[..^4]; // 兼容旧配置带 .exe 后缀
            string kw = title.ToLowerInvariant();
            IntPtr found = IntPtr.Zero;
            Native.EnumWindows((hwnd, _) =>
            {
                if (!IsAcceptableTarget(hwnd)) return true;
                if (excludeHandles?.Contains(hwnd) == true) return true;
                int len = Native.GetWindowTextLength(hwnd);
                if (len == 0) return true;
                var sb = new StringBuilder(len + 1);
                Native.GetWindowText(hwnd, sb, len + 1);
                string wtitle = sb.ToString().ToLowerInvariant();
                // 精确模式要求完全一致；模糊模式允许标题包含关键词
                bool titleMatch = exact ? wtitle.Equals(kw) : wtitle.Contains(kw);
                if (!titleMatch) return true;
                Native.GetWindowThreadProcessId(hwnd, out uint pid);
                try
                {
                    if (Process.GetProcessById((int)pid).ProcessName?.ToLowerInvariant() == proc)
                    { found = hwnd; return false; }
                }
                catch (Exception ex) { Debug.WriteLine($"FindByTitleAndProcess enum error: {ex.Message}"); }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        public void Dispose()
        {
            _timer.Dispose();
            OnUpdate = null;
        }
    }
}
