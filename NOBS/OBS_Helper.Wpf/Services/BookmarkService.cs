using System.Text.Json;
using OBS_Helper.Wpf.Services.Host;

namespace OBS_Helper.Wpf.Services;

/// <summary>
/// 收藏与「步骤已完成」进度。数据落在 prefs.json，纯本地、不上传。
/// </summary>
public sealed class BookmarkService
{
    private const string BookmarksKey = "obs_bookmarks";
    private const string StepsKey = "obs_steps";

    private readonly LocalStore _store;
    private readonly HashSet<string> _bookmarks = new(StringComparer.Ordinal);
    private Dictionary<string, List<int>> _steps = new(StringComparer.Ordinal);
    private bool _loaded;
    private readonly object _gate = new();

    /// <summary>收藏发生变化时触发，供首页 / 详情页同步刷新星标。</summary>
    public event Action? BookmarksChanged;

    public BookmarkService(LocalStore store)
    {
        _store = store;
    }

    private void EnsureLoaded()
    {
        lock (_gate)
        {
            if (_loaded) return;
            _loaded = true;

            try
            {
                var raw = _store.GetItem(BookmarksKey);
                if (!string.IsNullOrEmpty(raw))
                {
                    var list = JsonSerializer.Deserialize<List<string>>(raw);
                    if (list is not null)
                    {
                        foreach (var id in list) _bookmarks.Add(id);
                    }
                }
            }
            catch (Exception)
            {
                // 内容损坏：当作没有收藏，不阻断使用。
            }

            try
            {
                var raw = _store.GetItem(StepsKey);
                if (!string.IsNullOrEmpty(raw))
                {
                    _steps = JsonSerializer.Deserialize<Dictionary<string, List<int>>>(raw)
                             ?? new Dictionary<string, List<int>>(StringComparer.Ordinal);
                }
            }
            catch (Exception)
            {
                _steps = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            }
        }
    }

    public List<string> GetAll()
    {
        EnsureLoaded();
        lock (_gate) return _bookmarks.ToList();
    }

    public bool IsBookmarked(string id)
    {
        EnsureLoaded();
        lock (_gate) return _bookmarks.Contains(id);
    }

    /// <summary>切换收藏状态，返回切换后的状态。</summary>
    public bool Toggle(string id)
    {
        EnsureLoaded();
        bool now;
        lock (_gate)
        {
            if (_bookmarks.Contains(id))
            {
                _bookmarks.Remove(id);
                now = false;
            }
            else
            {
                _bookmarks.Add(id);
                now = true;
            }
            _store.SetItem(BookmarksKey, JsonSerializer.Serialize(_bookmarks.ToList()));
        }
        BookmarksChanged?.Invoke();
        return now;
    }

    public void Clear()
    {
        EnsureLoaded();
        lock (_gate)
        {
            _bookmarks.Clear();
            _store.SetItem(BookmarksKey, "[]");
        }
        BookmarksChanged?.Invoke();
    }

    public List<int> GetCompletedSteps(string id)
    {
        EnsureLoaded();
        lock (_gate)
        {
            return _steps.TryGetValue(id, out var v) ? new List<int>(v) : new List<int>();
        }
    }

    public void SetCompletedSteps(string id, IEnumerable<int> steps)
    {
        EnsureLoaded();
        lock (_gate)
        {
            _steps[id] = steps.Distinct().OrderBy(i => i).ToList();
            _store.SetItem(StepsKey, JsonSerializer.Serialize(_steps));
        }
    }

    /// <summary>清空所有步骤进度（设置页「重置进度」）。</summary>
    public void ClearAllSteps()
    {
        EnsureLoaded();
        lock (_gate)
        {
            _steps.Clear();
            _store.SetItem(StepsKey, "{}");
        }
    }

    /// <summary>某个问题的完成进度（0~1），供详情页进度条使用。</summary>
    public double Progress(string id, int totalSteps)
    {
        if (totalSteps <= 0) return 0;
        return Math.Clamp(GetCompletedSteps(id).Count / (double)totalSteps, 0, 1);
    }
}
