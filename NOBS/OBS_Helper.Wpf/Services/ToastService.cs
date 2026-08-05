using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace OBS_Helper.Wpf.Services;

/// <summary>
/// 统一轻提示 Toast（P0「成功反馈不对称」）。
/// 消息卡片贴底堆叠，2.5s 后淡出移除（ReduceMotion 开启时直接移除，不播放动画）。
/// 仅用于「无内联反馈」的全局事件（连接成功、场景切换、定时停止生效等），
/// 页面本身已有内联结果栏的操作不重复弹，避免双反馈。
/// 线程安全：非 UI 线程调用自动投递到 UI 线程（StateChanged 可能来自 WebSocket 线程）。
/// </summary>
public sealed class ToastService
{
    private static readonly TimeSpan LifeTime = TimeSpan.FromSeconds(2.5);
    private static readonly TimeSpan FadeOut = TimeSpan.FromMilliseconds(200);

    private readonly StackPanel _host;
    private readonly DispatcherTimer _timer;

    public ToastService(StackPanel host)
    {
        _host = host;
        _timer = new DispatcherTimer { Interval = LifeTime };
        _timer.Tick += OnTick;
    }

    /// <summary>弹出一条轻提示。severity: ok / info / warn / danger（决定左侧状态点颜色）。</summary>
    public void Show(string message, string severity = "ok")
    {
        if (!_host.Dispatcher.CheckAccess())
        {
            _host.Dispatcher.BeginInvoke(() => Show(message, severity));
            return;
        }

        var card = new ContentControl
        {
            Style = (Style)_host.TryFindResource("ToastCard")!,
            Tag = severity,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }
        };
        _host.Children.Add(card);
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        // 每次到点只移除最旧的一张卡片；栈空即停表
        if (_host.Children.Count == 0)
        {
            _timer.Stop();
            return;
        }

        var card = (ContentControl)_host.Children[0];
        if (Application.Current?.Resources["ReduceMotion"] is bool reduce && reduce)
        {
            Remove(card);
            return;
        }

        var fade = new DoubleAnimation
        {
            To = 0,
            Duration = FadeOut,
            FillBehavior = FillBehavior.Stop
        };
        fade.Completed += (_, _) => Remove(card);
        card.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private void Remove(ContentControl card)
    {
        _host.Children.Remove(card);
        if (_host.Children.Count == 0) _timer.Stop();
    }
}
