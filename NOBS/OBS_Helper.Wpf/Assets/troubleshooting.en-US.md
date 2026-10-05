# OBS Streaming Troubleshooting Guide

> This guide collects troubleshooting approaches and solutions for the most common OBS problems. If you run into an issue, first run through the "Quick Self-Check" below; if it is still not resolved, use the in-app "🩺 Diagnostics" for smart analysis, or go to the "🎛️ Console" to connect to OBS and check its status live.

## 1. Quick Self-Check First

Most issues can be solved by checking the following in order:

1. Confirm OBS is the **latest official stable release** (not a beta, not a modified build) — get the installer only from the [official download page](https://obsproject.com/zh-cn/download) or the [GitHub releases page](https://github.com/obsproject/obs-studio/releases/latest). OBS is completely free, requires no registration or payment to download, and has no paid version. Fake packages in search-engine ads and "software download sites" often bundle installers and trojans (such as the Silver Fox family); before installing, verify the digital signature is `OBS Project Corporation`.
2. Run OBS **as administrator** (required for some capture and permission scenarios).
3. Close resource-hungry background programs (many browser tabs, games, other screen-recording software).
4. In "Settings → Output", switch the **encoder** back to `x264` / `software (CPU)` first to rule out graphics driver issues.
5. Open the "🎛️ Console" to confirm whether OBS is actually streaming / recording.

## 2. Black Screen / No Signal

- **Display Capture black screen**: right-click the source → "Properties" → check "Use anti-cheat compatibility hook", or switch to "Window Capture / Game Capture".
- **Game Capture black screen**: run OBS as administrator; set the game to "borderless windowed"; turn off in-game overlays (Discord, Steam, Overwolf).
- **Capture card black screen**: make sure the resolution / frame rate matches the OBS source settings; try `1920x1080, 60fps` first.
- **Browser Source black screen**: turn off "Local File" and use a URL instead, or uncheck "Hardware Acceleration" in the source properties.

> Tip: most black-screen issues relate to "permissions / anti-cheat / hardware acceleration" — check these three first.

## 3. Encoding Overload / Stutter and Dropped Frames

Symptom: the status bar shows **dropped frames (rendering / encoding)**, and the picture stutters.

- Lower the **Output resolution** and **frame rate**: e.g. `1280x720, 30fps`.
- Lower the **bitrate**: about `2500–4000 kbps` for 720p and `4500–6000 kbps` for 1080p.
- Choose **hardware encoding** for the encoder (NVENC / AMD / Apple VT) to reduce CPU load.
- In "Settings → Advanced", turn off extras such as "Dynamic Bitrate" and "Reconnect".
- Use Task Manager to check whether the CPU / GPU is maxed out; if so, keep lowering the quality.

```text
Recommended starting config for mid-range PCs:
Output: 1280x720, 30fps
Video: encoder NVENC, bitrate 4000, keyframe interval 2s
Advanced: Color Format NV12, Color Space 709
```

## 4. Audio/Video Desync / Crackling

- Set the sample rate to a uniform **48kHz** (keep OBS and the system sound settings consistent).
- Check desktop audio and microphone separately; crackling is usually caused by a **sample rate mismatch** or a **buffer that is too small**.
- "Desync" caused by high streaming latency is usually a **network** issue; see the next section.

## 5. Audio Issues

- **No microphone sound**: make sure the source is set to the correct device; set the system "Sound → Recording" device as default; unmute.
- **Echo / feedback**: do not monitor "Desktop Audio" and "Microphone" at the same time; use headphones instead.
- **Missing desktop audio**: re-add "Audio Output Capture" in the sources.

## 6. Streaming Failure / Connection Timeout

```text
Common errors:
- Failed to connect to server  → wrong address / key, or the server is unreachable
- Timed out                    → unstable network, or a firewall blocking 1935 / RTMPS
- Authentication failed        → wrong Stream Key
```

- Verify the **server address** and the **Stream Key** (copy them from the streaming platform's streaming settings; do not type them by hand).
- **Platform policy**: some platforms (such as Douyin) no longer provide an OBS / third-party RTMP streaming entry point, and officially only support their own live companion app. In this case it is not a network problem; just use the official tool to go live instead. (To get the OBS picture, forward it to the live companion app via Virtual Camera / NDI.)
- Prefer **RTMPS** (the platform default) to avoid the 1935 port being blocked.
- Disable the system firewall / antivirus restrictions on OBS, or switch networks (use a phone hotspot to troubleshoot).
- Use the "🎛️ Console" to check the `stream` status and connection time.

## 7. Locating the Root Cause with "Log Analysis"

1. Open "📜 Logs".
2. After reproducing the issue in OBS, export it from OBS "Help → Log Files → Previous Log File" and drag it into this app.
3. The app will automatically redact and analyze it, giving a **quantified lag ratio** and anomalies, which you can send straight to "🩺 Diagnostics".

## 8. Checking Live with the "Console"

The "🎛️ Console" connects to OBS via obs-websocket, letting you view:

- the current **Scene** and sources
- **audio** levels, mute state, and volume
- **recording / streaming / Virtual Camera** status
- live **Stats** (frame rate, bitrate, dropped frames, CPU)

> Write operations (switching scenes, starting / stopping the stream, etc.) require a second confirmation to avoid accidental clicks.

## 9. Security and Privacy Tips

- This app is **offline-first** and does not connect to the internet by default; it only makes external requests if you manually enable "Free AI" or "Cloud LLM" in "Settings".
- Cloud keys are **encrypted and stored by the desktop shell** (Windows DPAPI / macOS Keychain) and never enter the app's memory.
- Logs are **automatically redacted** (keys, paths, tokens, IPs, etc.) before analysis, so they are safe to use.

## 10. Still Can Not Solve It?

- Open "🩺 Diagnostics", enter the symptoms, and get step-by-step suggestions.
- Check the official documentation:
  - OBS Knowledge Base (Chinese): https://obsproject.com/zh-cn/kb
  - Log Analyzer: https://obsproject.com/analyzer
- Ask on the official OBS forum or community with the "symptoms + log"; attaching the log analysis conclusion will get you help faster.

---

*This guide is provided with OBS Helper 1.6.0; its content is compiled from the official OBS documentation and community experience.*
