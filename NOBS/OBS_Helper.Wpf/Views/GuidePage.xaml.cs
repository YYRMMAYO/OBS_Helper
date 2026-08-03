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
    /// <summary>指引是随包资源、永远不会变，加载一次即可；页面实例被导航复用，用它挡住重复渲染。</summary>
    private bool _loaded;

    public GuidePage()
    {
        InitializeComponent();

        // 文档首行的 H1 与顶栏标题是同一句话，渲染出来会重复
        Markdown.SkipTopLevelHeading = true;
    }

    public async Task OnNavigatedToAsync(object? parameter)
    {
        if (_loaded) return;

        try
        {
            var md = await AppServices.Problems.GetGuideMarkdownAsync();
            if (string.IsNullOrWhiteSpace(md))
            {
                ShowError("内置指引资源为空或缺失。");
                return;
            }

            Markdown.Render(md);
            BuildToc();

            LoadingText.Visibility = Visibility.Collapsed;
            ContentCard.Visibility = Visibility.Visible;
            _loaded = true;
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
