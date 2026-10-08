// Controls/Combo.cs - ComboBox 模板圆角附加属性
// WPF 的 ComboBox 没有 CornerRadius 属性，自定义 ControlTemplate 里只能把圆角写死，
// 导致「编辑弹窗 r-9（.edit-sec select）」与「存入弹窗 r-10（.paste-adv .adv-row select）」
// 两种规格无法共存于同一个 DarkCombo 样式，除非复制整份模板。
// 此附加属性让模板内的 Border 通过 RelativeSource TemplatedParent 读取实例级圆角，
// 样式里给默认值（RadiusBtnSm=10），个别弹窗用令牌覆盖（RadiusCombo=9）。
// 用法：<ComboBox Style="{StaticResource DarkCombo}" local:Combo.Radius="{StaticResource RadiusCombo}"/>
using System.Windows;

namespace ClipboardExe.Controls;

public static class Combo
{
    public static readonly DependencyProperty RadiusProperty =
        DependencyProperty.RegisterAttached("Radius", typeof(CornerRadius), typeof(Combo),
            new FrameworkPropertyMetadata(new CornerRadius(0), FrameworkPropertyMetadataOptions.Inherits));

    public static void SetRadius(DependencyObject d, CornerRadius v) => d.SetValue(RadiusProperty, v);
    public static CornerRadius GetRadius(DependencyObject d) => (CornerRadius)d.GetValue(RadiusProperty);
}
