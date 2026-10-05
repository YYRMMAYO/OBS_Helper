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
using OBS_Helper.Wpf.Services.ObsConfig;
using OBS_Helper.Wpf.Services.Shell;
using OBS_Helper.Wpf.Services.Update;
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

    /// <summary>路由 → (导航项, 标题键, 副标题键)。标题与副标题存**文案键**而不是成品文案：
    /// 语言切换后顶栏要跟着变，所以取文案的时机放在渲染时（见 OnNavigated / RefreshHeaderForLanguage）。</summary>
    private readonly Dictionary<string, (RadioButton? Tab, string TitleKey, string SubtitleKey)> _meta;

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
            [Routes.Home] = (NavHome, "page.home.title", "page.home.subtitle"),
            [Routes.Search] = (NavSearch, "page.search.title", "page.search.subtitle"),
            [Routes.Assistant] = (NavAssistant, "page.assistant.title", "page.assistant.subtitle"),
            [Routes.Diagnostic] = (NavDiagnostic, "page.diagnostic.title", "page.diagnostic.subtitle"),
            [Routes.Setup] = (NavSetup, "page.setup.title", "page.setup.subtitle"),
            [Routes.Templates] = (NavTemplates, "page.templates.title", "page.templates.subtitle"),
            [Routes.Plugins] = (NavPlugins, "page.plugins.title", "page.plugins.subtitle"),
            [Routes.Toolbox] = (NavToolbox, "page.toolbox.title", "page.toolbox.subtitle"),
            [Routes.Console] = (NavConsole, "page.console.title", "page.console.subtitle"),
            [Routes.Performance] = (NavPerformance, "page.performance.title", "page.performance.subtitle"),
            [Routes.Guide] = (NavGuide, "page.guide.title", "page.guide.subtitle"),
            [Routes.Feedback] = (NavFeedback, "page.feedback.title", "page.feedback.subtitle"),
            [Routes.Settings] = (NavSettings, "page.settings.title", "page.settings.subtitle"),
            [Routes.Category] = (null, "page.category.title", ""),
            [Routes.Problem] = (null, "page.problem.title", ""),
            [Routes.Logs] = (null, "page.logs.title", "page.logs.subtitle"),
            [Routes.ObsConfig] = (null, "page.obsconfig.title", "page.obsconfig.subtitle"),
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

        // 设置页「重新展示引导」→ 立即重播（静态事件解耦，见 App.RequestOnboardingReset）
        App.OnboardingResetRequested += OnOnboardingResetRequested;
        AppServices.Localization.Changed += OnLanguageChanged;
        BuildOnboardingDots();
    }

    private void RegisterRoutes()
    {
        _nav.Register(Routes.Home, () => new HomePage());
        _nav.Register(Routes.Search, () => new SearchPage());
        _nav.Register(Routes.Assistant, () => new AssistantPage());
        _nav.Register(Routes.Diagnostic, () => new DiagnosticPage());
        _nav.Register(Routes.Setup, () => new SetupPage());
        _nav.Register(Routes.Templates, () => new TemplatePage());
        _nav.Register(Routes.Plugins, () => new PluginsPage());
        _nav.Register(Routes.Toolbox, () => new ToolboxPage());
        _nav.Register(Routes.Console, () => new ConsolePage());
        _nav.Register(Routes.Performance, () => new PerformancePage());
        _nav.Register(Routes.Guide, () => new GuidePage());
        _nav.Register(Routes.Feedback, () => new FeedbackPage());
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
        AppServices.Tray.OpenRecordingFolderRequested += OnOpenRecordingFolderRequested;
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

        // 首次启动：先展示新手引导，把「启动更新检查」推迟到引导结束之后 ——
        // 首启同时弹引导与更新弹窗会互相打架，对新手也不友好。
        if (TryShowOnboardingForFirstRun())
        {
            _updateCheckDeferredUntilOnboarding = true;
        }
        else
        {
            // 启动后静默检查一次更新：有新版才弹窗，失败/无更新一律不打扰。
            _ = RunStartupUpdateCheckAsync();
        }

        // 启动后静默维护：知识库分离更新（节流 6h，自动应用）+ 旧安装包清理。
        _ = RunStartupMaintenanceAsync();
    }

    // ------------------------------------------------------------ 新手引导（V2.9.0）

    /// <summary>当前展示到第几步（0 基）。</summary>
    private int _onboardingStep;

    /// <summary>首启时更新检查被引导推迟；引导结束后补跑一次。</summary>
    private bool _updateCheckDeferredUntilOnboarding;

    /// <summary>进度圆点（数量与 OnboardingGuide.Steps 一致，构造时生成一次）。</summary>
    private readonly List<System.Windows.Shapes.Ellipse> _onboardingDots = new();

    /// <summary>按步骤数生成进度圆点：步骤数改动时不需要改 XAML。</summary>
    private void BuildOnboardingDots()
    {
        OnbDots.Children.Clear();
        _onboardingDots.Clear();

        for (var i = 0; i < OnboardingGuide.StepCount; i++)
        {
            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = 8,
                Height = 8,
                Margin = new Thickness(4, 0, 4, 0),
                // 未选中态：用次要文字色的低透明度，深浅主题下都能与卡片底拉开层次
                // （本应用没有 TextDisabledBrush 这类专用资源，避免为此新增色板项）
                Opacity = IdleDotOpacity
            };
            dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "MutedBrush");
            _onboardingDots.Add(dot);
            OnbDots.Children.Add(dot);
        }
    }

    /// <summary>未选中进度点的透明度。</summary>
    private const double IdleDotOpacity = 0.35;

    /// <summary>
    /// 首启按需展示引导。<c>OBS_SELFTEST</c> 自检模式下不展示（自检只验证路由能构造出来）。
    /// 返回是否已展示。
    /// </summary>
    private bool TryShowOnboardingForFirstRun()
    {
        if (App.HeadlessTest) return false;
        if (!OnboardingGuide.ShouldShow(AppServices.Store.GetItem(OnboardingGuide.PrefKey))) return false;

        ShowOnboarding();
        return true;
    }

    /// <summary>从头开始展示引导（首启与设置页「重新展示引导」共用）。</summary>
    private void ShowOnboarding()
    {
        _onboardingStep = 0;
        RenderOnboardingStep();

        OnboardingLayer.Visibility = Visibility.Visible;

        if (AppServices.Appearance.Settings.ReduceMotion)
        {
            // 无障碍「减少动画」：直接以终态显示（属性被动画持有期间直赋值无效，先清动画）
            OnboardingLayer.BeginAnimation(UIElement.OpacityProperty, null);
            OnboardingLayer.Opacity = 1;
            FocusOnboardingPrimaryButton();
            return;
        }

        OnboardingLayer.Opacity = 0;
        OnboardingLayer.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        FocusOnboardingPrimaryButton();
    }

    /// <summary>
    /// 把键盘焦点放到引导主按钮上：否则焦点可能残留在覆盖层之下的导航项上，
    /// 这时按空格会「隔着引导」切页面，看起来像引导失灵。
    /// </summary>
    private void FocusOnboardingPrimaryButton()
    {
        try { OnbNextBtn.Focus(); }
        catch (Exception) { /* 焦点失败不影响引导本身 */ }
    }

    /// <summary>覆盖层是否正盖在界面上（用于屏蔽导航热键）。</summary>
    private bool IsOnboardingVisible => OnboardingLayer.Visibility == Visibility.Visible;

    /// <summary>渲染当前步骤（文案 + 对应页面 + 跳转按钮 + 进度点 + 按钮状态）。越界下标由 OnboardingGuide 夹取。</summary>
    private void RenderOnboardingStep()
    {
        _onboardingStep = OnboardingGuide.Clamp(_onboardingStep);

        var step = OnboardingGuide.Step(_onboardingStep);
        OnbStepTitle.Text = step.Title;
        OnbStepDesc.Text = step.Description;
        OnbStepCounter.Text = Strings.T("onb.stepCounter", _onboardingStep + 1, OnboardingGuide.StepCount);

        var accent = (System.Windows.Media.Brush)FindResource("BrandBrush");
        var idle = (System.Windows.Media.Brush)FindResource("MutedBrush");
        for (var i = 0; i < _onboardingDots.Count; i++)
        {
            var isActive = i == _onboardingStep;
            _onboardingDots[i].Fill = isActive ? accent : idle;
            _onboardingDots[i].Opacity = isActive ? 1.0 : IdleDotOpacity;
        }

        OnbBackBtn.Visibility = OnboardingGuide.IsFirst(_onboardingStep)
            ? Visibility.Collapsed
            : Visibility.Visible;

        // 最后一步：主按钮变成「开始使用」，不再提供「跳过」（跳过与完成在此处等价）
        OnbNextBtn.Content = OnboardingGuide.IsLast(_onboardingStep) ? Strings.T("onb.start") : Strings.T("onb.next");
        OnbSkipBtn.Visibility = OnboardingGuide.IsLast(_onboardingStep)
            ? Visibility.Collapsed
            : Visibility.Visible;

        // V2.9.1：把界面切到本步对应的页面，并列出本步提到的其它页面
        BuildOnboardingLinks(step);
        ShowStepTargetPage(step);

        AnimateOnboardingText();
    }

    /// <summary>路由名 → 主窗口路由表里的页面标题（取不到时回退路由名本身）。</summary>
    private string PageNameOf(string route)
    {
        if (string.IsNullOrEmpty(route)) return "";
        return _meta.TryGetValue(route, out var meta) && !string.IsNullOrWhiteSpace(meta.TitleKey)
            ? Strings.T(meta.TitleKey)
            : route;
    }

    /// <summary>
    /// 展示本步对应页面：先在卡片上写明「本步对应页面」，再把界面导航过去。
    ///
    /// 用 <c>pushHistory: false</c>：引导的跳转不该进入「返回」栈（否则用户点几次返回
    /// 会在引导介绍过的页面之间来回跳，与刚讲完的内容对不上）。
    /// 跳转失败（路由没注册）由 NavigationService 转成报错码，不影响引导继续走。
    /// </summary>
    private void ShowStepTargetPage(OnboardingStep step)
    {
        var name = PageNameOf(step.Route);
        OnbTargetText.Text = name;
        OnbTargetRow.Visibility = string.IsNullOrEmpty(name) ? Visibility.Collapsed : Visibility.Visible;

        if (string.IsNullOrEmpty(name)) return;

        var nav = AppServices.Navigation;
        if (nav is null) return;
        if (string.Equals(nav.CurrentRoute, step.Route, StringComparison.OrdinalIgnoreCase)) return;

        nav.Navigate(step.Route, pushHistory: false);
    }

    /// <summary>
    /// 生成本步的跳转按钮：统一用次要按钮（可点的观感一致），站外链接加 ↗ 前缀、
    /// 站内页面加 → 后缀，用于区分「切页面」和「开浏览器」。
    /// 按钮数量随步骤变化，每次渲染重建（每步最多 4 个，开销可忽略）。
    /// </summary>
    private void BuildOnboardingLinks(OnboardingStep step)
    {
        OnbLinks.Children.Clear();

        foreach (var link in step.Links)
        {
            var label = link.External ? "↗ " + link.Label : link.Label + " →";
            var button = new Button
            {
                Style = (Style)FindResource("SecondaryButton"),
                Content = label,
                Tag = link,
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(0, 0, 8, 6),
                ToolTip = link.External ? Strings.T("onb.tipExternal") : Strings.T("onb.tipInternal", PageNameOf(link.Target))
            };
            button.Click += OnOnboardingLinkClick;
            OnbLinks.Children.Add(button);
        }

        OnbLinks.Visibility = OnbLinks.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>点引导卡片上的跳转按钮：站内切页面（不进返回栈），站外交给系统浏览器。</summary>
    private async void OnOnboardingLinkClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: OnboardingLink link }) return;

        try
        {
            if (link.External)
            {
                var ok = await AppServices.Host.OpenExternalAsync(link.Target).ConfigureAwait(true);
                if (!ok) AppServices.Toast.Show(Strings.T("common.openBrowserFailed") + link.Target, "error");
                return;
            }

            AppServices.Navigation?.Navigate(link.Target, pushHistory: false);
        }
        catch (Exception ex)
        {
            // 引导里的跳转失败不该打断引导
            FileLogger.Warn("Onboarding", $"引导跳转失败（{link.Target}）：{ex.Message}");
        }
    }

    /// <summary>步骤文字淡入（每次切步都重播，给「翻页」以视觉反馈）。</summary>
    private void AnimateOnboardingText()
    {
        if (AppServices.Appearance.Settings.ReduceMotion)
        {
            OnbStepTitle.BeginAnimation(UIElement.OpacityProperty, null);
            OnbStepDesc.BeginAnimation(UIElement.OpacityProperty, null);
            OnbStepTitle.Opacity = 1;
            OnbStepDesc.Opacity = 1;
            return;
        }

        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        OnbStepTitle.BeginAnimation(UIElement.OpacityProperty, fade);
        OnbStepDesc.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    /// <summary>结束引导（走完最后一步或点「跳过」）：记下已完成并淡出。</summary>
    private void FinishOnboarding()
    {
        try
        {
            AppServices.Store.SetItem(OnboardingGuide.PrefKey, OnboardingGuide.CompletedValue);
        }
        catch (Exception ex)
        {
            // 偏好写入失败不该拦住用户：本次会话照常收起，下次启动会再展示一次
            FileLogger.Warn("Onboarding", "写入引导完成标记失败：" + ex.Message);
        }

        void AfterHide()
        {
            OnboardingLayer.Visibility = Visibility.Collapsed;
            // 引导过程里为了展示页面做过多次导航，清掉历史：避免用户上手第一下点「返回」
            // 退回到某个只是被引导介绍过的页面。
            _nav.ClearHistory();
            ResumeDeferredUpdateCheck();
        }

        if (AppServices.Appearance.Settings.ReduceMotion)
        {
            OnboardingLayer.BeginAnimation(UIElement.OpacityProperty, null);
            OnboardingLayer.Opacity = 1;
            AfterHide();
            return;
        }

        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(140));
        fadeOut.Completed += (_, _) => AfterHide();
        OnboardingLayer.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    /// <summary>引导结束后补跑被推迟的启动更新检查（仅首启路径会推迟）。</summary>
    private void ResumeDeferredUpdateCheck()
    {
        if (!_updateCheckDeferredUntilOnboarding) return;
        _updateCheckDeferredUntilOnboarding = false;
        _ = RunStartupUpdateCheckAsync();
    }

    private void OnbNextBtn_Click(object sender, RoutedEventArgs e)
    {
        if (OnboardingGuide.IsLast(_onboardingStep))
        {
            FinishOnboarding();
            return;
        }

        _onboardingStep = OnboardingGuide.Next(_onboardingStep);
        RenderOnboardingStep();
    }

    private void OnbBackBtn_Click(object sender, RoutedEventArgs e)
    {
        _onboardingStep = OnboardingGuide.Back(_onboardingStep);
        RenderOnboardingStep();
    }

    private void OnbSkipBtn_Click(object sender, RoutedEventArgs e) => FinishOnboarding();

    /// <summary>设置页要求重播引导：可能在后台线程触发，切回 UI 线程再操作。</summary>
    private void OnOnboardingResetRequested()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(OnOnboardingResetRequested));
            return;
        }

        // 引导正开着时先收起再重播，避免出现「点重置没反应」的观感
        OnboardingLayer.BeginAnimation(UIElement.OpacityProperty, null);
        OnboardingLayer.Visibility = Visibility.Collapsed;
        ShowOnboarding();
    }

    /// <summary>
    /// 等 UI 调度队列里「当前及更早优先级」的工作跑完。
    /// 自检需要等 <c>NavigationService.Navigate</c>（async void）把跳转真正落地再断言：
    /// 这里在 Background 优先级插入一个空操作，排在它前面的 Normal / Loaded 级工作（含导航链路）
    /// 会先执行完，返回时 <c>CurrentRoute</c> 已确定。
    /// </summary>
    private Task WaitForUiIdleAsync()
        => Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background).Task;

    /// <summary>
    /// 启动静默维护（fire-and-forget）：
    /// <list type="bullet">
    ///   <item>知识库：后台拉取最新版，有新版本自动写入本地数据目录并热重载（不弹窗，仅 Toast）；</item>
    ///   <item>安装包：延迟数秒后清理各下载位置的旧安装包（每类保留最新一份）。</item>
    /// </list>
    /// 全部失败静默，不影响启动与正常使用。
    /// </summary>
    private static async Task RunStartupMaintenanceAsync()
    {
        try
        {
            var (updated, newVersion, _) = await AppServices.Kb.RefreshAsync(manual: false).ConfigureAwait(false);
            if (updated)
            {
                AppServices.Problems.Reload();
                AppServices.Toast.Show(Strings.T("settings.kb.updated", newVersion), "ok");
            }
        }
        catch (Exception)
        {
            // 知识库更新属于锦上添花，任何异常都不得打断主流程
        }

        // 插件目录分离更新（V2.2 P0-3）：与问题库同机制，更新后让插件广场下次进入时重建
        try
        {
            var (pluginsUpdated, pluginsVersion, _) = await AppServices.Kb.RefreshPluginsAsync(manual: false).ConfigureAwait(false);
            if (pluginsUpdated)
            {
                AppServices.PluginCatalog.Reload();
                AppServices.Toast.Show(Strings.T("settings.kb.pluginsUpdated", pluginsVersion), "ok");
            }
        }
        catch (Exception)
        {
            // 同上：静默失败
        }

        // 关注插件的静默查新（V2.2 P2-1）：节流 24h，有新版本仅 Toast 不弹窗
        try
        {
            var updates = await AppServices.PluginWatch.CheckForUpdatesAsync().ConfigureAwait(false);
            if (updates.Count > 0)
            {
                var names = string.Join(", ", updates.Take(3).Select(u => $"{u.PluginName} {u.NewTag}"));
                if (updates.Count > 3) names += Strings.T("main.watchedMore", updates.Count);
                AppServices.Toast.Show(Strings.T("main.watchedUpdates", names), "info");
            }
        }
        catch (Exception)
        {
            // 静默失败
        }

        try
        {
            AppServices.Cleanup.RunAtStartup();
        }
        catch (Exception)
        {
            // 清理失败无碍
        }
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
                var choice = UpdateDialog.Show(result.CurrentVersion, result.LatestVersion);
                if (choice == UpdateDialogResult.Applying)
                {
                    // 增量更新已就绪：退出应用，让自举进程完成文件替换后自动重启
                    Application.Current?.Shutdown();
                }
            }
        }
        catch (Exception)
        {
            // 更新检查属于锦上添花，任何异常都不得打断主流程
        }
    }

    /// <summary>
    /// 自动化自检：遍历全部 18 个路由（含带参数的分类页 / 问题详情页），
    /// 捕获 XAML 解析、构造函数、OnNavigatedToAsync 各阶段异常，汇总写入 <c>selftest_result.txt</c>。
    /// 这是「编译通过但运行时才炸」类错误（尤其 <c>{Static|Dynamic}Resource</c> 拼错）最有效的拦截手段。
    ///
    /// 报告文本（以及本方法里的检查项描述）**保持中文**（V2.9.2 的边界约定）：
    /// 它是给开发者 / CI 看的构建产物，不是界面内容，混入界面语言反而让跨版本比对变难。
    /// 用户可见的报错与提示一律走文案表。
    ///
    /// V2.9.2 追加两项：**文案资源解析**（三个代表性控件的实际取值）与
    /// **语言切换往返**（中 → 英 → 中，断言文案与顶栏即时跟随且能还原）。
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
            (Routes.Plugins, null),
            (Routes.Toolbox, null),
            (Routes.Console, null),
            (Routes.Performance, null),
            (Routes.Guide, null),
            (Routes.Feedback, null),
            (Routes.Settings, null),
            (Routes.Category, firstCategory),
            (Routes.Problem, firstProblem),
            (Routes.Plugins, "localvocal"),
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

        // 新手引导覆盖层（V2.9.0 / V2.9.1）：逐步渲染 + 显隐，拦截 FindResource 取不到资源、
        // 步骤越界、跳转目标路由不存在一类只有在展示时才会炸的错误。自检不动偏好（不调用 FinishOnboarding），
        // 避免把开发机的「已完成」标记真的写掉。
        //
        // 每一步走**生产路径**：RenderOnboardingStep 内部用 NavigationService.Navigate 触发跳转
        // （async void），这里靠 WaitForUiIdleAsync 等调度队列跑完再断言 —— 不再自检里单独 await
        // 一次 NavigateAsync 代劳，否则「引导真的会自动跳页」这件事根本没被验证。
        try
        {
            ShowOnboarding();
            var onboardingProblems = new List<string>();
            var errorsBeforeOnboarding = App.HeadlessErrors.Count;

            for (var i = 0; i < OnboardingGuide.StepCount; i++)
            {
                var step = OnboardingGuide.Step(i);

                _onboardingStep = i;
                RenderOnboardingStep();
                await WaitForUiIdleAsync().ConfigureAwait(true);

                // OnbTargetText 在路由名查不到时会回退成路由名本身，所以「读到的不是路由名」即证明
                // 这一步的路由确实在主窗口路由表里（否则界面上会出现 home / console 这种英文名）。
                var expectedName = _meta.TryGetValue(step.Route, out var stepMeta) ? Strings.T(stepMeta.TitleKey) : step.Route;
                if (string.IsNullOrWhiteSpace(OnbTargetText.Text))
                    onboardingProblems.Add($"第 {i + 1} 步没有显示对应页面");
                else if (!string.Equals(OnbTargetText.Text, expectedName, StringComparison.Ordinal))
                    onboardingProblems.Add($"第 {i + 1} 步的页面名未命中路由表（{step.Route}：显示 {OnbTargetText.Text}，期望 {expectedName}）");
                if (OnbLinks.Children.Count != step.Links.Count)
                    onboardingProblems.Add($"第 {i + 1} 步跳转按钮 {OnbLinks.Children.Count}/{step.Links.Count}");
                if (!string.Equals(AppServices.Navigation?.CurrentRoute, step.Route, StringComparison.OrdinalIgnoreCase))
                    onboardingProblems.Add($"第 {i + 1} 步未跳转到 {step.Route}（当前 {AppServices.Navigation?.CurrentRoute}）");
                foreach (var link in step.Links.Where(l => l.External))
                {
                    if (!ObsDownloadLinks.IsOfficialDownloadUrl(link.Target))
                        onboardingProblems.Add($"第 {i + 1} 步外链不在官方域名下：{link.Target}");
                }
            }

            var stepTitleOk = !string.IsNullOrWhiteSpace(OnbStepTitle.Text);
            var dotsOk = OnbDots.Children.Count == OnboardingGuide.StepCount;
            var buttonsOk = OnbNextBtn.Content is not null && OnbBackBtn.Content is not null;
            if (!stepTitleOk || !dotsOk || !buttonsOk || onboardingProblems.Count > 0)
            {
                results.Add($"FAIL  onboarding -> 渲染结果异常（标题={stepTitleOk} 圆点={OnbDots.Children.Count}/{OnboardingGuide.StepCount} 按钮={buttonsOk}"
                    + (onboardingProblems.Count > 0 ? $"，问题：{string.Join("；", onboardingProblems)}" : "") + "）");
            }
            else if (App.HeadlessErrors.Count > errorsBeforeOnboarding)
            {
                results.Add($"FAIL  onboarding -> 跳转过程报错 {App.HeadlessErrors.Count - errorsBeforeOnboarding} 处");
            }
            else
            {
                results.Add($"PASS  onboarding ({OnboardingGuide.StepCount} 步渲染 + 逐步跳转 + 进度点 + 显隐)");
            }
        }
        catch (Exception ex)
        {
            results.Add($"FAIL  onboarding -> {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            // 收起覆盖层，别影响后面的小窗自检
            OnboardingLayer.BeginAnimation(UIElement.OpacityProperty, null);
            OnboardingLayer.Visibility = Visibility.Collapsed;
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

        // 文案资源解析（V2.9.2）：XAML 上的文案走 {DynamicResource Loc.*}，只有 LocalizationService
        // 把这些键写进 Application.Resources 才解析得出来；解析失败是**静默**的（控件留空，
        // 编译期与运行期都不报错），所以这里在自检里钉住几个代表性控件的实际取值。
        try
        {
            var navText = NavHome.Content as string;
            var topSettingsText = (NavSettings.Content as string);
            var onboardingSkip = OnbSkipBtn.Content as string;
            var resolved = new List<string>();
            if (string.IsNullOrWhiteSpace(navText) || navText.StartsWith("Loc.", StringComparison.Ordinal))
                resolved.Add($"导航项（Content={navText ?? "null"}）");
            if (string.IsNullOrWhiteSpace(topSettingsText) || topSettingsText.StartsWith("Loc.", StringComparison.Ordinal))
                resolved.Add($"导航项设置（Content={topSettingsText ?? "null"}）");
            if (string.IsNullOrWhiteSpace(onboardingSkip) || onboardingSkip.StartsWith("Loc.", StringComparison.Ordinal))
                resolved.Add($"引导跳过按钮（Content={onboardingSkip ?? "null"}）");

            results.Add(resolved.Count == 0
                ? $"PASS  i18n      (文案资源解析：{navText})"
                : $"FAIL  i18n      -> 未解析的 {string.Join("；", resolved)}");
        }
        catch (Exception ex)
        {
            results.Add($"FAIL  i18n      -> {ex.GetType().Name}: {ex.Message}");
        }

        // 语言切换（V2.9.2）：走**生产路径**跑一遍中 → 英 → 中，断言文案真的跟着变、且往返后回到原状。
        // 这条覆盖的是「DynamicResource 文案在切换时是否即时生效 + 页面重放是否正常」，
        // 单测覆盖不到（需要真实 WPF 资源字典与窗口）。
        //
        // 注意：SetLanguage 会写偏好。这里先记下原值、结束时用同一个 API 还原，
        // 避免自检把开发机/CI 上的语言选择改掉（还原值就是默认的中文）。
        try
        {
            var originalLanguage = Strings.Current;
            var switchingProblems = new List<string>();

            AppServices.Localization.SetLanguage(Strings.EnUs);
            await WaitForUiIdleAsync().ConfigureAwait(true);
            if (!string.Equals(NavHome.Content as string, "Home", StringComparison.Ordinal))
                switchingProblems.Add($"切到英文后导航项仍是「{NavHome.Content}」");
            if (!string.Equals(Strings.Current, Strings.EnUs, StringComparison.Ordinal))
                switchingProblems.Add("Strings.Current 未切到英文");

            // 顶栏标题应当等于「当前路由」的英文标题 —— 自检结束时的当前路由不一定是首页
            // （引导最后一步把界面切到了搭建页），所以按当前路由取期望值，而不是写死首页标题。
            if (_meta.TryGetValue(_nav.CurrentRoute, out var currentMeta))
            {
                var expectedTitle = Strings.T(currentMeta.TitleKey);
                if (!string.Equals(PageTitle.Text, expectedTitle, StringComparison.Ordinal))
                    switchingProblems.Add($"切到英文后顶栏标题未跟随（{_nav.CurrentRoute}：显示 {PageTitle.Text}，期望 {expectedTitle}）");
            }

            AppServices.Localization.SetLanguage(originalLanguage);
            await WaitForUiIdleAsync().ConfigureAwait(true);
            if (!string.Equals(NavHome.Content as string, Strings.T("nav.home"), StringComparison.Ordinal))
                switchingProblems.Add($"切回中文后导航项未还原（{NavHome.Content}）");
            if (!string.Equals(Strings.Current, originalLanguage, StringComparison.Ordinal))
                switchingProblems.Add("语言未还原");

            results.Add(switchingProblems.Count == 0
                ? $"PASS  i18n-swap (中 → 英 → 中，文案与顶栏均即时跟随：{NavHome.Content})"
                : $"FAIL  i18n-swap -> {string.Join("；", switchingProblems)}");
        }
        catch (Exception ex)
        {
            results.Add($"FAIL  i18n-swap -> {ex.GetType().Name}: {ex.Message}");
        }

        var ok = results.Count(r => r.StartsWith("PASS"));
        var fail = results.Count - ok;
        var report = new StringBuilder();
        report.AppendLine($"OBS_Helper WPF 自检  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        // 总数不能只写 cases.Length：列表里还有引导覆盖层 / 小窗 / 文案解析三项非路由检查
        report.AppendLine($"检查项: {ok} PASS / {fail} FAIL  （路由 {cases.Length} 项 + 新手引导 + 迷你小窗 + 文案资源 + 语言切换）");
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
        // 静态事件持有本窗口引用：退出时退订，避免残留引用
        App.OnboardingResetRequested -= OnOnboardingResetRequested;
        AppServices.Localization.Changed -= OnLanguageChanged;
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
            AppServices.Tray.Notify(Strings.T("tray.minimizedTitle"), Strings.T("tray.minimizedMessage"));
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

    /// <summary>
    /// 托盘菜单「打开录像目录」（V2.9.4）：托盘线程只负责发事件，解析与打开都在 UI 线程做
    /// （目录解析会读 OBS 配置，返回结果要弹 Toast，都属于 UI 侧的事）。
    /// </summary>
    private void OnOpenRecordingFolderRequested()
        => Dispatcher.BeginInvoke(new Action(() => _ = OpenRecordingFolderAsync()));

    private async Task OpenRecordingFolderAsync()
    {
        try
        {
            var dir = await AppServices.RecordingTools.TryGetRecordingDirAsync().ConfigureAwait(true);
            var error = RecordingToolsService.OpenInExplorer(dir.Dir ?? "");
            if (error is not null) AppServices.Toast.Show(error, "error");
        }
        catch (Exception ex)
        {
            AppServices.Toast.Show(Strings.T("recording.dir.openFailed", ex.Message), "error");
        }
    }

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
            PageTitle.Text = Strings.T(meta.TitleKey);
            PageSubtitle.Text = Strings.T(meta.SubtitleKey);
            PageSubtitle.Visibility = string.IsNullOrEmpty(meta.SubtitleKey)
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

    // ------------------------------------------------------------ 语言切换（V2.9.2）

    /// <summary>
    /// 语言变化后把整窗刷新到位，四件事缺一不可：
    /// <list type="number">
    ///   <item>顶栏标题 / 副标题（存的是文案键，这里重新取一次）；</item>
    ///   <item>两枚连接徽章 —— 它们属于窗口 chrome、不在页面里，重建页面刷不到；</item>
    ///   <item>托盘菜单 —— 菜单项是 WinForms 对象、建在托盘线程上，必须让托盘服务自己重建；</item>
    ///   <item>当前页面 —— 用「原地导航」触发页面的 OnNavigatedToAsync，让各页按新语言重建内容。
    ///     这里刻意复用既有的页面生命周期，而不是给每个页面各写一套刷新逻辑。</item>
    /// </list>
    /// 纯 XAML 的文案（<c>{DynamicResource Loc.*}</c>）由 LocalizationService 写进
    /// <c>Application.Resources</c> 时已即时生效，不需要在这里处理。
    /// </summary>
    private void OnLanguageChanged(string oldLanguage, string newLanguage)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => OnLanguageChanged(oldLanguage, newLanguage)));
            return;
        }

        TopBadge.Refresh();
        SideBadge.Refresh();
        AppServices.Tray.RefreshLanguage();

        var route = _nav.CurrentRoute;
        if (string.IsNullOrEmpty(route)) return;

        // 原地重放：不进返回栈、不补历史，等价于「刷新当前页」
        _nav.Navigate(route, _nav.CurrentParameter, pushHistory: false);
    }

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

    /// <summary>Ctrl+F：引导覆盖层正开着时不响应，避免「隔着引导」跳页面。</summary>
    private void OnFindExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (IsOnboardingVisible) return;
        _nav.Navigate(Routes.Search);
    }

    /// <summary>Alt+←：同上，引导期间不响应返回。</summary>
    private void OnBrowseBackExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (IsOnboardingVisible) return;
        _nav.GoBack();
    }
}