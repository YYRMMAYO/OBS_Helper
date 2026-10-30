using System.Text.Json;
using System.Text.Json.Nodes;
using OBS_Helper.Wpf.Models.Live;

namespace OBS_Helper.Wpf.Services.Live;

/// <summary>
/// VTube Studio 公开 API 的协议层（V3.3.0 / P0-3）——纯逻辑。
///
/// 只实现**公开协议文档**里的请求 / 响应形状（MIT 协议，见
/// <c>https://github.com/DenchiSoft/VTubeStudio</c>），不复制其代码、不注入进程、不读内存。
/// 网络连接在 <see cref="VTubeStudioClient"/>；这里只管 JSON 与错误码映射，方便单测。
/// </summary>
public static class VtsProtocolCore
{
    /// <summary>默认 WebSocket 端口（用户可在 VTS 里改）。</summary>
    public const int DefaultPort = 8001;

    /// <summary>UDP 发现端口（VTS 每 2 秒广播一次状态）。</summary>
    public const int DiscoveryPort = 47779;

    /// <summary>API 版本（VTS 文档中的 apiVersion 固定为 1）。</summary>
    public const int ApiVersion = 1;

    /// <summary>构造一个请求对象。</summary>
    public static JsonObject BuildRequest(string apiName, string requestId, JsonObject? data = null)
    {
        return new JsonObject
        {
            ["apiName"] = apiName,
            ["apiVersion"] = ApiVersion,
            ["requestID"] = requestId,
            ["messageType"] = "APIStateRequest",
            ["data"] = data ?? new JsonObject()
        };
    }

    /// <summary>鉴权请求（首次：VTS 内弹窗让用户允许）。</summary>
    public static JsonObject BuildTokenRequest(string requestId, string pluginName, string developer)
        => BuildRequest("AuthenticationTokenRequest", requestId, new JsonObject
        {
            ["pluginName"] = pluginName,
            ["pluginDeveloper"] = developer
        });

    /// <summary>用已保存的 token 鉴权。</summary>
    public static JsonObject BuildAuthRequest(string requestId, string token, string pluginName, string developer)
        => BuildRequest("AuthenticationRequest", requestId, new JsonObject
        {
            ["authenticationToken"] = token,
            ["pluginName"] = pluginName,
            ["pluginDeveloper"] = developer
        });

    /// <summary>读取模型列表。</summary>
    public static JsonObject BuildModelsRequest(string requestId)
        => BuildRequest("ModelsRequest", requestId);

    /// <summary>按 modelID 加载模型。</summary>
    public static JsonObject BuildLoadModelRequest(string requestId, string modelId)
        => BuildRequest("ModelLoadRequest", requestId, new JsonObject { ["modelID"] = modelId });

    /// <summary>移动 / 缩放已加载的模型。</summary>
    public static JsonObject BuildMoveModelRequest(string requestId, double x, double y, double size, double rotation, bool faceCamera)
        => BuildRequest("MoveModelRequest", requestId, new JsonObject
        {
            ["timeInSeconds"] = 0.0,
            ["valuesAreRelativeToModel"] = false,
            ["positionX"] = x,
            ["positionY"] = y,
            ["rotation"] = rotation,
            ["size"] = size,
            ["faceAngleX"] = 0,
            ["faceAngleY"] = 0,
            ["faceAngleZ"] = 0,
            ["eyeOpenLeft"] = 1,
            ["eyeOpenRight"] = 1,
            ["autoEyeBlink"] = true,
            ["faceFound"] = faceCamera
        });

    /// <summary>读取模型目录名（用于告诉用户把模型放到哪里）。</summary>
    public static JsonObject BuildFolderInfoRequest(string requestId)
        => BuildRequest("VTSFolderInfoRequest", requestId);

    /// <summary>
    /// 解析一行响应。返回 null 表示不是合法 JSON 对象（调用方应忽略该行继续读）。
    /// </summary>
    public static JsonObject? ParseResponse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>响应的 messageType 是否为「API 错误」。</summary>
    public static bool IsError(JsonObject? response)
        => response is not null
           && string.Equals(response["messageType"]?.GetValue<string>(), "APIError", StringComparison.OrdinalIgnoreCase);

    /// <summary>从鉴权响应里取 token（没有则返回 null）。</summary>
    public static string? ExtractToken(JsonObject? response)
    {
        if (response is null) return null;
        if (response["data"] is not JsonObject data) return null;
        var token = data["authenticationToken"]?.GetValue<string>();
        return string.IsNullOrWhiteSpace(token) ? null : token;
    }

    /// <summary>鉴权是否成功（APIError 视为失败）。</summary>
    public static bool IsAuthSuccess(JsonObject? response)
        => response is not null && !IsError(response)
           && string.Equals(response["messageType"]?.GetValue<string>(), "AuthenticationResponse", StringComparison.OrdinalIgnoreCase);

    /// <summary>读取模型列表。</summary>
    public static IReadOnlyList<VtsModel> ParseModels(JsonObject? response)
    {
        if (response?["data"] is not JsonObject data) return Array.Empty<VtsModel>();
        if (data["availableModels"] is not JsonArray arr) return Array.Empty<VtsModel>();

        var list = new List<VtsModel>();
        foreach (var node in arr)
        {
            if (node is not JsonObject m) continue;
            var id = m["modelID"]?.GetValue<string>() ?? "";
            if (id.Length == 0) continue;
            list.Add(new VtsModel
            {
                ModelId = id,
                ModelName = m["modelName"]?.GetValue<string>() ?? "",
                VtsModelName = m["vtsModelName"]?.GetValue<string>() ?? "",
                ModelFileName = m["modelFileName"]?.GetValue<string>() ?? ""
            });
        }
        return list;
    }

    /// <summary>读取 VTS 的模型目录名（VTSFolderInfoResponse.data.models）。</summary>
    public static string? ParseModelsFolder(JsonObject? response)
        => response?["data"] is JsonObject data ? data["models"]?.GetValue<string>() : null;

    /// <summary>
    /// 把 VTS 的 errorID / message 映射成本项目的稳定错误码。
    /// 不做重试风暴：调用方拿到错误码后直接给用户出口，由用户决定是否重试。
    /// </summary>
    public static string MapError(JsonObject? errorResponse)
    {
        if (errorResponse is null) return "L2D_VTS_REQUEST_FAILED";

        var errorId = errorResponse["data"] is JsonObject d
            ? d["errorID"]?.GetValue<string>() ?? ""
            : "";
        var message = errorResponse["data"] is JsonObject d2
            ? d2["message"]?.GetValue<string>() ?? ""
            : "";

        if (errorId.Length == 0 && message.Length == 0) return "L2D_VTS_REQUEST_FAILED";

        // 文档里的错误 ID 是驼峰 / 大写混用，且 message 可能是自然语言，
        // 统一去掉空格后比较，避免 "API is not enabled" 这种写法漏判。
        var key = (errorId + " " + message).ToLowerInvariant().Replace(" ", "");

        if (key.Contains("apinot") || key.Contains("apidisabled") || key.Contains("notenabled") || key.Contains("disabled"))
            return "L2D_VTS_API_OFF";
        if (key.Contains("authentication") || key.Contains("token") || key.Contains("denied"))
            return "L2D_VTS_AUTH_DENIED";
        if (key.Contains("cooldown") || key.Contains("toomany"))
            return "L2D_VTS_COOLDOWN";
        if (key.Contains("config") && key.Contains("open"))
            return "L2D_VTS_CONFIG_OPEN";
        if (key.Contains("modelload") || key.Contains("modelloading") || key.Contains("model"))
            return "L2D_MODEL_LOAD_FAILED";
        return "L2D_VTS_REQUEST_FAILED";
    }

    /// <summary>
    /// 从模型列表里挑一个最可能匹配用户选择的名字（大小写 / 空格不敏感）。
    /// 先比 modelName，再比 vtsModelName 与文件名；找不到返回 null。
    /// </summary>
    public static VtsModel? FindModel(IEnumerable<VtsModel> models, string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var wanted = Normalize(name);

        foreach (var m in models)
        {
            if (Normalize(m.ModelName) == wanted || Normalize(m.VtsModelName) == wanted || Normalize(m.ModelFileName) == wanted)
                return m;
        }

        // 退一步：包含匹配（用户目录名常带版本后缀，如 "Hiyori_v3"）
        foreach (var m in models)
        {
            if (Normalize(m.ModelName).Contains(wanted, StringComparison.Ordinal) ||
                wanted.Contains(Normalize(m.ModelName), StringComparison.Ordinal))
                return m;
        }
        return null;
    }

    private static string Normalize(string? s)
        => (s ?? "").Trim().Replace(".model3.json", "", StringComparison.OrdinalIgnoreCase)
            .Replace("_", "").Replace("-", "").Replace(" ", "").ToLowerInvariant();
}
