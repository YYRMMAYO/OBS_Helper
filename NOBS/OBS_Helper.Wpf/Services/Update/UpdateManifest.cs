using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OBS_Helper.Wpf.Services.Update;

/// <summary>
/// 增量更新包清单（update_manifest.json 的反序列化目标）。
///
/// 增量包 = 从 <see cref="BaseVersion"/> 升级到 <see cref="TargetVersion"/> 所需的全部差异：
/// <see cref="Files"/> 是需要新增 / 覆盖的文件（相对应用目录），<see cref="Remove"/> 是应当删除的旧文件。
/// 由构建脚本（build.ps1）比对上一版本完整清单生成，随增量 zip 一并发布。
/// </summary>
public sealed class UpdateManifest
{
    [JsonPropertyName("format")]
    public int Format { get; set; } = 1;

    /// <summary>增量包的基准版本（用户必须 ≥ 此版本才可直接应用，否则回退完整安装包）。</summary>
    [JsonPropertyName("baseVersion")]
    public string BaseVersion { get; set; } = "";

    /// <summary>升级目标版本。</summary>
    [JsonPropertyName("targetVersion")]
    public string TargetVersion { get; set; } = "";

    /// <summary>需要新增 / 覆盖的文件（相对应用目录，正斜杠分隔）。</summary>
    [JsonPropertyName("files")]
    public List<ManifestFileEntry> Files { get; set; } = new();

    /// <summary>应当删除的旧文件（相对应用目录，正斜杠分隔；目录为空目录由运行时顺带清理）。</summary>
    [JsonPropertyName("remove")]
    public List<string> Remove { get; set; } = new();
}

/// <summary>清单中的单个文件：相对路径 + 大小 + SHA256，下载后据此校验完整性。</summary>
public sealed class ManifestFileEntry
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = "";
}
