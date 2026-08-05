namespace OBS_Helper.Wpf.Services;

/// <summary>
/// 输入防抖：连续触发时只执行最后一次（如搜索框「边打边搜」）。
///
/// 实现：每次触发先取消上一次待执行的延迟任务，再以新的 300ms 延迟执行最新一次；
/// 回调恢复到调用方的 <see cref="SynchronizationContext"/>（UI 线程）执行，保证内部 UI 操作安全；
/// 执行前校验 CancellationToken，避免竞态。线程安全：可任意线程调用。
/// </summary>
public sealed class Debouncer
{
    private readonly TimeSpan _delay;
    private CancellationTokenSource? _cts;

    public Debouncer(TimeSpan delay) => _delay = delay;

    /// <summary>触发一次（同步回调）。回调只会在距上次触发至少 <see cref="_delay"/> 后执行一次。</summary>
    public void Debounce(Action action)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        var cts = _cts = new CancellationTokenSource();
        var token = cts.Token;
        // 捕获调用方上下文（UI 线程调用时为 DispatcherSynchronizationContext），延迟后恢复回来
        var context = SynchronizationContext.Current;

        _ = Task.Delay(_delay, token).ContinueWith(_ =>
        {
            if (token.IsCancellationRequested) return;
            if (context is not null)
                context.Post(_ => InvokeSafe(action), null);
            else
                InvokeSafe(action);
        }, TaskScheduler.Default);
    }

    /// <summary>触发一次（异步回调）。回调异常会被捕获上报，不会变成未观察异常。</summary>
    public void DebounceAsync(Func<Task> action)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        var cts = _cts = new CancellationTokenSource();
        var token = cts.Token;
        var context = SynchronizationContext.Current;

        _ = Task.Delay(_delay, token).ContinueWith(_ =>
        {
            if (token.IsCancellationRequested) return;
            if (context is not null)
                context.Post(_ => InvokeSafeAsync(action), null);
            else
                InvokeSafeAsync(action);
        }, TaskScheduler.Default);
    }

    /// <summary>取消未执行的防抖任务（页面离开 / 输入框清空时调用）。</summary>
    public void Cancel()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private static void InvokeSafe(Action action)
    {
        try { action(); }
        catch (Exception ex)
        {
            // 防抖回调里的异常不能变成未观察异常，统一交给全局错误处理
            App.ReportError(Errors.ErrorCodes.Unknown, ex);
        }
    }

    private static async void InvokeSafeAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex)
        {
            App.ReportError(Errors.ErrorCodes.Unknown, ex);
        }
    }
}
