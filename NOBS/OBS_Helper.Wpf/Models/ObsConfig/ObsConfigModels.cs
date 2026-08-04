namespace OBS_Helper.Wpf.Models.ObsConfig;

/// <summary>OBS 配置目录的定位结果。</summary>
public record ObsConfigLocation(string ConfigDir, bool IsPortable, bool Exists, string Source);

/// <summary>OBS 进程检测结论：是否运行，以及判定依据（用于在不满足前置条件时把证据摆给用户）。</summary>
public sealed class ObsProcessInfo
{
    public bool IsRunning { get; set; }
    public string? Evidence { get; set; }
    public int? Pid { get; set; }
    public string? ProcessName { get; set; }
}

// ---------------------------------------------------------------- 备份 / 导出

/// <summary>一次备份 / 导出的选项。</summary>
public sealed class ObsBackupOptions
{
    /// <summary>是否包含推流密钥（默认 false，即脱敏）。</summary>
    public bool IncludeKey { get; set; }

    /// <summary>是否打包 plugin_config（含各平台 OAuth token、浏览器源 cookie 等）。</summary>
    public bool IncludePluginConfig { get; set; }

    /// <summary>备份原因，写入 manifest，也用于文件名。</summary>
    public string? Reason { get; set; }

    /// <summary>导出场景：指定目标 zip 路径；备份场景为 null（自动生成到备份目录）。</summary>
    public string? TargetZipPath { get; set; }

    /// <summary>是否为自动备份（自动备份强制含密钥与 plugin_config）。</summary>
    public bool Auto { get; set; }
}

/// <summary>单条被打进 zip 的条目（用于统计与审计）。</summary>
public sealed class ObsBackupEntry
{
    public string Name { get; set; } = "";
    public long Uncompressed { get; set; }
    public bool Redacted { get; set; }
}

/// <summary>备份 / 导出结果。</summary>
public record ObsBackupResult(bool Ok, string? ZipPath, string? Error);

/// <summary>备份清单中关于场景集合 / 配置文件的统计（导入预检时复用）。</summary>
public record BackupManifest(
    bool Ok,
    string? Reason,
    int SceneCollectionCount,
    int ProfileCount,
    bool IncludeKey,
    IReadOnlyList<string> SceneCollections,
    IReadOnlyList<string> ProfileNames,
    IReadOnlyList<string> Skipped);

// ---------------------------------------------------------------- 导入

/// <summary>导入模式：覆盖 = 同名直接替换；合并 = 同名加 (导入) 后缀，不覆盖本机配置。</summary>
public enum ObsImportMode { Overwrite, Merge }

/// <summary>单条已有备份的元信息（备份目录列表用）。</summary>
public sealed class BackupInfo
{
    public string ZipPath { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string Reason { get; set; } = "";
    public bool IncludeKey { get; set; }
    public bool IncludePluginConfig { get; set; }
}

/// <summary>导入结果。</summary>
public record ObsImportResult(bool Ok, string? Error, string? AutoBackupPath, int ImportedCollections, int ImportedProfiles);

// ---------------------------------------------------------------- 重置

/// <summary>重置力度。</summary>
public enum ObsResetLevel { Light, Full }

/// <summary>彻底重置选项。</summary>
public sealed class ObsResetOptions
{
    /// <summary>保留 profiles（不删 basic/profiles）。</summary>
    public bool KeepProfiles { get; set; }

    /// <summary>保留 plugin_config（不删 plugin_config 目录）。</summary>
    public bool KeepPluginConfig { get; set; }
}

/// <summary>重置过程中的单步进展。</summary>
public sealed class ObsResetStep
{
    public string Label { get; set; } = "";
    public bool Ok { get; set; } = true;
    public bool Skipped { get; set; }
    public string? Detail { get; set; }
}

/// <summary>重置结果。</summary>
public record ObsResetResult(
    bool Ok,
    string? AutoBackupPath,
    string? Note,
    IReadOnlyList<ObsResetStep>? Steps = null);

// ---------------------------------------------------------------- 场景模板

/// <summary>模板落地结果。</summary>
public record ApplyResult(
    bool Ok,
    int Created,
    int Skipped,
    IReadOnlyList<string> Placeholders,
    string? Error = null);
