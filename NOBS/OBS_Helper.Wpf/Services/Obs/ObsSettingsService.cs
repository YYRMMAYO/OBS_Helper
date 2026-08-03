using OBS_Helper.Wpf.Services.Host;

namespace OBS_Helper.Wpf.Services.Obs;

/// <summary>
/// OBS 连接配置。**不含密码**——密码永远不会出现在这个对象里，
/// 也就不会被序列化进明文偏好文件。
/// </summary>
public sealed class ObsConnectionSettings
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 4455;

    /// <summary>应用启动时自动尝试连接。</summary>
    public bool AutoConnect { get; set; }

    /// <summary>断线后按指数退避自动重连。</summary>
    public bool AutoReconnect { get; set; } = true;

    /// <summary>是否用 DPAPI 加密保存密码。</summary>
    public bool RememberPassword { get; set; }

    /// <summary>
    /// 构造 WebSocket URL。刻意固定为 <c>ws://</c>：obs-websocket 只监听明文。
    /// </summary>
    public string BuildUrl() => $"ws://{Host}:{Port}";

    public ObsConnectionSettings Clone() => new()
    {
        Host = Host,
        Port = Port,
        AutoConnect = AutoConnect,
        AutoReconnect = AutoReconnect,
        RememberPassword = RememberPassword
    };
}

/// <summary>
/// 连接配置的读写。非机密项存 prefs.json，密码交由 DPAPI 加密落盘。
/// </summary>
public sealed class ObsSettingsService
{
    private const string SettingsKey = "obs_connection_settings";
    private const string PasswordSecretKey = "obs_websocket_password";

    private readonly LocalStore _store;
    private readonly HostBridge _host;

    /// <summary>用户勾选「不记住密码」时的会话内密码（不落盘，关闭即失效）。</summary>
    private string? _sessionPassword;
    private bool _loaded;

    public ObsSettingsService(LocalStore store, HostBridge host)
    {
        _store = store;
        _host = host;
    }

    public ObsConnectionSettings Current { get; private set; } = new();

    /// <summary>原生宿主始终能加密保存密码。</summary>
    public bool CanPersistSecrets => _host.IsAvailable;

    public Task LoadAsync()
    {
        if (_loaded) return Task.CompletedTask;
        _loaded = true;

        var s = _store.GetObject<ObsConnectionSettings>(SettingsKey);
        if (s is not null) Current = Sanitize(s);
        return Task.CompletedTask;
    }

    public Task SaveAsync(ObsConnectionSettings settings)
    {
        Current = Sanitize(settings);
        _store.SetObject(SettingsKey, Current);
        return Task.CompletedTask;
    }

    /// <summary>取得连接密码：优先读加密存储，其次会话内存。</summary>
    public async Task<string?> GetPasswordAsync()
    {
        if (Current.RememberPassword)
        {
            var v = await _host.GetSecretAsync(PasswordSecretKey).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(v)) return v;
        }
        return _sessionPassword;
    }

    /// <summary>
    /// 设置密码。<paramref name="remember"/> 为 true 时 DPAPI 加密落盘，否则仅保存在内存中。
    /// </summary>
    public async Task SetPasswordAsync(string? password, bool remember)
    {
        _sessionPassword = password;

        if (remember && !string.IsNullOrEmpty(password))
        {
            await _host.SetSecretAsync(PasswordSecretKey, password).ConfigureAwait(false);
            Current.RememberPassword = true;
        }
        else
        {
            await _host.DeleteSecretAsync(PasswordSecretKey).ConfigureAwait(false);
            Current.RememberPassword = false;
        }
        await SaveAsync(Current).ConfigureAwait(false);
    }

    /// <summary>清空已保存的密码（含加密存储与内存）。</summary>
    public async Task ClearPasswordAsync()
    {
        _sessionPassword = null;
        await _host.DeleteSecretAsync(PasswordSecretKey).ConfigureAwait(false);
        Current.RememberPassword = false;
        await SaveAsync(Current).ConfigureAwait(false);
    }

    /// <summary>是否已经存在一份加密保存的密码（设置页用于显示「已保存」占位）。</summary>
    public async Task<bool> HasStoredPasswordAsync()
        => !string.IsNullOrEmpty(await _host.GetSecretAsync(PasswordSecretKey).ConfigureAwait(false));

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
