using OBS_Helper.Wpf.Services.Recording;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// 录制档案纯逻辑（V3.0 / D3）的回归测试。
///
/// 重点锁三类容易出错、且**用户直接看得见**的东西：
/// ① 健康度判定（「哪次是空文件」是这个功能存在的理由）；
/// ② 重命名后的文件名（非法字符会让重命名直接失败）；
/// ③ 章节文件（ffmetadata 的转义与时间轴错了，转码会静默丢章节）。
/// </summary>
public class RecordingArchiveCoreTests
{
    private static RecordingArchiveEntry Entry(
        string path = @"D:\录像\2026-10-05 22-30-00.mkv",
        long bytes = 500L * 1024 * 1024,
        TimeSpan? duration = null,
        IReadOnlyList<RecordingMarker>? markers = null)
        => new(
            Path: path,
            StartedLocal: new DateTime(2026, 10, 5, 22, 30, 0),
            Duration: duration ?? TimeSpan.FromMinutes(12),
            Bytes: bytes,
            DroppedRatio: 0,
            Segmented: false,
            Markers: markers ?? Array.Empty<RecordingMarker>());

    // ---------------------------------------------------------------- 健康度

    [Theory]
    [InlineData(true, 0L, 0, RecordingHealth.Empty)]                 // 0 字节
    [InlineData(true, 10_000L, 3, RecordingHealth.Tiny)]            // 又小又短
    [InlineData(true, 10_000L, 60, RecordingHealth.Ok)]             // 小但录了很久（低码率/静态画面）
    [InlineData(true, 500_000_000L, 720, RecordingHealth.Ok)]
    [InlineData(false, 500_000_000L, 720, RecordingHealth.Missing)]
    public void EvaluateHealth_CoversAllCases(bool exists, long bytes, int seconds, RecordingHealth expected)
        => Assert.Equal(expected, RecordingArchiveCore.EvaluateHealth(exists, bytes, TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Entry_Health_UsesFileExistence()
    {
        Assert.Equal(RecordingHealth.Ok, Entry().Health(exists: true));
        Assert.Equal(RecordingHealth.Missing, Entry().Health(exists: false));
    }

    // ---------------------------------------------------------------- 打点

    [Fact]
    public void AddMarker_KeepsSortedAndDedupesWithinTwoSeconds()
    {
        var list = RecordingArchiveCore.AddMarker(null, TimeSpan.FromSeconds(30), "A");
        list = RecordingArchiveCore.AddMarker(list, TimeSpan.FromSeconds(10), "B");   // 更早的点
        list = RecordingArchiveCore.AddMarker(list, TimeSpan.FromSeconds(30.5), "重复");   // 2 秒内 → 合并

        Assert.Equal(2, list.Count);
        Assert.Equal(TimeSpan.FromSeconds(10), list[0].At);   // 按位置排序
        Assert.Equal(TimeSpan.FromSeconds(30), list[1].At);
        Assert.Equal("A", list[1].Label);                     // 合并时保留原有标签
    }

    [Fact]
    public void AddMarker_ClampsNegativePosition()
    {
        var list = RecordingArchiveCore.AddMarker(null, TimeSpan.FromSeconds(-5));
        Assert.Equal(TimeSpan.Zero, Assert.Single(list).At);
    }

    /// <summary>打点位置相差超过 2 秒时必须各留一条（否则用户连点两次只想留两个点就丢了）。</summary>
    [Fact]
    public void AddMarker_KeepsMarkersFurtherApartThanTwoSeconds()
    {
        var list = RecordingArchiveCore.AddMarker(null, TimeSpan.FromSeconds(10));
        list = RecordingArchiveCore.AddMarker(list, TimeSpan.FromSeconds(12.1));
        Assert.Equal(2, list.Count);
    }

    // ---------------------------------------------------------------- 重命名

    [Fact]
    public void BuildNewFileName_UsesSelectedParts()
    {
        var rule = new RenameRule(Prefix: "我的频道", IncludeDate: true, IncludeTime: true,
            Scene: "游戏", PresetLabel: "game");
        var name = RecordingArchiveCore.BuildNewFileName(Entry(), rule);

        Assert.Equal("我的频道_20261005_223000_游戏_game.mkv", name);
    }

    [Fact]
    public void BuildNewFileName_DefaultsToOriginalNameWhenNothingSelected()
    {
        var name = RecordingArchiveCore.BuildNewFileName(Entry(), new RenameRule(
            Prefix: "", IncludeDate: false, IncludeTime: false, Scene: "", PresetLabel: ""));
        Assert.Equal("2026-10-05 22-30-00.mkv", name);
    }

    /// <summary>非法字符必须被替换：文件名里出现 <c>:</c> 会让重命名直接抛异常。</summary>
    [Theory]
    [InlineData("a:b", "a_b")]
    [InlineData("a/b\\c", "a_b_c")]
    [InlineData("  空格  ", "空格")]
    [InlineData("结尾点.", "结尾点")]
    [InlineData("A__B", "A__B")]          // 单次调用不做压缩（压缩发生在拼接时）
    public void Sanitize_RemovesIllegalCharacters(string raw, string expected)
        => Assert.Equal(expected, RecordingArchiveCore.Sanitize(raw));

    [Fact]
    public void BuildNewFileName_CollapsesRepeatedSeparators()
    {
        // 场景名带了下划线、前缀又以下划线结尾：不该出现「A___B」
        var rule = new RenameRule(Prefix: "频道_", IncludeDate: false, IncludeTime: false,
            Scene: "_游戏_", PresetLabel: "");
        var name = RecordingArchiveCore.BuildNewFileName(Entry(), rule);
        Assert.Equal("频道_游戏.mkv", name);
    }

    [Fact]
    public void BuildNewFileName_KeepsExtension()
    {
        var name = RecordingArchiveCore.BuildNewFileName(Entry(path: @"D:\a\b.mp4"), new RenameRule(Prefix: "X"));
        Assert.EndsWith(".mp4", name);
    }

    // ---------------------------------------------------------------- 章节

    [Fact]
    public void BuildFfMetadata_WritesChaptersWithCorrectTimebase()
    {
        var markers = new[]
        {
            new RecordingMarker(TimeSpan.FromSeconds(5), "开场"),
            new RecordingMarker(TimeSpan.FromSeconds(65), "第一次击杀"),
        };
        var text = RecordingArchiveCore.BuildFfMetadata(markers, TimeSpan.FromMinutes(3), "演示");

        Assert.Contains(";FFMETADATA1", text);
        Assert.Contains("title=演示", text);
        Assert.Contains("TIMEBASE=1/1000", text);
        Assert.Contains("START=5000", text);          // 章节起点
        Assert.Contains("END=65000", text);           // 下一章起点
        Assert.Contains("title=开场", text);
        Assert.Contains("title=第一次击杀", text);
        Assert.Contains("END=180000", text);          // 最后一章到录制结束
    }

    /// <summary>没有打点时**不生成**章节文件（免得用户以为「生成了但内容是空的」）。</summary>
    [Fact]
    public void BuildFfMetadata_WithoutMarkers_IsEmpty()
        => Assert.Equal("", RecordingArchiveCore.BuildFfMetadata(Array.Empty<RecordingMarker>(), TimeSpan.FromMinutes(1), "x"));

    [Fact]
    public void BuildFfMetadata_UsesDefaultLabelWhenUnnamed()
    {
        var markers = new[] { new RecordingMarker(TimeSpan.FromSeconds(1), "") };
        var text = RecordingArchiveCore.BuildFfMetadata(markers, TimeSpan.FromMinutes(1), "x");
        Assert.Contains("title=打点 1", text);
    }

    /// <summary>反斜杠与换行必须转义，否则整份 ffmetadata 解析失败。</summary>
    [Fact]
    public void BuildFfMetadata_EscapesBackslashAndNewline()
    {
        var markers = new[] { new RecordingMarker(TimeSpan.FromSeconds(1), "a\\b\nc") };
        var text = RecordingArchiveCore.BuildFfMetadata(markers, TimeSpan.FromMinutes(1), "t\\x");

        Assert.Contains("title=a\\\\b\\\nc", text);
        Assert.Contains("title=t\\\\x", text);
    }

    [Fact]
    public void BuildChapterList_IsPasteableIntoADescription()
    {
        var markers = new[]
        {
            new RecordingMarker(TimeSpan.Zero, "开场"),
            new RecordingMarker(TimeSpan.FromSeconds(372), "高能"),
        };
        var text = RecordingArchiveCore.BuildChapterList(markers, "标题");
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToArray();

        Assert.Equal("标题", lines[0]);
        Assert.Equal("00:00 开场", lines[1]);
        Assert.Equal("06:12 高能", lines[2]);
    }

    [Fact]
    public void ChapterPathFor_SitsNextToTheVideo()
    {
        var ff = RecordingArchiveCore.ChapterPathFor(@"D:\录像\a.mkv", ffmpegFormat: true);
        var txt = RecordingArchiveCore.ChapterPathFor(@"D:\录像\a.mkv", ffmpegFormat: false);

        Assert.Equal(@"D:\录像\a.ffmetadata", ff);
        Assert.Equal(@"D:\录像\a.chapters.txt", txt);
    }

    // ---------------------------------------------------------------- 展示

    [Fact]
    public void Describe_IncludesSizeDurationDropsAndMarkers()
    {
        var entry = Entry(bytes: 3L * 1024 * 1024 * 1024, duration: TimeSpan.FromMinutes(12)) with
        {
            DroppedRatio = 0.003,
            Segmented = true,
            Markers = new[] { new RecordingMarker(TimeSpan.FromSeconds(1), "a") },
        };
        var text = RecordingArchiveCore.Describe(entry, RecordingHealth.Ok);

        Assert.Contains("3", text);         // 3 GB
        Assert.Contains("12:00", text);
        Assert.Contains("丢帧", text);
        Assert.Contains("分段", text);
        Assert.Contains("1 个打点", text);
    }

    [Fact]
    public void Describe_AddsHealthWarningWhenSuspect()
    {
        var text = RecordingArchiveCore.Describe(Entry(bytes: 0), RecordingHealth.Empty);
        Assert.Contains("空文件", text);
    }

    [Theory]
    [InlineData(0, "0 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(5 * 1024 * 1024, "5 MB")]
    [InlineData(2L * 1024 * 1024 * 1024, "2 GB")]
    public void FormatSize_IsHumanReadable(long bytes, string expected)
        => Assert.Equal(expected, RecordingArchiveCore.FormatSize(bytes));
}
