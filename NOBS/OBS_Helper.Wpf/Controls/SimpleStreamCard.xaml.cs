using System.Windows;
using System.Windows.Controls;
using OBS_Helper.Wpf.Services;
using OBS_Helper.Wpf.Services.ObsConfig;
using OBS_Helper.Wpf.Services.Shell;

namespace OBS_Helper.Wpf.Controls;

/// <summary>
/// 首页「简单开播」卡片（V3.0 / D2）。
///
/// 与「简单录像」卡片同一套生命周期约定：<see cref="RefreshAsync"/> 在进入页面时调用（订阅 + 只读检查），
/// <see cref="Detach"/> 在离开时调用（退订）—— 页面实例被导航缓存复用，不这样做会在缓存期间被反复刷新。
/// </summary>
public partial class SimpleStreamCard : UserControl
{
    private bool _subscribed;
    private bool _busy;

    public SimpleStreamCard()
    {
        InitializeComponent();
    }

    /// <summary>进入页面时调用：订阅服务事件、回填开关、做一次只读检查。</summary>
    public async Task RefreshAsync()
    {
        Subscribe();
        AlsoRecordCheck.IsChecked = AppServices.SimpleStream.AlsoRecord;
        await AppServices.SimpleStream.CheckAsync().ConfigureAwait(true);
        Render();
    }

    /// <summary>离开页面时调用：退订事件。</summary>
    public void Detach() => Unsubscribe();

    private void Subscribe()
    {
        if (_subscribed) return;
        AppServices.SimpleStream.StateChanged += OnServiceChanged;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        AppServices.SimpleStream.StateChanged -= OnServiceChanged;
        _subscribed = false;
    }

    private void OnServiceChanged()
    {
        // 服务事件可能来自 WebSocket / 计时器线程，切回 UI 线程再刷界面
        try
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(Render));
        }
        catch (Exception)
        {
            // 退出途中 Dispatcher 已关闭，忽略
        }
    }

    private void OnAlsoRecordChanged(object sender, RoutedEventArgs e)
    {
        // 用户没点开播时也只是记住偏好（服务不会因此开始录制）
        AppServices.SimpleStream.AlsoRecord = AlsoRecordCheck.IsChecked == true;
    }

    private async void OnGoClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        GoButton.IsEnabled = false;
        try
        {
            var svc = AppServices.SimpleStream;
            if (SimpleStreamingCore.IsStopAction(svc.Step))
                await svc.StopAsync();
            else
                await svc.StartAsync();
        }
        catch (Exception ex)
        {
            // 服务层自身不抛异常（失败写进 LastError），这里兜住意外，避免整个页面崩掉
            FileLogger.Warn("SimpleStreamCard", $"开播/停播操作异常：{ex.Message}");
            AppServices.Toast?.Show(Strings.T("simple.stream.state.startFailed", ex.Message));
        }
        finally
        {
            _busy = false;
            Render();
        }
    }

    /// <summary>按服务当前状态刷新文案、按钮与指标。</summary>
    private void Render()
    {
        var svc = AppServices.SimpleStream;

        CheckText.Text = svc.StatusText.Length > 0 ? svc.StatusText : Strings.T("simple.stream.blocked.unknown");

        if (!string.IsNullOrEmpty(svc.WarningText))
        {
            WarnText.Text = "⚠️ " + svc.WarningText;
            WarnText.Visibility = Visibility.Visible;
        }
        else
        {
            WarnText.Visibility = Visibility.Collapsed;
        }

        RenderMetrics(svc);

        var stopping = SimpleStreamingCore.IsStopAction(svc.Step);
        GoButton.Content = Strings.T(stopping ? "simple.stream.action.stop" : "simple.stream.action.start");

        // 停止按钮永远可用（正在推流时必须能停）；开始按钮在明确不可播时禁用，
        // 但**不因为「还没检查」而禁用** —— 让用户可以随时点，服务层会先检查再说原因。
        GoButton.IsEnabled = !_busy && (stopping || svc.Step != SimpleStreamStep.Blocked);
    }

    private void RenderMetrics(SimpleStreamingService svc)
    {
        if (!svc.IsStreaming)
        {
            MetricText.Visibility = Visibility.Collapsed;
            return;
        }

        var parts = new List<string> { Strings.T("simple.stream.metric.elapsed", SimpleStreamingCore.FormatDuration(svc.Elapsed)) };

        var kbps = svc.CurrentKbps;
        if (kbps > 0) parts.Add(Strings.T("simple.stream.metric.kbps", kbps));

        var status = AppServices.Obs.StreamStatus;
        if (status.TotalFrames > 0)
            parts.Add(Strings.T("simple.stream.metric.dropped", (double)status.SkippedFrames / status.TotalFrames * 100));
        if (status.Reconnecting) parts.Add(Strings.T("simple.stream.metric.reconnecting"));

        MetricText.Text = string.Join(" · ", parts);
        MetricText.Visibility = Visibility.Visible;
    }
}
