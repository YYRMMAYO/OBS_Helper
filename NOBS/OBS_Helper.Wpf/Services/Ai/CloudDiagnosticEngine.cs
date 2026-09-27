using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OBS_Helper.Wpf.Services.Host;

namespace OBS_Helper.Wpf.Services.Ai;

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
            result.Error = Strings.T("ai.cloud.noHost");
            return result;
        }
        if (!_ai.IsCloudConfigured)
        {
            result.Success = false;
            result.Error = Strings.T("ai.cloud.notConfigured");
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

        try
        {
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
                    result.Error = Strings.T("ai.cloud.requestFailed", ex.Message);
                    return result;
                }

                JsonNode? resp;
                try
                {
                    // 显式限制嵌套深度：JsonNode.Parse 内部虽默认 64 层，这里写清楚防止任何实现变更带来栈溢出风险
                    resp = JsonNode.Parse(respJson, documentOptions: new JsonDocumentOptions { MaxDepth = 64 });
                }
                catch (JsonException ex)
                {
                    result.Success = false;
                    result.Error = Strings.T("ai.cloud.parseFailed", ex.Message);
                    return result;
                }

                if (resp?["error"] is not null)
                {
                    var errMsg = resp["error"]?["message"]?.GetValue<string>()
                                 ?? resp["error"]?.ToString()
                                 ?? Strings.T("ai.cloud.unknownError");
                    result.Success = false;
                    result.Error = Strings.T("ai.cloud.errorPrefix", errMsg);
                    return result;
                }

                var msg = resp?["choices"]?[0]?["message"];
                if (msg is null)
                {
                    result.Success = false;
                    result.Error = Strings.T("ai.cloud.badFormat");
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
                    try { toolOut = tool is null ? "{\"error\":" + JsonValue.Create(Strings.T("ai.cloud.unknownTool", name))!.ToJsonString() + "}" : await tool.InvokeAsync(ctx, argsNode); }
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
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException or FormatException)
        {
            // 云端返回的字段类型异常 / 嵌套过深等：转成失败结果，让上层回退本地引擎，
            // 而不是把异常一路抛到 UI。
            result.Success = false;
            result.Error = Strings.T("ai.cloud.processingFailed", ex.Message);
            return result;
        }

        result.Summary = lastContent ?? Strings.T("ai.cloud.noText");
        result.Items = toolItems;
        result.Success = true;
        return result;
    }

    // ----------------------------------------------------------- 提示词与请求

    internal static string BuildSystemPrompt()
    {
        return Strings.T("ai.prompt.system");
    }

    internal static string BuildUserPrompt(DiagnosticContext ctx, string? query)
    {
        var sb = new StringBuilder();
        sb.Append(Strings.T("ai.prompt.userHeader"));
        sb.Append(string.IsNullOrWhiteSpace(query) ? Strings.T("ai.prompt.userNoQuery") : query);
        sb.Append(Strings.T("ai.prompt.stateHeader"));
        sb.Append(ObsToolRegistry.SnapshotJson(ctx.Connection));
        sb.Append(Strings.T("ai.prompt.findingsHeader"));
        sb.Append(ObsToolRegistry.FindingsJson(ctx.Report));

        if (ctx.Report is { SanitizedText.Length: > 0 })
        {
            var text = ctx.Report.SanitizedText;
            const int cap = 16000;
            if (text.Length > cap) text = text[..cap] + Strings.T("ai.prompt.logTruncated");
            sb.Append(Strings.T("ai.prompt.logHeader"));
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
                Source = Strings.T("log.source.knowledgeBaseCloud"),
                Reason = Strings.T("log.reason.cloudTool")
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