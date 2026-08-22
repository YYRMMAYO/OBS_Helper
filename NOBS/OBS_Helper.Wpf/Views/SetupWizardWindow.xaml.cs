using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OBS_Helper.Wpf.Navigation;
using OBS_Helper.Wpf.Services.Plugins;

namespace OBS_Helper.Wpf.Views;

/// <summary>向导的一步。可携带知识库条目 / 插件卡片 / 外链跳转按钮。</summary>
public sealed record WizardStep(
    string Title,
    string Detail,
    string? ProblemId = null,
    string? PluginId = null,
    string? Url = null);

/// <summary>一个完整的向导定义。</summary>
public sealed record WizardDefinition(string Id, string Icon, string Title, string Intro, WizardStep[] Steps);

/// <summary>
/// 搭建向导数据（路线图 P1-3）：基于 Aitum Vertical / Aitum Multistream 的实测流程整理，
/// 步骤按钮直达对应知识库条目与插件广场卡片。
/// 数据量小且与 UI 强相关，保留在代码内（与 SetupPage 的流程数组同策略）。
/// </summary>
public static class SetupWizards
{
    public static readonly WizardDefinition Vertical = new(
        "vertical", "📱", "竖屏 9:16 双画布",
        "用 Aitum Vertical 插件在同一个 OBS 里加一块竖屏画布：横屏推流、竖屏录制/推流互不打架。全程约 10 分钟。",
        [
            new("安装 Aitum Vertical",
                "先完全退出 OBS，再运行插件安装器（或把 zip 解压到 OBS 安装目录），重新打开 OBS 生效。",
                PluginId: "aitum-vertical"),
            new("认识竖屏画布",
                "重启后主界面底部会多出一条「Vertical」竖屏画布，默认 1080×1920；它与横屏画布相互独立、同时输出，可在插件设置里改分辨率。"),
            new("搭建竖屏场景",
                "像普通场景一样往竖屏画布添加来源。构图建议：人脸居中偏上、字幕与互动区放下三分之一；摄像头想横竖复用时可用 Source Clone 克隆后再裁剪，避免设备被占用。",
                PluginId: "source-clone"),
            new("横竖联动切换",
                "把竖屏场景命名成与横屏场景相同的名字，并在插件设置里开启场景同步（Scene Sync），切换横屏场景时同名竖屏场景会跟着切；不需要联动就保持不同名手动切换。"),
            new("输出与开播",
                "竖屏画布支持单独「开始录制」；要向抖音 / 视频号 / Shorts 推竖屏流时，配合「多平台同时推流」向导把竖屏画布作为第二路输出。",
                ProblemId: "st-multi"),
            new("开播前自检",
                "双画布会增加 GPU 与编码开销。先各录一段确认不掉帧，再到「监控」页观察 CPU / 内存余量，最后看统计面板三类丢帧是否正常。",
                ProblemId: "lag-stats"),
        ]);

    public static readonly WizardDefinition MultiStream = new(
        "multistream", "📡", "多平台同时推流",
        "一个 OBS 同时推 B 站 / 抖音 / Twitch 等多个平台。核心约束只有一条：所有分路码率之和不超过实测上行带宽的 70%。",
        [
            new("选择多路方案",
                "新装用户优先选 Aitum Multistream（维护活跃、每路独立码率与设置）；仍在使用老牌 obs-multi-rtmp 的用户建议先读一下已知问题条目再决定是否迁移。",
                ProblemId: "st-multi-rtmp",
                PluginId: "aitum-multistream"),
            new("安装并重启 OBS",
                "完全退出 OBS 后运行安装器或解压 zip 到安装目录，重新打开 OBS；插件面板里会出现多路输出的管理界面。"),
            new("配置每一路输出",
                "每一路分别选择服务、服务器与串流密钥，并单独设置码率。总码率 = 各路之和，务必控制在实测上行的 70% 以内；分辨率可以逐路下调（例如主路 1080p、副路 720p）。",
                ProblemId: "st-general"),
            new("逐路验证再开播",
                "先在各平台开私密直播 / 未公开直播，逐路确认画面、声音与延迟都正常，再正式公开开播。",
                ProblemId: "sf-bandwidth-test"),
            new("开播中的监控与取舍",
                "直播中盯着统计面板：某一路丢帧超标就先降那一路的码率或分辨率；带宽实在不够就减少并发平台数。开启动态码率可以让主输出更抗网络抖动。",
                ProblemId: "lag-dynamic-bitrate"),
        ]);
}

/// <summary>
/// 分步向导窗口（P1-3）：只读引导 + 跳转入口，不修改任何 OBS 设置。
/// 点击「问题方案 / 插件卡片」会先在主窗口完成导航，再关闭本窗口。
/// </summary>
public partial class SetupWizardWindow : Window
{
    private readonly WizardDefinition _def;

    public SetupWizardWindow(WizardDefinition def)
    {
        InitializeComponent();
        _def = def;

        IconText.Text = def.Icon;
        TitleText.Text = def.Title;
        Title = $"搭建向导 · {def.Title}";
        IntroText.Text = def.Intro;

        BuildSteps();
    }

    private void BuildSteps()
    {
        StepHost.Children.Clear();

        for (var i = 0; i < _def.Steps.Length; i++)
        {
            var step = _def.Steps[i];
            StepHost.Children.Add(BuildStepCard(i + 1, step, isLast: i == _def.Steps.Length - 1));
        }
    }

    private FrameworkElement BuildStepCard(int no, WizardStep step, bool isLast)
    {
        var badgeText = new TextBlock
        {
            Text = no.ToString(),
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        badgeText.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBase");

        var badge = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(14),
            VerticalAlignment = VerticalAlignment.Top,
            Child = badgeText
        };
        badge.SetResourceReference(Border.BackgroundProperty, "BrandBrush");

        var title = new TextBlock
        {
            Text = step.Title,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        title.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBase");
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

        var detail = new TextBlock
        {
            Text = step.Detail,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        };
        detail.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSm");
        detail.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");

        var textCol = new StackPanel();
        textCol.Children.Add(title);
        textCol.Children.Add(detail);

        var actions = BuildStepActions(step);
        if (actions is not null) textCol.Children.Add(actions);

        var grid = new Grid { Margin = new Thickness(0, 0, 0, isLast ? 4 : 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(badge, 0);
        Grid.SetColumn(textCol, 1);
        textCol.Margin = new Thickness(12, 0, 0, 0);
        grid.Children.Add(badge);
        grid.Children.Add(textCol);

        return grid;
    }

    /// <summary>按步骤声明的跳转目标生成按钮行。</summary>
    private StackPanel? BuildStepActions(WizardStep step)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 2) };

        if (!string.IsNullOrEmpty(step.ProblemId))
        {
            var b = new Button
            {
                Style = (Style)TryFindResource("LinkButton"),
                Content = "📄 查看知识库条目 →",
                Tag = step.ProblemId,
                ToolTip = "打开对应的分步排障方案"
            };
            b.Click += OnJumpProblemClick;
            panel.Children.Add(b);
        }

        if (!string.IsNullOrEmpty(step.PluginId))
        {
            var entry = PluginCatalogCore.FindById(AppServices.PluginCatalog.GetData(), step.PluginId!);
            if (entry is not null)
            {
                var b = new Button
                {
                    Style = (Style)TryFindResource("LinkButton"),
                    Margin = new Thickness(16, 0, 0, 0),
                    Content = $"🧩 插件广场：{entry.Name} →",
                    Tag = entry.Id,
                    ToolTip = "跳转到插件广场该插件的卡片"
                };
                b.Click += OnJumpPluginClick;
                panel.Children.Add(b);
            }
        }

        if (!string.IsNullOrEmpty(step.Url))
        {
            var b = new Button
            {
                Style = (Style)TryFindResource("LinkButton"),
                Margin = new Thickness(16, 0, 0, 0),
                Content = "↗ 打开链接",
                Tag = step.Url
            };
            b.Click += OnOpenUrlClick;
            panel.Children.Add(b);
        }

        return panel.Children.Count > 0 ? panel : null;
    }

    // -------------------------------------------------------------- 跳转

    private void OnJumpProblemClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        AppServices.Navigation?.Navigate(Routes.Problem, id);
        Close();
    }

    private void OnJumpPluginClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        AppServices.Navigation?.Navigate(Routes.Plugins, id);
        Close();
    }

    private async void OnOpenUrlClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string url }) return;
        try
        {
            await AppServices.Host.OpenExternalAsync(url);
        }
        catch (Exception)
        {
            AppServices.Toast.Show("无法打开链接，请检查系统默认浏览器设置", "warn");
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
