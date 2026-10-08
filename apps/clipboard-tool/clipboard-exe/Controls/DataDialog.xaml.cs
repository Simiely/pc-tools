// Controls/DataDialog.xaml.cs - 数据管理（本地备份 + WebDAV 同步）
//  - 导出：BuildExport → 写 JSON 文件（无 BOM，camelCase，与 Web 互导）
//  - 导入：读 JSON → 校验 {clips[]} → ImportClips 合并 → 刷新
//  - 清空：Confirm → ClearAll → 刷新
//  - WebDAV 同步：测试连接并保存 / 立即同步（配置写入 数据管理，工具栏「同步」只负责同步）
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ClipboardExe.Models;
using ClipboardExe.Services;

namespace ClipboardExe.Controls;

public partial class DataDialog : UserControl
{
    private readonly ClipService _svc;
    private readonly Action _onChanged;
    private readonly SyncController _sync;
    private SyncConfig? _saved;

    public DataDialog(ClipService svc, Action onChanged, SyncController sync)
    {
        InitializeComponent();
        _svc = svc;
        _onChanged = onChanged;
        _sync = sync;

        // 默认地址规则：输入过（已保存配置）→ 一直用输入过的；从没输入过 → WebDavSync.DefaultUrl
        var current = sync.Config;
        UrlBox.Text = current?.Url ?? WebDavSync.DefaultUrl;
        UserBox.Text = current?.User ?? "";
        // 密码不回显：留空 = 复用已保存密码（对齐 saveSyncConfig 语义）
        SyncFilesChk.IsChecked = current?.SyncFiles ?? true; // v0.7.6：默认开启（此前默认关闭 → 他端图片/附件下载失败）
        AutoSyncChk.IsChecked = current?.AutoSync ?? false;
        IntervalBox.Text = (current != null && current.IntervalMin > 0 ? current.IntervalMin : WebDavSync.DefaultIntervalMin).ToString();
        _saved = current;
        PwdHint.Text = current != null ? "留空则沿用已保存密码" : "首次配置需填写密码";
        // 账号昵称：仅已绑定账号（有配置且带账号名）可改；否则禁用并提示先绑定同步账号
        NickBox.Text = current?.DisplayName ?? "";
        var hasAcct = current != null && !string.IsNullOrWhiteSpace(current.AccountName);
        NickBox.IsEnabled = hasAcct;
        NickHint.Text = hasAcct
            ? "同步后其它设备可见；留空则显示账号名"
            : "先绑定同步账号（点同步从远端选一个）后，才能设置本账号昵称";
        if (current != null) ShowStatus(current);
    }

    // ---------- 本地备份 ----------

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = "导出剪贴板备份",
            Filter = "JSON 文件|*.json|所有文件|*.*",
            FileName = "clipboard-" + System.DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json",
            DefaultExt = ".json",
            AddExtension = true,
        };
        ModalHost.SuppressDismiss = true; // 子对话框期间屏蔽失焦自动关闭
        var okSave = dlg.ShowDialog() == true;
        ModalHost.SuppressDismiss = false;
        if (!okSave) return;
        try
        {
            var doc = _svc.BuildExport();
            File.WriteAllText(dlg.FileName, Storage.Serialize(doc), new System.Text.UTF8Encoding(false));
            ModalHost.Close();
            ToastService.Flash($"已导出 {doc.Clips.Count} 条（含归档）");
        }
        catch (Exception ex) { ToastService.Error("导出失败：" + ex.Message); }
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "导入剪贴板备份",
            Filter = "JSON 文件|*.json|所有文件|*.*",
            Multiselect = false,
        };
        ModalHost.SuppressDismiss = true; // 子对话框期间屏蔽失焦自动关闭
        var okOpen = dlg.ShowDialog() == true;
        ModalHost.SuppressDismiss = false;
        if (!okOpen) return;
        try
        {
            var doc = Storage.Deserialize<ExportDoc>(File.ReadAllText(dlg.FileName));
            if (doc == null || doc.Clips == null) throw new InvalidOperationException("不是剪贴板备份文件");
            var res = _svc.ImportClips(doc.Clips);
            ModalHost.Close();
            _onChanged();
            ToastService.Flash($"导入完成：新增 {res.Added} / 更新 {res.Updated} / 跳过 {res.Skipped}（共 {res.Total} 条）");
        }
        catch (Exception ex) { ToastService.Error("导入失败：" + ex.Message); }
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        ModalHost.Confirm("确定清空全部内容（含归档）？此操作不可撤销。",
            () =>
            {
                var n = _svc.ClearAll();
                _onChanged();
                ToastService.Flash($"已清空 {n} 条内容");
            }, "全部清空");
    }

    // ---------- WebDAV 同步 ----------

    private void ShowStatus(SyncConfig c)
    {
        var when = c.LastSyncAt > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(c.LastSyncAt).LocalDateTime.ToString("yyyy-MM-dd HH:mm")
            : "从未";
        StatusText.Text = string.IsNullOrEmpty(c.LastSyncError)
            ? $"上次同步：{when}"
            : $"上次同步：{when} · 错误：{c.LastSyncError}";
        StatusCard.Visibility = Visibility.Visible;
    }

    /// <summary>从表单构建配置：地址留空则回退到（已保存地址 或 默认 http://192.168.2.1:6086）；用户名留空用已保存值；密码留空 = 复用已保存/当前密码。</summary>
    private SyncConfig BuildFromFields()
    {
        int.TryParse(IntervalBox.Text, out var iv);
        var url = string.IsNullOrWhiteSpace(UrlBox.Text) ? (_saved?.Url ?? WebDavSync.DefaultUrl) : UrlBox.Text.Trim();
        var user = string.IsNullOrWhiteSpace(UserBox.Text) ? (_saved?.User ?? "") : UserBox.Text.Trim();
        var cfg = WebDavSync.ValidateAndBuild(
            url, user, PwdBox.Password,
            SyncFilesChk.IsChecked == true, AutoSyncChk.IsChecked == true,
            iv > 0 ? iv : WebDavSync.DefaultIntervalMin, _saved);
        cfg.DisplayName = NickBox.Text?.Trim() ?? ""; // 昵称以输入框为准（清空 = 清掉昵称，显示回账号名）
        return cfg;
    }

    private async void TestSave_Click(object sender, RoutedEventArgs e)
    {
        SyncConfig cfg;
        try { cfg = BuildFromFields(); }
        catch (Exception ex) { ToastService.Error(ex.Message); return; }
        try
        {
            await WebDavClient.TestConnection(cfg); // 连通测试（含写探针+读回）
            WebDavSync.SaveConfig(_sync.DataDir, cfg);
            _saved = cfg;
            _sync.SetConfig(cfg);
            ToastService.Flash("已连接并保存");
            ShowStatus(cfg);
        }
        catch (Exception ex) { ToastService.Error("连接失败：" + ex.Message); }
    }

    private async void SyncNow_Click(object sender, RoutedEventArgs e)
    {
        SyncConfig cfg;
        try { cfg = BuildFromFields(); }
        catch (Exception ex) { ToastService.Error(ex.Message); return; }
        try
        {
            ToastService.Flash("同步中…");
            // 后台执行（WebDAV 网络 IO，避免阻塞 UI）
            var r = await _sync.RunNow(cfg);
            if (r.Ok)
            {
                _saved = cfg;
                _onChanged(); // 同步可能增/删/改本地条目 → 刷新卡片墙（含归档→活跃恢复等场景）
                ToastService.Flash($"同步完成 · 共 {r.Clips} 条");
                if (_sync.Config != null) ShowStatus(_sync.Config);
            }
            else ToastService.Error("同步失败：" + (r.Error ?? "未知错误"));
        }
        catch (Exception ex) { ToastService.Error("同步失败：" + ex.Message); }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => ModalHost.Close();
}
