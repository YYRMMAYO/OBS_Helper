using OBS_Helper.Wpf.Services;
using OBS_Helper.Wpf.Services.Ai;
using OBS_Helper.Wpf.Services.Host;
using OBS_Helper.Wpf.Services.Obs;
using OBS_Helper.Wpf.Services.ObsConfig;
using OBS_Helper.Wpf.Services.Plugins;
using OBS_Helper.Wpf.Services.Shell;
using OBS_Helper.Wpf.Services.Tools;
using OBS_Helper.Wpf.Services.Update;

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

    private static readonly Lazy<ObsPathService> _obsPaths = new(() => new ObsPathService(Store));
    private static readonly Lazy<ObsBackupService> _obsBackups = new(() => new ObsBackupService(ObsPaths));
    private static readonly Lazy<ObsResetService> _obsReset = new(() => new ObsResetService(ObsPaths, ObsBackups, Obs));
    private static readonly Lazy<SceneTemplateService> _templates = new(() => new SceneTemplateService(Obs, ObsPaths));
    // 录前自检（C1，只读）：读 OBS 配置检查录制格式 / 路径 / 编码器 / 音频设置
    private static readonly Lazy<PreflightCheckService> _preflight = new(() => new PreflightCheckService(ObsPaths));
    // 录像工具 / 冲突软件扫描 / OBS 新版本情报（V2.6 工具箱）
    private static readonly Lazy<RecordingToolsService> _recTools = new(() => new RecordingToolsService(ObsPaths));
    private static readonly Lazy<ObsReleaseInfoService> _obsRelease = new(() => new ObsReleaseInfoService());

    private static readonly Lazy<UpdateService> _updates = new(() => new UpdateService());
    private static readonly Lazy<IncrementalUpdateService> _delta = new(() => new IncrementalUpdateService());
    private static readonly Lazy<KnowledgeBaseUpdater> _kb = new(() => new KnowledgeBaseUpdater());
    private static readonly Lazy<InstallerCleanupService> _cleanup = new(() => new InstallerCleanupService());

    // 插件生态（V2.2）：目录数据 / 本机体检 / Releases 查新 / 关注提醒
    private static readonly Lazy<PluginCatalogService> _pluginCatalog = new(() => new PluginCatalogService());
    // 体检的安装目录探测走 ObsPathService 实例方法：优先尊重「OBS 配置管理」手动指定的目录（V2.3）
    private static readonly Lazy<LocalPluginScanner> _pluginScanner =
        new(() => new LocalPluginScanner(() => _obsPaths.Value.TryDetectInstallDirForScan()));
    private static readonly Lazy<PluginReleaseService> _pluginReleases = new(() => new PluginReleaseService());
    private static readonly Lazy<PluginWatchService> _pluginWatch =
        new(() => new PluginWatchService(Store, () => _pluginCatalog.Value.GetData(), _pluginReleases.Value));

    // 后台 / 遥控能力
    private static readonly Lazy<TrayService> _tray = new(() => new TrayService(Obs, Store));
    private static readonly Lazy<GlobalHotkeyService> _hotkeys = new(() => new GlobalHotkeyService(Store, Obs));
    private static readonly Lazy<MiniWindowService> _mini = new(() => new MiniWindowService(Store));
    private static readonly Lazy<SceneAutoSwitcher> _autoSwitcher = new(() => new SceneAutoSwitcher(Store, Obs));
    private static readonly Lazy<ControlTimerService> _timer = new(() => new ControlTimerService(Obs, Tray));
    private static readonly Lazy<SystemMonitorService> _systemMonitor = new(() => new SystemMonitorService());

    private static readonly Lazy<AiSettingsService> _aiSettings = new(() => new AiSettingsService(Store, Host));
    private static readonly Lazy<ObsToolRegistry> _tools = new(() => new ObsToolRegistry(Problems));
    private static readonly Lazy<LocalDiagnosticEngine> _localEngine = new(() => new LocalDiagnosticEngine(Problems, Assistant));
    private static readonly Lazy<CloudDiagnosticEngine> _cloudEngine = new(() => new CloudDiagnosticEngine(AiSettings, Host, Tools));
    private static readonly Lazy<FreeRateLimiter> _freeLimiter = new(() => new FreeRateLimiter(Store));
    private static readonly Lazy<FreeAiKeyProvider> _freeAiKey = new(() => new FreeAiKeyProvider());
    private static readonly Lazy<FreeDiagnosticEngine> _freeEngine = new(() => new FreeDiagnosticEngine(AiSettings, Host, FreeAiKey));
    private static readonly Lazy<DiagnosticOrchestrator> _orchestrator = new(() =>
        new DiagnosticOrchestrator(AiSettings, Obs, Analyzer, Problems, Assistant, Host, Tools, LocalEngine, CloudEngine, FreeEngine, FreeLimiter));

    public static HostBridge Host => _host.Value;
    public static LocalStore Store => _store.Value;
    public static ProblemService Problems => _problems.Value;
    public static BookmarkService Bookmarks => _bookmarks.Value;
    public static AppearanceService Appearance => _appearance.Value;
    public static AssistantService Assistant => _assistant.Value;

    public static ObsSettingsService ObsSettings => _obsSettings.Value;
    public static ObsConnectionService Obs => _obs.Value;
    public static ObsLogAnalyzer Analyzer => _analyzer.Value;

    public static ObsPathService ObsPaths => _obsPaths.Value;
    public static ObsBackupService ObsBackups => _obsBackups.Value;
    public static ObsResetService ObsReset => _obsReset.Value;
    public static SceneTemplateService Templates => _templates.Value;
    public static PreflightCheckService Preflight => _preflight.Value;
    public static RecordingToolsService RecordingTools => _recTools.Value;
    // 冲突扫描为纯逻辑核心 + 页面侧进程枚举，无独立服务实例
    public static ObsReleaseInfoService ObsRelease => _obsRelease.Value;

    public static AiSettingsService AiSettings => _aiSettings.Value;
    public static UpdateService Updates => _updates.Value;
    public static IncrementalUpdateService Delta => _delta.Value;
    public static KnowledgeBaseUpdater Kb => _kb.Value;
    public static InstallerCleanupService Cleanup => _cleanup.Value;

    // 插件生态（V2.2）
    public static PluginCatalogService PluginCatalog => _pluginCatalog.Value;
    public static LocalPluginScanner PluginScanner => _pluginScanner.Value;
    public static PluginReleaseService PluginReleases => _pluginReleases.Value;
    public static PluginWatchService PluginWatch => _pluginWatch.Value;
    public static ObsToolRegistry Tools => _tools.Value;
    public static LocalDiagnosticEngine LocalEngine => _localEngine.Value;
    public static CloudDiagnosticEngine CloudEngine => _cloudEngine.Value;
    public static FreeRateLimiter FreeLimiter => _freeLimiter.Value;
    public static FreeAiKeyProvider FreeAiKey => _freeAiKey.Value;
    public static FreeDiagnosticEngine FreeEngine => _freeEngine.Value;

    // 后台 / 遥控能力
    public static TrayService Tray => _tray.Value;
    public static GlobalHotkeyService Hotkeys => _hotkeys.Value;
    public static MiniWindowService Mini => _mini.Value;
    public static SceneAutoSwitcher AutoSwitcher => _autoSwitcher.Value;
    public static ControlTimerService Timer => _timer.Value;
    public static SystemMonitorService SystemMonitor => _systemMonitor.Value;
    public static DiagnosticOrchestrator Orchestrator => _orchestrator.Value;

    /// <summary>导航服务由 MainWindow 在构造时注入，供各页面互相跳转。</summary>
    public static Navigation.NavigationService Navigation { get; internal set; } = null!;

    /// <summary>全局加载态遮罩，由 MainWindow 在构造时注入（P0）。</summary>
    public static BusyService Busy { get; internal set; } = null!;

    /// <summary>统一轻提示 Toast，由 MainWindow 在构造时注入（P0）。</summary>
    public static ToastService Toast { get; internal set; } = null!;

    /// <summary>应用启动时的一次性初始化（外观 + 各类设置 + 后台能力）。</summary>
    public static async Task InitializeAsync()
    {
        Appearance.Initialize();
        // P3-1 启动加速：两份设置加载互相独立，串行 await 改为并行，冷启动可省一次 IO 往返
        await Task.WhenAll(
            ObsSettings.LoadAsync(),
            AiSettings.LoadAsync()).ConfigureAwait(false);

        // 后台能力：托盘、全局热键、场景自动切换（默认按各自配置启动）
        Tray.LoadSettings();
        Hotkeys.Load();
        Hotkeys.Start();
        AutoSwitcher.Load();
        AutoSwitcher.Start();
        Tray.Start();
    }

    /// <summary>应用退出时的清理（MainWindow.OnClosed 调用）。</summary>
    public static void ShutdownServices()
    {
        try { Mini.Stop(); } catch (Exception ex) { FileLogger.Warn("Shutdown", $"Mini.Stop 失败: {ex.Message}"); }
        try { AutoSwitcher.Stop(); } catch (Exception ex) { FileLogger.Warn("Shutdown", $"AutoSwitcher.Stop 失败: {ex.Message}"); }
        try { Hotkeys.Dispose(); } catch (Exception ex) { FileLogger.Warn("Shutdown", $"Hotkeys.Dispose 失败: {ex.Message}"); }
        try { SystemMonitor.Dispose(); } catch (Exception ex) { FileLogger.Warn("Shutdown", $"SystemMonitor.Dispose 失败: {ex.Message}"); }
        try { Timer.Dispose(); } catch (Exception ex) { FileLogger.Warn("Shutdown", $"Timer.Dispose 失败: {ex.Message}"); }
        try { Tray.Stop(); } catch (Exception ex) { FileLogger.Warn("Shutdown", $"Tray.Stop 失败: {ex.Message}"); }
    }
}
