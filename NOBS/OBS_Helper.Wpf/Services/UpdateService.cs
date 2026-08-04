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

    private const string TagsApi = "https://api.github.com/repos/YYRMMAYO/OBS_Helper/tags";

    /// <summary>
    /// tag 版本解析：兼容 V1.3.0 / v1.3.0 / 1.3.0 三种写法；
    /// 带后缀的（如 1.0-beta）能取到前缀版本，若 GitHub 上混用也无碍。
    /// </summary>
    private static readonly Regex VersionPattern = new(
        @"^\s*[vV]?\s*(\d+)\.(\d+)(?:\.(\d+))?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HttpClient Http = CreateClient();

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

    /// <summary>最近一次检查结果；会话内未检查过时为 null。</summary>
    public UpdateCheckResult? LastResult => _lastResult;

    /// <summary>
    /// 执行一次更新检查。永不抛异常。
    /// </summary>
    public async Task<UpdateCheckResult> CheckAsync()
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
                _lastResult = new UpdateCheckResult
                {
                    Status = UpdateCheckStatus.Failed,
                    CurrentVersion = current,
                    Error = "GitHub 返回的 tag 中无法解析出版本号。",
                };
                return _lastResult;
            }

            var status = Compare(current, latest) < 0
                ? UpdateCheckStatus.UpdateAvailable
                : UpdateCheckStatus.UpToDate;

            _lastResult = new UpdateCheckResult
            {
                Status = status,
                CurrentVersion = current,
                LatestVersion = latest,
            };
            return _lastResult;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _lastResult = new UpdateCheckResult
            {
                Status = UpdateCheckStatus.Failed,
                CurrentVersion = typeof(UpdateService).Assembly.GetName().Version,
                Error = ex.Message,
            };
            return _lastResult;
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
}
