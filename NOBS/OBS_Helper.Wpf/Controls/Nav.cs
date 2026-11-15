using System.Windows;

namespace OBS_Helper.Wpf.Controls;

/// <summary>
/// 导航项附加属性（V3.7.2）。
///
/// C · 排版手册用「01–16 编号目录」代替图标导航；编号来自这里，而不是让 16 个 RadioButton
/// 各自手写 TextBlock。这样 A/C 共用同一份导航数据，路由与选中态不会漂移；
/// 编号只是目录序号，不是快捷键（读屏名称里带「第 N 项」，见 MainWindow.RefreshNavAccessibility）。
/// </summary>
public static class Nav
{
    /// <summary>导航项在目录中的序号（1 基，跨分组连续）。0 表示不参与编号。</summary>
    public static readonly DependencyProperty NumberProperty = DependencyProperty.RegisterAttached(
        "Number", typeof(int), typeof(Nav), new PropertyMetadata(0));

    public static int GetNumber(DependencyObject element) =>
        (int)element.GetValue(NumberProperty);

    public static void SetNumber(DependencyObject element, int value) =>
        element.SetValue(NumberProperty, value);
}
