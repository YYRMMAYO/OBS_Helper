using System.IO;
using System.Text.Json;
using OBS_Helper.Wpf.Services.Automation;
using OBS_Helper.Wpf.Services.Mobile;

namespace OBS_Helper.Wpf.Services.Live;

/// <summary>
/// 装修市场（E03）：资产包哈希校验 → 预览目录 → 应用 → 回滚。
/// 只接受白名单扩展名、相对路径与 64 位哈希；不执行远程代码；CSP 保持不变。
/// </summary>
public sealed class ThemeMarketService
{
    private readonly string _root;
    public ThemeMarketService(string? root = null)
        => _root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OBS_Helper", "themes");

    public string ActiveVersion(string themeId)
    {
        try
        {
            var marker = Path.Combine(_root, themeId, "active.txt");
            return File.Exists(marker) ? File.ReadAllText(marker).Trim() : "";
        }
        catch (Exception) { return ""; }
    }

    public (bool Ok, string Code, List<string> Issues) Validate(ThemeMarketCore.ThemePackage package, string sourceDirectory)
    {
        var (ok, code, issues) = ThemeMarketCore.Validate(package);
        if (!ok) return (false, code, issues);
        foreach (var file in package.Files)
        {
            var relative = OBS_Helper.Wpf.Services.Interop.SidecarCore.SafeRelativePath(file.Path);
            if (relative is null) { issues.Add("不安全路径：" + file.Path); continue; }
            var source = Path.Combine(sourceDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
            var (fileOk, fileCode) = ThemeMarketCore.VerifyFile(source, file);
            if (!fileOk) issues.Add(fileCode + "：" + file.Path);
        }
        return (issues.Count == 0, issues.Count == 0 ? "" : FeatureErrors.ThemeHashMismatch, issues);
    }

    /// <summary>应用主题：先复制到临时目录，校验通过后再切换；保留上一版用于回滚。</summary>
    public (bool Ok, string Code, string Detail) Apply(ThemeMarketCore.ThemePackage package, string sourceDirectory)
    {
        var (ok, code, issues) = Validate(package, sourceDirectory);
        if (!ok) return (false, code, string.Join("; ", issues));
        try
        {
            var themeDir = Path.Combine(_root, package.Id);
            var target = Path.Combine(themeDir, package.Version);
            var temp = target + ".tmp";
            if (Directory.Exists(temp)) Directory.Delete(temp, true);
            Directory.CreateDirectory(temp);
            foreach (var file in package.Files)
            {
                var relative = OBS_Helper.Wpf.Services.Interop.SidecarCore.SafeRelativePath(file.Path)!;
                var source = Path.Combine(sourceDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
                var destination = Path.Combine(temp, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, overwrite: true);
            }
            var previous = ActiveVersion(package.Id);
            if (Directory.Exists(target)) Directory.Delete(target, true);
            Directory.Move(temp, target);
            Directory.CreateDirectory(themeDir);
            File.WriteAllText(Path.Combine(themeDir, "active.txt"), package.Version);
            File.WriteAllText(Path.Combine(themeDir, "previous.txt"), previous);
            return (true, "", target);
        }
        catch (Exception ex)
        {
            return (false, FeatureErrors.ThemeUnsafeAsset, ex.GetType().Name);
        }
    }

    public (bool Ok, string Code) Rollback(string themeId)
    {
        try
        {
            var themeDir = Path.Combine(_root, themeId);
            var previousFile = Path.Combine(themeDir, "previous.txt");
            if (!File.Exists(previousFile)) return (false, FeatureErrors.ThemeHashMismatch);
            var previous = File.ReadAllText(previousFile).Trim();
            if (previous.Length == 0 || !Directory.Exists(Path.Combine(themeDir, previous))) return (false, FeatureErrors.ThemeHashMismatch);
            var current = ActiveVersion(themeId);
            File.WriteAllText(Path.Combine(themeDir, "active.txt"), previous);
            File.WriteAllText(Path.Combine(themeDir, "previous.txt"), current);
            return (true, "");
        }
        catch (Exception) { return (false, FeatureErrors.ThemeUnsafeAsset); }
    }

    public string ActiveDirectory(string themeId)
        => Path.Combine(_root, themeId, ActiveVersion(themeId));
}
