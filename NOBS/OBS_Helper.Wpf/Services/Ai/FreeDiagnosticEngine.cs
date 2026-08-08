using System.Text.Json;
using System.Text.Json.Nodes;
using OBS_Helper.Wpf.Services.Host;

namespace OBS_Helper.Wpf.Services.Ai;

/// <summary>
/// 免费内置 AI 诊断引擎（无需注册、无需 API Key，本地端每日限次）。
///
/// 与 <see cref="CloudDiagnosticEngine"/> 的差别：
/// <list type="bullet">
///   <item>请求不携带 Authorization 头，走 <see cref="HostBridge.AiChatNoAuthAsync"/>（https / SSRF 防护不变）；</item>
///   <item>免费共享端点不支持 function calling（实测传 tools 会返回 402），因此是「单轮普通对话」，
///         不做工具调用，也不产生知识库诊断项——结论直接来自模型对脱敏日志 + 实时状态的分析；</item>
///   <item>单轮请求即返回，无多轮工具循环，响应更快、对共享端点更友好。</item>
/// </list>
///
/// 限额由 <see cref="FreeRateLimiter"/> 在编排层强制：每次发起请求前消耗 1 次，
/// 超出后由编排器自动回退本地引擎，不把压力打到免费端点。
/// </summary>
public sealed class FreeDiagnosticEngine
{
    private readonly AiSettingsService _ai;
    private readonly HostBridge _host;

    public FreeDiagnosticEngine(AiSettingsService ai, HostBridge host)
    {
        _ai = ai;
        _host = host;
    }

    public async Task<DiagnosticResult> DiagnoseAsync(DiagnosticContext ctx, string? query)
    {
        var result = new DiagnosticResult { Engine = "free" };

        if (!_host.IsAvailable)
        {
            result.Success = false;
            result.Error = "当前环境没有桌面宿主，无法发起免费 AI 请求。请在桌面客户端中打开，或在「AI 设置」切回本地引擎。";
            return result;
        }

        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "system", ["content"] = CloudDiagnosticEngine.BuildSystemPrompt() },
            new JsonObject { ["role"] = "user", ["content"] = CloudDiagnosticEngine.BuildUserPrompt(ctx, query) }
        };

        var request = new JsonObject
        {
            ["model"] = _ai.EffectiveFreeModel,
            ["messages"] = messages,
            ["temperature"] = 0.3,
            ["max_tokens"] = 1600
        };

        string respJson;
        try
        {
            respJson = await _host.AiChatNoAuthAsync(AiSettingsService.FreeEndpointUrl, request.ToJsonString());
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = "免费 AI 请求失败：" + ex.Message;
            return result;
        }

        JsonNode? resp;
        try
        {
            resp = JsonNode.Parse(respJson, documentOptions: new JsonDocumentOptions { MaxDepth = 64 });
        }
        catch (JsonException ex)
        {
            result.Success = false;
            result.Error = "免费 AI 返回了无法解析的内容：" + ex.Message;
            return result;
        }

        if (resp?["error"] is not null)
        {
            var errMsg = resp["error"]?["message"]?.GetValue<string>()
                         ?? resp["error"]?.ToString()
                         ?? "免费端点返回未知错误";
            result.Success = false;
            result.Error = "免费 AI 错误：" + errMsg;
            return result;
        }

        var msg = resp?["choices"]?[0]?["message"];
        if (msg is null)
        {
            result.Success = false;
            result.Error = "免费 AI 返回格式异常（缺少 choices[0].message）。";
            return result;
        }

        result.Summary = msg["content"]?.GetValue<string>() ?? "（免费 AI 未返回文本结论）";
        result.Items = new List<DiagnosticItem>();
        result.Success = true;
        return result;
    }
}
