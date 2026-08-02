namespace OBS_Helper.Client.Errors;

/// <summary>
/// 全局报错码定义。所有报错码以 <c>OBS</c> 开头，后接 3 位数字：
/// <list type="bullet">
///   <item>1xx 启动 / 运行时（主要在 Windows 桌面壳抛出，经 JS 互操作上报）</item>
///   <item>2xx 数据加载</item>
///   <item>3xx 路由 / 页面</item>
///   <item>4xx 本地存储（收藏 / 步骤进度）</item>
///   <item>5xx 助手 / 搜索</item>
///   <item>9xx 组件 / 未知</item>
/// </list>
/// 每个报错码对应的现象、原因与解决方案见仓库 <c>docs/ERROR_CODES.md</c>。
/// </summary>
public static class ErrorCodes
{
    public const string Unknown = "OBS900";

    // 1xx 启动 / 运行时
    public const string WebViewInitFailed = "OBS101";
    public const string SiteResourceMissing = "OBS102";
    public const string RuntimeMissing = "OBS103";

    // 2xx 数据加载
    public const string DataLoadFailed = "OBS201";
    public const string DataParseFailed = "OBS202";

    // 3xx 路由 / 页面
    public const string PageNotFound = "OBS301";
    public const string NavigationFailed = "OBS302";

    // 4xx 本地存储
    public const string LocalStorageUnavailable = "OBS401";

    // 5xx 助手 / 搜索
    public const string AssistantIndexFailed = "OBS501";

    /// <summary>返回某报错码的用户可读说明（含解决建议）。</summary>
    public static string Describe(string code) => code switch
    {
        Unknown => "发生未知错误，可尝试重启应用。",
        WebViewInitFailed => "内置浏览器（WebView2）初始化失败，请确认已安装 Microsoft Edge WebView2 Runtime，或使用随附安装包（已内置运行时）。",
        SiteResourceMissing => "站点资源目录 wwwroot 缺失，请确认 OBS_Helper.exe 与 wwwroot 文件夹位于同一目录。",
        RuntimeMissing => "未找到可用的 WebView2 运行时，请安装后重试。",
        DataLoadFailed => "问题数据加载失败，请检查网络或重启应用。",
        DataParseFailed => "问题数据解析错误，数据文件可能已损坏，请重新安装或更新应用。",
        PageNotFound => "未找到对应页面（404），请返回首页重新进入。",
        NavigationFailed => "页面导航失败，请返回首页重试。",
        LocalStorageUnavailable => "本地收藏 / 进度存储不可用，收藏与步骤勾选将无法保存（不影响浏览）。",
        AssistantIndexFailed => "离线问答索引建立失败，可改用「搜索」或「分类」查找。",
        _ => "未定义的错误码。"
    };
}
