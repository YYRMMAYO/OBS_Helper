using OBS_Helper.Wpf.Models.Obs;
using OBS_Helper.Wpf.Services.Mobile;
using OBS_Helper.Wpf.Services.Obs;

namespace OBS_Helper.Wpf.Tests;

/// <summary>
/// V3.5.2：输出状态对账。
///
/// 实测复现：OBS 发出 <c>RecordStateChanged(STOPPED, outputActive=false)</c> 之后，
/// <c>GetRecordStatus</c> 仍会短暂返回 <c>outputActive=true</c>。旧实现把这次查询结果写回，
/// 于是「停止录制」后界面又翻回「进行中」，桌面端与手机端都会一直显示在录制。
/// </summary>
public class ObsOutputStateCoreTests
{
    [Fact]
    public void QueryCannotFlipStopBackToRecordingWhileEventIsFresh()
    {
        // 事件刚说「已停止」，查询还说 true（OBS 收尾阶段）→ 以事件为准。
        Assert.False(ObsOutputStateCore.ResolveActive(
            queryActive: true,
            eventActive: false,
            sinceEvent: TimeSpan.FromMilliseconds(300)));
    }

    [Fact]
    public void QueryCannotFlipStartedBackToStoppedWhileEventIsFresh()
    {
        // 对称场景：刚开始录制时查询偶尔报 false，也不能把刚亮的「录制中」熄灭。
        Assert.True(ObsOutputStateCore.ResolveActive(
            queryActive: false,
            eventActive: true,
            sinceEvent: TimeSpan.FromMilliseconds(300)));
    }

    [Fact]
    public void QueryWinsWhenEventIsOlderThanWindow()
    {
        // 事件已经过期（可能丢失）：查询是对账的唯一来源，必须能自愈。
        Assert.True(ObsOutputStateCore.ResolveActive(
            queryActive: true,
            eventActive: false,
            sinceEvent: TimeSpan.FromSeconds(10)));
        Assert.False(ObsOutputStateCore.ResolveActive(
            queryActive: false,
            eventActive: true,
            sinceEvent: TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void MatchingValuesAlwaysAgree()
    {
        Assert.True(ObsOutputStateCore.ResolveActive(true, true, TimeSpan.Zero));
        Assert.False(ObsOutputStateCore.ResolveActive(false, false, TimeSpan.Zero));
    }

    [Fact]
    public void MobileTallyLabelsDistinguishRecordingFromLive()
    {
        // 旧实现把所有红灯都写成「直播中」，只录制时手机上也显示直播中。
        foreach (var key in new[] { "tallyLive", "tallyRecord", "tallyLiveRecord", "tallyStandby", "offline" })
        {
            Assert.True(MobilePageAssets.I18n.ContainsKey(key), $"手机页文案缺少 {key}");
            var (zh, en) = MobilePageAssets.I18n[key];
            Assert.False(string.IsNullOrWhiteSpace(zh));
            Assert.False(string.IsNullOrWhiteSpace(en));
        }

        Assert.Equal("录制中", MobilePageAssets.I18n["tallyRecord"].Zh);
        Assert.Equal("直播中 · 录制中", MobilePageAssets.I18n["tallyLiveRecord"].Zh);
    }

    [Fact]
    public void BatchRequestUsesProtocolOpcodesAndNumericExecutionType()
    {
        // V3.5.2 实测：把批量当成 requestType="CallBatch" 的普通请求会被 OBS 回 204，
        // executionType 传字符串 "Parallel" 会被 4004 直接断开 —— 两种都会让刷新静默失效。
        Assert.Equal(8, ObsOpCode.RequestBatch);
        Assert.Equal(9, ObsOpCode.RequestBatchResponse);
        Assert.Equal(1, ObsBatchExecution.Parse("SerialFrame"));
        // OBS 33 实测 Parallel 会串位，统一按串行处理（见 ObsBatchExecution 注释）。
        Assert.Equal(0, ObsBatchExecution.Parse("SerialRealtime"));
        Assert.Equal(0, ObsBatchExecution.Parse("Parallel"));
        Assert.Equal(0, ObsBatchExecution.Parse(null));
        Assert.Equal(0, ObsBatchExecution.Parse("parallel"));
    }
}
