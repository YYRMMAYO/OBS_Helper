using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace OBS_Helper.Wpf.Views;

/// <summary>
/// 启动动画（V3.5.0）：深色圆角面板 + 品牌图标 + 进度线，显示约 1 秒后淡出。
/// 不阻塞主窗口创建；自检 / 无界面模式不显示。
/// </summary>
public partial class SplashWindow : Window
{
    private readonly DispatcherTimer _timer;

    public SplashWindow()
    {
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(850) };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            FadeOut();
        };

        Loaded += (_, _) =>
        {
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            Root.BeginAnimation(OpacityProperty, fade);

            var grow = new DoubleAnimation(0, 480, TimeSpan.FromMilliseconds(820))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Progress.BeginAnimation(WidthProperty, grow);
            _timer.Start();
        };
    }

    private void FadeOut()
    {
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        fade.Completed += (_, _) =>
        {
            try { Close(); } catch { /* 退出途中忽略 */ }
        };
        Root.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>主窗口已经显示时调用：最多再停留 <paramref name="delay"/> 后淡出。</summary>
    public void CloseAfter(TimeSpan delay)
    {
        var timer = new DispatcherTimer { Interval = delay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            FadeOut();
        };
        timer.Start();
    }
}
