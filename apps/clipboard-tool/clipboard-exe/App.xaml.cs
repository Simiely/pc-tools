using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using ClipboardExe.Services;

namespace ClipboardExe;

/// <summary>
/// 入口装配（对齐 Web 版 server.mjs 入口职责）：
///  - 单实例互斥（已有实例则退出）
///  - 版本指纹（AppVersion/GitCommit，日志首行，实例身份可追溯）
///  - 全局异常兜底（记录不退出，对齐 Web 版进程级兜底语义）
///  - 数据目录：exe 同目录 data/（便携式）
/// </summary>
public partial class App : Application
{
    public const string AppName = "clipboard-tool";

    /// <summary>版本号（csproj Version=0.7.0 → 程序集版本三段）。</summary>
    public static string AppVersion { get; } = ReadVersion();

    /// <summary>Git commit（发布时替换为实际 commit；dev = 未发布）。</summary>
    public static string GitCommit { get; set; } = "dev";

    /// <summary>数据目录（exe 同目录 data/）。</summary>
    public static string DataDir { get; private set; } = ".";

    private const string MutexName = "ClipboardTool_SingleInstance_7e2c1a9f";
    // 唤醒信号（AutoReset 命名事件）：让第二实例通知"活着的第一实例"把主窗口恢复前置。
    // 不能用 Mutex 传信号（Mutex 不可 WaitOne 于其他进程持有）；事件 + 线程循环 WaitOne 支持多次双击多次唤醒。
    private const string WakeEventName = "ClipboardTool_Wake_7e2c1a9f";
    private Mutex? _mutex;
    private bool _mutexOwned;
    private EventWaitHandle? _wakeEvent; // 第一实例持有：后台线程等待"第二实例"唤醒信号
    private Thread? _wakeThread;
    private MainWindow? _main;           // 供唤醒线程在 UI 线程恢复主窗口
    private TrayIconService? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 强制软件渲染：WPF 单文件发布（PublishSingleFile）下，硬件渲染线程在含
        // DropShadowEffect 的视觉树里会静默崩溃（窗口客户区变白、内容不绘制）。
        // 剪贴板工具对渲染性能无要求，软件渲染稳定可靠。
        // 必须在任何视觉对象创建之前设置。
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

        // 生命周期改为显式退出：托盘常驻工具（含 /startup 开机静默模式：主窗最小化/或用户收进托盘后无可见窗）
        // 不能依赖默认 OnLastWindowClose——否则主窗一旦被关/静默无可见窗时应用自动退出。
        // 对齐 WindowTinter 托盘语义：程序只被托盘右键「退出」真正终止（ReallyExit→Shutdown），
        // 点 X/最小化收托盘、静默启动都不结束进程。
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // 自检模式（--selftest）：跑数据层规则断言后退出，不启动主窗（发版前健康检查）。
        // 置于单实例检查之前：自检与主程序互不干扰，可随时并行跑（对齐 Web 版 node 测试脚本思路）。
        if (e.Args.Contains("--selftest"))
        {
            Environment.Exit(Services.SelfTest.Run());
            return;
        }

        // 单实例：已有实例 → 通知它把主窗口恢复前置后退出（不静默退出——静默会让用户
        // 双击 exe 毫无反应，误判为"打不开/崩溃"）。
        // 唤醒用"命名 AutoReset 事件 + 第一实例后台线程 WaitOne"，而非遍历进程取
        // MainWindowHandle + ShowWindowAsync：主窗口 Hide 进托盘（点 X/最小化→托盘）后
        // MainWindowHandle 返回 0，外部 ShowWindowAsync 拿不到句柄 → 托盘态再双击无反应。
        // 事件唤醒让第一实例自己 Show/Normal/Activate，托盘态与最小化态都可靠（对齐 plan §7 WM_SHOW_MAIN）。
        _mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            SignalExistingInstance();
            Environment.Exit(0);
            return;
        }
        _mutexOwned = true;

        // 第一实例：启动后台线程监听唤醒事件。AutoReset 保证每次 Set 只放行一次 →
        // 循环 WaitOne 支持"多次双击、每次都唤起主窗口"。收到信号回到 UI 线程恢复主窗。
        _wakeEvent = new EventWaitHandle(false, EventResetMode.AutoReset, WakeEventName);
        _wakeThread = new Thread(() =>
        {
            while (_wakeEvent.WaitOne())
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    // 极早启动竞争：主窗尚未装配则稍后重试（正常装配在数毫秒内完成）。
                    if (_main != null) { _main.WakeMainFromSecondInstance(); return; }
                    var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                    timer.Tick += (_, _) =>
                    {
                        timer.Stop();
                        if (_main != null) _main.WakeMainFromSecondInstance();
                    };
                    timer.Start();
                }));
            }
        }) { IsBackground = true };
        _wakeThread.Start();

        // 数据目录：exe 同目录 data/（便携式，整个文件夹拷走即迁移）
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? ".";
        DataDir = Path.Combine(exeDir, "data");

        // 日志：首行版本指纹（对齐 Web 版"实例身份可追溯"约定）
        AppLog.Init(DataDir, $"clipboard v{AppVersion} ({GitCommit})");
        AppLog.Info("startup");

#if WPFMCP_INSPECTOR
        // WpfVisualTreeMcp Inspector (self-hosted, DEBUG-only "AI 眼睛")：让外部 MCP/CLI
        // 经 named pipe 读取真实 visual tree / 依赖属性 / 模拟点击，做自动视觉验收。
        // 仅在装了 WpfVisualTreeMcp 的 Debug 构建启用（见 csproj），Release 产物不含此段；
        // 初始化失败只记日志，绝不影响主流程（AI 眼睛是辅助，app 本身不依赖它）。
        try
        {
            WpfVisualTreeMcp.Inspector.InspectorService.Initialize(Environment.ProcessId);
            AppLog.Info("inspector-ready");
        }
        catch (Exception inspectorEx) { AppLog.Info("inspector-init-failed: " + inspectorEx); }
#endif

        // 全局异常兜底：记录且不让进程退出（数据全落盘 JSON 原子写，进程存活比崩溃重启更友好）。
        // 必须设置 e.Handled=true，否则 WPF 仍视异常为未处理而关闭整个应用（"闪退"）。
        DispatcherUnhandledException += (_, ex) =>
        {
            AppLog.Info("DispatcherUnhandledException: " + ex.Exception);
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => AppLog.Info("UnhandledException: " + ex.ExceptionObject);

        // 装配主窗体 + 托盘。启动期同步异常必须在装配段就地兜住并显式弹错：
        // DispatcherUnhandledException 只对消息循环内的异常生效，OnStartup 里抛出的
        // 异常会直接走 AppDomain 未处理路径——不弹错的话用户只看到进程消失，
        // 再次误判"打不开/闪退"。
        try
        {
            var settings = Settings.Load(DataDir);
            _tray = new TrayIconService("剪贴板", () => { _main?.ReallyExit(); return true; });
            // 开机自启静默：注册表 Run 值带 /startup → 只驻留托盘、不显示主窗（手动双击无参数则正常显示）。
            // 支持多别名（对齐 WindowTinter /startup /silent /minimized /background /tray 惯例）。
            bool startSilent = e.Args.Any(a =>
                a.Equals("/startup", StringComparison.OrdinalIgnoreCase)
                || a.Equals("/silent", StringComparison.OrdinalIgnoreCase)
                || a.Equals("/minimized", StringComparison.OrdinalIgnoreCase)
                || a.Equals("/background", StringComparison.OrdinalIgnoreCase)
                || a.Equals("/tray", StringComparison.OrdinalIgnoreCase));
            _main = new MainWindow(settings, _tray, startSilent);
            _main.Closed += (_, _) => _tray?.Dispose();
            _main.Show();
        }
        catch (Exception startupEx)
        {
            AppLog.Info("startup-failed: " + startupEx);
            try { _tray?.Dispose(); } catch { /* 托盘释放失败不掩盖主错误 */ }
            try
            {
                MessageBox.Show(
                    "clipboard-tool 启动失败：\n\n" + startupEx.Message +
                    "\n\n详细信息已写入 " + Path.Combine(DataDir, "clipboard-exe.log"),
                    "clipboard-tool", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { /* 弹窗失败也不能再抛 */ }
            Shutdown(-1);
            return;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 停唤醒线程 + 释放命名事件，避免本进程退出后内核对象残留被新进程误 Open
        try { _wakeThread?.Interrupt(); } catch { /* 线程退出中断不影响主流程 */ }
        try { _wakeEvent?.Dispose(); } catch { /* 释放失败不影响退出 */ }
        if (_mutexOwned)
        {
            try { _mutex?.ReleaseMutex(); } catch { /* 释放失败不影响退出 */ }
        }
        base.OnExit(e);
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>第二实例：打开第一实例创建的唤醒事件并置位，让它自己恢复主窗口（随后本进程退出）。</summary>
    private static void SignalExistingInstance()
    {
        try
        {
            using var ev = EventWaitHandle.OpenExisting(WakeEventName);
            ev.Set();
        }
        catch (Exception ex) { AppLog.Info("wake-signal-failed: " + ex.Message); /* 第一实例未建/异常：忽略 */ }
    }

    private static string ReadVersion()
    {
        try
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v == null ? "0.7.0" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
        catch { return "0.7.0"; }
    }
}
