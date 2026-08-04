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

        // --- 头部：图标 + 标题 + 摘要
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var icon = new TextBlock
        {
            Text = string.IsNullOrEmpty(t.Icon) ? "🧩" : t.Icon,
            FontSize = 24,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        DockPanel.SetDock(icon, Dock.Left);
        header.Children.Add(icon);

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

        // --- 竖屏警告
        var isPortrait = t.Canvas.BaseWidth < t.Canvas.BaseHeight;
        if (isPortrait)
        {
            var portraitWarn = new TextBlock
            {
                Text = "⚠️ 竖屏模板：落地后请确认 OBS 视频设置已改为竖屏分辨率，否则画面将会拉伸变形。",
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
            Content = "🚀 落地到 OBS",
            Style = (Style)FindResource("PrimaryButton"),
            Tag = t.Id,
            Margin = new Thickness(0, 0, 10, 0)
        };
        applyBtn.Click += OnApplyClick;
        buttons.Children.Add(applyBtn);

        var exportBtn = new Button
        {
            Content = "📥 导出场景集合 JSON",
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

    // -------------------------------------------------------------- 操作

    private async void OnApplyClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (sender is not Button btn || btn.Tag is not string id) return;

        var t = _templates.FirstOrDefault(x => x.Id == id);
        if (t is null) return;

        if (!AppServices.Obs.IsConnected)
        {
            ShowStatus("⚠️", "尚未连接 OBS。请先去「OBS 控制台」连上 obs-websocket 后再试，或改用「导出场景集合 JSON」离线使用。");
            return;
        }

        if (!ConfirmDialog.Show(
                $"落地模板「{t.Title}」",
                $"将在 OBS 中新建一个干净的配置集合并切换过去，为你创建 {t.Scenes.Count} 个场景、{t.Scenes.Sum(s => s.Sources.Count)} 个来源。设备 / 文件 / URL 类来源需您后续手动补齐。\n\n确认继续？",
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
                ShowStatus("✅", msg);
            }
            else
            {
                ShowStatus("❌", $"模板落地失败：{result.Error ?? "未知错误"}");
                App.ReportError(ErrorCodes.TemplateApplyFailed);
            }
        }
        catch (Exception ex)
        {
            ShowStatus("❌", $"模板落地时发生异常：{ex.Message}");
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
            ShowStatus("✅", $"已导出到 {dir}（文件名以 obshelper_{SceneTemplateService.Slugify(t.Id)} 开头，请放入 OBS 的 basic/scenes/ 目录）。");
        }
        catch (Exception ex)
        {
            ShowStatus("❌", $"导出失败：{ex.Message}");
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

    private void ShowStatus(string icon, string text)
    {
        StatusIcon.Text = icon;
        StatusText.Text = text;
        StatusBar.Visibility = Visibility.Visible;
    }
}
