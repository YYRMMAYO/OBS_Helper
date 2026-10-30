namespace OBS_Helper.Wpf.Models.Plugins;

/// <summary>
/// 应用管理的插件安装清单（V3.3.0）：<c>%LocalAppData%\OBS_Helper\plugins\installed.json</c>。
///
/// 只记录**本应用装过的**插件；用户自己手工放进 OBS 的插件不在这里，
/// 因此卸载 / 回滚永远不会误删未管理的文件（PLAN_V3.3.0 §8.4）。
/// </summary>
public sealed class PluginInstallManifest
{
    /// <summary>清单格式版本（未来迁移用）。</summary>
    public int Version { get; set; } = 1;

    public List<PluginInstallEntry> Installed { get; set; } = new();
}

/// <summary>一条已安装插件记录。</summary>
public sealed class PluginInstallEntry
{
    /// <summary>目录 / 清单标识（来自插件 id 或 owner-repo 净化后的 slug）。</summary>
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";
    public string Version { get; set; } = "";

    /// <summary>安装时间（UTC，ISO-8601）。</summary>
    public string InstalledAtUtc { get; set; } = "";

    /// <summary>来源仓库（owner/repo）；精选目录安装时也记录。</summary>
    public string SourceRepo { get; set; } = "";

    /// <summary>下载的 Release 资产名（便于复核）。</summary>
    public string AssetName { get; set; } = "";

    /// <summary>资产 SHA-256（下载后现算；有目录摘要时与目录摘要比对）。</summary>
    public string Sha256 { get; set; } = "";

    /// <summary>实际安装目录（绝对路径）。</summary>
    public string InstallDirectory { get; set; } = "";

    /// <summary>user / global。</summary>
    public string Scope { get; set; } = "user";

    /// <summary>是否为精选目录（true）或 GitHub 导入（false）。</summary>
    public bool Curated { get; set; }

    /// <summary>安装进目标目录的相对文件列表（用于精确卸载）。</summary>
    public List<string> Files { get; set; } = new();

    /// <summary>上一次备份目录（更新 / 回滚用）；没有则为空串。</summary>
    public string LastBackupDirectory { get; set; } = "";
}

/// <summary>GitHub Release 资产（V3.3.0）。</summary>
public sealed class PluginReleaseAsset
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";

    /// <summary>字节数；0 表示未知。</summary>
    public long Size { get; set; }

    /// <summary>GitHub API 的 digest（形如 sha256:...）；没有则空串。</summary>
    public string Digest { get; set; } = "";
}

/// <summary>GitHub 仓库搜索结果（V3.3.0）。</summary>
public sealed class GitHubRepoResult
{
    public string FullName { get; set; } = "";
    public string Description { get; set; } = "";
    public int Stars { get; set; }
    public string License { get; set; } = "";
    public string PushedAt { get; set; } = "";
    public string HtmlUrl { get; set; } = "";
}
