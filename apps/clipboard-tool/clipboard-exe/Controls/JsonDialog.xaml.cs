// Controls/JsonDialog.xaml.cs - JSON 格式化预览（对齐 app.js openJsonPreview；M3a 只读；M4c 加覆盖保存）
//  - PrettyJson（LF 统一，互导字节一致）；复制美化结果 → flash"已复制美化 JSON"
//  - 覆盖保存：回传新 content，由 MainWindow 调 ClipService.Update 并同步重建 html（带 html 条目）
using System;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ClipboardExe.Models;
using ClipboardExe.Services;

namespace ClipboardExe.Controls;

public partial class JsonDialog : UserControl
{
    private readonly ClipItem _clip;
    private readonly string _formatted;

    /// <summary>覆盖保存请求：参数（原条目, 新的格式化 content）。由 MainWindow 处理 ClipService.Update。</summary>
    public event Action<ClipItem, string>? SaveRequested;

    /// <summary>复制成功：MainWindow 据此标记"本程序写入"，避免关闭本窗后激活主窗被误弹存入窗。</summary>
    public event Action? Copied;

    public JsonDialog(ClipItem clip)
    {
        InitializeComponent();
        _clip = clip;
        HeadTitle.Text = "JSON 预览 · " + (string.IsNullOrEmpty(clip.Title) ? "未命名" : clip.Title);
        _formatted = Format.PrettyJson(clip.Content);
        JsonBox.Text = _formatted;
        JsonBox.CaretIndex = 0;
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ClipboardHelper.SetText(JsonBox.Text);
            ToastService.Flash("已复制美化 JSON");
            Copied?.Invoke();
        }
        catch (Exception ex) { AppLog.Info("json copy failed: " + ex); ToastService.Error("复制失败"); }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var text = JsonBox.Text;
        try { JsonSerializer.Deserialize<object>(text); }
        catch { ToastService.Error("JSON 格式无效，无法保存"); return; }
        SaveRequested?.Invoke(_clip, text);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => ModalHost.Close();
}
