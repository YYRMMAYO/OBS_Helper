namespace OBS_Helper.Wpf.Services.Update;

/// <summary>
/// 知识库 / 插件目录「raw 主通道」的地址常量（纯常量、无依赖，可被单测工程直接链接编译）。
///
/// 为什么单独成一个文件：这两条地址是「跨仓库布局的约定」，一旦写错不会编译报错，
/// 只会在运行时静默 404 并悄悄退化成 Release 资产兜底（V2.9 及之前就是这样）。
/// 单独成文件后可以像其它 Core 一样被单测钉住（见 KnowledgeBaseUrlsTests）：
/// 既校验 URL 形状，也用本机源码树校验「这段路径真的存在」。
/// </summary>
public static class KnowledgeBaseUrls
{
    /// <summary>GitHub 仓库名。</summary>
    public const string RepoSlug = "YYRMMAYO/OBS_Helper";

    /// <summary>raw 通道里的分支名（本仓库的默认分支就是 master）。</summary>
    public const string Branch = "master";

    /// <summary>
    /// 仓库中源码所在的前缀。**仓库根目录下是 NOBS/**（根下另有 README、assets 等），
    /// 所以 raw 路径必须是 <c>NOBS/OBS_Helper.Wpf/Assets/…</c>。
    ///
    /// 【V2.9.1 修正】此前两条地址都漏了这个 <c>NOBS/</c> 前缀，实测恒为 404
    /// （补上前缀后同一路径返回 200），也就是「GitHub raw 主通道」自上线起从未生效，
    /// 知识库更新一直靠 Release 资产兜底。改错这一处不会有任何编译期报错，
    /// 因此这里把前缀抽成常量并配了单元测试。
    /// </summary>
    public const string SourcePathPrefix = "NOBS/OBS_Helper.Wpf/Assets/";

    /// <summary>raw 主通道：问题库（problems.json，简体中文）。</summary>
    public const string RawProblems =
        "https://raw.githubusercontent.com/" + RepoSlug + "/" + Branch + "/" + SourcePathPrefix + "problems.json";

    /// <summary>raw 主通道：插件目录（plugins.json，简体中文）。</summary>
    public const string RawPlugins =
        "https://raw.githubusercontent.com/" + RepoSlug + "/" + Branch + "/" + SourcePathPrefix + "plugins.json";

    /// <summary>raw 主通道：问题库英文版（problems.en-US.json，V2.9.3 新增）。</summary>
    public const string RawProblemsEn =
        "https://raw.githubusercontent.com/" + RepoSlug + "/" + Branch + "/" + SourcePathPrefix + "problems.en-US.json";

    /// <summary>raw 主通道：插件目录英文版（plugins.en-US.json，V2.9.3 新增）。</summary>
    public const string RawPluginsEn =
        "https://raw.githubusercontent.com/" + RepoSlug + "/" + Branch + "/" + SourcePathPrefix + "plugins.en-US.json";

    /// <summary>问题库在仓库里的相对路径（校验用，与 <see cref="RawProblems"/> 的尾部必须一致）。</summary>
    public const string RepoRelativeProblemsPath = SourcePathPrefix + "problems.json";

    /// <summary>插件目录在仓库里的相对路径（校验用）。</summary>
    public const string RepoRelativePluginsPath = SourcePathPrefix + "plugins.json";

    /// <summary>英文问题库在仓库里的相对路径（V2.9.3）。</summary>
    public const string RepoRelativeProblemsEnPath = SourcePathPrefix + "problems.en-US.json";

    /// <summary>英文插件目录在仓库里的相对路径（V2.9.3）。</summary>
    public const string RepoRelativePluginsEnPath = SourcePathPrefix + "plugins.en-US.json";

    /// <summary>
    /// 按语言取 raw 地址（V2.9.3）。英文走 <c>.en-US.json</c>，其余一律中文那份 ——
    /// 与 <c>ContentAssets</c> 的资产命名约定完全一致，避免两处各写一套后缀。
    /// </summary>
    public static string RawFor(string baseName, string? language)
        => RawBase + SourcePathPrefix + OBS_Helper.Wpf.Localization.ContentAssets.FileName(baseName, language);

    /// <summary>raw 主通道的基址（拼完整 URL 用，避免各处手写前缀）。</summary>
    public const string RawBase =
        "https://raw.githubusercontent.com/" + RepoSlug + "/" + Branch + "/";

    /// <summary>仓库网页地址（Release / 源码），供 UI「打开 GitHub」类入口复用。</summary>
    public const string RepoWebUrl = "https://github.com/" + RepoSlug;
}
