using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OBS_Helper.Wpf.Controls;
using OBS_Helper.Wpf.Errors;
using OBS_Helper.Wpf.Models.Shell;
using OBS_Helper.Wpf.Navigation;
using OBS_Helper.Wpf.Services;
using OBS_Helper.Wpf.Services.Ai;
using OBS_Helper.Wpf.Services.ObsConfig;
using OBS_Helper.Wpf.Services.Shell;

namespace OBS_Helper.Wpf.Views;

/// <summary>
/// 设置：AI 诊断引擎、外观与无障碍、排障指引与本地数据、关于。
///
/// 页面实例被导航缓存复用，每次进入都重新从服务读一遍当前值，
/// 避免别处（例如系统主题变化）改过设置之后这里还显示旧的选中态。
/// </summary>
public partial class SettingsPage : UserControl, INavigationAware
{
    /// <summary>
    /// 回填控件初值时会触发 Checked / Unchecked，处理函数又会去写设置，形成回环。
    /// 所有「程序主动赋值」的区间都用它挡住，只让真正的用户操作落到服务上。
    /// </summary>
    private bool _syncing;

    public SettingsPage()
    {
        InitializeComponent();
    }

    public async Task OnNavigatedToAsync(object? parameter)
    {
        await AppServices.AiSettings.LoadAsync();
        AppServices.Appearance.Initialize();
        AppServices.Hotkeys.Load();
        AppServices.AutoSwitcher.Load();
        AppServices.Tray.LoadSettings();

        // 已连接 OBS 时刷新一次场景列表，让「自动切换规则」能选到目标场景
        if (AppServices.Obs.IsConnected)
        {
            try { await AppServices.Obs.RefreshScenesAsync(); }
            catch (Exception) { /* 刷新失败不阻塞设置页 */ }
        }

        SyncControls();
        RefreshDataSummary();

        await RefreshKeyStatusAsync();
        await RefreshFreeQuotaAsync();
        await RefreshAboutAsync();
        await RefreshObsConfigHintAsync();
        RefreshUpdateStatus();
    }

    // ------------------------------------------------------------ 状态回填

    private void SyncControls()
    {
        _syncing = true;
        try
        {
            var ai = AppServices.AiSettings;
            var mode = ai.Mode;
            ModeLocal.IsChecked = mode == DiagnosticEngineMode.Local;
            ModeFree.IsChecked = mode == DiagnosticEngineMode.Free;
            ModeCloud.IsChecked = mode == DiagnosticEngineMode.Cloud;
            FreePanel.Visibility = mode == DiagnosticEngineMode.Free ? Visibility.Visible : Visibility.Collapsed;
            CloudPanel.Visibility = mode == DiagnosticEngineMode.Cloud ? Visibility.Visible : Visibility.Collapsed;

            var provider = ai.FreeProviderMode;
            UpdateEgressNote(mode, provider);
            FreeProviderZhipu.IsChecked = provider == FreeAiProvider.Zhipu;
            FreeProviderPollinations.IsChecked = provider == FreeAiProvider.Pollinations;
            FillFreeModelItems(provider, selectEffective: true);
            CloudUrlBox.Text = ai.Settings.CloudUrl;
            CloudKeyNameBox.Text = ai.Settings.CloudSecretKeyName;
            CloudModelBox.Text = ai.Settings.CloudModel;

            var ap = AppServices.Appearance;
            ThemeSystem.IsChecked = ap.Theme == AppTheme.System;
            ThemeLight.IsChecked = ap.Theme == AppTheme.Light;
            ThemeDark.IsChecked = ap.Theme == AppTheme.Dark;

            FontSm.IsChecked = ap.FontScale == AppFontScale.Sm;
            FontMd.IsChecked = ap.FontScale == AppFontScale.Md;
            FontLg.IsChecked = ap.FontScale == AppFontScale.Lg;
            FontXl.IsChecked = ap.FontScale == AppFontScale.Xl;

            HighContrastSwitch.IsChecked = ap.Settings.HighContrast;
            ReduceMotionSwitch.IsChecked = ap.Settings.ReduceMotion;

            BuildAccentSwatches();
            BuildLanguageSwitcher();

            // 自定义背景（v1.10）
            var bgMode = ap.Settings.BackgroundMode;
            BgDefault.IsChecked = bgMode == "default";
            BgColor.IsChecked = bgMode == "color";
            BgImage.IsChecked = bgMode == "image";
            BgColorPanel.Visibility = bgMode == "color" ? Visibility.Visible : Visibility.Collapsed;
            BgImagePanel.Visibility = bgMode == "image" ? Visibility.Visible : Visibility.Collapsed;
            SyncBgColorState();
            SyncBgImageState();
        }
        finally
        {
            _syncing = false;
        }

        ShellSync();
        RefreshCloudWarning();
    }

    /// <summary>云端选项没配全时给出提示：此时诊断会静默回退到本地引擎，不提示用户会以为云端已生效。</summary>
    private void RefreshCloudWarning()
        => CloudWarnText.Visibility =
            AppServices.AiSettings.Mode == DiagnosticEngineMode.Cloud && !AppServices.AiSettings.IsCloudConfigured
                ? Visibility.Visible
                : Visibility.Collapsed;

    /// <summary>刷新免费 AI 的本地限额展示（只读统计，不消耗额度；按当前选中通道展示对应上限）。</summary>
    private async Task RefreshFreeQuotaAsync()
    {
        try
        {
            var provider = AppServices.AiSettings.FreeProviderMode;
            var info = await AppServices.FreeLimiter.GetInfoAsync(provider);
            var channel = provider == FreeAiProvider.Pollinations ? Strings.T("settings.ai.free.channel.pollinations") : Strings.T("settings.ai.free.channel.zhipu");
            FreeQuotaText.Text = Strings.T("settings.ai.free.quota", channel, info.Used, info.Max, info.Remaining);
        }
        catch (Exception)
        {
            FreeQuotaText.Text = Strings.T("settings.ai.free.quotaUnavailable");
        }

        // 内置密钥状态：只展示「有没有」，绝不展示密钥本身；仅智谱通道需要密钥
        var keyMissing = AppServices.AiSettings.FreeProviderMode == FreeAiProvider.Zhipu && !AppServices.FreeAiKey.IsAvailable;
        FreeKeyStatusText.Text = keyMissing
            ? Strings.T("settings.ai.free.keyMissing")
            : "";
        FreeKeyStatusText.Visibility = keyMissing ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>只查询「有没有」，绝不把密钥取回 UI。</summary>
    private async Task RefreshKeyStatusAsync()
    {
        bool has;
        try
        {
            has = await AppServices.AiSettings.HasApiKeyAsync();
        }
        catch (Exception)
        {
            // 加密存储不可用时按「未保存」显示即可，真正保存失败时才需要打扰用户
            has = false;
        }

        KeyStatusText.Text = has ? Strings.T("settings.ai.cloud.statusSaved") : Strings.T("settings.ai.cloud.notSaved");
        KeyStatusText.SetResourceReference(TextBlock.ForegroundProperty, has ? "OkBrush" : "MutedBrush");
        KeyStatusPill.SetResourceReference(Border.BackgroundProperty, has ? "OkSoftBrush" : "Surface3Brush");
        ClearKeyButton.IsEnabled = has;
    }

    // ------------------------------------------------------------ 后台与遥控（托盘 / 热键）

    private void ShellSync()
    {
        _syncing = true;
        try
        {
            var tray = AppServices.Tray.Settings;
            CloseToTraySwitch.IsChecked = tray.CloseToTray;
            NotifyStateSwitch.IsChecked = tray.NotifyStateChange;
            RecordWatchdogSwitch.IsChecked = tray.RecordWatchdogEnabled;
            RealtimeLogAlertSwitch.IsChecked = tray.RealtimeLogAlertEnabled;

            var h = AppServices.Hotkeys.Settings;
            RecHotkeyEnabled.IsChecked = h.RecordEnabled;
            RecHotkeyCtrl.IsChecked = h.Record.Ctrl;
            RecHotkeyAlt.IsChecked = h.Record.Alt;
            RecHotkeyShift.IsChecked = h.Record.Shift;
            RecHotkeyWin.IsChecked = h.Record.Win;
            RecHotkeyKey.Text = h.Record.Key;

            StreamHotkeyEnabled.IsChecked = h.StreamEnabled;
            StreamHotkeyCtrl.IsChecked = h.Stream.Ctrl;
            StreamHotkeyAlt.IsChecked = h.Stream.Alt;
            StreamHotkeyShift.IsChecked = h.Stream.Shift;
            StreamHotkeyWin.IsChecked = h.Stream.Win;
            StreamHotkeyKey.Text = h.Stream.Key;

            VcamHotkeyEnabled.IsChecked = h.VirtualCamEnabled;
            VcamHotkeyCtrl.IsChecked = h.VirtualCam.Ctrl;
            VcamHotkeyAlt.IsChecked = h.VirtualCam.Alt;
            VcamHotkeyShift.IsChecked = h.VirtualCam.Shift;
            VcamHotkeyWin.IsChecked = h.VirtualCam.Win;
            VcamHotkeyKey.Text = h.VirtualCam.Key;

            // V3.0：存片热键（回放缓存）
            ReplayHotkeyEnabled.IsChecked = h.SaveReplayEnabled;
            ReplayHotkeyCtrl.IsChecked = h.SaveReplay.Ctrl;
            ReplayHotkeyAlt.IsChecked = h.SaveReplay.Alt;
            ReplayHotkeyShift.IsChecked = h.SaveReplay.Shift;
            ReplayHotkeyWin.IsChecked = h.SaveReplay.Win;
            ReplayHotkeyKey.Text = h.SaveReplay.Key;

            // V3.0（D3）：录制打点热键
            MarkHotkeyEnabled.IsChecked = h.MarkRecordingEnabled;
            MarkHotkeyCtrl.IsChecked = h.MarkRecording.Ctrl;
            MarkHotkeyAlt.IsChecked = h.MarkRecording.Alt;
            MarkHotkeyShift.IsChecked = h.MarkRecording.Shift;
            MarkHotkeyWin.IsChecked = h.MarkRecording.Win;
            MarkHotkeyKey.Text = h.MarkRecording.Key;

            WinHotkeyEnabled.IsChecked = h.ToggleWindowEnabled;
            WinHotkeyCtrl.IsChecked = h.ToggleWindow.Ctrl;
            WinHotkeyAlt.IsChecked = h.ToggleWindow.Alt;
            WinHotkeyShift.IsChecked = h.ToggleWindow.Shift;
            WinHotkeyWin.IsChecked = h.ToggleWindow.Win;
            WinHotkeyKey.Text = h.ToggleWindow.Key;

            MiniHotkeyEnabled.IsChecked = h.MiniWindowEnabled;
            MiniHotkeyCtrl.IsChecked = h.MiniWindow.Ctrl;
            MiniHotkeyAlt.IsChecked = h.MiniWindow.Alt;
            MiniHotkeyShift.IsChecked = h.MiniWindow.Shift;
            MiniHotkeyWin.IsChecked = h.MiniWindow.Win;
            MiniHotkeyKey.Text = h.MiniWindow.Key;

            AutoSwitchEnabled.IsChecked = AppServices.AutoSwitcher.Settings.Enabled;

            // 简单录像（V2.9.4）：预设下拉 + 是否允许文件通道时自动拉起 OBS
            SyncSimpleRecord();
        }
        finally
        {
            _syncing = false;
        }

        RefreshHotkeyDisplays();
        RefreshHotkeyStatus();
        RefreshAutoSwitchRules();
    }

    private void OnShellSettingToggled(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        var s = AppServices.Tray.Settings;
        s.CloseToTray = CloseToTraySwitch.IsChecked == true;
        s.NotifyStateChange = NotifyStateSwitch.IsChecked == true;
        var watchdogBefore = AppServices.RecordWatchdog.Enabled;
        var tailerBefore = AppServices.LogTailer.Enabled;
        s.RecordWatchdogEnabled = RecordWatchdogSwitch.IsChecked == true;
        s.RealtimeLogAlertEnabled = RealtimeLogAlertSwitch.IsChecked == true;
        AppServices.Tray.SaveSettings();

        // V2.8 守护开关即时生效
        if (AppServices.RecordWatchdog.Enabled != watchdogBefore) AppServices.RecordWatchdog.ApplyEnabled();
        if (AppServices.LogTailer.Enabled != tailerBefore) AppServices.LogTailer.ApplyEnabled();
    }

    // ------------------------------------------------------------ 简单录像（V2.9.4）

    /// <summary>把简单录像的两个设置同步到控件（下拉项按当前语言重建）。</summary>
    private void SyncSimpleRecord()
    {
        var svc = AppServices.SimpleRecord;

        SimplePresetBox.Items.Clear();
        foreach (var preset in SimpleRecordingCore.Presets)
        {
            SimplePresetBox.Items.Add(new ComboBoxItem
            {
                Content = Strings.T("simple.preset." + preset.Key),
                Tag = preset.Id
            });
        }

        var index = SimpleRecordingCore.Presets
            .Select((p, i) => (p, i))
            .FirstOrDefault(x => x.p.Id == svc.Preset).i;
        SimplePresetBox.SelectedIndex = index;

        SimpleAutoLaunchSwitch.IsChecked = AppServices.Tray.Settings.SimpleRecordAutoLaunch;
    }

    private void OnSimplePresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing) return;
        if (SimplePresetBox.SelectedItem is not ComboBoxItem item || item.Tag is not SimplePresetId id) return;

        // 服务自己负责持久化（与首页卡片共用同一个偏好键，两处不会各存一份）
        AppServices.SimpleRecord.Preset = id;
    }

    private void OnSimpleAutoLaunchToggled(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        AppServices.Tray.Settings.SimpleRecordAutoLaunch = SimpleAutoLaunchSwitch.IsChecked == true;
        AppServices.Tray.SaveSettings();
    }

    /// <summary>热键任一修饰键 / 主键 / 启用勾选变化：只刷新预览文本，注册在「保存」时统一做。</summary>
    private void OnHotkeyToggled(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        RefreshHotkeyDisplays();
    }

    private void OnHotkeyChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing) return;
        RefreshHotkeyDisplays();
    }

    private void RefreshHotkeyDisplays()
    {
        RecHotkeyDisplay.Text = HotkeyDisplay(RecHotkeyCtrl, RecHotkeyAlt, RecHotkeyShift, RecHotkeyWin, RecHotkeyKey);
        StreamHotkeyDisplay.Text = HotkeyDisplay(StreamHotkeyCtrl, StreamHotkeyAlt, StreamHotkeyShift, StreamHotkeyWin, StreamHotkeyKey);
        VcamHotkeyDisplay.Text = HotkeyDisplay(VcamHotkeyCtrl, VcamHotkeyAlt, VcamHotkeyShift, VcamHotkeyWin, VcamHotkeyKey);
        ReplayHotkeyDisplay.Text = HotkeyDisplay(ReplayHotkeyCtrl, ReplayHotkeyAlt, ReplayHotkeyShift, ReplayHotkeyWin, ReplayHotkeyKey);
        MarkHotkeyDisplay.Text = HotkeyDisplay(MarkHotkeyCtrl, MarkHotkeyAlt, MarkHotkeyShift, MarkHotkeyWin, MarkHotkeyKey);
        MiniHotkeyDisplay.Text = HotkeyDisplay(MiniHotkeyCtrl, MiniHotkeyAlt, MiniHotkeyShift, MiniHotkeyWin, MiniHotkeyKey);
        WinHotkeyDisplay.Text = HotkeyDisplay(WinHotkeyCtrl, WinHotkeyAlt, WinHotkeyShift, WinHotkeyWin, WinHotkeyKey);
    }

    private static string HotkeyDisplay(CheckBox ctrl, CheckBox alt, CheckBox shift, CheckBox win, TextBox key)
        => ReadBinding(ctrl, alt, shift, win, key).DisplayName;

    private static HotkeyBinding ReadBinding(CheckBox ctrl, CheckBox alt, CheckBox shift, CheckBox win, TextBox key)
        => new()
        {
            Ctrl = ctrl.IsChecked == true,
            Alt = alt.IsChecked == true,
            Shift = shift.IsChecked == true,
            Win = win.IsChecked == true,
            Key = key.Text.Trim()
        };

    private void OnSaveHotkeys(object sender, RoutedEventArgs e)
    {
        var h = AppServices.Hotkeys.Settings;
        h.RecordEnabled = RecHotkeyEnabled.IsChecked == true;
        h.Record = ReadBinding(RecHotkeyCtrl, RecHotkeyAlt, RecHotkeyShift, RecHotkeyWin, RecHotkeyKey);
        h.StreamEnabled = StreamHotkeyEnabled.IsChecked == true;
        h.Stream = ReadBinding(StreamHotkeyCtrl, StreamHotkeyAlt, StreamHotkeyShift, StreamHotkeyWin, StreamHotkeyKey);
        h.VirtualCamEnabled = VcamHotkeyEnabled.IsChecked == true;
        h.VirtualCam = ReadBinding(VcamHotkeyCtrl, VcamHotkeyAlt, VcamHotkeyShift, VcamHotkeyWin, VcamHotkeyKey);
        h.SaveReplayEnabled = ReplayHotkeyEnabled.IsChecked == true;
        h.SaveReplay = ReadBinding(ReplayHotkeyCtrl, ReplayHotkeyAlt, ReplayHotkeyShift, ReplayHotkeyWin, ReplayHotkeyKey);
        h.MarkRecordingEnabled = MarkHotkeyEnabled.IsChecked == true;
        h.MarkRecording = ReadBinding(MarkHotkeyCtrl, MarkHotkeyAlt, MarkHotkeyShift, MarkHotkeyWin, MarkHotkeyKey);
        h.MiniWindowEnabled = MiniHotkeyEnabled.IsChecked == true;
        h.MiniWindow = ReadBinding(MiniHotkeyCtrl, MiniHotkeyAlt, MiniHotkeyShift, MiniHotkeyWin, MiniHotkeyKey);
        h.ToggleWindowEnabled = WinHotkeyEnabled.IsChecked == true;
        h.ToggleWindow = ReadBinding(WinHotkeyCtrl, WinHotkeyAlt, WinHotkeyShift, WinHotkeyWin, WinHotkeyKey);

        AppServices.Hotkeys.SaveAndReapply();
        RefreshHotkeyStatus();
    }

    private void RefreshHotkeyStatus()
    {
        var errs = AppServices.Hotkeys.RegistrationErrors;
        HotkeyStatusText.Text = errs.Count == 0
            ? Strings.T("settings.hotkey.saved")
            : Strings.T("settings.hotkey.failed", string.Join("; ", errs));
        HotkeyStatusText.SetResourceReference(TextBlock.ForegroundProperty, errs.Count == 0 ? "OkBrush" : "WarnBrush");
    }

    // ------------------------------------------------------------ 场景自动切换

    private void OnAutoSwitchToggled(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        AppServices.AutoSwitcher.Settings.Enabled = AutoSwitchEnabled.IsChecked == true;
        AppServices.AutoSwitcher.Save();
    }

    private void OnAddAutoSwitchRule(object sender, RoutedEventArgs e)
    {
        AppServices.AutoSwitcher.Settings.Rules.Add(new AutoSwitchRule
        {
            Pattern = "",
            SceneName = AppServices.Obs.Scenes.FirstOrDefault()?.Name ?? ""
        });
        AppServices.AutoSwitcher.Save();
        RefreshAutoSwitchRules();
    }

    private void RefreshAutoSwitchRules()
    {
        _syncing = true;
        try
        {
            AutoSwitchRulesPanel.Children.Clear();
            var settings = AppServices.AutoSwitcher.Settings;
            if (settings.Rules.Count == 0)
            {
                AutoSwitchRulesPanel.Children.Add(new TextBlock
                {
                    Text = Strings.T("settings.autoSwitch.empty"),
                    Style = (Style)FindResource("MutedText")
                });
                return;
            }

            var sceneNames = AppServices.Obs.Scenes.Select(s => s.Name).ToList();
            foreach (var rule in settings.Rules)
                AutoSwitchRulesPanel.Children.Add(BuildRuleRow(rule, sceneNames));
        }
        finally
        {
            _syncing = false;
        }
    }

    private FrameworkElement BuildRuleRow(AutoSwitchRule rule, IReadOnlyList<string> sceneNames)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var enabled = new CheckBox
        {
            Style = (Style)FindResource("AppCheckBox"),
            IsChecked = rule.Enabled,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = Strings.T("settings.autoSwitch.enableRuleTip")
        };
        enabled.Checked += (_, _) => { rule.Enabled = true; AppServices.AutoSwitcher.Save(); };
        enabled.Unchecked += (_, _) => { rule.Enabled = false; AppServices.AutoSwitcher.Save(); };
        Grid.SetColumn(enabled, 0);

        var pattern = new TextBox
        {
            Style = (Style)FindResource("AppTextBox"),
            Text = rule.Pattern,
            Tag = rule,
            MaxLength = 80,
            MinWidth = 150,
            Margin = new Thickness(10, 0, 0, 0),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        pattern.TextChanged += (_, _) => { rule.Pattern = pattern.Text; AppServices.AutoSwitcher.Save(); };
        Grid.SetColumn(pattern, 1);

        var regex = new CheckBox
        {
            Content = Strings.T("settings.autoSwitch.regex"),
            Style = (Style)FindResource("AppCheckBox"),
            IsChecked = rule.UseRegex,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
            ToolTip = Strings.T("settings.autoSwitch.regexTip")
        };
        regex.Checked += (_, _) => { rule.UseRegex = true; AppServices.AutoSwitcher.Save(); };
        regex.Unchecked += (_, _) => { rule.UseRegex = false; AppServices.AutoSwitcher.Save(); };
        Grid.SetColumn(regex, 2);

        var scene = new ComboBox
        {
            Style = (Style)FindResource("AppComboBox"),
            Width = 170,
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsEnabled = sceneNames.Count > 0
        };
        scene.ItemsSource = sceneNames;
        scene.SelectedItem = sceneNames.FirstOrDefault(n => string.Equals(n, rule.SceneName, StringComparison.OrdinalIgnoreCase));
        scene.SelectionChanged += (_, _) =>
        {
            if (_syncing) return;
            rule.SceneName = scene.SelectedItem as string ?? "";
            AppServices.AutoSwitcher.Save();
        };
        scene.ToolTip = sceneNames.Count > 0 ? Strings.T("settings.autoSwitch.targetScene") : Strings.T("settings.autoSwitch.connectFirst");
        Grid.SetColumn(scene, 3);

        var delete = new Button
        {
            Content = "✕",
            Style = (Style)FindResource("GhostButton"),
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = Strings.T("settings.autoSwitch.deleteRule"),
            Tag = rule
        };
        delete.Click += (_, _) =>
        {
            AppServices.AutoSwitcher.Settings.Rules.Remove(rule);
            AppServices.AutoSwitcher.Save();
            RefreshAutoSwitchRules();
        };
        Grid.SetColumn(delete, 4);

        grid.Children.Add(enabled);
        grid.Children.Add(pattern);
        grid.Children.Add(regex);
        grid.Children.Add(scene);
        grid.Children.Add(delete);
        return grid;
    }

    private void RefreshDataSummary()
    {
        var count = AppServices.Bookmarks.GetAll().Count;
        ClearBookmarksButton.IsEnabled = count > 0;
        DataSummaryText.Text = count > 0
            ? Strings.T("settings.data.bookmarksCount", count)
            : Strings.T("settings.data.bookmarksEmpty");
    }

    private async Task RefreshAboutAsync()
    {
        var ver = typeof(SettingsPage).Assembly.GetName().Version;
        AppVersionText.Text = ver is null ? "1.0.0" : $"{ver.Major}.{ver.Minor}.{ver.Build}";

        var data = await AppServices.Problems.GetDataAsync();
        DataVersionText.Text = string.IsNullOrWhiteSpace(data.Version) ? "—" : data.Version;
        DataUpdatedText.Text = string.IsNullOrWhiteSpace(data.Updated) ? "—" : data.Updated;
    }

    // ------------------------------------------------------------ AI 诊断引擎

    private async void OnModeChecked(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;

        var mode = ReferenceEquals(sender, ModeFree) ? DiagnosticEngineMode.Free
            : ReferenceEquals(sender, ModeCloud) ? DiagnosticEngineMode.Cloud
            : DiagnosticEngineMode.Local;
        await AppServices.AiSettings.SetModeAsync(mode);

        FreePanel.Visibility = mode == DiagnosticEngineMode.Free ? Visibility.Visible : Visibility.Collapsed;
        CloudPanel.Visibility = mode == DiagnosticEngineMode.Cloud ? Visibility.Visible : Visibility.Collapsed;
        UpdateEgressNote(mode, AppServices.AiSettings.FreeProviderMode);
        RefreshCloudWarning();
        if (mode == DiagnosticEngineMode.Free) await RefreshFreeQuotaAsync();
        if (mode == DiagnosticEngineMode.Cloud) await RefreshKeyStatusAsync();
    }

    /// <summary>
    /// 按当前引擎模式更新「数据出站」提示的最后一行（V3.0 / B3）。
    ///
    /// 为什么要有这一行：上面两行说的是**一般情况**，而用户真正需要知道的是「我现在这个选择会怎样」。
    /// 免费通道与自建云端是两台不同的接收方，必须分别点名。
    /// </summary>
    private void UpdateEgressNote(DiagnosticEngineMode mode, FreeAiProvider provider)
    {
        if (AiEgressNowText is null) return;   // 初始化早期可能尚未构建

        AiEgressNowText.Text = mode switch
        {
            DiagnosticEngineMode.Local => Strings.T("settings.ai.egress.now.local"),
            DiagnosticEngineMode.Free => Strings.T("settings.ai.egress.now.free",
                Strings.T(provider == FreeAiProvider.Pollinations
                    ? "settings.ai.egress.host.pollinations"
                    : "settings.ai.egress.host.zhipu")),
            _ => Strings.T("settings.ai.egress.now.cloud", AppServices.AiSettings.Settings.CloudUrl)
        };
    }

    /// <summary>按通道填充模型下拉（数据源 = 服务端的线上可用白名单，避免两处维护漂移）。</summary>
    private void FillFreeModelItems(FreeAiProvider provider, bool selectEffective)
    {
        FreeModelBox.Items.Clear();
        var models = provider == FreeAiProvider.Pollinations
            ? AiSettingsService.KnownPollinationsModels
            : AiSettingsService.KnownFreeModels;
        foreach (var m in models)
        {
            FreeModelBox.Items.Add(new ComboBoxItem { Content = m, Tag = m });
        }

        if (!selectEffective) return;
        var effective = AppServices.AiSettings.EffectiveFreeModel;
        FreeModelBox.SelectedItem = FreeModelBox.Items
            .Cast<ComboBoxItem>()
            .FirstOrDefault(i => (i.Tag as string) == effective);
    }

    private async void OnFreeProviderChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;

        var provider = ReferenceEquals(sender, FreeProviderPollinations)
            ? FreeAiProvider.Pollinations
            : FreeAiProvider.Zhipu;
        await AppServices.AiSettings.SetFreeProviderAsync(provider);

        // 换通道后按新通道重填模型下拉并选中有效默认
        FillFreeModelItems(provider, selectEffective: true);
        await RefreshFreeQuotaAsync();
    }

    private async void OnSaveFreeModel(object sender, RoutedEventArgs e)
    {
        try
        {
            var model = (FreeModelBox.SelectedItem as ComboBoxItem)?.Tag as string
                        ?? AppServices.AiSettings.EffectiveFreeModel;
            await AppServices.AiSettings.SetFreeModelAsync(model);
            SetFreeStatus(Strings.T("settings.ai.free.modelSaved", AppServices.AiSettings.EffectiveFreeModel));
        }
        catch (Exception ex)
        {
            SetFreeStatus(Strings.T("settings.ai.free.modelSaveFailed", ex.Message));
        }
    }

    private void SetFreeStatus(string text)
    {
        FreeStatusText.Text = text;
        FreeStatusText.Visibility = Visibility.Visible;
    }

    private async void OnSaveCloud(object sender, RoutedEventArgs e)
    {
        try
        {
            await AppServices.AiSettings.SetCloudAsync(CloudUrlBox.Text, CloudKeyNameBox.Text, CloudModelBox.Text);
        }
        catch (ArgumentException ex)
        {
            // URL 不合规（非 https / 内网地址）：就地提示，不落盘
            SetAiStatus(ex.Message);
            return;
        }

        // 服务会把空键名回填成默认值，同步回输入框，免得用户以为没生效
        CloudKeyNameBox.Text = AppServices.AiSettings.Settings.CloudSecretKeyName;

        SetAiStatus(Strings.T("settings.ai.cloud.saved"));
        RefreshCloudWarning();

        // 键名可能刚被改过，密钥状态要按新键名重查
        await RefreshKeyStatusAsync();
    }

    private async void OnSaveKey(object sender, RoutedEventArgs e)
    {
        var key = ApiKeyBox.Password;
        if (string.IsNullOrWhiteSpace(key))
        {
            SetAiStatus(Strings.T("settings.ai.cloud.needKey"));
            return;
        }

        // 键名为空会存到一个取不回来的条目上，先补默认值并落盘
        if (string.IsNullOrWhiteSpace(CloudKeyNameBox.Text))
        {
            await AppServices.AiSettings.SetCloudAsync(CloudUrlBox.Text, "", CloudModelBox.Text);
            CloudKeyNameBox.Text = AppServices.AiSettings.Settings.CloudSecretKeyName;
        }

        SaveKeyButton.IsEnabled = false;
        bool ok;
        try
        {
            ok = await AppServices.AiSettings.SetApiKeyAsync(key);
        }
        finally
        {
            SaveKeyButton.IsEnabled = true;
        }

        // 无论成败都清空输入框，明文不在控件里多留一秒
        ApiKeyBox.Clear();

        if (ok)
        {
            SetAiStatus(Strings.T("settings.ai.cloud.keySaved"));
        }
        else
        {
            SetAiStatus(Strings.T("settings.ai.cloud.keySaveFailed"));
            App.ReportError(ErrorCodes.SecretStoreUnavailable);
        }

        RefreshCloudWarning();
        await RefreshKeyStatusAsync();
    }

    private async void OnClearKey(object sender, RoutedEventArgs e)
    {
        var name = string.IsNullOrWhiteSpace(CloudKeyNameBox.Text) ? "obs_ai_apikey" : CloudKeyNameBox.Text.Trim();
        if (!ConfirmDialog.Show(
                Strings.T("settings.ai.cloud.clearTitle"),
                Strings.T("settings.ai.cloud.clearMessage", name),
                Strings.T("settings.ai.cloud.clearButton"), Strings.T("common.cancel")))
        {
            return;
        }

        var ok = await AppServices.AiSettings.ClearApiKeyAsync();
        SetAiStatus(ok ? Strings.T("settings.ai.cloud.cleared") : Strings.T("settings.ai.cloud.clearFailed"));
        if (!ok) App.ReportError(ErrorCodes.SecretStoreUnavailable);

        await RefreshKeyStatusAsync();
    }

    private void SetAiStatus(string text)
    {
        AiStatusText.Text = text;
        AiStatusText.Visibility = Visibility.Visible;
    }

    // ------------------------------------------------------------ 外观与无障碍

    private void OnThemeChecked(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;

        AppServices.Appearance.SetTheme(
            ReferenceEquals(sender, ThemeLight) ? AppTheme.Light :
            ReferenceEquals(sender, ThemeDark) ? AppTheme.Dark :
            AppTheme.System);
    }

    private void OnFontScaleChecked(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;

        AppServices.Appearance.SetFontScale(
            ReferenceEquals(sender, FontSm) ? AppFontScale.Sm :
            ReferenceEquals(sender, FontLg) ? AppFontScale.Lg :
            ReferenceEquals(sender, FontXl) ? AppFontScale.Xl :
            AppFontScale.Md);
    }

    private void OnHighContrastToggled(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        AppServices.Appearance.SetHighContrast(HighContrastSwitch.IsChecked == true);
    }

    private void OnReduceMotionToggled(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        AppServices.Appearance.SetReduceMotion(ReduceMotionSwitch.IsChecked == true);
    }

    // ------------------------------------------------------------ 主题色（v2.7.1 强调色）

    /// <summary>按 AccentScheme.Catalog 生成色板圆点，并回填当前选中描边。</summary>
    private void BuildAccentSwatches()
    {
        AccentPanel.Children.Clear();
        foreach (var scheme in AccentScheme.Catalog)
        {
            var swatch = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 8, 8),
                Cursor = Cursors.Hand,
                Background = ParseHexBrush(scheme.Preview),
                ToolTip = $"{Strings.T("settings.accent." + scheme.Key)}（{scheme.Preview}）",
                Tag = scheme.Key,
            };
            swatch.MouseLeftButtonUp += OnAccentSwatchClick;
            AccentPanel.Children.Add(swatch);
        }
        SyncAccentSelection();
    }

    /// <summary>选中项用文字色加粗描边，未选中用分隔线细描边；走 DynamicResource，主题切换即时跟随。</summary>
    private void SyncAccentSelection()
    {
        var current = AppServices.Appearance.CurrentAccent.Key;
        foreach (Border swatch in AccentPanel.Children)
        {
            swatch.SetResourceReference(
                Border.BorderBrushProperty, Equals(swatch.Tag, current) ? "TextBrush" : "LineBrush");
            swatch.BorderThickness = new Thickness(Equals(swatch.Tag, current) ? 2 : 1);
        }
    }

    private void OnAccentSwatchClick(object sender, MouseButtonEventArgs e)
    {
        if (_syncing) return;
        if (sender is Border { Tag: string key })
        {
            AppServices.Appearance.SetAccent(key);
            SyncAccentSelection();
        }
    }

    private static SolidColorBrush ParseHexBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    // ------------------------------------------------------------ 语言（V2.9.2）

    /// <summary>
    /// 生成语言切换条：选项来自 <see cref="Strings.Supported"/>，与主题 / 字号用的是同一套段控样式。
    /// 切换后由 <c>LocalizationService</c> 即时改语言，主窗口负责把当前页面重新渲染一遍。
    /// </summary>
    private void BuildLanguageSwitcher()
    {
        LanguagePanel.Children.Clear();
        foreach (var language in Strings.Supported)
        {
            var radio = new RadioButton
            {
                GroupName = "SettingsLanguage",
                Style = (Style)FindResource("SegmentButton"),
                Content = Strings.DisplayName(language),
                Tag = language,
                IsChecked = string.Equals(Strings.Current, language, StringComparison.OrdinalIgnoreCase),
            };
            radio.Checked += OnLanguageChecked;
            LanguagePanel.Children.Add(radio);
        }
    }

    private void OnLanguageChecked(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        if (sender is not RadioButton { Tag: string language }) return;

        // 语言切换会重放当前页面（主窗口负责），本页不必自己刷新其余控件
        if (!AppServices.Localization.SetLanguage(language)) return;

        // 段控选中态在语言切换后仍需与真实语言一致（重放前先对齐，避免「点了没反应」的错觉）
        BuildLanguageSwitcher();
    }

    // ------------------------------------------------------------ 自定义背景（v1.10）

    private void OnBgModeChecked(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        AppServices.Appearance.SetBackgroundMode(
            ReferenceEquals(sender, BgColor) ? "color" :
            ReferenceEquals(sender, BgImage) ? "image" :
            "default");
        BgColorPanel.Visibility = ReferenceEquals(sender, BgColor) ? Visibility.Visible : Visibility.Collapsed;
        BgImagePanel.Visibility = ReferenceEquals(sender, BgImage) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnBgSwatchClick(object sender, MouseButtonEventArgs e)
    {
        if (_syncing || sender is not Border { Tag: string hex }) return;
        AppServices.Appearance.SetBackgroundColor(hex);
        SyncBgColorState();
    }

    /// <summary>
    /// 色板的键盘激活（V3.0 / E4）：Border 不是控件，回车/空格不会自动触发点击。
    /// 键盘用户此前**换不了背景色** —— 这属于「功能对某类用户直接不可用」，不是小瑕疵。
    /// </summary>
    private void OnBgSwatchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Space)) return;
        if (_syncing || sender is not Border { Tag: string hex }) return;

        e.Handled = true;
        AppServices.Appearance.SetBackgroundColor(hex);
        SyncBgColorState();
    }

    /// <summary>回填当前纯色选中态（高亮所选色块，显示色值）。</summary>
    private void SyncBgColorState()
    {
        var cur = AppServices.Appearance.Settings.BackgroundColor;
        foreach (var child in BgColorPanel.Children)
        {
            if (child is Border b && b.Tag is string hex)
            {
                var selected = string.Equals(hex, cur, StringComparison.OrdinalIgnoreCase);
                b.BorderBrush = selected
                    ? (System.Windows.Media.Brush)Application.Current.Resources["BrandBrush"]
                    : (System.Windows.Media.Brush)Application.Current.Resources["LineBrush"];
                b.BorderThickness = new Thickness(selected ? 2 : 1);
            }
        }
        BgColorHint.Text = string.Equals(cur, "#f4f4fb", StringComparison.OrdinalIgnoreCase)
            ? Strings.T("settings.bg.swatchTip")
            : Strings.T("settings.bg.current", cur);
    }

    private void OnPickBgImage(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = Strings.T("settings.bg.pickTitle"),
            Filter = Strings.T("settings.bg.pickFilter"),
            CheckFileExists = true
        };
        if (dlg.ShowDialog() == true)
        {
            AppServices.Appearance.SetBackgroundImage(dlg.FileName);
            SyncBgImageState();
        }
    }

    private void OnClearBgImage(object sender, RoutedEventArgs e)
    {
        AppServices.Appearance.SetBackgroundImage(null);
        SyncBgImageState();
    }

    /// <summary>回填图片路径与「清除」按钮显隐。</summary>
    private void SyncBgImageState()
    {
        var path = AppServices.Appearance.Settings.BackgroundImage;
        var has = !string.IsNullOrWhiteSpace(path);
        BgClearImageButton.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        BgImagePathText.Text = has
            ? Strings.T("settings.bg.currentImage", path)
            : Strings.T("settings.bg.noImage");
    }

    // ------------------------------------------------------------ 指引与本地数据

    private void OnOpenGuide(object sender, RoutedEventArgs e)
        => AppServices.Navigation.Navigate(Routes.Guide);

    private void OnOpenLogs(object sender, RoutedEventArgs e)
        => AppServices.Navigation.Navigate(Routes.Logs);

    private void OnClearBookmarks(object sender, RoutedEventArgs e)
    {
        var count = AppServices.Bookmarks.GetAll().Count;
        if (!ConfirmDialog.Show(
                Strings.T("settings.data.clearBookmarksTitle"),
                Strings.T("settings.data.clearBookmarksMessage", count),
                Strings.T("settings.data.clearButton"), Strings.T("common.cancel")))
        {
            return;
        }

        AppServices.Bookmarks.Clear();
        RefreshDataSummary();
        SetDataStatus(Strings.T("settings.data.bookmarksCleared"));
    }

    private void OnClearSteps(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDialog.Show(
                Strings.T("settings.data.resetStepsTitle"),
                Strings.T("settings.data.resetStepsMessage"),
                Strings.T("settings.data.resetButton"), Strings.T("common.cancel")))
        {
            return;
        }

        AppServices.Bookmarks.ClearAllSteps();
        RefreshDataSummary();
        SetDataStatus(Strings.T("settings.data.stepsReset"));
    }

    private void SetDataStatus(string text)
    {
        DataStatusText.Text = text;
        DataStatusText.Visibility = Visibility.Visible;
    }

    // ------------------------------------------------------------ 新手引导（V2.9.0）

    /// <summary>
    /// 「重新展示引导」：通知主窗口立即重播覆盖层，并顺手把「已完成」标记清掉 ——
    /// 否则用户重看后中途关掉窗口，下次启动又不会展示（标记仍为已完成）。
    /// </summary>
    private void OnReplayOnboarding(object sender, RoutedEventArgs e)
    {
        AppServices.Store.RemoveItem(OnboardingGuide.PrefKey);
        App.RequestOnboardingReset();

        OnboardingStatusText.Text = Strings.T("settings.onboarding.replayed");
        OnboardingStatusText.Visibility = Visibility.Visible;
    }

    // ------------------------------------------------------------ 关于

    private async void OnOpenLink(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string url })
        {
            await AppServices.Host.OpenExternalAsync(url);
        }
    }

    /// <summary>把最近一次更新检查结果回填到状态文本（启动检查或手动检查后调用）。</summary>
    private void RefreshUpdateStatus()
    {
        var last = AppServices.Updates.LastResult;
        if (last is null)
        {
            UpdateStatusText.Text = Strings.T("settings.about.notChecked");
            UpdateStatusText.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            return;
        }

        switch (last.Status)
        {
            case UpdateCheckStatus.UpToDate:
                UpdateStatusText.Text = Strings.T("settings.update.upToDate", CurrentVersionText(last.CurrentVersion));
                UpdateStatusText.SetResourceReference(TextBlock.ForegroundProperty, "OkBrush");
                break;
            case UpdateCheckStatus.UpdateAvailable:
                UpdateStatusText.Text = Strings.T("settings.update.available", CurrentVersionText(last.LatestVersion));
                UpdateStatusText.SetResourceReference(TextBlock.ForegroundProperty, "OkBrush");
                break;
            default:
                UpdateStatusText.Text = Strings.T("settings.update.failed");
                UpdateStatusText.SetResourceReference(TextBlock.ForegroundProperty, "WarnBrush");
                break;
        }
    }

    private static string CurrentVersionText(Version? v)
        => v is null ? "—" : $"{v.Major}.{v.Minor}.{v.Build}";

    private async void OnCheckUpdate(object sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        UpdateStatusText.Text = Strings.T("settings.update.checking");
        UpdateStatusText.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");

        try
        {
            var result = await AppServices.Updates.CheckAsync();
            RefreshUpdateStatus();
            if (result.Status == UpdateCheckStatus.UpdateAvailable)
            {
                // 弹窗本身（XAML/下载）出错也要兜底，别让 async void 把整个应用带崩
                try
                {
                    var choice = UpdateDialog.Show(result.CurrentVersion, result.LatestVersion);
                    if (choice == UpdateDialogResult.Applying)
                    {
                        // 增量更新已就绪：退出应用，自举进程完成替换后自动重启
                        Application.Current?.Shutdown();
                    }
                }
                catch (Exception ex)
                {
                    UpdateStatusText.Text = Strings.T("settings.update.openDialogFailed", ex.Message);
                    UpdateStatusText.SetResourceReference(TextBlock.ForegroundProperty, "WarnBrush");
                }
            }
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = Strings.T("settings.update.checkFailed", ex.Message);
            UpdateStatusText.SetResourceReference(TextBlock.ForegroundProperty, "WarnBrush");
        }
        finally
        {
            CheckUpdateButton.IsEnabled = true;
        }
    }

    // ------------------------------------------------------------ 知识库分离更新

    /// <summary>手动检查并应用知识库更新（绕过启动节流，立即联网拉取）。问题库与插件目录两条通道都查。</summary>
    private async void OnCheckKbUpdate(object sender, RoutedEventArgs e)
    {
        CheckKbButton.IsEnabled = false;
        KbStatusText.Text = Strings.T("settings.kb.checking");

        try
        {
            var (updated, newVersion, message) = await AppServices.Kb.RefreshAsync(manual: true);
            if (updated)
            {
                AppServices.Problems.Reload();
                await RefreshAboutAsync(); // 问题库版本 / 更新日期显示同步刷新
                KbStatusText.Text = Strings.T("settings.kb.updated", newVersion);
                KbStatusText.SetResourceReference(TextBlock.ForegroundProperty, "OkBrush");
            }
            else if (message is not null)
            {
                KbStatusText.Text = message;
                KbStatusText.SetResourceReference(TextBlock.ForegroundProperty, "WarnBrush");
            }
            else
            {
                KbStatusText.Text = Strings.T("settings.kb.upToDate", newVersion);
                KbStatusText.SetResourceReference(TextBlock.ForegroundProperty, "OkBrush");
            }

            // 插件目录（V2.2 P0-3）：与问题库同通道，手动检查时一并刷新
            var (pluginsUpdated, pluginsVersion, pluginsMessage) = await AppServices.Kb.RefreshPluginsAsync(manual: true);
            if (pluginsUpdated)
            {
                AppServices.PluginCatalog.Reload();
                var prefix = KbStatusText.Text.Length > 0 ? KbStatusText.Text + "；" : "";
                KbStatusText.Text = prefix + Strings.T("settings.kb.pluginsUpdated", pluginsVersion);
                KbStatusText.SetResourceReference(TextBlock.ForegroundProperty, "OkBrush");
            }
            else if (pluginsMessage is not null && message is null)
            {
                // 问题库正常而插件目录通道异常时补充提示
                KbStatusText.Text += Strings.T("settings.kb.pluginsSuffix", pluginsMessage);
                KbStatusText.SetResourceReference(TextBlock.ForegroundProperty, "WarnBrush");
            }
        }
        catch (Exception ex)
        {
            KbStatusText.Text = Strings.T("settings.kb.failed", ex.Message);
            KbStatusText.SetResourceReference(TextBlock.ForegroundProperty, "WarnBrush");
        }
        finally
        {
            CheckKbButton.IsEnabled = true;
        }
    }

    // ------------------------------------------------------------ OBS 配置管理

    private void OnOpenObsConfig(object sender, RoutedEventArgs e)
        => AppServices.Navigation.Navigate(Routes.ObsConfig);

    private void OnOpenTemplates(object sender, RoutedEventArgs e)
        => AppServices.Navigation.Navigate(Routes.Templates);

    private async Task RefreshObsConfigHintAsync()
    {
        try
        {
            var loc = await AppServices.ObsPaths.LocateAsync();
            if (loc.Exists)
            {
                ObsConfigHintText.Text = Strings.T("settings.obsconfig.found", loc.ConfigDir);
            }
            else
            {
                ObsConfigHintText.Text = Strings.T("settings.obsconfig.notFound");
            }
        }
        catch (Exception ex)
        {
            ObsConfigHintText.Text = Strings.T("settings.obsconfig.error", ex.Message);
        }
    }
}
