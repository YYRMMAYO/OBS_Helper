using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using OBS_Helper.Wpf.Localization;
using OBS_Helper.Wpf.Models.Live;
using OBS_Helper.Wpf.Models.ObsConfig;
using OBS_Helper.Wpf.Navigation;
using OBS_Helper.Wpf.Services.Live;

namespace OBS_Helper.Wpf.Views;

/// <summary>
/// 虚拟直播工作台（V3.3.0）：形象（Live2D）/ 直播间（模板）/ 开播（平台引导）三个 Tab。
///
/// 设计取舍：
/// <list type="bullet">
///   <item>不新增三个一级导航，避免侧栏膨胀（老年模式尤其需要克制）；</item>
///   <item>房间模板复用 <c>SceneTemplateService</c> 的落地链路，本页只做筛选与检查清单；</item>
///   <item>所有失败都落到具体错误码 + 一句下一步，不把「部分成功」显示成成功；</item>
///   <item>开播页的推流码只进 PasswordBox / 内存，界面只显示长度遮罩。</item>
/// </list>
/// </summary>
public partial class VirtualLivePage : UserControl, INavigationAware
{
    private const string VtsTokenSecretKey = "live2d.vts.token";

    private string? _modelJson;
    private string _modelName = "";
    private Live2DEnvStatus? _env;
    private IReadOnlyList<SceneTemplate> _roomTemplates = Array.Empty<SceneTemplate>();
    private IReadOnlyList<LivePlatform> _platforms = Array.Empty<LivePlatform>();
    private LivePlatform? _currentPlatform;
    private bool _busy;

    public VirtualLivePage()
    {
        InitializeComponent();
        BuildLayoutCombo();
    }

    public async Task OnNavigatedToAsync(object? parameter)
    {
        SelectTab(parameter as string);
        await RefreshEnvironmentAsync().ConfigureAwait(true);
        await LoadRoomsAsync().ConfigureAwait(true);
        LoadPlatforms();
    }

    // ------------------------------------------------------------ 通用

    private void SelectTab(string? parameter)
    {
        switch ((parameter ?? "").Trim().ToLowerInvariant())
        {
            case "room":
                RoomTab.IsChecked = true;
                break;
            case "golive":
                GoLiveTab.IsChecked = true;
                break;
            default:
                AvatarTab.IsChecked = true;
                break;
        }
        ApplyTabVisibility();
    }

    /// <summary>三个 Tab 用统一的 SegmentButton 实现，切换时只改面板显隐（不重建内容）。</summary>
    private void OnTabChecked(object sender, RoutedEventArgs e) => ApplyTabVisibility();

    private void ApplyTabVisibility()
    {
        AvatarPanel.Visibility = AvatarTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        RoomPanel.Visibility = RoomTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        GoLivePanel.Visibility = GoLiveTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        ApplyLive2DButton.IsEnabled = !busy && _modelJson is not null;
        ChooseModelButton.IsEnabled = !busy;
        RefreshEnvButton.IsEnabled = !busy;
        LoadVtsButton.IsEnabled = !busy && _modelJson is not null;
        StartDirectButton.IsEnabled = !busy;
        StopDirectButton.IsEnabled = !busy;
        StartVcamButton.IsEnabled = !busy;
        StopVcamButton.IsEnabled = !busy;
    }

    private static TextBlock Bullet(string text, string brushKey = "TextBrush")
    {
        var tb = new TextBlock
        {
            Text = "• " + text,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 5),
            FontSize = (double)Application.Current.Resources["FontSizeSm"]
        };
        tb.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        return tb;
    }

    private static string ShortError(string? code, string prefix)
    {
        if (string.IsNullOrWhiteSpace(code)) return "";
        var clean = code.Trim();
        if (clean.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) clean = clean[prefix.Length..];
        return clean.ToLowerInvariant().Replace('_', '.');
    }

    // ------------------------------------------------------------ 形象 / Live2D

    private void BuildLayoutCombo()
    {
        LayoutCombo.Items.Clear();
        foreach (var layout in Live2DModelCore.Layouts)
            LayoutCombo.Items.Add(Strings.T(layout.TitleKey));
        LayoutCombo.SelectedIndex = 1; // 默认半身
    }

    private async Task RefreshEnvironmentAsync()
    {
        try
        {
            _env = await AppServices.Live2D.CheckEnvironmentAsync(CancellationToken.None).ConfigureAwait(true);
            ObsStateText.Text = Strings.T(_env.ObsConnected ? "vlive.env.obs.ok" : "vlive.env.obs.no");
            VtsStateText.Text = Strings.T(_env.VtsRunning ? "vlive.env.vts.ok" : "vlive.env.vts.no");
            CaptureStateText.Text = Strings.T(_env.CaptureHintKey);
            CanvasStateText.Text = $"{_env.CanvasWidth} × {_env.CanvasHeight}";
        }
        catch (Exception ex)
        {
            Live2DStatusText.Text = Strings.T("vlive.env.failed", ex.Message);
        }
    }

    private async void OnRefreshEnvClick(object sender, RoutedEventArgs e)
        => await RefreshEnvironmentAsync();

    private void OnChooseModelClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = Strings.T("vlive.model.chooseTitle"),
            Filter = "Live2D 模型 (*.model3.json)|*.model3.json|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() != true) return;

        if (!Live2DModelCore.TryValidate(dialog.FileName, out var model, out var error))
        {
            _modelJson = null;
            _modelName = "";
            ModelPathText.Text = Strings.T("live2d.err." + ShortError(error, "L2D_"));
            ApplyLive2DButton.IsEnabled = false;
            LoadVtsButton.IsEnabled = false;
            return;
        }

        _modelJson = model!.ModelJsonPath;
        _modelName = model.Name;
        ModelPathText.Text = Strings.T("vlive.model.selected", model.Name, model.FileCount, model.TotalBytes / 1024 / 1024);
        ApplyLive2DButton.IsEnabled = !_busy;
        LoadVtsButton.IsEnabled = !_busy;
        Live2DStatusText.Text = "";
        Live2DStatusList.Children.Clear();
    }

    private async void OnApplyLive2DClick(object sender, RoutedEventArgs e)
    {
        if (_busy || _modelJson is null) return;

        var layout = Live2DModelCore.Layouts[Math.Clamp(LayoutCombo.SelectedIndex, 0, Live2DModelCore.Layouts.Count - 1)];
        if (!Controls.ConfirmDialog.Show(
                Strings.T("vlive.apply.title"),
                Strings.T("vlive.apply.confirm", _modelName, Strings.T(layout.TitleKey)),
                Strings.T("vlive.apply.ok"), Strings.T("common.cancel"), danger: false))
            return;

        SetBusy(true);
        Live2DStatusList.Children.Clear();
        Live2DStatusText.Text = Strings.T("common.busy");
        try
        {
            var progress = new Progress<string>(msg =>
            {
                Live2DStatusList.Children.Add(Bullet(msg, "MutedBrush"));
            });

            var result = await AppServices.Live2D.ApplyAsync(_modelJson, layout.Id, progress, CancellationToken.None)
                .ConfigureAwait(true);

            if (!result.Ok)
            {
                Live2DStatusText.Text = Strings.T("live2d.err." + ShortError(result.ErrorCode, "L2D_"));
                foreach (var note in result.Notes) Live2DStatusList.Children.Add(Bullet(note, "WarnBrush"));
                AppServices.Toast.Show(Live2DStatusText.Text, "danger");
                return;
            }

            Live2DStatusText.Text = Strings.T("vlive.apply.done", result.SceneName);
            foreach (var note in result.Notes) Live2DStatusList.Children.Add(Bullet(note, "MutedBrush"));
            AppServices.Toast.Show(Strings.T("vlive.apply.done", result.SceneName), "ok");
        }
        catch (Exception ex)
        {
            Live2DStatusText.Text = Strings.T("live2d.err.unexpected") + " " + ex.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnLoadVtsClick(object sender, RoutedEventArgs e)
    {
        if (_busy || _modelJson is null) return;

        SetBusy(true);
        Live2DStatusText.Text = Strings.T("common.busy");
        try
        {
            string? token = null;
            try { token = await AppServices.Host.GetSecretAsync(VtsTokenSecretKey).ConfigureAwait(true); }
            catch (Exception) { /* 密钥库不可用时走首次授权 */ }

            var (ok, error, folder) = await AppServices.Live2D
                .LoadInVtsAsync(_modelName, token, t => AppServices.Host.SetSecretAsync(VtsTokenSecretKey, t),
                    CancellationToken.None)
                .ConfigureAwait(true);

            if (ok)
            {
                Live2DStatusText.Text = Strings.T("vlive.vts.done", _modelName);
                AppServices.Toast.Show(Live2DStatusText.Text, "ok");
            }
            else
            {
                Live2DStatusText.Text = Strings.T("live2d.err." + ShortError(error, "L2D_"));
                if (!string.IsNullOrWhiteSpace(folder))
                    Live2DStatusList.Children.Add(Bullet(Strings.T("vlive.vts.folder", folder), "MutedBrush"));
                AppServices.Toast.Show(Live2DStatusText.Text, "warn");
            }
        }
        catch (Exception ex)
        {
            Live2DStatusText.Text = Strings.T("live2d.err.unexpected") + " " + ex.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    // ------------------------------------------------------------ 直播间

    private async Task LoadRoomsAsync()
    {
        try
        {
            var all = await AppServices.Templates.LoadAsync().ConfigureAwait(true);
            _roomTemplates = AppServices.Rooms.FilterVtuber(all);
        }
        catch (Exception ex)
        {
            _roomTemplates = Array.Empty<SceneTemplate>();
            RoomList.Children.Add(Bullet(Strings.T("vlive.room.loadFailed", ex.Message), "WarnBrush"));
            return;
        }

        RoomList.Children.Clear();
        if (_roomTemplates.Count == 0)
        {
            RoomList.Children.Add(Bullet(Strings.T("vlive.room.empty"), "MutedBrush"));
            return;
        }

        foreach (var template in _roomTemplates)
            RoomList.Children.Add(BuildRoomCard(template));
    }

    private FrameworkElement BuildRoomCard(SceneTemplate template)
    {
        var card = new Border
        {
            Style = (Style)Application.Current.Resources["Card"],
            Margin = new Thickness(0, 0, 0, 12)
        };

        var stack = new StackPanel();
        card.Child = stack;

        var title = new TextBlock
        {
            Text = $"{template.Icon}  {template.Title}",
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
        title.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeMd");
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        stack.Children.Add(title);

        var summary = new TextBlock
        {
            Text = template.Summary,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        };
        summary.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSm");
        summary.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        stack.Children.Add(summary);

        var meta = new TextBlock
        {
            Text = Strings.T("vlive.room.meta", template.Scenes.Count,
                template.Scenes.Sum(s => s.Sources.Count), template.Canvas.BaseWidth, template.Canvas.BaseHeight),
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        meta.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeXs");
        meta.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        stack.Children.Add(meta);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var apply = new Button
        {
            Content = Strings.T("vlive.room.apply"),
            Style = (Style)Application.Current.Resources["PrimaryButton"],
            Tag = template
        };
        apply.Click += OnApplyRoomClick;
        actions.Children.Add(apply);

        var checklist = new Button
        {
            Content = Strings.T("vlive.room.checklist"),
            Style = (Style)Application.Current.Resources["GhostButton"],
            Margin = new Thickness(10, 0, 0, 0),
            Tag = template
        };
        checklist.Click += OnShowRoomChecklistClick;
        actions.Children.Add(checklist);

        stack.Children.Add(actions);
        return card;
    }

    private void OnShowRoomChecklistClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SceneTemplate template })
            ShowChecklist(template, Array.Empty<string>());
    }

    private async void OnApplyRoomClick(object sender, RoutedEventArgs e)
    {
        if (_busy || sender is not Button { Tag: SceneTemplate template }) return;

        if (!AppServices.Obs.IsConnected)
        {
            AppServices.Toast.Show(Strings.T("vlive.room.notConnected"), "warn");
            return;
        }

        if (!Controls.ConfirmDialog.Show(
                Strings.T("vlive.room.applyTitle", template.Title),
                Strings.T("vlive.room.applyConfirm", template.Scenes.Count,
                    template.Scenes.Sum(s => s.Sources.Count)),
                Strings.T("vlive.room.applyOk"), Strings.T("common.cancel"), danger: false))
            return;

        SetBusy(true);
        AppServices.Toast.Show(Strings.T("common.busy"), "info");
        try
        {
            var progress = new Progress<string>(msg => AppServices.Toast.Show(msg, "info"));
            var result = await AppServices.Templates
                .ApplyAsync(template.Id, applyCanvas: true, CancellationToken.None, progress)
                .ConfigureAwait(true);

            if (!result.Ok)
            {
                AppServices.Toast.Show(Strings.T("vlive.room.failed", result.Error ?? ""), "danger");
                return;
            }

            ShowChecklist(template, result.Placeholders);
            AppServices.Toast.Show(Strings.T("vlive.room.applied", template.Title), "ok");
        }
        catch (Exception ex)
        {
            AppServices.Toast.Show(Strings.T("vlive.room.failed", ex.Message), "danger");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ShowChecklist(SceneTemplate template, IReadOnlyList<string> placeholders)
    {
        ChecklistPanel.Visibility = Visibility.Visible;
        ChecklistList.Children.Clear();
        ChecklistIntroText.Text = Strings.T("vlive.checklist.intro", template.Title);

        foreach (var item in RoomOverlayCore.BuildChecklist(template))
        {
            ChecklistList.Children.Add(Bullet(
                Strings.T(item.TitleKey) + " — " + Strings.T(item.HintKey),
                item.Required ? "TextBrush" : "MutedBrush"));
        }

        foreach (var note in placeholders)
            ChecklistList.Children.Add(Bullet(note, "WarnBrush"));
    }

    // ------------------------------------------------------------ 开播

    private void LoadPlatforms()
    {
        var data = AppServices.Platforms.GetData();
        _platforms = data.Platforms;
        PlatformCombo.Items.Clear();

        foreach (var platform in _platforms)
        {
            var region = string.Equals(platform.Region, "cn", StringComparison.OrdinalIgnoreCase)
                ? Strings.T("vlive.golive.region.cn")
                : Strings.T("vlive.golive.region.global");
            PlatformCombo.Items.Add($"{region} · {platform.Name}");
        }

        if (_platforms.Count > 0)
            PlatformCombo.SelectedIndex = 0;
    }

    private async void OnPlatformChanged(object sender, SelectionChangedEventArgs e)
    {
        var index = PlatformCombo.SelectedIndex;
        if (index < 0 || index >= _platforms.Count) return;

        _currentPlatform = _platforms[index];
        var platform = _currentPlatform;
        PlatformNameText.Text = platform.Name;
        PlatformMetaText.Text = Strings.T("vlive.golive.meta",
            Strings.T("golive.mode." + GoLiveCore.ParseMode(platform.Mode).ToString().ToLowerInvariant()),
            string.IsNullOrWhiteSpace(platform.VerifiedAt) ? Strings.T("vlive.golive.unverified") : platform.VerifiedAt);
        PlatformNotesText.Text = platform.Notes;
        OpenOfficialButton.IsEnabled = !string.IsNullOrWhiteSpace(platform.OfficialUrl);
        OpenHelpButton.IsEnabled = !string.IsNullOrWhiteSpace(platform.HelpUrl);

        var mode = GoLiveCore.ParseMode(platform.Mode);
        DirectPanel.Visibility = mode == GoLiveMode.Direct && GoLiveService.SupportsDirectStreaming
            ? Visibility.Visible : Visibility.Collapsed;
        VcamPanel.Visibility = mode is GoLiveMode.Direct or GoLiveMode.VirtualCamera
            ? Visibility.Visible : Visibility.Collapsed;
        VcamGuideText.Text = Strings.T(CompanionAppCore.VirtualCameraGuideKey(platform));

        // 上次「记住」的推流码（有就填进 PasswordBox；界面仍然只显示遮罩）
        KeyBox.Password = "";
        try
        {
            var remembered = await AppServices.GoLive.GetRememberedKeyAsync(platform.Id).ConfigureAwait(true);
            if (!string.IsNullOrWhiteSpace(remembered))
            {
                KeyBox.Password = remembered!;
                RememberKeySwitch.IsChecked = true;
            }
        }
        catch (Exception) { /* 密钥库不可用：留空 */ }

        UpdateKeyMask();
        await DetectCompanionsAsync(platform).ConfigureAwait(true);
    }

    private void OnKeyChanged(object sender, RoutedEventArgs e) => UpdateKeyMask();

    private void UpdateKeyMask()
    {
        var mask = GoLiveCore.MaskKey(KeyBox.Password);
        KeyMaskText.Text = mask.Length == 0
            ? Strings.T("vlive.golive.keyEmpty")
            : Strings.T("vlive.golive.keyLength", mask);
    }

    private async Task DetectCompanionsAsync(LivePlatform platform)
    {
        CompanionList.Children.Clear();
        try
        {
            var statuses = await Task.Run(() => AppServices.GoLive.DetectCompanions(platform, AppServices.Platforms))
                .ConfigureAwait(true);

            if (statuses.Count == 0)
            {
                CompanionTitle.Visibility = Visibility.Collapsed;
                return;
            }

            CompanionTitle.Visibility = Visibility.Visible;
            foreach (var status in statuses)
            {
                var stateKey = status.Running ? "vlive.golive.comp.running"
                    : status.Installed ? "vlive.golive.comp.installed"
                    : "vlive.golive.comp.missing";
                CompanionList.Children.Add(Bullet(
                    Strings.T(stateKey, status.App.Name),
                    status.Running ? "OkBrush" : status.Installed ? "TextBrush" : "MutedBrush"));
            }
        }
        catch (Exception)
        {
            CompanionTitle.Visibility = Visibility.Collapsed;
        }
    }

    private async void OnOpenOfficialClick(object sender, RoutedEventArgs e)
    {
        if (_currentPlatform is null || string.IsNullOrWhiteSpace(_currentPlatform.OfficialUrl)) return;
        await AppServices.Host.OpenExternalAsync(_currentPlatform.OfficialUrl);
    }

    private async void OnOpenHelpClick(object sender, RoutedEventArgs e)
    {
        if (_currentPlatform is null || string.IsNullOrWhiteSpace(_currentPlatform.HelpUrl)) return;
        await AppServices.Host.OpenExternalAsync(_currentPlatform.HelpUrl);
    }

    private async void OnStartDirectClick(object sender, RoutedEventArgs e)
    {
        if (_busy || _currentPlatform is null) return;

        var validation = GoLiveCore.ValidateDirectInput(ServerBox.Text, KeyBox.Password);
        if (validation.Length > 0)
        {
            GoLiveStatusText.Text = Strings.T("golive.err." + ShortError(validation, "GOLIVE_"));
            return;
        }

        if (!Controls.ConfirmDialog.Show(
                Strings.T("vlive.golive.startTitle"),
                Strings.T("vlive.golive.startConfirm", _currentPlatform.Name),
                Strings.T("vlive.golive.startOk"), Strings.T("common.cancel"), danger: false))
            return;

        SetBusy(true);
        GoLiveSteps.Children.Clear();
        GoLiveStatusText.Text = Strings.T("common.busy");
        try
        {
            var progress = new Progress<string>(msg =>
            {
                GoLiveSteps.Children.Add(Bullet(msg, "MutedBrush"));
            });

            var result = await AppServices.GoLive.StartDirectAsync(
                    _currentPlatform, ServerBox.Text, KeyBox.Password,
                    RememberKeySwitch.IsChecked == true, progress, CancellationToken.None)
                .ConfigureAwait(true);

            if (!result.Ok)
            {
                GoLiveStatusText.Text = Strings.T("golive.err." + ShortError(result.ErrorCode, "GOLIVE_"));
                AppServices.Toast.Show(GoLiveStatusText.Text, "danger");
                return;
            }

            GoLiveStatusText.Text = Strings.T("vlive.golive.started", _currentPlatform.Name);
            foreach (var note in result.Notes) GoLiveSteps.Children.Add(Bullet(note, "MutedBrush"));
            AppServices.Toast.Show(GoLiveStatusText.Text, "ok");
        }
        catch (Exception ex)
        {
            GoLiveStatusText.Text = Strings.T("golive.err.stream.failed") + " " + ex.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnStopDirectClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        SetBusy(true);
        try
        {
            var result = await AppServices.GoLive.StopDirectAsync(CancellationToken.None).ConfigureAwait(true);
            GoLiveStatusText.Text = result.Ok
                ? Strings.T("vlive.golive.stopped")
                : Strings.T("golive.err.stop.failed");
            foreach (var note in result.Notes) GoLiveSteps.Children.Add(Bullet(note, "MutedBrush"));
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnStartVcamClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        SetBusy(true);
        GoLiveStatusText.Text = Strings.T("common.busy");
        try
        {
            var progress = new Progress<string>(msg => GoLiveSteps.Children.Add(Bullet(msg, "MutedBrush")));
            var result = await AppServices.GoLive.StartVirtualCameraAsync(progress, CancellationToken.None)
                .ConfigureAwait(true);

            GoLiveStatusText.Text = result.Ok
                ? Strings.T("vlive.golive.vcamRunning")
                : Strings.T("golive.err." + ShortError(result.ErrorCode, "GOLIVE_"));
            foreach (var note in result.Notes) GoLiveSteps.Children.Add(Bullet(note, "MutedBrush"));
            AppServices.Toast.Show(GoLiveStatusText.Text, result.Ok ? "ok" : "danger");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnStopVcamClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        SetBusy(true);
        try
        {
            var result = await AppServices.GoLive.StopVirtualCameraAsync(CancellationToken.None).ConfigureAwait(true);
            GoLiveStatusText.Text = result.Ok
                ? Strings.T("vlive.golive.vcamStopped")
                : Strings.T("golive.err.vcam.stop.failed");
            foreach (var note in result.Notes) GoLiveSteps.Children.Add(Bullet(note, "MutedBrush"));
        }
        finally
        {
            SetBusy(false);
        }
    }
}
