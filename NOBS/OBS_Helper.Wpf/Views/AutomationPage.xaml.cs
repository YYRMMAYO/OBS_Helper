using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using OBS_Helper.Wpf.Navigation;
using OBS_Helper.Wpf.Services.Automation;
using OBS_Helper.Wpf.Services.Mobile;
using OBS_Helper.Wpf.Services.Clips;
using OBS_Helper.Wpf.Services.Shell;

namespace OBS_Helper.Wpf.Views;

/// <summary>自动化与生态页（E01~E06、S04、S09）。</summary>
public partial class AutomationPage : UserControl, INavigationAware
{
    private readonly List<MidiCore.Mapping> _mappings = new();
    private readonly TeamRolesCore _team = new();
    private readonly WledClient _wled = new();
    private TeamRolesCore.Invite? _invite;
    private DirectorCore.Suggestion? _directorSuggestion;
    private bool _midiErrorHooked;

    private static bool IsChineseUi => Strings.Current.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

    public AutomationPage()
    {
        InitializeComponent();
        foreach (var preset in WledCore.Presets)
            WledPresetCombo.Items.Add(new ComboBoxItem { Content = WledPresetLabel(preset.Id), Tag = preset.Id });
        WledPresetCombo.SelectedIndex = 1;
        AiLabelText.Text = AiHostCore.Label(IsChineseUi);
        DirectorBox.Text = Strings.T("automation.director.defaultContext");
        ThemeBox.Text = "{\n  \"id\": \"demo\",\n  \"name\": \"Demo\",\n  \"version\": \"1.0.0\",\n  \"files\": [\n    { \"path\": \"chat.css\", \"sha256\": \"0000000000000000000000000000000000000000000000000000000000000000\", \"size\": 1 }\n  ]\n}";
    }

    public Task OnNavigatedToAsync(object? parameter)
    {
        RefreshTeam();
        RefreshMidiDevices();
        if (_mappings.Count == 0)
        {
            _mappings.AddRange(MidiCore.Defaults());
            RefreshMidi();
        }
        var lighting = AppServices.Lighting.Current;
        WledUrlBox.Text = lighting.BaseUrl;
        LightEnableCheck.IsChecked = lighting.Enabled;
        MidiDeviceText.Text = Strings.T("automation.midiDevices", Services.Hardware.MidiService.DeviceCount);
        AiLabelText.Text = AiHostCore.Label(IsChineseUi);
        return Task.CompletedTask;
    }

    private static string WledPresetLabel(string id) => Strings.T("automation.wled.preset." + id);
    private static string MidiActionLabel(string id) => Strings.T("automation.action." + id);
    private static string RoleLabel(TeamRolesCore.Role role) => Strings.T("automation.role." + role.ToString().ToLowerInvariant());

    private void RefreshMidiDevices()
        => MidiDeviceText.Text = Strings.T("automation.midiDevices", Services.Hardware.MidiService.DeviceCount);

    private void OnMidiStart(object sender, RoutedEventArgs e)
    {
        var devices = Services.Hardware.MidiService.DeviceCount;
        if (devices == 0)
        {
            MidiText.Text = Strings.T("automation.midiNoDevice");
            return;
        }
        var (valid, code, detail) = MidiCore.Validate(_mappings);
        if (!valid)
        {
            MidiText.Text = FeatureErrors.Describe(code) + " · " + detail;
            return;
        }
        var service = AppServices.Midi;
        service.SetMappings(_mappings);
        if (!_midiErrorHooked)
        {
            service.Error += message => Dispatcher.BeginInvoke(new Action(() => MidiText.Text = message));
            _midiErrorHooked = true;
        }
        if (service.Start())
        {
            MidiStartButton.IsEnabled = false;
            MidiStopButton.IsEnabled = true;
            MidiText.Text = Strings.T("automation.midiRunning");
        }
        else
        {
            MidiText.Text = Strings.T("automation.midiStartFailed");
        }
    }

    private void OnMidiStop(object sender, RoutedEventArgs e)
    {
        AppServices.Midi.Stop();
        MidiStartButton.IsEnabled = true;
        MidiStopButton.IsEnabled = false;
        MidiText.Text = Strings.T("automation.midiStopped");
    }

    private void OnLightSave(object sender, RoutedEventArgs e)
    {
        var preset = (WledPresetCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "idle";
        var url = WledUrlBox.Text.Trim();
        if (LightEnableCheck.IsChecked == true && !WledCore.ValidateUrl(url).Ok)
        {
            WledText.Text = Strings.T("selfcheck.wledHint");
            return;
        }
        AppServices.Lighting.ApplySettings(new Services.Hardware.LightingService.Settings
        {
            Enabled = LightEnableCheck.IsChecked == true,
            BaseUrl = url,
            GiftPreset = "gift",
            LivePreset = preset,
            IdlePreset = "idle",
        });
        WledText.Text = Strings.T("common.saved");
    }

    private void OnThemeApply(object sender, RoutedEventArgs e)
    {
        var source = ThemeSourceBox.Text.Trim();
        if (source.Length == 0)
        {
            ThemeText.Text = Strings.T("automation.themeSourceHint");
            return;
        }
        try
        {
            var package = JsonSerializer.Deserialize<ThemeMarketCore.ThemePackage>(ThemeBox.Text);
            if (package is null) { ThemeText.Text = FeatureErrors.Describe(FeatureErrors.ThemeUnsafeAsset); return; }
            var check = AppServices.Themes.Validate(package, source);
            if (!check.Ok)
            {
                ThemeText.Text = FeatureErrors.Describe(check.Code) + " · " + string.Join("; ", check.Issues);
                return;
            }
            var (ok, code, detail) = AppServices.Themes.Apply(package, source);
            ThemeText.Text = ok
                ? Strings.T("automation.themeApplied", detail)
                : FeatureErrors.Describe(code) + " · " + detail;
        }
        catch (JsonException) { ThemeText.Text = FeatureErrors.Describe(FeatureErrors.ThemeUnsafeAsset); }
    }

    private void OnThemeRollback(object sender, RoutedEventArgs e)
    {
        try
        {
            var package = JsonSerializer.Deserialize<ThemeMarketCore.ThemePackage>(ThemeBox.Text);
            if (package is null)
            {
                ThemeText.Text = FeatureErrors.Describe(FeatureErrors.ThemeUnsafeAsset);
                return;
            }
            var (ok, code) = AppServices.Themes.Rollback(package.Id);
            ThemeText.Text = ok
                ? Strings.T("automation.themeRolledBack", AppServices.Themes.ActiveVersion(package.Id))
                : FeatureErrors.Describe(code);
        }
        catch (JsonException) { ThemeText.Text = FeatureErrors.Describe(FeatureErrors.ThemeUnsafeAsset); }
    }

    private void OnOpenThemeFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OBS_Helper", "themes");
            Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", dir)
            { UseShellExecute = true });
        }
        catch (Exception) { AppServices.Toast.Show(Strings.T("common.openLinkFailed")); }
    }

    private void OnPluginVerify(object sender, RoutedEventArgs e)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize<PluginTrustCore.TrustManifest>(PluginManifestBox.Text);
            if (manifest is null) { PluginText.Text = Strings.T("automation.pluginBad"); return; }
            var valid = PluginTrustCore.VerifySignature(manifest, PluginPayloadBox.Text, PluginKeyBox.Text);
            var (ok, issues) = PluginTrustCore.Check(manifest, PluginTrustCore.DefaultPolicy, valid);
            PluginText.Text = Strings.T("automation.pluginResult", ok ? "✓" : "✗",
                PluginTrustCore.TrustLabel(manifest.Rating), string.Join("; ", issues));
        }
        catch (JsonException) { PluginText.Text = Strings.T("automation.pluginBad"); }
    }

    private async void OnAiSpeak(object sender, RoutedEventArgs e)
    {
        var text = AiTextBox.Text.Trim();
        if (text.Length == 0)
        {
            AiText.Text = Strings.T("automation.aiEmpty");
            return;
        }
        if (!AppServices.AiHost.TtsAvailable)
        {
            AiText.Text = Strings.T("automation.ttsUnavailable");
            return;
        }
        AiLabelText.Text = AiHostCore.Label(IsChineseUi);
        AiText.Text = AiHostCore.Label(IsChineseUi) + " · " + text;
        await AppServices.AiHost.SpeakAsync(text).ConfigureAwait(true);
        AiText.Text = AppServices.AiHost.LastError.Length > 0
            ? FeatureErrors.Describe(FeatureErrors.AiHostTtsFailed)
            : Strings.T("automation.aiSpoken");
    }

    private async void OnVtsConnect(object sender, RoutedEventArgs e)
    {
        var ok = await AppServices.AiHost.ConnectVtsAsync().ConfigureAwait(true);
        AiText.Text = ok ? Strings.T("automation.vtsConnected") : FeatureErrors.Describe(FeatureErrors.AiHostLipsyncFailed);
    }

    private void OnSelfCheck(object sender, RoutedEventArgs e)
    {
        SelfCheck.SetBusy(true);
        try
        {
            var lines = new List<string>();
            var devices = Services.Hardware.MidiService.DeviceCount;
            lines.Add(devices > 0
                ? SelfCheckResult.Ok(Strings.T("automation.midi"), devices.ToString())
                : SelfCheckResult.Warn(Strings.T("automation.midi"), Strings.T("selfcheck.midiHint")));
            var wled = WledCore.ValidateUrl(WledUrlBox.Text.Trim());
            lines.Add(wled.Ok
                ? SelfCheckResult.Ok(Strings.T("automation.wled"), WledUrlBox.Text.Trim())
                : SelfCheckResult.Warn(Strings.T("automation.wled"), Strings.T("selfcheck.wledHint")));
            var themesRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OBS_Helper", "themes");
            var writable = false;
            try { Directory.CreateDirectory(themesRoot); writable = true; } catch (Exception) { }
            lines.Add(writable
                ? SelfCheckResult.Ok(Strings.T("automation.theme"), themesRoot)
                : SelfCheckResult.Fail(Strings.T("automation.theme"), Strings.T("selfcheck.themeHint")));
            lines.Add(!string.IsNullOrWhiteSpace(PluginManifestBox.Text) && !string.IsNullOrWhiteSpace(PluginKeyBox.Text)
                ? SelfCheckResult.Ok(Strings.T("automation.pluginTrust"))
                : SelfCheckResult.Warn(Strings.T("automation.pluginTrust"), Strings.T("selfcheck.pluginHint")));
            lines.Add(_team.Members.Count > 0
                ? SelfCheckResult.Ok(Strings.T("automation.team"), _team.Members.Count.ToString())
                : SelfCheckResult.Warn(Strings.T("automation.team"), Strings.T("selfcheck.teamHint")));
            lines.Add(AppServices.AiHost.TtsAvailable
                ? SelfCheckResult.Ok(Strings.T("automation.aihost"), Strings.T("selfcheck.ok"))
                : SelfCheckResult.Fail(Strings.T("automation.aihost"), Strings.T("selfcheck.ttsHint")));
            lines.Add(AppServices.AiHost.VtsConnected
                ? SelfCheckResult.Ok("VTube Studio")
                : SelfCheckResult.Warn("VTube Studio", Strings.T("selfcheck.vtsHint")));
            lines.Add(DirectorCore.HasConflict(DirectorCore.Defaults)
                ? SelfCheckResult.Warn(Strings.T("automation.director"), Strings.T("err.DIRECTOR_RULE_CONFLICT"))
                : SelfCheckResult.Ok(Strings.T("automation.director")));
            SelfCheck.SetResult(SelfCheckResult.Join(lines));
        }
        finally { SelfCheck.SetBusy(false); }
    }

    private void OnLoadDefaults(object sender, RoutedEventArgs e)
    {
        _mappings.Clear();
        _mappings.AddRange(MidiCore.Defaults());
        RefreshMidi();
    }

    private void RefreshMidi()
    {
        MidiList.Items.Clear();
        foreach (var mapping in _mappings)
            MidiList.Items.Add($"{mapping.Device} ch{mapping.Channel} note={mapping.Note} cc={mapping.Controller} → {MidiActionLabel(mapping.Action)}");
    }

    private void OnValidateMidi(object sender, RoutedEventArgs e)
    {
        var (ok, code, detail) = MidiCore.Validate(_mappings);
        MidiText.Text = ok
            ? Strings.T("automation.midiValid", _mappings.Count)
            : FeatureErrors.Describe(code) + " · " + detail;
    }

    private void OnExportMidi(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "JSON|*.json", FileName = "midi-mappings.json" };
        if (dialog.ShowDialog() != true) return;
        File.WriteAllText(dialog.FileName, MidiCore.Export(_mappings));
        AppServices.Toast.Show(Strings.T("common.saved"));
    }

    private async void OnTestLight(object sender, RoutedEventArgs e)
    {
        var url = WledUrlBox.Text.Trim();
        if (!WledCore.ValidateUrl(url).Ok)
        {
            WledText.Text = Strings.T("selfcheck.wledHint");
            return;
        }
        var id = (WledPresetCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "idle";
        var preset = WledCore.Presets.FirstOrDefault(p => p.Id == id) ?? WledCore.Presets[0];
        var ok = await _wled.ApplyAsync(url, preset).ConfigureAwait(true);
        WledText.Text = ok
            ? Strings.T("automation.lightApplied", WledPresetLabel(preset.Id))
            : FeatureErrors.Describe(_wled.LastError ?? FeatureErrors.HardWledTimeout);
    }

    private void OnValidateTheme(object sender, RoutedEventArgs e)
    {
        var source = ThemeSourceBox.Text.Trim();
        if (source.Length == 0)
        {
            ThemeText.Text = Strings.T("automation.themeSourceHint");
            return;
        }
        try
        {
            var package = JsonSerializer.Deserialize<ThemeMarketCore.ThemePackage>(ThemeBox.Text);
            if (package is null)
            {
                ThemeText.Text = FeatureErrors.Describe(FeatureErrors.ThemeUnsafeAsset);
                return;
            }
            var (ok, code, issues) = AppServices.Themes.Validate(package, source);
            ThemeText.Text = ok
                ? Strings.T("automation.themeValid")
                : FeatureErrors.Describe(code) + " · " + string.Join("; ", issues);
        }
        catch (JsonException)
        {
            ThemeText.Text = FeatureErrors.Describe(FeatureErrors.ThemeUnsafeAsset);
        }
    }

    private void OnInvite(object sender, RoutedEventArgs e)
    {
        _invite = _team.CreateInvite(TeamRolesCore.Role.Operator);
        InviteBox.Text = _invite.Code;
        TeamText.Text = Strings.T("automation.inviteCreated", _invite.Code,
            _invite.ExpiresUtc.ToLocalTime().ToString("HH:mm"));
    }

    private void OnAcceptInvite(object sender, RoutedEventArgs e)
    {
        if (_invite is null)
        {
            TeamText.Text = Strings.T("automation.inviteFirst");
            return;
        }
        var name = MemberNameBox.Text.Trim();
        if (name.Length == 0) name = "operator-" + (_team.Members.Count + 1);
        var (ok, code, member) = _team.Accept(_invite, name, DateTimeOffset.Now);
        if (!ok)
        {
            TeamText.Text = FeatureErrors.Describe(code);
            return;
        }
        TeamText.Text = Strings.T("automation.memberAccepted", member?.Name ?? name);
        _invite = null;
        InviteBox.Text = "";
        RefreshTeam();
    }

    private void OnKickLast(object sender, RoutedEventArgs e)
    {
        var last = _team.Members.LastOrDefault();
        if (last is null)
        {
            TeamText.Text = Strings.T("automation.teamEmpty");
            return;
        }
        _team.Kick(last.Id);
        TeamText.Text = Strings.T("automation.memberKicked", last.Name);
        RefreshTeam();
    }

    private void RefreshTeam()
    {
        TeamList.Items.Clear();
        foreach (var member in _team.Members)
            TeamList.Items.Add($"{member.Name} · {RoleLabel(member.Role)} · {member.JoinedUtc:HH:mm}");
        foreach (var audit in _team.AuditLog.TakeLast(5))
            TeamList.Items.Add($"audit {audit.At:HH:mm} {audit.Action} {audit.Detail}");
        if (_team.Members.Count == 0 && TeamText.Text.Length == 0)
            TeamText.Text = Strings.T("automation.teamEmpty");
    }

    private void OnLipsync(object sender, RoutedEventArgs e)
    {
        var value = AiHostCore.LipSyncValue(AiTextBox.Text, 0.5);
        AiText.Text = Strings.T("automation.lipsyncValue", value.ToString("0.00"), AiHostCore.Label(IsChineseUi));
    }

    private void OnInterrupt(object sender, RoutedEventArgs e)
    {
        AiText.Text = AiHostCore.ShouldInterrupt(AiTextBox.Text)
            ? Strings.T("automation.interrupted")
            : Strings.T("automation.notInterrupted");
    }

    private void OnDirector(object sender, RoutedEventArgs e)
    {
        _directorSuggestion = DirectorCore.Evaluate(DirectorCore.Defaults, DirectorBox.Text.Trim());
        if (_directorSuggestion is null)
        {
            DirectorText.Text = Strings.T("automation.director.noMatch");
            DirectorApplyButton.IsEnabled = false;
            return;
        }
        var conflict = DirectorCore.HasConflict(DirectorCore.Defaults);
        DirectorText.Text = Strings.T("automation.director.suggestion", _directorSuggestion.Scene) +
            (conflict ? " " + Strings.T("automation.director.conflict") : "");
        DirectorApplyButton.IsEnabled = true;
    }

    private async void OnDirectorApply(object sender, RoutedEventArgs e)
    {
        if (_directorSuggestion is null) return;
        if (!AppServices.Obs.IsConnected)
        {
            DirectorText.Text = FeatureErrors.Describe(FeatureErrors.MobObsOffline);
            return;
        }
        var scene = _directorSuggestion.Scene;
        if (MessageBox.Show(Strings.T("automation.director.confirm", scene), Strings.T("automation.director"),
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        var result = await AppServices.Obs.SetSceneAsync(scene).ConfigureAwait(true);
        DirectorText.Text = result.Ok
            ? Strings.T("automation.director.applied", scene)
            : FeatureErrors.Describe(FeatureErrors.MobObsOffline) + " · " + (result.Comment ?? "");
    }
}
