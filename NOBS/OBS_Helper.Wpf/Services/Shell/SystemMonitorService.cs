using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace OBS_Helper.Wpf.Services.Shell;

/// <summary>一次系统资源采样。</summary>
public sealed class SystemSample
{
    public DateTime At { get; init; } = DateTime.Now;
    public double CpuPercent { get; init; }
    public double MemUsedMb { get; init; }
    public double MemTotalMb { get; init; }
    public double MemUsedPercent => MemTotalMb > 0 ? MemUsedMb / MemTotalMb * 100.0 : 0;
    public double NetDownKbps { get; init; }
    public double NetUpKbps { get; init; }
    public IReadOnlyList<DiskSample> Disks { get; init; } = Array.Empty<DiskSample>();

    /// <summary>所有磁盘中剩余空间最小的一块（用于磁盘预警）。</summary>
    public DiskSample? LowestDisk => Disks.OrderBy(d => d.FreeGb).FirstOrDefault();
}

public sealed class DiskSample
{
    public string Name { get; init; } = "";
    public double TotalGb { get; init; }
    public double FreeGb { get; init; }
    public double FreePercent => TotalGb > 0 ? FreeGb / TotalGb * 100.0 : 0;
}

/// <summary>
/// 系统资源监控：CPU / 内存 / 网络上下行速率 / 磁盘剩余空间，每 1 秒采样一次。
///
/// 全本地实现：
/// <list type="bullet">
///   <item>CPU 用 <see cref="PerformanceCounter"/>（% Processor Time _Total）；</item>
///   <item>内存用 GlobalMemoryStatusEx（总量 / 可用）；</item>
///   <item>网络用 <see cref="NetworkInterface"/> 计数器差值算实时速率；</item>
///   <item>磁盘用 <see cref="DiskProbe"/> 枚举固定盘。</item>
/// </list>
/// 任一指标读取失败（权限 / 无计数器）自动降级为 0，不影响其它指标与整体功能。
///
/// 磁盘预警不在此服务内做（避免 Stop 时连带停掉预警），由 <see cref="TrayService"/>
/// 用独立低频定时器承担，见 <see cref="TrayService.StartDiskWarning"/>。
/// </summary>
public sealed class SystemMonitorService : IDisposable
{
    private const int MaxHistory = 120;          // 1s × 120 = 2 分钟曲线
    private const int NetworkRefreshEvery = 10;  // 每 10 次采样刷新一次网卡列表

    private readonly DispatcherTimer _timer;
    private readonly List<SystemSample> _history = new();

    private PerformanceCounter? _cpuCounter;
    private List<(long Recv, long Sent, DateTime At)> _netPoints = new();
    private List<NetworkInterface> _netInterfaces = new();
    private int _tick;

    public SystemMonitorService()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTick;
    }

    public event Action? SampleReady;

    /// <summary>最近一次采样。</summary>
    public SystemSample? Latest { get; private set; }

    /// <summary>历史采样（最多 2 分钟），供曲线绘制。</summary>
    public IReadOnlyList<SystemSample> History => _history;

    public bool IsRunning => _timer.IsEnabled;

    public void Start()
    {
        if (_timer.IsEnabled) return;
        try
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _ = _cpuCounter.NextValue();   // 预热：首次读取恒为 0
        }
        catch (Exception)
        {
            _cpuCounter = null;            // 无计数器权限等：CPU 降级为 0
        }
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        _cpuCounter?.Dispose();
        _cpuCounter = null;
    }

    public void Dispose() => Stop();

    // ------------------------------------------------------------ 采样

    private void OnTick(object? sender, EventArgs e)
    {
        var disks = DiskProbe.Sample();
        var sample = new SystemSample
        {
            CpuPercent = SampleCpu(),
            MemUsedMb = SampleMemUsedMb(),
            MemTotalMb = SampleMemTotalMb(),
            NetDownKbps = SampleNetwork(out var up),
            NetUpKbps = up,
            Disks = disks
        };

        Latest = sample;
        _history.Add(sample);
        if (_history.Count > MaxHistory) _history.RemoveAt(0);

        _tick++;
        SampleReady?.Invoke();
    }

    private double SampleCpu()
    {
        try { return _cpuCounter?.NextValue() ?? 0; }
        catch (Exception) { return 0; }
    }

    private double SampleMemTotalMb() => GetMemoryStatus()?.ullTotalPhys / 1024.0 / 1024.0 ?? 0;

    private double SampleMemUsedMb()
    {
        var m = GetMemoryStatus();
        return m is null ? 0 : (m.ullTotalPhys - m.ullAvailPhys) / 1024.0 / 1024.0;
    }

    private double SampleNetwork(out double upKbps)
    {
        upKbps = 0;
        try
        {
            if (_tick % NetworkRefreshEvery == 0 || _netInterfaces.Count == 0)
            {
                _netInterfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up
                                && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                                && !n.Name.StartsWith("Loopback", StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            long recv = 0, sent = 0;
            foreach (var ni in _netInterfaces)
            {
                var stats = ni.GetIPv4Statistics();
                recv += stats.BytesReceived;
                sent += stats.BytesSent;
            }

            var now = DateTime.UtcNow;
            if (_netPoints.Count > 0)
            {
                var prev = _netPoints[^1];
                var secs = Math.Max(0.001, (now - prev.At).TotalSeconds);
                var down = Math.Max(0, recv - prev.Recv) / secs * 8 / 1024;   // Kbps
                var up = Math.Max(0, sent - prev.Sent) / secs * 8 / 1024;
                _netPoints.Add((recv, sent, now));
                if (_netPoints.Count > 4) _netPoints.RemoveAt(0);
                upKbps = up;
                return down;
            }

            _netPoints.Add((recv, sent, now));
            return 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    // ------------------------------------------------------------ 内存

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint dwLength = (uint)Marshal.SizeOf(typeof(MemoryStatusEx));
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(MemoryStatusEx lpBuffer);

    private static MemoryStatusEx? GetMemoryStatus()
    {
        try
        {
            var m = new MemoryStatusEx();
            return GlobalMemoryStatusEx(m) ? m : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
