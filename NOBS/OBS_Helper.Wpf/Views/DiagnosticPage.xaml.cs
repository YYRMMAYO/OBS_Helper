using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using OBS_Helper.Wpf.Errors;
using OBS_Helper.Wpf.Navigation;
using OBS_Helper.Wpf.Services.Ai;

namespace OBS_Helper.Wpf.Views;

/// <summary>
/// 配置诊断页：智能诊断（本地规则 / 云端大模型）+ 快速自检清单 + 平台相关速查。
///
/// 页面实例被导航缓存复用，所以引擎模式、日志报告、OBS 连接状态都必须在
/// <see cref="OnNavigatedToAsync"/> 里重新读取，否则会停留在第一次进入时的快照。
/// </summary>
public partial class DiagnosticPage : UserControl, INavigationAware
{
    /// <summary>自检清单一项。勾选状态只存在内存里，与 Blazor 版一致（刷新即重置）。</summary>
    private sealed class CheckItem
    {
        public required string Text { get; init; }
        public bool Done { get; set; }
    }

    private readonly List<CheckItem> _checks = new()
    {
        new CheckItem { Text = "OBS 以管理员身份运行（右键 → 以管理员身份运行）" },
        new CheckItem { Text = "编码器使用硬件编码（NVENC / AMF / QSV）" },
        new CheckItem { Text = "视频码率不超过实际上行速度的 75%" },
        new CheckItem { Text = "捕获方式正确（游戏/窗口/显示器，且双显卡统一 GPU）" },
        new CheckItem { Text = "音频采样率统一为 48kHz" },
        new CheckItem { Text = "关闭 Chrome / Discord 等程序的硬件加速" },
        new CheckItem { Text = "使用有线网络推流（避免 WiFi 2.4G）" },
        new CheckItem { Text = "推流服务器与串流密钥正确" }
    };

    private bool _diagnosing;

    /// <summary>最近一次成功的诊断结果（「导出报告」用，页面被缓存复用，离开再回来仍可导出）。</summary>
    private DiagnosticResult? _lastResult;

    /// <summary>发起诊断时的原始描述（导出报告用，避免用户之后改了输入框影响报告内容）。</summary>
    private string _lastQuery = "";

    public DiagnosticPage()
    {
        InitializeComponent();
        BuildChecks();
        RefreshCheckProgress();
        RefreshHeader();
    }

    public Task OnNavigatedToAsync(object? parameter)
    {
        RefreshHeader();
        return Task.CompletedTask;
    }

    // ------------------------------------------------------ 顶部状态提示

    private void RefreshHeader()
    {
        var ai = AppServices.AiSettings;
        var engineName = ai.Mode switch
        {
            DiagnosticEngineMode.Free => "免费 AI（内置）",
            DiagnosticEngineMode.Cloud => "云端大模型",
            _ => "本地的搜索助手"
        };

        EngineText.Inlines.Clear();
        EngineText.Inlines.Add(new Run("当前引擎："));
        EngineText.Inlines.Add(new Run(engineName) { FontWeight = FontWeights.SemiBold });

        var report = AppServices.Orchestrator.LatestReport;
        if (report is { HasIssues: true })
            EngineText.Inlines.Add(new Run($" · 已载入日志分析报告（{report.Findings.Count} 项发现）"));

        // 选了云端但配置不完整：直接告诉用户会走本地，并给出配置入口
        var cloudReady = AppServices.Orchestrator.CanUseCloud;
        CloudHintText.Text = "云端引擎尚未配置完整（需 https 接口地址 + 已保存的 API Key），本次诊断将使用本地的搜索助手。";
        CloudHintPanel.Visibility = ai.Mode == DiagnosticEngineMode.Cloud && !cloudReady ? Visibility.Visible : Visibility.Collapsed;

        ObsHintPanel.Visibility = AppServices.Obs.IsConnected ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---------------------------------------------------------- 智能诊断

    private async void OnDiagnose(object sender, RoutedEventArgs e)
    {
        if (_diagnosing) return;

        _diagnosing = true;
        _lastQuery = QueryBox.Text;
        DiagnoseButton.IsEnabled = false;
        DiagnoseButton.Content = "分析中…";
        BusyText.Visibility = Visibility.Visible;
        AppServices.Busy.Show("正在智能诊断…");

        try
        {
            var result = await AppServices.Orchestrator.DiagnoseAsync(QueryBox.Text);
            RenderResult(result);
        }
        catch (Exception ex)
        {
            // 编排器内部已处理云端失败并回退，能漏到这里的都是意料之外的故障
            App.ReportError(ErrorCodes.AiCloudRequestFailed, ex);
        }
        finally
        {
            _diagnosing = false;
            DiagnoseButton.IsEnabled = true;
            DiagnoseButton.Content = "诊断";
            BusyText.Visibility = Visibility.Collapsed;
            AppServices.Busy.Hide();
            RefreshHeader();
        }
    }

    private void RenderResult(DiagnosticResult r)
    {
        _lastResult = r.Success ? r : null;
        // 免费内置 AI 无诊断项，只要结论非空即可导出
        ExportButton.IsEnabled = r.Success && (r.Items.Count > 0 || !string.IsNullOrWhiteSpace(r.Summary));

        ResultPanel.Visibility = Visibility.Visible;
        ItemList.Children.Clear();

        var failed = !r.Success;
        ErrorText.Text = r.Error ?? "诊断失败，请稍后重试。";
        ErrorText.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;

        // 回退时 Success 仍为 true，Error 里带着云端失败原因，这里单独说明一次
        FallbackText.Text = failed || string.IsNullOrEmpty(r.Error)
            ? "已自动回退到本地离线引擎。"
            : $"已自动回退到本地离线引擎（{r.Error}）。";
        FallbackText.Visibility = r.FellBackToLocal ? Visibility.Visible : Visibility.Collapsed;

        if (failed)
        {
            SummaryPanel.Visibility = Visibility.Collapsed;
            return;
        }

        SummaryText.Text = r.Summary;
        SummaryPanel.Visibility = string.IsNullOrWhiteSpace(r.Summary)
            ? Visibility.Collapsed
            : Visibility.Visible;

        foreach (var it in r.Items) ItemList.Children.Add(BuildItemCard(it));
    }

    private Border BuildItemCard(DiagnosticItem it)
    {
        var (fgKey, softKey) = SeverityKeys(it.Severity);

        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var sevText = MakeText(it.SeverityText, "FontSizeXs", fgKey, wrap: false);
        sevText.FontWeight = FontWeights.SemiBold;
        var sevPill = new Border
        {
            Style = TryFindResource("Pill") as Style,
            Margin = new Thickness(0, 0, 8, 0),
            Child = sevText
        };
        sevPill.SetResourceReference(Border.BackgroundProperty, softKey);
        Grid.SetColumn(sevPill, 0);
        head.Children.Add(sevPill);

        var title = MakeText(it.Title, "FontSizeBase", "TextBrush");
        title.FontWeight = FontWeights.SemiBold;
        title.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(title, 1);
        head.Children.Add(title);

        var source = MakeText(it.Source, "FontSizeXs", "MutedBrush", wrap: false);
        source.Margin = new Thickness(8, 0, 0, 0);
        source.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(source, 2);
        head.Children.Add(source);

        var body = new StackPanel();
        body.Children.Add(head);

        if (!string.IsNullOrEmpty(it.Reason))
        {
            var reason = MakeText($"{it.Reason}", "FontSizeSm", "MutedBrush");
            reason.Margin = new Thickness(0, 8, 0, 0);
            body.Children.Add(reason);
        }

        for (var i = 0; i < it.Steps.Count; i++)
        {
            var step = MakeText($"{i + 1}. {it.Steps[i]}", "FontSizeSm", "TextBrush");
            step.Margin = new Thickness(0, i == 0 ? 8 : 4, 0, 0);
            body.Children.Add(step);
        }

        if (!string.IsNullOrEmpty(it.ProblemId))
        {
            var link = new Button
            {
                Content = "查看分步方案 →",
                Tag = it.ProblemId,
                Style = TryFindResource("LinkButton") as Style,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 10, 0, 0)
            };
            link.Click += OnOpenProblem;
            body.Children.Add(link);
        }

        // P0-2：嫌疑插件联动 —— 日志线索定位到具体插件时给「插件广场」跳转
        if (!string.IsNullOrEmpty(it.SuspectModule))
        {
            var entry = Services.Plugins.PluginCatalogCore.MatchByDll(
                AppServices.PluginCatalog.GetData(), it.SuspectModule);
            if (entry is not null)
            {
                var pluginLink = new Button
                {
                    Content = $"在插件广场查看「{entry.Name}」→",
                    Tag = entry.Id,
                    Style = TryFindResource("LinkButton") as Style,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 8, 0, 0)
                };
                pluginLink.Click += OnOpenPlugin;
                body.Children.Add(pluginLink);
            }
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

    /// <summary>严重度 → (文字色, 浅底色) 资源键。严重与错误共用红色系，靠文案与字重区分。</summary>
    private static (string Foreground, string Soft) SeverityKeys(DiagnosticSeverity s) => s switch
    {
        DiagnosticSeverity.Critical => ("DangerBrush", "DangerSoftBrush"),
        DiagnosticSeverity.Error => ("DangerBrush", "DangerSoftBrush"),
        DiagnosticSeverity.Warning => ("WarnBrush", "WarnSoftBrush"),
        DiagnosticSeverity.Suggestion => ("BrandBrush", "BrandSoftBrush"),
        _ => ("InfoBrush", "InfoSoftBrush")
    };

    // ---------------------------------------------------------- 自检清单

    private void BuildChecks()
    {
        for (var i = 0; i < _checks.Count; i++)
        {
            var item = _checks[i];

            var no = MakeText((i + 1).ToString(), "FontSizeXs", "MutedBrush", wrap: false);
            var noPill = new Border
            {
                Style = TryFindResource("Pill") as Style,
                Margin = new Thickness(0, 0, 8, 0),
                MinWidth = 24,
                Child = no
            };
            no.HorizontalAlignment = HorizontalAlignment.Center;

            var text = MakeText(item.Text, "FontSizeBase", "TextBrush");
            text.VerticalAlignment = VerticalAlignment.Center;

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(noPill);
            row.Children.Add(text);

            var box = new CheckBox
            {
                Content = row,
                Tag = item,
                Style = TryFindResource("AppCheckBox") as Style,
                Margin = new Thickness(0, 0, 0, 10)
            };
            box.Checked += OnCheckToggled;
            box.Unchecked += OnCheckToggled;
            CheckList.Children.Add(box);
        }
    }

    private void OnCheckToggled(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: CheckItem item } box)
        {
            item.Done = box.IsChecked == true;
            RefreshCheckProgress();
        }
    }

    private void OnResetChecks(object sender, RoutedEventArgs e)
    {
        foreach (var box in CheckList.Children.OfType<CheckBox>()) box.IsChecked = false;
        // 勾选状态由 Unchecked 事件同步回 _checks，这里只需刷新一次进度
        RefreshCheckProgress();
    }

    private void RefreshCheckProgress()
    {
        var done = _checks.Count(c => c.Done);
        CheckProgress.Value = _checks.Count == 0 ? 0 : 100.0 * done / _checks.Count;
        CheckCountText.Text = $"已完成 {done} / {_checks.Count}";
    }

    // -------------------------------------------------------------- 跳转

    private void OnOpenProblem(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id } && !string.IsNullOrEmpty(id))
            AppServices.Navigation?.Navigate(Routes.Problem, id);
    }

    // ------------------------------------------------------ 录前自检（C1，只读）

    private bool _preflightRunning;

    private async void OnPreflightRun(object sender, RoutedEventArgs e)
    {
        if (_preflightRunning) return;
        _preflightRunning = true;

        var btn = (Button)sender;
        btn.IsEnabled = false;
        btn.Content = "检查中…";

        try
        {
            var report = await AppServices.Preflight.RunAsync();
            RenderPreflight(report);
        }
        catch (Exception)
        {
            AppServices.Toast.Show("录前自检失败，请稍后重试", "warn");
        }
        finally
        {
            _preflightRunning = false;
            btn.IsEnabled = true;
            btn.Content = "一键自检";
        }
    }

    private void RenderPreflight(Services.Obs.PreflightReport report)
    {
        PreflightList.Children.Clear();

        var head = report.FailCount > 0
            ? $"发现问题 {report.FailCount} 项 · 建议 {report.WarnCount} 项"
            : report.WarnCount > 0
                ? $"无阻塞问题，{report.WarnCount} 项可优化"
                : "全部通过";
        var headText = MakeText(head, "FontSizeSm",
            report.FailCount > 0 ? "DangerBrush" : report.WarnCount > 0 ? "WarnBrush" : "OkBrush");
        headText.FontWeight = FontWeights.SemiBold;
        PreflightList.Children.Add(headText);

        foreach (var item in report.Items)
        {
            PreflightList.Children.Add(BuildPreflightRow(item));
        }
    }

    private FrameworkElement BuildPreflightRow(Services.Obs.PreflightItem item)
    {
        var fgKey = item.Status switch
        {
            Services.Obs.PreflightStatus.Ok => "OkBrush",
            Services.Obs.PreflightStatus.Warn => "WarnBrush",
            Services.Obs.PreflightStatus.Fail => "DangerBrush",
            _ => "MutedBrush"
        };

        var statusText = MakeText(item.StatusText, "FontSizeXs", fgKey, wrap: false);
        statusText.FontWeight = FontWeights.SemiBold;
        var statusPill = new Border
        {
            Style = TryFindResource("Pill") as Style,
            MinWidth = 52,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Child = statusText
        };
        statusPill.SetResourceReference(Border.BackgroundProperty, "Surface2Brush");

        var body = new StackPanel();
        var title = MakeText(item.Title, "FontSizeBase", "TextBrush");
        title.FontWeight = FontWeights.SemiBold;
        body.Children.Add(title);

        if (!string.IsNullOrEmpty(item.Detail))
        {
            var detail = MakeText(item.Detail, "FontSizeSm", "MutedBrush");
            detail.Margin = new Thickness(0, 3, 0, 0);
            body.Children.Add(detail);
        }

        if (!string.IsNullOrEmpty(item.ProblemId))
        {
            var link = new Button
            {
                Content = "查看分步方案 →",
                Tag = item.ProblemId,
                Style = TryFindResource("LinkButton") as Style,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 4, 0, 0)
            };
            link.Click += OnOpenProblem;
            body.Children.Add(link);
        }

        var grid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(statusPill, 0);
        Grid.SetColumn(body, 1);
        grid.Children.Add(statusPill);
        grid.Children.Add(body);
        return grid;
    }

    /// <summary>P0-2：跳转插件广场并定位嫌疑插件。</summary>
    private void OnOpenPlugin(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id } && !string.IsNullOrEmpty(id))
            AppServices.Navigation?.Navigate(Routes.Plugins, id);
    }

    private void OnOpenLogs(object sender, RoutedEventArgs e)
        => AppServices.Navigation?.Navigate(Routes.Logs);

    private void OnOpenSettings(object sender, RoutedEventArgs e)
        => AppServices.Navigation?.Navigate(Routes.Settings);

    private void OnOpenConsole(object sender, RoutedEventArgs e)
        => AppServices.Navigation?.Navigate(Routes.Console);

    private void OnOpenAnalyzer(object sender, RoutedEventArgs e)
        => _ = AppServices.Host.OpenExternalAsync("https://obsproject.com/analyzer/");

    // ------------------------------------------------------------ 导出报告

    /// <summary>把最近一次成功诊断的结果导出为 Markdown 文件（纯本地，不走网络）。</summary>
    private void OnExportReport(object sender, RoutedEventArgs e)
    {
        var result = _lastResult;
        // 免费内置 AI 不产生诊断项（无工具调用），只回结论文本——只要有结论也允许导出
        if (result is null || (result.Items.Count == 0 && string.IsNullOrWhiteSpace(result.Summary))) return;

        var sb = new StringBuilder();
        sb.AppendLine("# OBS 诊断报告");
        sb.AppendLine();
        sb.AppendLine($"- 生成时间：{result.CreatedAt:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"- 引擎：{(result.Engine switch
        {
            "free" => "免费内置 AI",
            "cloud" => "云端大模型",
            _ => "本地规则引擎"
        })}{(result.FellBackToLocal ? "（云端/免费失败已回退本地）" : "")}");
        sb.AppendLine($"- 问题描述：{_lastQuery.Trim()}");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(result.Summary))
        {
            sb.AppendLine("## 结论");
            sb.AppendLine();
            sb.AppendLine(result.Summary);
            sb.AppendLine();
        }

        sb.AppendLine("## 发现");
        sb.AppendLine();
        foreach (var it in result.Items)
        {
            sb.AppendLine($"### [{it.SeverityText}] {it.Title}（来源：{it.Source}）");
            if (!string.IsNullOrWhiteSpace(it.Reason))
            {
                sb.AppendLine();
                sb.AppendLine(it.Reason);
            }
            if (it.Steps.Count > 0)
            {
                sb.AppendLine();
                for (var i = 0; i < it.Steps.Count; i++) sb.AppendLine($"{i + 1}. {it.Steps[i]}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine("由 OBS 排障助手生成");

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出诊断报告",
            Filter = "Markdown 文档 (*.md)|*.md|文本文件 (*.txt)|*.txt",
            FileName = $"OBS诊断报告_{DateTime.Now:yyyyMMdd_HHmmss}.md",
            DefaultExt = ".md",
            AddExtension = true
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        try
        {
            File.WriteAllText(dialog.FileName, sb.ToString(), new UTF8Encoding(false));
            AppServices.Toast.Show($"诊断报告已导出：{Path.GetFileName(dialog.FileName)}", "ok");
        }
        catch (Exception ex)
        {
            App.ReportError(ErrorCodes.DiagnosticExportFailed, ex);
        }
    }

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
