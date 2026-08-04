using OBS_Helper.Client.Services.Obs;

namespace OBS_Helper.Tests;

public class ObsAuthTests
{
    private static string Sha256Base64(string input)
        => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(input)));

    [Fact]
    public void BuildAuthResponse_MatchesSpec()
    {
        const string password = "hunter2";
        const string salt = "YWJjZA==";
        const string challenge = "ZWZnaA==";

        var expectedSecret = Sha256Base64(password + salt);
        var expected = Sha256Base64(expectedSecret + challenge);

        var actual = ObsAuth.BuildAuthResponse(password, salt, challenge);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void BuildAuthResponse_IsBase64_32Bytes()
    {
        var actual = ObsAuth.BuildAuthResponse("pw", "salt", "chal");
        var bytes = Convert.FromBase64String(actual);
        Assert.Equal(32, bytes.Length);
    }

    [Fact]
    public void BuildAuthResponse_IsDeterministic()
    {
        var a = ObsAuth.BuildAuthResponse("pw", "salt", "chal");
        var b = ObsAuth.BuildAuthResponse("pw", "salt", "chal");
        Assert.Equal(a, b);
    }

    [Fact]
    public void BuildAuthResponse_DifferentPassword_DifferentResult()
    {
        var a = ObsAuth.BuildAuthResponse("pw1", "salt", "chal");
        var b = ObsAuth.BuildAuthResponse("pw2", "salt", "chal");
        Assert.NotEqual(a, b);
    }

    [Theory]
    [InlineData(null, "s", "c")]
    [InlineData("p", null, "c")]
    [InlineData("p", "s", null)]
    public void BuildAuthResponse_NullThrows(string? p, string? s, string? c)
    {
        Assert.Throws<ArgumentNullException>(() => ObsAuth.BuildAuthResponse(p!, s!, c!));
    }
}
