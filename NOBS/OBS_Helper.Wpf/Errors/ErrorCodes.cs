namespace OBS_Helper.Wpf.Errors;

/// <summary>
/// 全局报错码定义。所有报错码以 <c>OBS</c> 开头，后接 3 位数字：
/// <list type="bullet">
///   <item>1xx 启动 / 运行时</item>
///   <item>2xx 数据加载</item>
///   <item>3xx 路由 / 页面</item>
///   <item>4xx 本地存储（收藏 / 步骤进度 / 设置）</item>
///   <item>5xx 助手 / 搜索</item>
///   <item>6xx OBS 连接</item>
///   <item>7xx AI 诊断</item>
///   <item>9xx 组件 / 未知</item>
/// </list>
/// 编码沿用 Blazor 版，便于历史工单与文档对照；1xx 的文案已按原生 WPF 场景重写。
/// </summary>
public static class ErrorCodes
{
    public const string Unknown = "OBS900";

    // 1xx 启动 / 运行时
    public const string StartupFailed = "OBS101";
    public const string ResourceMissing = "OBS102";
    public const string RuntimeMissing = "OBS103";

    // 2xx 数据加载
    public const string DataLoadFailed = "OBS201";
    public const string DataParseFailed = "OBS202";

    // 3xx 路由 / 页面
    public const string PageNotFound = "OBS301";
    public const string NavigationFailed = "OBS302";

    // 4xx 本地存储
    public const string LocalStorageUnavailable = "OBS401";
    public const string SecretStoreUnavailable = "OBS402";

    // 5xx 助手 / 搜索
    public const string AssistantIndexFailed = "OBS501";

    // 6xx OBS 连接
    public const string ObsConnectFailed = "OBS601";
    public const string ObsAuthFailed = "OBS602";
    public const string ObsRequestFailed = "OBS603";
    public const string ObsHandshakeTimeout = "OBS604";

    // 7xx AI 诊断
    public const string AiCloudNotConfigured = "OBS701";
    public const string AiCloudRequestFailed = "OBS702";
    public const string AiResponseInvalid = "OBS703";

    /// <summary>返回某报错码的用户可读说明（含解决建议）。</summary>
    public static string Describe(string code) => code switch
    {
        Unknown => "发生未知错误，可尝试重启应用。",
        StartupFailed => "应用启动失败，请确认已安装 .NET 桌面运行时，或改用随附的自包含安装包。",
        ResourceMissing => "内置资源缺失，程序文件可能不完整，请重新安装。",
        RuntimeMissing => "未找到可用的 .NET 运行时，请安装后重试。",
        DataLoadFailed => "问题数据加载失败，请重启应用；若持续出现请重新安装。",
        DataParseFailed => "问题数据解析错误，数据文件可能已损坏，请重新安装或更新应用。",
        PageNotFound => "未找到对应页面，请返回首页重新进入。",
        NavigationFailed => "页面切换失败，请返回首页重试。",
        LocalStorageUnavailable => "本地收藏 / 进度存储不可用，收藏与步骤勾选将无法保存（不影响浏览）。",
        SecretStoreUnavailable => "加密存储不可用，密码与 API Key 将无法保存，仅本次运行有效。",
        AssistantIndexFailed => "离线问答索引建立失败，可改用「搜索」或「分类」查找。",
        ObsConnectFailed => "连接 OBS 失败，请确认 OBS 已启动并在「工具 → obs-websocket 设置」中开启了服务器。",
        ObsAuthFailed => "OBS 鉴权失败，请核对 obs-websocket 密码是否正确。",
        ObsRequestFailed => "向 OBS 发送的请求执行失败，请查看返回的具体说明。",
        ObsHandshakeTimeout => "已连上 OBS 端口但未完成握手，请确认 obs-websocket 版本为 5.x（OBS 28 及以上内置）。",
        AiCloudNotConfigured => "尚未配置云端 AI：请在「设置」中填写 https 接口地址、模型名并保存 API Key。",
        AiCloudRequestFailed => "云端 AI 请求失败，已自动回退到本地规则引擎。",
        AiResponseInvalid => "云端 AI 返回内容无法解析，已按本地规则给出结论。",
        _ => "未定义的错误码。"
    };

    /// <summary>把错误码与说明拼成一行提示，便于直接显示在状态栏。</summary>
    public static string Format(string code, string? extra = null)
        => string.IsNullOrWhiteSpace(extra)
            ? $"[{code}] {Describe(code)}"
            : $"[{code}] {Describe(code)}（{extra}）";
}
