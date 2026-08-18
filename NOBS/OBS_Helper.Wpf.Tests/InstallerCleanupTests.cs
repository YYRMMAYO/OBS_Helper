using OBS_Helper.Wpf.Services.Update;

namespace OBS_Helper.Wpf.Tests;

public class InstallerCleanupTests
{
    [Theory]
    [InlineData("OBS_Helper_Setup_2.0.0.exe", InstallerCleanup.Kind.Setup)]
    [InlineData("obs_helper_setup_2.1.0.exe", InstallerCleanup.Kind.Setup)]
    [InlineData("OBS_Helper_Portable_2.0.0.exe", InstallerCleanup.Kind.PortableExe)]
    [InlineData("OBS_Helper_Portable_2.0.0.zip", InstallerCleanup.Kind.PortableZip)]
    [InlineData("OBS_Helper_Update_2.1.0.zip", InstallerCleanup.Kind.UpdateZip)]
    [InlineData("OBS_Helper_Manifest_2.0.0.json", InstallerCleanup.Kind.Manifest)]
    [InlineData("OBS_Helper_Setup_2.0.0.zip", InstallerCleanup.Kind.None)]
    [InlineData("Other_Setup_1.0.exe", InstallerCleanup.Kind.None)]
    [InlineData("report.pdf", InstallerCleanup.Kind.None)]
    [InlineData("OBS_Helper.exe", InstallerCleanup.Kind.None)]
    public void Classify_MatchesOnlyOwnArtifacts(string name, InstallerCleanup.Kind expected)
    {
        Assert.Equal(expected, InstallerCleanup.Classify(name));
    }

    [Fact]
    public void SelectFilesToDelete_KeepsNewestPerKind()
    {
        var files = new[]
        {
            ("C:\\t\\OBS_Helper_Setup_2.0.0.exe", new DateTime(2026, 1, 1)),
            ("C:\\t\\OBS_Helper_Setup_2.1.0.exe", new DateTime(2026, 8, 1)),
            ("C:\\t\\OBS_Helper_Update_2.1.0.zip", new DateTime(2026, 8, 1)),
            ("C:\\t\\OBS_Helper_Update_2.0.0.zip", new DateTime(2026, 1, 1)),
            ("C:\\t\\notes.txt", new DateTime(2026, 8, 1)),
        };

        var toDelete = InstallerCleanup.SelectFilesToDelete(files);

        Assert.Equal(2, toDelete.Count);
        Assert.Contains("C:\\t\\OBS_Helper_Setup_2.0.0.exe", toDelete);
        Assert.Contains("C:\\t\\OBS_Helper_Update_2.0.0.zip", toDelete);
        Assert.DoesNotContain("C:\\t\\OBS_Helper_Setup_2.1.0.exe", toDelete);
        Assert.DoesNotContain("C:\\t\\notes.txt", toDelete);
    }

    [Fact]
    public void SelectFilesToDelete_SingleFile_DeletesNothing()
    {
        var toDelete = InstallerCleanup.SelectFilesToDelete(new[]
        {
            ("C:\\t\\OBS_Helper_Setup_2.1.0.exe", DateTime.Now),
        });

        Assert.Empty(toDelete);
    }

    [Fact]
    public void SelectFilesToDelete_EmptyInput_DeletesNothing()
    {
        Assert.Empty(InstallerCleanup.SelectFilesToDelete(Array.Empty<(string, DateTime)>()));
    }
}
