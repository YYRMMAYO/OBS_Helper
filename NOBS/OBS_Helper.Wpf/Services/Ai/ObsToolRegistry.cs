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

    public ObsToolRegistry(ProblemService problems) => _problems = problems;

    /// <summary>
    /// 工具清单（含名称 / 说明 / 参数 schema）。
    ///
    /// 每次访问现建而不是构造时缓存（V2.9.2）：说明文案取自文案表，缓存会在切换语言后
    /// 继续把旧语言的描述发给模型。只 4 个工具、构造极轻，不值得为此做缓存。
    /// </summary>
    public IReadOnlyList<DiagnosticTool> Tools => BuildTools();

    public DiagnosticTool? Find(string name) => Tools.FirstOrDefault(t => t.Name == name);

    private List<DiagnosticTool> BuildTools()
    {
        return new()
        {
            new DiagnosticTool
            {
                Name = "get_connection_snapshot",
                Description = Strings.T("ai.tool.connectionSnapshot.desc"),
                ParametersJson = "{\"type\":\"object\",\"properties\":{}}",
                InvokeAsync = (ctx, _) => Task.FromResult(SnapshotJson(ctx.Connection))
            },
            new DiagnosticTool
            {
                Name = "get_log_findings",
                Description = Strings.T("ai.tool.logFindings.desc"),
                ParametersJson = "{\"type\":\"object\",\"properties\":{}}",
                InvokeAsync = (ctx, _) => Task.FromResult(FindingsJson(ctx.Report))
            },
            new DiagnosticTool
            {
                Name = "get_problem_detail",
                Description = Strings.T("ai.tool.problemDetail.desc"),
                ParametersJson = "{\"type\":\"object\",\"properties\":{\"problemId\":{\"type\":\"string\",\"description\":" + JsonValue.Create(Strings.T("ai.tool.problemDetail.param"))!.ToJsonString() + "}},\"required\":[\"problemId\"]}",
                InvokeAsync = async (ctx, args) =>
                {
                    var id = ArgsString(args, "problemId");
                    if (string.IsNullOrWhiteSpace(id)) return "{\"found\":false,\"reason\":" + JsonValue.Create(Strings.T("ai.tool.missingProblemId"))!.ToJsonString() + "}";
                    var p = await _problems.GetByIdAsync(id);
                    return p is null ? "{\"found\":false}" : ProblemToNode(p).ToJsonString();
                }
            },
            new DiagnosticTool
            {
                Name = "search_problems",
                Description = Strings.T("ai.tool.searchProblems.desc"),
                ParametersJson = "{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\",\"description\":" + JsonValue.Create(Strings.T("ai.tool.searchProblems.param"))!.ToJsonString() + "}},\"required\":[\"query\"]}",
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
