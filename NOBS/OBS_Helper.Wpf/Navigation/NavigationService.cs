using System.Windows.Controls;

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
    /// 默认 false 保持全缓存（保滚动位置 / 避免重建）；页面无有价值状态时可声明 true。
    /// 不写死 LRU 上限，由页面自己决定是否可逐出（P1-3）。
    /// </summary>
    bool CanReleaseOnLeave => false;
}

/// <summary>应用内路由名。集中定义，避免各页面拼字符串拼错。</summary>
public static class Routes
{
    public const string Home = "home";
    public const string Search = "search";
    public const string Assistant = "assistant";
    public const string Diagnostic = "diagnostic";
    public const string Setup = "setup";
    public const string Console = "console";
    public const string Guide = "guide";
    public const string Settings = "settings";

    /// <summary>分类页，参数为分类 id（string）。</summary>
    public const string Category = "category";
    /// <summary>问题详情页，参数为问题 id（string）。</summary>
    public const string Problem = "problem";
    /// <summary>日志分析页（从诊断页 / 设置页进入，无独立导航项）。</summary>
    public const string Logs = "logs";

    /// <summary>直播间场景模板页（一级导航）。</summary>
    public const string Templates = "templates";
    /// <summary>OBS 配置管理页：备份 / 导入导出 / 重置（从设置页进入，无独立导航项）。</summary>
    public const string ObsConfig = "obsconfig";

    /// <summary>系统资源监控页（一级导航）。</summary>
    public const string Performance = "performance";

    /// <summary>OBS 插件广场：常用插件的分类导航与官方下载跳转（一级导航）。</summary>
    public const string Plugins = "plugins";
}

/// <summary>
/// 极简的页面导航。
///
/// 取代 Blazor 的 &lt;Router&gt;：路由名 → 页面工厂，页面实例缓存复用（避免每次切页重建列表），
/// 并维护一个前进 / 后退栈供顶栏的「返回」按钮使用。
/// </summary>
public sealed class NavigationService
{
    private readonly Dictionary<string, Func<UserControl>> _factories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, UserControl> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stack<(string Route, object? Parameter)> _back = new();

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
            App.ReportError(Errors.ErrorCodes.PageNotFound, new InvalidOperationException($"未注册的路由：{route}"));
            return;
        }

        // 离开当前页：先让旧页面对称收尾（退订事件 / 停计时器），再切换
        UserControl? currentView = null;
        if (!string.IsNullOrEmpty(CurrentRoute) && _cache.TryGetValue(CurrentRoute, out var oldView))
        {
            currentView = oldView;
            if (oldView is INavigationAware leavingAware)
            {
                await leavingAware.OnNavigatedFromAsync().ConfigureAwait(true);
            }
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
            _cache.Remove(CurrentRoute);
        }

        if (pushHistory && !string.IsNullOrEmpty(CurrentRoute))
        {
            _back.Push((CurrentRoute, CurrentParameter));
            CanGoBackChanged?.Invoke();
        }

        CurrentRoute = route;
        CurrentParameter = parameter;
        Navigated?.Invoke(route, view);

        if (view is INavigationAware aware)
        {
            await aware.OnNavigatedToAsync(parameter).ConfigureAwait(true);
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
