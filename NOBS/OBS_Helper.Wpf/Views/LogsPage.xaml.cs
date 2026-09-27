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
        LogDirText.Text = hosted ? Strings.T("logs.dir", HostBridge.ObsLogDirectory) : "";
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
        SetBusy(true, Strings.T("logs.readingDir"));
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

        SetBusy(true, Strings.T("logs.readingLog"));
        AppServices.Busy.Show(Strings.T("logs.analyzing"));
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
            Title = Strings.T("logs.pickTitle"),
            Filter = Strings.T("logs.pickFilter"),
            CheckFileExists = true
        };
        if (Directory.Exists(HostBridge.ObsLogDirectory))
            dlg.InitialDirectory = HostBridge.ObsLogDirectory;

        if (dlg.ShowDialog() != true) return;

        var path = dlg.FileName;
        _selectedPath = path;
        RenderFileList();

        SetBusy(true, Strings.T("logs.readingLog"));
        AppServices.Busy.Show(Strings.T("logs.analyzing"));
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
            ShowHint(Strings.T("logs.dirMissing"));
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
                Title = Strings.T("logs.aiPlugin.title", string.Join(", ", aiInstalled)),
                Suggestion = Strings.T("logs.aiPlugin.suggestion"),
                Evidence = Strings.T("logs.aiPlugin.evidence"),
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
        ReportTitleText.Text = Strings.T("logs.reportTitle", _report.SourceName);

        var has = _report.HasIssues;
        CountText.Text = has ? Strings.T("logs.findingsCount", _report.Findings.Count) : Strings.T("logs.noIssues");
        CountText.SetResourceReference(TextBlock.ForegroundProperty, has ? "DangerBrush" : "OkBrush");
        CountPill.SetResourceReference(Border.BackgroundProperty, has ? "DangerSoftBrush" : "OkSoftBrush");

        var s = _report.Summary;
        ReportMetaText.Text =
            $"OBS {Fallback(s.ObsVersion)} · {Fallback(s.Platform)} · " +
            Strings.T("logs.summary", Percent(s.RenderLagRatio), Percent(s.EncodingLagRatio), Percent(s.NetworkDropRatio));

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
                    Content = Strings.T("plugin.viewInSquare", entry.Name),
                    Tag = entry.Id,
                    Style = TryFindResource("LinkButton") as Style,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 8, 0, 0),
                    ToolTip = Strings.T("logs.pluginCardTip")
                };
                pluginLink.Click += OnOpenPlugin;
                body.Children.Add(pluginLink);
            }
            else
            {
                var moduleText = MakeText(Strings.T("logs.suspectUnknown", f.SuspectModule),
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
                Content = Strings.T("problem.viewSteps"),
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
            ShowHint(Strings.T("logs.nothingToCopy"));
            return;
        }

        try
        {
            Clipboard.SetText(_report.SanitizedText);
            ShowHint(Strings.T("logs.copiedSanitized"));
        }
        catch (Exception)
        {
            // 剪贴板被其他进程占用时会抛异常，属可预期情况，页面内提示即可
            ShowHint(Strings.T("logs.clipboardUnavailable"));
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

    private static string Fallback(string value) => string.IsNullOrWhiteSpace(value) ? Strings.T("common.unknown") : value;

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
