using System.Windows;
using System.Windows.Controls;
using OBS_Helper.Wpf.Controls;
using OBS_Helper.Wpf.Models;
using OBS_Helper.Wpf.Navigation;

namespace OBS_Helper.Wpf.Views;

/// <summary>
/// 首页：两个快捷入口 + 数据驱动的分类卡 + 我的收藏。
///
/// 页面实例被导航服务缓存复用，所以数据装配放在 <see cref="OnNavigatedToAsync"/> 而不是构造函数，
/// 每次回到首页都会按最新的收藏状态重建列表。
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
        public string Color { get; init; } = "";
        public string CountText { get; init; } = "";
    }

    public HomePage()
    {
        InitializeComponent();

        // 收藏可能在详情页 / 搜索页被改动。页面实例常驻，这里订阅后不再退订，
        // 生命周期与应用一致，不会泄漏。
        AppServices.Bookmarks.BookmarksChanged += OnBookmarksChanged;
    }

    public async Task OnNavigatedToAsync(object? parameter)
    {
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
                Color = c.Color,
                CountText = $"{counts.GetValueOrDefault(c.Id, 0)} 个方案"
            }).ToList();

            var error = AppServices.Problems.LoadError;
            LoadErrorPanel.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
            if (error is not null) LoadErrorText.Text = Errors.ErrorCodes.Format(error);

            RefreshBookmarks();
        }
        catch (Exception ex)
        {
            App.ReportError(Errors.ErrorCodes.DataLoadFailed, ex);
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

    private void OnCategoryClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string id && !string.IsNullOrEmpty(id))
        {
            AppServices.Navigation.Navigate(Routes.Category, id);
        }
    }
}
