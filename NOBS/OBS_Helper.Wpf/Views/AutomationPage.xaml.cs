using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using OBS_Helper.Wpf.Navigation;
using OBS_Helper.Wpf.Services.Automation;
using OBS_Helper.Wpf.Services.Mobile;
using OBS_Helper.Wpf.Services.Clips;

namespace OBS_Helper.Wpf.Views;

/// <summary>自动化与生态页（E01~E06、S04、S09）。</summary>
public partial class AutomationPage : UserControl, INavigationAware
{
    private readonly List<MidiCore.Mapping> _mappings = new();
    private readonly TeamRolesCore _team = new();
    private readonly WledClient _wled = new();
    private TeamRolesCore.Invite? _invite;

    public AutomationPage()
    {
        InitializeComponent();
        foreach (var preset in WledCore.Presets) WledPresetCombo.Items.Add(preset.Id);
        WledPresetCombo.SelectedIndex = 1;
        AiLabelText.Text = AiHostCore.Label();
        ThemeBox.Text = "{\n  \"id\": \"demo\",\n  \"name\": \"Demo\",\n  \"version\": \"1.0.0\",\n  \"files\": [\n    { \"path\": \"chat.css\", \"sha256\": \"0000000000000000000000000000000000000000000000000000000000000000\", \"size\": 1 }\n  ]\n}";
    }

    public Task OnNavigatedToAsync(object? parameter)
    {
        RefreshTeam();
        RefreshMidiDevices();
        var lighting = AppServices.Lighting.Current;
        WledUrlBox.Text = lighting.BaseUrl;
        LightEnableCheck.IsChecked = lighting.Enabled;
        MidiDeviceText.Text = Strings.T("automation.midiDevices", Services.Hardware.MidiService.DeviceCount);
        return Task.CompletedTask;
    }

    private void RefreshMidiDevices()
        => MidiDeviceText.Text = Strings.T("automation.midiDevices", Services.Hardware.MidiService.DeviceCount);

    private void OnMidiStart(object sender, RoutedEventArgs e)
    {
        var service = AppServices.Midi;
        service.SetMappings(_mappings);
        service.Error += message => Dispatcher.BeginInvoke(new Action(() => MidiText.Text = message));
        if (service.Start())
        {
            MidiStartButton.IsEnabled = false;
            MidiStopButton.IsEnabled = true;
            MidiText.Text = Strings.T("automation.midiRunning");
        }
    }

    private void OnMidiStop(object sender, RoutedEventArgs e)
    {
        AppServices.Midi.Stop();
        MidiStartButton.IsEnabled = true;
        MidiStopButton.IsEnabled = false;
    }

    private void OnLightSave(object sender, RoutedEventArgs e)
    {
        var preset = WledCore.Presets[Math.Max(0, WledPresetCombo.SelectedIndex)].Id;
        AppServices.Lighting.ApplySettings(new Services.Hardware.LightingService.Settings
        {
            Enabled = LightEnableCheck.IsChecked == true,
            BaseUrl = WledUrlBox.Text.Trim(),
            GiftPreset = "gift",
            LivePreset = preset,
            IdlePreset = "idle",
        });
        WledText.Text = Strings.T("common.saved");
    }

    private void OnThemeApply(object sender, RoutedEventArgs e)
    {
        try
        {
            var package = JsonSerializer.Deserialize<ThemeMarketCore.ThemePackage>(ThemeBox.Text);
            if (package is null) { ThemeText.Text = FeatureErrors.Describe(FeatureErrors.ThemeUnsafeAsset); return; }
            var (ok, code, detail) = AppServices.Themes.Apply(package, ThemeSourceBox.Text.Trim());
            ThemeText.Text = ok ? "✓ " + detail : FeatureErrors.Describe(code) + " · " + detail;
        }
        catch (JsonException) { ThemeText.Text = FeatureErrors.Describe(FeatureErrors.ThemeUnsafeAsset); }
    }

    private void OnThemeRollback(object sender, RoutedEventArgs e)
    {
        try
        {
            var package = JsonSerializer.Deserialize<ThemeMarketCore.ThemePackage>(ThemeBox.Text);
            if (package is null) return;
            var (ok, code) = AppServices.Themes.Rollback(package.Id);
            ThemeText.Text = ok ? Strings.T("common.saved") : FeatureErrors.Describe(code);
        }
        catch (JsonException) { }
    }

    private void OnPluginVerify(object sender, RoutedEventArgs e)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize<PluginTrustCore.TrustManifest>(PluginManifestBox.Text);
            if (manifest is null) { PluginText.Text = "manifest"; return; }
            var valid = PluginTrustCore.VerifySignature(manifest, PluginPayloadBox.Text, PluginKeyBox.Text);
            var (ok, issues) = PluginTrustCore.Check(manifest, PluginTrustCore.DefaultPolicy, valid);
            PluginText.Text = (ok ? "✓ " : "✗ ") + PluginTrustCore.TrustLabel(manifest.Rating) +
                " · " + string.Join("; ", issues);
        }
        catch (JsonException) { PluginText.Text = "manifest"; }
    }

    private async void OnAiSpeak(object sender, RoutedEventArgs e)
    {
        AiText.Text = AiHostCore.Label() + " · " + AiTextBox.Text;
        await AppServices.AiHost.SpeakAsync(AiTextBox.Text).ConfigureAwait(true);
    }

    private async void OnVtsConnect(object sender, RoutedEventArgs e)
    {
        var ok = await AppServices.AiHost.ConnectVtsAsync().ConfigureAwait(true);
        AiText.Text = ok ? "VTS connected" : FeatureErrors.Describe(FeatureErrors.AiHostLipsyncFailed);
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
            MidiList.Items.Add($"{mapping.Device} ch{mapping.Channel} note={mapping.Note} cc={mapping.Controller} → {mapping.Action}");
    }

    private void OnValidateMidi(object sender, RoutedEventArgs e)
    {
        var (ok, code, detail) = MidiCore.Validate(_mappings);
        MidiText.Text = ok ? "✓" : FeatureErrors.Describe(code) + " · " + detail;
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
        var preset = WledCore.Presets[Math.Max(0, WledPresetCombo.SelectedIndex)];
        var ok = await _wled.ApplyAsync(WledUrlBox.Text.Trim(), preset).ConfigureAwait(true);
        WledText.Text = ok ? "✓ " + preset.Id : FeatureErrors.Describe(_wled.LastError ?? FeatureErrors.HardWledTimeout);
    }

    private void OnValidateTheme(object sender, RoutedEventArgs e)
    {
        try
        {
            var package = JsonSerializer.Deserialize<ThemeMarketCore.ThemePackage>(ThemeBox.Text);
            if (package is null)
            {
                ThemeText.Text = FeatureErrors.Describe(FeatureErrors.ThemeUnsafeAsset);
                return;
            }
            var (ok, code, issues) = ThemeMarketCore.Validate(package);
            ThemeText.Text = ok ? "✓" : FeatureErrors.Describe(code) + " · " + string.Join("; ", issues);
        }
        catch (JsonException)
        {
            ThemeText.Text = FeatureErrors.Describe(FeatureErrors.ThemeUnsafeAsset);
        }
    }

    private void OnInvite(object sender, RoutedEventArgs e)
    {
        _invite = _team.CreateInvite(TeamRolesCore.Role.Operator);
        var (ok, code, member) = _team.Accept(_invite, "operator-" + _team.Members.Count, DateTimeOffset.Now);
        InviteBox.Text = _invite.Code;
        RefreshTeam();
        if (!ok) AppServices.Toast.Show(FeatureErrors.Describe(code));
    }

    private void OnKickLast(object sender, RoutedEventArgs e)
    {
        var last = _team.Members.LastOrDefault();
        if (last is null) return;
        _team.Kick(last.Id);
        RefreshTeam();
    }

    private void RefreshTeam()
    {
        TeamList.Items.Clear();
        foreach (var member in _team.Members)
            TeamList.Items.Add($"{member.Name} · {member.Role} · {member.JoinedUtc:HH:mm}");
        foreach (var audit in _team.AuditLog.TakeLast(5))
            TeamList.Items.Add($"audit {audit.At:HH:mm} {audit.Action} {audit.Detail}");
    }

    private void OnLipsync(object sender, RoutedEventArgs e)
    {
        var value = AiHostCore.LipSyncValue(AiTextBox.Text, 0.5);
        AiText.Text = $"MouthOpen = {value:0.00} · {AiHostCore.Label()}";
    }

    private void OnInterrupt(object sender, RoutedEventArgs e)
    {
        AiText.Text = AiHostCore.ShouldInterrupt(AiTextBox.Text) ? "已打断" : "未命中打断词";
    }

    private void OnDirector(object sender, RoutedEventArgs e)
    {
        var suggestion = DirectorCore.Evaluate(DirectorCore.Defaults, DirectorBox.Text.Trim());
        DirectorText.Text = suggestion is null
            ? "没有匹配的规则"
            : DirectorCore.Describe(suggestion) + (DirectorCore.HasConflict(DirectorCore.Defaults) ? "（规则冲突）" : "");
    }
}
