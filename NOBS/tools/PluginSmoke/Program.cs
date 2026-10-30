using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using OBS_Helper.Wpf.Models.Plugins;
using OBS_Helper.Wpf.Services.Plugins;

// 插件管理冒烟测试：不需要 OBS / 不需要联网。
// 用假 HttpClient 返回内存 zip，验证「安装 → 覆盖安装(备份) → 回滚 → 卸载」整条事务链路。

var failures = 0;
void Check(string name, bool ok, string detail = "")
{
    Console.WriteLine((ok ? "PASS  " : "FAIL  ") + name + (detail.Length > 0 ? "  -> " + detail : ""));
    if (!ok) failures++;
}

var temp = Path.Combine(Path.GetTempPath(), "nobs-plugin-smoke-" + Guid.NewGuid().ToString("N"));
var stateRoot = Path.Combine(temp, "state");
var installRoot = Path.Combine(temp, "plugins");
Directory.CreateDirectory(temp);

try
{
    var zipV1 = BuildZip(new[]
    {
        ("plugin-1.0/bin/64bit/test-plugin.dll", BuildPe("v1")),
        ("plugin-1.0/data/locale/en-US.ini", Encoding.UTF8.GetBytes("name=Test"))
    });
    var store = new PluginStateStore(stateRoot);
    var handler = new FakeHandler(zipV1);
    var svc = new PluginInstallService(store, new HttpClient(handler), () => false, _ => installRoot);

    var req = Request("1.0", zipV1);
    var r1 = await svc.InstallAsync(req, null, CancellationToken.None);
    var installedDll = Path.Combine(installRoot, "test-plugin", "bin", "64bit", "test-plugin.dll");
    Check("install ok", r1.Ok, r1.ErrorCode ?? "");
    Check("install writes dll", File.Exists(installedDll));
    var entry = store.Find("test-plugin");
    Check("manifest entry", entry is not null && entry.Version == "1.0" && entry.Files.Count == 2,
        entry is null ? "missing" : $"v{entry.Version}, {entry.Files.Count} files");
    Check("manifest sha", entry is not null && entry.Sha256.Length == 64);

    var zipV2 = BuildZip(new[]
    {
        ("plugin-1.1/bin/64bit/test-plugin.dll", BuildPe("v2")),
        ("plugin-1.1/data/locale/en-US.ini", Encoding.UTF8.GetBytes("name=Test2"))
    });
    handler.Payload = zipV2;
    var r2 = await svc.InstallAsync(Request("1.1", zipV2), null, CancellationToken.None);
    var backups = store.ListBackups("test-plugin");
    Check("upgrade ok", r2.Ok, r2.ErrorCode ?? "");
    Check("upgrade keeps backup", backups.Count == 1, backups.Count.ToString());
    Check("upgrade writes new dll", File.Exists(installedDll) &&
        Encoding.UTF8.GetString(File.ReadAllBytes(installedDll)).Contains("v2"));

    var r3 = await svc.RollbackAsync("test-plugin", CancellationToken.None);
    Check("rollback ok", r3.Ok, r3.ErrorCode ?? "");
    Check("rollback restores old dll", File.Exists(installedDll) &&
        Encoding.UTF8.GetString(File.ReadAllBytes(installedDll)).Contains("v1"));

    var r4 = await svc.UninstallAsync("test-plugin", CancellationToken.None);
    Check("uninstall ok", r4.Ok, r4.ErrorCode ?? "");
    Check("uninstall removes install dir", !Directory.Exists(Path.Combine(installRoot, "test-plugin")));
    Check("uninstall clears manifest", store.Find("test-plugin") is null);
    Check("uninstall keeps backup", store.ListBackups("test-plugin").Count >= 1);

    var obsSvc = new PluginInstallService(new PluginStateStore(Path.Combine(temp, "state2")),
        new HttpClient(new FakeHandler(zipV1)), () => true, _ => Path.Combine(temp, "plugins2"));
    var r5 = await obsSvc.InstallAsync(Request("1.0", zipV1), null, CancellationToken.None);
    Check("obs running rejected", !r5.Ok && r5.ErrorCode == "PLUGIN_OBS_RUNNING", r5.ErrorCode ?? "");

    var slip = BuildZip(new[] { ("../evil.dll", BuildPe("bad")) });
    var slipSvc = new PluginInstallService(new PluginStateStore(Path.Combine(temp, "state3")),
        new HttpClient(new FakeHandler(slip)), () => false, _ => Path.Combine(temp, "plugins3"));
    var r6 = await slipSvc.InstallAsync(Request("1.0", slip), null, CancellationToken.None);
    Check("zip slip rejected", !r6.Ok && r6.ErrorCode == "PLUGIN_ZIP_UNSAFE", r6.ErrorCode ?? "");

    var x86 = BuildZip(new[] { ("bin/64bit/test-plugin.dll", BuildPe("x86", 0x014C, managed: false)) });
    var x86Svc = new PluginInstallService(new PluginStateStore(Path.Combine(temp, "state4")),
        new HttpClient(new FakeHandler(x86)), () => false, _ => Path.Combine(temp, "plugins4"));
    var r7 = await x86Svc.InstallAsync(Request("1.0", x86), null, CancellationToken.None);
    Check("native x86 rejected", !r7.Ok && r7.ErrorCode == "PLUGIN_BAD_ARCH", r7.ErrorCode ?? "");

    var hashSvc = new PluginInstallService(new PluginStateStore(Path.Combine(temp, "state5")),
        new HttpClient(new FakeHandler(zipV1)), () => false, _ => Path.Combine(temp, "plugins5"));
    var badReq = Request("1.0", zipV1);
    badReq = new PluginInstallRequest
    {
        Id = badReq.Id, Name = badReq.Name, Version = badReq.Version, SourceRepo = badReq.SourceRepo,
        Asset = badReq.Asset, Scope = "user", Curated = false,
        ExpectedSha256 = new string('0', 64)
    };
    var r8 = await hashSvc.InstallAsync(badReq, null, CancellationToken.None);
    Check("hash mismatch rejected", !r8.Ok && r8.ErrorCode == "PLUGIN_HASH_MISMATCH", r8.ErrorCode ?? "");
}
catch (Exception ex)
{
    Console.WriteLine("EXCEPTION " + ex);
    failures++;
}
finally
{
    try { Directory.Delete(temp, true); } catch { }
}

Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILED");
return failures == 0 ? 0 : 1;

static PluginInstallRequest Request(string version, byte[] zip) => new()
{
    Id = "test-plugin",
    Name = "Test Plugin",
    Version = version,
    SourceRepo = "test/plugin",
    Asset = new PluginReleaseAsset
    {
        Name = "test-plugin.zip",
        Url = "https://github.com/test/plugin/releases/download/v" + version + "/test-plugin.zip",
        Size = zip.Length
    },
    Scope = "user",
    Curated = false
};

static byte[] BuildZip((string Name, byte[] Content)[] entries)
{
    using var ms = new MemoryStream();
    using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
    {
        foreach (var (name, content) in entries)
        {
            var e = zip.CreateEntry(name, CompressionLevel.Fastest);
            using var s = e.Open();
            s.Write(content);
        }
    }
    return ms.ToArray();
}

// 构造最小 PE：MZ + PE + machine（x64 / x86），末尾带标记方便验证回滚内容。
static byte[] BuildPe(string marker, ushort machine = 0x8664, bool managed = true)
{
    var bytes = new byte[4096];
    bytes[0] = (byte)'M';
    bytes[1] = (byte)'Z';
    const int pe = 0x80;
    BitConverter.GetBytes(pe).CopyTo(bytes, 0x3C);
    bytes[pe] = (byte)'P';
    bytes[pe + 1] = (byte)'E';
    BitConverter.GetBytes(machine).CopyTo(bytes, pe + 4);
    var optional = pe + 24;
    BitConverter.GetBytes((ushort)0x10B).CopyTo(bytes, optional);
    if (managed)
    {
        var clr = optional + 208;
        BitConverter.GetBytes(0x2000).CopyTo(bytes, clr);
        BitConverter.GetBytes(0x48).CopyTo(bytes, clr + 4);
    }
    Encoding.UTF8.GetBytes(marker).CopyTo(bytes, 0x200);
    return bytes;
}

sealed class FakeHandler : HttpMessageHandler
{
    public byte[] Payload { get; set; }
    public FakeHandler(byte[] payload) => Payload = payload;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var resp = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new ByteArrayContent(Payload)
        };
        resp.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip");
        return Task.FromResult(resp);
    }
}
