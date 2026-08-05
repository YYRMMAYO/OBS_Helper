using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace OBS_Helper.Wpf.Controls;

/// <summary>
/// 监控指标 → 语义状态色画刷（对应 P2「指标无异常聚焦色」）。
///
/// 配合 ConverterParameter 指定指标类型，阈值为：
///   cpu / mem （百分比 0–100）：&lt;70 正常(Ok) / &lt;90 警告(Warn) / 其余 危险(Danger)
///   disk      （剩余 GB）     ：&lt;10 危险 / &lt;20 警告 / 其余 正常
///   skip      （丢帧率 %）    ：&lt;1  正常 / &lt;5  警告 / 其余 危险
///   net       （下行 Kbps）   ：暂不分级，统一品牌色
///
/// 用法：
///   &lt;TextBlock Foreground="{Binding CpuPercent, Converter={StaticResource MetricStatusBrush}, ConverterParameter=cpu}" /&gt;
/// 或在 code-behind 中调用（见 PerformancePage 的 SetMetricColor 辅助方法）。
/// </summary>
public sealed class MetricStatusBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = "BrandBrush";
        if (value is double v && parameter is string kind)
        {
            key = kind.Trim().ToLowerInvariant() switch
            {
                "cpu" or "mem" => v < 70 ? "OkBrush" : v < 90 ? "WarnBrush" : "DangerBrush",
                "disk" => v < 10 ? "DangerBrush" : v < 20 ? "WarnBrush" : "OkBrush",
                "skip" => v < 1 ? "OkBrush" : v < 5 ? "WarnBrush" : "DangerBrush",
                _ => "BrandBrush"
            };
        }

        return Application.Current?.TryFindResource(key)
               ?? Application.Current?.TryFindResource("BrandBrush")!;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
