using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using OBS_Helper.Wpf.Navigation;
using OBS_Helper.Wpf.Services.Analytics;
using OBS_Helper.Wpf.Services.Mobile;
using OBS_Helper.Wpf.Services.Host;

namespace OBS_Helper.Wpf.Views;

/// <summary>智能与复盘页（S08 / S07 / E06）。</summary>
public partial class InsightsPage : UserControl, INavigationAware
{
    public InsightsPage() => InitializeComponent();

    public Task OnNavigatedToAsync(object? parameter)
    {
        var obs = AppServices.Obs;
        PeakBox.Text = "0";
        ChatBox.Text = "0";
        FollowBox.Text = "0";
        GiftBox.Text = "0";
        BitrateBox.Text = Math.Round(obs.StreamStatus.Bytes > 0 && obs.StreamElapsed.TotalSeconds > 0
            ? obs.StreamStatus.Bytes * 8 / 1000 / obs.StreamElapsed.TotalSeconds
            : 0).ToString();
        DropBox.Text = (obs.Stats.OutputSkipRatio * 100).ToString("0.0");
        return Task.CompletedTask;
    }

    private SessionMetrics ReadMetrics()
    {
        int Parse(TextBox box) => int.TryParse(box.Text.Trim(), out var value) ? Math.Max(0, value) : 0;
        double ParseDouble(TextBox box) => double.TryParse(box.Text.Trim(), out var value) ? Math.Max(0, value) : 0;
        return new SessionMetrics
        {
            Duration = AppServices.Obs.StreamElapsed > TimeSpan.Zero ? AppServices.Obs.StreamElapsed : AppServices.Obs.RecordElapsed,
            PeakViewers = Parse(PeakBox),
            ChatMessages = Parse(ChatBox),
            NewFollowers = Parse(FollowBox),
            GiftValue = Parse(GiftBox),
            AvgBitrateKbps = ParseDouble(BitrateBox),
            DroppedRatio = ParseDouble(DropBox) / 100.0,
            ClipCount = 0,
        };
    }

    private void OnGenerateReport(object sender, RoutedEventArgs e)
    {
        var metrics = ReadMetrics();
        var report = SessionCoachCore.BuildReport("本场直播", DateTimeOffset.Now, metrics);
        ReportText.Text = report;
        try
        {
            var dir = Path.Combine(HostBridge.AppDataDirectory, "analytics");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"review-{DateTime.Now:yyyyMMdd-HHmmss}.md");
            File.WriteAllText(path, report);
            AppServices.Toast.Show(Strings.T("insights.saved", path));
        }
        catch (Exception ex)
        {
            AppServices.Toast.Show(Strings.T("common.failed", ex.Message));
        }
    }

    private void OnRetention(object sender, RoutedEventArgs e)
    {
        var retention = AppServices.ViewerStats.Retention();
        ReportText.Text = Strings.T("insights.retention", retention);
    }

    private void OnExportStats(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "CSV|*.csv", FileName = "viewer-stats.csv" };
        if (dialog.ShowDialog() != true) return;
        File.WriteAllText(dialog.FileName, AppServices.ViewerStats.ExportCsv());
        AppServices.Toast.Show(Strings.T("common.saved"));
    }

    private void OnClearStats(object sender, RoutedEventArgs e)
    {
        AppServices.ViewerStats.Clear();
        ReportText.Text = "";
    }

    private void OnSuggest(object sender, RoutedEventArgs e)
    {
        CoverList.Items.Clear();
        foreach (var (title, topics) in CoverCopyCore.Suggest(GameBox.Text, KeywordBox.Text, MoodBox.Text))
            CoverList.Items.Add(title + "    [" + topics + "]");
    }
}
