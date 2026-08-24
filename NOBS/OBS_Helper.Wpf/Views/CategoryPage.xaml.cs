using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using OBS_Helper.Wpf.Controls;
using OBS_Helper.Wpf.Navigation;

namespace OBS_Helper.Wpf.Views;

/// <summary>
/// 分类页：按分类 id 列出问题。
/// </summary>
public partial class CategoryPage : UserControl, INavigationAware
{
    public CategoryPage()
    {
        InitializeComponent();
    }

    /// <param name="parameter">分类 id（string）。</param>
    public async Task OnNavigatedToAsync(object? parameter)
    {
        // 页面实例是复用的，先把上一个分类的内容清干净
        ProblemList.Children.Clear();
        HeaderPanel.Visibility = Visibility.Collapsed;
        EmptyText.Visibility = Visibility.Collapsed;

        var id = parameter as string ?? "";

        try
        {
            var category = await AppServices.Problems.GetCategoryAsync(id);
            if (category is null)
            {
                SetHeader("分类", null);
                EmptyText.Text = "未找到该分类。";
                EmptyText.Visibility = Visibility.Visible;
                return;
            }

            SetHeader($"{category.Icon} {category.Title}", category.Description);

            HeaderPanel.Visibility = Visibility.Visible;
            AccentBar.Background = AccentBrush(category.Semantic);
            DescriptionText.Text = category.Description;

            var problems = await AppServices.Problems.GetByCategoryAsync(id);
            CountText.Text = $"共 {problems.Count} 个方案";

            foreach (var p in problems)
            {
                var card = new ProblemCard();
                card.Bind(p, category.Title);
                ProblemList.Children.Add(card);
            }

            if (problems.Count == 0)
            {
                EmptyText.Text = "该分类下暂无方案。";
                EmptyText.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            App.ReportError(Errors.ErrorCodes.DataLoadFailed, ex);
        }
    }

    /// <summary>把分类名写到顶栏，页面内就不必再放一遍大标题。</summary>
    private static void SetHeader(string title, string? subtitle)
        => (Application.Current.MainWindow as MainWindow)?.SetHeader(title, subtitle);

    /// <summary>
    /// 把分类语义键（red/orange/...）映射到主题资源画刷，深浅色自动生效（P2-1）。
    /// 找不到时回退品牌色。
    /// </summary>
    private Brush AccentBrush(string semantic)
    {
        var brush = (TryFindResource("SemanticBrush") as IValueConverter)?
            .Convert(semantic, typeof(Brush), null, CultureInfo.InvariantCulture) as Brush;
        return brush ?? TryFindResource("BrandBrush") as Brush ?? Brushes.Gray;
    }
}
