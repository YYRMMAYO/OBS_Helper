using Xunit;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// X1031：新应用图标必须包含 Windows Shell 会取的全部尺寸。
/// 缺一档就会在任务栏 / 资源管理器 / 安装包里退回默认图标。
/// </summary>
public class XAppIconTests
{
    private static readonly int[] RequiredIconSizes = [16, 24, 32, 48, 64, 128, 256];

    [Fact]
    public void XAppIconContainsAllShellSizes()
    {
        var path = Path.Combine(SourceRoot(), "OBS_Helper.Wpf", "Assets", "appicon_x.ico");
        Assert.True(File.Exists(path), $"X 应用图标缺失：{path}");

        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length > 1024, $"图标文件过小（{bytes.Length} 字节）");

        static int U16(byte[] b, int offset) => b[offset] | (b[offset + 1] << 8);

        Assert.Equal(0, U16(bytes, 0));   // reserved
        Assert.Equal(1, U16(bytes, 2));   // type = icon
        var count = U16(bytes, 4);
        Assert.True(count >= RequiredIconSizes.Length, $"只包含 {count} 档尺寸");

        var sizes = new List<int>();
        for (var i = 0; i < count; i++)
        {
            var offset = 6 + i * 16;
            var w = bytes[offset] == 0 ? 256 : bytes[offset];
            var h = bytes[offset + 1] == 0 ? 256 : bytes[offset + 1];
            Assert.Equal(w, h);
            sizes.Add(w);
        }

        foreach (var size in RequiredIconSizes)
            Assert.Contains(size, sizes);
    }

    private static string SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "OBS_Helper.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("找不到源码根目录（OBS_Helper.slnx）");
    }
}
