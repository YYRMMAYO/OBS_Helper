using System.Windows;
using System.Windows.Controls;
using OBS_Helper.Wpf.Navigation;
using OBS_Helper.Wpf.Services.Mobile;
using OBS_Helper.Wpf.Services.Host;
using System.IO;
using OBS_Helper.Wpf.Services.Streaming;
using OBS_Helper.Wpf.Services.Automation;

namespace OBS_Helper.Wpf.Views;

/// <summary>画质与分发页（Q01~Q08）：推流健康、构图、预案、事故回放、转推、归档。</summary>
public partial class StreamPage : UserControl, INavigationAware
{
    private readonly List<RelayCore.Target> _targets = new();
    private readonly RunbookCore.State _runbook = new();

    public StreamPage()
    {
        InitializeComponent();
        BuildRunbook();
    }

    public Task OnNavigatedToAsync(object? parameter)
    {
        RefreshHealth();
        LoadRelayTargets();
        LoadAudioInputs();
        SentinelCheck.IsChecked = AppServices.Panic.Enabled;
        AppServices.Panic.Suggestion += OnSuggestion;
        return Task.CompletedTask;
    }

    public Task OnNavigatedFromAsync()
    {
        AppServices.Panic.Suggestion -= OnSuggestion;
        return Task.CompletedTask;
    }

    private void OnSuggestion(IReadOnlyList<PanicCore.Action> actions, string reason)
        => Dispatcher.BeginInvoke(new Action(() =>
        {
            RunbookText.Text = "⚠ " + reason + " · " + string.Join(" / ", actions.Select(a => a.Title));
        }));

    private void LoadRelayTargets()
    {
        RelayList.Items.Clear();
        foreach (var target in AppServices.Relay.LoadTargets())
            RelayList.Items.Add(RelayCore.Redact(target));
        RelayText.Text = AppServices.Relay.IsRunning ? "ingest: " + AppServices.Relay.IngestUrl : "";
    }

    private void LoadAudioInputs()
    {
        AudioInputCombo.Items.Clear();
        foreach (var input in AppServices.Obs.AudioInputs.Where(i => i.IsAudio))
            AudioInputCombo.Items.Add(input.Name);
        if (AudioInputCombo.Items.Count > 0) AudioInputCombo.SelectedIndex = 0;
        AudioPresetCombo.Items.Clear();
        foreach (var preset in AudioChainCore.Presets)
            AudioPresetCombo.Items.Add(new ComboBoxItem { Content = preset.Title, Tag = preset.Id });
        AudioPresetCombo.SelectedIndex = 0;
    }

    private void RefreshHealth()
    {
        var obs = AppServices.Obs;
        var bitrate = obs.StreamElapsed.TotalSeconds > 0
            ? obs.StreamStatus.Bytes * 8 / 1000 / obs.StreamElapsed.TotalSeconds
            : 0;
        var input = new StreamHealthInput
        {
            Streaming = obs.StreamStatus.Active,
            ActiveFps = obs.Stats.ActiveFps,
            BitrateKbps = bitrate,
            DroppedFrames = obs.Stats.OutputSkippedFrames,
            TotalFrames = obs.Stats.OutputTotalFrames,
            Congestion = obs.StreamStatus.Congestion,
            UploadKbps = 0,
            ElapsedSeconds = (int)obs.StreamElapsed.TotalSeconds,
        };
        var (score, grade, advice) = StreamHealthCore.Evaluate(input);
        HealthText.Text = $"Score {score} ({grade})\n" + string.Join("\n", advice.Select(a => $"· {a.Title}：{a.Detail}"));
    }

    private void OnRefreshHealth(object sender, RoutedEventArgs e) => RefreshHealth();

    private void OnEvaluateComposition(object sender, RoutedEventArgs e)
    {
        var sample = new CompositionSample
        {
            Brightness = BrightnessSlider.Value,
            ColorTempK = TempSlider.Value,
            SkinToneRatio = SkinSlider.Value,
            CenterOffset = OffsetSlider.Value,
            Contrast = ContrastSlider.Value,
        };
        var (score, issues) = CompositionCheckCore.Evaluate(sample);
        CompositionText.Text = $"Score {score}\n" + string.Join("\n", issues.Select(i => "· " + i));
    }

    private void BuildRunbook()
    {
        RunbookPanel.Children.Clear();
        foreach (var step in RunbookCore.DefaultSteps)
        {
            var button = new Button
            {
                Content = step.Title,
                Style = (Style)FindResource("SecondaryButton"),
                Margin = new Thickness(0, 0, 8, 8),
                Tag = step.Id,
            };
            button.Click += OnRunbookStep;
            RunbookPanel.Children.Add(button);
        }
    }

    private void OnRunbookStep(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        var obs = AppServices.Obs;
        var ok = id switch
        {
            "obs" => obs.IsConnected,
            "scene" => obs.Scenes.Count > 0,
            "audio" => obs.AudioInputs.Any(i => i.IsAudio && !i.Muted),
            "stream" => obs.StreamStatus.Active || obs.Scenes.Count > 0,
            "record" => !string.IsNullOrEmpty(obs.LastRecordFile) || obs.RecordStatus.Active,
            "backup" => true,
            _ => false,
        };
        RunbookCore.Mark(_runbook, id, ok);
        RunbookText.Text = _runbook.AllRequiredPassed(RunbookCore.DefaultSteps)
            ? "✓ 必检项全部通过"
            : "未通过：" + string.Join(", ", _runbook.Failed);
    }

    private async void OnSaveReplay(object sender, RoutedEventArgs e)
    {
        if (!AppServices.Obs.ReplayBufferStatus.Active)
        {
            AppServices.Toast.Show(FeatureErrors.Describe(FeatureErrors.MobCommandInvalid));
            return;
        }
        var (from, to) = IncidentClipCore.Window(DateTimeOffset.Now);
        var result = await AppServices.Obs.SaveReplayBufferAsync().ConfigureAwait(true);
        AppServices.Toast.Show(result.Ok
            ? $"{from:HH:mm:ss} → {to:HH:mm:ss}"
            : result.Comment ?? FeatureErrors.Describe(FeatureErrors.MobObsOffline));
    }

    private async void OnPanic(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("将静音所有音频并切到 BRB 场景（若存在），是否继续？", "安全哨兵",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        await AppServices.Panic.ExecuteAsync(stopStream: false).ConfigureAwait(true);
        AppServices.Toast.Show("已执行安全动作");
    }

    private void OnAddRelay(object sender, RoutedEventArgs e)
    {
        var name = RelayNameBox.Text.Trim();
        var url = RelayUrlBox.Text.Trim();
        if (name.Length == 0 || url.Length == 0) return;
        var target = new RelayCore.Target { Name = name, RtmpUrl = url, StreamKey = RelayKeyBox.Password };
        _targets.Add(target);
        var all = AppServices.Relay.LoadTargets().ToList();
        all.Add(target);
        AppServices.Relay.SaveTargets(all);
        if (target.StreamKey.Length > 0) _ = AppServices.Host.SetSecretAsync("relay." + target.Id + ".key", target.StreamKey);
        RelayList.Items.Add(RelayCore.Redact(target));
        RelayNameBox.Text = RelayUrlBox.Text = RelayKeyBox.Password = "";
    }

    private void OnValidateRelay(object sender, RoutedEventArgs e)
    {
        var (ok, detail) = RelayCore.Check(_targets);
        RelayText.Text = ok ? detail : FeatureErrors.Describe(FeatureErrors.StreamRelayFailed) + " · " + detail;
    }

    private async void OnRelayStart(object sender, RoutedEventArgs e)
    {
        var targets = AppServices.Relay.LoadTargets().ToList();
        foreach (var target in _targets)
        {
            if (!targets.Any(t => t.Id == target.Id)) targets.Add(target);
            if (target.StreamKey.Length > 0)
                await AppServices.Host.SetSecretAsync("relay." + target.Id + ".key", target.StreamKey).ConfigureAwait(true);
        }
        AppServices.Relay.SaveTargets(targets);
        var (ok, code) = await AppServices.Relay.StartAsync(targets).ConfigureAwait(true);
        RelayText.Text = ok ? "ingest: " + AppServices.Relay.IngestUrl : FeatureErrors.Describe(code);
        LoadRelayTargets();
    }

    private void OnRelayStop(object sender, RoutedEventArgs e)
    {
        AppServices.Relay.Stop();
        LoadRelayTargets();
    }

    private async void OnRelayPoint(object sender, RoutedEventArgs e)
    {
        var (ok, code) = await AppServices.Relay.PointObsToRelayAsync().ConfigureAwait(true);
        RelayText.Text = ok ? "OBS 已指向本地收流" : FeatureErrors.Describe(code);
    }

    private async void OnRelayRestore(object sender, RoutedEventArgs e)
    {
        var (ok, code) = await AppServices.Relay.RestoreObsAsync().ConfigureAwait(true);
        RelayText.Text = ok ? "OBS 推流设置已还原" : FeatureErrors.Describe(code);
    }

    private async void OnArchiveRun(object sender, RoutedEventArgs e)
    {
        var file = AppServices.Obs.LastRecordFile;
        if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
        {
            ArchiveText.Text = FeatureErrors.Describe(FeatureErrors.ArchiveUploadFailed);
            return;
        }
        var (ok, code, detail) = await AppServices.ArchiveUpload.UploadAsync(file, RemoteBox.Text.Trim()).ConfigureAwait(true);
        ArchiveText.Text = ok ? "✓ " + Path.GetFileName(file) : FeatureErrors.Describe(code) + " · " + detail;
    }

    private async void OnArchiveVerify(object sender, RoutedEventArgs e)
    {
        var file = AppServices.Obs.LastRecordFile;
        var (ok, code, detail) = await AppServices.ArchiveUpload.VerifyAsync(file, RemoteBox.Text.Trim()).ConfigureAwait(true);
        ArchiveText.Text = ok ? "✓ 校验通过" : FeatureErrors.Describe(code) + " · " + detail;
    }

    private async void OnArchiveUndo(object sender, RoutedEventArgs e)
    {
        var file = AppServices.Obs.LastRecordFile;
        var (ok, code, detail) = await AppServices.ArchiveUpload.DeleteRemoteAsync(RemoteBox.Text.Trim(), Path.GetFileName(file)).ConfigureAwait(true);
        ArchiveText.Text = ok ? "已删除远端副本" : FeatureErrors.Describe(code) + " · " + detail;
    }

    private async void OnAudioApply(object sender, RoutedEventArgs e)
    {
        if (AudioInputCombo.SelectedItem is not string input) return;
        var preset = (AudioPresetCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "clean";
        var (ok, code) = await AppServices.AudioChain.ApplyAsync(input, preset, bypass: false).ConfigureAwait(true);
        AudioText.Text = ok ? "✓ " + preset : FeatureErrors.Describe(code);
    }

    private async void OnAudioBypass(object sender, RoutedEventArgs e)
    {
        if (AudioInputCombo.SelectedItem is not string input) return;
        var preset = (AudioPresetCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "clean";
        var (ok, code) = await AppServices.AudioChain.ApplyAsync(input, preset, bypass: true).ConfigureAwait(true);
        AudioText.Text = ok ? "旁路" : FeatureErrors.Describe(code);
    }

    private void OnSentinelToggle(object sender, RoutedEventArgs e)
        => AppServices.Panic.SetEnabled(SentinelCheck.IsChecked == true);

    private void OnValidateArchive(object sender, RoutedEventArgs e)
    {
        var plan = new ArchiveCore.Plan
        {
            LocalPath = AppServices.Obs.LastRecordFile,
            RemoteName = System.IO.Path.GetFileName(AppServices.Obs.LastRecordFile),
        };
        var (ok, code) = ArchiveCore.Validate(plan, RcloneBox.Text.Trim(), RemoteBox.Text.Trim());
        ArchiveText.Text = ok ? "✓ 可以归档" : FeatureErrors.Describe(code);
    }
}
