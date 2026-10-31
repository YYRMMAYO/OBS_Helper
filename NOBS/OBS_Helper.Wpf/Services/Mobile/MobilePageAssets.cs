using System.Text;
using System.Text.Json;
using OBS_Helper.Wpf.Localization;

namespace OBS_Helper.Wpf.Services.Mobile;

/// <summary>
/// 手机端页面（V3.4.1 / M01/M02/M05/M07）。
///
/// 约束（PLAN_V3.4.0 §4.2 / §6.1）：
/// <list type="bullet">
///   <item>无框架、无远程资源（字体/图标/脚本全部内联），CSP 为 <c>default-src 'none'</c>；</item>
///   <item>消息与场景名一律 <c>textContent</c> 渲染，不使用 <c>innerHTML</c>；</item>
///   <item>触控目标 ≥ 44×44 px，支持 360×640 起的竖屏与深色模式；</item>
///   <item>中英双语由页面内嵌词表切换，不在 JS 里硬编码单语文案；</item>
///   <item>补光页是**独立页面**，纯 CSS/本地 JS，不发任何网络请求（离线可用）。</item>
/// </list>
/// 这里只产出字符串，可被单测直接做静态检查（无远程 URL / 无 innerHTML / 有 viewport 等）。
///
/// V3.4.1 修复：<see cref="I18n"/> 是元组字典，System.Text.Json 默认不序列化元组**字段**，
/// 旧实现注入页面的 <c>I18N</c> 全是空对象，导致所有文案为 undefined、页面只剩空壳。
/// 现在显式投影成 <c>{ key: [Zh, En] }</c>，并由单测回读校验。
/// </summary>
public static class MobilePageAssets
{
    /// <summary>遥控主页面。<paramref name="appVersion"/> 会显示在页头，便于确认手机加载的是哪一版。</summary>
    public static string AppHtml(string language = Strings.ZhHans, string? appVersion = null)
    {
        var i18n = JsonSerializer.Serialize(
            I18n.ToDictionary(p => p.Key, p => new[] { p.Value.Zh, p.Value.En }),
            new JsonSerializerOptions { WriteIndented = false });
        var version = new string((appVersion ?? "").Where(ch => char.IsDigit(ch) || ch == '.').ToArray());
        if (version.Length == 0) version = "dev";
        return AppTemplate
            .Replace("__LANG__", language == Strings.EnUs ? "en" : "zh")
            .Replace("__I18N__", i18n)
            .Replace("__VERSION__", version);
    }

    /// <summary>补光 / 色卡 / 倒计时页面：完全离线，不引用任何网络 API。</summary>
    public static string LightHtml(string language = Strings.ZhHans)
        => LightTemplate.Replace("__LANG__", language == Strings.EnUs ? "en" : "zh");

    /// <summary>手机端词表（中英各一份）。键名与桌面端文案表有意不共用，便于独立演进。</summary>
    public static readonly Dictionary<string, (string Zh, string En)> I18n = new(StringComparer.Ordinal)
    {
        ["title"] = ("OBS 手机遥控", "OBS Helper Remote"),
        ["remote"] = ("遥控", "Remote"),
        ["monitor"] = ("监看", "Monitor"),
        ["prompter"] = ("提词", "Prompt"),
        ["light"] = ("补光", "Light"),
        ["upload"] = ("快传", "Upload"),
        ["theme"] = ("主题", "Theme"),
        ["themeAuto"] = ("自动", "Auto"),
        ["themeLight"] = ("浅色", "Light"),
        ["themeDark"] = ("深色", "Dark"),
        ["pairing"] = ("正在配对…", "Pairing…"),
        ["pairFailed"] = ("配对失败，请在电脑上重新生成二维码。", "Pairing failed. Generate a new QR code on the PC."),
        ["pairNeeded"] = ("还没有配对信息，请重新扫码。", "Not paired yet. Scan the QR code again."),
        ["scenes"] = ("场景", "Scenes"),
        ["noScenes"] = ("暂无场景", "No scenes yet"),
        ["waiting"] = ("正在读取 OBS 状态…", "Loading OBS state…"),
        ["audio"] = ("音频", "Audio"),
        ["noAudio"] = ("没有可用的音频输入", "No audio inputs"),
        ["controls"] = ("控制", "Controls"),
        ["record"] = ("录制", "Record"),
        ["stopRecord"] = ("停止录制", "Stop recording"),
        ["replay"] = ("保存回放", "Save replay"),
        ["virtualcam"] = ("虚拟摄像头", "Virtual camera"),
        ["studio"] = ("演播室模式", "Studio mode"),
        ["stream"] = ("推流", "Stream"),
        ["streamStart"] = ("开始推流", "Start streaming"),
        ["stopStream"] = ("停止推流", "Stop streaming"),
        ["confirmStream"] = ("推流会立刻对外直播，确定继续？", "Going live publishes immediately. Continue?"),
        ["confirmStopStream"] = ("停止推流会立刻结束直播，确定继续？", "Stopping ends the live stream immediately. Continue?"),
        ["online"] = ("OBS 已连接", "OBS connected"),
        ["offline"] = ("OBS 未连接", "OBS offline"),
        ["tallyLive"] = ("直播中", "LIVE"),
        ["tallyStandby"] = ("待机", "STANDBY"),
        ["viewer"] = ("只读权限，只能查看", "Read-only: view only"),
        ["operator"] = ("操作员", "Operator"),
        ["admin"] = ("管理员", "Admin"),
        ["previewTitle"] = ("节目画面", "Program preview"),
        ["previewHint"] = ("画面每秒刷新一次", "Refreshes once per second"),
        ["previewOff"] = ("页面不可见，预览已暂停", "Page hidden — preview paused"),
        ["previewFailed"] = ("预览失败，请确认 OBS 已输出画面", "Preview failed. Check the OBS output."),
        ["system"] = ("电脑状态", "PC status"),
        ["memory"] = ("内存", "Memory"),
        ["diskFree"] = ("磁盘剩余", "Disk free"),
        ["play"] = ("播放", "Play"),
        ["pause"] = ("暂停", "Pause"),
        ["restart"] = ("回到开头", "Restart"),
        ["speed"] = ("速度", "Speed"),
        ["font"] = ("字号", "Font"),
        ["mirror"] = ("镜像", "Mirror"),
        ["script"] = ("提词稿", "Script"),
        ["scriptEmpty"] = ("电脑上还没有提词稿。", "No script on the PC yet."),
        ["scriptEdit"] = ("编辑提词稿", "Edit script"),
        ["saveScript"] = ("保存到电脑", "Save to PC"),
        ["refreshScript"] = ("从电脑刷新", "Refresh from PC"),
        ["scriptSaved"] = ("已保存到电脑", "Saved to the PC"),
        ["scriptTooLong"] = ("提词稿不能超过 20000 字", "Script must be 20000 characters or fewer"),
        ["scriptRequired"] = ("提词稿不能为空", "Script cannot be empty"),
        ["scriptDirty"] = ("有未保存的修改", "Unsaved changes"),
        ["offsetUp"] = ("上移", "Scroll up"),
        ["offsetDown"] = ("下移", "Scroll down"),
        ["openLight"] = ("打开补光 / 色卡", "Open light & color card"),
        ["openLightHint"] = ("独立离线页面：补光灯、灰卡与倒计时。", "Separate offline page: fill light, gray card, countdown."),
        ["uploadHint"] = ("选择 JPEG / PNG / MP4，最大 50 MB", "Choose a JPEG, PNG or MP4 up to 50 MB"),
        ["uploading"] = ("上传中…", "Uploading…"),
        ["uploaded"] = ("已上传：", "Uploaded: "),
        ["uploadFailed"] = ("上传失败：", "Upload failed: "),
        ["connectionLost"] = ("与电脑的连接已断开，正在重试…", "Connection lost. Reconnecting…"),
        ["offlineHint"] = ("网络异常，请检查与电脑的连接", "Network error. Check the connection to the PC."),
        ["rateLimited"] = ("操作太频繁，请稍候。", "Too many actions. Please wait."),
        ["commandFailed"] = ("操作失败", "Action failed"),
        ["sent"] = ("已发送", "Sent"),
        ["mute"] = ("静音", "Mute"),
        ["muted"] = ("已静音", "Muted"),
    };

    private const string AppTemplate = """
<!DOCTYPE html>
<html lang="__LANG__">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
<meta name="color-scheme" content="dark light">
<meta name="theme-color" content="#f4f6f8" id="themeColor">
<meta name="referrer" content="no-referrer">
<meta name="format-detection" content="telephone=no">
<meta http-equiv="Cache-Control" content="no-store">
<title>OBS Helper</title>
<script>
(function () {
  var mode = "auto";
  try {
    mode = localStorage.getItem("obshelper.theme") || "auto";
  } catch (e) {
    try { mode = sessionStorage.getItem("obshelper.theme") || "auto"; } catch (e2) { mode = "auto"; }
  }
  var dark = false;
  try { dark = window.matchMedia && window.matchMedia("(prefers-color-scheme: dark)").matches; } catch (e3) { }
  document.documentElement.setAttribute("data-theme", mode === "auto" ? (dark ? "dark" : "light") : mode);
})();
</script>
<style>
:root{
  --bg:#f4f6f8;--surface:#ffffff;--surface-2:#edf0f4;--line:#dbe1e8;--fg:#161b21;--muted:#5a6673;
  --accent:#2563eb;--accent-fg:#ffffff;--accent-soft:#e8efff;
  --red:#c62828;--red-soft:#fdecec;--green:#0f7a4d;--green-soft:#e6f6ee;
  --radius:16px;--radius-sm:12px;--shadow:0 1px 2px rgba(16,24,40,.05);
}
:root[data-theme="dark"]{
  --bg:#1a1e24;--surface:#232830;--surface-2:#2b3139;--line:#3a424d;--fg:#eef2f6;--muted:#a7b2be;
  --accent:#78a9ff;--accent-fg:#10161f;--accent-soft:#243449;
  --red:#ff8a80;--red-soft:#3b2323;--green:#5bd6a0;--green-soft:#173328;
  --shadow:none;
}
*{box-sizing:border-box;-webkit-tap-highlight-color:transparent}
html{font-size:100%;-webkit-text-size-adjust:100%}
body{margin:0;background:var(--bg);color:var(--fg);
  font:1.0625rem/1.5 -apple-system,BlinkMacSystemFont,"Segoe UI","PingFang SC","Hiragino Sans GB","Microsoft YaHei",Roboto,sans-serif;
  padding:0 constant(safe-area-inset-right) constant(safe-area-inset-bottom) constant(safe-area-inset-left);
  padding:0 env(safe-area-inset-right) env(safe-area-inset-bottom) env(safe-area-inset-left)}
.top{position:-webkit-sticky;position:sticky;top:0;z-index:10;background:var(--bg);border-bottom:1px solid var(--line);
  padding-top:constant(safe-area-inset-top);padding-top:env(safe-area-inset-top)}
.bar{display:flex;align-items:center;gap:12px;padding:10px 16px}
.brand{display:flex;align-items:center;gap:10px;min-width:0;flex:1}
.dot{width:10px;height:10px;border-radius:50%;background:var(--muted);flex:0 0 auto}
.dot.on{background:var(--green)}
.dot.red{background:var(--red)}
#devname{font-weight:650;font-size:1.0625rem;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.ver{flex:0 0 auto;font-size:.6875rem;font-weight:600;color:var(--muted);border:1px solid var(--line);border-radius:999px;padding:2px 7px}
.bar-actions{display:flex;align-items:center;gap:8px;flex:0 0 auto}
.chip{font-size:.8125rem;color:var(--muted);border:1px solid var(--line);border-radius:999px;padding:4px 10px;white-space:nowrap}
.icon-btn{min-width:44px;min-height:44px;padding:0 12px;border-radius:12px;border:1px solid var(--line);
  background:var(--surface);color:var(--fg);font-size:.875rem;font-weight:600}
.tabs{display:flex;gap:6px;padding:0 12px 12px}
.tabs::-webkit-scrollbar{display:none}
.tabs button{flex:1 1 0;min-width:0;min-height:44px;padding:0 8px;border-radius:999px;border:1px solid var(--line);
  background:var(--surface);color:var(--fg);font-size:.875rem;font-weight:500;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
html[lang="en"] .tabs button{font-size:.8125rem}
.tabs button[aria-selected="true"]{background:var(--accent);border-color:var(--accent);color:var(--accent-fg);font-weight:600}
.banner{margin:0;padding:10px 16px;font-size:.875rem;background:var(--red-soft);color:var(--red);border-bottom:1px solid var(--line)}
main{padding:4px 16px 40px;max-width:760px;margin:0 auto}
section{display:none}
section.active{display:block}
h2{font-size:.8125rem;font-weight:650;letter-spacing:.05em;color:var(--muted);margin:22px 2px 10px}
h2:first-child{margin-top:16px}
.card{background:var(--surface);border:1px solid var(--line);border-radius:var(--radius);padding:14px;box-shadow:var(--shadow)}
.grid{display:grid;gap:10px;grid-template-columns:repeat(auto-fill,minmax(148px,1fr))}
.actions{grid-template-columns:repeat(2,minmax(0,1fr));margin-top:12px}
.actions .span-2{grid-column:1/-1}
.btn{display:flex;align-items:center;justify-content:center;gap:8px;width:100%;min-height:52px;padding:12px 14px;
  border-radius:var(--radius-sm);border:1px solid var(--line);background:var(--surface);box-shadow:var(--shadow);
  color:var(--fg);font:inherit;font-size:1rem;font-weight:500;text-align:center;text-decoration:none;
  -webkit-appearance:none;appearance:none;
  transition:background .12s,border-color .12s,color .12s}
.btn:active{opacity:.85}
.btn[aria-pressed="true"],.btn.active{border-color:var(--accent);background:var(--accent-soft);color:var(--accent);font-weight:600}
.btn.danger{border-color:var(--red);color:var(--red)}
.btn.danger.active{background:var(--red);border-color:var(--red);color:#fff}
.btn:disabled{opacity:.45}
.scene{min-height:60px;padding:14px;font-size:.9375rem;line-height:1.35;text-align:left;
  display:-webkit-box;-webkit-line-clamp:2;-webkit-box-orient:vertical;overflow:hidden;word-break:break-word}
.tally{display:flex;align-items:center;gap:12px;padding:14px 16px;border-radius:var(--radius);
  border:1px solid var(--line);background:var(--surface);box-shadow:var(--shadow)}
.tally .dot{width:12px;height:12px}
.tally .label{font-size:1.125rem;font-weight:650}
.tally .time{margin-left:auto;font-variant-numeric:tabular-nums;color:var(--muted);font-weight:600}
.tally.green{border-color:var(--green);background:var(--green-soft)}
.tally.green .dot{background:var(--green)}
.tally.red{border-color:var(--red);background:var(--red-soft)}
.tally.red .dot{background:var(--red)}
.tally.red .label{color:var(--red)}
.timers{display:grid;grid-template-columns:1fr 1fr;gap:10px;margin-top:10px}
.timer{background:var(--surface);border:1px solid var(--line);border-radius:var(--radius-sm);padding:12px;box-shadow:var(--shadow)}
.timer .k{font-size:.8125rem;color:var(--muted)}
.timer .v{font-size:1.25rem;font-weight:650;font-variant-numeric:tabular-nums;margin-top:2px}
.audio-row{display:grid;grid-template-columns:minmax(0,1fr) 48px;gap:2px 10px;align-items:center;
  padding:10px 0;border-top:1px solid var(--line)}
.audio-row:first-child{border-top:0;padding-top:2px}
.audio-row .name{overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font-weight:500}
.mute{width:48px;height:48px;min-height:48px;padding:0;border-radius:var(--radius-sm);border:1px solid var(--line);
  background:var(--surface);color:var(--fg);font:inherit;font-size:.8125rem}
.mute[aria-pressed="true"]{border-color:var(--red);color:var(--red);background:var(--red-soft)}
.vol{grid-column:1/-1;display:flex;align-items:center;gap:10px;min-height:40px}
.vol input{flex:1;min-height:44px;accent-color:var(--accent)}
.vol .db{font-size:.8125rem;color:var(--muted);font-variant-numeric:tabular-nums;min-width:56px;text-align:right}
.preview-wrap{position:relative;aspect-ratio:16/9;min-height:180px;border-radius:var(--radius);overflow:hidden;
  border:1px solid var(--line);background:var(--surface-2)}
#preview{width:100%;height:100%;max-height:60vh;display:block;object-fit:contain}
.preview-hint{position:absolute;top:0;right:0;bottom:0;left:0;inset:0;display:flex;align-items:center;justify-content:center;
  padding:16px;text-align:center;color:var(--muted);font-size:.9375rem}
.stats{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:10px;margin-top:12px}
.stat{background:var(--surface);border:1px solid var(--line);border-radius:var(--radius-sm);padding:12px;box-shadow:var(--shadow)}
.stat .k{font-size:.75rem;color:var(--muted)}
.stat .v{font-size:1.125rem;font-weight:650;font-variant-numeric:tabular-nums;margin-top:2px}
.row{display:flex;align-items:center;gap:12px;min-height:48px}
.row label{min-width:56px;color:var(--muted);font-size:.9375rem}
.row input[type=range]{flex:1;min-height:44px;accent-color:var(--accent)}
.row .val{min-width:48px;text-align:right;font-variant-numeric:tabular-nums;color:var(--muted)}
.check{display:flex;align-items:center;justify-content:space-between;min-height:52px;border-top:1px solid var(--line);margin-top:6px}
.check input{width:24px;height:24px;accent-color:var(--accent)}
.script{white-space:pre-wrap;font-size:var(--prompt-font,1.375rem);line-height:1.6;max-height:46vh;overflow:auto;
  padding:14px;background:var(--surface);border:1px solid var(--line);border-radius:var(--radius);
  box-shadow:var(--shadow);word-break:break-word}
.script.mirror{transform:scaleX(-1)}
.hint{color:var(--muted);font-size:.875rem;margin:10px 2px 0}
.filebox{display:block;padding:18px;border:1px dashed var(--line);border-radius:var(--radius);
  background:var(--surface);text-align:center}
.filebox input{width:100%;min-height:48px;color:var(--fg);font-size:.9375rem}
.hidden{display:none !important}
.toast{position:fixed;left:50%;bottom:20px;bottom:calc(20px + constant(safe-area-inset-bottom));
  bottom:calc(20px + env(safe-area-inset-bottom));
  transform:translateX(-50%) translateY(8px);max-width:calc(100% - 32px);padding:12px 18px;border-radius:999px;
  background:var(--fg);color:var(--bg);font-size:.9375rem;opacity:0;pointer-events:none;
  transition:opacity .15s,transform .15s;z-index:50;text-align:center}
.toast.show{opacity:1;transform:translateX(-50%) translateY(0)}
.toast.bad{background:var(--red);color:#fff}
@media (min-width:520px){.actions{grid-template-columns:repeat(3,minmax(0,1fr))}.actions .span-2{grid-column:auto}}
@media (prefers-reduced-motion:reduce){*{transition:none !important}}
</style>
</head>
<body>
<div class="top">
  <header class="bar">
    <div class="brand"><span id="status" class="dot" aria-hidden="true"></span><span id="devname">OBS Helper</span><span class="ver" id="ver">v__VERSION__</span></div>
    <div class="bar-actions">
      <span id="role" class="chip hidden"></span>
      <button id="theme" class="icon-btn" type="button">自动</button>
    </div>
  </header>
  <p id="netNotice" class="banner hidden" role="status" aria-live="polite"></p>
  <nav id="tabs" class="tabs" role="tablist">
    <button role="tab" data-tab="remote" aria-selected="true">—</button>
    <button role="tab" data-tab="monitor" aria-selected="false">—</button>
    <button role="tab" data-tab="prompter" aria-selected="false">—</button>
    <button role="tab" data-tab="light" aria-selected="false">—</button>
    <button role="tab" data-tab="upload" aria-selected="false">—</button>
  </nav>
</div>
<noscript>
  <p class="banner">请启用 JavaScript 后刷新页面。 / Please enable JavaScript and reload.</p>
</noscript>
<p id="bootError" class="banner hidden" role="alert">当前浏览器无法运行页面脚本，请升级浏览器或换用系统浏览器后重新扫码。 / This browser could not run the page script. Please update it or open the QR link in the system browser.</p>
<main>
<section id="remote" class="active" role="tabpanel" aria-labelledby="tab-remote">
  <div class="tally js-tally"><span class="dot" aria-hidden="true"></span><span class="label">—</span><span class="time">00:00:00</span></div>
  <div class="timers">
    <div class="timer"><div class="k" data-i18n="record"></div><div class="v" data-timer="record">00:00:00</div></div>
    <div class="timer"><div class="k" data-i18n="stream"></div><div class="v" data-timer="stream">00:00:00</div></div>
  </div>
  <h2 data-i18n="scenes"></h2>
  <div id="scenes" class="grid"></div>
  <p class="hint hidden" id="scenesEmpty" data-i18n="noScenes"></p>
  <h2 data-i18n="audio"></h2>
  <div id="audio" class="card"></div>
  <p class="hint hidden" id="audioEmpty" data-i18n="noAudio"></p>
  <h2 data-i18n="controls"></h2>
  <div class="grid actions">
    <button class="btn" id="btnRecord" data-cmd="record.toggle" type="button"></button>
    <button class="btn" id="btnReplay" data-cmd="replay.save" data-i18n="replay" type="button"></button>
    <button class="btn" id="btnCam" data-cmd="virtualcam.toggle" data-i18n="virtualcam" type="button"></button>
    <button class="btn" id="btnStudio" data-cmd="studio.toggle" data-i18n="studio" type="button"></button>
    <button class="btn danger span-2" id="btnStream" data-cmd="stream" type="button"></button>
  </div>
  <p class="hint" id="roleNotice"></p>
</section>
<section id="monitor" role="tabpanel" aria-labelledby="tab-monitor">
  <div class="tally js-tally"><span class="dot" aria-hidden="true"></span><span class="label">—</span><span class="time">00:00:00</span></div>
  <h2 data-i18n="previewTitle"></h2>
  <div class="preview-wrap">
    <img id="preview" alt="" class="hidden" />
    <div id="previewNotice" class="preview-hint"></div>
  </div>
  <div class="timers">
    <div class="timer"><div class="k" data-i18n="record"></div><div class="v" data-timer="record">00:00:00</div></div>
    <div class="timer"><div class="k" data-i18n="stream"></div><div class="v" data-timer="stream">00:00:00</div></div>
  </div>
  <h2 data-i18n="system"></h2>
  <div class="stats">
    <div class="stat"><div class="k">CPU</div><div class="v" id="statCpu">—</div></div>
    <div class="stat"><div class="k" data-i18n="memory"></div><div class="v" id="statMem">—</div></div>
    <div class="stat"><div class="k" data-i18n="diskFree"></div><div class="v" id="statDisk">—</div></div>
  </div>
</section>
<section id="prompter" role="tabpanel" aria-labelledby="tab-prompter">
  <div class="grid actions">
    <button class="btn" id="pPlay" type="button"></button>
    <button class="btn" id="pPause" type="button"></button>
  </div>
  <div class="grid actions">
    <button class="btn" id="pRestart" type="button"></button>
    <button class="btn" id="pUp" type="button"></button>
    <button class="btn" id="pDown" type="button"></button>
  </div>
  <div class="card" style="margin-top:12px">
    <div class="row"><label data-i18n="speed"></label><input id="pSpeed" type="range" min="1" max="10" step="1" /><span class="val" id="pSpeedVal">5</span></div>
    <div class="row"><label data-i18n="font"></label><input id="pFont" type="range" min="16" max="72" step="2" /><span class="val" id="pFontVal">26</span></div>
    <div class="check"><label data-i18n="mirror"></label><input id="pMirror" type="checkbox" /></div>
  </div>
  <h2 data-i18n="script"></h2>
  <div class="card" style="margin-bottom:12px">
    <label for="pScript" data-i18n="scriptEdit" style="font-weight:600"></label>
    <textarea id="pScript" rows="6" maxlength="20000" style="width:100%;margin-top:8px;padding:10px;border:1px solid var(--line);border-radius:var(--radius-sm);background:var(--surface);color:var(--fg);font:inherit;font-size:1rem;line-height:1.5;resize:vertical"></textarea>
    <div class="grid actions">
      <button class="btn" id="pSaveScript" type="button" data-i18n="saveScript"></button>
      <button class="btn" id="pReloadScript" type="button" data-i18n="refreshScript"></button>
    </div>
    <p id="pScriptNotice" class="hint" role="status" aria-live="polite"></p>
  </div>
  <div id="prompt" class="script"></div>
</section>
<section id="light" role="tabpanel" aria-labelledby="tab-light">
  <div class="card" style="margin-top:16px">
    <p class="hint" data-i18n="openLightHint"></p>
    <div id="lightLink" style="margin-top:12px"></div>
  </div>
</section>
<section id="upload" role="tabpanel" aria-labelledby="tab-upload">
  <div class="card" style="margin-top:16px">
    <p class="hint" data-i18n="uploadHint"></p>
    <label class="filebox" style="margin-top:12px"><input id="file" type="file" accept="image/jpeg,image/png,video/mp4" /></label>
    <p class="hint" id="uploadNotice" role="status" aria-live="polite"></p>
  </div>
</section>
</main>
<div id="toast" class="toast" role="status" aria-live="polite"></div>
<script>
"use strict";
/* 旧内核兜底（iOS 9 / Android 5 自带浏览器）：补齐最常用的 DOM 方法，缺失的能力由页面给出明确提示。 */
if (window.NodeList && !NodeList.prototype.forEach) NodeList.prototype.forEach = Array.prototype.forEach;
if (window.Element && !Element.prototype.matches) {
  Element.prototype.matches = Element.prototype.msMatchesSelector || Element.prototype.webkitMatchesSelector;
}
if (window.Element && !Element.prototype.closest) {
  Element.prototype.closest = function (selector) {
    var node = this;
    while (node && node.nodeType === 1) {
      if (node.matches(selector)) return node;
      node = node.parentElement;
    }
    return null;
  };
}
const I18N = __I18N__;
const lang = document.documentElement.lang === "en" ? "en" : "zh";
const $ = id => document.getElementById(id);
const memoryStore = {};
const state = {
  token: readStore("obshelper.token"), role: "", snapshot: null, ws: null, es: null,
  previewTimer: 0, previewMsg: "", retry: 0, theme: readStore("obshelper.theme") || "auto", audioDrag: "",
  scriptDirty: false
};

function readStore(key) {
  try { const v = localStorage.getItem(key); if (v !== null) return v; } catch (e) { }
  try { const s = sessionStorage.getItem(key); if (s !== null) return s; } catch (e) { }
  return memoryStore[key] || "";
}
function writeStore(key, value) {
  memoryStore[key] = value || "";
  try { if (value) localStorage.setItem(key, value); else localStorage.removeItem(key); } catch (e) { }
  try { if (value) sessionStorage.setItem(key, value); else sessionStorage.removeItem(key); } catch (e) { }
}
function t(key) {
  const row = I18N[key];
  const text = row ? row[lang === "en" ? 1 : 0] : "";
  return text || key;
}

/* ---------------------------------------------------------------- 主题 */
const scheme = window.matchMedia("(prefers-color-scheme: dark)");
function resolvedTheme() {
  return state.theme === "auto" ? (scheme.matches ? "dark" : "light") : state.theme;
}
function applyTheme() {
  const dark = resolvedTheme() === "dark";
  document.documentElement.setAttribute("data-theme", dark ? "dark" : "light");
  const meta = $("themeColor");
  if (meta) meta.setAttribute("content", dark ? "#1a1e24" : "#f4f6f8");
  updateThemeButton();
}
function updateThemeButton() {
  const b = $("theme");
  if (!b) return;
  const label = state.theme === "light" ? t("themeLight") : state.theme === "dark" ? t("themeDark") : t("themeAuto");
  b.textContent = label;
  b.setAttribute("aria-label", t("theme") + ": " + label);
  b.title = t("theme") + ": " + label;
}
if (scheme.addEventListener) scheme.addEventListener("change", () => { if (state.theme === "auto") applyTheme(); });
else if (scheme.addListener) scheme.addListener(() => { if (state.theme === "auto") applyTheme(); });
$("theme").addEventListener("click", () => {
  const order = ["auto", "light", "dark"];
  state.theme = order[(order.indexOf(state.theme) + 1) % order.length];
  writeStore("obshelper.theme", state.theme === "auto" ? "" : state.theme);
  applyTheme();
});

/* ---------------------------------------------------------------- 文案 */
function applyI18n() {
  document.title = t("title");
  document.querySelectorAll("[data-i18n]").forEach(n => { n.textContent = t(n.getAttribute("data-i18n")); });
  $("tabs").querySelectorAll("button").forEach(b => {
    b.textContent = t(b.dataset.tab);
    b.id = "tab-" + b.dataset.tab;
  });
  $("pPlay").textContent = t("play");
  $("pPause").textContent = t("pause");
  $("pRestart").textContent = t("restart");
  $("pUp").textContent = t("offsetUp");
  $("pDown").textContent = t("offsetDown");
  updateThemeButton();
  renderLightLink();
  renderPreviewNotice();
  renderLabels();
}
function renderLightLink() {
  const box = $("lightLink");
  if (!box) return;
  box.textContent = "";
  const a = document.createElement("a");
  a.className = "btn";
  a.href = location.pathname + "/light";
  a.textContent = t("openLight");
  box.appendChild(a);
}

/* ---------------------------------------------------------------- 通用 */
function canOperate() { return state.role === "operator" || state.role === "admin"; }
function newId() {
  try { if (window.crypto && crypto.randomUUID) return crypto.randomUUID(); } catch (e) { }
  return String(Date.now()) + Math.random().toString(16).slice(2);
}
let toastTimer = 0;
function toast(text, bad) {
  const n = $("toast");
  if (!n) return;
  n.textContent = text;
  n.className = "toast show" + (bad ? " bad" : "");
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => { n.className = "toast"; }, 2600);
}
function showBanner(text) {
  const n = $("netNotice");
  if (!n) return;
  n.textContent = text;
  n.classList.remove("hidden");
}
function hideBanner() { const n = $("netNotice"); if (n) n.classList.add("hidden"); }
function setDot(cls) { const n = $("status"); if (n) n.className = "dot " + cls; }
function activeTab() {
  const b = document.querySelector("#tabs button[aria-selected='true']");
  return b ? b.dataset.tab : "remote";
}

/* ---------------------------------------------------------------- 配对 / 连接 */
function pairIdFromPath() {
  const marker = location.pathname.lastIndexOf("/m/");
  const id = marker >= 0 ? location.pathname.slice(marker + 3) : "";
  return id.replace(/\/+$/, "");
}
function secretFromHash() {
  const raw = (location.hash || "").replace(/^#/, "");
  if (!raw) return "";
  try { return decodeURIComponent(raw); } catch (e) { return raw; }
}
async function pair() {
  const secret = secretFromHash();
  if (!secret) {
    if (state.token) { $("devname").textContent = readStore("obshelper.deviceName") || "OBS Helper"; connect(); return true; }
    $("devname").textContent = t("pairNeeded");
    showBanner(t("pairNeeded"));
    return false;
  }
  let name = readStore("obshelper.deviceName");
  if (!name) {
    name = "phone";
    try { name = (navigator.userAgent.match(/\(([^)]+)\)/) || [])[1] || name; } catch (e) { }
    name = name.slice(0, 40);
    writeStore("obshelper.deviceName", name);
  }
  $("devname").textContent = t("pairing");
  showBanner(t("pairing"));
  try {
    const r = await fetch("/api/pair", {
      method: "POST", headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ pairId: pairIdFromPath(), secret: secret, deviceName: name, capabilities: ["remote", "preview", "prompter", "upload"] })
    });
    const j = await r.json();
    if (!r.ok || !j.ok) { pairFailed(); return false; }
    state.token = j.token;
    writeStore("obshelper.token", j.token);
    state.role = j.role || "viewer";
    if (j.deviceName) { $("devname").textContent = j.deviceName; writeStore("obshelper.deviceName", j.deviceName); }
    try { history.replaceState(null, "", location.pathname); } catch (e) { }
    hideBanner();
    applyI18n();
    if (j.snapshot) render(j.snapshot);
    connect();
    return true;
  } catch (e) { pairFailed(); return false; }
}
function pairFailed() {
  state.token = "";
  writeStore("obshelper.token", "");
  $("devname").textContent = t("pairFailed");
  showBanner(t("pairFailed"));
  setDot("");
}

function connect() {
  if (!state.token) return;
  if (state.ws) { try { state.ws.close(); } catch (e) { } state.ws = null; }
  const proto = location.protocol === "https:" ? "wss:" : "ws:";
  let ws;
  try { ws = new WebSocket(proto + "//" + location.host + "/ws"); } catch (e) { startSse(); return; }
  state.ws = ws;
  ws.onopen = () => { try { ws.send(JSON.stringify({ type: "auth", token: state.token })); } catch (e) { } };
  ws.onmessage = ev => {
    let m; try { m = JSON.parse(ev.data); } catch (e) { return; }
    onMessage(m);
  };
  ws.onclose = ev => {
    state.ws = null;
    setDot("");
    if (ev && ev.code === 4001) { pairFailed(); return; }
    if (state.token) {
      state.retry = Math.min(10, state.retry + 1);
      showBanner(t("connectionLost"));
      setTimeout(connect, state.retry * 1000);
    }
  };
  ws.onerror = () => { try { ws.close(); } catch (e) { } };
}
function startSse() {
  if (state.es || !state.token) return;
  state.es = new EventSource("/api/events?token=" + encodeURIComponent(state.token));
  state.es.onmessage = ev => { try { onMessage(JSON.parse(ev.data)); } catch (e) { } };
  state.es.onerror = () => {
    if (state.es) { state.es.close(); state.es = null; }
    showBanner(t("connectionLost"));
    setTimeout(startSse, 3000);
  };
}
function onMessage(m) {
  if (m.type === "auth") {
    if (!m.ok) { pairFailed(); return; }
    state.retry = 0;
    hideBanner();
    setDot("on");
    if (m.snapshot) render(m.snapshot);
    return;
  }
  if (m.type === "state" || m.type === "snapshot") { render(m); return; }
  if (m.type === "result" && !m.ok) toast(m.message || t("commandFailed"), true);
}

/* ---------------------------------------------------------------- 渲染 */
function render(s) {
  if (!s) return;
  state.snapshot = s;
  state.role = s.role || state.role;
  if (s.deviceName) $("devname").textContent = s.deviceName;
  setDot(s.tally === "off" ? "" : s.tally === "red" ? "red" : "on");
  renderTally(s);
  renderScenes(s);
  renderAudio(s);
  renderPrompt(s);
  renderStats(s);
  renderLabels();
}
function renderTally(s) {
  const label = s.tally === "red" ? t("tallyLive") : s.tally === "off" ? t("offline") : t("tallyStandby");
  const time = s.streaming ? (s.streamTime || "00:00:00") : (s.recording ? (s.recordTime || "00:00:00") : "00:00:00");
  document.querySelectorAll(".js-tally").forEach(n => {
    n.classList.remove("off", "green", "red");
    n.classList.add(s.tally || "off");
    const l = n.querySelector(".label"); if (l) l.textContent = label;
    const tm = n.querySelector(".time"); if (tm) tm.textContent = time;
  });
  document.querySelectorAll('[data-timer="record"]').forEach(n => { n.textContent = s.recordTime || "00:00:00"; });
  document.querySelectorAll('[data-timer="stream"]').forEach(n => { n.textContent = s.streamTime || "00:00:00"; });
}
function renderScenes(s) {
  const box = $("scenes");
  if (!box) return;
  box.textContent = "";
  const names = s.scenes || [];
  const empty = $("scenesEmpty");
  if (empty) empty.classList.toggle("hidden", names.length > 0);
  names.forEach(name => {
    const b = document.createElement("button");
    const current = name === s.currentScene;
    b.type = "button";
    b.className = "btn scene" + (current ? " active" : "");
    b.textContent = name;
    b.setAttribute("aria-pressed", current ? "true" : "false");
    b.disabled = !canOperate();
    b.dataset.cmd = "scene.set";
    b.dataset.scene = name;
    box.appendChild(b);
  });
}
function buildAudioRow(a) {
  const row = document.createElement("div");
  row.className = "audio-row";
  row.dataset.input = a.name;
  const name = document.createElement("span");
  name.className = "name";
  name.textContent = a.name;
  const mute = document.createElement("button");
  mute.type = "button";
  mute.className = "mute";
  mute.dataset.cmd = "audio.mute";
  mute.dataset.input = a.name;
  const vol = document.createElement("div");
  vol.className = "vol";
  const range = document.createElement("input");
  range.type = "range"; range.min = "-100"; range.max = "0"; range.step = "1";
  range.value = String(Math.round(a.volumeDb || 0));
  range.setAttribute("aria-label", a.name + " " + t("audio"));
  const db = document.createElement("span");
  db.className = "db";
  db.textContent = Math.round(a.volumeDb || 0) + " dB";
  range.addEventListener("input", () => {
    state.audioDrag = a.name;
    db.textContent = Math.round(Number(range.value)) + " dB";
  });
  range.addEventListener("change", () => {
    state.audioDrag = "";
    send("audio.volume", { input: a.name, volumeDb: Number(range.value) });
  });
  vol.appendChild(range);
  vol.appendChild(db);
  row.appendChild(name);
  row.appendChild(mute);
  row.appendChild(vol);
  return row;
}
function renderAudio(s) {
  const box = $("audio");
  if (!box) return;
  const items = s.audio || [];
  const empty = $("audioEmpty");
  if (empty) empty.classList.toggle("hidden", items.length > 0);
  const signature = items.map(a => a.name).join("\n");
  if (box.dataset.signature !== signature) {
    box.textContent = "";
    box.dataset.signature = signature;
    items.forEach(a => box.appendChild(buildAudioRow(a)));
  }
  items.forEach(a => {
    let row = null;
    for (let i = 0; i < box.children.length; i++) {
      if (box.children[i].dataset.input === a.name) { row = box.children[i]; break; }
    }
    if (!row) return;
    const mute = row.querySelector(".mute");
    mute.textContent = a.muted ? t("muted") : t("mute");
    mute.setAttribute("aria-pressed", a.muted ? "true" : "false");
    mute.dataset.muted = a.muted ? "0" : "1";
    mute.disabled = !canOperate();
    const range = row.querySelector("input");
    range.disabled = !canOperate();
    if (state.audioDrag !== a.name) {
      range.value = String(Math.round(a.volumeDb || 0));
      const db = row.querySelector(".db");
      if (db) db.textContent = Math.round(a.volumeDb || 0) + " dB";
    }
  });
}
function renderPrompt(s) {
  const p = s.teleprompter || {};
  const prompt = $("prompt");
  if (prompt) {
    prompt.textContent = p.script || t("scriptEmpty");
    prompt.style.setProperty("--prompt-font", (p.fontSize || 26) + "px");
    prompt.classList.toggle("mirror", !!p.mirror);
  }
  // 手机端改稿：用户正在编辑时不覆盖输入；保存成功后由本端清掉 dirty。
  const editor = $("pScript");
  if (editor && !state.scriptDirty) editor.value = p.script || "";
  if ($("pScriptNotice")) {
    $("pScriptNotice").textContent = state.scriptDirty ? t("scriptDirty") : "";
  }
  const speed = $("pSpeed"), font = $("pFont");
  if (speed && document.activeElement !== speed) speed.value = String(p.speed || 5);
  if (font && document.activeElement !== font) font.value = String(p.fontSize || 26);
  if ($("pSpeedVal")) $("pSpeedVal").textContent = String(p.speed || 5);
  if ($("pFontVal")) $("pFontVal").textContent = String(p.fontSize || 26);
  const mirror = $("pMirror");
  if (mirror) mirror.checked = !!p.mirror;
}
function formatMb(mb) {
  const n = Number(mb) || 0;
  return n >= 1024 ? (n / 1024).toFixed(1) + " GB" : Math.round(n) + " MB";
}
function renderStats(s) {
  const sys = s.system || {};
  if ($("statCpu")) $("statCpu").textContent = Math.round(sys.cpu || 0) + "%";
  if ($("statMem")) $("statMem").textContent = formatMb(sys.memoryMb);
  if ($("statDisk")) $("statDisk").textContent = formatMb(sys.diskFreeMb);
}
function renderLabels() {
  const s = state.snapshot || {};
  if (!state.snapshot) {
    const sc = $("scenesEmpty"), au = $("audioEmpty");
    if (sc) { sc.textContent = t("waiting"); sc.classList.remove("hidden"); }
    if (au) { au.textContent = t("waiting"); au.classList.remove("hidden"); }
  }
  const role = $("role");
  if (role) {
    role.textContent = t(state.role);
    role.classList.toggle("hidden", !state.role);
  }
  const notice = $("roleNotice");
  if (notice) notice.textContent = state.role === "viewer" ? t("viewer") : "";
  const record = $("btnRecord");
  if (record) {
    record.textContent = s.recording ? t("stopRecord") : t("record");
    record.classList.toggle("active", !!s.recording);
    record.setAttribute("aria-pressed", s.recording ? "true" : "false");
  }
  const cam = $("btnCam");
  if (cam) { cam.classList.toggle("active", !!s.virtualCam); cam.setAttribute("aria-pressed", s.virtualCam ? "true" : "false"); }
  const studio = $("btnStudio");
  if (studio) { studio.classList.toggle("active", !!s.studioMode); studio.setAttribute("aria-pressed", s.studioMode ? "true" : "false"); }
  const stream = $("btnStream");
  if (stream) {
    stream.textContent = s.streaming ? t("stopStream") : t("streamStart");
    stream.classList.toggle("active", !!s.streaming);
    stream.setAttribute("aria-pressed", s.streaming ? "true" : "false");
  }
  const replay = $("btnReplay");
  if (replay) replay.disabled = !state.snapshot || !s.replayBuffer;
  [record, cam, studio, stream].forEach(b => { if (b) b.disabled = !canOperate(); });
  ["pPlay", "pPause", "pRestart", "pUp", "pDown", "pSpeed", "pFont", "pMirror"].forEach(id => {
    const n = $(id);
    if (n) n.disabled = !canOperate();
  });
}

/* ---------------------------------------------------------------- 命令 */
async function send(cmd, params) {
  if (!state.token) return false;
  const requestId = newId();
  try {
    const r = await fetch("/api/command", {
      method: "POST",
      headers: { "Content-Type": "application/json", "Authorization": "Bearer " + state.token },
      body: JSON.stringify({ requestId: requestId, command: cmd, params: params || {} })
    });
    const j = await r.json();
    if (r.status === 401) { pairFailed(); return false; }
    if (!j.ok) {
      if (j.code === "MOB_RATE_LIMITED") toast(t("rateLimited"), true);
      else toast(j.message || t("commandFailed"), true);
      return false;
    }
    return true;
  } catch (e) {
    toast(t("offlineHint"), true);
    return false;
  }
}

document.addEventListener("click", ev => {
  const b = ev.target && ev.target.closest ? ev.target.closest("button[data-cmd]") : null;
  if (!b || b.disabled) return;
  const cmd = b.dataset.cmd;
  if (cmd === "scene.set") { send(cmd, { scene: b.dataset.scene }); return; }
  if (cmd === "audio.mute") { send(cmd, { input: b.dataset.input, muted: b.dataset.muted === "1" }); return; }
  if (cmd === "stream") {
    const live = !!(state.snapshot && state.snapshot.streaming);
    if (!confirm(live ? t("confirmStopStream") : t("confirmStream"))) return;
    send(live ? "stream.stop" : "stream.start", { confirm: true });
    return;
  }
  send(cmd);
});
document.addEventListener("change", ev => {
  const i = ev.target;
  if (!i) return;
  if (i.id === "pSpeed") {
    if ($("pSpeedVal")) $("pSpeedVal").textContent = i.value;
    return send("teleprompter.speed", { speed: Number(i.value) });
  }
  if (i.id === "pFont") {
    if ($("pFontVal")) $("pFontVal").textContent = i.value;
    return send("teleprompter.font", { fontSize: Number(i.value) });
  }
  if (i.id === "pMirror") return send("teleprompter.mirror", { mirror: !!i.checked });
  if (i.id === "file") return upload(i);
});
function scriptText() {
  const editor = $("pScript");
  return editor ? editor.value : "";
}
async function saveScript() {
  const text = scriptText();
  const notice = $("pScriptNotice");
  if (!text.trim()) { if (notice) notice.textContent = t("scriptRequired"); return; }
  if (text.length > 20000) { if (notice) notice.textContent = t("scriptTooLong"); return; }
  const ok = await send("teleprompter.set", { text });
  if (ok) {
    state.scriptDirty = false;
    if (notice) notice.textContent = t("scriptSaved");
    toast(t("scriptSaved"));
  }
}
$("pScript").addEventListener("input", () => {
  state.scriptDirty = true;
  if ($("pScriptNotice")) $("pScriptNotice").textContent = t("scriptDirty");
});
$("pSaveScript").addEventListener("click", saveScript);
$("pReloadScript").addEventListener("click", () => { state.scriptDirty = false; send("state.get"); });
$("pPlay").addEventListener("click", () => send("teleprompter.play"));
$("pPause").addEventListener("click", () => send("teleprompter.pause"));
$("pRestart").addEventListener("click", () => send("teleprompter.restart"));
function nudge(delta) {
  const p = (state.snapshot && state.snapshot.teleprompter) || {};
  const next = Math.max(0, Number(p.offset || 0) + delta);
  send("teleprompter.offset", { offset: next });
}
$("pUp").addEventListener("click", () => nudge(-80));
$("pDown").addEventListener("click", () => nudge(80));

document.querySelectorAll("#tabs button").forEach(b => b.addEventListener("click", () => {
  document.querySelectorAll("#tabs button").forEach(x => x.setAttribute("aria-selected", String(x === b)));
  document.querySelectorAll("main section").forEach(s => s.classList.toggle("active", s.id === b.dataset.tab));
  if (b.dataset.tab === "monitor") startPreview(); else stopPreview();
  if (b.dataset.tab === "prompter") send("state.get");
}));

/* ---------------------------------------------------------------- 监看 */
function renderPreviewNotice() {
  const n = $("previewNotice");
  if (!n) return;
  if (state.previewMsg) {
    n.textContent = t(state.previewMsg);
    n.classList.remove("hidden");
  } else {
    n.textContent = "";
    n.classList.add("hidden");
  }
}
function setPreviewNotice(key) {
  state.previewMsg = key || "";
  renderPreviewNotice();
}
function startPreview() {
  stopPreview();
  if (!state.token || document.visibilityState !== "visible") return;
  setPreviewNotice("");
  const tick = async () => {
    try {
      const r = await fetch("/api/preview.jpg?fps=1", {
        headers: { "Authorization": "Bearer " + state.token }, cache: "no-store"
      });
      if (r.status === 401) { pairFailed(); stopPreview(); return; }
      if (!r.ok) { setPreviewNotice("previewFailed"); return; }
      if (!window.URL || !URL.createObjectURL) { setPreviewNotice("previewFailed"); return; }
      const blob = await r.blob();
      const url = URL.createObjectURL(blob);
      const img = $("preview");
      const old = img.dataset.url;
      img.src = url;
      img.classList.remove("hidden");
      if (old) URL.revokeObjectURL(old);
      img.dataset.url = url;
      setPreviewNotice("");
    } catch (e) { setPreviewNotice("previewFailed"); }
  };
  tick();
  state.previewTimer = setInterval(tick, 1000);
}
function stopPreview() {
  if (state.previewTimer) { clearInterval(state.previewTimer); state.previewTimer = 0; }
}
document.addEventListener("visibilitychange", () => {
  if (document.visibilityState === "visible") {
    connect();
    if (activeTab() === "monitor") startPreview();
  } else {
    stopPreview();
    if (activeTab() === "monitor") setPreviewNotice("previewOff");
  }
});

/* ---------------------------------------------------------------- 快传 */
async function upload(input) {
  const file = input.files && input.files[0];
  if (!file) return;
  const notice = $("uploadNotice");
  if (file.size > 50 * 1024 * 1024) { notice.textContent = t("uploadFailed") + "50 MB"; input.value = ""; return; }
  const fd = new FormData();
  fd.append("file", file, file.name);
  notice.textContent = t("uploading");
  try {
    const r = await fetch("/api/upload", { method: "POST", headers: { "Authorization": "Bearer " + state.token }, body: fd });
    const j = await r.json();
    notice.textContent = r.ok && j.ok ? t("uploaded") + (j.fileName || "") : t("uploadFailed") + (j.code || r.status);
  } catch (e) { notice.textContent = t("uploadFailed") + "network"; }
  input.value = "";
}

/* ---------------------------------------------------------------- 启动 */
function showBootError() {
  const box = $("bootError");
  if (box) box.classList.remove("hidden");
}
try {
  const i18nReady = !!(I18N && I18N.remote && I18N.remote[0] && I18N.remote[1]);
  if (!i18nReady || !window.fetch || !window.Promise || !window.WebSocket || !window.JSON) {
    // 词表缺失（旧构建的注入缺陷）或旧内核缺 fetch/Promise/WebSocket：
    // 直接给出可读提示，避免只看到一页空按钮。
    showBootError();
  } else {
    applyTheme();
    applyI18n();
    pair().then(ok => { if (!ok) setDot(""); }, () => { pairFailed(); });
    window.__obsHelperBooted = true;
  }
} catch (e) {
  showBootError();
}
</script>
<script>
/* 兼容性看门狗（ES5）：主脚本解析失败或旧内核不支持时给出明确提示，而不是整页空白。 */
(function () {
  if (window.__obsHelperBooted) return;
  var box = document.getElementById("bootError");
  if (box) box.className = "banner";
  var top = document.querySelector(".top");
  if (top) top.style.display = "none";
  var main = document.querySelector("main");
  if (main) main.style.display = "none";
})();
</script>
</body>
</html>
""";

    private const string LightTemplate = """
<!DOCTYPE html>
<html lang="__LANG__">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
<meta name="color-scheme" content="dark light">
<title>OBS Helper Light</title>
<style>
*{box-sizing:border-box}
html,body{margin:0;height:100%;background:#000;color:#fff;font:16px/1.4 system-ui,sans-serif;overflow:hidden}
#stage{position:fixed;inset:0;display:flex;align-items:center;justify-content:center;transition:background .15s linear}
#count{font-size:28vmin;font-weight:800;font-variant-numeric:tabular-nums;text-shadow:0 0 24px rgba(0,0,0,.35)}
#panel{position:fixed;left:0;right:0;bottom:0;padding:12px env(safe-area-inset-right) calc(12px + env(safe-area-inset-bottom)) env(safe-area-inset-left);background:rgba(0,0,0,.72);backdrop-filter:blur(6px)}
.row{display:flex;gap:10px;align-items:center;margin:8px 0}
.row label{width:72px;font-size:14px;opacity:.8}
input[type=range]{flex:1;min-height:44px}
button{min-height:48px;min-width:64px;border-radius:12px;border:1px solid #444;background:#1b2027;color:#fff;font-size:16px;padding:0 14px}
.swatches{display:flex;gap:8px;flex-wrap:wrap}
.swatches button{width:48px;height:48px;min-width:48px;border-radius:50%;border:2px solid #666;padding:0}
#toggle{position:fixed;top:calc(12px + env(safe-area-inset-top));right:12px;opacity:.5}
/* 白平衡卡：中性灰 18%，用于手动白平衡 */
#stage.card{background:#808080}
#stage.gray{background:#b3b3b3}
</style>
</head>
<body>
<div id="stage"><span id="count"></span></div>
<button id="toggle" aria-label="panel">☰</button>
<div id="panel" class="hidden">
  <div class="row"><label>色温</label><input id="temp" type="range" min="2000" max="10000" step="100" value="5600"></div>
  <div class="row"><label>亮度</label><input id="bright" type="range" min="10" max="100" step="5" value="100"></div>
  <div class="swatches" id="swatches"></div>
  <div class="row"><button id="card">灰卡</button><button id="timer">倒计时</button><button id="reset">重置</button></div>
</div>
<script>
"use strict";
const stage = document.getElementById("stage");
const count = document.getElementById("count");
const temp = document.getElementById("temp");
const bright = document.getElementById("bright");
const panel = document.getElementById("panel");
let countdown = 0, timer = 0;

function kelvinToRgb(k) {
  const t = k / 100; let r, g, b;
  if (t <= 66) { r = 255; g = 99.47 * Math.log(t) - 161.12; b = t <= 19 ? 0 : 138.52 * Math.log(t - 10) - 305.04; }
  else { r = 329.7 * Math.pow(t - 60, -0.1332); g = 288.12 * Math.pow(t - 60, -0.0755); b = 255; }
  const clamp = v => Math.max(0, Math.min(255, Math.round(v)));
  return [clamp(r), clamp(g), clamp(b)];
}
function apply() {
  if (stage.classList.contains("card") || stage.classList.contains("gray")) return;
  const [r, g, b] = kelvinToRgb(Number(temp.value));
  const k = Number(bright.value) / 100;
  stage.style.background = "rgb(" + Math.round(r * k) + "," + Math.round(g * k) + "," + Math.round(b * k) + ")";
}
temp.addEventListener("input", apply); bright.addEventListener("input", apply);
document.getElementById("toggle").addEventListener("click", () => panel.classList.toggle("hidden"));
document.getElementById("card").addEventListener("click", () => { stage.classList.remove("gray"); stage.classList.toggle("card"); });
document.getElementById("reset").addEventListener("click", () => { stage.className = ""; countdown = 0; count.textContent = ""; apply(); });
document.getElementById("timer").addEventListener("click", () => {
  countdown = countdown ? 0 : 10; count.textContent = countdown ? countdown : "";
  if (timer) { clearInterval(timer); timer = 0; }
  if (!countdown) return;
  timer = setInterval(() => { countdown--; count.textContent = countdown > 0 ? countdown : ""; if (countdown <= 0) { clearInterval(timer); timer = 0; } }, 1000);
});
const colors = ["#ffffff", "#fff4e5", "#e8f0ff", "#ffd6e7", "#d6ffe8"];
document.getElementById("swatches").textContent = "";
colors.forEach(c => { const b = document.createElement("button"); b.style.background = c; b.setAttribute("aria-label", c); b.addEventListener("click", () => { stage.className = ""; stage.style.background = c; count.textContent = ""; }); document.getElementById("swatches").appendChild(b); });
apply();
</script>
</body>
</html>
""";
}
