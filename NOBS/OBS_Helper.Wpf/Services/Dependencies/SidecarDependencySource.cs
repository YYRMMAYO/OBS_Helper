using OBS_Helper.Wpf.Services.Interop;

namespace OBS_Helper.Wpf.Services.Dependencies;

/// <summary>把真实的 <see cref="SidecarManager"/> 适配成依赖中心需要的形状（不参与单测链接）。</summary>
public sealed class SidecarDependencySource : IDependencySidecars
{
    private readonly SidecarManager _manager;

    public SidecarDependencySource(SidecarManager manager) => _manager = manager;

    public string Root => _manager.Root;

    public SidecarEntry? Find(string id) => _manager.Manifest.Find(id);

    public string? InstalledExecutable(SidecarEntry entry) => _manager.InstalledExecutable(entry);

    public Task<(bool Ok, string Code, string Path)> InstallAsync(
        SidecarEntry entry, IProgress<(long Done, long Total)>? progress = null, CancellationToken ct = default)
        => _manager.InstallAsync(entry, progress, ct);
}
