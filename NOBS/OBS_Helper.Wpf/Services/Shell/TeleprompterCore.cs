using System.Globalization;
using System.Text;
using OBS_Helper.Wpf.Models.Shell;

namespace OBS_Helper.Wpf.Services.Shell;

/// <summary>提词器里一个可被场景联动的段落。</summary>
public sealed record TeleprompterSection(string Marker, int StartIndex, int Length);

/// <summary>
/// 提词器纯逻辑核心（V3.1.0 / §12.1.3）。
///
/// 只做能单测的决策：数值 clamp、滚动步进、镜像、脚本分段与场景匹配、文本归一化。
/// 窗口、滚动、穿透、捕获排除在 <see cref="TeleprompterService"/> / <c>TeleprompterWindow</c>。
/// </summary>
public static class TeleprompterCore
{
    public const double MinSpeed = 10;
    public const double MaxSpeed = 200;
    public const double DefaultSpeed = 40;

    public const double MinFontSize = 14;
    public const double MaxFontSize = 120;
    public const double DefaultFontSize = 42;

    public const double MinLineHeight = 1.0;
    public const double MaxLineHeight = 2.5;
    public const double DefaultLineHeight = 1.45;

    public const double MinSideMargin = 0;
    public const double MaxSideMargin = 400;
    public const double DefaultSideMargin = 80;

    public const double MinBackgroundOpacity = 0.0;
    public const double MaxBackgroundOpacity = 1.0;

    /// <summary>脚本长度上限（字符）：防止误导入巨型文件把窗口卡死。</summary>
    public const int MaxScriptChars = 200_000;

    /// <summary>默认内容（首次打开时给一段可照做的使用说明）。</summary>
    public const string DefaultScript =
        "## 使用说明\n" +
        "这是提词器。空格暂停 / 继续，Home 回到开头，方向键微调。\n" +
        "工具栏可以调速度、字号、镜像与点击穿透；设置会自动保存。\n" +
        "## 场景联动\n" +
        "在脚本里用 \"## 场景名\" 分段，切到该场景时会自动跳到对应段落（只滚动，不切场景）。\n";

    /// <summary>把设置里的脏值收敛到可用范围（就地修改）。</summary>
    public static TeleprompterSettings Normalize(TeleprompterSettings? settings)
    {
        var s = settings ?? new TeleprompterSettings();

        s.FontSize = ClampOr(s.FontSize, MinFontSize, MaxFontSize, DefaultFontSize);
        s.LineHeight = ClampOr(s.LineHeight, MinLineHeight, MaxLineHeight, DefaultLineHeight);
        s.Speed = ClampOr(s.Speed, MinSpeed, MaxSpeed, DefaultSpeed);
        s.SideMargin = ClampOr(s.SideMargin, MinSideMargin, MaxSideMargin, DefaultSideMargin);
        s.BackgroundOpacity = ClampOr(s.BackgroundOpacity, MinBackgroundOpacity, MaxBackgroundOpacity, 0.72);
        s.TextColor = NormalizeColor(s.TextColor, "#ffffff");
        s.Background = NormalizeColor(s.Background, "#000000");
        s.Monitor = s.Monitor is >= 0 and <= 15 ? s.Monitor : -1;

        if (double.IsNaN(s.Width) || s.Width < 320) s.Width = 900;
        if (double.IsNaN(s.Height) || s.Height < 160) s.Height = 320;
        if (double.IsNaN(s.Left)) s.Left = double.NaN;
        if (double.IsNaN(s.Top)) s.Top = double.NaN;
        return s;
    }

    private static double ClampOr(double value, double min, double max, double fallback)
        => double.IsNaN(value) || double.IsInfinity(value) ? fallback : Math.Clamp(value, min, max);

    /// <summary>只接受 #rgb / #rrggbb；非法值回退默认（不让脏配置把文字变成透明）。</summary>
    public static string NormalizeColor(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var v = value.Trim();
        if (v.Length is 4 or 7 && v[0] == '#')
        {
            for (var i = 1; i < v.Length; i++)
            {
                if (!Uri.IsHexDigit(v[i])) return fallback;
            }
            return v.ToLowerInvariant();
        }
        return fallback;
    }

    /// <summary>滚动步进（像素）：速度是 px/s，乘以经过的秒数。</summary>
    public static double ScrollStep(double speed, double seconds)
    {
        if (double.IsNaN(seconds) || seconds <= 0) return 0;
        var pxPerSecond = ClampOr(speed, MinSpeed, MaxSpeed, DefaultSpeed);
        return pxPerSecond * Math.Min(seconds, 1.0);   // 单帧最多按 1 秒算，避免卡顿后突然跳很远
    }

    /// <summary>推进滚动偏移并夹在 [0, max]。</summary>
    public static double Advance(double offset, double maxOffset, double speed, double seconds, bool paused)
    {
        if (paused) return ClampOffset(offset, maxOffset);
        return ClampOffset(offset + ScrollStep(speed, seconds), maxOffset);
    }

    /// <summary>把偏移夹到合法范围；最大偏移未知（0）时按 0 处理。</summary>
    public static double ClampOffset(double offset, double maxOffset)
    {
        if (double.IsNaN(offset) || offset < 0) offset = 0;
        if (double.IsNaN(maxOffset) || maxOffset < 0) maxOffset = 0;
        return Math.Min(offset, maxOffset);
    }

    /// <summary>镜像变换的缩放系数（-1 表示翻转）。</summary>
    public static (double ScaleX, double ScaleY) MirrorScale(bool horizontal, bool vertical)
        => (horizontal ? -1 : 1, vertical ? -1 : 1);

    /// <summary>归一化脚本：统一换行、去掉 BOM、限制长度；空内容回退默认说明。</summary>
    public static string NormalizeScript(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return DefaultScript;
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n').TrimStart('\uFEFF');
        if (normalized.Length > MaxScriptChars)
            normalized = normalized[..MaxScriptChars];
        return normalized;
    }

    /// <summary>
    /// 解析场景分段：支持 <c>## 场景名</c> / <c># 场景名</c> / <c>[场景名]</c> 三种写法。
    /// 返回的 <see cref="TeleprompterSection.StartIndex"/> 指向该行行首（字符偏移）。
    /// </summary>
    public static IReadOnlyList<TeleprompterSection> ParseSections(string? text)
    {
        var sections = new List<TeleprompterSection>();
        if (string.IsNullOrEmpty(text)) return sections;

        var offset = 0;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd();
            var marker = ExtractMarker(line);
            if (marker.Length > 0)
                sections.Add(new TeleprompterSection(marker, offset, line.Length));
            offset += rawLine.Length + 1;
        }
        return sections;
    }

    private static string ExtractMarker(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("## ", StringComparison.Ordinal)) return trimmed[3..].Trim();
        if (trimmed.StartsWith("# ", StringComparison.Ordinal)) return trimmed[2..].Trim();
        if (trimmed.Length >= 3 && trimmed[0] == '[' && trimmed[^1] == ']')
            return trimmed[1..^1].Trim();
        return "";
    }

    /// <summary>
    /// 找到场景对应的段落行号（0 基）；没有匹配返回 -1。
    /// 先精确匹配（忽略大小写与首尾空白），再尝试「场景名包含标记」的宽松匹配。
    /// </summary>
    public static int FindSectionForScene(IReadOnlyList<TeleprompterSection> sections, string? sceneName)
    {
        if (sections.Count == 0 || string.IsNullOrWhiteSpace(sceneName)) return -1;
        var target = sceneName.Trim();

        for (var i = 0; i < sections.Count; i++)
        {
            if (string.Equals(sections[i].Marker, target, StringComparison.OrdinalIgnoreCase)) return i;
        }
        for (var i = 0; i < sections.Count; i++)
        {
            var marker = sections[i].Marker;
            if (marker.Length == 0) continue;
            if (target.Contains(marker, StringComparison.OrdinalIgnoreCase)
                || marker.Contains(target, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    /// <summary>场景改名的提示判定：上次绑定的场景名已不在 OBS 场景列表里。</summary>
    public static bool SceneBindingMissing(string? boundScene, IReadOnlyCollection<string> sceneNames)
    {
        if (string.IsNullOrWhiteSpace(boundScene)) return false;
        if (sceneNames.Count == 0) return false;
        foreach (var name in sceneNames)
        {
            if (string.Equals(name, boundScene, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    /// <summary>把显示器索引收敛到实际数量；-1 表示「当前显示器」，越界回退主屏。</summary>
    public static int NormalizeMonitor(int index, int screenCount)
    {
        if (screenCount <= 0) return -1;
        if (index < 0 || index >= screenCount) return 0;
        return index;
    }

    /// <summary>
    /// 计算窗口在指定显示器上的初始位置（居中、留出边距）。
    /// 输入为工作区矩形与窗口尺寸，输出左上角坐标；纯数值，便于单测。
    /// </summary>
    public static (double Left, double Top) CenterOn(
        double workLeft, double workTop, double workWidth, double workHeight,
        double windowWidth, double windowHeight)
        => (workLeft + Math.Max(0, (workWidth - windowWidth) / 2),
            workTop + Math.Max(0, (workHeight - windowHeight) / 2));

    /// <summary>倒计时文案序列（秒），供窗口逐步显示；0 表示不倒数。</summary>
    public static IReadOnlyList<int> CountdownSteps(int seconds)
    {
        if (seconds <= 0) return Array.Empty<int>();
        var list = new List<int>(seconds);
        for (var i = seconds; i > 0; i--) list.Add(i);
        return list;
    }

    /// <summary>显示用的简短速度描述（避免界面上出现 39.999999）。</summary>
    public static string FormatSpeed(double speed)
        => Math.Round(ClampOr(speed, MinSpeed, MaxSpeed, DefaultSpeed)).ToString(CultureInfo.InvariantCulture);

    /// <summary>行高换算成像素（字号 × 行高倍数）。</summary>
    public static double LineHeightPixels(double fontSize, double lineHeight)
        => Math.Round(ClampOr(fontSize, MinFontSize, MaxFontSize, DefaultFontSize)
                      * ClampOr(lineHeight, MinLineHeight, MaxLineHeight, DefaultLineHeight), 1);
}
