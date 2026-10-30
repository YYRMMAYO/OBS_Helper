using System.Text;
using System.Text.Json;
using OBS_Helper.Wpf.Localization;

namespace OBS_Helper.Wpf.Services.Mobile;

/// <summary>
/// 手机端页面（V3.4.0 / M01/M02/M05/M07）。
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
/// </summary>
public static class MobilePageAssets
{
    /// <summary>遥控主页面。</summary>
    public static string AppHtml(string language = Strings.ZhHans)
    {
        var i18n = JsonSerializer.Serialize(I18n, new JsonSerializerOptions { WriteIndented = false });
        return AppTemplate
            .Replace("__LANG__", language == Strings.EnUs ? "en" : "zh")
            .Replace("__I18N__", i18n);
    }

    /// <summary>补光 / 色卡 / 倒计时页面：完全离线，不引用任何网络 API。</summary>
    public static string LightHtml(string language = Strings.ZhHans)
        => LightTemplate.Replace("__LANG__", language == Strings.EnUs ? "en" : "zh");

    /// <summary>手机端词表（中英各一份）。键名与桌面端文案表有意不共用，便于独立演进。</summary>
    public static readonly Dictionary<string, (string Zh, string En)> I18n = new(StringComparer.Ordinal)
    {
        ["title"] = ("手机遥控 · OBS帮助助手", "Phone Remote · OBS Helper"),
        ["remote"] = ("遥控", "Remote"),
        ["monitor"] = ("监看", "Monitor"),
        ["prompter"] = ("提词", "Prompt"),
        ["light"] = ("补光", "Light"),
        ["upload"] = ("快传", "Upload"),
        ["pairing"] = ("正在配对…", "Pairing…"),
        ["pairFailed"] = ("配对失败，请让电脑上重新生成二维码。", "Pairing failed. Generate a new QR code on the PC."),
        ["deviceName"] = ("设备名称", "Device name"),
        ["scenes"] = ("场景", "Scenes"),
        ["audio"] = ("音频", "Audio"),
        ["record"] = ("录制", "Record"),
        ["stream"] = ("推流", "Stream"),
        ["virtualcam"] = ("虚拟摄像头", "Virtual camera"),
        ["studio"] = ("演播室模式", "Studio mode"),
        ["replay"] = ("保存回放", "Save replay"),
        ["confirmStream"] = ("推流会立刻对外直播，确定继续？", "Going live publishes immediately. Continue?"),
        ["online"] = ("OBS 已连接", "OBS connected"),
        ["offline"] = ("OBS 未连接", "OBS offline"),
        ["viewer"] = ("只读", "Viewer"),
        ["operator"] = ("操作员", "Operator"),
        ["admin"] = ("管理员", "Admin"),
        ["previewOff"] = ("页面不可见，预览已暂停", "Page hidden — preview paused"),
        ["previewFailed"] = ("预览失败", "Preview failed"),
        ["scriptEmpty"] = ("电脑上还没有提词稿。", "No script on the PC yet."),
        ["play"] = ("播放", "Play"),
        ["pause"] = ("暂停", "Pause"),
        ["restart"] = ("回到开头", "Restart"),
        ["speed"] = ("速度", "Speed"),
        ["font"] = ("字号", "Font"),
        ["mirror"] = ("镜像", "Mirror"),
        ["pickFile"] = ("选择文件（JPEG/PNG/MP4，≤50 MB）", "Choose file (JPEG/PNG/MP4, ≤50 MB)"),
        ["uploading"] = ("上传中…", "Uploading…"),
        ["uploaded"] = ("已上传：", "Uploaded: "),
        ["uploadFailed"] = ("上传失败：", "Upload failed: "),
        ["offlineHint"] = ("与电脑的连接已断开，正在重试…", "Connection lost. Retrying…"),
        ["tallyLive"] = ("直播中", "LIVE"),
        ["tallyStandby"] = ("待机", "STANDBY"),
        ["rateLimited"] = ("操作太频繁，请稍候。", "Too many actions. Please wait."),
    };

    private const string AppTemplate = """
<!DOCTYPE html>
<html lang="__LANG__">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
<meta name="color-scheme" content="dark light">
<meta name="referrer" content="no-referrer">
<title>OBS Helper</title>
<style>
:root{--bg:#111418;--card:#1b2027;--line:#2b333d;--fg:#f2f5f8;--muted:#9aa7b4;--accent:#4c9ffe;--red:#e5484d;--green:#30a46c}
*{box-sizing:border-box;-webkit-tap-highlight-color:transparent}
html,body{margin:0;padding:0;background:var(--bg);color:var(--fg);font:16px/1.45 system-ui,-apple-system,"Segoe UI",Roboto,sans-serif}
body{padding:env(safe-area-inset-top) env(safe-area-inset-right) env(safe-area-inset-bottom) env(safe-area-inset-left)}
header{display:flex;align-items:center;gap:12px;padding:12px 16px;border-bottom:1px solid var(--line);position:sticky;top:0;background:var(--bg);z-index:5}
#status{width:10px;height:10px;border-radius:50%;background:var(--muted)}
#status.on{background:var(--green)}
#status.red{background:var(--red)}
#devname{flex:1;font-weight:600;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
#role{font-size:13px;color:var(--muted);border:1px solid var(--line);border-radius:999px;padding:2px 8px}
nav{display:flex;gap:8px;padding:12px 16px;overflow-x:auto}
nav button{flex:0 0 auto;min-height:44px;min-width:64px;padding:10px 16px;border-radius:12px;border:1px solid var(--line);background:var(--card);color:var(--fg);font-size:15px}
nav button[aria-selected="true"]{background:var(--accent);border-color:var(--accent);color:#fff}
main{padding:0 16px 32px}
section{display:none}
section.active{display:block}
.grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(120px,1fr));gap:10px}
.card{background:var(--card);border:1px solid var(--line);border-radius:14px;padding:12px}
button.action{min-height:52px;border-radius:14px;border:1px solid var(--line);background:var(--card);color:var(--fg);font-size:16px;width:100%}
button.action.active{border-color:var(--accent);box-shadow:0 0 0 1px var(--accent) inset}
button.action.danger{border-color:var(--red)}
h2{font-size:14px;color:var(--muted);font-weight:600;margin:18px 0 8px;letter-spacing:.02em}
.scene{min-height:64px;text-align:left;padding:12px}
.scene.current{outline:2px solid var(--accent)}
.audio{display:flex;align-items:center;gap:10px;padding:8px 0}
.audio name{flex:1;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.audio input[type=range]{flex:1.2}
#tally{border-radius:14px;padding:18px;text-align:center;font-size:22px;font-weight:700;background:var(--card);border:2px solid var(--line)}
#tally.red{border-color:var(--red);color:var(--red)}
#tally.green{border-color:var(--green);color:var(--green)}
#preview{width:100%;border-radius:14px;background:#000;aspect-ratio:16/9;object-fit:contain;display:block}
.timers{display:flex;gap:12px;margin-top:12px;color:var(--muted);font-variant-numeric:tabular-nums}
#prompt{white-space:pre-wrap;font-size:var(--prompt-font,26px);line-height:1.5;max-height:52vh;overflow:auto;padding:12px;background:#0b0e11;border-radius:12px;border:1px solid var(--line)}
#prompt.mirror{transform:scaleX(-1)}
.row{display:flex;gap:10px;align-items:center;margin:10px 0}
.row label{min-width:64px;color:var(--muted)}
input[type=file]{width:100%;min-height:48px;color:var(--fg)}
.notice{color:var(--muted);font-size:14px;margin:10px 0}
.hidden{display:none}
</style>
</head>
<body>
<header><span id="status" aria-hidden="true"></span><span id="devname">…</span><span id="role"></span></header>
<nav id="tabs" role="tablist">
  <button role="tab" data-tab="remote" aria-selected="true">—</button>
  <button role="tab" data-tab="monitor" aria-selected="false">—</button>
  <button role="tab" data-tab="prompter" aria-selected="false">—</button>
  <button role="tab" data-tab="light" aria-selected="false">—</button>
  <button role="tab" data-tab="upload" aria-selected="false">—</button>
</nav>
<main>
<section id="remote" class="active">
  <h2 data-i18n="scenes"></h2><div id="scenes" class="grid"></div>
  <h2 data-i18n="audio"></h2><div id="audio"></div>
  <h2 data-i18n="record"></h2>
  <div class="grid">
    <button class="action" data-cmd="record.toggle" data-i18n="record"></button>
    <button class="action" data-cmd="replay.save" data-i18n="replay"></button>
    <button class="action" data-cmd="virtualcam.toggle" data-i18n="virtualcam"></button>
    <button class="action" data-cmd="studio.toggle" data-i18n="studio"></button>
    <button class="action danger" data-cmd="stream.start" data-confirm="1" data-i18n="stream"></button>
  </div>
  <p class="notice" id="roleNotice"></p>
</section>
<section id="monitor">
  <div id="tally">—</div>
  <div class="timers"><span id="recTime">00:00:00</span><span id="streamTime">00:00:00</span></div>
  <p class="notice" id="previewNotice"></p>
  <img id="preview" alt="" />
</section>
<section id="prompter">
  <div class="row"><button class="action" id="pPlay"></button><button class="action" id="pPause"></button><button class="action" id="pRestart"></button></div>
  <div class="row"><label data-i18n="speed"></label><input id="pSpeed" type="range" min="1" max="10" step="1"></div>
  <div class="row"><label data-i18n="font"></label><input id="pFont" type="range" min="16" max="72" step="2"></div>
  <div class="row"><label data-i18n="mirror"></label><input id="pMirror" type="checkbox"></div>
  <div id="prompt"></div>
</section>
<section id="light">
  <p class="notice" id="lightLink"></p>
</section>
<section id="upload">
  <p class="notice" data-i18n="pickFile"></p>
  <input id="file" type="file" accept="image/jpeg,image/png,video/mp4">
  <p class="notice" id="uploadNotice"></p>
</section>
</main>
<script>
"use strict";
const I18N = __I18N__;
const lang = document.documentElement.lang === "en" ? "en" : "zh";
const t = k => (I18N[k] ? I18N[k][lang === "en" ? 1 : 0] : k);
const $ = id => document.getElementById(id);
const state = { token: "", role: "viewer", snapshot: null, ws: null, es: null, previewTimer: 0, retry: 0 };

function el(tag, cls, text) { const e = document.createElement(tag); if (cls) e.className = cls; if (text !== undefined) e.textContent = text; return e; }
function applyI18n() {
  document.querySelectorAll("[data-i18n]").forEach(n => { n.textContent = t(n.getAttribute("data-i18n")); });
  $("tabs").querySelectorAll("button").forEach(b => { b.textContent = t(b.dataset.tab); });
  $("pPlay").textContent = t("play"); $("pPause").textContent = t("pause"); $("pRestart").textContent = t("restart");
  $("roleNotice").textContent = state.role === "viewer" ? t("viewer") : "";
}
function newId() { return (crypto.randomUUID && crypto.randomUUID()) || String(Date.now()) + Math.random().toString(16).slice(2); }
function canOperate() { return state.role === "operator" || state.role === "admin"; }

async function pair() {
  const secret = decodeURIComponent(location.hash.replace(/^#/, ""));
  if (!secret) { $("devname").textContent = t("pairFailed"); return false; }
  let name = localStorage.getItem("obshelper.deviceName");
  if (!name) { name = "phone"; try { name = (navigator.userAgent.match(/\(([^)]+)\)/) || [])[1] || name; } catch (e) {} name = name.slice(0, 40); localStorage.setItem("obshelper.deviceName", name); }
  const pairId = location.pathname.split("/m/")[1] || "";
  try {
    const r = await fetch("/api/pair", { method: "POST", headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ pairId, secret, deviceName: name, capabilities: ["remote", "preview", "prompter", "upload"] }) });
    const j = await r.json();
    if (!r.ok || !j.ok) { $("devname").textContent = t("pairFailed"); return false; }
    state.token = j.token; state.role = j.role || "viewer"; $("devname").textContent = j.deviceName || name;
    history.replaceState(null, "", location.pathname);
    applyI18n(); connect(); return true;
  } catch (e) { $("devname").textContent = t("pairFailed"); return false; }
}

function connect() {
  if (state.ws) { try { state.ws.close(); } catch (e) {} state.ws = null; }
  const proto = location.protocol === "https:" ? "wss:" : "ws:";
  let ws;
  try { ws = new WebSocket(proto + "//" + location.host + "/ws"); } catch (e) { startSse(); return; }
  state.ws = ws;
  ws.onopen = () => { ws.send(JSON.stringify({ type: "auth", token: state.token })); };
  ws.onmessage = ev => { let m; try { m = JSON.parse(ev.data); } catch (e) { return; } onMessage(m); };
  ws.onclose = () => { state.ws = null; $("status").className = ""; if (state.token) { state.retry = Math.min(10, state.retry + 1); setTimeout(connect, state.retry * 1000); } };
  ws.onerror = () => { try { ws.close(); } catch (e) {} };
}
function startSse() {
  if (state.es || !state.token) return;
  state.es = new EventSource("/api/events?token=" + encodeURIComponent(state.token));
  state.es.onmessage = ev => { try { onMessage(JSON.parse(ev.data)); } catch (e) {} };
  state.es.onerror = () => { if (state.es) { state.es.close(); state.es = null; } setTimeout(startSse, 3000); };
}
function onMessage(m) {
  if (m.type === "auth" && !m.ok) { state.token = ""; $("devname").textContent = t("pairFailed"); return; }
  if (m.type === "state" || m.type === "snapshot") { state.snapshot = m; render(m); }
  if (m.type === "result" && !m.ok) { if (m.code === "MOB_RATE_LIMITED") flash(t("rateLimited")); }
}
function flash(text) { $("previewNotice").textContent = text; setTimeout(() => { $("previewNotice").textContent = ""; }, 3000); }

function render(s) {
  $("status").className = s.tally === "off" ? "" : (s.tally === "red" ? "red" : "on");
  if (s.role) { state.role = s.role; $("role").textContent = t(s.role); }
  const scenes = $("scenes"); scenes.textContent = "";
  (s.scenes || []).forEach(name => {
    const b = el("button", "action scene" + (name === s.currentScene ? " current" : ""), name);
    b.disabled = !canOperate(); b.dataset.cmd = "scene.set"; b.dataset.scene = name; scenes.appendChild(b);
  });
  const audio = $("audio"); audio.textContent = "";
  (s.audio || []).forEach(a => {
    const row = el("div", "audio");
    row.appendChild(el("name", "", a.name));
    const mute = el("button", "action", a.muted ? "🔇" : "🔊");
    mute.style.maxWidth = "64px"; mute.disabled = !canOperate(); mute.dataset.cmd = "audio.mute"; mute.dataset.input = a.name; mute.dataset.muted = a.muted ? "0" : "1";
    row.appendChild(mute);
    const vol = document.createElement("input"); vol.type = "range"; vol.min = "-100"; vol.max = "0"; vol.step = "1"; vol.value = String(Math.round(a.volumeDb || 0));
    vol.disabled = !canOperate(); vol.dataset.cmd = "audio.volume"; vol.dataset.input = a.name;
    row.appendChild(vol); audio.appendChild(row);
  });
  $("tally").className = s.tally || "off";
  $("tally").textContent = s.tally === "red" ? t("tallyLive") : (s.tally === "off" ? t("offline") : t("tallyStandby"));
  $("recTime").textContent = s.recordTime || "00:00:00"; $("streamTime").textContent = s.streamTime || "00:00:00";
  document.querySelectorAll("button[data-cmd]").forEach(b => {
    const cmd = b.dataset.cmd;
    if (cmd === "record.toggle") b.classList.toggle("active", !!s.recording);
    if (cmd === "virtualcam.toggle") b.classList.toggle("active", !!s.virtualCam);
    if (cmd === "studio.toggle") b.classList.toggle("active", !!s.studioMode);
    if (cmd === "stream.start" || cmd === "stream.stop") b.classList.toggle("active", !!s.streaming);
  });
  const prompt = s.teleprompter || {};
  $("prompt").textContent = prompt.script || t("scriptEmpty");
  $("prompt").style.setProperty("--prompt-font", (prompt.fontSize || 26) + "px");
  $("prompt").classList.toggle("mirror", !!prompt.mirror);
  $("pSpeed").value = String(prompt.speed || 5); $("pFont").value = String(prompt.fontSize || 26); $("pMirror").checked = !!prompt.mirror;
  $("lightLink").textContent = "";
  const a = el("a", "", t("light")); a.href = location.pathname + "/light"; a.style.color = "var(--accent)"; a.style.fontSize = "20px"; a.style.display = "block"; a.style.padding = "24px 0";
  $("lightLink").appendChild(a);
}

async function send(cmd, params) {
  if (!state.token) return;
  const requestId = newId();
  const body = { requestId, command: cmd, params: params || {} };
  try {
    const r = await fetch("/api/command", { method: "POST", headers: { "Content-Type": "application/json", "Authorization": "Bearer " + state.token }, body: JSON.stringify(body) });
    const j = await r.json();
    if (!j.ok && j.code === "MOB_RATE_LIMITED") flash(t("rateLimited"));
    if (!j.ok && j.code === "MOB_ROLE_DENIED") flash(j.message || "");
  } catch (e) { flash(t("offlineHint")); }
}

document.addEventListener("click", ev => {
  const b = ev.target.closest("button[data-cmd]"); if (!b || b.disabled) return;
  const cmd = b.dataset.cmd;
  if (cmd === "scene.set") return send(cmd, { scene: b.dataset.scene });
  if (cmd === "audio.mute") return send(cmd, { input: b.dataset.input, muted: b.dataset.muted === "1" });
  if (cmd === "stream.start" || cmd === "stream.stop") {
    if (b.dataset.confirm === "1" && !confirm(t("confirmStream"))) return;
    return send(cmd, { confirm: true });
  }
  send(cmd);
});
document.addEventListener("change", ev => {
  const i = ev.target;
  if (i.dataset && i.dataset.cmd === "audio.volume") return send("audio.volume", { input: i.dataset.input, volumeDb: Number(i.value) });
  if (i.id === "pSpeed") return send("teleprompter.speed", { speed: Number(i.value) });
  if (i.id === "pFont") return send("teleprompter.font", { fontSize: Number(i.value) });
  if (i.id === "pMirror") return send("teleprompter.mirror", { mirror: !!i.checked });
});
$("pPlay").addEventListener("click", () => send("teleprompter.play"));
$("pPause").addEventListener("click", () => send("teleprompter.pause"));
$("pRestart").addEventListener("click", () => send("teleprompter.restart"));

document.querySelectorAll("#tabs button").forEach(b => b.addEventListener("click", () => {
  document.querySelectorAll("#tabs button").forEach(x => x.setAttribute("aria-selected", String(x === b)));
  document.querySelectorAll("main section").forEach(s => s.classList.toggle("active", s.id === b.dataset.tab));
  if (b.dataset.tab === "monitor") startPreview(); else stopPreview();
  if (b.dataset.tab === "prompter") send("state.get");
}));

function startPreview() {
  stopPreview();
  if (document.visibilityState !== "visible" || !state.token) return;
  const tick = async () => {
    try {
      const r = await fetch("/api/preview.jpg?fps=1", { headers: { "Authorization": "Bearer " + state.token } });
      if (r.ok) { const blob = await r.blob(); const url = URL.createObjectURL(blob); const img = $("preview"); const old = img.dataset.url; img.src = url; if (old) URL.revokeObjectURL(old); img.dataset.url = url; $("previewNotice").textContent = ""; }
      else { $("previewNotice").textContent = t("previewFailed"); }
    } catch (e) { $("previewNotice").textContent = t("previewFailed"); }
  };
  tick(); state.previewTimer = setInterval(tick, 1000);
}
function stopPreview() { if (state.previewTimer) { clearInterval(state.previewTimer); state.previewTimer = 0; } }
document.addEventListener("visibilitychange", () => {
  if (document.visibilityState === "visible") { connect(); if (document.querySelector("#tabs button[aria-selected=true]").dataset.tab === "monitor") startPreview(); }
  else { stopPreview(); $("previewNotice").textContent = t("previewOff"); }
});

$("file").addEventListener("change", async ev => {
  const file = ev.target.files && ev.target.files[0]; if (!file) return;
  if (file.size > 50 * 1024 * 1024) { $("uploadNotice").textContent = t("uploadFailed") + "50 MB"; return; }
  const fd = new FormData(); fd.append("file", file, file.name);
  $("uploadNotice").textContent = t("uploading");
  try {
    const r = await fetch("/api/upload", { method: "POST", headers: { "Authorization": "Bearer " + state.token }, body: fd });
    const j = await r.json();
    $("uploadNotice").textContent = r.ok && j.ok ? t("uploaded") + (j.fileName || "") : t("uploadFailed") + (j.code || r.status);
  } catch (e) { $("uploadNotice").textContent = t("uploadFailed") + "network"; }
  ev.target.value = "";
});

applyI18n();
pair().then(ok => { if (!ok) { $("status").className = ""; } });
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
