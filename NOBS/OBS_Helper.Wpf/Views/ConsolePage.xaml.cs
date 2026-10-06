using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using OBS_Helper.Wpf.Controls;
using OBS_Helper.Wpf.Models.Obs;
using OBS_Helper.Wpf.Navigation;
using OBS_Helper.Wpf.Services;
using OBS_Helper.Wpf.Services.Obs;
using OBS_Helper.Wpf.Services.Recording;
using OBS_Helper.Wpf.Services.ObsConfig;
using OBS_Helper.Wpf.Services.Shell;

namespace OBS_Helper.Wpf.Views;

/// <summary>
/// OBS 控制台。连接 obs-websocket 后查看性能、切场景、控制场景元素显隐、
/// 调音量、开关录制 / 推流 / 虚拟摄像头。
///
/// 只有三个会中断直播的动作弹二次确认（断开连接、停止录制、停止推流），
/// 其余操作即点即执行——多一层确认只会拖慢开播现场的手速。
/// </summary>
public partial class ConsolePage : UserControl, INavigationAware
{
    /// <summary>一行音频输入用到的控件引用，避免每次刷新都重建整行（重建会打断拖动）。</summary>
    private sealed class AudioRow
    {
        public Button MuteButton = null!;
        public Slider VolumeSlider = null!;
        public TextBlock ValueText = null!;
    }

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private readonly DispatcherTimer _statsTimer;
    private readonly DispatcherTimer _volumeDebounce;

    private readonly Dictionary<string, (Button Button, TextBlock Flag)> _sceneButtons = new();
    private readonly Dictionary<int, CheckBox> _sceneItemChecks = new();
    private readonly Dictionary<string, AudioRow> _audioRows = new();

    // 列表指纹：内容没变就只做就地更新，避免 2 秒一次的统计刷新把列表整个重建（会丢焦点、闪烁）
    private string _sceneSignature = "";
    private string _sceneItemSignature = "";
    private string _audioSignature = "";

    /// <summary>程序化写控件值时置位，防止 Checked / ValueChanged 反过来又发请求。</summary>
    private bool _suppressInput;

    /// <summary>有请求在途。整块面板禁用，杜绝连点导致的乱序请求。</summary>
    private bool _busy;

    /// <summary>页面实例被导航缓存复用，自动连接只在首次进入时尝试一次。</summary>
    private bool _autoConnectTried;

    private (string Name, double Db)? _pendingVolume;

    public ConsolePage()
    {
        InitializeComponent();

        // obs-websocket 不推送性能统计，只能定时拉。2 秒一次足够看出掉帧趋势，又不会明显加重 OBS 负担。
        _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _statsTimer.Tick += OnStatsTick;

        // 拖动音量条时逐帧发请求会把 OBS 打满，停手 250ms 后只补发最后一个值
        _volumeDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _volumeDebounce.Tick += OnVolumeDebounceTick;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    // ------------------------------------------------------------ 生命周期

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AppServices.Obs.StateChanged += OnObsStateChanged;
        AppServices.Timer.StateChanged += OnTimerStateChanged;
        // 录制中的每秒刷新（时长 / 剩余）由简单录像服务广播 —— 它只在录制时起计时器；
        // 不订它的话，从首页开录后切到本页，这里的时长会停在最后一次 OBS 状态事件那一刻。
        AppServices.SimpleRecord.StateChanged += OnSimpleRecordStateChanged;
        // V3.0 第三轮验证：演播室模式在 OBS 窗口里被改后，本页按钮要跟着变
        AppServices.Obs.StudioModeChanged += OnStudioModeChanged;
        Render();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // 页面缓存复用，不退订就会越订越多；定时器不停就会在别的页面继续打请求
        AppServices.Obs.StateChanged -= OnObsStateChanged;
        AppServices.Timer.StateChanged -= OnTimerStateChanged;
        AppServices.SimpleRecord.StateChanged -= OnSimpleRecordStateChanged;
        AppServices.Obs.StudioModeChanged -= OnStudioModeChanged;
        _statsTimer.Stop();
        _volumeDebounce.Stop();
    }

    public async Task OnNavigatedToAsync(object? parameter)
    {
        await AppServices.ObsSettings.LoadAsync();
        ApplySettingsToForm();

        // 已经加密存过密码时，密码框留空即可复用，提示改成对应说明
        var hasStored = await SafeBoolAsync(AppServices.ObsSettings.HasStoredPasswordAsync);
        PasswordHintText.Text = hasStored
            ? Strings.T("console.passwordSaved")
            : Strings.T("console.passwordPlaceholder");

        await SafeAsync(AppServices.Obs.RefreshAllAsync);

        if (!_autoConnectTried)
        {
            _autoConnectTried = true;
            if (AppServices.ObsSettings.Current.AutoConnect && !AppServices.Obs.IsConnected)
            {
                await ConnectAsync();
            }
        }

        Render();
    }

    private void ApplySettingsToForm()
    {
        var cfg = AppServices.ObsSettings.Current;
        HostInput.Text = cfg.Host;
        PortInput.Text = cfg.Port.ToString(Inv);
        RememberCheck.IsChecked = cfg.RememberPassword;
        AutoConnectCheck.IsChecked = cfg.AutoConnect;
        AutoReconnectCheck.IsChecked = cfg.AutoReconnect;
    }

    /// <summary>连接服务可能在后台线程通知，必须回 UI 线程再动控件。</summary>
    private void OnObsStateChanged() => Dispatcher.BeginInvoke(new Action(Render));

    /// <summary>定时器每秒刷新倒计时（定时器本身跑在 UI 线程）。</summary>
    private void OnTimerStateChanged() => RenderTimer();

    /// <summary>简单录像服务每秒广播一次（仅在录制时）：只重画录制信息行，不动整页。</summary>
    private void OnSimpleRecordStateChanged()
        => Dispatcher.BeginInvoke(new Action(RenderRecordingProgress));

    private async void OnStatsTick(object? sender, EventArgs e)
    {
        if (_busy || !AppServices.Obs.IsConnected) return;
        // RefreshStatsAsync 内部会 Notify，界面刷新走 StateChanged 那条路
        await SafeAsync(AppServices.Obs.RefreshStatsAsync);
    }

    // -------------------------------------------------------------- 渲染

    private void Render()
    {
        var obs = AppServices.Obs;
        var connected = obs.IsConnected;

        ConnectPanel.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
        ConnectedPanel.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;

        // 连不上时服务把「去 OBS 里开 WebSocket 服务器」的指引放在 LastError 里，必须显示出来
        var showError = !connected && !string.IsNullOrEmpty(obs.LastError);
        ConnectErrorText.Text = obs.LastError ?? "";
        ConnectErrorText.Visibility = showError ? Visibility.Visible : Visibility.Collapsed;

        if (!connected)
        {
            _statsTimer.Stop();
            // 录像档案与自己录过的文件**与 OBS 是否连着无关**，所以先渲染再提前返回
            RenderRecentRecordings();
            RenderRemoteControls();   // 未连接时把演播室按钮置灰（V3.0 / D7）
            return;
        }

        if (!_statsTimer.IsEnabled) _statsTimer.Start();

        RenderStats(obs.Stats);
        RenderScenes(obs);
        RenderSceneItems(obs);
        RenderAudio(obs);
        RenderOutputs(obs);
        RenderTimer();
        RenderRemoteControls();
        RenderRecentRecordings();
    }

    // ------------------------------------------------------------ 最近录制（V3.0 / D3）

    /// <summary>最近录制列表的内容签名：只在内容真的变了才重建行（避免每秒重建掉用户正在点的按钮）。</summary>
    private string _recentSignature = "";

    /// <summary>
    /// 渲染「最近录制」列表。
    ///
    /// 列表本身与连接状态无关（OBS 关着也能看自己的录像），因此这个方法在 Render 的两条分支里都会走到。
    /// 每次重建（最多 10 行）比做增量更新简单得多，且这页本来就在按秒刷新。
    /// </summary>
    private void RenderRecentRecordings()
    {
        try
        {
            var entries = AppServices.Archive.Recent(10);
            RecentRecordsHint.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            if (entries.Count == 0) { RecentRecordsPanel.Children.Clear(); return; }

            // 列表很短时不做无谓重建（避免每秒钟把用户正在点的按钮重建掉）
            var signature = string.Join("|", entries.Select(e => $"{e.Path}#{e.Bytes}#{e.Markers.Count}"));
            if (signature == _recentSignature) return;
            _recentSignature = signature;

            RecentRecordsPanel.Children.Clear();
            foreach (var entry in entries) RecentRecordsPanel.Children.Add(BuildRecentRow(entry));
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Console", $"渲染最近录制失败：{ex.Message}");
        }
    }

    private UIElement BuildRecentRow(RecordingArchiveEntry entry)
    {
        var health = entry.Health(File.Exists(entry.Path));

        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

        var title = new TextBlock
        {
            Text = Strings.T("d3.recent.recordedAt", entry.StartedLocal.ToString("yyyy-MM-dd HH:mm")) + " · " + entry.FileName,
            TextWrapping = TextWrapping.Wrap
        };
        title.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSm");
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        panel.Children.Add(title);

        var detail = new TextBlock { Text = RecordingArchiveCore.Describe(entry, health), TextWrapping = TextWrapping.Wrap };
        detail.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeXs");
        // 可疑（空文件 / 过小 / 找不到）用警告色：这正是这个列表存在的理由
        detail.SetResourceReference(TextBlock.ForegroundProperty, health == RecordingHealth.Ok ? "MutedBrush" : "WarnBrush");
        panel.Children.Add(detail);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };

        buttons.Children.Add(MakeRowButton("d3.recent.openFolder", "GhostButton", () =>
        {
            var dir = Path.GetDirectoryName(entry.Path);
            if (!string.IsNullOrEmpty(dir)) TrashService.OpenInExplorer(dir);
        }));

        buttons.Children.Add(MakeRowButton("d3.recent.remux", "GhostButton", () => _ = RemuxAsync(entry)));

        if (entry.Markers.Count > 0)
            buttons.Children.Add(MakeRowButton("d3.recent.chapters", "GhostButton", () => ExportChapters(entry)));

        if (health != RecordingHealth.Missing)
            buttons.Children.Add(MakeRowButton("d3.recent.rename", "GhostButton", () => RenameEntry(entry)));

        panel.Children.Add(buttons);
        return panel;
    }

    private static Button MakeRowButton(string key, string styleKey, Action onClick)
    {
        var button = new Button
        {
            Content = Strings.T(key),
            Style = null,
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(10, 4, 10, 4)
        };
        button.SetResourceReference(FrameworkElement.StyleProperty, styleKey);
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>单个文件的转 MP4（有打点时会顺带把章节写进 MP4）。</summary>
    private async Task RemuxAsync(RecordingArchiveEntry entry)
    {
        try
        {
            var (ok, failed, messages) = await AppServices.Archive.BatchRemuxAsync(new[] { entry });
            AppServices.Toast?.Show(string.Join("\n", messages), ok > 0 && failed == 0 ? "ok" : "warn");
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Console", $"转 MP4 失败：{ex.Message}");
            AppServices.Toast?.Show(Strings.T("d3.remux.failed", entry.FileName, ex.Message), "warn");
        }
    }

    private void ExportChapters(RecordingArchiveEntry entry)
    {
        var (ok, message) = AppServices.Archive.ExportChapters(entry);
        AppServices.Toast?.Show(message, ok ? "ok" : "warn");
    }

    /// <summary>
    /// 按规则重命名。规则固定为「日期 + 时间」（不额外输入前缀），并在动手前把**新名字**给用户确认 ——
    /// 重命名是用户能直接感知的可见变更，先看名字再决定比事后撤销便宜。
    /// </summary>
    private void RenameEntry(RecordingArchiveEntry entry)
    {
        var rule = new RenameRule(IncludeDate: true, IncludeTime: true);
        var preview = RecordingArchiveCore.BuildNewFileName(entry, rule);

        if (!ConfirmDialog.Show(
                Strings.T("d3.recent.renameTitle"),
                Strings.T("d3.recent.renamePrefix") + "\n" + entry.FileName + "  →  " + preview,
                Strings.T("d3.recent.renameDo"), Strings.T("common.cancel"),
                danger: false, icon: "✏️"))
        {
            return;
        }

        var (newPath, error) = AppServices.Archive.Rename(entry, rule);
        AppServices.Toast?.Show(newPath is not null
            ? Strings.T("d3.recent.renamed", Path.GetFileName(newPath))
            : Strings.T("d3.recent.renameFailed", error ?? ""),
            newPath is not null ? "ok" : "warn");

        _recentSignature = "";   // 强制下一次刷新重建列表
        RenderRecentRecordings();
    }

    private void RenderStats(ObsStats s)
    {
        CpuText.Text = s.CpuUsage.ToString("0.0", Inv) + "%";
        FpsText.Text = s.ActiveFps.ToString("0.0", Inv);
        RenderSkipText.Text = (s.RenderSkipRatio * 100).ToString("0.##", Inv) + "%";
        OutputSkipText.Text = (s.OutputSkipRatio * 100).ToString("0.##", Inv) + "%";
        DiskText.Text = (s.AvailableDiskSpaceMb / 1024.0).ToString("0.0", Inv) + "G";
    }

    private void RenderScenes(ObsConnectionService obs)
    {
        var signature = string.Join("\u0001", obs.Scenes.Select(x => x.Name));
        if (signature != _sceneSignature)
        {
            _sceneSignature = signature;
            ScenesPanel.Children.Clear();
            _sceneButtons.Clear();

            foreach (var scene in obs.Scenes)
            {
                var nameText = new TextBlock
                {
                    Text = scene.Name,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };

                var flagText = new TextBlock
                {
                    Text = Strings.T("console.sceneCurrent"),
                    FontWeight = FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0),
                    Visibility = Visibility.Collapsed
                };
                flagText.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeXs");
                flagText.SetResourceReference(TextBlock.ForegroundProperty, "BrandBrush");

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Grid.SetColumn(flagText, 1);
                grid.Children.Add(nameText);
                grid.Children.Add(flagText);

                var button = new Button
                {
                    Style = (Style)FindResource("SecondaryButton"),
                    Content = grid,
                    Tag = scene.Name,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(0, 0, 0, 8)
                };
                button.Click += OnSceneClick;

                _sceneButtons[scene.Name] = (button, flagText);
                ScenesPanel.Children.Add(button);
            }
        }

        foreach (var scene in obs.Scenes)
        {
            if (!_sceneButtons.TryGetValue(scene.Name, out var row)) continue;
            ApplyActiveLook(row.Button, scene.IsCurrent);
            row.Flag.Visibility = scene.IsCurrent ? Visibility.Visible : Visibility.Collapsed;
        }

        ScenesEmptyText.Visibility = obs.Scenes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RenderSceneItems(ObsConnectionService obs)
    {
        var items = obs.CurrentSceneItems;
        var signature = string.Join("\u0001", items.Select(x => x.Id + ":" + x.SourceName));
        if (signature != _sceneItemSignature)
        {
            _sceneItemSignature = signature;
            SceneItemsPanel.Children.Clear();
            _sceneItemChecks.Clear();

            foreach (var item in items)
            {
                var check = new CheckBox
                {
                    Style = (Style)FindResource("AppCheckBox"),
                    // 锁定的源在 OBS 里仍可改显隐，加个锁标只是让用户知道它被锁了变换
                    Content = item.Locked ? item.SourceName + "  🔒" : item.SourceName,
                    Tag = item.Id,
                    Margin = new Thickness(0, 0, 0, 10)
                };
                check.Checked += OnSceneItemToggled;
                check.Unchecked += OnSceneItemToggled;

                _sceneItemChecks[item.Id] = check;
                SceneItemsPanel.Children.Add(check);
            }
        }

        _suppressInput = true;
        foreach (var item in items)
        {
            if (_sceneItemChecks.TryGetValue(item.Id, out var check)) check.IsChecked = item.Enabled;
        }
        _suppressInput = false;

        SceneItemsEmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RenderAudio(ObsConnectionService obs)
    {
        var inputs = obs.AudioInputs;
        var signature = string.Join("\u0001", inputs.Select(x => x.Name));
        if (signature != _audioSignature)
        {
            _audioSignature = signature;
            AudioPanel.Children.Clear();
            _audioRows.Clear();

            foreach (var input in inputs) AudioPanel.Children.Add(BuildAudioRow(input));
        }

        _suppressInput = true;
        foreach (var input in inputs)
        {
            if (!_audioRows.TryGetValue(input.Name, out var row)) continue;

            row.MuteButton.Content = input.Muted ? "🔇" : "🔊";
            ApplyActiveLook(row.MuteButton, input.Muted);

            // 用户正在拖 / 用键盘调时不要把滑块拽回服务端的旧值
            if (!row.VolumeSlider.IsMouseCaptureWithin && !row.VolumeSlider.IsKeyboardFocusWithin)
            {
                row.VolumeSlider.Value = Math.Clamp(input.VolumeDb, -100d, 0d);
            }
            row.ValueText.Text = row.VolumeSlider.Value.ToString("0", Inv);
        }
        _suppressInput = false;

        AudioEmptyText.Visibility = inputs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private FrameworkElement BuildAudioRow(ObsInputInfo input)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var muteButton = new Button
        {
            Style = (Style)FindResource("SecondaryButton"),
            Content = input.Muted ? "🔇" : "🔊",
            Tag = input.Name,
            Width = 38,
            Height = 38,
            Padding = new Thickness(0),
            ToolTip = Strings.T("console.muteToggle")
        };
        muteButton.Click += OnMuteClick;
        Grid.SetColumn(muteButton, 0);

        var nameText = new TextBlock
        {
            Text = input.Name,
            Margin = new Thickness(10, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        nameText.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBase");
        nameText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        Grid.SetColumn(nameText, 1);

        var slider = new Slider
        {
            Minimum = -100,
            Maximum = 0,
            SmallChange = 1,
            LargeChange = 5,
            IsMoveToPointEnabled = true,
            Width = 180,
            VerticalAlignment = VerticalAlignment.Center,
            Tag = input.Name,
            Value = Math.Clamp(input.VolumeDb, -100d, 0d)
        };
        slider.ValueChanged += OnVolumeChanged;
        Grid.SetColumn(slider, 2);

        var valueText = new TextBlock
        {
            Text = Math.Clamp(input.VolumeDb, -100d, 0d).ToString("0", Inv),
            Width = 38,
            Margin = new Thickness(8, 0, 0, 0),
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        valueText.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSm");
        valueText.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        Grid.SetColumn(valueText, 3);

        // 音画同步（V3.0 / D7）：OBS 里这个入口藏在「高级音频属性」，用户很难自己找到
        var syncButton = new Button
        {
            Style = (Style)FindResource("GhostButton"),
            Content = "⏱",
            Tag = input.Name,
            Width = 34,
            Height = 34,
            Padding = new Thickness(0),
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = Strings.T("console.syncOffset")
        };
        syncButton.Click += OnSyncOffsetClick;
        Grid.SetColumn(syncButton, 4);

        grid.Children.Add(muteButton);
        grid.Children.Add(nameText);
        grid.Children.Add(slider);
        grid.Children.Add(valueText);
        grid.Children.Add(syncButton);

        _audioRows[input.Name] = new AudioRow
        {
            MuteButton = muteButton,
            VolumeSlider = slider,
            ValueText = valueText
        };
        return grid;
    }

    /// <summary>
    /// 打开「音画同步」对话框（V3.0 / D7）。
    ///
    /// 打开时**现读**当前偏移（不在每次界面刷新时对每个输入都查一遍 —— 那会白白多出 N 次往返）；
    /// 读不到时按 0 处理并在对话框里如实说明，而不是假装读到了。
    /// </summary>
    private async void OnSyncOffsetClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string inputName }) return;

        int current;
        try
        {
            current = await AppServices.Obs.GetAudioSyncOffsetMsAsync(inputName) ?? 0;
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Console", $"读取同步偏移失败（按 0 处理）：{ex.Message}");
            current = 0;
        }

        var chosen = SyncOffsetDialog.Show(Window.GetWindow(this), inputName, current);
        if (chosen is null) return;   // 取消：不写任何东西

        try
        {
            var r = await AppServices.Obs.SetAudioSyncOffsetMsAsync(inputName, chosen.Value);
            AppServices.Toast?.Show(
                r.Ok ? Strings.T("sync.applied", inputName, chosen.Value) : Strings.T("sync.applyFailed", r.Comment ?? ""),
                r.Ok ? "ok" : "warn");
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Console", $"设置同步偏移失败：{ex.Message}");
            AppServices.Toast?.Show(Strings.T("sync.applyFailed", ex.Message), "warn");
        }
    }

    private void RenderOutputs(ObsConnectionService obs)
    {
        var rec = obs.RecordStatus;
        var recLabel = rec.Active ? (rec.Paused ? Strings.T("console.state.paused") : Strings.T("console.state.inProgress")) : Strings.T("console.state.idle");
        RecordButtonText.Text = Strings.T("console.record.prefix", recLabel);
        RecordStatusPill.Tag = rec.Active ? "danger" : "info";
        RecordStatusPill.Content = recLabel;
        ApplyActiveLook(RecordButton, rec.Active);

        var streamActive = obs.StreamStatus.Active;
        var streamLabel = streamActive ? Strings.T("console.state.inProgress") : Strings.T("console.state.idle");
        StreamButtonText.Text = Strings.T("console.stream.prefix", streamLabel);
        StreamStatusPill.Tag = streamActive ? "danger" : "info";
        StreamStatusPill.Content = streamLabel;
        ApplyActiveLook(StreamButton, streamActive);

        var vcamActive = obs.VirtualCamStatus.Active;
        var vcamLabel = vcamActive ? Strings.T("console.vcam.on") : Strings.T("console.vcam.off");
        VirtualCamButtonText.Text = Strings.T("console.vcam.prefix", vcamLabel);
        VirtualCamStatusPill.Tag = vcamActive ? "ok" : "info";
        VirtualCamStatusPill.Content = vcamLabel;
        ApplyActiveLook(VirtualCamButton, vcamActive);

        RenderRecordingProgress();
    }

    /// <summary>
    /// 录制中的时长与容量（V2.9.4）。数据来自简单录像服务 —— 它按预设码率估算剩余可录时长，
    /// 不在这里另算一份（两处各算一套迟早会给出不一样的结论）。
    /// </summary>
    private void RenderRecordingProgress()
    {
        var progress = AppServices.SimpleRecord.Progress;
        if (progress is null)
        {
            RecordProgressPanel.Visibility = Visibility.Collapsed;
            return;
        }

        RecordProgressPanel.Visibility = Visibility.Visible;

        var unknown = Strings.T("simple.record.unknown");
        RecordElapsedText.Text = Strings.T("simple.record.elapsed")
            + SimpleRecordingCore.FormatDuration(progress.Elapsed);

        RecordRemainingText.Text = Strings.T("simple.record.remaining")
            + (progress.FreeGb > 0
                ? Strings.T(progress.FromMeasuredRate
                        ? "simple.record.remainingValueMeasured"
                        : "simple.record.remainingValue",
                    progress.RemainingMinutes.ToString("0"), progress.FreeGb.ToString("0.#"))
                : unknown);
        RecordRemainingText.SetResourceReference(TextBlock.ForegroundProperty,
            progress.LowSpace ? "WarnBrush" : "MutedBrush");

        var file = AppServices.Obs.LastRecordFile;
        RecordFileText.Text = Strings.T("simple.record.fileSize")
            + (string.IsNullOrWhiteSpace(file)
                ? unknown
                : Strings.T("simple.record.fileSizeValue", file, progress.UsedMb.ToString("0.#")));
    }

    /// <summary>选中态的统一观感。用 SetResourceReference 而非直接赋画刷，换肤时才会跟着变。</summary>
    private static void ApplyActiveLook(Control control, bool active)
    {
        control.SetResourceReference(Control.BackgroundProperty, active ? "BrandSoftBrush" : "Surface2Brush");
        control.SetResourceReference(Control.BorderBrushProperty, active ? "BrandBrush" : "LineBrush");
        control.FontWeight = active ? FontWeights.Bold : FontWeights.Normal;
    }

    // -------------------------------------------------------------- 交互

    private void OnPortPreviewTextInput(object sender, TextCompositionEventArgs e)
        => e.Handled = !e.Text.All(Services.Compat.Compat.IsAsciiDigit);

    private async void OnConnectClick(object sender, RoutedEventArgs e) => await ConnectAsync();

    private async Task ConnectAsync()
    {
        if (_busy) return;

        var host = HostInput.Text.Trim();
        if (host.Length == 0)
        {
            ShowConnectError(Strings.T("console.err.host"));
            return;
        }
        if (!int.TryParse(PortInput.Text.Trim(), NumberStyles.Integer, Inv, out var port) || port is < 1 or > 65535)
        {
            ShowConnectError(Strings.T("console.err.port"));
            return;
        }

        var remember = RememberCheck.IsChecked == true;
        var password = PasswordInput.Password;

        SetBusy(true);
        ConnectButton.Content = Strings.T("badge.connecting");
        ConnectErrorText.Visibility = Visibility.Collapsed;
        try
        {
            await AppServices.ObsSettings.SaveAsync(new ObsConnectionSettings
            {
                Host = host,
                Port = port,
                AutoConnect = AutoConnectCheck.IsChecked == true,
                AutoReconnect = AutoReconnectCheck.IsChecked == true,
                RememberPassword = remember
            });

            // 密码框留空 + 勾了记住 + 已存过密码：说明用户想复用旧密码。
            // 此时不能调 SetPasswordAsync("")，那会把加密存储直接删掉。
            var hasStored = await SafeBoolAsync(AppServices.ObsSettings.HasStoredPasswordAsync);
            var useStored = password.Length == 0 && remember && hasStored;
            if (!useStored)
            {
                // V3.0：密码落盘失败要**当场**告诉用户（原先界面会一直显示「已记住」，
                // 直到下次重启才发现要重填 —— secrets.dat 损坏 / 被占用时就是这种表现）。
                var stored = await AppServices.ObsSettings.SetPasswordAsync(password, remember);
                if (!stored)
                    AppServices.Toast?.Show(Strings.T("console.passwordNotSaved"));
            }

            // 连接是跨网络的长操作，挂全局加载遮罩（P0）；页面内按钮态并行保留
            AppServices.Busy.Show(Strings.T("console.connectingBusy"));
            await AppServices.Obs.ConnectAsync(useStored ? null : password);
            if (AppServices.Obs.IsConnected) AppServices.Toast?.Show(Strings.T("console.connected"), "ok");
        }
        catch (Exception ex)
        {
            ShowConnectError(ex.Message);
        }
        finally
        {
            ConnectButton.Content = Strings.T("console.connect");
            SetBusy(false);
            AppServices.Busy.Hide();
            Render();
        }
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        SetBusy(true);
        try
        {
            await SafeAsync(AppServices.Obs.RefreshAllAsync);
        }
        finally
        {
            SetBusy(false);
            Render();
        }
    }

    private async void OnDisconnectClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (!ConfirmDialog.Show(Strings.T("console.disconnectTitle"), Strings.T("console.disconnectMessage"))) return;

        SetBusy(true);
        AppServices.Busy.Show(Strings.T("console.disconnectingBusy"));
        try
        {
            await AppServices.Obs.DisconnectAsync();
            HideOpError();
            AppServices.Toast.Show(Strings.T("console.disconnected"), "info");
        }
        catch (Exception ex)
        {
            ShowOpError(Strings.T("console.disconnectFailed") + ex.Message);
        }
        finally
        {
            SetBusy(false);
            AppServices.Busy.Hide();
            Render();
        }
    }

    private async void OnSceneClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string sceneName) return;
        await RunAsync(Strings.T("console.op.switchScene"), () => AppServices.Obs.SetSceneAsync(sceneName),
            onSuccess: () => AppServices.Toast.Show(Strings.T("console.sceneSwitched", sceneName), "ok"));
    }

    private async void OnSceneItemToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressInput || sender is not CheckBox check || check.Tag is not int itemId) return;

        var enabled = check.IsChecked == true;
        var scene = AppServices.Obs.CurrentScene;
        await RunAsync(enabled ? Strings.T("console.op.showItem") : Strings.T("console.op.hideItem"),
            () => AppServices.Obs.SetSceneItemEnabledAsync(scene, itemId, enabled));
    }

    private async void OnMuteClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string inputName) return;

        var input = AppServices.Obs.AudioInputs.FirstOrDefault(x => x.Name == inputName);
        if (input is null) return;

        var muted = !input.Muted;
        await RunAsync(muted ? Strings.T("console.op.mute") : Strings.T("console.op.unmute"), () => AppServices.Obs.SetMuteAsync(inputName, muted));
    }

    private void OnVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressInput || sender is not Slider slider || slider.Tag is not string inputName) return;

        if (_audioRows.TryGetValue(inputName, out var row))
        {
            row.ValueText.Text = e.NewValue.ToString("0", Inv);
        }

        _pendingVolume = (inputName, Math.Round(e.NewValue));
        _volumeDebounce.Stop();
        _volumeDebounce.Start();
    }

    private async void OnVolumeDebounceTick(object? sender, EventArgs e)
    {
        _volumeDebounce.Stop();
        if (_pendingVolume is not { } pending) return;
        _pendingVolume = null;

        await RunAsync(Strings.T("console.op.volume"), async () =>
        {
            var result = await AppServices.Obs.SetVolumeDbAsync(pending.Name, pending.Db);
            if (result.Ok)
            {
                // OBS 的 InputVolumeChanged 事件到达前先把本地值对齐，
                // 否则紧接着的一次渲染会把滑块弹回旧值。
                var input = AppServices.Obs.AudioInputs.FirstOrDefault(x => x.Name == pending.Name);
                if (input is not null) input.VolumeDb = (float)pending.Db;
            }
            return result;
        });
    }

    private async void OnRecordClick(object sender, RoutedEventArgs e)
    {
        if (AppServices.Obs.RecordStatus.Active)
        {
            if (!ConfirmDialog.Show(Strings.T("console.stopRecordTitle"), Strings.T("console.stopRecordMessage"))) return;
            await RunAsync(Strings.T("console.op.stopRecord"), () => AppServices.Obs.StopRecordAsync());
        }
        else
        {
            await RunAsync(Strings.T("console.op.startRecord"), () => AppServices.Obs.StartRecordAsync());
        }
    }

    private async void OnStreamClick(object sender, RoutedEventArgs e)
    {
        if (AppServices.Obs.StreamStatus.Active)
        {
            if (!ConfirmDialog.Show(Strings.T("console.stopStreamTitle"), Strings.T("console.stopStreamMessage"))) return;
            await RunAsync(Strings.T("console.op.stopStream"), () => AppServices.Obs.StopStreamAsync());
        }
        else
        {
            await RunAsync(Strings.T("console.op.startStream"), () => AppServices.Obs.StartStreamAsync());
        }
    }

    private async void OnVirtualCamClick(object sender, RoutedEventArgs e)
    {
        // 虚拟摄像头开关不影响正在进行的直播，按原版设计不做二次确认
        if (AppServices.Obs.VirtualCamStatus.Active)
            await RunAsync(Strings.T("console.op.stopVcam"), () => AppServices.Obs.StopVirtualCamAsync());
        else
            await RunAsync(Strings.T("console.op.startVcam"), () => AppServices.Obs.StartVirtualCamAsync());
    }

    // -------------------------------------------------------------- 定时停止 / 录制目录

    private void OnTimerStartClick(object sender, RoutedEventArgs e)
    {
        if (AppServices.Timer.IsRunning) return;

        var target = TimerTargetBox.SelectedIndex == 1 ? TimerTarget.Stream : TimerTarget.Record;
        var seconds = 1800; // 默认 30 分钟
        if (TimerDurationBox.SelectedItem is ComboBoxItem item && item.Tag is string tag
            && int.TryParse(tag, out var s) && s > 0)
        {
            seconds = s;
        }

        AppServices.Timer.Start(target, TimeSpan.FromSeconds(seconds));
    }

    private void OnTimerCancelClick(object sender, RoutedEventArgs e)
        => AppServices.Timer.Cancel();

    /// <summary>同步定时器相关控件状态：运行时锁定选项并显示倒计时。</summary>
    private void RenderTimer()
    {
        var running = AppServices.Timer.IsRunning;
        TimerStartButton.IsEnabled = !running;
        TimerTargetBox.IsEnabled = !running;
        TimerDurationBox.IsEnabled = !running;
        TimerCancelButton.Visibility = running ? Visibility.Visible : Visibility.Collapsed;

        if (running && AppServices.Timer.Current is { } t)
        {
            var label = t.Target == TimerTarget.Record ? Strings.T("console.timer.record") : Strings.T("console.timer.stream");
            var rem = AppServices.Timer.RemainingSeconds;
            TimerCountdownText.Text = Strings.T("console.timer.countdown", label, TimeSpan.FromSeconds(rem).ToString(@"mm\:ss"));
        }
        else
        {
            TimerCountdownText.Text = "";
        }
    }

    /// <summary>控制台页「迷你小窗」按钮：呼出或隐藏置顶小窗。</summary>
    private void OnMiniWindowClick(object sender, RoutedEventArgs e)
        => AppServices.Mini.Toggle();

    /// <summary>截图格式（与文件扩展名同源，避免两者不一致）。</summary>
    private const string ShotFormat = "png";

    // -------------------------------------------------------------- 演播室模式 / 节目截图（V3.0 / D7）

    /// <summary>演播室模式开关。切换后按钮文案与徽章立即跟手（不等下一次事件）。</summary>
    private async void OnStudioModeClick(object sender, RoutedEventArgs e)
    {
        if (!AppServices.Obs.IsConnected) return;

        var target = !AppServices.Obs.StudioModeEnabled;
        await RunAsync(
            target ? Strings.T("console.studio.off") : Strings.T("console.studio.on"),
            () => AppServices.Obs.SetStudioModeEnabledAsync(target),
            RenderRemoteControls);
    }

    /// <summary>演播室模式下「预览 → 节目」。未启用时按钮本身就是禁用的，这里再兜一次。</summary>
    private async void OnStudioTransitionClick(object sender, RoutedEventArgs e)
    {
        if (!AppServices.Obs.IsConnected || !AppServices.Obs.StudioModeEnabled) return;

        await RunAsync(
            Strings.T("console.studio.transition"),
            () => AppServices.Obs.TriggerStudioModeTransitionAsync(),
            () => AppServices.Toast?.Show(Strings.T("console.studio.transitionDone"), "ok"));
    }

    /// <summary>
    /// 同步「依赖 OBS 连接」的远程操作入口（V3.0 / D7）：
    /// 未连接时「演播室模式」「切换」「节目截图」都不可点 —— 点了只会弹一条错误，
    /// 而灰掉的按钮本身就是「现在不能用」最清楚的表达。
    /// </summary>
    /// <summary>
    /// 演播室状态变化（V3.0 第三轮验证）：OBS 窗口里开关演播室模式后要在本页立刻反映出来。
    /// 事件可能在 socket 接收线程触发，所以切回 UI 线程再刷。
    /// </summary>
    private void OnStudioModeChanged()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(RenderRemoteControls));
            return;
        }
        RenderRemoteControls();
    }

    private void RenderRemoteControls()
    {
        var connected = AppServices.Obs.IsConnected;
        var on = connected && AppServices.Obs.StudioModeEnabled;

        StudioModeButtonText.Text = on ? Strings.T("console.studio.on") : Strings.T("console.studio.off");
        StudioModePill.Content = on ? Strings.T("console.studio.onShort") : Strings.T("console.studio.offShort");
        StudioModePill.Tag = on ? "ok" : "info";
        StudioModeButton.IsEnabled = connected;
        StudioTransitionButton.IsEnabled = on;
        ScreenshotButton.IsEnabled = connected;
    }

    /// <summary>
    /// 自检用：未连接 OBS 时，依赖连接的操作入口是否都被禁用（V3.0 / D7）。
    /// 返回 (演播室按钮, 切换按钮, 截图按钮) 的可用状态。
    /// </summary>
    internal (bool Studio, bool Transition, bool Screenshot) RemoteControlEnabledState
        => (StudioModeButton.IsEnabled, StudioTransitionButton.IsEnabled, ScreenshotButton.IsEnabled);

    /// <summary>
    /// 节目画面截图：存到录制目录（拿不到就存图片目录），成功后提示并给出文件名。
    ///
    /// 为什么值得做：OBS 自带这个能力，而「画面还在不在」是主播最常需要留证的一件事
    /// （卡顿、黑屏、花屏的现场）；此前本工具一处未用。
    /// </summary>
    private async void OnScreenshotClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (!AppServices.Obs.IsConnected)
        {
            ShowOpError(Strings.T("console.screenshot.failed", Strings.T("console.notConnected")));
            return;
        }

        SetBusy(true);
        try
        {
            var dir = await AppServices.Obs.GetRecordDirectoryAsync().ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(dir) || !System.IO.Directory.Exists(dir))
            {
                dir = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            }

            // 扩展名由 format 决定（避免「jpg 内容 + .png 后缀」这种骗人的文件）；
            // 时间戳带毫秒：同一秒连点两次不再互相覆盖。
            var ext = string.IsNullOrWhiteSpace(ShotFormat) ? "png" : ShotFormat.TrimStart('.').ToLowerInvariant();
            var file = System.IO.Path.Combine(dir,
                $"obs-helper-shot-{DateTime.Now:yyyyMMdd_HHmmss_fff}.{ext}");

            // sourceName 传空串 = 当前节目画面（OBS 对空值的语义）
            var result = await AppServices.Obs.SaveSourceScreenshotAsync("", file, ShotFormat).ConfigureAwait(true);
            if (result.Ok)
            {
                HideOpError();
                AppServices.Toast?.Show(Strings.T("console.screenshot.saved", System.IO.Path.GetFileName(file)), "ok");
            }
            else
            {
                ShowOpError(Strings.T("console.screenshot.failed", Describe(result)));
            }
        }
        catch (Exception ex)
        {
            ShowOpError(Strings.T("console.screenshot.failed", ex.Message));
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnOpenRecordDirClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        SetBusy(true);
        try
        {
            var dir = await AppServices.Obs.GetRecordDirectoryAsync();
            if (string.IsNullOrEmpty(dir))
            {
                RecordDirHintText.Text = Strings.T("console.recordDir.none");
                RecordDirHintText.Visibility = Visibility.Visible;
                return;
            }

            var ok = AppServices.Host.OpenFolder(dir);
            RecordDirHintText.Text = ok ? Strings.T("console.recordDir.opened", dir) : Strings.T("console.recordDir.failed", dir);
            RecordDirHintText.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            RecordDirHintText.Text = Strings.T("console.recordDir.error") + ex.Message;
            RecordDirHintText.Visibility = Visibility.Visible;
        }
        finally
        {
            SetBusy(false);
        }
    }

    // -------------------------------------------------------------- 辅助

    /// <summary>统一跑一次写操作：期间禁用面板防连点，失败把 OBS 的原始说明摆到界面上。</summary>
    private async Task RunAsync(string what, Func<Task<ObsRequestResult>> operation, Action? onSuccess = null)
    {
        if (_busy) return;
        SetBusy(true);
        try
        {
            var result = await operation();
            if (result.Ok)
            {
                HideOpError();
                onSuccess?.Invoke();
            }
            else ShowOpError(Strings.T("console.opFailed", what, Describe(result)));
        }
        catch (Exception ex)
        {
            ShowOpError(Strings.T("console.opFailed", what, ex.Message));
        }
        finally
        {
            SetBusy(false);
            Render();
        }
    }

    private static string Describe(ObsRequestResult result)
        => !string.IsNullOrWhiteSpace(result.Comment) ? result.Comment! : Strings.T("console.opErrorCode", result.Code);

    private void SetBusy(bool busy)
    {
        _busy = busy;
        ConnectedPanel.IsEnabled = !busy;
        ConnectButton.IsEnabled = !busy;
    }

    private void ShowConnectError(string message)
    {
        ConnectErrorText.Text = message;
        ConnectErrorText.Visibility = Visibility.Visible;
    }

    private void ShowOpError(string message)
    {
        OpErrorText.Text = message;
        OpErrorPanel.Visibility = Visibility.Visible;
    }

    private void HideOpError() => OpErrorPanel.Visibility = Visibility.Collapsed;

    /// <summary>刷新类操作失败不该打断页面：下一次事件或手动刷新会纠正。</summary>
    private static async Task SafeAsync(Func<Task> operation)
    {
        try { await operation(); }
        catch (Exception) { /* 忽略：只影响一次快照 */ }
    }

    private static async Task<bool> SafeBoolAsync(Func<Task<bool>> operation)
    {
        try { return await operation(); }
        catch (Exception) { return false; }
    }
}
