using System.Text.Json;
using System.Text.Json.Nodes;
using OBS_Helper.Wpf.Services.Host;

namespace OBS_Helper.Wpf.Services.Ai;

/// <summary>
/// 免费内置 AI 诊断引擎，两种通道由用户自选、各按通道独立限频（见 <see cref="FreeRateLimiter"/>）：
/// <list type="bullet">
///   <item><see cref="FreeAiProvider.Zhipu"/>（默认）：智谱 GLM-4.7-Flash，国内直连；
///         密钥来自 <see cref="FreeAiKeyProvider"/>（构建时由脚本加密内嵌，运行时解密，不落盘），
///         经 <see cref="HostBridge.AiChatWithKeyAsync"/> 转发（https / SSRF 防护不变）；</item>
///   <item><see cref="FreeAiProvider.Pollinations"/>：国外免 Key 公共通道（无需任何密钥），
///         经 <see cref="HostBridge.AiChatNoAuthAsync"/> 转发；适合能直连国际网络的用户。</item>
/// </list>
/// 两种通道都是「单轮普通对话」，不做工具调用、不产生知识库诊断项——结论直接来自模型对脱敏日志 + 实时状态的分析。
///
/// 限额由 <see cref="FreeRateLimiter"/> 在编排层强制：两通道**各自独立**计数
/// （智谱每日 10 次、Pollinations 每日 20 次，10 秒间隔共用），
/// 每次发起请求前消耗 1 次，超出后由编排器自动回退本地引擎，不把压力打到免费端点。
/// </summary>
public sealed class FreeDiagnosticEngine
{
    private readonly AiSettingsService _ai;
    private readonly HostBridge _host;
    private readonly FreeAiKeyProvider _keyProvider;

    public FreeDiagnosticEngine(AiSettingsService ai, HostBridge host, FreeAiKeyProvider keyProvider)
    {
        _ai = ai;
        _host = host;
        _keyProvider = keyProvider;
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

        var provider = _ai.FreeProviderMode;
        string? apiKey = null;
        if (provider == FreeAiProvider.Zhipu)
        {
            apiKey = _keyProvider.GetKey();
            if (string.IsNullOrEmpty(apiKey))
            {
                result.Success = false;
                result.Error = "内置免费 AI 密钥未打包进当前安装包，智谱通道不可用；可在「AI 设置」把免费通道切到 Pollinations（国外免 Key），或切换到云端大模型接入自己的 API。";
                return result;
            }
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
            // glm-4.7-flash 会先消费 reasoning tokens 再输出结论，给足空间避免结论被截断
            ["max_tokens"] = 4096
        };

        string respJson;
        try
        {
            respJson = provider == FreeAiProvider.Zhipu
                ? await _host.AiChatWithKeyAsync(_ai.EffectiveFreeEndpoint, apiKey!, request.ToJsonString())
                : await _host.AiChatNoAuthAsync(_ai.EffectiveFreeEndpoint, request.ToJsonString());
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
