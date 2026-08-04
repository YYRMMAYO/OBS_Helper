using System.Text.Json;
using Microsoft.JSInterop;
using OBS_Helper.Client.Services.Host;

namespace OBS_Helper.Client.Services.Obs;

/// <summary>
/// OBS 连接配置。**不含密码**——密码永远不会出现在这个对象里，
/// 也就不会被序列化进 localStorage。
/// </summary>
public sealed class ObsConnectionSettings
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 4455;

    /// <summary>应用启动时自动尝试连接。</summary>
    public bool AutoConnect { get; set; }

    /// <summary>断线后按指数退避自动重连。</summary>
    public bool AutoReconnect { get; set; } = true;

    /// <summary>是否让桌面壳加密保存密码（无桌面宿主时该项无效）。</summary>
    public bool RememberPassword { get; set; }

    /// <summary>
    /// 构造 WebSocket URL。刻意固定为 <c>ws://</c>：obs-websocket 只监听明文，
    /// 而回环地址在浏览器安全模型中属于可信来源，不会被判定为混合内容。
    /// </summary>
    public string BuildUrl() => $"ws://{Host}:{Port}";
}

/// <summary>
/// 连接配置的读写。非机密项存 localStorage，密码交由桌面壳加密落盘。
/// </summary>
public sealed class ObsSettingsService
{
    private const string SettingsKey = "obs_connection_settings";
    private const string PasswordSecretKey = "obs_websocket_password";

    private readonly IJSRuntime _js;
    private readonly HostBridge _host;

    /// <summary>无桌面宿主时的会话内密码（不落盘，关闭即失效）。</summary>
    private string? _sessionPassword;
    private bool _loaded;

    public ObsSettingsService(IJSRuntime js, HostBridge host)
    {
        _js = js;
        _host = host;
    }

    public ObsConnectionSettings Current { get; private set; } = new();

    /// <summary>宿主是否能加密保存密码。false 时 UI 需提示「本次会话有效」。</summary>
    public bool CanPersistSecrets => _host.IsAvailable;

    public async Task LoadAsync()
    {
        if (_loaded) return;
        _loaded = true;
        await _host.ProbeAsync();
        try
        {
            var json = await _js.InvokeAsync<string>("localStorage.getItem", SettingsKey);
            if (!string.IsNullOrEmpty(json))
            {
                var s = JsonSerializer.Deserialize<ObsConnectionSettings>(json);
                if (s is not null) Current = Sanitize(s);
            }
        }
        catch (Exception)
        {
            // localStorage 不可用：使用默认配置，功能不受影响。
        }
    }

    public async Task SaveAsync(ObsConnectionSettings settings)
    {
        Current = Sanitize(settings);
        try
        {
            await _js.InvokeVoidAsync("localStorage.setItem", SettingsKey, JsonSerializer.Serialize(Current));
        }
        catch (Exception)
        {
            // 静默降级：本次会话内配置仍然生效。
        }
    }

    /// <summary>取得连接密码：优先读宿主加密存储，其次会话内存。</summary>
    public async Task<string?> GetPasswordAsync()
    {
        if (Current.RememberPassword && _host.IsAvailable)
        {
            var v = await _host.GetSecretAsync(PasswordSecretKey);
            if (!string.IsNullOrEmpty(v)) return v;
        }
        return _sessionPassword;
    }

    /// <summary>
    /// 设置密码。<paramref name="remember"/> 为 true 且存在桌面宿主时交给宿主加密落盘，
    /// 否则仅保存在内存中。
    /// </summary>
    public async Task SetPasswordAsync(string? password, bool remember)
    {
        _sessionPassword = password;

        if (!_host.IsAvailable)
        {
            Current.RememberPassword = false;
            return;
        }

        if (remember && !string.IsNullOrEmpty(password))
        {
            await _host.SetSecretAsync(PasswordSecretKey, password);
            Current.RememberPassword = true;
        }
        else
        {
            await _host.DeleteSecretAsync(PasswordSecretKey);
            Current.RememberPassword = false;
        }
        await SaveAsync(Current);
    }

    /// <summary>清空已保存的密码（含宿主存储与内存）。</summary>
    public async Task ClearPasswordAsync()
    {
        _sessionPassword = null;
        if (_host.IsAvailable) await _host.DeleteSecretAsync(PasswordSecretKey);
        Current.RememberPassword = false;
        await SaveAsync(Current);
    }

    /// <summary>收敛非法输入，避免把坏配置写进存储或拼出畸形 URL。</summary>
    private static ObsConnectionSettings Sanitize(ObsConnectionSettings s)
    {
        var host = (s.Host ?? "").Trim();
        if (host.Length == 0) host = "127.0.0.1";
        // 只允许主机名 / IPv4 字面量，杜绝把路径或协议头塞进来
        if (host.Contains('/') || host.Contains(':') || host.Contains(' ')) host = "127.0.0.1";

        var port = s.Port;
        if (port is < 1 or > 65535) port = 4455;

        return new ObsConnectionSettings
        {
            Host = host,
            Port = port,
            AutoConnect = s.AutoConnect,
            AutoReconnect = s.AutoReconnect,
            RememberPassword = s.RememberPassword
        };
    }
}
