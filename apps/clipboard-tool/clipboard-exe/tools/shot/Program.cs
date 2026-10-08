using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;

class Shot
{
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr ins, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] static extern bool MoveWindow(IntPtr h, int x, int y, int cx, int cy, bool repaint);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, uint dx, uint dy, uint data, IntPtr extra);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextW(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr ChildWindowFromPointEx(IntPtr h, POINT p, uint flags);

    const uint SWP_NOZORDER = 0x0004;
    const uint SWP_SHOWWINDOW = 0x0040;
    const uint SWP_NOMOVE = 0x0002;
    const uint SWP_NOSIZE = 0x0001;
    const int SW_RESTORE = 9, SW_SHOW = 5;
    const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    const uint MOUSEEVENTF_LEFTUP = 0x0004;
    const uint CWP_SKIPINVISIBLE = 0x0001;
    const uint CWP_SKIPDISABLED = 0x0002;
    const uint CWP_SKIPTRANSPARENT = 0x0004;
    static readonly IntPtr HWND_TOP = new IntPtr(0);
    static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

    delegate bool EnumProc(IntPtr h, IntPtr lp);

    /// <summary>找到 procName 进程的主可见窗口（hwnd + 客户区 clientRect）。</summary>
    static IntPtr FindWindow(string procName, out System.Diagnostics.Process proc)
    {
        proc = null!;
        var procs = System.Diagnostics.Process.GetProcessesByName(procName);
        if (procs.Length == 0) return IntPtr.Zero;
        proc = procs[0];
        int pid = proc.Id;
        IntPtr target = IntPtr.Zero;
        var sb = new StringBuilder(256);
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out int wp);
            if (wp != pid) return true;
            GetWindowText(h, sb, sb.Capacity);
            GetWindowRect(h, out var r);
            if (IsWindowVisible(h) && (r.R - r.L) > 200) target = h;
            return true;
        }, IntPtr.Zero);
        return target;
    }

    static int Main(string[] args)
    {
        // 子命令: click <procName> <x> <y>  — 客户区相对坐标点击（开发期冒烟验证用）
        if (args.Length > 0 && args[0] == "click") return DoClick(args);
        // 子命令: probe <procName> <x> <y>  — 输出该坐标处控件类名+标题（调试用）
        if (args.Length > 0 && args[0] == "probe") return DoProbe(args);
        // 子命令: clickby <procName> <name>  — UIAutomation 按 Name 找按钮 Invoke（推荐：WPF 不暴露 Win32 子窗口）
        if (args.Length > 0 && args[0] == "clickby") return DoClickByName(args);
        // 子命令: list <procName> [filter]  — 枚举窗口内所有控件 Name（UI 状态精确验证）
        if (args.Length > 0 && args[0] == "list") return DoList(args);
        // 子命令: crop <procName> <outPath> <x> <y> <w> <h> [scale] — 截图后裁剪放大（小控件细节核对）
        if (args.Length > 0 && args[0] == "crop") return DoCrop(args);
        // 子命令: wins <procName> — 枚举进程所有可见顶层窗口 rect（弹窗定位/拖动前后比对）
        if (args.Length > 0 && args[0] == "wins") return DoWins(args);
        // 子命令: drag <x1> <y1> <x2> <y2> [steps] — 屏幕坐标按住左键拖动(验证无边框弹窗系统拖动)
        if (args.Length > 0 && args[0] == "drag") return DoDrag(args);
        // 子命令: region <outPath> <x> <y> <w> <h> [count] — 屏幕区域抓图连拍(不依赖 PrintWindow,透明窗口有效;拖动中连拍验闪烁)
        if (args.Length > 0 && args[0] == "region") return DoRegion(args);

        // 默认: 截图。args: [procName] [outPath] [targetW?]
        var procName = args.Length > 0 ? args[0] : "Clipboard";
        var outPath = args.Length > 1 ? args[1] : "shot.png";
        int targetW = args.Length > 2 ? int.Parse(args[2]) : 0;

        var proc = System.Diagnostics.Process.GetProcessesByName(procName);
        if (proc.Length == 0) { Console.WriteLine("ERR no proc"); return 1; }
        var target = FindWindow(procName, out _);
        if (target == IntPtr.Zero) { Console.WriteLine("ERR no window"); return 1; }

        GetClientRect(target, out var cr);
        Console.WriteLine("before client: " + cr.L + "," + cr.T + " " + (cr.R-cr.L) + "x" + (cr.B-cr.T));

        if (targetW > 0)
        {
            GetWindowRect(target, out var wr);
            var nw = targetW;
            var nh = wr.B - wr.T;
            MoveWindow(target, wr.L, wr.T, nw, nh, true);
            Thread.Sleep(800);
            GetClientRect(target, out cr);
            Console.WriteLine("after client: " + cr.L + "," + cr.T + " " + (cr.R-cr.L) + "x" + (cr.B-cr.T));
        }

        ShowWindow(target, SW_RESTORE);
        ShowWindow(target, SW_SHOW);
        Thread.Sleep(300);

        GetClientRect(target, out var rect);
        int w = rect.R - rect.L, h = rect.B - rect.T;
        using var bmp = new Bitmap(w, h);
        using (var g = Graphics.FromImage(bmp))
        {
            var hdc = g.GetHdc();
            var ok = PrintWindow(target, hdc, 2);
            Console.WriteLine("PrintWindow=" + ok + " size=" + w + "x" + h);
            g.ReleaseHdc(hdc);
        }
        bmp.Save(outPath, ImageFormat.Png);
        var bitbltPath = outPath.Replace(".png", ".bitblt.png");
        bmp.Save(bitbltPath, ImageFormat.Png);
        Console.WriteLine("OK -> " + outPath);
        return 0;
    }

    /// <summary>客户区相对坐标点击（临时置顶避免被遮挡）。</summary>
    static int DoClick(string[] args)
    {
        if (args.Length < 4) { Console.WriteLine("用法: shot click <procName> <x> <y>"); return 1; }
        var procName = args[1];
        if (!int.TryParse(args[2], out int x) || !int.TryParse(args[3], out int y))
        { Console.WriteLine("ERR: x/y must be int"); return 1; }
        var target = FindWindow(procName, out _);
        if (target == IntPtr.Zero) { Console.WriteLine("ERR no window"); return 1; }
        SetWindowPos(target, HWND_TOP, 0, 0, 0, 0, SWP_NOZORDER | SWP_SHOWWINDOW | SWP_NOMOVE | SWP_NOSIZE);
        SetForegroundWindow(target);
        Thread.Sleep(150);
        var pt = new POINT { X = x, Y = y };
        ClientToScreen(target, ref pt);
        SetCursorPos(pt.X, pt.Y);
        Thread.Sleep(80);
        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, IntPtr.Zero);
        Thread.Sleep(40);
        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, IntPtr.Zero);
        SetWindowPos(target, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOZORDER | SWP_NOMOVE | SWP_NOSIZE);
        Console.WriteLine($"clicked ({x},{y}) -> screen ({pt.X},{pt.Y})");
        return 0;
    }

    /// <summary>探测 (x,y) 处最深子窗口信息（调试用）。</summary>
    static int DoProbe(string[] args)
    {
        if (args.Length < 4) { Console.WriteLine("用法: shot probe <procName> <x> <y>"); return 1; }
        var procName = args[1];
        if (!int.TryParse(args[2], out int x) || !int.TryParse(args[3], out int y))
        { Console.WriteLine("ERR: x/y must be int"); return 1; }
        var target = FindWindow(procName, out _);
        if (target == IntPtr.Zero) { Console.WriteLine("ERR no window"); return 1; }
        var pt = new POINT { X = x, Y = y };
        ClientToScreen(target, ref pt);
        var hwnd = target;
        for (int depth = 0; depth < 12; depth++)
        {
            var child = ChildWindowFromPointEx(hwnd, pt, CWP_SKIPINVISIBLE | CWP_SKIPDISABLED | CWP_SKIPTRANSPARENT);
            if (child == IntPtr.Zero || child == hwnd) break;
            hwnd = child;
        }
        var cls = new StringBuilder(64); GetClassName(hwnd, cls, 64);
        var txt = new StringBuilder(256); GetWindowTextW(hwnd, txt, 256);
        Console.WriteLine($"screen ({pt.X},{pt.Y}) hwnd={hwnd:X} class={cls} text='{txt}'");
        return 0;
    }

    /// <summary>截图后裁剪放大（小控件细节核对：圆角/间距/配色）。
    /// 用法: shot crop &lt;procName&gt; &lt;outPath&gt; &lt;x&gt; &lt;y&gt; &lt;w&gt; &lt;h&gt; [scale]</summary>
    static int DoCrop(string[] args)
    {
        if (args.Length < 7) { Console.WriteLine("用法: shot crop <procName> <outPath> <x> <y> <w> <h> [scale]"); return 1; }
        var procName = args[1];
        var outPath = args[2];
        if (!int.TryParse(args[3], out int cx) || !int.TryParse(args[4], out int cy) ||
            !int.TryParse(args[5], out int cw) || !int.TryParse(args[6], out int ch))
        { Console.WriteLine("ERR: x/y/w/h must be int"); return 1; }
        int scale = args.Length > 7 && int.TryParse(args[7], out var s) && s > 0 ? s : 1;

        var proc = System.Diagnostics.Process.GetProcessesByName(procName);
        if (proc.Length == 0) { Console.WriteLine("ERR no proc"); return 1; }
        var target = FindWindow(procName, out _);
        if (target == IntPtr.Zero) { Console.WriteLine("ERR no window"); return 1; }

        ShowWindow(target, SW_SHOW);
        Thread.Sleep(300);

        GetClientRect(target, out var rect);
        int w = rect.R - rect.L, h = rect.B - rect.T;
        using var bmp = new Bitmap(w, h);
        using (var g = Graphics.FromImage(bmp))
        {
            var hdc = g.GetHdc();
            PrintWindow(target, hdc, 2);
            g.ReleaseHdc(hdc);
        }
        var rw = Math.Max(1, Math.Min(cw, Math.Max(1, w - cx)));
        var rh = Math.Max(1, Math.Min(ch, Math.Max(1, h - cy)));
        using var src = bmp.Clone(new Rectangle(cx, cy, rw, rh), bmp.PixelFormat);
        if (scale == 1) { src.Save(outPath, ImageFormat.Png); Console.WriteLine($"OK crop ({cx},{cy}) {rw}x{rh} -> {outPath}"); return 0; }

        using var big = new Bitmap(rw * scale, rh * scale);
        using (var g2 = Graphics.FromImage(big))
        {
            g2.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            g2.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            g2.DrawImage(src, 0, 0, rw * scale, rh * scale);
        }
        big.Save(outPath, ImageFormat.Png);
        Console.WriteLine($"OK crop ({cx},{cy}) {rw}x{rh} x{scale} -> {outPath}");
        return 0;
    }

    /// <summary>UIAutomation 按 Name 找按钮并 Invoke（推荐：WPF 不暴露 Win32 子窗口，坐标点击不可靠）。
    /// 用法: shot clickby &lt;procName&gt; &lt;buttonName&gt;</summary>
    static int DoClickByName(string[] args)
    {
        if (args.Length < 3) { Console.WriteLine("用法: shot clickby <procName> <buttonName>"); return 1; }
        var procName = args[1];
        var name = args[2];
        var target = FindWindow(procName, out _);
        if (target == IntPtr.Zero) { Console.WriteLine("ERR no window"); return 1; }
        var element = AutomationElement.FromHandle(target);
        if (element == null) { Console.WriteLine("ERR no uia element"); return 1; }
        var cond = new System.Windows.Automation.PropertyCondition(AutomationElement.NameProperty, name);
        var btn = element.FindFirst(TreeScope.Descendants, cond);
        if (btn == null) { Console.WriteLine($"ERR no button named '{name}'"); return 1; }
        SetWindowPos(target, HWND_TOP, 0, 0, 0, 0, SWP_NOZORDER | SWP_SHOWWINDOW | SWP_NOMOVE | SWP_NOSIZE);
        SetForegroundWindow(target);
        Thread.Sleep(200);
        if (btn.TryGetCurrentPattern(InvokePattern.Pattern, out var p))
        {
            ((InvokePattern)p).Invoke();
            Console.WriteLine($"invoked '{name}'");
            return 0;
        }
        Console.WriteLine($"ERR '{name}' has no InvokePattern"); return 1;
    }

    /// <summary>枚举窗口内所有控件的 Name + ControlType（UI 状态精确验证）。用法: shot list <procName> [filter]</summary>
    static int DoList(string[] args)
    {
        if (args.Length < 2) { Console.WriteLine("用法: shot list <procName> [filter]"); return 1; }
        var procName = args[1];
        var filter = args.Length > 2 ? args[2] : null;
        var target = FindWindow(procName, out _);
        if (target == IntPtr.Zero) { Console.WriteLine("ERR no window"); return 1; }
        var element = AutomationElement.FromHandle(target);
        if (element == null) { Console.WriteLine("ERR no uia element"); return 1; }
        var all = element.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
        int count = 0, shown = 0;
        foreach (AutomationElement ae in all)
        {
            var name = ae.Current.Name ?? "";
            if (filter != null && !name.Contains(filter)) continue;
            count++;
            if (shown < 60)
            {
                Console.WriteLine($"btn: '{name}'");
                shown++;
            }
        }
        Console.WriteLine($"--- {count} buttons{(filter != null ? " (filter=" + filter + ")" : "")}");
        return 0;
    }

    /// <summary>枚举进程所有可见顶层窗口 rect。用法: shot wins &lt;procName&gt;</summary>
    static int DoWins(string[] args)
    {
        if (args.Length < 2) { Console.WriteLine("用法: shot wins <procName>"); return 1; }
        DumpRects(args[1]);
        return 0;
    }

    /// <summary>打印进程所有可见窗口 (class | title | rect)。</summary>
    static void DumpRects(string procName)
    {
        var procs = System.Diagnostics.Process.GetProcessesByName(procName);
        if (procs.Length == 0) { Console.WriteLine("ERR no proc"); return; }
        int pid = procs[0].Id;
        var sb = new StringBuilder(256);
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out int wp);
            if (wp != pid || !IsWindowVisible(h)) return true;
            GetClassName(h, sb, 256); var cls = sb.ToString();
            var txt = new StringBuilder(256); GetWindowTextW(h, txt, 256);
            GetWindowRect(h, out var r);
            Console.WriteLine($"win hwnd={h:X} class={cls} title='{txt}' rect=({r.L},{r.T})-({r.R},{r.B}) {r.R - r.L}x{r.B - r.T}");
            return true;
        }, IntPtr.Zero);
    }

    /// <summary>屏幕坐标按住左键从 (x1,y1) 拖到 (x2,y2) 松开——验证无边框弹窗的系统级拖动(HTCAPTION)。
    /// 先激活起点处的窗口(第一击仅激活不派发),拖动前/后打印进程全部窗口 rect 供客观比对位移。
    /// 用法: shot drag &lt;x1&gt; &lt;y1&gt; &lt;x2&gt; &lt;y2&gt; [steps]</summary>
    static int DoDrag(string[] args)
    {
        if (args.Length < 5) { Console.WriteLine("用法: shot drag <x1> <y1> <x2> <y2> [steps]"); return 1; }
        if (!int.TryParse(args[1], out int x1) || !int.TryParse(args[2], out int y1) ||
            !int.TryParse(args[3], out int x2) || !int.TryParse(args[4], out int y2))
        { Console.WriteLine("ERR: coords must be int"); return 1; }
        int steps = args.Length > 5 && int.TryParse(args[5], out var s) && s > 0 ? s : 20;

        var hwnd = WindowFromPoint(new POINT { X = x1, Y = y1 });
        Console.WriteLine($"drag from ({x1},{y1}) to ({x2},{y2}) steps={steps} downWin={hwnd:X}");
        if (hwnd != IntPtr.Zero) { SetForegroundWindow(hwnd); Thread.Sleep(400); } // 激活后首击才不会被吞

        DumpRects("Clipboard");
        SetCursorPos(x1, y1);
        Thread.Sleep(150);
        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, IntPtr.Zero);
        Thread.Sleep(150);
        for (int i = 1; i <= steps; i++)
        {
            int cx = x1 + (x2 - x1) * i / steps;
            int cy = y1 + (y2 - y1) * i / steps;
            SetCursorPos(cx, cy);
            Thread.Sleep(15);
        }
        Thread.Sleep(150);
        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, IntPtr.Zero);
        Thread.Sleep(400);
        Console.WriteLine("--- after drag ---");
        DumpRects("Clipboard");
        return 0;
    }

    /// <summary>屏幕区域抓图(CopyFromScreen,不依赖 PrintWindow → 对 AllowsTransparency 的 layered 窗口同样有效)。
    /// 拖动中连拍可客观检验"狂闪":闪烁 = 帧间出现空白/残影/错位,连续帧内容完整即无重绘滞后。
    /// 用法: shot region &lt;outPath&gt; &lt;x&gt; &lt;y&gt; &lt;w&gt; &lt;h&gt; [count] — 连拍 count 张(默认1,间隔30ms),多张输出 outPath_1.png...</summary>
    static int DoRegion(string[] args)
    {
        if (args.Length < 6) { Console.WriteLine("用法: shot region <outPath> <x> <y> <w> <h> [count]"); return 1; }
        var outPath = args[1];
        if (!int.TryParse(args[2], out int x) || !int.TryParse(args[3], out int y) ||
            !int.TryParse(args[4], out int w) || !int.TryParse(args[5], out int h) ||
            w <= 0 || h <= 0)
        { Console.WriteLine("ERR: x/y/w/h must be positive int"); return 1; }
        int count = args.Length > 6 && int.TryParse(args[6], out var c) && c > 0 ? Math.Min(c, 30) : 1;

        for (int i = 0; i < count; i++)
        {
            using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                try { g.CopyFromScreen(x, y, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy); }
                catch (Exception ex) { Console.WriteLine($"ERR frame {i + 1}: {ex.Message}"); return 1; }
            }
            var p = count == 1 ? outPath : outPath.Replace(".png", $"_{i + 1}.png");
            bmp.Save(p, ImageFormat.Png);
            if (i < count - 1) Thread.Sleep(30);
        }
        Console.WriteLine($"region ({x},{y}) {w}x{h} x{count} -> {outPath.Replace(".png", "")}_*.png");
        return 0;
    }
}
