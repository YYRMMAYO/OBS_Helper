using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using OBS_Helper.Wpf.Services.ObsConfig;
using OBS_Helper.Wpf.Services.Tools;

namespace OBS_Helper.Wpf.Views;

/// <summary>
/// 工具箱（V2.6）：录屏与直播的实用工具合集。
/// 所有操作均为只读探测或独立进程调用，不修改 OBS 配置；失败一律降级为提示。
/// </summary>
public partial class ToolboxPage : UserControl
{
    /// <summary>场景化参数处方（静态内置数据）。</summary>
    private static readonly (string Name, string Text)[] Presets =
    {
        ("录网课 / 视频会议",
         "画布：1920x1080（30fps 足够）\n" +
         "编码器：核显 QSV 或独显 NVENC/AMF（人像画面 CPU 占用低）\n" +
         "录像格式：MKV 或 Hybrid MP4（防崩溃），码率 4000~8000kbps\n" +
         "音频：麦克风 + 桌面音频双轨；麦克风加 RNNoise 降噪\n" +
         "来源：窗口捕获会议软件「共享内容」窗口，或显示器捕获兜底\n" +
         "提示：录前跑一遍隐私清单，试录 10 秒验证画面与声音"),
        ("录游戏",
         "画布：1920x1080 · 60fps（低配降到 720p60）\n" +
         "编码器：NVENC/AMF 硬件编码（P5 预设平衡画质与性能）\n" +
         "录像格式：MKV，码率 15000~20000kbps（1080p60 高动态）\n" +
         "关键帧间隔：2 秒\n" +
         "捕获方式：优先「游戏捕获」；反作弊游戏黑屏改「显示器捕获」\n" +
         "提示：游戏锁帧到刷新率以下，给 OBS 合成留 GPU 余量"),
        ("竖屏短视频（抖音 / TikTok）",
         "画布：1080x1920 · 30fps（横屏素材旋转或居中排版）\n" +
         "编码器：硬件编码优先，码率 8000~12000kbps\n" +
         "录像格式：MKV 录制 → 平台上传前转封装 MP4\n" +
         "布局：主体内容居中，字幕放安全区（避开平台 UI 遮挡区）\n" +
         "提示：画面边缘最容易带出隐私内容，裁剪后回看一遍再发布"),
        ("直播带货 / 人像直播",
         "画布：1920x1080 · 30fps（人像场景 30fps 即可，画质更从容）\n" +
         "编码器：NVENC/AMF，码率 4500~6000kbps（按上行带宽定）\n" +
         "推流：平台自定义 RTMP + 串流密钥；关键帧 2 秒\n" +
         "音频：压缩器侧链让 BGM 在说话时自动让位；限制器防爆音\n" +
         "提示：开播前用工具箱带宽计算器核对上行是否够用"),
    };

    private string _releaseUrl = "https://github.com/obsproject/obs-studio/releases";

    public ToolboxPage()
    {
        InitializeComponent();

        foreach (var (name, _) in Presets)
            PresetCombo.Items.Add(name);
        PresetCombo.SelectedIndex = 0;

        Loaded += OnLoadedAsync;
    }

    private async void OnLoadedAsync(object sender, RoutedEventArgs e)
    {
        // ffmpeg 探测很快，但 PATH 扫描仍可能涉及几十个目录，放到后台线程
        var ffmpeg = await System.Threading.Tasks.Task.Run(RecordingToolsService.FindFfmpeg)
            .ConfigureAwait(true);
        FfmpegText.Text = ffmpeg is null
            ? "未检测到 ffmpeg：转封装将给出替代方案指引（OBS 自带「文件 → 录像转封装」无需 ffmpeg）。"
            : $"已检测到 ffmpeg：{ffmpeg}";
    }

    // ------------------------------------------------------------ 录像工具

    private async void OnOpenRecordingDir(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = await AppServices.RecordingTools.TryGetRecordingDirAsync().ConfigureAwait(true);
            if (result.Dir is null)
            {
                RecordingDirText.Text = "未能解析出录像目录。";
                return;
            }

            RecordingDirText.Text = $"当前保存位置（{result.Source}）：{result.Dir}";
            var err = RecordingToolsService.OpenInExplorer(result.Dir);
            if (err is not null) AppServices.Toast.Show(err, "error");
        }
        catch (Exception ex)
        {
            AppServices.Toast.Show($"打开录像目录失败：{ex.Message}", "error");
        }
    }

    private async void OnRemuxPickFile(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择要转封装的录像文件",
                Filter = "录像文件 (*.mkv;*.mp4;*.flv;*.mov;*.ts)|*.mkv;*.mp4;*.flv;*.mov;*.ts|所有文件 (*.*)|*.*"
            };
            if (dlg.ShowDialog() != true) return;

            RemuxResultText.Visibility = Visibility.Visible;
            RemuxResultText.Text = $"正在转封装：{System.IO.Path.GetFileName(dlg.FileName)} …";
            AppServices.Busy.Show("正在无损转封装…");
            try
            {
                var (ok, message) = await RecordingToolsService.RemuxToMp4Async(dlg.FileName).ConfigureAwait(true);
                RemuxResultText.Text = (ok ? "[完成] " : "[失败] ") + message;
                AppServices.Toast.Show(ok ? "转封装完成" : "转封装失败", ok ? "ok" : "error");
            }
            finally
            {
                AppServices.Busy.Hide();
            }
        }
        catch (Exception ex)
        {
            AppServices.Toast.Show($"转封装异常：{ex.Message}", "error");
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
            AppServices.Toast.Show("处方已复制到剪贴板", "ok");
        }
        catch (Exception ex)
        {
            AppServices.Toast.Show($"复制失败：{ex.Message}", "error");
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
            AppServices.Toast.Show($"无法打开{label}：{ex.Message}", "error");
        }
    }

    private void OnOpenFocusAssist(object sender, RoutedEventArgs e) => OpenMsSettings("ms-settings:quietmoments", "专注助手设置");

    private void OnOpenNotifications(object sender, RoutedEventArgs e) => OpenMsSettings("ms-settings:notifications", "通知设置");

    private void OnOpenPersonalization(object sender, RoutedEventArgs e) => OpenMsSettings("ms-settings:personalization", "个性化设置");

    // ------------------------------------------------------------ 冲突扫描

    private void OnScanConflicts(object sender, RoutedEventArgs e)
    {
        try
        {
            var names = Process.GetProcesses()
                .Select(p => { using var _ = p; return SafeProcessName(p); })
                .Where(n => n.Length > 0);

            var hits = ConflictScannerCore.Scan(names);

            ConflictList.ItemsSource = hits;
            ConflictEmptyText.Visibility = hits.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ConflictSummaryText.Text = hits.Count == 0
                ? "扫描完成，未发现已知冲突。"
                : $"发现 {hits.Count} 项：高危 {hits.Count(h => h.Risk == "高")}、中危 {hits.Count(h => h.Risk == "中")}、提示 {hits.Count(h => h.Risk == "提示")}";
        }
        catch (Exception ex)
        {
            AppServices.Toast.Show($"扫描失败：{ex.Message}", "error");
        }
    }

    private static string SafeProcessName(Process p)
    {
        try { return p.ProcessName ?? ""; }
        catch (Exception) { return ""; } // 已退出 / 权限不足的进程会抛
    }

    // ------------------------------------------------------------ 带宽计算器

    private void OnBandwidthInputChanged(object sender, TextChangedEventArgs e)
    {
        if (RecommendText is null || MultiStreamText is null) return; // XAML 初始化阶段
        UpdateBandwidthAdvice();
    }

    private void UpdateBandwidthAdvice()
    {
        var upload = TryParseDouble(UploadInput.Text);
        RecommendText.Text = BandwidthAdvisorCore.Recommend(upload).Advice;

        if (!double.IsNaN(upload))
        {
            var streams = BandwidthAdvisorCore.ClampToInt(TryParseDouble(StreamCountInput.Text), BandwidthAdvisorCore.MaxStreams);
            var bitrate = BandwidthAdvisorCore.ClampToInt(TryParseDouble(StreamBitrateInput.Text), BandwidthAdvisorCore.MaxSingleBitrateKbps);
            MultiStreamText.Text = BandwidthAdvisorCore.DescribeMultiStream(upload, streams, bitrate);
        }
    }

    private static double TryParseDouble(string? raw)
        => double.TryParse(raw?.Trim(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) && v >= 0 ? v : double.NaN;

    // ------------------------------------------------------------ OBS 新版本情报

    private async void OnFetchRelease(object sender, RoutedEventArgs e)
    {
        try
        {
            ReleaseText.Text = "正在查询…";
            var info = await AppServices.ObsRelease.GetLatestAsync().ConfigureAwait(true);
            if (info is null)
            {
                ReleaseText.Text = "查询失败且无本地缓存。请检查网络后重试，或直接前往发布页查看。";
                return;
            }

            _releaseUrl = info.Url;
            var sourceTag = info.Source switch
            {
                "live" => "",
                "cache" => "（来自缓存）",
                _ => "（离线快照，可能不是最新）"
            };
            ReleaseText.Text =
                $"最新版本：OBS Studio {info.Tag}　发布日期：{info.PublishedText}{sourceTag}\n" +
                $"{info.Summary}\n" +
                "升级建议：稳定版用户一般值得跟进补丁版；跨大版本升级前先备份场景集合" +
                "（知识库「升级 / 试 Beta 前备份」条目有完整步骤）。";
        }
        catch (Exception ex)
        {
            ReleaseText.Text = $"查询异常：{ex.Message}";
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
            AppServices.Toast.Show($"打开链接失败：{ex.Message}", "error");
        }
    }
}
