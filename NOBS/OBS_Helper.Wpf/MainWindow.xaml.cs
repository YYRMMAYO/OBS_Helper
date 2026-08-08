using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using OBS_Helper.Wpf.Controls;
using OBS_Helper.Wpf.Navigation;
using OBS_Helper.Wpf.Services;
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

        // 全局加载遮罩与统一 Toast：宿主元素在本窗口 XAML 里，构造时注入组合根
        AppServices.Busy = new BusyService(BusyOverlayHost);
        AppServices.Toast = new ToastService(ToastHost);

        _meta = new(StringComparer.OrdinalIgnoreCase)
        {
            [Routes.Home] = (NavHome, "首页", "按分类查问题，或直接问助手"),
            [Routes.Search] = (NavSearch, "搜索问题", "输入关键词，边打边找"),
            [Routes.Assistant] = (NavAssistant, "问我一下", "描述你遇到的现象，我来定位"),
            [Routes.Diagnostic] = (NavDiagnostic, "智能诊断", "连上 OBS 后一键体检"),
            [Routes.Setup] = (NavSetup, "直播搭建", "从零到开播的完整流程"),
            [Routes.Templates] = (NavTemplates, "场景模板", "一键搭好整套场景与来源"),
            [Routes.Console] = (NavConsole, "OBS 控制台", "远程控制场景、录制与推流"),
            [Routes.Performance] = (NavPerformance, "系统监控", "CPU / 内存 / 网络 / 磁盘实时曲线"),
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
        Closing += OnClosing;
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
        _nav.Register(Routes.Performance, () => new PerformancePage());
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

        // 后台能力的事件接线（托盘 / 全局热键）
        AppServices.Tray.ShowRequested += OnTrayShowRequested;
        AppServices.Tray.ExitRequested += OnTrayExitRequested;
        AppServices.Tray.MiniWindowRequested += OnMiniWindowRequested;
        AppServices.Hotkeys.ToggleWindowRequested += OnToggleWindowRequested;
        AppServices.Hotkeys.ToggleMiniWindowRequested += OnMiniWindowRequested;

        // 自检测试：逐个导航所有路由，把异常写到 selftest_result.txt 后退出。
        // 用环境变量触发，避免影响正常启动。
        if (Environment.GetEnvironmentVariable("OBS_SELFTEST") == "1")
        {
            await RunSelfTestAsync();
            return;
        }

        await _nav.NavigateAsync(Routes.Home, pushHistory: false);

        // 启动后静默检查一次更新：有新版才弹窗，失败/无更新一律不打扰。
        _ = RunStartupUpdateCheckAsync();
    }

    /// <summary>
    /// 启动自动更新检查（fire-and-forget）。只在新版本可用时弹窗；
    /// 网络不可达、GitHub 异常等失败场景静默跳过，不影响启动与正常使用。
    /// </summary>
    private static async Task RunStartupUpdateCheckAsync()
    {
        try
        {
            var result = await AppServices.Updates.CheckAsync();
            if (result.Status == UpdateCheckStatus.UpdateAvailable)
            {
                UpdateDialog.Show(result.CurrentVersion, result.LatestVersion);
            }
        }
        catch (Exception)
        {
            // 更新检查属于锦上添花，任何异常都不得打断主流程
        }
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
            (Routes.Performance, null),
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

        // 小窗：创建 + 显示 + 隐藏，拦截 XAML 解析 / 资源引用 / 位置恢复错误（自检时窗口一闪而过）
        try
        {
            AppServices.Mini.Toggle();
            AppServices.Mini.Toggle();
            results.Add($"PASS  mini      (XAML + 状态刷新 + 显隐)");
        }
        catch (Exception ex)
        {
            results.Add($"FAIL  mini      -> {ex.GetType().Name}: {ex.Message}");
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
        AppServices.ShutdownServices();
    }

    // ------------------------------------------------------------ 托盘 / 关闭行为

    /// <summary>托盘「退出」已触发：允许真正关闭窗口并退出进程。</summary>
    private bool _allowExit;

    /// <summary>关闭窗口时若开启了「最小化到托盘」，改为隐藏而不是退出。</summary>
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!App.HeadlessTest && !_allowExit && AppServices.Tray.Settings.CloseToTray)
        {
            e.Cancel = true;
            Hide();
            AppServices.Tray.Notify("已最小化到托盘",
                "OBS 排障助手仍在后台运行，双击托盘图标或从托盘菜单可恢复窗口。");
        }
    }

    /// <summary>托盘菜单「显示主窗口」/ 双击图标。</summary>
    private void OnTrayShowRequested()
        => Dispatcher.BeginInvoke(new Action(() =>
        {
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
        }));

    /// <summary>托盘菜单「小窗控制」/ 全局热键「小窗」：呼出或隐藏迷你小窗。</summary>
    private void OnMiniWindowRequested()
        => Dispatcher.BeginInvoke(new Action(() => AppServices.Mini.Toggle()));

    /// <summary>托盘菜单「退出」。</summary>
    private void OnTrayExitRequested()
        => Dispatcher.BeginInvoke(new Action(() =>
        {
            _allowExit = true;
            Application.Current.Shutdown();
        }));

    /// <summary>全局热键「显示 / 隐藏主窗口」。</summary>
    private void OnToggleWindowRequested()
        => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (IsVisible && WindowState != WindowState.Minimized)
            {
                Hide();
            }
            else
            {
                Show();
                if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                Activate();
            }
        }));

    // ------------------------------------------------------------ 导航联动

    /// <summary>
    /// 页面过渡时长（毫秒）。模块间切换用「淡入 + 轻微上移」的组合动效，
    /// 时长比界面默认动效（MotionDuration 160ms）更长，过渡更明显、更顺滑。
    /// 「减少动画」开启时直接显示，不播任何动效。
    /// </summary>
    private static readonly TimeSpan PageFadeDuration = TimeSpan.FromMilliseconds(220);
    private static readonly TimeSpan PageSlideDuration = TimeSpan.FromMilliseconds(300);
    private const double PageSlideOffset = 16;

    private void OnNavigated(string route, UserControl view)
    {
        AnimatePageIn(view);
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

    /// <summary>
    /// 页面入场动效：透明度 0→1 + 从下方 16px 上移归位，用两次缓动曲线叠加出「浮上来」的感觉。
    /// 页面实例被导航缓存复用，重复入场时属性会从上次的终值重新开始动画，不会残留异常状态。
    /// </summary>
    private static void AnimatePageIn(FrameworkElement view)
    {
        if (AppServices.Appearance.Settings.ReduceMotion)
        {
            // 无障碍「减少动画」：直接以终态显示；先清掉可能残留的旧动画（属性被动画持有期间直赋值无效）
            view.BeginAnimation(UIElement.OpacityProperty, null);
            view.Opacity = 1;
            view.RenderTransform = Transform.Identity;
            view.RenderTransformOrigin = new Point(0.5, 0.5);
            return;
        }

        var slide = new TranslateTransform(0, PageSlideOffset);
        view.RenderTransformOrigin = new Point(0.5, 0.5);
        view.RenderTransform = slide;

        var fade = new DoubleAnimation(0, 1, PageFadeDuration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        view.BeginAnimation(UIElement.OpacityProperty, fade);

        var y = new DoubleAnimation(PageSlideOffset, 0, PageSlideDuration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        slide.BeginAnimation(TranslateTransform.YProperty, y);
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
