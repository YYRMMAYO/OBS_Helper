using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using OBS_Helper.Wpf.Models.Shell;
using OBS_Helper.Wpf.Services.Host;
using OBS_Helper.Wpf.Services.Obs;

namespace OBS_Helper.Wpf.Services.Shell;

/// <summary>
/// 场景自动切换：每 1 秒读一次前台窗口标题，命中规则时把 OBS 切到对应场景。
///
/// 设计要点：
/// <list type="bullet">
///   <item>规则按顺序匹配，第一条命中生效；只匹配「前台窗口」的标题（GetForegroundWindow）；</item>
///   <item>去抖：同一规则连续命中不重复发请求；规则切换之间至少间隔 3 秒，避免 OBS 还没反应过来就连发；</item>
///   <item>正则模式编译失败时该条规则静默跳过，不影响其它规则；</item>
///   <item>仅在已连接 OBS 且目标场景确实不同时才发起切换。</item>
/// </list>
/// 纯本地实现：不调用任何系统 API 之外的能力，配置存 prefs.json。
/// </summary>
public sealed class SceneAutoSwitcher : IDisposable
{
    private const string StorageKey = "obshelper.autoswitch";
    private const int PollMs = 1000;
    private const int MinSwitchGapMs = 3000;
    private const int MaxTitleLength = 512;

    private readonly LocalStore _store;
    private readonly ObsConnectionService _obs;

    private CancellationTokenSource? _cts;
    private Task? _loop;

    // 去抖状态
    private string? _activeRuleId;
    private DateTime _lastSwitch = DateTime.MinValue;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    public SceneAutoSwitcher(LocalStore store, ObsConnectionService obs)
    {
        _store = store;
        _obs = obs;
    }

    public AutoSwitchSettings Settings { get; private set; } = new();

    /// <summary>规则变化（设置页保存）时触发。</summary>
    public event Action? Changed;

    public void Load()
    {
        var s = _store.GetObject<AutoSwitchSettings>(StorageKey);
        if (s is not null) Settings = s;
    }

    /// <summary>保存规则并即时生效。</summary>
    public void Save()
    {
        _store.SetObject(StorageKey, Settings);
        _activeRuleId = null;   // 规则变更后重新评估
        Changed?.Invoke();
    }

    /// <summary>启动轮询。</summary>
    public void Start()
    {
        if (_loop is { IsCompleted: false }) return;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => LoopAsync(_cts.Token));
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch (Exception) { }
        try { _cts?.Dispose(); } catch (Exception) { }
        _cts = null;
        _loop = null;
    }

    public void Dispose() => Stop();

    // ------------------------------------------------------------ 轮询

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { Tick(); }
            catch (Exception) { /* 单轮失败不影响后续 */ }
            try { await Task.Delay(PollMs, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private void Tick()
    {
        if (!Settings.Enabled || !_obs.IsConnected) return;

        var title = GetForegroundWindowTitle();
        if (string.IsNullOrWhiteSpace(title)) return;

        var rule = MatchRule(title);
        if (rule is null)
        {
            // 前台窗口不再匹配任何规则：解除当前生效规则，窗口切回时重新触发
            _activeRuleId = null;
            return;
        }

        // 同一条规则仍在生效：不重复切换
        if (rule.Id == _activeRuleId) return;

        // 目标就是当前场景：只记录生效状态，不发请求
        if (string.Equals(_obs.CurrentScene, rule.SceneName, StringComparison.OrdinalIgnoreCase))
        {
            _activeRuleId = rule.Id;
            return;
        }

        // 切换间隔去抖
        if ((DateTime.UtcNow - _lastSwitch).TotalMilliseconds < MinSwitchGapMs) return;

        _activeRuleId = rule.Id;
        _lastSwitch = DateTime.UtcNow;
        _ = FireAndForgetAsync(() => _obs.SetSceneAsync(rule.SceneName));
    }

    private AutoSwitchRule? MatchRule(string title)
    {
        foreach (var rule in Settings.Rules)
        {
            if (!rule.Enabled || string.IsNullOrWhiteSpace(rule.Pattern) || string.IsNullOrWhiteSpace(rule.SceneName))
                continue;

            if (rule.UseRegex)
            {
                try
                {
                    if (Regex.IsMatch(title, rule.Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                        return rule;
                }
                catch (ArgumentException)
                {
                    // 用户写的正则不合法：跳过该条，不影响其它规则
                }
            }
            else
            {
                if (title.Contains(rule.Pattern, StringComparison.OrdinalIgnoreCase))
                    return rule;
            }
        }
        return null;
    }

    private static string GetForegroundWindowTitle()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return "";
        var sb = new StringBuilder(MaxTitleLength);
        GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static async Task FireAndForgetAsync(Func<Task<Models.Obs.ObsRequestResult>> action)
    {
        try { await action(); }
        catch (Exception) { /* 切换失败静默：状态事件会刷新 UI */ }
    }
}
