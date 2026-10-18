using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OBS_Helper.Wpf.Services.Appearance;

/// <summary>壁纸在内容区的铺放方式。</summary>
public enum WallpaperFit
{
    /// <summary>填充：铺满内容区，可能裁剪（默认）。</summary>
    Fill = 0,

    /// <summary>适应：完整显示，可能留边。</summary>
    Contain = 1,

    /// <summary>居中：按原尺寸居中，超出部分裁剪。</summary>
    Center = 2,

    /// <summary>平铺：小图 / 纹理重复铺满。</summary>
    Tile = 3
}

/// <summary>导入失败的原因（用于给出可照做的文案）。</summary>
public enum WallpaperImportError
{
    None = 0,
    Missing,
    Unsupported,
    TooLarge,
    TooManyPixels,
    Corrupt,
    WriteFailed
}

/// <summary>壁纸库中的一张壁纸。</summary>
public sealed class WallpaperItem
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("file")] public string File { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("addedAt")] public DateTimeOffset AddedAt { get; set; }
    [JsonPropertyName("width")] public int Width { get; set; }
    [JsonPropertyName("height")] public int Height { get; set; }
    [JsonPropertyName("bytes")] public long Bytes { get; set; }

    /// <summary>库内绝对路径（仅当 <paramref name="root"/> 有效时）。</summary>
    public string FullPath(string root) => Path.Combine(root, File);

    /// <summary>没有独立名称时用文件名兜底（旧 manifest / 扫描重建）。</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? File : Name;
}

/// <summary>壁纸库清单（<c>manifest.json</c>）。</summary>
public sealed class WallpaperManifest
{
    [JsonPropertyName("version")] public int Version { get; set; } = WallpaperCore.ManifestVersion;
    [JsonPropertyName("items")] public List<WallpaperItem> Items { get; set; } = new();
}

/// <summary>导入结果。</summary>
public sealed record WallpaperImportResult(
    WallpaperImportError Error,
    WallpaperItem? Item,
    /// <summary>同内容已存在（未产生副本）。</summary>
    bool Reused,
    string? Message = null)
{
    public bool Ok => Error == WallpaperImportError.None && Item is not null;
}

/// <summary>从旧设置迁移壁纸时的决策。</summary>
public enum WallpaperMigrationAction
{
    /// <summary>不需要迁移（已迁移过 / 没有旧路径 / 没有图片模式）。</summary>
    None = 0,

    /// <summary>旧路径存在，导入壁纸库。</summary>
    ImportLegacy,

    /// <summary>旧路径不存在，回退默认底并提示一次。</summary>
    FallbackDefault
}

/// <summary>
/// 壁纸功能的纯逻辑核心（V3.1.0）。
///
/// 这里只做「能单测的决策」：格式 / 大小 / 尺寸校验、哈希文件名、fit/dim/blur 归一化、
/// 轮播顺序、旧设置迁移决策、manifest 序列化容错。零 WPF 依赖，单测工程直接链接编译。
/// 真正的解码、缓存与计时器在 <see cref="WallpaperService"/>。
/// </summary>
public static class WallpaperCore
{
    /// <summary>manifest 当前 schema 版本。</summary>
    public const int ManifestVersion = 1;

    /// <summary>旧设置迁移标记：升到 1 表示已完成一次迁移尝试。</summary>
    public const int MigrationVersion = 1;

    /// <summary>单文件大小上限：50 MB。</summary>
    public const long MaxFileBytes = 50L * 1024 * 1024;

    /// <summary>解码后的单边上限（像素）。超过就不导入，避免 8K 图直接吃满内存。</summary>
    public const int MaxDimension = 16384;

    /// <summary>渲染解码上限：按窗口尺寸解码，最长边不超过这个值。</summary>
    public const int DecodeMaxEdge = 3840;

    /// <summary>压暗范围与默认值。</summary>
    public const double MinDim = 0.0;
    public const double MaxDim = 0.80;
    public const double DefaultDimLight = 0.15;
    public const double DefaultDimDark = 0.40;

    /// <summary>「自动压暗」的哨兵值：设置里存 -1，表示按主题取默认值。</summary>
    public const double AutoDim = -1.0;

    /// <summary>轮播允许的间隔（秒）。</summary>
    public static readonly int[] SlideshowIntervals = { 30, 60, 300, 900, 1800, 3600 };

    /// <summary>轮播默认间隔：5 分钟。</summary>
    public const int DefaultSlideshowSeconds = 300;

    /// <summary>支持的扩展名白名单（小写，带点）。</summary>
    public static readonly string[] SupportedExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff" };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true
    };

    // ------------------------------------------------------------ 校验

    /// <summary>文件扩展名是否在白名单内（大小写不敏感）。</summary>
    public static bool IsSupportedExtension(string? pathOrExtension)
    {
        if (string.IsNullOrWhiteSpace(pathOrExtension)) return false;
        var ext = pathOrExtension.StartsWith(".", StringComparison.Ordinal)
            ? pathOrExtension
            : Path.GetExtension(pathOrExtension);
        if (string.IsNullOrEmpty(ext)) return false;
        foreach (var e in SupportedExtensions)
        {
            if (string.Equals(e, ext, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>
    /// 导入前校验：文件存在、扩展名、大小与（可选的）像素尺寸。
    /// 解码本身在服务层做，这里不碰图片。
    /// </summary>
    public static WallpaperImportError Validate(
        string? path, long fileBytes, int pixelWidth = 0, int pixelHeight = 0, bool exists = true)
    {
        if (!exists || string.IsNullOrWhiteSpace(path)) return WallpaperImportError.Missing;
        if (!IsSupportedExtension(path)) return WallpaperImportError.Unsupported;
        if (fileBytes <= 0) return WallpaperImportError.Corrupt;
        if (fileBytes > MaxFileBytes) return WallpaperImportError.TooLarge;
        if (pixelWidth > MaxDimension || pixelHeight > MaxDimension) return WallpaperImportError.TooManyPixels;
        return WallpaperImportError.None;
    }

    /// <summary>扩展名归一化：<c>.jpeg</c> 仍保留原扩展名，但统一小写；未知返回空串。</summary>
    public static string NormalizeExtension(string path)
    {
        var ext = Path.GetExtension(path) ?? "";
        return IsSupportedExtension(ext) ? ext.ToLowerInvariant() : "";
    }

    /// <summary>文件内容的 SHA-256（小写十六进制），用作壁纸 id 与文件名主体。</summary>
    public static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(stream);
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    /// <summary>由 id + 扩展名生成库内文件名。</summary>
    public static string FileNameFor(string id, string extension)
    {
        var ext = NormalizeExtension("x" + (string.IsNullOrWhiteSpace(extension) ? ".jpg" : extension));
        if (ext.Length == 0) ext = ".jpg";
        return id + ext;
    }

    /// <summary>校验 id 形状（32~64 位十六进制），防止 manifest 脏值拼出越界路径。</summary>
    public static bool IsValidId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length < 32 || id.Length > 64) return false;
        foreach (var c in id)
        {
            var ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
            if (!ok) return false;
        }
        return true;
    }

    // ------------------------------------------------------------ 显示归一化

    /// <summary>把持久化的 fit 值归一化；未知值回退填充。</summary>
    public static WallpaperFit NormalizeFit(string? key) => key?.Trim().ToLowerInvariant() switch
    {
        "contain" or "fit" or "uniform" => WallpaperFit.Contain,
        "center" or "none" => WallpaperFit.Center,
        "tile" => WallpaperFit.Tile,
        _ => WallpaperFit.Fill
    };

    /// <summary>fit 的持久化键。</summary>
    public static string FitKey(WallpaperFit fit) => fit switch
    {
        WallpaperFit.Contain => "contain",
        WallpaperFit.Center => "center",
        WallpaperFit.Tile => "tile",
        _ => "fill"
    };

    /// <summary>
    /// 压暗值归一化。存储值 &lt; 0（默认 -1）表示「跟随主题」：
    /// 浅色 15%、深色 40%；其余值钳到 0~0.80。
    /// </summary>
    public static double NormalizeDim(double dim, bool dark)
    {
        if (dim < 0 || double.IsNaN(dim) || double.IsInfinity(dim))
            return dark ? DefaultDimDark : DefaultDimLight;
        return Math.Clamp(dim, MinDim, MaxDim);
    }

    /// <summary>压暗值是否来自用户（不是「跟随主题」）。</summary>
    public static bool IsCustomDim(double dim) => dim >= 0 && !double.IsNaN(dim);

    /// <summary>模糊半径归一化（0~24 px）；默认关闭。</summary>
    public static double NormalizeBlur(double blur)
    {
        if (blur <= 0 || double.IsNaN(blur) || double.IsInfinity(blur)) return 0;
        return Math.Clamp(blur, 0, 24);
    }

    /// <summary>轮播间隔归一化：只接受预设档位，其余回退 5 分钟。</summary>
    public static int NormalizeSlideshowSeconds(int seconds)
    {
        foreach (var s in SlideshowIntervals)
        {
            if (s == seconds) return s;
        }
        return DefaultSlideshowSeconds;
    }

    /// <summary>轮播只在壁纸库至少 2 张且开关打开时才有意义。</summary>
    public static bool CanSlideshow(bool enabled, int itemCount) => enabled && itemCount >= 2;

    /// <summary>渲染解码尺寸：按当前客户区 × DPI 计算，最长边不超过 <see cref="DecodeMaxEdge"/>。</summary>
    public static (int Width, int Height) DecodeSize(double viewWidth, double viewHeight, int pixelWidth, int pixelHeight)
    {
        if (pixelWidth <= 0 || pixelHeight <= 0) return (0, 0);

        var targetEdge = Math.Max(viewWidth, viewHeight);
        if (targetEdge <= 0 || double.IsNaN(targetEdge) || double.IsInfinity(targetEdge))
            targetEdge = 1920;

        var scale = targetEdge / Math.Max(pixelWidth, pixelHeight);
        if (scale >= 1) return (pixelWidth, pixelHeight);   // 不需要放大

        var w = (int)Math.Round(pixelWidth * scale);
        var h = (int)Math.Round(pixelHeight * scale);
        var maxEdge = Math.Min(DecodeMaxEdge, (int)Math.Ceiling(targetEdge * 1.25));
        if (w > maxEdge || h > maxEdge)
        {
            var k = maxEdge / (double)Math.Max(w, h);
            w = Math.Max(1, (int)Math.Round(w * k));
            h = Math.Max(1, (int)Math.Round(h * k));
        }
        return (Math.Max(1, w), Math.Max(1, h));
    }

    // ------------------------------------------------------------ 轮播顺序

    /// <summary>
    /// 生成一轮轮播顺序：顺序模式原样返回；随机模式用 Fisher–Yates 洗牌（一轮内不重复）。
    /// 单张 / 空列表原样返回。
    /// </summary>
    public static List<string> BuildOrder(IReadOnlyList<string> ids, bool shuffle, Random? random = null)
    {
        var list = ids.Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
        if (!shuffle || list.Count < 2) return list;

        var rng = random ?? new Random();
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }

    /// <summary>
    /// 取下一张的索引。顺序模式循环前进；随机模式下按预先生成的一轮顺序前进，
    /// 到末尾由调用方重新洗牌（这里只负责不越界、不重复）。
    /// </summary>
    public static int NextIndex(int current, int count)
    {
        if (count <= 0) return -1;
        if (current < 0 || current >= count) return 0;
        return (current + 1) % count;
    }

    /// <summary>从轮播列表中移除一个 id，并给出新的当前索引（尽量停在原位置附近）。</summary>
    public static (List<string> Order, int Index) RemoveFromOrder(
        IReadOnlyList<string> order, string id, int currentIndex)
    {
        var list = order.Where(x => !string.Equals(x, id, StringComparison.OrdinalIgnoreCase)).ToList();
        if (list.Count == 0) return (list, -1);
        if (currentIndex < 0) return (list, 0);
        if (currentIndex >= list.Count) return (list, list.Count - 1);
        return (list, currentIndex);
    }

    // ------------------------------------------------------------ 旧设置迁移

    /// <summary>
    /// 旧 <c>bgImage</c> 的迁移决策（W7）。
    /// 只做一次：<paramref name="migrationVersion"/> 已达 <see cref="MigrationVersion"/> 时返回 None。
    /// </summary>
    public static WallpaperMigrationAction DecideMigration(
        int migrationVersion,
        string? legacyPath,
        bool legacyExists,
        string? wallpaperId,
        bool wallpaperExists)
    {
        if (migrationVersion >= MigrationVersion) return WallpaperMigrationAction.None;
        if (!string.IsNullOrWhiteSpace(wallpaperId) && wallpaperExists) return WallpaperMigrationAction.None;
        if (string.IsNullOrWhiteSpace(legacyPath)) return WallpaperMigrationAction.None;
        return legacyExists ? WallpaperMigrationAction.ImportLegacy : WallpaperMigrationAction.FallbackDefault;
    }

    /// <summary>
    /// 解析当前应该显示的壁纸 id：优先 <c>bgWallpaperId</c>，其次壁纸库第一张（兼容旧配置）。
    /// </summary>
    public static string ResolveCurrentId(string? wallpaperId, IReadOnlyList<WallpaperItem> library)
    {
        if (!string.IsNullOrWhiteSpace(wallpaperId))
        {
            foreach (var item in library)
            {
                if (string.Equals(item.Id, wallpaperId, StringComparison.OrdinalIgnoreCase)) return item.Id;
            }
        }
        return library.Count > 0 ? library[0].Id : "";
    }

    /// <summary>按 id 找条目；找不到返回 null。</summary>
    public static WallpaperItem? Find(IReadOnlyList<WallpaperItem> library, string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        foreach (var item in library)
        {
            if (string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase)) return item;
        }
        return null;
    }

    // ------------------------------------------------------------ manifest

    /// <summary>序列化 manifest（原子写的文本部分）。</summary>
    public static string SerializeManifest(WallpaperManifest manifest)
        => JsonSerializer.Serialize(manifest, JsonOpts);

    /// <summary>
    /// 反序列化 manifest；损坏 / 空内容返回 null，由调用方扫描目录重建。
    /// 不在这里抛异常：manifest 损坏不能阻塞启动。
    /// </summary>
    public static WallpaperManifest? TryParseManifest(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var manifest = JsonSerializer.Deserialize<WallpaperManifest>(json, JsonOpts);
            if (manifest is null) return null;
            manifest.Items ??= new List<WallpaperItem>();
            manifest.Version = manifest.Version <= 0 ? ManifestVersion : manifest.Version;

            // 清掉脏条目：id 非法 / 文件名越界 / 重复
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            manifest.Items = manifest.Items
                .Where(i => i is not null
                            && IsValidId(i.Id)
                            && !string.IsNullOrWhiteSpace(i.File)
                            && seen.Add(i.Id))
                .ToList();
            return manifest;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }
}
