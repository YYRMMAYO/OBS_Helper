using System.Windows;
using System.Windows.Controls;
using OBS_Helper.Wpf.Controls;
using OBS_Helper.Wpf.Navigation;
using OBS_Helper.Wpf.Services;
using OBS_Helper.Wpf.Services.Timing;

namespace OBS_Helper.Wpf.Views;

/// <summary>
/// 导播计时器控制页（V3.6.0）。
///
/// 与计时窗分工：本页负责输入与状态（模式、时长、开始/暂停/停止、记录、设置），
/// 计时窗只负责「大字号、无边框、置顶」的显示。所有状态都在 <see cref="TimerService"/>，
/// 页面与窗口都只是订阅者，因此关掉任一窗口不会丢计时。
/// </summary>
public partial class TimerPage : UserControl, INavigationAware
{
    private readonly TimerService _service = AppServices.TimerClock;
    private bool _suppress = true;
    private string _recordsSignature = "";

    public TimerPage()
    {
        InitializeComponent();
        _service.SetViewFactory(() => new TimerWindow());
        _service.Changed += OnServiceChanged;
        _service.RecordAdded += _ => Dispatcher.BeginInvoke(RefreshRecords);
        ReloadFromSettings();
    }

    public Task OnNavigatedToAsync(object? parameter)
    {
        ReloadFromSettings();
        // 首次进入：一次性署名弹窗。自检模式不弹，避免无界面环境阻塞。
        if (!_service.AttributionAcknowledged
            && !App.HeadlessTest
            && Environment.GetEnvironmentVariable("OBS_SELFTEST") != "1")
        {
            TimerAttributionDialog.Show(Window.GetWindow(this));
            _service.AttributionAcknowledged = true;
        }
        return Task.CompletedTask;
    }

    private void OnServiceChanged()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(Refresh);
            return;
        }
        Refresh();
    }

    // ------------------------------------------------------------ 读取 / 刷新

    private void ReloadFromSettings()
    {
        _suppress = true;
        var s = _service.Settings;
        CountDownRadio.IsChecked = s.Mode == TimerMode.CountDown;
        CountUpRadio.IsChecked = s.Mode == TimerMode.CountUp;
        DurationBox.Text = TimerCore.FormatDuration(TimeSpan.FromSeconds(s.PlannedSeconds));
        OpacitySlider.Value = s.Opacity;
        FontScaleSlider.Value = s.FontScale;
        ShowHoursCheck.IsChecked = s.ShowHours;
        ExcludeCaptureCheck.IsChecked = s.ExcludeFromCapture;
        ClickThroughCheck.IsChecked = s.ClickThrough;
        AutoStartCheck.IsChecked = s.AutoStartWithRecording;
        AutoStopCheck.IsChecked = s.AutoStopWithRecording;
        SoundCheck.IsChecked = s.SoundOnOvertime;
        TrayStatusCheck.IsChecked = s.TrayStatusEnabled;
        DurationHintText.Text = "";
        _suppress = false;
        Refresh();
    }

    private void Refresh()
    {
        var snapshot = _service.Snapshot;
        TimeText.Text = snapshot.Display;
        SecondaryText.Text = snapshot.IsOvertime
            ? Strings.T("timer.overtime", snapshot.Secondary)
            : Strings.T("timer.plannedShort", snapshot.Secondary);

        StateText.Text = Strings.T(snapshot.State switch
        {
            TimerState.Running => "timer.state.running",
            TimerState.Paused => "timer.state.paused",
            TimerState.Finished => "timer.state.finished",
            _ => "timer.state.idle",
        });
        StateText.SetResourceReference(TextBlock.ForegroundProperty, snapshot.State switch
        {
            TimerState.Paused => "WarnBrush",
            TimerState.Finished => "DangerBrush",
            TimerState.Running => snapshot.IsOvertime ? "DangerBrush" : "BrandBrush",
            _ => "MutedTextBrush",
        });

        StartPauseButton.Content = Strings.T(snapshot.State switch
        {
            TimerState.Running => "timer.action.pause",
            TimerState.Paused => "timer.action.resume",
            _ => "timer.action.start",
        });
        StopButton.IsEnabled = snapshot.State != TimerState.Idle;
        ResetButton.IsEnabled = snapshot.State != TimerState.Idle;
        ToggleWindowButton.Content = Strings.T(_service.IsWindowVisible
            ? "timer.action.hideWindow"
            : "timer.action.showWindow");

        RefreshRecords();
    }

    private void RefreshRecords()
    {
        var records = _service.Records;
        var signature = records.Count == 0 ? "0" : $"{records.Count}:{records[0].Id}:{records[^1].Id}";
        if (signature == _recordsSignature) return;
        _recordsSignature = signature;

        RecordsList.Children.Clear();
        var ordered = TimerCore.OrderForDisplay(records);
        EmptyRecordsText.Visibility = ordered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var index = 1;
        foreach (var record in ordered)
        {
            var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            foreach (var width in new[] { 42d, 150, 86, 92, 92, 92, 44 })
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });

            AddCell(grid, 0, index.ToString());
            AddCell(grid, 1, record.StartedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            AddCell(grid, 2, Strings.T(record.Mode == TimerMode.CountDown
                ? "timer.mode.countdown" : "timer.mode.countup"));
            AddCell(grid, 3, TimerCore.FormatDuration(TimeSpan.FromSeconds(record.PlannedSeconds)));
            AddCell(grid, 4, TimerCore.FormatDuration(TimeSpan.FromSeconds(record.ActualSeconds)));
            AddCell(grid, 5, record.OvertimeSeconds > 0
                ? TimerCore.FormatSignedOvertime(TimeSpan.FromSeconds(record.OvertimeSeconds))
                : "-");

            var delete = new Button
            {
                Content = "✕",
                Tag = record.Id,
                Style = (Style)FindResource("GhostButton"),
                Width = 30,
                Height = 28,
                Padding = new Thickness(0),
                ToolTip = Strings.T("timer.record.delete"),
            };
            delete.Click += OnDeleteRecord;
            Grid.SetColumn(delete, 6);
            grid.Children.Add(delete);

            RecordsList.Children.Add(grid);
            index++;
        }
    }

    private void AddCell(Grid grid, int column, string text)
    {
        var block = new TextBlock
        {
            Text = text,
            Style = (Style)FindResource("BodyText"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };
        Grid.SetColumn(block, column);
        grid.Children.Add(block);
    }

    // ------------------------------------------------------------ 控制

    private void OnModeChecked(object sender, RoutedEventArgs e)
    {
        if (_suppress) return;
        _service.SetMode(CountUpRadio.IsChecked == true ? TimerMode.CountUp : TimerMode.CountDown);
    }

    private void OnApplyDuration(object sender, RoutedEventArgs e)
    {
        var parsed = TimerCore.ParseDuration(DurationBox.Text);
        var seconds = (int)Math.Floor(parsed.TotalSeconds);
        if (_service.Settings.Mode == TimerMode.CountDown && seconds <= 0)
        {
            DurationHintText.Text = Strings.T("timer.error.durationRequired");
            return;
        }
        DurationHintText.Text = "";
        _service.SetPlanned(parsed);
        DurationBox.Text = TimerCore.FormatDuration(parsed);
    }

    private void OnStartPause(object sender, RoutedEventArgs e) => _service.ToggleStartPause();

    private void OnStop(object sender, RoutedEventArgs e) => _service.Stop();

    private void OnReset(object sender, RoutedEventArgs e)
    {
        if (_service.Snapshot.State != TimerState.Idle
            && !ConfirmDialog.Show(Strings.T("timer.reset.title"), Strings.T("timer.reset.message"),
                Strings.T("timer.action.reset"), Strings.T("common.cancel"), danger: true, irreversible: false))
            return;
        _service.Reset();
    }

    private void OnToggleWindow(object sender, RoutedEventArgs e) => _service.ToggleTimerWindow();

    private void OnNextScreen(object sender, RoutedEventArgs e) => _service.MoveToNextScreen();

    private void OnCopyTsv(object sender, RoutedEventArgs e)
    {
        var tsv = _service.CopyTsv();
        if (string.IsNullOrEmpty(tsv))
        {
            AppServices.Toast?.Show(Strings.T("timer.error.export"), "warn");
            return;
        }
        try
        {
            Clipboard.SetText(tsv);
            AppServices.Toast?.Show(Strings.T("timer.records.copied"), "ok");
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Timer", $"复制 TSV 失败：{ex.Message}");
            AppServices.Toast?.Show(Strings.T("timer.error.export"), "warn");
        }
    }

    private void OnCopyCsv(object sender, RoutedEventArgs e)
    {
        var csv = _service.CopyCsv();
        if (string.IsNullOrEmpty(csv))
        {
            AppServices.Toast?.Show(Strings.T("timer.error.export"), "warn");
            return;
        }
        try
        {
            Clipboard.SetText(csv);
            AppServices.Toast?.Show(Strings.T("timer.records.csvCopied"), "ok");
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Timer", $"复制 CSV 失败：{ex.Message}");
            AppServices.Toast?.Show(Strings.T("timer.error.export"), "warn");
        }
    }

    private void OnClearRecords(object sender, RoutedEventArgs e)
    {
        if (_service.Records.Count == 0) return;
        if (!ConfirmDialog.Show(Strings.T("timer.records.clearTitle"), Strings.T("timer.records.clearMessage"),
                Strings.T("timer.action.clearRecords"), Strings.T("common.cancel"), danger: true, irreversible: true))
            return;
        _service.ClearRecords();
    }

    private void OnDeleteRecord(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int id }) _service.DeleteRecord(id);
    }

    private void OnCopyOverlay(object sender, RoutedEventArgs e)
    {
        var url = _service.EnsureOverlayUrl();
        if (string.IsNullOrEmpty(url))
        {
            AppServices.Toast?.Show(Strings.T("timer.overlay.failed"), "warn");
            return;
        }

        OverlayUrlText.Text = url;
        try
        {
            Clipboard.SetText(url);
            AppServices.Toast?.Show(Strings.T("timer.overlay.copied"), "ok");
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Timer", $"复制叠加地址失败：{ex.Message}");
            AppServices.Toast?.Show(Strings.T("timer.overlay.failed"), "warn");
        }
    }

    private void OnShowAttribution(object sender, RoutedEventArgs e)
    {
        TimerAttributionDialog.Show(Window.GetWindow(this));
        _service.AttributionAcknowledged = true;
    }

    // ------------------------------------------------------------ 设置

    private void OnOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppress) return;
        _service.UpdateSettings(s => s.Opacity = OpacitySlider.Value);
    }

    private void OnFontScaleChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppress) return;
        _service.UpdateSettings(s => s.FontScale = FontScaleSlider.Value);
    }

    private void OnSettingChanged(object sender, RoutedEventArgs e)
    {
        if (_suppress) return;
        _service.UpdateSettings(s =>
        {
            s.ShowHours = ShowHoursCheck.IsChecked == true;
            s.ExcludeFromCapture = ExcludeCaptureCheck.IsChecked == true;
            s.ClickThrough = ClickThroughCheck.IsChecked == true;
            s.AutoStartWithRecording = AutoStartCheck.IsChecked == true;
            s.AutoStopWithRecording = AutoStopCheck.IsChecked == true;
            s.SoundOnOvertime = SoundCheck.IsChecked == true;
            s.TrayStatusEnabled = TrayStatusCheck.IsChecked == true;
        });
    }
}
