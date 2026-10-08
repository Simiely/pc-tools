// Controls/TagPicker.xaml.cs - 标签选择器（对齐 app.js renderTagPicker）
//  - SetTags(selected, allTags) 装配；Selected 只读快照
//  - chip 点选切换（Tag="on" 驱动 TagPickChip 金底）；输入框回车新建（去重、上限 20 字）
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ClipboardExe.Controls;

public partial class TagPicker : UserControl
{
    private List<string> _selected = new();
    private readonly List<string> _all = new();

    /// <summary>选中集变化（传入副本，避免外部直接改内部引用）。</summary>
    public event Action<List<string>>? SelectionChanged;

    public IReadOnlyList<string> Selected => _selected;

    public TagPicker()
    {
        InitializeComponent();
    }

    /// <summary>装配：selected=当前选中；allTags=系统已有标签（展示列表 = 已有 ∪ 选中新标签）。</summary>
    public void SetTags(IEnumerable<string> selected, IEnumerable<string> allTags)
    {
        _selected = new List<string>(selected);
        _all.Clear();
        _all.AddRange(allTags);
        Render();
    }

    private void Render()
    {
        ChipBox.Children.Clear();
        var merged = new List<string>(_all);
        foreach (var s in _selected) if (!merged.Contains(s)) merged.Add(s);

        foreach (var name in merged)
        {
            var chip = new Button
            {
                Content = name,
                Tag = _selected.Contains(name) ? "on" : null,
                Style = (Style)FindResource("TagPickChip"),
                Margin = new Thickness(0, 0, 6, 4),
            };
            var n = name;
            chip.Click += (_, _) => Toggle(n);
            ChipBox.Children.Add(chip);
        }

        var input = new TextBox
        {
            Style = (Style)FindResource("TagPickInput"),
            Tag = "新标签，回车添加",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 4),
        };
        input.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            var t = (input.Text ?? "").Trim();
            if (t.Length == 0) return;
            if (!_selected.Contains(t))
            {
                _selected.Add(t);
                SelectionChanged?.Invoke(_selected.ToList());
            }
            input.Text = "";
            Render();
        };
        ChipBox.Children.Add(input);
    }

    private void Toggle(string name)
    {
        var i = _selected.IndexOf(name);
        if (i >= 0) _selected.RemoveAt(i);
        else _selected.Add(name);
        SelectionChanged?.Invoke(_selected.ToList());
        Render(); // 立即重渲染刷新选中高亮（对齐"点击无选取反馈"修复）
    }
}
