namespace OBS_Helper.Win.Errors;

/// <summary>
/// Windows 桌面壳（WebView2）侧的报错码。与 <c>OBS_Helper.Client.Errors.ErrorCodes</c>
/// 使用同一套编码规则，便于在文档 <c>docs/ERROR_CODES.md</c> 中统一索引。
/// </summary>
public static class AppError
{
    public const string WebViewInitFailed = "OBS101";
    public const string SiteResourceMissing = "OBS102";
    public const string RuntimeMissing = "OBS103";
    public const string Unknown = "OBS900";

    /// <summary>组装供 MessageBox 展示的「报错码 + 标题 + 解决方案 + 详细错误」文本。</summary>
    public static string Format(string code, string detail)
    {
        return $"[{code}] {Title(code)}\n\n{Solution(code)}\n\n详细错误：{detail}";
    }

    public static string Title(string code) => code switch
    {
        WebViewInitFailed => "无法初始化内置浏览器（WebView2）",
        SiteResourceMissing => "站点资源缺失",
        RuntimeMissing => "WebView2 运行时缺失",
        _ => "启动失败"
    };

    public static string Solution(string code) => code switch
    {
        WebViewInitFailed => "请确认系统已安装 Microsoft Edge WebView2 Runtime，或改用随附安装包（已内置运行时）。",
        SiteResourceMissing => "请确认 OBS_Helper.exe 与 wwwroot 文件夹位于同一目录。",
        RuntimeMissing => "请安装 Microsoft Edge WebView2 Runtime 后重试。",
        _ => "请重试；若持续出现，请联系支持并附上报错码。"
    };
}
