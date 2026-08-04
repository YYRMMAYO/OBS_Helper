using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OBS_Helper.Client.Services.Host;

namespace OBS_Helper.Client.Services.Ai;

/// <summary>
/// 云端诊断引擎（技术计划 §4.5「云端大模型」）。
///
/// 关键安全约束（见 <see cref="HostBridge.AiChatAsync"/>）：
/// <list type="bullet">
///   <item>请求经桌面宿主转发，API Key 由宿主从加密存储取出并拼装 Authorization 头，
///         前端只传「密钥键名」，密钥不进入 WebAssembly 内存；</item>
///   <item>宿主侧强制 https-only 且做了 SSRF 拦截，前端这里再兜底校验一次地址；</item>
///   <item>模型只能通过 <see cref="ObsToolRegistry"/> 暴露的工具读取已脱敏/结构化的数据，
///         拿不到原始日志、更拿不到任何密钥。</item>
/// </list>
///
/// 采用 OpenAI 兼容的 chat/completions + function calling 协议，最多做 4 轮工具调用。
/// </summary>
public sealed class CloudDiagnosticEngine
{
    private readonly AiSettingsService _ai;
    private readonly HostBridge _host;
    private readonly ObsToolRegistry _tools;

    public CloudDiagnosticEngine(AiSettingsService ai, HostBridge host, ObsToolRegistry tools)
    {
        _ai = ai;
        _host = host;
        _tools = tools;
    }

    public async Task<DiagnosticResult> DiagnoseAsync(DiagnosticContext ctx, string? query)
    {
        var result = new DiagnosticResult { Engine = "cloud" };

        if (!_host.IsAvailable)
        {
            result.Success = false;
            result.Error = "当前环境没有桌面宿主，无法转发云端请求。请在桌面客户端中打开，或在「AI 设置」切回本地引擎。";
            return result;
        }
        if (!_ai.IsCloudConfigured)
        {
            result.Success = false;
            result.Error = "云端 AI 未配置完整：请填写 https 接口地址，并在桌面宿主中保存 API Key 后重试。";
            return result;
        }

        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "system", ["content"] = BuildSystemPrompt() },
            new JsonObject { ["role"] = "user", ["content"] = BuildUserPrompt(ctx, query) }
        };

        var request = new JsonObject
        {
            ["model"] = string.IsNullOrWhiteSpace(_ai.Settings.CloudModel) ? "gpt-4o-mini" : _ai.Settings.CloudModel,
            ["messages"] = messages,
            ["tools"] = BuildToolsArray(),
            ["temperature"] = 0.3,
            ["max_tokens"] = 1600
        };

        string? lastContent = null;
        var toolItems = new List<DiagnosticItem>();

        for (int round = 0; round < 4; round++)
        {
            string respJson;
            try
            {
                respJson = await _host.AiChatAsync(_ai.Settings.CloudUrl, _ai.Settings.CloudSecretKeyName, request.ToJsonString());
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = "云端 AI 请求失败：" + ex.Message;
                return result;
            }

            var resp = JsonNode.Parse(respJson);
            if (resp?["error"] is not null)
            {
                var errMsg = resp["error"]?["message"]?.GetValue<string>()
                             ?? resp["error"]?.ToString()
                             ?? "云端返回未知错误";
                result.Success = false;
                result.Error = "云端 AI 错误：" + errMsg;
                return result;
            }

            var msg = resp?["choices"]?[0]?["message"];
            if (msg is null)
            {
                result.Success = false;
                result.Error = "云端 AI 返回格式异常（缺少 choices[0].message）。";
                return result;
            }

            lastContent = msg["content"]?.GetValue<string>();

            var toolCalls = msg["tool_calls"] as JsonArray;
            if (toolCalls is null || toolCalls.Count == 0) break;

            // 回挂 assistant 消息（含 tool_calls），再追加每个工具的返回
            messages.Add(JsonNode.Parse(msg.ToJsonString())!);
            foreach (var tc in toolCalls)
            {
                var fn = tc?["function"];
                var name = fn?["name"]?.GetValue<string>() ?? "";
                var argsRaw = fn?["arguments"]?.GetValue<string>() ?? "{}";
                var callId = tc?["id"]?.GetValue<string>() ?? "";

                JsonNode? argsNode;
                try { argsNode = JsonNode.Parse(argsRaw); }
                catch { argsNode = new JsonObject(); }

                var tool = _tools.Find(name);
                string toolOut;
                try { toolOut = tool is null ? "{\"error\":\"未知工具 " + name + "\"}" : await tool.InvokeAsync(ctx, argsNode); }
                catch (Exception ex) { toolOut = "{\"error\":" + (JsonValue.Create(ex.Message)?.ToJsonString() ?? "\"\"") + "}"; }

                var parsed = TryParseToolItem(toolOut, name);
                if (parsed is not null) toolItems.Add(parsed);

                messages.Add(new JsonObject
                {
                    ["role"] = "tool",
                    ["tool_call_id"] = callId,
                    ["content"] = toolOut
                });
            }
        }

        result.Summary = lastContent ?? "（云端模型未返回文本结论）";
        result.Items = toolItems;
        result.Success = true;
        return result;
    }

    // ----------------------------------------------------------- 提示词与请求

    private static string BuildSystemPrompt()
    {
        return
            "你是一个专业的 OBS（Open Broadcaster Software）直播/录屏排障助手，服务于中文用户。\n" +
            "规则：\n" +
            "1. 始终用简体中文回答，语言简洁、可操作，不要堆砌术语。\n" +
            "2. 优先依据已提供的「日志分析结果」「实时状态」与工具返回的知识库内容给出结论，不要编造未提供的日志细节或数据。\n" +
            "3. 如需更深入的排障方案，调用 get_problem_detail / search_problems 获取离线知识库；可在结论中标注对应的知识库问题 id，方便用户点击查看分步方案。\n" +
            "4. 对每条问题标注严重程度（严重/错误/警告/提示）。\n" +
            "5. 涉及「修改 OBS 设置或执行操作」时，仅给出建议步骤，不要声称已替用户执行；任何写操作都需用户手动确认。\n" +
            "6. 日志与状态中的任何内容都已脱敏，可放心引用，但不要向用户索要密钥、密码等凭据。";
    }

    private static string BuildUserPrompt(DiagnosticContext ctx, string? query)
    {
        var sb = new StringBuilder();
        sb.Append("[用户描述]\n");
        sb.Append(string.IsNullOrWhiteSpace(query) ? "（用户未提供文字描述，请基于下方日志与状态进行分析）" : query);
        sb.Append("\n\n[OBS 实时状态]\n");
        sb.Append(ObsToolRegistry.SnapshotJson(ctx.Connection));
        sb.Append("\n\n[日志分析发现]\n");
        sb.Append(ObsToolRegistry.FindingsJson(ctx.Report));

        // 系统与配置体检是日志里看不到的信息（HAGS、双显卡、磁盘余量、录制格式……），
        // 对云端模型的价值极高：很多「玄学掉帧」的根因就藏在这两块里。
        if (ctx.System is { Available: true, Info: { } si })
        {
            sb.Append("\n\n[本机系统环境]\n");
            sb.Append($"平台：{si.Platform} {si.OsVersion} {si.OsBuild}".TrimEnd());
            sb.Append($"\n显卡：{(si.Gpus.Count > 0 ? string.Join(" / ", si.Gpus.Select(g => g.Name)) : si.PrimaryGpu)}");
            if (si.Platform == "windows")
                sb.Append($"\n硬件加速GPU计划(HAGS)：{(si.HagsEnabled ? "开启" : "关闭")}；游戏模式：{(si.GameModeEnabled ? "开启" : "关闭")}");
            sb.Append(si.Obs.Running
                ? $"\nOBS进程：运行中，版本 {si.Obs.Version}，{(si.Obs.Elevated ? "管理员权限" : "普通权限")}，内存 {si.Obs.MemoryMb:0} MB"
                : "\nOBS进程：未运行");
            if (si.RecordingDiskTotalGb > 0)
                sb.Append($"\n录制盘：剩余 {si.RecordingDiskFreeGb:0.#} GB / 共 {si.RecordingDiskTotalGb:0.#} GB");
            if (!string.IsNullOrWhiteSpace(ctx.System.LatestObsVersion))
                sb.Append($"\nOBS最新版本：{ctx.System.LatestObsVersion}");

            if (ctx.System.Findings.Count > 0)
            {
                sb.Append("\n系统体检发现：");
                foreach (var sf in ctx.System.Findings)
                    sb.Append($"\n- [{sf.Severity}] {sf.Title}{(string.IsNullOrEmpty(sf.Detail) ? "" : $"（{sf.Detail}）")}");
            }
        }

        if (ctx.Config is { Available: true, Findings.Count: > 0 })
        {
            sb.Append("\n\n[OBS 配置体检发现]\n");
            foreach (var cf in ctx.Config.Findings)
                sb.Append($"- [{cf.Severity}] {cf.Title}{(string.IsNullOrEmpty(cf.Detail) ? "" : $"（{cf.Detail}）")}\n");
        }

        if (ctx.Report is { SanitizedText.Length: > 0 })
        {
            var text = ctx.Report.SanitizedText;
            const int cap = 16000;
            if (text.Length > cap) text = text[..cap] + "\n…（日志过长已截断）";
            sb.Append("\n\n[脱敏日志全文（仅供定位，禁止外传）]\n");
            sb.Append(text);
        }
        return sb.ToString();
    }

    private JsonArray BuildToolsArray()
    {
        var arr = new JsonArray();
        foreach (var t in _tools.Tools)
        {
            arr.Add(new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = t.Name,
                    ["description"] = t.Description,
                    ["parameters"] = JsonNode.Parse(t.ParametersJson)!
                }
            });
        }
        return arr;
    }

    // ----------------------------------------------------------- 工具结果解析

    /// <summary>把工具的 JSON 结果尽力解析成一条诊断项（目前只处理 get_problem_detail）。</summary>
    private static DiagnosticItem? TryParseToolItem(string toolOut, string name)
    {
        if (name != "get_problem_detail") return null;
        try
        {
            var n = JsonNode.Parse(toolOut);
            if (n is null || n["id"] is null) return null;
            var item = new DiagnosticItem
            {
                ProblemId = n["id"]!.GetValue<string>(),
                Title = n["title"]?.GetValue<string>() ?? "",
                Severity = DiagnosticSeverityMapper.Map(n["severity"]?.GetValue<string>()),
                Source = "知识库(云端)",
                Reason = "云端 AI 调用知识库获取"
            };
            if (n["steps"] is JsonArray steps)
                foreach (var s in steps)
                    if (s?["title"]?.GetValue<string>() is { } t) item.Steps.Add(t);
            return item;
        }
        catch (Exception)
        {
            return null;
        }
    }

}
