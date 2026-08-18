using System.Text.Json;
using Microsoft.JSInterop;
using OBS_Helper.Client.Services.Host;
using OBS_Helper.Client.Services.Obs;

namespace OBS_Helper.Client.Services.ObsConfig;

/// <summary>
/// OBS 配置管理（与 Windows 版 ObsPathService + ObsBackupService + ObsResetService 对应）：
/// 定位配置目录、备份 / 导出 / 导入（ZIP）、轻度重置（websocket）与彻底重置（文件级）。
/// 写操作全部由宿主执行并延续「先自动备份、永不硬删」的安全模型。
/// </summary>
public sealed class ObsConfigService
{
    private const string OverrideStorageKey = "obs_config_override";
    private const string MainSceneName = "主画面";
    private const string NewCollectionBase = "初始设置 (OBS 助手)";

    private readonly HostBridge _host;
    private readonly ObsConnectionService _obs;
    private readonly IJSRuntime _js;

    public ObsConfigService(HostBridge host, ObsConnectionService obs, IJSRuntime js)
    {
        _host = host;
        _obs = obs;
        _js = js;
    }

    // ------------------------------------------------------------ 定位

    /// <summary>定位 OBS 配置目录；手动指定路径会持久化到 localStorage，重启后仍生效。</summary>
    public async Task<HostConfigLocation?> LocateAsync(string? manualOverride = null)
    {
        if (!string.IsNullOrWhiteSpace(manualOverride))
            await SaveOverrideAsync(manualOverride);
        return await _host.LocateObsConfigAsync(await GetOverrideAsync());
    }

    private async Task SaveOverrideAsync(string path)
        => await _js.InvokeVoidAsync("eval", $"localStorage.setItem('{OverrideStorageKey}', {JsonSerializer.Serialize(path)})");

    private async Task<string> GetOverrideAsync()
    {
        try
        {
            var v = await _js.InvokeAsync<string>("eval", $"localStorage.getItem('{OverrideStorageKey}')");
            return string.IsNullOrEmpty(v) ? "" : v;
        }
        catch (Exception)
        {
            return "";
        }
    }

    // ------------------------------------------------------------ 备份 / 导出 / 导入 / 重置

    public Task<bool> IsRunningAsync() => _host.IsObsRunningAsync();

    /// <summary>创建自动备份（宿主写入应用备份目录），返回 zip 路径。</summary>
    public Task<string?> CreateBackupAsync(string reason, bool includeKey = true, bool includePluginConfig = true)
        => _host.PackObsConfigAsync("", includeKey, includePluginConfig, reason);

    /// <summary>导出到用户选择的位置（宿主弹保存对话框）。</summary>
    public Task<string?> ExportAsync(bool includeKey, bool includePluginConfig)
        => _host.ExportObsConfigAsync(includeKey, includePluginConfig);

    /// <summary>从用户选择的 zip 导入（宿主弹打开对话框）。mode = overwrite | merge。</summary>
    public Task<HostImportResult?> ImportAsync(string mode)
        => _host.ImportObsConfigAsync(mode);

    public Task<List<HostBackupInfo>> ListBackupsAsync()
        => _host.ListObsBackupsAsync();

    /// <summary>彻底重置：宿主把配置移入回收站并重建空目录。</summary>
    public Task<HostResetResult?> ResetFullAsync()
        => _host.ResetObsConfigFullAsync();

    // ------------------------------------------------------------ 轻度重置（websocket）

    /// <summary>轻度软重置：新建干净配置集合并切过去，不破坏任何原有数据。</summary>
    public async Task<(bool Ok, string Message, string? AutoBackupPath)> LightResetAsync(Action<string>? onProgress)
    {
        if (!_obs.IsConnected)
            return (false, "当前未连接 OBS，请先在「OBS 控制台」完成连接后再试。", null);
        if (_obs.RecordStatus.Active || _obs.StreamStatus.Active)
            return (false, "录制 / 推流正在进行中，请先停止后再重置。", null);

        try
        {
            onProgress?.Invoke("正在生成不冲突的配置集合名…");
            var name = await GenerateCollectionNameAsync();

            onProgress?.Invoke($"正在新建干净配置集合「{name}」…");
            await EnsureSceneCollectionAsync(name);

            onProgress?.Invoke("正在创建重置前自动备份…");
            string? autoBackup = null;
            try
            {
                autoBackup = await _host.PackObsConfigAsync("", true, true, "软重置前备份");
            }
            catch (Exception ex)
            {
                onProgress?.Invoke($"提示：自动备份跳过（{ex.Message}），不影响重置。");
            }

            onProgress?.Invoke($"正在创建主场景「{MainSceneName}」…");
            await _obs.RawRequestAsync("CreateScene", new { sceneName = MainSceneName });

            onProgress?.Invoke("正在切到主场景…");
            await _obs.RawRequestAsync("SetCurrentProgramScene", new { sceneName = MainSceneName });

            onProgress?.Invoke("正在清理多余默认场景…");
            await RemoveExtraScenesAsync();

            onProgress?.Invoke("正在重置画布为 1920×1080@30…");
            await _obs.RawRequestAsync("SetVideoSettings", new
            {
                baseWidth = 1920,
                baseHeight = 1080,
                outputWidth = 1920,
                outputHeight = 1080,
                fpsNumerator = 30,
                fpsDenominator = 1
            });

            onProgress?.Invoke("正在刷新状态…");
            await _obs.RefreshAllAsync();

            return (true, $"已新建并切换到干净配置集合「{name}」。原有集合仍然保留，可随时在 OBS 里切回。", autoBackup);
        }
        catch (Exception ex)
        {
            return (false, $"软重置失败：{ex.Message}", null);
        }
    }

    private async Task<string> GenerateCollectionNameAsync()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = await _obs.RawRequestAsync("GetSceneCollectionList");
        if (list.Ok && list.Data is JsonElement d && d.TryGetProperty("sceneCollections", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var e in arr.EnumerateArray())
                if (e.ValueKind == JsonValueKind.String) names.Add(e.GetString()!);

        var name = NewCollectionBase;
        var i = 1;
        while (names.Contains(name)) name = $"{NewCollectionBase} {i++}";
        return name;
    }

    private async Task EnsureSceneCollectionAsync(string name)
    {
        try
        {
            var r = await _obs.RawRequestAsync("CreateSceneCollection", new { sceneCollectionName = name });
            if (r.Ok) return;
        }
        catch (Exception) { /* 落到复查 */ }

        var list = await _obs.RawRequestAsync("GetSceneCollectionList");
        if (list.Ok && list.Data is JsonElement d && d.TryGetProperty("sceneCollections", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var e in arr.EnumerateArray())
                if (e.ValueKind == JsonValueKind.String && string.Equals(e.GetString(), name, StringComparison.OrdinalIgnoreCase))
                    return;
        throw new InvalidOperationException("新建配置集合失败。");
    }

    private async Task RemoveExtraScenesAsync()
    {
        var list = await _obs.RawRequestAsync("GetSceneList");
        if (!list.Ok || list.Data is not JsonElement d) return;
        if (!d.TryGetProperty("scenes", out var arr) || arr.ValueKind != JsonValueKind.Array) return;

        foreach (var e in arr.EnumerateArray())
        {
            if (e.TryGetProperty("sceneName", out var snv) && snv.ValueKind == JsonValueKind.String)
            {
                var sn = snv.GetString()!;
                if (!string.Equals(sn, MainSceneName, StringComparison.OrdinalIgnoreCase))
                    await _obs.RawRequestAsync("RemoveScene", new { sceneName = sn });
            }
        }
    }
}
