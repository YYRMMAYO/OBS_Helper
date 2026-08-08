using System.Windows;
using System.Windows.Controls;
using OBS_Helper.Wpf.Controls;
using OBS_Helper.Wpf.Errors;
using OBS_Helper.Wpf.Models.Shell;
using OBS_Helper.Wpf.Navigation;
using OBS_Helper.Wpf.Services;
using OBS_Helper.Wpf.Services.Ai;

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
            var channel = provider == FreeAiProvider.Pollinations ? "Pollinations（国外免 Key）" : "智谱免费 AI";
            FreeQuotaText.Text = $"今日本地限额（{channel}）：已用 {info.Used} / {info.Max} 次（{info.Remaining} 次剩余，每天 0 点重置）。";
        }
        catch (Exception)
        {
            FreeQuotaText.Text = "今日本地限额：无法读取（不影响使用，额度仍按每日上限强制）。";
        }

        // 内置密钥状态：只展示「有没有」，绝不展示密钥本身；仅智谱通道需要密钥
        var keyMissing = AppServices.AiSettings.FreeProviderMode == FreeAiProvider.Zhipu && !AppServices.FreeAiKey.IsAvailable;
        FreeKeyStatusText.Text = keyMissing
            ? "内置密钥：未打包（智谱通道不可用，会自动回退本地引擎；可改用 Pollinations 通道或换官方安装包）。"
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

        KeyStatusText.Text = has ? "已保存" : "未保存";
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
            ? "热键已保存并生效。"
            : "注意：" + string.Join("；", errs);
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
                    Text = "还没有规则。点「＋ 添加规则」开始，比如：窗口标题含「游戏名」→ 切到「游戏」场景。",
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
            ToolTip = "启用该规则"
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
            Content = "正则",
            Style = (Style)FindResource("AppCheckBox"),
            IsChecked = rule.UseRegex,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
            ToolTip = "按正则表达式匹配窗口标题"
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
        scene.ToolTip = sceneNames.Count > 0 ? "目标场景" : "请先连接 OBS 获取场景列表";
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
            ToolTip = "删除规则",
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
            ? $"收藏 {count} 条；步骤勾选进度同样只保存在本机，不会上传。"
            : "暂无收藏；步骤勾选进度只保存在本机，不会上传。";
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
        RefreshCloudWarning();
        if (mode == DiagnosticEngineMode.Free) await RefreshFreeQuotaAsync();
        if (mode == DiagnosticEngineMode.Cloud) await RefreshKeyStatusAsync();
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
            SetFreeStatus($"免费 AI 模型已保存为「{AppServices.AiSettings.EffectiveFreeModel}」。");
        }
        catch (Exception ex)
        {
            SetFreeStatus("保存模型失败：" + ex.Message);
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

        SetAiStatus("云端配置已保存。");
        RefreshCloudWarning();

        // 键名可能刚被改过，密钥状态要按新键名重查
        await RefreshKeyStatusAsync();
    }

    private async void OnSaveKey(object sender, RoutedEventArgs e)
    {
        var key = ApiKeyBox.Password;
        if (string.IsNullOrWhiteSpace(key))
        {
            SetAiStatus("请先粘贴 API Key 再保存。");
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
            SetAiStatus("API Key 已加密保存到本机。");
        }
        else
        {
            SetAiStatus("保存失败：本机加密存储不可用。");
            App.ReportError(ErrorCodes.SecretStoreUnavailable);
        }

        RefreshCloudWarning();
        await RefreshKeyStatusAsync();
    }

    private async void OnClearKey(object sender, RoutedEventArgs e)
    {
        var name = string.IsNullOrWhiteSpace(CloudKeyNameBox.Text) ? "obs_ai_apikey" : CloudKeyNameBox.Text.Trim();
        if (!ConfirmDialog.Show(
                "清除 API Key",
                $"将从本机加密存储中删除「{name}」下保存的密钥。清除后云端诊断会回退到本地引擎，需要重新粘贴密钥才能恢复。",
                "清除", "取消"))
        {
            return;
        }

        var ok = await AppServices.AiSettings.ClearApiKeyAsync();
        SetAiStatus(ok ? "已清除本机保存的 API Key。" : "清除失败：本机加密存储不可用。");
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

    // ------------------------------------------------------------ 指引与本地数据

    private void OnOpenGuide(object sender, RoutedEventArgs e)
        => AppServices.Navigation.Navigate(Routes.Guide);

    private void OnOpenLogs(object sender, RoutedEventArgs e)
        => AppServices.Navigation.Navigate(Routes.Logs);

    private void OnClearBookmarks(object sender, RoutedEventArgs e)
    {
        var count = AppServices.Bookmarks.GetAll().Count;
        if (!ConfirmDialog.Show(
                "清空收藏",
                $"将删除全部 {count} 条收藏，此操作无法撤销。步骤勾选进度不受影响。",
                "清空", "取消"))
        {
            return;
        }

        AppServices.Bookmarks.Clear();
        RefreshDataSummary();
        SetDataStatus("收藏已清空。");
    }

    private void OnClearSteps(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDialog.Show(
                "重置步骤进度",
                "将清除所有问题下已勾选的排查步骤，此操作无法撤销。收藏不受影响。",
                "重置", "取消"))
        {
            return;
        }

        AppServices.Bookmarks.ClearAllSteps();
        RefreshDataSummary();
        SetDataStatus("步骤进度已重置。");
    }

    private void SetDataStatus(string text)
    {
        DataStatusText.Text = text;
        DataStatusText.Visibility = Visibility.Visible;
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
            UpdateStatusText.Text = "尚未检查";
            UpdateStatusText.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            return;
        }

        switch (last.Status)
        {
            case UpdateCheckStatus.UpToDate:
                UpdateStatusText.Text = $"已是最新版本 V{CurrentVersionText(last.CurrentVersion)}";
                UpdateStatusText.SetResourceReference(TextBlock.ForegroundProperty, "OkBrush");
                break;
            case UpdateCheckStatus.UpdateAvailable:
                UpdateStatusText.Text = $"发现新版本 V{CurrentVersionText(last.LatestVersion)}，可下载更新";
                UpdateStatusText.SetResourceReference(TextBlock.ForegroundProperty, "OkBrush");
                break;
            default:
                UpdateStatusText.Text = "检查失败，请稍后重试";
                UpdateStatusText.SetResourceReference(TextBlock.ForegroundProperty, "WarnBrush");
                break;
        }
    }

    private static string CurrentVersionText(Version? v)
        => v is null ? "—" : $"{v.Major}.{v.Minor}.{v.Build}";

    private async void OnCheckUpdate(object sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        UpdateStatusText.Text = "正在检查更新…";
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
                    UpdateDialog.Show(result.CurrentVersion, result.LatestVersion);
                }
                catch (Exception ex)
                {
                    UpdateStatusText.Text = "打开更新窗口失败：" + ex.Message;
                    UpdateStatusText.SetResourceReference(TextBlock.ForegroundProperty, "WarnBrush");
                }
            }
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = "检查更新失败：" + ex.Message;
            UpdateStatusText.SetResourceReference(TextBlock.ForegroundProperty, "WarnBrush");
        }
        finally
        {
            CheckUpdateButton.IsEnabled = true;
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
                ObsConfigHintText.Text = $"已找到 OBS 配置目录：{loc.ConfigDir}";
            }
            else
            {
                ObsConfigHintText.Text = "未找到 OBS 配置目录。请确认已安装并至少启动过一次 OBS，再进入本页刷新。";
            }
        }
        catch (Exception ex)
        {
            ObsConfigHintText.Text = $"检测 OBS 配置目录时出错：{ex.Message}";
        }
    }
}
