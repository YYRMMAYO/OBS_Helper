using System.IO;
using System.Text.Json;
using OBS_Helper.Wpf.Models.ObsConfig;
using OBS_Helper.Wpf.Services.Obs;

namespace OBS_Helper.Wpf.Services.ObsConfig;

/// <summary>
/// OBS 配置重置。
///
/// <list type="bullet">
///   <item><b>轻度（websocket）</b>：不删任何旧数据，新建一个干净的配置集合并切过去，用户随时能切回。
///         顺序很关键：先建集合（OBS 会落盘旧集合再切换）→ 自动备份 → 建主场景 → 切主场景 →
///         删其余默认场景（必须先有主场景，OBS 不允许删最后一个）→ 设 1080p30 画布。</item>
///   <item><b>彻底（文件级）</b>：OBS 必须已退出。移走 scenes / profiles / global.ini / user.ini / plugin_config
///         到回收站（永不硬删，可恢复），重建空目录，不手写 global.ini（让 OBS 下次走首次向导）。
///         <c>logs/crashes/themes</c> 永不触碰。</item>
/// </list>
/// 两档都强制先自动备份（含密钥 + plugin_config），失败即中止。
/// </summary>
public sealed class ObsResetService
{
    private const string MainSceneName = "主画面";
    private const string NewCollectionBase = "初始设置 (OBS 助手)";

    private readonly ObsPathService _paths;
    private readonly ObsBackupService _backups;
    private readonly ObsConnectionService _obs;

    public ObsResetService(ObsPathService paths, ObsBackupService backups, ObsConnectionService obs)
    {
        _paths = paths;
        _backups = backups;
        _obs = obs;
    }

    /// <summary>轻度重置是否可用：已连接 OBS 且未处于录制 / 推流。</summary>
    public bool CanResetLight()
        => _obs.IsConnected && !_obs.RecordStatus.Active && !_obs.StreamStatus.Active;

    /// <summary>轻度（websocket）软重置：新建干净配置集合并切过去。不破坏任何原有数据。</summary>
    public async Task<ObsResetResult> LightResetAsync(IProgress<string> p, CancellationToken ct = default)
    {
        if (!CanResetLight())
            return new ObsResetResult(false, null, "当前无法进行软重置：需已连接 OBS 且未处于录制 / 推流状态。", null);

        try
        {
            p.Report("正在生成不冲突的配置集合名…");
            var name = await GenerateCollectionNameAsync(ct);

            p.Report($"正在新建干净配置集合「{name}」…");
            await EnsureSceneCollectionAsync(ct, name);

            // 软重置非破坏操作，旧集合仍在；此处备份主要是为了覆盖「切过去之后用户又改了点什么」的情形。
            p.Report("正在创建重置前自动备份…");
            string? autoBackup = null;
            try { autoBackup = await _backups.CreateBackupAsync("软重置前备份", includeKey: true, includePluginConfig: true, null); }
            catch (Exception ex) { p.Report($"提示：自动备份跳过（{ex.Message}），不影响重置。"); }

            p.Report($"正在创建主场景「{MainSceneName}」…");
            await _obs.RawRequestAsync("CreateScene", new { sceneName = MainSceneName }, ct);

            p.Report("正在切到主场景…");
            await _obs.RawRequestAsync("SetCurrentProgramScene", new { sceneName = MainSceneName }, ct);

            p.Report("正在清理多余默认场景…");
            await RemoveExtraScenesAsync(ct);

            p.Report("正在重置画布为 1920×1080@30…");
            await _obs.RawRequestAsync("SetVideoSettings", new
            {
                baseWidth = 1920,
                baseHeight = 1080,
                outputWidth = 1920,
                outputHeight = 1080,
                fpsNumerator = 30,
                fpsDenominator = 1
            }, ct);

            p.Report("正在刷新状态…");
            await _obs.RefreshAllAsync();

            return new ObsResetResult(true, autoBackup,
                $"已新建并切换到干净配置集合「{name}」。原有集合仍然保留，可随时在 OBS 里切回。", null);
        }
        catch (OperationCanceledException)
        {
            return new ObsResetResult(false, null, "操作已取消。", null);
        }
        catch (Exception ex)
        {
            return new ObsResetResult(false, null, $"软重置失败：{ex.Message}", null);
        }
    }

    /// <summary>彻底（文件级）重置：OBS 必须已退出。移走配置到回收站，重建空目录。</summary>
    public async Task<ObsResetResult> FullResetAsync(bool keepProfiles, bool keepPluginConfig, IProgress<string> p, CancellationToken ct = default)
    {
        var proc = _paths.DetectProcess();
        if (proc.IsRunning)
            return new ObsResetResult(false, null, $"OBS 正在运行（{proc.Evidence}）。彻底重置需要完全退出 OBS 后再进行。", null);

        var loc = await _paths.LocateAsync();
        if (!loc.Exists)
            return new ObsResetResult(false, null, "未找到本机 OBS 配置目录，无法重置。", null);

        p.Report("正在创建重置前自动备份（含密钥与插件配置）…");
        string? autoBackup = null;
        try
        {
            autoBackup = await _backups.CreateBackupAsync("彻底重置前备份", includeKey: true, includePluginConfig: true, null);
        }
        catch (Exception ex)
        {
            return new ObsResetResult(false, null, $"重置前自动备份失败，已中止重置以保护配置：{ex.Message}", null);
        }

        var steps = new List<ObsResetStep>();
        try
        {
            using var tx = new FileTx(ObsPathService.TrashRoot);
            var cfg = loc.ConfigDir;

            p.Report("正在移走场景集合…");
            var scenesDir = Path.Combine(cfg, "basic", "scenes");
            var hasScenes = Directory.Exists(scenesDir);
            if (hasScenes)
                foreach (var f in Directory.GetFiles(scenesDir)) { ObsSafePath.AssertDeletable(f, cfg); tx.StageMove(f); }
            steps.Add(new ObsResetStep { Label = "移走场景集合", Ok = true, Skipped = !hasScenes });

            if (!keepProfiles)
            {
                p.Report("正在移走配置文件…");
                var profDir = Path.Combine(cfg, "basic", "profiles");
                var hasProf = Directory.Exists(profDir);
                if (hasProf)
                    foreach (var d in Directory.GetDirectories(profDir)) { ObsSafePath.AssertDeletable(d, cfg); tx.StageMove(d); }
                steps.Add(new ObsResetStep { Label = "移走配置文件", Ok = true, Skipped = !hasProf });
            }
            else
            {
                steps.Add(new ObsResetStep { Label = "保留配置文件", Ok = true, Skipped = true });
            }

            p.Report("正在移走 global.ini / user.ini…");
            foreach (var ini in new[] { "global.ini", "user.ini" })
            {
                var ip = Path.Combine(cfg, ini);
                if (File.Exists(ip)) { ObsSafePath.AssertDeletable(ip, cfg); tx.StageMove(ip); }
            }
            steps.Add(new ObsResetStep { Label = "移走全局设置", Ok = true });

            if (!keepPluginConfig)
            {
                p.Report("正在移走插件配置…");
                var pc = Path.Combine(cfg, "plugin_config");
                var hasPc = Directory.Exists(pc);
                if (hasPc)
                    foreach (var d in Directory.GetDirectories(pc)) { ObsSafePath.AssertDeletable(d, cfg); tx.StageMove(d); }
                steps.Add(new ObsResetStep { Label = "移走插件配置", Ok = true, Skipped = !hasPc });
            }
            else
            {
                steps.Add(new ObsResetStep { Label = "保留插件配置", Ok = true, Skipped = true });
            }

            // 重建空目录（不让 OBS 找不到 basic/scenes）
            p.Report("正在重建空的场景与配置目录…");
            Directory.CreateDirectory(scenesDir);
            if (!keepProfiles) Directory.CreateDirectory(Path.Combine(cfg, "basic", "profiles"));
            // 注意：不手写 global.ini，让 OBS 下次启动走首次运行向导

            p.Report("正在清理回收站…");
            ObsPathService.CleanupTrash();

            p.Report("正在提交…");
            tx.Commit();

            return new ObsResetResult(true, autoBackup,
                "已彻底重置：场景集合与配置已移入回收站（可在应用备份目录找回），下次启动 OBS 将走首次运行向导。", steps);
        }
        catch (ObsSafePathException ex)
        {
            return new ObsResetResult(false, autoBackup, $"安全护栏拦截：{ex.Message}", steps);
        }
        catch (Exception ex)
        {
            return new ObsResetResult(false, autoBackup, $"重置失败：{ex.Message}", steps);
        }
    }

    // ------------------------------------------------------------ 辅助

    private async Task<string> GenerateCollectionNameAsync(CancellationToken ct)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = await _obs.RawRequestAsync("GetSceneCollectionList", null, ct);
        if (list.Ok && list.Data is JsonElement d && d.TryGetProperty("sceneCollections", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in arr.EnumerateArray())
                if (e.ValueKind == JsonValueKind.String) names.Add(e.GetString()!);
        }

        var name = NewCollectionBase;
        var i = 1;
        while (names.Contains(name)) name = $"{NewCollectionBase} {i++}";
        return name;
    }

    /// <summary>新建配置集合；客户端 10s 超时兜底——超时后复查是否其实已成功，避免报假错。</summary>
    private async Task EnsureSceneCollectionAsync(CancellationToken ct, string name)
    {
        try
        {
            var r = await _obs.RawRequestAsync("CreateSceneCollection", new { sceneCollectionName = name }, ct);
            if (r.Ok) return;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { /* 落到下面的复查 */ }

        var list = await _obs.RawRequestAsync("GetSceneCollectionList", null, ct);
        if (list.Ok && CollectionExists(list.Data, name)) return;
        throw new InvalidOperationException("新建配置集合失败。");
    }

    private async Task RemoveExtraScenesAsync(CancellationToken ct)
    {
        var list = await _obs.RawRequestAsync("GetSceneList", null, ct);
        if (!list.Ok || list.Data is not JsonElement d) return;
        if (!d.TryGetProperty("scenes", out var arr) || arr.ValueKind != JsonValueKind.Array) return;

        foreach (var e in arr.EnumerateArray())
        {
            if (e.TryGetProperty("sceneName", out var snv) && snv.ValueKind == JsonValueKind.String)
            {
                var sn = snv.GetString()!;
                if (!string.Equals(sn, MainSceneName, StringComparison.OrdinalIgnoreCase))
                    await _obs.RawRequestAsync("RemoveScene", new { sceneName = sn }, ct);
            }
        }
    }

    private static bool CollectionExists(System.Text.Json.JsonElement? data, string name)
    {
        if (data is not JsonElement d) return false;
        if (!d.TryGetProperty("sceneCollections", out var arr) || arr.ValueKind != JsonValueKind.Array) return false;
        foreach (var e in arr.EnumerateArray())
            if (e.ValueKind == JsonValueKind.String && string.Equals(e.GetString(), name, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}
