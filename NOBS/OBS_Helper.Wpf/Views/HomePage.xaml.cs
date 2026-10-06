using System.Windows;
using System.Windows.Controls;
using OBS_Helper.Wpf.Controls;
using OBS_Helper.Wpf.Models;
using OBS_Helper.Wpf.Navigation;

namespace OBS_Helper.Wpf.Views;

/// <summary>
/// 首页：两个快捷入口 + 数据驱动的分类卡 + 我的收藏。
///
/// 页面实例由导航服务缓存复用，所以数据装配放在 <see cref="OnNavigatedToAsync"/> 而不是构造函数，
/// 每次回到首页都会按最新的收藏状态重建列表。
///
/// V3.0（F7）：缓存现在有 LRU 上限，页面实例随时可能被逐出后重建，
/// 因此凡是订阅单例事件的地方都必须**成对**（进入订阅 / 离开退订），不能像以前那样赌「实例常驻」。
/// </summary>
public partial class HomePage : UserControl, INavigationAware
{
    /// <summary>
    /// 分类卡的绑定数据。问题数不在 <see cref="Category"/> 里，用一个展示用类型把两者拼起来。
    /// 声明成 internal 而非 private：WPF 绑定靠反射取值，非公开类型上的公开属性才取得到。
    /// </summary>
    internal sealed class CategoryTile
    {
        public string Id { get; init; } = "";
        public string Icon { get; init; } = "";
        public string Title { get; init; } = "";
        public string Description { get; init; } = "";
        public string Semantic { get; init; } = "";
        public string CountText { get; init; } = "";
    }

    /// <summary>
    /// 是否已经订阅了单例事件。订阅拆到导航回调后必须有这个标记：
    /// 同路由原地重放（切语言）会连着走一次 from / to，缺了它就会重复订阅。
    /// </summary>
    private bool _eventsAttached;

    public HomePage()
    {
        InitializeComponent();
    }

    public async Task OnNavigatedToAsync(object? parameter)
    {
        AttachEvents();
        try
        {
            var categories = await AppServices.Problems.GetCategoriesAsync();
            var counts = await AppServices.Problems.GetCategoryCountsAsync();

            CategoryList.ItemsSource = categories.Select(c => new CategoryTile
            {
                Id = c.Id,
                Icon = c.Icon,
                Title = c.Title,
                Description = c.Description,
                Semantic = c.Semantic,
                CountText = Strings.T("home.categoryCount", counts.GetValueOrDefault(c.Id, 0))
            }).ToList();

            var error = AppServices.Problems.LoadError;
            LoadErrorPanel.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
            if (error is not null) LoadErrorText.Text = Errors.ErrorCodes.Format(error);

            RefreshBookmarks();
            RefreshWelcome();

            // 简单录像卡（V2.9.4）：每次回到首页重读一次现状（配置可能在别处被改过）
            await SimpleRecord.RefreshAsync();

            // 简单开播卡（V3.0 / D2）：同理，回来就重查一次推流服务是否填全
            await SimpleStream.RefreshAsync();
        }
        catch (Exception ex)
        {
            App.ReportError(Errors.ErrorCodes.DataLoadFailed, ex);
        }
    }

    /// <summary>
    /// 离开首页：退订简单录像卡的服务事件（V2.9.4），并退掉首页自己订的两个单例事件（V3.0 / F7）。
    /// 后者原来是故意不退订的（注释说「页面实例常驻、生命周期与应用一致」）——LRU 逐出上线后
    /// 这个前提不再成立：不退订就会在实例重建后越订越多，同一个收藏变更把刷新跑到好几个死实例上。
    /// </summary>
    public Task OnNavigatedFromAsync()
    {
        SimpleRecord.Detach();
        SimpleStream.Detach();
        DetachEvents();
        return Task.CompletedTask;
    }

    /// <summary>收藏可能在详情页 / 搜索页被改动，连接状态会在控制台 / 托盘变化：进页面时订阅（幂等）。</summary>
    private void AttachEvents()
    {
        if (_eventsAttached) return;
        _eventsAttached = true;

        AppServices.Bookmarks.BookmarksChanged += OnBookmarksChanged;
        // 连接状态变化时同步引导卡显隐（从控制台连接成功返回首页，引导卡自动消失）
        AppServices.Obs.StateChanged += RefreshWelcome;
    }

    /// <summary>与 <see cref="AttachEvents"/> 严格配对；幂等，重复调用不会误退别人的订阅。</summary>
    private void DetachEvents()
    {
        if (!_eventsAttached) return;
        _eventsAttached = false;

        AppServices.Bookmarks.BookmarksChanged -= OnBookmarksChanged;
        AppServices.Obs.StateChanged -= RefreshWelcome;
    }

    /// <summary>新手引导卡：未连 OBS 时展示，连接后隐藏（StateChanged 可能来自 WebSocket 线程，需切回 UI 线程）。</summary>
    private void RefreshWelcome()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(RefreshWelcome));
            return;
        }
        WelcomeCard.Visibility = AppServices.Obs.IsConnected ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnConnectGuideClick(object sender, RoutedEventArgs e)
        => AppServices.Navigation.Navigate(Routes.Console);

    /// <summary>欢迎卡上的官方下载入口（V2.9.1）：只跳官方渠道，地址常量在 ObsDownloadLinks。</summary>
    private async void OnOpenObsDownload(object sender, RoutedEventArgs e)
        => await OpenOfficialAsync(Services.Update.ObsDownloadLinks.OfficialDownload);

    /// <summary>欢迎卡上的官方 GitHub 发布页入口（V2.9.1）。</summary>
    private async void OnOpenObsGitHub(object sender, RoutedEventArgs e)
        => await OpenOfficialAsync(Services.Update.ObsDownloadLinks.GitHubLatestRelease);

    private static async Task OpenOfficialAsync(string url)
    {
        try
        {
            var ok = await AppServices.Host.OpenExternalAsync(url).ConfigureAwait(true);
            if (!ok) AppServices.Toast.Show(Strings.T("common.openBrowserFailed") + url, "error");
        }
        catch (Exception ex)
        {
            AppServices.Toast.Show(Strings.T("common.openLinkError", ex.Message), "error");
        }
    }

    /// <summary>
    /// 重建收藏区。收藏事件是在 ProblemCard 的点击处理中同步触发的，
    /// 直接重建会把正在处理点击的卡片从可视树上摘掉，因此延后到本次输入处理之后再刷新。
    /// </summary>
    private void OnBookmarksChanged() => Dispatcher.BeginInvoke(new Action(RefreshBookmarks));

    private async void RefreshBookmarks()
    {
        try
        {
            var ids = AppServices.Bookmarks.GetAll();
            BookmarkList.Children.Clear();

            if (ids.Count == 0)
            {
                BookmarkSection.Visibility = Visibility.Collapsed;
                return;
            }

            var all = await AppServices.Problems.GetProblemsAsync();
            var categories = await AppServices.Problems.GetCategoriesAsync();
            var titles = categories.ToDictionary(c => c.Id, c => c.Title);

            var marked = all.Where(p => ids.Contains(p.Id)).ToList();
            foreach (var p in marked)
            {
                var card = new ProblemCard();
                card.Bind(p, titles.GetValueOrDefault(p.Category, ""));
                BookmarkList.Children.Add(card);
            }

            // 收藏 id 可能指向已下线的问题，此时列表为空，整块隐藏而不是留个空标题
            BookmarkSection.Visibility = marked.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            App.ReportError(Errors.ErrorCodes.DataLoadFailed, ex);
        }
    }

    private void OnSearchClick(object sender, RoutedEventArgs e)
        => AppServices.Navigation.Navigate(Routes.Search);

    private void OnAssistantClick(object sender, RoutedEventArgs e)
        => AppServices.Navigation.Navigate(Routes.Assistant);

    /// <summary>首页的「反馈」快捷入口（V2.9.4）：直达帮助与反馈页。</summary>
    private void OnFeedbackClick(object sender, RoutedEventArgs e)
        => AppServices.Navigation.Navigate(Routes.Feedback);

    private void OnCategoryClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string id && !string.IsNullOrEmpty(id))
        {
            AppServices.Navigation.Navigate(Routes.Category, id);
        }
    }
}
