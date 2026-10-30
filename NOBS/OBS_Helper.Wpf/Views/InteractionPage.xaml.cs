using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using OBS_Helper.Wpf.Navigation;
using OBS_Helper.Wpf.Services.Mobile;
using OBS_Helper.Wpf.Services.Chat;
using OBS_Helper.Wpf.Services.Clips;
using OBS_Helper.Wpf.Services.Subtitles;

namespace OBS_Helper.Wpf.Views;

/// <summary>直播互动页（C01~C10、S01~S03、S05/S06/S10 的桌面入口）。</summary>
public partial class InteractionPage : UserControl, INavigationAware
{
    private readonly ChatInteractionCore.SongQueue _songs = new();
    private readonly ChatInteractionCore.Raffle _raffle = new();
    private readonly List<ChatMessage> _messages = new();
    private readonly List<HighlightCore.Clip> _clips = new();
    private readonly List<CalendarCore.Entry> _calendar = new();
    private ChatInteractionCore.Poll? _poll;
    private ChatMessage? _lastMessage;
    private System.Windows.Threading.DispatcherTimer? _calendarTimer;
    private IChatAdapter? _adapter;
    private bool _subscribed;

    public InteractionPage()
    {
        InitializeComponent();
        PlatformCombo.Items.Add(new ComboBoxItem { Content = Strings.T("chat.bilibili"), Tag = "bilibili" });
        PlatformCombo.Items.Add(new ComboBoxItem { Content = Strings.T("chat.twitch"), Tag = "twitch" });
        PlatformCombo.SelectedIndex = 0;
        FfmpegBox.Text = AppServices.Sidecars.Manifest.Find("ffmpeg") is { } ff
            ? AppServices.Sidecars.InstalledExecutable(ff) ?? "" : "";
        WhisperBox.Text = AppServices.Sidecars.Manifest.Find("whisper") is { } w
            ? AppServices.Sidecars.InstalledExecutable(w) ?? "" : "";
        ModelBox.Text = Path.Combine(AppServices.Sidecars.Root, "models", "ggml-base.bin");
    }

    public Task OnNavigatedToAsync(object? parameter)
    {
        if (!_subscribed)
        {
            AppServices.ChatHub.MessageAccepted += OnChatMessage;
            AppServices.ChatHub.AlertRaised += OnAlert;
            AppServices.Asr.CaptionReady += OnCaption;
            _subscribed = true;
        }
        UpdateChatState();
        return Task.CompletedTask;
    }

    public Task OnNavigatedFromAsync()
    {
        if (_subscribed)
        {
            AppServices.ChatHub.MessageAccepted -= OnChatMessage;
            AppServices.ChatHub.AlertRaised -= OnAlert;
            AppServices.Asr.CaptionReady -= OnCaption;
            _subscribed = false;
        }
        return Task.CompletedTask;
    }

    // ------------------------------------------------------------ 弹幕

    private void OnChatStart(object sender, RoutedEventArgs e)
    {
        var platform = (PlatformCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "bilibili";
        var room = RoomBox.Text.Trim();
        if (platform == "bilibili")
        {
            if (!BilibiliPacketCore.IsValidRoomId(room))
            {
                AppServices.Toast.Show(FeatureErrors.Describe(FeatureErrors.ChatRoomInvalid));
                return;
            }
            _adapter = AppServices.ChatHub.StartBilibili(int.Parse(room));
        }
        else
        {
            if (TwitchIrcCore.NormalizeChannel(room).Length == 0)
            {
                AppServices.Toast.Show(FeatureErrors.Describe(FeatureErrors.ChatRoomInvalid));
                return;
            }
            _adapter = AppServices.ChatHub.StartTwitch(room);
        }
        var overlayUrl = AppServices.ChatOverlay.Start();
        OverlayText.Text = overlayUrl is null ? "" : Strings.T("chat.overlay") + " · " + overlayUrl;
        ChatStartButton.IsEnabled = false;
        ChatStopButton.IsEnabled = true;
        UpdateChatState();
    }

    private async void OnChatStop(object sender, RoutedEventArgs e)
    {
        await AppServices.ChatHub.StopAsync().ConfigureAwait(true);
        _adapter = null;
        ChatStartButton.IsEnabled = true;
        ChatStopButton.IsEnabled = false;
        UpdateChatState();
    }

    private void UpdateChatState()
    {
        var state = _adapter?.State ?? ChatConnectionState.Disconnected;
        ChatStateText.Text = Strings.T("chat.state", state.ToString());
    }

    private void OnChatMessage(ChatMessage message)
    {
        if (message.Kind is "gift" or "superchat" && GiftEffectCheck.IsChecked == true && GiftSceneBox.Text.Trim().Length > 0)
        {
            _ = AppServices.Obs.SetSceneAsync(GiftSceneBox.Text.Trim());
        }
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _messages.Add(message);
            if (_messages.Count > 500) _messages.RemoveAt(0);
            ChatList.Items.Add($"[{message.Platform}] {message.UserName}: {message.Text}");
            while (ChatList.Items.Count > 200) ChatList.Items.RemoveAt(0);
            ChatList.ScrollIntoView(ChatList.Items[^1]);
            AppServices.ChatOverlay.Broadcast(message);
            AppServices.ViewerStats.Add(DateTimeOffset.Now, 0, 1, 0);
            AppServices.ChatHistory.Append(message);
            _lastMessage = message;
            HandleInteractionCommand(message);
        }));
    }

    private void OnAlert(ChatModerationCore.Alert alert)
        => Dispatcher.BeginInvoke(new Action(() =>
        {
            AppServices.Toast.Show($"{alert.UserName} · {alert.Reason}");
        }));

    private void HandleInteractionCommand(ChatMessage message)
    {
        if (ChatInteractionCore.ParseCommand(message.Text) is not { } parsed) return;
        switch (parsed.Command)
        {
            case "点歌":
            case "song":
                if (_songs.Add(message.UserName, parsed.Argument))
                    InteractionText.Text = Strings.T("interaction.songCount", _songs.Count);
                break;
            case "投票":
            case "vote":
                if (_poll is not null && int.TryParse(parsed.Argument, out var option))
                {
                    _poll.Vote(message.UserId, option - 1);
                    RenderPoll();
                }
                break;
            case "抽奖":
            case "raffle":
                _raffle.Enter(message.UserId);
                break;
        }
    }

    private void OnPollCreate(object sender, RoutedEventArgs e)
    {
        var options = PollOptionsBox.Text.Split(',', '，')
            .Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        if (PollQuestionBox.Text.Trim().Length == 0 || options.Count < 2) return;
        _poll = new ChatInteractionCore.Poll(PollQuestionBox.Text.Trim(), options);
        RenderPoll();
    }

    private void OnPollClose(object sender, RoutedEventArgs e)
    {
        _poll?.Close();
        RenderPoll();
    }

    private void RenderPoll()
    {
        if (_poll is null) { PollText.Text = ""; return; }
        var results = _poll.Results();
        PollText.Text = _poll.Question + (_poll.Closed ? "  ✓" : "") + "\n" +
            string.Join("\n", results.Select((r, i) => $"{i + 1}. {r.Option} — {r.Votes}"));
    }

    private void OnSubmissionAdd(object sender, RoutedEventArgs e)
    {
        var text = SubmissionBox.Text.Trim();
        if (text.Length == 0) return;
        AppServices.Submissions.Add("viewer", text);
        SubmissionBox.Text = "";
        RefreshSubmissions();
        AppServices.Toast.Show(Strings.T("interaction.submitted"));
    }

    private void RefreshSubmissions()
    {
        SubmissionList.Items.Clear();
        foreach (var item in AppServices.Submissions.Items.Where(i => i.State == "pending"))
            SubmissionList.Items.Add(new ComboBoxItem { Content = item.Text, Tag = item.Id });
    }

    private async void OnSubmissionApprove(object sender, RoutedEventArgs e)
    {
        if (SubmissionList.SelectedItem is not ComboBoxItem { Tag: string id })
        {
            AppServices.Toast.Show(Strings.T("interaction.noSubmission"));
            return;
        }
        var item = AppServices.Submissions.Items.FirstOrDefault(i => i.Id == id);
        if (item is null) return;
        var scene = await AppServices.Obs.GetCurrentProgramSceneAsync().ConfigureAwait(true);
        if (string.IsNullOrEmpty(scene)) return;
        var exists = await AppServices.Obs.RawRequestAsync("GetInputSettings",
            new Dictionary<string, object?> { ["inputName"] = "观众投稿" }).ConfigureAwait(true);
        if (!exists.Ok)
        {
            await AppServices.Obs.RawRequestAsync("CreateInput", new Dictionary<string, object?>
            {
                ["sceneName"] = scene,
                ["inputName"] = "观众投稿",
                ["inputKind"] = "text_gdiplus_v3",
                ["inputSettings"] = new Dictionary<string, object?> { ["text"] = item.Text },
                ["sceneItemEnabled"] = true,
            }).ConfigureAwait(true);
        }
        else
        {
            await AppServices.Obs.RawRequestAsync("SetInputSettings", new Dictionary<string, object?>
            {
                ["inputName"] = "观众投稿",
                ["inputSettings"] = new Dictionary<string, object?> { ["text"] = item.Text },
                ["overlay"] = true,
            }).ConfigureAwait(true);
        }
        AppServices.Submissions.SetState(id, "approved");
        RefreshSubmissions();
        AppServices.Toast.Show(Strings.T("interaction.approved"));
    }

    private void OnCalendarAdd(object sender, RoutedEventArgs e)
    {
        if (!DateTime.TryParse(CalendarTimeBox.Text.Trim(), out var start)) 
        {
            AppServices.Toast.Show(Strings.T("interaction.calendarBad"));
            return;
        }
        _calendar.Add(new CalendarCore.Entry
        {
            Title = CalendarTitleBox.Text.Trim(),
            Start = new DateTimeOffset(start),
            TimeZoneId = TimeZoneInfo.Local.Id,
        });
        RefreshCalendar();
        _calendarTimer ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _calendarTimer.Tick -= OnCalendarTick;
        _calendarTimer.Tick += OnCalendarTick;
        _calendarTimer.Start();
    }

    private void OnCalendarTick(object? sender, EventArgs e) => RefreshCalendar();

    private void RefreshCalendar()
    {
        var next = CalendarCore.Next(_calendar, DateTimeOffset.Now);
        CalendarText.Text = next is null
            ? Strings.T("interaction.calendarHint")
            : Strings.T("interaction.calendarNext", next.Title + " " + next.Start.ToLocalTime().ToString("MM-dd HH:mm"),
                CalendarCore.Countdown(CalendarCore.Remaining(next, DateTimeOffset.Now)));
    }

    private async void OnAiReply(object sender, RoutedEventArgs e)
    {
        AiReplyList.Items.Clear();
        if (_lastMessage is null) return;
        var (suggestions, code) = await new AiReplyCore().SuggestAsync(_lastMessage).ConfigureAwait(true);
        if (code.Length > 0) { AiReplyList.Items.Add(FeatureErrors.Describe(code)); return; }
        foreach (var suggestion in suggestions) AiReplyList.Items.Add(suggestion);
        AiReplyList.Items.Add(Strings.T("interaction.aiHint"));
    }

    private void OnTtsMute(object sender, RoutedEventArgs e)
        => AppServices.ChatHub.MuteTts(TtsMuteCheck.IsChecked == true);

    private void OnExportAlerts(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "CSV|*.csv", FileName = "chat-alerts.csv" };
        if (dialog.ShowDialog() != true) return;
        File.WriteAllText(dialog.FileName, AppServices.ChatHub.ExportAlerts());
        AppServices.Toast.Show(Strings.T("common.saved"));
    }

    // ------------------------------------------------------------ 字幕

    private void OnBilingualClick(object sender, RoutedEventArgs e)
        => AppServices.Asr.SetTranslator(BilingualCheck.IsChecked == true ? AppServices.Translator : null);

    private void OnVoiceControlClick(object sender, RoutedEventArgs e)
    {
        var service = AppServices.VoiceControl;
        service.Confirm = message => MessageBox.Show(message, "语音控制", MessageBoxButton.OKCancel,
            MessageBoxImage.Warning) == MessageBoxResult.OK;
        service.SetEnabled(VoiceControlCheck.IsChecked == true);
    }

    private void OnBrowseWhisper(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "whisper.cpp|whisper*.exe|可执行文件|*.exe" };
        if (dialog.ShowDialog() == true) WhisperBox.Text = dialog.FileName;
    }

    private void OnBrowseModel(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "模型|*.bin|所有文件|*.*" };
        if (dialog.ShowDialog() == true) ModelBox.Text = dialog.FileName;
    }

    private void OnCaptionStart(object sender, RoutedEventArgs e)
    {
        var configured = AppServices.Asr.Configure(WhisperBox.Text.Trim(), ModelBox.Text.Trim());
        if (!configured.Ok)
        {
            CaptionStateText.Text = FeatureErrors.Describe(configured.Code);
            return;
        }
        if (!AppServices.Asr.Start(LoopbackCheck.IsChecked == true))
        {
            CaptionStateText.Text = FeatureErrors.Describe(AppServices.Asr.LastCode);
            return;
        }
        CaptionStartButton.IsEnabled = false;
        CaptionStopButton.IsEnabled = true;
        CaptionStateText.Text = Strings.T("caption.state", "running");
    }

    private void OnCaptionStop(object sender, RoutedEventArgs e)
    {
        AppServices.Asr.Stop();
        CaptionStartButton.IsEnabled = true;
        CaptionStopButton.IsEnabled = false;
        CaptionStateText.Text = Strings.T("caption.state", "stopped");
    }

    private void OnCaption(string text, string display)
        => Dispatcher.BeginInvoke(new Action(() =>
        {
            CaptionText.Text = display;
            AppServices.RecordingSearch.Add(new RecordingSearchCore.Segment
            {
                File = AppServices.Obs.LastRecordFile,
                Text = text,
                At = AppServices.Obs.RecordElapsed,
            });
        }));

    // ------------------------------------------------------------ 互动

    private void OnAddSong(object sender, RoutedEventArgs e)
    {
        var title = SongBox.Text.Trim();
        if (title.Length == 0) return;
        _songs.Add("me", title);
        SongBox.Text = "";
        InteractionText.Text = Strings.T("interaction.songCount", _songs.Count);
    }

    private void OnNextSong(object sender, RoutedEventArgs e)
    {
        var next = _songs.Next();
        InteractionText.Text = next is null ? Strings.T("interaction.songCount", 0) : $"▶ {next.Value.Title}";
    }

    private void OnDrawRaffle(object sender, RoutedEventArgs e)
    {
        var winner = _raffle.Draw(Environment.TickCount);
        InteractionText.Text = winner ?? "—";
    }

    // ------------------------------------------------------------ 切片 / 检索

    private void OnBrowseFfmpeg(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "FFmpeg|ffmpeg.exe|可执行文件|*.exe" };
        if (dialog.ShowDialog() == true) FfmpegBox.Text = dialog.FileName;
    }

    private void OnFindHighlights(object sender, RoutedEventArgs e)
    {
        _clips.Clear();
        ClipList.Items.Clear();
        if (_messages.Count == 0)
        {
            ClipStateText.Text = Strings.T("clips.noChat");
            return;
        }
        var signals = _messages
            .GroupBy(m => new DateTimeOffset(m.At.Year, m.At.Month, m.At.Day, m.At.Hour, m.At.Minute, m.At.Second, m.At.Offset))
            .Select(g => new HighlightCore.Signal
            {
                At = g.Key,
                ChatRate = g.Count(),
                AudioEnergy = 0,
                SceneChanged = false,
            });
        foreach (var clip in HighlightCore.FindHighlights(signals))
        {
            _clips.Add(clip);
            ClipList.Items.Add($"{clip.From:HH:mm:ss} → {clip.To:HH:mm:ss} · {clip.Score:0.0} · {clip.Reason}");
        }
        ClipStateText.Text = _clips.Count == 0 ? "—" : $"×{_clips.Count}";
    }

    private async void OnExportClip(object sender, RoutedEventArgs e) => await ExportClipAsync(false);

    private async void OnExportVertical(object sender, RoutedEventArgs e) => await ExportClipAsync(true);

    private async Task ExportClipAsync(bool vertical)
    {
        if (_clips.Count == 0)
        {
            ClipStateText.Text = Strings.T("clips.noChat");
            return;
        }
        var open = new OpenFileDialog { Filter = "录像|*.mp4;*.mkv;*.mov|所有文件|*.*" };
        if (open.ShowDialog() != true) return;
        var save = new SaveFileDialog { Filter = "MP4|*.mp4", FileName = (vertical ? "vertical-" : "highlight-") + DateTime.Now.ToString("HHmmss") + ".mp4" };
        if (save.ShowDialog() != true) return;

        var clip = _clips[0];
        var recordStart = AppServices.Obs.RecordStartedUtc?.ToLocalTime() ?? clip.From;
        var from = clip.From - recordStart;
        if (from < TimeSpan.Zero) from = TimeSpan.Zero;
        var duration = clip.To - clip.From;
        if (duration <= TimeSpan.Zero) duration = TimeSpan.FromSeconds(15);

        var runner = new FfmpegRunner(FfmpegBox.Text.Trim());
        var args = vertical
            ? FfmpegArgs.BuildVertical(open.FileName, save.FileName, 656, 0, 607, 1080)
            : FfmpegArgs.BuildClip(open.FileName, save.FileName, from, duration);
        ClipStateText.Text = Strings.T("common.busy");
        var (ok, code, output) = await runner.RunAsync(args).ConfigureAwait(true);
        ClipStateText.Text = ok
            ? "✓ " + save.FileName
            : FeatureErrors.Describe(code) + " · " + output;
    }

    private void OnSearchTranscripts(object sender, RoutedEventArgs e)
    {
        SearchList.Items.Clear();
        foreach (var segment in AppServices.RecordingSearch.Search(SearchBox.Text))
            SearchList.Items.Add($"{segment.File} · {segment.At:hh\\:mm\\:ss} · {segment.Text}");
    }

    private void OnClearIndex(object sender, RoutedEventArgs e)
    {
        AppServices.RecordingSearch.Clear();
        SearchList.Items.Clear();
    }
}
