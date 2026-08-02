using Microsoft.JSInterop;
using System.Text.Json;

namespace OBS_Helper.Client.Services;

public class BookmarkService
{
    private readonly IJSRuntime _js;
    private const string BookmarksKey = "obs_bookmarks";
    private const string StepsKey = "obs_steps";
    private HashSet<string> _bookmarks = new();

    public BookmarkService(IJSRuntime js) => _js = js;

    private bool _loaded;

    private async Task EnsureLoaded()
    {
        if (_loaded) return;
        try
        {
            var json = await _js.InvokeAsync<string>("localStorage.getItem", BookmarksKey);
            if (!string.IsNullOrEmpty(json))
            {
                var list = JsonSerializer.Deserialize<List<string>>(json);
                if (list is not null) _bookmarks = new HashSet<string>(list);
            }
        }
        catch (Exception)
        {
            // 本地存储不可用（隐私模式 / 被禁用）：静默降级，收藏功能仅本次会话有效。
        }
        _loaded = true;
    }

    public async Task<List<string>> GetAllAsync()
    {
        await EnsureLoaded();
        return _bookmarks.ToList();
    }

    public async Task<bool> IsBookmarkedAsync(string id)
    {
        await EnsureLoaded();
        return _bookmarks.Contains(id);
    }

    public async Task ToggleAsync(string id)
    {
        await EnsureLoaded();
        if (_bookmarks.Contains(id)) _bookmarks.Remove(id);
        else _bookmarks.Add(id);
        await Save(BookmarksKey, _bookmarks.ToList());
    }

    public async Task<List<int>> GetCompletedStepsAsync(string id)
    {
        try
        {
            var json = await _js.InvokeAsync<string>("localStorage.getItem", StepsKey);
            if (!string.IsNullOrEmpty(json))
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, List<int>>>(json);
                if (dict is not null && dict.TryGetValue(id, out var v)) return v;
            }
        }
        catch (Exception)
        {
            // 本地存储不可用：返回空进度，不影响浏览。
        }
        return new List<int>();
    }

    public async Task SetCompletedStepsAsync(string id, List<int> steps)
    {
        try
        {
            var json = await _js.InvokeAsync<string>("localStorage.getItem", StepsKey);
            var dict = string.IsNullOrEmpty(json)
                ? new Dictionary<string, List<int>>()
                : JsonSerializer.Deserialize<Dictionary<string, List<int>>>(json) ?? new Dictionary<string, List<int>>();
            dict[id] = steps;
            await Save(StepsKey, dict);
        }
        catch (Exception)
        {
            // 本地存储不可用：忽略写入失败，避免阻断 UI。
        }
    }

    private async Task Save(string key, object value)
    {
        try
        {
            var json = JsonSerializer.Serialize(value);
            await _js.InvokeVoidAsync("localStorage.setItem", key, json);
        }
        catch (Exception)
        {
            // 本地存储不可用：静默失败。
        }
    }
}
