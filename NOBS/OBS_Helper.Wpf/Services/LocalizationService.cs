using System.IO;
using System.Windows;
using OBS_Helper.Wpf.Localization;
using OBS_Helper.Wpf.Services.Host;

namespace OBS_Helper.Wpf.Services;

/// <summary>
/// 语言服务（V2.9.2）：把 <see cref="Strings"/> 的当前语言落地到界面。
///
/// 与 <see cref="AppearanceService"/> 同一套路数：把文案表写进 <c>Application.Resources</c>，
/// XAML 一律用 <c>{DynamicResource Loc.&lt;键&gt;}</c> 引用，因此语言切换是整窗即时生效的，
/// 与换肤共享同一套 DynamicResource 机制、不需要重启。
///
/// 语言默认值的三级来源：
/// <list type="number">
///   <item>用户在应用内选过的语言（<c>prefs.json</c> 的 <c>obshelper.language</c>）—— 最高优先级；</item>
///   <item>安装时在安装向导里选的语言（安装目录下的 <c>language.ini</c>，由 Inno Setup 写入）——
///     只在用户**没有**在应用内选过语言时生效，因此重装/升级不会覆盖用户的选择；</item>
///   <item>简体中文 —— 便携版（解压即用、没有安装向导）与任何取不到来源的情况都落到这里。</item>
/// </list>
/// </summary>
public sealed class LocalizationService
{
    /// <summary>prefs.json 里的语言偏好键。</summary>
    internal const string StorageKey = "obshelper.language";

    /// <summary>安装向导写下的语言文件（与可执行文件同目录）。</summary>
    internal const string InstallerLanguageFileName = "language.ini";

    private readonly LocalStore _store;
    private bool _loaded;

    public LocalizationService(LocalStore store) => _store = store;

    /// <summary>语言变化后触发（旧值, 新值）。页面据此重建代码里拼出来的文案。</summary>
    public event Action<string, string>? Changed;

    /// <summary>当前语言标识（<see cref="Strings.ZhHans"/> / <see cref="Strings.EnUs"/>）。</summary>
    public string Current => Strings.Current;

    /// <summary>当前语言是否来自用户的显式选择（而非安装向导 / 默认值）。</summary>
    public bool HasUserChoice => !string.IsNullOrWhiteSpace(_store.GetItem(StorageKey));

    /// <summary>应用启动时调用一次：解析语言 → 载入 → 写进 Application.Resources。</summary>
    public void Initialize()
    {
        if (_loaded) return;
        _loaded = true;

        var resolved = ResolveInitialLanguage();
        Strings.SetLanguage(resolved);
        Apply();
    }

    /// <summary>
    /// 解析首启语言：用户选择 &gt; 安装向导 &gt; 简体中文。
    /// </summary>
    internal string ResolveInitialLanguage()
    {
        var stored = _store.GetItem(StorageKey);
        if (Strings.IsSupported(stored)) return Strings.Normalize(stored);

        var installed = ReadInstallerLanguage();
        if (Strings.IsSupported(installed)) return Strings.Normalize(installed);

        return Strings.ZhHans;
    }

    /// <summary>
    /// 读取安装向导写下的语言（<c>language.ini</c>）：内容形如
    /// <c>[app]</c> / <c>language=chinesesimplified</c>。读不到或格式不对返回 null，
    /// 绝不让「文件缺失」影响启动。
    /// </summary>
    internal static string? ReadInstallerLanguage()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, InstallerLanguageFileName);
            if (!File.Exists(path)) return null;

            foreach (var rawLine in File.ReadAllLines(path))
            {
                var line = rawLine.Trim();
                var eq = line.IndexOf('=');
                if (eq <= 0) continue;
                if (!line[..eq].Trim().Equals("language", StringComparison.OrdinalIgnoreCase)) continue;

                var value = line[(eq + 1)..].Trim();
                if (value.Length > 0) return value;
            }
        }
        catch (Exception)
        {
            // 读不到就当没有：语言回退默认值，不影响任何功能
        }
        return null;
    }

    /// <summary>
    /// 切换语言并即时生效。返回是否真的变了（同语言重复设置返回 false，不触发 <see cref="Changed"/>）。
    /// </summary>
    public bool SetLanguage(string? language)
    {
        var target = Strings.Normalize(language);
        var previous = Strings.Current;
        if (string.Equals(previous, target, StringComparison.Ordinal)) return false;

        // 先记偏好再改语言：即便随后的刷新抛异常，下次启动也已经是用户选的语言
        try
        {
            _store.SetItem(StorageKey, target);
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Localization", "写入语言偏好失败：" + ex.Message);
        }

        Strings.SetLanguage(target);
        Apply();
        Changed?.Invoke(previous, target);
        return true;
    }

    /// <summary>
    /// 把当前语言的文案写进 <c>Application.Resources</c>（键为 <c>Loc.&lt;键&gt;</c>）。
    ///
    /// 写在 Application.Resources 顶层而不是合并字典里：顶层条目的查找优先级高于合并字典，
    /// 与 AppearanceService 写调色板是同一个理由 —— 覆盖即时生效、且不需要重建窗口。
    /// </summary>
    public void Apply()
    {
        var app = Application.Current;
        if (app is null) return;

        var res = app.Resources;
        foreach (var (key, value) in Strings.Table())
        {
            res[Strings.ResourceKey(key)] = value;
        }
    }
}
