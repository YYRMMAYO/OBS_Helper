using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using OBS_Helper.Wpf.Services;
using OBS_Helper.Wpf.Services.Appearance;

namespace OBS_Helper.Wpf.Controls;

/// <summary>
/// 内容区壁纸渲染层（V3.1.0）。
///
/// 只做渲染：读取 <see cref="WallpaperService"/> 的当前图与显示参数，负责
/// 填充 / 适应 / 居中 / 平铺、压暗、模糊与两层图片的交叉淡入。
/// 「减少动画」开启时不做淡入，直接换图（尊重前庭功能障碍用户）。
/// </summary>
public partial class WallpaperLayer : UserControl
{
    private bool _hooked;
    private ImageSource? _shown;
    private bool _animating;

    public WallpaperLayer()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_hooked) return;
        _hooked = true;

        // 主题 / 压暗 / 减少动画变化都会走 Appearance.Changed；壁纸切换走 Wallpapers.Changed
        AppServices.Appearance.Changed += Render;
        if (AppServices.WallpapersOrNull() is { } wallpapers)
        {
            wallpapers.Changed += Render;
            wallpapers.LibraryChanged += Render;
        }
        Render();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (!_hooked) return;
        _hooked = false;
        try { AppServices.Appearance.Changed -= Render; } catch (Exception) { /* 关闭阶段忽略 */ }
        if (AppServices.WallpapersOrNull() is { } wallpapers)
        {
            wallpapers.Changed -= Render;
            wallpapers.LibraryChanged -= Render;
        }
    }

    private void Render()
    {
        try
        {
            var appearance = AppServices.Appearance;
            var wallpapers = AppServices.WallpapersOrNull();

            // 高对比 / 非图片模式：完全隐藏壁纸层，只留主题底色
            if (appearance.HighContrastEffective
                || !string.Equals(appearance.Settings.BackgroundMode, "image", StringComparison.Ordinal)
                || wallpapers?.CurrentImage is null)
            {
                ResetToBase();
                return;
            }

            var fit = wallpapers.Fit;
            var blur = wallpapers.EffectiveBlur;
            var image = wallpapers.CurrentImage;

            ApplyFit(fit);
            ApplyBlur(blur);

            if (ReferenceEquals(_shown, image))
            {
                UpdateDim(wallpapers);
                return;
            }

            if (_shown is null || appearance.Settings.ReduceMotion || _animating)
            {
                // 首次显示 / 减少动画：直接切换
                ImageA.BeginAnimation(OpacityProperty, null);
                ImageB.BeginAnimation(OpacityProperty, null);
                ImageA.Opacity = 1;
                ImageB.Opacity = 0;
                ImageA.Source = image;
                ImageB.Source = null;
                _shown = image;
                UpdateDim(wallpapers);
                return;
            }

            // 交叉淡入：B 淡入，结束后把 A 换成新图并复位 B
            _animating = true;
            ImageB.Source = image;
            ImageB.Opacity = 0;
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            fade.Completed += (_, _) =>
            {
                ImageA.Source = image;
                ImageA.Opacity = 1;
                ImageB.BeginAnimation(OpacityProperty, null);
                ImageB.Opacity = 0;
                ImageB.Source = null;
                _shown = image;
                _animating = false;
                UpdateDim(wallpapers);
            };
            ImageB.BeginAnimation(OpacityProperty, fade);
            UpdateDim(wallpapers);
        }
        catch (Exception)
        {
            // 渲染失败不能让主界面崩：退回主题底色
            ResetToBase();
        }
    }

    private void ResetToBase()
    {
        _animating = false;
        _shown = null;
        ImageA.BeginAnimation(OpacityProperty, null);
        ImageB.BeginAnimation(OpacityProperty, null);
        ImageA.Source = null;
        ImageB.Source = null;
        ImageA.Effect = null;
        ImageB.Effect = null;
        TileLayer.Fill = null;
        TileLayer.Visibility = Visibility.Collapsed;
        ImageA.Visibility = Visibility.Visible;
        ImageB.Visibility = Visibility.Visible;
    }

    private void ApplyFit(WallpaperFit fit)
    {
        if (fit == WallpaperFit.Tile)
        {
            ImageA.Visibility = Visibility.Collapsed;
            ImageB.Visibility = Visibility.Collapsed;
            TileLayer.Visibility = Visibility.Visible;
            TileLayer.Fill = BuildTileBrush(_shown ?? ImageA.Source ?? ImageB.Source);
            return;
        }

        if (TileLayer.Visibility != Visibility.Visible) return;

        // 从平铺切回单图：重新构一次画笔，避免残留
        TileLayer.Fill = null;
        TileLayer.Visibility = Visibility.Collapsed;
        ImageA.Visibility = Visibility.Visible;
        ImageB.Visibility = Visibility.Visible;
    }

    private void ApplyBlur(double blur)
    {
        if (blur <= 0)
        {
            ImageA.Effect = null;
            ImageB.Effect = null;
            return;
        }

        var effect = new BlurEffect { Radius = blur * 0.5, KernelType = KernelType.Gaussian };
        ImageA.Effect = effect;
        ImageB.Effect = effect;
    }

    private void UpdateDim(WallpaperService wallpapers)
    {
        // 资源里的 ContentBackgroundDim 已由服务按主题算好；这里只兜底同步透明度
        var dim = wallpapers.EffectiveDim;
        if (DimLayer.Background is SolidColorBrush brush && brush.Color.A == 0 && dim > 0)
        {
            var b = new SolidColorBrush(Color.FromArgb((byte)Math.Round(dim * 255), 0, 0, 0));
            b.Freeze();
            DimLayer.Background = b;
        }
    }

    private static Brush? BuildTileBrush(ImageSource? image)
    {
        if (image is not BitmapSource bmp || bmp.PixelWidth <= 0) return null;
        var brush = new ImageBrush(bmp)
        {
            TileMode = TileMode.Tile,
            Stretch = Stretch.None,
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top,
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, bmp.PixelWidth, bmp.PixelHeight)
        };
        brush.Freeze();
        return brush;
    }
}
