using OBS_Helper.Wpf.Models.Mobile;
using OBS_Helper.Wpf.Services.Mobile;
using OBS_Helper.Wpf.Services.Security;
using Xunit;

namespace OBS_Helper.Wpf.Tests;

public class MobileCoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void PairingSessionExpiresAndIsOneTime()
    {
        var (session, secret) = PairingCore.CreateSession(Now, TimeSpan.FromSeconds(120), "123456", "pair1", "secret-value");
        Assert.Equal(PairOutcome.Ok, PairingCore.Consume(session, secret, Now.AddSeconds(1)));
        Assert.Equal(PairOutcome.Used, PairingCore.Consume(session, secret, Now.AddSeconds(2)));

        var (expired, secret2) = PairingCore.CreateSession(Now, TimeSpan.FromSeconds(1), "654321", "pair2", "s2");
        Assert.Equal(PairOutcome.Expired, PairingCore.Consume(expired, secret2, Now.AddSeconds(2)));
    }

    [Fact]
    public void PairingRejectsWrongSecretAndUnknownSession()
    {
        var (session, _) = PairingCore.CreateSession(Now, pairId: "p", secret: "right");
        Assert.Equal(PairOutcome.BadSecret, PairingCore.Consume(session, "wrong", Now));
        Assert.Equal(PairOutcome.UnknownPair, PairingCore.Consume(null, "right", Now));
    }

    [Fact]
    public void PairUrlKeepsSecretInFragment()
    {
        var url = PairingCore.BuildPairUrl("http://192.168.1.5:8080/", "abc", "topsecret");
        Assert.Equal("http://192.168.1.5:8080/m/abc#topsecret", url);
        Assert.True(PairingCore.TryParsePairUrl(url, out var pairId, out var secret));
        Assert.Equal("abc", pairId);
        Assert.Equal("topsecret", secret);
    }

    [Fact]
    public void CommandWhitelistHasNoRawObsPassthrough()
    {
        Assert.DoesNotContain("obs.request", MobileCommandCore.All);
        Assert.NotNull(MobileCommandCore.Find("scene.set"));
        Assert.Null(MobileCommandCore.Find("obs.request"));
        Assert.True(MobileCommandCore.Find("stream.start")!.Destructive);
    }

    [Fact]
    public void CommandRoleMatrixIsEnforced()
    {
        var request = new MobileCommandRequest { RequestId = "r1", Command = "stream.start" };
        var context = new MobileCommandCore.Context { ObsOnline = true };
        Assert.False(MobileCommandCore.Validate(request, MobileRole.Operator, context, "r1").Ok);
        Assert.Equal(FeatureErrors.MobRoleDenied,
            MobileCommandCore.Validate(request, MobileRole.Operator, context, "r1").Code);
        // admin 也必须有 confirm
        Assert.Equal(FeatureErrors.MobCommandInvalid,
            MobileCommandCore.Validate(request, MobileRole.Admin, context, "r1").Code);
        request.Params["confirm"] = System.Text.Json.JsonSerializer.SerializeToElement(true);
        Assert.True(MobileCommandCore.Validate(request, MobileRole.Admin, context, "r1").Ok);
    }

    [Fact]
    public void CommandParameterValidationUsesCurrentState()
    {
        var context = new MobileCommandCore.Context
        {
            ObsOnline = true,
            Scenes = new[] { "主场景" },
            Audio = new[] { new MobileAudioState { Name = "麦克风" } },
            SceneItemIds = new[] { 3 },
        };
        var scene = new MobileCommandRequest { RequestId = "a", Command = "scene.set" };
        scene.Params["scene"] = System.Text.Json.JsonSerializer.SerializeToElement("不存在");
        Assert.False(MobileCommandCore.Validate(scene, MobileRole.Operator, context, "a").Ok);

        var audio = new MobileCommandRequest { RequestId = "b", Command = "audio.volume" };
        audio.Params["input"] = System.Text.Json.JsonSerializer.SerializeToElement("麦克风");
        audio.Params["volumeDb"] = System.Text.Json.JsonSerializer.SerializeToElement(-6.0);
        Assert.True(MobileCommandCore.Validate(audio, MobileRole.Operator, context, "b").Ok);

        var source = new MobileCommandRequest { RequestId = "c", Command = "source.toggle" };
        source.Params["sceneItemId"] = System.Text.Json.JsonSerializer.SerializeToElement(99);
        source.Params["enabled"] = System.Text.Json.JsonSerializer.SerializeToElement(true);
        Assert.False(MobileCommandCore.Validate(source, MobileRole.Operator, context, "c").Ok);
    }

    [Fact]
    public void VolumeIsClamped()
    {
        Assert.Equal(-100, MobileCommandCore.ClampVolume(double.NaN));
        Assert.Equal(0, MobileCommandCore.ClampVolume(12));
        Assert.Equal(-15, MobileCommandCore.ClampVolume(-15));
    }

    [Fact]
    public void IdempotencyCacheReturnsSameResultAndPrunes()
    {
        var now = Now;
        var cache = new MobileCommandCore.IdempotencyCache(() => now);
        var result = MobileCommandResult.Success("r1");
        cache.Put("dev", "r1", result);
        Assert.True(cache.TryGet("dev", "r1", out var cached));
        Assert.Same(result, cached);
        now = now.AddSeconds(61);
        Assert.False(cache.TryGet("dev", "r1", out _));
    }

    [Fact]
    public void TallyAndElapsedFormatting()
    {
        Assert.Equal("off", MobileStateCore.Tally(false, true, true));
        Assert.Equal("red", MobileStateCore.Tally(true, true, false));
        Assert.Equal("green", MobileStateCore.Tally(true, false, false));
        Assert.Equal("01:02:03", MobileStateCore.FormatElapsed(new TimeSpan(1, 2, 3)));
        Assert.Equal("00:00:00", MobileStateCore.FormatElapsed(TimeSpan.FromSeconds(-5)));
        var (chosen, drift) = MobileStateCore.ReconcileElapsed(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10.4));
        Assert.Equal(TimeSpan.FromSeconds(10), chosen);
        Assert.True(drift < 1);
    }

    [Fact]
    public void SessionStoreRegistersAuthenticatesAndRevokes()
    {
        var path = Path.Combine(Path.GetTempPath(), "obshelper-test-" + Guid.NewGuid().ToString("N"), "devices.json");
        var store = new MobileSessionStore(path, () => Now);
        var registered = store.Register("  小明的手机 ", MobileRole.Operator);
        Assert.NotNull(registered);
        var (device, token) = registered!.Value;
        Assert.Equal("小明的手机", device.Name);
        Assert.NotNull(store.Authenticate(token));
        Assert.True(store.Revoke(device.DeviceId));
        Assert.Null(store.Authenticate(token));
        try { Directory.Delete(Path.GetDirectoryName(path)!, true); } catch { }
    }

    [Fact]
    public void SessionStoreEnforcesDeviceLimit()
    {
        var path = Path.Combine(Path.GetTempPath(), "obshelper-test-" + Guid.NewGuid().ToString("N"), "devices.json");
        var store = new MobileSessionStore(path, () => Now);
        for (var i = 0; i < LanPolicy.MaxDevices; i++) Assert.NotNull(store.Register("p" + i, MobileRole.Viewer));
        Assert.Null(store.Register("overflow", MobileRole.Viewer));
        try { Directory.Delete(Path.GetDirectoryName(path)!, true); } catch { }
    }

    [Fact]
    public void SessionStoreExpiresIdleDevices()
    {
        var now = Now;
        var path = Path.Combine(Path.GetTempPath(), "obshelper-test-" + Guid.NewGuid().ToString("N"), "devices.json");
        var store = new MobileSessionStore(path, () => now);
        var registered = store.Register("p", MobileRole.Viewer)!.Value;
        now = now.AddMinutes(LanPolicy.IdleMinutes + 1);
        Assert.Null(store.Authenticate(registered.Token));
        try { Directory.Delete(Path.GetDirectoryName(path)!, true); } catch { }
    }

    [Fact]
    public void CameraUrlsUsePublishKeyAndLoopbackRtsp()
    {
        var key = PhoneCameraCore.NewStreamKey();
        Assert.True(PhoneCameraCore.IsValidKey(key));
        Assert.Equal($"srt://192.168.1.9:9000?streamid=publish:live/{key}", PhoneCameraCore.SrtPublishUrl("192.168.1.9", 9000, key));
        Assert.Equal($"rtmp://192.168.1.9:1935/live/{key}", PhoneCameraCore.RtmpPublishUrl("192.168.1.9", 1935, key));
        Assert.Equal($"rtsp://127.0.0.1:8554/live/{key}", PhoneCameraCore.RtspLoopbackUrl(8554, key));
    }

    [Fact]
    public void CameraOnlyAcceptsPrivatePublishers()
    {
        Assert.True(PhoneCameraCore.AllowsPublishFrom("192.168.1.20"));
        Assert.False(PhoneCameraCore.AllowsPublishFrom("8.8.8.8"));
        Assert.False(PhoneCameraCore.AllowsPublishFrom(null));
        Assert.False(PhoneCameraCore.IsValidKey("short"));
        Assert.False(PhoneCameraCore.IsValidKey("../../etc"));
    }

    [Fact]
    public void CameraLayoutsFitInsideCanvas()
    {
        foreach (PhoneCameraLayout layout in Enum.GetValues<PhoneCameraLayout>())
        {
            var (x, y, w, h) = PhoneCameraCore.ComputeBounds(1920, 1080, 1280, 720, layout);
            Assert.True(x >= 0 && y >= 0, layout.ToString());
            Assert.True(x + w <= 1920, layout.ToString());
            Assert.True(y + h <= 1080, layout.ToString());
            Assert.True(w > 0 && h > 0, layout.ToString());
        }
        var portrait = PhoneCameraCore.ComputeBounds(1920, 1080, 720, 1280, PhoneCameraLayout.PortraitCrop);
        Assert.True(Math.Abs(portrait.Width * 16 - portrait.Height * 9) <= 16);
    }

    [Fact]
    public void MediaServerConfigBindsApiToLoopbackAndDisablesWebrtc()
    {
        var yml = PhoneCameraCore.BuildConfig(8554, 1935, 8890, 9997);
        Assert.Contains("rtsp: yes", yml);
        Assert.Contains("srt: yes", yml);
        Assert.Contains("hls: no", yml);
        Assert.Contains("webrtc: no", yml);
        Assert.Contains("apiAddress: 127.0.0.1:9997", yml);
        Assert.DoesNotContain("0.0.0.0", yml);
    }

    [Fact]
    public void NotifyCoreBlocksSensitiveContent()
    {
        Assert.True(MobileNotifyCore.IsSensitive("验证码 123456"));
        Assert.True(MobileNotifyCore.IsSensitive("password: hunter2"));
        Assert.True(MobileNotifyCore.IsSensitive("6222 0212 3456 7890"));
        Assert.False(MobileNotifyCore.IsSensitive("今天直播很不错"));
        Assert.False(MobileNotifyCore.TryPrepare("验证码 123456", out _));
        Assert.True(MobileNotifyCore.TryPrepare("大家好", out var prepared));
        Assert.Equal("大家好", prepared);
        Assert.Equal("ab************yz", MobileNotifyCore.Redact("abcdefghijklmnopqrstuvwxyz"));
    }

    [Fact]
    public void MobilePagesAreSelfContainedAndAccessible()
    {
        foreach (var html in new[] { MobilePageAssets.AppHtml(), MobilePageAssets.LightHtml() })
        {
            Assert.Contains("name=\"viewport\"", html);
            Assert.Contains("width=device-width", html);
            Assert.DoesNotContain("innerHTML", html);
            Assert.DoesNotContain("http://", html.Replace("xmlns=\"http://www.w3.org/2000/svg\"", ""));
            Assert.DoesNotContain("https://", html);
            Assert.Contains("textContent", html);
        }
        var app = MobilePageAssets.AppHtml();
        Assert.Contains("min-height:44px", app);
        Assert.Contains("min-height:52px", app);
        Assert.Contains("textContent", app);
        var light = MobilePageAssets.LightHtml();
        Assert.DoesNotContain("fetch(", light);
        Assert.DoesNotContain("XMLHttpRequest", light);
        Assert.DoesNotContain("EventSource", light);
    }

    [Fact]
    public void MobileI18nHasBothLanguages()
    {
        foreach (var (key, value) in MobilePageAssets.I18n)
        {
            Assert.False(string.IsNullOrWhiteSpace(value.Zh), key);
            Assert.False(string.IsNullOrWhiteSpace(value.En), key);
        }
    }
}
