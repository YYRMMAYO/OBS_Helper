using System.IO;
using System.Reflection;
using OBS_Helper.Wpf.Services.Update;

namespace OBS_Helper.Wpf.Services.Plugins;

/// <summary>
/// 插件广场目录数据访问（P0-3：数据外置到知识库通道）。
///
/// 数据源优先级与 <see cref="ProblemService"/> 完全一致：
/// <list type="bullet">
///   <item><b>外部覆盖</b>：%LocalAppData%\OBS_Helper\data\plugins.json（由 KnowledgeBaseUpdater 的
///         plugins 通道写入，链接纠错 / 新插件上架无需发版）；</item>
///   <item><b>内置种子</b>：程序集内嵌的 plugins.json，外部缺失 / 损坏时兜底。</item>
/// </list>
/// </summary>
public sealed class PluginCatalogService
{
    private static readonly SemaphoreSlim Lock = new(1, 1);

    private PluginCatalogData? _data;
    private bool _usingExternal;

    /// <summary>当前缓存是在哪种语言下加载的（V2.9.3）。为空表示还没加载过。</summary>
    private string _loadedLanguage = "";

    /// <summary>当前生效的目录版本（外部文件优先，其次内置种子）。</summary>
    public string Version => Load().Version;

    /// <summary>当前数据源：external / embedded。</summary>
    public string DataSource => _usingExternal ? "external" : "embedded";

    /// <summary>清空缓存；知识库插件通道更新完成后调用，让插件广场下次进入时重建。</summary>
    public void Reload()
    {
        Lock.Wait();
        try
        {
            _data = null;
            _loadedLanguage = "";
        }
        finally { Lock.Release(); }
    }

    /// <summary>语言变了就丢弃缓存（V2.9.3）：中英各一份目录，缓存必须跟着语言走。</summary>
    private void InvalidateOnLanguageChange()
    {
        if (_loadedLanguage.Length > 0
            && !string.Equals(_loadedLanguage, Strings.Current, StringComparison.Ordinal))
        {
            Reload();
        }
    }

    /// <summary>获取目录数据（带缓存，线程安全）。极端情况下返回空数据结构而非 null。</summary>
    public PluginCatalogData GetData()
    {
        InvalidateOnLanguageChange();
        var d = Load();
        return d;
    }

    private PluginCatalogData Load()
    {
        if (_data is not null) return _data;

        Lock.Wait();
        try
        {
            if (_data is not null) return _data;

            // 外部覆盖文件优先（与问题库同策略：损坏 / 缺失静默回退内置）
            try
            {
                var externalPath = KnowledgeBaseUpdater.LocalDataFile(ContentAssets.Plugins, Strings.Current);
                if (File.Exists(externalPath))
                {
                    var parsed = PluginCatalogCore.Parse(File.ReadAllText(externalPath));
                    if (parsed is not null)
                    {
                        _usingExternal = true;
                        _loadedLanguage = Strings.Current;
                        _data = parsed;
                        return _data;
                    }
                    FileLogger.Warn("Plugins", "外部插件目录损坏，回退内置：" + externalPath);
                }
            }
            catch (Exception ex)
            {
                FileLogger.Warn("Plugins", "外部插件目录读取失败，回退内置：" + ex.Message);
            }

            _usingExternal = false;
            _loadedLanguage = Strings.Current;
            _data = LoadEmbedded() ?? new PluginCatalogData();
            return _data;
        }
        finally
        {
            Lock.Release();
        }
    }

    private static PluginCatalogData? LoadEmbedded()
    {
        var raw = ContentAssets.ReadEmbeddedWithFallback(ContentAssets.Plugins, Strings.Current, out var fellBack);
        if (fellBack)
        {
            FileLogger.Warn("Plugins", "英文插件目录资产缺失，已回退中文随包内容："
                + ContentAssets.FileName(ContentAssets.Plugins, Strings.Current) + "（英文界面下会显示中文）");
        }
        if (raw is null) return null;
        try
        {
            return PluginCatalogCore.Parse(raw);
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Plugins", "内嵌插件目录解析失败：" + ex.Message);
            return null;
        }
    }
}
