using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using OBS_Helper.Wpf.Errors;
using OBS_Helper.Wpf.Navigation;
using OBS_Helper.Wpf.Services.Host;
using OBS_Helper.Wpf.Services.Obs;
using OBS_Helper.Wpf.Services.Plugins;

namespace OBS_Helper.Wpf.Views;

/// <summary>
/// 日志分析页：从本机 OBS 日志目录挑一份，或用文件选择器手动指定，离线扫描已知故障特征。
///
/// 只有这两种来源——日志正文不允许手动粘贴，避免用户贴进未脱敏的第三方文本。
/// 分析结果会写进 <see cref="Services.Ai.DiagnosticOrchestrator.LatestReport"/>，供「智能诊断」直接引用。
/// </summary>
public partial class LogsPage : UserControl, INavigationAware
{
    /// <summary>与宿主读取策略一致：超过 8MB 只取尾部，关键错误都集中在末尾。</summary>
    private const long MaxLogBytes = 8L * 1024 * 1024;

    private List<HostLogFile> _files = new();
    private string? _selectedPath;
    private ObsLogReport? _report;
    private bool _busy;

    public LogsPage()
    {
        InitializeComponent();

        var hosted = AppServices.Host.IsAvailable;
        NoHostPanel.Visibility = hosted ? Visibility.Collapsed : Visibility.Visible;
        RefreshButton.Visibility = hosted ? Visibility.Visible : Visibility.Collapsed;
        OpenDirButton.Visibility = hosted ? Visibility.Visible : Visibility.Collapsed;
        LogDirText.Text = hosted ? $"日志目录：{HostBridge.ObsLogDirectory}" : "";
        LogDirText.Visibility = hosted ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>页面实例会被复用，每次进入都重新列一次目录——日志文件随每次开播新增。</summary>
    public async Task OnNavigatedToAsync(object? parameter)
    {
        if (AppServices.Host.IsAvailable) await ReloadListAsync();
    }

    // ---------------------------------------------------------- 日志来源

    private async void OnRefreshList(object sender, RoutedEventArgs e) => await ReloadListAsync();

    private async Task ReloadListAsync()
    {
        SetBusy(true, "正在读取日志目录…");
        try
        {
            _files = await AppServices.Host.ListObsLogsAsync();
            RenderFileList();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void RenderFileList()
    {
        FileList.Children.Clear();
        foreach (var f in _files) FileList.Children.Add(BuildFileRow(f));
    }

    private Button BuildFileRow(HostLogFile f)
    {
        var selected = string.Equals(f.Path, _selectedPath, StringComparison.OrdinalIgnoreCase);

        var name = MakeText(f.Name, "FontSizeBase", selected ? "BrandBrush" : "TextBrush");
        name.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;

        var meta = MakeText($"{f.SizeText} · {f.ModifiedText}", "FontSizeXs", "MutedBrush");
        meta.Margin = new Thickness(0, 4, 0, 0);

        var body = new StackPanel();
        body.Children.Add(name);
        body.Children.Add(meta);

        var row = new Button
        {
            Content = body,
            Tag = f,
            Style = TryFindResource("CardButton") as Style,
            Margin = new Thickness(0, 0, 0, 8)
        };
        row.Click += OnFileRowClick;
        return row;
    }

    private async void OnFileRowClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: HostLogFile f }) await ReadAndAnalyzeAsync(f);
    }

    private async Task ReadAndAnalyzeAsync(HostLogFile f)
    {
        if (_busy) return;

        _selectedPath = f.Path;
        RenderFileList();

        SetBusy(true, "正在读取并分析日志…");
        AppServices.Busy.Show("正在分析日志…");
        try
        {
            var text = await AppServices.Host.ReadObsLogAsync(f.Path);
            if (string.IsNullOrEmpty(text))
            {
                // 文件被占用 / 已删除 / 不在允许目录内：给一份空报告，明确告诉用户没读到内容
                _report = new ObsLogReport { SourceName = f.Name };
                RenderReport();
                return;
            }
            await AnalyzeAsync(text, f.Name);
        }
        finally
        {
            SetBusy(false);
            AppServices.Busy.Hide();
        }
    }

    private async void OnPickFile(object sender, RoutedEventArgs e)
    {
        if (_busy) return;

        var dlg = new OpenFileDialog
        {
            Title = "选择 OBS 日志文件",
            Filter = "OBS 日志 (*.txt;*.log)|*.txt;*.log|所有文件 (*.*)|*.*",
            CheckFileExists = true
        };
        if (Directory.Exists(HostBridge.ObsLogDirectory))
            dlg.InitialDirectory = HostBridge.ObsLogDirectory;

        if (dlg.ShowDialog() != true) return;

        var path = dlg.FileName;
        _selectedPath = path;
        RenderFileList();

        SetBusy(true, "正在读取并分析日志…");
        AppServices.Busy.Show("正在分析日志…");
        try
        {
            var text = await Task.Run(() => ReadTail(path));
            await AnalyzeAsync(text, Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            App.ReportError(ErrorCodes.DataLoadFailed, ex);
        }
        finally
        {
            SetBusy(false);
            AppServices.Busy.Hide();
        }
    }

    private void OnOpenDir(object sender, RoutedEventArgs e)
    {
        if (!AppServices.Host.OpenFolder(HostBridge.ObsLogDirectory))
            ShowHint("日志目录不存在，可能尚未运行过 OBS。");
    }

    /// <summary>读取日志文本；超过 8MB 只取尾部，避免把整份大日志读进内存。</summary>
    private static string ReadTail(string path)
    {
        var info = new FileInfo(path);
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (info.Length > MaxLogBytes) fs.Seek(info.Length - MaxLogBytes, SeekOrigin.Begin);
        using var sr = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return sr.ReadToEnd();
    }

    /// <summary>规则匹配是逐行正则，8MB 日志能跑上百毫秒，放后台线程免得界面卡住。</summary>
    private async Task AnalyzeAsync(string text, string name)
    {
        _report = await Task.Run(() =>
        {
            var report = AppServices.Analyzer.Analyze(text, name);
            AppendAiPluginCostFinding(report);
            return report;
        }).ConfigureAwait(true);
        AppServices.Orchestrator.LatestReport = _report;
        RenderReport();
    }

    /// <summary>
    /// P1-2 联动：日志存在渲染 / 编码滞后，且本机体检查到已安装的 AI 类插件时，
    /// 追加一条「AI 插件开销」提示，把掉帧与 AI 推理的固定开销关联起来。
    /// </summary>
    private static void AppendAiPluginCostFinding(ObsLogReport report)
    {
        try
        {
            var perfHit = report.Findings.Any(f =>
                f.Code is "LOG-STAT-RENDER" or "LOG-STAT-ENCODE"
                        or "LOG-RENDER-LAG" or "LOG-ENC-OVERLOAD");
            if (!perfHit) return;

            var scan = AppServices.PluginScanner.Scan();
            var catalog = AppServices.PluginCatalog.GetData();

            var aiInstalled = new List<string>();
            foreach (var installed in scan.Plugins)
            {
                var entry = PluginCatalogCore.MatchByDll(catalog, installed.FileName);
                if (entry is { HasAiCost: true } && !aiInstalled.Contains(entry.Name))
                    aiInstalled.Add(entry.Name);
            }

            if (aiInstalled.Count == 0) return;

            report.Findings.Add(new LogFinding
            {
                Code = "LOG-AI-COST",
                Severity = LogSeverity.Warning,
                Title = $"检测到 AI 插件可能加剧掉帧：{string.Join("、", aiInstalled)}",
                Suggestion = "AI 插件的实时推理有固定开销（抠像约 +5~15% CPU / 100~300MB 内存，字幕约 +5~10% CPU / 200~500MB）。" +
                             "排查掉帧时可先临时停用对应滤镜对比验证；确需使用请降低模型档位、改用 GPU 推理，并关闭其他高占用程序。",
                Evidence = "本机体检测到上述 AI 插件已安装，同时日志中存在渲染 / 编码滞后记录。",
                FirstLine = 0
            });

            // 与分析器保持同一排序口径：严重度优先，其次命中次数
            report.Findings.Sort((a, b) =>
            {
                var bySeverity = ((int)b.Severity).CompareTo((int)a.Severity);
                return bySeverity != 0 ? bySeverity : b.Occurrences.CompareTo(a.Occurrences);
            });
        }
        catch (Exception)
        {
            // 联动提示失败不影响日志分析主流程
        }
    }

    // ---------------------------------------------------------- 结果渲染

    private void RenderReport()
    {
        if (_report is null) return;

        ReportPanel.Visibility = Visibility.Visible;
        ReportTitleText.Text = $"② 分析结果：{_report.SourceName}";

        var has = _report.HasIssues;
        CountText.Text = has ? $"{_report.Findings.Count} 项发现" : "未发现明显问题";
        CountText.SetResourceReference(TextBlock.ForegroundProperty, has ? "DangerBrush" : "OkBrush");
        CountPill.SetResourceReference(Border.BackgroundProperty, has ? "DangerSoftBrush" : "OkSoftBrush");

        var s = _report.Summary;
        ReportMetaText.Text =
            $"OBS {Fallback(s.ObsVersion)} · {Fallback(s.Platform)} · " +
            $"渲染滞后 {Percent(s.RenderLagRatio)} · 编码滞后 {Percent(s.EncodingLagRatio)} · " +
            $"网络丢帧 {Percent(s.NetworkDropRatio)}";

        CopyHintText.Visibility = Visibility.Collapsed;

        NoFindingText.Visibility = _report.Findings.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        FindingList.Children.Clear();
        foreach (var f in _report.Findings) FindingList.Children.Add(BuildFindingCard(f));
    }

    private Border BuildFindingCard(LogFinding f)
    {
        var (fgKey, softKey) = SeverityKeys(f.Severity);

        var head = new StackPanel { Orientation = Orientation.Horizontal };

        var sevText = MakeText(f.SeverityText, "FontSizeXs", fgKey, wrap: false);
        sevText.FontWeight = FontWeights.SemiBold;
        var sevPill = new Border
        {
            Style = TryFindResource("Pill") as Style,
            Margin = new Thickness(0, 0, 8, 0),
            Child = sevText
        };
        sevPill.SetResourceReference(Border.BackgroundProperty, softKey);
        head.Children.Add(sevPill);

        var title = MakeText(f.Title, "FontSizeBase", "TextBrush", wrap: false);
        title.FontWeight = FontWeights.SemiBold;
        title.VerticalAlignment = VerticalAlignment.Center;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        head.Children.Add(title);

        if (f.Occurrences > 1)
        {
            var occ = MakeText($"×{f.Occurrences}", "FontSizeXs", "MutedBrush", wrap: false);
            occ.Margin = new Thickness(8, 0, 0, 0);
            occ.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(occ);
        }

        var body = new StackPanel();
        body.Children.Add(head);

        if (!string.IsNullOrEmpty(f.Evidence))
        {
            var evidence = new TextBox
            {
                Text = f.Evidence,
                Style = TryFindResource("AppCodeBox") as Style,
                Margin = new Thickness(0, 8, 0, 0),
                MinHeight = 0,
                MaxHeight = 90
            };
            body.Children.Add(evidence);
        }

        var suggestion = MakeText($"{f.Suggestion}", "FontSizeSm", "TextBrush");
        suggestion.Margin = new Thickness(0, 8, 0, 0);
        body.Children.Add(suggestion);

        // P0-2：插件嫌疑联动 —— 能对上广场条目的给跳转按钮，否则展示嫌疑模块名
        if (!string.IsNullOrEmpty(f.SuspectModule))
        {
            var entry = PluginCatalogCore.MatchByDll(AppServices.PluginCatalog.GetData(), f.SuspectModule);
            if (entry is not null)
            {
                var pluginLink = new Button
                {
                    Content = $"在插件广场查看「{entry.Name}」→",
                    Tag = entry.Id,
                    Style = TryFindResource("LinkButton") as Style,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 8, 0, 0),
                    ToolTip = "查看该插件的介绍、本机安装状态与官方下载"
                };
                pluginLink.Click += OnOpenPlugin;
                body.Children.Add(pluginLink);
            }
            else
            {
                var moduleText = MakeText($"嫌疑模块：{f.SuspectModule}（未收录于插件广场，可先更新或安全模式排查）",
                    "FontSizeXs", "MutedBrush");
                moduleText.Margin = new Thickness(0, 6, 0, 0);
                moduleText.TextWrapping = TextWrapping.Wrap;
                body.Children.Add(moduleText);
            }
        }

        if (!string.IsNullOrEmpty(f.ProblemId))
        {
            var link = new Button
            {
                Content = "查看分步方案 →",
                Tag = f.ProblemId,
                Style = TryFindResource("LinkButton") as Style,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 10, 0, 0)
            };
            link.Click += OnOpenProblem;
            body.Children.Add(link);
        }

        var card = new Border
        {
            Style = TryFindResource("CardTight") as Style,
            Margin = new Thickness(0, 0, 0, 10),
            Child = body
        };
        card.SetResourceReference(Border.BorderBrushProperty, softKey);
        card.SetResourceReference(Border.BackgroundProperty, "Surface2Brush");
        return card;
    }

    /// <summary>日志严重度 → (文字色, 浅底色) 资源键。</summary>
    private static (string Foreground, string Soft) SeverityKeys(LogSeverity s) => s switch
    {
        LogSeverity.Critical => ("DangerBrush", "DangerSoftBrush"),
        LogSeverity.Error => ("DangerBrush", "DangerSoftBrush"),
        LogSeverity.Warning => ("WarnBrush", "WarnSoftBrush"),
        _ => ("InfoBrush", "InfoSoftBrush")
    };

    // -------------------------------------------------------------- 动作

    private void OnCopySanitized(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_report?.SanitizedText))
        {
            ShowHint("这份日志没有可复制的内容。");
            return;
        }

        try
        {
            Clipboard.SetText(_report.SanitizedText);
            ShowHint("已复制脱敏后的日志全文。");
        }
        catch (Exception)
        {
            // 剪贴板被其他进程占用时会抛异常，属可预期情况，页面内提示即可
            ShowHint("剪贴板暂时不可用，请稍后重试。");
        }
    }

    private void OnOpenDiagnostic(object sender, RoutedEventArgs e)
        => AppServices.Navigation?.Navigate(Routes.Diagnostic);

    private void OnOpenProblem(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id } && !string.IsNullOrEmpty(id))
            AppServices.Navigation?.Navigate(Routes.Problem, id);
    }

    /// <summary>P0-2：跳转到插件广场并定位到对应插件卡片。</summary>
    private void OnOpenPlugin(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id } && !string.IsNullOrEmpty(id))
            AppServices.Navigation?.Navigate(Routes.Plugins, id);
    }

    // -------------------------------------------------------------- 杂项

    private void SetBusy(bool busy, string? hint = null)
    {
        _busy = busy;
        RefreshButton.IsEnabled = !busy;
        PickButton.IsEnabled = !busy;
        FileList.IsEnabled = !busy;

        ListHintText.Text = hint ?? "";
        ListHintText.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowHint(string text)
    {
        CopyHintText.Text = text;
        CopyHintText.Visibility = Visibility.Visible;
    }

    private static string Percent(double ratio)
        => (ratio * 100).ToString("0.##", CultureInfo.InvariantCulture) + "%";

    private static string Fallback(string value) => string.IsNullOrWhiteSpace(value) ? "未知" : value;

    /// <summary>建一个跟随主题的文本块：字号与颜色都用资源引用，换肤 / 改字号时自动生效。</summary>
    private static TextBlock MakeText(string text, string sizeKey, string brushKey, bool wrap = true)
    {
        var tb = new TextBlock
        {
            Text = text,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap
        };
        tb.SetResourceReference(TextBlock.FontSizeProperty, sizeKey);
        tb.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        return tb;
    }
}
