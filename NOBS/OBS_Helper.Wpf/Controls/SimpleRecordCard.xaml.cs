using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OBS_Helper.Wpf.Navigation;
using OBS_Helper.Wpf.Services.ObsConfig;
using OBS_Helper.Wpf.Services.Shell;

namespace OBS_Helper.Wpf.Controls;

/// <summary>
/// 简单录像卡（V2.9.4）。
///
/// 交互：选预设 → 看一句「现在能不能录」→ 一键开始（服务层自检 / 备份 / 落地 / 按需拉起 OBS / 开录）
/// → 录制中显示时长与剩余可录 → 停止后给文件并露出「打开目录 / 转成 MP4」。
///
/// 职责边界：本控件只做展示与确认，**不写任何配置**；
/// 写入、备份、回滚、拉起 OBS 全部在 <see cref="SimpleRecordingService"/> 里。
/// </summary>
public partial class SimpleRecordCard : UserControl
{
    private readonly List<(SimplePresetId Id, RadioButton Button)> _presets = new();
    private bool _subscribed;
    private bool _refreshing;

    /// <summary>预设按钮是在哪种语言下建起来的：换语言要整块重建（V2.9.2 口径：切语言即时生效）。</summary>
    private string _presetsLang = "";

    public SimpleRecordCard()
    {
        InitializeComponent();

        // 拉起 OBS 前问一次（V2.9.4）：服务层的 ConfirmLaunch 没被注入时按「不允许」处理，
        // 所以这里必须挂上，否则「OBS 没启动」这条路径会走到「已落地、请自己启动」的提示上。
        AppServices.SimpleRecord.ConfirmLaunch = () => ConfirmDialog.Show(
            Strings.T("simple.launch.title"),
            Strings.T("simple.launch.message"),
            Strings.T("simple.launch.yes"),
            Strings.T("simple.launch.no"),
            danger: false,
            icon: "🎬");
    }

    // ---------------------------------------------------------------- 生命周期

    /// <summary>进入页面时调用：订阅服务事件并做一次只读检查。</summary>
    public async Task RefreshAsync()
    {
        Subscribe();
        BuildPresets();
        // 上次会话遗留的待回滚记录（文件通道落地后关掉程序的情况）
        AppServices.SimpleRecord.LoadPendingRollback();
        await RecheckAsync().ConfigureAwait(true);
        Render();
    }

    /// <summary>离开页面时调用：退订事件，避免页面缓存期间被反复刷新。</summary>
    public void Detach()
    {
        Unsubscribe();
        // 断开确认回调：页面已不可见时不应再弹窗（服务层会按「不允许」处理）
        AppServices.SimpleRecord.ConfirmLaunch = null;
    }

    private void Subscribe()
    {
        if (_subscribed) return;
        AppServices.SimpleRecord.StateChanged += OnServiceChanged;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        AppServices.SimpleRecord.StateChanged -= OnServiceChanged;
        _subscribed = false;
    }

    private void OnServiceChanged() => Dispatcher.BeginInvoke(new Action(Render));

    // ---------------------------------------------------------------- 预设

    private void BuildPresets()
    {
        var current = AppServices.SimpleRecord.Preset;
        var language = Strings.Current;

        // 只更新选中态的前提是「文案没变」：换语言后按钮上的字还是旧的，必须整块重建。
        // 漏了这一步会出现「旧语言的按钮 + 新语言的说明」这种混排（V2.9.4 审查发现）。
        if (_presets.Count == SimpleRecordingCore.Presets.Count &&
            string.Equals(_presetsLang, language, StringComparison.Ordinal))
        {
            foreach (var (id, button) in _presets) button.IsChecked = id == current;
            return;
        }

        _presetsLang = language;
        _presets.Clear();
        PresetPanel.Children.Clear();

        foreach (var preset in SimpleRecordingCore.Presets)
        {
            var radio = new RadioButton
            {
                Style = (Style)FindResource("SegmentButton"),
                GroupName = "SimpleRecordPreset",
                Content = Strings.T("simple.preset." + preset.Key),
                Tag = preset.Id,
                IsChecked = preset.Id == current,
                Margin = new Thickness(0, 0, 8, 0)
            };
            radio.Checked += OnPresetChecked;
            _presets.Add((preset.Id, radio));
            PresetPanel.Children.Add(radio);
        }
    }

    private async void OnPresetChecked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton radio || radio.Tag is not SimplePresetId id) return;
        if (AppServices.SimpleRecord.Preset == id) return;

        AppServices.SimpleRecord.Preset = id;

        // 换预设后要重新读一次「当前值 → 建议值」，否则折叠区还显示上一档的目标值
        await RecheckAsync().ConfigureAwait(true);
        Render();
    }

    // ---------------------------------------------------------------- 刷新

    private async Task RecheckAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            StatusText.Text = Strings.T("simple.check.checking");
            StatusText.Visibility = Visibility.Visible;
            await AppServices.SimpleRecord.CheckAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusText.Text = Strings.T("simple.error.checkFailed", ex.Message);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void Render()
    {
        var svc = AppServices.SimpleRecord;

        try
        {
            RenderPresetDesc();
            RenderCheck(svc);
            RenderButtons(svc);
            RenderItems(svc);
            RenderProgress(svc);
            RenderStatus(svc);
            RenderResult(svc);
            RenderPending(svc);
        }
        catch (Exception ex)
        {
            // 渲染异常不该把整页打挂：降级成一行可读提示
            StatusText.Text = Strings.T("simple.error.checkFailed", ex.Message);
            StatusText.Visibility = Visibility.Visible;
        }
    }

    private void RenderPresetDesc()
        => PresetDescText.Text = Strings.T("simple.preset." + AppServices.SimpleRecord.CurrentPreset.Key + ".desc");

    private void RenderCheck(SimpleRecordingService svc)
    {
        if (svc.IsRecording)
        {
            CheckText.Text = Strings.T("simple.state.recording");
            CheckText.SetResourceReference(TextBlock.ForegroundProperty, "OkBrush");
            HintText.Visibility = Visibility.Collapsed;
            return;
        }

        var ready = svc.Ready;
        if (ready.Ready)
        {
            CheckText.Text = Strings.T("simple.check.ready");
            CheckText.SetResourceReference(TextBlock.ForegroundProperty, "OkBrush");
        }
        else
        {
            CheckText.Text = Strings.T("simple.check.blocked");
            CheckText.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
        }

        // 阻断时：先给具体原因（哪一件事挡住了），再补一句通用出口。
        // 通用出口那句（「不会替你静默改配置或重启 OBS」）是功能承诺，
        // 只在文案表里躺着而界面上不显示，等于没承诺。
        var parts = new List<string>();
        var reason = ready.Ready ? ready.Hint : ready.BlockReason;
        if (!string.IsNullOrWhiteSpace(reason)) parts.Add(reason!);
        if (!ready.Ready) parts.Add(Strings.T("simple.check.hint"));

        if (parts.Count == 0)
        {
            HintText.Visibility = Visibility.Collapsed;
        }
        else
        {
            HintText.Text = string.Join(" ", parts);
            HintText.Visibility = Visibility.Visible;
        }
    }

    private void RenderButtons(SimpleRecordingService svc)
    {
        var recording = svc.IsRecording;
        var busy = svc.Busy;

        StartButton.Visibility = recording ? Visibility.Collapsed : Visibility.Visible;
        StopButton.Visibility = recording ? Visibility.Visible : Visibility.Collapsed;

        // 就绪判定失败时仍允许点开始：用户可能刚在 OBS 里改好设置，
        // 由服务层再检查一次并给出具体原因，比一个禁用的按钮更有信息量。
        StartButton.IsEnabled = !busy && !recording;
        StopButton.IsEnabled = !busy && recording;
        RescanButton.IsEnabled = !busy;
        RollbackButton.Visibility = svc.HasFileRollback ? Visibility.Visible : Visibility.Collapsed;

        StartButton.Content = busy && !recording ? Strings.T("simple.starting")
            : recording ? Strings.T("simple.stop")
            : Strings.T("simple.start");
        StopButton.Content = busy ? Strings.T("simple.stopping") : Strings.T("simple.stop");
    }

    private void RenderItems(SimpleRecordingService svc)
    {
        var plan = svc.Plan;
        if (plan is null) return;

        // 每次重建：预设一换，目标值就变了；页面缓存的旧控件树必须丢掉
        ItemsPanel.Children.Clear();

        foreach (var item in plan.Items)
        {
            // 文件通道写不了采样率：不列出来，避免「勾了没生效」这种最让人困惑的结果
            if (!plan.ViaWebSocket && item.WebSocketOnly) continue;

            var current = item.Current.Length == 0 ? "-" : item.Current;

            // 取文案：Strings.T 在缺键时会**原样返回键名**，所以不能只判「非空」——
            // 那样兜底分支永远进不去，界面上直接显示 env.item.xxx.label（V2.9.4 审查发现的真实缺陷）。
            var locKey = "env.item." + item.Key + ".label";
            var label = Strings.T(locKey);
            var line = string.Equals(label, locKey, StringComparison.Ordinal) ? item.Key : label;

            var text = new TextBlock
            {
                Text = Strings.T("simple.summary.line", line, current, item.Recommended),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4)
            };
            text.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeXs");
            text.SetResourceReference(TextBlock.ForegroundProperty, item.AlreadyOk ? "MutedBrush" : "TextBrush");
            ItemsPanel.Children.Add(text);
        }
    }

    private void RenderProgress(SimpleRecordingService svc)
    {
        var progress = svc.Progress;
        if (progress is null)
        {
            InfoPanel.Visibility = Visibility.Collapsed;
            return;
        }

        InfoPanel.Visibility = Visibility.Visible;

        var unknown = Strings.T("simple.record.unknown");
        ElapsedText.Text = Strings.T("simple.record.elapsed")
            + SimpleRecordingCore.FormatDuration(progress.Elapsed);

        // 单位串走文案表：硬编码 " min / " + " GB" 会让英文界面出现中英混排，
        // 而且中英各自的标点习惯（全角/半角）也不同（V2.9.4 审查发现）。
        RemainingText.Text = Strings.T("simple.record.remaining")
            + (progress.FreeGb > 0
                ? Strings.T(progress.FromMeasuredRate
                        ? "simple.record.remainingValueMeasured"
                        : "simple.record.remainingValue",
                    progress.RemainingMinutes.ToString("0"), progress.FreeGb.ToString("0.#"))
                : unknown);
        RemainingText.SetResourceReference(TextBlock.ForegroundProperty,
            progress.LowSpace ? "WarnBrush" : "MutedBrush");

        var file = AppServices.Obs.LastRecordFile;
        FileSizeText.Text = Strings.T("simple.record.fileSize")
            + (string.IsNullOrWhiteSpace(file)
                ? unknown
                : Strings.T("simple.record.fileSizeValue", file, progress.UsedMb.ToString("0.#")));
    }

    private void RenderStatus(SimpleRecordingService svc)
    {
        if (svc.Busy && svc.StatusText.Length > 0)
        {
            StatusText.Text = svc.StatusText;
            StatusText.Visibility = Visibility.Visible;
            return;
        }

        if (svc.LastError is { Length: > 0 } error)
        {
            StatusText.Text = error;
            StatusText.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
            StatusText.Visibility = Visibility.Visible;
            return;
        }

        if (svc.StatusText.Length > 0)
        {
            StatusText.Text = svc.StatusText;
            StatusText.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            StatusText.Visibility = Visibility.Visible;
            return;
        }

        StatusText.Visibility = Visibility.Collapsed;
    }

    private void RenderResult(SimpleRecordingService svc)
    {
        ResultPanel.Children.Clear();

        if (svc.Step != SimpleRecordingStep.Stopped) return;

        if (!svc.LastStopProducedFile)
        {
            AddResultLine(Strings.T("simple.stopped.noFile"), "WarnBrush");
            RemuxButton.Visibility = Visibility.Collapsed;
            return;
        }

        AddResultLine(Strings.T("simple.stopped.message",
            svc.LastOutputFile ?? "", svc.LastOutputMb.ToString("0.#") + " MB"), "TextBrush");
        RemuxButton.Visibility = Visibility.Visible;
    }

    /// <summary>上次会话遗留的待回滚记录：有就显示提示与两个按钮，没有就整块收起。</summary>
    private void RenderPending(SimpleRecordingService svc)
    {
        var notice = svc.PendingRollbackNotice();
        if (notice is null)
        {
            PendingPanel.Visibility = Visibility.Collapsed;
            return;
        }

        PendingText.Text = notice;
        PendingPanel.Visibility = Visibility.Visible;
    }

    private void AddResultLine(string text, string brushKey)
    {
        var line = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 3) };
        line.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSm");
        line.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        ResultPanel.Children.Add(line);
    }

    // ---------------------------------------------------------------- 动作

    private async void OnStartClick(object sender, RoutedEventArgs e)
    {
        StartButton.IsEnabled = false;
        try
        {
            var progress = new Progress<string>(s =>
            {
                StatusText.Text = s;
                StatusText.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                StatusText.Visibility = Visibility.Visible;
            });
            var ok = await AppServices.SimpleRecord.StartAsync(progress).ConfigureAwait(true);
            if (ok)
                AppServices.Toast.Show(Strings.T("simple.state.recording"), "ok");
        }
        catch (Exception ex)
        {
            AppServices.Toast.Show(Strings.T("simple.error.startFailed", ex.Message), "error");
        }
        finally
        {
            Render();
        }
    }

    private async void OnStopClick(object sender, RoutedEventArgs e)
    {
        StopButton.IsEnabled = false;
        try
        {
            await AppServices.SimpleRecord.StopAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AppServices.Toast.Show(Strings.T("simple.error.stopFailed", ex.Message), "error");
        }
        finally
        {
            Render();
        }
    }

    private async void OnRescanClick(object sender, RoutedEventArgs e)
    {
        await RecheckAsync().ConfigureAwait(true);
        Render();
    }

    private void OnOpenDirClick(object sender, RoutedEventArgs e)
    {
        var svc = AppServices.SimpleRecord;
        var dir = svc.LastOutputFile is { Length: > 0 } file
            ? System.IO.Path.GetDirectoryName(file)
            : null;

        if (dir is null)
        {
            // 没有本次文件（例如刚打开卡片）：退回 OBS 配置里的录像目录
            _ = OpenConfiguredDirAsync();
            return;
        }

        var error = SimpleRecordingService.OpenRecordingFolder(dir);
        if (error is not null) AppServices.Toast.Show(error, "error");
    }

    private async Task OpenConfiguredDirAsync()
    {
        try
        {
            var dir = await AppServices.SimpleRecord.GetRecordingDirAsync().ConfigureAwait(true);
            var error = SimpleRecordingService.OpenRecordingFolder(dir);
            if (error is not null) AppServices.Toast.Show(error, "error");
        }
        catch (Exception ex)
        {
            AppServices.Toast.Show(Strings.T("recording.dir.openFailed", ex.Message), "error");
        }
    }

    private async void OnRemuxClick(object sender, RoutedEventArgs e)
    {
        RemuxButton.IsEnabled = false;
        try
        {
            var (ok, message) = await AppServices.SimpleRecord.RemuxLastAsync().ConfigureAwait(true);
            AppServices.Toast.Show(message, ok ? "ok" : "error");
        }
        catch (Exception ex)
        {
            AppServices.Toast.Show(Strings.T("recording.remux.exception", ex.Message), "error");
        }
        finally
        {
            RemuxButton.IsEnabled = true;
        }
    }

    private async void OnRollbackClick(object sender, RoutedEventArgs e)
    {
        RollbackButton.IsEnabled = false;
        try
        {
            var ok = await AppServices.SimpleRecord.RollbackFileChangesAsync().ConfigureAwait(true);
            var message = ok
                ? Strings.T("env.rollback.ok")
                : AppServices.SimpleRecord.LastError ?? Strings.T("env.applied.partial");
            AppServices.Toast.Show(message, ok ? "ok" : "error");
        }
        finally
        {
            RollbackButton.IsEnabled = true;
            Render();
        }
    }

    private void OnAdvancedClick(object sender, RoutedEventArgs e)
        => AppServices.Navigation.Navigate(Routes.Setup);

    // ---------------------------------------------------------------- 跨会话回滚

    private async void OnRestorePendingClick(object sender, RoutedEventArgs e)
    {
        PendingRestoreButton.IsEnabled = false;
        try
        {
            var ok = await AppServices.SimpleRecord.RestorePendingRollbackAsync().ConfigureAwait(true);
            AppServices.Toast.Show(
                ok ? Strings.T("simple.pending.restored")
                   : AppServices.SimpleRecord.LastError ?? Strings.T("env.applied.partial"),
                ok ? "ok" : "error");
        }
        finally
        {
            PendingRestoreButton.IsEnabled = true;
            Render();
        }
    }

    private void OnDiscardPendingClick(object sender, RoutedEventArgs e)
    {
        AppServices.SimpleRecord.DiscardPendingRollback();
        AppServices.Toast.Show(Strings.T("simple.pending.discarded"), "ok");
        Render();
    }
}
