using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OBS_Helper.Wpf.Models;
using OBS_Helper.Wpf.Services.Plugins;
using OBS_Helper.Wpf.Services.Host;

namespace OBS_Helper.Wpf.Services.Update;

/// <summary>
/// 知识库分离更新：问题库与插件目录从「随应用整包发布」改为「本地数据目录 + 独立远程更新」。
///
/// 数据流（problems.json 与 plugins.json 各自独立一条通道，机制完全相同）：
/// <list type="bullet">
///   <item><b>本地</b>：%LocalAppData%\OBS_Helper\data\&lt;文件&gt; —— 有则优先使用；</item>
///   <item><b>内置</b>：程序集内嵌的同名 JSON 作为「种子」，本地缺失 / 损坏时兜底；</item>
///   <item><b>远程</b>：优先拉 GitHub raw（仓库 master 分支，随 commit 更新）；
///          raw 失败时兜底拉 GitHub Release 资产（OBS_Helper_Knowledge_&lt;ver&gt;.json /
///          OBS_Helper_Plugins_&lt;ver&gt;.json）。raw 地址见 <see cref="KnowledgeBaseUrls"/>
///          ——仓库根目录下是 <c>NOBS/</c>，路径漏了这一段会静默 404（V2.9 及以前的实际缺陷）。</item>
/// </list>
/// 版本号取 JSON 里的 <c>version</c> 字段（如 "1.5"），与程序集版本完全解耦——
/// 知识库可以随时独立更新，不需要等应用发版。
/// </summary>
public sealed class KnowledgeBaseUpdater
{
    /// <summary>远程问题库主通道（raw，仓库 master 分支，简体中文）。地址常量见 <see cref="KnowledgeBaseUrls"/>。</summary>
    public const string RawKbUrl = KnowledgeBaseUrls.RawProblems;

    /// <summary>远程插件目录主通道（P0-3）：同一仓库 master 分支的 plugins.json。</summary>
    public const string RawPluginsUrl = KnowledgeBaseUrls.RawPlugins;

    /// <summary>按语言取 raw 主通道地址（V2.9.3）：英文走 <c>.en-US.json</c>。</summary>
    public static string RawUrlFor(string baseName, string? language)
        => KnowledgeBaseUrls.RawFor(baseName, language);

    /// <summary>问题库 Release 资产兜底的文件名前缀（OBS_Helper_Knowledge_&lt;ver&gt;.json）。</summary>
    public const string KbAssetPrefix = "OBS_Helper_Knowledge_";

    /// <summary>插件目录 Release 资产兜底的文件名前缀（OBS_Helper_Plugins_&lt;ver&gt;.json）。</summary>
    public const string PluginsAssetPrefix = "OBS_Helper_Plugins_";

    /// <summary>静默检查节流：距上次成功检查不足 6 小时则跳过（手动检查不受限，避免每次启动都联网）。</summary>
    private static readonly TimeSpan SilentThrottle = TimeSpan.FromHours(6);

    private static readonly HttpClient Http = CreateClient();
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private static readonly SemaphoreSlim Lock = new(1, 1);

    private static string DataDir => Path.Combine(HostBridge.AppDataDirectory, "data");

    /// <summary>
    /// 本地缓存文件路径。**按语言分文件**（V2.9.3）：中文沿用 <c>problems.json</c> 这个名字
    /// （已下载到用户本机的缓存、raw 地址、Release 资产名都依赖它，改名就是静默回归），
    /// 英文用 <c>problems.en-US.json</c>。
    /// </summary>
    public static string LocalDataFile(string baseName, string? language)
        => Path.Combine(DataDir, ContentAssets.FileName(baseName, language));

    /// <summary>本地问题库覆盖文件路径（%LocalAppData%\OBS_Helper\data\problems[.en-US].json）。</summary>
    public static string KbFile => LocalDataFile(ContentAssets.Problems, Strings.Current);

    /// <summary>本地插件目录覆盖文件路径（%LocalAppData%\OBS_Helper\data\plugins[.en-US].json）。</summary>
    public static string PluginsKbFile => LocalDataFile(ContentAssets.Plugins, Strings.Current);

    /// <summary>节流状态文件也按语言分（否则切到英文后会被中文那份的 6 小时节流挡住）。</summary>
    private static string StatePath(string stem, string? language)
        => Path.Combine(DataDir, ContentAssets.SuffixedFileName(stem, ".json", language));

    private static string StateFile => StatePath("kb_state", Strings.Current);
    private static string PluginsStateFile => StatePath("kb_plugins_state", Strings.Current);

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OBS_Helper.Wpf-KB/1.0");
        return client;
    }

    // ------------------------------------------------------------ 状态

    private sealed class KbState
    {
        public string? Version { get; set; }
        public DateTime? LastCheckedUtc { get; set; }
    }

    private static KbState LoadState(string stateFile)
    {
        try
        {
            if (!File.Exists(stateFile)) return new KbState();
            var json = File.ReadAllText(stateFile);
            return JsonSerializer.Deserialize<KbState>(json, JsonOpts) ?? new KbState();
        }
        catch (Exception)
        {
            return new KbState();
        }
    }

    private static void SaveState(string stateFile, KbState state)
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(stateFile, JsonSerializer.Serialize(state));
        }
        catch (Exception)
        {
            // 状态写盘失败不影响主流程
        }
    }

    /// <summary>本地问题库当前生效版本（外部文件优先，其次内置种子）；读不到返回空串。</summary>
    public string GetCurrentVersion()
    {
        try
        {
            if (File.Exists(KbFile))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(KbFile));
                if (doc.RootElement.TryGetProperty("version", out var v)) return v.GetString() ?? "";
            }
        }
        catch (Exception)
        {
            // 外部文件损坏 → 回退内置，下面照常返回内置版本
        }
        return AppServices.Problems.Version;
    }

    /// <summary>本地插件目录当前生效版本（外部文件优先，其次内置种子）；读不到返回空串。</summary>
    public string GetCurrentPluginsVersion()
    {
        try
        {
            if (File.Exists(PluginsKbFile))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(PluginsKbFile));
                if (doc.RootElement.TryGetProperty("version", out var v)) return v.GetString() ?? "";
            }
        }
        catch (Exception)
        {
            // 外部文件损坏 → 回退内置
        }
        return AppServices.PluginCatalog.Version;
    }

    // ------------------------------------------------------------ 检查与更新

    /// <summary>
    /// 检查远程问题库并（若更新）自动应用。永不抛异常。
    /// V2.9.3 起按**当前语言**分发：英文用户拉 <c>problems.en-US.json</c>，写进 <c>problems.en-US.json</c> 缓存。
    /// </summary>
    /// <returns>(是否更新成功, 新版本号, 说明)。未检查（节流内）返回 (false, null, null)。</returns>
    public Task<(bool Updated, string? NewVersion, string? Message)> RefreshAsync(bool manual)
        => RefreshChannelAsync(manual, RawUrlFor(ContentAssets.Problems, Strings.Current), KbFile, StateFile, usePluginsChannel: false);

    /// <summary>
    /// 检查远程插件目录并（若更新）自动应用（P0-3）。机制与问题库通道完全一致，同样按语言分发。
    /// </summary>
    public Task<(bool Updated, string? NewVersion, string? Message)> RefreshPluginsAsync(bool manual)
        => RefreshChannelAsync(manual, RawUrlFor(ContentAssets.Plugins, Strings.Current), PluginsKbFile, PluginsStateFile, usePluginsChannel: true);

    /// <summary>
    /// 语言切换后补一次静默刷新（V2.9.3）：切到英文时英文那份的节流状态文件是全新的，
    /// 因此这次调用会真的联网拉一次英文资产，而不是被中文那份的 6 小时节流挡住。
    /// </summary>
    public async Task RefreshCurrentLanguageAsync()
    {
        try
        {
            await RefreshAsync(manual: false).ConfigureAwait(false);
            await RefreshPluginsAsync(manual: false).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            FileLogger.Warn("KB", "语言切换后刷新内容失败：" + ex.Message);
        }
    }

    /// <summary>两条更新通道的共享实现：fetch → 校验 → 版本比较 → 原子写盘。</summary>
    private async Task<(bool Updated, string? NewVersion, string? Message)> RefreshChannelAsync(
        bool manual, string rawUrl, string localFile, string stateFile, bool usePluginsChannel)
    {
        // 本次刷新绑定「开始时的语言」：中途用户切语言也不会把另一种语言的资产写进这份缓存。
        var language = Strings.Current;
        await Lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var state = LoadState(stateFile);
            if (!manual && state.LastCheckedUtc is { } last && DateTime.UtcNow - last < SilentThrottle)
            {
                return (false, null, null); // 节流内，跳过
            }

            var (remoteJson, fallback) = await FetchRemoteKbAsync(rawUrl, usePluginsChannel, language).ConfigureAwait(false);
            state.LastCheckedUtc = DateTime.UtcNow;
            SaveState(stateFile, state);

            if (remoteJson is null)
            {
                return (false, null, Strings.T("kb.remoteFailed"));
            }

            // 校验：必须是合法且非空的数据，防止坏文件覆盖本地
            string remoteVersion;
            if (usePluginsChannel)
            {
                var parsed = PluginCatalogCore.Parse(remoteJson);
                if (parsed is null)
                {
                    return (false, null, Strings.T("kb.pluginsInvalid"));
                }
                remoteVersion = parsed.Version;
            }
            else
            {
                ProblemData? remote;
                try
                {
                    remote = JsonSerializer.Deserialize<ProblemData>(remoteJson, JsonOpts);
                }
                catch (JsonException)
                {
                    remote = null;
                }

                if (remote is null || remote.Problems.Count == 0)
                {
                    return (false, null, Strings.T("kb.invalid"));
                }
                remoteVersion = remote.Version;
            }

            var current = usePluginsChannel ? GetCurrentPluginsVersion() : GetCurrentVersion();
            if (!KbVersion.IsNewer(current, remoteVersion))
            {
                return (false, remoteVersion, null); // 已是最新
            }

            try
            {
                Directory.CreateDirectory(DataDir);
                var tmp = localFile + ".tmp";
                File.WriteAllText(tmp, remoteJson);
                File.Move(tmp, localFile, overwrite: true);
            }
            catch (Exception ex)
            {
                return (false, remoteVersion, Strings.T("kb.writeFailed", ex.Message));
            }

            state.Version = remoteVersion;
            SaveState(stateFile, state);
            FileLogger.Info("KB", $"知识库已更新：{current} → {remoteVersion}（来源：{(fallback ? "Release 资产" : "GitHub raw")}）");
            return (true, remoteVersion, null);
        }
        catch (Exception ex)
        {
            FileLogger.Warn("KB", "知识库检查异常：" + ex.Message);
            return (false, null, Strings.T("kb.checkError", ex.Message));
        }
        finally
        {
            Lock.Release();
        }
    }

    /// <summary>
    /// 拉取远程知识库文本。主通道 raw.githubusercontent；失败时兜底 Release 资产。
    /// 返回 (内容, 是否走了兜底通道)；两者都失败返回 (null, false)。
    ///
    /// 两条通道都会记录失败原因：raw 地址写错这类问题在本机只表现为「兜底通道生效」，
    /// 不写日志的话等于没人能发现（V2.9 的 NOBS/ 前缀缺陷就是这么潜伏下来的）。
    /// </summary>
    private async Task<(string? Json, bool Fallback)> FetchRemoteKbAsync(string rawUrl, bool usePluginsChannel, string language)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var resp = await Http.GetAsync(rawUrl, cts.Token).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
            {
                var text = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(text)) return (text, false);

                FileLogger.Warn("KB", $"raw 通道返回空内容，转兜底：{rawUrl}");
            }
            else
            {
                // 关键：非 2xx（例如地址少一段导致的 404）以前是静默的，现在明确落盘
                FileLogger.Warn("KB", $"raw 通道返回 {(int)resp.StatusCode}，转兜底：{rawUrl}");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            FileLogger.Warn("KB", $"raw 通道拉取失败：{ex.Message}（{rawUrl}）");
        }

        // 兜底：GitHub Release 资产（OBS_Helper_Knowledge_*.json / OBS_Helper_Plugins_*.json）
        try
        {
            var info = usePluginsChannel
                ? await AppServices.Updates.GetLatestPluginsAssetAsync(language).ConfigureAwait(false)
                : await AppServices.Updates.GetLatestKbAssetAsync(language).ConfigureAwait(false);
            if (info.IsOk)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                using var resp = await Http.GetAsync(info.AssetUrl!, cts.Token).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                {
                    var text = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(text)) return (text, true);

                    FileLogger.Warn("KB", "Release 资产内容为空：" + info.AssetUrl);
                }
                else
                {
                    FileLogger.Warn("KB", $"Release 资产返回 {(int)resp.StatusCode}：" + info.AssetUrl);
                }
            }
            else
            {
                FileLogger.Warn("KB", "Release 资产兜底不可用：" + (info.Error ?? "未找到资产"));
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            FileLogger.Warn("KB", "Release 资产兜底拉取失败：" + ex.Message);
        }

        return (null, false);
    }

    /// <summary>版本比较：remote 比 current 新返回 true。版本串按点分数字解析，解析失败视为 0。</summary>
    public static bool IsNewer(string? current, string? remote) => KbVersion.IsNewer(current, remote);
}
