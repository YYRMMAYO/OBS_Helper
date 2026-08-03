using System.Windows;
using System.Windows.Threading;
using OBS_Helper.Wpf.Errors;

namespace OBS_Helper.Wpf;

public partial class App : Application
{
    /// <summary>
    /// 自检测试用开关。置 true 时 <see cref="ReportError"/> 不弹窗，改为累加到 <see cref="HeadlessErrors"/>，
    /// 以便自动化脚本（如 <c>OBS_SELFTEST=1</c>）在无界面的环境下捕获全部错误。
    /// </summary>
    public static bool HeadlessTest { get; set; }

    /// <summary>自检测试期间收集到的错误文本（仅 <see cref="HeadlessTest"/> 为 true 时填充）。</summary>
    public static List<string> HeadlessErrors { get; } = new();

    /// <summary>兜底错误提示。所有未捕获异常都在这里转成「报错码 + 人话」展示。</summary>
    public static void ReportError(string code, Exception? ex = null)
    {
        var detail = ex is null ? null : ex.Message;
        var text = ErrorCodes.Format(code, detail);

        if (HeadlessTest)
        {
            HeadlessErrors.Add(text + (ex is null ? "" : $"\n{ex}"));
            return;
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
        // 未处理异常：提示报错码而不是直接闪退
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) ReportError(ErrorCodes.Unknown, ex);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            args.SetObserved();
        };

        base.OnStartup(e);

        // 外观必须在主窗体创建前套用，否则会先闪一帧默认浅色
        AppServices.Appearance.Initialize();

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ReportError(ErrorCodes.Unknown, e.Exception);
        e.Handled = true;
    }
}
