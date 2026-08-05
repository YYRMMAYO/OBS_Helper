using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Interop;
using OBS_Helper.Wpf.Models.Shell;
using OBS_Helper.Wpf.Services.Host;
using OBS_Helper.Wpf.Services.Obs;

namespace OBS_Helper.Wpf.Services.Shell;

/// <summary>热键对应的动作。</summary>
public enum HotkeyAction
{
    Record,
    Stream,
    VirtualCam,
    ToggleWindow
}

/// <summary>
/// 全局热键：注册系统级快捷键（RegisterHotKey），窗口不在前台也能控制 OBS。
///
/// 实现要点：
/// <list type="bullet">
///   <item>热键挂在主窗口句柄上（<see cref="HwndSource"/>），WM_HOTKEY 走 WndProc 钩子；</item>
///   <item>键位配置存 <c>prefs.json</c>（非敏感）；保存后重新注册；</item>
///   <item>单个键被其他程序占用时只报那条的错，不影响其余热键与整体功能；</item>
///   <item>WM_HOTKEY 按住会连续触发，加 250ms 防抖。</item>
/// </list>
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private const string StorageKey = "obshelper.hotkeys";
    private const int WmHotkey = 0x0312;

    // Windows 消息参数
    private const uint ModAlt = 0x1;
    private const uint ModControl = 0x2;
    private const uint ModShift = 0x4;
    private const uint ModWin = 0x8;

    // 4 个动作的注册 id（约定固定值，仅进程内使用）
    private static readonly (HotkeyAction Action, int Id)[] Bindings =
    {
        (HotkeyAction.Record, 0xC101),
        (HotkeyAction.Stream, 0xC102),
        (HotkeyAction.VirtualCam, 0xC103),
        (HotkeyAction.ToggleWindow, 0xC104)
    };

    private readonly LocalStore _store;
    private readonly ObsConnectionService _obs;
    private readonly HwndSource? _source;

    private DateTime _lastFire = DateTime.MinValue;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public GlobalHotkeyService(LocalStore store, ObsConnectionService obs)
    {
        _store = store;
        _obs = obs;

        // 主窗口 Show 之后句柄一定已创建（AppServices.InitializeAsync 在 Loaded 里调用）
        var hwnd = new WindowInteropHelper(System.Windows.Application.Current.MainWindow).Handle;
        if (hwnd != IntPtr.Zero)
        {
            _source = HwndSource.FromHwnd(hwnd);
            _source?.AddHook(WndProc);
        }
    }

    /// <summary>配置（含各动作启用开关与键位）。</summary>
    public HotkeySettings Settings { get; private set; } = new();

    /// <summary>注册失败的描述（键位冲突等），供设置页展示。空表示全部注册成功。</summary>
    public List<string> RegistrationErrors { get; } = new();

    /// <summary>「显示/隐藏主窗口」热键触发。</summary>
    public event Action? ToggleWindowRequested;

    /// <summary>注册状态变化（重新注册后），供设置页刷新。</summary>
    public event Action? Changed;

    public void Load()
    {
        var s = _store.GetObject<HotkeySettings>(StorageKey);
        if (s is not null)
        {
            Normalize(s);
            Settings = s;
        }
    }

    /// <summary>保存配置并重新注册。失败（键位冲突等）不抛异常，错误进入 <see cref="RegistrationErrors"/>。</summary>
    public void SaveAndReapply()
    {
        Normalize(Settings);
        _store.SetObject(StorageKey, Settings);
        RegisterAll();
        Changed?.Invoke();
    }

    /// <summary>首次注册（Attach 时调用）。</summary>
    public void Start() => RegisterAll();

    /// <summary>（重新）注册全部启用中的热键，并收集失败原因。</summary>
    public void RegisterAll()
    {
        RegistrationErrors.Clear();
        var hwnd = _source?.Handle ?? IntPtr.Zero;
        if (hwnd == IntPtr.Zero)
        {
            RegistrationErrors.Add("主窗口句柄不可用，热键未注册。");
            return;
        }

        foreach (var (action, id) in Bindings)
        {
            UnregisterHotKey(hwnd, id);
            if (!IsEnabled(action)) continue;

            var binding = BindingFor(action);
            var mods = ModifiersFor(binding);
            if (mods == 0 || !TryGetVk(binding.Key, out var vk))
            {
                RegistrationErrors.Add($"{action}：键位无效（{binding.DisplayName}）。");
                continue;
            }

            if (!RegisterHotKey(hwnd, id, mods, vk))
            {
                var err = Marshal.GetLastWin32Error();
                RegistrationErrors.Add($"{binding.DisplayName} 注册失败（错误码 {err}）：可能已被其他程序占用。");
            }
        }
    }

    public void Dispose()
    {
        if (_source is not null)
        {
            _source.RemoveHook(WndProc);
            var hwnd = _source.Handle;
            if (hwnd != IntPtr.Zero)
                foreach (var (_, id) in Bindings) UnregisterHotKey(hwnd, id);
            // 注意：不 Dispose HwndSource——它由主窗口的 WindowInteropHelper 管理，
            // 手动释放会破坏窗口句柄。
        }
    }

    // ------------------------------------------------------------ 内部

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmHotkey) return IntPtr.Zero;

        var id = wParam.ToInt32();
        var action = Bindings.FirstOrDefault(b => b.Id == id).Action;

        // 按住热键会连续触发 WM_HOTKEY，250ms 防抖
        var now = DateTime.UtcNow;
        if ((now - _lastFire).TotalMilliseconds < 250) { handled = true; return IntPtr.Zero; }
        _lastFire = now;

        handled = true;
        _ = ExecuteAsync(action);
        return IntPtr.Zero;
    }

    private async Task ExecuteAsync(HotkeyAction action)
    {
        try
        {
            switch (action)
            {
                case HotkeyAction.Record:
                    await _obs.ToggleRecordAsync();
                    break;
                case HotkeyAction.Stream:
                    await _obs.ToggleStreamAsync();
                    break;
                case HotkeyAction.VirtualCam:
                    await _obs.ToggleVirtualCamAsync();
                    break;
                case HotkeyAction.ToggleWindow:
                    ToggleWindowRequested?.Invoke();
                    break;
            }
        }
        catch (Exception)
        {
            // 热键动作失败静默：Obs 状态事件会刷新 UI，避免反复打扰
        }
    }

    private bool IsEnabled(HotkeyAction action) => action switch
    {
        HotkeyAction.Record => Settings.RecordEnabled,
        HotkeyAction.Stream => Settings.StreamEnabled,
        HotkeyAction.VirtualCam => Settings.VirtualCamEnabled,
        HotkeyAction.ToggleWindow => Settings.ToggleWindowEnabled,
        _ => false
    };

    private HotkeyBinding BindingFor(HotkeyAction action) => action switch
    {
        HotkeyAction.Record => Settings.Record,
        HotkeyAction.Stream => Settings.Stream,
        HotkeyAction.VirtualCam => Settings.VirtualCam,
        HotkeyAction.ToggleWindow => Settings.ToggleWindow,
        _ => Settings.Record
    };

    private static uint ModifiersFor(HotkeyBinding b)
    {
        uint m = 0;
        if (b.Ctrl) m |= ModControl;
        if (b.Alt) m |= ModAlt;
        if (b.Shift) m |= ModShift;
        if (b.Win) m |= ModWin;
        return m;
    }

    /// <summary>把主键文本转成虚拟键码。支持 A-Z / 0-9 / F1-F12。</summary>
    private static bool TryGetVk(string key, out uint vk)
    {
        vk = 0;
        var k = (key ?? "").Trim().ToUpperInvariant();
        if (k.Length == 0) return false;

        if (k.Length == 1 && k[0] is >= 'A' and <= 'Z')
        {
            vk = (uint)(Keys.A + (k[0] - 'A'));
            return true;
        }
        if (k.Length == 1 && k[0] is >= '0' and <= '9')
        {
            vk = (uint)(Keys.D0 + (k[0] - '0'));
            return true;
        }
        if (k.Length >= 2 && k[0] == 'F' && int.TryParse(k[1..], out var n) && n is >= 1 and <= 12)
        {
            vk = (uint)(Keys.F1 + (n - 1));
            return true;
        }
        return false;
    }

    /// <summary>收敛配置：清掉空主键、Win 键组合需显式允许（默认去勾）等。</summary>
    private static void Normalize(HotkeySettings s)
    {
        foreach (var b in new[] { s.Record, s.Stream, s.VirtualCam, s.ToggleWindow })
        {
            b.Key = (b.Key ?? "").Trim();
            // 只有 Ctrl/Alt/Shift/Win 任意一个作为修饰才注册（裸键太容易误触）
            if (!b.Ctrl && !b.Alt && !b.Shift && !b.Win) b.Ctrl = true;
        }
    }
}
