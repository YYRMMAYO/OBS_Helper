namespace OBS_Helper.Wpf.Services.Compat;

/// <summary>
/// 「选择一个文件夹」的跨版本实现（V2.9.3）。
///
/// WPF 自带的 <c>Microsoft.Win32.OpenFolderDialog</c> 是 **.NET 8 才加入**的；
/// Windows 7 兼容构建跑在 net6.0-windows 上，没有这个类型。这里按目标框架分两条分支：
/// <list type="bullet">
///   <item>net8.0+：用 WPF 自家的 <c>OpenFolderDialog</c>（视觉与主程序一致）；</item>
///   <item>net6.0：退到 WinForms 的 <c>FolderBrowserDialog</c>（本工程已启用 UseWindowsForms，
///         托盘图标早就在用它，不引入新依赖）。</item>
/// </list>
/// 两条分支都返回「选中的绝对路径 / 取消时 null」，调用方无感知。
/// </summary>
internal static class FolderPicker
{
    public static string? Pick(string? title)
    {
#if NET8_0_OR_GREATER
        var dialog = new Microsoft.Win32.OpenFolderDialog();
        if (!string.IsNullOrEmpty(title)) dialog.Title = title;
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
#else
        using var dialog = new System.Windows.Forms.FolderBrowserDialog();
        if (!string.IsNullOrEmpty(title)) dialog.Description = title;
        return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK
            ? dialog.SelectedPath
            : null;
#endif
    }
}
