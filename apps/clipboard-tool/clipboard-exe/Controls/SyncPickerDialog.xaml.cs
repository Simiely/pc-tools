// Controls/SyncPickerDialog.xaml.cs - 首次同步/切换远端账号：连服务器→枚举远端账号→选一个拉回
// 本机保持单账号：所选账号名即持久化为本机 accountName，后续工具条「同步」直接用它（不再每次选）。
// 仅明文快照（本轮不做加密账号）；服务器不支持 PROPFIND 时给出可操作提示。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ClipboardExe.Models;
using ClipboardExe.Services;

namespace ClipboardExe.Controls;

public partial class SyncPickerDialog : UserControl
{
    private readonly SyncController _sync;
    private readonly Action _onChanged;
    private readonly List<string> _found = new(); // 枚举到的远端账号名（显示序）
    private string? _chosen; // 当前选中的账号名

    public SyncPickerDialog(SyncController sync, Action onChanged)
    {
        InitializeComponent();
        _sync = sync;
        _onChanged = onChanged;

        // 预填服务器凭据（有已保存配置则带出，密码不回显留空=复用）
        var cur = _sync.Config;
        UrlBox.Text = cur?.Url ?? WebDavSync.DefaultUrl;
        UserBox.Text = cur?.User ?? "";
        PwdBox.Focus();
    }

    // ---------- 服务器凭据 ----------

    private SyncConfig BuildServerCfg()
    {
        var url = string.IsNullOrWhiteSpace(UrlBox.Text) ? WebDavSync.DefaultUrl : UrlBox.Text.Trim();
        var user = UserBox.Text?.Trim() ?? "";
        var pass = PwdBox.Password;
        var old = _sync.Config;
        if (string.IsNullOrEmpty(pass)) pass = old?.Pass ?? "";
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new WebDavException(400, "服务器地址需以 http(s):// 开头");
        return new SyncConfig { Url = url, User = user, Pass = pass };
    }

    // ---------- 连接 + 枚举 ----------

    private async void ListBtn_Click(object sender, RoutedEventArgs e)
    {
        SyncConfig srv;
        try { srv = BuildServerCfg(); }
        catch (Exception ex) { SetStatus(ex.Message, true); return; }
        SetBusy(true, "连接并读取远端账号…");
        ListBtn.IsEnabled = false;
        try
        {
            // 先连通测试（含自动建目录），再枚举——既验证凭据又确保能读到
            await Task.Run(() => WebDavClient.TestConnection(srv));
            _found.Clear();
            _found.AddRange(await Task.Run(() => WebDavClient.ListRemoteAccountNames(srv)));
            _chosen = null;
            SyncBtn.IsEnabled = false;
            RenderAccounts();
            if (_found.Count == 0)
            {
                SetStatus("连接成功，但该服务器上没有检测到任何剪贴板账号。请先在任一客户端完成一次同步生成账号，或检查服务器地址。", true);
            }
            else
            {
                ListCard.Visibility = Visibility.Visible;
                SetStatus($"连接成功，检测到 {_found.Count} 个远端账号。", false);
            }
        }
        catch (Exception ex)
        {
            SetStatus("无法读取远端账号：" + ex.Message, true);
        }
        finally { SetBusy(false); ListBtn.IsEnabled = true; }
    }

    private void RenderAccounts()
    {
        AcctList.Children.Clear();
        if (_found.Count == 0) return;
        ListHead.Text = "选择要同步的账号（本机将固定用它，后续同步不再询问）：";
        for (var i = 0; i < _found.Count; i++)
        {
            var name = _found[i];
            var rb = new RadioButton
            {
                Content = name,
                GroupName = "RemoteAcct",
                FontSize = 13,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xDA, 0xDA, 0xDA)),
                Margin = new Thickness(2, 5, 2, 5),
                IsChecked = i == 0, // 默认选第一个
            };
            var cap = name;
            rb.Checked += (_, _) => { _chosen = cap; SyncBtn.IsEnabled = true; };
            AcctList.Children.Add(rb);
        }
        SyncBtn.IsEnabled = true;
        _chosen = _found[0];
    }

    // ---------- 同步所选 ----------

    private async void SyncBtn_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_chosen)) return;
        SyncConfig srv;
        try { srv = BuildServerCfg(); }
        catch (Exception ex) { SetStatus(ex.Message, true); return; }
        // 保留本机其它偏好（同步文件/自动同步/间隔）不变；仅把账号名切到所选 + 落库 url/user/pass
        var old = _sync.Config ?? new SyncConfig();
        var cfg = new SyncConfig
        {
            Url = srv.Url,
            User = srv.User,
            Pass = srv.Pass,
            SyncFiles = old.SyncFiles,
            AutoSync = old.AutoSync,
            IntervalMin = old.IntervalMin > 0 ? old.IntervalMin : WebDavSync.DefaultIntervalMin,
            LastSyncAt = old.LastSyncAt,
            LastSyncError = old.LastSyncError,
            AccountName = _chosen, // 关键：切到所选远端账号名
            PendingNameMigrations = old.PendingNameMigrations ?? new List<string>(),
        };
        SetBusy(true, $"正在同步账号「{_chosen}」…");
        SyncBtn.IsEnabled = false;
        try
        {
            var r = await _sync.RunNow(cfg); // RunSync 内部会 SaveConfig(含账号名)；成功后 SetConfig 由 RunNow 内重载
            if (r.Ok)
            {
                _onChanged(); // 同步可能增/删/改本地条目 → 刷新卡片墙
                ToastService.Flash($"已同步账号「{_chosen}」· 共 {r.Clips} 条");
                ModalHost.Close();
            }
            else
            {
                SetStatus("同步失败：" + (r.Error ?? "未知错误"), true);
                SyncBtn.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            SetStatus("同步失败：" + ex.Message, true);
            SyncBtn.IsEnabled = true;
        }
        finally { SetBusy(false); }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => ModalHost.Close();

    // ---------- 工具 ----------

    private void SetBusy(bool busy, string? text = null)
    {
        if (text != null) SetStatus(text, false);
        Cursor = busy ? System.Windows.Input.Cursors.Wait : System.Windows.Input.Cursors.Arrow;
    }

    private void SetStatus(string msg, bool isError)
    {
        StatusText.Text = msg;
        StatusText.Foreground = isError
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE0, 0x70, 0x70))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x84, 0x84, 0x84));
        StatusCard.Visibility = Visibility.Visible;
    }
}
