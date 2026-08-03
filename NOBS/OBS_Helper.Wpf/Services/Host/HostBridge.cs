using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OBS_Helper.Wpf.Services.Host;

/// <summary>宿主运行环境信息。</summary>
public sealed class HostEnvironment
{
    public string Platform { get; set; } = "windows";
    public string AppVersion { get; set; } = "";

    /// <summary>本机 OBS 日志目录。</summary>
    public string ObsLogDirectory { get; set; } = "";
    public bool LogDirectoryExists { get; set; }
}

/// <summary>OBS 日志文件条目。</summary>
public sealed class HostLogFile
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public long Size { get; set; }

    /// <summary>最后修改时间（Unix 毫秒）。</summary>
    public long Modified { get; set; }

    public DateTime ModifiedLocal => Modified <= 0
        ? DateTime.MinValue
        : DateTimeOffset.FromUnixTimeMilliseconds(Modified).LocalDateTime;

    public string ModifiedText => Modified <= 0 ? "—" : ModifiedLocal.ToString("yyyy-MM-dd HH:mm");

    public string SizeText => Size >= 1024 * 1024
        ? $"{Size / 1024.0 / 1024.0:0.0} MB"
        : $"{Size / 1024.0:0.0} KB";
}

/// <summary>
/// 桌面宿主能力（机密存储 / OBS 日志访问 / 环境信息 / 云端 AI 转发 / 打开外链）。
///
/// WPF 版是原生进程，能力直接在本类型内实现，不再经过 WebView 消息通道。
/// 保留 async 签名与原 Blazor 版一致，是为了让上层诊断编排、设置服务无需改动即可复用。
///
/// 安全设计（沿用桌面壳的纵深防御）：
/// <list type="bullet">
///   <item><b>机密加密</b>：密码 / API Key 用 DPAPI（CurrentUser 范围 + 应用附加熵）加密后写入
///         %LocalAppData%\OBS_Helper\secrets.dat，换用户或换机器均无法解密。</item>
///   <item><b>目录限定</b>：只允许读取 %AppData%\obs-studio\logs 与 crashes 下的 .txt/.log，
///         解析真实路径后二次校验，杜绝 <c>..</c> 穿越。</item>
///   <item><b>大小上限</b>：单个日志最多读 8 MB，超出只读尾部（关键错误集中在末尾）。</item>
///   <item><b>SSRF 防护</b>：云端 AI 强制 https，且拒绝内网 / 回环地址。</item>
/// </list>
/// </summary>
public sealed class HostBridge
{
    private const long MaxLogBytes = 8L * 1024 * 1024;
    private const int MaxSecretLength = 4096;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>DPAPI 附加熵：与应用绑定，降低同一用户下其他程序解密的可能。</summary>
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("OBS_Helper.SecretStore.v1");

    private readonly SemaphoreSlim _secretLock = new(1, 1);

    /// <summary>原生宿主始终可用。保留该属性是为兼容上层「无宿主降级」分支。</summary>
    public bool IsAvailable => true;

    public string Platform => "windows";

    /// <summary>兼容旧接口：原生进程无需探测，恒为 true。</summary>
    public Task<bool> ProbeAsync() => Task.FromResult(true);

    // ------------------------------------------------------------ 应用数据目录

    /// <summary>应用私有数据目录（%LocalAppData%\OBS_Helper），不存在时自动创建。</summary>
    public static string AppDataDirectory
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OBS_Helper");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private static string SecretsFile => Path.Combine(AppDataDirectory, "secrets.dat");

    // ------------------------------------------------------------ 机密存储

    private static Dictionary<string, string> LoadSecrets()
    {
        try
        {
            if (!File.Exists(SecretsFile)) return new Dictionary<string, string>();
            var encrypted = File.ReadAllBytes(SecretsFile);
            if (encrypted.Length == 0) return new Dictionary<string, string>();

            var plain = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(plain);
            Array.Clear(plain, 0, plain.Length);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
        }
        catch (Exception)
        {
            // 文件损坏 / 换了用户账户导致无法解密：当作空存储，用户重新输入即可。
            return new Dictionary<string, string>();
        }
    }

    private static void SaveSecrets(Dictionary<string, string> secrets)
    {
        var json = JsonSerializer.Serialize(secrets);
        var plain = Encoding.UTF8.GetBytes(json);
        var encrypted = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
        Array.Clear(plain, 0, plain.Length);

        // 先写临时文件再替换，避免写入过程中断电导致存储文件损坏
        var tmp = SecretsFile + ".tmp";
        File.WriteAllBytes(tmp, encrypted);
        File.Copy(tmp, SecretsFile, overwrite: true);
        File.Delete(tmp);
    }

    private static void ValidateSecretKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128)
            throw new ArgumentException("机密键名非法。");
        foreach (var c in key)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '.' && c != '_' && c != '-')
                throw new ArgumentException("机密键名包含非法字符。");
        }
    }

    /// <summary>写入一条机密（DPAPI 加密后落盘）。空值等同删除。</summary>
    public async Task<bool> SetSecretAsync(string key, string value)
    {
        await _secretLock.WaitAsync().ConfigureAwait(false);
        try
        {
            ValidateSecretKey(key);
            if (value.Length > MaxSecretLength) throw new ArgumentException("机密内容过长。");

            var s = LoadSecrets();
            if (value.Length == 0)
            {
                if (s.Remove(key)) SaveSecrets(s);
            }
            else
            {
                s[key] = value;
                SaveSecrets(s);
            }
            return true;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            _secretLock.Release();
        }
    }

    /// <summary>读取一条机密；不存在时返回 null。</summary>
    public async Task<string?> GetSecretAsync(string key)
    {
        await _secretLock.WaitAsync().ConfigureAwait(false);
        try
        {
            ValidateSecretKey(key);
            var s = LoadSecrets();
            return s.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v) ? v : null;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            _secretLock.Release();
        }
    }

    /// <summary>删除一条机密。</summary>
    public async Task<bool> DeleteSecretAsync(string key)
    {
        await _secretLock.WaitAsync().ConfigureAwait(false);
        try
        {
            ValidateSecretKey(key);
            var s = LoadSecrets();
            if (s.Remove(key)) SaveSecrets(s);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            _secretLock.Release();
        }
    }

    /// <summary>同步读取机密。仅供云端 AI 转发内部使用，避免密钥在异步链上多次复制。</summary>
    private static string SecretGetRaw(string key)
    {
        ValidateSecretKey(key);
        var s = LoadSecrets();
        return s.TryGetValue(key, out var v) ? v : "";
    }

    // ------------------------------------------------------------ 日志访问

    /// <summary>OBS 在 Windows 上的日志目录。</summary>
    public static string ObsLogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "obs-studio", "logs");

    /// <summary>OBS 崩溃报告目录（同样允许读取，用于崩溃类问题诊断）。</summary>
    public static string ObsCrashDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "obs-studio", "crashes");

    /// <summary>列出本机 OBS 日志与崩溃报告（各取最近 20 条，按修改时间倒序）。</summary>
    public Task<List<HostLogFile>> ListObsLogsAsync() => Task.Run(() =>
    {
        var items = new List<HostLogFile>();
        foreach (var dir in new[] { ObsLogDirectory, ObsCrashDirectory })
        {
            if (!Directory.Exists(dir)) continue;
            try
            {
                var files = new DirectoryInfo(dir)
                    .GetFiles()
                    .Where(f => f.Extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)
                             || f.Extension.Equals(".log", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .Take(20);

                foreach (var f in files)
                {
                    items.Add(new HostLogFile
                    {
                        Name = f.Name,
                        Path = f.FullName,
                        Size = f.Length,
                        Modified = new DateTimeOffset(f.LastWriteTimeUtc).ToUnixTimeMilliseconds()
                    });
                }
            }
            catch (Exception)
            {
                // 目录权限异常：跳过该目录，不影响另一个目录的枚举。
            }
        }
        return items;
    });

    /// <summary>校验目标路径确实位于允许的目录内（已解析 .. 之后）。</summary>
    private static bool IsUnderAllowedDirectory(string fullPath)
    {
        foreach (var dir in new[] { ObsLogDirectory, ObsCrashDirectory })
        {
            var root = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>读取指定日志文件；只允许 OBS 日志目录内的 .txt/.log，超大文件只读尾部 8MB。</summary>
    public Task<string?> ReadObsLogAsync(string path) => Task.Run<string?>(() =>
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            var full = Path.GetFullPath(path);
            if (!IsUnderAllowedDirectory(full)) return null;

            var ext = Path.GetExtension(full);
            if (!ext.Equals(".txt", StringComparison.OrdinalIgnoreCase) &&
                !ext.Equals(".log", StringComparison.OrdinalIgnoreCase))
                return null;

            if (!File.Exists(full)) return null;

            var info = new FileInfo(full);
            using var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (info.Length > MaxLogBytes)
            {
                fs.Seek(info.Length - MaxLogBytes, SeekOrigin.Begin);
            }
            using var sr = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return sr.ReadToEnd();
        }
        catch (Exception)
        {
            return null;
        }
    });

    // ------------------------------------------------------------ 环境信息

    /// <summary>应用版本号（取自程序集版本）。</summary>
    public static string AppVersion
        => typeof(HostBridge).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    public Task<HostEnvironment> GetEnvironmentAsync() => Task.FromResult(new HostEnvironment
    {
        Platform = "windows",
        AppVersion = AppVersion,
        ObsLogDirectory = ObsLogDirectory,
        LogDirectoryExists = Directory.Exists(ObsLogDirectory)
    });

    // ------------------------------------------------------------ 打开外链 / 目录

    /// <summary>用系统默认浏览器打开外链（仅 http/https）。</summary>
    public Task<bool> OpenExternalAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return Task.FromResult(false);
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return Task.FromResult(false);
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            return Task.FromResult(true);
        }
        catch (Exception)
        {
            return Task.FromResult(false);
        }
    }

    /// <summary>在资源管理器中打开一个本地目录（WPF 版新增：日志页「打开日志目录」）。</summary>
    public bool OpenFolder(string directory)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return false;
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{directory}\"") { UseShellExecute = true });
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // -------------------------------------------------- 云端 AI 转发（可选）

    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(120)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OBS-Helper/1.0");
        return client;
    }

    /// <summary>拦截指向本机 / 内网的地址，降低 SSRF 风险。</summary>
    public static bool IsPrivateHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return true;
        var h = host.Trim().Trim('[', ']').ToLowerInvariant();

        if (h == "localhost" || h.EndsWith(".localhost", StringComparison.Ordinal)
            || h.EndsWith(".local", StringComparison.Ordinal)
            || h.EndsWith(".internal", StringComparison.Ordinal))
            return true;

        if (IPAddress.TryParse(h, out var ip))
        {
            if (IPAddress.IsLoopback(ip)) return true;
            var b = ip.GetAddressBytes();
            if (b.Length == 4)
            {
                if (b[0] == 10) return true;                                  // 10/8
                if (b[0] == 192 && b[1] == 168) return true;                  // 192.168/16
                if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;     // 172.16/12
                if (b[0] == 169 && b[1] == 254) return true;                  // 169.254/16
                if (b[0] == 0) return true;
            }
            else if (b.Length == 16)
            {
                // fc00::/7（唯一本地地址）与 fe80::/10（链路本地）
                if ((b[0] & 0xFE) == 0xFC) return true;
                if (b[0] == 0xFE && (b[1] & 0xC0) == 0x80) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 转发一次云端 AI 请求。API Key 由本方法从加密存储取出后拼装 Authorization 头，
    /// 调用方（诊断引擎）只知道键名，密钥不会流经 UI 层。
    /// </summary>
    /// <param name="url">https 的 chat/completions 接口地址。</param>
    /// <param name="secretKey">API Key 在机密存储中的键名。</param>
    /// <param name="body">完整的请求体 JSON（不含鉴权信息）。</param>
    /// <returns>响应体原文；失败时抛出异常，异常消息可直接展示给用户。</returns>
    public async Task<string> AiChatAsync(string url, string secretKey, string body)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new ArgumentException("接口地址不合法。");
        if (uri.Scheme != Uri.UriSchemeHttps)
            throw new UnauthorizedAccessException("云端 AI 接口必须使用 https。");
        if (IsPrivateHost(uri.Host))
            throw new UnauthorizedAccessException("出于安全考虑，不允许请求内网或本机地址。");
        if (string.IsNullOrWhiteSpace(body))
            throw new ArgumentException("请求体为空。");

        string apiKey;
        await _secretLock.WaitAsync().ConfigureAwait(false);
        try
        {
            apiKey = SecretGetRaw(secretKey);
        }
        finally
        {
            _secretLock.Release();
        }

        if (string.IsNullOrEmpty(apiKey))
            throw new InvalidOperationException("尚未配置 API Key。");
        if (apiKey.Any(char.IsControl))
            throw new ArgumentException("API Key 含有非法字符。");

        using var req = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseContentRead, cts.Token)
                                   .ConfigureAwait(false);

        var text = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            // 只回传状态码与响应体，绝不把 Authorization 头写进任何日志或错误信息
            throw new HttpRequestException($"云端 AI 请求失败（HTTP {(int)resp.StatusCode}）: {Truncate(text, 500)}");
        }
        return text;
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max] + "…");
}
