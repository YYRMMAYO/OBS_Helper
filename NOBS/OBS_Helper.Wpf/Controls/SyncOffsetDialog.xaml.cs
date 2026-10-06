using System.Windows;
using System.Windows.Controls;

namespace OBS_Helper.Wpf.Controls;

/// <summary>
/// 音画同步偏移对话框（V3.0 / D7）。
///
/// 返回用户确认的毫秒值（正数 = 声音延后，负数 = 声音提前）；取消返回 null。
/// </summary>
public partial class SyncOffsetDialog : Window
{
    private SyncOffsetDialog(string inputName, int currentMs)
    {
        InitializeComponent();

        Title = Strings.T("sync.title");
        HeaderText.Text = Strings.T("sync.header", inputName);
        OffsetSlider.Value = Math.Clamp(currentMs, -500, 500);
        UpdateValueText();
    }

    /// <summary>用户确认的偏移（毫秒）。</summary>
    public int ResultMs { get; private set; }

    /// <summary>
    /// 打开对话框。取消时返回 null（调用方据此不做任何写操作）。
    /// </summary>
    public static int? Show(Window? owner, string inputName, int currentMs)
    {
        var dlg = new SyncOffsetDialog(inputName, currentMs) { Owner = owner };
        // 无障碍：焦点落在滑块上，键盘用户可以直接用方向键微调
        dlg.Loaded += (_, _) => dlg.OffsetSlider.Focus();
        return dlg.ShowDialog() == true ? dlg.ResultMs : null;
    }

    private void OnSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateValueText();

    private void UpdateValueText()
        => ValueText.Text = FormatMs((int)Math.Round(OffsetSlider.Value));

    private static string FormatMs(int ms)
        => ms == 0 ? Strings.T("sync.zero") : Strings.T("sync.valueMs", ms > 0 ? "+" + ms : ms.ToString());

    private void OnPreset(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string raw } && int.TryParse(raw, out var ms))
            OffsetSlider.Value = ms;
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        ResultMs = (int)Math.Round(OffsetSlider.Value);
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
