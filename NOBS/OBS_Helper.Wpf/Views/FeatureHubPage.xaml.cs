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
        return Task.CompletedTask;
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
        var icon = new Path
        {
            Data = (Geometry)FindResource(module.IconKey),
            Fill = (Brush)FindResource("BrandBrush"),
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
