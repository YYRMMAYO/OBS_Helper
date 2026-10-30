using System.IO;
using System.Text;
using System.Windows;
using OBS_Helper.Wpf.Models.Shell;
using OBS_Helper.Wpf.Services.Compat;
using OBS_Helper.Wpf.Services.Host;
using OBS_Helper.Wpf.Views;

namespace OBS_Helper.Wpf.Services.Shell;

/// <summary>
/// 提词器服务（V3.1.0 / §12.1.3）：窗口生命周期、设置 / 脚本持久化、热键入口、场景联动。
///
/// 单实例：同一时间只有一个提词器窗口；关闭后再次呼出会恢复位置、字号、速度与脚本。
/// 内容只存本机（<c>%LocalAppData%\OBS_Helper\teleprompter\current.txt</c>），不联网、不上传。
/// </summary>
public sealed class TeleprompterService : IDisposable
{
    private const string SettingsKey = "shell.teleprompter";
    private const string FolderName = "teleprompter";
    private const string ScriptFileName = "current.txt";

    private readonly LocalStore _store;
    private TeleprompterSettings _settings = new();
    private string _script = TeleprompterCore.DefaultScript;
    private TeleprompterWindow? _window;
    private bool _initialized;

    public TeleprompterService(LocalStore store)
    {
        _store = store;
    }

    /// <summary>设置或显示状态变化时触发（供设置页 / 托盘刷新）。</summary>
    public event Action? StateChanged;

    /// <summary>当前设置（已归一化）。</summary>
    public TeleprompterSettings Settings => _settings;

    /// <summary>当前脚本（纯文本，已归一化换行）。</summary>
    public string Script => _script;

    /// <summary>提词器窗口是否已经创建。</summary>
    public bool IsOpen => _window is not null;

    /// <summary>提词器是否可见。</summary>
    public bool IsVisible => _window?.IsVisible == true;

    /// <summary>最近一次捕获排除的结果（窗口未打开时返回 UnsupportedOs）。</summary>
    public CaptureExclusionResult CaptureExclusion =>
        _window?.CaptureExclusionState ?? CaptureExclusionResult.UnsupportedOs;

    /// <summary>脚本文件路径（供「打开所在目录」/ 排障）。</summary>
    public static string ScriptPath => Path.Combine(HostBridge.AppDataDirectory, FolderName, ScriptFileName);

    /// <summary>加载设置与脚本。幂等；应用启动时调用一次。</summary>
    public void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            var loaded = _store.GetObject<TeleprompterSettings>(SettingsKey);
            _settings = TeleprompterCore.Normalize(loaded);
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Teleprompter", $"读取提词器设置失败，已用默认值：{ex.Message}");
            _settings = TeleprompterCore.Normalize(new TeleprompterSettings());
        }

        try
        {
            if (File.Exists(ScriptPath))
                _script = TeleprompterCore.NormalizeScript(File.ReadAllText(ScriptPath, Encoding.UTF8));
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Teleprompter", $"读取提词器脚本失败，已用默认内容：{ex.Message}");
            _script = TeleprompterCore.DefaultScript;
        }
    }

    // ------------------------------------------------------------ 窗口

    /// <summary>呼出提词器（已打开则置前）。</summary>
    public void Show()
    {
        Initialize();

        if (_window is null)
        {
            _window = new TeleprompterWindow(this);
            _window.Closed += OnWindowClosed;
            _window.Show();
        }
        else
        {
            if (!_window.IsVisible) _window.Show();
            if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
            _window.Activate();
        }

        StateChanged?.Invoke();
    }

    /// <summary>隐藏提词器（不销毁窗口，滚动位置保留）。</summary>
    public void Hide()
    {
        _window?.Hide();
        StateChanged?.Invoke();
    }

    /// <summary>呼出 / 隐藏。</summary>
    public void Toggle()
    {
        if (IsVisible) Hide();
        else Show();
    }

    /// <summary>播放 / 暂停（全局热键）。</summary>
    public void TogglePlayPause()
    {
        if (_window is null) Show();
        _window?.TogglePlayPause();
    }

    /// <summary>提词器是否正在滚动（未打开时为 false）。</summary>
    public bool IsPlaying => _window?.IsPlaying == true;

    /// <summary>手机端遥控播放/暂停（V3.4.0 / M05）：窗口未打开时先呼出。</summary>
    public void SetPlaying(bool playing)
    {
        if (_window is null) Show();
        _window?.SetPlayingState(playing);
        StateChanged?.Invoke();
    }

    /// <summary>手机端「回到开头」。</summary>
    public void ScrollToStart()
    {
        if (_window is null) Show();
        _window?.ScrollToStart();
        StateChanged?.Invoke();
    }

    /// <summary>切换点击穿透（开启后靠全局热键关掉）。</summary>
    public void ToggleClickThrough()
    {
        if (_window is null) Show();
        _window?.SetClickThrough(!_window.IsClickThrough, save: true);
    }

    /// <summary>场景切换时把提词器滚到对应段落（只滚动，不切 OBS 场景）。</summary>
    public void ApplyScene(string? sceneName)
    {
        if (_window is null || string.IsNullOrWhiteSpace(sceneName)) return;
        _window.ApplyScene(sceneName!);
    }

    /// <summary>3-2-1 倒计时。</summary>
    public void StartCountdown(int seconds = 3)
    {
        if (_window is null) Show();
        _window?.StartCountdown(seconds);
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (_window is not null)
        {
            // 保存位置 / 尺寸（关闭即记住，下次呼出同位置）
            var s = _settings.Clone();
            s.Left = _window.Left;
            s.Top = _window.Top;
            s.Width = _window.Width;
            s.Height = _window.Height;
            SaveSettings(s, applyToWindow: false);
            _window.Closed -= OnWindowClosed;
            _window = null;
        }
        StateChanged?.Invoke();
    }

    // ------------------------------------------------------------ 持久化

    /// <summary>保存设置并立即应用到已打开的窗口。</summary>
    public void SaveSettings(TeleprompterSettings settings, bool applyToWindow = true)
    {
        _settings = TeleprompterCore.Normalize(settings);
        try { _store.SetObject(SettingsKey, _settings); }
        catch (Exception ex) { FileLogger.Warn("Teleprompter", $"保存提词器设置失败：{ex.Message}"); }

        if (applyToWindow) _window?.ApplySettings(_settings);
        StateChanged?.Invoke();
    }

    /// <summary>保存脚本（原子写；自动保存也走这里）。</summary>
    public void SaveScript(string? text)
    {
        _script = TeleprompterCore.NormalizeScript(text);
        try
        {
            var path = ScriptPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, _script, new UTF8Encoding(false));
            if (File.Exists(path))
            {
                try { File.Replace(tmp, path, null); }
                catch (Exception) { File.Copy(tmp, path, overwrite: true); File.Delete(tmp); }
            }
            else
            {
                File.Move(tmp, path);
            }
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Teleprompter", $"保存提词器脚本失败：{ex.Message}");
        }
    }

    /// <summary>读取脚本文件（用户手动替换后重新加载）。</summary>
    public string ReloadScript()
    {
        try
        {
            if (File.Exists(ScriptPath))
                _script = TeleprompterCore.NormalizeScript(File.ReadAllText(ScriptPath, Encoding.UTF8));
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Teleprompter", $"重新读取提词器脚本失败：{ex.Message}");
        }
        _window?.SetScript(_script);
        StateChanged?.Invoke();
        return _script;
    }

    public void Dispose()
    {
        if (_window is null) return;
        try
        {
            _window.Closed -= OnWindowClosed;
            _window.Close();
        }
        catch (Exception)
        {
            // 退出路径，忽略
        }
        _window = null;
    }
}
