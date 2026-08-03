using System.IO;
using System.Text;
using System.Text.Json;

namespace OBS_Helper.Wpf.Services.Host;

/// <summary>
/// 非机密偏好的本地键值存储，取代 Blazor 版的 <c>localStorage</c>。
///
/// 落盘位置：%LocalAppData%\OBS_Helper\prefs.json（明文 JSON，仅存放外观、书签、
/// 连接主机端口等非敏感项；密码与 API Key 一律走 <see cref="HostBridge"/> 的 DPAPI 存储）。
///
/// 实现取舍：
/// <list type="bullet">
///   <item>整份读入内存字典，写入时整体落盘。条目量级在几十条以内，不值得引入数据库。</item>
///   <item>写入走「临时文件 + 覆盖」，避免中途崩溃留下半截 JSON。</item>
///   <item>文件损坏时静默重置为空，不阻塞应用启动——偏好丢失远比启不来好。</item>
/// </list>
/// </summary>
public sealed class LocalStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly string _file;
    private readonly Dictionary<string, string> _items;
    private readonly object _gate = new();

    public LocalStore()
    {
        _file = Path.Combine(HostBridge.AppDataDirectory, "prefs.json");
        _items = Load(_file);
    }

    private static Dictionary<string, string> Load(string file)
    {
        try
        {
            if (!File.Exists(file)) return new Dictionary<string, string>();
            var json = File.ReadAllText(file, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, string>();
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
        }
        catch (Exception)
        {
            return new Dictionary<string, string>();
        }
    }

    private void Flush()
    {
        try
        {
            var json = JsonSerializer.Serialize(_items, JsonOpts);
            var tmp = _file + ".tmp";
            File.WriteAllText(tmp, json, new UTF8Encoding(false));
            File.Copy(tmp, _file, overwrite: true);
            File.Delete(tmp);
        }
        catch (Exception)
        {
            // 磁盘满 / 只读目录：本次会话内的内存值仍然生效。
        }
    }

    /// <summary>读取一项；不存在返回 null。</summary>
    public string? GetItem(string key)
    {
        lock (_gate)
        {
            return _items.TryGetValue(key, out var v) ? v : null;
        }
    }

    /// <summary>写入一项并立即落盘。</summary>
    public void SetItem(string key, string value)
    {
        lock (_gate)
        {
            _items[key] = value ?? "";
            Flush();
        }
    }

    /// <summary>删除一项。</summary>
    public void RemoveItem(string key)
    {
        lock (_gate)
        {
            if (_items.Remove(key)) Flush();
        }
    }

    /// <summary>读取并反序列化一个对象；失败时返回 default。</summary>
    public T? GetObject<T>(string key) where T : class
    {
        var raw = GetItem(key);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(raw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>序列化并写入一个对象。</summary>
    public void SetObject<T>(string key, T value)
    {
        try
        {
            SetItem(key, JsonSerializer.Serialize(value, JsonOpts));
        }
        catch (Exception)
        {
            // 序列化失败（理论上不会发生）：忽略，保持旧值。
        }
    }
}
