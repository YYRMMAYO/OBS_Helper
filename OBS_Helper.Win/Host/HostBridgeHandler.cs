using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OBS_Helper.Win.Host
{
    /// <summary>
    /// Windows 桌面壳的宿主命令处理器（对应前端 wwwroot/js/hostbridge.js）。
    ///
    /// 设计原则（纵深防御）：
    /// <list type="bullet">
    ///   <item><b>白名单</b>：只认识固定的几条命令，未知命令直接拒绝。</item>
    ///   <item><b>目录限定</b>：读日志只允许 %AppData%\obs-studio\logs 及 crashes 目录下的
    ///         .txt/.log 文件，且解析真实路径后再次校验，杜绝 <c>..</c> 穿越。</item>
    ///   <item><b>机密加密</b>：密码 / API Key 用 DPAPI（CurrentUser 范围）加密后写入
    ///         %LocalAppData%\OBS_Helper\secrets.dat，其他用户与其他机器都无法解密。</item>
    ///   <item><b>大小上限</b>：单个日志最多读取 8 MB，避免超大文件撑爆 WebView 消息通道。</item>
    /// </list>
    /// </summary>
    internal static class HostBridgeHandler
    {
        private const long MaxLogBytes = 8L * 1024 * 1024;
        private const int MaxSecretLength = 4096;

        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        /// <summary>DPAPI 附加熵：与应用绑定，降低同一用户下其他程序误/恶意解密的可能。</summary>
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("OBS_Helper.SecretStore.v1");

        /// <summary>
        /// 处理一条来自 WebView 的命令，返回要回传的 JSON 字符串。
        /// 任何异常都会被转换为 <c>{ok:false,error:...}</c>，绝不让宿主进程崩溃。
        /// </summary>
        public static async Task<string> HandleAsync(string rawMessage)
        {
            string id = "";
            try
            {
                string cmd, payloadJson;
                using (var doc = JsonDocument.Parse(rawMessage))
                {
                    var root = doc.RootElement;
                    id = GetString(root, "id");
                    cmd = GetString(root, "cmd");
                    payloadJson = GetString(root, "payload");
                }
                if (string.IsNullOrEmpty(payloadJson)) payloadJson = "{}";

                string result;
                using (var payloadDoc = JsonDocument.Parse(payloadJson))
                {
                    var p = payloadDoc.RootElement;

                    if (cmd == "ai.chat")
                    {
                        // 唯一需要走网络的命令，单独异步处理
                        result = await AiChatAsync(
                            GetString(p, "url"),
                            GetString(p, "secretKey"),
                            GetString(p, "body")).ConfigureAwait(false);
                    }
                    else
                    {
                    result = cmd switch
                    {
                        "secret.set" => SecretSet(GetString(p, "key"), GetString(p, "value")),
                        "secret.get" => SecretGet(GetString(p, "key")),
                        "secret.delete" => SecretDelete(GetString(p, "key")),
                        "logs.list" => LogsList(),
                        "logs.read" => LogsRead(GetString(p, "path")),
                        "env.info" => EnvInfo(),
                        "shell.open" => ShellOpen(GetString(p, "url")),
                        "system.info" => SystemInfo(),
                        "obs.latestVersion" => ObsLatestVersion(),
                        "config.list" => ConfigList(GetString(p, "path")),
                        "config.read" => ConfigRead(GetString(p, "path")),
                        _ => throw new InvalidOperationException("未知命令: " + cmd)
                    };
                    }
                }

                return Reply(id, true, result, null);
            }
            catch (Exception ex)
            {
                return Reply(id, false, null, ex.Message);
            }
        }

        private static string Reply(string id, bool ok, string? result, string? error)
        {
            return JsonSerializer.Serialize(new { id, ok, result, error }, JsonOpts);
        }

        private static string GetString(JsonElement e, string name)
            => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                ? (v.GetString() ?? "") : "";

        // ------------------------------------------------------ 机密存储

        private static string SecretsFile
        {
            get
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "OBS_Helper");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "secrets.dat");
            }
        }

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
            // 只允许「字母 / 数字 / . _ -」，与 macOS 钥匙串实现保持同一套约束
            foreach (var c in key)
            {
                if (!char.IsAsciiLetterOrDigit(c) && c != '.' && c != '_' && c != '-')
                    throw new ArgumentException("机密键名包含非法字符。");
            }
        }

        private static string SecretSet(string key, string value)
        {
            ValidateSecretKey(key);
            if (value.Length > MaxSecretLength) throw new ArgumentException("机密内容过长。");

            // 空值等同删除，避免存储里留下空条目（与 macOS 钥匙串实现保持一致）
            if (value.Length == 0) return SecretDelete(key);

            var s = LoadSecrets();
            s[key] = value;
            SaveSecrets(s);
            return "";
        }

        private static string SecretGet(string key)
        {
            ValidateSecretKey(key);
            var s = LoadSecrets();
            return s.TryGetValue(key, out var v) ? v : "";
        }

        private static string SecretDelete(string key)
        {
            ValidateSecretKey(key);
            var s = LoadSecrets();
            if (s.Remove(key)) SaveSecrets(s);
            return "";
        }

        // -------------------------------------------------------- 日志访问

        /// <summary>OBS 在 Windows 上的日志目录。</summary>
        internal static string ObsLogDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "obs-studio", "logs");

        /// <summary>OBS 崩溃报告目录（同样允许读取，用于崩溃类问题诊断）。</summary>
        internal static string ObsCrashDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "obs-studio", "crashes");

        private static string LogsList()
        {
            var items = new List<object>();
            foreach (var dir in new[] { ObsLogDirectory, ObsCrashDirectory })
            {
                if (!Directory.Exists(dir)) continue;
                var files = new DirectoryInfo(dir)
                    .GetFiles()
                    .Where(f => f.Extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)
                             || f.Extension.Equals(".log", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .Take(20);

                foreach (var f in files)
                {
                    items.Add(new
                    {
                        name = f.Name,
                        path = f.FullName,
                        size = f.Length,
                        // Unix 毫秒时间戳：与 macOS 宿主口径一致，由前端按本地时区格式化
                        modified = new DateTimeOffset(f.LastWriteTimeUtc).ToUnixTimeMilliseconds()
                    });
                }
            }
            return JsonSerializer.Serialize(items, JsonOpts);
        }

        /// <summary>校验目标路径确实位于允许的目录内（已解析符号链接与 .. 之后）。</summary>
        private static bool IsUnderAllowedDirectory(string fullPath)
        {
            foreach (var dir in new[] { ObsLogDirectory, ObsCrashDirectory })
            {
                var root = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static string LogsRead(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("路径为空。");

            var full = Path.GetFullPath(path);
            if (!IsUnderAllowedDirectory(full))
                throw new UnauthorizedAccessException("只允许读取 OBS 日志目录内的文件。");

            var ext = Path.GetExtension(full);
            if (!ext.Equals(".txt", StringComparison.OrdinalIgnoreCase) &&
                !ext.Equals(".log", StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("只允许读取 .txt / .log 文件。");

            if (!File.Exists(full)) throw new FileNotFoundException("日志文件不存在。");

            var info = new FileInfo(full);
            using var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            // 超大日志只读尾部：OBS 的关键错误与统计都集中在文件末尾
            if (info.Length > MaxLogBytes)
            {
                fs.Seek(info.Length - MaxLogBytes, SeekOrigin.Begin);
            }
            using var sr = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return sr.ReadToEnd();
        }

        // -------------------------------------------------------- 环境信息

        private static string EnvInfo()
        {
            var version = typeof(HostBridgeHandler).Assembly.GetName().Version?.ToString() ?? "1.0.0";
            return JsonSerializer.Serialize(new
            {
                platform = "windows",
                appVersion = version,
                obsLogDirectory = ObsLogDirectory,
                logDirectoryExists = Directory.Exists(ObsLogDirectory)
            }, JsonOpts);
        }

        private static string ShellOpen(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                throw new ArgumentException("链接格式不合法。");
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                throw new UnauthorizedAccessException("只允许打开 http/https 链接。");

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri)
            {
                UseShellExecute = true
            });
            return "";
        }

    // -------------------------------------------------- 系统环境探测（方向 A）

    /// <summary>OBS 在 Windows 上的数据根目录（basic.ini / 配置 / 场景集合都在其下）。</summary>
    internal static string ObsConfigDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "obs-studio");

    private static readonly string GpuClassKey =
        @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr hProcess, uint dwDesiredAccess, out IntPtr hToken);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr hToken, int tokenInformationClass, IntPtr tokenInformation, int tokenInformationLength, out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint TOKEN_QUERY = 0x0008;
    private const int TokenElevation = 20;

    /// <summary>聚合本机系统环境，供前端「配置诊断」直接检测根因（黑屏/卡顿高频诱因）。</summary>
    private static string SystemInfo()
    {
        var gpus = new List<object>();
        string primaryGpu = "";
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(GpuClassKey);
            if (root != null)
            {
                foreach (var sub in root.GetSubKeyNames())
                {
                    if (!sub.StartsWith("0")) continue; // 仅适配器子键（0000, 0001…）
                    using var ak = root.OpenSubKey(sub);
                    if (ak == null) continue;
                    var name = ak.GetValue("DriverDesc") as string;
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    var vendor = (ak.GetValue("ProviderName") as string) ?? "";
                    var isActive = gpus.Count == 0; // 第一个视作当前主 GPU（best-effort）
                    if (isActive) primaryGpu = name;
                    gpus.Add(new { name, vendor, isActive });
                }
            }
        }
        catch (Exception) { /* 注册表读取失败不阻断整体探测 */ }

        // OBS 进程
        bool obsRunning = false, obsElevated = false;
        double obsMemMb = 0;
        string obsVersion = "";
        try
        {
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    if (!proc.ProcessName.Equals("obs64", StringComparison.OrdinalIgnoreCase)
                        && !proc.ProcessName.Equals("obs", StringComparison.OrdinalIgnoreCase))
                        continue;
                    obsRunning = true;
                    obsElevated = IsProcessElevated(proc);
                    obsMemMb = proc.WorkingSet64 / (1024.0 * 1024.0);
                    try
                    {
                        if (!string.IsNullOrEmpty(proc.MainModule?.FileName))
                            obsVersion = FileVersionInfo.GetVersionInfo(proc.MainModule.FileName).FileVersion ?? "";
                    }
                    catch (Exception) { /* 取不到版本不影响 */ }
                    break;
                }
                finally { proc.Dispose(); }
            }
        }
        catch (Exception) { /* 进程枚举失败不阻断 */ }

        // 录制盘空间（以 OBS 配置目录所在盘为代理）
        double freeGb = 0, totalGb = 0;
        try
        {
            var driveRoot = Path.GetPathRoot(ObsConfigDirectory);
            var di = new DriveInfo(driveRoot!);
            freeGb = di.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
            totalGb = di.TotalSize / (1024.0 * 1024.0 * 1024.0);
        }
        catch (Exception) { /* 盘符解析失败不阻断 */ }

        var info = new
        {
            platform = "windows",
            osVersion = OsVersionString(),
            osBuild = OsBuildString(),
            hagsEnabled = IsHagsEnabled(),
            gameModeEnabled = IsGameModeEnabled(),
            obs = new { running = obsRunning, elevated = obsElevated, cpuPercent = 0.0, memoryMb = Math.Round(obsMemMb, 1), version = obsVersion },
            gpus,
            primaryGpu,
            recordingDiskFreeGb = Math.Round(freeGb, 1),
            recordingDiskTotalGb = Math.Round(totalGb, 1)
        };
        return JsonSerializer.Serialize(info, JsonOpts);
    }

    private static bool IsProcessElevated(Process proc)
    {
        IntPtr hProc = IntPtr.Zero, hToken = IntPtr.Zero;
        try
        {
            hProc = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, proc.Id);
            if (hProc == IntPtr.Zero) return false;
            if (!OpenProcessToken(hProc, TOKEN_QUERY, out hToken) || hToken == IntPtr.Zero) return false;
            var buf = Marshal.AllocHGlobal(4);
            try
            {
                if (GetTokenInformation(hToken, TokenElevation, buf, 4, out _))
                    return Marshal.ReadInt32(buf) != 0;
                return false;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        catch (Exception) { return false; }
        finally
        {
            if (hToken != IntPtr.Zero) CloseHandle(hToken);
            if (hProc != IntPtr.Zero) CloseHandle(hProc);
        }
    }

    private static string OsVersionString()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (k != null)
            {
                var disp = k.GetValue("DisplayVersion") as string;
                var prod = k.GetValue("ProductName") as string;
                if (!string.IsNullOrWhiteSpace(disp)) return disp;
                if (!string.IsNullOrWhiteSpace(prod)) return prod;
            }
        }
        catch (Exception) { }
        return Environment.OSVersion.VersionString;
    }

    private static string OsBuildString()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            return k?.GetValue("CurrentBuild") as string ?? "";
        }
        catch (Exception) { return ""; }
    }

    private static bool IsHagsEnabled()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers");
            var v = k?.GetValue("HwSchMode");
            if (v is int i) return i == 2; // 2 = 已开启硬件加速 GPU 调度
        }
        catch (Exception) { }
        return false;
    }

    private static bool IsGameModeEnabled()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\GameBar");
            if (k?.GetValue("GameMode") is int i) return i == 1;
        }
        catch (Exception) { }
        return false;
    }

    /// <summary>查询 GitHub 上 OBS Studio 的最新发布版本（可选联网，失败返回空）。</summary>
    private static string ObsLatestVersion()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("OBS-Helper/1.0");
            var json = client.GetStringAsync("https://api.github.com/repos/obsproject/obs-studio/releases/latest").Result;
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("tag_name", out var tag))
            {
                var v = tag.GetString() ?? "";
                return v.TrimStart('v'); // 去掉可能的 v 前缀
            }
        }
        catch (Exception) { /* 离线 / 限流 / 解析失败：返回空，前端按「未知」处理 */ }
        return "";
    }

    // -------------------------------------------------- OBS 配置文件读取（方向 B）

    private static readonly string[] ConfigAllowedExt = { ".ini", ".json", ".jsonc", ".txt", ".conf" };

    private static bool IsUnderObsConfig(string fullPath)
    {
        var root = Path.GetFullPath(ObsConfigDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>列出 OBS 配置目录（或其子目录）下的条目，用于配置扫描器发现 profiles / 场景集合。</summary>
    private static string ConfigList(string relativePath)
    {
        var items = new List<object>();
        var dir = ObsConfigDirectory;
        if (!string.IsNullOrWhiteSpace(relativePath))
        {
            var resolved = Path.GetFullPath(Path.Combine(dir, relativePath.TrimStart('/', '\\')));
            if (IsUnderObsConfig(resolved) && Directory.Exists(resolved)) dir = resolved;
        }
        if (!Directory.Exists(dir)) return JsonSerializer.Serialize(items, JsonOpts);
        foreach (var e in new DirectoryInfo(dir).EnumerateFileSystemInfos())
        {
            items.Add(new
            {
                name = e.Name,
                isDir = (e.Attributes & FileAttributes.Directory) != 0,
                size = e is FileInfo fi ? fi.Length : 0L,
                modified = new DateTimeOffset(e.LastWriteTimeUtc).ToUnixTimeMilliseconds()
            });
        }
        return JsonSerializer.Serialize(items, JsonOpts);
    }

    private static string ConfigRead(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) throw new ArgumentException("路径为空。");
        // 防穿越：把相对路径拼到 OBS 配置根，再解析并校验仍在根内
        var full = Path.GetFullPath(Path.Combine(ObsConfigDirectory, relativePath.TrimStart('/', '\\')));
        if (!IsUnderObsConfig(full)) throw new UnauthorizedAccessException("只允许读取 OBS 配置目录内的文件。");
        if (!ConfigAllowedExt.Contains(Path.GetExtension(full).ToLowerInvariant()))
            throw new UnauthorizedAccessException("只允许读取 .ini / .json / .jsonc / .txt / .conf 文件。");
        if (!File.Exists(full)) throw new FileNotFoundException("配置文件不存在。");

        var info = new FileInfo(full);
        using var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (info.Length > MaxLogBytes) // 复用 8MB 上限
        {
            fs.Seek(info.Length - MaxLogBytes, SeekOrigin.Begin);
        }
        using var sr = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return sr.ReadToEnd();
    }

    // -------------------------------------------------- 云端 AI 转发（可选）

    // 为什么由宿主转发而不是 WebAssembly 直连？
        //   1. API Key 永远不进入 WebView 内存，降低被注入脚本窃取的风险；
        //   2. 绕开浏览器 CORS —— 绝大多数 LLM 服务不给浏览器来源发 CORS 头；
        //   3. 可以在宿主侧统一施加 https 强制、内网地址拦截与超时。
        // 该命令只在用户显式开启「云端引擎」后才会被调用，默认走完全离线的本地引擎。

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
        internal static bool IsPrivateHost(string host)
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

        private static async Task<string> AiChatAsync(string url, string secretKey, string body)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                throw new ArgumentException("接口地址不合法。");
            if (uri.Scheme != Uri.UriSchemeHttps)
                throw new UnauthorizedAccessException("云端 AI 接口必须使用 https。");
            if (IsPrivateHost(uri.Host))
                throw new UnauthorizedAccessException("出于安全考虑，不允许请求内网或本机地址。");
            if (string.IsNullOrWhiteSpace(body))
                throw new ArgumentException("请求体为空。");

            var apiKey = SecretGet(secretKey);
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
            => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s.Substring(0, max) + "…");
    }
}
