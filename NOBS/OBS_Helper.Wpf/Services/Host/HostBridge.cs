using OBS_Helper.Wpf.Localization;
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

    /// <summary>
    /// 应用私有数据目录，不存在时自动创建。
    ///
    /// <list type="bullet">
    ///   <item><b>主构建</b>（net10.0-windows）：<c>%LocalAppData%\OBS_Helper</c>；</item>
    ///   <item><b>Win7 兼容构建</b>（net6.0-windows，定义了 <c>WIN7_COMPAT</c>）：
    ///         <c>%LocalAppData%\OBS_Helper_Win7</c>。</item>
    /// </list>
    ///
    /// 为什么兼容构建要单独一套（V3.0）：两代构建面向的系统与 OBS 代次完全不同
    /// （主构建 OBS 28+ / obs-websocket 5.x，兼容构建 OBS 27 / obs-websocket 4.x），
    /// 设置、语言、连接端口、知识库缓存、待回滚记录都会不一样。共用一份 <c>prefs.json</c> 的后果是
    /// 「在 Win7 机器上连过 4444 的配置被带到 Win10 机器上」这种互相打架的状态，
    /// 而用户往往两台机器都装了同一个软件。分开之后两边互不影响，也能各自安全卸载。
    ///
    /// 注意：<b>主构建的目录名 <c>OBS_Helper</c> 一个字都不改</b> —— 存量用户的设置与密钥都在那里。
    /// </summary>
    public static string AppDataDirectory
    {
        get
        {
            if (_appDataDirectory is not null) return _appDataDirectory;

#if WIN7_COMPAT
            const string folder = "OBS_Helper_Win7";
#else
            const string folder = "OBS_Helper";
#endif
            var preferred = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                folder);

            try
            {
                Directory.CreateDirectory(preferred);
                _appDataDirectory = preferred;
                return preferred;
            }
            catch (Exception ex)
            {
                // V3.0（第四轮验证发现）：这里原先让异常冒出去 —— 而第一个碰它的往往是
                // FileLogger 的**静态构造**，于是「数据目录建不出来」直接变成
                // 「进程启动即死、界面上一个字都没有」（实测：把该目录设为不可创建后 exe 立即崩溃）。
                // 对一个排障工具来说这是最糟的失败方式。现在退到临时目录并记下原因，功能降级但能起来。
                _appDataDirectoryFallbackReason = ex.Message;
                _appDataDirectory = Path.Combine(Path.GetTempPath(), folder);

                try { Directory.CreateDirectory(_appDataDirectory); }
                catch (Exception)
                {
                    // 临时目录也不可写：仍然返回路径，让上层的各次读写各自失败（宁愿功能降级，也不要崩）
                }
                return _appDataDirectory;
            }
        }
    }

    private static string? _appDataDirectory;
    private static string? _appDataDirectoryFallbackReason;

    /// <summary>
    /// 首选数据目录不可用时记下的原因（正常为 null）。
    /// 界面/日志用它告诉用户「设置这次落在临时目录、重启可能丢」。
    /// </summary>
    public static string? AppDataDirectoryFallbackReason => _appDataDirectoryFallbackReason;

    private static string SecretsFile => Path.Combine(AppDataDirectory, "secrets.dat");

    // ------------------------------------------------------------ 机密存储（多层加密）

    // 第 1 层：DPAPI（CurrentUser 作用域 + 应用熵）加密整个文件（既有设计）。
    // 第 2 层（V1.7.0 起）：每条机密值在进 DPAPI 之前，先用「机器绑定密钥」做 AES-256-GCM 加密，
    //   密钥由 PBKDF2-SHA256(MachineGuid + 应用熵) 派生。这样即使 secrets.dat 被离线窃取，
    //   攻击者只有 DPAPI 的口令级保护可破（可离线爆破），却拿不到本机 MachineGuid，第二层无法解开。
    //   存储格式 v2:<nonce>:<tag>:<cipher>（均 Base64）；旧版明文值读取时自动兼容，下次写入自动升级为 v2。
    // V3.0（E2）：值级编解码已拆到 SecretCodec（可单测）；这里只保留转发，避免调用点大面积改动。
    private const string SecretV2Prefix = SecretCodec.V2Prefix;

    // 机器密钥缓存：MachineGuid 在系统生命周期内不变，PBKDF2 每次派生约百毫秒，
    // 而 LoadSecrets 在每次机密读取时都会跑——缓存后全进程只派生一次。
    // 权衡：派生密钥会常驻进程内存（与应用内持有明文密钥的既有事实一致）。
    private static readonly object MachineKeyGate = new();
    private static byte[]? _machineKeyCache;

    /// <summary>由本机 MachineGuid 派生 AES-256-GCM 密钥（进程内缓存）；读不到时返回 null（退化为「仅 DPAPI」）。</summary>
    private static byte[]? TryDeriveMachineKey()
    {
        if (_machineKeyCache is not null) return _machineKeyCache;
        lock (MachineKeyGate)
        {
            if (_machineKeyCache is not null) return _machineKeyCache;
            try
            {
                var guid = Microsoft.Win32.Registry.GetValue(
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography", "MachineGuid", null) as string;
                if (string.IsNullOrWhiteSpace(guid)) return null;
                // 纯计算交给 SecretCodec（可单测）；本方法只负责「读注册表 + 进程内缓存」
                _machineKeyCache = SecretCodec.DeriveKey(guid);
                return _machineKeyCache;
            }
            catch (Exception)
            {
                return null; // 读不到 MachineGuid（极少数精简系统）：退化为仅 DPAPI，保证功能可用
            }
        }
    }

    /// <summary>
    /// 把明文机密值加密为 v2 存储格式；无机器密钥时原样返回（仅 DPAPI 层）。
    /// V3.0（E2）：实现已搬到 <see cref="SecretCodec"/>（纯计算、可单测），这里只做「取密钥 + 转发」。
    /// </summary>
    private static string EncryptSecretValue(string plain) => SecretCodec.Encrypt(plain, TryDeriveMachineKey());

    /// <summary>
    /// 解密 v2 存储值。规则：
    /// <list type="number">
    ///   <item>不以 v2: 开头 → 旧版明文，原样返回；</item>
    ///   <item>以 v2: 开头但格式不严格合法（段数 / 长度 / Base64 错误）→ 视为旧版明文恰好以 v2: 开头，原样返回，
    ///         绝不误删——真实 v2 一定出自本代码，格式必然严格合法；</item>
    ///   <item>格式合法但 GCM 认证失败（密钥不符 / 数据损坏）→ 返回 null，调用方按「不存在」处理（fail-closed）。</item>
    /// </list>
    /// </summary>
    /// <summary>
    /// 解密 v2 存储值。V3.0（E2）：实现已搬到 <see cref="SecretCodec"/>（纯计算、可单测，
    /// 旧版明文兼容 / 格式容错 / 认证失败 fail-closed 三个分支都有对应测试），这里只做「取密钥 + 转发」。
    /// </summary>
    private static string? DecryptSecretValue(string stored) => SecretCodec.Decrypt(stored, TryDeriveMachineKey());
    /// <summary>
    /// 读取机密存储。
    ///
    /// <b>只有「文件不存在 / 空文件」才返回空字典</b>；读取或解密失败一律抛异常。
    /// V3.0 修复：早期实现把任何异常都吞成「空存储」，而调用方（SetSecret / DeleteSecret）会拿这份字典
    /// **整份覆盖写回** —— 于是「杀软临时占用 secrets.dat」这种瞬时故障，会变成用户改一次 API Key
    /// 就把 OBS WebSocket 密码与其它密钥一起清掉，而界面还显示「已保存」。
    /// </summary>
    private static Dictionary<string, string> LoadSecrets()
    {
        if (!File.Exists(SecretsFile)) return new Dictionary<string, string>();
        var encrypted = File.ReadAllBytes(SecretsFile);           // 读失败 → 抛给调用方
        if (encrypted.Length == 0) return new Dictionary<string, string>();

        var plain = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
        var json = Encoding.UTF8.GetString(plain);
        Array.Clear(plain, 0, plain.Length);

        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
        // 值解密（v2 解密、旧版原样）：内存里始终是明文，文件里才是密文
        foreach (var k in dict.Keys.ToList())
        {
            var v = dict[k];
            if (string.IsNullOrEmpty(v)) continue;
            var decrypted = DecryptSecretValue(v);
            if (decrypted is null)
            {
                // 单条解密失败不拖垮整个存储：移除该条，用户重填
                dict.Remove(k);
            }
            else
            {
                dict[k] = decrypted;
            }
        }
        return dict;
    }

    private static void SaveSecrets(Dictionary<string, string> secrets)
    {
        // 值先做第二层加密（AES-256-GCM + 机器绑定密钥），再整体 DPAPI
        var toStore = new Dictionary<string, string>(secrets.Count);
        foreach (var (k, v) in secrets)
        {
            toStore[k] = string.IsNullOrEmpty(v) ? "" : EncryptSecretValue(v);
        }

        var json = JsonSerializer.Serialize(toStore);
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
            throw new ArgumentException(Strings.T("host.badSecretKey"));
        foreach (var c in key)
        {
            if (!Services.Compat.Compat.IsAsciiLetterOrDigit(c) && c != '.' && c != '_' && c != '-')
                throw new ArgumentException(Strings.T("host.badSecretKeyChars"));
        }
    }

    /// <summary>写入一条机密（DPAPI 加密后落盘）。空值等同删除。</summary>
    public async Task<bool> SetSecretAsync(string key, string value)
    {
        await _secretLock.WaitAsync().ConfigureAwait(false);
        try
        {
            // V3.0（F8）：DPAPI + AES + JSON 全是同步调用。信号量空闲时上面那个 await **不会让出线程**，
            // 于是这段会直接在 UI 线程上跑（改一次密钥就卡一下）。统一放进后台线程。
            return await Task.Run(() =>
            {
                ValidateSecretKey(key);
                if (value.Length > MaxSecretLength) throw new ArgumentException(Strings.T("host.secretTooLong"));

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
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // V3.0：失败必须留痕（原来是静默 return false）。最常见的触发是 secrets.dat 被占用 / 换机后无法解密。
            FileLogger.Warn("Secrets", $"写入机密「{key}」失败，未改动磁盘上的既有内容：{ex.Message}");
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
            // V3.0（F8）：同上 —— 读机密同样要挪出 UI 线程（进设置页时每次都会走这里）
            return await Task.Run(() =>
            {
                ValidateSecretKey(key);
                var s = LoadSecrets();
                return s.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v) ? v : null;
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Secrets", $"读取机密「{key}」失败：{ex.Message}");
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
            // V3.0（F8）：同上
            return await Task.Run(() =>
            {
                ValidateSecretKey(key);
                var s = LoadSecrets();
                if (s.Remove(key)) SaveSecrets(s);
                return true;
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Secrets", $"删除机密「{key}」失败：{ex.Message}");
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
        try
        {
            var s = LoadSecrets();
            return s.TryGetValue(key, out var v) ? v : "";
        }
        catch (Exception ex)
        {
            // LoadSecrets 现在会把「读失败」抛出（见那里的注释）；这里只是取值，失败当没有即可
            FileLogger.Warn("Secrets", $"同步读取机密「{key}」失败：{ex.Message}");
            return "";
        }
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

    /// <summary>
    /// 用系统默认浏览器打开外链。
    ///
    /// 策略（V3.0 与出站请求口径统一）：<b>只放行 http/https，且拒绝本机 / 私网 / 保留地址</b>。
    /// 知识库、插件目录、排障指引都会经热更新通道下发，里面的链接是**外部数据**，
    /// 不能因为「是用户点的」就当可信 —— 一条 <c>http://127.0.0.1:xxxx/…</c> 或
    /// <c>http://192.168.x.x/…</c> 的链接足够把用户引到内网设备的管理页面上。
    /// 公开站点的普通 https 链接照常放行（白名单只用于「下载 / 更新 / 反馈」这类本产品自己发起的跳转）。
    /// </summary>
    public Task<bool> OpenExternalAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return Task.FromResult(false);
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            FileLogger.Warn("OpenLink", $"拒绝打开非 http(s) 链接：{url}");
            return Task.FromResult(false);
        }
        if (IsPrivateHost(uri.Host))
        {
            FileLogger.Warn("OpenLink", $"拒绝打开指向本机 / 内网的链接：{uri.Host}");
            return Task.FromResult(false);
        }
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            FileLogger.Warn("OpenLink", $"打开链接失败：{ex.Message}");
            return Task.FromResult(false);
        }
    }

    /// <summary>在资源管理器中打开一个本地目录（WPF 版新增：日志页「打开日志目录」）。</summary>
    public bool OpenFolder(string directory)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(directory)) return false;

            // OBS 的 GetRecordDirectory 会原样返回配置文件里的录制路径，可能带正斜杠
            // （如 D:/Captures，OBS 设置里手输路径就会存成正斜杠）甚至相对路径。
            // 实测 explorer 接到正斜杠路径会解析错误——跳到 C:\Users\...\Documents。
            // 先用 GetFullPath 统一规范化为绝对路径（正斜杠转反斜杠），再做存在性检查。
            var full = Path.GetFullPath(directory);
            full = Path.TrimEndingDirectorySeparator(full);
            if (!Directory.Exists(full)) return false;

            // 用 ArgumentList 而非字符串拼接，避免路径中的特殊字符被 shell 解析
            var psi = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                UseShellExecute = true
            };
            psi.ArgumentList.Add(full);
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
                        throw new UnauthorizedAccessException(Strings.T("host.ssrfBlocked"));
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

    /// <summary>
    /// 拦截指向本机 / 内网的地址，降低 SSRF 风险。
    ///
    /// V3.0 审查补正：比较前先 <c>TrimEnd('.')</c>。
    /// <c>http://localhost./</c> 的 <see cref="Uri.Host"/> 是 <c>"localhost."</c> ——
    /// 既不等于 <c>localhost</c>、也不以 <c>.localhost</c> 结尾，<c>IPAddress.TryParse</c> 同样失败，
    /// 于是被放行；而 Windows/浏览器会把尾点当 FQDN 处理并解析到 127.0.0.1。
    /// 通配 DNS（如 <c>127.0.0.1.nip.io</c>）无法用字符串规则拦住 —— 那条边界写在
    /// <see cref="OpenExternalAsync"/> 的注释里，属于已知残留风险。
    /// </summary>
    public static bool IsPrivateHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return true;
        var h = host.Trim().Trim('[', ']').TrimEnd('.').ToLowerInvariant();
        if (h.Length == 0) return true;

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
        var uri = ValidateAiUrl(url, body);

        // V3.0（F8）：原来是 _secretLock.Wait() + 同步读机密 —— 从 UI 线程发起诊断时会先卡在
        // 「DPAPI + AES + JSON」上。改成异步取锁 + 后台读取（密钥仍然不进 UI 层）。
        await _secretLock.WaitAsync().ConfigureAwait(false);
        string apiKey;
        try
        {
            apiKey = await Task.Run(() => SecretGetRaw(secretKey)).ConfigureAwait(false);
        }
        finally
        {
            _secretLock.Release();
        }

        if (string.IsNullOrEmpty(apiKey))
            throw new InvalidOperationException(Strings.T("host.aiKeyMissing"));
        if (apiKey.Any(char.IsControl))
            throw new ArgumentException(Strings.T("host.aiKeyBadChars"));

        var auth = new AuthenticationHeaderValue("Bearer", apiKey);
        // Authorization 头已拼装完毕，显式释放 apiKey 引用以缩小密钥在托管内存中的窗口。
        // 注意：AuthenticationHeaderValue 构造时会内部分配一份副本，apiKey 设为 null
        // 不影响请求发送；真正的限制在于 .NET 字符串不可变性——GC 回收之前密钥无法从堆上
        // 擦除，这是托管语言共有的局限。
        apiKey = null!;
        return await AiChatPostAsync(uri, body, auth).ConfigureAwait(false);
    }

    /// <summary>
    /// 转发一次「无需 API Key」的 AI 请求（国外免 Key 免费通道 Pollinations 用）：
    /// 同 <see cref="AiChatAsync"/> 的 https / SSRF 防护，但不带 Authorization 头、不触碰任何密钥。
    /// </summary>
    public Task<string> AiChatNoAuthAsync(string url, string body)
    {
        var uri = ValidateAiUrl(url, body);
        return AiChatPostAsync(uri, body, auth: null);
    }

    /// <summary>
    /// 转发一次「应用内嵌密钥」的 AI 请求（内置免费通道用）：https / SSRF 防护与
    /// <see cref="AiChatAsync"/> 一致，但密钥由调用方（FreeAiKeyProvider）直接提供——
    /// 不写入机密存储、不进 prefs.json，密钥明文只在进程内存中短暂存在。
    /// </summary>
    public Task<string> AiChatWithKeyAsync(string url, string apiKey, string body)
    {
        var uri = ValidateAiUrl(url, body);
        if (string.IsNullOrEmpty(apiKey))
            throw new InvalidOperationException(Strings.T("host.freeKeyEmpty"));
        if (apiKey.Any(char.IsControl))
            throw new ArgumentException(Strings.T("host.freeKeyBadChars"));

        var auth = new AuthenticationHeaderValue("Bearer", apiKey);
        return AiChatPostAsync(uri, body, auth);
    }

    /// <summary>校验 AI 接口地址与请求体（https + 非内网），返回解析后的 Uri。</summary>
    private static Uri ValidateAiUrl(string url, string body)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new ArgumentException(Strings.T("host.badEndpoint"));
        if (uri.Scheme != Uri.UriSchemeHttps)
            throw new UnauthorizedAccessException(Strings.T("host.httpsRequired"));
        if (IsPrivateHost(uri.Host))
            throw new UnauthorizedAccessException(Strings.T("host.privateBlocked"));
        if (string.IsNullOrWhiteSpace(body))
            throw new ArgumentException(Strings.T("host.emptyBody"));
        return uri;
    }

    /// <summary>实际发送 AI 请求（带可选 Authorization 头），限量读取响应体。</summary>
    private static async Task<string> AiChatPostAsync(Uri uri, string body, AuthenticationHeaderValue? auth)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        if (auth is not null) req.Headers.Authorization = auth;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token)
                                   .ConfigureAwait(false);

        var text = await ReadBodyLimitedAsync(resp.Content, MaxAiResponseBytes, cts.Token).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            // 只回传状态码与响应体，绝不把 Authorization 头写进任何日志或错误信息
            throw new HttpRequestException(Strings.T("host.httpFailed", (int)resp.StatusCode, Truncate(text, 500)));
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
                throw new HttpRequestException(Strings.T("host.bodyTooLarge", maxBytes / (1024 * 1024)));
            sb.Append(buf, 0, n);
        }
        return sb.ToString();
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max] + "…");
}
