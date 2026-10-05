using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OBS_Helper.Wpf.Services.ObsConfig;

namespace OBS_Helper.Wpf.Controls;

/// <summary>
/// 一键部署录制环境卡（V2.9.3）。
///
/// 交互：进页面读一次当前配置 → 逐项列出「当前 → 建议」（已经符合的标「已是建议值」，
/// 其余默认勾选）→ 点「应用勾选项」先自动备份再落地 → 底部给逐项结果，并露出「回滚」按钮。
///
/// 两条落地通道由 <see cref="RecordingEnvService.BuildPlanAsync"/> 决定，卡片只负责显示：
/// 连上 OBS 走 obs-websocket（即时生效），没连但 OBS 没跑则改 basic.ini（重启生效），
/// 两者都不行时把原因原样显示出来，不让用户对着一个点不动的按钮猜。
/// </summary>
public partial class RecordingEnvCard : UserControl
{
    private readonly List<(RecordingEnvItem Item, CheckBox Box)> _rows = new();
    private RecordingEnvPlan? _plan;

    /// <summary>上一次成功应用时记下的旧值（用于回滚）。为空表示本次会话还没应用过。</summary>
    private IReadOnlyList<RecordingRollbackEntry> _lastRollback = Array.Empty<RecordingRollbackEntry>();

    public RecordingEnvCard()
    {
        InitializeComponent();
    }

    /// <summary>重新检测并重建列表（每次进入搭建页都会调一次，语言切换后也自然刷新）。</summary>
    public async Task RefreshAsync()
    {
        ItemsPanel.Children.Clear();
        ResultPanel.Children.Clear();
        _rows.Clear();
        HideProgress();

        try
        {
            _plan = await AppServices.RecordingEnv.BuildPlanAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ShowProgress(ex.Message, error: true);
            return;
        }

        if (_plan.BlockedReason is { } blocked)
        {
            ChannelText.Text = blocked;
            ApplyButton.IsEnabled = false;
            return;
        }

        ApplyButton.IsEnabled = true;
        ChannelText.Text = _plan.ViaWebSocket ? Strings.T("env.viaWs") : Strings.T("env.viaFile");
        if (!_plan.ViaWebSocket) ChannelText.Text += "  " + Strings.T("env.fileNote");

        foreach (var item in _plan.Items)
        {
            // 文件通道写不了采样率：直接不列出来，避免「勾了却没落地」这种最让人困惑的结果。
            if (!_plan.ViaWebSocket && item.WebSocketOnly) continue;
            ItemsPanel.Children.Add(BuildRow(item));
        }
    }

    // ---------------------------------------------------------------- 单行

    private UIElement BuildRow(RecordingEnvItem item)
    {
        var check = new CheckBox
        {
            IsChecked = !item.AlreadyOk,
            IsEnabled = !item.AlreadyOk,
            VerticalAlignment = VerticalAlignment.Center,
            Content = Strings.T("env.item." + item.Key + ".label"),
            MinWidth = 150,
        };

        var current = MakeText(Display(item.Current), "FontSizeSm", "MutedBrush");
        var arrow = MakeText("→", "FontSizeSm", "MutedBrush");
        arrow.Margin = new Thickness(8, 0, 8, 0);
        var recommended = MakeText(item.Recommended, "FontSizeSm", "TextBrush");

        var badge = MakeText(
            Strings.T(item.AlreadyOk ? "env.okBadge" : "env.changeBadge"),
            "FontSizeXs",
            item.AlreadyOk ? "OkBrush" : "WarnBrush");
        badge.Margin = new Thickness(10, 0, 0, 0);

        var top = new WrapPanel();
        top.Children.Add(check);
        top.Children.Add(current);
        top.Children.Add(arrow);
        top.Children.Add(recommended);
        top.Children.Add(badge);

        var reason = Strings.T("env.item." + item.Key + ".reason");
        var reasonText = MakeText(reason, "FontSizeXs", "MutedBrush");
        reasonText.TextWrapping = TextWrapping.Wrap;
        reasonText.Margin = new Thickness(22, 2, 0, 0);

        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        panel.Children.Add(top);
        panel.Children.Add(reasonText);

        _rows.Add((item, check));
        return panel;
    }

    private static string Display(string value) => value.Length == 0 ? "-" : value;

    private TextBlock MakeText(string text, string fontSizeKey, string brushKey)
    {
        var t = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(TextBlock.FontSizeProperty, fontSizeKey);
        t.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        return t;
    }

    // ---------------------------------------------------------------- 应用 / 回滚

    private async void OnApply(object sender, RoutedEventArgs e)
    {
        if (_plan is null) return;

        var selected = _rows.Where(r => r.Box.IsChecked == true).Select(r => r.Item.Key).ToList();
        if (selected.Count == 0)
        {
            ShowProgress(Strings.T("env.nothingSelected"), error: true);
            return;
        }

        ApplyButton.IsEnabled = false;
        ResultPanel.Children.Clear();
        ShowProgress(Strings.T("env.applying"));

        try
        {
            var progress = new Progress<string>(s => ShowProgress(s));
            var result = await AppServices.RecordingEnv.ApplyAsync(_plan, selected, progress).ConfigureAwait(true);

            _lastRollback = result.Rollback;
            RollbackButton.Visibility = result.Rollback.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            // 先重新读一遍配置（RefreshAsync 会清空结果区），**再**画结果 ——
            // 反过来的话刚渲染的逐项结果会被下一次刷新清掉，用户只看得到一句总结。
            if (result.Ok) await RefreshAsync().ConfigureAwait(true);

            ShowProgress(BuildSummary(result), error: !result.Ok);
            RenderSteps(result);
        }
        catch (Exception ex)
        {
            ShowProgress(ex.Message, error: true);
        }
        finally
        {
            ApplyButton.IsEnabled = true;
        }
    }

    private async void OnRollback(object sender, RoutedEventArgs e)
    {
        if (_lastRollback.Count == 0) return;

        RollbackButton.IsEnabled = false;
        ResultPanel.Children.Clear();
        try
        {
            var progress = new Progress<string>(s => ShowProgress(s));
            var result = await AppServices.RecordingEnv.RollbackAsync(_lastRollback, progress).ConfigureAwait(true);
            ShowProgress(BuildSummary(result), error: !result.Ok);
            if (result.Ok)
            {
                _lastRollback = Array.Empty<RecordingRollbackEntry>();
                RollbackButton.Visibility = Visibility.Collapsed;
                await RefreshAsync().ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            ShowProgress(ex.Message, error: true);
        }
        finally
        {
            RollbackButton.IsEnabled = true;
        }
    }

    private async void OnRescan(object sender, RoutedEventArgs e) => await RefreshAsync().ConfigureAwait(true);

    // ---------------------------------------------------------------- 结果展示

    private static string BuildSummary(RecordingEnvResult result)
    {
        var text = result.Message;
        if (result.BackupPath is { Length: > 0 } p) text += "  " + p;
        if (result.Error is { Length: > 0 } err) text += "  " + err;
        return text;
    }

    private void RenderSteps(RecordingEnvResult result)
    {
        foreach (var step in result.Steps)
        {
            var line = (step.Ok ? "✓ " : "✗ ") + StepLabel(step.Key)
                       + (step.Error is { Length: > 0 } e ? " — " + e : "");
            ResultPanel.Children.Add(MakeText(line, "FontSizeXs", step.Ok ? "OkBrush" : "DangerBrush"));
        }
    }

    /// <summary>
    /// 把结果条目里的键转成展示名：推荐项用文案键，**回滚条目里放的是 INI 参数名**
    /// （如 BaseCX），取不到文案时直接显示参数名，而不是把 <c>env.item.BaseCX.label</c>
    /// 这种半成品键名糊到界面上。
    /// </summary>
    private static string StepLabel(string key)
    {
        if (key.Length == 0) return "";
        var locKey = "env.item." + key + ".label";
        var text = Strings.T(locKey);
        return string.Equals(text, locKey, StringComparison.Ordinal) ? key : text;
    }

    private void ShowProgress(string text, bool error = false)
    {
        ProgressText.Text = text;
        ProgressText.Visibility = Visibility.Visible;
        ProgressText.SetResourceReference(TextBlock.ForegroundProperty, error ? "DangerBrush" : "MutedBrush");
    }

    private void HideProgress()
    {
        ProgressText.Visibility = Visibility.Collapsed;
        ProgressText.Text = "";
    }
}
