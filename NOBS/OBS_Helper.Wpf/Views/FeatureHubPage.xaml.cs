using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using OBS_Helper.Wpf.Navigation;
using OBS_Helper.Wpf.Services.Shell;

namespace OBS_Helper.Wpf.Views;

/// <summary>
/// 功能中心（V3.4.0）：把所有新功能按模块集中展示，手机联动作为主推模块置顶。
///
/// 设计目的：新功能不能只“加在首页某个角落”，必须在这里登记为模块 ——
/// 用户一眼能看到有哪些能力、每个模块包含什么、从哪里进入。
/// </summary>
public partial class FeatureHubPage : UserControl, INavigationAware
{
    public FeatureHubPage()
    {
        InitializeComponent();
        BuildModules();
        _builtLanguage = Strings.Current;
    }

    /// <summary>页面实例被导航缓存复用；语言切换后必须重建代码生成的模块卡片（V3.4.2）。</summary>
    private string? _builtLanguage;

    public Task OnNavigatedToAsync(object? parameter)
    {
        var language = Strings.Current;
        if (!string.Equals(_builtLanguage, language, StringComparison.Ordinal))
        {
            _builtLanguage = language;
            BuildModules();
        }
        RefreshStatus();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 「现在能做什么」状态条（V3.5.0）：OBS / 手机服务 / 媒体服务 / 提词器。
    /// 只读、不启动任何服务；没有配置时直接告诉用户下一步。
    /// </summary>
    private void RefreshStatus()
    {
        StatusHost.Children.Clear();

        var obs = AppServices.Obs.IsConnected;
        AddStatusChip(Strings.T("feature.status.obs"), obs ? Strings.T("feature.status.connected") : Strings.T("feature.status.disconnected"), obs ? "OkBrush" : "WarnBrush");

        var phone = AppServices.MobileLink.IsRunning;
        AddStatusChip(Strings.T("feature.status.phone"), phone ? Strings.T("feature.status.running") : Strings.T("feature.status.stopped"), phone ? "OkBrush" : "MutedBrush");

        var entry = AppServices.Sidecars.Manifest.Find("mediamtx");
        var media = entry is not null && AppServices.Sidecars.IsInstalled(entry);
        AddStatusChip(Strings.T("feature.status.media"), media ? Strings.T("feature.status.installed") : Strings.T("feature.status.missing"), media ? "OkBrush" : "WarnBrush");

        var script = AppServices.Teleprompter.Script;
        var hasScript = !string.IsNullOrWhiteSpace(script);
        AddStatusChip(Strings.T("feature.status.teleprompter"), hasScript ? Strings.T("feature.status.hasScript") : Strings.T("feature.status.noScript"), hasScript ? "OkBrush" : "MutedBrush");
    }

    private void AddStatusChip(string label, string value, string brushKey)
    {
        var dot = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center };
        dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, brushKey);

        var text = new TextBlock { Text = $"{label} · {value}", Margin = new Thickness(6, 0, 0, 0) };
        text.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeXs");
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(dot);
        row.Children.Add(text);

        var chip = new Border
        {
            Background = (Brush)FindResource("SurfaceBrush"),
            BorderBrush = (Brush)FindResource("LineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(9, 4, 9, 4),
            Margin = new Thickness(0, 0, 8, 6),
            Child = row
        };
        StatusHost.Children.Add(chip);
    }

    private void OnMobileAction(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tab })
            AppServices.Navigation.Navigate(Routes.MobileLink, tab);
    }

    private void BuildModules()
    {
        ModuleList.Children.Clear();
        // V3.4.2：目录已把主推模块排在最前，界面不再按版本号分段或贴版本标签。
        foreach (var module in FeatureCatalog.Modules)
            ModuleList.Children.Add(BuildModuleCard(module));
    }

    private Border BuildModuleCard(FeatureModule module)
    {
        var card = new Border
        {
            Style = (Style)FindResource("Card"),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 12),
        };
        if (module.Highlighted)
        {
            card.BorderBrush = (Brush)FindResource("BrandBrush");
            card.BorderThickness = new Thickness(1);
        }

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var iconChip = new Border
        {
            Width = 52,
            Height = 52,
            CornerRadius = new CornerRadius(14),
            Background = (Brush)FindResource("BrandSoftBrush"),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 14, 0),
        };
        // V3.5.1：Icons.xaml 里的几何是描边路径，Fill 会把圆 / 方框糊成实心块；
        // 与侧边导航、设置页头部保持同一套 Stroke 渲染。
        var icon = new Path
        {
            Data = (Geometry)FindResource(module.IconKey),
            Stroke = (Brush)FindResource("BrandBrush"),
            StrokeThickness = 1.8,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Width = 24,
            Height = 24,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        iconChip.Child = icon;
        grid.Children.Add(iconChip);

        var text = new StackPanel();
        Grid.SetColumn(text, 1);
        var titleRow = new WrapPanel();
        titleRow.Children.Add(new TextBlock
        {
            Text = Strings.T(module.TitleKey),
            Style = (Style)FindResource("H3Text"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        if (module.Highlighted)
        {
            titleRow.Children.Add(new Border
            {
                Background = (Brush)FindResource("BrandBrush"),
                CornerRadius = new CornerRadius(999),
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = Strings.T("feature.hub.highlight"),
                    Foreground = (Brush)FindResource("BrandForegroundBrush"),
                    FontSize = (double)FindResource("FontSizeXs"),
                },
            });
        }
        titleRow.Children.Add(new TextBlock
        {
            Text = Strings.T("feature.hub.count", module.Features.Count),
            Style = (Style)FindResource("CaptionText"),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        text.Children.Add(titleRow);
        text.Children.Add(new TextBlock
        {
            Text = Strings.T(module.DescKey),
            Style = (Style)FindResource("BodyText"),
            Margin = new Thickness(0, 4, 0, 8),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 760,
            HorizontalAlignment = HorizontalAlignment.Left,
        });

        var chips = new WrapPanel();
        foreach (var feature in module.Features)
        {
            chips.Children.Add(new Border
            {
                Background = (Brush)FindResource("Surface2Brush"),
                BorderBrush = (Brush)FindResource("LineBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(999),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 0, 6, 6),
                Child = new TextBlock
                {
                    Text = Strings.T(feature.TitleKey),
                    FontSize = (double)FindResource("FontSizeXs"),
                    Foreground = (Brush)FindResource("TextBrush"),
                },
            });
        }
        text.Children.Add(chips);
        grid.Children.Add(text);

        var open = new Button
        {
            Content = Strings.T("feature.hub.open"),
            Style = (Style)FindResource(module.Highlighted ? "PrimaryButton" : "SecondaryButton"),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(14, 0, 0, 0),
            Tag = (module.Route, module.Tab),
        };
        open.Click += OnOpenModule;
        Grid.SetColumn(open, 2);
        grid.Children.Add(open);

        card.Child = grid;
        return card;
    }

    private void OnOpenModule(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ValueTuple<string, string> target }) return;
        AppServices.Navigation.Navigate(target.Item1, string.IsNullOrEmpty(target.Item2) ? null : target.Item2);
    }
}
