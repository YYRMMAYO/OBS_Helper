using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using OBS_Helper.Wpf.Navigation;
using OBS_Helper.Wpf.Services.Plugins;
using OBS_Helper.Wpf.Services.Shell;

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
/// </summary>
public partial class PluginsPage : UserControl, INavigationAware
{
    /// <summary>官方 / 社区入口，放在分类列表之前（量小且稳定，保留在代码内）。</summary>
    private static readonly (string Label, string Desc, string Url)[] Entries =
    {
        ("OBS 论坛 · 插件区", "官方插件发布与更新公告", "https://obsproject.com/forum/plugins/"),
        ("Exeldro 作品集", "Move / Source 系列等 20+ 高产插件", "https://github.com/exeldro"),
        ("occ-ai 系列", "抠像、字幕、降噪等 AI 插件全家桶", "https://github.com/occ-ai"),
    };

    private PluginCatalogData _catalog = new();
    private string _builtVersion = "";
    private string _activeCategory = "all";

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
    }

    public async Task OnNavigatedToAsync(object? parameter)
    {
        var data = AppServices.PluginCatalog.GetData();
        var versionChanged = !string.Equals(_builtVersion, data.Version, StringComparison.Ordinal);
        _catalog = data;

        if (versionChanged)
        {
            ResourceHost.Children.Clear();
            CategoryPanel.Children.Clear();
            ListHost.Children.Clear();
            BuildResourceCards();
            BuildCategoryChips();
            _builtVersion = data.Version ?? "";
            // 目录换版后 chips 已重建为「全部」，分类状态同步复位，避免 UI 与实际过滤不一致
            _activeCategory = "all";
        }

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

        _aiBudgetHint = ComputeAiBudgetHint();

        RenderList();

        await EnsureScanAsync(force: false);
    }

    // ---------------------------------------------------------- 官方入口

    private void BuildResourceCards()
    {
        foreach (var (label, desc, url) in Entries)
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

    // ---------------------------------------------------------- 分类 chips

    private void BuildCategoryChips()
    {
        var allChip = new RadioButton
        {
            Style = (Style)FindResource("SegmentButton"),
            GroupName = "PluginCategory",
            Content = "🌐 全部",
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
                Content = $"{category.Icon} {category.Label}",
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

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e) => RenderList();

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
            RenderList(); // 卡片上的「已安装」标记跟着刷新
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
        HealthRefreshButton.Content = busy ? "扫描中…" : "↻ 重新扫描";
    }

    private void OnRescanClick(object sender, RoutedEventArgs e) => _ = EnsureScanAsync(force: true);

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
        HealthToggleButton.Content = _healthExpanded ? "收起" : "展开";

        var dirs = scan.ScannedDirs.Count > 0 ? string.Join("；", scan.ScannedDirs) : "";
        HealthMetaText.Text = $"检测来源：{dirs}（只读扫描，不会修改任何文件；结果仅存本机）";
        HealthMetaText.Visibility = scan.ScannedDirs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        HealthList.Children.Clear();

        if (scan.Plugins.Count == 0)
        {
            HealthHintText.Text = scan.ObsInstallFound
                ? "未在常见目录发现第三方插件 DLL。"
                : "未检测到 OBS 安装目录；若为便携版或自定义路径，请先在「设置 → OBS 配置管理」中手动指定目录后重试。";
            HealthHintText.Visibility = Visibility.Visible;
            HealthTitleText.Text = "本机已装插件体检（只读）· 未发现插件";
            return;
        }

        HealthTitleText.Text = $"本机已装 {scan.Plugins.Count} 个插件 · 其中 {scan.CataloguedCount} 个收录于下方广场";

        if (!_healthExpanded)
        {
            HealthHintText.Text = "点击「展开」查看完整清单。";
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

        // 状态徽标：收录的显示对应广场条目名（可点击跳转卡片）；未收录灰标
        FrameworkElement status;
        var entry = plugin.CatalogId is null ? null : PluginCatalogCore.FindById(_catalog, plugin.CatalogId);
        if (entry is not null)
        {
            var linkText = new TextBlock
            {
                Text = $"✓ 广场收录：{entry.Name}",
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
                ToolTip = "在下方广场中定位该插件"
            };
            ((Button)status).Click += OnLocateFromHealthClick;
        }
        else
        {
            var unknown = new TextBlock
            {
                Text = "未收录",
                VerticalAlignment = VerticalAlignment.Center
            };
            unknown.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeXs");
            unknown.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            status = unknown;
        }

        var sourceTag = plugin.SourceLabel == "user" ? "用户目录" : "安装目录";

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
                        p.Desc.Contains(query, StringComparison.OrdinalIgnoreCase))
                    .ToList();

            if (filtered.Count > 0) yield return (category, filtered);
        }
    }

    /// <summary>渲染时记录 id → 卡片元素，供路由参数定位高亮。</summary>
    private readonly Dictionary<string, FrameworkElement> _cardsById = new(StringComparer.OrdinalIgnoreCase);

    private void RenderList()
    {
        ListHost.Children.Clear();
        _cardsById.Clear();

        var any = false;
        foreach (var (category, items) in VisibleCategories())
        {
            any = true;

            var sectionTitle = new TextBlock
            {
                Text = $"{category.Icon} {category.Label}",
                Style = (Style)FindResource("SectionTitle"),
                Margin = new Thickness(2, 14, 0, 8)
            };
            ListHost.Children.Add(sectionTitle);

            foreach (var plugin in items)
            {
                var card = BuildPluginCard(plugin);
                _cardsById[plugin.Id] = card;
                ListHost.Children.Add(card);
            }
        }

        EmptyText.Visibility = any ? Visibility.Collapsed : Visibility.Visible;

        HighlightTargetCard();
    }

    private void HighlightTargetCard()
    {
        if (string.IsNullOrEmpty(_highlightId)) return;
        var id = _highlightId;
        _highlightId = null;

        if (!_cardsById.TryGetValue(id, out var card)) return;

        // 布局还没跑完时 BringIntoView 可能无效，推迟到渲染完成后执行
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
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

    private Border BuildPluginCard(PluginEntry plugin)
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
            headRow.Children.Add(BuildBadge(plugin.Badge, plugin.Badge == "热门" ? "WarnBrush" : "BrandBrush"));

        // 已安装标记（P0-1 联动）
        var installed = FindInstalled(plugin);
        if (installed is not null)
        {
            var installedLabel = string.IsNullOrWhiteSpace(installed.FileVersion)
                ? "✓ 已安装"
                : $"✓ 已安装 v{installed.FileVersion}";
            headRow.Children.Add(BuildBadge(installedLabel, "OkBrush"));
        }

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

        // AI 插件：公开开销说明 + 实时性能预算提示（P1-2）
        if (plugin.HasAiCost)
        {
            var costs = new[] { plugin.AiCostCpu, plugin.AiCostMem }.Where(s => !string.IsNullOrWhiteSpace(s));
            var costLine = "开销参考：" + string.Join(" · ", costs);
            if (!string.IsNullOrEmpty(_aiBudgetHint)) costLine += $"\n⚠ {_aiBudgetHint}";

            var costText = new TextBlock
            {
                Text = costLine,
                Margin = new Thickness(0, 6, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
            costText.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeXs");
            costText.SetResourceReference(TextBlock.ForegroundProperty,
                string.IsNullOrEmpty(_aiBudgetHint) ? "MutedBrush" : "WarnBrush");
            body.Children.Add(costText);
        }

        // 动作行：下载 + 最新版本角标 + 关注（P1-1 / P2-1）
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 0)
        };

        var downloadUrl = BuildReleasesLatestUrl(plugin.Repo);
        if (!string.IsNullOrEmpty(downloadUrl))
        {
            var downloadBtn = new Button
            {
                Style = (Style)TryFindResource("SecondaryButton"),
                Content = "⬇ 下载",
                Padding = new Thickness(10, 4, 10, 5),
                Tag = downloadUrl,
                ToolTip = "打开 GitHub Releases 最新版下载页"
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
            ToolTip = "关注后，应用启动时会静默检查该插件的最新版本（有新版仅在角落轻提示，不弹窗）"
        };
        RefreshWatchVisual(watchBtn, plugin.Id);
        watchBtn.Click += OnWatchToggleClick;
        actions.Children.Add(watchBtn);

        body.Children.Add(actions);

        var button = new Button
        {
            Style = (Style)FindResource("CardButton"),
            Content = body,
            Tag = plugin.Url,
            Margin = new Thickness(0, 0, 0, 10),
            ToolTip = "在浏览器中打开项目主页"
        };
        button.Click += OnOpenLinkClick;

        // 卡片本体是按钮，外面包一层 Border 承担高亮动画（按钮自身样式会吃掉 Opacity 动画目标）
        var cardBorder = new Border
        {
            Child = button,
            Tag = $"card:{plugin.Id}"
        };
        _latestBadgeTargets[plugin.Id] = latestBadge;

        if (!string.IsNullOrEmpty(downloadUrl) && !string.IsNullOrEmpty(plugin.Repo))
            _ = UpdateLatestBadgeAsync(plugin.Id, plugin.Repo, latestBadge);

        return cardBorder;
    }

    private readonly Dictionary<string, TextBlock> _latestBadgeTargets = new(StringComparer.OrdinalIgnoreCase);

    private static FrameworkElement BuildBadge(string text, string brushKey)
    {
        var badgeText = new TextBlock
        {
            Text = text,
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(7, 1, 7, 2)
        };
        badgeText.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeXs");

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

    private async Task UpdateLatestBadgeAsync(string pluginId, string repo, TextBlock badge)
    {
        try
        {
            var info = await AppServices.PluginReleases.GetLatestAsync(repo).ConfigureAwait(true);
            if (info is null) return;

            // 页面可能在等待期间被重建：只有仍在展示中的角标才更新
            if (!_latestBadgeTargets.TryGetValue(pluginId, out var current) ||
                !ReferenceEquals(current, badge))
            {
                return;
            }

            badge.Text = $"最新 {ShortTag(info.Tag)}";
            badge.Visibility = Visibility.Visible;
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
        watchBtn.Content = watched ? "★ 已关注" : "☆ 关注";
    }

    private void OnWatchToggleClick(object sender, RoutedEventArgs e)
    {
        // 同「下载」按钮：阻止 Click 冒泡到外层卡片（避免顺手打开项目主页）
        e.Handled = true;
        if (sender is not Button { Tag: string id }) return;
        var watched = !AppServices.PluginWatch.IsWatched(id);
        AppServices.PluginWatch.SetWatched(id, watched);
        RefreshWatchVisual((Button)sender, id);
        AppServices.Toast.Show(watched ? "已关注，启动时将静默检查该插件的新版本" : "已取消关注", "ok");
    }

    // ---------------------------------------------------------- 性能预算（P1-2）

    /// <summary>
    /// 结合监控服务的实时采样给 AI 类卡片一句个性化预算提示；
    /// 数据不足（监控刚起步）返回 null，不硬凑文案。
    /// </summary>
    private static string? ComputeAiBudgetHint()
    {
        try
        {
            var monitor = AppServices.SystemMonitor;
            if (!monitor.IsRunning) monitor.Start();
            var s = monitor.Latest;
            if (s is null) return null;

            var freeMb = s.MemTotalMb - s.MemUsedMb;
            if (s.MemTotalMb > 0 && freeMb < 500)
                return $"当前空闲内存约 {freeMb:0}MB（低于 500MB），启用 AI 插件可能加剧掉帧";
            if (s.CpuPercent >= 80)
                return $"当前 CPU 占用 {s.CpuPercent:0}%，AI 插件实时推理可能进一步推高负载";
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
            if (!ok) AppServices.Toast.Show("无法打开链接，请检查系统默认浏览器设置", "warn");
        }
        catch (Exception)
        {
            AppServices.Toast.Show("无法打开链接，请检查系统默认浏览器设置", "warn");
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
}
