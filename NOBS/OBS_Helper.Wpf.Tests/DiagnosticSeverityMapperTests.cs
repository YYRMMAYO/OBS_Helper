using OBS_Helper.Wpf.Services.Ai;
using OBS_Helper.Wpf.Services.Obs;

namespace OBS_Helper.Wpf.Tests;

public class DiagnosticSeverityMapperTests
{
    [Theory]
    [InlineData(LogSeverity.Critical, DiagnosticSeverity.Critical)]
    [InlineData(LogSeverity.Error, DiagnosticSeverity.Error)]
    [InlineData(LogSeverity.Warning, DiagnosticSeverity.Warning)]
    [InlineData(LogSeverity.Info, DiagnosticSeverity.Info)]
    public void Map_LogSeverity(LogSeverity input, DiagnosticSeverity expected)
    {
        Assert.Equal(expected, DiagnosticSeverityMapper.Map(input));
    }

    [Theory]
    [InlineData("严重", DiagnosticSeverity.Critical)]
    [InlineData("错误", DiagnosticSeverity.Error)]
    [InlineData("警告", DiagnosticSeverity.Warning)]
    [InlineData("一般", DiagnosticSeverity.Warning)]
    [InlineData("常见", DiagnosticSeverity.Suggestion)]
    [InlineData(null, DiagnosticSeverity.Suggestion)]
    [InlineData("unknown", DiagnosticSeverity.Suggestion)]
    public void Map_String(string? input, DiagnosticSeverity expected)
    {
        Assert.Equal(expected, DiagnosticSeverityMapper.Map(input));
    }
}
