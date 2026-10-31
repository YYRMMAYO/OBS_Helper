using System.IO;

namespace OBS_Helper.Wpf.Services.Dependencies;

/// <summary>一次「依赖已就绪」检查 / 一键安装的结果。</summary>
public sealed record DependencyProvisionResult(bool Ok, string Code, string Path, bool DigestVerified);

/// <summary>
/// 依赖准备器（X1031Y3）：把「查最新 Release → 下载 → 校验 → 解压 → 写入路径」收成一个动作，
/// 供字幕 / 切片 / 转推 / 归档 / 功能中心共用。
///
/// 已有可用路径时直接返回，不重复下载；安装成功后调用 <see cref="DependencyService.Configure"/>，
/// 由它负责持久化与立即生效。失败返回稳定错误码，页面负责显示下一步。
/// </summary>
public sealed class DependencyProvisioner
{
    private readonly DependencyInstallService _installer;
    private readonly DependencyService _config;

    public DependencyProvisioner(DependencyInstallService installer, DependencyService config)
    {
        _installer = installer;
        _config = config;
    }

    public async Task<DependencyProvisionResult> EnsureAsync(
        DependencySpec spec, IProgress<(long Done, long Total)>? progress, CancellationToken ct)
    {
        var existing = _config.ResolvePath(spec);
        if (!string.IsNullOrWhiteSpace(existing) && File.Exists(existing))
            return new DependencyProvisionResult(true, "", existing, true);

        var installed = await _installer.InstallAsync(spec, progress, ct).ConfigureAwait(false);
        if (!installed.Ok)
            return new DependencyProvisionResult(false, installed.Code, "", installed.DigestVerified);

        var (ok, code, path) = _config.Configure(spec, installed.Path);
        return new DependencyProvisionResult(ok, ok ? "" : code, path, installed.DigestVerified);
    }
}
