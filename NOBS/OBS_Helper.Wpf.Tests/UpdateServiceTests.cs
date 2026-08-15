using OBS_Helper.Wpf.Services;

namespace OBS_Helper.Wpf.Tests;

public class UpdateServiceTests
{
    [Theory]
    [InlineData("V1.4.8", "1.4.8")]
    [InlineData("v1.4.8", "1.4.8")]
    [InlineData("1.4.8", "1.4.8")]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void StripVersionPrefix_Correct(string? input, string expected)
    {
        Assert.Equal(expected, UpdateService.StripVersionPrefix(input));
    }

    [Theory]
    [InlineData("1.4.8", 1, 4, 8)]
    [InlineData("V1.4.8", 1, 4, 8)]
    [InlineData("v1.4.8", 1, 4, 8)]
    public void ParseVersion_Valid(string input, int maj, int min, int build)
    {
        var v = UpdateService.ParseVersion(input);
        Assert.NotNull(v);
        Assert.Equal(maj, v!.Major);
        Assert.Equal(min, v.Minor);
        Assert.Equal(build, v.Build);
    }

    [Theory]
    [InlineData("1.0-beta")]
    [InlineData("release-1.4.8")]
    [InlineData("garbage")]
    [InlineData(null)]
    public void ParseVersion_Invalid_ReturnsNull(string? input)
    {
        Assert.Null(UpdateService.ParseVersion(input));
    }
}
