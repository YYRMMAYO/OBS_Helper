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
    private static readonly (string No, string Title, string Desc, string Href)[] Flow =
    {
        ("1", "准备与权限", "更新 OBS、以管理员运行（Win）或授予屏幕录制/麦克风权限（macOS）。", "diagnostic"),
        ("2", "场景与来源", "新建场景，添加捕获源、摄像头、音频，排好层级。", "problem/st-scene"),
        ("3", "音频校对", "麦克风/桌面声音电平正常，加降噪，统一 48kHz。", "problem/au-mic"),
        ("4", "推流设置", "选平台、粘贴密钥，硬件编码 + CBR + 关键帧 2s。", "problem/st-general"),
        ("5", "开播自检", "看统计面板掉帧，先录一段自测再开播。", "diagnostic"),
        ("6", "多平台 / 竖屏", "用插件做多路推流或竖屏 9:16 画布。", "problem/st-multi"),
    };

    /// <summary>
    /// 平台筛选项。Kw 是匹配问题标题 / id 的关键词，"all" 表示不过滤。
    /// Color 沿用原版数据：原版 CSS 里 .chip 并没有用到这个色值，此处同样只作数据保留。
    /// </summary>
    private static readonly (string Key, string Label, string Icon, string Color, string Kw)[] Platforms =
    {
        ("all", "全部", "📋", "#8e44ad", ""),
        ("bilibili", "B站", "📺", "#fb7299", "B站"),
        ("douyin", "抖音", "🎵", "#fe2c55", "抖音"),
        ("kuaishou", "快手", "⚡", "#ff4906", "快手"),
        ("youtube", "YouTube", "▶️", "#ff0000", "YouTube"),
        ("twitch", "Twitch", "🟣", "#9146ff", "Twitch"),
        ("videoaccount", "视频号", "💬", "#07c160", "视频号"),
        ("xhs", "小红书", "📕", "#ff2442", "小红书"),
        ("vertical", "竖屏", "📱", "#1abc9c", "竖屏"),
        ("mac", "macOS", "🍎", "#555555", "macOS"),
    };

    private List<Problem> _setupProblems = new();
    private string _activePlatform = "all";

    /// <summary>页面实例被导航缓存复用，静态区块只搭一次。</summary>
    private bool _chromeBuilt;

    public SetupPage()
    {
        InitializeComponent();
    }

    public async Task OnNavigatedToAsync(object? parameter)
    {
        if (!_chromeBuilt)
        {
            _chromeBuilt = true;
            BuildFlow();
            BuildWizards();
            BuildPlatformChips();
        }

        _setupProblems = await AppServices.Problems.GetByCategoryAsync("setup");

        // 每次进入都重建卡片：ProblemCard 的收藏星标是 Bind 时取的快照，
        // 用户在详情页改过收藏后回到这里需要跟着变。
        RenderProblems();
    }

    // ---------------------------------------------------------- 搭建流程

    private void BuildFlow()
    {
        foreach (var step in Flow) FlowPanel.Children.Add(BuildFlowCard(step));
    }

    private Button BuildFlowCard((string No, string Title, string Desc, string Href) step)
    {
        var noText = new TextBlock
        {
            Text = step.No,
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        noText.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBase");

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
        AddWizardCard(SetupWizards.Vertical,
            "Aitum Vertical 双画布 · 横竖同播 · 约 10 分钟");
        AddWizardCard(SetupWizards.MultiStream,
            "多平台并发推流 · 独立码率 · 带宽预算 70%");
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
            Text = def.Title + " 向导",
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
