using System.Text.Json.Nodes;
using OBS_Helper.Wpf.Localization;
using OBS_Helper.Wpf.Models.ObsConfig;

namespace OBS_Helper.Wpf.Services.Live;

/// <summary>
/// 虚拟直播间服务（V3.3.0 / P0-2）：把「模板里的 overlay 页面」变成 OBS 能打开的本地 URL。
///
/// 只负责资产侧（本地 HTTP + 页面解析）；场景落地仍由 <see cref="ObsConfig.SceneTemplateService"/>
/// 完成 —— 房间模板和既有场景模板共用同一条落地链路，不另起一套。
/// </summary>
public sealed class RoomService : IDisposable
{
    private RoomOverlayServer _server = new();
    private readonly object _gate = new();

    /// <summary>服务是否已启动（供界面显示「聊天框可用」）。</summary>
    public bool IsRunning
    {
        get { lock (_gate) return _server.IsRunning; }
    }

    /// <summary>确保 overlay 服务已启动；失败返回 false（不影响没有浏览器源的模板）。</summary>
    public bool EnsureStarted()
    {
        lock (_gate) return _server.Start();
    }

    /// <summary>
    /// 把页面名解析成完整 URL（带 token 与语言参数）。服务起不来 / 页面非法时返回 null。
    /// </summary>
    public string? ResolveUrl(string? page)
    {
        lock (_gate)
        {
            if (!_server.IsRunning && !_server.Start()) return null;
            var url = RoomOverlayCore.BuildUrl(_server.BaseUrl, _server.Token, page, Strings.Current);
            if (url is null)
                FileLogger.Warn("Room", $"拒绝非内置 overlay 页面：{page}");
            return url;
        }
    }

    /// <summary>
    /// 场景模板落地时调用：把 <c>browser</c> 规格换成真实的 <c>browser_source</c> 设置。
    /// 没有 browser 字段时返回 null（调用方沿用模板自带 settings）。
    /// </summary>
    public JsonObject? ResolveSourceSettings(TemplateSource source)
    {
        if (source.Browser is null) return null;

        var url = ResolveUrl(source.Browser.Page);
        if (url is null)
        {
            // 回退：不把整个来源创建流程炸掉，给一个空白页并在落地提示里说明
            FileLogger.Warn("Room", $"overlay 页面不可用，已回退空白页：{source.Browser.Page}");
            url = "about:blank";
        }
        return RoomOverlayCore.BuildBrowserSettings(source.Browser, url);
    }

    /// <summary>只保留虚拟主播模板（按 <c>vtuber</c> 标签）。</summary>
    public IReadOnlyList<SceneTemplate> FilterVtuber(IEnumerable<SceneTemplate> templates)
        => RoomOverlayCore.FilterByTag(templates, "vtuber");

    /// <summary>
    /// 重新初始化服务（端口被占用 / 用户切换网络后）。旧端口先释放，再随机分配。
    /// </summary>
    public void Restart()
    {
        lock (_gate)
        {
            try { _server.Dispose(); } catch { /* 忽略 */ }
            _server = new RoomOverlayServer();
            _server.Start();
        }
    }

    public void Dispose()
    {
        lock (_gate) _server.Dispose();
    }
}
