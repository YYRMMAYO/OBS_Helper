using System.Windows;
using System.Windows.Media;

namespace OBS_Helper.Wpf.Controls;

/// <summary>
/// 迷你折线图：把一串数值画成一条折线。零依赖、纯自绘（StreamGeometry）。
/// 用于性能监控页的 CPU / 内存 / 网络曲线。
/// </summary>
public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<double>), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaxValueProperty = DependencyProperty.Register(
        nameof(MaxValue), typeof(double), typeof(Sparkline),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>要绘制的数值序列。</summary>
    public IReadOnlyList<double>? Values
    {
        get => (IReadOnlyList<double>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    /// <summary>折线颜色。</summary>
    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    /// <summary>纵轴上限；0 表示按数据最大值自适应。</summary>
    public double MaxValue
    {
        get => (double)GetValue(MaxValueProperty);
        set => SetValue(MaxValueProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var values = Values;
        if (values is null || values.Count < 2) return;

        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        double max = MaxValue > 0 ? MaxValue : 0;
        if (max <= 0)
        {
            foreach (var v in values)
            {
                if (v > max) max = v;
            }
            if (max <= 0) max = 1;
        }

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            var first = true;
            for (int i = 0; i < values.Count; i++)
            {
                var x = w * i / (values.Count - 1);
                var y = h - h * Math.Clamp(values[i], 0, max) / max;
                if (first)
                {
                    ctx.BeginFigure(new Point(x, y), isFilled: false, isClosed: false);
                    first = false;
                }
                else
                {
                    ctx.LineTo(new Point(x, y), isStroked: true, isSmoothJoin: false);
                }
            }
        }

        var pen = new Pen(Stroke, 1.5)
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        dc.DrawGeometry(null, pen, geo);
    }
}
