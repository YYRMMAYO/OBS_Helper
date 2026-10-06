using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using OBS_Helper.Wpf.Controls;
using OBS_Helper.Wpf.Models;
using OBS_Helper.Wpf.Navigation;
using OBS_Helper.Wpf.Services;

namespace OBS_Helper.Wpf.Views;

/// <summary>
/// 问题详情页：症状 / 原因 / 分步方案（勾选进度持久化）/ 小贴士 / 参考链接 / 相关问题。
/// </summary>
public partial class ProblemPage : UserControl, INavigationAware
{
    private Problem? _problem;
    private string _categoryTitle = "";
    private bool _bookmarked;

    /// <summary>已完成的步骤序号（0 基），与 BookmarkService 里存的一致。</summary>
    private readonly HashSet<int> _doneSteps = new();

    /// <summary>步骤行与其标题，勾选后要改整行透明度与标题删除线，先存起来省得再去遍历可视树。</summary>
    private readonly List<(Border Row, TextBlock Title)> _stepRows = new();

    public ProblemPage()
    {
        InitializeComponent();
    }

    /// <param name="parameter">问题 id（string）。</param>
    public async Task OnNavigatedToAsync(object? parameter)
    {
        ResetUi();
        FeedbackStatusText.Visibility = Visibility.Collapsed;

        var id = parameter as string ?? "";

        try
        {
            var problem = await AppServices.Problems.GetByIdAsync(id);
            if (problem is null)
            {
                SetHeader(Strings.T("page.problem.title"), null);
                NotFoundText.Visibility = Visibility.Visible;
                return;
            }

            _problem = problem;

            var category = await AppServices.Problems.GetCategoryAsync(problem.Category);
            _categoryTitle = category?.Title ?? "";
            SetHeader(problem.Title, _categoryTitle);

            ContentRoot.Visibility = Visibility.Visible;

            BuildBadges(problem);
            BuildBullets(SymptomList, problem.Symptoms);
            BuildBullets(CauseList, problem.Causes);
            BuildSteps(problem);

            if (problem.Tips is { Length: > 0 })
            {
                BuildBullets(TipList, problem.Tips);
                TipsBlock.Visibility = Visibility.Visible;
            }

            BuildLinks(problem);
            await BuildRelatedAsync(problem);

            _bookmarked = AppServices.Bookmarks.IsBookmarked(problem.Id);
            RefreshBookmarkButton();
        }
        catch (Exception ex)
        {
            App.ReportError(Errors.ErrorCodes.DataLoadFailed, ex);
        }
    }

    /// <summary>页面实例复用，进入新问题前把上一条的痕迹全部抹掉。</summary>
    private void ResetUi()
    {
        _problem = null;
        _categoryTitle = "";
        _bookmarked = false;
        _doneSteps.Clear();
        _stepRows.Clear();

        BadgePanel.Children.Clear();
        SymptomList.Children.Clear();
        CauseList.Children.Clear();
        StepList.Children.Clear();
        TipList.Children.Clear();
        LinkList.Children.Clear();
        RelatedList.Children.Clear();

        ContentRoot.Visibility = Visibility.Collapsed;
        NotFoundText.Visibility = Visibility.Collapsed;
        TipsBlock.Visibility = Visibility.Collapsed;
        LinksBlock.Visibility = Visibility.Collapsed;
        RelatedBlock.Visibility = Visibility.Collapsed;
        HintText.Visibility = Visibility.Collapsed;

        StepProgress.Visibility = Visibility.Visible;
        StepProgress.Value = 0;
        ProgressText.Text = "";
    }

    // ------------------------------------------------------------ 各区块构建

    private void BuildBadges(Problem problem)
    {
        // 本地条目（V3.0 / D8）：让用户一眼分清「官方说的」和「我自己记的」
        if (problem.IsLocal)
        {
            var minePill = new Border
            {
                Style = (Style)FindResource("Pill"),
                Margin = new Thickness(0, 0, 6, 6)
            };
            var mineText = new TextBlock { Text = Strings.T("kb.mine.badge") };
            mineText.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeXs");
            mineText.SetResourceReference(TextBlock.ForegroundProperty, "BrandBrush");
            minePill.Child = mineText;
            BadgePanel.Children.Add(minePill);
        }

        foreach (var platform in problem.Platforms ?? Array.Empty<string>())
        {
            var pill = new Border
            {
                Style = (Style)FindResource("Pill"),
                Margin = new Thickness(0, 0, 6, 6)
            };
            var text = new TextBlock { Text = platform };
            text.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeXs");
            text.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            pill.Child = text;
            BadgePanel.Children.Add(pill);
        }

        var severityPill = new Border
        {
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(0, 0, 6, 6),
            VerticalAlignment = VerticalAlignment.Center,
            Background = ConvertBrush("SeveritySoftBrush", problem.Severity)
        };
        var severityText = new TextBlock
        {
            Text = problem.Severity,
            FontWeight = FontWeights.SemiBold,
            Foreground = ConvertBrush("SeverityBrush", problem.Severity)
        };
        severityText.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeXs");
        severityPill.Child = severityText;
        BadgePanel.Children.Add(severityPill);
    }

    /// <summary>项目符号列表。用 Grid 而不是「•」前缀，是为了让换行的文字对齐到正文而不是圆点下。</summary>
    private void BuildBullets(Panel host, IEnumerable<string> items)
    {
        foreach (var item in items)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var dot = new TextBlock
            {
                Text = "•",
                Style = (Style)FindResource("BodyText"),
                Margin = new Thickness(0, 0, 8, 0)
            };
            dot.SetResourceReference(TextBlock.ForegroundProperty, "BrandBrush");

            var body = new TextBlock { Text = item, Style = (Style)FindResource("BodyText") };
            Grid.SetColumn(body, 1);

            row.Children.Add(dot);
            row.Children.Add(body);
            host.Children.Add(row);
        }
    }

    private void BuildSteps(Problem problem)
    {
        foreach (var index in AppServices.Bookmarks.GetCompletedSteps(problem.Id))
        {
            _doneSteps.Add(index);
        }

        // 局部变量接一手：可空集合上的 `?.Count` 与随后的下标访问分两次求值，
        // 编译器（正确地）无法证明第二次不是 null —— 零警告基线不允许这种写法。
        var steps = problem.Steps ?? new List<Step>();
        if (steps.Count == 0)
        {
            StepProgress.Visibility = Visibility.Collapsed;
            var empty = new TextBlock { Text = Strings.T("problem.noSteps"), Style = (Style)FindResource("MutedText") };
            StepList.Children.Add(empty);
            return;
        }

        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];

            var content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var badge = new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(12),
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Top
            };
            badge.SetResourceReference(Border.BackgroundProperty, "BrandBrush");
            badge.Child = new TextBlock
            {
                Text = (i + 1).ToString(CultureInfo.InvariantCulture),
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            ((TextBlock)badge.Child).SetResourceReference(TextBlock.ForegroundProperty, "BrandForegroundBrush");

            var titleRow = new Grid();
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var title = new TextBlock
            {
                Text = step.Title,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            };
            title.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBase");
            title.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

            var levelPill = BuildLevelPill(step.Level);
            Grid.SetColumn(levelPill, 1);
            titleRow.Children.Add(title);
            titleRow.Children.Add(levelPill);

            var body = new StackPanel();
            body.Children.Add(titleRow);
            if (!string.IsNullOrWhiteSpace(step.Detail))
            {
                body.Children.Add(new TextBlock
                {
                    Text = step.Detail,
                    Style = (Style)FindResource("MutedText"),
                    Margin = new Thickness(0, 4, 0, 0)
                });
            }
            Grid.SetColumn(body, 1);

            content.Children.Add(badge);
            content.Children.Add(body);

            // 先设初值再挂事件，避免恢复历史进度时把「已完成」又写回一遍存储
            var check = new CheckBox
            {
                Style = (Style)FindResource("AppCheckBox"),
                Tag = i,
                IsChecked = _doneSteps.Contains(i),
                Content = content
            };
            check.Checked += OnStepToggled;
            check.Unchecked += OnStepToggled;

            var row = new Border
            {
                Style = (Style)FindResource("SoftPanel"),
                Margin = new Thickness(0, 0, 0, 8),
                Child = check
            };

            StepList.Children.Add(row);
            _stepRows.Add((row, title));
            ApplyStepVisual(i);
        }

        UpdateProgress();
    }

    /// <summary>难度药丸。基础用信息蓝、进阶用警示橙，与原版 .lvl-basic / .lvl-adv 对应。</summary>
    private Border BuildLevelPill(string level)
    {
        var advanced = DataValues.IsAdvancedLevel(level);
        var pill = new Border
        {
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(7, 1, 7, 1),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        pill.SetResourceReference(Border.BackgroundProperty, advanced ? "WarnSoftBrush" : "InfoSoftBrush");

        var text = new TextBlock { Text = level, FontWeight = FontWeights.SemiBold };
        text.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeXs");
        text.SetResourceReference(TextBlock.ForegroundProperty, advanced ? "WarnBrush" : "InfoBrush");

        pill.Child = text;
        return pill;
    }

    private void BuildLinks(Problem problem)
    {
        foreach (var link in problem.Links)
        {
            if (IsSafeUrl(link.Url))
            {
                var button = new Button
                {
                    Style = (Style)FindResource("LinkButton"),
                    Content = link.Title + " ↗",
                    Tag = link.Url,
                    ToolTip = link.Url,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 0, 0, 6)
                };
                button.Click += OnLinkClick;
                LinkList.Children.Add(button);
            }
            else
            {
                // 非 http/https 的地址一律不给点，只留标题（与原版的屏蔽策略一致）
                LinkList.Children.Add(new TextBlock
                {
                    Text = link.Title + " ⚠",
                    Style = (Style)FindResource("MutedText"),
                    ToolTip = Strings.T("problem.linkBlocked"),
                    Margin = new Thickness(0, 0, 0, 6)
                });
            }
        }

        LinksBlock.Visibility = problem.Links.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task BuildRelatedAsync(Problem problem)
    {
        if (problem.Related.Length == 0) return;

        var all = await AppServices.Problems.GetProblemsAsync();
        var categories = await AppServices.Problems.GetCategoriesAsync();
        var titles = categories.ToDictionary(c => c.Id, c => c.Title);

        foreach (var relatedId in problem.Related)
        {
            var target = all.FirstOrDefault(p => p.Id == relatedId);
            if (target is null) continue;

            var card = new ProblemCard();
            card.Bind(target, titles.GetValueOrDefault(target.Category, ""));
            RelatedList.Children.Add(card);
        }

        RelatedBlock.Visibility = RelatedList.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ------------------------------------------------------------ 交互

    private void OnStepToggled(object sender, RoutedEventArgs e)
    {
        if (_problem is null) return;
        if (sender is not CheckBox check || check.Tag is not int index) return;

        if (check.IsChecked == true) _doneSteps.Add(index);
        else _doneSteps.Remove(index);

        AppServices.Bookmarks.SetCompletedSteps(_problem.Id, _doneSteps);
        ApplyStepVisual(index);
        UpdateProgress();
    }

    private void ApplyStepVisual(int index)
    {
        if (index < 0 || index >= _stepRows.Count) return;

        var (row, title) = _stepRows[index];
        var done = _doneSteps.Contains(index);
        row.Opacity = done ? 0.6 : 1.0;
        title.TextDecorations = done ? TextDecorations.Strikethrough : null;
    }

    private void UpdateProgress()
    {
        var total = _problem?.Steps.Count ?? 0;
        if (_problem is null || total == 0) return;

        StepProgress.Value = AppServices.Bookmarks.Progress(_problem.Id, total) * 100;
        ProgressText.Text = Strings.T("problem.progress", _doneSteps.Count, total);
    }

    private void OnToggleBookmark(object sender, RoutedEventArgs e)
    {
        if (_problem is null) return;

        _bookmarked = AppServices.Bookmarks.Toggle(_problem.Id);
        RefreshBookmarkButton();
    }

    private void RefreshBookmarkButton()
    {
        BookmarkButton.Content = _bookmarked ? Strings.T("problem.bookmark.on") : Strings.T("problem.bookmark.off");
        BookmarkButton.Style = (Style)FindResource(_bookmarked ? "PrimaryButton" : "SecondaryButton");
        BookmarkButton.ToolTip = _bookmarked ? Strings.T("problem.bookmark.remove") : Strings.T("problem.bookmark.addTip");
    }

    private void OnCopyAll(object sender, RoutedEventArgs e)
    {
        if (_problem is null) return;

        try
        {
            Clipboard.SetText(ProblemService.BuildText(_problem, _categoryTitle));
            ShowHint(Strings.T("problem.copiedAll"));
        }
        catch (Exception ex)
        {
            // 剪贴板被别的进程占用时会抛 COM 异常，这属于可重试的小故障，就地提示即可
            ShowHint(Strings.T("common.copyFailed") + ex.Message);
        }
    }

    // -------------------------------------------------------------- 知识库回执（V3.0 / D8）

    private void OnSolvedClick(object sender, RoutedEventArgs e) => SendFeedback(solved: true);

    private void OnUnsolvedClick(object sender, RoutedEventArgs e) => SendFeedback(solved: false);

    /// <summary>
    /// 生成回执并复制到剪贴板，然后打开 GitHub 新建 Issue 页面。
    ///
    /// **只带标题不带正文**：正文上千字，塞进 URL 有可能被浏览器/代理截断，
    /// 而截断后的 Issue 比没有更糟（用户以为贴上了）。正文由用户从剪贴板粘贴。
    /// </summary>
    private async void SendFeedback(bool solved)
    {
        if (_problem is null) return;

        // 占位符现在走 Tag（不再进 Text），所以 Text 就是用户真正写的东西
        var note = FeedbackNoteBox.Text ?? "";

        Services.Knowledge.ProblemFeedback Build() => new(
            Problem: _problem,
            Solved: solved,
            UserNote: note,
            KbVersion: AppServices.Problems.Version,
            AppVersion: FeedbackPage.AppVersion(),
            UsingExternalKb: AppServices.Problems.DataSource == "external",
            ObsVersion: AppServices.Obs.Profile.ObsVersion ?? "",
            Platform: AppServices.Obs.Profile.Platform ?? "");

        try
        {
            Clipboard.SetText(Services.Knowledge.ProblemFeedbackCore.BuildMarkdown(Build()));
            ShowFeedbackStatus(Strings.T("kb.feedback.copied"), ok: true);
            // 发完就清空：留在框里会让下一次回执把上一轮的说明再带一遍
            FeedbackNoteBox.Text = "";
        }
        catch (Exception ex)
        {
            ShowFeedbackStatus(Strings.T("kb.feedback.copyFailed", ex.Message), ok: false);
            return;
        }

        try
        {
            var url = Services.Knowledge.ProblemFeedbackCore.BuildIssueUrl(
                Services.Update.FeedbackLinks.RepositorySlug, Build());

            if (await AppServices.Host.OpenExternalAsync(url))
                ShowFeedbackStatus(Strings.T("kb.feedback.issueOpened"), ok: true);
        }
        catch (Exception ex)
        {
            ShowFeedbackStatus(Strings.T("kb.feedback.copyFailed", ex.Message), ok: false);
        }
    }

    private void ShowFeedbackStatus(string text, bool ok)
    {
        FeedbackStatusText.Text = text;
        FeedbackStatusText.SetResourceReference(TextBlock.ForegroundProperty, ok ? "MutedBrush" : "WarnBrush");
        FeedbackStatusText.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// 创建「我的知识库」示例文件并打开目录（本地新增条目的入口）。
    ///
    /// V3.0 第三轮验证修正：创建后必须**立刻重载知识库**并原地重导航 ——
    /// 原先文案让用户「按刷新」，而本页根本没有刷新按钮，用户按提示做完会以为功能坏了。
    /// </summary>
    private async void OnCreateMineFileClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var created = AppServices.MyKnowledge.WriteStarterFile();
            var dir = System.IO.Path.GetDirectoryName(AppServices.MyKnowledge.FilePath)!;
            System.IO.Directory.CreateDirectory(dir);
            Services.ObsConfig.RecordingToolsService.OpenInExplorer(dir);

            // 重新载入 + 原地重放本页：用户编辑完文件回到应用时，条目已经在列表/搜索里
            AppServices.Problems.Reload();
            await AppServices.Navigation.NavigateAsync(Navigation.Routes.Problem, _problem?.Id, pushHistory: false)
                .ConfigureAwait(true);

            ShowFeedbackStatus(Strings.T(created ? "kb.mine.created" : "kb.mine.exists"), ok: true);        }
        catch (Exception ex)
        {
            ShowFeedbackStatus(Strings.T("kb.mine.failed", ex.Message), ok: false);
        }
    }

    private async void OnLinkClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not string url) return;

        try
        {
            var opened = await AppServices.Host.OpenExternalAsync(url);
            if (!opened) ShowHint(Strings.T("problem.linkOpenFailed") + url);
        }
        catch (Exception ex)
        {
            App.ReportError(Errors.ErrorCodes.Unknown, ex);
        }
    }

    private void ShowHint(string text)
    {
        HintText.Text = text;
        HintText.Visibility = Visibility.Visible;
    }

    // ------------------------------------------------------------ 小工具

    /// <summary>只放行 http/https，挡掉 javascript: / file: 之类的协议。</summary>
    private static bool IsSafeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
    }

    private static void SetHeader(string title, string? subtitle)
        => (Application.Current.MainWindow as MainWindow)?.SetHeader(title, subtitle);

    /// <summary>复用 Controls.xaml 里注册的转换器，避免在页面里再写一份严重度配色表。</summary>
    private Brush ConvertBrush(string converterKey, object? value)
    {
        var brush = (TryFindResource(converterKey) as IValueConverter)?
            .Convert(value, typeof(Brush), null, CultureInfo.InvariantCulture) as Brush;
        return brush ?? TryFindResource("MutedBrush") as Brush ?? Brushes.Gray;
    }
}
