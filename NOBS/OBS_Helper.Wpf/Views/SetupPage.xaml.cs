using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OBS_Helper.Wpf.Controls;
using OBS_Helper.Wpf.Models;
using OBS_Helper.Wpf.Navigation;

namespace OBS_Helper.Wpf.Views;

/// <summary>
/// 直播间搭建向导。上半部分是固定的六步搭建流程（点一步直达对应问题 / 诊断页），
/// 下半部分是按平台筛选的 setup 分类问题列表。
///
/// 流程与平台两组数据在原 Blazor 页面里就是页内静态数组，没有进知识库 JSON，
/// 这里原样搬过来，保持文案与跳转目标逐字一致。
/// </summary>
public partial class SetupPage : UserControl, INavigationAware
{
    /// <summary>搭建流程六步。Href 沿用原版写法：无斜杠是路由名，带斜杠是「路由/参数」。</summary>
    private static (string No, string Title, string Desc, string Href)[] Flow => new[]
    {
        ("1", Strings.T("setup.step1.title"), Strings.T("setup.step1.desc"), "diagnostic"),
        ("2", Strings.T("setup.step2.title"), Strings.T("setup.step2.desc"), "problem/st-scene"),
        ("3", Strings.T("setup.step3.title"), Strings.T("setup.step3.desc"), "problem/au-mic"),
        ("4", Strings.T("setup.step4.title"), Strings.T("setup.step4.desc"), "problem/st-general"),
        ("5", Strings.T("setup.step5.title"), Strings.T("setup.step5.desc"), "diagnostic"),
        ("6", Strings.T("setup.step6.title"), Strings.T("setup.step6.desc"), "problem/st-multi"),
    };

    /// <summary>
    /// 平台筛选项。Kw 是匹配问题标题 / id 的关键词，"all" 表示不过滤。
    /// Color 沿用原版数据：原版 CSS 里 .chip 并没有用到这个色值，此处同样只作数据保留。
    /// </summary>
    private static (string Key, string Label, string Icon, string Color, string Kw)[] Platforms => new[]
    {
        ("all", Strings.T("setup.platform.all"), "📋", "#8e44ad", ""),
        ("bilibili", Strings.T("setup.platform.bilibili"), "📺", "#fb7299", Strings.T("setup.platformKw.bilibili")),
        ("douyin", Strings.T("setup.platform.douyin"), "🎵", "#fe2c55", Strings.T("setup.platformKw.douyin")),
        ("kuaishou", Strings.T("setup.platform.kuaishou"), "⚡", "#ff4906", Strings.T("setup.platformKw.kuaishou")),
        ("youtube", "YouTube", "▶️", "#ff0000", "YouTube"),
        ("twitch", "Twitch", "🟣", "#9146ff", "Twitch"),
        ("videoaccount", Strings.T("setup.platform.videoaccount"), "💬", "#07c160", Strings.T("setup.platformKw.videoaccount")),
        ("xhs", Strings.T("setup.platform.xhs"), "📕", "#ff2442", Strings.T("setup.platformKw.xhs")),
        ("vertical", Strings.T("setup.platform.vertical"), "📱", "#1abc9c", Strings.T("setup.platformKw.vertical")),
        ("mac", "macOS", "🍎", "#555555", "macOS"),
    };

    private List<Problem> _setupProblems = new();
    private string _activePlatform = "all";

    /// <summary>页面实例被导航缓存复用，静态区块只搭一次。</summary>
    private bool _chromeBuilt;

    /// <summary>静态区块是在哪种语言下搭起来的：换语言后要整块重建（V2.9.2）。</summary>
    private string _chromeLang = "";

    public SetupPage()
    {
        InitializeComponent();
    }

    public async Task OnNavigatedToAsync(object? parameter)
    {
        var language = Strings.Current;
        if (!_chromeBuilt || !string.Equals(_chromeLang, language, StringComparison.Ordinal))
        {
            _chromeBuilt = true;
            _chromeLang = language;
            BuildFlow();
            BuildWizards();
            BuildPlatformChips();
        }

        _setupProblems = await AppServices.Problems.GetByCategoryAsync("setup");

        // 每次进入都重建卡片：ProblemCard 的收藏星标是 Bind 时取的快照，
        // 用户在详情页改过收藏后回到这里需要跟着变。
        RenderProblems();

        // 一键部署录制环境（V2.9.3）：每次进入重读一次 OBS 配置，语言切换后也自然跟着刷新。
        await RecordingEnv.RefreshAsync();
    }

    // ---------------------------------------------------------- 搭建流程

    private void BuildFlow()
    {
        FlowPanel.Children.Clear();
        foreach (var step in Flow) FlowPanel.Children.Add(BuildFlowCard(step));
    }

    private Button BuildFlowCard((string No, string Title, string Desc, string Href) step)
    {
        var noText = new TextBlock
        {
            Text = step.No,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        noText.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBase");
        // 【v2.7.1】品牌底反衬文字走资源，深色模式亮底上不再用白字
        noText.SetResourceReference(TextBlock.ForegroundProperty, "BrandForegroundBrush");

        var badge = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(14),
            VerticalAlignment = VerticalAlignment.Center,
            Child = noText
        };
        badge.SetResourceReference(Border.BackgroundProperty, "BrandBrush");

        var titleText = new TextBlock
        {
            Text = step.Title,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
        titleText.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBase");
        titleText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

        var descText = new TextBlock
        {
            Text = step.Desc,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        descText.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSm");
        descText.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");

        var body = new StackPanel { Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        body.Children.Add(titleText);
        body.Children.Add(descText);

        var chevron = new TextBlock { Text = "›", VerticalAlignment = VerticalAlignment.Center };
        chevron.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeXl");
        chevron.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(badge, 0);
        Grid.SetColumn(body, 1);
        Grid.SetColumn(chevron, 2);
        grid.Children.Add(badge);
        grid.Children.Add(body);
        grid.Children.Add(chevron);

        var button = new Button
        {
            Style = (Style)FindResource("CardButton"),
            Content = grid,
            Tag = step.Href,
            Margin = new Thickness(0, 0, 0, 10)
        };
        button.Click += OnFlowClick;
        return button;
    }

    private void OnFlowClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string href) return;

        var slash = href.IndexOf('/');
        if (slash < 0) AppServices.Navigation.Navigate(href);
        else AppServices.Navigation.Navigate(href[..slash], href[(slash + 1)..]);
    }

    // ---------------------------------------------------------- 进阶向导（P1-3）

    /// <summary>两条进阶向导入口：竖屏双画布 / 多平台同时推流，点开分步向导窗口。</summary>
    private void BuildWizards()
    {
        WizardPanel.Children.Clear();

        AddWizardCard(SetupWizards.Vertical,
            Strings.T("setup.wizard.vertical.meta"));
        AddWizardCard(SetupWizards.MultiStream,
            Strings.T("setup.wizard.multistream.meta"));
    }

    private void AddWizardCard(WizardDefinition def, string meta)
    {
        var icon = new TextBlock
        {
            Text = def.Icon,
            FontSize = 26,
            VerticalAlignment = VerticalAlignment.Center
        };

        var title = new TextBlock
        {
            Text = Strings.T("setup.wizard.suffix", def.Title),
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
        title.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBase");
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

        var metaText = new TextBlock
        {
            Text = meta,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        metaText.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeXs");
        metaText.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");

        var body = new StackPanel { Margin = new Thickness(12, 10, 12, 11), VerticalAlignment = VerticalAlignment.Center };
        body.Children.Add(title);
        body.Children.Add(metaText);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(icon, 0);
        Grid.SetColumn(body, 1);
        var chevron = new TextBlock { Text = "›", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        chevron.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeXl");
        chevron.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        Grid.SetColumn(chevron, 2);
        grid.Children.Add(icon);
        grid.Children.Add(body);
        grid.Children.Add(chevron);

        var button = new Button
        {
            Style = (Style)FindResource("CardButton"),
            Content = grid,
            Tag = def.Id,
            MinWidth = 300,
            Margin = new Thickness(0, 0, 10, 10)
        };
        button.Click += (_, _) =>
        {
            var win = new SetupWizardWindow(def) { Owner = Window.GetWindow(this) };
            win.ShowDialog();
        };

        WizardPanel.Children.Add(button);
    }


    // ---------------------------------------------------------- 平台筛选

    private void BuildPlatformChips()
    {
        PlatformPanel.Children.Clear();

        foreach (var platform in Platforms)
        {
            var chip = new RadioButton
            {
                Style = (Style)FindResource("SegmentButton"),
                GroupName = "SetupPlatform",
                Content = platform.Icon + " " + platform.Label,
                Tag = platform.Key,
                IsChecked = platform.Key == _activePlatform
            };
            chip.Checked += OnPlatformChecked;
            PlatformPanel.Children.Add(chip);
        }
    }

    private void OnPlatformChecked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton chip || chip.Tag is not string key) return;
        if (key == _activePlatform) return;

        _activePlatform = key;
        RenderProblems();
    }

    /// <summary>原版的筛选逻辑：标题或 id 含平台关键词即视为该平台的指南。</summary>
    private List<Problem> VisibleSetups()
    {
        if (string.IsNullOrEmpty(_activePlatform) || _activePlatform == "all") return _setupProblems;

        var keyword = PlatformKeyword(_activePlatform);
        return _setupProblems
            .Where(p => p.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                     || p.Id.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static string PlatformKeyword(string key)
        => Platforms.FirstOrDefault(p => p.Key == key).Kw ?? "";

    // ---------------------------------------------------------- 问题列表

    private void RenderProblems()
    {
        var visible = VisibleSetups();

        ProblemPanel.Children.Clear();
        foreach (var problem in visible)
        {
            var card = new ProblemCard();
            card.Bind(problem);
            ProblemPanel.Children.Add(card);
        }

        EmptyText.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
