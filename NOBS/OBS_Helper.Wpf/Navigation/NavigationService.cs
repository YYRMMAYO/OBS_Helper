using System.Windows.Controls;

namespace OBS_Helper.Wpf.Navigation;

/// <summary>
/// 页面在被导航到时的回调。页面若需要按参数加载数据（分类页、问题详情页），实现本接口。
/// </summary>
public interface INavigationAware
{
    /// <summary>导航到本页时调用。<paramref name="parameter"/> 为路由参数，可能为 null。</summary>
    Task OnNavigatedToAsync(object? parameter);
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

        if (!_cache.TryGetValue(route, out var view))
        {
            view = factory();
            _cache[route] = view;
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
