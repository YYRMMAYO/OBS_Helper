using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace OBS_Helper.Wpf.Controls;

/// <summary>true → Visible，false → Collapsed。</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility.Visible;
}

/// <summary>true → Collapsed，false → Visible。</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not Visibility.Visible;
}

/// <summary>布尔取反。</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not true;
}

/// <summary>非空（非 null / 非空字符串 / 非空集合）→ Visible。</summary>
public sealed class NotEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => IsEmpty(value) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;

    internal static bool IsEmpty(object? value) => value switch
    {
        null => true,
        string s => string.IsNullOrWhiteSpace(s),
        ICollection c => c.Count == 0,
        int i => i == 0,
        _ => false
    };
}

/// <summary>为空 → Visible（用于空状态占位）。</summary>
public sealed class EmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => NotEmptyToVisibilityConverter.IsEmpty(value) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>
/// 值与参数相等 → true。用于单选组（主题 / 字号档位）的选中态绑定：
/// <c>IsChecked="{Binding Theme, Converter={StaticResource EqualsConverter}, ConverterParameter=Dark}"</c>
/// </summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true && parameter is not null ? parameter : Binding.DoNothing;
}

/// <summary>严重度文案 → 语义色画刷键名（常见/严重 → Danger，一般 → Warn，其余 → Info）。</summary>
public sealed class SeverityToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = (value?.ToString() ?? "").Trim() switch
        {
            "严重" => "DangerBrush",
            "常见" => "WarnBrush",
            "一般" => "InfoBrush",
            "进阶" => "BrandBrush",
            _ => "MutedBrush"
        };
        return Application.Current?.TryFindResource(key) ?? Application.Current?.TryFindResource("MutedBrush")!;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>严重度文案 → 浅色底画刷。</summary>
public sealed class SeverityToSoftBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = (value?.ToString() ?? "").Trim() switch
        {
            "严重" => "DangerSoftBrush",
            "常见" => "WarnSoftBrush",
            "一般" => "InfoSoftBrush",
            "进阶" => "BrandSoftBrush",
            _ => "Surface3Brush"
        };
        return Application.Current?.TryFindResource(key) ?? Application.Current?.TryFindResource("Surface3Brush")!;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>把十六进制色串（problems.json 里的 category.color）转成画刷；无效值回退品牌色。</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hex = value?.ToString();
        if (!string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
                var brush = new System.Windows.Media.SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }
            catch (Exception)
            {
                // 色值格式不合法：回退品牌色。
            }
        }
        return Application.Current?.TryFindResource("BrandBrush")!;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>字符串列表 → 顿号连接的一行文本。</summary>
public sealed class JoinConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var sep = parameter?.ToString() ?? "、";
        return value is IEnumerable<string> list ? string.Join(sep, list) : (value?.ToString() ?? "");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>序号转换：集合索引（0 基）→ 显示用的 1 基编号。</summary>
public sealed class IndexToNumberConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int i ? (i + 1).ToString(CultureInfo.InvariantCulture) : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
