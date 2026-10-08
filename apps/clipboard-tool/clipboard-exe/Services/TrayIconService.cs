// Services/TrayIconService.cs - 托盘常驻（对齐 Web 版语义：点 X/最小化 → 托盘；托盘"退出"才真正退出）
// 右键菜单：固定暗色渲染（对齐主窗口暗色 UI，见 TrayDarkMenu.cs）——默认 WinForms ContextMenuStrip
// 是浅色白底，与主程序 #1A1A1A 暗色割裂；用自定义 ToolStripProfessionalRenderer 强制暗色，不随系统切。
//
// 托盘图标：不再用占位的 SystemIcons.Application，从 WPF pack 资源 Assets/app.ico 加载
// （与 exe ApplicationIcon 共用同一份二进制，0 维护成本）。Windows 托盘 NotifyIcon 会自动
// 从多分辨率 ico 挑 16×16 帧渲染；如系统 dpi ≠ 96%，NotifyIcon 自动放大，锐利不糊。
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Forms;

namespace ClipboardExe.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _tray;
    private readonly Func<bool> _exitHandler; // 返回 true 才真正退出

    public TrayIconService(string tooltip, Func<bool> exitHandler)
    {
        _exitHandler = exitHandler;
        _tray = new NotifyIcon
        {
            Text = tooltip,
            Icon = LoadAppIcon(),
            Visible = true,
        };
        var menu = new ContextMenuStrip
        {
            Renderer = new TrayDarkRenderer(),      // 固定暗色渲染（见 TrayDarkMenu.cs）
            Font = new Font("Segoe UI", 9f),
            ShowImageMargin = false,                // 无图标列：去掉左侧浅色渐变空列，整条菜单等宽
        };
        var showItem = new ToolStripMenuItem("显示主窗口");
        showItem.Click += (_, _) => ShowMain();
        menu.Items.Add(showItem);
        menu.Items.Add(new ToolStripSeparator());
        var exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => RequestExit();
        menu.Items.Add(exitItem);
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowMain();
    }

    public event Action? ShowMainRequested;

    private void ShowMain() => ShowMainRequested?.Invoke();

    private void RequestExit()
    {
        if (_exitHandler()) _tray.Visible = false;
    }

    public void Dispose()
    {
        _tray.Visible = false;
        _tray.Dispose();
    }

    /// <summary>
    /// 从 WPF pack 资源加载与 exe 同款的 .ico（Assets/app.ico）并返回 System.Drawing.Icon。
    /// - StreamUri：pack://application:,,,/Assets/app.ico —— SDK 已用 &lt;Resource&gt; 把它编进托管资源。
    /// - 文件丢失/资源被剥：兜底用 SystemIcons.Application，托盘不空白（任务栏图标另由 ApplicationIcon 保证）。
    /// - Icon(stream, size) 重载：从 ico 字节流挑最接近 size 的帧；源 stream 立即释放（Icon 已拷贝底层 HICON）。
    /// </summary>
    private static Icon LoadAppIcon()
    {
        const string ResourceUri = "pack://application:,,,/Assets/app.ico";
        try
        {
            var info = System.Windows.Application.GetResourceStream(new Uri(ResourceUri, UriKind.Absolute));
            if (info?.Stream is null) return SystemIcons.Application;
            using var stream = info.Stream;
            // System.Windows.Size 是 WPF 的双精度版本,Icon 要的是 System.Drawing.Size(整数)。
            // 从 ico 中按 32×32 找最匹配的帧；NotifyIcon 会再按 dpi 放大,无须 16/24/32 都挑。
            return new Icon(stream, new System.Drawing.Size(32, 32));
        }
        catch (Exception ex)
        {
            // 资源不可用：托盘仍要给个看得过去的图标，绝不能因缺资源让托盘空白/弹错。
            // 启动期同步异常已在 App.OnStartup 装配段 catch 兜底，这里只兜读取失败。
            System.Diagnostics.Debug.WriteLine("TrayIconService: load app icon failed, fallback to SystemIcons.Application. " + ex);
            return SystemIcons.Application;
        }
    }
}
