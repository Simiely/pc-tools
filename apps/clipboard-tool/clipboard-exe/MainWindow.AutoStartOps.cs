using System.Windows;
using System.Windows.Media;
using ClipboardExe.Controls;
using ClipboardExe.Services;

namespace ClipboardExe;

/// <summary>
/// MainWindow 支线：开机自启动（顶栏 ⚡ 按钮）。
/// - 状态：以注册表 Run 项实际态为准（AutoStart.IsEnabled()），而非 settings.json——
///   用户可被任务管理器禁用/手动改注册表，按钮必须反映真实值。
/// - 写：AutoStart.Enable()/Disable() 幂等操作注册表；Settings.LaunchAtStartup 仅记忆上次意图。
/// - 选中态高亮金 0xD4AF37（对齐 PinBtn/ArchBtn 开启色），关闭暗灰 0x848484（对齐 MutedBrush）。
/// </summary>
public partial class MainWindow
{
    private const string AutoStartOffText = "⚡ 开机自启";
    private const string AutoStartOnText = "⚡ 开机自启 ✓";

    /// <summary>按注册表实际态刷新按钮文字 + 颜色。</summary>
    private void RefreshAutoStartBtn()
    {
        bool on = AutoStart.IsEnabled();
        AutoStartBtn.Content = on ? AutoStartOnText : AutoStartOffText;
        AutoStartBtn.Foreground = new SolidColorBrush(Color.FromRgb(
            (byte)(on ? 0xD4 : 0x84),
            (byte)(on ? 0xAF : 0x84),
            (byte)(on ? 0x37 : 0x84)));
    }

    private void AutoStartBtn_Click(object sender, RoutedEventArgs e)
    {
        bool on = AutoStart.IsEnabled();
        bool ok;
        if (on)
        {
            ok = AutoStart.Disable();
            if (ok) ToastService.Flash("已关闭开机自启");
        }
        else
        {
            ok = AutoStart.Enable();
            if (ok) ToastService.Flash("已开启开机自启（下次开机自动驻留托盘）");
        }
        if (!ok)
        {
            ToastService.Error("切换开机自启失败，请检查权限");
            return;
        }
        // 记录意图（Settings 仅作持久化痕迹；按钮状态下次启动仍以注册表实际态为准）
        _settings.LaunchAtStartup = AutoStart.IsEnabled();
        _settings.Save();
        RefreshAutoStartBtn();
    }
}
