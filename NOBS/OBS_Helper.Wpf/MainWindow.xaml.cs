using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OBS_Helper.Wpf.Navigation;
using OBS_Helper.Wpf.Views;

namespace OBS_Helper.Wpf;

/// <summary>
/// 主窗口：左侧固定导航 + 顶栏（返回 / 标题 / 连接状态）+ 中间页面容器。
///
/// 取代 Blazor 版的 MainLayout：导航项一一对应原来的底部 8 个 Tab，
/// 分类页 / 问题详情页 / 日志页没有导航项，只能从其它页面跳进来（与原版一致）。
/// </summary>
public partial class MainWindow : Window
{
    private readonly NavigationService _nav = new();

    /// <summary>路由 → (导航项, 标题, 副标题)。没有导航项的页面第一个元素为 null。</summary>
    private readonly Dictionary<string, (RadioButton? Tab, string Title, string Subtitle)> _meta;

    /// <summary>切换导航高亮时抑制 Checked 事件，避免自己触发自己。</summary>
    private bool _syncingNav;

    public MainWindow()
    {
        InitializeComponent();

        _meta = new(StringComparer.OrdinalIgnoreCase)
        {
            [Routes.Home] = (NavHome, "首页", "按分类查问题，或直接问助手"),
            [Routes.Search] = (NavSearch, "搜索问题", "输入关键词，边打边找"),
            [Routes.Assistant] = (NavAssistant, "问我一下", "描述你遇到的现象，我来定位"),
            [Routes.Diagnostic] = (NavDiagnostic, "智能诊断", "连上 OBS 后一键体检"),
            [Routes.Setup] = (NavSetup, "直播搭建", "从零到开播的完整流程"),
            [Routes.Templates] = (NavTemplates, "场景模板", "一键搭好整套场景与来源"),
            [Routes.Console] = (NavConsole, "OBS 控制台", "远程控制场景、录制与推流"),
            [Routes.Guide] = (NavGuide, "排障指引", "通用排查思路与速查手册"),
            [Routes.Settings] = (NavSettings, "设置", "诊断引擎、外观与关于"),
            [Routes.Category] = (null, "分类", ""),
            [Routes.Problem] = (null, "问题详情", ""),
            [Routes.Logs] = (null, "日志分析", "离线解析 OBS 日志，定位异常"),
            [Routes.ObsConfig] = (null, "OBS 配置管理", "备份、导入导出与重置"),
        };

        RegisterRoutes();

        AppServices.Navigation = _nav;
        _nav.Navigated += OnNavigated;
        _nav.CanGoBackChanged += SyncBackButton;

        var ver = typeof(MainWindow).Assembly.GetName().Version;
        if (ver is not null) VersionText.Text = $"v{ver.Major}.{ver.Minor}.{ver.Build}";

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void RegisterRoutes()
    {
        _nav.Register(Routes.Home, () => new HomePage());
        _nav.Register(Routes.Search, () => new SearchPage());
        _nav.Register(Routes.Assistant, () => new AssistantPage());
        _nav.Register(Routes.Diagnostic, () => new DiagnosticPage());
        _nav.Register(Routes.Setup, () => new SetupPage());
        _nav.Register(Routes.Templates, () => new TemplatePage());
        _nav.Register(Routes.Console, () => new ConsolePage());
        _nav.Register(Routes.Guide, () => new GuidePage());
        _nav.Register(Routes.Settings, () => new SettingsPage());
        _nav.Register(Routes.Category, () => new CategoryPage());
        _nav.Register(Routes.Problem, () => new ProblemPage());
        _nav.Register(Routes.Logs, () => new LogsPage());
        _nav.Register(Routes.ObsConfig, () => new ObsConfigPage());
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await AppServices.InitializeAsync();
        }
        catch (Exception ex)
        {
            App.ReportError(Errors.ErrorCodes.StartupFailed, ex);
        }

        // 自检测试：逐个导航所有路由，把异常写到 selftest_result.txt 后退出。
        // 用环境变量触发，避免影响正常启动。
        if (Environment.GetEnvironmentVariable("OBS_SELFTEST") == "1")
        {
            await RunSelfTestAsync();
            return;
        }

        await _nav.NavigateAsync(Routes.Home, pushHistory: false);
    }

    /// <summary>
    /// 自动化自检：遍历全部 13 个路由（含带参数的分类页 / 问题详情页），
    /// 捕获 XAML 解析、构造函数、OnNavigatedToAsync 各阶段异常，汇总写入 <c>selftest_result.txt</c>。
    /// 这是「编译通过但运行时才炸」类错误（尤其 <c>{Static|Dynamic}Resource</c> 拼错）最有效的拦截手段。
    /// </summary>
    private async Task RunSelfTestAsync()
    {
        App.HeadlessTest = true;
        var results = new List<string>();
        var data = await AppServices.Problems.GetDataAsync().ConfigureAwait(true);
        var firstCategory = data.Categories.FirstOrDefault()?.Id;
        var firstProblem = data.Problems.FirstOrDefault()?.Id;

        // (路由, 参数)。无独立导航项的页面需要给个合法参数，否则只会走空数据分支。
        var cases = new (string Route, object? Param)[]
        {
            (Routes.Home, null),
            (Routes.Search, null),
            (Routes.Assistant, null),
            (Routes.Diagnostic, null),
            (Routes.Setup, null),
            (Routes.Templates, null),
            (Routes.Console, null),
            (Routes.Guide, null),
            (Routes.Settings, null),
            (Routes.Category, firstCategory),
            (Routes.Problem, firstProblem),
            (Routes.Logs, null),
            (Routes.ObsConfig, null),
        };

        foreach (var (route, param) in cases)
        {
            var before = App.HeadlessErrors.Count;
            try
            {
                await _nav.NavigateAsync(route, param, pushHistory: false).ConfigureAwait(true);
                var extra = before == App.HeadlessErrors.Count
                    ? ""
                    : $"  [ReportError x{App.HeadlessErrors.Count - before}]";
                results.Add($"PASS  {route,-10} param={(param ?? "null")}{extra}");
            }
            catch (Exception ex)
            {
                results.Add($"FAIL  {route,-10} param={(param ?? "null")}  -> {ex.GetType().Name}: {ex.Message}");
            }
        }

        var ok = results.Count(r => r.StartsWith("PASS"));
        var fail = results.Count - ok;
        var report = new StringBuilder();
        report.AppendLine($"OBS_Helper WPF 自检  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        report.AppendLine($"路由覆盖: {ok} PASS / {fail} FAIL  (共 {cases.Length})");
        report.AppendLine(new string('-', 60));
        foreach (var line in results) report.AppendLine(line);
        if (App.HeadlessErrors.Count > 0)
        {
            report.AppendLine(new string('-', 60));
            report.AppendLine("ReportError 收集到的错误:");
            foreach (var err in App.HeadlessErrors) report.AppendLine("  - " + err.Replace("\n", "\n    "));
        }

        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest_result.txt");
        File.WriteAllText(path, report.ToString());

        // 自检完毕，退出进程，让调用方读取结果文件
        _ = Dispatcher.BeginInvoke(new Action(() => Application.Current.Shutdown()));
    }

    private async void OnClosed(object? sender, EventArgs e)
    {
        // 退出时断开 OBS，避免 WebSocket 线程拖住进程
        try { await AppServices.Obs.DisposeAsync(); } catch { /* 退出路径，忽略 */ }
        AppServices.Appearance.Dispose();
    }

    // ------------------------------------------------------------ 导航联动

    private void OnNavigated(string route, UserControl view)
    {
        PageHost.Content = view;

        if (_meta.TryGetValue(route, out var meta))
        {
            PageTitle.Text = meta.Title;
            PageSubtitle.Text = meta.Subtitle;
            PageSubtitle.Visibility = string.IsNullOrEmpty(meta.Subtitle)
                ? Visibility.Collapsed
                : Visibility.Visible;

            _syncingNav = true;
            foreach (var (tab, _, _) in _meta.Values)
            {
                if (tab is not null) tab.IsChecked = false;
            }
            if (meta.Tab is not null) meta.Tab.IsChecked = true;
            _syncingNav = false;
        }

        SyncBackButton();
    }

    /// <summary>供页面在加载完数据后改写顶栏标题（分类页 / 问题详情页用）。</summary>
    public void SetHeader(string title, string? subtitle = null)
    {
        PageTitle.Text = title;
        PageSubtitle.Text = subtitle ?? "";
        PageSubtitle.Visibility = string.IsNullOrEmpty(subtitle) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SyncBackButton()
        => BackButton.Visibility = _nav.CanGoBack ? Visibility.Visible : Visibility.Collapsed;

    private void OnNavChecked(object sender, RoutedEventArgs e)
    {
        if (_syncingNav || sender is not RadioButton rb) return;

        var route = _meta.FirstOrDefault(kv => ReferenceEquals(kv.Value.Tab, rb)).Key;
        if (string.IsNullOrEmpty(route) || route == _nav.CurrentRoute) return;

        // 一级 Tab 之间切换视为「换主线」，清空历史，避免返回栈无限增长
        _nav.ClearHistory();
        _nav.Navigate(route, pushHistory: false);
    }

    private void OnBackClick(object sender, RoutedEventArgs e) => _nav.GoBack();

    private void OnSettingsClick(object sender, RoutedEventArgs e) => _nav.Navigate(Routes.Settings);

    private void OnFindExecuted(object sender, ExecutedRoutedEventArgs e) => _nav.Navigate(Routes.Search);

    private void OnBrowseBackExecuted(object sender, ExecutedRoutedEventArgs e) => _nav.GoBack();
}
