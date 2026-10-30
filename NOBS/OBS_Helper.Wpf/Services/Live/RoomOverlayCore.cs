using System.Text.Json.Nodes;
using OBS_Helper.Wpf.Models.ObsConfig;

namespace OBS_Helper.Wpf.Services.Live;

/// <summary>落地后检查清单的一项（V3.3.0）。</summary>
/// <param name="Role">角色（avatar / chat / audio …），空串表示通用项。</param>
/// <param name="TitleKey">文案键（标题）。</param>
/// <param name="HintKey">文案键（说明 / 下一步）。</param>
/// <param name="Required">是否必须完成才算「可开播」。</param>
public sealed record RoomChecklistItem(string Role, string TitleKey, string HintKey, bool Required);

/// <summary>房间模板校验问题（V3.3.0）。Code 是稳定的机器可读标识，文案由界面层映射。</summary>
public sealed record RoomValidationIssue(string Code, string Detail);

/// <summary>
/// 虚拟直播间的纯逻辑（V3.3.0 / P0-2）：overlay 页面安全、URL 拼接、浏览器源设置、检查清单、模板校验。
///
/// 为什么单独成文件：这些规则一旦写错，表现是「模板能落地但聊天框是空白」或「本地 HTTP 服务被当成
/// 目录穿越入口」。它们不依赖 WPF，因此可以被单测直接钉住。
/// </summary>
public static class RoomOverlayCore
{
    /// <summary>内置页面名（不含查询参数）。新增页面必须同时补 <c>RoomOverlayAssets</c>。</summary>
    public static readonly string[] KnownPages =
    {
        "chat.html", "frame.html", "brb.html", "ending.html", "timer.html"
    };

    /// <summary>overlay 路径前缀长度上限（token + 页面名），防止异常长的请求把日志刷爆。</summary>
    public const int MaxUrlLength = 512;

    /// <summary>ASCII 字母 / 数字（net6.0 没有 char.IsAsciiLetterOrDigit，双目标构建必须自己写）。</summary>
    private static bool IsAsciiAlnum(char c)
        => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9';

    /// <summary>生成一次性路径 token（只含 URL 安全字符）。</summary>
    public static string NewToken()
        => Guid.NewGuid().ToString("N");

    /// <summary>
    /// 页面名是否安全：必须是内置页面之一，可带查询参数；拒绝绝对路径 / .. / 反斜杠 / 协议头。
    /// </summary>
    public static bool IsKnownPage(string? page)
    {
        var name = PageNameOf(page);
        if (string.IsNullOrEmpty(name)) return false;
        return KnownPages.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>"frame.html?theme=cream" → "frame.html"；非法输入返回空串。</summary>
    public static string PageNameOf(string? page)
    {
        if (string.IsNullOrWhiteSpace(page)) return "";
        var raw = page.Trim();
        if (raw.Length > MaxUrlLength) return "";

        var q = raw.IndexOf('?');
        var name = (q >= 0 ? raw[..q] : raw).Trim();
        if (name.Length == 0) return "";

        // 只接受「文件名.html」，且只允许字母 / 数字 / 点 / 连字符 / 下划线
        if (name.Contains("..", StringComparison.Ordinal)) return "";
        if (name.Contains('/') || name.Contains('\\') || name.Contains(':')) return "";
        if (!name.EndsWith(".html", StringComparison.OrdinalIgnoreCase)) return "";

        foreach (var ch in name)
        {
            var ok = IsAsciiAlnum(ch) || ch is '.' or '-' or '_';
            if (!ok) return "";
        }
        return name;
    }

    /// <summary>查询串是否安全（只允许字母数字与少量分隔符，拒绝引号 / 尖括号 / 反斜杠）。</summary>
    public static bool IsSafeQuery(string? query)
    {
        if (string.IsNullOrEmpty(query)) return true;
        if (query.Length > 200) return false;
        foreach (var ch in query)
        {
            var ok = IsAsciiAlnum(ch) || ch is '-' or '_' or '.' or '=' or '&' or '%' or '+';
            if (!ok) return false;
        }
        return true;
    }

    /// <summary>
    /// 拼出 overlay 的访问地址：<c>http://127.0.0.1:port/&lt;token&gt;/&lt;page&gt;[?query][&amp;lang=xx]</c>。
    /// <paramref name="baseUrl"/> 必须来自本机回环服务；不合法时返回 null（调用方回退 about:blank）。
    /// </summary>
    public static string? BuildUrl(string? baseUrl, string? token, string? page, string? language = null)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(token)) return null;
        if (!IsKnownPage(page)) return null;

        var raw = page!.Trim();
        var q = raw.IndexOf('?');
        var name = q >= 0 ? raw[..q] : raw;
        var query = q >= 0 ? raw[(q + 1)..] : "";
        if (!IsSafeQuery(query)) return null;

        var sb = new System.Text.StringBuilder(baseUrl!.TrimEnd('/'));
        sb.Append('/').Append(token!.Trim()).Append('/').Append(name);
        sb.Append('?');
        if (query.Length > 0) sb.Append(query).Append('&');
        sb.Append("lang=").Append(string.Equals(language, "en-US", StringComparison.OrdinalIgnoreCase) ? "en" : "zh");
        var url = sb.ToString();
        return url.Length <= MaxUrlLength ? url : null;
    }

    /// <summary>把模板里的 <see cref="BrowserSpec"/> 变成 obs-websocket <c>browser_source</c> 的 inputSettings。</summary>
    public static JsonObject BuildBrowserSettings(BrowserSpec browser, string url)
    {
        return new JsonObject
        {
            ["url"] = url,
            ["width"] = Math.Clamp(browser.Width, 64, 7680),
            ["height"] = Math.Clamp(browser.Height, 64, 4320),
            ["css"] = browser.Css ?? "",
            ["reroute_audio"] = browser.RerouteAudio,
            // overlay 页面是静态本地文件，不需要 OBS 的「关闭时关闭源」之类额外选项
            ["shutdown"] = false,
            ["restart_when_active"] = false
        };
    }

    /// <summary>
    /// 落地后的检查清单（V3.3.0）：只列模板真正需要的项，不堆一串「你还没配的十件事」。
    /// 顺序固定：形象 → 聊天 → 音频 → 文本 → 装饰 → 通用（热键 / 体检）。
    /// </summary>
    public static IReadOnlyList<RoomChecklistItem> BuildChecklist(SceneTemplate? template)
    {
        if (template is null) return Array.Empty<RoomChecklistItem>();

        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var scene in template.Scenes)
        foreach (var src in scene.Sources)
            if (!string.IsNullOrWhiteSpace(src.Role)) roles.Add(src.Role);

        var items = new List<RoomChecklistItem>();
        foreach (var role in new[] { "avatar", "chat", "audio", "caption", "alert", "frame", "bgm" })
        {
            if (!roles.Contains(role)) continue;
            items.Add(new RoomChecklistItem(
                role,
                $"room.check.{role}.title",
                $"room.check.{role}.hint",
                Required: role is "avatar" or "audio"));
        }

        items.Add(new RoomChecklistItem("hotkey", "room.check.hotkey.title", "room.check.hotkey.hint", false));
        items.Add(new RoomChecklistItem("preflight", "room.check.preflight.title", "room.check.preflight.hint", false));
        return items;
    }

    /// <summary>
    /// 模板结构校验（V3.3.0）：只报「会导致落地后不可用」的问题，不做风格审查。
    /// 返回空列表表示通过。
    /// </summary>
    public static IReadOnlyList<RoomValidationIssue> Validate(SceneTemplate? template)
    {
        var issues = new List<RoomValidationIssue>();
        if (template is null)
        {
            issues.Add(new RoomValidationIssue("ROOM_TEMPLATE_NULL", "template is null"));
            return issues;
        }

        if (template.Scenes.Count == 0)
            issues.Add(new RoomValidationIssue("ROOM_NO_SCENES", template.Id));

        var sceneNames = new HashSet<string>(StringComparer.Ordinal);
        var hasAvatar = false;

        foreach (var scene in template.Scenes)
        {
            if (string.IsNullOrWhiteSpace(scene.Name))
                issues.Add(new RoomValidationIssue("ROOM_EMPTY_SCENE_NAME", template.Id));
            else if (!sceneNames.Add(scene.Name))
                issues.Add(new RoomValidationIssue("ROOM_DUPLICATE_SCENE", scene.Name));

            foreach (var src in scene.Sources)
            {
                if (string.IsNullOrWhiteSpace(src.Name))
                    issues.Add(new RoomValidationIssue("ROOM_EMPTY_SOURCE_NAME", scene.Name));

                if (string.Equals(src.Role, "avatar", StringComparison.OrdinalIgnoreCase)) hasAvatar = true;

                if (src.Browser is not null && !IsKnownPage(src.Browser.Page))
                    issues.Add(new RoomValidationIssue("ROOM_BAD_PAGE", $"{src.Name}:{src.Browser.Page}"));

                if (src.Transform is { } tr)
                {
                    var w = template.Canvas.BaseWidth;
                    var h = template.Canvas.BaseHeight;
                    // 允许故意放到画布外（做飞入动画），但超出 2 倍画布基本是写错了数字
                    if (tr.PosX is { } x && (x < -2 * w || x > 2 * w))
                        issues.Add(new RoomValidationIssue("ROOM_TRANSFORM_OUT_OF_CANVAS", $"{src.Name}:posX={x}"));
                    if (tr.PosY is { } y && (y < -2 * h || y > 2 * h))
                        issues.Add(new RoomValidationIssue("ROOM_TRANSFORM_OUT_OF_CANVAS", $"{src.Name}:posY={y}"));
                }
            }
        }

        if (template.Tags.Contains("vtuber", StringComparer.OrdinalIgnoreCase)
            && !template.Tags.Contains("pack", StringComparer.OrdinalIgnoreCase)
            && !hasAvatar)
            issues.Add(new RoomValidationIssue("ROOM_NO_AVATAR", template.Id));

        return issues;
    }

    /// <summary>按标签筛选（大小写不敏感）；标签为空时返回全部。</summary>
    public static IReadOnlyList<SceneTemplate> FilterByTag(IEnumerable<SceneTemplate> templates, string? tag)
    {
        var list = templates.ToList();
        if (string.IsNullOrWhiteSpace(tag)) return list;
        return list
            .Where(t => t.Tags.Any(x => string.Equals(x, tag, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    /// <summary>判断模板是否属于「虚拟主播」工作流。</summary>
    public static bool IsVtuber(SceneTemplate? template)
        => template?.Tags.Any(t => string.Equals(t, "vtuber", StringComparison.OrdinalIgnoreCase)) == true;
}
