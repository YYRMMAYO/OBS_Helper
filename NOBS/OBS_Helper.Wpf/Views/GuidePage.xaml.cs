using System.Windows;
using System.Windows.Controls;
using OBS_Helper.Wpf.Controls;
using OBS_Helper.Wpf.Errors;
using OBS_Helper.Wpf.Navigation;

namespace OBS_Helper.Wpf.Views;

/// <summary>
/// 排障指引。正文是随包内嵌的 troubleshooting.md，由 <see cref="MarkdownView"/> 渲染成控件树。
///
/// 与 Blazor 版的差异：桌面窗口比手机宽得多，右侧固定一列二级章节目录，
/// 省去在十个章节之间反复滚动；正文本身的内容与语法支持范围完全一致。
/// </summary>
public partial class GuidePage : UserControl, INavigationAware
{
    /// <summary>
    /// 已经为**哪种语言**渲染过正文（V3.0 / F5）。
    ///
    /// 原先这里是一个 <c>bool _loaded</c>：指引是随包资源没错，但它是**分语言的**
    /// （<c>troubleshooting.md</c> / <c>troubleshooting.en-US.md</c>）——
    /// 切了语言却因为「加载过一次」而直接早退，页面就会一直显示旧语言。
    /// 现在记语言：同一语言才跳过重复渲染。
    /// </summary>
    private string? _renderedLanguage;

    public GuidePage()
    {
        InitializeComponent();

        // 文档首行的 H1 与顶栏标题是同一句话，渲染出来会重复
        Markdown.SkipTopLevelHeading = true;
    }

    public async Task OnNavigatedToAsync(object? parameter)
    {
        // 同一语言不必重渲染（正文是随包资源）；语言变了必须重来
        if (_renderedLanguage == Strings.Current) return;

        try
        {
            var md = await AppServices.Problems.GetGuideMarkdownAsync();
            if (string.IsNullOrWhiteSpace(md))
            {
                ShowError(Strings.T("guide.empty"));
                return;
            }

            Markdown.Render(md);
            BuildToc();

            LoadingText.Visibility = Visibility.Collapsed;
            ContentCard.Visibility = Visibility.Visible;
            ErrorPanel.Visibility = Visibility.Collapsed;
            _renderedLanguage = Strings.Current;
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            App.ReportError(ErrorCodes.DataLoadFailed, ex);
        }
    }

    /// <summary>用二级标题生成目录。一级标题是文档名、三级标题太碎，都不进目录。</summary>
    private void BuildToc()
    {
        TocList.Children.Clear();

        foreach (var section in Markdown.Sections)
        {
            var button = new Button
            {
                Style = TryFindResource("LinkButton") as Style,
                Content = new TextBlock { Text = section.Title, TextWrapping = TextWrapping.Wrap },
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 8),
                Tag = section.Anchor,
                ToolTip = section.Title
            };
            button.Click += OnTocClick;
            TocList.Children.Add(button);
        }

        TocPanel.Visibility = TocList.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnTocClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: FrameworkElement anchor })
        {
            anchor.BringIntoView();
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        LoadingText.Visibility = Visibility.Collapsed;
        ContentCard.Visibility = Visibility.Collapsed;
        TocPanel.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Visible;
    }
}
