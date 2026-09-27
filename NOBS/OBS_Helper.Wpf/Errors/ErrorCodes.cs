using OBS_Helper.Wpf.Localization;

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
///   <item>8xx OBS 配置管理（备份 / 导入导出 / 重置 / 模板）</item>
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
    public const string DiagnosticExportFailed = "OBS704";

    // 8xx OBS 配置管理
    public const string ObsConfigNotFound = "OBS801";
    public const string ObsRunning = "OBS802";
    public const string BackupFailed = "OBS803";
    public const string ImportRejected = "OBS804";
    public const string ResetFailed = "OBS805";
    public const string TemplateApplyFailed = "OBS806";

    /// <summary>
    /// 返回某报错码的用户可读说明（含解决建议）。
    /// 文案取自文案表（键 <c>err.&lt;码&gt;</c>），跟随语言切换；未知码回退到兜底说明。
    /// </summary>
    public static string Describe(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return Strings.T("err." + Unknown);

        var key = "err." + code;
        var text = Strings.T(key);

        // Strings.T 查不到时会原样返回键名（"err.OBS999"）——那就是没定义过的码
        return string.Equals(text, key, StringComparison.Ordinal)
            ? Strings.T("err.undefined")
            : text;
    }

    /// <summary>把错误码与说明拼成一行提示，便于直接显示在状态栏。</summary>
    public static string Format(string code, string? extra = null)
        => string.IsNullOrWhiteSpace(extra)
            ? $"[{code}] {Describe(code)}"
            : Strings.T("err.formatWithExtra", code, Describe(code), extra);
}
