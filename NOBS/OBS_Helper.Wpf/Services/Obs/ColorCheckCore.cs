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
                Title = "色彩范围",
                Detail = "未自定义（默认 Limited / 部分），与绝大多数平台和观众端匹配。"
            });
        }
        else if (range.Equals("partial", StringComparison.OrdinalIgnoreCase) ||
                 range.Contains("limited", StringComparison.OrdinalIgnoreCase))
        {
            items.Add(new ColorCheckItem
            {
                Status = "ok",
                Title = "色彩范围",
                Detail = $"当前 {range}，安全值。"
            });
        }
        else if (range.Equals("full", StringComparison.OrdinalIgnoreCase) ||
                 range.Contains("full", StringComparison.OrdinalIgnoreCase))
        {
            items.Add(new ColorCheckItem
            {
                Status = "warn",
                Title = "色彩范围：Full 可能导致画面发灰",
                Detail = "当前为 Full（完全）：本地播放正常，但多数直播平台按 Limited 解读，画面会发灰、对比度下降。" +
                         "\n建议：设置 → 高级 → 视频把色彩范围改回「Limited / 部分」，除非你明确知道全链路都是 Full。",
                ProblemId = "cf-colorrange"
            });
        }
        else
        {
            items.Add(new ColorCheckItem
            {
                Status = "info",
                Title = "色彩范围",
                Detail = $"读到非标准值「{range}」，建议在 设置 → 高级 → 视频 里核对一遍。"
            });
        }

        // ---- 色彩空间 ----
        var space = FirstValue(dict, "advout.colorspace", "simpleoutput.colorspace");
        if (space.Length == 0)
        {
            items.Add(new ColorCheckItem
            {
                Status = "ok",
                Title = "色彩空间",
                Detail = "未自定义（默认 Rec.709），SDR 直播 / 录制的安全值。"
            });
        }
        else if (space.StartsWith("709", StringComparison.OrdinalIgnoreCase) ||
                 space.Contains("709", StringComparison.OrdinalIgnoreCase))
        {
            items.Add(new ColorCheckItem
            {
                Status = "ok",
                Title = "色彩空间",
                Detail = $"当前 {space}，安全值。"
            });
        }
        else if (space.Contains("2100", StringComparison.OrdinalIgnoreCase) ||
                 space.Contains("pq", StringComparison.OrdinalIgnoreCase) ||
                 space.Contains("hlg", StringComparison.OrdinalIgnoreCase))
        {
            items.Add(new ColorCheckItem
            {
                Status = "info",
                Title = "色彩空间：HDR（Rec.2100）",
                Detail = "检测到 HDR 色彩空间：仅在采集、合成、编码到平台全链路都支持 HDR 时才有意义；" +
                         "SDR 平台观看会出现偏灰或过饱和。普通 SDR 直播请改回 Rec.709。"
            });
        }
        else
        {
            items.Add(new ColorCheckItem
            {
                Status = "warn",
                Title = "色彩空间非标准值",
                Detail = $"当前「{space}」不是 Rec.709：不同设备解读不一致会造成偏色。" +
                         "\n建议：设置 → 高级 → 视频改为 Rec.709。",
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
                Title = "色彩格式",
                Detail = "未自定义（默认 NV12），兼容性最好。"
            });
        }
        else if (format.Contains("nv12", StringComparison.OrdinalIgnoreCase))
        {
            items.Add(new ColorCheckItem
            {
                Status = "ok",
                Title = "色彩格式",
                Detail = "NV12，8-bit 标准值，所有平台均支持。"
            });
        }
        else if (format.Contains("p010", StringComparison.OrdinalIgnoreCase) ||
                 format.Contains("i010", StringComparison.OrdinalIgnoreCase))
        {
            items.Add(new ColorCheckItem
            {
                Status = "info",
                Title = "色彩格式：10-bit（P010）",
                Detail = "10-bit 仅在 HDR 输出场景有意义；SDR 直播用 NV12 即可，10-bit 还会增加编码负担。"
            });
        }
        else if (format.Contains("argb", StringComparison.OrdinalIgnoreCase) ||
                 format.Contains("rgba", StringComparison.OrdinalIgnoreCase) ||
                 format.Contains("bgra", StringComparison.OrdinalIgnoreCase))
        {
            items.Add(new ColorCheckItem
            {
                Status = "warn",
                Title = "色彩格式用了 RGB（ARGB/RGBA）",
                Detail = "RGB 格式部分编码器与平台不支持或需转换，可能带来性能损耗与兼容性问题。" +
                         "\n建议：改回 NV12（设置 → 高级 → 视频）。",
                ProblemId = "cf-colorspace"
            });
        }
        else
        {
            items.Add(new ColorCheckItem
            {
                Status = "info",
                Title = "色彩格式",
                Detail = $"读到非标准值「{format}」，建议核对 设置 → 高级 → 视频。"
            });
        }

        return items;
    }

    private static string FirstValue(IReadOnlyDictionary<string, string> ini, params string[] keys)
        => keys.Select(k => ini.TryGetValue(k, out var v) ? v : "").FirstOrDefault(v => v.Length > 0) ?? "";
}
