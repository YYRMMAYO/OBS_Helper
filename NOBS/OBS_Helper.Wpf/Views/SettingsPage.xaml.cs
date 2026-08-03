using System.Windows;
using System.Windows.Controls;
using OBS_Helper.Wpf.Controls;
using OBS_Helper.Wpf.Errors;
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

        SyncControls();
        RefreshDataSummary();

        await RefreshKeyStatusAsync();
        await RefreshAboutAsync();
    }

    // ------------------------------------------------------------ 状态回填

    private void SyncControls()
    {
        _syncing = true;
        try
        {
            var ai = AppServices.AiSettings;
            var cloud = ai.Mode == DiagnosticEngineMode.Cloud;
            ModeLocal.IsChecked = !cloud;
            ModeCloud.IsChecked = cloud;
            CloudPanel.Visibility = cloud ? Visibility.Visible : Visibility.Collapsed;

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

        RefreshCloudWarning();
    }

    /// <summary>云端选项没配全时给出提示：此时诊断会静默回退到本地引擎，不提示用户会以为云端已生效。</summary>
    private void RefreshCloudWarning()
        => CloudWarnText.Visibility =
            AppServices.AiSettings.Mode == DiagnosticEngineMode.Cloud && !AppServices.AiSettings.IsCloudConfigured
                ? Visibility.Visible
                : Visibility.Collapsed;

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

        var cloud = ReferenceEquals(sender, ModeCloud);
        await AppServices.AiSettings.SetModeAsync(cloud ? DiagnosticEngineMode.Cloud : DiagnosticEngineMode.Local);

        CloudPanel.Visibility = cloud ? Visibility.Visible : Visibility.Collapsed;
        RefreshCloudWarning();
        if (cloud) await RefreshKeyStatusAsync();
    }

    private async void OnSaveCloud(object sender, RoutedEventArgs e)
    {
        await AppServices.AiSettings.SetCloudAsync(CloudUrlBox.Text, CloudKeyNameBox.Text, CloudModelBox.Text);

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
}
