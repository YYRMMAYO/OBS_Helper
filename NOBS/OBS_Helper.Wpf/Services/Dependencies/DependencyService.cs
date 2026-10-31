using System.IO;
using OBS_Helper.Wpf.Services.Interop;

namespace OBS_Helper.Wpf.Services.Dependencies;

/// <summary>sidecar 能力的最小抽象：便于单测注入，也避免依赖服务层整条链路。</summary>
public interface IDependencySidecars
{
    string Root { get; }
    SidecarEntry? Find(string id);
    string? InstalledExecutable(SidecarEntry entry);
    Task<(bool Ok, string Code, string Path)> InstallAsync(
        SidecarEntry entry, IProgress<(long Done, long Total)>? progress = null, CancellationToken ct = default);
}

/// <summary>一个依赖的当前状态。</summary>
public sealed record DependencyStatus(DependencySpec Spec, string? Path)
{
    public bool Ready => Path is not null && File.Exists(Path);
}

/// <summary>
/// 开源依赖中心的业务逻辑（V3.4.1）。
///
/// <list type="bullet">
///   <item><b>一键下载</b>：直达官方 GitHub Releases。清单里补全了 SHA-256 / 体积后会自动改走
///         <see cref="IDependencySidecars.InstallAsync"/>（下载 + 哈希校验 + 解压），否则只打开官方页面；</item>
///   <item><b>一键配置</b>：在本机有界搜索同名 exe，校验通过后写入设置并立即生效；
///         mediamtx 这类只认 sidecar 目录的组件会复制到安装目录，而不是改 PATH；</item>
///   <item>所有路径都来自用户本机或官方来源，不联网抓取不明二进制、不改系统环境变量。</item>
/// </list>
/// </summary>
public sealed class DependencyService
{
    private readonly IDependencySidecars _sidecars;
    private readonly Func<string, string?> _read;
    private readonly Action<string, string?> _write;
    private readonly Action<string, string> _apply;
    private readonly Func<string, Task<bool>> _openUrl;

    public DependencyService(
        IDependencySidecars sidecars,
        Func<string, string?> read,
        Action<string, string?> write,
        Action<string, string> apply,
        Func<string, Task<bool>> openUrl)
    {
        _sidecars = sidecars;
        _read = read;
        _write = write;
        _apply = apply;
        _openUrl = openUrl;
    }

    public IReadOnlyList<DependencyStatus> Scan()
        => DependencyCore.Items.Select(spec => new DependencyStatus(spec, ResolvePath(spec))).ToList();

    /// <summary>已配置路径（设置优先，其次 sidecar 安装目录）。</summary>
    public string? ResolvePath(DependencySpec spec)
    {
        if (!string.IsNullOrWhiteSpace(spec.StoreKey))
        {
            var stored = _read(spec.StoreKey);
            if (!string.IsNullOrWhiteSpace(stored) && File.Exists(stored)) return stored;
        }
        var entry = SidecarEntry(spec);
        return entry is null ? null : _sidecars.InstalledExecutable(entry);
    }

    /// <summary>候选搜索目录（含 sidecar 安装目录），用于“一键配置”的自动识别。</summary>
    public List<string> SearchDirectories(DependencySpec spec)
    {
        var extra = new List<string>();
        var entry = SidecarEntry(spec);
        if (entry is not null)
        {
            var dir = SidecarCore.InstallDirectory(_sidecars.Root, entry);
            if (!string.IsNullOrWhiteSpace(dir)) extra.Add(dir);
        }
        return DependencyCore.CandidateDirectories(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetEnvironmentVariable("PATH"),
            extra);
    }

    /// <summary>自动识别本机已下载的 exe；找不到返回 null，由界面再弹文件选择。</summary>
    public string? Detect(DependencySpec spec)
        => DependencyCore.FindExecutable(spec.Executable, SearchDirectories(spec));

    /// <summary>
    /// 应用一个用户选定的路径。返回的错误码本身就是词表键（<c>dep.*</c>），由界面直接翻译。
    /// </summary>
    public (bool Ok, string Code, string Path) Configure(DependencySpec spec, string? path)
    {
        if (!DependencyCore.IsAcceptablePath(path, spec)) return (false, "dep.error.invalid", "");
        var full = Path.GetFullPath(path!.Trim());

        if (string.IsNullOrWhiteSpace(spec.StoreKey))
        {
            // 只认 sidecar 目录的组件（mediamtx）：复制到安装目录，不改 PATH、不写注册表。
            var entry = SidecarEntry(spec);
            var dir = entry is null ? null : SidecarCore.InstallDirectory(_sidecars.Root, entry);
            if (string.IsNullOrWhiteSpace(dir)) return (false, "dep.error.invalid", "");
            try
            {
                Directory.CreateDirectory(dir);
                var target = Path.Combine(dir, spec.Executable);
                if (!string.Equals(full, Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                    File.Copy(full, target, overwrite: true);
                return File.Exists(target)
                    ? (true, "", target)
                    : (false, "dep.error.copy", "");
            }
            catch (Exception)
            {
                return (false, "dep.error.copy", "");
            }
        }

        _write(spec.StoreKey, full);
        _apply(spec.Id, full);
        return (true, "", full);
    }

    /// <summary>
    /// 一键下载：优先走清单里已核验哈希的自动安装；否则打开官方 Releases 页面。
    /// 返回的 Code 为词表键或空串。
    /// </summary>
    public async Task<(bool Ok, string Code)> DownloadAsync(
        DependencySpec spec, IProgress<(long Done, long Total)>? progress = null, CancellationToken ct = default)
    {
        var entry = SidecarEntry(spec);
        if (entry is { Manual: false } && DependencyCore.IsOfficialGitHubUrl(entry.Url))
        {
            var (ok, code, _) = await _sidecars.InstallAsync(entry, progress, ct).ConfigureAwait(false);
            return (ok, ok ? "" : code);
        }
        if (!DependencyCore.IsOfficialGitHubUrl(spec.ReleasesUrl)) return (false, "dep.error.invalid");
        var opened = await _openUrl(spec.ReleasesUrl).ConfigureAwait(false);
        return (opened, opened ? "dep.download.opened" : "dep.download.failed");
    }

    private SidecarEntry? SidecarEntry(DependencySpec spec)
        => string.IsNullOrWhiteSpace(spec.SidecarId) ? null : _sidecars.Find(spec.SidecarId);
}
