using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using OBS_Helper.Wpf.Controls;
using OBS_Helper.Wpf.Errors;
using OBS_Helper.Wpf.Models.ObsConfig;
using OBS_Helper.Wpf.Navigation;
using OBS_Helper.Wpf.Services.ObsConfig;

namespace OBS_Helper.Wpf.Views;

/// <summary>
/// OBS 配置管理：备份 / 导出 / 导入 / 重置。
///
/// 二级路由页，从设置页进入，左侧导航无独立 Tab。
/// 所有写操作都强制先自动备份，误操作可回滚。
/// </summary>
public partial class ObsConfigPage : UserControl, INavigationAware
{
    private bool _busy;
    private ObsConfigLocation _location = new("", false, false, "");

    public ObsConfigPage()
    {
        InitializeComponent();
    }

    public async Task OnNavigatedToAsync(object? parameter)
    {
        await RefreshLocationAsync();
        await RefreshBackupListAsync();
        await RefreshTrashAsync();
    }

    // -------------------------------------------------------------- 位置

    private async Task RefreshLocationAsync()
    {
        try
        {
            _location = await AppServices.ObsPaths.LocateAsync();
            if (_location.Exists)
            {
                ConfigPathText.Text = Strings.T("obsconfig.path", _location.ConfigDir);
                ConfigDetailText.Text = _location.IsPortable
                    ? Strings.T("obsconfig.portable")
                    : Strings.T("obsconfig.standard");
            }
            else
            {
                ConfigPathText.Text = Strings.T("obsconfig.notFound");
                ConfigDetailText.Text = Strings.T("obsconfig.notFoundDetail");
            }
        }
        catch (Exception ex)
        {
            ConfigPathText.Text = Strings.T("obsconfig.detectError", ex.Message);
            ConfigDetailText.Text = "";
        }
    }

    private async void OnDetectClick(object sender, RoutedEventArgs e)
    {
        await RefreshLocationAsync();
        ShowResult("✅", Strings.T("obsconfig.redetected"));
    }

    private async void OnManualPathClick(object sender, RoutedEventArgs e)
    {
        // V2.9.3：OpenFolderDialog 是 .NET 8 才有的类型，兼容构建走 WinForms 分支。
        var path = OBS_Helper.Wpf.Services.Compat.FolderPicker.Pick(Strings.T("obsconfig.pickTitle"));
        if (string.IsNullOrEmpty(path)) return;

        // V3.0（审查发现的缺陷）：手动目录原先**零校验**就落盘，随后被登记为「可信配置根」——
        // 把整卷（D:\）或任意目录都变成可写区。这里按「必须像 OBS 配置目录」收口。
        if (!LooksLikeObsConfig(path, out var reason))
        {
            ShowResult("⚠️", Strings.T("obsconfig.manualRejected", reason));
            return;
        }

        AppServices.Store.SetItem(ObsPathService.OverrideKey, path);
        // 覆盖目录变了，探测缓存（含进程状态）必须作废，否则界面还会显示旧位置
        ObsPathService.InvalidateCache();
        await RefreshLocationAsync();
        ShowResult("✅", Strings.T("obsconfig.manualSet", path));
    }

    /// <summary>
    /// 手动指定的目录是否「像 OBS 配置目录」：不是盘符根 / 系统目录，且含 <c>basic</c> 或 <c>global.ini</c>。
    /// 判定失败时给出可照做的原因。
    /// </summary>
    private static bool LooksLikeObsConfig(string path, out string reason)
    {
        reason = "";
        try
        {
            if (!Directory.Exists(path)) { reason = Strings.T("obsconfig.rejectNotExist"); return false; }

            var full = Path.GetFullPath(path);
            var root = Path.GetPathRoot(full)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.IsNullOrEmpty(root) &&
                string.Equals(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), root, StringComparison.OrdinalIgnoreCase))
            {
                reason = Strings.T("obsconfig.rejectDriveRoot");
                return false;
            }

            if (Directory.Exists(Path.Combine(full, "basic")) || File.Exists(Path.Combine(full, "global.ini")))
                return true;

            reason = Strings.T("obsconfig.rejectNotObsConfig");
            return false;
        }
        catch (Exception ex)
        {
            reason = ex.Message;
            return false;
        }
    }

    // -------------------------------------------------------------- 备份 / 导出

    private async Task RefreshBackupListAsync()
    {
        try
        {
            // V3.0（F8）：列出备份要逐个 zip「打开 + 读 manifest」，是实打实的磁盘 IO。
            // 方法本来就标了 async，但以前是同步调用 —— 备份多了会在 UI 线程上卡一下。
            var backups = await Task.Run(AppServices.ObsBackups.ListBackups).ConfigureAwait(true);
            BackupList.Children.Clear();

            if (backups.Count == 0)
            {
                BackupListHint.Text = Strings.T("obsconfig.backupsEmpty");
                BackupListHint.Visibility = Visibility.Visible;
                BackupList.Visibility = Visibility.Collapsed;
                return;
            }

            BackupListHint.Text = Strings.T("obsconfig.backupsCount", backups.Count);
            BackupListHint.Visibility = Visibility.Visible;
            BackupList.Visibility = Visibility.Visible;

            foreach (var b in backups.Take(10))
            {
                var tb = new TextBlock
                {
                    Text = $"  · {b.CreatedAt:yyyy-MM-dd HH:mm} — {b.Reason}",
                    TextWrapping = TextWrapping.Wrap
                };
                tb.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeXs");
                tb.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                BackupList.Children.Add(tb);
            }

            if (backups.Count > 10)
            {
                var more = new TextBlock
                {
                    Text = Strings.T("obsconfig.backupsMore", backups.Count - 10)
                };
                more.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeXs");
                more.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                BackupList.Children.Add(more);
            }
        }
        catch
        {
            BackupListHint.Text = Strings.T("obsconfig.backupsUnavailable");
            BackupListHint.Visibility = Visibility.Visible;
        }
    }

    // -------------------------------------------------------------- 配置回收站（V3.0 / A4）

    private async void OnRefreshTrash(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        SetBusy(true, Strings.T("trash.refresh"));
        try { await RefreshTrashAsync(); }
        finally { SetBusy(false); }
    }

    /// <summary>
    /// 列出「永不硬删」留下的恢复副本。
    ///
    /// 这一块补的是本产品数据安全承诺的**最后一环**：副本一直在写，但此前用户看不到它们。
    /// 列表按时间倒序，每条给出时间、文件数、体积、是否被锁定（回滚未竟），
    /// 有原始路径记录时提供「放回原位」。
    /// </summary>
    private Task RefreshTrashAsync()
    {
        try
        {
            var groups = AppServices.Trash.List();
            TrashList.Children.Clear();

            if (groups.Count == 0)
            {
                TrashHint.Text = Strings.T("trash.empty");
                TrashHint.Visibility = Visibility.Visible;
                TrashList.Visibility = Visibility.Collapsed;
                return Task.CompletedTask;
            }

            TrashHint.Visibility = Visibility.Collapsed;
            TrashList.Visibility = Visibility.Visible;

            foreach (var g in groups)
                TrashList.Children.Add(BuildTrashRow(g));
        }
        catch (Exception ex)
        {
            TrashHint.Text = Strings.T("trash.restore.failed", "", ex.Message);
            TrashHint.Visibility = Visibility.Visible;
        }
        return Task.CompletedTask;
    }
    private UIElement BuildTrashRow(TrashGroup g)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };

        var head = new TextBlock
        {
            Text = Strings.T("trash.groupMeta", g.CreatedLocal.ToString("yyyy-MM-dd HH:mm"),
                g.FileCount, FormatSize(g.TotalBytes)),
            TextWrapping = TextWrapping.Wrap
        };
        panel.Children.Add(head);

        if (g.Retained)
        {
            var badge = new TextBlock { Text = "⚠️ " + Strings.T("trash.retainedBadge"), TextWrapping = TextWrapping.Wrap };
            badge.SetResourceReference(TextBlock.ForegroundProperty, "WarnBrush");
            badge.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeXs");
            panel.Children.Add(badge);
        }

        // 没有原始路径记录的旧事务目录：如实说明，只提供「打开文件夹」
        if (!g.CanRestore)
        {
            var noManifest = new TextBlock { Text = Strings.T("trash.noManifest"), TextWrapping = TextWrapping.Wrap };
            noManifest.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            noManifest.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeXs");
            panel.Children.Add(noManifest);
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };

        if (g.CanRestore)
        {
            var restore = new Button
            {
                Content = Strings.T("trash.restore"),
                Style = TryFindResource("SecondaryButton") as Style,
                Margin = new Thickness(0, 0, 8, 0),
                Tag = g
            };
            restore.Click += OnRestoreTrashGroup;
            buttons.Children.Add(restore);
        }

        var open = new Button
        {
            Content = Strings.T("trash.openFolder"),
            Style = TryFindResource("GhostButton") as Style,
            Tag = g.Dir
        };
        open.Click += (_, _) => { if (open.Tag is string dir) TrashService.OpenInExplorer(dir); };
        buttons.Children.Add(open);

        panel.Children.Add(buttons);
        return panel;
    }

    private static string FormatSize(long bytes)
        => bytes >= 1024L * 1024 * 1024 ? $"{bytes / 1024.0 / 1024 / 1024:0.#} GB"
         : bytes >= 1024 * 1024 ? $"{bytes / 1024.0 / 1024:0.#} MB"
         : $"{bytes / 1024.0:0.#} KB";

    private async void OnRestoreTrashGroup(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TrashGroup g }) return;
        if (_busy) return;   // 与页面其它处理器一致：防重复触发（连点会不断产生 .restoredN）

        if (!ConfirmDialog.Show(
                Strings.T("trash.restoreConfirmTitle"),
                Strings.T("trash.restoreConfirmMessage"),
                Strings.T("trash.restore"), Strings.T("common.cancel"),
                danger: false, icon: "↩️"))
        {
            return;
        }

        SetBusy(true, Strings.T("trash.restore"));
        try
        {
            var result = await AppServices.Trash.RestoreAsync(g);
            ShowResult(result.Failed == 0 ? "✅" : "⚠️",
                Strings.T("trash.restoreDone", result.Restored, result.PlacedAside, result.Failed));

            // 逐条结果放进结果栏的详情里（用户需要知道具体哪一项去了哪里）
            ResultDetailText.Text = string.Join("\n", result.Messages);
            ResultDetailText.Visibility = Visibility.Visible;

            if (result.Failed > 0) App.ReportError(ErrorCodes.FileTransactionRollbackFailed);
            await RefreshTrashAsync();
        }
        catch (Exception ex)
        {
            ShowResult("❌", Strings.T("trash.restore.failed", "", ex.Message));
            App.ReportError(ErrorCodes.FileTransactionRollbackFailed, ex);
        }
        finally { SetBusy(false); }
    }

    private async void OnCreateBackup(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        SetBusy(true, Strings.T("obsconfig.creatingBackup"));
        try
        {
            var includeKey = IncludeKeyCheck.IsChecked == true;
            var path = await AppServices.ObsBackups.CreateBackupAsync(
                Strings.T("obsconfig.manualReason"), includeKey: includeKey, includePluginConfig: true);
            await RefreshBackupListAsync();
            ShowResult("✅", Strings.T("obsconfig.backupCreated", path));
        }
        catch (Exception ex)
        {
            ShowResult("❌", Strings.T("obsconfig.backupFailed", ex.Message));
            App.ReportError(ErrorCodes.BackupFailed, ex);
        }
        finally { SetBusy(false); }
    }

    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;

        var dialog = new SaveFileDialog
        {
            Title = Strings.T("obsconfig.exportDialogTitle"),
            FileName = Strings.T("obsconfig.exportFileName", DateTime.Now.ToString("yyyyMMdd_HHmm")),
            DefaultExt = ".zip",
            Filter = Strings.T("obsconfig.zipFilter")
        };

        if (dialog.ShowDialog() != true) return;

        SetBusy(true, Strings.T("obsconfig.exporting"));
        try
        {
            var includeKey = IncludeKeyCheck.IsChecked == true;
            await AppServices.ObsBackups.ExportToAsync(dialog.FileName, includeKey, true);
            ShowResult("✅", Strings.T("obsconfig.exported", dialog.FileName));
        }
        catch (Exception ex)
        {
            ShowResult("❌", Strings.T("obsconfig.exportFailed", ex.Message));
            App.ReportError(ErrorCodes.BackupFailed, ex);
        }
        finally { SetBusy(false); }
    }

    // -------------------------------------------------------------- 导入

    private async void OnImportOverwrite(object sender, RoutedEventArgs e)
        => await DoImportAsync(ObsImportMode.Overwrite);

    private async void OnImportMerge(object sender, RoutedEventArgs e)
        => await DoImportAsync(ObsImportMode.Merge);

    private async Task DoImportAsync(ObsImportMode mode)
    {
        if (_busy) return;

        var dialog = new OpenFileDialog
        {
            Title = mode == ObsImportMode.Overwrite ? Strings.T("obsconfig.importPickOverwrite") : Strings.T("obsconfig.importPickMerge"),
            Filter = Strings.T("obsconfig.zipFilter")
        };

        if (dialog.ShowDialog() != true) return;

        var label = mode == ObsImportMode.Overwrite ? Strings.T("obsconfig.modeOverwrite") : Strings.T("obsconfig.modeMerge");
        if (!ConfirmDialog.Show(
                Strings.T("obsconfig.importConfirmTitle", label),
                Strings.T("obsconfig.importConfirmMessage", label),
                Strings.T("obsconfig.importConfirmButton"), Strings.T("common.cancel")))
        {
            return;
        }

        SetBusy(true, Strings.T("obsconfig.importing"));
        try
        {
            var progress = new Progress<string>(msg =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    ResultDetailText.Text = msg;
                    ResultDetailText.Visibility = Visibility.Visible;
                }));
            });

            var result = await AppServices.ObsBackups.ImportAsync(dialog.FileName, mode, progress);
            if (result.Ok)
            {
                var detail = Strings.T("obsconfig.importDone", result.ImportedCollections, result.ImportedProfiles);
                if (!string.IsNullOrEmpty(result.AutoBackupPath))
                    detail += Strings.T("obsconfig.importAutoBackup", result.AutoBackupPath);
                ShowResult("✅", detail);
            }
            else
            {
                ShowResult("❌", Strings.T("obsconfig.importFailed", result.Error));
                App.ReportError(ErrorCodes.ImportRejected);
            }
        }
        catch (Exception ex)
        {
            ShowResult("❌", Strings.T("obsconfig.importException", ex.Message));
            App.ReportError(ErrorCodes.ImportRejected, ex);
        }
        finally
        {
            SetBusy(false);
            ResultDetailText.Visibility = Visibility.Collapsed;
        }
    }

    // -------------------------------------------------------------- 重置

    private async void OnLightReset(object sender, RoutedEventArgs e)
    {
        if (_busy) return;

        if (!AppServices.Obs.IsConnected)
        {
            ShowResult("⚠️", Strings.T("obsconfig.lightNeedsConnection"));
            return;
        }

        if (!ConfirmDialog.Show(
                Strings.T("obsconfig.lightTitle"),
                Strings.T("obsconfig.lightMessage"),
                Strings.T("obsconfig.resetConfirmButton"), Strings.T("common.cancel")))
        {
            return;
        }

        SetBusy(true, Strings.T("obsconfig.lightBusy"));
        try
        {
            var progress = new Progress<string>(msg =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    ResultDetailText.Text = msg;
                    ResultDetailText.Visibility = Visibility.Visible;
                }));
            });

            var result = await AppServices.ObsReset.LightResetAsync(progress);
            if (result.Ok)
            {
                var msg = Strings.T("obsconfig.lightDone");
                if (!string.IsNullOrEmpty(result.AutoBackupPath))
                    msg += Strings.T("obsconfig.autoBackupLine", result.AutoBackupPath);
                if (!string.IsNullOrEmpty(result.Note))
                    msg += $"\n\n{result.Note}";
                ShowResult("✅", msg);
            }
            else
            {
                ShowResult("❌", Strings.T("obsconfig.resetFailed", result.Note ?? Strings.T("common.unknown")));
                App.ReportError(ErrorCodes.ResetFailed);
            }
        }
        catch (Exception ex)
        {
            ShowResult("❌", Strings.T("obsconfig.resetException", ex.Message));
            App.ReportError(ErrorCodes.ResetFailed, ex);
        }
        finally
        {
            SetBusy(false);
            ResultDetailText.Visibility = Visibility.Collapsed;
        }
    }

    private async void OnFullReset(object sender, RoutedEventArgs e)
    {
        if (_busy) return;

        var proc = AppServices.ObsPaths.DetectProcess();
        if (proc.IsRunning)
        {
            ShowResult("⚠️",
                Strings.T("obsconfig.obsRunning", proc.ProcessName));
            App.ReportError(ErrorCodes.ObsRunning);
            return;
        }

        if (!ConfirmDialog.Show(
                Strings.T("obsconfig.fullTitle"),
                Strings.T("obsconfig.fullMessage"),
                Strings.T("obsconfig.fullButton"), Strings.T("common.cancel"),
                danger: true, irreversible: true))
        {
            return;
        }

        // 二次确认
        if (!ConfirmDialog.Show(
                Strings.T("obsconfig.fullConfirmTitle"),
                Strings.T("obsconfig.fullConfirmMessage"),
                Strings.T("obsconfig.fullConfirmButton"), Strings.T("common.cancel"),
                danger: true, irreversible: true))
        {
            return;
        }

        SetBusy(true, Strings.T("obsconfig.fullBusy"));
        try
        {
            var progress = new Progress<string>(msg =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    ResultDetailText.Text = msg;
                    ResultDetailText.Visibility = Visibility.Visible;
                }));
            });

            var result = await AppServices.ObsReset.FullResetAsync(
                keepProfiles: false, keepPluginConfig: false, p: progress);

            if (result.Ok)
            {
                var msg = Strings.T("obsconfig.fullDone");
                if (!string.IsNullOrEmpty(result.AutoBackupPath))
                    msg += Strings.T("obsconfig.fullBackupLine", result.AutoBackupPath);
                if (!string.IsNullOrEmpty(result.Note)) msg += $"\n\n{result.Note}";
                ShowResult("✅", msg);
            }
            else
            {
                ShowResult("❌", Strings.T("obsconfig.fullFailed", result.Note ?? Strings.T("obsconfig.fullFailedFallback")));
                App.ReportError(ErrorCodes.ResetFailed);
            }

            await RefreshBackupListAsync();
        }
        catch (Exception ex)
        {
            ShowResult("❌", Strings.T("obsconfig.fullException", ex.Message));
            App.ReportError(ErrorCodes.ResetFailed, ex);
        }
        finally
        {
            SetBusy(false);
            ResultDetailText.Visibility = Visibility.Collapsed;
        }
    }

    // -------------------------------------------------------------- 辅助

    /// <summary>
    /// 整页阻塞操作统一加载态（P2-2）：按钮禁用防重入 + 全局 BusyOverlay 遮罩提供视觉反馈，
    /// 与诊断 / 控制台 / 日志页的加载体验一致。必须 try/finally 配对调用。
    /// </summary>
    private void SetBusy(bool busy, string? message = null)
    {
        _busy = busy;
        BackupButton.IsEnabled = !busy;
        ExportButton.IsEnabled = !busy;
        ImportOverwriteButton.IsEnabled = !busy;
        ImportMergeButton.IsEnabled = !busy;
        LightResetButton.IsEnabled = !busy;
        FullResetButton.IsEnabled = !busy;

        if (busy) AppServices.Busy.Show(message ?? Strings.T("common.busy"));
        else AppServices.Busy.Hide();
    }

    private void ShowResult(string icon, string text)
    {
        ResultIcon.Text = icon;
        ResultText.Text = text;
        ResultBar.Visibility = Visibility.Visible;
    }
}
