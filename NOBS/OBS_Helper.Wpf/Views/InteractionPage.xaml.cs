using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using OBS_Helper.Wpf.Navigation;
using OBS_Helper.Wpf.Services.Mobile;
using OBS_Helper.Wpf.Services.Chat;
using OBS_Helper.Wpf.Services.Clips;
using OBS_Helper.Wpf.Services.Shell;
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
        FfmpegBox.Text = StoredOrSidecar("ffmpeg");
        WhisperBox.Text = StoredOrSidecar("whisper");
        ModelBox.Text = Path.Combine(AppServices.Sidecars.Root, "models", "ggml-base.bin");
    }

    /// <summary>V3.4.1：优先使用「一键配置」写入的路径，其次 sidecar 安装目录。</summary>
    private static string StoredOrSidecar(string id)
    {
        var stored = AppServices.Store.GetItem("dep." + id + ".path");
        if (!string.IsNullOrWhiteSpace(stored) && File.Exists(stored)) return stored;
        var entry = AppServices.Sidecars.Manifest.Find(id);
        return entry is null ? "" : AppServices.Sidecars.InstalledExecutable(entry) ?? "";
    }

    public Task OnNavigatedToAsync(object? parameter)
    {
        if (!_subscribed)
        {
            AppServices.ChatHub.MessageAccepted += OnChatMessage;
            AppServices.ChatHub.AlertRaised += OnAlert;
            AppServices.ChatHub.StateChanged += OnChatStateChanged;
            AppServices.Asr.CaptionReady += OnCaption;
            _subscribed = true;
        }
        TtsMuteCheck.IsChecked = AppServices.ChatHub.Tts.Muted;
        if (AppServices.ChatOverlay.IsRunning)
            OverlayText.Text = Strings.T("chat.overlay") + " · " + AppServices.ChatOverlay.BaseUrl;
        UpdateChatState();
        RefreshSubmissions();
        RefreshCalendar();
        return Task.CompletedTask;
    }

    public Task OnNavigatedFromAsync()
    {
        if (_subscribed)
        {
            AppServices.ChatHub.MessageAccepted -= OnChatMessage;
            AppServices.ChatHub.AlertRaised -= OnAlert;
            AppServices.ChatHub.StateChanged -= OnChatStateChanged;
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
        // 以 ChatHub 里的真实适配器为准：页面重新进入时 _adapter 可能已失效，
        // 但后台连接仍在（自动重连/从别的页面发起），连接状态不能因此显示成“未连接”。
        var adapters = AppServices.ChatHub.Adapters;
        var state = adapters.Count == 0
            ? ChatConnectionState.Disconnected
            : adapters.Any(a => a.State == ChatConnectionState.Connected)
                ? ChatConnectionState.Connected
                : adapters.Any(a => a.State == ChatConnectionState.Connecting)
                    ? ChatConnectionState.Connecting
                    : adapters.Any(a => a.State == ChatConnectionState.Failed)
                        ? ChatConnectionState.Failed
                        : ChatConnectionState.Disconnected;
        var label = state switch
        {
            ChatConnectionState.Connected => Strings.T("chat.state.connected"),
            ChatConnectionState.Connecting => Strings.T("chat.state.connecting"),
            ChatConnectionState.Failed => Strings.T("chat.state.failed"),
            _ => Strings.T("chat.state.disconnected"),
        };
        ChatStateText.Text = Strings.T("chat.state", label);
        ChatStartButton.IsEnabled = state == ChatConnectionState.Disconnected;
        ChatStopButton.IsEnabled = state != ChatConnectionState.Disconnected;
    }

    private void OnChatStateChanged(ChatConnectionState state)
        => Dispatcher.BeginInvoke(new Action(UpdateChatState));

    private void OnSelfCheck(object sender, RoutedEventArgs e)
    {
        SelfCheck.SetBusy(true);
        try
        {
            var lines = new List<string>();
            var adapters = AppServices.ChatHub.Adapters;
            var connected = adapters.Any(a => a.State == ChatConnectionState.Connected);
            lines.Add(connected
                ? SelfCheckResult.Ok(Strings.T("chat.title"), string.Join(" / ", adapters.Select(a => a.Platform)))
                : SelfCheckResult.Warn(Strings.T("chat.title"), Strings.T("selfcheck.chatHint")));

            var platform = (PlatformCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "bilibili";
            var room = RoomBox.Text.Trim();
            var roomOk = platform == "bilibili"
                ? BilibiliPacketCore.IsValidRoomId(room)
                : TwitchIrcCore.NormalizeChannel(room).Length > 0;
            lines.Add(roomOk
                ? SelfCheckResult.Ok(Strings.T("chat.roomLabel"), room)
                : SelfCheckResult.Warn(Strings.T("chat.roomLabel"), Strings.T("selfcheck.chatHint")));

            lines.Add(AppServices.ChatOverlay.IsRunning
                ? SelfCheckResult.Ok(Strings.T("chat.overlay"), AppServices.ChatOverlay.BaseUrl)
                : SelfCheckResult.Warn(Strings.T("chat.overlay"), Strings.T("selfcheck.overlayHint")));
            lines.Add(AppServices.ChatTts.Available
                ? SelfCheckResult.Ok(Strings.T("chat.tts"))
                : SelfCheckResult.Warn(Strings.T("chat.tts"), Strings.T("selfcheck.ttsHint")));
            lines.Add(AppServices.Obs.IsConnected
                ? SelfCheckResult.Ok("OBS")
                : SelfCheckResult.Warn("OBS", Strings.T("selfcheck.obsHint")));
            lines.Add(File.Exists(WhisperBox.Text.Trim())
                ? SelfCheckResult.Ok(Strings.T("caption.whisperPath"), WhisperBox.Text.Trim())
                : SelfCheckResult.Warn(Strings.T("caption.whisperPath"), Strings.T("selfcheck.whisperHint")));
            lines.Add(File.Exists(ModelBox.Text.Trim())
                ? SelfCheckResult.Ok(Strings.T("caption.modelPath"), ModelBox.Text.Trim())
                : SelfCheckResult.Warn(Strings.T("caption.modelPath"), Strings.T("selfcheck.whisperHint")));
            lines.Add(File.Exists(FfmpegBox.Text.Trim())
                ? SelfCheckResult.Ok(Strings.T("clips.ffmpegPath"), FfmpegBox.Text.Trim())
                : SelfCheckResult.Warn(Strings.T("clips.ffmpegPath"), Strings.T("selfcheck.ffmpegHint")));
            lines.Add(SelfCheckResult.Ok(Strings.T("clips.index"), AppServices.RecordingSearch.Count.ToString()));
            lines.Add(SelfCheckResult.Ok(Strings.T("interaction.pending"),
                AppServices.Submissions.Items.Count(i => i.State == "pending").ToString()));
            SelfCheck.SetResult(SelfCheckResult.Join(lines));
        }
        finally { SelfCheck.SetBusy(false); }
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
            if (TranslateCheck.IsChecked == true) _ = TranslateAndBroadcastAsync(message);
            else AppServices.ChatOverlay.Broadcast(message);
            AppServices.ChatHistory.Append(message);
            _lastMessage = message;
            HandleInteractionCommand(message);
        }));
    }

    /// <summary>C08：弹幕翻译只在本机完成（本地短语表），译文同时进聊天列表与 overlay。</summary>
    private async Task TranslateAndBroadcastAsync(ChatMessage message)
    {
        var target = Strings.Current.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? "zh" : "en";
        var (_, translated, code) = await AppServices.Translator.TranslateAsync(message.Text, target).ConfigureAwait(false);
        var changed = code.Length == 0 && !string.Equals(translated, message.Text, StringComparison.Ordinal);
        if (changed)
        {
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                ChatList.Items.Add("   ↳ " + translated);
                while (ChatList.Items.Count > 200) ChatList.Items.RemoveAt(0);
                ChatList.ScrollIntoView(ChatList.Items[^1]);
            }));
        }
        AppServices.ChatOverlay.Broadcast(new ChatMessage
        {
            Platform = message.Platform,
            Room = message.Room,
            UserId = message.UserId,
            UserName = message.UserName,
            Text = changed ? TranslationCore.BilingualLine(message.Text, translated) : message.Text,
            Kind = message.Kind,
            Value = message.Value,
            At = message.At,
            IsModerated = message.IsModerated,
        });
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
        if (!AppServices.Obs.IsConnected)
        {
            AppServices.Toast.Show(FeatureErrors.Describe(FeatureErrors.MobObsOffline));
            return;
        }
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
        if (_lastMessage is null)
        {
            AiReplyList.Items.Add(Strings.T("selfcheck.chatHint"));
            return;
        }
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

        var clip = _clips[ClipList.SelectedIndex >= 0 ? ClipList.SelectedIndex : 0];
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
        if (ok) AppServices.ViewerStats.AddClip();
        ClipStateText.Text = ok
            ? "✓ " + save.FileName
            : FeatureErrors.Describe(code) + " · " + output;
    }

    private void OnSearchTranscripts(object sender, RoutedEventArgs e)
    {
        SearchList.Items.Clear();
        var results = AppServices.RecordingSearch.Search(SearchBox.Text).ToList();
        if (results.Count == 0)
        {
            SearchList.Items.Add(Strings.T("clips.noResults"));
            return;
        }
        foreach (var segment in results)
            SearchList.Items.Add($"{segment.File} · {segment.At:hh\\:mm\\:ss} · {segment.Text}");
    }

    private void OnClearIndex(object sender, RoutedEventArgs e)
    {
        AppServices.RecordingSearch.Clear();
        SearchList.Items.Clear();
        AppServices.Toast.Show(Strings.T("clips.indexCleared"));
    }
}
