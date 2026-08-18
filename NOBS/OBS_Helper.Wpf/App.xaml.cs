using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using OBS_Helper.Wpf.Errors;
using OBS_Helper.Wpf.Services;

namespace OBS_Helper.Wpf;

/// <summary>应用入口：单实例锁、全局异常挂钩、服务启动装配与自检模式。</summary>
public partial class App : Application
{
    /// <summary>
    /// 自检测试用开关。置 true 时 <see cref="ReportError"/> 不弹窗，改为累加到 <see cref="HeadlessErrors"/>，
    /// 以便自动化脚本（如 <c>OBS_SELFTEST=1</c>）在无界面的环境下捕获全部错误。
    /// </summary>
    public static bool HeadlessTest { get; set; }

    /// <summary>自检测试期间收集到的错误文本（仅 <see cref="HeadlessTest"/> 为 true 时填充）。</summary>
    public static List<string> HeadlessErrors { get; } = new();

    /// <summary>同一报错码弹窗节流：5 秒内不重复弹，避免连环异常刷屏（日志照常记录）。</summary>
    private static readonly TimeSpan ErrorDialogThrottle = TimeSpan.FromSeconds(5);
    private static readonly Dictionary<string, DateTime> LastDialogAt = new();
    private static readonly object DialogThrottleLock = new();

    // ------------------------------------------------------------ 单实例
    // 桌面快捷方式 / 安装完成后启动 / 托盘常驻都可能重复拉起进程。
    // 用「会话级命名 Mutex」做唯一判定：第二个实例直接退出，并通知第一个实例把窗口带回前台。
    // 拿到锁的实例再顺带清理历史版本 / 崩溃残留的同名进程，保证同一时刻只留一个。

    /// <summary>会话级单实例锁（Local\ 前缀：同一登录会话内唯一，不跨用户）。</summary>
    private const string SingleInstanceMutex = @"Local\OBS_Helper.SingleInstance";

    /// <summary>第一个实例创建、后续实例用来「唤起主窗口」的命名事件。</summary>
    private const string SingleInstanceShowEvent = @"Local\OBS_Helper.ShowMainWindow";

    private Mutex? _singleMutex;
    private EventWaitHandle? _showEvent;
    private Thread? _showListener;
    private bool _ownsSingleInstance;

    /// <summary>
    /// 尝试获取单实例锁。返回 true 表示本进程是当前唯一实例，可继续启动；
    /// 返回 false 表示已有实例在运行（本进程应立即退出）。
    /// </summary>
    private bool TryAcquireSingleInstance()
    {
        _singleMutex = new Mutex(initiallyOwned: true, SingleInstanceMutex, out var createdNew);
        if (createdNew)
        {
            _ownsSingleInstance = true;
            StartShowListener();
            return true;
        }

        // 锁已存在但可能被占用（AbandonedMutex 时 createdNew 仍为 false 但异常抛出）。
        // 这里直接视为「已有实例」，进入唤起 + 退出流程即可。
        _ownsSingleInstance = false;
        return false;
    }

    /// <summary>通知正在运行的实例把主窗口带到前台（本实例随即退出）。</summary>
    private static void SignalExistingInstance()
    {
        try
        {
            using var evt = EventWaitHandle.OpenExisting(SingleInstanceShowEvent);
            evt.Set();
        }
        catch (Exception)
        {
            // 已有实例连命名事件都创建不了（极端情况）：忽略，反正本实例要退出，
            // 不会出现两个窗口并存。
        }
    }

    /// <summary>清理同名残留进程，只留当前这一个（含旧版本无单实例保护的僵尸进程）。</summary>
    private static void KillStrayInstances()
    {
        var currentId = Environment.ProcessId;
        try
        {
            foreach (var p in Process.GetProcessesByName("OBS_Helper"))
            {
                if (p.Id == currentId) continue;

                // 防误杀：只清理确认是「本应用」的残留进程，主模块路径必须匹配当前安装目录；
                // 避免同名无关进程（用户重命名的其它程序）被误杀。
                if (!IsOwnExecutable(p)) continue;

                try
                {
                    p.Kill();
                    p.WaitForExit(3000);
                }
                catch (Exception)
                {
                    // 权限不足或进程已退出：跳过，不影响主流程
                }
            }
        }
        catch (Exception)
        {
            // 进程枚举失败（权限 / 系统限制）：不做清理，也不阻塞启动
        }
    }

    /// <summary>
    /// 判断进程是否为本应用的残留实例：主模块路径与当前程序集所在目录一致，
    /// 或文件名恰为 OBS_Helper.exe（兼容旧版安装到其它目录）。其余一律视为无关进程、不清理。
    /// </summary>
    private static bool IsOwnExecutable(Process p)
    {
        try
        {
            var ownDir = Path.GetDirectoryName(Environment.ProcessPath);
            var target = p.MainModule?.FileName;
            if (string.IsNullOrEmpty(target)) return false;
            var targetDir = Path.GetDirectoryName(target);
            return string.Equals(ownDir, targetDir, StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetFileName(target), "OBS_Helper.exe", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            // 读不到主模块（权限 / 已退出）：宁可不清理，也不要误杀
            return false;
        }
    }

    /// <summary>后台线程监听「唤起主窗口」事件，收到信号就把窗口显示并激活。</summary>
    private void StartShowListener()
    {
        try
        {
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, SingleInstanceShowEvent);
        }
        catch (Exception)
        {
            _showEvent = null; // 创建失败则退化为纯 Mutex 防双开，不做窗口唤起
            return;
        }

        _showListener = new Thread(() =>
        {
            while (_showEvent.WaitOne())
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (MainWindow is not { } w) return;

                    if (!w.IsVisible || w.WindowState == WindowState.Minimized)
                    {
                        w.Show();
                        if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
                    }
                    w.Activate();
                    // 用 Topmost 翻转把窗口强行带到最前（Activate 有时会被其他窗口盖住）
                    w.Topmost = true;
                    w.Topmost = false;
                }));
            }
        })
        {
            IsBackground = true
        };
        _showListener.Start();
    }

    /// <summary>兜底错误提示。所有未捕获异常都在这里转成「报错码 + 人话」展示，并先落盘日志。</summary>
    public static void ReportError(string code, Exception? ex = null)
    {
        var detail = ex is null ? null : ex.Message;
        var text = ErrorCodes.Format(code, detail);

        // 先写日志：任何异常都可追溯（弹窗被节流 / Headless 下不弹也不丢记录）
        FileLogger.Error("ReportError", $"{code} {detail}");

        if (HeadlessTest)
        {
            HeadlessErrors.Add(text + (ex is null ? "" : $"\n{ex}"));
            return;
        }

        // 同类错误节流：5 秒内只弹一次窗，防止连环异常刷屏
        lock (DialogThrottleLock)
        {
            var now = DateTime.UtcNow;
            if (LastDialogAt.TryGetValue(code, out var last) && now - last < ErrorDialogThrottle) return;
            LastDialogAt[code] = now;
        }

        // 可能在后台线程抛出，切回 UI 线程再弹窗
        var app = Current;
        if (app is null)
        {
            return;
        }
        app.Dispatcher.BeginInvoke(new Action(() =>
        {
            // 主窗体可能还没建出来（启动早期出错），这时不能传 owner，否则 MessageBox 自己会抛
            var owner = app.MainWindow;
            if (owner is not null && owner.IsLoaded)
            {
                MessageBox.Show(owner, text, "OBS 排障助手", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                MessageBox.Show(text, "OBS 排障助手", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }));
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // 更新自举模式（--apply-update）：无窗口跑完替换即退出。
        // 必须放在单实例判断之前——自举进程是「旧进程已退出、新进程尚未拉起」间隙的过渡角色，
        // 不抢单实例锁，也不能因为锁冲突被拦下。
        if (e.Args.Contains(Services.Update.UpdaterBootstrap.ArgFlag, StringComparer.OrdinalIgnoreCase))
        {
            Services.Update.UpdaterBootstrap.Run(e.Args);
            Shutdown();
            return;
        }

        // 单实例守卫：已有实例在运行则唤起其窗口并退出本进程，杜绝多开。
        // 必须在创建任何窗口之前判断，否则闪一个窗口再关掉会很难看。
        if (!TryAcquireSingleInstance())
        {
            SignalExistingInstance();
            Shutdown();
            return;
        }

        // 本进程持有单实例锁：顺带清理历史版本 / 崩溃残留的同名进程，同一时刻只保留一个。
        // 清理是后台杂活（可能等待被杀进程退出最多 3 秒），后台执行不阻塞首屏（P3-1）。
        Task.Run(() => KillStrayInstances()).FireAndForget("Startup", "清理残留实例");

        // 增量更新自举后遗留的临时文件（pending 目录 / *.old），新版本首次启动时顺手清掉。
        Task.Run(Services.Update.UpdaterBootstrap.CleanupResidue).FireAndForget("Startup", "清理更新残留");

        // 未处理异常：先写日志、再提示报错码，而不是直接闪退
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                FileLogger.Error("AppDomain", ex);
                ReportError(ErrorCodes.Unknown, ex);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            // 之前只 SetObserved 不记录，异常信息直接丢失；现在先落盘再标记已观察
            if (args.Exception is { } ex)
                FileLogger.Error("TaskScheduler", ex);
            args.SetObserved();
        };

#if DEBUG
        // P2-4 绑定错误追踪：WPF 绑定失败（属性名/类型不匹配）默认只在调试输出打一行且常被吞掉，
        // 这里把绑定错误源挂到 FileLogger，开发构建下可落盘排查；Release 构建零开销。
        PresentationTraceSources.DataBindingSource.Listeners.Add(new TraceLoggerListener("Binding"));
        PresentationTraceSources.Refresh();
#endif

        base.OnStartup(e);

        // 外观必须在主窗体创建前套用，否则会先闪一帧默认浅色
        AppServices.Appearance.Initialize();

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            if (_ownsSingleInstance)
            {
                _singleMutex?.ReleaseMutex();
            }
        }
        catch (Exception)
        {
            // 释放失败（非所有者等极端情况）：进程退出时系统会自动回收
        }
        _singleMutex?.Dispose();
        _showEvent?.Dispose();
        // 退出前把日志队列写盘（最多等 2 秒），保证本次会话的异常记录不丢
        FileLogger.Flush();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        FileLogger.Error("Dispatcher", e.Exception);
        ReportError(ErrorCodes.Unknown, e.Exception);
        e.Handled = true;
    }
}
