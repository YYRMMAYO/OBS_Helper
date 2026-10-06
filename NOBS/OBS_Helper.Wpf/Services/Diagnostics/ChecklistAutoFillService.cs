using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Security.Principal;
using OBS_Helper.Wpf.Services.Obs;
using OBS_Helper.Wpf.Services.ObsConfig;
using OBS_Helper.Wpf.Services.Tools;

namespace OBS_Helper.Wpf.Services.Diagnostics;

/// <summary>
/// 采集「本机事实」供自检清单自动回填（V3.0 / D4）。
///
/// 分工：本服务只负责**读**（全部只读、绝不写任何配置），
/// 「事实 → 清单结论」的映射在纯逻辑 <see cref="ChecklistAutoFillCore"/> 里，可单测。
/// 每一项读不到就留 null，由核心判成「无法判定」并说明原因 —— 不猜、不假装。
/// </summary>
public sealed class ChecklistAutoFillService
{
    private readonly PreflightCheckService _preflight;

    public ChecklistAutoFillService(PreflightCheckService preflight) => _preflight = preflight;

    /// <summary>采集证据。永不抛异常（任一项失败只让那一项保持「未知」）。</summary>
    public async Task<ChecklistEvidence> CollectAsync()
    {
        var evidence = new ChecklistEvidence
        {
            Elevated = TryElevated(),
            HardwareAccelConflicts = TryConflictCount(),
            OnWiredNetwork = TryWiredNetwork(),
            DroppedRatio = TryDroppedRatio(),
        };

        // ① 编码器 / 码率 / 采样率：读 OBS 的 basic.ini
        try
        {
            var facts = await _preflight.ReadFactsAsync().ConfigureAwait(false);
            if (facts is not null)
            {
                evidence = evidence with
                {
                    EncoderNames = facts.EncoderNames,
                    VideoBitrateKbps = facts.VideoBitrateKbps,
                    AudioSampleRateHz = facts.AudioSampleRateHz,
                };
            }
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Checklist", $"读取 OBS 配置事实失败（按未知处理）：{ex.Message}");
        }

        // ④ 显卡数量与 obs64 的 GPU 偏好（只读注册表）
        try
        {
            var snapshot = await Task.Run(SystemCheck.GraphicsEnvCheckService.CollectSnapshot).ConfigureAwait(false);
            if (snapshot is not null)
            {
                evidence = evidence with
                {
                    GpuCount = snapshot.Gpus.Count,
                    ObsGpuPreferenceSet = !string.IsNullOrEmpty(snapshot.ObsGpuPreference),
                };
            }
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Checklist", $"读取显卡环境失败（按未知处理）：{ex.Message}");
        }

        // ⑧ 推流服务与密钥（走既有的「只判存在性」接口，密钥不进本应用）
        try
        {
            var stream = await AppServices.Obs.GetStreamServiceInfoAsync().ConfigureAwait(false);
            if (stream is not null)
            {
                evidence = evidence with
                {
                    StreamHasServer = stream.HasServer,
                    StreamHasKey = stream.HasKey,
                };
            }
        }
        catch (Exception ex)
        {
            FileLogger.Warn("Checklist", $"读取推流设置失败（按未知处理）：{ex.Message}");
        }

        // ③ 上行速度：来自用户在工具箱「推流带宽测算」里填写的值（本产品不做真实测速）。
        //    没填过就保持未知 —— 此时把「码率不超过上行 75%」判成失败是冤枉用户。
        try
        {
            var raw = AppServices.Store.GetItem("bandwidth_upload_mbps");
            if (!string.IsNullOrWhiteSpace(raw) &&
                double.TryParse(raw, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var mbps) && mbps > 0)
            {
                evidence = evidence with { UplinkMbps = mbps };
            }
        }
        catch (Exception)
        {
            // 没有历史输入属正常情况
        }

        return evidence;
    }

    /// <summary>当前进程是否以管理员身份运行。取不到返回 null。</summary>
    private static bool? TryElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>开启硬件加速的常见冲突程序数量（Chrome / Discord 等）。扫描失败返回 null。</summary>
    private static int? TryConflictCount()
    {
        try
        {
            var names = Process.GetProcesses()
                .Select(p =>
                {
                    try { return p.ProcessName; } catch (Exception) { return ""; }
                })
                .Where(n => n.Length > 0)
                .ToList();

            return ConflictScannerCore.Scan(names).Count;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// 是否走有线网络。
    ///
    /// 判定口径保守：只有当存在一块**已连接且有默认网关**的以太网卡时才算「有线」；
    /// 否则返回 false（有网卡但没连 = 很可能在用 WiFi），读不到任何网卡信息时返回 null。
    /// </summary>
    private static bool? TryWiredNetwork()
    {
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();
            if (interfaces.Length == 0) return null;

            foreach (var ni in interfaces)
            {
                if (ni.NetworkInterfaceType != NetworkInterfaceType.Ethernet) continue;
                if (ni.OperationalStatus != OperationalStatus.Up) continue;

                var hasGateway = ni.GetIPProperties().GatewayAddresses
                    .Any(g => g.Address is not null && !g.Address.Equals(System.Net.IPAddress.Any));
                if (hasGateway) return true;
            }

            // 没有任何「已连接且有默认网关」的以太网卡 → 明确按「不是有线」处理。
            // 这里不返回 null：能枚举到网卡就说明探测本身是成功的，
            // 而「有网卡但没连」正是 WiFi 场景，是这一条要提醒的对象。
            return false;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static double? TryDroppedRatio()
    {
        try
        {
            var status = AppServices.Obs.StreamStatus;
            return status.Active && status.TotalFrames > 0 ? status.DroppedRatio : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
