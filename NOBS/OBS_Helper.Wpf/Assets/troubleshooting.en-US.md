# OBS Streaming Guide & Troubleshooting

> Applies to V3.5.1 / X1031. Both use the same features; only the update channel differs.
> Start with “5-minute setup”. If a problem remains, use the in-app **Diagnose** page
> or connect OBS in **Console** to inspect live state.

## 0. 5-minute setup

1. **Connect OBS**: OBS → Tools → WebSocket Server Settings → enable the server, note the port and
   password; enter them in Settings → OBS connection. The badge turns green when connected.
2. **Phone remote**: Mobile → Remote → Generate pairing code → scan the QR code.
   Phone and PC must be on the same Wi-Fi. If the page does not load, allow `OBS_Helper.exe`
   on **private networks** in Windows Firewall.
3. **Apply a scene template**: Templates / Virtual Live → Room → choose a template → Apply to OBS.
   Then replace camera, microphone and avatar placeholders.
4. **Record 10 seconds**: Home → Simple recording → start → stop after 10 s → play the file.
   This catches encoder, disk and permission problems early.
5. **Teleprompter**: click Teleprompter in the top bar, paste your script, start scrolling.
   You can edit the script from the phone; the PC updates immediately after saving.

> Tip: in Settings → Appearance → Background you can use your own image. Since V3.5.0 the
> background covers the entire window.

## 1. Quick self-check

1. Use the **official stable OBS** from [obsproject.com](https://obsproject.com/zh-cn/download) or
   [GitHub releases](https://github.com/obsproject/obs-studio/releases/latest). OBS is free;
   download-site bundles and “paid versions” are scams. Check the signer
   `OBS Project Corporation` before installing.
2. Run OBS as administrator for capture / permission-sensitive sources.
3. Close heavy background apps (browser tabs, games, other recorders).
4. In Settings → Output, switch the encoder to `x264` / software to rule out GPU drivers.
5. Use Console to confirm whether OBS is actually streaming / recording.

## 2. Black screen / no signal

- **Display capture**: enable anti-cheat compatibility, or use Window / Game capture.
- **Game capture**: run OBS as administrator; use borderless windowed mode; disable overlays
  (Discord, Steam, Overwolf).
- **Capture card**: match resolution / frame rate in OBS; try `1920x1080, 60fps` first.
- **Browser source**: use a URL instead of a local file, or disable hardware acceleration.
- **Avatar missing**: make sure Live2D / VTube Studio is publishing. Without NDI, capture the
  VTube Studio window, or use Virtual Live → Avatar → one-click Live2D setup.

## 3. Encoding overload / stutter

- Lower output resolution / frame rate (e.g. `1280x720, 30fps`).
- Lower bitrate: 720p `2500–4000 kbps`, 1080p `4500–6000 kbps`.
- Prefer hardware encoders (NVENC / AMD / Intel QSV).
- Disable dynamic bitrate / repeated retry in advanced settings.
- Check Task Manager: if CPU / GPU is saturated, lower quality further.

```text
Suggested starting point for a mid-range PC:
Output: 1280x720, 30fps
Video: NVENC, 4000 kbps, keyframe interval 2s
Advanced: NV12, color space 709
```

## 4. Audio drift / crackling

- Use **48 kHz** everywhere (OBS and Windows sound settings).
- Check desktop audio and microphone separately; crackling usually means sample-rate mismatch
  or too-small buffering.
- For phone cameras, set an audio sync offset (-500 to 500 ms) in Mobile → Camera.
- High stream latency is a network problem, not audio.

## 5. Stream fails / times out

- Copy server URL and stream key from the platform; never type them by hand.
- Some platforms (e.g. Douyin) no longer expose RTMP for third-party apps; use their official
  companion app and feed it OBS via virtual camera / NDI.
- Prefer RTMPS; port 1935 is often blocked.
- Allow OBS through firewall / antivirus; test on a phone hotspot.
- Check `stream` state and connection time in Console.

```text
Common errors:
- Failed to connect to server  -> wrong URL / key, or server unreachable
- Timed out                    -> unstable network, or firewall blocks 1935 / RTMPS
- Authentication failed        -> wrong stream key
```

## 6. Plugin catalog & GitHub accelerator (new in V3.5.0)

The catalog, search and downloads all use GitHub. On some networks requests hang or fail.

1. Open Plugins; a card at the top explains GitHub acceleration.
2. Click **Download Watt Toolkit (Steam++)**, install it and enable **GitHub acceleration**.
3. Return and click **I enabled it — check again**; continue when it says GitHub is reachable.
4. A third-party accelerator is optional; your own proxy works too. This app never bundles or
   installs one.

**Installed plugins not shown?** Since V3.5.0 the list merges:

- **Managed by this app**: rollback / uninstall, source and SHA-256 recorded;
- **Detected locally**: plugins you added to OBS yourself, read-only and never modified.

If both are empty, verify the OBS folder in Settings → OBS path.

## 7. Phone remote & phone camera (reworked in V3.5.0)

### Remote

- PC and phone on the same Wi-Fi. The server binds a private address only, is off by default,
  and can be revoked at any time.
- “Generate pairing code” starts the server; the QR code is valid for 120 seconds.
- If the phone cannot open the page, allow `OBS_Helper.exe` on private networks; guest / hotel
  Wi-Fi often uses client isolation — use a phone hotspot instead.

### Phone as camera

Browsers cannot call the camera over plain HTTP on a LAN (secure-context restriction), so use
an SRT / RTMP publishing app such as Larix Broadcaster:

1. Mobile → Camera → **Download and install** the media server (mediamtx, MIT, ~28 MB).
2. Click **Start camera link** and note the SRT / RTMP URL.
3. Paste the full URL (including the random key) into the phone app and start publishing.
4. If OBS is connected, the app creates a “Phone camera” scene/source automatically.
   You can also start the media server before OBS; the source is added after OBS connects.
5. No picture? Confirm the phone is publishing, the URL is complete, the firewall allows
   `mediamtx.exe`, and the port is free.

> Windows 7 does not support the Wi-Fi camera path; use a USB capture card instead.

## 8. Virtual live templates & Live2D (expanded in V3.5.0)

- Available: chat, gaming, karaoke, study companion, duo collab, hand camera, vertical,
  anniversary, and starting/BRB/ending pack.
- Avatar fallback order: **NDI → Spout → camera → window capture**.
- Without NDI, capture the VTube Studio window and crop it in OBS.
- Chat / frame overlays are local `127.0.0.1` pages with no network access. Replace the URL
  with your own chat widget if needed.

## 9. Teleprompter (phone editing is new in V3.5.0)

- Open from the top bar; supports mirror, font size, speed, restart and click-through.
- On the phone’s Prompt tab, edit the script and press **Save to PC**; the PC updates instantly.
  **Refresh from PC** discards unsaved phone changes.
- Previous scripts are backed up to `%LocalAppData%\OBS_Helper\teleprompter\backup-*.txt`
  (latest 5 kept).
- Scene changes can scroll to `## Scene name` sections automatically.

## 10. Log analysis

1. Open Logs.
2. Reproduce the problem, then export OBS → Help → Log Files → Last Log and drop it here.
3. The app sanitizes secrets and reports quantified latencies / anomalies for Diagnose.

## 11. Console

Console shows current scene and sources, audio levels / mute / volume,
record / stream / virtual camera state, and live stats (FPS, bitrate, drops, CPU).
Write actions ask for confirmation.

## 12. Security & privacy

- Offline-first: no outbound requests unless you enable AI or cloud models.
- Cloud keys are protected with Windows DPAPI.
- Logs are sanitized (keys, paths, tokens, IPs) before analysis.
- Phone remote binds private addresses only, is off by default, uses random ports and
  one-time pairing codes. Phones never receive the OBS password, stream key or API key.

## 13. Still stuck?

- Use Diagnose with the observed symptoms.
- OBS knowledge base: https://obsproject.com/kb
- Log analyzer: https://obsproject.com/analyzer
- When asking in the community, include symptoms + logs; the analysis summary helps.

---

*Provided with OBS Helper V3.5.1 / X1031. Based on OBS documentation, community experience and
in-project testing.*
