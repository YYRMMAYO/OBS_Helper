using OBS_Helper.Wpf.Localization;
namespace OBS_Helper.Wpf.Services.Obs;

/// <summary>色彩体检单项结论（纯数据，供单元测试与界面复用）。</summary>
public sealed class ColorCheckItem
{
    /// <summary>"ok" / "warn" / "info"。</summary>
    public string Status { get; init; } = "ok";
    public required string Title { get; init; }
    public required string Detail { get; init; }
    /// <summary>命中问题时关联的知识库条目 id。</summary>
    public string? ProblemId { get; init; }
}

/// <summary>
/// 色彩设置体检核心（只读，纯逻辑，供单元测试）。
///
/// 检查当前 Profile basic.ini 里的色彩三件套：
/// - 色彩范围（Partial/Limited 为安全值；Full 在多数观众端会发灰泛白）；
/// - 色彩空间（Rec.709 为 SDR 直播安全值；Rec.2100/PQ 仅 HDR 全链路时使用）；
/// - 色彩格式（NV12 兼容性最好；仅 HDR 用 P010）。
///
/// 键缺失一律按 OBS 默认（NV12 · Rec.709 · Limited）处理，不制造虚假告警。
/// </summary>
public static class ColorCheckCore
{
    /// <summary>从已解析的 basic.ini 键值里评估色彩设置。ini 可为空字典。</summary>
    public static List<ColorCheckItem> Evaluate(IReadOnlyDictionary<string, string>? ini)
    {
        var items = new List<ColorCheckItem>();
        var dict = ini ?? new Dictionary<string, string>();

        // ---- 色彩范围 ----
        var range = FirstValue(dict, "advout.colorrange", "simpleoutput.colorrange");
        if (range.Length == 0)
        {
            items.Add(new ColorCheckItem
            {
                Status = "ok",
                Title = Strings.T("colorcheck.range.title"),
                Detail = Strings.T("colorcheck.range.default")
            });
        }
        else if (range.Equals("partial", StringComparison.OrdinalIgnoreCase) ||
                 range.Contains("limited", StringComparison.OrdinalIgnoreCase))
        {
            items.Add(new ColorCheckItem
            {
                Status = "ok",
                Title = Strings.T("colorcheck.range.title"),
                Detail = Strings.T("colorcheck.range.ok", range)
            });
        }
        else if (range.Equals("full", StringComparison.OrdinalIgnoreCase) ||
                 range.Contains("full", StringComparison.OrdinalIgnoreCase))
        {
            items.Add(new ColorCheckItem
            {
                Status = "warn",
                Title = Strings.T("colorcheck.range.fullTitle"),
                Detail = Strings.T("colorcheck.range.fullDetail"),
                ProblemId = "cf-colorrange"
            });
        }
        else
        {
            items.Add(new ColorCheckItem
            {
                Status = "info",
                Title = Strings.T("colorcheck.range.title"),
                Detail = Strings.T("colorcheck.range.unknown", range)
            });
        }

        // ---- 色彩空间 ----
        var space = FirstValue(dict, "advout.colorspace", "simpleoutput.colorspace");
        if (space.Length == 0)
        {
            items.Add(new ColorCheckItem
            {
                Status = "ok",
                Title = Strings.T("colorcheck.space.title"),
                Detail = Strings.T("colorcheck.space.default")
            });
        }
        else if (space.StartsWith("709", StringComparison.OrdinalIgnoreCase) ||
                 space.Contains("709", StringComparison.OrdinalIgnoreCase))
        {
            items.Add(new ColorCheckItem
            {
                Status = "ok",
                Title = Strings.T("colorcheck.space.title"),
                Detail = Strings.T("colorcheck.space.ok", space)
            });
        }
        else if (space.Contains("2100", StringComparison.OrdinalIgnoreCase) ||
                 space.Contains("pq", StringComparison.OrdinalIgnoreCase) ||
                 space.Contains("hlg", StringComparison.OrdinalIgnoreCase))
        {
            items.Add(new ColorCheckItem
            {
                Status = "info",
                Title = Strings.T("colorcheck.space.hdrTitle"),
                Detail = Strings.T("colorcheck.space.hdrDetail")
            });
        }
        else
        {
            items.Add(new ColorCheckItem
            {
                Status = "warn",
                Title = Strings.T("colorcheck.space.unknownTitle"),
                Detail = Strings.T("colorcheck.space.unknownDetail", space),
                ProblemId = "cf-colorspace"
            });
        }

        // ---- 色彩格式 ----
        var format = FirstValue(dict, "advout.colorformat", "simpleoutput.colorformat");
        if (format.Length == 0)
        {
            items.Add(new ColorCheckItem
            {
                Status = "ok",
                Title = Strings.T("colorcheck.format.title"),
                Detail = Strings.T("colorcheck.format.default")
            });
        }
        else if (format.Contains("nv12", StringComparison.OrdinalIgnoreCase))
        {
            items.Add(new ColorCheckItem
            {
                Status = "ok",
                Title = Strings.T("colorcheck.format.title"),
                Detail = Strings.T("colorcheck.format.ok")
            });
        }
        else if (format.Contains("p010", StringComparison.OrdinalIgnoreCase) ||
                 format.Contains("i010", StringComparison.OrdinalIgnoreCase))
        {
            items.Add(new ColorCheckItem
            {
                Status = "info",
                Title = Strings.T("colorcheck.format.tenBitTitle"),
                Detail = Strings.T("colorcheck.format.tenBitDetail")
            });
        }
        else if (format.Contains("argb", StringComparison.OrdinalIgnoreCase) ||
                 format.Contains("rgba", StringComparison.OrdinalIgnoreCase) ||
                 format.Contains("bgra", StringComparison.OrdinalIgnoreCase))
        {
            items.Add(new ColorCheckItem
            {
                Status = "warn",
                Title = Strings.T("colorcheck.format.rgbTitle"),
                Detail = Strings.T("colorcheck.format.rgbDetail"),
                ProblemId = "cf-colorspace"
            });
        }
        else
        {
            items.Add(new ColorCheckItem
            {
                Status = "info",
                Title = Strings.T("colorcheck.format.title"),
                Detail = Strings.T("colorcheck.format.unknown", format)
            });
        }

        return items;
    }

    private static string FirstValue(IReadOnlyDictionary<string, string> ini, params string[] keys)
        => keys.Select(k => ini.TryGetValue(k, out var v) ? v : "").FirstOrDefault(v => v.Length > 0) ?? "";
}