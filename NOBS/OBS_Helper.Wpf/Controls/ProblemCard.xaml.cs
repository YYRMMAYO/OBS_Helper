using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OBS_Helper.Wpf.Models;
using OBS_Helper.Wpf.Navigation;

namespace OBS_Helper.Wpf.Controls;

/// <summary>
/// 问题列表项。搜索页、分类页、首页收藏区共用同一张卡，保证三处的视觉与交互一致。
/// </summary>
public partial class ProblemCard : UserControl
{
    private Problem? _problem;

    public ProblemCard()
    {
        InitializeComponent();
    }

    /// <param name="problem">问题数据。</param>
    /// <param name="categoryTitle">分类名，用于卡片上的分类药丸；传空则隐藏。</param>
    public void Bind(Problem problem, string? categoryTitle = null)
    {
        _problem = problem;

        TitleText.Text = problem.Title;

        var symptom = problem.Symptoms.Length > 0
            ? string.Join(" · ", problem.Symptoms.Take(2))
            : "";
        SymptomText.Text = symptom;
        SymptomText.Visibility = string.IsNullOrEmpty(symptom) ? Visibility.Collapsed : Visibility.Visible;

        SeverityText.Text = problem.Severity;
        SeverityText.Foreground = SeverityBrush(problem.Severity, soft: false);
        SeverityPill.Background = SeverityBrush(problem.Severity, soft: true);

        if (string.IsNullOrEmpty(categoryTitle))
        {
            CategoryPill.Visibility = Visibility.Collapsed;
        }
        else
        {
            CategoryPill.Visibility = Visibility.Visible;
            CategoryText.Text = categoryTitle;
        }

        PlatformText.Text = problem.Platforms.Length > 0
            ? string.Join(" / ", problem.Platforms)
            : "";

        RefreshStar();
    }

    private Brush SeverityBrush(string severity, bool soft)
    {
        var key = severity switch
        {
            "严重" => soft ? "DangerSoftBrush" : "DangerBrush",
            "常见" => soft ? "WarnSoftBrush" : "WarnBrush",
            _ => soft ? "InfoSoftBrush" : "InfoBrush"
        };
        return TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    private void RefreshStar()
    {
        if (_problem is null) return;
        var on = AppServices.Bookmarks.IsBookmarked(_problem.Id);
        StarButton.Content = on ? "★" : "☆";
        StarButton.Foreground = on
            ? (TryFindResource("WarnBrush") as Brush ?? Brushes.Goldenrod)
            : (TryFindResource("MutedBrush") as Brush ?? Brushes.Gray);
        StarButton.ToolTip = on ? "取消收藏" : "收藏";
    }

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        if (_problem is null) return;
        AppServices.Navigation?.Navigate(Routes.Problem, _problem.Id);
    }

    private void OnToggleBookmark(object sender, RoutedEventArgs e)
    {
        if (_problem is null) return;
        AppServices.Bookmarks.Toggle(_problem.Id);
        RefreshStar();
    }
}
