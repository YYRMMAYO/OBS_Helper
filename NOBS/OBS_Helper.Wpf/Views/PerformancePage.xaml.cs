using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OBS_Helper.Wpf.Controls;
using OBS_Helper.Wpf.Navigation;
using OBS_Helper.Wpf.Services.Shell;

namespace OBS_Helper.Wpf.Views;

/// <summary>
/// 系统资源监控页：CPU / 内存 / 网络 / 磁盘实时曲线 + OBS 渲染状态。
/// 数据来自 <see cref="SystemMonitorService"/>（1 秒采样）与 OBS 控制服务的统计。
/// </summary>
public partial class PerformancePage : UserControl, INavigationAware
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>监控指标 → 语义状态色（P2）。阈值见 <see cref="MetricStatusBrushConverter"/>。</summary>
    private static readonly MetricStatusBrushConverter MetricBrush = new();

    /// <summary>曲线最近展示的点数（2 分钟 × 1s）。</summary>
    private const int ChartPoints = 120;

    public PerformancePage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AppServices.SystemMonitor.SampleReady += OnSampleReady;
        AppServices.Obs.StateChanged += OnObsStateChanged;
        AppServices.SystemMonitor.Start();
        Render(AppServices.SystemMonitor);
        OnObsStateChanged();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        AppServices.SystemMonitor.SampleReady -= OnSampleReady;
        AppServices.Obs.StateChanged -= OnObsStateChanged;
    }

    public Task OnNavigatedToAsync(object? parameter)
    {
        Render(AppServices.SystemMonitor);
        OnObsStateChanged();
        return Task.CompletedTask;
    }

    private void OnSampleReady() => Dispatcher.BeginInvoke(new Action(() => Render(AppServices.SystemMonitor)));

    private void OnObsStateChanged() => Dispatcher.BeginInvoke(new Action(RenderObs));

    // ------------------------------------------------------------ 渲染

    private void Render(SystemMonitorService monitor)
    {
        var s = monitor.Latest;
        if (s is null)
        {
            CpuValueText.Text = "—";
            MemValueText.Text = "—";
            NetValueText.Text = "—";
            DiskValueText.Text = "—";
            return;
        }

        CpuValueText.Text = s.CpuPercent.ToString("0.0", Inv) + "%";
        MemValueText.Text = s.MemUsedPercent.ToString("0.0", Inv) + "%";
        NetValueText.Text = s.NetDownKbps >= 1024
            ? (s.NetDownKbps / 1024).ToString("0.0", Inv) + " Mbps"
            : s.NetDownKbps.ToString("0", Inv) + " Kbps";
        var disk = s.LowestDisk;
        DiskValueText.Text = disk is null ? "—" : disk.FreeGb.ToString("0.0", Inv) + " G";

        // 指标状态色：cpu/mem 低于 70 正常、90 以下警告、其余危险；磁盘剩余 <10G 危险、<20G 警告（P2）
        SetMetricColor(CpuValueText, "cpu", s.CpuPercent);
        SetMetricColor(MemValueText, "mem", s.MemUsedPercent);
        if (disk is not null)
            SetMetricColor(DiskValueText, "disk", disk.FreeGb);
        else
            DiskValueText.SetResourceReference(TextBlock.ForegroundProperty, "BrandBrush");
        // 网络下行暂不分级，保持品牌色。

        // 曲线：从历史取最近 N 点
        var hist = monitor.History;
        var n = Math.Min(ChartPoints, hist.Count);
        if (n > 0)
        {
            var cpu = new double[n];
            var mem = new double[n];
            var down = new double[n];
            var up = new double[n];
            for (int i = 0; i < n; i++)
            {
                var x = hist[hist.Count - n + i];
                cpu[i] = x.CpuPercent;
                mem[i] = x.MemUsedPercent;
                down[i] = x.NetDownKbps;
                up[i] = x.NetUpKbps;
            }
            CpuChart.Values = cpu;
            MemChart.Values = mem;
            NetDownChart.Values = down;
            NetUpChart.Values = up;

            CpuTrendValue.Text = cpu[^1].ToString("0.0", Inv) + "%";
            MemTrendValue.Text = mem[^1].ToString("0.0", Inv) + "%";
            NetDownTrendValue.Text = down[^1].ToString("0", Inv) + " Kbps";
            NetUpTrendValue.Text = up[^1].ToString("0", Inv) + " Kbps";
        }

        RenderDisks(s.Disks);
    }

    private void RenderDisks(IReadOnlyList<DiskSample> disks)
    {
        DisksPanel.Children.Clear();
        if (disks.Count == 0)
        {
            DisksPanel.Children.Add(new TextBlock
            {
                Text = "未检测到固定磁盘。",
                Style = (Style)FindResource("MutedText")
            });
            return;
        }

        foreach (var d in disks)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var name = new TextBlock
            {
                Text = d.Name,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 90
            };
            name.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            Grid.SetColumn(name, 0);

            // 进度条
            var bar = new ProgressBar
            {
                Height = 10,
                Minimum = 0,
                Maximum = 100,
                Value = Math.Clamp(100 - d.FreePercent, 0, 100),
                Margin = new Thickness(12, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(bar, 1);

            var text = new TextBlock
            {
                Text = $"剩余 {d.FreeGb:0.0} G / {d.TotalGb:0.0} G",
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 130,
                TextAlignment = TextAlignment.Right
            };
            text.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            text.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSm");
            Grid.SetColumn(text, 2);

            grid.Children.Add(name);
            grid.Children.Add(bar);
            grid.Children.Add(text);
            DisksPanel.Children.Add(grid);
        }
    }

    private void RenderObs()
    {
        var obs = AppServices.Obs;
        if (!obs.IsConnected)
        {
            ObsStatusText.Text = "未连接 OBS。连接后这里会显示渲染帧率、丢帧率与 OBS 报告的性能数据。";
            ObsFpsText.Text = "—";
            ObsRenderSkipText.Text = "—";
            ObsOutputSkipText.Text = "—";
            return;
        }

        ObsStatusText.Text = $"已连接 OBS {obs.Profile.ObsVersion}";
        ObsFpsText.Text = obs.Stats.ActiveFps.ToString("0.0", Inv);
        ObsRenderSkipText.Text = (obs.Stats.RenderSkipRatio * 100).ToString("0.##", Inv) + "%";
        ObsOutputSkipText.Text = (obs.Stats.OutputSkipRatio * 100).ToString("0.##", Inv) + "%";

        // 丢帧率状态色：<1% 正常、<5% 警告、其余危险（P2）
        SetMetricColor(ObsRenderSkipText, "skip", obs.Stats.RenderSkipRatio * 100);
        SetMetricColor(ObsOutputSkipText, "skip", obs.Stats.OutputSkipRatio * 100);
    }

    /// <summary>按指标类型与当前值设置语义状态色（正常/警告/危险）。</summary>
    private static void SetMetricColor(TextBlock tb, string kind, double value)
        => tb.Foreground = (Brush)MetricBrush.Convert(value, typeof(Brush), kind, CultureInfo.InvariantCulture);

    private void OnOpenDiagnostic(object sender, RoutedEventArgs e)
        => AppServices.Navigation.Navigate(Routes.Diagnostic);
}
