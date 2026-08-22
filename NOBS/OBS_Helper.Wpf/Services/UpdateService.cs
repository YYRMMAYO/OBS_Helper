using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OBS_Helper.Wpf.Services;

/// <summary>更新检查结果状态。</summary>
public enum UpdateCheckStatus
{
    /// <summary>当前已是最新版本。</summary>
    UpToDate,

    /// <summary>GitHub 上有更高版本。</summary>
    UpdateAvailable,

    /// <summary>检查失败（网络不可达、接口异常、解析失败等），按离线优先原则不应打扰用户。</summary>
    Failed,
}

/// <summary>一次更新检查的结果。</summary>
public sealed class UpdateCheckResult
{
    public required UpdateCheckStatus Status { get; init; }

    /// <summary>当前应用版本（程序集版本）。</summary>
    public Version? CurrentVersion { get; init; }

    /// <summary>GitHub 最新 tag 解析出的版本；失败时为空。</summary>
    public Version? LatestVersion { get; init; }

    /// <summary>失败原因（仅 Status == Failed 时有意义，用于手动检查时展示）。</summary>
    public string? Error { get; init; }
}

/// <summary>
/// 更新检查：读取 GitHub 仓库的最新 tag，与自身程序集版本比较。
///
/// 遵循应用的离线优先原则：默认不发起任何网络请求，只有调用方显式触发
/// （启动自动检查 / 设置页手动检查）才会请求 GitHub API；任何失败都返回
/// <see cref="UpdateCheckStatus.Failed"/>，绝不抛异常。
/// </summary>
public sealed class UpdateService
{
    /// <summary>蓝奏云下载链接（新版本安装包存放处）。</summary>
    public const string DownloadUrl = "https://wwbpq.lanzouu.com/b01d7578be";

    /// <summary>蓝奏云下载密码，随更新提示一并展示。</summary>
    public const string UpdatePassword = "YYKWY";

    /// <summary>GitHub 仓库主页。</summary>
    public const string RepoUrl = "https://github.com/YYRMMAYO/OBS_Helper";

    /// <summary>GitHub Release 页（浏览器打开，看发布说明 / 历史版本 / 手动下载）。</summary>
    public const string ReleasesPageUrl = RepoUrl + "/releases/latest";

    private const string TagsApi = "https://api.github.com/repos/YYRMMAYO/OBS_Helper/tags";

    /// <summary>
    /// GitHub Releases 列表接口（应用内下载用）。
    /// 不用 /releases/latest：它只按「发布时间」取最新的一条，若某版本只推了 tag 没建 Release，
    /// latest 会一直指向旧版，导致检查更新（tags）提示有新版本、应用内下载却拿到旧包的自相矛盾。
    /// 这里遍历全部 Releases，取「版本号最高且带安装包资产」的一条，与检查更新口径一致。
    /// </summary>
    private const string ReleasesApi = "https://api.github.com/repos/YYRMMAYO/OBS_Helper/releases?per_page=100";

    /// <summary>应用内下载安装包时的容量上限（自包含安装包一般不到 300MB，1GB 是安全兜底）。</summary>
    private const long MaxInstallerBytes = 1L * 1024 * 1024 * 1024;

    /// <summary>
    /// tag 版本解析：兼容 V1.3.0 / v1.3.0 / 1.3.0 三种写法；
    /// 带后缀的（如 1.0-beta）能取到前缀版本，若 GitHub 上混用也无碍。
    /// </summary>
    private static readonly Regex VersionPattern = new(
        @"^\s*[vV]?\s*(\d+)\.(\d+)(?:\.(\d+))?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HttpClient Http = CreateClient();

    /// <summary>应用内下载用的 HttpClient：跟随 GitHub 资产下载的 302 跳转，超时放宽到 10 分钟。</summary>
    private static readonly HttpClient DownloadHttp = CreateDownloadClient();

    /// <summary>会话内最近一次检查结果，启动自动检查与手动检查共享，避免重复弹窗。</summary>
    private UpdateCheckResult? _lastResult;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        // GitHub API 强制要求 User-Agent，否则返回 403。
        var ver = typeof(UpdateService).Assembly.GetName().Version;
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"OBS_Helper.Wpf/{ver?.Major}.{ver?.Minor}.{ver?.Build}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static HttpClient CreateDownloadClient()
    {
        // 下载走 browser_download_url，会被 GitHub 302 到带签名的对象存储地址，
        // 必须跟随重定向；自包含安装包体积大，超时放宽到 10 分钟。
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OBS_Helper.Wpf-Updater/1.0");
        return client;
    }

    /// <summary>
    /// 去掉版本号前头的 "V"（不区分大小写，V 和 v 都去掉），返回纯数字版本字符串。
    /// 例如 "V1.4.8" / "v1.4.8" → "1.4.8"；"1.4.8" 保持原样。
    /// 应用内从 GitHub 拉版本时统一先剥掉 V 再比较大小，避免 "V1.4.8" 和 "1.4.8" 被当成不同版本。
    /// </summary>
    public static string StripVersionPrefix(string? tag)
    {
        var s = (tag ?? "").Trim();
        if (s.Length > 1 && (s[0] == 'V' || s[0] == 'v'))
        {
            return s[1..].TrimStart();
        }
        return s;
    }

    /// <summary>
    /// 把版本字符串（可能带 V/v 前缀）解析成 <see cref="Version"/> 用于比较。
    /// 解析失败返回 null。
    /// </summary>
    public static Version? ParseVersion(string? text)
    {
        var stripped = StripVersionPrefix(text);
        // 只接受纯数字点分格式，拒绝 "1.4.8-beta" 之类带后缀的（比较会失真）
        return Version.TryParse(stripped, out var v) ? v : null;
    }

    /// <summary>最近一次检查结果；会话内未检查过时为 null。</summary>
    public UpdateCheckResult? LastResult => _lastResult;

    /// <summary>
    /// 执行一次更新检查。永不抛异常。
    /// GitHub API 在国内网络偶发超时 / 抖动，失败自动重试一次再报失败。
    /// </summary>
    public async Task<UpdateCheckResult> CheckAsync()
    {
        // 最多尝试 2 次（首次 + 1 次重试），每次独立超时
        string? lastError = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var (result, error) = await TryCheckOnceAsync().ConfigureAwait(false);
            if (result is not null)
            {
                _lastResult = result;
                return result;
            }

            lastError = error;
            if (attempt == 0)
            {
                // 网络抖动常见，重试一次（短等待）
                await Task.Delay(500).ConfigureAwait(false);
            }
        }

        // 理论不可达（循环内必然 return）
        _lastResult = new UpdateCheckResult
        {
            Status = UpdateCheckStatus.Failed,
            CurrentVersion = typeof(UpdateService).Assembly.GetName().Version,
            Error = lastError ?? "检查更新失败。",
        };
        return _lastResult;
    }

    /// <summary>
    /// 执行单次更新检查。成功返回结果；网络 / 解析类异常返回 (null, 错误信息)，
    /// 由 <see cref="CheckAsync"/> 决定重试或报失败。
    /// </summary>
    private async Task<(UpdateCheckResult? Result, string? Error)> TryCheckOnceAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            using var resp = await Http.GetAsync(TagsApi, cts.Token).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            var body = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            var latest = ParseLatestVersion(body);

            var current = typeof(UpdateService).Assembly.GetName().Version;
            if (latest is null)
            {
                return (new UpdateCheckResult
                {
                    Status = UpdateCheckStatus.Failed,
                    CurrentVersion = current,
                    Error = "GitHub 返回的 tag 中无法解析出版本号。",
                }, null);
            }

            var status = Compare(current, latest) < 0
                ? UpdateCheckStatus.UpdateAvailable
                : UpdateCheckStatus.UpToDate;

            return (new UpdateCheckResult
            {
                Status = status,
                CurrentVersion = current,
                LatestVersion = latest,
            }, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // 网络抖动常见：交给调用方重试一次再报失败
            return (null, ex.Message);
        }
    }

    /// <summary>
    /// 从 GitHub tags 接口响应中解析出最新版本。
    /// 不依赖接口排序：遍历全部 tag，取所有能解析出版本号的最大值。
    /// </summary>
    private static Version? ParseLatestVersion(string json)
    {
        using var doc = JsonDocument.Parse(json);
        Version? max = null;

        foreach (var el in doc.RootElement.EnumerateArray())
        {
            if (!el.TryGetProperty("name", out var nameProp)) continue;
            var tag = nameProp.GetString();
            if (string.IsNullOrWhiteSpace(tag)) continue;

            var m = VersionPattern.Match(tag);
            if (!m.Success) continue;

            var major = int.Parse(m.Groups[1].Value);
            var minor = int.Parse(m.Groups[2].Value);
            var build = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0;

            var v = new Version(major, minor, build);
            if (max is null || v > max) max = v;
        }

        return max;
    }

    /// <summary>a &lt; b 返回负数，a == b 返回 0，a &gt; b 返回正数。null 视为 0.0.0。</summary>
    private static int Compare(Version? a, Version? b)
    {
        var va = a ?? new Version(0, 0, 0);
        var vb = b ?? new Version(0, 0, 0);
        return va.CompareTo(vb);
    }

    // ------------------------------------------------------------ 应用内加载 GitHub 下载

    /// <summary>GitHub 最新 Release 的安装包信息（tag + 资产下载地址）。失败时 Error 非空。</summary>
    public sealed record GitHubReleaseInfo(string? Tag, string? SetupAssetUrl, string? Error)
    {
        public bool IsOk => Error is null && !string.IsNullOrEmpty(SetupAssetUrl);
    }

    /// <summary>GitHub Release 上某个命名资产的信息（tag + 下载地址）。失败时 Error 非空。</summary>
    public sealed record GitHubAssetInfo(string? Tag, string? AssetUrl, string? Error)
    {
        public bool IsOk => Error is null && !string.IsNullOrEmpty(AssetUrl);
    }

    /// <summary>
    /// 查询 GitHub 最新 Release，从中找出安装包资产（OBS_Helper_Setup_*.exe）的下载地址。
    ///
    /// 与 <see cref="CheckAsync"/> 同口径：遍历全部 Release，取「版本号最高且带安装包资产」的一条。
    /// 这样即使某个版本只推了 tag 没建 Release，也不会让 /releases/latest 一直指向旧包。
    /// 永不抛异常：任何失败都放进 <see cref="GitHubReleaseInfo.Error"/>。
    /// </summary>
    public async Task<GitHubReleaseInfo> GetLatestReleaseAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var resp = await Http.GetAsync(ReleasesApi, cts.Token).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            var body = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);

            var best = FindBestRelease(doc.RootElement);
            if (best is null)
                return new GitHubReleaseInfo(null, null, "GitHub 上还没有发布过带安装包的版本。");

            return new GitHubReleaseInfo(best.Tag, best.AssetUrl, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new GitHubReleaseInfo(null, null, ex.Message);
        }
    }

    private sealed record ReleaseCandidate(string Tag, string AssetUrl);

    /// <summary>遍历全部 Release，取「版本号最高且确实带安装包资产」的一条；找不到返回 null。</summary>
    private static ReleaseCandidate? FindBestRelease(JsonElement root)
    {
        Version? best = null;
        string? bestTag = null;
        string? bestAssetUrl = null;

        foreach (var rel in root.EnumerateArray())
        {
            var tag = rel.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() : null;
            if (string.IsNullOrWhiteSpace(tag)) continue;
            var v = ParseVersion(tag);
            if (v is null) continue; // 跳过无法解析出版本号的 Release

            var assetUrl = FindSetupAssetUrl(rel);
            if (string.IsNullOrEmpty(assetUrl)) continue; // 该版本没带安装包资产，跳过（例如纯说明性质的 Release）

            if (best is null || v > best)
            {
                best = v;
                bestTag = tag;
                bestAssetUrl = assetUrl;
            }
        }

        if (bestTag is null || string.IsNullOrEmpty(bestAssetUrl)) return null;
        return new ReleaseCandidate(bestTag, bestAssetUrl);
    }

    /// <summary>在 Release 的 assets 里找安装包（OBS_Helper_Setup_*.exe）的下载地址。</summary>
    private static string? FindSetupAssetUrl(JsonElement release)
    {
        return FindAssetUrl(release, name =>
            name.StartsWith("OBS_Helper_Setup_", StringComparison.OrdinalIgnoreCase)
            && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>在 Release 的 assets 里找第一个匹配命名规则的资产下载地址。</summary>
    private static string? FindAssetUrl(JsonElement release, Func<string, bool> match)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var n) ? n.GetString() : "";
            if (string.IsNullOrEmpty(name)) continue;
            if (!match(name)) continue;

            if (asset.TryGetProperty("browser_download_url", out var u))
            {
                var url = u.GetString();
                if (!string.IsNullOrEmpty(url)) return url;
            }
        }
        return null;
    }

    // ------------------------------------------------------------ 增量包 / 知识库资产

    /// <summary>
    /// 查询 GitHub 最新 Release 中的「增量更新包」（OBS_Helper_Update_&lt;ver&gt;.zip）下载地址。
    /// 遍历全部 Release，取「版本号最高且确实带增量包资产」的一条；找不到返回 Error。
    /// 永不抛异常。
    /// </summary>
    public async Task<GitHubAssetInfo> GetLatestDeltaPackageAsync()
    {
        return await FindNamedAssetAsync(
            name => name.StartsWith("OBS_Helper_Update_", StringComparison.OrdinalIgnoreCase)
                 && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase),
            "GitHub 上还没有发布过增量更新包。").ConfigureAwait(false);
    }

    /// <summary>
    /// 查询 GitHub 最新 Release 中的「独立知识库文件」（OBS_Helper_Knowledge_&lt;ver&gt;.json）下载地址。
    /// 作为 raw.githubusercontent 通道失败时的兜底。永不抛异常。
    /// </summary>
    public async Task<GitHubAssetInfo> GetLatestKbAssetAsync()
    {
        return await FindNamedAssetAsync(
            name => name.StartsWith("OBS_Helper_Knowledge_", StringComparison.OrdinalIgnoreCase)
                 && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase),
            "GitHub 上还没有发布过独立知识库文件。").ConfigureAwait(false);
    }

    /// <summary>
    /// 查询 GitHub 最新 Release 中的「插件目录文件」（OBS_Helper_Plugins_&lt;ver&gt;.json）下载地址。
    /// 插件知识库 raw 通道失败时的兜底（V2.2 P0-3）。永不抛异常。
    /// </summary>
    public async Task<GitHubAssetInfo> GetLatestPluginsAssetAsync()
    {
        return await FindNamedAssetAsync(
            name => name.StartsWith("OBS_Helper_Plugins_", StringComparison.OrdinalIgnoreCase)
                 && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase),
            "GitHub 上还没有发布过插件目录文件。").ConfigureAwait(false);
    }

    /// <summary>遍历全部 Release，取「版本号最高且带指定命名资产」的一条；找不到返回 Error。</summary>
    private async Task<GitHubAssetInfo> FindNamedAssetAsync(Func<string, bool> match, string notFoundMessage)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var resp = await Http.GetAsync(ReleasesApi, cts.Token).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            var body = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);

            Version? best = null;
            string? bestTag = null;
            string? bestUrl = null;

            foreach (var rel in doc.RootElement.EnumerateArray())
            {
                var tag = rel.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() : null;
                if (string.IsNullOrWhiteSpace(tag)) continue;
                var v = ParseVersion(tag);
                if (v is null) continue;

                var url = FindAssetUrl(rel, match);
                if (string.IsNullOrEmpty(url)) continue;

                if (best is null || v > best)
                {
                    best = v;
                    bestTag = tag;
                    bestUrl = url;
                }
            }

            if (bestTag is null || string.IsNullOrEmpty(bestUrl))
                return new GitHubAssetInfo(null, null, notFoundMessage);

            return new GitHubAssetInfo(bestTag, bestUrl, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new GitHubAssetInfo(null, null, ex.Message);
        }
    }

    /// <summary>
    /// 应用内下载 GitHub Release 安装包到临时目录，返回本地文件路径；失败返回 null。
    /// <paramref name="progress"/> 回调进度：(已下载字节, 总字节)，总字节未知时为 null。
    /// </summary>
    public async Task<string?> DownloadReleaseAssetAsync(
        string assetUrl,
        IProgress<(long Received, long? Total)>? progress = null,
        CancellationToken ct = default)
    {
        try
        {
            using var resp = await DownloadHttp.GetAsync(assetUrl, HttpCompletionOption.ResponseHeadersRead, ct)
                                                .ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            var total = resp.Content.Headers.ContentLength;
            if (total is > MaxInstallerBytes)
            {
                return null; // 远超合理体积，判定为异常响应，不落盘
            }

            // 随机文件名 + 下载后校验 PE 头：既避免临时文件被占位 / 符号链接劫持，
            // 也保证启动的一定是可执行的 Windows 程序（防止下载到残缺文件后白弹 UAC）。
            var tmp = Path.Combine(Path.GetTempPath(), "OBS_Helper_Setup_" + Path.GetRandomFileName() + ".exe");

            // 先完整写入，再关闭句柄，最后才校验 PE 头（旧版曾因句柄未释放导致共享冲突误判损坏）。
            var ok = await DownloadToTempFileAsync(resp, tmp, total, progress, ct).ConfigureAwait(false);

            // 文件句柄已关闭，此时清理 / 校验才有效
            if (!ok || !IsValidPeExecutable(tmp))
            {
                try { File.Delete(tmp); } catch { /* 清理失败无妨 */ }
                return null;
            }

            return tmp;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>把响应体流式写入临时文件；超过体积上限时中止并返回 false（句柄已关闭，可安全清理）。</summary>
    private static async Task<bool> DownloadToTempFileAsync(HttpResponseMessage resp, string tmp, long? total,
        IProgress<(long Received, long? Total)>? progress, CancellationToken ct)
    {
        var oversized = false;
        await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

            var buffer = new byte[81920];
            long received = 0;
            while (true)
            {
                var n = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
                if (n == 0) break;
                await fs.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                received += n;
                if (received > MaxInstallerBytes)
                {
                    // 先跳出循环、关闭写句柄，再清理——句柄开着时 File.Delete 会共享冲突（同旧版 bug）
                    oversized = true;
                    break;
                }
                progress?.Report((received, total));
            }
        }
        return !oversized;
    }

    /// <summary>校验文件头为 Windows 可执行文件（MZ 签名），避免启动非 PE 文件。</summary>
    private static bool IsValidPeExecutable(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (fs.Length < 2) return false;
            Span<byte> head = stackalloc byte[2];
            var n = fs.Read(head);
            return n == 2 && head[0] == 'M' && head[1] == 'Z';
        }
        catch (Exception)
        {
            return false;
        }
    }
}
