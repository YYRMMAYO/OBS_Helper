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
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Default
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
            var data = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (data is null)
            {
                PreserveCorrupt(file, "反序列化结果为 null");
                return new Dictionary<string, string>();
            }
            return data;
        }
        catch (Exception ex)
        {
            PreserveCorrupt(file, ex.Message);
            return new Dictionary<string, string>();
        }
    }

    /// <summary>
    /// 读取失败时先把损坏文件另存一份再重置（V3.0）。
    ///
    /// 原来这里是「静默重置为空」：下一次任意一次 <see cref="SetItem"/> 就会用空字典**整份覆盖** prefs.json，
    /// 用户的设置、书签、以及「文件通道改过 basic.ini，重启后可回滚」的待办记录一起没了，
    /// 而日志里一个字的线索都没有。现在至少留下一份可人工恢复的副本 + 一条日志。
    /// </summary>
    private static void PreserveCorrupt(string file, string reason)
    {
        try
        {
            // 带时间戳：同一天里连续两次损坏不会互相覆盖（审查建议），多留几代成本很低
            var corrupt = $"{file}.corrupt-{DateTime.Now:yyyyMMddHHmmss}";
            File.Copy(file, corrupt, overwrite: false);
            FileLogger.Warn("Store", $"prefs.json 读取失败（{reason}），已另存为 {corrupt}，本次会话以空设置启动");
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Store", $"prefs.json 读取失败（{reason}），且另存副本也失败：{ex.Message}");
        }
    }

    private void Flush()
    {
        try
        {
            var json = JsonSerializer.Serialize(_items, JsonOpts);
            var tmp = _file + ".tmp";
            File.WriteAllText(tmp, json, new UTF8Encoding(false));
            try
            {
                if (File.Exists(_file))
                    File.Replace(tmp, _file, null);
                else
                    File.Move(tmp, _file);
            }
            catch
            {
                try { File.Delete(tmp); } catch { /* 清理失败无妨 */ }
                throw; // 本次 Flush 失败，内存值仍然有效，下次写入会重试
            }
        }
        catch (Exception ex)
        {
            // 磁盘满 / 只读目录：本次会话内的内存值仍然生效。
            // 注意：由于上方 throw 已重新抛出 File.Replace/Move 异常，
            // 此 catch 主要捕获序列化 / 临时文件写入阶段的异常。
            // V3.0：写入失败不再完全静默 —— 否则「设置看起来改了、重启后又变回去」无从追查。
            FileLogger.Warn("Store", $"prefs.json 写入失败，本次改动只保留在内存中：{ex.Message}");
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
