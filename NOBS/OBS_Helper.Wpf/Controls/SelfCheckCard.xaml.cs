using System.Windows;
using System.Windows.Controls;

namespace OBS_Helper.Wpf.Controls;

/// <summary>
/// 页面顶部的统一自检卡：一个按钮跑本页检查，结果逐行列出「正常 / 需配置 / 不可用」。
/// 四个新页面共用同一套视觉与交互，避免每个页面各写一套。
/// </summary>
public partial class SelfCheckCard : UserControl
{
    public SelfCheckCard() => InitializeComponent();

    public event RoutedEventHandler? RunRequested;

    public void SetResult(string text)
    {
        ResultText.Text = text;
        ResultText.Visibility = string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    public void SetBusy(bool busy)
    {
        RunButton.IsEnabled = !busy;
        if (busy) ResultText.Text = Strings.T("common.busy");
    }

    private void OnRunClick(object sender, RoutedEventArgs e) => RunRequested?.Invoke(this, e);
}
