using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using OBS_Helper.Wpf.Controls;
using OBS_Helper.Wpf.Models.Plugins;
using OBS_Helper.Wpf.Navigation;
using OBS_Helper.Wpf.Services;
using OBS_Helper.Wpf.Services.Dependencies;
using OBS_Helper.Wpf.Services.Plugins;
using OBS_Helper.Wpf.Services.Shell;
using OBS_Helper.Wpf.Services.Update;

namespace OBS_Helper.Wpf.Views;

/// <summary>
/// 插件广场（V2.2 重构）：目录数据来自 <see cref="PluginCatalogService"/>（知识库分离热更新通道，
/// 链接纠错 / 新插件上架无需发版），页面在目录版本变化时自动重建。
///
/// 本页能力：
/// <list type="bullet">
///   <item>本机已装插件体检（P0-1，只读扫描，标注「广场收录 / 未收录」）；</item>
///   <item>卡片直达 Releases 下载 + 最新版本角标（P1-1，缓存节流）；</item>
///   <item>AI 插件开销说明与实时性能预算提示（P1-2，联动系统监控）；</item>
///   <item>「关注」插件启动静默查新（P2-1，仅 Toast）；</item>
///   <item>路由参数定位：日志分析 / 模板页可带插件 id 跳转高亮（P0-2 / P2-2）。</item>
/// </list>
///
/// V3.0（F6）重做本页的输入与网络路径：57 条目录不能再用「每次输入全量重建卡片 + 每张卡都打一次
/// GitHub API」这套做法（匿名限额 60 次/小时）。现在搜索走防抖、卡片按 id 常驻复用、
/// 角标只查当前筛选结果的前若干张并按视口懒补。
/// </summary>
public partial class PluginsPage : UserControl, INavigationAware
{
    /// <summary>
    /// 官方 / 社区入口，放在分类列表之前（量小且稳定，保留在代码内）。
    ///
    /// V3.0（F5）：由 <c>static readonly</c> 改为**按需构造** —— 静态字段在类型初始化时就把文案冻住了，
    /// 切语言后这一块仍是旧语言。只有 3 条，重建代价可以忽略。
    /// </summary>
    private static (string Label, string Desc, string Url)[] Entries() => new[]
    {
        (Strings.T("plugin.link.forum.title"), Strings.T("plugin.link.forum.desc"), "https://obsproject.com/forum/plugins/"),
        (Strings.T("plugin.link.exeldro.title"), Strings.T("plugin.link.exeldro.desc"), "https://github.com/exeldro"),
        (Strings.T("plugin.link.occai.title"), Strings.T("plugin.link.occai.desc"), "https://github.com/occ-ai"),
    };

    /// <summary>
    /// StreamFX 迁移矩阵（B4）：常用功能 → 广场内的替代插件 id。
    /// 替代品均为维护活跃的单一职责插件，id 必须与 plugins.json 保持一致。
    /// V3.0（F5）：同样改为按需构造（文案不能冻在首次加载）。
    /// </summary>
    private static (string Usage, string PluginId)[] StreamFxMigrations() => new[]
    {
        (Strings.T("plugin.migrate.blur"), "composite-blur"),
        (Strings.T("plugin.migrate.masks"), "advanced-masks"),
        (Strings.T("plugin.migrate.transform"), "3d-effect"),
        (Strings.T("plugin.migrate.stroke"), "stroke-glow-shadow"),
        (Strings.T("plugin.migrate.retro"), "retro-effects"),
    };

    private PluginCatalogData _catalog = new();

    /// <summary>开源依赖中心（V3.4.1）：一键下载 / 一键配置。</summary>
    private DependencyService? _dependencies;

    /// <summary>本机插件管理（X1031Y2）：卸载 / 迁移 / 接管。</summary>
    private LocalPluginManageService? _localPluginManager;

    private LocalPluginManageService LocalPlugins
        => _localPluginManager ??= new LocalPluginManageService(AppServices.PluginState);

    private DependencyService Dependencies => _dependencies ??= new DependencyService(
        new SidecarDependencySource(AppServices.Sidecars),
        AppServices.Store.GetItem,
        (key, value) =>
        {
            if (string.IsNullOrWhiteSpace(value)) AppServices.Store.RemoveItem(key);
            else AppServices.Store.SetItem(key, value!);
        },
        ApplyDependencyPath,
        AppServices.Host.OpenExternalAsync);

    /// <summary>把“一键配置”选定的路径立刻接到使用方（持久化由 DependencyService 负责）。</summary>
    private static void ApplyDependencyPath(string id, string path)
    {
        if (string.Equals(id, "ffmpeg", StringComparison.OrdinalIgnoreCase))
            AppServices.Relay.SetFfmpegPath(path);
        else if (string.Equals(id, "rclone", StringComparison.OrdinalIgnoreCase))
            AppServices.ArchiveUpload.SetRclonePath(path);
        // whisper 由互动页启动字幕时读取设置键；mediamtx 直接复制进 sidecar 目录。
    }

    /// <summary>
    /// 把本机 OBS 版本注入目录条目（V3.0 / D9）。
    ///
    /// 兼容性结论是**条目 + 本机版本**的联合判定，所以本机版本要在渲染前写进条目；
    /// 未连接 OBS 时为空串，判定结果一律「未知」——不显示任何兼容性提示。
    /// </summary>
    private void InjectLocalObsVersion()
    {
        try
        {
            var local = AppServices.Obs.Profile.ObsVersion ?? "";
            foreach (var p in _catalog.Plugins) p.LocalObsVersion = local;
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Plugins", $"注入本机 OBS 版本失败（兼容性提示将不显示）：{ex.Message}");
        }
    }
    private string _builtVersion = "";
    /// <summary>静态区块是在哪种语言下搭起来的：换语言后要整块重建（V2.9.2）。</summary>
    private string _builtLang = "";
    private string _activeCategory = "all";

    /// <summary>搜索防抖（F6）：停止输入 300ms 后才重排列表，逐击不再重建 57 张卡。</summary>
    private readonly Debouncer _searchDebouncer = new(TimeSpan.FromMilliseconds(300));

    /// <summary>
    /// 一次渲染里立即查询角标的卡片数（F6）：只查当前筛选结果的前 N 张，其余保持「未查询」，
    /// 滚动到可视区附近再补（见 <see cref="RevealBadgesInViewport"/>）。
    /// </summary>
    private const int EagerBadgeCount = 12;

    /// <summary>按视口补查角标时的预取余量（像素）：卡片进入视口下方这一段就先查，滚到时角标已经在了。</summary>
    private const double BadgePrefetchPx = 400;

    // ---- 一键配置串行化：同一时间只允许一个下载 / 安装，避免重复落盘
    private bool _pluginInstallBusy;

    // ---- 本机体检状态
    private LocalPluginScanResult? _scan;
    private Task<LocalPluginScanResult>? _scanTask;
    private bool _healthExpanded;
    private bool _healthScanStarted;

    // ---- P1-2 性能预算提示（每次导航计算一次）
    private string? _aiBudgetHint;

    // ---- 路由参数定位
    private string? _highlightId;

    public PluginsPage()
    {
        InitializeComponent();

        // 角标按视口懒查：滚动事件在这里接（XAML 里没有可挂的事件名），
        // 事件源是本页自己的 ScrollViewer，实例被逐出时随之消失，不需要退订。
        PageScroller.ScrollChanged += OnPageScrolled;
    }

    public async Task OnNavigatedToAsync(object? parameter)
    {
        var data = AppServices.PluginCatalog.GetData();
        var versionChanged = !string.Equals(_builtVersion, data.Version, StringComparison.Ordinal)
            || !string.Equals(_builtLang, Strings.Current, StringComparison.Ordinal);
        _catalog = data;

        // 兼容性判定需要本机 OBS 版本（V3.0 / D9）：未连接时为空 → 判定为「未知」，
        // 界面不会因此误报不兼容（宁可不说，也不要说错）。
        InjectLocalObsVersion();

        if (versionChanged)
        {
            ResourceHost.Children.Clear();
            CategoryPanel.Children.Clear();
            ListHost.Children.Clear();
            // 目录换版 / 换语言：卡片池整批作废（条目内容与文案都变了），下次渲染按新目录重建
            _cardPool.Clear();
            _sectionTitles.Clear();
            _displayed.Clear();
            _renderedSignature = "";
            BuildResourceCards();
            BuildMigrationPanel();
            BuildCategoryChips();
            _builtVersion = data.Version ?? "";
            _builtLang = Strings.Current;
            // 目录换版后 chips 已重建为「全部」，分类状态同步复位，避免 UI 与实际过滤不一致
            _activeCategory = "all";
        }

        UpdateCatalogMeta();
        BuildDependencyPanel();

        // 路由参数：插件 id → 切到对应分类、清空筛选，渲染后滚动高亮（P0-2 / P2-2 联动入口）
        _highlightId = parameter as string;
        if (!string.IsNullOrEmpty(_highlightId))
        {
            var entry = PluginCatalogCore.FindById(_catalog, _highlightId);
            if (entry is not null)
            {
                _activeCategory = entry.Category;
                SyncCategoryChips();
                if (SearchBox.Text.Length > 0) SearchBox.Text = "";
            }
        }

        _aiBudgetHint = ComputeAiBudgetHint(AppServices.SystemMonitor.Latest);

        // 重新进入时把角标查询标记清掉：上一次可能整批撞上限流（失败缓存 5~10 分钟就过期），
        // 再进一次才有机会重试；命中成功缓存或失败冷却时都不会真的打网络，代价很低。
        foreach (var slot in _cardPool.Values) slot.BadgeRequested = false;

        RenderList();

        await EnsureScanAsync(force: false);

        // V3.0：只读缓存拿不到时，后台取**一次**快照再刷新提示 ——
        // 绝不 Start() 常驻采样（那会让 1 秒计时器活到进程退出，用户只是看了一眼插件列表）。
        await RefreshAiBudgetHintAsync();

        // V3.3.0：应用管理的已安装插件（installed.json）
        RefreshManagedList();

        // V3.5.0：GitHub 加速引导（用户点「不再提示」后隐藏）
        RefreshAcceleratorPanel();
    }

    /// <summary>
    /// 离开页面：取消尚未执行的防抖重排（F7 之后本页也可能被缓存逐出，
    /// 但即便留在缓存里，也没必要在用户已经看不见的时候重排一次列表）。
    /// </summary>
    public Task OnNavigatedFromAsync()
    {
        _searchDebouncer.Cancel();
        return Task.CompletedTask;
    }

    private async Task RefreshAiBudgetHintAsync()
    {
        if (AppServices.SystemMonitor.Latest is not null) return;
        try
        {
            var s = await Task.Run(() => AppServices.SystemMonitor.SampleOnce()).ConfigureAwait(true);
            var hint = ComputeAiBudgetHint(s);
            if (!string.Equals(hint, _aiBudgetHint, StringComparison.Ordinal))
            {
                _aiBudgetHint = hint;
                RefreshCardDynamicState();
            }
        }
        catch (Exception)
        {
            // 提示是锦上添花：取不到就不显示，不影响插件列表
        }
    }

    // ---------------------------------------------------------- GitHub 加速引导（V3.5.0）

    /// <summary>「不再提示」偏好键；只影响提示，不影响插件功能。</summary>
    private const string AcceleratorDismissKey = "plugin.githubAcceleratorDismissed";

    private void RefreshAcceleratorPanel()
    {
        var dismissed = string.Equals(AppServices.Store.GetItem(AcceleratorDismissKey), "1", StringComparison.Ordinal);
        AcceleratorPanel.Visibility = dismissed ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void OnDownloadAcceleratorClick(object sender, RoutedEventArgs e)
    {
        var ok = await AppServices.Host.OpenExternalAsync(ObsDownloadLinks.WattToolkitSite);
        AcceleratorStatusText.Text = ok
            ? ""
            : Strings.T("plugin.accelerator.failed", Strings.T("common.openBrowserFailed"));
    }

    private async void OnCheckGitHubClick(object sender, RoutedEventArgs e)
    {
        AcceleratorCheckButton.IsEnabled = false;
        AcceleratorStatusText.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        AcceleratorStatusText.Text = Strings.T("plugin.accelerator.checking");
        try
        {
            var (status, error) = await GitHubReachability.ProbeAsync().ConfigureAwait(true);
            AcceleratorStatusText.Text = status == GitHubReachabilityStatus.Failed
                ? Strings.T(GitHubReachability.MessageKey(status), error)
                : Strings.T(GitHubReachability.MessageKey(status));
            AcceleratorStatusText.SetResourceReference(
                TextBlock.ForegroundProperty,
                status == GitHubReachabilityStatus.Reachable ? "OkBrush" : "WarnBrush");
        }
        finally
        {
            AcceleratorCheckButton.IsEnabled = true;
        }
    }

    private void OnDismissAcceleratorClick(object sender, RoutedEventArgs e)
    {
        AppServices.Store.SetItem(AcceleratorDismissKey, "1");
        AcceleratorPanel.Visibility = Visibility.Collapsed;
    }

    // ---------------------------------------------------------- 目录元信息（V2.9）

    /// <summary>
    /// 显示当前生效的目录版本、复核日期与维护放缓条数。
    /// 数据源可能是「内置种子」也可能是热更新的外部文件，这里如实反映实际生效的那一份，
    /// 用户据此就能判断插件广场有没有更新过。
    /// </summary>
    private void UpdateCatalogMeta()
    {
        try
        {
            var slow = _catalog.Plugins.Count(p => p.IsMaintenanceSlow);
            var parts = new List<string>
            {
                Strings.T("plugin.catalog.version", _catalog.Version),
                Strings.T("plugin.catalog.count", _catalog.Plugins.Count)
            };
            if (!string.IsNullOrWhiteSpace(_catalog.Updated)) parts.Add(Strings.T("plugin.catalog.reviewed", _catalog.Updated));
            if (slow > 0) parts.Add(Strings.T("plugin.catalog.slow", slow));
            if (AppServices.PluginCatalog.DataSource == "external") parts.Add(Strings.T("plugin.catalog.hotUpdated"));

            CatalogMetaText.Text = string.Join(" · ", parts);
        }
        catch (Exception)
        {
            // 元信息属锦上添花：任何异常都不该影响插件列表本身
            CatalogMetaText.Text = "";
        }
    }

    // ---------------------------------------------------------- 官方入口

    private void BuildResourceCards()
    {
        foreach (var (label, desc, url) in Entries())
        {
            var titleText = new TextBlock
            {
                Text = label,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            };
            titleText.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSm");
            titleText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

            var descText = new TextBlock
            {
                Text = desc,
                Margin = new Thickness(0, 3, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
            descText.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeXs");
            descText.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");

            var body = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            body.Children.Add(titleText);
            body.Children.Add(descText);

            var chevron = new TextBlock { Text = "↗", VerticalAlignment = VerticalAlignment.Center };
            chevron.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeLg");
            chevron.SetResourceReference(TextBlock.ForegroundProperty, "BrandBrush");

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(body, 0);
            Grid.SetColumn(chevron, 1);
            grid.Children.Add(body);
            grid.Children.Add(chevron);

            var button = new Button
            {
                Style = (Style)FindResource("CardButton"),
                Content = grid,
                Tag = url,
                MinWidth = 240,
                Margin = new Thickness(0, 0, 10, 10)
            };
            button.Click += OnOpenLinkClick;

            ResourceHost.Children.Add(button);
        }
    }

    // ---------------------------------------------------------- StreamFX 迁移矩阵（B4）

    /// <summary>
    /// 构建「从 StreamFX 平滑迁移」面板。目录里找不到对应替代插件时自动隐藏整块，
    /// 避免外置目录热更新后出现死链接。
    /// </summary>
    private void BuildMigrationPanel()
    {
        MigrationList.Children.Clear();

        var rows = 0;
        foreach (var (usage, pluginId) in StreamFxMigrations())
        {
            var entry = PluginCatalogCore.FindById(_catalog, pluginId);
            if (entry is null) continue;
            rows++;

            var usageText = new TextBlock
            {
                Text = usage,
                VerticalAlignment = VerticalAlignment.Center
            };
            usageText.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSm");
            usageText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

            var arrow = new TextBlock
            {
                Text = "→",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            arrow.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSm");
            arrow.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");

            var linkText = new TextBlock { Text = entry.Name, FontWeight = FontWeights.SemiBold };
            linkText.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSm");
            linkText.SetResourceReference(TextBlock.ForegroundProperty, "BrandBrush");

            var link = new Button
            {
                Style = (Style)TryFindResource("LinkButton"),
                Content = linkText,
                Tag = entry.Id,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = Strings.T("plugin.health.locateSubstituteTip")
            };
            link.Click += OnLocateFromHealthClick;

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
            row.Children.Add(usageText);
            row.Children.Add(arrow);
            row.Children.Add(link);

            MigrationList.Children.Add(row);
        }

        MigrationPanel.Visibility = rows > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------------------------------------------------------- 分类 chips

    private void BuildCategoryChips()
    {
        var allChip = new RadioButton
        {
            Style = (Style)FindResource("SegmentButton"),
            GroupName = "PluginCategory",
            Content = Strings.T("plugin.filterAll"),
            Tag = "all",
            IsChecked = true
        };
        allChip.Checked += OnCategoryChecked;
        CategoryPanel.Children.Add(allChip);

        foreach (var category in _catalog.Categories)
        {
            var chip = new RadioButton
            {
                Style = (Style)FindResource("SegmentButton"),
                GroupName = "PluginCategory",
                // 分类不渲染知识库自带的图标字段：本页统一纯文字风格，也避免外部目录数据把 emoji 带回界面
                Content = category.Label,
                Tag = category.Key
            };
            chip.Checked += OnCategoryChecked;
            CategoryPanel.Children.Add(chip);
        }
    }

    private void SyncCategoryChips()
    {
        foreach (var chip in CategoryPanel.Children.OfType<RadioButton>())
        {
            if (chip.Tag is string key && string.Equals(key, _activeCategory, StringComparison.Ordinal))
            {
                chip.IsChecked = true;
                break;
            }
        }
    }

    private void OnCategoryChecked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton chip || chip.Tag is not string key) return;
        if (key == _activeCategory) return;

        _activeCategory = key;
        RenderList();
    }

    /// <summary>
    /// 边打边筛（F6）：走 300ms 防抖，连续输入只在停顿后重排一次。
    /// 原来每次击键都同步全量重建 57 张卡，并把同一批角标请求重发一遍。
    /// </summary>
    private void OnSearchTextChanged(object sender, TextChangedEventArgs e) => _searchDebouncer.Debounce(RenderList);

    /// <summary>
    /// 手动检查插件目录更新（V3.3.0）：走与知识库相同的热更新通道；
    /// 有新版本就写盘并立即重建列表，没有则给出「已是最新」。
    /// </summary>
    private async void OnCatalogUpdateClick(object sender, RoutedEventArgs e)
    {
        CatalogUpdateButton.IsEnabled = false;
        try
        {
            var (updated, version, message) = await AppServices.Kb.RefreshPluginsAsync(manual: true);
            if (updated)
            {
                AppServices.PluginCatalog.Reload();
                _builtVersion = "";
                _builtLang = "";
                await OnNavigatedToAsync(null);
                AppServices.Toast.Show(Strings.T("plugin.catalog.updated", version ?? ""), "ok");
            }
            else
            {
                AppServices.Toast.Show(message ?? Strings.T("plugin.catalog.upToDate"), "info");
            }
        }
        catch (Exception ex)
        {
            AppServices.Toast.Show(Strings.T("plugin.catalog.failed", ex.Message), "danger");
        }
        finally
        {
            CatalogUpdateButton.IsEnabled = true;
        }
    }

    // ---------------------------------------------------------- 本机体检（P0-1）

    private async Task EnsureScanAsync(bool force)
    {
        try
        {
            if (!force && (_scan is not null || _healthScanStarted)) return;
            _healthScanStarted = true;

            SetHealthBusy(true);
            _scanTask = Task.Run(() => AppServices.PluginScanner.Scan());
            _scan = await _scanTask.ConfigureAwait(true);

            // 把扫描结果与广场目录对上（回填 CatalogId）
            if (_scan is not null)
            {
                foreach (var p in _scan.Plugins)
                {
                    p.CatalogId = PluginCatalogCore.MatchByDll(_catalog, p.FileName)?.Id;
                }
            }

            RenderHealth();
            // 卡片上的「已安装」标记跟着刷新。这里只更新徽标与开销行，
            // 不再整表重排（F6 卡片复用后没有重建的必要，重排还会把滚动位置顶回顶部）
            RefreshCardDynamicState();
        }
        catch (Exception)
        {
            // 扫描失败不打扰：面板保持折叠
        }
        finally
        {
            SetHealthBusy(false);
        }
    }

    private void SetHealthBusy(bool busy)
    {
        HealthRefreshButton.IsEnabled = !busy;
        HealthRefreshButton.Content = busy ? Strings.T("plugin.health.scanning") : Strings.T("plugin.health.rescan");
    }

    private void OnRescanClick(object sender, RoutedEventArgs e) => _ = EnsureScanAsync(force: true);

    // ============================================================ 一键安装与管理（V3.3.0）

    /// <summary>按 DLL 全路径找本机检测结果（列表行只保留路径，操作需要原始插件对象）。</summary>
    private InstalledPluginFile? FindLocalPlugin(string path)
        => _scan?.Plugins.FirstOrDefault(p => string.Equals(p.FullPath, path, StringComparison.OrdinalIgnoreCase));

    private void OnOpenPluginFolderClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string path } || string.IsNullOrWhiteSpace(path)) return;
        var dir = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir) || !AppServices.Host.OpenFolder(dir))
            AppServices.Toast.Show(Strings.T("plugin.managed.openFailed"), "warn");
    }

    /// <summary>接管管理：只登记，不移动文件；之后可用广场更新 / 回滚 / 卸载。</summary>
    private async Task AdoptLocalPluginAsync(InstalledPluginFile plugin)
    {
        if (PluginInstallService.IsObsRunning())
        {
            AppServices.Toast.Show(InstallError("PLUGIN_OBS_RUNNING"), "warn");
            return;
        }

        if (!ConfirmDialog.Show(
                Strings.T("plugin.managed.adoptConfirmTitle"),
                Strings.T("plugin.managed.adoptConfirmMessage", plugin.Stem, plugin.FullPath),
                Strings.T("plugin.managed.adopt"), Strings.T("common.cancel"),
                danger: false, icon: "📋"))
            return;

        var result = LocalPlugins.Adopt(plugin, _catalog);
        if (result.Ok)
        {
            AppServices.Toast.Show(Strings.T("plugin.managed.adoptDone", plugin.Stem), "ok");
            RefreshManagedList();
        }
        else
        {
            AppServices.Toast.Show(InstallError(result.Code), "warn");
        }
    }

    /// <summary>迁移：用户级 ⇄ 全局级，或 OBS 安装目录 → 用户级。</summary>
    private async Task MigrateLocalPluginAsync(InstalledPluginFile plugin)
    {
        if (PluginInstallService.IsObsRunning())
        {
            AppServices.Toast.Show(InstallError("PLUGIN_OBS_RUNNING"), "warn");
            return;
        }

        var unit = LocalPluginManageService.Describe(plugin);
        var target = LocalPlugins.MigrationTargetDirectory(plugin);
        if (unit is null || string.IsNullOrWhiteSpace(target))
        {
            AppServices.Toast.Show(InstallError("PLUGIN_LOCAL_NO_TARGET"), "warn");
            return;
        }

        if (!ConfirmDialog.Show(
                Strings.T("plugin.managed.migrateConfirmTitle"),
                Strings.T("plugin.managed.migrateConfirmMessage", plugin.Stem, unit.PluginDirectory, target),
                Strings.T("plugin.managed.migrate"), Strings.T("common.cancel"),
                danger: false, icon: "📁"))
            return;

        var result = await LocalPlugins.MigrateAsync(plugin, CancellationToken.None);
        if (result.Ok)
        {
            AppServices.Toast.Show(Strings.T("plugin.managed.migrateDone", plugin.Stem, result.Path), "ok");
            await EnsureScanAsync(force: true);
            RefreshManagedList();
        }
        else
        {
            AppServices.Toast.Show(InstallError(result.Code), "warn");
        }
    }

    /// <summary>卸载本机插件：整目录移进本应用备份区，不硬删。</summary>
    private async Task UninstallLocalPluginAsync(InstalledPluginFile plugin)
    {
        if (PluginInstallService.IsObsRunning())
        {
            AppServices.Toast.Show(InstallError("PLUGIN_OBS_RUNNING"), "warn");
            return;
        }

        var unit = LocalPluginManageService.Describe(plugin);
        var directory = unit?.PluginDirectory ?? Path.GetDirectoryName(plugin.FullPath) ?? plugin.FullPath;
        if (!ConfirmDialog.Show(
                Strings.T("plugin.managed.uninstallLocalTitle"),
                Strings.T("plugin.managed.uninstallLocalMessage", plugin.Stem, directory),
                Strings.T("plugin.managed.uninstall"), Strings.T("common.cancel"),
                danger: true, icon: "⚠", irreversible: true))
            return;

        var result = await LocalPlugins.UninstallAsync(plugin, CancellationToken.None);
        if (result.Ok)
        {
            AppServices.Toast.Show(Strings.T("plugin.managed.uninstallLocalDone", plugin.Stem, result.Path), "ok");
            await EnsureScanAsync(force: true);
            RefreshManagedList();
        }
        else
        {
            AppServices.Toast.Show(InstallError(result.Code), "warn");
        }
    }

    /// <summary>已安装（本应用管理 + 本机检测）列表：版本、路径、回滚、卸载、迁移与接管。</summary>
    private void RefreshManagedList()
    {
        ManagedHost.Children.Clear();

        IReadOnlyList<PluginInstallEntry> installed;
        try
        {
            installed = AppServices.PluginInstall.Installed;
        }
        catch (Exception ex)
        {
            ManagedHost.Children.Add(InstallLine(Strings.T("plugin.managed.readFailed", ex.Message), "WarnBrush"));
            ManagedEmptyText.Visibility = Visibility.Collapsed;
            return;
        }

        // V3.5.0：合并「本应用管理」与「本机检测」两套数据源。
        // 旧实现只看 installed.json，用户用 OBS 自己装的插件完全不可见，
        // 页面却显示「还没有插件」——与事实相反（用户反馈截图）。
        var rows = PluginManagedListCore.Merge(
            installed,
            _scan?.Plugins ?? Enumerable.Empty<InstalledPluginFile>(),
            backupCount: id => AppServices.PluginInstall.Store.ListBackups(id).Count);

        if (rows.Count == 0)
        {
            ManagedEmptyText.Text = _scan is not null && !_scan.ObsInstallFound
                ? Strings.T("plugin.managed.emptyNoObs")
                : Strings.T("plugin.managed.emptyNoPlugins");
            ManagedEmptyText.Visibility = Visibility.Visible;
            return;
        }

        ManagedEmptyText.Visibility = Visibility.Collapsed;

        var managedCount = rows.Count(r => r.Kind == ManagedPluginKind.Managed);
        var localCount = rows.Count - managedCount;
        var summary = new TextBlock
        {
            Text = Strings.T("plugin.managed.summary", managedCount, localCount),
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap
        };
        summary.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeXs");
        summary.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        ManagedHost.Children.Add(summary);

        foreach (var row in rows)
        {
            var box = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

            var title = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(row.Version) ? row.Name : $"{row.Name}  ·  v{row.Version}",
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            };
            title.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSm");
            title.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            box.Children.Add(title);

            var badges = new WrapPanel { Margin = new Thickness(0, 3, 0, 0) };
            badges.Children.Add(Badge(
                Strings.T(row.Kind == ManagedPluginKind.Managed ? "plugin.managed.managedBadge" : "plugin.managed.localBadge"),
                row.Kind == ManagedPluginKind.Managed ? "BrandSoftBrush" : "Surface3Brush",
                row.Kind == ManagedPluginKind.Managed ? "BrandBrush" : "MutedBrush"));
            if (row.Kind == ManagedPluginKind.Managed && !row.Curated)
                badges.Children.Add(Badge(Strings.T("plugin.managed.unverifiedBadge"), "WarnSoftBrush", "WarnBrush"));
            if (row.DirectoryMissing)
                badges.Children.Add(Badge(Strings.T("plugin.managed.directoryMissing"), "WarnSoftBrush", "WarnBrush"));
            box.Children.Add(badges);

            var path = new TextBlock
            {
                Text = row.Path,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            };
            path.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeXs");
            path.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            box.Children.Add(path);

            if (row.Kind == ManagedPluginKind.Managed)
            {
                var metaParts = new List<string>();
                if (!string.IsNullOrWhiteSpace(row.SourceRepo)) metaParts.Add(row.SourceRepo);
                if (!string.IsNullOrWhiteSpace(row.Sha256))
                    metaParts.Add(Strings.T("plugin.managed.sha", row.Sha256[..Math.Min(12, row.Sha256.Length)]));
                if (metaParts.Count > 0)
                {
                    var meta = new TextBlock
                    {
                        Text = string.Join(" · ", metaParts),
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 2, 0, 0)
                    };
                    meta.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeXs");
                    meta.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                    box.Children.Add(meta);
                }

                var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
                var rollback = new Button
                {
                    Content = Strings.T("plugin.managed.rollback"),
                    Style = (Style)Application.Current.Resources["SecondaryButton"],
                    IsEnabled = row.CanRollback
                };
                rollback.Click += async (_, _) =>
                {
                    var entry = AppServices.PluginInstall.Store.Find(row.Id);
                    if (entry is not null) await RollbackAsync(entry);
                };
                actions.Children.Add(rollback);

                var uninstall = new Button
                {
                    Content = Strings.T("plugin.managed.uninstall"),
                    Style = (Style)Application.Current.Resources["GhostButton"],
                    Margin = new Thickness(10, 0, 0, 0),
                    IsEnabled = !row.DirectoryMissing
                };
                uninstall.Click += async (_, _) =>
                {
                    var entry = AppServices.PluginInstall.Store.Find(row.Id);
                    if (entry is not null) await UninstallAsync(entry);
                };
                actions.Children.Add(uninstall);

                var openManagedFolder = new Button
                {
                    Content = Strings.T("plugin.managed.openFolder"),
                    Style = (Style)Application.Current.Resources["LinkButton"],
                    Margin = new Thickness(10, 0, 0, 0),
                    Tag = row.Path
                };
                openManagedFolder.Click += OnOpenPluginFolderClick;
                actions.Children.Add(openManagedFolder);
                box.Children.Add(actions);
            }
            else
            {
                var hint = new TextBlock
                {
                    Text = Strings.T("plugin.managed.localHint"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0)
                };
                hint.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeXs");
                hint.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                box.Children.Add(hint);

                // X1031Y2：本机检测不再只读。打开目录 / 接管管理 / 迁移 / 卸载（先备份）。
                var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };

                var openFolder = new Button
                {
                    Content = Strings.T("plugin.managed.openFolder"),
                    Style = (Style)Application.Current.Resources["LinkButton"],
                    Tag = row.Path
                };
                openFolder.Click += OnOpenPluginFolderClick;
                actions.Children.Add(openFolder);

                var local = FindLocalPlugin(row.Path);
                if (local is not null)
                {
                    var adopt = new Button
                    {
                        Content = Strings.T("plugin.managed.adopt"),
                        Style = (Style)Application.Current.Resources["SecondaryButton"],
                        Margin = new Thickness(10, 0, 0, 0),
                        Tag = local,
                        ToolTip = Strings.T("plugin.managed.adoptTip")
                    };
                    adopt.Click += async (_, _) => await AdoptLocalPluginAsync(local);
                    actions.Children.Add(adopt);

                    var migrateScope = LocalPlugins.MigrationTargetScope(local);
                    if (migrateScope is not null)
                    {
                        var migrate = new Button
                        {
                            Content = Strings.T("plugin.managed.migrate." + migrateScope),
                            Style = (Style)Application.Current.Resources["GhostButton"],
                            Margin = new Thickness(8, 0, 0, 0),
                            Tag = local,
                            ToolTip = Strings.T("plugin.managed.migrateTip")
                        };
                        migrate.Click += async (_, _) => await MigrateLocalPluginAsync(local);
                        actions.Children.Add(migrate);
                    }

                    var remove = new Button
                    {
                        Content = Strings.T("plugin.managed.uninstall"),
                        Style = (Style)Application.Current.Resources["GhostButton"],
                        Margin = new Thickness(8, 0, 0, 0),
                        Tag = local,
                        ToolTip = Strings.T("plugin.managed.uninstallLocalTip")
                    };
                    remove.Click += async (_, _) => await UninstallLocalPluginAsync(local);
                    actions.Children.Add(remove);
                }

                box.Children.Add(actions);
            }

            ManagedHost.Children.Add(box);
        }
    }

    /// <summary>紧凑状态徽标（本应用管理 / 本机检测 / 未验证 / 目录缺失）。</summary>
    private static Border Badge(string text, string backgroundKey, string foregroundKey)
    {
        var tb = new TextBlock { Text = text };
        tb.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeXs");
        tb.SetResourceReference(TextBlock.ForegroundProperty, foregroundKey);
        var border = new Border
        {
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(7, 2, 7, 2),
            Margin = new Thickness(0, 0, 6, 0),
            Child = tb
        };
        border.SetResourceReference(Border.BackgroundProperty, backgroundKey);
        border.SetResourceReference(Border.BorderBrushProperty, foregroundKey);
        border.BorderThickness = new Thickness(1);
        return border;
    }

    private async Task RollbackAsync(PluginInstallEntry entry)
    {
        if (!ConfirmDialog.Show(
                Strings.T("plugin.managed.rollbackTitle"),
                Strings.T("plugin.managed.rollbackConfirm", entry.Name, entry.Version),
                Strings.T("plugin.managed.rollback"), Strings.T("common.cancel"), danger: true))
            return;

        var result = await AppServices.PluginInstall.RollbackAsync(entry.Id, CancellationToken.None);
        if (result.Ok)
        {
            AppServices.Toast.Show(Strings.T("plugin.managed.rollbackDone", entry.Name), "ok");
            RefreshManagedList();
        }
        else
        {
            AppServices.Toast.Show(InstallError(result.ErrorCode), "danger");
        }
    }

    private async Task UninstallAsync(PluginInstallEntry entry)
    {
        if (!ConfirmDialog.Show(
                Strings.T("plugin.managed.uninstallTitle"),
                Strings.T("plugin.managed.uninstallConfirm", entry.Name, entry.InstallDirectory),
                Strings.T("plugin.managed.uninstall"), Strings.T("common.cancel"), danger: true))
            return;

        var result = await AppServices.PluginInstall.UninstallAsync(entry.Id, CancellationToken.None);
        if (result.Ok)
        {
            AppServices.Toast.Show(Strings.T("plugin.managed.uninstallDone", entry.Name), "ok");
            RefreshManagedList();
        }
        else
        {
            AppServices.Toast.Show(InstallError(result.ErrorCode), "danger");
        }
    }

    /// <summary>查询 owner/repo 的最新 Release，挑出可安装资产。</summary>
    private async void OnQueryRepoClick(object sender, RoutedEventArgs e)
        => await QueryRepoAsync(RepoBox.Text);

    /// <summary>查询某个 owner/repo 的最新 Release 并给出安装入口（仓库输入框与 GitHub 搜索结果共用）。</summary>
    private async Task QueryRepoAsync(string input)
    {
        AssetHost.Children.Clear();
        RepoStatusText.Text = "";

        if (!PluginInstallCore.TryParseOwnerRepo(input, out var slug))
        {
            RepoStatusText.Text = Strings.T("plugin.github.badRepo");
            return;
        }

        RepoQueryButton.IsEnabled = false;
        RepoStatusText.Text = Strings.T("plugin.github.querying", slug);
        try
        {
            var (version, assets, error) = await AppServices.PluginSearch
                .GetLatestReleaseAsync(slug, CancellationToken.None);

            if (error.Length > 0)
            {
                RepoStatusText.Text = NetError(error);
                return;
            }

            var asset = PluginInstallCore.SelectAsset(assets);
            if (asset is null)
            {
                RepoStatusText.Text = Strings.T("plugin.github.noAsset", slug);
                return;
            }

            RepoStatusText.Text = Strings.T("plugin.github.found", slug, version, asset.Name,
                Math.Max(1, asset.Size / 1024 / 1024));

            var install = new Button
            {
                Content = Strings.T("plugin.github.install", asset.Name),
                Style = (Style)Application.Current.Resources["PrimaryButton"]
            };
            install.Click += async (_, _) => await InstallAssetAsync(
                slug, slug, version, asset, curated: false, sourceRepo: slug, networkConfirmed: false,
                status: (text, brush) =>
                {
                    RepoStatusText.Text = text;
                    RepoStatusText.SetResourceReference(TextBlock.ForegroundProperty, brush);
                });
            AssetHost.Children.Add(install);
        }
        catch (Exception ex)
        {
            RepoStatusText.Text = Strings.T("plugin.github.queryFailed", ex.Message);
        }
        finally
        {
            RepoQueryButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// GitHub 原生搜索（V3.3.0）：搜 <c>topic:obs-plugin</c>，结果可以直接「查看最新版本」→ 安装。
    /// 搜索 API 额度低（10 次/分钟），因此只在点击时请求，服务层自带 5 分钟缓存与 60 秒冷却。
    /// </summary>
    private async void OnGitHubSearchClick(object sender, RoutedEventArgs e)
    {
        var query = GitHubSearchBox.Text.Trim();
        GitHubResultsHost.Children.Clear();
        GitHubSearchStatusText.Text = Strings.T("plugin.github.searching", query);
        GitHubSearchButton.IsEnabled = false;
        try
        {
            var (repos, error) = await AppServices.PluginSearch.SearchAsync(query, CancellationToken.None);
            if (error.Length > 0)
            {
                GitHubSearchStatusText.Text = NetError(error);
                return;
            }

            if (repos.Count == 0)
            {
                GitHubSearchStatusText.Text = Strings.T("plugin.github.searchEmpty", query);
                return;
            }

            GitHubSearchStatusText.Text = Strings.T("plugin.github.searchCount", repos.Count);
            foreach (var repo in repos)
                GitHubResultsHost.Children.Add(BuildGitHubResultRow(repo));
        }
        catch (Exception ex)
        {
            GitHubSearchStatusText.Text = Strings.T("plugin.github.searchFailed", ex.Message);
        }
        finally
        {
            GitHubSearchButton.IsEnabled = true;
        }
    }

    private FrameworkElement BuildGitHubResultRow(GitHubRepoResult repo)
    {
        var box = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };

        var title = new TextBlock
        {
            Text = repo.FullName,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
        title.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSm");
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        box.Children.Add(title);

        var meta = new TextBlock
        {
            Text = Strings.T("plugin.github.repoMeta",
                repo.Stars, string.IsNullOrWhiteSpace(repo.License) ? Strings.T("common.unknown") : repo.License,
                repo.PushedAt.Length >= 10 ? repo.PushedAt[..10] : repo.PushedAt),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0)
        };
        meta.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeXs");
        meta.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        box.Children.Add(meta);

        if (!string.IsNullOrWhiteSpace(repo.Description))
        {
            var desc = new TextBlock
            {
                Text = repo.Description,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 0)
            };
            desc.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSm");
            desc.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            box.Children.Add(desc);
        }

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };

        var view = new Button
        {
            Content = Strings.T("plugin.github.viewRelease"),
            Style = (Style)Application.Current.Resources["SecondaryButton"]
        };
        view.Click += async (_, _) =>
        {
            RepoBox.Text = repo.FullName;
            await QueryRepoAsync(repo.FullName);
        };
        actions.Children.Add(view);

        if (!string.IsNullOrWhiteSpace(repo.HtmlUrl))
        {
            var open = new Button
            {
                Content = Strings.T("plugin.github.openRepo"),
                Style = (Style)Application.Current.Resources["LinkButton"],
                Margin = new Thickness(10, 0, 0, 0),
                Tag = repo.HtmlUrl
            };
            open.Click += async (_, _) => await AppServices.Host.OpenExternalAsync(repo.HtmlUrl);
            actions.Children.Add(open);
        }

        box.Children.Add(actions);
        return box;
    }

    private async Task InstallAssetAsync(
        string id, string name, string version, PluginReleaseAsset asset,
        bool curated, string sourceRepo, bool networkConfirmed,
        Action<string, string>? status = null)
    {
        if (PluginInstallService.IsObsRunning())
        {
            status?.Invoke(InstallError("PLUGIN_OBS_RUNNING"), "WarnBrush");
            AppServices.Toast.Show(InstallError("PLUGIN_OBS_RUNNING"), "warn");
            return;
        }

        // 网络预检：从 GitHub 直接拉取前，让用户确认已开加速器或网络可用（X1031Y2）。
        if (!networkConfirmed && !ConfirmNetwork(Strings.T("plugin.network.actionPackage", name)))
            return;

        if (curated)
        {
            if (!ConfirmDialog.Show(
                    Strings.T("plugin.install.curatedConfirmTitle"),
                    Strings.T("plugin.install.curatedConfirmMessage", name, version, asset.Name),
                    Strings.T("plugin.install.curatedConfirmOk"), Strings.T("common.cancel"),
                    danger: false, icon: "🧩"))
                return;
        }
        else
        {
            // 任意仓库 = 未经验证的第三方代码：强警告 + 明确确认，不做「静默安装」
            if (!ConfirmDialog.Show(
                    Strings.T("plugin.github.confirmTitle"),
                    Strings.T("plugin.github.confirmMessage", sourceRepo, version, asset.Name),
                    Strings.T("plugin.github.confirmOk"), Strings.T("common.cancel"),
                    danger: true, icon: "⚠", irreversible: true))
                return;
        }

        var request = new PluginInstallRequest
        {
            Id = PluginInstallCore.SafeSlug(id.Replace('/', '-')),
            Name = name,
            Version = version,
            SourceRepo = sourceRepo,
            Asset = asset,
            Scope = "user",
            Curated = curated,
            ExpectedSha256 = asset.Digest
        };

        var progress = new Progress<PluginInstallProgress>(p =>
            status?.Invoke(Strings.T("plugin.install.stage." + p.Stage), "MutedBrush"));

        status?.Invoke(Strings.T("plugin.github.installing"), "MutedBrush");
        var result = await AppServices.PluginInstall.InstallAsync(request, progress, CancellationToken.None);

        if (result.Ok)
        {
            var done = Strings.T("plugin.github.installed", name, version);
            status?.Invoke(done, "OkBrush");
            AppServices.Toast.Show(done, "ok");
            await EnsureScanAsync(force: true);
            RefreshManagedList();
        }
        else
        {
            var error = InstallError(result.ErrorCode);
            status?.Invoke(error, "DangerBrush");
            AppServices.Toast.Show(error, "danger");
        }
    }

    /// <summary>卡片「一键配置」：查询最新 Release → 选 Windows 包 → 下载校验 → 安装。</summary>
    private async void OnOneClickInstallClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button { Tag: PluginEntry plugin }) return;
        await OneClickInstallAsync(plugin);
    }

    private async Task OneClickInstallAsync(PluginEntry plugin)
    {
        if (_pluginInstallBusy)
        {
            AppServices.Toast.Show(Strings.T("plugin.oneClick.busy"), "warn");
            return;
        }

        if (PluginInstallService.IsObsRunning())
        {
            AppServices.Toast.Show(InstallError("PLUGIN_OBS_RUNNING"), "warn");
            return;
        }

        if (string.IsNullOrWhiteSpace(plugin.Repo))
        {
            AppServices.Toast.Show(Strings.T("plugin.oneClick.noRepo"), "warn");
            return;
        }

        var slot = _cardPool.TryGetValue(plugin.Id, out var found) ? found : null;
        void Status(string text, string brush)
        {
            if (slot is null) return;
            slot.StatusText.Text = text;
            slot.StatusText.Visibility = Visibility.Visible;
            slot.StatusText.SetResourceReference(TextBlock.ForegroundProperty, brush);
        }

        // 网络预检先于任何 GitHub 请求：用户点「一键配置」之前就被告知可以开加速器。
        if (!ConfirmNetwork(Strings.T("plugin.network.actionPlugin", plugin.Name))) return;

        _pluginInstallBusy = true;
        if (slot is not null) slot.OneClickButton.IsEnabled = false;
        try
        {
            Status(Strings.T("plugin.github.querying", plugin.Repo), "MutedBrush");
            var (version, assets, error) = await AppServices.PluginSearch
                .GetLatestReleaseAsync(plugin.Repo, CancellationToken.None);

            if (error.Length > 0)
            {
                Status(NetError(error), "DangerBrush");
                ShowAcceleratorHint();
                return;
            }

            var asset = PluginInstallCore.SelectAsset(assets);
            if (asset is null)
            {
                Status(Strings.T("plugin.github.noAsset", plugin.Repo), "WarnBrush");
                return;
            }

            await InstallAssetAsync(plugin.Id, plugin.Name, version, asset,
                curated: true, sourceRepo: plugin.Repo, networkConfirmed: true, status: Status);
        }
        catch (Exception ex)
        {
            Status(Strings.T("plugin.github.queryFailed", ex.Message), "DangerBrush");
        }
        finally
        {
            _pluginInstallBusy = false;
            if (slot is not null)
            {
                slot.OneClickButton.IsEnabled = true;
                slot.OneClickButton.Content = Strings.T("plugin.oneClick");
            }
        }
    }

    /// <summary>网络预检文案：把「先开 Steam++ / 确认网络」放在真正下载之前。</summary>
    private static bool ConfirmNetwork(string action)
        => ConfirmDialog.Show(
            Strings.T("plugin.network.title"),
            Strings.T("plugin.network.message", action),
            Strings.T("plugin.network.confirm"), Strings.T("common.cancel"),
            danger: false, icon: "🌐");

    /// <summary>网络失败时把加速引导重新露出来，并滚到用户看得见的位置。</summary>
    private void ShowAcceleratorHint()
    {
        RefreshAcceleratorPanel();
        AcceleratorPanel.Visibility = Visibility.Visible;
        try { AcceleratorPanel.BringIntoView(); } catch (Exception) { }
    }

    /// <summary>仓库输入框按回车 = 一键配置，不用先点查询再点安装。</summary>
    private async void OnRepoBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await RepoOneClickAsync();
    }

    private async void OnRepoOneClick(object sender, RoutedEventArgs e) => await RepoOneClickAsync();

    /// <summary>粘贴 owner/repo 或完整仓库地址 → 直接在应用内拉取并安装最新 Release。</summary>
    private async Task RepoOneClickAsync()
    {
        AssetHost.Children.Clear();
        RepoStatusText.Text = "";

        if (!PluginInstallCore.TryParseOwnerRepo(RepoBox.Text, out var slug))
        {
            RepoStatusText.Text = Strings.T("plugin.github.badRepo");
            return;
        }

        if (_pluginInstallBusy)
        {
            AppServices.Toast.Show(Strings.T("plugin.oneClick.busy"), "warn");
            return;
        }

        if (!ConfirmNetwork(Strings.T("plugin.network.actionRepo", slug))) return;

        _pluginInstallBusy = true;
        RepoOneClickButton.IsEnabled = false;
        try
        {
            RepoStatusText.Text = Strings.T("plugin.github.querying", slug);
            var (version, assets, error) = await AppServices.PluginSearch
                .GetLatestReleaseAsync(slug, CancellationToken.None);
            if (error.Length > 0)
            {
                RepoStatusText.Text = NetError(error);
                ShowAcceleratorHint();
                return;
            }

            var asset = PluginInstallCore.SelectAsset(assets);
            if (asset is null)
            {
                RepoStatusText.Text = Strings.T("plugin.github.noAsset", slug);
                return;
            }

            await InstallAssetAsync(slug, slug, version, asset,
                curated: false, sourceRepo: slug, networkConfirmed: true,
                status: (text, brush) =>
                {
                    RepoStatusText.Text = text;
                    RepoStatusText.SetResourceReference(TextBlock.ForegroundProperty, brush);
                });
        }
        catch (Exception ex)
        {
            RepoStatusText.Text = Strings.T("plugin.github.queryFailed", ex.Message);
        }
        finally
        {
            _pluginInstallBusy = false;
            RepoOneClickButton.IsEnabled = true;
        }
    }

    private async void OnOpenGitHubSearchClick(object sender, RoutedEventArgs e)
        => await AppServices.Host.OpenExternalAsync(
            "https://github.com/search?q=topic%3Aobs-plugin&type=repositories");

    private static string InstallError(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return Strings.T("plugin.install.err.unknown");
        var clean = code.StartsWith("PLUGIN_", StringComparison.OrdinalIgnoreCase) ? code[7..] : code;
        return Strings.T("plugin.install.err." + clean.ToLowerInvariant().Replace('_', '.'));
    }

    private static string NetError(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return Strings.T("plugin.install.err.unknown");
        var clean = code.StartsWith("NET_", StringComparison.OrdinalIgnoreCase) ? code[4..] : code;
        return Strings.T("net.err." + clean.ToLowerInvariant().Replace('_', '.'));
    }

    private static TextBlock InstallLine(string text, string brushKey)
    {
        var tb = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        tb.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSm");
        tb.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        return tb;
    }

    private void OnToggleHealthClick(object sender, RoutedEventArgs e)
    {
        _healthExpanded = !_healthExpanded;
        RenderHealth();
    }

    private void RenderHealth()
    {
        var scan = _scan;
        if (scan is null)
        {
            HealthPanel.Visibility = Visibility.Collapsed;
            return;
        }

        HealthPanel.Visibility = Visibility.Visible;
        HealthToggleButton.Content = _healthExpanded ? Strings.T("plugin.health.collapse") : Strings.T("plugin.health.expand");

        var dirs = scan.ScannedDirs.Count > 0 ? string.Join("；", scan.ScannedDirs) : "";
        HealthMetaText.Text = Strings.T("plugin.health.source", dirs);
        HealthMetaText.Visibility = scan.ScannedDirs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        HealthList.Children.Clear();

        if (scan.Plugins.Count == 0)
        {
            HealthHintText.Text = scan.ObsInstallFound
                ? Strings.T("plugin.health.noDlls")
                : Strings.T("plugin.health.noInstallDir");
            HealthHintText.Visibility = Visibility.Visible;
            HealthTitleText.Text = Strings.T("plugin.health.noneTitle");
            return;
        }

        HealthTitleText.Text = Strings.T("plugin.health.countTitle", scan.Plugins.Count, scan.CataloguedCount);

        if (!_healthExpanded)
        {
            HealthHintText.Text = Strings.T("plugin.health.expandHint");
            HealthHintText.Visibility = Visibility.Visible;
            return;
        }

        HealthHintText.Visibility = Visibility.Collapsed;
        foreach (var plugin in scan.Plugins)
            HealthList.Children.Add(BuildHealthRow(plugin));
    }

    private FrameworkElement BuildHealthRow(InstalledPluginFile plugin)
    {
        var nameText = new TextBlock
        {
            Text = plugin.FileName,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 320
        };
        nameText.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSm");
        nameText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

        var version = string.IsNullOrWhiteSpace(plugin.FileVersion) ? "" : $" v{plugin.FileVersion}";
        var metaText = new TextBlock
        {
            Text = $"{version} · {plugin.SizeBytes / 1024.0:0} KB".TrimStart(),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        metaText.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeXs");
        metaText.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");

        var headRow = new StackPanel { Orientation = Orientation.Horizontal };
        headRow.Children.Add(nameText);
        headRow.Children.Add(metaText);

        // 已知问题插件标注（V2.8，GAP-6）：命中广场条目的 riskNote 时打黄标
        if (plugin.CatalogId is not null &&
            PluginCatalogCore.FindById(_catalog, plugin.CatalogId) is { HasRiskNote: true } risky)
        {
            var riskText = new TextBlock
            {
                Text = $"⚠ {risky.RiskNote}",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                MaxWidth = 420,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = risky.RiskNote
            };
            riskText.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeXs");
            riskText.SetResourceReference(TextBlock.ForegroundProperty, "WarnBrush");
            headRow.Children.Add(riskText);
        }

        // 状态徽标：收录的显示对应广场条目名（可点击跳转卡片）；未收录灰标
        FrameworkElement status;
        var entry = plugin.CatalogId is null ? null : PluginCatalogCore.FindById(_catalog, plugin.CatalogId);
        if (entry is not null)
        {
            var linkText = new TextBlock
            {
                Text = Strings.T("plugin.health.catalogued", entry.Name),
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            linkText.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeXs");
            linkText.SetResourceReference(TextBlock.ForegroundProperty, "OkBrush");

            status = new Button
            {
                Style = TryFindResource("LinkButton") as Style,
                Content = linkText,
                Tag = entry.Id,
                ToolTip = Strings.T("plugin.health.locateTip")
            };
            ((Button)status).Click += OnLocateFromHealthClick;
        }
        else
        {
            var unknown = new TextBlock
            {
                Text = Strings.T("plugin.health.uncatalogued"),
                VerticalAlignment = VerticalAlignment.Center
            };
            unknown.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeXs");
            unknown.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            status = unknown;
        }

        var sourceTag = plugin.SourceLabel switch
        {
            "user" => Strings.T("plugin.health.source.user"),
            "global" => Strings.T("plugin.health.source.global"),
            _ => Strings.T("plugin.health.source.install")
        };

        var grid = new Grid { Margin = new Thickness(0, 5, 0, 5) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(headRow, 0);
        Grid.SetColumn(status, 1);
        var sourceElement = BuildSourceTag(sourceTag);
        Grid.SetColumn(sourceElement, 2);
        grid.Children.Add(headRow);
        grid.Children.Add(status);
        grid.Children.Add(sourceElement);

        return grid;
    }

    private static FrameworkElement BuildSourceTag(string label)
    {
        var tb = new TextBlock
        {
            Text = label,
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        tb.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeXs");
        tb.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        return tb;
    }

    private void OnLocateFromHealthClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        var entry = PluginCatalogCore.FindById(_catalog, id);
        if (entry is null) return;

        _activeCategory = entry.Category;
        SyncCategoryChips();
        if (SearchBox.Text.Length > 0) SearchBox.Text = "";

        _highlightId = id;
        RenderList();
    }

    // ---------------------------------------------------------- 列表渲染

    /// <summary>当前筛选条件下应展示的分类（关键词命中时跨分类全量匹配）。</summary>
    private IEnumerable<(PluginCategoryDef Category, List<PluginEntry> Items)> VisibleCategories()
    {
        var query = SearchBox.Text.Trim();

        foreach (var (category, items) in PluginCatalogCore.GroupByCategory(_catalog))
        {
            if (_activeCategory != "all" && category.Key != _activeCategory) continue;

            var filtered = string.IsNullOrEmpty(query)
                ? items
                : items.Where(p =>
                        p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        p.Desc.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        p.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        p.Repo.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        p.Badge.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        p.MaintainNote.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        category.Label.Contains(query, StringComparison.Ordinal) ||
                        p.Dlls.Any(d => d.Contains(query, StringComparison.OrdinalIgnoreCase)))
                    .ToList();

            if (filtered.Count > 0) yield return (category, filtered);
        }
    }

    // ---------------------------------------------------------- 卡片池（F6）

    /// <summary>
    /// 一张插件卡的常驻外壳。卡片与插件 id 一一对应，建一次就一直用：
    /// 筛选 / 搜索只改变 ListHost 里子元素的组成与顺序，不再逐条重建控件树。
    ///
    /// 这样做换来三件事：击键路径上的开销从「重建 57 张卡（每张十来个元素 + 一堆资源引用）」
    /// 降为「一次子元素重排」；角标不会因为重建而重打一遍网络请求；滚动位置也不会被无谓的重排顶回顶部。
    /// </summary>
    private sealed class PluginCardSlot
    {
        /// <summary>卡片对应的目录条目：动态部分（已安装版本、AI 开销）刷新时要用它取静态数据。</summary>
        public PluginEntry Entry { get; init; } = new();

        /// <summary>参与高亮动画的外层（卡片按钮的样式会吃掉 Opacity 动画目标，见 <see cref="CreateCardSlot"/>）。</summary>
        public Border Root { get; init; } = null!;

        /// <summary>「已安装」徽标：始终建好、靠 Visibility 切换，体检结果异步回来时只改文本。</summary>
        public Border InstalledBadge { get; init; } = null!;
        public TextBlock InstalledText { get; init; } = null!;

        /// <summary>关注按钮：关注状态可能在别处被改，刷新时同步文案。</summary>
        public Button WatchButton { get; init; } = null!;

        /// <summary>一键配置按钮：安装期间改文案 / 禁用，完成后再按已安装状态恢复。</summary>
        public Button OneClickButton { get; init; } = null!;

        /// <summary>一键配置的状态行（下载 / 校验 / 安装 / 失败原因）。</summary>
        public TextBlock StatusText { get; init; } = null!;

        /// <summary>最新版本角标。</summary>
        public TextBlock LatestBadge { get; init; } = null!;

        /// <summary>AI 开销行（非 AI 插件为 null）：性能预算提示变化时只改这一行。</summary>
        public TextBlock? CostText { get; init; }

        /// <summary>本卡是否已发起过角标查询（含失败或命中缓存）：避免滚动 / 重排时反复请求。</summary>
        public bool BadgeRequested { get; set; }
    }

    private readonly Dictionary<string, PluginCardSlot> _cardPool = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TextBlock> _sectionTitles = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>当前展示中的卡片（按可视顺序），供路由高亮与滚动补查角标使用。</summary>
    private readonly List<PluginCardSlot> _displayed = new();

    /// <summary>上一轮渲染的内容指纹：相同就整段跳过重排，避免无谓的布局与滚动位置复位。</summary>
    private string _renderedSignature = "";

    private bool _badgeScanQueued;
    private bool _revealScheduled;

    private PluginCardSlot GetCardSlot(PluginEntry plugin)
    {
        if (_cardPool.TryGetValue(plugin.Id, out var slot)) return slot;

        slot = CreateCardSlot(plugin);
        _cardPool[plugin.Id] = slot;
        return slot;
    }

    /// <summary>分类小标题也复用：它在每次重排里都会被用到，顺序与分类定义一一对应。</summary>
    private TextBlock GetSectionTitle(PluginCategoryDef category)
    {
        if (_sectionTitles.TryGetValue(category.Key, out var title))
        {
            // 分类标签可能随热更新目录变化：文字跟着同步，元素继续复用
            title.Text = category.Label;
            return title;
        }

        title = new TextBlock
        {
            Text = category.Label,
            Style = (Style)FindResource("SectionTitle"),
            Margin = new Thickness(2, 14, 0, 8)
        };
        _sectionTitles[category.Key] = title;
        return title;
    }

    private void RenderList()
    {
        var groups = VisibleCategories().ToList();

        // 内容指纹：同一批卡片（含顺序）就不动可视树。体检完成、预算提示回来都会走到这里，
        // 若每次都 Clear + 重排，用户的滚动位置会被反复顶回顶部（而这些刷新本身不改列表内容）。
        var signature = new StringBuilder();
        foreach (var (category, items) in groups)
        {
            signature.Append(category.Key).Append('|');
            foreach (var plugin in items) signature.Append(plugin.Id).Append(',');
            signature.Append(';');
        }

        if (!string.Equals(signature.ToString(), _renderedSignature, StringComparison.Ordinal))
        {
            _renderedSignature = signature.ToString();
            ListHost.Children.Clear();
            _displayed.Clear();

            foreach (var (category, items) in groups)
            {
                ListHost.Children.Add(GetSectionTitle(category));
                foreach (var plugin in items)
                {
                    var slot = GetCardSlot(plugin);
                    _displayed.Add(slot);
                    ListHost.Children.Add(slot.Root);
                }
            }
        }

        EmptyText.Visibility = groups.Count > 0 ? Visibility.Collapsed : Visibility.Visible;

        // 动态部分统一在这里刷一遍（新建的卡也走这条路）：无论刚才有没有重排，
        // 卡片上的「已安装 / 关注 / AI 开销」都是最新的，且每个条目每轮只算一次
        RefreshCardDynamicState();
        RefreshBadgeTargets();
        HighlightTargetCard();
    }

    /// <summary>体检结果 / 预算提示变化后刷新卡片的动态部分（不动静态结构，也不重排）。</summary>
    private void RefreshCardDynamicState()
    {
        foreach (var slot in _cardPool.Values)
        {
            ApplyInstalledState(slot);
            ApplyAiHint(slot);
            RefreshWatchVisual(slot);
        }
    }

    /// <summary>刷新「已安装」徽标：扫描（含重扫）结果变化时只动这一处。</summary>
    private void ApplyInstalledState(PluginCardSlot slot)
    {
        var installed = FindInstalled(slot.Entry);
        slot.OneClickButton.Content = Strings.T(
            installed is null ? "plugin.oneClick" : "plugin.oneClick.reinstall");

        if (installed is null)
        {
            slot.InstalledBadge.Visibility = Visibility.Collapsed;
            return;
        }

        slot.InstalledText.Text = string.IsNullOrWhiteSpace(installed.FileVersion)
            ? Strings.T("plugin.installed")
            : Strings.T("plugin.installedVersion", installed.FileVersion);
        slot.InstalledBadge.Visibility = Visibility.Visible;
    }

    /// <summary>刷新 AI 开销行（含当前性能预算提示）：卡片复用后，提示变化只改这一行文字与颜色。</summary>
    private void ApplyAiHint(PluginCardSlot slot)
    {
        var costText = slot.CostText;
        if (costText is null) return;

        var plugin = slot.Entry;
        var costs = new[] { plugin.AiCostCpu, plugin.AiCostMem }.Where(s => !string.IsNullOrWhiteSpace(s));
        var line = Strings.T("plugin.costs", string.Join(" · ", costs));
        if (!string.IsNullOrEmpty(_aiBudgetHint)) line += $"\n{_aiBudgetHint}";

        costText.Text = line;
        costText.SetResourceReference(TextBlock.ForegroundProperty,
            string.IsNullOrEmpty(_aiBudgetHint) ? "MutedBrush" : "WarnBrush");
    }

    private void HighlightTargetCard()
    {
        if (string.IsNullOrEmpty(_highlightId)) return;
        var id = _highlightId;
        _highlightId = null;

        // 只有真的挂在可视树上的卡片才谈得上滚动定位（池子里可能有被筛掉的卡片）
        if (!_cardPool.TryGetValue(id, out var slot) || slot.Root.Parent is null) return;
        var card = slot.Root;

        // 布局还没跑完时 BringIntoView 可能无效，推迟到渲染完成后执行
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            try { card.BringIntoView(); } catch (Exception) { }
        }));

        // 轻微闪烁两次提示位置（尊重「减少动画」设置）
        if (AppServices.Appearance.Settings.ReduceMotion) return;
        try
        {
            var blink = new DoubleAnimation(1, 0.35, TimeSpan.FromMilliseconds(260))
            {
                AutoReverse = true,
                RepeatBehavior = new RepeatBehavior(2),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            card.BeginAnimation(OpacityProperty, blink);
        }
        catch (Exception)
        {
            // 动画失败无碍
        }
    }

    // ---------------------------------------------------------- 角标查询调度（F6）

    /// <summary>
    /// 只查「看得见的那批」：当前筛选结果的前 <see cref="EagerBadgeCount"/> 张立即发请求，
    /// 其余保持「未查询」，由布局完成与滚动事件按视口补 —— 一次进页面最多十几个请求，而不是 57 个。
    /// </summary>
    private void RefreshBadgeTargets()
    {
        for (var i = 0; i < _displayed.Count && i < EagerBadgeCount; i++)
            RequestBadge(_displayed[i]);

        ScheduleRevealScan();
    }

    /// <summary>发起一张卡的角标查询。成功缓存 / 失败冷却都命中时，这里只是把已知结果填回卡片，不打网络。</summary>
    private void RequestBadge(PluginCardSlot slot)
    {
        if (slot.BadgeRequested || slot.Entry.Repo.Length == 0) return;
        slot.BadgeRequested = true;
        _ = UpdateLatestBadgeAsync(slot);
    }

    /// <summary>把「按视口补查」推迟到布局完成之后跑一次：首屏真正能看到的卡片宁多勿少。</summary>
    private void ScheduleRevealScan()
    {
        if (_revealScheduled) return;
        _revealScheduled = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _revealScheduled = false;
            RevealBadgesInViewport();
        }));
    }

    /// <summary>
    /// 滚到哪查到哪：只对靠近视口的卡片发起查询，已查过的直接跳过，
    /// 遇到第一张「还在视口下方一大截」的卡片就停 —— 每次滚动最多量一两张卡片的位置。
    /// 视口上方的未查卡片也一并补上（快速滚动可能直接跳过它们），总量有 57 的上限兜着。
    /// </summary>
    private void RevealBadgesInViewport()
    {
        var viewport = PageScroller.ViewportHeight;
        if (viewport <= 0) return; // 布局还没跑出来：等下一次滚动 / 渲染再补

        foreach (var slot in _displayed)
        {
            if (slot.BadgeRequested) continue;

            double top;
            try
            {
                top = slot.Root.TransformToAncestor(PageScroller).Transform(new Point(0, 0)).Y;
            }
            catch (Exception)
            {
                // 元素暂时不在可视树里（正在重排）：这一轮放弃，下一轮再补
                return;
            }

            if (top > viewport + BadgePrefetchPx) return; // 更靠下的卡只会更远
            RequestBadge(slot);
        }
    }

    /// <summary>滚动事件：一次滚动会连发很多个，合并成每帧一次扫描。</summary>
    private void OnPageScrolled(object sender, ScrollChangedEventArgs e)
    {
        if (_badgeScanQueued) return;
        _badgeScanQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _badgeScanQueued = false;
            RevealBadgesInViewport();
        }));
    }

    /// <summary>已安装标记查找：stem → 已装版本文本。</summary>
    private InstalledPluginFile? FindInstalled(PluginEntry entry)
    {
        if (_scan is null || entry.Dlls.Count == 0) return null;
        foreach (var installed in _scan.Plugins)
        {
            if (installed.CatalogId == entry.Id) return installed;
            foreach (var alias in entry.Dlls)
            {
                if (string.Equals(alias?.Trim(), installed.Stem, StringComparison.OrdinalIgnoreCase))
                    return installed;
            }
        }
        return null;
    }

    /// <summary>
    /// 建一张插件卡（每个目录条目只建一次，之后一直复用）。
    /// 卡片里凡是会变的东西（已安装徽标、AI 开销行、关注文案、角标）都先建好、留出引用，
    /// 由 <see cref="ApplyInstalledState"/> 等方法就地更新 —— 这是卡片能复用的前提。
    /// </summary>
    private PluginCardSlot CreateCardSlot(PluginEntry plugin)
    {
        var nameText = new TextBlock
        {
            Text = plugin.Name,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        nameText.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeBase");
        nameText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

        var headRow = new StackPanel { Orientation = Orientation.Horizontal };
        headRow.Children.Add(nameText);

        if (!string.IsNullOrEmpty(plugin.Badge))
            headRow.Children.Add(BuildBadge(plugin.Badge, DataValues.IsHotBadge(plugin.Badge) ? "WarnBrush" : "BrandBrush"));

        // 已安装标记（P0-1 联动）：先建好隐藏着，体检结果回来时只改文字与显隐（F6 卡片复用）
        var installedText = new TextBlock
        {
            Text = "",
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(7, 1, 7, 2)
        };
        installedText.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeXs");
        installedText.SetResourceReference(TextBlock.ForegroundProperty, "PillForegroundBrush");

        var installedBadge = new Border
        {
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
            Child = installedText
        };
        installedBadge.SetResourceReference(Border.BackgroundProperty, "OkBrush");
        headRow.Children.Add(installedBadge);

        var urlHost = new TextBlock
        {
            Text = HostLabel(plugin.Url),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(10, 0, 0, 0)
        };
        urlHost.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeXs");
        urlHost.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");

        var headGrid = new Grid();
        headGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(headRow, 0);
        Grid.SetColumn(urlHost, 2);
        headGrid.Children.Add(headRow);
        headGrid.Children.Add(urlHost);

        var body = new StackPanel { Margin = new Thickness(12, 9, 12, 11) };
        body.Children.Add(headGrid);

        var descText = new TextBlock
        {
            Text = plugin.Desc,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        descText.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSm");
        descText.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        body.Children.Add(descText);

        // AI 插件：公开开销说明 + 实时性能预算提示（P1-2）。
        // 文案与颜色交给 ApplyAiHint：预算提示可能在页面停留期间才拿到，届时只改这一行文字。
        TextBlock? costText = null;
        if (plugin.HasAiCost)
        {
            costText = new TextBlock
            {
                Text = "",
                Margin = new Thickness(0, 6, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
            costText.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeXs");
            body.Children.Add(costText);
        }

        // 维护状态（V2.9，目录 v1.4）：把「上游还在不在维护」摆到卡片上。
        // 维护放缓用警示色（并带 ToolTip），活跃用次要色，不给用户造成无谓焦虑。
        if (plugin.HasMaintenanceInfo)
        {
            var maintainText = new TextBlock
            {
                Text = Strings.T("plugin.maintain.label", plugin.MaintenanceText),
                Margin = new Thickness(0, 6, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                ToolTip = plugin.IsMaintenanceSlow
                    ? Strings.T("plugin.maintain.slowTip")
                    : Strings.T("plugin.maintain.activeTip")
            };
            maintainText.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeXs");
            maintainText.SetResourceReference(TextBlock.ForegroundProperty,
                plugin.IsMaintenanceSlow ? "WarnBrush" : "MutedBrush");
            body.Children.Add(maintainText);
        }

        // 与本机 OBS 的兼容性（V3.0 / D9）：OBS 每次大版本都会让一批插件悄悄失效
        // （面板不显示、捕获源消失），而用户完全不知道原因。目录声明了兼容范围就在这里提前说清楚；
        // 未声明（旧目录）与匹配时不显示 —— 不给正常条目刷屏。
        if (plugin.HasCompatWarning)
        {
            var compatText = new TextBlock
            {
                Text = plugin.CompatText,
                Margin = new Thickness(0, 6, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
            compatText.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeXs");
            compatText.SetResourceReference(TextBlock.ForegroundProperty,
                plugin.CompatStatus == PluginCompatStatus.Broken ? "DangerBrush" : "WarnBrush");
            body.Children.Add(compatText);
        }

        // 动作行：一键配置 + 下载页 + 最新版本角标 + 关注（P1-1 / P2-1 / X1031Y2）
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 0)
        };

        var oneClick = new Button
        {
            Style = (Style)TryFindResource("PrimaryButton"),
            Content = Strings.T("plugin.oneClick"),
            Padding = new Thickness(12, 4, 12, 5),
            Tag = plugin,
            ToolTip = Strings.T("plugin.oneClickTip")
        };
        oneClick.Click += OnOneClickInstallClick;
        actions.Children.Add(oneClick);

        var downloadUrl = BuildReleasesLatestUrl(plugin.Repo);
        if (!string.IsNullOrEmpty(downloadUrl))
        {
            var downloadBtn = new Button
            {
                Style = (Style)TryFindResource("LinkButton"),
                Content = Strings.T("plugin.releases"),
                Padding = new Thickness(8, 2, 8, 3),
                Margin = new Thickness(8, 0, 0, 0),
                Tag = downloadUrl,
                ToolTip = Strings.T("plugin.downloadTip")
            };
            downloadBtn.Click += OnDownloadClick;
            actions.Children.Add(downloadBtn);
        }

        var latestBadge = new TextBlock
        {
            Text = "",
            Visibility = Visibility.Collapsed,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
        };
        latestBadge.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeXs");
        latestBadge.SetResourceReference(TextBlock.ForegroundProperty, "OkBrush");
        actions.Children.Add(latestBadge);

        var watchBtn = new Button
        {
            Style = (Style)TryFindResource("LinkButton"),
            Padding = new Thickness(8, 2, 8, 3),
            Tag = plugin.Id,
            ToolTip = Strings.T("plugin.watchTip")
        };
        watchBtn.Click += OnWatchToggleClick;
        actions.Children.Add(watchBtn);

        body.Children.Add(actions);

        // 一键配置的状态行：下载 / 校验 / 安装 / 失败原因。默认隐藏，不占视觉噪音。
        var statusText = new TextBlock
        {
            Text = "",
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 5, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        statusText.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeXs");
        statusText.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        body.Children.Add(statusText);

        var button = new Button
        {
            Style = (Style)FindResource("CardButton"),
            Content = body,
            Tag = plugin.Url,
            Margin = new Thickness(0, 0, 0, 10),
            ToolTip = Strings.T("plugin.homeTip")
        };
        button.Click += OnOpenLinkClick;

        // 卡片本体是按钮，外面包一层 Border 承担高亮动画（按钮自身样式会吃掉 Opacity 动画目标）
        var cardBorder = new Border
        {
            Child = button,
            Tag = $"card:{plugin.Id}"
        };

        return new PluginCardSlot
        {
            Entry = plugin,
            Root = cardBorder,
            InstalledBadge = installedBadge,
            InstalledText = installedText,
            WatchButton = watchBtn,
            OneClickButton = oneClick,
            StatusText = statusText,
            LatestBadge = latestBadge,
            CostText = costText
        };
    }

    private static FrameworkElement BuildBadge(string text, string brushKey)
    {
        var badgeText = new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(7, 1, 7, 2)
        };
        badgeText.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeXs");
        // 【v2.7.1】彩色小标反衬文字走资源：深色模式语义色提亮后统一深字压亮底
        badgeText.SetResourceReference(TextBlock.ForegroundProperty, "PillForegroundBrush");

        var badge = new Border
        {
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = badgeText
        };
        badge.SetResourceReference(Border.BackgroundProperty, brushKey);
        return badge;
    }

    // ---------------------------------------------------------- 最新版本角标（P1-1）

    private async Task UpdateLatestBadgeAsync(PluginCardSlot slot)
    {
        try
        {
            var info = await AppServices.PluginReleases.GetLatestAsync(slot.Entry.Repo).ConfigureAwait(true);
            if (info is null) return;

            // 页面可能在等待期间被重建（目录换版 / LRU 逐出）：只有仍属于当前卡片池的角标才更新
            if (!_cardPool.TryGetValue(slot.Entry.Id, out var current) || !ReferenceEquals(current, slot))
            {
                return;
            }

            slot.LatestBadge.Text = Strings.T("plugin.latest", ShortTag(info.Tag));
            slot.LatestBadge.Visibility = Visibility.Visible;
        }
        catch (Exception)
        {
            // 角标属锦上添花，任何异常静默
        }
    }

    private static string ShortTag(string tag)
        => tag.Length > 1 && (tag[0] == 'v' || tag[0] == 'V') ? tag[1..] : tag;

    private static string? BuildReleasesLatestUrl(string repo)
    {
        var normalized = PluginReleaseService.NormalizeRepo(repo);
        return normalized.Length == 0 ? null : $"https://github.com/{normalized}/releases/latest";
    }

    // ---------------------------------------------------------- 关注（P2-1）

    private void RefreshWatchVisual(Button watchBtn, string pluginId)
    {
        var watched = AppServices.PluginWatch.IsWatched(pluginId);
        watchBtn.Content = watched ? Strings.T("plugin.watched") : Strings.T("plugin.watch");
    }

    /// <summary>卡片复用版的关注文案刷新：关注状态可能在别处被改，重排 / 重进页面时同步一次。</summary>
    private void RefreshWatchVisual(PluginCardSlot slot) => RefreshWatchVisual(slot.WatchButton, slot.Entry.Id);

    private void OnWatchToggleClick(object sender, RoutedEventArgs e)
    {
        // 同「下载」按钮：阻止 Click 冒泡到外层卡片（避免顺手打开项目主页）
        e.Handled = true;
        if (sender is not Button { Tag: string id }) return;
        var watched = !AppServices.PluginWatch.IsWatched(id);
        AppServices.PluginWatch.SetWatched(id, watched);
        RefreshWatchVisual((Button)sender, id);
        AppServices.Toast.Show(watched ? Strings.T("plugin.watchedToast") : Strings.T("plugin.unwatchedToast"), "ok");
    }

    // ---------------------------------------------------------- 性能预算（P1-2）

    /// <summary>
    /// 结合监控采样给 AI 类卡片一句个性化预算提示；数据不足返回 null，不硬凑文案。
    ///
    /// V3.0：<b>只读传入的采样</b>，绝不调用 <c>SystemMonitor.Start()</c> ——
    /// 原来那行 `Start()` 会让 1 秒采样的计时器常驻到进程退出（全仓只有监控页会 Stop）。
    /// </summary>
    private static string? ComputeAiBudgetHint(SystemSample? s)
    {
        try
        {
            if (s is null) return null;

            var freeMb = s.MemTotalMb - s.MemUsedMb;
            if (s.MemTotalMb > 0 && freeMb < 500)
                return Strings.T("plugin.aiMemoryHint", freeMb.ToString("0", System.Globalization.CultureInfo.InvariantCulture));
            if (s.CpuPercent >= 80)
                return Strings.T("plugin.aiCpuHint", s.CpuPercent.ToString("0", System.Globalization.CultureInfo.InvariantCulture));
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ---------------------------------------------------------- 打开外链

    /// <summary>
    /// 卡片内的「下载」按钮：Click 是冒泡路由事件，不置 Handled 会连带触发外层卡片按钮
    /// 的「打开项目主页」，因此这里必须标记已处理。
    /// </summary>
    private async void OnDownloadClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button { Tag: string url }) return;
        await OpenUrlAsync(url);
    }

    private async void OnOpenLinkClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string url) return;
        await OpenUrlAsync(url);
    }

    private async Task OpenUrlAsync(string url)
    {
        try
        {
            var ok = await AppServices.Host.OpenExternalAsync(url);
            if (!ok) AppServices.Toast.Show(Strings.T("common.openLinkFailed"), "warn");
        }
        catch (Exception)
        {
            AppServices.Toast.Show(Strings.T("common.openLinkFailed"), "warn");
        }
    }

    /// <summary>链接展示为短标签：GitHub 仓库名或站点域名。</summary>
    private static string HostLabel(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return uri.Host == "github.com"
                ? uri.AbsolutePath.TrimStart('/')
                : uri.Host.Replace("www.", "");
        }
        return url;
    }

    // ------------------------------------------------------------ 开源依赖（V3.4.1）

    private void BuildDependencyPanel()
    {
        DependencyHost.Children.Clear();
        foreach (var status in Dependencies.Scan())
            DependencyHost.Children.Add(BuildDependencyRow(status));
    }

    /// <summary>
    /// 一行依赖：名称 / 许可证 / 用途 / 当前状态 + 「一键下载」「一键配置」「仓库」。
    /// 全部用代码构建，文案随语言切换（每次进页重建，不缓存旧语言）。
    /// </summary>
    private Border BuildDependencyRow(DependencyStatus status)
    {
        var spec = status.Spec;
        var name = Strings.T("dep.name." + spec.Id);

        var row = new Border { Margin = new Thickness(0, 0, 0, 8) };
        row.SetResourceReference(FrameworkElement.StyleProperty, "SoftPanel");

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var info = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        info.Children.Add(new TextBlock
        {
            Text = name + "  ·  " + spec.License,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });

        var desc = new TextBlock { Text = Strings.T("dep.desc." + spec.Id), Margin = new Thickness(0, 3, 0, 0) };
        desc.SetResourceReference(FrameworkElement.StyleProperty, "MutedText");
        info.Children.Add(desc);

        var state = new TextBlock
        {
            Text = status.Ready
                ? Strings.T("dep.status.ready") + "  " + status.Path
                : Strings.T("dep.status.missing"),
            Margin = new Thickness(0, 5, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        state.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeXs");
        state.SetResourceReference(TextBlock.ForegroundProperty, status.Ready ? "OkBrush" : "WarnBrush");
        info.Children.Add(state);

        grid.Children.Add(info);

        var actions = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var auto = new Button
        {
            Content = Strings.T("dep.configureAuto"),
            Tag = spec,
            MinWidth = 92,
            ToolTip = Strings.T("dep.configureAutoTip")
        };
        auto.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButton");
        auto.Click += OnDependencyAutoClick;
        actions.Children.Add(auto);

        var configure = new Button
        {
            Content = Strings.T("dep.configureManual"),
            Tag = spec,
            MinWidth = 92,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = Strings.T("dep.configureManualTip")
        };
        configure.SetResourceReference(FrameworkElement.StyleProperty, "GhostButton");
        configure.Click += OnDependencyConfigureClick;
        actions.Children.Add(configure);

        var download = new Button
        {
            Content = Strings.T("dep.downloadPage"),
            Tag = spec,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = Strings.T("dep.downloadTip")
        };
        download.SetResourceReference(FrameworkElement.StyleProperty, "LinkButton");
        download.Click += OnDependencyDownloadClick;
        actions.Children.Add(download);

        var repo = new Button { Content = Strings.T("dep.repo"), Tag = spec.RepoUrl, Margin = new Thickness(8, 0, 0, 0) };
        repo.SetResourceReference(FrameworkElement.StyleProperty, "LinkButton");
        repo.Click += OnOpenLinkClick;
        actions.Children.Add(repo);

        Grid.SetColumn(actions, 1);
        grid.Children.Add(actions);
        row.Child = grid;
        return row;
    }

    /// <summary>
    /// 一键配置（X1031Y2）：在应用内查最新 Release、下载、校验、解压并接入。
    /// 下载前先确认网络 / 加速器；失败时保留「选择已下载文件」与「下载页」两条退路。
    /// </summary>
    private async void OnDependencyAutoClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not DependencySpec spec) return;

        var name = Strings.T("dep.name." + spec.Id);
        if (!ConfirmNetwork(Strings.T("dep.download.action", name))) return;

        button.IsEnabled = false;
        var original = button.Content;
        try
        {
            var progress = new Progress<(long Done, long Total)>(p =>
            {
                if (p.Total > 0)
                {
                    var percent = (int)Math.Clamp(p.Done * 100 / p.Total, 0, 100);
                    button.Content = Strings.T("dep.download.progress", percent);
                }
                else
                {
                    button.Content = Strings.T("dep.installing", name);
                }
            });

            button.Content = Strings.T("dep.installing", name);
            var result = await AppServices.DependencyInstall
                .InstallAsync(spec, progress, CancellationToken.None).ConfigureAwait(true);

            if (!result.Ok)
            {
                var code = string.IsNullOrWhiteSpace(result.Code) ? "dep.error.download" : result.Code;
                AppServices.Toast.Show(Strings.T(code, spec.Executable), "warn");
                return;
            }

            var (configured, configureCode, path) = Dependencies.Configure(spec, result.Path);
            if (configured)
            {
                AppServices.Toast.Show(
                    result.DigestVerified
                        ? Strings.T("dep.configure.autoDone", name, path)
                        : Strings.T("dep.configure.autoDoneNoDigest", name, path),
                    "ok");
            }
            else
            {
                AppServices.Toast.Show(Strings.T(string.IsNullOrWhiteSpace(configureCode) ? "dep.error.invalid" : configureCode), "warn");
            }
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Dependency", $"一键配置失败（{spec.Id}）：{ex.Message}");
            AppServices.Toast.Show(Strings.T("dep.error.download"), "warn");
        }
        finally
        {
            button.Content = original;
            button.IsEnabled = true;
            BuildDependencyPanel();
        }
    }

    private async void OnDependencyDownloadClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not DependencySpec spec) return;
        button.IsEnabled = false;
        try
        {
            var (ok, code) = await Dependencies.DownloadAsync(spec).ConfigureAwait(true);
            var name = Strings.T("dep.name." + spec.Id);
            if (ok)
            {
                AppServices.Toast.Show(
                    string.IsNullOrEmpty(code) ? Strings.T("dep.download.done", name) : Strings.T(code), "ok");
            }
            else
            {
                AppServices.Toast.Show(
                    Strings.T(string.IsNullOrEmpty(code) ? "dep.download.failed" : code), "warn");
            }
            BuildDependencyPanel();
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Dependency", $"一键下载失败（{spec.Id}）：{ex.Message}");
            AppServices.Toast.Show(Strings.T("dep.download.failed"), "warn");
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private void OnDependencyConfigureClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not DependencySpec spec) return;
        var name = Strings.T("dep.name." + spec.Id);
        var found = Dependencies.Detect(spec);
        if (string.IsNullOrWhiteSpace(found))
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = Strings.T("dep.pick.title", name),
                Filter = Strings.T("dep.pick.filter"),
                CheckFileExists = true,
            };
            if (dialog.ShowDialog() != true) return;
            found = dialog.FileName;
        }

        var (ok, code, path) = Dependencies.Configure(spec, found);
        AppServices.Toast.Show(
            ok ? Strings.T("dep.configure.done", name, path) : Strings.T(code),
            ok ? "ok" : "warn");
        BuildDependencyPanel();
    }
}
