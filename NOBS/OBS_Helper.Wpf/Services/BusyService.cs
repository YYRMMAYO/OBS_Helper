using System.Windows;
using System.Windows.Controls;

namespace OBS_Helper.Wpf.Services;

/// <summary>
/// 全局加载态遮罩（P0「长操作缺加载态」）。
/// 挂在 MainWindow 的 BusyOverlayHost 上；用计数器防止多个异步操作嵌套时提前消失。
/// 接入方必须用 try/finally 保证异常路径也调用 <see cref="Hide"/>，避免遮罩卡死。
/// 线程安全：非 UI 线程调用自动投递到 UI 线程。
/// </summary>
public sealed class BusyService
{
    private readonly ContentControl _host;
    private int _count;

    public BusyService(ContentControl host) => _host = host;

    /// <summary>显示遮罩（可传提示文案；为空时沿用上一次文案）。</summary>
    public void Show(string? message = null)
    {
        if (!_host.Dispatcher.CheckAccess())
        {
            _host.Dispatcher.BeginInvoke(() => Show(message));
            return;
        }

        _count++;
        if (!string.IsNullOrEmpty(message)) _host.Tag = message;
        _host.Visibility = Visibility.Visible;
    }

    /// <summary>隐藏遮罩（计数归零才真正收起，避免覆盖外层操作）。</summary>
    public void Hide()
    {
        if (!_host.Dispatcher.CheckAccess())
        {
            _host.Dispatcher.BeginInvoke(Hide);
            return;
        }

        if (_count > 0) _count--;
        if (_count == 0) _host.Visibility = Visibility.Collapsed;
    }
}
