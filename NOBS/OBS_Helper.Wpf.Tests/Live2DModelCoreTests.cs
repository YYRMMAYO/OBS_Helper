using System.Text;
using OBS_Helper.Wpf.Models.Live;
using OBS_Helper.Wpf.Services.Live;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// Live2D 模型校验与布局（V3.3.0 / P0-3）。
///
/// 模型来自用户磁盘，天然不可信：越界引用、可执行文件混入、超大目录都必须在**打开前**拒绝。
/// 这里用临时目录构造真实文件，覆盖「合法模型 / 越界 / 缺文件 / 危险扩展 / 布局几何」。
/// </summary>
public class Live2DModelCoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "nobs-live2d-" + Guid.NewGuid().ToString("N"));

    public Live2DModelCoreTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* 清理失败无妨 */ }
    }

    [Fact]
    public void ValidModel_IsAcceptedAndHashed()
    {
        var (json, modelDir) = CreateModel("Hiyori", "textures/t0.png");

        Assert.True(Live2DModelCore.TryValidate(json, out var model, out var error), error);
        Assert.NotNull(model);
        Assert.Equal("Hiyori", model!.Name);
        Assert.Equal(64, model.ManifestHash.Length);
        Assert.Single(model.Textures);
        Assert.True(model.FileCount >= 3);
        Assert.True(model.TotalBytes > 0);
        Assert.Equal(Path.GetFullPath(modelDir), model.RootDirectory);
    }

    [Fact]
    public void PathTraversal_IsRejected()
    {
        var (json, _) = CreateModel("Evil", "../../outside.png", createTexture: false);

        Assert.False(Live2DModelCore.TryValidate(json, out _, out var error));
        Assert.Equal("L2D_MODEL_PATH_TRAVERSAL", error);
    }

    [Fact]
    public void AbsolutePath_IsRejected()
    {
        var (json, _) = CreateModel("Evil", @"C:\Windows\System32\drivers\etc\hosts.png", createTexture: false);

        Assert.False(Live2DModelCore.TryValidate(json, out _, out var error));
        Assert.Equal("L2D_MODEL_PATH_TRAVERSAL", error);
    }

    [Fact]
    public void MissingTexture_IsRejected()
    {
        var (json, _) = CreateModel("Broken", "textures/missing.png", createTexture: false);

        Assert.False(Live2DModelCore.TryValidate(json, out _, out var error));
        Assert.Equal("L2D_MODEL_INVALID", error);
    }

    [Fact]
    public void DangerousFileInFolder_IsRejected()
    {
        var (json, modelDir) = CreateModel("Evil", "textures/t0.png");
        File.WriteAllText(Path.Combine(modelDir, "payload.exe"), "MZ");

        Assert.False(Live2DModelCore.TryValidate(json, out _, out var error));
        Assert.Equal("L2D_MODEL_UNSAFE_FILE", error);
    }

    [Fact]
    public void MissingFile_ReturnsNotFound()
    {
        Assert.False(Live2DModelCore.TryValidate(Path.Combine(_root, "nope.model3.json"), out _, out var error));
        Assert.Equal("L2D_MODEL_NOT_FOUND", error);
    }

    [Fact]
    public void NonModelJson_IsRejected()
    {
        var path = Path.Combine(_root, "random.json");
        File.WriteAllText(path, "{}");

        Assert.False(Live2DModelCore.TryValidate(path, out _, out var error));
        Assert.Equal("L2D_MODEL_INVALID", error);
    }

    // ---------------------------------------------------------------- 布局

    [Fact]
    public void Layout_FullBody_IsCentered()
    {
        var tf = Live2DModelCore.ComputeTransform("full", 1920, 1080);

        Assert.Equal("OBS_BOUNDS_SCALE_INNER", tf.BoundsType);
        Assert.Equal(883.2, tf.Width, 1);
        Assert.Equal(928.8, tf.Height, 1);
        Assert.Equal((1920 - tf.Width) / 2, tf.PosX, 1);
        Assert.Equal((1080 - tf.Height) / 2, tf.PosY, 1);
    }

    [Fact]
    public void Layout_Right_StaysInsideCanvas()
    {
        var tf = Live2DModelCore.ComputeTransform("right", 1920, 1080);

        Assert.True(tf.PosX >= 0 && tf.PosX + tf.Width <= 1920 + 1, $"越界：{tf}");
        Assert.True(tf.PosY >= 0 && tf.PosY + tf.Height <= 1080 + 1, $"越界：{tf}");
    }

    [Fact]
    public void Layout_Portrait_UsesPortraitCanvas()
    {
        var tf = Live2DModelCore.ComputeTransform("half", 1080, 1920);

        Assert.True(tf.Width < 1080);
        Assert.True(tf.PosX >= 0 && tf.PosX + tf.Width <= 1080 + 1);
        Assert.True(tf.PosY + tf.Height <= 1920 + 1);
    }

    [Fact]
    public void Layout_UnknownId_FallsBackToHalf()
    {
        Assert.Equal("half", Live2DModelCore.ResolveLayout("no-such-layout").Id);
    }

    [Fact]
    public void ShortHash_And_Slug_AreSafe()
    {
        Assert.Equal("abcdef12", Live2DModelCore.ShortHash("abcdef123456"));
        Assert.Equal("00000000", Live2DModelCore.ShortHash(""));
        Assert.Equal("Hiyori-v3", Live2DModelCore.SafeSlug("Hiyori v3"));
        Assert.Equal("model", Live2DModelCore.SafeSlug("///"));
    }

    // ---------------------------------------------------------------- helpers

    private (string JsonPath, string ModelDir) CreateModel(string name, string textureRel, bool createTexture = true)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(dir, "textures"));

        if (createTexture)
        {
            var tex = Path.Combine(dir, textureRel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(tex)!);
            // 1x1 PNG（真实文件，SHA-256 需要能读）
            File.WriteAllBytes(tex, Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="));
        }

        File.WriteAllBytes(Path.Combine(dir, "Hiyori.moc3"), Encoding.UTF8.GetBytes("moc3"));

        var json = new
        {
            Version = 3,
            FileReferences = new
            {
                Moc = "Hiyori.moc3",
                Textures = new[] { textureRel }
            }
        };
        var path = Path.Combine(dir, "Hiyori.model3.json");
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(json));
        return (path, dir);
    }
}
