using OBS_Helper.Wpf.Services;
using OBS_Helper.Wpf.Services.Ai;
using OBS_Helper.Wpf.Services.Host;
using OBS_Helper.Wpf.Services.Obs;

namespace OBS_Helper.Wpf;

/// <summary>
/// 组合根。全部服务都是单例，用惰性字段手工装配。
///
/// 为什么不引入 DI 容器：服务只有十来个、依赖关系是一棵静态的树，
/// 手工装配零依赖、启动更快，也让「谁依赖谁」在一屏里看得清清楚楚。
/// </summary>
public static class AppServices
{
    private static readonly Lazy<HostBridge> _host = new(() => new HostBridge());
    private static readonly Lazy<LocalStore> _store = new(() => new LocalStore());
    private static readonly Lazy<ProblemService> _problems = new(() => new ProblemService());
    private static readonly Lazy<BookmarkService> _bookmarks = new(() => new BookmarkService(Store));
    private static readonly Lazy<AppearanceService> _appearance = new(() => new AppearanceService(Store));
    private static readonly Lazy<AssistantService> _assistant = new(() => new AssistantService(Problems));

    private static readonly Lazy<ObsSettingsService> _obsSettings = new(() => new ObsSettingsService(Store, Host));
    private static readonly Lazy<ObsConnectionService> _obs = new(() => new ObsConnectionService(ObsSettings));
    private static readonly Lazy<ObsLogAnalyzer> _analyzer = new(() => new ObsLogAnalyzer());

    private static readonly Lazy<AiSettingsService> _aiSettings = new(() => new AiSettingsService(Store, Host));
    private static readonly Lazy<ObsToolRegistry> _tools = new(() => new ObsToolRegistry(Problems));
    private static readonly Lazy<LocalDiagnosticEngine> _localEngine = new(() => new LocalDiagnosticEngine(Problems, Assistant));
    private static readonly Lazy<CloudDiagnosticEngine> _cloudEngine = new(() => new CloudDiagnosticEngine(AiSettings, Host, Tools));
    private static readonly Lazy<DiagnosticOrchestrator> _orchestrator = new(() =>
        new DiagnosticOrchestrator(AiSettings, Obs, Analyzer, Problems, Assistant, Host, Tools, LocalEngine, CloudEngine));

    public static HostBridge Host => _host.Value;
    public static LocalStore Store => _store.Value;
    public static ProblemService Problems => _problems.Value;
    public static BookmarkService Bookmarks => _bookmarks.Value;
    public static AppearanceService Appearance => _appearance.Value;
    public static AssistantService Assistant => _assistant.Value;

    public static ObsSettingsService ObsSettings => _obsSettings.Value;
    public static ObsConnectionService Obs => _obs.Value;
    public static ObsLogAnalyzer Analyzer => _analyzer.Value;

    public static AiSettingsService AiSettings => _aiSettings.Value;
    public static ObsToolRegistry Tools => _tools.Value;
    public static LocalDiagnosticEngine LocalEngine => _localEngine.Value;
    public static CloudDiagnosticEngine CloudEngine => _cloudEngine.Value;
    public static DiagnosticOrchestrator Orchestrator => _orchestrator.Value;

    /// <summary>导航服务由 MainWindow 在构造时注入，供各页面互相跳转。</summary>
    public static Navigation.NavigationService Navigation { get; internal set; } = null!;

    /// <summary>应用启动时的一次性初始化（外观 + 各类设置）。</summary>
    public static async Task InitializeAsync()
    {
        Appearance.Initialize();
        await ObsSettings.LoadAsync().ConfigureAwait(false);
        await AiSettings.LoadAsync().ConfigureAwait(false);
    }
}
