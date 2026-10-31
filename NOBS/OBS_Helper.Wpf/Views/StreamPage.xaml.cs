using System.Windows;
using System.Windows.Controls;
using OBS_Helper.Wpf.Navigation;
using OBS_Helper.Wpf.Services.Mobile;
using OBS_Helper.Wpf.Services.Host;
using OBS_Helper.Wpf.Services.Shell;
using System.IO;
using OBS_Helper.Wpf.Services.Streaming;
using OBS_Helper.Wpf.Services.Automation;

namespace OBS_Helper.Wpf.Views;

/// <summary>画质与分发页（Q01~Q08）：推流健康、构图、预案、事故回放、转推、归档。</summary>
public partial class StreamPage : UserControl, INavigationAware
{
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
        // sidecar 路径：优先保留用户显式选择，其次回退到已安装 sidecar 的可执行文件。
        if (string.IsNullOrWhiteSpace(RelayFfmpegBox.Text))
            RelayFfmpegBox.Text = ResolveSidecar("ffmpeg", AppServices.Relay.FfmpegPath);
        if (string.IsNullOrWhiteSpace(RcloneBox.Text))
            RcloneBox.Text = ResolveSidecar("rclone", AppServices.ArchiveUpload.RclonePath);
        if (RelayFfmpegBox.Text.Trim().Length > 0) AppServices.Relay.SetFfmpegPath(RelayFfmpegBox.Text.Trim());
        if (RcloneBox.Text.Trim().Length > 0) AppServices.ArchiveUpload.SetRclonePath(RcloneBox.Text.Trim());
        AppServices.Panic.Suggestion += OnSuggestion;
        return Task.CompletedTask;
    }

    private static string ResolveSidecar(string id, string configured)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        var entry = AppServices.Sidecars.Manifest.Find(id);
        return entry is null ? "" : AppServices.Sidecars.InstalledExecutable(entry) ?? "";
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
        RelayText.Text = AppServices.Relay.IsRunning
            ? Strings.T("stream.relay.ingest", AppServices.Relay.IngestUrl)
            : "";
    }

    private void LoadAudioInputs()
    {
        AudioInputCombo.Items.Clear();
        foreach (var input in AppServices.Obs.AudioInputs.Where(i => i.IsAudio))
            AudioInputCombo.Items.Add(input.Name);
        if (AudioInputCombo.Items.Count > 0) AudioInputCombo.SelectedIndex = 0;
        AudioPresetCombo.Items.Clear();
        foreach (var preset in AudioChainCore.Presets)
            AudioPresetCombo.Items.Add(new ComboBoxItem
            {
                Content = Strings.T("stream.audio.preset." + preset.Id),
                Tag = preset.Id,
            });
        AudioPresetCombo.SelectedIndex = 0;
    }

    private void OnSelfCheck(object sender, RoutedEventArgs e)
    {
        SelfCheck.SetBusy(true);
        try
        {
            var lines = new List<string>();
            var obs = AppServices.Obs;
            lines.Add(obs.IsConnected ? SelfCheckResult.Ok("OBS") : SelfCheckResult.Warn("OBS", Strings.T("selfcheck.obsHint")));
            lines.Add(obs.Scenes.Count > 0
                ? SelfCheckResult.Ok(Strings.T("stream.runbook"), obs.Scenes.Count.ToString())
                : SelfCheckResult.Warn(Strings.T("stream.runbook"), Strings.T("selfcheck.obsHint")));
            lines.Add(obs.AudioInputs.Any(i => i.IsAudio)
                ? SelfCheckResult.Ok(Strings.T("stream.audioInput"), obs.AudioInputs.Count(i => i.IsAudio).ToString())
                : SelfCheckResult.Warn(Strings.T("stream.audioInput"), Strings.T("selfcheck.audioHint")));

            var mediamtx = AppServices.Sidecars.Manifest.Find("mediamtx");
            lines.Add(mediamtx is not null && AppServices.Sidecars.IsInstalled(mediamtx)
                ? SelfCheckResult.Ok("mediamtx")
                : SelfCheckResult.Warn("mediamtx", Strings.T("selfcheck.relayHint")));
            var ffmpeg = AppServices.Sidecars.Manifest.Find("ffmpeg");
            var ffmpegPath = !string.IsNullOrWhiteSpace(RelayFfmpegBox.Text) && File.Exists(RelayFfmpegBox.Text.Trim())
                ? RelayFfmpegBox.Text.Trim()
                : ffmpeg is null ? "" : AppServices.Sidecars.InstalledExecutable(ffmpeg) ?? "";
            lines.Add(File.Exists(ffmpegPath)
                ? SelfCheckResult.Ok(Strings.T("stream.relayFfmpeg"), ffmpegPath)
                : SelfCheckResult.Warn(Strings.T("stream.relayFfmpeg"), Strings.T("selfcheck.ffmpegHint")));
            var rclone = AppServices.Sidecars.Manifest.Find("rclone");
            var rclonePath = !string.IsNullOrWhiteSpace(RcloneBox.Text) && File.Exists(RcloneBox.Text.Trim())
                ? RcloneBox.Text.Trim()
                : rclone is null ? "" : AppServices.Sidecars.InstalledExecutable(rclone) ?? "";
            lines.Add(File.Exists(rclonePath)
                ? SelfCheckResult.Ok("rclone", rclonePath)
                : SelfCheckResult.Warn("rclone", Strings.T("selfcheck.archiveHint")));
            lines.Add(AppServices.Relay.LoadTargets().Count > 0
                ? SelfCheckResult.Ok(Strings.T("stream.relay"), AppServices.Relay.LoadTargets().Count.ToString())
                : SelfCheckResult.Warn(Strings.T("stream.relay"), Strings.T("selfcheck.relayHint")));
            lines.Add(AppServices.Relay.IsRunning
                ? SelfCheckResult.Ok(Strings.T("stream.relayStart"), AppServices.Relay.IngestUrl)
                : SelfCheckResult.Warn(Strings.T("stream.relayStart"), Strings.T("selfcheck.relayHint")));
            var (score, grade, _) = StreamHealthCore.Evaluate(BuildHealthInput());
            lines.Add(SelfCheckResult.Ok(Strings.T("stream.health"), Strings.T("stream.health.score", score, grade)));
            var required = RunbookCore.DefaultSteps.Count(s => s.Required);
            lines.Add(_runbook.AllRequiredPassed(RunbookCore.DefaultSteps)
                ? SelfCheckResult.Ok(Strings.T("stream.runbook"), $"{_runbook.Done.Count}/{required}")
                : SelfCheckResult.Warn(Strings.T("stream.runbook"),
                    _runbook.Failed.Count > 0 ? string.Join(", ", _runbook.Failed) : $"{_runbook.Done.Count}/{required}"));
            lines.Add(SelfCheckResult.Ok(Strings.T("stream.composition"), Strings.T("stream.composition.score",
                CompositionCheckCore.Evaluate(new CompositionSample
                {
                    Brightness = BrightnessSlider.Value,
                    ColorTempK = TempSlider.Value,
                    SkinToneRatio = SkinSlider.Value,
                    CenterOffset = OffsetSlider.Value,
                    Contrast = ContrastSlider.Value,
                }).Score)));
            SelfCheck.SetResult(SelfCheckResult.Join(lines));
        }
        finally { SelfCheck.SetBusy(false); }
    }

    private void OnOpenSidecars(object sender, RoutedEventArgs e)
    {
        try
        {
            System.IO.Directory.CreateDirectory(AppServices.Sidecars.Root);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", AppServices.Sidecars.Root)
            { UseShellExecute = true });
        }
        catch (Exception) { AppServices.Toast.Show(Strings.T("common.openLinkFailed")); }
    }

    private void OnBrowseRelayFfmpeg(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "FFmpeg|ffmpeg.exe|可执行文件|*.exe" };
        if (dialog.ShowDialog() == true)
        {
            RelayFfmpegBox.Text = dialog.FileName;
            AppServices.Relay.SetFfmpegPath(dialog.FileName);
        }
    }

    private void OnBrowseRclone(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "rclone|rclone.exe|可执行文件|*.exe" };
        if (dialog.ShowDialog() == true)
        {
            RcloneBox.Text = dialog.FileName;
            AppServices.ArchiveUpload.SetRclonePath(dialog.FileName);
        }
    }

    private void RefreshHealth()
    {
        var (score, grade, advice) = StreamHealthCore.Evaluate(BuildHealthInput());
        HealthText.Text = Strings.T("stream.health.score", score, grade) + "\n" +
            string.Join("\n", advice.Select(a => Strings.T("stream.health.advice", a.Title, a.Detail)));
    }

    private StreamHealthInput BuildHealthInput()
    {
        var obs = AppServices.Obs;
        var bitrate = obs.StreamStatus.Kbps > 0 ? obs.StreamStatus.Kbps : CurrentBitrateKbps();
        return new StreamHealthInput
        {
            Streaming = obs.StreamStatus.Active,
            ActiveFps = obs.Stats.ActiveFps,
            BitrateKbps = bitrate,
            DroppedFrames = obs.Stats.OutputTotalFrames > 0 ? obs.Stats.OutputSkippedFrames : obs.StreamStatus.SkippedFrames,
            TotalFrames = obs.Stats.OutputTotalFrames > 0 ? obs.Stats.OutputTotalFrames : obs.StreamStatus.TotalFrames,
            Congestion = obs.StreamStatus.Congestion,
            UploadKbps = 0,
            ElapsedSeconds = (int)obs.StreamElapsed.TotalSeconds,
        };
    }

    private static double CurrentBitrateKbps()
    {
        var obs = AppServices.Obs;
        return obs.StreamElapsed.TotalSeconds > 0
            ? obs.StreamStatus.Bytes * 8 / 1000 / obs.StreamElapsed.TotalSeconds
            : 0;
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
        CompositionText.Text = Strings.T("stream.composition.score", score) + "\n" +
            string.Join("\n", issues.Select(i => "· " + i));
    }

    private void BuildRunbook()
    {
        RunbookPanel.Children.Clear();
        foreach (var step in RunbookCore.DefaultSteps)
        {
            var button = new Button
            {
                Content = Strings.T("stream.runbook.step." + step.Id),
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
            "scene" => obs.CurrentSceneItems.Count > 0 || obs.Scenes.Count > 0,
            "audio" => obs.AudioInputs.Any(i => i.IsAudio && !i.Muted),
            "stream" => obs.StreamStatus.Active || obs.StreamStatus.Bytes > 0 || obs.StreamElapsed > TimeSpan.Zero,
            "record" => !string.IsNullOrEmpty(obs.LastRecordFile) || obs.RecordStatus.Active,
            "backup" => AppServices.ObsBackups.ListBackups().Count > 0,
            _ => false,
        };
        RunbookCore.Mark(_runbook, id, ok);
        RunbookText.Text = _runbook.AllRequiredPassed(RunbookCore.DefaultSteps)
            ? Strings.T("stream.runbook.passed")
            : Strings.T("stream.runbook.failed", string.Join(", ", _runbook.Failed));
    }

    private async void OnSaveReplay(object sender, RoutedEventArgs e)
    {
        if (!AppServices.Obs.ReplayBufferStatus.Active)
        {
            AppServices.Toast.Show(Strings.T("stream.replay.inactive"));
            return;
        }
        var (from, to) = IncidentClipCore.Window(DateTimeOffset.Now);
        var result = await AppServices.Obs.SaveReplayBufferAsync().ConfigureAwait(true);
        AppServices.Toast.Show(result.Ok
            ? Strings.T("stream.replay.saved", from.ToString("HH:mm:ss"), to.ToString("HH:mm:ss"))
            : result.Comment ?? FeatureErrors.Describe(FeatureErrors.MobObsOffline));
    }

    private async void OnPanic(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(Strings.T("stream.panic.confirm"), Strings.T("stream.panic"),
                MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        await AppServices.Panic.ExecuteAsync(stopStream: false).ConfigureAwait(true);
        AppServices.Toast.Show(Strings.T("stream.panic.done"));
    }

    private void OnAddRelay(object sender, RoutedEventArgs e)
    {
        var name = RelayNameBox.Text.Trim();
        var url = RelayUrlBox.Text.Trim();
        if (name.Length == 0 || url.Length == 0)
        {
            RelayText.Text = Strings.T("stream.relay.noTargets");
            return;
        }
        var target = new RelayCore.Target { Name = name, RtmpUrl = url, StreamKey = RelayKeyBox.Password };
        if (!RelayCore.IsValidTarget(target))
        {
            RelayText.Text = FeatureErrors.Describe(FeatureErrors.StreamRelayFailed);
            return;
        }
        var all = AppServices.Relay.LoadTargets().ToList();
        all.Add(target);
        AppServices.Relay.SaveTargets(all);
        if (target.StreamKey.Length > 0) _ = AppServices.Host.SetSecretAsync("relay." + target.Id + ".key", target.StreamKey);
        RelayNameBox.Text = RelayUrlBox.Text = RelayKeyBox.Password = "";
        RelayText.Text = Strings.T("stream.relay.added", target.Name);
        LoadRelayTargets();
    }

    private void OnValidateRelay(object sender, RoutedEventArgs e)
    {
        var targets = AppServices.Relay.LoadTargets().ToList();
        var (ok, detail) = RelayCore.Check(targets);
        RelayText.Text = ok
            ? Strings.T("stream.relay.valid", detail)
            : Strings.T("stream.relay.invalid", detail);
    }

    private async void OnRelayStart(object sender, RoutedEventArgs e)
    {
        var targets = AppServices.Relay.LoadTargets().ToList();
        if (targets.Count == 0)
        {
            RelayText.Text = Strings.T("selfcheck.relayHint");
            return;
        }
        if (!File.Exists(RelayFfmpegBox.Text.Trim()))
        {
            RelayText.Text = Strings.T("selfcheck.ffmpegHint");
            return;
        }
        AppServices.Relay.SetFfmpegPath(RelayFfmpegBox.Text.Trim());
        var (ok, code) = await AppServices.Relay.StartAsync(targets).ConfigureAwait(true);
        RelayText.Text = ok
            ? Strings.T("stream.relay.ingest", AppServices.Relay.IngestUrl)
            : FeatureErrors.Describe(code);
        LoadRelayTargets();
    }

    private void OnRelayStop(object sender, RoutedEventArgs e)
    {
        AppServices.Relay.Stop();
        RelayText.Text = Strings.T("stream.relay.stopped");
        LoadRelayTargets();
    }

    private async void OnRelayPoint(object sender, RoutedEventArgs e)
    {
        var (ok, code) = await AppServices.Relay.PointObsToRelayAsync().ConfigureAwait(true);
        RelayText.Text = ok ? Strings.T("stream.relay.pointed") : FeatureErrors.Describe(code);
    }

    private async void OnRelayRestore(object sender, RoutedEventArgs e)
    {
        var (ok, code) = await AppServices.Relay.RestoreObsAsync().ConfigureAwait(true);
        RelayText.Text = ok ? Strings.T("stream.relay.restored") : FeatureErrors.Describe(code);
    }

    private async void OnArchiveRun(object sender, RoutedEventArgs e)
    {
        var file = AppServices.Obs.LastRecordFile;
        if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
        {
            ArchiveText.Text = Strings.T("stream.archive.noFile");
            return;
        }
        if (RemoteBox.Text.Trim().Length == 0)
        {
            ArchiveText.Text = Strings.T("stream.archive.noRemote");
            return;
        }
        AppServices.ArchiveUpload.SetRclonePath(RcloneBox.Text.Trim());
        var (ok, code, detail) = await AppServices.ArchiveUpload.UploadAsync(file, RemoteBox.Text.Trim()).ConfigureAwait(true);
        ArchiveText.Text = ok
            ? Strings.T("stream.archive.ok", Path.GetFileName(file))
            : FeatureErrors.Describe(code) + " · " + detail;
    }

    private async void OnArchiveVerify(object sender, RoutedEventArgs e)
    {
        var file = AppServices.Obs.LastRecordFile;
        if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
        {
            ArchiveText.Text = Strings.T("stream.archive.noFile");
            return;
        }
        AppServices.ArchiveUpload.SetRclonePath(RcloneBox.Text.Trim());
        var (ok, code, detail) = await AppServices.ArchiveUpload.VerifyAsync(file, RemoteBox.Text.Trim()).ConfigureAwait(true);
        ArchiveText.Text = ok ? Strings.T("stream.archive.verifyOk") : FeatureErrors.Describe(code) + " · " + detail;
    }

    private async void OnArchiveUndo(object sender, RoutedEventArgs e)
    {
        var file = AppServices.Obs.LastRecordFile;
        if (string.IsNullOrWhiteSpace(file))
        {
            ArchiveText.Text = Strings.T("stream.archive.noFile");
            return;
        }
        if (RemoteBox.Text.Trim().Length == 0)
        {
            ArchiveText.Text = Strings.T("stream.archive.noRemote");
            return;
        }
        AppServices.ArchiveUpload.SetRclonePath(RcloneBox.Text.Trim());
        var (ok, code, detail) = await AppServices.ArchiveUpload.DeleteRemoteAsync(RemoteBox.Text.Trim(), Path.GetFileName(file)).ConfigureAwait(true);
        ArchiveText.Text = ok ? Strings.T("stream.archive.undoOk") : FeatureErrors.Describe(code) + " · " + detail;
    }

    private async void OnAudioApply(object sender, RoutedEventArgs e)
    {
        if (AudioInputCombo.SelectedItem is not string input || !AppServices.Obs.IsConnected)
        {
            AudioText.Text = Strings.T("stream.audio.noInput");
            return;
        }
        var preset = (AudioPresetCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "clean";
        var (ok, code) = await AppServices.AudioChain.ApplyAsync(input, preset, bypass: false).ConfigureAwait(true);
        AudioText.Text = ok ? Strings.T("stream.audio.applied", preset) : FeatureErrors.Describe(code);
    }

    private async void OnAudioBypass(object sender, RoutedEventArgs e)
    {
        if (AudioInputCombo.SelectedItem is not string input || !AppServices.Obs.IsConnected)
        {
            AudioText.Text = Strings.T("stream.audio.noInput");
            return;
        }
        var preset = (AudioPresetCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "clean";
        var (ok, code) = await AppServices.AudioChain.ApplyAsync(input, preset, bypass: true).ConfigureAwait(true);
        AudioText.Text = ok ? Strings.T("stream.audio.bypassed") : FeatureErrors.Describe(code);
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
        ArchiveText.Text = ok ? Strings.T("stream.archive.valid") : FeatureErrors.Describe(code);
    }
}
