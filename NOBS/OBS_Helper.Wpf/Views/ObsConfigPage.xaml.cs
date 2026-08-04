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
    }

    // -------------------------------------------------------------- 位置

    private async Task RefreshLocationAsync()
    {
        try
        {
            _location = await AppServices.ObsPaths.LocateAsync();
            if (_location.Exists)
            {
                ConfigPathText.Text = $"配置目录：{_location.ConfigDir}";
                ConfigDetailText.Text = _location.IsPortable
                    ? "检测到便携版 OBS（portable_mode.txt），配置在 OBS 安装目录下。"
                    : "标准安装版，配置位于 %AppData%\\obs-studio。";
            }
            else
            {
                ConfigPathText.Text = "未找到 OBS 配置目录，请点击「手动指定」。";
                ConfigDetailText.Text = "程序会自动尝试常规路径（%AppData%\\obs-studio 与便携版目录）。如果没有检测到，请在下方手动选择。";
            }
        }
        catch (Exception ex)
        {
            ConfigPathText.Text = $"检测异常：{ex.Message}";
            ConfigDetailText.Text = "";
        }
    }

    private async void OnDetectClick(object sender, RoutedEventArgs e)
    {
        await RefreshLocationAsync();
        ShowResult("✅", "已重新检测 OBS 配置目录。");
    }

    private async void OnManualPathClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择 OBS 配置目录（obs-studio）",
        };

        if (dialog.ShowDialog() != true) return;

        var path = dialog.FolderName;
        if (string.IsNullOrEmpty(path)) return;

        AppServices.Store.SetItem(ObsPathService.OverrideKey, path);
        await RefreshLocationAsync();
        ShowResult("✅", $"已手动指定配置目录为：{path}（重启后仍生效，可在下次检测时覆盖）。");
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
                BackupListHint.Text = "暂无本地备份记录。";
                BackupListHint.Visibility = Visibility.Visible;
                BackupList.Visibility = Visibility.Collapsed;
                return;
            }

            BackupListHint.Text = $"共 {backups.Count} 条自动备份（存在本程序数据目录下）：";
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
                    Text = $"  （还有 {backups.Count - 10} 条更早的备份）"
                };
                more.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeXs");
                more.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                BackupList.Children.Add(more);
            }
        }
        catch
        {
            BackupListHint.Text = "无法读取备份列表。";
            BackupListHint.Visibility = Visibility.Visible;
        }
    }

    private async void OnCreateBackup(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        SetBusy(true);
        try
        {
            var includeKey = IncludeKeyCheck.IsChecked == true;
            var path = await AppServices.ObsBackups.CreateBackupAsync(
                "手动创建", includeKey: includeKey, includePluginConfig: true);
            await RefreshBackupListAsync();
            ShowResult("✅", $"备份已创建，保存位置：\n{path}");
        }
        catch (Exception ex)
        {
            ShowResult("❌", $"备份失败：{ex.Message}");
            App.ReportError(ErrorCodes.BackupFailed, ex);
        }
        finally { SetBusy(false); }
    }

    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;

        var dialog = new SaveFileDialog
        {
            Title = "导出 OBS 配置",
            FileName = $"OBS_备份_{DateTime.Now:yyyyMMdd_HHmm}",
            DefaultExt = ".zip",
            Filter = "ZIP 压缩包 (*.zip)|*.zip"
        };

        if (dialog.ShowDialog() != true) return;

        SetBusy(true);
        try
        {
            var includeKey = IncludeKeyCheck.IsChecked == true;
            await AppServices.ObsBackups.ExportToAsync(dialog.FileName, includeKey, true);
            ShowResult("✅", $"已导出到：{dialog.FileName}");
        }
        catch (Exception ex)
        {
            ShowResult("❌", $"导出失败：{ex.Message}");
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
            Title = mode == ObsImportMode.Overwrite ? "选择备份 ZIP（将覆盖当前配置）" : "选择备份 ZIP（将合并到当前配置）",
            Filter = "ZIP 压缩包 (*.zip)|*.zip"
        };

        if (dialog.ShowDialog() != true) return;

        var label = mode == ObsImportMode.Overwrite ? "覆盖" : "合并";
        if (!ConfirmDialog.Show(
                $"导入并{label}配置",
                $"将用选中备份包以「{label}」模式导入 OBS 配置。导入前会自动创建当前配置的备份。\n\n确认继续？",
                "导入", "取消"))
        {
            return;
        }

        SetBusy(true);
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
                var detail = $"导入完成：{result.ImportedCollections} 个场景集合、{result.ImportedProfiles} 个 Profile。";
                if (!string.IsNullOrEmpty(result.AutoBackupPath))
                    detail += $"\n\n导入前的自动备份：{result.AutoBackupPath}";
                ShowResult("✅", detail);
            }
            else
            {
                ShowResult("❌", $"导入失败：{result.Error}");
                App.ReportError(ErrorCodes.ImportRejected);
            }
        }
        catch (Exception ex)
        {
            ShowResult("❌", $"导入过程中出现异常：{ex.Message}");
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
            ShowResult("⚠️", "轻度重置需要先连接 OBS WebSocket，请先去「OBS 控制台」完成连接。");
            return;
        }

        if (!ConfirmDialog.Show(
                "轻度重置",
                "将在 OBS 中新建一个名为「初始设置 (OBS 助手)」的干净配置集合，重置分辨率到 1920×1080@30，并切换过去。原有配置不会被删除。\n\n操作前会自动备份当前配置，确认继续？",
                "重置", "取消"))
        {
            return;
        }

        SetBusy(true);
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
                var msg = "轻度重置完成！已切换到新的干净配置集「初始设置 (OBS 助手)」。";
                if (!string.IsNullOrEmpty(result.AutoBackupPath))
                    msg += $"\n\n自动备份：{result.AutoBackupPath}";
                if (!string.IsNullOrEmpty(result.Note))
                    msg += $"\n\n{result.Note}";
                ShowResult("✅", msg);
            }
            else
            {
                ShowResult("❌", $"重置失败：{result.Note ?? "未知错误"}");
                App.ReportError(ErrorCodes.ResetFailed);
            }
        }
        catch (Exception ex)
        {
            ShowResult("❌", $"重置异常：{ex.Message}");
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
                $"检测到 OBS 正在运行（进程：{proc.ProcessName}）。彻底重置需要完全退出 OBS（包括系统托盘里的图标），请先手动退出后重试。");
            App.ReportError(ErrorCodes.ObsRunning);
            return;
        }

        if (!ConfirmDialog.Show(
                "⚠️ 彻底重置 OBS",
                "将删除当前所有场景、Profile、插件设置与 global.ini，恢复到第一次安装 OBS 时的空白状态。\n\n操作前会自动创建一份包含密钥的完整备份，但仍建议再做一份导出。\n\n确认继续？",
                "彻底重置", "取消",
                danger: true))
        {
            return;
        }

        // 二次确认
        if (!ConfirmDialog.Show(
                "再次确认 — 彻底重置",
                "此操作不可撤销。删除后无法恢复当前配置（备份除外）。\n\n真的要继续吗？",
                "确认重置", "取消",
                danger: true))
        {
            return;
        }

        SetBusy(true);
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
                var msg = "彻底重置完成！OBS 已恢复到初始状态。";
                if (!string.IsNullOrEmpty(result.AutoBackupPath))
                    msg += $"\n\n完整备份已保存至：{result.AutoBackupPath}\n如需恢复，请用上面的「导入」功能选中该 ZIP 文件。";
                if (!string.IsNullOrEmpty(result.Note)) msg += $"\n\n{result.Note}";
                ShowResult("✅", msg);
            }
            else
            {
                ShowResult("❌", $"重置未能完成：{result.Note ?? "已尝试回滚。原配置备份保存在备份列表中。"}");
                App.ReportError(ErrorCodes.ResetFailed);
            }

            await RefreshBackupListAsync();
        }
        catch (Exception ex)
        {
            ShowResult("❌", $"重置过程发生异常：{ex.Message}\n\n程序已尝试回滚，原配置备份保存在备份列表中。");
            App.ReportError(ErrorCodes.ResetFailed, ex);
        }
        finally
        {
            SetBusy(false);
            ResultDetailText.Visibility = Visibility.Collapsed;
        }
    }

    // -------------------------------------------------------------- 辅助

    private void SetBusy(bool busy)
    {
        _busy = busy;
        BackupButton.IsEnabled = !busy;
        ExportButton.IsEnabled = !busy;
        ImportOverwriteButton.IsEnabled = !busy;
        ImportMergeButton.IsEnabled = !busy;
        LightResetButton.IsEnabled = !busy;
        FullResetButton.IsEnabled = !busy;
    }

    private void ShowResult(string icon, string text)
    {
        ResultIcon.Text = icon;
        ResultText.Text = text;
        ResultBar.Visibility = Visibility.Visible;
    }
}
