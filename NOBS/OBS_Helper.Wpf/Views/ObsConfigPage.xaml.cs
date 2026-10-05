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
    {        await RefreshLocationAsync();
        await RefreshBackupListAsync();
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
        if (string.IsNullOrEmpty(path)) return;

        AppServices.Store.SetItem(ObsPathService.OverrideKey, path);
        await RefreshLocationAsync();
        ShowResult("✅", Strings.T("obsconfig.manualSet", path));
    }

    // -------------------------------------------------------------- 备份 / 导出

    private async Task RefreshBackupListAsync()
    {
        try
        {
            var backups = AppServices.ObsBackups.ListBackups();
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
                danger: true))
        {
            return;
        }

        // 二次确认
        if (!ConfirmDialog.Show(
                Strings.T("obsconfig.fullConfirmTitle"),
                Strings.T("obsconfig.fullConfirmMessage"),
                Strings.T("obsconfig.fullConfirmButton"), Strings.T("common.cancel"),
                danger: true))
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
