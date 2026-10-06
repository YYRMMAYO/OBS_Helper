using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace OBS_Helper.Wpf.Services;

/// <summary>一条已弹出的轻提示（供「最近提示」回顾与支持报告使用）。</summary>
public sealed record ToastRecord(DateTime LocalTime, string Severity, string Message);

/// <summary>
/// 统一轻提示 Toast（P0「成功反馈不对称」）。
///
/// V3.0（C7）把它从「2.5 秒后一律消失」改成按**重要程度**决定停留时间，并补了三条：
/// <list type="bullet">
///   <item><b>警告 / 错误停留更久</b>（6 / 10 秒）：这两类是「用户必须知道」的失败反馈，
///         而 Toast 常常是它们的唯一出口（例如「开录失败」），2.5 秒看漏就永久丢失；</item>
///   <item><b>鼠标悬停暂停消失</b>：正在读的时候不该被清掉（离开后给 2 秒缓冲）；</item>
///   <item><b>点击立刻关掉 + 保留最近 30 条历史</b>：历史会进「诊断报告」的导出内容，
///         用户报障时说「刚才弹了句什么」比复述界面状态可靠得多。</item>
/// </list>
///
/// 线程安全：非 UI 线程调用自动投递到 UI 线程（StateChanged 可能来自 WebSocket 线程）。
/// </summary>
public sealed class ToastService
{
    /// <summary>各重要程度的停留时长。ok/info 保持原来的短停留，避免刷屏。</summary>
    private static readonly TimeSpan LifeOk = TimeSpan.FromSeconds(2.5);
    private static readonly TimeSpan LifeWarn = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan LifeDanger = TimeSpan.FromSeconds(10);

    /// <summary>鼠标离开后额外给的缓冲时间（正在读的时候不该立刻被清掉）。</summary>
    private static readonly TimeSpan HoverGrace = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan FadeOut = TimeSpan.FromMilliseconds(200);

    /// <summary>「最近提示」最多保留多少条（够报障回溯，也不会长成大日志）。</summary>
    private const int MaxHistory = 30;

    private sealed class ToastItem
    {
        public required ContentControl Card { get; init; }
        public DateTime Deadline { get; set; }
        public bool Paused { get; set; }
    }

    private readonly StackPanel _host;
    private readonly DispatcherTimer _timer;
    private readonly List<ToastItem> _items = new();
    private readonly List<ToastRecord> _history = new();

    public ToastService(StackPanel host)
    {
        _host = host;
        // 统一用一个短周期计时器扫描到期项：每张卡的停留时长不同，逐个开计时器会在
        // 「连弹几条时留下多个常驻计时器」（本项目刚清理过这类问题）。
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += OnTick;
    }

    /// <summary>最近弹出的提示，最新的在前（供支持报告 / 回顾）。</summary>
    public IReadOnlyList<ToastRecord> Recent => _history;

    /// <summary>弹出一条轻提示。severity: ok / info / warn / danger（决定左侧状态点颜色与停留时长）。</summary>
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
            Cursor = Cursors.Hand,
            ToolTip = Strings.T("toast.clickToDismiss"),
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }
        };
        _host.Children.Add(card);

        var item = new ToastItem { Card = card, Deadline = DateTime.UtcNow + LifetimeOf(severity) };
        _items.Add(item);

        // 悬停暂停 + 点击关闭：把「什么时候消失」的控制权还给用户
        card.MouseEnter += (_, _) => item.Paused = true;
        card.MouseLeave += (_, _) =>
        {
            item.Paused = false;
            item.Deadline = DateTime.UtcNow + HoverGrace;
        };
        card.MouseLeftButtonUp += (_, _) => Remove(item);

        AddHistory(severity, message);

        // V3.0 无障碍：光在样式里声明 AutomationProperties.LiveSetting 是**不够的** ——
        // 微软的无障碍指南明确要求：实时区域必须在代码里显式抛一次 LiveRegionChanged 事件，
        // 否则读屏不会播报新出现的 Toast（而 Toast 是「已连接 OBS」「开录失败」这类反馈的唯一出口）。
        RaiseLiveRegionChanged(card);

        if (!_timer.IsEnabled) _timer.Start();
    }

    private static TimeSpan LifetimeOf(string severity) => severity switch
    {
        "danger" => LifeDanger,
        "warn" => LifeWarn,
        _ => LifeOk
    };

    private void AddHistory(string severity, string message)
    {
        _history.Insert(0, new ToastRecord(DateTime.Now, severity, message));
        if (_history.Count > MaxHistory) _history.RemoveRange(MaxHistory, _history.Count - MaxHistory);
    }

    /// <summary>把卡片作为实时区域通告给读屏（失败不影响提示本身）。</summary>
    private static void RaiseLiveRegionChanged(UIElement card)
    {
        try
        {
            var peer = UIElementAutomationPeer.CreatePeerForElement(card)
                       ?? new FrameworkElementAutomationPeer((FrameworkElement)card);
            peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
        catch (Exception)
        {
            // 无障碍通告失败不能影响提示本身
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;

        // 倒序遍历：Remove 会改动集合
        for (var i = _items.Count - 1; i >= 0; i--)
        {
            var item = _items[i];
            if (!item.Paused && now >= item.Deadline) Remove(item);
        }

        if (_items.Count == 0) _timer.Stop();
    }

    private void Remove(ToastItem item)
    {
        if (!_items.Remove(item)) return;

        var card = item.Card;
        var active = _host.Children.Contains(card);
        if (!active) return;

        if (Application.Current?.Resources["ReduceMotion"] is bool reduce && reduce)
        {
            _host.Children.Remove(card);
        }
        else
        {
            var fade = new DoubleAnimation
            {
                To = 0,
                Duration = FadeOut,
                FillBehavior = FillBehavior.Stop
            };
            fade.Completed += (_, _) => _host.Children.Remove(card);
            card.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        if (_items.Count == 0) _timer.Stop();
    }
}
