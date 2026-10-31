using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace OBS_Helper.Wpf.Controls;

/// <summary>
/// 把列表内部的鼠标滚轮交给外层页面滚动。
///
/// 为什么需要：页面大量使用 <see cref="ListBox"/> + 外层 <see cref="ScrollViewer"/>，
/// 鼠标停在列表上时滚轮会被列表自己的 ScrollViewer 吃掉，用户会觉得“页面滚不动”。
/// 该附加属性把滚轮事件转发给最近的父级 ScrollViewer，并标记已处理。
/// </summary>
public static class WheelForwarder
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(WheelForwarder), new PropertyMetadata(false, OnEnabledChanged));

    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);
    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;
        if (e.NewValue is true) element.PreviewMouseWheel += OnPreviewMouseWheel;
        else element.PreviewMouseWheel -= OnPreviewMouseWheel;
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var parent = FindParentScrollViewer(sender as DependencyObject);
        if (parent is null) return;
        parent.ScrollToVerticalOffset(parent.VerticalOffset - e.Delta);
        e.Handled = true;
    }

    private static ScrollViewer? FindParentScrollViewer(DependencyObject? child)
    {
        while (child is not null)
        {
            if (child is ScrollViewer viewer) return viewer;
            child = VisualTreeHelper.GetParent(child);
        }
        return null;
    }
}
