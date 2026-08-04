using OBS_Helper.Client.Services.Obs;
using OBS_Helper.Client.Services.Ai;

namespace OBS_Helper.Tests;

public class DiagnosticSeverityMapperTests
{
    [Theory]
    [InlineData(LogSeverity.Critical, DiagnosticSeverity.Critical)]
    [InlineData(LogSeverity.Error, DiagnosticSeverity.Error)]
    [InlineData(LogSeverity.Warning, DiagnosticSeverity.Warning)]
    [InlineData(LogSeverity.Info, DiagnosticSeverity.Info)]
    public void Map_FromLogSeverity(LogSeverity input, DiagnosticSeverity expected)
        => Assert.Equal(expected, DiagnosticSeverityMapper.Map(input));

    [Theory]
    [InlineData("严重", DiagnosticSeverity.Critical)]
    [InlineData("错误", DiagnosticSeverity.Error)]
    [InlineData("警告", DiagnosticSeverity.Warning)]
    [InlineData("一般", DiagnosticSeverity.Warning)]
    [InlineData("常见", DiagnosticSeverity.Suggestion)]
    public void Map_FromKbString(string input, DiagnosticSeverity expected)
        => Assert.Equal(expected, DiagnosticSeverityMapper.Map(input));

    [Fact]
    public void Map_UnknownOrNull_DefaultsToSuggestion()
    {
        Assert.Equal(DiagnosticSeverity.Suggestion, DiagnosticSeverityMapper.Map("不存在的级别"));
        Assert.Equal(DiagnosticSeverity.Suggestion, DiagnosticSeverityMapper.Map(null));
    }
}
