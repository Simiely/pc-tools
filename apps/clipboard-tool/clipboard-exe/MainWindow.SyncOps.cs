using System;
using System.Windows;
using System.Windows.Threading;
using ClipboardExe.Controls;
using ClipboardExe.Services;

namespace ClipboardExe;

/// <summary>
/// MainWindow 支线：WebDAV 同步（M5c，编排下沉 SyncController；工具栏「同步」只负责触发）。
/// 与主文件同属一个 partial class——仅做文件级关注点分离，不改变运行时行为。
/// 同步状态字段（_sync/_autoTimer）随方法一并收敛到此文件；ctor 通过 InitAutoSync() 接线（见主文件 ctor）。
/// </summary>
public partial class MainWindow
{
    private readonly SyncController _sync; // M5c：WebDAV 同步编排（从 UI 层下沉，降低耦合）
    private readonly DispatcherTimer _autoTimer = new() { Interval = TimeSpan.FromMinutes(1) }; // M5c：定时自动同步

    // ---------- M5c：WebDAV 同步（编排下沉 SyncController；工具栏「同步」只负责触发同步） ----------

    /// <summary>ctor 调用：启动定时自动同步（1 分钟轮询，到点才跑；编排逻辑在 SyncController）。</summary>
    internal void InitAutoSync()
    {
        _autoTimer.Tick += (_, _) => _ = _sync.Tick();
        _autoTimer.Start();
    }

    /// <summary>工具栏「同步」按钮：本地已配账号 → 用已保存配置立即同步；
    /// 本地无账号（从未配置）→ 打开「选远端账号」弹窗：连服务器列出远端账号，选一个拉回（不做登录式手填账号）。
    /// 工具条行2操作组同步钮触发；DataDialog 内仍有完整配置/立即同步入口。</summary>
    private async void SyncBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_sync.Config == null)
        {
            // 本地无账号：先枚举远端账号让用户挑（SyncPickerDialog 内部处理凭据/枚举/同步）
            ModalHost.Show(new SyncPickerDialog(_sync, RefreshWall));
            return;
        }
        try
        {
            ToastService.Flash("同步中…");
            var r = await _sync.RunNow(_sync.Config);
            if (r.Ok)
            {
                RefreshWall(); // 同步可能增/删/改本地条目 → 刷新卡片墙（本地为空恢复远端等场景）
                ToastService.Flash($"同步完成 · 共 {r.Clips} 条");
            }
            else ToastService.Error("同步失败：" + (r.Error ?? "未知错误"));
        }
        catch (Exception ex) { ToastService.Error("同步失败：" + ex.Message); }
    }
}
