using System.IO;
using System.Text;

namespace OBS_Helper.Wpf.Services.ObsConfig;

/// <summary>
/// 对 OBS 配置文件（<c>basic.ini</c> 一类）的**受护栏保护的原子写入**（V3.0）。
///
/// 为什么要有这个类：写 OBS 配置的路径原先各写各的 —— 有的直接 <c>File.WriteAllText</c>（既不过
/// <see cref="ObsSafePath"/> 护栏，也不是原子替换，写一半断电就留半截 ini），有的连护栏都没有。
/// 这里把「先过护栏 → 同目录临时文件 → 原子替换」固定成唯一写法。
///
/// 用法：<c>SafeIniFile.Write(path, text, configDir)</c>，<paramref name="allowedRoot"/> 传 OBS 配置根目录。
/// </summary>
internal static class SafeIniFile
{
    /// <summary>同目录备份后缀（写前留一份原样内容，成本几乎为零）。</summary>
    public const string BackupSuffix = ".obshelper.bak";

    /// <summary>
    /// 写前先留 <c>.obshelper.bak</c>，再过护栏做原子替换。
    /// 任一步失败都抛异常（由调用方转成「写入失败」提示），**绝不半途留下截断的文件**。
    /// </summary>
    public static void Write(string path, string text, string allowedRoot)
    {
        ObsSafePath.AssertWritable(path, allowedRoot);

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        // 1) 原样备份（原文件不存在时跳过：新建文件没有「原内容」可备份）
        if (File.Exists(path))
        {
            try { File.WriteAllText(path + BackupSuffix, File.ReadAllText(path), new UTF8Encoding(false)); }
            catch (Exception) { /* 备份失败不阻断主写入：外层还有整份配置 zip 备份与回滚表 */ }
        }

        // 2) 同目录临时文件 + 原子替换（避免中途崩溃留下半截 ini）
        var tmp = path + ".obshelper.tmp";
        File.WriteAllText(tmp, text, new UTF8Encoding(false));
        try
        {
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }
        catch (Exception)
        {
            try { File.Delete(tmp); } catch (Exception) { /* 清理失败无妨 */ }
            throw;
        }
    }
}
