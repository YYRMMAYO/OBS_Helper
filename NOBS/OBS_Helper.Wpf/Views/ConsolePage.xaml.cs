using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using OBS_Helper.Wpf.Controls;
using OBS_Helper.Wpf.Models.Obs;
using OBS_Helper.Wpf.Navigation;
using OBS_Helper.Wpf.Services.Obs;

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
        Render();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // 页面缓存复用，不退订就会越订越多；定时器不停就会在别的页面继续打请求
        AppServices.Obs.StateChanged -= OnObsStateChanged;
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
            ? "已保存过密码，留空即使用已保存的密码。"
            : "obs-websocket 密码（可留空）";

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
            return;
        }

        if (!_statsTimer.IsEnabled) _statsTimer.Start();

        RenderStats(obs.Stats);
        RenderScenes(obs);
        RenderSceneItems(obs);
        RenderAudio(obs);
        RenderOutputs(obs);
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
                    Text = "当前",
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

        var muteButton = new Button
        {
            Style = (Style)FindResource("SecondaryButton"),
            Content = input.Muted ? "🔇" : "🔊",
            Tag = input.Name,
            Width = 38,
            Height = 38,
            Padding = new Thickness(0),
            ToolTip = "静音切换"
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

        grid.Children.Add(muteButton);
        grid.Children.Add(nameText);
        grid.Children.Add(slider);
        grid.Children.Add(valueText);

        _audioRows[input.Name] = new AudioRow
        {
            MuteButton = muteButton,
            VolumeSlider = slider,
            ValueText = valueText
        };
        return grid;
    }

    private void RenderOutputs(ObsConnectionService obs)
    {
        var rec = obs.RecordStatus;
        RecordButton.Content = "🔴 录制：" + (rec.Active ? (rec.Paused ? "已暂停" : "进行中") : "未开始");
        ApplyActiveLook(RecordButton, rec.Active);

        StreamButton.Content = "📡 推流：" + (obs.StreamStatus.Active ? "进行中" : "未开始");
        ApplyActiveLook(StreamButton, obs.StreamStatus.Active);

        VirtualCamButton.Content = "🎥 虚拟摄像头：" + (obs.VirtualCamStatus.Active ? "开启" : "关闭");
        ApplyActiveLook(VirtualCamButton, obs.VirtualCamStatus.Active);
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
        => e.Handled = !e.Text.All(char.IsAsciiDigit);

    private async void OnConnectClick(object sender, RoutedEventArgs e) => await ConnectAsync();

    private async Task ConnectAsync()
    {
        if (_busy) return;

        var host = HostInput.Text.Trim();
        if (host.Length == 0)
        {
            ShowConnectError("请填写主机地址，默认是 127.0.0.1。");
            return;
        }
        if (!int.TryParse(PortInput.Text.Trim(), NumberStyles.Integer, Inv, out var port) || port is < 1 or > 65535)
        {
            ShowConnectError("端口必须是 1~65535 之间的数字，obs-websocket 默认 4455。");
            return;
        }

        var remember = RememberCheck.IsChecked == true;
        var password = PasswordInput.Password;

        SetBusy(true);
        ConnectButton.Content = "连接中…";
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
            if (!useStored) await AppServices.ObsSettings.SetPasswordAsync(password, remember);

            await AppServices.Obs.ConnectAsync(useStored ? null : password);
        }
        catch (Exception ex)
        {
            ShowConnectError(ex.Message);
        }
        finally
        {
            ConnectButton.Content = "连接";
            SetBusy(false);
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
        if (!ConfirmDialog.Show("断开连接", "确定要断开与 OBS 的连接吗？")) return;

        SetBusy(true);
        try
        {
            await AppServices.Obs.DisconnectAsync();
            HideOpError();
        }
        catch (Exception ex)
        {
            ShowOpError("断开失败：" + ex.Message);
        }
        finally
        {
            SetBusy(false);
            Render();
        }
    }

    private async void OnSceneClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string sceneName) return;
        await RunAsync("切换场景", () => AppServices.Obs.SetSceneAsync(sceneName));
    }

    private async void OnSceneItemToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressInput || sender is not CheckBox check || check.Tag is not int itemId) return;

        var enabled = check.IsChecked == true;
        var scene = AppServices.Obs.CurrentScene;
        await RunAsync(enabled ? "显示元素" : "隐藏元素",
            () => AppServices.Obs.SetSceneItemEnabledAsync(scene, itemId, enabled));
    }

    private async void OnMuteClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string inputName) return;

        var input = AppServices.Obs.AudioInputs.FirstOrDefault(x => x.Name == inputName);
        if (input is null) return;

        var muted = !input.Muted;
        await RunAsync(muted ? "静音" : "取消静音", () => AppServices.Obs.SetMuteAsync(inputName, muted));
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

        await RunAsync("调整音量", async () =>
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
            if (!ConfirmDialog.Show("停止录制", "确定要停止当前录制吗？")) return;
            await RunAsync("停止录制", () => AppServices.Obs.StopRecordAsync());
        }
        else
        {
            await RunAsync("开始录制", () => AppServices.Obs.StartRecordAsync());
        }
    }

    private async void OnStreamClick(object sender, RoutedEventArgs e)
    {
        if (AppServices.Obs.StreamStatus.Active)
        {
            if (!ConfirmDialog.Show("停止推流", "确定要停止当前推流吗？观众将立即断开。")) return;
            await RunAsync("停止推流", () => AppServices.Obs.StopStreamAsync());
        }
        else
        {
            await RunAsync("开始推流", () => AppServices.Obs.StartStreamAsync());
        }
    }

    private async void OnVirtualCamClick(object sender, RoutedEventArgs e)
    {
        // 虚拟摄像头开关不影响正在进行的直播，按原版设计不做二次确认
        if (AppServices.Obs.VirtualCamStatus.Active)
            await RunAsync("关闭虚拟摄像头", () => AppServices.Obs.StopVirtualCamAsync());
        else
            await RunAsync("开启虚拟摄像头", () => AppServices.Obs.StartVirtualCamAsync());
    }

    // -------------------------------------------------------------- 辅助

    /// <summary>统一跑一次写操作：期间禁用面板防连点，失败把 OBS 的原始说明摆到界面上。</summary>
    private async Task RunAsync(string what, Func<Task<ObsRequestResult>> operation)
    {
        if (_busy) return;
        SetBusy(true);
        try
        {
            var result = await operation();
            if (result.Ok) HideOpError();
            else ShowOpError($"{what}失败：{Describe(result)}");
        }
        catch (Exception ex)
        {
            ShowOpError($"{what}失败：{ex.Message}");
        }
        finally
        {
            SetBusy(false);
            Render();
        }
    }

    private static string Describe(ObsRequestResult result)
        => !string.IsNullOrWhiteSpace(result.Comment) ? result.Comment! : $"OBS 返回错误码 {result.Code}";

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
