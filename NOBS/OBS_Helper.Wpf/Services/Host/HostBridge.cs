using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
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

        // 先写临时文件再原子替换（File.Replace 在目标已存在时是原子操作，
        // 不存在时退化为 Move；避免 File.Copy+Delete 中间的窗口因崩溃丢失数据）
        var tmp = SecretsFile + ".tmp";
        File.WriteAllBytes(tmp, encrypted);
        try
        {
            if (File.Exists(SecretsFile))
                File.Replace(tmp, SecretsFile, null);
            else
                File.Move(tmp, SecretsFile);
        }
        catch
        {
            try { File.Delete(tmp); } catch { /* 清理失败无妨 */ }
            throw;
        }
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
            // 用 ArgumentList 而非字符串拼接，避免路径中的特殊字符被 shell 解析
            var psi = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                UseShellExecute = true
            };
            psi.ArgumentList.Add(directory);
            Process.Start(psi);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // -------------------------------------------------- 云端 AI 转发（可选）

    /// <summary>云端 AI 响应体大小上限：诊断结论一般几十 KB，2MB 足以容纳任何合法返回。</summary>
    private const long MaxAiResponseBytes = 2L * 1024 * 1024;

    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        // 用 SocketsHttpHandler + ConnectCallback：TCP 连接建立后校验「实际解析出的」远端 IP。
        // 这是对 IsPrivateHost 字符串检查的补充——域名可以被解析到内网地址（DNS rebinding），
        // 仅检查 URL 里的 host 挡不住这类绕过。
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            ConnectCallback = async (ctx, ct) =>
            {
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    // URL 直接是 IP 字面量时 ctx.DnsEndPoint 为 null，回退成从请求 URI 重建终点
                    var endpoint = ctx.DnsEndPoint
                        ?? new DnsEndPoint(ctx.InitialRequestMessage.RequestUri!.Host, ctx.InitialRequestMessage.RequestUri.Port);

                    await socket.ConnectAsync(endpoint, ct).ConfigureAwait(false);
                    if (socket.RemoteEndPoint is IPEndPoint remote && IsPrivateIp(remote.Address))
                    {
                        socket.Dispose();
                        throw new UnauthorizedAccessException("拒绝连接本机 / 内网地址（含解析后的实际 IP）。");
                    }
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }
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

        return IPAddress.TryParse(h, out var ip) && IsPrivateIp(ip);
    }

    /// <summary>
    /// 判断一个 IP 是否属于本机 / 内网 / 不可路由地址。
    /// 同时处理 IPv4-mapped IPv6（<c>::ffff:127.0.0.1</c>）与 IPv4-compatible（<c>::1.2.3.4</c>），
    /// 二者会按内嵌的 IPv4 判定，避免绕过私网检查。
    /// </summary>
    private static bool IsPrivateIp(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return true;   // 127/8 与 ::1
        if (ip.Equals(IPAddress.IPv6Any)) return true; // ::（未指定地址，不可路由）

        var b = ip.GetAddressBytes();

        if (b.Length == 4)
        {
            if (b[0] == 0) return true;                                       // 0/8
            if (b[0] == 10) return true;                                      // 10/8
            if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) return true;        // 100.64.0.0/10 CGNAT
            if (b[0] == 127) return true;                                     // 127/8
            if (b[0] == 169 && b[1] == 254) return true;                      // 169.254/16 链路本地
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;         // 172.16/12
            if (b[0] == 192 && b[1] == 168) return true;                      // 192.168/16
            return false;
        }

        if (b.Length == 16)
        {
            // IPv4-mapped（::ffff:a.b.c.d）与 IPv4-compatible（::a.b.c.d）：前 10 字节全 0
            bool zeroHead = true;
            for (int i = 0; i < 10; i++)
            {
                if (b[i] != 0) { zeroHead = false; break; }
            }
            if (zeroHead)
            {
                if (b[10] == 0xFF && b[11] == 0xFF)   // mapped
                    return IsPrivateIp(new IPAddress(new[] { b[12], b[13], b[14], b[15] }));
                if (b[10] == 0 && b[11] == 0)         // compatible
                    return IsPrivateIp(new IPAddress(new[] { b[12], b[13], b[14], b[15] }));
            }

            // fc00::/7（唯一本地地址）
            if ((b[0] & 0xFE) == 0xFC) return true;
            // fe80::/10 与 fec0::/10（链路本地 / 站点本地）
            if (b[0] == 0xFE && (b[1] & 0xC0) == 0x80) return true;
            if (b[0] == 0xFE && (b[1] & 0xC0) == 0xC0) return true;
            return false;
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

        // Authorization 头已拼装完毕，显式释放 apiKey 引用以缩小密钥在托管内存中的窗口。
        // 注意：AuthenticationHeaderValue 构造时会内部分配一份副本，apiKey 设为 null
        // 不影响请求发送；真正的限制在于 .NET 字符串不可变性——GC 回收之前密钥无法从堆上
        // 擦除，这是托管语言共有的局限。
        apiKey = null!;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token)
                                   .ConfigureAwait(false);

        var text = await ReadBodyLimitedAsync(resp.Content, MaxAiResponseBytes, cts.Token).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            // 只回传状态码与响应体，绝不把 Authorization 头写进任何日志或错误信息
            throw new HttpRequestException($"云端 AI 请求失败（HTTP {(int)resp.StatusCode}）: {Truncate(text, 500)}");
        }
        return text;
    }

    /// <summary>限量读取响应体，防止恶意 / 异常服务器返回超大内容撑爆内存。</summary>
    private static async Task<string> ReadBodyLimitedAsync(HttpContent content, long maxBytes, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var sb = new StringBuilder();
        var buf = new char[8192];
        long total = 0;
        while (true)
        {
            var n = await reader.ReadAsync(buf.AsMemory(), ct).ConfigureAwait(false);
            if (n == 0) break;
            total += n;
            if (total > maxBytes)
                throw new HttpRequestException($"云端响应体过大（>{maxBytes / (1024 * 1024)}MB），已中止读取。");
            sb.Append(buf, 0, n);
        }
        return sb.ToString();
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max] + "…");
}
