using System.Windows.Controls;
using OBS_Helper.Wpf.Localization;

namespace OBS_Helper.Wpf.Navigation;

/// <summary>
/// 页面在被导航到 / 离开时的回调。页面若需要按参数加载数据（分类页、问题详情页），实现本接口。
///
/// <see cref="OnNavigatedFromAsync"/> 与 <see cref="CanReleaseOnLeave"/> 是默认接口实现（C# 8+），
/// 现有页面无需改动；需要管理计时器 / 事件订阅 / 大资源的页面按需覆写即可。
/// </summary>
public interface INavigationAware
{
    /// <summary>导航到本页时调用。<paramref name="parameter"/> 为路由参数，可能为 null。</summary>
    Task OnNavigatedToAsync(object? parameter);

    /// <summary>
    /// 导航离开本页时调用（切换前触发）。页面在此对称退订事件 / 停止计时器，
    /// 从根上消除「离开页面后后台任务常驻」这类泄漏。
    /// </summary>
    Task OnNavigatedFromAsync() => Task.CompletedTask;

    /// <summary>
    /// 离开本页后是否允许从导航缓存释放页面实例（下次进入重新创建）。
    /// 默认 false 保持缓存（保滚动位置 / 避免重建）；页面无有价值状态时可声明 true。
    /// 另外缓存本身还有一道 LRU 上限（见 <see cref="NavigationService"/>），即便页面声明 false 也可能被逐出，
    /// 因此页面**必须**把所有跨导航要保留的状态放进服务单例，而不是自己的字段里。
    /// </summary>
    bool CanReleaseOnLeave => false;
}

/// <summary>
/// 极简的页面导航。
///
/// 取代 Blazor 的 &lt;Router&gt;：路由名 → 页面工厂，页面实例缓存复用（避免每次切页重建列表），
/// 并维护一个前进 / 后退栈供顶栏的「返回」按钮使用。
/// 路由名常量在 <see cref="Routes"/>（单独一个文件，便于被单测链接校验）。
///
/// V3.0（F7）给这份缓存加了 LRU 上限：以前它无上限、只有页面自己声明可释放才会逐出，
/// 结果是「访问过的每一页连同整棵控件树一起常驻」——17 个页面里 16 个默认留下，
/// 日志页还连带一份 8MB 脱敏全文。现在超出 <see cref="MaxCachedPages"/> 就按最久未用逐出，
/// 逐出前保证调用过页面的离开钩子（见 <see cref="DetachAsync"/>），不留幽灵订阅。
/// </summary>
public sealed class NavigationService
{
    /// <summary>
    /// 常驻页面实例的上限。8 足够覆盖「首页 + 几个常用页」的来回切换（命中缓存不重建），
    /// 又能在长尾浏览后把没人再看的页面（含其控件树与大对象）交还给 GC。
    /// </summary>
    private const int MaxCachedPages = 8;

    private readonly Dictionary<string, Func<UserControl>> _factories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, UserControl> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stack<(string Route, object? Parameter)> _back = new();

    /// <summary>缓存页面的使用顺序，索引 0 = 最久未用；LRU 逐出时从这里挑牺牲者。</summary>
    private readonly List<string> _usageOrder = new();

    /// <summary>
    /// 当前「已附加」的页面路由（已调用过 OnNavigatedToAsync、尚未收尾的页面）。
    /// 它是「每个页面每次进入只收尾一次」的唯一事实来源，详见 <see cref="DetachAsync"/>。
    /// </summary>
    private string? _attachedRoute;

    /// <summary>导航完成后触发：(路由名, 页面实例)。MainWindow 据此换内容并同步导航高亮。</summary>
    public event Action<string, UserControl>? Navigated;

    /// <summary>可后退状态变化时触发。</summary>
    public event Action? CanGoBackChanged;

    public string CurrentRoute { get; private set; } = "";
    public object? CurrentParameter { get; private set; }

    public bool CanGoBack => _back.Count > 0;

    public void Register(string route, Func<UserControl> factory) => _factories[route] = factory;

    /// <summary>导航到指定路由。同路由重复导航也会重新触发参数加载。</summary>
    public async void Navigate(string route, object? parameter = null, bool pushHistory = true)
    {
        try
        {
            await NavigateAsync(route, parameter, pushHistory).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // 导航链路里的异常不能让应用崩溃，交给全局处理器提示报错码
            App.ReportError(Errors.ErrorCodes.NavigationFailed, ex);
        }
    }

    public async Task NavigateAsync(string route, object? parameter = null, bool pushHistory = true)
    {
        if (!_factories.TryGetValue(route, out var factory))
        {
            App.ReportError(Errors.ErrorCodes.PageNotFound, new InvalidOperationException(Strings.T("err.detail.unregisteredRoute", route)));
            return;
        }

        // 离开当前页：先让旧页面对称收尾（退订事件 / 停计时器），再切换
        UserControl? currentView = null;
        if (!string.IsNullOrEmpty(CurrentRoute) && _cache.TryGetValue(CurrentRoute, out var oldView))
        {
            currentView = oldView;
            await DetachAsync(CurrentRoute, oldView).ConfigureAwait(true);
        }

        if (!_cache.TryGetValue(route, out var view))
        {
            view = factory();
            _cache[route] = view;
        }

        // 页面自声明「离开后可释放」且目标是另一页面时，从缓存逐出（下次进入重建）
        if (currentView is not null && !ReferenceEquals(currentView, view)
            && currentView is INavigationAware { CanReleaseOnLeave: true })
        {
            Drop(CurrentRoute);
        }

        if (pushHistory && !string.IsNullOrEmpty(CurrentRoute))
        {
            _back.Push((CurrentRoute, CurrentParameter));
            CanGoBackChanged?.Invoke();
        }

        CurrentRoute = route;
        CurrentParameter = parameter;
        Navigated?.Invoke(route, view);

        // 先记「已附加」再进行页面加载：即使 to 钩子中途抛异常，离开时也仍会配对收尾，
        // 不会因为一次异常就留下一个订着单例事件、却再没人来退订的页面。
        _attachedRoute = route;
        MarkUsed(route);

        try
        {
            if (view is INavigationAware aware)
            {
                await aware.OnNavigatedToAsync(parameter).ConfigureAwait(true);
            }
        }
        finally
        {
            // 页面加载失败也不能让缓存撑破上限：收敛这一步放在 finally 里
            await TrimCacheAsync().ConfigureAwait(true);
        }
    }

    /// <summary>
    /// 让一个页面「对称收尾」：调用它的离开钩子（退订事件 / 停计时器 / 放掉大对象）。
    ///
    /// 调用唯一性由 <see cref="_attachedRoute"/> 保证：页面每次进入（含同路由原地重放）只记一次账，
    /// 因此「离开时收尾一次 + 之后被 LRU 逐出时又收尾一次」不可能发生 —— 逐出时这里会识别出
    /// 该页早已收过尾而直接返回。两个方向都不能松：
    /// 漏调 → 页面订着单例事件不放，成了幽灵订阅；重复调 → 页面里那些不幂等的收尾逻辑跑两遍。
    /// </summary>
    private async Task DetachAsync(string route, UserControl view)
    {
        if (!string.Equals(_attachedRoute, route, StringComparison.OrdinalIgnoreCase)) return;

        // 先清标记再调用：钩子内部若又触发一次导航（收尾时跳页），不会递归回到这里
        _attachedRoute = null;
        if (view is INavigationAware aware)
        {
            await aware.OnNavigatedFromAsync().ConfigureAwait(true);
        }
    }

    /// <summary>把路由记为「最近使用」（列表尾 = 最近用过）。</summary>
    private void MarkUsed(string route)
    {
        _usageOrder.RemoveAll(r => string.Equals(r, route, StringComparison.OrdinalIgnoreCase));
        _usageOrder.Add(route);
    }

    /// <summary>从缓存与使用顺序里同时摘掉一个路由（两处都摘，避免 LRU 挑到已不在缓存里的名字）。</summary>
    private void Drop(string route)
    {
        _cache.Remove(route);
        _usageOrder.RemoveAll(r => string.Equals(r, route, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 按 LRU 把常驻页面收敛到 <see cref="MaxCachedPages"/> 以内。当前页永远不逐出
    /// （它正显示在窗口里，逐出等于把用户眼前的页面拆掉）。
    /// </summary>
    private async Task TrimCacheAsync()
    {
        while (_cache.Count > MaxCachedPages)
        {
            string? victim = null;
            UserControl? victimView = null;

            // 使用顺序表里最靠前的、仍在缓存中的非当前页就是最久未用的
            foreach (var route in _usageOrder)
            {
                if (string.Equals(route, CurrentRoute, StringComparison.OrdinalIgnoreCase)) continue;
                if (_cache.TryGetValue(route, out var cached))
                {
                    victim = route;
                    victimView = cached;
                    break;
                }
            }

            // 兜底：使用顺序表与实际缓存理论上同步，万一不同步（如外部误删）就从缓存里挑一个非当前页
            if (victim is null)
            {
                foreach (var (route, cached) in _cache)
                {
                    if (string.Equals(route, CurrentRoute, StringComparison.OrdinalIgnoreCase)) continue;
                    victim = route;
                    victimView = cached;
                    break;
                }
            }

            if (victim is null || victimView is null) return;

            Drop(victim);

            try
            {
                // 逐出前再补一次收尾：正常路径下该页离开时已经退订过（DetachAsync 会识别并跳过），
                // 这里兜的是「那一次离开钩子抛异常中断」的情况 —— 不留幽灵订阅是硬要求。
                await DetachAsync(victim, victimView).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                // 一个旁观页面的收尾失败不能连累本次导航
                App.ReportError(Errors.ErrorCodes.NavigationFailed, ex);
            }
        }
    }

    /// <summary>后退一步；没有历史时什么也不做。</summary>
    public void GoBack()
    {
        if (_back.Count == 0) return;
        var (route, parameter) = _back.Pop();
        CanGoBackChanged?.Invoke();
        Navigate(route, parameter, pushHistory: false);
    }

    /// <summary>清空历史（回到首页时调用，避免历史无限增长）。</summary>
    public void ClearHistory()
    {
        _back.Clear();
        CanGoBackChanged?.Invoke();
    }
}
