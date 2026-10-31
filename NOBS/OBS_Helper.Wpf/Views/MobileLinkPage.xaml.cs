using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OBS_Helper.Wpf.Models.Mobile;
using OBS_Helper.Wpf.Navigation;
using OBS_Helper.Wpf.Services.Mobile;
using OBS_Helper.Wpf.Services.Security;

namespace OBS_Helper.Wpf.Views;

/// <summary>
/// 手机联动页（V3.4.0 / M01~M12 的桌面入口）。
///
/// 分四段：遥控（配对 + 设备）、摄像头（Wi-Fi / USB 引导）、多机位、设备（快传）。
/// 服务默认不启动；用户点「生成配对码」才绑定端口，离开页面不会自作主张继续开端口。
/// </summary>
public partial class MobileLinkPage : UserControl, INavigationAware
{
    private PairingSession? _pairing;
    private string _guestInvite = "";
    private DateTimeOffset _pairingExpiry;
    private System.Windows.Threading.DispatcherTimer? _tick;
    private bool _subscribed;

    public MobileLinkPage()
    {
        InitializeComponent();
        BuildCombos();
        ApplyTab("remote");
    }

    public Task OnNavigatedToAsync(object? parameter)
    {
        if (!_subscribed)
        {
            AppServices.MobileLink.StateChanged += OnServiceChanged;
            _subscribed = true;
        }
        ApplyTab(parameter as string);
        RefreshAll();
        return Task.CompletedTask;
    }

    public Task OnNavigatedFromAsync()
    {
        if (_subscribed)
        {
            AppServices.MobileLink.StateChanged -= OnServiceChanged;
            _subscribed = false;
        }
        _tick?.Stop();
        _tick = null;
        return Task.CompletedTask;
    }

    private void OnServiceChanged() => Dispatcher.BeginInvoke(new Action(RefreshDeviceArea));

    private void BuildCombos()
    {
        foreach (var quality in PhoneCameraQuality.All)
            QualityCombo.Items.Add(new ComboBoxItem { Content = quality.Id, Tag = quality });
        QualityCombo.SelectedIndex = 0;

        var layouts = new[]
        {
            (PhoneCameraLayout.Fullscreen, Strings.T("mobile.camera.layout") + " · Full"),
            (PhoneCameraLayout.PipBottomRight, "PiP · " + Strings.T("mobile.camera.layout")),
            (PhoneCameraLayout.SideLeftThird, "1/3"),
            (PhoneCameraLayout.PortraitCrop, "9:16"),
        };
        foreach (var (layout, label) in layouts)
            LayoutCombo.Items.Add(new ComboBoxItem { Content = label, Tag = layout });
        LayoutCombo.SelectedIndex = 0;
        foreach (var (layout, label) in layouts)
            MulticamLayoutCombo.Items.Add(new ComboBoxItem { Content = label, Tag = layout });
        MulticamLayoutCombo.SelectedIndex = 0;
    }

    private void ApplyTab(string? tab)
    {
        var key = (tab ?? "remote").Trim().ToLowerInvariant();
        RemotePanel.Visibility = key == "remote" ? Visibility.Visible : Visibility.Collapsed;
        CameraPanel.Visibility = key == "camera" ? Visibility.Visible : Visibility.Collapsed;
        MulticamPanel.Visibility = key == "multicam" ? Visibility.Visible : Visibility.Collapsed;
        DevicesPanel.Visibility = key == "devices" ? Visibility.Visible : Visibility.Collapsed;
        SetTabStyle(RemoteTab, key == "remote");
        SetTabStyle(CameraTab, key == "camera");
        SetTabStyle(MulticamTab, key == "multicam");
        SetTabStyle(DevicesTab, key == "devices");
    }

    private static void SetTabStyle(Button button, bool active)
    {
        button.Style = (Style)Application.Current.FindResource(active ? "PrimaryButton" : "SecondaryButton");
    }

    private void OnTabClick(object sender, RoutedEventArgs e)
        => ApplyTab((sender as Button)?.Tag as string);

    // ------------------------------------------------------------ 刷新

    private void RefreshAll()
    {
        RefreshServerState();
        RefreshDeviceArea();
        RefreshCamera();
        RefreshMulticam();
        RefreshUploads();
        RefreshUsbGuide();
        RenderQr();
    }

    private void RefreshServerState()
    {
        var link = AppServices.MobileLink;
        var version = typeof(MobileLinkPage).Assembly.GetName().Version?.ToString(3) ?? "";
        ServerStateText.Text = (link.IsRunning
            ? Strings.T("mobile.server.running", link.BaseUrl)
            : Strings.T("mobile.server.stopped"))
            + (version.Length > 0 ? "  ·  v" + version : "");
        StopServerButton.IsEnabled = link.IsRunning;
        HeroStopButton.IsEnabled = link.IsRunning;
    }

    private void RefreshDeviceArea()
    {
        RefreshServerState();
        RefreshDeviceList();
    }

    private void RefreshDeviceList()
    {
        DeviceList.Children.Clear();
        var devices = AppServices.MobileLink.Devices;
        if (devices.Count == 0)
        {
            DeviceList.Children.Add(new TextBlock
            {
                Text = Strings.T("mobile.devices.empty"),
                Style = (Style)FindResource("MutedText"),
            });
            return;
        }
        foreach (var device in devices)
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var name = new StackPanel();
            name.Children.Add(new TextBlock { Text = device.Name, Style = (Style)FindResource("BodyText") });
            name.Children.Add(new TextBlock
            {
                Text = Strings.T("mobile.devices.lastSeen") + " · " + device.LastSeenUtc.ToLocalTime().ToString("MM-dd HH:mm"),
                Style = (Style)FindResource("CaptionText"),
            });
            row.Children.Add(name);

            var role = new ComboBox { Width = 120, Margin = new Thickness(8, 0, 8, 0), Tag = device.DeviceId };
            foreach (var r in new[] { MobileRole.Viewer, MobileRole.Operator, MobileRole.Admin })
                role.Items.Add(new ComboBoxItem { Content = Strings.T("mobile.devices.role") + " · " + MobileRoles.Name(r), Tag = r });
            role.SelectedIndex = (int)device.ParsedRole;
            role.SelectionChanged += OnRoleChanged;
            Grid.SetColumn(role, 1);
            row.Children.Add(role);

            var revoke = new Button
            {
                Content = Strings.T("mobile.devices.revoke"),
                Style = (Style)FindResource("GhostButton"),
                Tag = device.DeviceId,
            };
            revoke.Click += OnRevokeClick;
            Grid.SetColumn(revoke, 2);
            row.Children.Add(revoke);

            DeviceList.Children.Add(new Border
            {
                BorderBrush = (Brush)FindResource("LineBrush"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 2, 0, 6),
                Child = row,
            });
        }
    }

    private void OnRoleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo || combo.Tag is not string deviceId) return;
        if (combo.SelectedItem is not ComboBoxItem { Tag: MobileRole role }) return;
        if (AppServices.MobileLink.SetRole(deviceId, role))
            AppServices.Toast.Show(Strings.T("mobile.devices.roleChanged"));
    }

    private void OnRevokeClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string deviceId }) return;
        AppServices.MobileLink.RevokeDevice(deviceId);
        AppServices.Toast.Show(Strings.T("mobile.devices.revoked"));
        RefreshDeviceList();
    }

    private void OnRevokeAllClick(object sender, RoutedEventArgs e)
    {
        AppServices.MobileSessions.RevokeAll();
        foreach (var device in AppServices.MobileLink.Devices) AppServices.MobileLink.RevokeDevice(device.DeviceId);
        RefreshDeviceList();
    }

    // ------------------------------------------------------------ 配对

    private void OnPairClick(object sender, RoutedEventArgs e)
    {
        var (ok, code, session, _, url) = AppServices.MobileLink.BeginPairing();
        if (!ok || session is null)
        {
            AppServices.Toast.Show(Strings.T("mobile.pair.failed", FeatureErrors.Describe(code)));
            RefreshServerState();
            return;
        }
        _pairing = session;
        _pairingExpiry = session.ExpiresUtc;
        PairCodeText.Text = Strings.T("mobile.pair.shortCode") + " · " + session.ShortCode;
        PairUrlText.Text = url;
        RenderQr();
        StartTick();
        RefreshServerState();
    }

    private void RenderQr()
    {
        if (_pairing is null)
        {
            QrImage.Source = null;
            PairCodeText.Text = Strings.T("mobile.pair.none");
            PairUrlText.Text = "";
            PairExpiryText.Text = "";
            return;
        }
        var url = PairUrlText.Text;
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            var code = QrCodeCore.Encode(url);
            QrImage.Source = ToBitmap(code);
        }
        catch (Exception)
        {
            QrImage.Source = null;
        }
    }

    private static BitmapSource ToBitmap(QrCodeCore.QrCode code)
    {
        const int scale = 6;
        const int quiet = 4;
        var size = code.Size + quiet * 2;
        var pixels = new byte[size * scale * size * scale * 4];
        for (var y = 0; y < size * scale; y++)
        {
            for (var x = 0; x < size * scale; x++)
            {
                var mx = x / scale - quiet;
                var my = y / scale - quiet;
                var dark = mx >= 0 && my >= 0 && mx < code.Size && my < code.Size && code.Modules[my][mx];
                var v = dark ? (byte)0 : (byte)255;
                var i = (y * size * scale + x) * 4;
                pixels[i] = v;
                pixels[i + 1] = v;
                pixels[i + 2] = v;
                pixels[i + 3] = 255;
            }
        }
        return BitmapSource.Create(size * scale, size * scale, 96, 96, PixelFormats.Bgra32, null,
            pixels, size * scale * 4);
    }

    private void StartTick()
    {
        _tick?.Stop();
        _tick = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _tick.Tick += (_, _) =>
        {
            if (_pairing is null) return;
            var left = (int)Math.Ceiling((_pairingExpiry - DateTimeOffset.UtcNow).TotalSeconds);
            if (left <= 0)
            {
                PairExpiryText.Text = Strings.T("err.MOB_PAIR_EXPIRED");
                _tick?.Stop();
            }
            else
            {
                PairExpiryText.Text = Strings.T("mobile.pair.expires", left);
            }
        };
        _tick.Start();
    }

    private void OnStopServerClick(object sender, RoutedEventArgs e)
    {
        AppServices.MobileLink.Stop();
        _pairing = null;
        _tick?.Stop();
        RefreshAll();
    }

    // ------------------------------------------------------------ 摄像头

    private void RefreshCamera()
    {
        var manager = AppServices.MediaServer;
        var entry = AppServices.Sidecars.Manifest.Find("mediamtx");
        var installed = entry is not null && AppServices.Sidecars.IsInstalled(entry);
        SidecarButton.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
        SidecarButton.IsEnabled = entry is not null;
        if (entry is { Manual: true })
        {
            SidecarButton.Content = Strings.T("mobile.camera.openSource");
            var dir = Services.Interop.SidecarCore.InstallDirectory(AppServices.Sidecars.Root, entry) ?? "";
            CameraStatusText.Text = Strings.T("mobile.camera.manual", dir);
        }
        CameraStartButton.IsEnabled = installed;
        CameraStopButton.IsEnabled = manager.IsRunning;

        if (!manager.IsSupported)
        {
            CameraStatusText.Text = Strings.T("mobile.camera.win7");
            CameraStartButton.IsEnabled = false;
            return;
        }
        if (!installed)
        {
            if (entry is not { Manual: true }) CameraStatusText.Text = Strings.T("mobile.camera.sidecarMissing");
            CameraUrlText.Text = entry is null
                ? ""
                : $"{entry.Version} · {entry.License} · {entry.Source}";
            return;
        }
        var session = manager.Session;
        if (session is null)
        {
            CameraStatusText.Text = "";
            CameraUrlText.Text = "";
            return;
        }

        // 媒体服务先跑起来、OBS 后连接：这里补建来源，成功后状态会从「等待」变成「运行中」。
        if (!session.Ready && AppServices.Obs.IsConnected)
            _ = manager.BindPendingAsync();

        CameraStatusText.Text = session.Ready
            ? Strings.T("mobile.camera.running")
            : Strings.T("mobile.camera.obsPending");
        CameraUrlText.Text =
            Strings.T("mobile.camera.srt") + ":\n" + PhoneCameraCore.SrtPublishUrl(session.LanAddress, session.SrtPort, session.StreamKey) +
            "\n" + Strings.T("mobile.camera.rtmp") + ":\n" + PhoneCameraCore.RtmpPublishUrl(session.LanAddress, session.RtmpPort, session.StreamKey);
    }

    private async void OnCameraStartClick(object sender, RoutedEventArgs e)
    {
        var lan = AppServices.MobileLink.Address?.ToString() ?? MobileLinkServer.DetectLanAddress() ?? "";
        if (!LanPolicy.IsPrivateAddress(lan))
        {
            AppServices.Toast.Show(FeatureErrors.Describe(FeatureErrors.CamNoPrivateIp));
            return;
        }
        var quality = (QualityCombo.SelectedItem as ComboBoxItem)?.Tag as PhoneCameraQuality ?? PhoneCameraQuality.Hd720;
        var layout = (LayoutCombo.SelectedItem as ComboBoxItem)?.Tag is PhoneCameraLayout l ? l : PhoneCameraLayout.Fullscreen;
        CameraStartButton.IsEnabled = false;
        var (ok, code, _) = await AppServices.MediaServer.StartAsync(lan, quality, layout, AudioCheck.IsChecked == true)
            .ConfigureAwait(true);
        if (!ok)
            AppServices.Toast.Show(Strings.T("mobile.camera.failed", FeatureErrors.Describe(code)));
        RefreshCamera();
    }

    private void OnCameraStopClick(object sender, RoutedEventArgs e)
    {
        AppServices.MediaServer.StopAll();
        RefreshCamera();
    }

    private async void OnSidecarClick(object sender, RoutedEventArgs e)
    {
        var entry = AppServices.Sidecars.Manifest.Find("mediamtx");
        if (entry is null)
        {
            AppServices.Toast.Show(FeatureErrors.Describe(FeatureErrors.CamSidecarMissing));
            return;
        }
        if (entry.Manual)
        {
            try { Process.Start(new ProcessStartInfo(entry.Source) { UseShellExecute = true }); }
            catch (Exception) { AppServices.Toast.Show(Strings.T("common.openLinkFailed")); }
            RefreshCamera();
            return;
        }
        SidecarButton.IsEnabled = false;
        CameraStatusText.Text = Strings.T("common.busy");
        var (ok, code, _) = await AppServices.Sidecars.InstallAsync(entry).ConfigureAwait(true);
        if (!ok) AppServices.Toast.Show(FeatureErrors.Describe(code));
        RefreshCamera();
    }

    private async void OnApplySyncClick(object sender, RoutedEventArgs e)
    {
        var session = AppServices.MediaServer.Session;
        if (session is null)
        {
            AppServices.Toast.Show(Strings.T("mobile.camera.sidecarMissing"));
            return;
        }
        if (!int.TryParse(SyncOffsetBox.Text.Trim(), out var offset) || offset is < -500 or > 500)
        {
            AppServices.Toast.Show(Strings.T("mobile.camera.syncRange"));
            return;
        }
        var result = await AppServices.Obs.SetAudioSyncOffsetMsAsync(session.SourceName, offset).ConfigureAwait(true);
        AppServices.Toast.Show(result.Ok ? Strings.T("common.saved") : result.Comment ?? Strings.T("common.failed", ""));
    }

    private void RefreshMulticam()
    {
        MulticamList.Children.Clear();
        var sessions = AppServices.MediaServer.Sessions;
        var devices = AppServices.MobileLink.Devices;
        MulticamNote.Text = Strings.T("mobile.multicam.active", AppServices.MobileLink.ActiveSessions, devices.Count);
        if (sessions.Count == 0)
        {
            MulticamList.Children.Add(new TextBlock
            {
                Text = Strings.T("mobile.multicam.none"),
                Style = (Style)FindResource("MutedText"),
            });
        }
        foreach (var session in sessions)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            row.Children.Add(new TextBlock
            {
                Text = $"{session.CameraId} · {session.Quality.Id} · {session.Layout} · SRT {session.SrtPort}",
                Style = (Style)FindResource("BodyText"),
                VerticalAlignment = VerticalAlignment.Center,
            });
            var stop = new Button
            {
                Content = Strings.T("mobile.multicam.stop"),
                Style = (Style)FindResource("GhostButton"),
                Tag = session.CameraId,
                Margin = new Thickness(8, 0, 0, 0),
            };
            stop.Click += (_, _) =>
            {
                AppServices.MediaServer.StopCamera(session.CameraId);
                RefreshCamera();
                RefreshMulticam();
            };
            row.Children.Add(stop);
            MulticamList.Children.Add(row);
        }
        foreach (var device in devices)
        {
            MulticamList.Children.Add(new TextBlock
            {
                Text = $"{device.Name} · {MobileRoles.Name(device.ParsedRole)}",
                Style = (Style)FindResource("MutedText"),
                Margin = new Thickness(0, 2, 0, 2),
            });
        }

        var service = AppServices.RemoteGuests;
        GuestStartButton.IsEnabled = !service.IsRunning;
        GuestStopButton.IsEnabled = service.IsRunning;
        GuestRecordCheck.IsChecked = service.Room?.RecordingEnabled == true;
        GuestInviteBox.Text = service.IsRunning
            ? Strings.T("mobile.guest.invite") + "\n" + _guestInvite
            : "";
        GuestBlockedText.Text = service.MediaBlockedReason + "\n" + Strings.T("mobile.guest.consent");
        GuestList.Children.Clear();
        if (service.Room is null || service.Room.Guests.Count == 0)
        {
            GuestList.Children.Add(new TextBlock
            {
                Text = Strings.T("mobile.guest.noGuests"),
                Style = (Style)FindResource("MutedText"),
            });
            return;
        }
        foreach (var guest in service.Room.Guests)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            row.Children.Add(new TextBlock
            {
                Text = $"{guest.Name} · {(guest.RecordingConsent ? "consent" : "no-consent")}",
                Style = (Style)FindResource("BodyText"),
                VerticalAlignment = VerticalAlignment.Center,
            });
            var kick = new Button
            {
                Content = Strings.T("mobile.guest.kick"),
                Style = (Style)FindResource("GhostButton"),
                Margin = new Thickness(8, 0, 0, 0),
            };
            kick.Click += (_, _) =>
            {
                AppServices.RemoteGuests.Kick(guest.Id);
                RefreshMulticam();
            };
            row.Children.Add(kick);
            GuestList.Children.Add(row);
        }
    }

    private async void OnAddCameraClick(object sender, RoutedEventArgs e)
    {
        var lan = AppServices.MobileLink.Address?.ToString() ?? MobileLinkServer.DetectLanAddress() ?? "";
        if (!LanPolicy.IsPrivateAddress(lan))
        {
            AppServices.Toast.Show(FeatureErrors.Describe(FeatureErrors.CamNoPrivateIp));
            return;
        }
        var quality = (QualityCombo.SelectedItem as ComboBoxItem)?.Tag as PhoneCameraQuality ?? PhoneCameraQuality.Hd720;
        var layout = (MulticamLayoutCombo.SelectedItem as ComboBoxItem)?.Tag is PhoneCameraLayout l ? l : PhoneCameraLayout.Fullscreen;
        var (ok, code, _) = await AppServices.MediaServer.StartAsync(lan, quality, layout, AudioCheck.IsChecked == true)
            .ConfigureAwait(true);
        if (!ok) AppServices.Toast.Show(Strings.T("mobile.camera.failed", FeatureErrors.Describe(code)));
        RefreshCamera();
        RefreshMulticam();
    }

    private void OnStopCameraClick(object sender, RoutedEventArgs e)
    {
        var session = AppServices.MediaServer.Sessions.LastOrDefault();
        if (session is null) return;
        AppServices.MediaServer.StopCamera(session.CameraId);
        RefreshCamera();
        RefreshMulticam();
    }

    private async void OnApplyLayoutClick(object sender, RoutedEventArgs e)
    {
        var session = AppServices.MediaServer.Sessions.FirstOrDefault();
        if (session is null) return;
        var layout = (MulticamLayoutCombo.SelectedItem as ComboBoxItem)?.Tag is PhoneCameraLayout l ? l : PhoneCameraLayout.Fullscreen;
        var (ok, code) = await AppServices.MediaServer.SetLayoutAsync(session.CameraId, layout).ConfigureAwait(true);
        if (!ok) AppServices.Toast.Show(FeatureErrors.Describe(code));
        RefreshMulticam();
    }

    private void OnGuestStartClick(object sender, RoutedEventArgs e)
    {
        var lan = AppServices.MobileLink.Address?.ToString() ?? "";
        var (ok, code, invite) = AppServices.RemoteGuests.Start(lan);
        if (!ok)
        {
            AppServices.Toast.Show(FeatureErrors.Describe(code));
            return;
        }
        _guestInvite = invite;
        RefreshMulticam();
    }

    private void OnGuestStopClick(object sender, RoutedEventArgs e)
    {
        AppServices.RemoteGuests.Stop();
        _guestInvite = "";
        RefreshMulticam();
    }

    private void OnGuestRecordClick(object sender, RoutedEventArgs e)
        => AppServices.RemoteGuests.SetRecording(GuestRecordCheck.IsChecked == true);

    private void RefreshUsbGuide()
    {
        var guide = AppServices.UsbCamera;
        UsbDetectText.Text = guide.AdbFound
            ? Strings.T("mobile.usb.detected", guide.AdbPath ?? "")
            : Strings.T("mobile.usb.notDetected");
        AndroidStepsText.Text = string.Join("\n", guide.AndroidSteps);
        IosStepsText.Text = string.Join("\n", guide.IosSteps);
    }

    // ------------------------------------------------------------ 通知 / 快传

    private void OnNotifyEnableClick(object sender, RoutedEventArgs e)
    {
        var service = AppServices.MobileNotify;
        service.SetEnabled(!service.Enabled);
        NotifyEnableButton.Content = Strings.T("mobile.notify.enable") + (service.Enabled ? " ✓" : "");
        NotifyResultText.Text = "";
    }

    private void OnNotifyAuthClick(object sender, RoutedEventArgs e)
    {
        AppServices.MobileNotify.Authorize();
        NotifyResultText.Text = Strings.T("mobile.notify.authorize");
    }

    private async void OnNotifyReadClick(object sender, RoutedEventArgs e)
    {
        var (ok, text, code) = await AppServices.MobileNotify.ReadClipboardOnceAsync().ConfigureAwait(true);
        if (!ok)
        {
            NotifyResultText.Text = FeatureErrors.Describe(code);
            return;
        }
        if (!MobileNotifyCore.TryPrepare(text, out var prepared))
        {
            NotifyResultText.Text = Strings.T("mobile.notify.blocked");
            return;
        }
        NotifyResultText.Text = Strings.T("mobile.notify.result", prepared);
    }

    private void RefreshUploads()
    {
        UploadList.Children.Clear();
        var files = AppServices.MobileUploads.List(20);
        if (files.Count == 0)
        {
            UploadList.Children.Add(new TextBlock
            {
                Text = Strings.T("mobile.upload.empty"),
                Style = (Style)FindResource("MutedText"),
            });
            return;
        }
        foreach (var info in files)
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = info.FileName, Style = (Style)FindResource("BodyText") });
            text.Children.Add(new TextBlock
            {
                Text = $"{info.Kind} · {info.Size / 1024} KB · {info.UploadedUtc:MM-dd HH:mm}",
                Style = (Style)FindResource("CaptionText"),
            });
            row.Children.Add(text);
            var add = new Button
            {
                Content = Strings.T("mobile.upload.add"),
                Style = (Style)FindResource("SecondaryButton"),
                Tag = info,
            };
            add.Click += OnAddUploadClick;
            Grid.SetColumn(add, 1);
            row.Children.Add(add);
            UploadList.Children.Add(row);
        }
    }

    private async void OnAddUploadClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MobileUploadInfo info }) return;
        var result = await AppServices.MobileUploads.AddToObsAsync(AppServices.Obs, info).ConfigureAwait(true);
        AppServices.Toast.Show(result.Ok
            ? Strings.T("mobile.upload.added")
            : Strings.T("mobile.upload.failed", result.Comment ?? FeatureErrors.Describe(FeatureErrors.MobObsOffline)));
    }

    private void OnOpenUploadsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppServices.MobileUploads.DirectoryPath);
            Process.Start(new ProcessStartInfo("explorer.exe", AppServices.MobileUploads.DirectoryPath)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception)
        {
            AppServices.Toast.Show(Strings.T("common.openLinkFailed"));
        }
    }
}
