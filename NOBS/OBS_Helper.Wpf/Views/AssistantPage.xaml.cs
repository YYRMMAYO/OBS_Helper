using System.Windows;
using System.Windows.Controls;
using OBS_Helper.Wpf.Navigation;
using OBS_Helper.Wpf.Services;

namespace OBS_Helper.Wpf.Views;

/// <summary>
/// 问答助手：把自然语言描述交给离线匹配器，按得分列出候选问题。
///
/// 与 Blazor 版保持一致——输入即匹配，不必点「提问」；点结果卡直接进问题详情。
/// </summary>
public partial class AssistantPage : UserControl
{
    /// <summary>提问序号。边打边搜时先发的请求可能后返回，用序号丢弃过期结果，
    /// 避免列表被上一个关键词的结果覆盖。</summary>
    private int _askSeq;

    /// <summary>是否已经问过。没问过时不显示「没找到匹配」，否则一进页面就是空状态。</summary>
    private bool _asked;

    /// <summary>输入防抖：停止输入 300ms 后才匹配，避免逐击穿发（P1-2）。</summary>
    private readonly Debouncer _debouncer = new(TimeSpan.FromMilliseconds(300));

    public AssistantPage()
    {
        InitializeComponent();
        BuildChips();
    }

    private void BuildChips()
    {
        foreach (var s in AppServices.Assistant.Suggestions)
        {
            var chip = new Button
            {
                Content = s,
                Tag = s,
                Style = TryFindResource("SecondaryButton") as Style,
                Padding = new Thickness(12, 6, 12, 6),
                MinHeight = 0,
                Margin = new Thickness(0, 0, 8, 8)
            };
            // 字号走资源引用，切换字号档位时药丸跟着变
            chip.SetResourceReference(Control.FontSizeProperty, "FontSizeSm");
            chip.Click += OnChipClick;
            ChipPanel.Children.Add(chip);
        }
    }

    // ------------------------------------------------------------ 交互

    private void OnChipClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string s })
        {
            // 只改文本，匹配由 TextChanged 顺带触发，保持单一入口
            QueryBox.Text = s;
            QueryBox.CaretIndex = s.Length;
            QueryBox.Focus();
        }
    }

    private void OnQueryChanged(object sender, TextChangedEventArgs e)
    {
        // 防抖：连续输入只在停顿后触发一次匹配（P1-2）；AskAsync 内部有 _askSeq 二次防竞态
        _debouncer.DebounceAsync(AskAsync);
    }

    private void OnAskClick(object sender, RoutedEventArgs e) => _debouncer.DebounceAsync(AskAsync);

    private async Task AskAsync()
    {
        _asked = true;
        var seq = ++_askSeq;

        var matches = await AppServices.Assistant.AskAsync(QueryBox.Text);
        if (seq != _askSeq) return; // 已有更新的输入，这次结果作废

        Render(matches);
    }

    private void Render(List<AssistantMatch> matches)
    {
        MatchList.Children.Clear();
        foreach (var m in matches) MatchList.Children.Add(BuildMatchCard(m));

        EmptyText.Visibility = _asked && matches.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ------------------------------------------------------------ 结果卡

    private Button BuildMatchCard(AssistantMatch m)
    {
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = MakeText(m.Problem.Title, "FontSizeBase", "TextBrush");
        title.FontWeight = FontWeights.SemiBold;
        title.Margin = new Thickness(0, 0, 10, 0);
        title.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(title, 0);
        head.Children.Add(title);

        var scorePill = new Border
        {
            Style = TryFindResource("Pill") as Style,
            Child = MakeText($"匹配 {m.Score}", "FontSizeXs", "BrandBrush", wrap: false)
        };
        scorePill.SetResourceReference(Border.BackgroundProperty, "BrandSoftBrush");
        Grid.SetColumn(scorePill, 1);
        head.Children.Add(scorePill);

        var body = new StackPanel();
        body.Children.Add(head);

        if (!string.IsNullOrEmpty(m.Reason))
        {
            var reason = MakeText($"命中：{m.Reason}", "FontSizeSm", "MutedBrush");
            reason.Margin = new Thickness(0, 6, 0, 0);
            body.Children.Add(reason);
        }

        var platforms = MakeText(string.Join(" · ", m.Problem.Platforms), "FontSizeXs", "MutedBrush");
        platforms.Margin = new Thickness(0, 8, 0, 0);
        body.Children.Add(platforms);

        var card = new Button
        {
            Content = body,
            Tag = m.Problem.Id,
            Style = TryFindResource("CardButton") as Style,
            Margin = new Thickness(0, 0, 0, 10)
        };
        card.Click += OnMatchClick;
        return card;
    }

    private void OnMatchClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id } && !string.IsNullOrEmpty(id))
            AppServices.Navigation?.Navigate(Routes.Problem, id);
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
