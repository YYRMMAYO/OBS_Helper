using System.Text.Json;
using System.Text.Json.Nodes;
using OBS_Helper.Wpf.Models;
using OBS_Helper.Wpf.Models.Obs;
using OBS_Helper.Wpf.Services.Host;
using OBS_Helper.Wpf.Services.Obs;

namespace OBS_Helper.Wpf.Services.Ai;

/// <summary>一个可供云端大模型通过 function-calling 调用的诊断工具。</summary>
public sealed class DiagnosticTool
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    /// <summary>JSON Schema 对象字符串（不含外层 type:object 包裹也可，这里直接给完整对象）。</summary>
    public string ParametersJson { get; init; } = "{\"type\":\"object\",\"properties\":{}}";
    public Func<DiagnosticContext, JsonNode?, Task<string>> InvokeAsync { get; init; } =
        (_, _) => Task.FromResult("{}");
}

/// <summary>
/// 诊断工具注册表（技术计划 §4.5「工具调用」）。
///
/// 云端大模型并不直接触碰 OBS 实时状态或知识库——它只能通过这些工具拿到
/// 「经过我们裁剪、脱敏、结构化」的数据。这样既保护了隐私，也让模型输出更可控：
/// <list type="bullet">
///   <item><c>get_connection_snapshot</c>：当前 OBS 实时状态（连接/场景/音频/性能）。</item>
///   <item><c>get_log_findings</c>：最近一次日志分析的发现清单。</item>
///   <item><c>get_problem_detail</c>：按 id 取离线知识库的完整排障方案。</item>
///   <item><c>search_problems</c>：在知识库里按关键词搜索。</item>
/// </list>
/// 所有工具返回都是 JSON 文本，且只包含已脱敏/结构化的内容。
/// </summary>
public sealed class ObsToolRegistry
{
    private readonly ProblemService _problems;
    private readonly IReadOnlyList<DiagnosticTool> _tools;

    public ObsToolRegistry(ProblemService problems)
    {
        _problems = problems;
        _tools = BuildTools();
    }

    public IReadOnlyList<DiagnosticTool> Tools => _tools;

    public DiagnosticTool? Find(string name) => _tools.FirstOrDefault(t => t.Name == name);

    private List<DiagnosticTool> BuildTools()
    {
        return new()
        {
            new DiagnosticTool
            {
                Name = "get_connection_snapshot",
                Description = "获取当前 OBS 的实时连接状态、场景列表、音频输入、录制/推流状态与性能统计（已结构化，不含隐私）。",
                ParametersJson = "{\"type\":\"object\",\"properties\":{}}",
                InvokeAsync = (ctx, _) => Task.FromResult(SnapshotJson(ctx.Connection))
            },
            new DiagnosticTool
            {
                Name = "get_log_findings",
                Description = "获取最近一次 OBS 日志分析的发现清单（含严重程度、证据与建议），用于核对已知条件。",
                ParametersJson = "{\"type\":\"object\",\"properties\":{}}",
                InvokeAsync = (ctx, _) => Task.FromResult(FindingsJson(ctx.Report))
            },
            new DiagnosticTool
            {
                Name = "get_problem_detail",
                Description = "根据问题 id 获取离线知识库中的完整排障方案（症状、成因、分步步骤、参考链接）。",
                ParametersJson = "{\"type\":\"object\",\"properties\":{\"problemId\":{\"type\":\"string\",\"description\":\"问题条目 id，如 enc-overload、sf-auth\"}},\"required\":[\"problemId\"]}",
                InvokeAsync = async (ctx, args) =>
                {
                    var id = ArgsString(args, "problemId");
                    if (string.IsNullOrWhiteSpace(id)) return "{\"found\":false,\"reason\":\"缺少 problemId\"}";
                    var p = await _problems.GetByIdAsync(id);
                    return p is null ? "{\"found\":false}" : ProblemToNode(p).ToJsonString();
                }
            },
            new DiagnosticTool
            {
                Name = "search_problems",
                Description = "在离线知识库中按关键词搜索排障条目，适用于日志/状态里没有直接给出 id 的情况。",
                ParametersJson = "{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\",\"description\":\"中文或英文关键词\"}},\"required\":[\"query\"]}",
                InvokeAsync = async (ctx, args) =>
                {
                    var q = ArgsString(args, "query");
                    if (string.IsNullOrWhiteSpace(q)) return "[]";
                    var list = await _problems.SearchAsync(q);
                    var arr = new JsonArray();
                    foreach (var p in list.Take(10))
                        arr.Add(new JsonObject { ["id"] = p.Id, ["title"] = p.Title, ["category"] = p.Category });
                    return arr.ToJsonString();
                }
            }
        };
    }

    private static string ArgsString(JsonNode? args, string name)
    {
        if (args is JsonObject o && o.TryGetPropertyValue(name, out var v) && v is JsonValue jv)
            return jv.ToString() ?? "";
        return "";
    }

    /// <summary>把实时连接状态结构化为 JSON（供工具与云端提示词共用）。</summary>
    internal static string SnapshotJson(ObsConnectionService c)
    {
        var root = new JsonObject
        {
            ["connected"] = c.IsConnected,
            ["state"] = c.State.ToString(),
            ["obsVersion"] = c.Profile.ObsVersion,
            ["platform"] = c.Profile.Platform,
            ["baseResolution"] = $"{c.Profile.BaseWidth}x{c.Profile.BaseHeight}",
            ["outputResolution"] = $"{c.Profile.OutputWidth}x{c.Profile.OutputHeight}",
            ["fps"] = Math.Round(c.Profile.Fps, 2),
            ["activeFps"] = Math.Round(c.Stats.ActiveFps, 1),
            ["cpuUsage"] = Math.Round(c.Stats.CpuUsage, 1),
            ["renderSkipRatio"] = Math.Round(c.Stats.RenderSkipRatio, 4),
            ["outputSkipRatio"] = Math.Round(c.Stats.OutputSkipRatio, 4),
            ["recording"] = c.RecordStatus.Active,
            ["streaming"] = c.StreamStatus.Active,
            ["streamCongestion"] = Math.Round(c.StreamStatus.Congestion, 3),
            ["streamDroppedRatio"] = Math.Round(c.StreamStatus.DroppedRatio, 4),
            ["currentScene"] = c.CurrentScene
        };

        var scenes = new JsonArray();
        foreach (var s in c.Scenes) scenes.Add(s.Name);
        root["scenes"] = scenes;

        var audio = new JsonArray();
        foreach (var a in c.AudioInputs)
            audio.Add(new JsonObject { ["name"] = a.Name, ["muted"] = a.Muted, ["volumeDb"] = Math.Round(a.VolumeDb, 1) });
        root["audioInputs"] = audio;

        return root.ToJsonString();
    }

    /// <summary>把日志分析发现结构化为 JSON（供工具与云端提示词共用）。</summary>
    internal static string FindingsJson(ObsLogReport? report)
    {
        if (report is null) return "{\"available\":false}";

        var arr = new JsonArray();
        foreach (var f in report.Findings)
        {
            arr.Add(new JsonObject
            {
                ["code"] = f.Code,
                ["severity"] = f.SeverityText,
                ["title"] = f.Title,
                ["problemId"] = f.ProblemId ?? "",
                ["occurrences"] = f.Occurrences,
                ["suggestion"] = f.Suggestion
            });
        }

        var root = new JsonObject
        {
            ["available"] = true,
            ["source"] = report.SourceName,
            ["obsVersion"] = report.Summary.ObsVersion,
            ["renderLagRatio"] = Math.Round(report.Summary.RenderLagRatio, 4),
            ["encodingLagRatio"] = Math.Round(report.Summary.EncodingLagRatio, 4),
            ["networkDropRatio"] = Math.Round(report.Summary.NetworkDropRatio, 4),
            ["findings"] = arr
        };
        return root.ToJsonString();
    }

    /// <summary>把知识库条目结构化为 JSON（供 get_problem_detail 工具返回）。</summary>
    internal static JsonObject ProblemToNode(Problem p)
    {
        var steps = new JsonArray();
        foreach (var s in p.Steps)
            steps.Add(new JsonObject { ["title"] = s.Title, ["detail"] = s.Detail, ["level"] = s.Level });

        var links = new JsonArray();
        foreach (var l in p.Links)
            links.Add(new JsonObject { ["title"] = l.Title, ["url"] = l.Url });

        return new JsonObject
        {
            ["id"] = p.Id,
            ["title"] = p.Title,
            ["category"] = p.Category,
            ["severity"] = p.Severity,
            ["symptoms"] = JsonArrayFrom(p.Symptoms),
            ["causes"] = JsonArrayFrom(p.Causes),
            ["steps"] = steps,
            ["tips"] = JsonArrayFrom(p.Tips),
            ["platforms"] = JsonArrayFrom(p.Platforms),
            ["links"] = links
        };
    }

    private static JsonArray JsonArrayFrom(string[] arr)
    {
        var a = new JsonArray();
        foreach (var s in arr) a.Add(s);
        return a;
    }
}
