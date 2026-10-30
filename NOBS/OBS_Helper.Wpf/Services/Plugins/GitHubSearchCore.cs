using System.Text.Json;
using OBS_Helper.Wpf.Models.Plugins;

namespace OBS_Helper.Wpf.Services.Plugins;

/// <summary>
/// GitHub 搜索 / Release 响应的纯逻辑解析（V3.3.0 / P0-5）。
///
/// 只解析界面需要的字段，不保留整个响应；解析失败返回空集合 + 错误码，
/// 让界面显示「没有结果 / 网络失败」而不是空白一片。
/// </summary>
public static class GitHubSearchCore
{
    /// <summary>解析 <c>/search/repositories</c>。</summary>
    public static IReadOnlyList<GitHubRepoResult> ParseSearch(string? json, out string errorCode)
    {
        errorCode = "";
        if (string.IsNullOrWhiteSpace(json))
        {
            errorCode = "NET_EMPTY_RESPONSE";
            return Array.Empty<GitHubRepoResult>();
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("items", out var items)
                || items.ValueKind != JsonValueKind.Array)
            {
                errorCode = "NET_BAD_RESPONSE";
                return Array.Empty<GitHubRepoResult>();
            }

            var list = new List<GitHubRepoResult>();
            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var fullName = Str(item, "full_name");
                if (fullName.Length == 0) continue;

                list.Add(new GitHubRepoResult
                {
                    FullName = fullName,
                    Description = Str(item, "description"),
                    Stars = Int(item, "stargazers_count"),
                    License = item.TryGetProperty("license", out var lic) && lic.ValueKind == JsonValueKind.Object
                        ? Str(lic, "spdx_id")
                        : "",
                    PushedAt = Str(item, "pushed_at"),
                    HtmlUrl = Str(item, "html_url")
                });
            }
            return list;
        }
        catch (JsonException)
        {
            errorCode = "NET_BAD_RESPONSE";
            return Array.Empty<GitHubRepoResult>();
        }
    }

    /// <summary>解析 <c>/repos/{owner}/{repo}/releases/latest</c>，返回版本号与资产列表。</summary>
    public static (string Version, IReadOnlyList<PluginReleaseAsset> Assets, string ErrorCode) ParseLatestRelease(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return ("", Array.Empty<PluginReleaseAsset>(), "NET_EMPTY_RESPONSE");

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return ("", Array.Empty<PluginReleaseAsset>(), "NET_BAD_RESPONSE");

            var version = Str(doc.RootElement, "tag_name");
            if (version.Length == 0) version = Str(doc.RootElement, "name");

            var assets = new List<PluginReleaseAsset>();
            if (doc.RootElement.TryGetProperty("assets", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in arr.EnumerateArray())
                {
                    if (a.ValueKind != JsonValueKind.Object) continue;
                    var name = Str(a, "name");
                    var url = Str(a, "browser_download_url");
                    if (name.Length == 0 || url.Length == 0) continue;

                    assets.Add(new PluginReleaseAsset
                    {
                        Name = name,
                        Url = url,
                        Size = Long(a, "size"),
                        Digest = Str(a, "digest")
                    });
                }
            }

            return (version.TrimStart('v', 'V'), assets, "");
        }
        catch (JsonException)
        {
            return ("", Array.Empty<PluginReleaseAsset>(), "NET_BAD_RESPONSE");
        }
    }

    private static string Str(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static int Int(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : 0;

    private static long Long(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : 0;
}
