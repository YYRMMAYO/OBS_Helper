using System.Text.Json;

namespace OBS_Helper.Wpf.Services.Update;

/// <summary>
/// 从 GitHub <c>releases/latest</c> 的 JSON 里挑出「Windows x64 安装包」的直链。
///
/// 为什么要按资产名挑，而不是硬编码版本号：OBS 的安装包文件名带版本
/// （<c>OBS-Studio-32.2.2-Windows-x64-Installer.exe</c>），写死版本号的下场是链接很快失效；
/// 走 API 动态取，才能拿到「当前稳定版」的直链。
///
/// 纯逻辑（只依赖 System.Text.Json），可被单测工程直接链接编译；
/// 网络 IO 在 <see cref="ObsReleaseInfoService"/> 里。**永不抛异常**。
/// </summary>
public static class ObsInstallerAsset
{
    /// <summary>Windows x64 安装包资产名的后缀（大小写不敏感）。</summary>
    public const string WindowsInstallerSuffix = "-Windows-x64-Installer.exe";

    /// <summary>资产名是否为本应用要的 Windows 安装包（排除 .zip 便携包与 pdb）。</summary>
    public static bool IsWindowsInstaller(string? assetName)
        => !string.IsNullOrEmpty(assetName)
           && assetName.EndsWith(WindowsInstallerSuffix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 从 Release JSON 中取 Windows 安装包直链；取不到返回 null。
    ///
    /// 只认 <c>browser_download_url</c>，并且必须通过
    /// <see cref="ObsDownloadLinks.IsOfficialDownloadUrl"/>（https + github.com/obsproject/obs-studio）——
    /// 响应体一旦异常（被代理篡改 / 结构变化），宁可返回 null 让上层退回「打开发布页」。
    /// </summary>
    public static string? PickWindowsInstallerUrl(string? releaseJson)
    {
        if (string.IsNullOrWhiteSpace(releaseJson)) return null;

        try
        {
            using var doc = JsonDocument.Parse(releaseJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
                return null;

            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.ValueKind != JsonValueKind.Object) continue;

                var name = asset.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                    ? n.GetString() : null;
                if (!IsWindowsInstaller(name)) continue;

                var url = asset.TryGetProperty("browser_download_url", out var u) && u.ValueKind == JsonValueKind.String
                    ? u.GetString() : null;
                if (ObsDownloadLinks.IsOfficialDownloadUrl(url)) return url;
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// 资产名里解析出的版本号（如 <c>OBS-Studio-32.2.2-Windows-x64-Installer.exe</c> → <c>32.2.2</c>）；
    /// 解析不出返回 null。用于给按钮 / 提示补上「当前稳定版」字样。
    /// </summary>
    public static string? PickWindowsInstallerVersion(string? releaseJson)
    {
        if (string.IsNullOrWhiteSpace(releaseJson)) return null;

        try
        {
            using var doc = JsonDocument.Parse(releaseJson);
            if (!doc.RootElement.TryGetProperty("assets", out var assets)
                || assets.ValueKind != JsonValueKind.Array) return null;

            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.ValueKind != JsonValueKind.Object) continue;
                var name = asset.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                    ? n.GetString() : null;
                if (!IsWindowsInstaller(name)) continue;

                const string prefix = "OBS-Studio-";
                if (name!.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    var rest = name[prefix.Length..];
                    var idx = rest.IndexOf('-');
                    if (idx > 0) return rest[..idx];
                }
            }

            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
