using System.Text.Json.Nodes;
using OBS_Helper.Wpf.Models.Live;
using OBS_Helper.Wpf.Services.Live;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// VTube Studio 公开 API 的协议层（V3.3.0 / P0-3）：请求形状、鉴权状态、模型列表、错误映射。
/// 不联网：全部是 JSON 与字符串逻辑，协议写错会在这里先炸。
/// </summary>
public class VtsProtocolCoreTests
{
    [Fact]
    public void Request_HasDocumentedShape()
    {
        var req = VtsProtocolCore.BuildRequest("ModelsRequest", "id-1");

        Assert.Equal("ModelsRequest", req["apiName"]!.GetValue<string>());
        Assert.Equal(1, req["apiVersion"]!.GetValue<int>());
        Assert.Equal("id-1", req["requestID"]!.GetValue<string>());
        Assert.Equal("APIStateRequest", req["messageType"]!.GetValue<string>());
        Assert.IsType<JsonObject>(req["data"]);
    }

    [Fact]
    public void TokenRequest_CarriesPluginIdentity()
    {
        var req = VtsProtocolCore.BuildTokenRequest("id", "OBS Help Assistant", "OBS Helper");
        var data = req["data"]!.AsObject();

        Assert.Equal("OBS Help Assistant", data["pluginName"]!.GetValue<string>());
        Assert.Equal("OBS Helper", data["pluginDeveloper"]!.GetValue<string>());
    }

    [Fact]
    public void AuthRequest_CarriesToken()
    {
        var req = VtsProtocolCore.BuildAuthRequest("id", "tok", "p", "d");
        Assert.Equal("tok", req["data"]!["authenticationToken"]!.GetValue<string>());
    }

    [Fact]
    public void ParseResponse_HandlesValidAndInvalidJson()
    {
        Assert.NotNull(VtsProtocolCore.ParseResponse("{\"messageType\":\"APIStateResponse\"}"));
        Assert.Null(VtsProtocolCore.ParseResponse("not json"));
        Assert.Null(VtsProtocolCore.ParseResponse(""));
        Assert.Null(VtsProtocolCore.ParseResponse(null));
    }

    [Fact]
    public void AuthSuccess_RequiresMatchingMessageType_AndNoError()
    {
        var ok = JsonNode.Parse("{\"messageType\":\"AuthenticationResponse\",\"data\":{\"authenticationToken\":\"abc\"}}")!.AsObject();
        var err = JsonNode.Parse("{\"messageType\":\"APIError\",\"data\":{\"errorID\":\"AuthenticationTokenError\"}}")!.AsObject();

        Assert.True(VtsProtocolCore.IsAuthSuccess(ok));
        Assert.Equal("abc", VtsProtocolCore.ExtractToken(ok));
        Assert.False(VtsProtocolCore.IsAuthSuccess(err));
        Assert.True(VtsProtocolCore.IsError(err));
    }

    [Fact]
    public void ParseModels_ReadsAvailableModels()
    {
        var resp = JsonNode.Parse("""
        {
          "messageType": "APIStateResponse",
          "data": {
            "availableModels": [
              { "modelID": "m1", "modelName": "Hiyori", "vtsModelName": "Hiyori.vtube.json", "modelFileName": "Hiyori.model3.json" },
              { "modelID": "", "modelName": "broken" }
            ]
          }
        }
        """)!.AsObject();

        var models = VtsProtocolCore.ParseModels(resp);
        Assert.Single(models);
        Assert.Equal("m1", models[0].ModelId);
        Assert.Equal("Hiyori", models[0].ModelName);
    }

    [Theory]
    [InlineData("API is not enabled", "L2D_VTS_API_OFF")]
    [InlineData("AuthenticationTokenError", "L2D_VTS_AUTH_DENIED")]
    [InlineData("ModelLoadError", "L2D_MODEL_LOAD_FAILED")]
    [InlineData("ModelLoadCooldown", "L2D_VTS_COOLDOWN")]
    [InlineData("ConfigWindowOpen", "L2D_VTS_CONFIG_OPEN")]
    public void MapError_ProducesStableCodes(string errorId, string expected)
    {
        var resp = JsonNode.Parse($"{{\"messageType\":\"APIError\",\"data\":{{\"errorID\":\"{errorId}\"}}}}")!.AsObject();
        Assert.Equal(expected, VtsProtocolCore.MapError(resp));
    }

    [Fact]
    public void MapError_DefaultsToRequestFailed()
    {
        Assert.Equal("L2D_VTS_REQUEST_FAILED", VtsProtocolCore.MapError(null));
        Assert.Equal("L2D_VTS_REQUEST_FAILED",
            VtsProtocolCore.MapError(JsonNode.Parse("{\"messageType\":\"APIError\",\"data\":{}}")!.AsObject()));
    }

    [Fact]
    public void FindModel_MatchesNameFileAndLooseVariants()
    {
        var models = new[]
        {
            new VtsModel { ModelId = "1", ModelName = "Hiyori", ModelFileName = "Hiyori.model3.json" },
            new VtsModel { ModelId = "2", ModelName = "Mao", VtsModelName = "Mao_2024" }
        };

        Assert.Equal("1", VtsProtocolCore.FindModel(models, "Hiyori")!.ModelId);
        Assert.Equal("1", VtsProtocolCore.FindModel(models, "hiyori.model3.json")!.ModelId);
        Assert.Equal("2", VtsProtocolCore.FindModel(models, "Mao 2024")!.ModelId);
        Assert.Null(VtsProtocolCore.FindModel(models, "Unknown"));
        Assert.Null(VtsProtocolCore.FindModel(models, null));
    }
}

/// <summary>Live2D 采集优先级与设备识别（V3.3.0 / P0-3）。</summary>
public class Live2DCaptureCoreTests
{
    [Fact]
    public void Priority_Ndi_BeatsEverything()
    {
        var choice = Live2DCaptureCore.Choose(
            new[] { "window_capture", "dshow_input", "spout2_source", "ndi_source" },
            new[] { "VTube Studio Camera" });

        Assert.Equal(Live2DCaptureKind.Ndi, choice.Method);
        Assert.Equal("ndi_source", choice.InputKind);
    }

    [Fact]
    public void Priority_Spout_BeatsVirtualCamera()
    {
        var choice = Live2DCaptureCore.Choose(
            new[] { "window_capture", "dshow_input", "spout2_source" },
            new[] { "VTube Studio Camera" });

        Assert.Equal(Live2DCaptureKind.Spout2, choice.Method);
    }

    [Fact]
    public void Priority_VirtualCamera_UsesMatchingDevice()
    {
        var choice = Live2DCaptureCore.Choose(
            new[] { "dshow_input", "window_capture" },
            new[] { "HD Webcam", "VTube Studio Camera" });

        Assert.Equal(Live2DCaptureKind.VirtualCamera, choice.Method);
        Assert.Equal("dshow_input", choice.InputKind);
        Assert.Equal("VTube Studio Camera", choice.Device);
        Assert.Equal("VTube Studio Camera",
            Live2DCaptureCore.BuildSettings(choice)["video_device_id"]!.GetValue<string>());
    }

    [Fact]
    public void Priority_WindowCapture_WhenOnlyWindowAvailable()
    {
        var choice = Live2DCaptureCore.Choose(new[] { "window_capture" }, null);
        Assert.Equal(Live2DCaptureKind.WindowCapture, choice.Method);
        Assert.Equal("window_capture", choice.InputKind);
    }

    [Fact]
    public void None_WhenNoCaptureKindAvailable()
    {
        var choice = Live2DCaptureCore.Choose(Array.Empty<string>(), null);
        Assert.Equal(Live2DCaptureKind.None, choice.Method);
        Assert.Equal("live2d.capture.none", Live2DCaptureCore.UnavailableHintKey(choice));
    }

    [Theory]
    [InlineData("VTube Studio Camera", true)]
    [InlineData("OBS Virtual Camera", true)]
    [InlineData("HD Webcam", false)]
    [InlineData(null, false)]
    public void LooksLikeVtsCamera_IsCaseInsensitive(string? name, bool expected)
        => Assert.Equal(expected, Live2DCaptureCore.LooksLikeVtsCamera(name));

    [Theory]
    [InlineData("VTube Studio", true)]
    [InlineData("VTube Studio.exe", true)]
    [InlineData("obs64", false)]
    public void IsVtsProcess_MatchesProcessName(string name, bool expected)
        => Assert.Equal(expected, Live2DCaptureCore.IsVtsProcess(name));
}
