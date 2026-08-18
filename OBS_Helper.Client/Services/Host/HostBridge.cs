using System.Text.Json;
using Microsoft.JSInterop;

namespace OBS_Helper.Client.Services.Host;

/// <summary>宿主上报的运行环境信息。</summary>
public sealed class HostEnvironment
{
    public string Platform { get; set; } = "none";
    public string AppVersion { get; set; } = "";
    /// <summary>本机 OBS 日志目录（宿主解析，前端只读展示）。</summary>
    public string ObsLogDirectory { get; set; } = "";
    public bool LogDirectoryExists { get; set; }
}

/// <summary>OBS 日志文件条目。</summary>
public sealed class HostLogFile
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public long Size { get; set; }

    /// <summary>最后修改时间（Unix 毫秒）。两个宿主统一用时间戳上报，避免时区/格式差异。</summary>
    public long Modified { get; set; }

    public DateTime ModifiedLocal => Modified <= 0
        ? DateTime.MinValue
        : DateTimeOffset.FromUnixTimeMilliseconds(Modified).LocalDateTime;

    public string ModifiedText => Modified <= 0 ? "—" : ModifiedLocal.ToString("yyyy-MM-dd HH:mm");

    public string SizeText => Size >= 1024 * 1024
        ? $"{Size / 1024.0 / 1024.0:0.0} MB"
        : $"{Size / 1024.0:0.0} KB";
}

/// <summary>OBS 配置目录下的一个条目（文件或子目录）。</summary>
public sealed class HostConfigEntry
{
    public string Name { get; set; } = "";
    public bool IsDir { get; set; }
    public long Size { get; set; }

    /// <summary>最后修改时间（Unix 毫秒）。</summary>
    public long Modified { get; set; }

    public DateTime ModifiedLocal => Modified <= 0
        ? DateTime.MinValue
        : DateTimeOffset.FromUnixTimeMilliseconds(Modified).LocalDateTime;
}

/// <summary>
/// 桌面宿主能力的 C# 封装（技术计划 §4.6「设置与凭证存储」、§4.4「日志自动定位」）。
///
/// 所有机密（obs-websocket 密码、LLM API Key）都不由 WebAssembly 侧持久化：
/// 前端只负责传值，实际加密与落盘发生在桌面壳进程内
/// （Windows：DPAPI CurrentUser；macOS：系统钥匙串 Keychain）。
///
/// 当没有桌面宿主时（例如用浏览器打开 dev server），<see cref="IsAvailable"/> 为 false，
/// 调用方须降级为「仅本次会话内存保存」，绝不写入 localStorage。
/// </summary>
public sealed class HostBridge
{
    private readonly IJSRuntime _js;
    private bool _probed;
    private bool _available;
    private string _platform = "none";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public HostBridge(IJSRuntime js) => _js = js;

    public bool IsAvailable => _available;
    public string Platform => _platform;

    /// <summary>探测宿主是否存在。多次调用只会真正探测一次。</summary>
    public async Task<bool> ProbeAsync()
    {
        if (_probed) return _available;
        _probed = true;
        try
        {
            _available = await _js.InvokeAsync<bool>("eval", "!!(window.obsHelperHost && window.obsHelperHost.available)");
            if (_available)
                _platform = await _js.InvokeAsync<string>("eval", "window.obsHelperHost.platform");
        }
        catch (Exception)
        {
            // JS 互操作不可用（预渲染 / 极早期调用）：视作无宿主。
            _available = false;
        }
        return _available;
    }

    private async Task<string> InvokeAsync(string cmd, object? payload = null)
    {
        if (!await ProbeAsync())
            throw new InvalidOperationException("当前环境没有桌面宿主。");

        var json = payload is null ? "{}" : JsonSerializer.Serialize(payload, JsonOpts);
        return await _js.InvokeAsync<string>("obsHelperHost.invoke", cmd, json);
    }

    // ------------------------------------------------------------ 机密存储

    /// <summary>写入一条机密（宿主加密后落盘）。</summary>
    public async Task<bool> SetSecretAsync(string key, string value)
    {
        try
        {
            await InvokeAsync("secret.set", new { key, value });
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>读取一条机密；不存在或无宿主时返回 null。</summary>
    public async Task<string?> GetSecretAsync(string key)
    {
        try
        {
            var v = await InvokeAsync("secret.get", new { key });
            return string.IsNullOrEmpty(v) ? null : v;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>删除一条机密。</summary>
    public async Task<bool> DeleteSecretAsync(string key)
    {
        try
        {
            await InvokeAsync("secret.delete", new { key });
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // ------------------------------------------------------------ 日志访问

    /// <summary>列出本机 OBS 日志目录中的日志文件（按修改时间倒序，最多 20 条）。</summary>
    public async Task<List<HostLogFile>> ListObsLogsAsync()
    {
        try
        {
            var json = await InvokeAsync("logs.list");
            return JsonSerializer.Deserialize<List<HostLogFile>>(json, JsonOpts) ?? new();
        }
        catch (Exception)
        {
            return new();
        }
    }

    /// <summary>读取指定日志文件内容；宿主会限制只能读取 OBS 日志目录内的 .txt/.log 文件。</summary>
    public async Task<string?> ReadObsLogAsync(string path)
    {
        try
        {
            return await InvokeAsync("logs.read", new { path });
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ------------------------------------------------------------ 环境信息

    public async Task<HostEnvironment> GetEnvironmentAsync()
    {
        try
        {
            var json = await InvokeAsync("env.info");
            return JsonSerializer.Deserialize<HostEnvironment>(json, JsonOpts) ?? new();
        }
        catch (Exception)
        {
            return new HostEnvironment { Platform = _platform };
        }
    }

    // ------------------------------------------------------------ 系统探测（方向 A）

    /// <summary>拉取本机系统环境（HAGS / Game Mode / OBS 进程与权限 / GPU / 录制盘空间）。无宿主时返回 null。</summary>
    public async Task<HostSystemInfo?> GetSystemInfoAsync()
    {
        try
        {
            var json = await InvokeAsync("system.info");
            return JsonSerializer.Deserialize<HostSystemInfo>(json, JsonOpts);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>查询 OBS Studio 最新发布版本（可选联网，失败返回 null）。</summary>
    public async Task<string?> GetObsLatestVersionAsync()
    {
        try
        {
            var v = await InvokeAsync("obs.latestVersion");
            return string.IsNullOrWhiteSpace(v) ? null : v;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ------------------------------------------------------------ OBS 配置读取（方向 B）

    /// <summary>列出 OBS 配置目录（或其子目录）下的条目，用于发现 profiles / 场景集合。</summary>
    public async Task<List<HostConfigEntry>> ListObsConfigAsync(string relativePath = "")
    {
        try
        {
            var json = await InvokeAsync("config.list", new { path = relativePath });
            return JsonSerializer.Deserialize<List<HostConfigEntry>>(json, JsonOpts) ?? new();
        }
        catch (Exception)
        {
            return new();
        }
    }

    /// <summary>读取 OBS 配置文件内容（宿主限定在 obs-studio 目录内）。</summary>
    public async Task<string?> ReadObsConfigAsync(string relativePath)
    {
        try
        {
            return await InvokeAsync("config.read", new { path = relativePath });
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ------------------------------------------------------------ 云端 AI 转发

    /// <summary>
    /// 通过桌面宿主转发一次云端 AI 请求。
    ///
    /// 前端只传「机密键名」而非 API Key 本身：宿主自行从加密存储里取出并拼装
    /// Authorization 头，密钥全程不进入 WebAssembly 内存。
    /// 同时也绕开了浏览器的 CORS 限制（多数 LLM 服务不给浏览器来源发 CORS 头）。
    /// </summary>
    /// <param name="url">https 的 chat/completions 接口地址。</param>
    /// <param name="secretKey">API Key 在宿主机密存储中的键名。</param>
    /// <param name="body">完整的请求体 JSON（不含鉴权信息）。</param>
    /// <returns>响应体原文；失败时抛出异常，异常消息可直接展示给用户。</returns>
    public async Task<string> AiChatAsync(string url, string secretKey, string body)
        => await InvokeAsync("ai.chat", new { url, secretKey, body });

    /// <summary>用系统默认浏览器打开外链（仅 http/https）。</summary>
    public async Task<bool> OpenExternalAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme is not ("http" or "https")) return false;
        try
        {
            await InvokeAsync("shell.open", new { url = uri.AbsoluteUri });
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // ------------------------------------------------------------ 场景模板导出

    /// <summary>把场景模板 JSON 导出到用户选择的位置（宿主弹原生保存对话框）。取消返回 null。</summary>
    public async Task<string?> ExportTemplateAsync(string filename, string json)
    {
        try
        {
            var path = await InvokeAsync("template.export", new { filename, json });
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ------------------------------------------------------------ 配置管理

    /// <summary>定位 OBS 配置目录；overridePath 非空时为手动指定。</summary>
    public async Task<HostConfigLocation?> LocateObsConfigAsync(string? overridePath = null)
    {
        try
        {
            var json = await InvokeAsync("config.locate", new { @override = overridePath ?? "" });
            return JsonSerializer.Deserialize<HostConfigLocation>(json, JsonOpts);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>检测 OBS 是否正在运行（彻底重置 / 导入的前置条件）。</summary>
    public async Task<bool> IsObsRunningAsync()
    {
        try
        {
            var json = await InvokeAsync("config.running");
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("running", out var v) && v.GetBoolean();
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>打包 OBS 配置为 zip。targetPath 为空时自动落到应用备份目录，返回实际路径。</summary>
    public async Task<string?> PackObsConfigAsync(string targetPath, bool includeKey, bool includePluginConfig, string reason)
    {
        try
        {
            var path = await InvokeAsync("config.pack", new { targetPath, includeKey, includePluginConfig, reason });
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>导出 OBS 配置到用户选择的位置（宿主弹原生保存对话框）。取消返回 null。</summary>
    public async Task<string?> ExportObsConfigAsync(bool includeKey, bool includePluginConfig)
    {
        try
        {
            var path = await InvokeAsync("config.export", new { includeKey, includePluginConfig });
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>从用户选择的 zip 导入 OBS 配置（宿主弹原生打开对话框）。mode = overwrite | merge。</summary>
    public async Task<HostImportResult?> ImportObsConfigAsync(string mode)
    {
        try
        {
            var json = await InvokeAsync("config.import", new { mode });
            return JsonSerializer.Deserialize<HostImportResult>(json, JsonOpts);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>列出应用备份目录下的全部备份（按创建时间倒序）。</summary>
    public async Task<List<HostBackupInfo>> ListObsBackupsAsync()
    {
        try
        {
            var json = await InvokeAsync("config.listBackups");
            return JsonSerializer.Deserialize<List<HostBackupInfo>>(json, JsonOpts) ?? new();
        }
        catch (Exception)
        {
            return new();
        }
    }

    /// <summary>彻底重置 OBS 配置（移入回收站，永不硬删）。</summary>
    public async Task<HostResetResult?> ResetObsConfigFullAsync()
    {
        try
        {
            var json = await InvokeAsync("config.resetFull");
            return JsonSerializer.Deserialize<HostResetResult>(json, JsonOpts);
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ------------------------------------------------------------ 系统资源采样

    /// <summary>拉取一次系统资源采样（CPU / 内存 / 网络 / 磁盘）。</summary>
    public async Task<HostSystemSample?> GetSystemSampleAsync()
    {
        try
        {
            var json = await InvokeAsync("system.sample");
            return JsonSerializer.Deserialize<HostSystemSample>(json, JsonOpts);
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ------------------------------------------------------------ 应用更新检查

    /// <summary>查询本应用（新仓库 OBS-Helpmac）的 GitHub tags；失败或离线返回 null。</summary>
    public async Task<List<string>?> CheckAppUpdateAsync()
    {
        try
        {
            var json = await InvokeAsync("app.checkUpdate");
            if (string.IsNullOrWhiteSpace(json)) return null;
            return JsonSerializer.Deserialize<List<string>>(json, JsonOpts);
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ------------------------------------------------------------ Finder 显示

    /// <summary>在 Finder 中显示指定文件 / 目录（仅限应用数据与 OBS 配置目录）。</summary>
    public async Task<bool> RevealInFinderAsync(string path)
    {
        try
        {
            await InvokeAsync("shell.reveal", new { path });
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

/// <summary>OBS 配置目录定位结果。</summary>
public sealed class HostConfigLocation
{
    public string ConfigDir { get; set; } = "";
    public bool Exists { get; set; }
    public bool Portable { get; set; }
    public string Source { get; set; } = "";
}

/// <summary>备份目录里的一条备份记录。</summary>
public sealed class HostBackupInfo
{
    public string Path { get; set; } = "";
    /// <summary>创建时间（Unix 毫秒）。</summary>
    public long CreatedAt { get; set; }
    public string Reason { get; set; } = "";
    public bool IncludeKey { get; set; }
    public bool IncludePluginConfig { get; set; }

    public DateTime CreatedAtLocal => CreatedAt <= 0
        ? DateTime.MinValue
        : DateTimeOffset.FromUnixTimeMilliseconds(CreatedAt).LocalDateTime;

    public string CreatedAtText => CreatedAtLocal == DateTime.MinValue ? "—" : CreatedAtLocal.ToString("yyyy-MM-dd HH:mm");
}

/// <summary>配置导入结果。</summary>
public sealed class HostImportResult
{
    public bool Ok { get; set; }
    public int ImportedCollections { get; set; }
    public int ImportedProfiles { get; set; }
    public string? AutoBackupPath { get; set; }
    public string? Message { get; set; }
}

/// <summary>彻底重置结果。</summary>
public sealed class HostResetResult
{
    public bool Ok { get; set; }
    public string? AutoBackupPath { get; set; }
    public string? TrashPath { get; set; }
    public string? Message { get; set; }
}

/// <summary>一次系统资源采样。</summary>
public sealed class HostSystemSample
{
    public double CpuPercent { get; set; }
    public double MemUsedMb { get; set; }
    public double MemTotalMb { get; set; }
    public double MemUsedPercent { get; set; }
    public double NetDownKbps { get; set; }
    public double NetUpKbps { get; set; }
    public List<HostDiskSample> Disks { get; set; } = new();

    /// <summary>剩余空间最小的一块盘（用于磁盘预警）。</summary>
    public HostDiskSample? LowestDisk => Disks.Count == 0 ? null : Disks.OrderBy(d => d.FreeGb).First();
}

/// <summary>磁盘采样。</summary>
public sealed class HostDiskSample
{
    public string Name { get; set; } = "";
    public double TotalGb { get; set; }
    public double FreeGb { get; set; }
    public double FreePercent => TotalGb > 0 ? FreeGb / TotalGb * 100.0 : 0;
}
