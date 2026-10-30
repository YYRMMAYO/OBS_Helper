namespace OBS_Helper.Wpf.Services.Live;

/// <summary>
/// 内置 overlay 页面（V3.3.0）。
///
/// 为什么把 HTML 放在代码里而不是 Assets/*.html：
/// <list type="bullet">
///   <item>单文件发布 / 便携版不需要额外解包资源目录，OBS 里也不会出现「文件不见了」；</item>
///   <item>页面是**纯本地静态资源**（无远程请求、无 CDN、无字体下载），CSP 能收紧到 <c>default-src 'none'</c>；</item>
///   <item>中英文案按 <c>?lang=</c> 切换，不需要为两套语言各维护一份页面文件。</item>
/// </list>
///
/// 页面只允许内置这几张；未知页面一律 404（见 <see cref="RoomOverlayCore.IsKnownPage"/>）。
/// </summary>
public static class RoomOverlayAssets
{
    /// <summary>按页面名取 HTML；未知页面返回 null。</summary>
    public static string? TryGet(string page)
    {
        var name = RoomOverlayCore.PageNameOf(page);
        return name.ToLowerInvariant() switch
        {
            "chat.html" => ChatPage,
            "frame.html" => FramePage,
            "brb.html" => BrbPage,
            "ending.html" => EndingPage,
            "timer.html" => TimerPage,
            _ => null
        };
    }

    private const string ChatPage = """
<!doctype html>
<html lang="zh">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Chat</title>
<style>
  :root { --bg: rgba(255,255,255,0.86); --fg: #2b2540; --muted: #6c6780; --accent: #6b8cff; --line: rgba(43,37,64,0.10); }
  [data-theme="mint"] { --bg: rgba(240,255,250,0.88); --accent: #2fbf9b; --fg: #163b34; --muted: #4c7468; }
  [data-theme="night"] { --bg: rgba(22,20,36,0.86); --fg: #ece9ff; --muted: #a8a3c7; --accent: #a98cff; --line: rgba(255,255,255,0.10); }
  [data-theme="star"] { --bg: rgba(30,26,52,0.84); --fg: #f4efff; --muted: #b9b0d8; --accent: #ff9ad5; --line: rgba(255,255,255,0.10); }
  html, body { margin: 0; height: 100%; background: transparent; overflow: hidden; }
  body { font-family: "Segoe UI", "Microsoft YaHei UI", system-ui, sans-serif; display: flex; align-items: flex-end; }
  .panel { width: 100%; height: 100%; display: flex; flex-direction: column; background: var(--bg);
           border-radius: 18px; box-shadow: 0 14px 34px rgba(20,16,40,0.20); border: 1px solid var(--line); overflow: hidden; }
  .head { display: flex; align-items: center; gap: 8px; padding: 12px 16px 10px; border-bottom: 1px solid var(--line); }
  .dot { width: 8px; height: 8px; border-radius: 50%; background: var(--accent); }
  .title { font-weight: 700; font-size: 15px; color: var(--fg); letter-spacing: .04em; }
  .body { flex: 1; padding: 12px 16px; overflow: hidden; }
  .msg { display: flex; gap: 8px; margin-bottom: 10px; font-size: 13.5px; line-height: 1.45; color: var(--fg); }
  .who { font-weight: 700; color: var(--accent); white-space: nowrap; }
  .hint { margin-top: 10px; padding: 10px 12px; border-radius: 12px; border: 1px dashed var(--line);
          color: var(--muted); font-size: 12px; line-height: 1.5; }
  .foot { padding: 9px 16px 12px; color: var(--muted); font-size: 11.5px; border-top: 1px solid var(--line); }
</style>
</head>
<body>
  <div class="panel">
    <div class="head"><span class="dot"></span><span class="title" data-i18n="chat">聊天</span></div>
    <div class="body" id="body">
      <div class="msg"><span class="who">Aki</span><span>今天的直播开始啦～</span></div>
      <div class="msg"><span class="who">Momo</span><span>画风好可爱！</span></div>
      <div class="msg"><span class="who">Yuu</span><span>打卡 🎉</span></div>
      <div class="hint" data-i18n="hint">这是占位聊天框。在 OBS 里把本来源的「URL」换成你自己的聊天 widget 地址即可。</div>
    </div>
    <div class="foot" data-i18n="foot">本地占位页 · 不连接任何平台账号</div>
  </div>
<script>
  const p = new URLSearchParams(location.search);
  const theme = p.get('theme');
  if (theme) document.documentElement.dataset.theme = theme;
  if (p.get('lang') === 'en') {
    const en = { chat: 'Chat', hint: 'Placeholder chat. Replace this source URL in OBS with your own chat widget.', foot: 'Local placeholder · no platform account is used' };
    document.querySelectorAll('[data-i18n]').forEach(el => { el.textContent = en[el.dataset.i18n] || el.textContent; });
    document.documentElement.lang = 'en';
  }
</script>
</body>
</html>
""";

    private const string FramePage = """
<!doctype html>
<html lang="zh">
<head>
<meta charset="utf-8">
<title>Frame</title>
<style>
  :root { --a: #ffd7e6; --b: #ffeccc; --c: #d9ecff; --ink: #6a5a7a; }
  [data-theme="mint"] { --a: #c9f4e4; --b: #e7fbf3; --c: #cfe9ff; --ink: #3f6b60; }
  [data-theme="night"] { --a: #3a3566; --b: #241f45; --c: #4d3f7a; --ink: #b9b0e8; }
  [data-theme="star"] { --a: #4c3a6e; --b: #2a2147; --c: #6a4a8a; --ink: #ffd9f2; }
  [data-theme="cocoa"] { --a: #f0d9c3; --b: #fff3e6; --c: #e6cdb4; --ink: #7a5a44; }
  html, body { margin: 0; height: 100%; background: transparent; overflow: hidden; }
  body { position: relative; }
  .edge { position: absolute; inset: 18px; border-radius: 26px; pointer-events: none;
          border: 6px solid var(--a); box-shadow: 0 0 0 2px var(--b) inset, 0 0 36px rgba(255,255,255,0.05); }
  .glow { position: absolute; border-radius: 50%; filter: blur(26px); opacity: .55; }
  .g1 { width: 320px; height: 320px; left: -90px; top: -90px; background: var(--a); }
  .g2 { width: 280px; height: 280px; right: -70px; bottom: -80px; background: var(--c); }
  .g3 { width: 220px; height: 220px; right: 12%; top: -70px; background: var(--b); }
  .corner { position: absolute; width: 64px; height: 64px; border-radius: 20px; background: var(--b); opacity: .9; }
  .c1 { left: 4px; top: 4px; border-bottom-right-radius: 28px; }
  .c2 { right: 4px; top: 4px; border-bottom-left-radius: 28px; }
  .c3 { left: 4px; bottom: 4px; border-top-right-radius: 28px; }
  .c4 { right: 4px; bottom: 4px; border-top-left-radius: 28px; }
  .title { position: absolute; left: 50%; bottom: 34px; transform: translateX(-50%);
           font: 700 18px/1 "Segoe UI", "Microsoft YaHei UI", sans-serif; letter-spacing: .18em;
           color: var(--ink); opacity: .85; }
</style>
</head>
<body>
  <div class="glow g1"></div><div class="glow g2"></div><div class="glow g3"></div>
  <div class="edge"></div>
  <div class="corner c1"></div><div class="corner c2"></div><div class="corner c3"></div><div class="corner c4"></div>
  <div class="title" id="title">ON AIR</div>
<script>
  const p = new URLSearchParams(location.search);
  if (p.get('theme')) document.documentElement.dataset.theme = p.get('theme');
  if (p.get('lang') === 'en') { document.documentElement.lang = 'en'; }
</script>
</body>
</html>
""";

    private const string BrbPage = """
<!doctype html>
<html lang="zh">
<head>
<meta charset="utf-8">
<title>BRB</title>
<style>
  :root { --bg1: #fff1f6; --bg2: #e8f1ff; --fg: #4a3f5c; --accent: #ff9ec4; }
  [data-theme="mint"] { --bg1: #e6fff7; --bg2: #e8f4ff; --fg: #2f5a50; --accent: #58d6b0; }
  [data-theme="night"] { --bg1: #1d1936; --bg2: #2b2350; --fg: #efeaff; --accent: #b39cff; }
  [data-theme="star"] { --bg1: #241a3d; --bg2: #3a2758; --fg: #ffe9fb; --accent: #ff9ad5; }
  html, body { margin: 0; height: 100%; overflow: hidden; }
  body { display: grid; place-items: center; background: radial-gradient(circle at 30% 20%, var(--bg1), var(--bg2) 70%);
         font-family: "Segoe UI", "Microsoft YaHei UI", system-ui, sans-serif; color: var(--fg); }
  .card { text-align: center; }
  .big { font-size: 64px; font-weight: 800; letter-spacing: .22em; }
  .sub { margin-top: 12px; font-size: 20px; opacity: .8; letter-spacing: .12em; }
  .dots { margin-top: 26px; display: flex; gap: 10px; justify-content: center; }
  .d { width: 12px; height: 12px; border-radius: 50%; background: var(--accent); animation: p 1.2s infinite ease-in-out; }
  .d:nth-child(2) { animation-delay: .15s } .d:nth-child(3) { animation-delay: .3s }
  @keyframes p { 0%,100% { opacity: .25; transform: translateY(0) } 50% { opacity: 1; transform: translateY(-6px) } }
</style>
</head>
<body>
  <div class="card">
    <div class="big" id="big">BRB</div>
    <div class="sub" id="sub">马上回来</div>
    <div class="dots"><span class="d"></span><span class="d"></span><span class="d"></span></div>
  </div>
<script>
  const p = new URLSearchParams(location.search);
  if (p.get('theme')) document.documentElement.dataset.theme = p.get('theme');
  if (p.get('lang') === 'en') { document.documentElement.lang = 'en'; document.getElementById('sub').textContent = 'Be right back'; }
</script>
</body>
</html>
""";

    private const string EndingPage = """
<!doctype html>
<html lang="zh">
<head>
<meta charset="utf-8">
<title>Ending</title>
<style>
  :root { --bg1: #fff6ea; --bg2: #ffe6f0; --fg: #4a3b46; --accent: #ffb3cf; }
  [data-theme="mint"] { --bg1: #effff9; --bg2: #e3f3ff; --fg: #28524a; --accent: #66d9b6; }
  [data-theme="night"] { --bg1: #1a1730; --bg2: #2a1f45; --fg: #f1ecff; --accent: #c4a7ff; }
  [data-theme="star"] { --bg1: #2a1a3d; --bg2: #452a58; --fg: #ffeafb; --accent: #ffa7dd; }
  html, body { margin: 0; height: 100%; overflow: hidden; }
  body { display: grid; place-items: center; background: radial-gradient(circle at 70% 30%, var(--bg1), var(--bg2) 72%);
         font-family: "Segoe UI", "Microsoft YaHei UI", system-ui, sans-serif; color: var(--fg); }
  .wrap { text-align: center; }
  .big { font-size: 58px; font-weight: 800; letter-spacing: .14em; }
  .sub { margin-top: 14px; font-size: 19px; opacity: .78; letter-spacing: .1em; }
  .line { width: 120px; height: 4px; border-radius: 2px; background: var(--accent); margin: 26px auto 0; }
</style>
</head>
<body>
  <div class="wrap">
    <div class="big" id="big">感谢观看</div>
    <div class="sub" id="sub">下次直播见</div>
    <div class="line"></div>
  </div>
<script>
  const p = new URLSearchParams(location.search);
  if (p.get('theme')) document.documentElement.dataset.theme = p.get('theme');
  if (p.get('lang') === 'en') {
    document.documentElement.lang = 'en';
    document.getElementById('big').textContent = 'Thanks for watching';
    document.getElementById('sub').textContent = 'See you next stream';
  }
</script>
</body>
</html>
""";

    private const string TimerPage = """
<!doctype html>
<html lang="zh">
<head>
<meta charset="utf-8">
<title>Timer</title>
<style>
  :root { --fg: #4a3b56; --accent: #ffb3cf; }
  html, body { margin: 0; height: 100%; background: transparent; overflow: hidden; }
  body { display: grid; place-items: center; font-family: "Segoe UI", "Microsoft YaHei UI", system-ui, sans-serif; color: var(--fg); }
  .box { text-align: center; padding: 18px 28px; border-radius: 22px; background: rgba(255,255,255,.82);
         box-shadow: 0 12px 30px rgba(40,26,50,.18); }
  .t { font-size: 54px; font-weight: 800; font-variant-numeric: tabular-nums; letter-spacing: .04em; }
  .s { margin-top: 6px; font-size: 14px; opacity: .7; letter-spacing: .16em; }
</style>
</head>
<body>
  <div class="box"><div class="t" id="t">00:00</div><div class="s" id="s">开播倒计时</div></div>
<script>
  const p = new URLSearchParams(location.search);
  const minutes = Math.max(0, Math.min(600, parseInt(p.get('minutes') || '10', 10) || 10));
  const end = Date.now() + minutes * 60000;
  const fmt = n => String(n).padStart(2, '0');
  function tick() {
    const left = Math.max(0, end - Date.now());
    const m = Math.floor(left / 60000), s = Math.floor(left % 60000 / 1000);
    document.getElementById('t').textContent = fmt(m) + ':' + fmt(s);
    if (left > 0) setTimeout(tick, 250);
  }
  if (p.get('lang') === 'en') { document.documentElement.lang = 'en'; document.getElementById('s').textContent = 'Starting soon'; }
  tick();
</script>
</body>
</html>
""";
}
