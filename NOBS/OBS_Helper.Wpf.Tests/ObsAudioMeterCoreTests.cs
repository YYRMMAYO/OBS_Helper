using System.Text.Json;
using OBS_Helper.Wpf.Services.Obs;

namespace OBS_Helper.Wpf.Tests;

/// <summary>音量表解析（V3.6.0 / G05）：只接受合法输入，数值收敛到 0–1 / -100–0。</summary>
public class ObsAudioMeterCoreTests
{
    [Fact]
    public void Parse_ReadsAndClampsLevels()
    {
        using var doc = JsonDocument.Parse(
            "{\"inputs\":[{\"inputName\":\"Mic\",\"inputVolumeMul\":0.5,\"inputVolumeDb\":-6.0}," +
            "{\"inputName\":\"Desktop\",\"inputVolumeMul\":2.0,\"inputVolumeDb\":5.0}," +
            "{\"inputName\":\"\",\"inputVolumeMul\":1.0}]}");

        var levels = ObsAudioMeterCore.Parse(doc.RootElement);

        Assert.Equal(2, levels.Count);
        Assert.Equal("Mic", levels[0].InputName);
        Assert.Equal(0.5, levels[0].Mul, 3);
        Assert.Equal(-6.0, levels[0].Db, 3);
        Assert.Equal("Desktop", levels[1].InputName);
        Assert.Equal(1.0, levels[1].Mul, 3);
        Assert.Equal(0.0, levels[1].Db, 3);
    }

    [Fact]
    public void Parse_MissingOrInvalidPayload_ReturnsEmpty()
    {
        using var noInputs = JsonDocument.Parse("{\"foo\":1}");
        Assert.Empty(ObsAudioMeterCore.Parse(noInputs.RootElement));

        using var wrongType = JsonDocument.Parse("{\"inputs\":\"nope\"}");
        Assert.Empty(ObsAudioMeterCore.Parse(wrongType.RootElement));
    }
}
