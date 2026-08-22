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
///          OBS_Helper_Plugins_&lt;ver&gt;.json）。</item>
/// </list>
/// 版本号取 JSON 里的 <c>version</c> 字段（如 "1.5"），与程序集版本完全解耦——
/// 知识库可以随时独立更新，不需要等应用发版。
/// </summary>
public sealed class KnowledgeBaseUpdater
{
    /// <summary>远程问题库主通道：GitHub raw（仓库 master 分支）。</summary>
    public const string RawKbUrl =
        "https://raw.githubusercontent.com/YYRMMAYO/OBS_Helper/master/OBS_Helper.Wpf/Assets/problems.json";

    /// <summary>远程插件目录主通道（P0-3）：同一仓库 master 分支的 plugins.json。</summary>
    public const string RawPluginsUrl =
        "https://raw.githubusercontent.com/YYRMMAYO/OBS_Helper/master/OBS_Helper.Wpf/Assets/plugins.json";

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

    /// <summary>本地问题库覆盖文件路径（%LocalAppData%\OBS_Helper\data\problems.json）。</summary>
    public static string KbFile => Path.Combine(DataDir, "problems.json");

    /// <summary>本地插件目录覆盖文件路径（%LocalAppData%\OBS_Helper\data\plugins.json）。</summary>
    public static string PluginsKbFile => Path.Combine(DataDir, "plugins.json");

    private static string StateFile => Path.Combine(DataDir, "kb_state.json");
    private static string PluginsStateFile => Path.Combine(DataDir, "kb_plugins_state.json");

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
    /// </summary>
    /// <returns>(是否更新成功, 新版本号, 说明)。未检查（节流内）返回 (false, null, null)。</returns>
    public Task<(bool Updated, string? NewVersion, string? Message)> RefreshAsync(bool manual)
        => RefreshChannelAsync(manual, RawKbUrl, KbFile, StateFile, usePluginsChannel: false);

    /// <summary>
    /// 检查远程插件目录并（若更新）自动应用（P0-3）。机制与问题库通道完全一致。永不抛异常。
    /// </summary>
    public Task<(bool Updated, string? NewVersion, string? Message)> RefreshPluginsAsync(bool manual)
        => RefreshChannelAsync(manual, RawPluginsUrl, PluginsKbFile, PluginsStateFile, usePluginsChannel: true);

    /// <summary>两条更新通道的共享实现：fetch → 校验 → 版本比较 → 原子写盘。</summary>
    private async Task<(bool Updated, string? NewVersion, string? Message)> RefreshChannelAsync(
        bool manual, string rawUrl, string localFile, string stateFile, bool usePluginsChannel)
    {
        await Lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var state = LoadState(stateFile);
            if (!manual && state.LastCheckedUtc is { } last && DateTime.UtcNow - last < SilentThrottle)
            {
                return (false, null, null); // 节流内，跳过
            }

            var (remoteJson, fallback) = await FetchRemoteKbAsync(rawUrl, usePluginsChannel).ConfigureAwait(false);
            state.LastCheckedUtc = DateTime.UtcNow;
            SaveState(stateFile, state);

            if (remoteJson is null)
            {
                return (false, null, "远程知识库拉取失败（网络不可达或地址变更），本次跳过。");
            }

            // 校验：必须是合法且非空的数据，防止坏文件覆盖本地
            string remoteVersion;
            if (usePluginsChannel)
            {
                var parsed = PluginCatalogCore.Parse(remoteJson);
                if (parsed is null)
                {
                    return (false, null, "远程插件目录内容无效，已拒绝应用。");
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
                    return (false, null, "远程知识库内容无效，已拒绝应用。");
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
                return (false, remoteVersion, "知识库写入本地失败：" + ex.Message);
            }

            state.Version = remoteVersion;
            SaveState(stateFile, state);
            FileLogger.Info("KB", $"知识库已更新：{current} → {remoteVersion}（来源：{(fallback ? "Release 资产" : "GitHub raw")}）");
            return (true, remoteVersion, null);
        }
        catch (Exception ex)
        {
            FileLogger.Warn("KB", "知识库检查异常：" + ex.Message);
            return (false, null, "知识库检查异常：" + ex.Message);
        }
        finally
        {
            Lock.Release();
        }
    }

    /// <summary>
    /// 拉取远程知识库文本。主通道 raw.githubusercontent；失败时兜底 Release 资产。
    /// 返回 (内容, 是否走了兜底通道)；两者都失败返回 (null, false)。
    /// </summary>
    private async Task<(string? Json, bool Fallback)> FetchRemoteKbAsync(string rawUrl, bool usePluginsChannel)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var resp = await Http.GetAsync(rawUrl, cts.Token).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
            {
                var text = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(text)) return (text, false);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            FileLogger.Warn("KB", "raw 通道拉取失败：" + ex.Message);
        }

        // 兜底：GitHub Release 资产（OBS_Helper_Knowledge_*.json / OBS_Helper_Plugins_*.json）
        try
        {
            var info = usePluginsChannel
                ? await AppServices.Updates.GetLatestPluginsAssetAsync().ConfigureAwait(false)
                : await AppServices.Updates.GetLatestKbAssetAsync().ConfigureAwait(false);
            if (info.IsOk)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                using var resp = await Http.GetAsync(info.AssetUrl!, cts.Token).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                {
                    var text = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(text)) return (text, true);
                }
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
