using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using OBS_Helper.Wpf.Controls;
using OBS_Helper.Wpf.Models;
using OBS_Helper.Wpf.Navigation;
using OBS_Helper.Wpf.Services;

namespace OBS_Helper.Wpf.Views;

/// <summary>
/// 搜索页：边打边搜（不设搜索按钮），可再按分类收窄结果。
/// </summary>
public partial class SearchPage : UserControl, INavigationAware
{
    /// <summary>
    /// 分类药丸的绑定数据。「全部」也是一项，Id 为空串。
    /// 声明成 internal 而非 private：WPF 绑定靠反射取值，非公开类型上的公开属性才取得到。
    /// </summary>
    internal sealed class Chip
    {
        public string Id { get; init; } = "";
        public string Label { get; init; } = "";
        public bool IsAll { get; init; }
    }

    /// <summary>
    /// 结果卡池。95 条数据全量渲染时每次按键都重建控件会明显卡顿，
    /// 因此卡片只增不删，多出来的隐藏掉，下一次搜索直接重新 Bind。
    /// </summary>
    private readonly List<ProblemCard> _cards = new();

    private Dictionary<string, string> _categoryTitles = new();
    private string _activeCategory = "";

    /// <summary>搜索序号：输入很快时丢弃过期的异步结果，避免旧结果覆盖新结果。</summary>
    private int _searchSeq;

    /// <summary>输入防抖：停止输入 300ms 后才发起搜索，避免逐击穿发（P1-2）。</summary>
    private readonly Debouncer _debouncer = new(TimeSpan.FromMilliseconds(300));

    private bool _chipsBuilt;

    public SearchPage()
    {
        InitializeComponent();

        // 事件在这里挂而不是写在 XAML 里：TextBox 初始化阶段就可能抛一次 TextChanged，
        // 那时后面几个 x:Name 字段还没赋值，处理函数会撞上 null。
        QueryBox.TextChanged += OnQueryChanged;
    }

    public async Task OnNavigatedToAsync(object? parameter)
    {
        try
        {
            await BuildChipsAsync();
            await RunSearchAsync();
        }
        catch (Exception ex)
        {
            App.ReportError(Errors.ErrorCodes.DataLoadFailed, ex);
            return;
        }

        // 进来就能直接打字（顶栏 Ctrl+F 也会跳到本页），布局完成后再抢焦点。
        // 弃元接住 DispatcherOperation：这里是「排队执行」而非「等它跑完」，不需要 await。
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            QueryBox.Focus();
            QueryBox.SelectAll();
        }), DispatcherPriority.Input);
    }

    public Task OnNavigatedFromAsync()
    {
        // 离开页面：取消尚未执行的防抖搜索，避免回来后还触发一次 UI 更新
        _debouncer.Cancel();
        return Task.CompletedTask;
    }

    private async Task BuildChipsAsync()
    {
        if (_chipsBuilt) return;

        var categories = await AppServices.Problems.GetCategoriesAsync();
        _categoryTitles = categories.ToDictionary(c => c.Id, c => c.Title);

        var chips = new List<Chip> { new() { Id = "", Label = "全部", IsAll = true } };
        chips.AddRange(categories.Select(c => new Chip
        {
            Id = c.Id,
            Label = $"{c.Icon} {c.Title}"
        }));

        ChipList.ItemsSource = chips;
        _chipsBuilt = true;
    }

    private async Task RunSearchAsync()
    {
        var seq = ++_searchSeq;
        var query = QueryBox.Text;

        var all = await AppServices.Problems.SearchAsync(query);
        if (seq != _searchSeq) return;

        var results = string.IsNullOrEmpty(_activeCategory)
            ? all
            : all.Where(p => p.Category == _activeCategory).ToList();

        Render(results);

        CountText.Text = string.IsNullOrWhiteSpace(query)
            ? $"共 {results.Count} 个方案，输入关键词可缩小范围"
            : $"找到 {results.Count} 条结果";

        EmptyText.Visibility = results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Render(List<Problem> results)
    {
        for (var i = 0; i < results.Count; i++)
        {
            ProblemCard card;
            if (i < _cards.Count)
            {
                card = _cards[i];
            }
            else
            {
                card = new ProblemCard();
                _cards.Add(card);
                ResultList.Children.Add(card);
            }

            card.Bind(results[i], _categoryTitles.GetValueOrDefault(results[i].Category, ""));
            card.Visibility = Visibility.Visible;
        }

        for (var i = results.Count; i < _cards.Count; i++)
        {
            _cards[i].Visibility = Visibility.Collapsed;
        }
    }

    private void OnQueryChanged(object sender, TextChangedEventArgs e)
    {
        // 防抖：连续输入只在停顿后触发一次搜索（P1-2），RunSearchAsync 内部有 _searchSeq 二次防竞态
        _debouncer.DebounceAsync(RunSearchAsync);
    }

    private async void OnChipChecked(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        _activeCategory = fe.Tag as string ?? "";

        try
        {
            await RunSearchAsync();
        }
        catch (Exception ex)
        {
            App.ReportError(Errors.ErrorCodes.DataLoadFailed, ex);
        }
    }
}
