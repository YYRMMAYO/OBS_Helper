using System.IO;

namespace OBS_Helper.Wpf.Services.Shell;

/// <summary>
/// 磁盘剩余空间探测：监控页曲线与托盘磁盘预警共用。
/// 任一磁盘读取失败自动跳过，整体失败返回空列表，不影响调用方。
/// </summary>
public static class DiskProbe
{
    /// <summary>枚举固定磁盘剩余空间（GB）。单个盘失败跳过，枚举失败返回空列表。</summary>
    public static IReadOnlyList<DiskSample> Sample()
    {
        var list = new List<DiskSample>();
        try
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                if (d.DriveType != DriveType.Fixed) continue;
                try
                {
                    if (!d.IsReady) continue;
                    list.Add(new DiskSample
                    {
                        Name = d.Name.TrimEnd('\\'),
                        TotalGb = d.TotalSize / 1024.0 / 1024.0 / 1024.0,
                        FreeGb = d.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0
                    });
                }
                catch (Exception)
                {
                    // 单个盘读取失败（权限 / 已弹出）：跳过该盘，不影响其它盘
                }
            }
        }
        catch (Exception)
        {
            // 磁盘枚举失败（权限 / 系统限制）：返回空列表，调用方自行降级
        }
        return list;
    }
}
