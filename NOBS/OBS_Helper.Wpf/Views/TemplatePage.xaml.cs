using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OBS_Helper.Wpf.Controls;
using OBS_Helper.Wpf.Errors;
using OBS_Helper.Wpf.Models.ObsConfig;
using OBS_Helper.Wpf.Navigation;
using OBS_Helper.Wpf.Services.ObsConfig;
using OBS_Helper.Wpf.Services.Plugins;

namespace OBS_Helper.Wpf.Views;

/// <summary>
/// 场景模板页：展示 6 套内置直播间模板。
///
/// 连上 OBS 时可一键落地（websocket 实时创建场景与来源），
/// 未连接时可导出为标准场景集合 JSON 文件，手动放入 OBS 目录。
/// 每套模板的设备 / 文件 / URL 来源留空，落地后汇总成「还需手动补齐」清单。
/// </summary>
public partial class TemplatePage : UserControl, INavigationAware
{
    private IReadOnlyList<SceneTemplate> _templates = Array.Empty<SceneTemplate>();
    private bool _busy;
    /// <summary>模板加载是否出过错（不阻止页面展示但卡片上会标记）。</summary>
    private string? _loadError;

    public TemplatePage()
    {
        InitializeComponent();
    }

    public async Task OnNavigatedToAsync(object? parameter)
    {
        // 依赖标注用的体检结果每次进入都重扫（只读、毫秒级），保证「已装/未装」状态新鲜
        _depScan = null;
        await LoadTemplatesAsync();
        BuildCards();
    }

    // -------------------------------------------------------------- 加载

    private async Task LoadTemplatesAsync()
    {
        try
        {
            _templates = await AppServices.Templates.LoadAsync();
            _loadError = null;
        }
        catch (Exception ex)
        {
            _templates = Array.Empty<SceneTemplate>();
            _loadError = $"模板数据加载失败：{ex.Message}";
            App.ReportError(ErrorCodes.DataLoadFailed, ex);
        }
    }

    // -------------------------------------------------------------- 卡片构建

    private void BuildCards()
    {
        TemplateList.Children.Clear();

        if (_templates.Count == 0)
        {
            EmptyText.Text = _loadError ?? "没有可用的模板数据，请重新安装程序。";
            EmptyText.Visibility = Visibility.Visible;
            TemplateList.Visibility = Visibility.Collapsed;
            return;
        }

        EmptyText.Visibility = Visibility.Collapsed;
        TemplateList.Visibility = Visibility.Visible;

        foreach (var t in _templates)
        {
            TemplateList.Children.Add(BuildCard(t));
        }
    }

    private FrameworkElement BuildCard(SceneTemplate t)
    {
        var card = new Border
        {
            Style = (Style)FindResource("Card"),
            Margin = new Thickness(0, 0, 0, 14)
        };

        var stack = new StackPanel();

        // --- 头部：图标 + 标题 + 摘要（模板数据未带图标时不再兜底 emoji，直接省略图标列）
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        if (!string.IsNullOrEmpty(t.Icon))
        {
            var icon = new TextBlock
            {
                Text = t.Icon,
                FontSize = 24,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            DockPanel.SetDock(icon, Dock.Left);
            header.Children.Add(icon);
        }

        var titleStack = new StackPanel();
        var title = new TextBlock
        {
            Text = t.Title,
            FontWeight = FontWeights.Bold
        };
        title.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeMd");
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        titleStack.Children.Add(title);

        if (!string.IsNullOrEmpty(t.Summary))
        {
            var summary = new TextBlock
            {
                Text = t.Summary,
                TextWrapping = TextWrapping.Wrap
            };
            summary.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSm");
            summary.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            titleStack.Children.Add(summary);
        }

        header.Children.Add(titleStack);
        stack.Children.Add(header);

        // --- 画布信息
        var canvas = t.Canvas;
        var canvasText = new TextBlock
        {
            Text = $"画布 {canvas.BaseWidth}×{canvas.BaseHeight} → 输出 {canvas.OutputWidth}×{canvas.OutputHeight} · {canvas.FpsNumerator} FPS",
            Margin = new Thickness(0, 0, 0, 6)
        };
        canvasText.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeXs");
        canvasText.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        stack.Children.Add(canvasText);

        // --- 场景 / 来源 / 待补
        var counts = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        counts.Children.Add(BuildPill($"场景 ×{t.Scenes.Count}", ""));
        var totalSources = t.Scenes.Sum(s => s.Sources.Count);
        var placeholders = t.Scenes.Sum(s => s.Sources.Count(x => x.Placeholder is not null));
        counts.Children.Add(BuildPill($"来源 ×{totalSources}", ""));
        if (placeholders > 0)
            counts.Children.Add(BuildPill($"待补 ×{placeholders}", "WarnBrush"));
        stack.Children.Add(counts);

        // --- 推荐插件依赖标注（P2-2）：对照本机体检结果显示「已安装 / 未安装」
        AddPluginRequirementRow(t, stack);

        // --- 场景设置：过渡 + 快捷键
        var hotkeys = t.Scenes.Where(s => !string.IsNullOrWhiteSpace(s.Hotkey)).Select(s => $"{s.Hotkey} {s.Name}").ToList();
        var transitionInfo = $"{t.Transition} {t.TransitionDurationMs}ms";
        if (hotkeys.Count > 0)
            transitionInfo += $" · 快捷键 {string.Join(" / ", hotkeys)}";
        var sceneSettings = new TextBlock
        {
            Text = $"场景切换：{transitionInfo}",
            Margin = new Thickness(0, 0, 0, 6)
        };
        sceneSettings.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeXs");
        sceneSettings.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        stack.Children.Add(sceneSettings);

        // --- 竖屏警告
        var isPortrait = t.Canvas.BaseWidth < t.Canvas.BaseHeight;
        if (isPortrait)
        {
            var portraitWarn = new TextBlock
            {
                Text = "竖屏模板：落地后请确认 OBS 视频设置已改为竖屏分辨率，否则画面将会拉伸变形。",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };
            portraitWarn.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSm");
            portraitWarn.SetResourceReference(TextBlock.ForegroundProperty, "WarnBrush");
            stack.Children.Add(portraitWarn);
        }

        // --- 备注
        if (!string.IsNullOrEmpty(t.Notes))
        {
            var notesBlock = new TextBlock
            {
                Text = t.Notes,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };
            notesBlock.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSm");
            notesBlock.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            stack.Children.Add(notesBlock);
        }

        // --- 按钮行
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var applyBtn = new Button
        {
            Content = "落地到 OBS",
            Style = (Style)FindResource("PrimaryButton"),
            Tag = t.Id,
            Margin = new Thickness(0, 0, 10, 0)
        };
        applyBtn.Click += OnApplyClick;
        buttons.Children.Add(applyBtn);

        var exportBtn = new Button
        {
            Content = "导出场景集合 JSON",
            Style = (Style)FindResource("SecondaryButton"),
            Tag = t.Id
        };
        exportBtn.Click += OnExportClick;
        buttons.Children.Add(exportBtn);

        stack.Children.Add(buttons);

        card.Child = stack;
        return card;
    }

    private static Border BuildPill(string text, string colorKey)
    {
        var pill = new Border
        {
            Style = (Style)Application.Current.FindResource("Pill"),
            Margin = new Thickness(0, 0, 8, 0)
        };
        var tb = new TextBlock
        {
            Text = text
        };
        tb.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeXs");
        if (!string.IsNullOrEmpty(colorKey))
            tb.SetResourceReference(TextBlock.ForegroundProperty, colorKey);
        else
            tb.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        pill.Child = tb;
        return pill;
    }

    // ---------------------------------------------------- 插件依赖标注（P2-2）

    private LocalPluginScanResult? _depScan;

    /// <summary>确保本机体检结果可用（页面内缓存一次）。</summary>
    private LocalPluginScanResult? EnsureDepScan()
    {
        try { _depScan ??= AppServices.PluginScanner.Scan(); }
        catch (Exception) { _depScan ??= new LocalPluginScanResult(); }
        return _depScan;
    }

    private static bool IsRequirementInstalled(TemplatePluginRequirement req,
        PluginEntry? entry, LocalPluginScanResult? scan)
    {
        if (entry is null || scan is null) return false;
        return scan.Plugins.Any(ip =>
            string.Equals(ip.CatalogId, entry.Id, StringComparison.OrdinalIgnoreCase) ||
            entry.Dlls.Any(alias =>
                !string.IsNullOrWhiteSpace(alias) &&
                string.Equals(alias.Trim(), ip.Stem, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>对照体检结果，返回模板缺失的推荐插件（名称 + 用途说明）。</summary>
    private List<(string Name, string Reason)> GetMissingRequirements(SceneTemplate t)
    {
        var missing = new List<(string, string)>();
        if (t.RequiresPlugins.Count == 0) return missing;

        var catalog = AppServices.PluginCatalog.GetData();
        var scan = t.RequiresPlugins.Count > 0 ? EnsureDepScan() : null;

        foreach (var req in t.RequiresPlugins)
        {
            var entry = PluginCatalogCore.FindById(catalog, req.Id);
            if (!IsRequirementInstalled(req, entry, scan))
                missing.Add((entry?.Name ?? req.Id, req.Reason));
        }
        return missing;
    }

    /// <summary>在卡片上显示推荐插件行：已安装绿标；未安装黄标，可点击跳转插件广场对应卡片。</summary>
    private void AddPluginRequirementRow(SceneTemplate t, StackPanel stack)
    {
        if (t.RequiresPlugins.Count == 0) return;

        var catalog = AppServices.PluginCatalog.GetData();
        var scan = EnsureDepScan();

        var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        var label = new TextBlock
        {
            Text = "推荐插件：",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 2, 8)
        };
        label.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeXs");
        label.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        row.Children.Add(label);

        foreach (var req in t.RequiresPlugins)
        {
            var entry = PluginCatalogCore.FindById(catalog, req.Id);
            var name = entry?.Name ?? req.Id;
            var installed = IsRequirementInstalled(req, entry, scan);

            var text = new TextBlock
            {
                Text = installed ? $"{name} ✓已安装" : $"{name} 未安装 →",
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            text.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeXs");
            text.SetResourceReference(TextBlock.ForegroundProperty, installed ? "OkBrush" : "WarnBrush");

            var pill = new Border
            {
                Style = (Style)Application.Current.FindResource("Pill"),
                Margin = new Thickness(0, 0, 8, 8),
                Child = text,
                ToolTip = installed
                    ? $"用途：{req.Reason}"
                    : $"用途：{req.Reason}。点击前往插件广场查看与下载"
            };

            if (!installed && entry is not null)
            {
                var btn = new Button
                {
                    Style = (Style)Application.Current.FindResource("LinkButton"),
                    Content = pill,
                    Padding = new Thickness(0),
                    Tag = entry.Id
                };
                btn.Click += OnMissingPluginClick;
                row.Children.Add(btn);
            }
            else
            {
                row.Children.Add(pill);
            }
        }

        stack.Children.Add(row);
    }

    private void OnMissingPluginClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id } && !string.IsNullOrEmpty(id))
            AppServices.Navigation?.Navigate(Routes.Plugins, id);
    }

    // -------------------------------------------------------------- 操作

    private async void OnApplyClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (sender is not Button btn || btn.Tag is not string id) return;

        var t = _templates.FirstOrDefault(x => x.Id == id);
        if (t is null) return;

        if (!AppServices.Obs.IsConnected)
        {
            ShowStatus("warn", "尚未连接 OBS。请先去「OBS 控制台」连上 obs-websocket 后再试，或改用「导出场景集合 JSON」离线使用。");
            return;
        }

        var confirmMsg = $"将在 OBS 中新建一个干净的配置集合并切换过去，为你创建 {t.Scenes.Count} 个场景、{t.Scenes.Sum(s => s.Sources.Count)} 个来源。设备 / 文件 / URL 类来源需您后续手动补齐。";

        // P2-2：落地前对照本机体检结果提示缺失的推荐插件（不阻断）
        var missing = GetMissingRequirements(t);
        if (missing.Count > 0)
        {
            confirmMsg += $"\n\n注意：未检测到推荐插件 {string.Join("、", missing.Select(m => m.Name))}（{string.Join("；", missing.Select(m => m.Reason))}）。模板仍可落地，相关能力可稍后补装。";
        }

        if (!ConfirmDialog.Show(
                $"落地模板「{t.Title}」",
                confirmMsg + "\n\n确认继续？",
                "落地", "取消"))
        {
            return;
        }

        SetBusy(true);
        try
        {
            var result = await AppServices.Templates.ApplyAsync(id, applyCanvas: true, ct: CancellationToken.None, p: null!);
            if (result.Ok)
            {
                var msg = $"模板「{t.Title}」已落地！共创建 {result.Created} 个来源";
                if (result.Skipped > 0) msg += $"（{result.Skipped} 个已跳过）";
                if (result.Placeholders.Count > 0)
                    msg += $"\n\n还需手动补齐：\n  · " + string.Join("\n  · ", result.Placeholders);
                ShowStatus("ok", msg);
            }
            else
            {
                ShowStatus("danger", $"模板落地失败：{result.Error ?? "未知错误"}");
                App.ReportError(ErrorCodes.TemplateApplyFailed);
            }
        }
        catch (Exception ex)
        {
            ShowStatus("danger", $"模板落地时发生异常：{ex.Message}");
            App.ReportError(ErrorCodes.TemplateApplyFailed, ex);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (sender is not Button btn || btn.Tag is not string id) return;

        var t = _templates.FirstOrDefault(x => x.Id == id);
        if (t is null) return;

        // 用 WPF 内置的文件夹选择器
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = $"导出模板「{t.Title}」",
            FileName = SceneTemplateService.Slugify(t.Id),
            DefaultExt = ".json",
            Filter = "OBS 场景集合 (*.json)|*.json"
        };

        if (dialog.ShowDialog() != true) return;

        SetBusy(true);
        try
        {
            var dir = Path.GetDirectoryName(dialog.FileName)!;
            var baseName = Path.GetFileNameWithoutExtension(dialog.FileName);
            await AppServices.Templates.ExportToObsAsync(id, dir, CancellationToken.None);
            ShowStatus("ok", $"已导出到 {dir}（文件名以 obshelper_{SceneTemplateService.Slugify(t.Id)} 开头，请放入 OBS 的 basic/scenes/ 目录）。");
        }
        catch (Exception ex)
        {
            ShowStatus("danger", $"导出失败：{ex.Message}");
            App.ReportError(ErrorCodes.TemplateApplyFailed, ex);
        }
        finally
        {
            SetBusy(false);
        }
    }

    // -------------------------------------------------------------- 辅助

    private void SetBusy(bool busy)
    {
        _busy = busy;
        TemplateList.IsEnabled = !busy;
    }

    private void ShowStatus(string kind, string text)
    {
        // kind: "info" / "ok" / "warn" / "danger" → 切换矢量状态图标
        StatusIcon.Tag = kind;
        StatusIconInfo.Visibility = kind == "info" ? Visibility.Visible : Visibility.Collapsed;
        StatusIconOk.Visibility = kind == "ok" ? Visibility.Visible : Visibility.Collapsed;
        StatusIconWarn.Visibility = kind == "warn" ? Visibility.Visible : Visibility.Collapsed;
        StatusIconDanger.Visibility = kind == "danger" ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = text;
        StatusBar.Visibility = Visibility.Visible;
    }
}
