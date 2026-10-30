using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using OBS_Helper.Wpf.Models.Live;

namespace OBS_Helper.Wpf.Services.Live;

/// <summary>
/// Live2D 模型校验与布局（V3.3.0 / P0-3）——纯逻辑、纯 BCL。
///
/// 安全边界（PLAN_V3.3.0 §4.4）：
/// <list type="bullet">
///   <item>只接受 <c>.model3.json</c> 里的**相对引用**，且必须落在模型根目录内（拒绝 <c>..</c> / 绝对路径 / 盘符）；</item>
///   <item>拒绝可执行文件混入（.exe/.dll/.bat/.cmd/.ps1/.js）；</item>
///   <item>限制文件数 / 总大小，避免用户误选整个磁盘目录；</item>
///   <item>清单哈希只读文件、不写文件，失败也不影响模型本身。</item>
/// </list>
///
/// 只做只读校验；复制到 VTS 目录、在 OBS 建源都在服务层完成。
/// </summary>
public static class Live2DModelCore
{
    /// <summary>单个模型允许的最大文件数。</summary>
    public const int MaxFiles = 2000;

    /// <summary>单个模型允许的最大总字节数（512 MB）。</summary>
    public const long MaxTotalBytes = 512L * 1024 * 1024;

    /// <summary>允许被模型引用的扩展名（音频只允许在 Motions 的 Sound 字段里）。</summary>
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".moc3", ".png", ".jpg", ".jpeg", ".webp", ".json",
        ".wav", ".mp3", ".ogg", ".motion3.json", ".physics3.json",
        ".pose3.json", ".exp3.json", ".userdata3.json", ".cdi3.json"
    };

    private static readonly HashSet<string> DangerousExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".bat", ".cmd", ".ps1", ".js", ".vbs", ".scr", ".msi", ".com", ".lnk"
    };

    /// <summary>布局预设（顺序即界面展示顺序）。</summary>
    public static readonly IReadOnlyList<Live2DLayout> Layouts = new[]
    {
        new Live2DLayout { Id = "full", TitleKey = "live2d.layout.full", WidthRatio = 0.46, HeightRatio = 0.86, AlignX = 0.5, AlignY = 0.5 },
        new Live2DLayout { Id = "half", TitleKey = "live2d.layout.half", WidthRatio = 0.42, HeightRatio = 0.62, AlignX = 0.5, AlignY = 1.0, BottomInset = 0.02 },
        new Live2DLayout { Id = "right", TitleKey = "live2d.layout.right", WidthRatio = 0.34, HeightRatio = 0.56, AlignX = 0.94, AlignY = 1.0, BottomInset = 0.02 },
        new Live2DLayout { Id = "left", TitleKey = "live2d.layout.left", WidthRatio = 0.34, HeightRatio = 0.56, AlignX = 0.06, AlignY = 1.0, BottomInset = 0.02 }
    };

    /// <summary>按 id 取布局；未知 id 回退「半身」。</summary>
    public static Live2DLayout ResolveLayout(string? id)
        => Layouts.FirstOrDefault(l => string.Equals(l.Id, id, StringComparison.OrdinalIgnoreCase))
           ?? Layouts[1];

    /// <summary>
    /// 计算 OBS 里的变换：对齐点 + 尺寸，全部按画布比例算，横屏 / 竖屏都能用。
    /// </summary>
    public static Live2DTransform ComputeTransform(string? layoutId, int canvasWidth, int canvasHeight)
    {
        var layout = ResolveLayout(layoutId);
        var w = Math.Max(1, canvasWidth);
        var h = Math.Max(1, canvasHeight);

        var width = Math.Round(w * layout.WidthRatio, 1);
        var height = Math.Round(h * layout.HeightRatio, 1);

        var posX = Math.Round((w - width) * layout.AlignX, 1);
        var posY = Math.Round((h - height) * layout.AlignY - h * layout.BottomInset, 1);

        // 不允许整块跑出画布（布局参数写错时至少还能看见一角，而不是「配置成功但画面全黑」）
        posX = Math.Clamp(posX, -width * 0.5, w - width * 0.5);
        posY = Math.Clamp(posY, -height * 0.5, h - height * 0.5);

        return new Live2DTransform(posX, posY, width, height);
    }

    /// <summary>
    /// 校验并读取模型清单。失败时返回 false 并给出稳定错误码（<c>L2D_*</c>），界面据此取文案。
    /// </summary>
    public static bool TryValidate(string? modelJsonPath, out Live2DModelInfo? model, out string errorCode)
    {
        model = null;
        errorCode = "";

        if (string.IsNullOrWhiteSpace(modelJsonPath) || !File.Exists(modelJsonPath))
        {
            errorCode = "L2D_MODEL_NOT_FOUND";
            return false;
        }

        var fullPath = Path.GetFullPath(modelJsonPath);
        if (!fullPath.EndsWith(".model3.json", StringComparison.OrdinalIgnoreCase))
        {
            errorCode = "L2D_MODEL_INVALID";
            return false;
        }

        var root = Path.GetDirectoryName(fullPath) ?? "";
        if (root.Length == 0)
        {
            errorCode = "L2D_MODEL_INVALID";
            return false;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(File.ReadAllText(fullPath), new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
                MaxDepth = 64
            });
        }
        catch (JsonException)
        {
            errorCode = "L2D_MODEL_INVALID";
            return false;
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("FileReferences", out var refs)
                || refs.ValueKind != JsonValueKind.Object)
            {
                errorCode = "L2D_MODEL_INVALID";
                return false;
            }

            if (!refs.TryGetProperty("Moc", out var mocEl) || mocEl.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(mocEl.GetString()))
            {
                errorCode = "L2D_MODEL_INVALID";
                return false;
            }

            var referenced = new List<string>();
            var textures = new List<string>();

            // 必填：.moc3
            if (!TryResolveInside(root, mocEl.GetString(), out var mocPath, out var pathError))
            {
                errorCode = pathError;
                return false;
            }
            if (!File.Exists(mocPath))
            {
                errorCode = "L2D_MODEL_INVALID";
                return false;
            }

            // 纹理
            if (refs.TryGetProperty("Textures", out var texEl) && texEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in texEl.EnumerateArray())
                {
                    if (t.ValueKind != JsonValueKind.String) continue;
                    if (!TryResolveInside(root, t.GetString(), out var texPath, out pathError))
                    {
                        errorCode = pathError;
                        return false;
                    }
                    if (!File.Exists(texPath) || !IsImage(texPath))
                    {
                        errorCode = "L2D_MODEL_INVALID";
                        return false;
                    }
                    textures.Add(texPath);
                    referenced.Add(texPath);
                }
            }

            if (textures.Count == 0)
            {
                errorCode = "L2D_MODEL_INVALID";
                return false;
            }

            // 物理 / 姿势 / 表情 / 用户数据
            foreach (var key in new[] { "Physics", "Pose", "UserData", "DisplayInfo" })
            {
                if (!refs.TryGetProperty(key, out var el) || el.ValueKind != JsonValueKind.String) continue;
                if (!CollectFile(el.GetString(), root, referenced, ref errorCode)) return false;
            }

            // 表情：数组，条目直接是路径
            if (refs.TryGetProperty("Expressions", out var expEl) && expEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in expEl.EnumerateArray())
                {
                    if (e.ValueKind != JsonValueKind.Object) continue;
                    if (!e.TryGetProperty("File", out var fileEl) || fileEl.ValueKind != JsonValueKind.String) continue;
                    if (!CollectFile(fileEl.GetString(), root, referenced, ref errorCode)) return false;
                }
            }

            // 动作：{ "Idle": [ { "File": "...", "Sound": "..." } ] }
            if (refs.TryGetProperty("Motions", out var motionsEl) && motionsEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var group in motionsEl.EnumerateObject())
                {
                    if (group.Value.ValueKind != JsonValueKind.Array) continue;
                    foreach (var m in group.Value.EnumerateArray())
                    {
                        if (m.ValueKind != JsonValueKind.Object) continue;
                        if (m.TryGetProperty("File", out var f) && f.ValueKind == JsonValueKind.String
                            && !CollectFile(f.GetString(), root, referenced, ref errorCode)) return false;
                        if (m.TryGetProperty("Sound", out var s) && s.ValueKind == JsonValueKind.String
                            && !CollectFile(s.GetString(), root, referenced, ref errorCode)) return false;
                    }
                }
            }

            var (hash, bytes, count, manifestError) = ComputeManifest(root);
            if (manifestError.Length > 0)
            {
                errorCode = manifestError;
                return false;
            }

            var name = Path.GetFileName(root);
            if (string.IsNullOrWhiteSpace(name)) name = Path.GetFileNameWithoutExtension(fullPath);

            model = new Live2DModelInfo
            {
                ModelJsonPath = fullPath,
                RootDirectory = root,
                Name = name,
                MocPath = mocPath,
                Textures = textures,
                ReferencedFiles = referenced.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                ManifestHash = hash,
                TotalBytes = bytes,
                FileCount = count
            };
            return true;
        }
    }

    /// <summary>把相对引用解析到模型根内；越界 / 绝对路径 / 危险扩展名分别返回不同错误码。</summary>
    private static bool TryResolveInside(string root, string? relative, out string fullPath, out string errorCode)
    {
        fullPath = "";
        errorCode = "";

        if (string.IsNullOrWhiteSpace(relative))
        {
            errorCode = "L2D_MODEL_INVALID";
            return false;
        }

        var rel = relative.Replace('\\', '/').Trim();
        if (rel.StartsWith('/') || rel.Contains(':') || rel.Split('/').Any(p => p == ".."))
        {
            errorCode = "L2D_MODEL_PATH_TRAVERSAL";
            return false;
        }

        var combined = Path.GetFullPath(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!combined.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
        {
            errorCode = "L2D_MODEL_PATH_TRAVERSAL";
            return false;
        }

        var ext = Path.GetExtension(rel);
        if (DangerousExtensions.Contains(ext))
        {
            errorCode = "L2D_MODEL_UNSAFE_FILE";
            return false;
        }

        // .motion3.json 这类双扩展名：Path.GetExtension 只给 .json，整体仍允许
        if (!AllowedExtensions.Contains(ext) && !rel.EndsWith(".motion3.json", StringComparison.OrdinalIgnoreCase))
        {
            errorCode = "L2D_MODEL_UNSAFE_FILE";
            return false;
        }

        fullPath = combined;
        return true;
    }

    private static bool CollectFile(string? relative, string root, List<string> into, ref string errorCode)
    {
        if (string.IsNullOrWhiteSpace(relative)) return true;
        if (!TryResolveInside(root, relative, out var full, out errorCode)) return false;
        if (!File.Exists(full))
        {
            errorCode = "L2D_MODEL_INVALID";
            return false;
        }
        into.Add(full);
        return true;
    }

    private static bool IsImage(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".webp", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 目录清单：按相对路径排序后逐个喂进 SHA-256（相对路径 + 长度 + 内容）。
    /// 这样即使用户把模型移动到别的磁盘，哈希仍然稳定；同名不同文件也不会撞。
    /// </summary>
    public static (string Hash, long TotalBytes, int FileCount, string Error) ComputeManifest(string root)
    {
        try
        {
            if (!Directory.Exists(root)) return ("", 0, 0, "L2D_MODEL_NOT_FOUND");

            var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith(".obshelper-tmp", StringComparison.OrdinalIgnoreCase))
                .Select(f => (Full: f, Rel: Path.GetRelativePath(root, f).Replace('\\', '/')))
                .OrderBy(x => x.Rel, StringComparer.Ordinal)
                .ToList();

            if (files.Count > MaxFiles) return ("", 0, 0, "L2D_MODEL_TOO_LARGE");

            using var sha = SHA256.Create();
            long total = 0;
            using var buffer = new MemoryStream(64 * 1024);

            foreach (var (full, rel) in files)
            {
                var info = new FileInfo(full);
                if (info.Length > MaxTotalBytes) return ("", 0, 0, "L2D_MODEL_TOO_LARGE");
                total += info.Length;
                if (total > MaxTotalBytes) return ("", 0, 0, "L2D_MODEL_TOO_LARGE");

                // 危险扩展名即使未被引用也不接受（避免整目录里混入可执行文件）
                if (DangerousExtensions.Contains(info.Extension)) return ("", 0, 0, "L2D_MODEL_UNSAFE_FILE");

                var nameBytes = System.Text.Encoding.UTF8.GetBytes($"{rel}\0{info.Length}\0");
                sha.TransformBlock(nameBytes, 0, nameBytes.Length, null, 0);

                using var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
                int read;
                while ((read = fs.Read(buffer.GetBuffer(), 0, (int)buffer.Capacity)) > 0)
                    sha.TransformBlock(buffer.GetBuffer(), 0, read, null, 0);
            }

            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            var hash = Convert.ToHexString(sha.Hash ?? Array.Empty<byte>()).ToLowerInvariant();
            return (hash, total, files.Count, "");
        }
        catch (Exception)
        {
            return ("", 0, 0, "L2D_MODEL_READ_FAILED");
        }
    }

    /// <summary>短哈希（用于托管目录名 &lt;slug&gt;-&lt;hash8&gt;）。</summary>
    public static string ShortHash(string? hash)
        => string.IsNullOrWhiteSpace(hash) ? "00000000" : hash!.Length >= 8 ? hash[..8] : hash.PadRight(8, '0');

    /// <summary>目录名净化：只保留字母数字与 - _，避免把用户目录名拼出非法路径。</summary>
    public static string SafeSlug(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "model";
        var chars = name.Trim().Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray();
        var slug = new string(chars).Trim('-');
        return slug.Length == 0 ? "model" : slug.Length > 40 ? slug[..40] : slug;
    }
}
