using OBS_Helper.Wpf.Services;
using OBS_Helper.Wpf.Services.Ai;
using OBS_Helper.Wpf.Services.Audio;
using OBS_Helper.Wpf.Services.Host;
using OBS_Helper.Wpf.Services.Obs;
using OBS_Helper.Wpf.Services.ObsConfig;
using OBS_Helper.Wpf.Services.Diagnostics;
using OBS_Helper.Wpf.Services.Recording;using OBS_Helper.Wpf.Services.Plugins;
using OBS_Helper.Wpf.Services.Shell;
using OBS_Helper.Wpf.Services.Tools;
using OBS_Helper.Wpf.Services.Mobile;
using OBS_Helper.Wpf.Services.Interop;
using OBS_Helper.Wpf.Services.Chat;
using OBS_Helper.Wpf.Services.Streaming;
using OBS_Helper.Wpf.Services.Recording;
using OBS_Helper.Wpf.Services.Audio;
using OBS_Helper.Wpf.Services.Subtitles;
using OBS_Helper.Wpf.Services.Analytics;
using OBS_Helper.Wpf.Services.Clips;
using OBS_Helper.Wpf.Services.Update;

namespace OBS_Helper.Wpf;

/// <summary>
/// 组合根。全部服务都是单例，用惰性字段手工装配。
///
/// 为什么不引入 DI 容器：依赖关系是一棵静态的树，手工装配零依赖、启动更快，
/// 也让「谁依赖谁」在一屏里看得清清楚楚（服务数量本身不构成引入容器的理由）。
/// </summary>
public static class AppServices
{
    private static readonly Lazy<HostBridge> _host = new(() => new HostBridge());
    private static readonly Lazy<LocalStore> _store = new(() => new LocalStore());
    private static readonly Lazy<ProblemService> _problems = new(() => new ProblemService());
    private static readonly Lazy<BookmarkService> _bookmarks = new(() => new BookmarkService(Store));
    private static readonly Lazy<AppearanceService> _appearance = new(() => new AppearanceService(Store));
    // 壁纸库 / 解码 / 轮播（V3.1.0）：组合根在 Appearance 初始化前把引用挂上去
    private static readonly Lazy<Services.Appearance.WallpaperService> _wallpapers = new(() =>
    {
        var service = new Services.Appearance.WallpaperService(
            Appearance,
            new Services.Appearance.WallpaperStore(
                System.IO.Path.Combine(HostBridge.AppDataDirectory, "wallpapers"),
                message => FileLogger.Warn("Wallpaper", message)));
        Appearance.Wallpapers = service;
        return service;
    });
    // 语言服务（V2.9.2）：把当前语言的文案写进 Application.Resources，XAML 用 Loc.* 引用
    private static readonly Lazy<LocalizationService> _localization = new(() => new LocalizationService(Store));
    private static readonly Lazy<AssistantService> _assistant = new(() => new AssistantService(Problems));

    private static readonly Lazy<ObsSettingsService> _obsSettings = new(() => new ObsSettingsService(Store, Host));
    private static readonly Lazy<ObsConnectionService> _obs = new(() => new ObsConnectionService(ObsSettings, () => ObsPaths.IsObsRunning()));
    private static readonly Lazy<ObsLogAnalyzer> _analyzer = new(() => new ObsLogAnalyzer());

    private static readonly Lazy<ObsPathService> _obsPaths = new(() => new ObsPathService(Store));
    private static readonly Lazy<ObsBackupService> _obsBackups = new(() => new ObsBackupService(ObsPaths));
    private static readonly Lazy<TrashService> _trash = new(() => new TrashService(ObsPaths));
    // 录制档案与收尾流水线（V3.0 / D3）：登记 / 重命名 / 批量转 MP4 / 打点章节
    private static readonly Lazy<RecordingArchiveService> _archive = new(() => new RecordingArchiveService(Store));
    private static readonly Lazy<ObsResetService> _obsReset = new(() => new ObsResetService(ObsPaths, ObsBackups, Obs));
    private static readonly Lazy<SceneTemplateService> _templates = new(() =>
    {
        var service = new SceneTemplateService(Obs, ObsPaths);
        // V3.3.0：虚拟直播间的 browser 页面在落地时解析成本地回环 URL
        service.SourceSettingsResolver = src => Rooms.ResolveSourceSettings(src);
        return service;
    });
    // 虚拟直播间的本地 overlay 服务（V3.3.0）：只绑回环、随机端口、随机 token
    private static readonly Lazy<Services.Live.RoomService> _rooms =
        new(() => new Services.Live.RoomService());
    // Live2D 一键配置（V3.3.0）：模型校验 + 采集源 + VTS 公开 API
    private static readonly Lazy<Services.Live.Live2DSetupService> _live2d =
        new(() => new Services.Live.Live2DSetupService(Obs));
    // 开播引导（V3.3.0）：平台目录 + 直推 / 虚拟摄像头 + 第三方客户端探测
    private static readonly Lazy<Services.Live.PlatformCatalogService> _platforms =
        new(() => new Services.Live.PlatformCatalogService());
    private static readonly Lazy<Services.Live.GoLiveService> _goLive =
        new(() => new Services.Live.GoLiveService(Obs, Host));
    // 录前自检（C1，只读）：读 OBS 配置检查录制格式 / 路径 / 编码器 / 音频设置
    private static readonly Lazy<PreflightCheckService> _preflight = new(() => new PreflightCheckService(ObsPaths));
    // 自检清单按本机实测回填（V3.0 / D4）
    private static readonly Lazy<ChecklistAutoFillService> _checklistAutoFill =
        new(() => new ChecklistAutoFillService(Preflight));
    // 开播前体检 + 一键处方（V3.0 / D4 后半段）
    private static readonly Lazy<MachineProfileService> _machineProfile =
        new(() => new MachineProfileService(Preflight, ObsPaths));
    // 录像工具 / 冲突软件扫描 / OBS 新版本情报（V2.6 工具箱）
    private static readonly Lazy<RecordingToolsService> _recTools = new(() => new RecordingToolsService(ObsPaths));
    // 一键部署录制环境（V2.9.3）：按语言无关的推荐值改写录制相关配置，先备份、可回滚
    private static readonly Lazy<RecordingEnvService> _recEnv =
        new(() => new RecordingEnvService(ObsPaths, ObsBackups, Obs, RecordingTools));
    private static readonly Lazy<ObsReleaseInfoService> _obsRelease = new(() => new ObsReleaseInfoService());
    // 色彩体检 / 音频采样率体检（V2.7 工具箱）
    private static readonly Lazy<ColorCheckService> _colorCheck = new(() => new ColorCheckService(ObsPaths));
    private static readonly Lazy<SampleRateCheckService> _sampleRateCheck = new(() => new SampleRateCheckService(ObsPaths));

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
    // 插件一键安装 / 管理（V3.3.0）：状态清单 + 事务安装 + GitHub 搜索
    private static readonly Lazy<PluginStateStore> _pluginState =
        new(() => new PluginStateStore(System.IO.Path.Combine(HostBridge.AppDataDirectory, "plugins")));
    private static readonly Lazy<PluginInstallService> _pluginInstall =
        new(() => new PluginInstallService(PluginState));
    private static readonly Lazy<GitHubSearchService> _pluginSearch =
        new(() => new GitHubSearchService(tokenProvider: () => Host.GetSecretAsync("github.token")));

    // 后台 / 遥控能力
    private static readonly Lazy<TrayService> _tray = new(() => new TrayService(Obs, Store));
    private static readonly Lazy<GlobalHotkeyService> _hotkeys = new(() => new GlobalHotkeyService(Store, Obs));
    private static readonly Lazy<MiniWindowService> _mini = new(() => new MiniWindowService(Store));
    private static readonly Lazy<SceneAutoSwitcher> _autoSwitcher = new(() => new SceneAutoSwitcher(Store, Obs));
    private static readonly Lazy<ControlTimerService> _timer = new(() => new ControlTimerService(Obs, Tray));
    private static readonly Lazy<SystemMonitorService> _systemMonitor = new(() => new SystemMonitorService());
    // 录制守护 / 实时日志尾随预警（V2.8）
    private static readonly Lazy<RecordWatchdogService> _watchdog = new(() => new RecordWatchdogService(Obs, Tray));
    private static readonly Lazy<LogTailerService> _logTailer = new(() => new LogTailerService(Tray));
    // 录制 / 推流中静音与音量过低提醒（V3.1.0 / §12.5.1）
    private static readonly Lazy<Services.Audio.AudioAlertService> _audioAlert =
        new(() => new Services.Audio.AudioAlertService(Obs, Tray));
    // 会话复盘（V3.0 / D5）：把实时命中累计 + 上次会话摘要变成可导出报告
    private static readonly Lazy<SessionReviewService> _sessionReview =
        new(() => new SessionReviewService(LogTailer, Store));
    // 本地知识库（V3.0 / D8）：用户自己的条目，与热更新文件同目录、同 schema
    private static readonly Lazy<Services.Knowledge.MyKnowledgeBaseService> _myKnowledge =
        new(() => new Services.Knowledge.MyKnowledgeBaseService(System.IO.Path.Combine(HostBridge.AppDataDirectory, "data")));
    // 简单录像（V2.9.4）：自检 → 落地 → 拉起 OBS → 开录 → 录中信息 → 停止收尾
    private static readonly Lazy<SimpleRecordingService> _simpleRecord =
        new(() => new SimpleRecordingService(ObsPaths, RecordingEnv, RecordingTools, Obs, Tray, Store));
    // 简单开播（V3.0 / D2）：检查推流配置 → （可选）同时录制 → 开播 → 确认真的播出去了
    private static readonly Lazy<SimpleStreamingService> _simpleStream =
        new(() => new SimpleStreamingService(Obs, SimpleRecord, Store));
    // 提词器（V3.1.0 / §12.1）：独立窗口、脚本 / 设置持久化、场景联动
    private static readonly Lazy<TeleprompterService> _teleprompter =
        new(() => new TeleprompterService(Store));

    // ---------------------------------------------------------------- V3.4.0 手机联动
    // sidecar 管理器（mediamtx / whisper.cpp / ffmpeg / rclone）：清单随包，二进制按需下载
    private static readonly Lazy<SidecarManager> _sidecars = new(() =>
    {
        var manager = new SidecarManager();
        try
        {
            var json = OBS_Helper.Wpf.Localization.ContentAssets.ReadEmbedded(OBS_Helper.Wpf.Localization.ContentAssets.ResourcePrefix + "sidecars.json");
            if (!string.IsNullOrWhiteSpace(json)) manager.LoadManifest(json);
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Sidecar", $"清单加载失败：{ex.Message}");
        }
        return manager;
    });
    private static readonly Lazy<MobileSessionStore> _mobileSessions = new(() =>
        new MobileSessionStore(System.IO.Path.Combine(HostBridge.AppDataDirectory, "mobile", "devices.json")));
    private static readonly Lazy<MobileSnapshotService> _mobileSnapshots = new(() =>
        new MobileSnapshotService(Obs, Teleprompter, SystemMonitor));
    private static readonly Lazy<MobilePreviewService> _mobilePreview = new(() => new MobilePreviewService(Obs));
    private static readonly Lazy<MobileUploadService> _mobileUploads = new(() => new MobileUploadService());
    private static readonly Lazy<MobileCommandRouter> _mobileRouter = new(() =>
        new MobileCommandRouter(Obs, Teleprompter, MobileSessions, MobileSnapshots, MobilePreview,
            deviceId =>
            {
                if (_mobileLink.IsValueCreated) MobileLink.RevokeDevice(deviceId);
            }));
    private static readonly Lazy<MobileLinkServer> _mobileLink = new(() =>
        new MobileLinkServer(Obs, MobileSessions, MobileSnapshots, MobileRouter, MobilePreview, MobileUploads));
    private static readonly Lazy<MediaServerManager> _mediaServer = new(() => new MediaServerManager(Sidecars, Obs));
    private static readonly Lazy<UsbCameraGuide> _usbCamera = new(() => new UsbCameraGuide());
    private static readonly Lazy<MobileNotifyService> _mobileNotify = new(() => new MobileNotifyService(UsbCamera));
    private static readonly Lazy<Services.Live.RemoteGuestService> _remoteGuests =
        new(() => new Services.Live.RemoteGuestService(Sidecars));
    private static readonly Lazy<MultiPlatformRelayService> _relay = new(() =>
        new MultiPlatformRelayService(Sidecars, Obs, Store, Host, InstalledSidecar("ffmpeg")));
    private static readonly Lazy<ArchiveService> _archiveUpload = new(() =>
        new ArchiveService(InstalledSidecar("rclone")));
    private static readonly Lazy<AudioChainService> _audioChain = new(() => new AudioChainService(Obs));
    private static readonly Lazy<PanicService> _panic = new(() => new PanicService(Obs));
    private static readonly Lazy<Services.Hardware.MidiService> _midi = new(() => new Services.Hardware.MidiService(Obs, Teleprompter));
    private static readonly Lazy<Services.Hardware.LightingService> _lighting =
        new(() => new Services.Hardware.LightingService(new Services.Automation.WledClient(), ChatHub, Obs, Store));
    private static readonly Lazy<Services.Live.ThemeMarketService> _themes = new(() => new Services.Live.ThemeMarketService());
    private static readonly Lazy<Services.Ai.AiHostService> _aiHost = new(() => new Services.Ai.AiHostService(Host));

    private static string InstalledSidecar(string id)
    {
        var entry = Sidecars.Manifest.Find(id);
        return entry is null ? "" : Sidecars.InstalledExecutable(entry) ?? "";
    }

    // ---------------------------------------------------------------- V3.4.0 弹幕 / 字幕 / 统计
    private static readonly Lazy<ChatHub> _chatHub = new(() => new ChatHub());
    private static readonly Lazy<SubmissionQueue> _submissions = new(() => new SubmissionQueue());
    private static readonly Lazy<ChatHistoryStore> _chatHistory = new(() =>
        new ChatHistoryStore(System.IO.Path.Combine(HostBridge.AppDataDirectory, "chat", "logs")));
    private static readonly Lazy<ChatTtsService> _chatTts = new(() => new ChatTtsService(ChatHub));
    private static readonly Lazy<ChatOverlayServer> _chatOverlay = new(() => new ChatOverlayServer());
    private static readonly Lazy<TranslationCore> _translator = new(() => new TranslationCore());
    private static readonly Lazy<AsrService> _asr = new(() => new AsrService(Obs, Translator));
    private static readonly Lazy<Services.Subtitles.VoiceControlService> _voice =
        new(() => new Services.Subtitles.VoiceControlService(Asr, Obs));
    private static readonly Lazy<ViewerStatsCore> _viewerStats = new(() => new ViewerStatsCore());
    private static readonly Lazy<RecordingSearchCore> _recordingSearch = new(() => new RecordingSearchCore());

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

    /// <summary>壁纸服务（V3.1.0）：壁纸库、解码缓存、轮播、迁移。</summary>
    public static Services.Appearance.WallpaperService Wallpapers => _wallpapers.Value;

    /// <summary>壁纸服务是否已经装配（供控件在启动早期安全订阅）。</summary>
    public static Services.Appearance.WallpaperService? WallpapersOrNull()
        => _wallpapers.IsValueCreated ? _wallpapers.Value : null;
    /// <summary>语言服务（V2.9.2）：默认中文，可在设置页即时切换为英文。</summary>
    public static LocalizationService Localization => _localization.Value;
    public static AssistantService Assistant => _assistant.Value;

    public static ObsSettingsService ObsSettings => _obsSettings.Value;
    public static ObsConnectionService Obs => _obs.Value;
    public static ObsLogAnalyzer Analyzer => _analyzer.Value;

    public static ObsPathService ObsPaths => _obsPaths.Value;
    public static ObsBackupService ObsBackups => _obsBackups.Value;

    /// <summary>配置回收站（V3.0 / A4）：列出「永不硬删」留下的恢复副本，并可放回原位。</summary>
    public static TrashService Trash => _trash.Value;

    /// <summary>录制档案（V3.0 / D3）。</summary>
    public static RecordingArchiveService Archive => _archive.Value;

    /// <summary>自检清单自动回填（V3.0 / D4）。</summary>
    public static ChecklistAutoFillService ChecklistAutoFill => _checklistAutoFill.Value;

    /// <summary>开播前体检（V3.0 / D4 后半段）：五项检查合成一个结论。</summary>
    public static MachineProfileService MachineProfile => _machineProfile.Value;
    public static ObsResetService ObsReset => _obsReset.Value;
    public static SceneTemplateService Templates => _templates.Value;

    /// <summary>虚拟直播间服务（V3.3.0）：本地 overlay 页面与模板筛选。</summary>
    public static Services.Live.RoomService Rooms => _rooms.Value;

    /// <summary>Live2D 一键配置（V3.3.0）。</summary>
    public static Services.Live.Live2DSetupService Live2D => _live2d.Value;

    /// <summary>开播平台目录（V3.3.0）。</summary>
    public static Services.Live.PlatformCatalogService Platforms => _platforms.Value;

    /// <summary>开播编排（V3.3.0）：直推 / 虚拟摄像头 / 客户端探测。</summary>
    public static Services.Live.GoLiveService GoLive => _goLive.Value;
    public static PreflightCheckService Preflight => _preflight.Value;
    public static RecordingToolsService RecordingTools => _recTools.Value;
    /// <summary>一键部署录制环境（V2.9.3）。</summary>
    public static RecordingEnvService RecordingEnv => _recEnv.Value;
    // 冲突扫描为纯逻辑核心 + 页面侧进程枚举，无独立服务实例
    public static ObsReleaseInfoService ObsRelease => _obsRelease.Value;
    // 色彩 / 采样率体检（V2.7）；磁盘测速、编码顾问、节点探测为纯静态核心，页面直接调用
    public static ColorCheckService ColorCheck => _colorCheck.Value;
    public static SampleRateCheckService SampleRateCheck => _sampleRateCheck.Value;

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

    /// <summary>插件安装状态（V3.3.0）：installed.json / downloads / backups。</summary>
    public static PluginStateStore PluginState => _pluginState.Value;

    /// <summary>插件事务安装（V3.3.0）。</summary>
    public static PluginInstallService PluginInstall => _pluginInstall.Value;

    /// <summary>GitHub 搜索 / Release 查询（V3.3.0，带缓存与限频冷却）。</summary>
    public static GitHubSearchService PluginSearch => _pluginSearch.Value;
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
    // 录制守护 / 实时日志尾随预警（V2.8）
    public static RecordWatchdogService RecordWatchdog => _watchdog.Value;
    public static LogTailerService LogTailer => _logTailer.Value;

    /// <summary>录制 / 推流中静音与音量过低提醒（V3.1.0）。</summary>
    public static Services.Audio.AudioAlertService AudioAlert => _audioAlert.Value;

    /// <summary>会话复盘（V3.0 / D5）。</summary>
    public static SessionReviewService SessionReview => _sessionReview.Value;

    /// <summary>本地知识库读写（V3.0 / D8）。</summary>
    public static Services.Knowledge.MyKnowledgeBaseService MyKnowledge => _myKnowledge.Value;
    /// <summary>简单录像（V2.9.4）：首页卡片、控制台录制行与托盘菜单共用同一个实例。</summary>
    public static SimpleRecordingService SimpleRecord => _simpleRecord.Value;

    /// <summary>简单开播（V3.0 / D2）。</summary>
    public static SimpleStreamingService SimpleStream => _simpleStream.Value;

    /// <summary>提词器（V3.1.0）：窗口、脚本、设置与场景联动。</summary>
    public static TeleprompterService Teleprompter => _teleprompter.Value;

    // ---------------------------------------------------------------- V3.4.0 手机联动
    /// <summary>sidecar 清单与下载/启动（mediamtx / whisper.cpp / ffmpeg / rclone）。</summary>
    public static SidecarManager Sidecars => _sidecars.Value;
    /// <summary>已配对设备（只存 token 哈希）。</summary>
    public static MobileSessionStore MobileSessions => _mobileSessions.Value;
    public static MobileSnapshotService MobileSnapshots => _mobileSnapshots.Value;
    public static MobilePreviewService MobilePreview => _mobilePreview.Value;
    public static MobileUploadService MobileUploads => _mobileUploads.Value;
    public static MobileCommandRouter MobileRouter => _mobileRouter.Value;
    /// <summary>手机联动服务端（默认不启动，用户点「生成配对码」才开端口）。</summary>
    public static MobileLinkServer MobileLink => _mobileLink.Value;
    /// <summary>手机摄像头（mediamtx sidecar）。</summary>
    public static MediaServerManager MediaServer => _mediaServer.Value;
    public static UsbCameraGuide UsbCamera => _usbCamera.Value;
    /// <summary>手机通知/剪贴板上屏（Android + ADB，默认关闭）。</summary>
    public static MobileNotifyService MobileNotify => _mobileNotify.Value;
    /// <summary>远程嘉宾房间（M10）：房间/权限/SFU 配置；浏览器推流需 HTTPS（见服务内说明）。</summary>
    public static Services.Live.RemoteGuestService RemoteGuests => _remoteGuests.Value;
    public static MultiPlatformRelayService Relay => _relay.Value;
    public static ArchiveService ArchiveUpload => _archiveUpload.Value;
    public static AudioChainService AudioChain => _audioChain.Value;
    public static PanicService Panic => _panic.Value;
    public static Services.Hardware.MidiService Midi => _midi.Value;
    public static Services.Hardware.LightingService Lighting => _lighting.Value;
    public static Services.Live.ThemeMarketService Themes => _themes.Value;
    public static Services.Ai.AiHostService AiHost => _aiHost.Value;

    // ---------------------------------------------------------------- V3.4.0 弹幕 / 字幕 / 统计
    public static ChatHub ChatHub => _chatHub.Value;
    /// <summary>可选本地弹幕留存（默认关闭，最多 7 天）。</summary>
    public static ChatHistoryStore ChatHistory => _chatHistory.Value;
    /// <summary>观众投稿/来信队列（C06）：人工审核后才上屏。</summary>
    public static SubmissionQueue Submissions => _submissions.Value;
    public static ChatTtsService ChatTts => _chatTts.Value;
    public static ChatOverlayServer ChatOverlay => _chatOverlay.Value;
    public static TranslationCore Translator => _translator.Value;
    public static AsrService Asr => _asr.Value;
    /// <summary>语音控制（S03）：唤醒词 + 白名单 + 破坏性确认，默认关闭。</summary>
    public static Services.Subtitles.VoiceControlService VoiceControl => _voice.Value;
    public static ViewerStatsCore ViewerStats => _viewerStats.Value;
    public static RecordingSearchCore RecordingSearch => _recordingSearch.Value;
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
        // V3.1.0：壁纸服务必须在 Appearance.Initialize 之前装配（Apply 会向它要图片资源），
        // 但库加载要等设置读完之后 —— 见下面的 Wallpapers.Initialize()。
        _ = Wallpapers;
        Appearance.Initialize();
        // 语言必须在任何窗口创建前定下来（App.OnStartup 已调一次，这里保证幂等）
        Localization.Initialize();

        // V3.1.0：读壁纸库、迁移旧 bgImage、解析当前壁纸并启动轮播
        try { Wallpapers.Initialize(); }
        catch (Exception ex) { FileLogger.Warn("Wallpaper", $"壁纸初始化失败（已回退默认底）：{ex.Message}"); }

        // V3.1.0：提词器设置 / 脚本预加载（窗口仍然按需创建）
        try { Teleprompter.Initialize(); }
        catch (Exception ex) { FileLogger.Warn("Teleprompter", $"提词器初始化失败：{ex.Message}"); }
        // V2.9.3：随包内容（知识库 / 插件目录 / 场景模板 / 排障指引）各有中英两份，
        // 语言一变就丢弃缓存，并按新语言补一次静默热更新（P3-1 启动加速：不阻塞首屏）。
        Localization.Changed += OnLanguageChanged;
        // P3-1 启动加速：两份设置加载互相独立，串行 await 改为并行，冷启动可省一次 IO 往返
        await Task.WhenAll(
            ObsSettings.LoadAsync(),
            AiSettings.LoadAsync()).ConfigureAwait(false);

        // V3.1.0 / D6：启动自动连接只在 OBS 已在运行时尝试；失败只记日志，不弹窗、不阻塞首屏。
        if (ObsSettings.Current.AutoConnect && ObsPaths.IsObsRunning())
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await Obs.EnsureConnectedAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                    if (!result.Ok && result.Outcome != Services.Obs.EnsureConnectionOutcome.Cancelled)
                        FileLogger.Warn("ObsAutoConnect", $"启动自动连接未成功：{result.Outcome}（{result.Elapsed.TotalSeconds:0.0}s）");
                }
                catch (Exception ex)
                {
                    FileLogger.Warn("ObsAutoConnect", $"启动自动连接异常：{ex.Message}");
                }
            });
        }

        // V3.0：文件事务「回滚没能完全恢复」时的出口。
        // 副本已经被刻意保留（FileTx.RollbackFailures + .obshelper-retained 标记），
        // 但如果不告诉用户它在哪，等于没救回来 —— 这里接到日志与全局 Toast 上。
        FileTx.RollbackFailureReporter = (txDir, count) =>
        {
            FileLogger.Error("FileTx", $"回滚未完成：{count} 项未能恢复，恢复副本保留在 {txDir}");
            // V3.0 审查修正：上报可检索的错误码（原先只有 Toast 文本，用户截图求助时给不出编号，
            // 而 `err.OBS807` 的双语文案也就永远用不上）
            try { App.ReportError(Errors.ErrorCodes.FileTransactionRollbackFailed); }
            catch (Exception) { /* 启动早期 / 无主窗口时忽略 */ }
            try { Toast?.Show(Strings.T("filetx.rollbackFailed", count, txDir)); }
            catch (Exception) { /* Toast 还没注入（启动早期）时只留日志 */ }
        };

        // 后台能力：托盘、全局热键、场景自动切换（默认按各自配置启动）
        Tray.LoadSettings();

        // 托盘「打点」需要简单录像服务（V3.0 / D3）；在装配完成后用 provider 注入，
        // 避免托盘服务内部直接读 Lazy（构造期重入会抛 InvalidOperationException）。
        Tray.SimpleRecordProvider = () => SimpleRecord;

        Hotkeys.Load();
        Hotkeys.Start();
        AutoSwitcher.Load();
        AutoSwitcher.Start();
        Tray.Start();
        // V2.8 后台守护：录制守护与实时日志尾随（按各自设置开关启动）
        RecordWatchdog.ApplyEnabled();
        LogTailer.ApplyEnabled();
        // V3.1.0：录制 / 推流中的静音与音量过低提醒
        AudioAlert.ApplyEnabled();
    }

    /// <summary>
    /// 语言切换后的内容侧善后（V2.9.3）：丢弃按语言缓存的内容，再补一次静默刷新。
    ///
    /// 只对**已经创建过**的服务调 Reload —— 惰性字段还没求值时缓存本来就是空的，
    /// 强行求值会把 OBS 连接服务等一并提前拉起来，得不偿失。
    /// </summary>
    private static void OnLanguageChanged(string previous, string current)
    {
        try
        {
            Problems.Reload();
            if (_pluginCatalog.IsValueCreated) PluginCatalog.Reload();
            if (_templates.IsValueCreated) Templates.Reload();
            if (_platforms.IsValueCreated) Platforms.Reload();
            // 切到英文时，英文那份的节流状态文件是全新的，这次调用会真的联网拉一次英文资产。
            Kb.RefreshCurrentLanguageAsync().FireAndForget("KB", "语言切换后刷新内容");
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Localization", $"语言切换后刷新内容失败：{ex.Message}");
        }
    }

    /// <summary>应用退出时的清理（MainWindow.OnClosed 调用）。</summary>
    public static void ShutdownServices()
    {
        try { if (_wallpapers.IsValueCreated) Wallpapers.Dispose(); } catch (Exception ex) { FileLogger.Warn("Shutdown", $"Wallpapers.Dispose 失败: {ex.Message}"); }
        try { if (_teleprompter.IsValueCreated) Teleprompter.Dispose(); } catch (Exception ex) { FileLogger.Warn("Shutdown", $"Teleprompter.Dispose 失败: {ex.Message}"); }
        // V3.4.0：先停手机联动，再停媒体服务器与 sidecar（顺序见 PLAN §5.2）
        try { if (_mobileLink.IsValueCreated) MobileLink.Dispose(); } catch (Exception ex) { FileLogger.Warn("Shutdown", $"MobileLink.Dispose 失败: {ex.Message}"); }
        try { if (_panic.IsValueCreated) Panic.Dispose(); } catch (Exception ex) { FileLogger.Warn("Shutdown", $"Panic.Dispose 失败: {ex.Message}"); }
        try { if (_midi.IsValueCreated) Midi.Dispose(); } catch (Exception ex) { FileLogger.Warn("Shutdown", $"Midi.Dispose 失败: {ex.Message}"); }
        try { if (_lighting.IsValueCreated) Lighting.Dispose(); } catch (Exception ex) { FileLogger.Warn("Shutdown", $"Lighting.Dispose 失败: {ex.Message}"); }
        try { if (_aiHost.IsValueCreated) AiHost.Dispose(); } catch (Exception ex) { FileLogger.Warn("Shutdown", $"AiHost.Dispose 失败: {ex.Message}"); }
        try { if (_relay.IsValueCreated) Relay.Dispose(); } catch (Exception ex) { FileLogger.Warn("Shutdown", $"Relay.Dispose 失败: {ex.Message}"); }
        try { if (_remoteGuests.IsValueCreated) RemoteGuests.Dispose(); } catch (Exception ex) { FileLogger.Warn("Shutdown", $"RemoteGuests.Dispose 失败: {ex.Message}"); }
        try { if (_mediaServer.IsValueCreated) MediaServer.Dispose(); } catch (Exception ex) { FileLogger.Warn("Shutdown", $"MediaServer.Dispose 失败: {ex.Message}"); }
        try { if (_sidecars.IsValueCreated) Sidecars.Dispose(); } catch (Exception ex) { FileLogger.Warn("Shutdown", $"Sidecars.Dispose 失败: {ex.Message}"); }
        // V3.4.0：字幕 / 弹幕 / overlay 依序停止
        try { if (_voice.IsValueCreated) VoiceControl.Dispose(); } catch (Exception ex) { FileLogger.Warn("Shutdown", $"VoiceControl.Dispose 失败: {ex.Message}"); }
        try { if (_asr.IsValueCreated) Asr.Dispose(); } catch (Exception ex) { FileLogger.Warn("Shutdown", $"Asr.Dispose 失败: {ex.Message}"); }
        try { if (_chatTts.IsValueCreated) ChatTts.Dispose(); } catch (Exception ex) { FileLogger.Warn("Shutdown", $"ChatTts.Dispose 失败: {ex.Message}"); }
        try { if (_chatOverlay.IsValueCreated) ChatOverlay.Dispose(); } catch (Exception ex) { FileLogger.Warn("Shutdown", $"ChatOverlay.Dispose 失败: {ex.Message}"); }
        try { if (_chatHub.IsValueCreated) ChatHub.StopAsync().GetAwaiter().GetResult(); } catch (Exception ex) { FileLogger.Warn("Shutdown", $"ChatHub.Stop 失败: {ex.Message}"); }
        // V3.3.0：释放 overlay 本地端口（未启动过就不必碰它）
        try { if (_rooms.IsValueCreated) Rooms.Dispose(); } catch (Exception ex) { FileLogger.Warn("Shutdown", $"Rooms.Dispose 失败: {ex.Message}"); }
        try { if (_live2d.IsValueCreated) Live2D.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch (Exception ex) { FileLogger.Warn("Shutdown", $"Live2D.Dispose 失败: {ex.Message}"); }
        try { if (_pluginInstall.IsValueCreated) PluginInstall.Dispose(); } catch (Exception ex) { FileLogger.Warn("Shutdown", $"PluginInstall.Dispose 失败: {ex.Message}"); }
        try { if (_pluginSearch.IsValueCreated) PluginSearch.Dispose(); } catch (Exception ex) { FileLogger.Warn("Shutdown", $"PluginSearch.Dispose 失败: {ex.Message}"); }
        try { Mini.Stop(); } catch (Exception ex) { FileLogger.Warn("Shutdown", $"Mini.Stop 失败: {ex.Message}"); }
        try { AutoSwitcher.Stop(); } catch (Exception ex) { FileLogger.Warn("Shutdown", $"AutoSwitcher.Stop 失败: {ex.Message}"); }
        try { Hotkeys.Dispose(); } catch (Exception ex) { FileLogger.Warn("Shutdown", $"Hotkeys.Dispose 失败: {ex.Message}"); }
        try { SystemMonitor.Dispose(); } catch (Exception ex) { FileLogger.Warn("Shutdown", $"SystemMonitor.Dispose 失败: {ex.Message}"); }
        try { RecordWatchdog.Dispose(); } catch (Exception ex) { FileLogger.Warn("Shutdown", $"RecordWatchdog.Dispose 失败: {ex.Message}"); }
        try { LogTailer.Dispose(); } catch (Exception ex) { FileLogger.Warn("Shutdown", $"LogTailer.Dispose 失败: {ex.Message}"); }
        try { if (_audioAlert.IsValueCreated) AudioAlert.Dispose(); } catch (Exception ex) { FileLogger.Warn("Shutdown", $"AudioAlert.Dispose 失败: {ex.Message}"); }
        if (_simpleRecord.IsValueCreated)
        {
            try { SimpleRecord.Dispose(); } catch (Exception ex) { FileLogger.Warn("Shutdown", $"SimpleRecord.Dispose 失败: {ex.Message}"); }
        }
        try { Timer.Dispose(); } catch (Exception ex) { FileLogger.Warn("Shutdown", $"Timer.Dispose 失败: {ex.Message}"); }
        try { Tray.Stop(); } catch (Exception ex) { FileLogger.Warn("Shutdown", $"Tray.Stop 失败: {ex.Message}"); }
    }
}
