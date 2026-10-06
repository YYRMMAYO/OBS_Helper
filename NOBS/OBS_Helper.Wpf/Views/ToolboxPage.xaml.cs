using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using OBS_Helper.Wpf.Navigation;
using OBS_Helper.Wpf.Services;
using OBS_Helper.Wpf.Services.ObsConfig;
using OBS_Helper.Wpf.Services.Tools;

namespace OBS_Helper.Wpf.Views;

/// <summary>
/// 工具箱（V2.6）：录屏与直播的实用工具合集。
/// 所有操作均为只读探测或独立进程调用，不修改 OBS 配置；失败一律降级为提示。
/// </summary>
public partial class ToolboxPage : UserControl, INavigationAware
{
    /// <summary>场景化参数处方（静态内置数据）。</summary>
    /// <summary>
    /// 场景化参数处方（V2.9.2）。做成属性而不是静态字段：文案取自文案表，
    /// 语言切换后要能换一套，而不是把首次访问时的语言冻结下来。
    /// </summary>
    private static (string Name, string Text)[] Presets => new[]
    {
        (Strings.T("toolbox.prescription.1.title"), Strings.T("toolbox.prescription.1.body")),
        (Strings.T("toolbox.prescription.2.title"), Strings.T("toolbox.prescription.2.body")),
        (Strings.T("toolbox.prescription.3.title"), Strings.T("toolbox.prescription.3.body")),
        (Strings.T("toolbox.prescription.4.title"), Strings.T("toolbox.prescription.4.body")),
    };

    private string _releaseUrl = "https://github.com/obsproject/obs-studio/releases";

    public ToolboxPage()
    {
        InitializeComponent();

        foreach (var (name, _) in Presets)
            PresetCombo.Items.Add(name);
        PresetCombo.SelectedIndex = 0;

        BuildSectionNav();

        Loaded += OnLoadedAsync;
    }

    // ------------------------------------------------------------ 分节导航（V3.0 / C6）

    /// <summary>
    /// 生成跳转按钮。顺序与页面自上而下一致，因此它同时是「这页有什么」的目录。
    ///
    /// 元素通过 <see cref="ToolboxSections.ElementNameOf"/> 的命名约定查找（而不是在页面里
    /// 再抄一份「元素 → 键」的表）：这样「分节清单」只有一个真源，单测可以拿它校验 XAML。
    /// </summary>
    private void BuildSectionNav()
    {
        foreach (var key in ToolboxSections.Keys)
        {
            var target = FindName(ToolboxSections.ElementNameOf(key)) as FrameworkElement;
            if (target is null) continue;   // 名字对不上就跳过（宁可少一个入口，也不要让整页崩掉）

            var button = new Button
            {
                Content = Strings.T(key),
                Style = TryFindResource("GhostButton") as Style,
                Margin = new Thickness(0, 0, 8, 6),
                Padding = new Thickness(10, 4, 10, 4),
                Tag = target
            };
            button.Click += (_, _) =>
            {
                if (button.Tag is not FrameworkElement el) return;
                // 只滚动，不抢焦点：焦点留在导航按钮上，用户想连跳几节不必一路 Tab 回来；
                // 而小节标题本身不可聚焦，把焦点丢过去反而会让读屏失去落点。
                el.BringIntoView();
            };
            SectionNav.Children.Add(button);
        }
    }

    private async void OnLoadedAsync(object sender, RoutedEventArgs e)
    {
        // ffmpeg 探测很快，但 PATH 扫描仍可能涉及几十个目录，放到后台线程
        var ffmpeg = await System.Threading.Tasks.Task.Run(RecordingToolsService.FindFfmpeg)
            .ConfigureAwait(true);
        FfmpegText.Text = ffmpeg is null
            ? Strings.T("toolbox.remux.noFfmpeg")
            : Strings.T("toolbox.remux.ffmpegFound", ffmpeg);
    }

    /// <summary>
    /// 语言切换后重建「构造期取过文案」的部分（V3.0 / F5）。
    ///
    /// 本页在构造函数里做了三件与语言有关的事：填处方下拉框、生成分节导航按钮、装载两枚共用控件
    /// （它们的文案也在各自构造函数里取）。页面实例被导航缓存复用，所以语言一变就必须重建这些，
    /// 否则整页看起来只有一半跟着切。
    /// </summary>
    public Task OnNavigatedToAsync(object? parameter)
    {
        RefreshLanguage();
        return Task.CompletedTask;
    }

    private string? _builtLanguage;

    private void RefreshLanguage()
    {
        var language = Strings.Current;
        if (string.Equals(_builtLanguage, language, StringComparison.Ordinal))
        {
            // 语言没变也刷一遍两枚共用控件：它们在别的页面被切换语言后可能已经过时
            DownloadCardControl.ApplyLanguage();
            FeedbackCardControl.ApplyLanguage();
            return;
        }

        _builtLanguage = language;

        var selected = PresetCombo.SelectedIndex;
        PresetCombo.Items.Clear();
        foreach (var (name, _) in Presets) PresetCombo.Items.Add(name);
        PresetCombo.SelectedIndex = selected >= 0 && selected < PresetCombo.Items.Count ? selected : 0;

        BuildSectionNav();

        DownloadCardControl.ApplyLanguage();
        FeedbackCardControl.ApplyLanguage();
    }

    // ------------------------------------------------------------ 录像工具

    private async void OnOpenRecordingDir(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = await AppServices.RecordingTools.TryGetRecordingDirAsync().ConfigureAwait(true);
            if (result.Dir is null)
            {
                RecordingDirText.Text = Strings.T("toolbox.recordingDir.none");
                return;
            }

            RecordingDirText.Text = Strings.T("toolbox.recordingDir.current", result.Source, result.Dir);
            var err = RecordingToolsService.OpenInExplorer(result.Dir);
            if (err is not null) AppServices.Toast.Show(err, "error");
        }
        catch (Exception ex)
        {
            AppServices.Toast.Show(Strings.T("toolbox.openRecordingDirFailed", ex.Message), "error");
        }
    }

    private async void OnRemuxPickFile(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = Strings.T("toolbox.remux.pickTitle"),
                Filter = Strings.T("toolbox.remux.pickFilter")
            };
            if (dlg.ShowDialog() != true) return;

            RemuxResultText.Visibility = Visibility.Visible;
            RemuxResultText.Text = Strings.T("toolbox.remux.working", System.IO.Path.GetFileName(dlg.FileName));
            AppServices.Busy.Show(Strings.T("toolbox.remux.busy"));
            try
            {
                var (ok, message) = await RecordingToolsService.RemuxToMp4Async(dlg.FileName).ConfigureAwait(true);
                RemuxResultText.Text = (ok ? Strings.T("toolbox.remux.donePrefix") : Strings.T("toolbox.remux.failedPrefix")) + message;
                AppServices.Toast.Show(ok ? Strings.T("toolbox.remux.done") : Strings.T("toolbox.remux.failed"), ok ? "ok" : "error");
            }
            finally
            {
                AppServices.Busy.Hide();
            }
        }
        catch (Exception ex)
        {
            AppServices.Toast.Show(Strings.T("toolbox.remux.exception", ex.Message), "error");
        }
    }

    // ------------------------------------------------------------ 参数处方

    private void OnPresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PresetText is null) return;
        var i = PresetCombo.SelectedIndex;
        PresetText.Text = i >= 0 && i < Presets.Length ? Presets[i].Text : "";
    }

    private void OnCopyPreset(object sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrEmpty(PresetText.Text)) return;
            Clipboard.SetText($"{Presets[Math.Max(PresetCombo.SelectedIndex, 0)].Name}\n{PresetText.Text}");
            AppServices.Toast.Show(Strings.T("toolbox.prescription.copied"), "ok");
        }
        catch (Exception ex)
        {
            AppServices.Toast.Show(Strings.T("toolbox.copyFailed", ex.Message), "error");
        }
    }

    // ------------------------------------------------------------ 隐私清单

    private void OpenMsSettings(string uri, string label)
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo
            {
                FileName = uri,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppServices.Toast.Show(Strings.T("toolbox.openMsSettingsFailed", label, ex.Message), "error");
        }
    }

    private void OnOpenFocusAssist(object sender, RoutedEventArgs e) => OpenMsSettings("ms-settings:quietmoments", Strings.T("toolbox.privacy.focus"));

    private void OnOpenNotifications(object sender, RoutedEventArgs e) => OpenMsSettings("ms-settings:notifications", Strings.T("toolbox.privacy.notifications"));

    private void OnOpenPersonalization(object sender, RoutedEventArgs e) => OpenMsSettings("ms-settings:personalization", Strings.T("toolbox.privacy.personalization"));

    // ------------------------------------------------------------ 冲突扫描

    private async void OnScanConflicts(object sender, RoutedEventArgs e)
    {
        try
        {
            // V3.0（F8）：枚举全机进程并逐个取名字/路径是同步系统调用，进程多时会在 UI 线程上顿一下
            var names = await Task.Run(() => Process.GetProcesses()
                .Select(p => { using var _ = p; return SafeProcessName(p); })
                .Where(n => n.Length > 0)
                .ToList()).ConfigureAwait(true);

            var hits = ConflictScannerCore.Scan(names);

            ConflictList.ItemsSource = hits;
            ConflictEmptyText.Visibility = hits.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ConflictSummaryText.Text = hits.Count == 0
                ? Strings.T("toolbox.conflicts.scanDone")
                : Strings.T("toolbox.conflicts.found", hits.Count, hits.Count(h => h.RiskLevel == ConflictScannerCore.RiskHigh), hits.Count(h => h.RiskLevel == ConflictScannerCore.RiskMedium), hits.Count(h => h.RiskLevel == ConflictScannerCore.RiskInfo));
        }
        catch (Exception ex)
        {
            AppServices.Toast.Show(Strings.T("toolbox.conflicts.failed", ex.Message), "error");
        }
    }

    private static string SafeProcessName(Process p)
    {
        try { return p.ProcessName ?? ""; }
        catch (Exception) { return ""; } // 已退出 / 权限不足的进程会抛
    }

    // ------------------------------------------------------------ 带宽计算器

    /// <summary>UI 层第一道防线：只放行数字与小数点（粘贴的非法字符同样拦截）。</summary>
    private void OnNumericPreviewTextInput(object sender, TextCompositionEventArgs e)
        => e.Handled = !e.Text.All(c => Services.Compat.Compat.IsAsciiDigit(c) || c == '.');

    private void OnBandwidthInputChanged(object sender, TextChangedEventArgs e)
    {
        if (RecommendText is null || MultiStreamText is null) return; // XAML 初始化阶段
        UpdateBandwidthAdvice();
    }

    private void UpdateBandwidthAdvice()
    {
        var uploadRaw = TryParseDouble(UploadInput.Text);
        RecommendText.Text = BandwidthAdvisorCore.Recommend(uploadRaw).Advice
            + ClampedNote(uploadRaw, BandwidthAdvisorCore.MaxUploadMbps, "Mbps");

        // V3.0（D4）：把用户填写的上行速度记下来，供诊断页的清单自动回填使用。
        // 这是**用户填写值**而不是本机实测值，界面上也如实这么写（见 check.auto.bitrate.*）。
        if (!double.IsNaN(uploadRaw) && uploadRaw > 0)
        {
            try
            {
                AppServices.Store.SetItem("bandwidth_upload_mbps",
                    uploadRaw.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                FileLogger.Warn("Toolbox", $"保存上行速度失败：{ex.Message}");
            }
        }

        if (!double.IsNaN(uploadRaw))
        {
            var streamsRaw = TryParseDouble(StreamCountInput.Text);
            var bitrateRaw = TryParseDouble(StreamBitrateInput.Text);
            var streams = BandwidthAdvisorCore.ClampToInt(streamsRaw, BandwidthAdvisorCore.MaxStreams);
            var bitrate = BandwidthAdvisorCore.ClampToInt(bitrateRaw, BandwidthAdvisorCore.MaxSingleBitrateKbps);
            MultiStreamText.Text = BandwidthAdvisorCore.DescribeMultiStream(uploadRaw, streams, bitrate)
                + ClampedNote(streamsRaw, BandwidthAdvisorCore.MaxStreams, Strings.T("toolbox.unit.streams"))
                + ClampedNote(bitrateRaw, BandwidthAdvisorCore.MaxSingleBitrateKbps, "kbps");
        }
    }

    /// <summary>UI 层第二道防线：核心层钳制是静默的，这里把「已按上限计算」明确告诉用户。</summary>
    private static string ClampedNote(double raw, double max, string unit)
        => !double.IsNaN(raw) && raw > max ? Strings.T("toolbox.clampedNote", max, unit) : "";

    private static double TryParseDouble(string? raw)
        => double.TryParse(raw?.Trim(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) && v >= 0 ? v : double.NaN;

    // ------------------------------------------------------------ OBS 新版本情报

    private async void OnFetchRelease(object sender, RoutedEventArgs e)
    {
        try
        {
            ReleaseText.Text = Strings.T("toolbox.release.querying");
            var info = await AppServices.ObsRelease.GetLatestAsync().ConfigureAwait(true);
            if (info is null)
            {
                ReleaseText.Text = Strings.T("toolbox.release.failedNoCache");
                return;
            }

            _releaseUrl = info.Url;
            var sourceTag = info.Source switch
            {
                "live" => "",
                "cache" => Strings.T("toolbox.release.sourceCache"),
                _ => Strings.T("toolbox.release.sourceSnapshot")
            };
            ReleaseText.Text =
                Strings.T("toolbox.release.result", info.Tag, info.PublishedText, sourceTag, info.Summary);
        }
        catch (Exception ex)
        {
            ReleaseText.Text = Strings.T("toolbox.release.exception", ex.Message);
        }
    }

    private void OnOpenReleaseUrl(object sender, RoutedEventArgs e)
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo
            {
                FileName = _releaseUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppServices.Toast.Show(Strings.T("common.openLinkError", ex.Message), "error");
        }
    }

    // ------------------------------------------------------------ 色彩体检（V2.7）

    private async void OnRunColorCheck(object sender, RoutedEventArgs e)
    {
        try
        {
            ColorSummaryText.Text = Strings.T("toolbox.check.readingObsConfig");
            var result = await AppServices.ColorCheck.RunAsync().ConfigureAwait(true);
            if (!result.Ok)
            {
                ColorSummaryText.Text = result.Message;
                return;
            }

            ColorSummaryText.Text = result.Items.Count(i => i.Status == "warn") == 0
                ? Strings.T("toolbox.colorCheck.ok")
                : Strings.T("toolbox.colorCheck.warn", result.Items.Count(i => i.Status == "warn"));
            ColorCheckList.ItemsSource = result.Items;
        }
        catch (Exception ex)
        {
            AppServices.Toast.Show(Strings.T("toolbox.colorCheck.failed", ex.Message), "error");
        }
    }

    // ------------------------------------------------------------ 音频采样率体检（V2.7）

    private async void OnRunSampleRateCheck(object sender, RoutedEventArgs e)
    {
        try
        {
            SampleRateSummaryText.Text = Strings.T("toolbox.check.enumeratingAudio");
            var result = await AppServices.SampleRateCheck.RunAsync().ConfigureAwait(true);

            SampleRateSummaryText.Text = result.Items.Count(i => i.Status == "warn") == 0
                ? Strings.T("toolbox.sampleRate.ok")
                : Strings.T("toolbox.sampleRate.warn", result.Items.Count(i => i.Status == "warn"));
            SampleRateText.Visibility = Visibility.Visible;
            SampleRateText.Text = string.Join("\n\n", result.Items.Select(
                i => (i.Status switch
                {
                    "ok" => Strings.T("toolbox.item.ok"),
                    "warn" => Strings.T("toolbox.item.warn"),
                    _ => Strings.T("toolbox.item.info")
                }) + i.Title + "\n" + i.Detail));
        }
        catch (Exception ex)
        {
            AppServices.Toast.Show(Strings.T("toolbox.sampleRate.failed", ex.Message), "error");
        }
    }

    // ------------------------------------------------------------ 黑屏专项体检（V2.8）

    private async void OnRunGraphicsEnvCheck(object sender, RoutedEventArgs e)
    {
        try
        {
            GraphicsEnvSummaryText.Text = Strings.T("toolbox.check.probingGraphics");
            GraphicsEnvEmptyText.Visibility = Visibility.Collapsed;
            var items = await OBS_Helper.Wpf.Services.SystemCheck.GraphicsEnvCheckService.RunAsync().ConfigureAwait(true);

            GraphicsEnvSummaryText.Text = items.Count(i => i.Status == "warn") == 0
                ? Strings.T("toolbox.graphics.ok")
                : Strings.T("toolbox.graphics.warn", items.Count(i => i.Status == "warn"));
            GraphicsEnvList.ItemsSource = items;
        }
        catch (Exception ex)
        {
            GraphicsEnvEmptyText.Visibility = Visibility.Visible;
            AppServices.Toast.Show(Strings.T("toolbox.graphics.failed", ex.Message), "error");
        }
    }

    // ------------------------------------------------------------ 音频设备深度体检（V2.8）

    private async void OnRunAudioHealthCheck(object sender, RoutedEventArgs e)
    {
        try
        {
            AudioHealthSummaryText.Text = Strings.T("toolbox.check.audioHealth");
            var connected = AppServices.Obs.IsConnected;
            var obsInputs = connected
                ? AppServices.Obs.AudioInputs.Select(i => i.Name).ToList()
                : new List<string>();

            var items = await OBS_Helper.Wpf.Services.Audio.AudioDeviceHealthService
                .RunAsync(obsInputs)
                .ConfigureAwait(true);

            AudioHealthSummaryText.Text =
                (items.Count(i => i.Status == "error"), items.Count(i => i.Status == "warn")) switch
                {
                    (0, 0) => Strings.T("toolbox.audioHealth.ok"),
                    var (err, warn) => Strings.T("toolbox.audioHealth.warn", err, warn)
                };
            AudioHealthList.ItemsSource = items;
        }
        catch (Exception ex)
        {
            AppServices.Toast.Show(Strings.T("toolbox.audioHealth.failed", ex.Message), "error");
        }
    }

    // ------------------------------------------------------------ 虚拟摄像头体检（V2.8）

    private async void OnRunVirtualCamCheck(object sender, RoutedEventArgs e)
    {
        try
        {
            VcamSummaryText.Text = Strings.T("toolbox.check.vcam");
            VcamEmptyText.Visibility = Visibility.Collapsed;
            var items = await OBS_Helper.Wpf.Services.Tools.VirtualCamCheckService.RunAsync().ConfigureAwait(true);

            VcamSummaryText.Text = items.Count(i => i.Status is "warn" or "error") == 0
                ? Strings.T("toolbox.vcam.ok")
                : Strings.T("toolbox.vcam.issue");
            VcamCheckList.ItemsSource = items;
        }
        catch (Exception ex)
        {
            VcamEmptyText.Visibility = Visibility.Visible;
            AppServices.Toast.Show(Strings.T("toolbox.vcam.failed", ex.Message), "error");
        }
    }

    // ------------------------------------------------------------ 磁盘写入基准（V2.7）

    private async void OnRunDiskBenchmark(object sender, RoutedEventArgs e)
    {
        try
        {
            var bitrateRaw = TryParseDouble(DiskBitrateInput.Text);
            var bitrate = BandwidthAdvisorCore.ClampToInt(
                bitrateRaw, DiskBenchmarkInput.MaxBitrateKbps);
            if (bitrate <= 0)
            {
                AppServices.Toast.Show(Strings.T("toolbox.disk.needBitrate"), "error");
                return;
            }

            var dirResult = await AppServices.RecordingTools.TryGetRecordingDirAsync().ConfigureAwait(true);
            var dir = dirResult.Dir is not null && System.IO.Directory.Exists(dirResult.Dir)
                ? dirResult.Dir
                : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

            DiskBenchmarkText.Text = Strings.T("toolbox.disk.running", dir, ClampedNote(bitrateRaw, DiskBenchmarkInput.MaxBitrateKbps, "kbps"));
            AppServices.Busy.Show(Strings.T("toolbox.disk.busy"));
            try
            {
                var writeMbps = await Task.Run(() => MeasureSequentialWrite(dir)).ConfigureAwait(true);
                var verdict = DiskBenchmarkCore.Verdict(writeMbps, bitrate);
                DiskBenchmarkText.Text = verdict.Advice;
                AppServices.Toast.Show(verdict.Pass ? Strings.T("toolbox.disk.ok") : Strings.T("toolbox.disk.risk"), verdict.Pass ? "ok" : "error");
            }
            finally
            {
                AppServices.Busy.Hide();
            }
        }
        catch (Exception ex)
        {
            DiskBenchmarkText.Text = Strings.T("toolbox.disk.exception", ex.Message);
            AppServices.Toast.Show(Strings.T("toolbox.disk.failed", ex.Message), "error");
        }
    }

    /// <summary>
    /// 向目录顺序写入临时文件并返回 MB/s（V3.0 起实现搬到 <see cref="DiskBenchmarkService"/>：
    /// 「开播前体检」也要用它，服务层共用一份实现，口径不会漂移）。
    /// </summary>
    internal static double MeasureSequentialWrite(string dir) => DiskBenchmarkService.MeasureSequentialWrite(dir);

    private static long FreeBytesOf(string dir)
    {
        var free = DiskBenchmarkService.FreeBytesOf(dir);
        return free > 0 ? free : long.MaxValue;
    }

    // ------------------------------------------------------------ 编码顾问（V2.7）

    private void OnRunEncoderAdvice(object sender, RoutedEventArgs e)
    {
        try
        {
            var gpu = DetectGpuName();
            var scenario = EncoderAdvisorCore.Scenario.Both;
            var advice = EncoderAdvisorCore.Recommend(gpu, scenario, DualEncodeCheck.IsChecked == true);

            EncoderAdviceText.Visibility = Visibility.Visible;
            EncoderAdviceText.Text = advice.Advice;
        }
        catch (Exception ex)
        {
            AppServices.Toast.Show(Strings.T("toolbox.encoder.failed", ex.Message), "error");
        }
    }

    /// <summary>从注册表显卡类驱动的 DriverDesc 枚举显卡名；全部失败返回 null（走通用建议）。</summary>
    internal static string? DetectGpuName()
    {
        const string classKey =
            @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(classKey);
            if (root is null) return null;

            foreach (var sub in root.GetSubKeyNames())
            {
                if (!sub.StartsWith("0", StringComparison.Ordinal)) continue;
                try
                {
                    using var k = root.OpenSubKey(sub);
                    if (k?.GetValue("DriverDesc") is string desc && desc.Length > 0 &&
                        !desc.StartsWith("HDA", StringComparison.OrdinalIgnoreCase))
                    {
                        return desc;
                    }
                }
                catch (Exception) { }
            }
        }
        catch (Exception) { }
        return null;
    }

    // ------------------------------------------------------------ 推流节点探测（V2.7）

    private async void OnRunIngestPing(object sender, RoutedEventArgs e)
    {
        try
        {
            IngestList.ItemsSource = null;
            IngestHintText.Text = Strings.T("toolbox.ingest.probing");

            var targets = new List<IngestTarget>(IngestPingService.DefaultTargets);
            var custom = CustomHostInput.Text.Trim();
            if (custom.Length > 0)
            {
                var parts = custom.Split(':');
                var host = parts[0].Trim();
                var port = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 1935;
                targets.Insert(0, new IngestTarget(Strings.T("toolbox.ingest.customLabel"), host, port));
            }

            var results = await IngestPingService.MeasureAllAsync(targets.Where(t => t.Host.Length > 0))
                .ConfigureAwait(true);

            IngestList.ItemsSource = results;
            IngestHintText.Text =
                Strings.T("toolbox.ingest.result", results.Count(r => r.Ok),
                    (results.Count > 0 && results[0].Ok
                        ? Strings.T("toolbox.ingest.best", results[0].Target.Label, results[0].RttText)
                        : "") + Strings.T("toolbox.ingest.note"));
        }
        catch (Exception ex)
        {
            IngestHintText.Text = Strings.T("toolbox.ingest.failed");
            AppServices.Toast.Show(Strings.T("toolbox.ingest.failedToast", ex.Message), "error");
        }
    }

    // ------------------------------------------------------------ 浏览器源健康检查（V2.7）

    private void OnOpenObsConfigDir(object sender, RoutedEventArgs e)
    {
        try
        {
            var err = RecordingToolsService.OpenInExplorer(
                System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "obs-studio"));
            if (err is not null) AppServices.Toast.Show(err, "error");
        }
        catch (Exception ex)
        {
            AppServices.Toast.Show(Strings.T("toolbox.openDirFailed", ex.Message), "error");
        }
    }
}