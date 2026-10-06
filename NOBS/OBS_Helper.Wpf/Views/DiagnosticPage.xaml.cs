using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using OBS_Helper.Wpf.Controls;
using OBS_Helper.Wpf.Services.ObsConfig;
using OBS_Helper.Wpf.Services.Diagnostics;
using OBS_Helper.Wpf.Errors;
using OBS_Helper.Wpf.Navigation;
using OBS_Helper.Wpf.Services;
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
    /// <summary>
    /// 自检清单一项。
    ///
    /// V3.0 修了两件事：
    /// ① 勾选状态**落盘**（原先只在内存里，刷新即重置 —— 而问题页的步骤勾选一直是落盘的，
    ///    两处口径不一致，用户第二天回来要重新勾一遍）；
    /// ② 文案存**键**而不是成品字符串（原先在字段初始化时就取 `Strings.T`，
    ///    切英文后这一块仍是中文）。
    /// </summary>
    private sealed class CheckItem
    {
        public required string Key { get; init; }
        public bool Done { get; set; }
        /// <summary>渲染出来的文本块，供语言切换后就地刷新。</summary>
        public TextBlock? Label { get; set; }
        /// <summary>本机实测证据（V3.0 / D4）；未回填时为 null。</summary>
        public TextBlock? EvidenceLabel { get; set; }
    }

    /// <summary>清单项的文案键（顺序即界面顺序）。</summary>
    private static readonly string[] CheckKeys =
    {
        "diagnostic.check.1", "diagnostic.check.2", "diagnostic.check.3", "diagnostic.check.4",
        "diagnostic.check.5", "diagnostic.check.6", "diagnostic.check.7", "diagnostic.check.8"
    };

    private readonly List<CheckItem> _checks = CheckKeys.Select(k => new CheckItem { Key = k }).ToList();

    /// <summary>清单勾选的落盘键与结构。</summary>
    private const string ChecklistStoreKey = "diagnostic_checklist";

    private sealed class SavedChecklist
    {
        public Dictionary<string, bool> Items { get; set; } = new();
        public string SavedAt { get; set; } = "";
    }

    private bool _diagnosing;

    /// <summary>最近一次成功的诊断结果（「导出报告」用，页面被缓存复用，离开再回来仍可导出）。</summary>
    private DiagnosticResult? _lastResult;

    /// <summary>发起诊断时的原始描述（导出报告用，避免用户之后改了输入框影响报告内容）。</summary>
    private string _lastQuery = "";

    public DiagnosticPage()
    {
        InitializeComponent();
        LoadChecklist();
        BuildChecks();
        RefreshCheckProgress();
        RefreshHeader();
    }

    public Task OnNavigatedToAsync(object? parameter)
    {
        // V3.0：语言切换后清单文案要跟着变（文案存在键上，这里就地重取）
        foreach (var item in _checks)
        {
            if (item.Label is not null) item.Label.Text = Strings.T(item.Key);
        }
        RefreshCheckProgress();   // V3.0 审查修正：进度文案（x/y）也必须跟随语言，而不是停在旧语言
        RefreshHeader();
        return Task.CompletedTask;
    }

    // ------------------------------------------------------ 自检清单的落盘

    private void LoadChecklist()
    {
        try
        {
            var saved = AppServices.Store.GetObject<SavedChecklist>(ChecklistStoreKey);
            if (saved is null) return;
            foreach (var item in _checks)
                if (saved.Items.TryGetValue(item.Key, out var done)) item.Done = done;
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Checklist", $"自检清单读取失败（按未勾选处理）：{ex.Message}");
        }
    }

    private void SaveChecklist()
    {
        try
        {
            AppServices.Store.SetObject(ChecklistStoreKey, new SavedChecklist
            {
                Items = _checks.ToDictionary(c => c.Key, c => c.Done),
                SavedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm")
            });
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Checklist", $"自检清单保存失败：{ex.Message}");
        }
    }

    // ------------------------------------------------------ 顶部状态提示

    private void RefreshHeader()
    {
        var ai = AppServices.AiSettings;
        var engineName = ai.Mode switch
        {
            DiagnosticEngineMode.Free => Strings.T("engine.free"),
            DiagnosticEngineMode.Cloud => Strings.T("engine.cloud"),
            _ => Strings.T("engine.local")
        };

        EngineText.Inlines.Clear();
        EngineText.Inlines.Add(new Run(Strings.T("diagnostic.currentEngine")));
        EngineText.Inlines.Add(new Run(engineName) { FontWeight = FontWeights.SemiBold });

        var report = AppServices.Orchestrator.LatestReport;
        if (report is { HasIssues: true })
            EngineText.Inlines.Add(new Run(Strings.T("diagnostic.logReportLoaded", report.Findings.Count)));

        // 选了云端但配置不完整：直接告诉用户会走本地，并给出配置入口
        var cloudReady = AppServices.Orchestrator.CanUseCloud;
        CloudHintText.Text = Strings.T("diagnostic.cloudNotConfigured");
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
        DiagnoseButton.Content = Strings.T("diagnostic.buttonAnalyzing");
        BusyText.Visibility = Visibility.Visible;
        AppServices.Busy.Show(Strings.T("diagnostic.busy"));

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
            DiagnoseButton.Content = Strings.T("diagnostic.run");
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
        ErrorText.Text = r.Error ?? Strings.T("diagnostic.failed");
        ErrorText.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;

        // 回退时 Success 仍为 true，Error 里带着云端失败原因，这里单独说明一次
        FallbackText.Text = failed || string.IsNullOrEmpty(r.Error)
            ? Strings.T("diagnostic.fellBack")
            : Strings.T("diagnostic.fellBackWithReason", r.Error);
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
                Content = Strings.T("problem.viewSteps"),
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
                    Content = Strings.T("plugin.viewInSquare", entry.Name),
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

            var text = MakeText(Strings.T(item.Key), "FontSizeBase", "TextBrush");
            text.VerticalAlignment = VerticalAlignment.Center;
            item.Label = text;   // 语言切换后由 OnNavigatedToAsync 就地刷新

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(noPill);
            row.Children.Add(text);

            // 证据行（V3.0 / D4）：回填后就地显示「本机实测到什么、为什么这么判」
            var evidence = MakeText("", "FontSizeXs", "MutedBrush");
            evidence.Margin = new Thickness(34, 2, 0, 0);
            evidence.Visibility = Visibility.Collapsed;
            item.EvidenceLabel = evidence;

            var rowWithEvidence = new StackPanel();
            rowWithEvidence.Children.Add(row);
            rowWithEvidence.Children.Add(evidence);

            var box = new CheckBox
            {
                Content = rowWithEvidence,
                Tag = item,
                Style = TryFindResource("AppCheckBox") as Style,
                IsChecked = item.Done,          // 回填上次的勾选状态
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
            SaveChecklist();   // V3.0：勾选落盘，第二天回来不用重勾
        }
    }

    private void OnResetChecks(object sender, RoutedEventArgs e)
    {
        foreach (var box in CheckList.Children.OfType<CheckBox>()) box.IsChecked = false;
        // 勾选状态由 Unchecked 事件同步回 _checks，这里只需刷新一次进度
        RefreshCheckProgress();
        SaveChecklist();
    }

    private void RefreshCheckProgress()
    {
        var done = _checks.Count(c => c.Done);
        CheckProgress.Value = _checks.Count == 0 ? 0 : 100.0 * done / _checks.Count;
        CheckCountText.Text = Strings.T("diagnostic.checkProgress", done, _checks.Count);
    }

    /// <summary>
    /// 按本机实测回填清单（V3.0 / D4）。
    ///
    /// 只读：采集事实 → 纯逻辑判定 → 写回勾选与证据。只有判定为「通过」的才勾上，
    /// 「需注意」与「无法判定」都不勾，并各自说明原因 —— 不替用户假装通过。
    /// </summary>
    private async void OnAutoFillChecks(object sender, RoutedEventArgs e)
    {
        AutoFillChecksButton.IsEnabled = false;
        AutoFillNoteText.Text = Strings.T("check.auto.busy");
        AutoFillNoteText.Visibility = Visibility.Visible;

        try
        {
            var evidence = await AppServices.ChecklistAutoFill.CollectAsync();
            var results = ChecklistAutoFillCore.Evaluate(evidence);

            var byKey = results.ToDictionary(r => r.ItemKey, StringComparer.Ordinal);
            var boxes = CheckList.Children.OfType<CheckBox>().ToList();

            for (var i = 0; i < _checks.Count && i < boxes.Count; i++)
            {
                var item = _checks[i];
                if (!byKey.TryGetValue(item.Key, out var result)) continue;

                var shouldCheck = ChecklistAutoFillCore.ShouldCheck(result.Status);
                item.Done = shouldCheck;
                boxes[i].IsChecked = shouldCheck;

                if (item.EvidenceLabel is not null)
                {
                    item.EvidenceLabel.Text = EvidencePrefix(result.Status) + result.Evidence;
                    item.EvidenceLabel.SetResourceReference(TextBlock.ForegroundProperty,
                        result.Status switch
                        {
                            ChecklistStatus.Pass => "OkBrush",
                            ChecklistStatus.Attention => "WarnBrush",
                            _ => "MutedBrush"
                        });
                    item.EvidenceLabel.Visibility = Visibility.Visible;
                }
            }

            RefreshCheckProgress();
            SaveChecklist();

            AutoFillNoteText.Text = Strings.T("check.auto.done",
                results.Count, ChecklistAutoFillCore.DecidedCount(results));
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Diagnostic", $"清单自动回填失败：{ex.Message}");
            AutoFillNoteText.Text = Strings.T("check.auto.failed", ex.Message);
        }
        finally
        {
            AutoFillNoteText.Visibility = Visibility.Visible;
            AutoFillChecksButton.IsEnabled = true;
        }
    }

    /// <summary>证据前缀：用符号区分「通过 / 需注意 / 无法判定」，不靠颜色单独传达信息。</summary>
    private static string EvidencePrefix(ChecklistStatus status) => status switch
    {
        ChecklistStatus.Pass => "✅ ",
        ChecklistStatus.Attention => "⚠️ ",
        _ => "ℹ️ "
    };

    // ------------------------------------------------------------ 开播前体检（V3.0 / D4 后半段）

    /// <summary>最近一次体检结果（「按建议落地」按钮要用）。</summary>
    private MachineProfile? _machineProfile;

    private async void OnRunMachineProfile(object sender, RoutedEventArgs e)
    {
        MachineRunButton.IsEnabled = false;
        MachineApplyButton.Visibility = Visibility.Collapsed;
        MachineItemsPanel.Children.Clear();

        var progress = new Progress<string>(msg => MachineConclusionText.Text = Strings.T("machine.running", msg));
        try
        {
            _machineProfile = await AppServices.MachineProfile.RunAsync(progress);
            RenderMachineProfile(_machineProfile);
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Diagnostic", $"开播前体检失败：{ex.Message}");
            MachineConclusionText.Text = Strings.T("check.auto.failed", ex.Message);
        }
        finally
        {
            MachineRunButton.IsEnabled = true;
        }
    }

    private void RenderMachineProfile(MachineProfile profile)
    {
        MachineConclusionText.Text = profile.Conclusion;

        MachineItemsPanel.Children.Clear();
        foreach (var item in profile.Items)
            MachineItemsPanel.Children.Add(BuildMachineItemRow(item));

        MachineApplyButton.Visibility = profile.SuggestedPreset is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private UIElement BuildMachineItemRow(MachineItem item)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };

        var title = new TextBlock
        {
            Text = $"{MachineProfileCore.SeveritySymbol(item.Severity)} {MachineProfileCore.AreaSymbol(item.Area)} {item.Title}",
            TextWrapping = TextWrapping.Wrap
        };
        title.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSm");
        title.SetResourceReference(TextBlock.ForegroundProperty,
            item.Severity == MachineSeverity.Blocker ? "DangerBrush"
            : item.Severity == MachineSeverity.Recommend ? "WarnBrush"
            : "TextBrush");
        panel.Children.Add(title);

        var detail = new TextBlock { Text = item.Detail, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        detail.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeXs");
        detail.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        panel.Children.Add(detail);

        return panel;
    }

    /// <summary>按建议落地录制档位（复用 SimpleRecordingService 的备份 + 回滚链路，只写录制侧）。</summary>
    private async void OnApplySuggestedPreset(object sender, RoutedEventArgs e)
    {
        if (_machineProfile?.SuggestedPreset is not { } preset) return;
        if (!ConfirmDialog.Show(
                Strings.T("machine.applyPreset"),
                Strings.T("machine.conclusion.preset", SimpleRecordingCore.Get(preset).Key),
                Strings.T("machine.applyPreset"), Strings.T("common.cancel"),
                danger: false, icon: "🎬"))
        {
            return;
        }

        MachineApplyButton.IsEnabled = false;
        AppServices.Busy.Show(Strings.T("machine.applyPresetBusy"));
        try
        {
            var (ok, error) = await AppServices.SimpleRecord.ApplyPresetAsync(preset);
            AppServices.Toast?.Show(
                ok ? Strings.T("machine.applyPresetDone") : Strings.T("machine.applyPresetBlocked", error ?? ""),
                ok ? "ok" : "warn");
        }
        catch (Exception ex)
        {
            AppServices.Toast?.Show(Strings.T("machine.applyPresetFailed", ex.Message), "warn");
        }
        finally
        {
            AppServices.Busy.Hide();
            MachineApplyButton.IsEnabled = true;
        }
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
        btn.Content = Strings.T("diagnostic.preflight.checking");

        try
        {
            var report = await AppServices.Preflight.RunAsync();
            RenderPreflight(report);
        }
        catch (Exception)
        {
            AppServices.Toast.Show(Strings.T("diagnostic.preflight.failed"), "warn");
        }
        finally
        {
            _preflightRunning = false;
            btn.IsEnabled = true;
            btn.Content = Strings.T("diagnostic.preflight.run");
        }
    }

    private void RenderPreflight(Services.Obs.PreflightReport report)
    {
        PreflightList.Children.Clear();

        var head = report.FailCount > 0
            ? Strings.T("diagnostic.preflight.failWarn", report.FailCount, report.WarnCount)
            : report.WarnCount > 0
                ? Strings.T("diagnostic.preflight.warnOnly", report.WarnCount)
                : Strings.T("diagnostic.preflight.allPass");
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
                Content = Strings.T("problem.viewSteps"),
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
        sb.AppendLine(Strings.T("diagnostic.report.title"));
        sb.AppendLine();
        sb.AppendLine(Strings.T("diagnostic.report.generatedAt", result.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")));
        sb.AppendLine(Strings.T("diagnostic.report.engine",
            (result.Engine switch
            {
                "free" => Strings.T("engine.reportFree"),
                "cloud" => Strings.T("engine.reportCloud"),
                _ => Strings.T("engine.reportLocal")
            }),
            result.FellBackToLocal ? Strings.T("diagnostic.report.fellBack") : ""));
        sb.AppendLine(Strings.T("diagnostic.report.query", _lastQuery.Trim()));
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(result.Summary))
        {
            sb.AppendLine(Strings.T("diagnostic.report.summaryHeading"));
            sb.AppendLine();
            sb.AppendLine(result.Summary);
            sb.AppendLine();
        }

        sb.AppendLine(Strings.T("diagnostic.report.findingsHeading"));
        sb.AppendLine();
        foreach (var it in result.Items)
        {
            sb.AppendLine(Strings.T("diagnostic.report.item", it.SeverityText, it.Title, it.Source));
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

        // V3.0（C7）：把最近弹出的提示一并写进报告。
        // 用户报障时最常说的是「刚才弹了句什么」，而 Toast 只停留几秒 ——
        // 把最近 30 条留在报告里，比让用户复述界面状态可靠得多。
        var toasts = AppServices.Toast?.Recent;
        if (toasts is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine(Strings.T("diagnostic.report.toastsHeading"));
            foreach (var t in toasts)
                sb.AppendLine(Strings.T("diagnostic.report.toastItem", t.LocalTime.ToString("HH:mm:ss"), t.Severity, t.Message));
        }

        sb.AppendLine("---");
        sb.AppendLine(Strings.T("diagnostic.report.footer"));

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = Strings.T("diagnostic.exportTitle"),
            Filter = Strings.T("diagnostic.exportFilter"),
            FileName = Strings.T("diagnostic.exportFileName", DateTime.Now.ToString("yyyyMMdd_HHmmss")),
            DefaultExt = ".md",
            AddExtension = true
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        try
        {
            File.WriteAllText(dialog.FileName, sb.ToString(), new UTF8Encoding(false));
            AppServices.Toast?.Show(Strings.T("diagnostic.exported", Path.GetFileName(dialog.FileName)), "ok");
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