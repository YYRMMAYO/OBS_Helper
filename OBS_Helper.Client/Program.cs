using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using OBS_Helper.Client;
using OBS_Helper.Client.Services;
using OBS_Helper.Client.Services.Ai;
using OBS_Helper.Client.Services.Host;
using OBS_Helper.Client.Services.Obs;
using OBS_Helper.Client.Services.ObsConfig;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// —— 原有的离线知识库模块（保持不变） ——
builder.Services.AddScoped<ProblemService>();
builder.Services.AddScoped<BookmarkService>();
builder.Services.AddScoped<AssistantService>();

// —— 新增：桌面壳桥接 / 外观无障碍 ——
// Blazor WebAssembly 是单用户单会话，Scoped 实质等价于 Singleton；
// 这里统一用 Singleton 表达「整个应用生命周期共享一份状态」的语义。
builder.Services.AddSingleton<HostBridge>();
builder.Services.AddSingleton<AppearanceService>();

// —— 新增：obs-websocket 5.x 控制层（技术计划 §4.2/§4.3） ——
// ObsConnectionService 持有连接状态与场景/音频/输出快照，必须全局唯一，
// 否则切换页面会导致连接被重复建立。
builder.Services.AddSingleton<ObsSettingsService>();
builder.Services.AddSingleton<ObsConnectionService>();
// 实时监控：连接建立后自动轮询统计并按阈值告警，必须与连接服务同生命周期。
builder.Services.AddSingleton<LiveMonitorService>();

// —— 新增：日志分析与 AI 诊断（技术计划 §4.4/§4.5） ——
builder.Services.AddSingleton<ObsLogAnalyzer>();
// 配置体检：读取 obs-studio 目录下的 basic.ini / 场景集合，找出「配置本身就不对」的问题。
// 与日志分析互补——日志只能证明「已经出过事」，配置能提前预警「早晚要出事」。
builder.Services.AddSingleton<ObsConfigScanner>();
// 系统体检：显卡 / HAGS / 游戏模式 / 磁盘余量等宿主环境信息，宿主不可用时自动降级。
builder.Services.AddSingleton<SystemHealthService>();
builder.Services.AddSingleton<AiSettingsService>();
builder.Services.AddSingleton<ObsToolRegistry>();
builder.Services.AddSingleton<LocalDiagnosticEngine>();
builder.Services.AddSingleton<CloudDiagnosticEngine>();
builder.Services.AddSingleton<DiagnosticOrchestrator>();

// —— 新增：场景模板 + OBS 配置管理（Windows 版功能同步） ——
builder.Services.AddSingleton<SceneTemplateService>();
builder.Services.AddSingleton<ObsConfigService>();

await builder.Build().RunAsync();
