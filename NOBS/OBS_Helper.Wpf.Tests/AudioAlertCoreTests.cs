using OBS_Helper.Wpf.Localization;
using OBS_Helper.Wpf.Services.Audio;

namespace OBS_Helper.Wpf.Tests;

/// <summary>静音 / 音量过低提醒（V3.1.0 / §12.5.1 第 5 项）：阈值、持续时间与防重复。</summary>
public class AudioAlertCoreTests
{
    [Fact]
    public void NoAlertWhenOutputInactiveOrPaused()
    {
        Assert.Equal(AudioAlertKind.None, AudioAlertCore.Evaluate(
            outputActive: false, paused: false, muted: true, 0, TimeSpan.FromMinutes(1), false));
        Assert.Equal(AudioAlertKind.None, AudioAlertCore.Evaluate(
            outputActive: true, paused: true, muted: true, 0, TimeSpan.FromMinutes(1), false));
    }

    [Fact]
    public void MutedNeedsEightSeconds()
    {
        Assert.Equal(AudioAlertKind.None, AudioAlertCore.Evaluate(
            true, false, true, 0, TimeSpan.FromSeconds(7.9), false));
        Assert.Equal(AudioAlertKind.Muted, AudioAlertCore.Evaluate(
            true, false, true, 0, TimeSpan.FromSeconds(8), false));
    }

    [Fact]
    public void QuietNeedsTenSecondsAndThreshold()
    {
        Assert.Equal(AudioAlertKind.None, AudioAlertCore.Evaluate(
            true, false, false, -60, TimeSpan.FromSeconds(9.9), false));
        Assert.Equal(AudioAlertKind.TooQuiet, AudioAlertCore.Evaluate(
            true, false, false, -60, TimeSpan.FromSeconds(10), false));
        Assert.Equal(AudioAlertKind.None, AudioAlertCore.Evaluate(
            true, false, false, -54.9, TimeSpan.FromMinutes(1), false));
    }

    [Fact]
    public void MutedTakesPrecedenceOverQuiet()
    {
        Assert.Equal(AudioAlertKind.Muted, AudioAlertCore.Evaluate(
            true, false, true, -60, TimeSpan.FromMinutes(1), false));
    }

    [Fact]
    public void AlreadyAlertedSuppressesRepeat()
    {
        Assert.Equal(AudioAlertKind.None, AudioAlertCore.Evaluate(
            true, false, true, 0, TimeSpan.FromMinutes(5), alreadyAlerted: true));
        Assert.Equal(AudioAlertKind.None, AudioAlertCore.Evaluate(
            true, false, false, -60, TimeSpan.FromMinutes(5), alreadyAlerted: true));
    }

    [Fact]
    public void KeysAreLocalized()
    {
        foreach (var kind in new[] { AudioAlertKind.Muted, AudioAlertKind.TooQuiet, AudioAlertKind.None })
        {
            var (title, message) = AudioAlertCore.KeysFor(kind);
            Assert.NotEqual(title, Strings.T(title));
            Assert.NotEqual(message, Strings.T(message));
        }
    }
}
