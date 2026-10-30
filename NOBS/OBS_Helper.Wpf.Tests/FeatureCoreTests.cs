using OBS_Helper.Wpf.Services.Analytics;
using OBS_Helper.Wpf.Services.Automation;
using OBS_Helper.Wpf.Services.Chat;
using OBS_Helper.Wpf.Services.Clips;
using OBS_Helper.Wpf.Services.Live;
using OBS_Helper.Wpf.Services.Mobile;
using OBS_Helper.Wpf.Services.Streaming;
using OBS_Helper.Wpf.Services.Subtitles;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

public class ChatCoreTests
{
    [Fact]
    public void TextIsNormalizedAndLimited()
    {
        Assert.Equal("hello world", ChatCore.NormalizeText("  hello\u0000 world\u200B  "));
        Assert.Equal(500, ChatCore.NormalizeText(new string('x', 600)).Length);
        Assert.Null(ChatCore.Create(ChatPlatform.Twitch, "r", "u", "n", "   "));
    }

    [Fact]
    public void DeduperAndRateWindowWork()
    {
        var now = DateTimeOffset.UtcNow;
        var deduper = new ChatCore.Deduper(TimeSpan.FromSeconds(5), clock: () => now);
        var message = ChatCore.Create(ChatPlatform.Twitch, "r", "u", "n", "hi")!;
        Assert.False(deduper.IsDuplicate(message));
        Assert.True(deduper.IsDuplicate(message));

        var window = new ChatCore.RateWindow(2, () => now);
        Assert.True(window.TryAccept());
        Assert.True(window.TryAccept());
        Assert.False(window.TryAccept());
        Assert.Equal(1, window.Dropped);
    }

    [Fact]
    public void BilibiliFramesSplitAndParse()
    {
        var auth = BilibiliPacketCore.BuildAuthPacket(123, "token");
        var packets = BilibiliPacketCore.SplitPackets(auth);
        Assert.NotNull(packets);
        Assert.Single(packets!);
        Assert.Equal(BilibiliPacketCore.OpAuth, packets[0].Operation);
        Assert.Contains("123", System.Text.Encoding.UTF8.GetString(packets[0].Body));

        var json = "{\"cmd\":\"DANMU_MSG\",\"info\":[[],\"大家好\",[123,\"小明\"]]}";
        var frame = BilibiliPacketCore.BuildFrame(BilibiliPacketCore.OpMessage, System.Text.Encoding.UTF8.GetBytes(json));
        var parsed = BilibiliPacketCore.ParseMessages("123", frame[16..]).ToList();
        Assert.Single(parsed);
        Assert.Equal("大家好", parsed[0].Text);
        Assert.Equal("小明", parsed[0].UserName);
    }

    [Fact]
    public void BilibiliTruncatedFrameReturnsNull()
    {
        var frame = BilibiliPacketCore.BuildFrame(BilibiliPacketCore.OpMessage, new byte[] { 1, 2, 3 });
        Assert.Null(BilibiliPacketCore.SplitPackets(frame.AsSpan(0, frame.Length - 1)));
    }

    [Fact]
    public void TwitchParsesTagsAndPing()
    {
        var line = "@display-name=Foo;login=foo :foo!foo@foo.tmi.twitch.tv PRIVMSG #chan :hello world";
        var message = TwitchIrcCore.ParseLine(line, "chan");
        Assert.NotNull(message);
        Assert.Equal("Foo", message!.UserName);
        Assert.Equal("hello world", message.Text);
        Assert.Equal("PONG :tmi.twitch.tv", TwitchIrcCore.ResponseFor("PING :tmi.twitch.tv"));
        Assert.Equal("", TwitchIrcCore.NormalizeChannel("#Bad Channel"));
    }

    [Fact]
    public void ModerationFlagsWordsAndSpam()
    {
        var now = DateTimeOffset.UtcNow;
        var detector = new ChatModerationCore.Detector(clock: () => now);
        var message = ChatCore.Create(ChatPlatform.Bilibili, "1", "u", "n", "加微信领福利")!;
        Assert.NotNull(detector.Check(message));
        for (var i = 0; i < 9; i++)
            detector.Check(ChatCore.Create(ChatPlatform.Bilibili, "1", "spam", "s", "正常内容")!);
        Assert.Contains(detector.Alerts, a => a.Reason == "刷屏");
        Assert.Contains("敏感词", detector.ExportCsv());
    }

    [Fact]
    public void TtsSkipsLinksAndRespectsQueue()
    {
        var now = DateTimeOffset.UtcNow;
        var tts = new ChatTtsCore(2, TimeSpan.Zero, () => now);
        var link = ChatCore.Create(ChatPlatform.Twitch, "r", "u", "n", "https://example.com")!;
        Assert.False(tts.TryEnqueue(link, out _));
        var ok = ChatCore.Create(ChatPlatform.Twitch, "r", "u", "n", "你好呀")!;
        Assert.True(tts.TryEnqueue(ok, out var spoken));
        Assert.Equal("你好呀", spoken);
        Assert.True(tts.TryDequeue(out _));
    }

    [Fact]
    public void InteractionStateMachines()
    {
        var songs = new ChatInteractionCore.SongQueue();
        Assert.True(songs.Add("a", "歌名"));
        Assert.Equal(1, songs.Count);
        Assert.Equal("歌名", songs.Next()!.Value.Title);

        var poll = new ChatInteractionCore.Poll("选一个", new[] { "A", "B" });
        Assert.True(poll.Vote("u1", 0));
        Assert.True(poll.Vote("u1", 1));
        Assert.Equal(1, poll.Results()[1].Votes);
        poll.Close();
        Assert.False(poll.Vote("u2", 0));

        var raffle = new ChatInteractionCore.Raffle();
        Assert.True(raffle.Enter("u1"));
        Assert.Equal("u1", raffle.Draw(0));
        Assert.Equal(("song", "abc"), ChatInteractionCore.ParseCommand("!song abc"));
    }

    [Fact]
    public void CalendarAndTranslation()
    {
        var entry = new CalendarCore.Entry
        {
            Title = "开播",
            Start = DateTimeOffset.UtcNow.AddHours(1),
            TimeZoneId = TimeZoneInfo.Local.Id,
        };
        Assert.NotNull(CalendarCore.Next(new[] { entry }, DateTimeOffset.UtcNow));
        Assert.Equal("01:00:00", CalendarCore.Countdown(TimeSpan.FromHours(1)));

        var core = new TranslationCore();
        var result = core.TranslateAsync("hello").GetAwaiter().GetResult();
        Assert.Equal(FeatureErrors.TransEngineMissing, result.Code);
        Assert.Equal("原\n译", TranslationCore.BilingualLine("原", "译"));
        Assert.Equal("原", TranslationCore.BilingualLine("原", "原"));
    }

    [Fact]
    public void ChatHistoryIsOptionalRetainedAndClearable()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "chatlog-" + Guid.NewGuid().ToString("N"));
        var now = new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
        var store = new ChatHistoryStore(dir, enabled: false, clock: () => now);
        store.Append(ChatCore.Create(ChatPlatform.Twitch, "r", "u", "n", "hidden")!);
        Assert.Equal(0, store.CountFiles());

        store.Enabled = true;
        store.Append(ChatCore.Create(ChatPlatform.Twitch, "r", "u", "n", "visible")!);
        Assert.Equal(1, store.CountFiles());
        Assert.Contains(store.LoadDay(DateOnly.FromDateTime(now.LocalDateTime)), line => line.Contains("visible"));

        store.Clear();
        Assert.Equal(0, store.CountFiles());
        try { Directory.Delete(dir, true); } catch { }
    }

    [Fact]
    public void AiReplyReturnsAtMostThreeSuggestions()
    {
        var core = new AiReplyCore();
        var message = ChatCore.Create(ChatPlatform.Twitch, "r", "u", "n", "hi")!;
        var (suggestions, code) = core.SuggestAsync(message).GetAwaiter().GetResult();
        Assert.Empty(code);
        Assert.InRange(suggestions.Count, 1, 3);
    }
}

public class AsrCoreTests
{
    [Fact]
    public void ResampleAndDownmix()
    {
        var stereo = new[] { 1f, -1f, 0.5f, -0.5f };
        var mono = SpeechSegmenter.DownmixToMono(stereo, 2);
        Assert.Equal(new[] { 0f, 0f }, mono);
        var resampled = SpeechSegmenter.ResampleTo16k(new float[480], 48000);
        Assert.Equal(160, resampled.Length);
    }

    [Fact]
    public void WavHeaderIsValid()
    {
        var wav = SpeechSegmenter.ToWav(new float[16000]);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Equal(16000, BitConverter.ToInt32(wav, 24));
        Assert.Equal(44 + 32000, wav.Length);
    }

    [Fact]
    public void SegmenterCutsOnSilenceAndSkipsSilence()
    {
        var now = DateTimeOffset.UtcNow;
        var segmenter = new SpeechSegmenter(threshold: 0.01f, silenceMs: 40, maxMs: 4000, clock: () => now);
        var loud = Enumerable.Repeat(0.5f, 320).ToArray();
        var silent = new float[320];
        Assert.Empty(segmenter.Push(loud, now));
        Assert.Empty(segmenter.Push(silent, now.AddMilliseconds(20)));
        var cuts = segmenter.Push(silent, now.AddMilliseconds(40));
        Assert.Single(cuts);
        Assert.Equal(960, cuts[0].Samples.Length);
        Assert.Empty(segmenter.Push(silent, now.AddMilliseconds(60)));
    }

    [Fact]
    public void SubtitleMergeSplitAndTail()
    {
        Assert.Equal("hello world", SubtitleCore.Merge("hello", "world"));
        Assert.Equal("你好世界", SubtitleCore.Merge("你好", "世界"));
        var lines = SubtitleCore.Split("第一句。第二句！第三句？");
        Assert.Equal(3, lines.Count);
        Assert.Equal("b\nc", SubtitleCore.Tail(new[] { "a", "b", "c" }));
    }

    [Fact]
    public void WhisperArgsAreSanitized()
    {
        var args = WhisperArgs.Build("model.bin", "a.wav", "zh", 4);
        Assert.Contains("-m", args);
        Assert.Contains("zh", args);
        Assert.True(WhisperArgs.IsSafePath(@"C:\temp\a.wav"));
        Assert.False(WhisperArgs.IsSafePath("a.wav|calc"));
        Assert.Equal("hello", WhisperArgs.ParseOutput("[00:00:00.000 --> 00:00:01.000] hello"));
    }
}

public class StreamClipsTests
{
    [Fact]
    public void HealthScoresAndAdvice()
    {
        var bad = StreamHealthCore.Evaluate(new StreamHealthInput
        {
            Streaming = true, ActiveFps = 20, BitrateKbps = 6000, DroppedFrames = 100, TotalFrames = 1000,
            Congestion = 0.5, UploadKbps = 6500,
        });
        Assert.True(bad.Score < 70);
        Assert.Contains(bad.Advice, a => a.Code == "STREAM_BANDWIDTH_LOW");
        var idle = StreamHealthCore.Evaluate(new StreamHealthInput { Streaming = false });
        Assert.Equal("idle", idle.Grade);
        Assert.InRange(StreamHealthCore.RecommendBitrate(10000, 1920, 1080, 60), 800, 50000);
    }

    [Fact]
    public void CompositionFlagsDarkAndOffCenter()
    {
        var (score, issues) = CompositionCheckCore.Evaluate(new CompositionSample
        {
            Brightness = 0.05, ColorTempK = 3000, SkinToneRatio = 0.01, CenterOffset = 0.5, Contrast = 0.1,
        });
        Assert.True(score < 50);
        Assert.Contains(issues, i => i.Contains("偏暗"));
    }

    [Fact]
    public void RunbookAndIncident()
    {
        var state = new RunbookCore.State();
        RunbookCore.Mark(state, "obs", true);
        Assert.False(state.AllRequiredPassed(RunbookCore.DefaultSteps));
        foreach (var step in RunbookCore.DefaultSteps.Where(s => s.Required))
            RunbookCore.Mark(state, step.Id, true);
        Assert.True(state.AllRequiredPassed(RunbookCore.DefaultSteps));

        var (from, to) = IncidentClipCore.Window(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal(60, (to - from).TotalSeconds);
        var remove = IncidentClipCore.TrimToDiskLimit(new[] { ("a", 60L), ("b", 60L) }, 100);
        Assert.Equal(new[] { "a" }, remove);
    }

    [Fact]
    public void PanicRelayAndAudioChain()
    {
        Assert.Equal(2, PanicCore.Suggest(true, true, false, false).Count);
        var auditor = new PanicCore.Auditor();
        auditor.Record("mute", "black", true);
        Assert.Single(auditor.Entries);

        var targets = new List<RelayCore.Target>
        {
            new() { Name = "B站", RtmpUrl = "rtmp://live.example.com/live", StreamKey = "secret" },
        };
        Assert.True(RelayCore.Check(targets).Ok);
        Assert.DoesNotContain("secret", RelayCore.Redact(targets[0]));
        Assert.False(RelayCore.Check(new[] { new RelayCore.Target { RtmpUrl = "http://x" } }).Ok);
        Assert.DoesNotContain("http", AudioChainCore.BuildFilter(AudioChainCore.Presets[0]));
    }

    [Fact]
    public void HighlightReframeSearchAndDirector()
    {
        var origin = DateTimeOffset.UtcNow;
        var signals = Enumerable.Range(0, 20).Select(i => new HighlightCore.Signal
        {
            At = origin.AddSeconds(i * 5),
            ChatRate = i == 5 ? 10 : 0.1,
            AudioEnergy = i == 5 ? 1 : 0,
            SceneChanged = i == 5,
        });
        var clips = HighlightCore.FindHighlights(signals);
        Assert.NotEmpty(clips);
        Assert.True(clips[0].Score > 0);

        var window = ReframeCore.CropWindow(1920, 1080, new[] { new ReframeCore.Point(1400, 500, 0.9) }, 0);
        Assert.True(window.X + window.Width <= 1920);

        var search = new RecordingSearchCore();
        search.Add(new RecordingSearchCore.Segment { File = "a.mp4", Text = "上次说的那句话", At = TimeSpan.FromMinutes(3) });
        Assert.Single(search.Search("那句话"));
        search.Clear();
        Assert.Equal(0, search.Count);

        var suggestion = DirectorCore.Evaluate(DirectorCore.Defaults, "游戏窗口获得焦点");
        Assert.NotNull(suggestion);
        Assert.True(suggestion!.RequiresConfirm);
    }

    [Fact]
    public void FfmpegArgsAreValidated()
    {
        var args = FfmpegArgs.BuildClip("in.mp4", "out.mp4", TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10));
        Assert.Contains("in.mp4", args);
        Assert.Contains("out.mp4", args);
        Assert.True(FfmpegArgs.ValidateOutput("in.mp4", @"C:\out\clip.mp4").Ok);
        Assert.False(FfmpegArgs.ValidateOutput("in.mp4", "clip.exe").Ok);
    }

    [Fact]
    public void CoachCoverAndViewerStats()
    {
        var metrics = new SessionMetrics
        {
            Duration = TimeSpan.FromHours(2), PeakViewers = 50, ChatMessages = 200, NewFollowers = 30,
            GiftValue = 100, AvgBitrateKbps = 4000, DroppedRatio = 0, ClipCount = 2,
        };
        var (score, actions, grade) = SessionCoachCore.Evaluate(metrics);
        Assert.True(score >= 85);
        Assert.Equal("A", grade);
        Assert.NotEmpty(actions);
        Assert.Contains("复盘", SessionCoachCore.BuildReport("t", DateTimeOffset.Now, metrics));

        var covers = CoverCopyCore.Suggest("游戏", "新版本", "今晚");
        Assert.Equal(3, covers.Count);
        Assert.False(CoverCopyCore.IsSafe("最强直播"));

        var stats = new ViewerStatsCore();
        stats.Add(DateTimeOffset.UtcNow, 10, 5, 1);
        stats.Add(DateTimeOffset.UtcNow.AddMinutes(1), 8, 3, 0);
        Assert.True(stats.Retention() > 0);
        Assert.Contains("viewers", stats.ExportCsv());
        stats.Clear();
        Assert.Empty(stats.Snapshot());
    }

    [Fact]
    public void MultiCamAndRemoteGuests()
    {
        var slots = new List<MultiCamCore.Slot>
        {
            new() { CameraId = "a", Enabled = true, BitrateKbps = 2000 },
            new() { CameraId = "b", Enabled = true, BitrateKbps = 2000 },
        };
        Assert.True(MultiCamCore.Validate(slots, 10000).Ok);
        Assert.False(MultiCamCore.Validate(slots, 3000).Ok);
        Assert.Equal("b", MultiCamCore.Switch(slots, "b")[0].CameraId);
        var bounds = MultiCamCore.SlotBounds(MultiCamCore.Layout.Grid2x2, 3, 1920, 1080);
        Assert.Equal(1920, bounds.X + bounds.Width);

        var (token, room) = RemoteGuestCore.Create();
        Assert.True(RemoteGuestCore.Join(room, token, "guest", true).Ok);
        Assert.False(RemoteGuestCore.Join(room, "wrong", "guest", true).Ok);
        Assert.False(RemoteGuestCore.CanRecord(room));
        room.RecordingEnabled = true;
        Assert.True(RemoteGuestCore.CanRecord(room));
        Assert.True(RemoteGuestCore.Kick(room, room.Guests[0].Id));
    }

    [Fact]
    public void VoiceCommandsRequireWakeWordAndConfirmation()
    {
        var (command, confirm, code) = VoiceCommandCore.Parse("小助手开始录制", VoiceCommandCore.DefaultWakeWords);
        Assert.NotNull(command);
        Assert.False(confirm);
        Assert.Empty(code);

        var (destructive, needsConfirm, _) = VoiceCommandCore.Parse("小助手开始推流", VoiceCommandCore.DefaultWakeWords);
        Assert.True(needsConfirm);
        Assert.Equal("stream.toggle", destructive!.Id);

        var (none, _, denied) = VoiceCommandCore.Parse("随便说说", VoiceCommandCore.DefaultWakeWords);
        Assert.Null(none);
        Assert.Equal(FeatureErrors.VoiceWakeMissing, denied);
    }

    [Fact]
    public void ThemeMarketAppliesVerifiesAndRollsBack()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "themes-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(AppContext.BaseDirectory, "theme-src-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(source);
        var css = Path.Combine(source, "chat.css");
        File.WriteAllText(css, "body{}");
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(css))).ToLowerInvariant();
        var package = new ThemeMarketCore.ThemePackage
        {
            Id = "demo", Name = "Demo", Version = "1.0.0",
            Files = new List<ThemeMarketCore.ThemeFile> { new() { Path = "chat.css", Sha256 = hash, Size = 7 } },
        };
        var service = new Services.Live.ThemeMarketService(root);
        var applied = service.Apply(package, source);
        Assert.True(applied.Ok);
        Assert.Equal("1.0.0", service.ActiveVersion("demo"));

        package.Version = "1.1.0";
        var second = service.Apply(package, source);
        Assert.True(second.Ok);
        Assert.Equal("1.1.0", service.ActiveVersion("demo"));
        Assert.True(service.Rollback("demo").Ok);
        Assert.Equal("1.0.0", service.ActiveVersion("demo"));

        package.Files[0].Sha256 = new string('0', 64);
        Assert.False(service.Apply(package, source).Ok);
        try { Directory.Delete(root, true); Directory.Delete(source, true); } catch { }
    }

    [Fact]
    public void PluginTrustRejectsBadSignatureAndUnsafePaths()
    {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var payload = "{\"id\":\"demo\"}";
        var signature = Convert.ToBase64String(rsa.SignData(System.Text.Encoding.UTF8.GetBytes(payload),
            System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1));
        var manifest = new PluginTrustCore.TrustManifest
        {
            Id = "demo", Version = "1.0", Signature = signature, Rating = 4.6,
        };
        Assert.True(PluginTrustCore.VerifySignature(manifest, payload, rsa.ExportSubjectPublicKeyInfoPem()));
        Assert.False(PluginTrustCore.VerifySignature(manifest, payload + "x", rsa.ExportSubjectPublicKeyInfoPem()));
        Assert.True(PluginTrustCore.Check(manifest, PluginTrustCore.DefaultPolicy, true).Ok);
        manifest.Files.Add("../evil.dll");
        Assert.False(PluginTrustCore.Check(manifest, PluginTrustCore.DefaultPolicy, true).Ok);
        Assert.Equal("trusted", PluginTrustCore.TrustLabel(4.8));
    }

    [Fact]
    public void AutomationCores()
    {
        Assert.True(MidiCore.Validate(MidiCore.Defaults()).Ok);
        var duplicate = MidiCore.Defaults();
        duplicate.Add(new MidiCore.Mapping { Channel = 1, Controller = 64, Action = "mute.toggle" });
        Assert.False(MidiCore.Validate(duplicate).Ok);
        Assert.Contains("scene.next", MidiCore.Export(MidiCore.Defaults()));

        var (ok, maps) = MidiCore.Import(MidiCore.Export(MidiCore.Defaults()));
        Assert.True(ok);
        Assert.NotEmpty(maps);

        Assert.True(WledCore.ValidateUrl("http://192.168.1.30").Ok);
        Assert.False(WledCore.ValidateUrl("http://8.8.8.8").Ok);
        Assert.Contains("on", WledCore.BuildPayload(WledCore.Presets[0]));

        var package = new ThemeMarketCore.ThemePackage
        {
            Id = "t", Name = "T", Version = "1.0",
            Files = new List<ThemeMarketCore.ThemeFile>
            {
                new() { Path = "chat.css", Sha256 = new string('a', 64), Size = 10 },
            },
        };
        Assert.True(ThemeMarketCore.Validate(package).Ok);
        package.Files.Add(new ThemeMarketCore.ThemeFile { Path = "../evil.exe", Sha256 = new string('a', 64) });
        Assert.False(ThemeMarketCore.Validate(package).Ok);

        var team = new TeamRolesCore();
        var invite = team.CreateInvite(TeamRolesCore.Role.Operator, TimeSpan.FromMinutes(1));
        var (accepted, _, member) = team.Accept(invite, "assistant", DateTimeOffset.UtcNow);
        Assert.True(accepted);
        Assert.True(team.Can(member!.Role, TeamRolesCore.Role.Operator));
        Assert.True(team.Kick(member.Id));
        Assert.NotEmpty(team.AuditLog);

        Assert.True(AiHostCore.ShouldInterrupt("停一下"));
        Assert.False(AiHostCore.ShouldInterrupt("继续"));
        Assert.InRange(AiHostCore.LipSyncValue("你好", 0.3), 0, 1);
        Assert.Contains("AI", AiHostCore.Label());

        var temp = Path.Combine(AppContext.BaseDirectory, "archive-" + Guid.NewGuid().ToString("N") + ".mp4");
        File.WriteAllText(temp, "x");
        var plan = new ArchiveCore.Plan { LocalPath = temp, RemoteName = "clip.mp4" };
        Assert.True(ArchiveCore.BuildArgs(plan, "remote:backup").Contains("--checksum"));
        File.Delete(temp);
    }
}
