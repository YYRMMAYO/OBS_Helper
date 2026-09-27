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
    public static WizardDefinition Vertical => new(
        "vertical", "", Strings.T("wizard.vertical.title"),
        Strings.T("wizard.vertical.intro"),
        [
            new(Strings.T("wizard.vertical.step1.title"),
                Strings.T("wizard.vertical.step1.desc"),
                PluginId: "aitum-vertical"),
            new(Strings.T("wizard.vertical.step2.title"),
                Strings.T("wizard.vertical.step2.desc")),
            new(Strings.T("wizard.vertical.step3.title"),
                Strings.T("wizard.vertical.step3.desc"),
                PluginId: "source-clone"),
            new(Strings.T("wizard.vertical.step4.title"),
                Strings.T("wizard.vertical.step4.desc")),
            new(Strings.T("wizard.vertical.step5.title"),
                Strings.T("wizard.vertical.step5.desc"),
                ProblemId: "st-multi"),
            new(Strings.T("wizard.vertical.step6.title"),
                Strings.T("wizard.vertical.step6.desc"),
                ProblemId: "lag-stats"),
        ]);

    public static WizardDefinition MultiStream => new(
        "multistream", "", Strings.T("wizard.multistream.title"),
        Strings.T("wizard.multistream.intro"),
        [
            new(Strings.T("wizard.multistream.step1.title"),
                Strings.T("wizard.multistream.step1.desc"),
                ProblemId: "st-multi-rtmp",
                PluginId: "aitum-multistream"),
            new(Strings.T("wizard.multistream.step2.title"),
                Strings.T("wizard.multistream.step2.desc")),
            new(Strings.T("wizard.multistream.step3.title"),
                Strings.T("wizard.multistream.step3.desc"),
                ProblemId: "st-general"),
            new(Strings.T("wizard.multistream.step4.title"),
                Strings.T("wizard.multistream.step4.desc"),
                ProblemId: "sf-bandwidth-test"),
            new(Strings.T("wizard.multistream.step5.title"),
                Strings.T("wizard.multistream.step5.desc"),
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

        TitleText.Text = def.Title;
        Title = Strings.T("wizard.windowTitle", def.Title);
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
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        badgeText.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBase");
        // 【v2.7.1】品牌底反衬文字走资源，深色模式亮底上不再用白字
        badgeText.SetResourceReference(TextBlock.ForegroundProperty, "BrandForegroundBrush");

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
                Content = Strings.T("wizard.viewProblem"),
                Tag = step.ProblemId,
                ToolTip = Strings.T("wizard.viewProblemTip")
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
                    Content = Strings.T("wizard.viewPlugin", entry.Name),
                    Tag = entry.Id,
                    ToolTip = Strings.T("wizard.viewPluginTip")
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
                Content = Strings.T("wizard.openLink"),
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
            AppServices.Toast.Show(Strings.T("common.openLinkFailed"), "warn");
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
