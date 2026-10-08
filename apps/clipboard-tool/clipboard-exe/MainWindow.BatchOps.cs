using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using ClipboardExe.Controls;
using ClipboardExe.Services;

namespace ClipboardExe;

/// <summary>
/// MainWindow 支线：批量编辑（M3b-3b，对齐 Web 版 .batch-bar + batchDeleteClips / batchSetTags）。
/// 与主文件同属一个 partial class——仅做文件级关注点分离，不改变运行时行为。
/// 批量状态字段（_batchMode/_batchSel/_cards/_visibleIds）随方法一并收敛到此文件，
/// RefreshWall / MakeCard 等主线代码仍可直接访问（partial 类成员共享）。
/// </summary>
public partial class MainWindow
{
    // 批量编辑状态（M3b-3b：进入批量模式后维护；_batchSel 跨刷新持久，卡片按 id 重渲染选中态）
    private bool _batchMode;
    private readonly HashSet<string> _batchSel = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CardView> _cards = new(StringComparer.Ordinal);
    private List<string> _visibleIds = new();

    // ---- 批量编辑（M3b-3b：对齐 Web 版 .batch-bar + batchDeleteClips / batchSetTags） ----

    /// <summary>进入/退出批量模式（编辑按钮 / 完成按钮）。进入时清空选择；重建卡片墙应用 BatchMode。</summary>
    private void SetBatchMode(bool on)
    {
        _batchMode = on;
        if (!on) _batchSel.Clear();
        BatchBar.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        RefreshWall(); // 重建以应用 BatchMode 覆盖层 + 恢复选中态
    }

    private void EditBtn_Click(object sender, RoutedEventArgs e) => SetBatchMode(!_batchMode); // toggle：首次进入批量,再次点击退出(用户期望)

    /// <summary>数据管理弹窗：本地备份（导入/导出/清空）+ WebDAV 同步设置（同步配置入口在此，工具栏「同步」只负责同步）。</summary>
    private void DataBtn_Click(object sender, RoutedEventArgs e)
        => ModalHost.Show(new DataDialog(_svc, RefreshWall, _sync));
    private void BatchDoneBtn_Click(object sender, RoutedEventArgs e) => SetBatchMode(false);

    /// <summary>卡片 SelectionToggled → 切换选择集 → 同步该卡视觉 + 计数。</summary>
    private void OnCardSelectionToggled(string id)
    {
        if (!_batchMode) return;
        if (_batchSel.Contains(id)) _batchSel.Remove(id);
        else _batchSel.Add(id);
        if (_cards.TryGetValue(id, out var card)) card.SetSelected(_batchSel.Contains(id));
        SyncBatchUI();
    }

    /// <summary>全选/取消全选当前页（基于当前 Search 可见集，对齐 getVisibleClips；非全库）。</summary>
    private void BatchSelectAllBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_visibleIds.Count == 0) return;
        bool allSel = _visibleIds.All(id => _batchSel.Contains(id));
        if (allSel) foreach (var id in _visibleIds) _batchSel.Remove(id);
        else foreach (var id in _visibleIds) _batchSel.Add(id);
        foreach (var id in _visibleIds)
            if (_cards.TryGetValue(id, out var card)) card.SetSelected(_batchSel.Contains(id));
        SyncBatchUI();
    }

    private void BatchAddTagBtn_Click(object sender, RoutedEventArgs e) => OpenBatchTagModal(isAdd: true);
    private void BatchRemoveTagBtn_Click(object sender, RoutedEventArgs e) => OpenBatchTagModal(isAdd: false);

    /// <summary>批量标签弹窗：复用 TagPicker；add=全局标签（可新建）/ remove=已选条目标签并集。确认后走 ClipService.BatchSetTags。</summary>
    private void OpenBatchTagModal(bool isAdd)
    {
        if (_batchSel.Count == 0) { ToastService.Flash("请先选择内容"); return; }

        var title = new TextBlock
        {
            Text = isAdd ? "批量添加标签" : "批量移除标签",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
        };
        var hint = new TextBlock
        {
            Text = isAdd ? "选择要添加的标签（输入框回车可新建）" : "选择要移除的标签",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0x84, 0x84, 0x84)),
            Margin = new Thickness(0, 0, 0, 12),
        };
        var picker = new TagPicker();
        if (isAdd)
            picker.SetTags(new List<string>(), GetAllTagsIncludingArchive()); // 全局标签（可新建）
        else
            picker.SetTags(new List<string>(), UnionTagsOfSelection());       // 已选条目标签并集

        var wrap = new StackPanel { Children = { title, hint } };
        // 勾选含被过滤隐藏项时提示：标签会作用到看不见的勾选，避免用户遗忘(S-13 "跨过滤保持")
        var hidden = HiddenSelectionCount();
        if (hidden > 0)
            wrap.Children.Add(new TextBlock
            {
                Text = $"⚠ {_batchSel.Count} 条已选中，其中 {hidden} 条当前被搜索/标签过滤隐藏，仍会被操作",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x2E)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12),
            });
        wrap.Children.Add(picker);

        var ok = new Button
        {
            Style = (Style)FindResource("BtnPrimary"),
            Content = isAdd ? "添加" : "移除",
            MinWidth = 130,
            Margin = new Thickness(0, 16, 10, 0),
        };
        var cancel = new Button
        {
            Style = (Style)FindResource("BtnClose"),
            Content = "取消",
            MinWidth = 130,
        };
        var row = new Grid { Margin = new Thickness(0, 0, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(ok, 0);
        Grid.SetColumn(cancel, 1);
        ok.Margin = new Thickness(0, 0, 10, 0);
        row.Children.Add(ok);
        row.Children.Add(cancel);

        var sp = new StackPanel { Children = { wrap, row } };
        var card = new Border
        {
            Style = (Style)FindResource("ModalCard"),
            Width = 420,
            Child = sp,
        };

        ok.Click += (_, _) =>
        {
            ModalHost.Close();
            try
            {
                var n = _svc.BatchSetTags(_batchSel, picker.Selected, isAdd);
                ToastService.Flash(isAdd ? $"已为 {n} 条添加标签" : $"已从 {n} 条移除标签");
                RefreshWall(); // 标签变化可能触发重排序；重建后保留批量选中态
            }
            catch (Exception ex) { ToastService.Error(ex.Message); }
        };
        cancel.Click += (_, _) => ModalHost.Close();
        ModalHost.Show(card);
    }

    /// <summary>已选条目标签并集（跨活跃+归档读取），供批量移除弹窗展示可选标签。</summary>
    private List<string> UnionTagsOfSelection()
    {
        var items = _storage.LoadClips().Concat(_storage.LoadArchive())
            .Where(c => _batchSel.Contains(c.Id));
        return items.SelectMany(c => c.Tags ?? new List<string>())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>系统已有标签（聚合全部条目去重，含归档；对齐 /api/tags 全量）。</summary>
    private List<string> GetAllTagsIncludingArchive()
        => _svc.Search("", includeArchived: true).SelectMany(c => c.Tags ?? new List<string>())
        .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();

    /// <summary>批量删除：确认 → ClipService.BatchDelete（跨区+清文件+墓碑）→ 退出批量模式 → 刷新。</summary>
    private void BatchDelBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_batchSel.Count == 0) { ToastService.Flash("请先选择内容"); return; }
        var hidden = HiddenSelectionCount();
        var msg = $"删除选中的 {_batchSel.Count} 条内容？此操作不可撤销";
        if (hidden > 0) msg += $"\n⚠ 其中 {hidden} 条当前被搜索/标签过滤隐藏，仍会被删除";
        ModalHost.Confirm(msg, () =>
        {
            try
            {
                var n = _svc.BatchDelete(_batchSel);
                ToastService.Flash($"已删除 {n} 条");
                SetBatchMode(false); // 选择集已删除，退出批量模式
            }
            catch (Exception ex) { ToastService.Error(ex.Message); }
        }, "删除");
    }

    /// <summary>同步批量条 UI：已选计数 + 全选按钮文案（全选→取消全选）。</summary>
    private void SyncBatchUI()
    {
        BatchCountText.Text = $"已选 {_batchSel.Count}";
        bool allSel = _visibleIds.Count > 0 && _visibleIds.All(id => _batchSel.Contains(id));
        BatchSelectAllBtn.Content = allSel ? "取消全选" : "全选当前页";
    }

    /// <summary>当前勾选集中"被过滤隐藏"(不在当前可见集 _visibleIds)的条数。0=全部当前可见。
    /// 勾选集按 id 跨过滤持久(S-13 对齐 Web "跨过滤保持")，用户可能遗忘被搜索/标签隐藏的勾选，
    /// 批量删除/标签前据此提示，避免"看不见的勾选"被误操作。</summary>
    private int HiddenSelectionCount()
    {
        if (_batchSel.Count == 0) return 0;
        var vis = new HashSet<string>(_visibleIds, StringComparer.Ordinal);
        int hidden = 0;
        foreach (var id in _batchSel)
            if (!vis.Contains(id)) hidden++;
        return hidden;
    }
}
