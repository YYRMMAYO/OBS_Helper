using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OBS_Helper.Wpf.Localization;
using OBS_Helper.Wpf.Services.Appearance;

namespace OBS_Helper.Wpf.Services.Appearance;

/// <summary>
/// 壁纸服务的 WPF 侧（V3.1.0 / W1~W9）。
///
/// 职责：壁纸库读写编排、导入校验（后台解码）、显示图解码与缓存、轮播计时器、
/// 旧 <c>bgImage</c> 迁移、把结果写进应用级资源供 <c>WallpaperLayer</c> 渲染。
///
/// 线程约定：公开方法在 UI 线程调用；解码走 <see cref="Task.Run(Func{object?})"/>，
/// 结果 <c>Freeze()</c> 后回到 UI 线程。轮播计时器用 <see cref="DispatcherTimer"/>。
/// </summary>
public sealed class WallpaperService : IDisposable
{
    private readonly AppearanceService _appearance;
    private readonly WallpaperStore _store;
    private readonly Dispatcher? _dispatcher;

    private readonly List<WallpaperItem> _library = new();
    private readonly Dictionary<string, ImageSource> _imageCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ImageSource> _thumbCache = new(StringComparer.OrdinalIgnoreCase);

    private readonly List<string> _order = new();
    private int _orderIndex;

    private DispatcherTimer? _timer;
    private bool _paused;
    private bool _disposed;
    private int _loadGeneration;
    private string _loadedId = "";
    private ImageSource? _currentImage;

    public WallpaperService(AppearanceService appearance, WallpaperStore store)
    {
        _appearance = appearance;
        _store = store;
        _dispatcher = Application.Current?.Dispatcher;
    }

    /// <summary>壁纸库变化（导入 / 删除 / 重新加载）时触发。</summary>
    public event Action? LibraryChanged;

    /// <summary>当前显示的图片 / 显示参数变化时触发（供 <c>WallpaperLayer</c> 与设置页刷新）。</summary>
    public event Action? Changed;

    /// <summary>一次性提示（迁移成功 / 原文件丢失 / 文件无法读取）；参数已是本地化文案。</summary>
    public event Action<string>? Notice;

    /// <summary>
    /// 启动早期产生的提示（迁移结果）。主窗口还没订阅 <see cref="Notice"/> 时先存在这里，
    /// 由 <see cref="TakePendingNotice"/> 取走并显示一次。
    /// </summary>
    public string? PendingNotice { get; private set; }

    /// <summary>取走启动期提示（取过一次就清空）。</summary>
    public string? TakePendingNotice()
    {
        var notice = PendingNotice;
        PendingNotice = null;
        return notice;
    }

    private void RaiseNotice(string message)
    {
        PendingNotice = message;
        Notice?.Invoke(message);
    }

    /// <summary>壁纸库（按导入时间倒序）。</summary>
    public IReadOnlyList<WallpaperItem> Library => _library;

    /// <summary>当前选中的壁纸；没有 / 已丢失时为 null。</summary>
    public WallpaperItem? CurrentItem => WallpaperCore.Find(_library, _currentId);

    /// <summary>当前壁纸 id（可能指向已不存在的条目）。</summary>
    public string CurrentId => _currentId;

    private string _currentId = "";

    /// <summary>当前已解码的显示图；没有时为 null。</summary>
    public ImageSource? CurrentImage => _currentImage;

    /// <summary>当前实际生效的铺放方式。</summary>
    public WallpaperFit Fit => WallpaperCore.NormalizeFit(_appearance.Settings.BackgroundFit);

    /// <summary>当前实际生效的压暗值。</summary>
    public double EffectiveDim => WallpaperCore.NormalizeDim(
        _appearance.Settings.BackgroundDim, _appearance.IsDarkEffective);

    /// <summary>当前实际生效的模糊半径。</summary>
    public double EffectiveBlur => WallpaperCore.NormalizeBlur(_appearance.Settings.BackgroundBlur);

    /// <summary>轮播是否真的在运行。</summary>
    public bool IsSlideshowActive =>
        WallpaperCore.CanSlideshow(_appearance.Settings.BackgroundSlideshow, _library.Count);

    /// <summary>
    /// 启动加载：读壁纸库 → 旧设置迁移 → 解析当前壁纸 → 同步轮播计时器。
    /// 必须在 <see cref="AppearanceService.Initialize"/> 之前由组合根挂好依赖，
    /// 但初始化本身在 Appearance 之后调用（此时资源已有一份默认底）。
    /// </summary>
    public void Initialize()
    {
        ReloadLibrary();
        MigrateLegacySetting();

        var s = _appearance.Settings;
        if (s.BackgroundMode == "image")
        {
            _currentId = WallpaperCore.ResolveCurrentId(s.BackgroundWallpaperId, _library);

            if (_currentId.Length == 0)
            {
                // 没有任何可用壁纸：回退默认底，并给一次可关闭提示（不静默变回白底）
                _appearance.RestoreDefaultBackground();
                RaiseNotice(Strings.T("settings.bg.missing"));
            }
            else if (!string.Equals(s.BackgroundWallpaperId, _currentId, StringComparison.OrdinalIgnoreCase))
            {
                // 旧配置里没有 bgWallpaperId：把解析出的第一张写回去，避免每次启动重新猜
                _appearance.SetWallpaper(_currentId);
            }
        }
        else
        {
            _currentId = "";
        }

        SyncTimer();
        EnsureCurrentImage();
        Reapply();
    }

    // ------------------------------------------------------------ 壁纸库

    /// <summary>重新读取壁纸库，并保证当前选择仍然指向有效条目。</summary>
    public void ReloadLibrary()
    {
        _library.Clear();
        _library.AddRange(_store.Load());
        _thumbCache.Clear();

        if (_currentId.Length > 0 && WallpaperCore.Find(_library, _currentId) is null)
            _currentId = "";
        if (_currentId.Length == 0)
            _currentId = WallpaperCore.ResolveCurrentId(_appearance.Settings.BackgroundWallpaperId, _library);

        LibraryChanged?.Invoke();
    }

    /// <summary>
    /// 导入一张图片（异步校验 + 后台读取尺寸）。
    /// 同内容重复导入直接复用；失败不改变当前壁纸。
    /// </summary>
    public async Task<WallpaperImportResult> ImportAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return new WallpaperImportResult(WallpaperImportError.Missing, null, false);

        var invalid = WallpaperCore.Validate(path, SafeLength(path));
        if (invalid != WallpaperImportError.None)
            return new WallpaperImportResult(invalid, null, false);

        var (width, height, readable) = await Task.Run(() => Probe(path)).ConfigureAwait(true);
        if (!readable)
            return new WallpaperImportResult(WallpaperImportError.Corrupt, null, false);

        invalid = WallpaperCore.Validate(path, SafeLength(path), width, height);
        if (invalid != WallpaperImportError.None)
            return new WallpaperImportResult(invalid, null, false);

        var result = await Task.Run(() => _store.Import(path, width, height)).ConfigureAwait(true);
        if (!result.Ok) return result;

        ReloadLibrary();
        if (_currentId.Length == 0 && result.Item is not null
            && string.Equals(_appearance.Settings.BackgroundMode, "image", StringComparison.Ordinal))
        {
            _currentId = result.Item.Id;
            _appearance.SetWallpaper(_currentId);
            EnsureCurrentImage();
        }
        else
        {
            Reapply();
        }

        LibraryChanged?.Invoke();
        return result;
    }

    /// <summary>选中一张壁纸（未知 id 会被忽略）。</summary>
    public void Select(string? id)
    {
        var item = WallpaperCore.Find(_library, id);
        if (item is null) return;

        _currentId = item.Id;
        _appearance.SetWallpaper(item.Id);   // 同时持久化并触发 Apply → ApplyResources
        EnsureCurrentImage();
        SyncOrder();
        Reapply();
    }

    /// <summary>删除一张壁纸；若删的是当前壁纸，自动切到下一张或回退默认底。</summary>
    public bool Delete(string? id)
    {
        var item = WallpaperCore.Find(_library, id);
        if (item is null) return false;

        var wasCurrent = string.Equals(_currentId, item.Id, StringComparison.OrdinalIgnoreCase);
        var ok = _store.Delete(item.Id);
        ReloadLibrary();

        if (wasCurrent)
        {
            var next = _library.FirstOrDefault();
            if (next is not null)
            {
                _currentId = next.Id;
                _appearance.SetWallpaper(next.Id);
                EnsureCurrentImage();
            }
            else
            {
                _currentId = "";
                _appearance.RestoreDefaultBackground();
                SetImage(null);
            }
        }
        else
        {
            SyncOrder();
            Reapply();
        }

        LibraryChanged?.Invoke();
        Changed?.Invoke();
        return ok;
    }

    // ------------------------------------------------------------ 显示参数

    /// <summary>设置铺放方式。</summary>
    public void SetFit(WallpaperFit fit) => _appearance.SetWallpaperFit(fit);

    /// <summary>设置压暗强度（0~0.8）。</summary>
    public void SetDim(double dim) => _appearance.SetWallpaperDim(dim);

    /// <summary>设置模糊半径（0~24）。</summary>
    public void SetBlur(double blur) => _appearance.SetWallpaperBlur(blur);

    /// <summary>设置轮播。</summary>
    public void SetSlideshow(bool enabled, int seconds, bool shuffle)
    {
        _appearance.SetWallpaperSlideshow(enabled, seconds, shuffle);
        SyncTimer();
        Reapply();
    }

    /// <summary>恢复默认底（不删除壁纸库文件）。</summary>
    public void RestoreDefault()
    {
        _currentId = "";
        _appearance.RestoreDefaultBackground();
        SetImage(null);
        StopTimer();
    }

    /// <summary>窗口最小化 / 失去激活时暂停轮播；恢复时从当前张继续。</summary>
    public void SetPaused(bool paused)
    {
        if (_paused == paused) return;
        _paused = paused;
        if (paused) StopTimer();
        else SyncTimer();
    }

    /// <summary>
    /// 把当前壁纸写进应用级资源（由 <see cref="AppearanceService.Apply"/> 调用）。
    /// 高对比 / 默认 / 纯色模式由外观服务处理，这里只负责图片模式。
    /// </summary>
    public void ApplyResources(ResourceDictionary res, bool dark)
    {
        var s = _appearance.Settings;
        if (s.BackgroundMode != "image")
        {
            res["ContentBackgroundImage"] = null;
            res["ContentBackgroundImageA"] = null;
            res["ContentBackgroundImageB"] = null;
            res["ContentBackgroundDim"] = Brushes.Transparent;
            res["GlassWallpaperBrush"] = Brushes.Transparent;
            return;
        }

        var dim = WallpaperCore.NormalizeDim(s.BackgroundDim, dark);
        var blur = WallpaperCore.NormalizeBlur(s.BackgroundBlur);
        var fit = WallpaperCore.NormalizeFit(s.BackgroundFit);

        res["ContentBackgroundImage"] = _currentImage;
        res["ContentBackgroundImageA"] = _currentImage;
        res["ContentBackgroundImageB"] = null;
        res["ContentBackgroundFit"] = WallpaperCore.FitKey(fit);
        res["ContentBackgroundOpacity"] = 1.0;
        res["ContentBackgroundBlurRadius"] = blur;
        res["ContentBackgroundFadeDuration"] = _appearance.Settings.ReduceMotion
            ? new Duration(TimeSpan.Zero)
            : new Duration(TimeSpan.FromMilliseconds(300));
        res["ContentBackgroundDim"] = DimBrush(dim);
        res["GlassWallpaperBrush"] = BuildGlassBrush(_currentImage, dark) ?? Brushes.Transparent;
    }

    /// <summary>
    /// 生成导航 / 顶栏的「毛玻璃」底：把当前壁纸缩小并高斯模糊，再叠一层主题色半透明遮罩。
    /// 只在壁纸切换 / 主题切换时生成一次，不参与每秒渲染。
    /// </summary>
    private static Brush? BuildGlassBrush(ImageSource? source, bool dark)
    {
        if (source is not BitmapSource bmp || bmp.PixelWidth <= 0 || bmp.PixelHeight <= 0) return null;
        const int W = 160, H = 90;
        try
        {
            // DrawingContext 只接受旧的 BitmapEffect；这里把 BlurEffect 挂在 Visual.Effect 上，
            // 再渲染到 RenderTargetBitmap，效果相同且不会用到已过时的 BitmapEffect API。
            var visual = new DrawingVisual
            {
                Effect = new BlurEffect { Radius = 22, KernelType = KernelType.Gaussian }
            };
            using (var dc = visual.RenderOpen())
            {
                dc.PushOpacity(0.95);
                dc.DrawImage(bmp, new Rect(0, 0, W, H));
                dc.Pop();
            }

            var rtb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();

            var tint = new SolidColorBrush(dark
                ? Color.FromArgb(118, 10, 12, 11)
                : Color.FromArgb(96, 255, 255, 252));
            tint.Freeze();

            var group = new DrawingGroup();
            group.Children.Add(new ImageDrawing(rtb, new Rect(0, 0, 1, 1)));
            group.Children.Add(new GeometryDrawing { Brush = tint, Geometry = new RectangleGeometry(new Rect(0, 0, 1, 1)) });
            group.Freeze();

            var brush = new DrawingBrush(group)
            {
                Stretch = Stretch.Fill,
                Viewbox = new Rect(0, 0, 1, 1),
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0, 1, 1),
                ViewportUnits = BrushMappingMode.RelativeToBoundingBox,
            };
            brush.Freeze();
            return brush;
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Wallpaper", $"生成毛玻璃底失败，退回半透明表面：{ex.Message}");
            return null;
        }
    }

    /// <summary>取（并缓存）一张缩略图；设置页的网格使用。</summary>
    public ImageSource? GetThumbnail(WallpaperItem? item, int edge = 220)
    {
        if (item is null) return null;
        if (_thumbCache.TryGetValue(item.Id, out var cached)) return cached;

        var image = Decode(item.FullPath(_store.Root), edge);
        if (image is not null) _thumbCache[item.Id] = image;
        return image;
    }

    // ------------------------------------------------------------ 内部：迁移

    private void MigrateLegacySetting()
    {
        var s = _appearance.Settings;
        if (s.BackgroundMode != "image" && string.IsNullOrWhiteSpace(s.BackgroundImage)) return;

        var legacyPath = s.BackgroundImage;
        var legacyExists = SafeExists(legacyPath);
        var action = WallpaperCore.DecideMigration(
            s.BackgroundMigrationVersion,
            legacyPath,
            legacyExists,
            s.BackgroundWallpaperId,
            WallpaperCore.Find(_library, s.BackgroundWallpaperId) is not null);

        if (action == WallpaperMigrationAction.None) return;

        if (action == WallpaperMigrationAction.FallbackDefault)
        {
            _appearance.RestoreDefaultBackground();
            _appearance.MarkWallpaperMigrationDone();
            RaiseNotice(Strings.T("settings.bg.missing"));
            return;
        }

        var (width, height, readable) = Probe(legacyPath);
        if (!readable)
        {
            _appearance.RestoreDefaultBackground();
            _appearance.MarkWallpaperMigrationDone();
            RaiseNotice(Strings.T("settings.bg.import.corrupt"));
            return;
        }

        var result = _store.Import(legacyPath, width, height);
        if (!result.Ok || result.Item is null)
        {
            // 磁盘写入失败：保留旧字段与迁移标记，下次启动再试
            RaiseNotice(Strings.T("settings.bg.import.failed"));
            return;
        }

        _library.Clear();
        _library.AddRange(_store.Load());
        _currentId = result.Item.Id;
        _appearance.Settings.BackgroundWallpaperId = result.Item.Id;
        _appearance.Settings.BackgroundMode = "image";
        _appearance.MarkWallpaperMigrationDone();
        RaiseNotice(Strings.T("settings.bg.migrated", result.Item.DisplayName));
    }

    // ------------------------------------------------------------ 内部：解码

    private void EnsureCurrentImage()
    {
        var item = CurrentItem;
        if (item is null)
        {
            SetImage(null);
            return;
        }

        if (string.Equals(_loadedId, item.Id, StringComparison.OrdinalIgnoreCase) && _currentImage is not null)
            return;

        if (_imageCache.TryGetValue(item.Id, out var cached))
        {
            SetImage(cached, item.Id);
            return;
        }

        var path = item.FullPath(_store.Root);
        var id = item.Id;
        var generation = ++_loadGeneration;

        if (_dispatcher is null)
        {
            SetImage(Decode(path, WallpaperCore.DecodeMaxEdge), id);
            return;
        }

        Task.Run(() => Decode(path, WallpaperCore.DecodeMaxEdge)).ContinueWith(t =>
        {
            if (_disposed || generation != _loadGeneration) return;
            _dispatcher.BeginInvoke(new Action(() =>
            {
                if (_disposed || generation != _loadGeneration) return;
                var image = t.Result;
                if (image is null)
                {
                    // 文件被外部删除 / 损坏：从库里移除并回退，不崩溃
                    _library.RemoveAll(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
                    if (string.Equals(_currentId, id, StringComparison.OrdinalIgnoreCase))
                    {
                        SetImage(null);
                        _currentId = "";
                        _appearance.RestoreDefaultBackground();
                        RaiseNotice(Strings.T("settings.bg.missing"));
                    }
                    LibraryChanged?.Invoke();
                    Changed?.Invoke();
                    return;
                }

                CacheImage(id, image);
                SetImage(image, id);
                SyncOrder();
                Changed?.Invoke();
            }));
        }, TaskScheduler.Default);
    }

    private void CacheImage(string id, ImageSource image)
    {
        _imageCache[id] = image;
        if (_imageCache.Count <= 3) return;

        // 淘汰非当前、非下一张的旧图，避免长期停留的内存
        var nextId = PeekNextId();
        var remove = _imageCache.Keys
            .Where(k => !string.Equals(k, id, StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(k, nextId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var key in remove) _imageCache.Remove(key);
    }

    /// <summary>预加载下一张（轮播切换时不再同步解码）。</summary>
    private void PreloadNext()
    {
        var nextId = PeekNextId();
        if (nextId.Length == 0 || _imageCache.ContainsKey(nextId)) return;

        var item = WallpaperCore.Find(_library, nextId);
        if (item is null) return;

        var path = item.FullPath(_store.Root);
        Task.Run(() => Decode(path, WallpaperCore.DecodeMaxEdge)).ContinueWith(t =>
        {
            if (_disposed || t.Result is null) return;
            if (_dispatcher is null) { CacheImage(nextId, t.Result); return; }
            _dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_disposed) CacheImage(nextId, t.Result!);
            }));
        }, TaskScheduler.Default);
    }

    private void SetImage(ImageSource? image, string? id = null)
    {
        _currentImage = image;
        if (id is not null) _loadedId = id;
        if (image is null) _loadedId = "";

        // 直接更新资源，供 WallpaperLayer 之外的旧绑定继续工作
        var app = Application.Current;
        if (app is not null)
        {
            app.Resources["ContentBackgroundImage"] = image;
            app.Resources["ContentBackgroundImageA"] = image;
            app.Resources["ContentBackgroundImageB"] = null;
            app.Resources["ContentBackgroundDim"] = image is null
                ? Brushes.Transparent
                : DimBrush(EffectiveDim);
        }

        Changed?.Invoke();
    }

    private static ImageSource? Decode(string path, int maxEdge)
    {
        try
        {
            if (!File.Exists(path)) return null;

            var (pixelWidth, pixelHeight, readable) = Probe(path);
            if (!readable || pixelWidth <= 0 || pixelHeight <= 0) return null;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;

            // 只缩小、不放大：小图保持原尺寸，避免糊掉
            if (maxEdge > 0 && Math.Max(pixelWidth, pixelHeight) > maxEdge)
            {
                if (pixelWidth >= pixelHeight) bmp.DecodePixelWidth = maxEdge;
                else bmp.DecodePixelHeight = maxEdge;
            }

            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                bmp.StreamSource = stream;
                bmp.EndInit();
            }
            bmp.Freeze();
            return bmp;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>读尺寸并验证可解码；失败返回 readable=false。</summary>
    private static (int Width, int Height, bool Readable) Probe(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            if (decoder.Frames.Count == 0) return (0, 0, false);
            var frame = decoder.Frames[0];
            return (frame.PixelWidth, frame.PixelHeight, frame.PixelWidth > 0 && frame.PixelHeight > 0);
        }
        catch (Exception)
        {
            return (0, 0, false);
        }
    }

    private static SolidColorBrush DimBrush(double dim)
    {
        var alpha = (byte)Math.Round(Math.Clamp(dim, 0, 1) * 255);
        var brush = new SolidColorBrush(Color.FromArgb(alpha, 0, 0, 0));
        brush.Freeze();
        return brush;
    }

    // ------------------------------------------------------------ 内部：轮播

    private void SyncTimer()
    {
        if (IsSlideshowActive && !_paused && !_disposed)
        {
            StartTimer();
            SyncOrder();
        }
        else
        {
            StopTimer();
        }
    }

    private void StartTimer()
    {
        var seconds = WallpaperCore.NormalizeSlideshowSeconds(_appearance.Settings.BackgroundSlideshowSeconds);
        if (_timer is null)
        {
            _timer = new DispatcherTimer(DispatcherPriority.Background);
            _timer.Tick += OnTick;
        }

        _timer.Interval = TimeSpan.FromSeconds(seconds);
        _timer.Start();
    }

    private void StopTimer() => _timer?.Stop();

    private void OnTick(object? sender, EventArgs e)
    {
        if (_disposed || _paused || !IsSlideshowActive) return;

        if (_order.Count < 2)
        {
            StopTimer();
            return;
        }

        _orderIndex++;
        if (_orderIndex >= _order.Count)
        {
            // 一轮结束：随机模式重新洗牌（尽量换一张开头），顺序模式从头开始
            var current = _currentId;
            _order.Clear();
            _order.AddRange(WallpaperCore.BuildOrder(
                _library.Select(i => i.Id).ToList(), _appearance.Settings.BackgroundShuffle));
            if (_appearance.Settings.BackgroundShuffle && _order.Count > 1
                && string.Equals(_order[0], current, StringComparison.OrdinalIgnoreCase))
                (_order[0], _order[1]) = (_order[1], _order[0]);
            _orderIndex = 0;
        }

        var id = _order[_orderIndex];
        var item = WallpaperCore.Find(_library, id);
        if (item is null)
        {
            // 切图失败：跳过该张，继续下一张（连续失败由调用点自然收敛）
            return;
        }

        _currentId = id;
        _appearance.Settings.BackgroundWallpaperId = id;
        EnsureCurrentImage();
        PreloadNext();
        Reapply();
        Changed?.Invoke();
    }

    private string PeekNextId()
    {
        if (_order.Count == 0) return "";
        var next = WallpaperCore.NextIndex(_orderIndex, _order.Count);
        return next >= 0 && next < _order.Count ? _order[next] : "";
    }

    private void SyncOrder()
    {
        var ids = _library.Select(i => i.Id).ToList();
        _order.Clear();
        _order.AddRange(WallpaperCore.BuildOrder(ids, _appearance.Settings.BackgroundShuffle));
        _orderIndex = _order.FindIndex(x => string.Equals(x, _currentId, StringComparison.OrdinalIgnoreCase));
        if (_orderIndex < 0) _orderIndex = 0;
    }

    /// <summary>把当前状态重新写进应用资源（不重建整份调色板）。</summary>
    private void Reapply()
    {
        var app = Application.Current;
        if (app is null) return;
        ApplyResources(app.Resources, _appearance.IsDarkEffective);
        Changed?.Invoke();
    }

    private static long SafeLength(string path)
    {
        try { return new FileInfo(path).Length; }
        catch (Exception) { return 0; }
    }

    private static bool SafeExists(string? path)
    {
        try { return !string.IsNullOrWhiteSpace(path) && File.Exists(path); }
        catch (Exception) { return false; }
    }

    public void Dispose()
    {
        _disposed = true;
        StopTimer();
        _timer = null;
        _imageCache.Clear();
        _thumbCache.Clear();
    }
}
