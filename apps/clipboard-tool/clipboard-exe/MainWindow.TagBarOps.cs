using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ClipboardExe.Services;

namespace ClipboardExe;

/// <summary>
/// MainWindow 支线：搜索 / 类型过滤 / 标签栏 / 列数 / 归档（M3b-1 轻量 tagbar）。
/// 与主文件同属一个 partial class——仅做文件级关注点分离，不改变运行时行为。
/// 过滤状态字段（_q/_tagFilter/_typeFilter/_includeArchived）随方法一并收敛到此文件，
/// RefreshWall 主线仍直接读取这些字段（partial 类成员共享）。
/// </summary>
public partial class MainWindow
{
    // 过滤状态（对齐 state.filter）
    private string _q = "";
    private string _tagFilter = "";
    private string _typeFilter = "all";
    private bool _includeArchived; // M3b-1：归档按钮 toggle 状态（默认 false）

    // ---- 搜索 / 类型过滤 / 标签栏 ----

    private void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        _q = (SearchBox.Text ?? "").Trim();
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void TypeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb || rb.IsChecked != true) return;
        _typeFilter = rb.Tag as string ?? "all";
        if (_svc == null) return; // InitializeComponent 期间首个 RadioButton 初始 Checked（服务尚未装配）
        RefreshWall();
    }

    /// <summary>标签栏横向滚轮：鼠标滚轮（含 Shift）直接横向滚动标签栏；无溢出时不拦截（让位默认）。
    /// 用 PreviewMouseWheel(隧道) 而非 MouseWheel(冒泡)——在 ScrollViewer 内部任何处理之前抢先接住，
    /// 确保溢出时滚轮必定横滚（不再可能被内部拦截而"无滚轮反应"）；未溢出则不设 Handled，事件继续走默认路径。
    /// ScrollViewer 默认滚轮只纵向，这里把垂直增量转成横向偏移，改善多标签浏览体验。</summary>
    private void TagBarScroller_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (TagBarScroller == null || TagBarScroller.ScrollableWidth <= 0) return; // 无横向溢出，忽略（不 Handled，放行默认）
        double step = e.Delta > 0 ? -48 : 48; // 每格滚 ~48px（与 chip 节奏相称）
        TagBarScroller.ScrollToHorizontalOffset(TagBarScroller.HorizontalOffset + step);
        e.Handled = true; // 阻断默认纵向滚动/冒泡
    }

    /// <summary>完整标签栏 chips（M3b-1：聚合全部条目标签去重 + 当前选中金底 + 点击 toggle 过滤）。
    /// chips 用 ItemsControl/WrapPanel 风格横排，过多可水平滚动（外层 TagBar StackPanel → 改 ScrollViewer）。</summary>
    private void RenderTagBar()
    {
        TagBar.Children.Clear();
        // 全部（永远在最左）
        var all = new Button
        {
            Content = "全部",
            Tag = string.IsNullOrEmpty(_tagFilter) ? "on" : null,
            Style = (Style)FindResource("TagChip"),
        };
        all.Click += (_, _) => { _tagFilter = ""; RenderTagBar(); RefreshWall(); };
        TagBar.Children.Add(all);

        // 聚合标签（活跃区 + 归档——恢复条目带标签也要能筛，Search("",includeArchived=true)）
        var allTags = _svc.Search("", includeArchived: true)
            .SelectMany(c => c.Tags ?? new List<string>())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal);
        foreach (var tag in allTags)
        {
            var chip = new Button
            {
                Content = tag,
                Tag = (_tagFilter == tag) ? "on" : null,
                Style = (Style)FindResource("TagChip"),
            };
            var captured = tag;
            chip.Click += (_, _) =>
            {
                _tagFilter = (_tagFilter == captured) ? "" : captured;
                RenderTagBar();
                RefreshWall();
            };
            TagBar.Children.Add(chip);
        }
    }

    /// <summary>列数偏好切换：0(自动) → 1 → 2 → 3 → 4 → 0 循环；立即持久化 + 刷新列宽。
    /// 按钮文案显示当前选择（"列数·自动" / "列数·2"）便于查看。</summary>
    private void ColsBtn_Click(object sender, RoutedEventArgs e)
    {
        _settings.MaxColumns = (_settings.MaxColumns + 1) % 5; // 0~4 循环
        _settings.Save();
        UpdateColsBtnText();
        UpdateColumnWidth();
    }

    private void UpdateColsBtnText()
        => ColsBtn.Content = _settings.MaxColumns == 0 ? "列数·自动" : "列数·" + _settings.MaxColumns;

    /// <summary>含归档 toggle：默认 false 只看活跃区；点开后看到归档卡（可 ↺ 恢复 + ✕ 删除）。
    /// 按钮文案 + 颜色双反馈："归档·关"暗色 / "归档·开"金色（与置顶选中态一致）。</summary>
    private void ArchBtn_Click(object sender, RoutedEventArgs e)
    {
        _includeArchived = !_includeArchived;
        ArchBtn.Content = _includeArchived ? "归档·开" : "归档·关";
        ArchBtn.Foreground = _includeArchived
            ? new SolidColorBrush(Color.FromRgb(0xD4, 0xAF, 0x37)) // 选中金（与 PinBtn 一致）
            : new SolidColorBrush(Color.FromRgb(0x84, 0x84, 0x84)); // 暗灰（与 MutedBrush 视觉一致）
        RefreshWall();
    }
}
