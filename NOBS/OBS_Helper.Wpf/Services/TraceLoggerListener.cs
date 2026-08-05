using System.Diagnostics;

namespace OBS_Helper.Wpf.Services;

/// <summary>
/// 把 <see cref="TraceSource"/>（如 WPF 的 PresentationTraceSources 绑定错误源）重定向到
/// <see cref="FileLogger"/>。DEBUG 构建下由 App.OnStartup 挂到 DataBindingSource，用于
/// 落盘排查绑定路径错误（P2-4）；Release 构建不注册，零开销。
/// </summary>
public sealed class TraceLoggerListener : TraceListener
{
    private readonly string _category;

    public TraceLoggerListener(string category) => _category = category;

    public override void Write(string? message)
    {
        if (!string.IsNullOrWhiteSpace(message))
            FileLogger.Info(_category, message.TrimEnd());
    }

    public override void WriteLine(string? message) => Write(message);
}
