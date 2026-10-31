using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using OBS_Helper.Wpf.Navigation;
using OBS_Helper.Wpf.Services;
using OBS_Helper.Wpf.Services.Analytics;
using OBS_Helper.Wpf.Services.Shell;
using OBS_Helper.Wpf.Services.Mobile;
using OBS_Helper.Wpf.Services.Host;

namespace OBS_Helper.Wpf.Views;

/// <summary>智能与复盘页（S08 / S07 / E06）。</summary>
public partial class InsightsPage : UserControl, INavigationAware
{
    public InsightsPage() => InitializeComponent();

    public Task OnNavigatedToAsync(object? parameter)
    {
        if (!_statsLoaded)
        {
            _statsLoaded = true;
            try
            {
                var saved = AppServices.Store.GetObject<ViewerStatsCore.State>("insights.viewerstats");
                if (saved is not null) AppServices.ViewerStats.ImportState(saved);
            }
            catch (Exception ex)
            {
                FileLogger.Warn("Insights", $"读取观众看板失败：{ex.Message}");
            }
        }
        RefreshFromLocal();
        RefreshDashboard();
        return Task.CompletedTask;
    }

    private static bool _statsLoaded;

    public Task OnNavigatedFromAsync()
    {
        SaveStats();
        return Task.CompletedTask;
    }

    private static void SaveStats()
    {
        try
        {
            AppServices.Store.SetObject("insights.viewerstats", AppServices.ViewerStats.ExportState());
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Insights", $"保存观众看板失败：{ex.Message}");
        }
    }

    /// <summary>指标自动回填：本地看板 + OBS 实时统计；用户仍可手动修正。</summary>
    private void RefreshFromLocal()
    {
        var obs = AppServices.Obs;
        var stats = AppServices.ViewerStats;
        PeakBox.Text = stats.PeakViewers.ToString();
        ChatBox.Text = stats.TotalMessages.ToString();
        FollowBox.Text = stats.TotalFollows.ToString();
        GiftBox.Text = stats.GiftValue.ToString();
        var kbps = obs.StreamStatus.Kbps > 0
            ? obs.StreamStatus.Kbps
            : obs.StreamStatus.Bytes > 0 && obs.StreamElapsed.TotalSeconds > 0
                ? obs.StreamStatus.Bytes * 8 / 1000 / obs.StreamElapsed.TotalSeconds
                : 0;
        BitrateBox.Text = Math.Round(kbps).ToString();
        var dropped = obs.Stats.OutputTotalFrames > 0 ? obs.Stats.OutputSkipRatio : obs.StreamStatus.DroppedRatio;
        DropBox.Text = (dropped * 100).ToString("0.0");
    }

    private SessionMetrics ReadMetrics()
    {
        int Parse(TextBox box) => int.TryParse(box.Text.Trim(), out var value) ? Math.Max(0, value) : 0;
        long ParseLong(TextBox box) => long.TryParse(box.Text.Trim(), out var value) ? Math.Max(0, value) : 0;
        double ParseDouble(TextBox box) => double.TryParse(box.Text.Trim(), out var value) ? Math.Max(0, value) : 0;
        return new SessionMetrics
        {
            Duration = AppServices.Obs.StreamElapsed > TimeSpan.Zero ? AppServices.Obs.StreamElapsed : AppServices.Obs.RecordElapsed,
            PeakViewers = Parse(PeakBox),
            ChatMessages = Parse(ChatBox),
            NewFollowers = Parse(FollowBox),
            GiftValue = ParseLong(GiftBox),
            AvgBitrateKbps = ParseDouble(BitrateBox),
            DroppedRatio = ParseDouble(DropBox) / 100.0,
            ClipCount = AppServices.ViewerStats.ClipsExported,
        };
    }

    private void OnAutoFill(object sender, RoutedEventArgs e)
    {
        RefreshFromLocal();
        RefreshDashboard();
        AppServices.Toast.Show(Strings.T("insights.autofilled"));
    }

    /// <summary>本地看板：按分钟列出观看/弹幕/关注，并显示留存率；数据不出本机。</summary>
    private void RefreshDashboard()
    {
        var stats = AppServices.ViewerStats;
        var list = stats.Snapshot();
        DashboardList.Items.Clear();
        if (list.Count == 0)
        {
            DashboardText.Text = Strings.T("insights.dashboard.empty");
            return;
        }
        DashboardText.Text = Strings.T("insights.dashboard.summary", list.Count, stats.Retention());
        foreach (var bucket in list.TakeLast(60))
        {
            DashboardList.Items.Add(Strings.T("insights.dashboard.row",
                bucket.Minute.ToLocalTime().ToString("HH:mm"),
                bucket.Viewers, bucket.Messages, bucket.Follows));
        }
    }

    private void OnSelfCheck(object sender, RoutedEventArgs e)
    {
        SelfCheck.SetBusy(true);
        try
        {
            var lines = new List<string>();
            lines.Add(AppServices.Obs.IsConnected
                ? SelfCheckResult.Ok("OBS")
                : SelfCheckResult.Warn("OBS", Strings.T("selfcheck.obsHint")));
            var dir = Path.Combine(HostBridge.AppDataDirectory, "analytics");
            var writable = false;
            try
            {
                Directory.CreateDirectory(dir);
                var probe = Path.Combine(dir, ".write-test");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                writable = true;
            }
            catch (Exception) { }
            lines.Add(writable
                ? SelfCheckResult.Ok(Strings.T("insights.generate"), dir)
                : SelfCheckResult.Fail(Strings.T("insights.generate"), Strings.T("selfcheck.reportHint")));
            var stats = AppServices.ViewerStats;
            var samples = stats.Snapshot().Count;
            lines.Add(samples > 0
                ? SelfCheckResult.Ok(Strings.T("insights.metrics"), Strings.T("insights.samples", samples))
                : SelfCheckResult.Warn(Strings.T("insights.metrics"), Strings.T("selfcheck.statsHint")));
            lines.Add(SelfCheckResult.Ok(Strings.T("insights.metrics.chat"), stats.TotalMessages.ToString()));
            lines.Add(SelfCheckResult.Ok(Strings.T("insights.metrics.peak"), stats.PeakViewers.ToString()));
            lines.Add(SelfCheckResult.Ok(Strings.T("insights.metrics.gifts"), stats.GiftValue.ToString()));
            var kbps = AppServices.Obs.StreamStatus.Kbps > 0
                ? AppServices.Obs.StreamStatus.Kbps
                : AppServices.Obs.StreamElapsed.TotalSeconds > 0
                    ? AppServices.Obs.StreamStatus.Bytes * 8 / 1000 / AppServices.Obs.StreamElapsed.TotalSeconds
                    : 0;
            lines.Add(kbps > 0
                ? SelfCheckResult.Ok(Strings.T("insights.metrics.bitrate"), Math.Round(kbps).ToString())
                : SelfCheckResult.Warn(Strings.T("insights.metrics.bitrate"), Strings.T("selfcheck.statsHint")));
            lines.Add(SelfCheckResult.Ok(Strings.T("insights.cover"), "3"));
            SelfCheck.SetResult(SelfCheckResult.Join(lines));
        }
        finally { SelfCheck.SetBusy(false); }
    }

    private void OnGenerateReport(object sender, RoutedEventArgs e)
    {
        var metrics = ReadMetrics();
        var title = TitleBox.Text.Trim();
        if (title.Length == 0) title = Strings.T("insights.defaultTitle");
        var report = SessionCoachCore.BuildReport(title, DateTimeOffset.Now, metrics);
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

    private void OnCopyReport(object sender, RoutedEventArgs e)
    {
        if (ReportText.Text.Trim().Length == 0)
        {
            AppServices.Toast.Show(Strings.T("insights.noReport"));
            return;
        }
        try
        {
            Clipboard.SetText(ReportText.Text);
            AppServices.Toast.Show(Strings.T("insights.copied"));
        }
        catch (Exception)
        {
            AppServices.Toast.Show(Strings.T("common.copyFailed"));
        }
    }

    private void OnOpenReportFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            var dir = Path.Combine(HostBridge.AppDataDirectory, "analytics");
            Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", dir)
            { UseShellExecute = true });
        }
        catch (Exception)
        {
            AppServices.Toast.Show(Strings.T("common.openLinkFailed"));
        }
    }

    private void OnRetention(object sender, RoutedEventArgs e)
    {
        var retention = AppServices.ViewerStats.Retention();
        ReportText.Text = Strings.T("insights.retention", retention) + Environment.NewLine +
            Strings.T("insights.retentionDetail", AppServices.ViewerStats.Snapshot().Count);
        RefreshDashboard();
    }

    private void OnExportStats(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "CSV|*.csv", FileName = "viewer-stats.csv" };
        if (dialog.ShowDialog() != true) return;
        File.WriteAllText(dialog.FileName, AppServices.ViewerStats.ExportCsv());
        RefreshDashboard();
        AppServices.Toast.Show(Strings.T("common.saved"));
    }

    private void OnClearStats(object sender, RoutedEventArgs e)
    {
        AppServices.ViewerStats.Clear();
        ReportText.Text = "";
        CoverList.Items.Clear();
        CoverEditBox.Text = "";
        CoverStateText.Text = "";
        RefreshFromLocal();
        RefreshDashboard();
        AppServices.Toast.Show(Strings.T("insights.cleared"));
    }

    private readonly List<string> _coverCandidates = new();

    private void OnSuggest(object sender, RoutedEventArgs e)
    {
        CoverList.Items.Clear();
        _coverCandidates.Clear();
        CoverStateText.Text = "";
        var suggestions = CoverCopyCore.Suggest(GameBox.Text, KeywordBox.Text, MoodBox.Text);
        var unsafeAny = false;
        foreach (var (title, topics) in suggestions)
        {
            _coverCandidates.Add(title);
            CoverList.Items.Add(title + "    [" + topics + "]");
            unsafeAny |= !CoverCopyCore.IsSafe(title);
        }
        CoverStateText.Text = unsafeAny
            ? Strings.T("insights.cover.unsafe")
            : Strings.T("insights.cover.hint");
        if (CoverList.Items.Count > 0) CoverList.SelectedIndex = 0;
    }

    private void OnCoverSelected(object sender, SelectionChangedEventArgs e)
    {
        var index = CoverList.SelectedIndex;
        if (index >= 0 && index < _coverCandidates.Count)
            CoverEditBox.Text = _coverCandidates[index];
    }

    private void OnCopyCover(object sender, RoutedEventArgs e)
    {
        var text = CoverEditBox.Text.Trim();
        if (text.Length == 0)
        {
            CoverStateText.Text = Strings.T("insights.cover.empty");
            return;
        }
        if (!CoverCopyCore.IsSafe(text))
        {
            CoverStateText.Text = Strings.T("insights.cover.unsafe");
            return;
        }
        try
        {
            Clipboard.SetText(text);
            CoverStateText.Text = Strings.T("insights.copied");
            AppServices.Toast.Show(Strings.T("insights.copied"));
        }
        catch (Exception)
        {
            CoverStateText.Text = Strings.T("common.copyFailed");
        }
    }
}
