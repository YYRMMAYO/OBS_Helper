<div align="center">

<img src="assets/banner.svg" alt="OBS Helper — an OBS troubleshooting tool for streamers who are just getting started" width="100%"/>

**OBS Helper · OBS帮助助手** — an OBS troubleshooting / recording / streaming assistant for streamers who are just getting started (native Windows WPF)

**Current release: V3.5.0** (stable) · the first X-series release `X1031` is available too

**Focus of this release**: **Phone Link** — pair by QR code and turn your phone into a second screen, a second camera and a second microphone; plus chat aggregation, local live captions, review/clips, multi-platform relay, MIDI/lighting and team collaboration. See [`NOBS/RELEASE_NOTES_v3.4.0.md`](NOBS/RELEASE_NOTES_v3.4.0.md) for details.

[![CI](https://github.com/YYRMMAYO/OBS_Helper/actions/workflows/ci.yml/badge.svg)](https://github.com/YYRMMAYO/OBS_Helper/actions/workflows/ci.yml)
[![Platform](https://img.shields.io/badge/Platform-Windows_7_SP1%2B_%2F_10_%2F_11-0078D6.svg)]()
[![.NET](https://img.shields.io/badge/.NET-10_%2F_6-512BD4.svg)]()
[![Stack](https://img.shields.io/badge/Stack-WPF_%2F_C%23-239120.svg)]()
[![Release](https://img.shields.io/badge/Release-3.5.0-38bdf8.svg)](https://github.com/YYRMMAYO/OBS_Helper/releases)
[![OBS](https://img.shields.io/badge/OBS-32.2.2-302E31.svg)](https://github.com/obsproject/obs-studio/releases)
[![offline-first](https://img.shields.io/badge/offline--first-2ea44f.svg)]()
[![Incremental Update](https://img.shields.io/badge/Incremental%20Update-2.5MB-7dd3fc.svg)]()
[![Tests](https://img.shields.io/badge/Tests-1729%20passed-2ea44f.svg)]()
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

**English** · [简体中文](README.md)

</div>

> **The default branch of this repository is `master` (stable channel)**: settled features, full regression and release verification — the right choice for everyday streaming and recording.
> **The DEV test branch was deleted in V3.3.0**: stable releases go straight from `master`; historical `01`/`02`/`03` tags remain on the Releases page, but no new DEV builds are produced.

**OBS Helper** (known as 「OBS 排障助手」 before V2.9.6 — the same app on the same upgrade path) is an OBS Studio troubleshooting and recording assistant for streamers who are just getting started: **it works fully offline** — the 212-entry knowledge base, the troubleshooting guide and the log-analysis rules are all embedded in the app, so they are searchable without a connection; once connected to the local OBS you also get one-click health checks, remote control of scenes / recording / streaming, and recording-environment deployment. **Since V2.9.4** the whole journey from "configure OBS" to "start recording" fits into one card on the home page; **since V3.1.0** OBS can also be launched and recording started with one click when it is not running; **V3.1.1** completed the teleprompter's manual up/down movement and window nudging; **V3.3.0** adds the **Virtual Live** workspace (Live2D avatar · 6 room templates · 16-platform go-live guidance), one-click plugin install / rollback / GitHub search, and Accessibility / Elder modes; **V3.4.0** brings the phone into the streaming workflow: QR remote / monitor / teleprompter / fill light / file upload, phone as a Wi-Fi camera and microphone, plus chat aggregation, local live captions, review/clips, multi-platform relay, MIDI/lighting and team collaboration.

This is the **native WPF rewrite** of the original Blazor WebAssembly + WebView2 version (source in [`NOBS/`](NOBS/)): feature-for-feature parity, but the browser engine is gone — a cold start goes straight to the window, it ships as a self-contained single folder, and it needs neither WebView2 nor a .NET runtime install. **Since V2.9.2 the UI switches between Chinese and English instantly**, and **since V2.9.3 even the bundled offline content is bilingual** (the 212-entry knowledge base / 57-entry plugin catalog / 12 scene templates / the whole troubleshooting guide are translated, and follow the language switch immediately); **since V2.2 there is a built-in plugin square** (57 curated plugins plus a read-only local health check; the v1.4 catalog has been fully re-audited); **since V2.1 updates are incremental and the knowledge base updates independently**.

> **Compatibility**: the main build targets **Windows 10 / 11** (.NET 10); a separate **Windows 7 SP1 compatible build** (.NET 6, the last .NET version that supports Win7) ships as both installer and portable ZIP in Releases — pick whichever matches your system.
>
> **OBS version alignment**: currently aligned with **OBS Studio 32.2.2** (the stable version at the time of this release) — log parsing, config keys, the obs-websocket protocol and the local plugin scan (including the "one folder per plugin" plugin root added in 32.x) have all been verified against a real 32.2.2 environment. See the [V2.9.3 release notes](https://github.com/YYRMMAYO/OBS_Helper/releases/tag/V2.9.3) for details.

> **Where the docs live**: this README at the repository root is the **complete user-facing guide** (features · usage · diagnostics · privacy · installation · build); developer- and history-oriented details (quality baseline, per-release notes, code inventory, architecture overview) live in [`NOBS/README.md`](NOBS/README.md). Read [`docs/BRANCH_AND_DOC_RULES.md`](docs/BRANCH_AND_DOC_RULES.md) before editing a README or release notes.

---

**Contents**: [Start here](#start-here) · [5-minute quick start](#5-minute-quick-start) · [Highlights at a glance](#highlights-at-a-glance) · [Features](#features) · [Smart diagnostics engine](#smart-diagnostics-engine) · [Release channels: stable vs DEV](#release-channels-stable-vs-dev) · [What's new in V3.5.0](#whats-new-in-v350) · [X1031](#x1031) · [What's new in V3.3.0](#whats-new-in-v330) · [What's new in V3.1.1](#whats-new-in-v311) · [Version history](#version-history) · [Privacy & security](#privacy--security) · [Installation & updates](#installation--updates) · [Build](#build) · [Project structure](#project-structure) · [Quality baseline & known limits](#quality-baseline--known-limits) · [Promo video](#promo-video) · [Donate](#donate) · [License](#license)

---

## Start here

| What you want to do | Where to go |
| --- | --- |
| Download the installer / portable build, read release notes | [Releases](https://github.com/YYRMMAYO/OBS_Helper/releases) |
| See what it can do and how to use it first | Read on: [5-minute quick start](#5-minute-quick-start) and [Features](#features) |
| Read the developer-oriented full documentation (quality baseline · release history · code inventory) | [`NOBS/README.md`](NOBS/README.md) |
| Full documentation in Chinese | [`README.md`](README.md) · [`NOBS/README.md`](NOBS/README.md) |
| What changed in this release | [`NOBS/RELEASE_NOTES_v3.4.0.md`](NOBS/RELEASE_NOTES_v3.4.0.md) |
| DEV test channel numbering and update rules | [`NOBS/docs/DEV_CHANNEL.md`](NOBS/docs/DEV_CHANNEL.md) |
| Before editing docs: which documents belong on master, which on dev | [`docs/BRANCH_AND_DOC_RULES.md`](docs/BRANCH_AND_DOC_RULES.md) |
| Report a bug / disclose a security vulnerability | [Issues](https://github.com/YYRMMAYO/OBS_Helper/issues) · [SECURITY.md](SECURITY.md) |
| License | [LICENSE](LICENSE) (MIT) |

## 5-minute quick start

1. **Install it** — download the installer or the portable build from [Releases](https://github.com/YYRMMAYO/OBS_Helper/releases). The portable build unzips and runs, .NET runtime included; the first step of the installer wizard asks for a language (Simplified Chinese / English, Chinese by default). On Windows 7 SP1, pick the download whose file name contains `_win7`.
2. **Connect OBS** — enter the OBS WebSocket port and password in the connection card on the **Console** page (the main build defaults to 4455 / obs-websocket 5.x; the Win7-compatible build defaults to 4444 / the legacy 4.x protocol). Open *Tools → WebSocket Server Settings* in OBS to see the port and password; the password is stored only on this machine, **double-encrypted**. Once connected, the badge in the top bar turns green and pages such as the console, health check and recording really come alive.
3. **Record a clip** — pick one of the presets in the **simple recording** card on the home page (Quick recording / Meeting / online class / High-bitrate game) and a single click does it all: pre-flight check → back up the current config → apply the recording settings → (launch OBS and wait for it to become ready if it is not running) → start recording. While recording you see the elapsed time and the estimated time still available; when you stop, the file size is reported and you can open the folder or convert to MP4 in one click.
4. **When something breaks, look it up first** — tap **Search issues** on the home page, or search as you type on the *Search* page, or describe the symptom in plain words with **Ask me anything**; you can also run a one-click health check on the connected OBS from the *Diagnose* page, or analyze an OBS log directly (offline, sanitized first). Health-check and log-analysis results can both be exported as a Markdown report, and when you report a problem you can copy the error materials in one click.
5. **Control things while you stream** — switch scenes, show or hide sources, mute audio, and start / stop recording and streaming from the *Console*; minimize the window to the tray, bring up the mini window, or use the global hotkeys `Ctrl+Alt+R/S/C/V/K/M/O` at any time. With the teleprompter enabled, `Ctrl+Alt+T` shows / hides it and `Ctrl+Alt+P` plays / pauses.

> Apart from the free AI / cloud diagnostics engine, everything else (knowledge base, log analysis, health check, plugin catalog, templates, teleprompter, wallpapers) **works fully offline**; the app only goes online when you start an AI diagnostic yourself, and logs are sanitized before the request is sent.

## Highlights at a glance

| | |
|---|---|
| **212 issues, fully offline** | **212 issues built in** (knowledge base v2.2), organised by category; each one contains **symptoms / causes / step-by-step fixes / tips / related issues**; steps can be checked off and progress is remembered automatically. **The knowledge base updates independently** (no need to wait for a release), with one update channel per language |
| **Phone Link (V3.4.0)** | QR pairing turns a phone into a remote / monitor / teleprompter / fill light / file-transfer device; phone as a Wi-Fi camera and microphone in OBS; up to 3 read-only monitor sessions, multi-cam and remote guest rooms. Every LAN feature is off by default, bound to private addresses and revocable |
| **Chat / captions / review / clips (V3.4.0)** | Read-only Bilibili / Twitch chat aggregation, overlay, TTS and chat translation; song requests / polls / raffles / gifts / submissions / calendar / AI reply suggestions; local Whisper live captions and bilingual captions; review report, cover candidates and viewer dashboard; highlight clips / vertical export / transcript search |
| **Quality / distribution / automation (V3.4.0)** | Stream health, composition check, go-live runbook, incident replay, AI director, safety sentinel, AI audio chain, multi-platform relay and rclone archive; MIDI / WLED / plugin signatures / theme market / team / AI co-host |
| **Smart diagnostics + log analysis** | One-click health check once connected to OBS, with three engines to choose from (local rules / free AI / your own cloud API); results export as Markdown. Log analysis is **offline**: **34 rules + 5 derived determinations + 3 quantitative ratios (42 distinct findings in total)**, logs sanitized before analysis; dropped-frame / crash clues can be linked to a suspect plugin |
| **Simple recording: one click to start** | One card on the home page, three presets (Quick recording / Meeting / online class / High-bitrate game) — pick one and a single click runs the whole chain: pre-flight check → backup → apply → launch OBS → start recording. While recording it shows the elapsed time and the estimated time left; when you stop you can open the folder or remux to MP4 in one click |
| **One-click recording environment** | Lists the recording-oriented recommended and current values item by item (recording format / quality / folder / canvas & output / frame rate / sample rate); tick what you want and it **backs up first**, then applies, with one-click rollback. Connected to OBS it goes through obs-websocket and takes effect immediately; if OBS has exited it edits `basic.ini` and **reads it back to verify** |
| **Teleprompter (V3.1.0 / V3.1.1)** | A separate always-on-top window: auto-scroll, adjustable speed / font size / line height / opacity / margins; horizontal / vertical mirroring, click-through, a 3-2-1 countdown and a reading guide line; the script can be edited / pasted / imported from `.txt` `.md`; V3.1.1 adds **four independent ways to move up and down** — toolbar / dragging / wheel / keyboard — and the window can be nudged with `Ctrl + arrow keys` |
| **Remote-control OBS** | Scene switching, source visibility, audio mute and volume, record / stream / virtual camera, live stats, scheduled stop, open the recording folder — always at hand through **four entry points: console, system tray, mini window and global hotkeys**; scenes can also switch automatically based on the foreground window title |
| **Plugin square + local health check** | **57 curated plugins** (8 categories, the v1.4 catalog fully re-audited); cards show maintenance status and link straight to GitHub Releases; a **read-only scan of locally installed plugins** locates the OBS install folder across drives (multi-view registry / every fixed drive / multiple Steam libraries) and covers the OBS 32.x "one folder per plugin" plugin root |
| **Scene templates and stream setup** | **12 built-in live-room templates** (Gaming Stream / Vertical Live Selling / Duo Co-Stream / Teaching Screen Recording / Radio Idle / Go-Live / BRB / End Pack / Green Screen Background / Selling Dual Camera / Esports Cast / Podcast Talk / Vertical Short Recording / Meeting/Class Multicam) — with OBS connected, one click creates the scenes and sources; when not connected you can export a standard scene-collection JSON. There is also a 6-step go-live walkthrough plus streaming presets for Bilibili / Douyin / Kuaishou / WeChat Channels / Xiaohongshu / YouTube / Twitch and other major platforms |
| **Chinese / English bilingual** | The interface, tray, notifications, errors, log rules, health-check verdicts and diagnostic reports are all bilingual (**3387 string keys**), **Simplified Chinese by default**, and switching takes effect instantly with no restart; bundled content (knowledge base / templates / plugin catalog / troubleshooting guide) is bilingual too |
| **Data safety (the V3.0 focus)** | All writes to user files go through a **transactional channel**: snapshot before writing + atomic replace + read-back verification + rollback on failure; every write resolves the absolute path first and passes an **out-of-bounds guard**; secrets such as OBS passwords and API keys are encrypted and stored separately |
| **Privacy first** | Preferences contain no credentials at all; passwords and API keys are **double-encrypted** (AES-256-GCM + DPAPI, bound to this machine and this user); the app only goes online when you start an AI diagnostic, and logs are sanitized first; the plugin health check is entirely read-only and its results stay on this machine |
| **Incremental updates, lighter by design** | The in-app "incremental update" downloads only changed files (a delta between neighbouring versions is usually just a few MB); every file is verified with SHA-256, and on failure it falls back to the full installer automatically; installed builds auto-elevate, swap files and restart. The knowledge base / plugin catalog can be updated separately |
| **Personalization & accessibility** | Wallpaper library (multi-image import / de-duplication / thumbnails / fill / fit / center / tile / dimming / blur / slideshow from 30 seconds to 1 hour); light / dark / follow system, 4 font sizes, high contrast and reduced motion; the keyboard can drive the main tasks, and a static check runs in CI |
| **Zero third-party dependencies** | Native WPF + .NET 10 (plus a .NET 6 Win7-compatible build), no NuGet packages, no WebView2; the `obs-websocket` protocol is written by hand; the string tables are plain-BCL C# dictionaries; self-contained single folder, instant startup |

## Features

### Learn & troubleshoot

- **First-run tutorial** — a four-step walkthrough on first launch (connect to OBS → where to look when something breaks → one-click health check → go live & decorate), and every entry point it mentions is a real navigation name; since V2.9.1 it is a "jump-style tour": each step switches the UI to the page it is describing, with the card tucked into the bottom-right corner and the overlay kept at about 17% black so the page stays visible. Replay it any time from *Settings → Onboarding*, no restart needed.
- **Knowledge base** — 212 issues (knowledge base v2.2), organised by category; each one contains **symptoms, possible causes, step-by-step fixes, tips and related issues**, and steps can be checked off with progress remembered automatically. Both Chinese and English content is built in and follows the language switch immediately.
- **Search** — instant results as you type, matching titles, symptoms and causes.
- **Ask me anything** — describe the symptom in plain words (for example "the video I recorded stutters") and it matches the most likely issue entry.
- **Troubleshooting guide** — a quick-reference manual of general debugging approaches, from "what to check first" to "what order to rule things out in".
- **Independent knowledge-base updates** — the issue database / plugin catalog is decoupled from the app version: three entry points — silent auto-update at startup (throttled to once every 6 hours), "Check knowledge base updates" in Settings, and "Update knowledge base only" in the update dialog; GitHub raw as the primary channel with Release assets as fallback, one channel per language.

### Smart diagnostics & log analysis

- **One-click health check & pre-flight check** — once connected to OBS, run it from the *Diagnose* page: it first runs the **pre-flight check (read-only)** to verify the recording format is the crash-safe MKV, the recording path and disk space, the encoder, the audio sample rate and the microphone settings, then works through the "quick self-check list" item by item; results are graded **pass / warning / fail** with actionable advice, and can be **exported as a Markdown report**.
- **Three diagnostic engines** (see [Smart diagnostics engine](#smart-diagnostics-engine) below): the local rules engine (default, fully offline), built-in free AI (two channels, Zhipu / Pollinations, no sign-up) and your own cloud LLM (OpenAI-compatible API + function calling).
- **Log analysis** — offline OBS log parsing: **34 rules + 5 derived determinations + 3 quantitative ratios (42 distinct findings in total)**, verified against real OBS 32.2.2 logs; logs are **sanitized** before analysis. Beyond the individual rules, it triages in three ways: **three-way dropped-frame root-cause triage**, **encoding-overload triage against the current settings**, and **dual-GPU misconfiguration detection**.
- **Log × plugin correlation** — analysis extracts modules that failed to load or crashed, and jumps to the matching plugin-square entry in one click; AI plugins are labelled with their public cost and get performance-budget hints from system monitoring.
- **Health-check toolbox (8 specialist cards)** — colour, sample rate, black-screen diagnostics, audio in depth, virtual camera, disk writes, encoding advisor and node probing; made for the "everything looks right but it still isn't" moments.

### Recording, streaming & teleprompter

- **Simple recording (one click from the home page)** — the three presets answer only one question, "what are you recording": **Quick recording** (1080p / 60fps, the general recommendation), **Meeting / online class** (1080p / 30fps, smaller files and CPU-friendly) and **High-bitrate game** (1080p / 60fps + automatic file splitting, so a long session does not produce one gigantic file). One click runs the whole chain: pre-flight check → full backup → apply → (launch OBS and wait for obs-websocket to become ready if it is not running) → start recording. While recording it shows the **elapsed time** (pauses not counted) and the **estimated time still available** (estimated from bitrate × 1.5 redundancy; below 10 minutes it warns about low space); when you stop it reads back the file size, and if ffmpeg is installed you can remux to MP4 losslessly in one click — otherwise it points you at OBS's built-in *Remux Recordings*.
- **One-click recording environment** — lists the recording-oriented recommended and current values item by item: recording format → Hybrid MP4, recording quality → HQ, recording folder, canvas & output → 1920×1080, frame rate → 60, audio sample rate → 48 kHz. **Everything is backed up automatically first, rollback is one click, and each item can be deselected individually**; connected to OBS it goes through obs-websocket (immediate effect), and if OBS has exited it edits the current profile's `basic.ini` (backed up before writing, **read back and verified item by item after writing**). It deliberately never touches streaming parameters, output mode, encoder family or bitrate — those are the user's own choices.
- **One-click OBS launch + default WebSocket** — when OBS is not running, it explains once the first time and remembers the choice (continue and remember / ask every time / I'll open it myself), then it is one click from then on; when OBS is running but not connected it connects proactively (single-flight, with a timeout, cancellable, and it returns immediately if OBS exits); once connected it applies the settings automatically and starts recording. Failures are classified (OBS not found / launch failed / port closed / handshake failed / password needed / wrong password / timeout / cancelled / OBS exited), and each class offers at least one actionable way out; an optional 0 / 3 / 5 / 10 second countdown before recording (0 = off, disabled by default).
- **Teleprompter** — a separate always-on-top window: borderless, draggable and resizable, auto-scroll, with adjustable speed / font size / line height / opacity / side margins; horizontal / vertical mirroring, click-through (turned off with the global hotkey `Ctrl+Alt+L`), a 3-2-1 countdown and a reading guide line; the script can be edited / pasted / imported from `.txt` `.md` with an auto-saved draft, and it is **entirely local with no network**. Hotkeys: `Ctrl+Alt+T` show / hide, `Ctrl+Alt+P` play / pause. Add `## scene name` markers to the script for **scene sync**: switching scenes only scrolls the teleprompter and never switches OBS scenes automatically. Capture exclusion: window capture and game capture never include the teleprompter; display capture attempts `WDA_EXCLUDEFROMCAPTURE` on Windows 10 2004+, and where that is unsupported the app explicitly offers window capture or a second screen as the alternative (**no promise that it can never be captured**).
- **Teleprompter: manual up/down movement (V3.1.1)** — four independent routes: toolbar `⤒` (back to start) / `▲` (one line up) / `▼` (one line down); hold and drag vertically in the body (auto-scroll pauses while dragging); mouse wheel at about 72 px per notch; keyboard `↑` / `↓` one line, `PgUp` / `PgDn` one screen, `Home` / `End` jump to the start / end, space to pause / resume. Click the body once to activate the window; `Ctrl + ↑ / ↓ / ← / →` moves the window in 10 px steps and always clamps it to the virtual screen; position / size are saved automatically 600 ms after a drag or nudge. The scroll end is computed so that **the last line stops exactly at the reading guide**, and no longer scrolls into the 220 px bottom padding.
- **Recording filename templates** — `{date}` `{time}` `{datetime}` `{scene}` `{profile}` `{resolution}` `{fps}` `{ext}` and other variables; illegal characters are sanitized, name collisions get an automatic sequence number, and if a rename fails the original file is kept.
- **Recording watchdog and mic-quiet alerts** — a background recording watchdog plus live log-tail warnings; while recording / streaming, the tray alerts you if the mic stays muted for more than 8 seconds or the level stays below -55 dB for more than 10 seconds (**alert only — your audio state is never changed for you**).
- **Replay buffer and recording markers** — `Ctrl+Alt+V` saves the replay buffer in one keypress; `Ctrl+Alt+K` drops a recording marker (something OBS itself does not offer yet), which you can use to locate chapters after stopping.
- **Live control** — start / stop streaming from the console, view live bitrate / frame rate / dropped-frame stats, and set a **scheduled stop**; the *Setup* page provides a 6-step go-live walkthrough (preparation & permissions → scenes & sources → audio check → streaming settings → pre-flight check → multi-platform / vertical) plus setup guides for Bilibili / Douyin / Kuaishou / WeChat Channels / Xiaohongshu / YouTube / Twitch and other major platforms.

### Control OBS

- **OBS console** — scene switching, source visibility, audio mute and volume, record / stream / virtual camera, live stats, scheduled stop of recording / streaming, and one-click open of the recording folder.
- **System tray + notifications** — closing the window minimizes it to the tray, and the tray menu controls recording / streaming / the virtual camera directly; recording / streaming state changes raise a system notification (can be turned off).
- **Mini window** — an always-on-top panel with one-click start / stop for recording and streaming (including the virtual camera); draggable and position-remembering; summon it from the tray menu, the console button or the global hotkey `Ctrl+Alt+M`.
- **Global hotkeys** — system-wide shortcuts, by default `Ctrl+Alt+R` record, `Ctrl+Alt+S` stream, `Ctrl+Alt+C` virtual camera, `Ctrl+Alt+V` save replay, `Ctrl+Alt+K` recording marker, `Ctrl+Alt+M` mini window, `Ctrl+Alt+O` show / hide the window, `Ctrl+Alt+T/P/L` teleprompter; all rebindable or disable-able in Settings.
- **Auto scene switching** — switch scenes automatically based on the foreground window title, with keyword / regex rules; the rule list is managed in Settings and each rule can be enabled / disabled individually.

### Plugin square

- **Curated plugin directory** — 8 categories × 57 curated plugins (automation / visual FX / AI / audio / multi-platform & vertical / recording & post-production / input & interaction, etc.); the catalog is external and hot-updatable, so link fixes and new entries ship without a release. **The v1.4 catalog was fully re-audited against "repository not archived + active in the last 12 months + a Windows build available"**, and each card shows its maintenance status (slower-moving ones state the reason).
- **Local plugin health check (read-only)** — scans installed DLLs and versions and labels them "in the catalog / not catalogued"; the OBS install directory is located across drives using multiple signals: running process → registry (HKLM dual view + HKCU + DisplayIcon fallback) → standard layouts on every fixed drive → Steam multi-library parsing; portable builds, per-user plugin folders and a manually specified directory are all supported.
- **Direct downloads and watch list** — every card links straight to the latest GitHub Releases with a "latest vX.Y.Z" badge; you can "watch" a plugin and the app checks for updates silently at startup (Toast only).
- **Log × plugin correlation** — see [Smart diagnostics & log analysis](#smart-diagnostics--log-analysis).

### Streaming setup & toolbox

- **Scene templates** — 12 built-in live-room templates (Gaming Stream / Vertical Live Selling / Duo Co-Stream / Teaching Screen Recording / Radio Idle / Go-Live / BRB / End Pack / Green Screen Background / Selling Dual Camera / Esports Cast / Podcast Talk / Vertical Short Recording / Meeting/Class Multicam); with OBS connected, one click creates the scenes and sources (including transitions and duration settings); when not connected you can export them as a standard scene-collection JSON. Templates annotate recommended plugins and flag missing ones against the local health check.
- **My templates (V3.0)** — save OBS's **current scene collection** back into a template: scene order, source settings, position / scale / crop / rotation, filters and groups all included; apply it in one go after switching machines or reinstalling. Applying verifies item by item and reports honestly, with explicit placeholders for anything unreadable; template files **never overwrite** existing files.
- **My knowledge base (V3.0)** — beyond the built-in 212 entries you can write your own (including custom categories); they merge with the official knowledge base by id, with local entries taking precedence; element-level fault tolerance means one broken entry never makes the whole base unreadable; search, local-entry marking and feedback acknowledgement are supported.
- **OBS config management** — config directory detection, backup / export (ZIP, sanitized by default and without stream keys), import (overwrite / merge, with an automatic pre-backup), light reset and full factory reset.
- **System monitor** — live CPU / memory / network up-and-down / disk space curves (last 2 minutes), linked with OBS render frame rate and dropped-frame data, with a warning when disk space runs low.
- **Get OBS** — the official download page and the official GitHub releases; the app can download the official installer itself (resolve the current stable direct link → validate the official domain → download with progress → launch the installer wizard, **never a silent install**); two alternative routes are Watt Toolkit (formerly Steam++) and the Microsoft Store. All external links are forced to https and restricted to official domains, keeping out impostor packages from "download sites" and "Silver Fox"-style scams.
- **Help & feedback** — a top-level navigation entry: the feedback-form QR code is embedded in the package and scannable offline; a GitHub Issue entry; **security-reporting guidance** (email / the repository's private Security channel); and a **one-click copy of error materials** (version, OS, OBS connection and scenes, pre-flight result — **no paths, device names or raw logs**).

### Phone Link & live workflow (V3.4.0)

- **Phone as a second screen** — QR pairing (one-time secret, 120-second TTL, token stored only as SHA-256) with viewer / operator / admin roles; the phone can switch scenes, toggle sources, mute / set volume, control recording / replay / virtual camera / studio mode and control the teleprompter.
- **Phone monitor and fill light** — 480p JPEG preview at 1–5 fps, tally, recording / streaming timers, all stopped when the page is hidden; fill light / colour card / countdown are separate offline pages; file upload up to 50 MB with JPEG/PNG/MP4 magic-byte checks and an explicit confirmation before entering OBS.
- **Phone as camera / microphone** — Wi-Fi goes through mediamtx + SRT/RTMP and creates the OBS media source and layout automatically; audio travels with the stream and supports a sync offset; when Wi-Fi is unstable the USB route (scrcpy / OBS Camera / Iriun) is explained, with no driver auto-install.
- **Multi-cam and guests** — 2–4 camera switching / picture-in-picture / split screen; up to 3 read-only monitor sessions; remote guest rooms / permissions / kick / recording consent (browser publishing needs your own HTTPS/SFU).

### Interaction, captions & review (V3.4.0)

- **Chat and interaction** — read-only Bilibili public protocol and Twitch anonymous IRC aggregation with de-duplication / rate limiting / reconnect; overlay (CSP, textContent, 50-message cap); sensitive-word / spam alerts, TTS and chat translation; song requests / polls / raffles / Q&A, gift-triggered scenes, submission review, stream calendar and AI reply suggestions (suggestions only).
- **Live captions** — WASAPI → VAD → whisper.cpp local transcription, Chinese/English captions into an OBS text source or overlay; bilingual captions and offline phrase translation; voice control with wake word + whitelist + confirmation for destructive actions; audio is not uploaded by default.
- **Review and clips** — chat / follows / gifts / viewer popularity accumulate in the background; the review page auto-fills and generates a report; cover candidates can be edited / banned-word checked / copied; per-minute viewer dashboard with CSV export / clear; highlight clips, vertical export and transcript search.
- **Quality, distribution and ecosystem** — stream health, composition / lighting / colour temperature, go-live runbook, incident replay, AI director suggestions, safety sentinel, AI audio chain, multi-platform relay and rclone archive; MIDI / foot pedal, WLED, viewer dashboard, plugin signatures, theme market, team and AI co-host.

### Personalization, bilingual & accessibility

- **Wallpaper library (V3.1.0)** — multi-image import / locally hosted copies / thumbnail picker / delete / SHA-256 de-duplication, and it survives restarts; fit modes: fill / fit / center / tile; dimming 0–80% (default 15% in light mode / 40% in dark); optional blur; slideshow from 30 seconds to 1 hour in random order with no repeats within a round, paused while the window is minimized / inactive; the old `bgImage` setting is migrated automatically, and missing / corrupt / unsupported files show a visible note and fall back to the default background. High contrast forces wallpapers off; "reduce motion" skips the fades.
- **Appearance** — light / dark / follow system, 4 font sizes, high contrast and reduced motion.
- **Language** — Simplified Chinese / English switched instantly (Chinese by default, no restart); the language chosen in the installer is the first-run default, and the precedence is "chosen in-app > chosen in the installer > Chinese", so reinstalling / upgrading never overrides your choice. The interface and the bundled content (knowledge base / templates / plugin catalog / guide) follow together.
- **Accessibility** — the connection badge can be Tab-focused and activated with space / enter; the background colour palette in Settings is keyboard-selectable; icon buttons have accessible names; Toasts raise live-region events. A new [`NOBS/scripts/check_accessibility.py`](NOBS/scripts/check_accessibility.py) static-rule check is wired into CI; the commitments and known limits are in [`ACCESSIBILITY.md`](ACCESSIBILITY.md).
- **Easter egg** — the KONAMI code unlocks a hidden "midnight teal" palette — entirely local, silent, optional, and it respects "reduce motion".

## Smart diagnostics engine

Connect to OBS and run a one-click health check. Three engines are available:

| Engine | How it works | Cost |
| --- | --- | --- |
| **Local rules engine** (default) | Deterministic offline rule matching, sharing its core with log analysis | Free, fully offline |
| **Free AI (built-in)** | Two channels — the **Zhipu free model** (the most reliable direct route in mainland China; the key is encrypted and embedded at build time) and **Pollinations** (a key-free public channel worldwide). Locally rate-limited: **10 a day** on Zhipu and **20 a day** on Pollinations, counted independently so they never eat into each other, resetting at midnight | Free, no sign-up |
| **Cloud LLM** | OpenAI-compatible API + function calling against your own API key (stored on this machine with double encryption) | Your API cost |

When the cloud / free engine fails it **falls back to the local engine automatically** and explains why in the result. The free tier is a single-turn ordinary conversation (no knowledge-base tool calls); for multi-turn deep troubleshooting or more frequent use, plug in your own cloud API.

## Release channels: V series vs X series

| Channel | Version style | What it is | Where updates come from |
| --- | --- | --- | --- |
| **V series** (`master`) | `V3.5.0` | **Stable first**: settled features, full regression and release verification — the right choice for everyday streaming / recording | The regular Releases page · the Lanzou mirror · in-app incremental updates |
| **X series** (`master`) | `X1031` | **Independent major line**: date-code versions and the X-specific icon; updates only look at `X` tags | X-series Releases · in-app update |

- **Both series share features and the data folder**: install folder, assembly name and `%LocalAppData%\OBS_Helper` are unchanged, so the upgrade path is identical.
- **Channel filtering happens in the parser**: V builds only recognise `V3.5.0`-style tags; X builds only recognise `X1031`-style tags. They never mistake one another for a newer version.
- **X1031 is smaller than V3.5.0**: X1031 contains items 1–9 and the new icon, but not the global background UI refactor, mobile page layout or Feature Center UX work. V3.5.0 contains those UI changes but keeps the original icon as requested.

## What's new in V3.5.0

> Full details: [`NOBS/RELEASE_NOTES_v3.5.0.md`](NOBS/RELEASE_NOTES_v3.5.0.md).

- **Global background UI refactor**: the wallpaper layer now covers the left navigation, top bar and pages; image mode gives the navigation/top bar a frosted-glass treatment (downscaled + Gaussian-blurred wallpaper) and translucent cards. The previous UI is saved as tag `ui-baseline-v3.4.2`.
- **Minimal visual system**: indigo default accent, lighter shadows, smaller button radii, grouped navigation and roomier spacing. Settings can adjust corner radius, panel opacity and UI font (Microsoft YaHei / Noto Sans SC / HarmonyOS Sans / MiSans / Alibaba PuHuiTi / Smiley Sans / LXGW WenKai, if installed).
- **Startup animation**: a dark brand panel with a progress line fades out after the main window appears.
- **Mobile connection page**: hero area, equal-width segmented navigation, pairing card and device cards.
- **Feature Center**: a “what works right now” status strip (OBS / phone service / media server / teleprompter).
- **Plugins**: GitHub accelerator guidance with re-check; installed list merges managed and locally detected plugins.
- **Phone camera**: pinned mediamtx version with SHA-256 and one-click install; the server can start before OBS connects.
- **VTuber templates**: 10 templates (karaoke, study, duo collab, hand camera); the chat/avatar overlap is fixed.
- **Mini window / teleprompter**: replay, scenes, mute, teleprompter, studio mode and main window; phone-side script editing.
- **Guide & icons**: a 5-minute setup section in both languages; corrected icon semantics in the Tabler style.
- **X1031**: the same shared features plus the new icon and the independent X channel, without this UI refactor.

## X1031

> Full details: [`NOBS/RELEASE_NOTES_x1031.md`](NOBS/RELEASE_NOTES_x1031.md) (Chinese).

- Independent X channel (only `X*` tags) and the minimal “OBS helper” icon.
- Shared plugin, phone camera, template, mini window, teleprompter and guide improvements.

## What's new in V3.4.0

> Full details: [`RELEASE_NOTES_v3.4.0.md`](NOBS/RELEASE_NOTES_v3.4.0.md).

**Theme: Phone Link — turn your phone into a second screen, a second camera and a second microphone.**

### Phones & multi-device
- QR pairing, viewer/operator/admin roles, command whitelist, rate limiting and device revocation; every LAN feature is off by default, bound to private addresses and can be switched off at any time.
- Phone remote: scenes, source visibility, mute/volume, record/replay/virtual camera/studio mode and teleprompter play/speed/font size/mirror.
- Phone monitor: 480p JPEG at 1–5 fps, tally, recording/streaming timers; stops when the page is hidden; up to 3 read-only sessions.
- Fill light / colour card / countdown offline pages; file upload (50 MB, JPEG/PNG/MP4 magic-byte checks, enters OBS only after confirmation).
- Wi-Fi camera: mediamtx + SRT/RTMP → OBS media source, multi-cam layout/switching; phone mic with sync offset; USB guidance.
- Remote guest room/permissions/kick/recording consent; phone notification/clipboard on screen (Android + ADB only; verification codes and passwords are blocked by default).

### Interaction / captions / clips
- Read-only Bilibili/Twitch chat aggregation, overlay, sensitive-word/spam alerts, TTS and chat translation.
- Song requests / polls / raffles / Q&A, gift-triggered scenes, submission review, stream calendar and AI reply suggestions (suggestions only).
- Local live captions (WASAPI → VAD → whisper.cpp), bilingual captions and voice control (wake word + whitelist + confirmation for destructive actions).
- Highlight clips / vertical export and transcript search.

### Insights / quality / distribution / ecosystem
- Session review report, cover candidates, stream health, composition check, go-live runbook and incident replay.
- AI director, safety sentinel, AI audio chain, multi-platform relay and rclone archive.
- MIDI/foot pedal, WLED lighting, viewer dashboard, plugin signature checks, theme market, team collaboration and AI co-host.
- The four module pages (Interaction / Insights / Stream / Automation) share a read-only **Run self-check** that lists each dependency as OK / needs setup / unavailable.

**Compatibility**: main build `net10.0-windows`, Windows 7 compatible build `net6.0-windows`; on Win7, Wi-Fi camera, captions, multicam and relay are explicitly degraded with a reason.

## What's new in V3.3.0

> Full details: [`RELEASE_NOTES_v3.3.0.md`](NOBS/RELEASE_NOTES_v3.3.0.md).

- **Virtual Live workspace**: a top-level entry with Avatar (Live2D) / Room (templates) / Go live (platform guidance) tabs.
- **Live2D setup**: model safety checks (path traversal / symlinks / executables), SHA-256 manifest, VTube Studio public WebSocket API (auth / model load), OBS capture (NDI → Spout2 → virtual camera → window capture) and layout presets.
- **Virtual rooms**: 6 templates + local overlay pages (chat, frame, BRB, ending, countdown; loopback + random token + CSP) and an after-apply checklist.
- **Go-live guidance**: 16 platforms (direct / virtual camera / manual); direct streaming does write → verify → start → confirm; stream keys stay in memory unless explicitly remembered.
- **Plugin marketplace**: transactional install (SHA-256 / x64 PE / OBS closed / backup & rollback), installed management, `owner/repo` import, native GitHub search (topic:obs-plugin) and a one-click catalog update.
- **Accessibility / Elder modes**: mode composition → global tokens (text, density, focus, sidebar width, skip link), with system reduce-motion and text scaling as the floor.
- **Open-source attribution**: README, repository intro and the in-app About page list projects and licenses; see [`NOBS/docs/OPEN_SOURCE_ATTRIBUTION.md`](NOBS/docs/OPEN_SOURCE_ATTRIBUTION.md).

## What's new in V3.1.1

> This release does one thing: it completes the teleprompter's **manual up/down movement** — toolbar, mouse drag, wheel and keyboard all work, and the window itself can be nudged with `Ctrl + arrow keys`; it also fixes auto-scroll ending on a blank screen.
> Everything else — features, data formats and the upgrade path — is identical to V3.1.0, so incremental updates just work.

**Teleprompter: manual up/down movement (P0)**

- New toolbar buttons `⤒` (back to start) / `▲` (one line up) / `▼` (one line down): one click and it moves, **no keyboard focus needed**;
- **Drag the text**: hold and drag vertically in the body and the content follows your hand (like dragging a sheet of paper); auto-scroll pauses temporarily while dragging;
- **Keyboard**: `↑` / `↓` one line, `PgUp` / `PgDn` one screen, `Home` / `End` jump to the start / end, space to pause / resume; click the body once to activate the window and the keys work immediately (previously they did nothing right after opening);
- **Wheel**: with the pointer over the body, about 72 px per notch for fine adjustments;
- **Window position**: `Ctrl + ↑ / ↓ / ← / →` moves the window in 10 px steps, always clamped to the virtual screen;
- Position / size are saved automatically 600 ms after a drag or nudge (previously only on close), and restored in place the next time it opens;
- The whole top-bar background can be used to drag the window (previously only the tiny "teleprompter" label in the top-left corner worked).

**Scroll end fixed (P1)**

- The old scroll limit used `ScrollableHeight`, which included the 220 px bottom padding: once the last line slid out of the viewport, all that was left was black;
- The end is now computed so that **the last line stops exactly at the reading guide** (`TeleprompterCore.ScrollEnd`), and both auto and manual scrolling stop there;
- Trailing blank lines / spaces at the end of the script are trimmed on save and load, so the last "empty line" cannot eat into the scroll range.

**Tests and release**

- 4 new unit tests: scroll end, drag offset, window-move clamping and trailing blank lines in the script;
- The full suite of **1670 unit tests** passed three rounds in a row, with both builds (`net10.0-windows` / `net6.0-windows`) + the 36-item headless self-test + static checks all green;
- Full details and known limits are in [`NOBS/RELEASE_NOTES_v3.1.1.md`](NOBS/RELEASE_NOTES_v3.1.1.md).

## Privacy & security

All data stays on your machine:

- **Preferences** (appearance, language, bookmarks, step progress, connection settings, hotkeys, auto-switch rules, tray behaviour, wallpaper library) → `%LocalAppData%\OBS_Helper\prefs.json` — plain JSON, **containing no credentials at all**;
- **OBS passwords and AI API keys** → `%LocalAppData%\OBS_Helper\secrets.dat` — **double encryption**: each value is first encrypted with AES-256-GCM (the key is derived from the local `MachineGuid` via PBKDF2-SHA256), then the whole file is encrypted with DPAPI (bound to the current Windows user + app entropy); another machine or user cannot unlock it, and even an offline copy of the file cannot be restored.

**Data safety (since V3.0)**: writes to user files — settings, knowledge base, templates, recording archives — all go through the transactional channel: snapshot before writing, atomic replace and read-back verification, with automatic rollback on failure; every write resolves the absolute path first and confirms it lands inside the target directory (a path out-of-bounds guard); secrets are encrypted and stored separately, never mixed with ordinary configuration.

**Found a security vulnerability?** Please do not disclose details in a public Issue — use the repository's private **Security → Report a vulnerability** channel, or email **752139192@qq.com** with `[SECURITY]` in the subject. The full policy is in [SECURITY.md](SECURITY.md) (bilingual). Chinese or English is fine.

The app only goes online when you **explicitly enable** the free AI or cloud diagnostic engine and start a diagnostic, and logs are sanitized before the request is sent. OBS config backups / exports exclude stream keys by default (with an opt-in to include them), and passwords and tokens are redacted automatically.

## Installation & updates

- **GitHub Releases** — download the installer or portable build from the [Releases page](https://github.com/YYRMMAYO/OBS_Helper/releases); the portable build needs no installation and carries its own .NET runtime. **The installer wizard asks for a language first** (Simplified Chinese / English), Chinese by default, and that choice becomes the app's first-run default language
  - `OBS_Helper_Setup_3.1.1.exe` / `OBS_Helper_Portable_3.1.1.zip` — the **main build**, for **Windows 10 / 11**
  - `OBS_Helper_Setup_3.1.1_win7.exe` / `OBS_Helper_Portable_3.1.1_win7.zip` — the **Win7-compatible build**, for **Windows 7 SP1 and later** (.NET 6, no longer receiving security updates; on a new machine use the main build)
  - Both builds share the same AppId, so switching from the compatible build to the main build is recognised as an upgrade rather than a second installation
- **Lanzou cloud (CN mirror)** — extract code `YYKWY` (see the in-app update dialog for details)
- **In-app update (recommended)** — after "Check for updates" compares against the latest GitHub release, choose any of:
  - **Incremental update, all features**: downloads only the files changed since the previous version (usually a few MB), then verifies them, auto-elevates, swaps files and restarts
  - **Knowledge base only**: upgrades the issue database on its own, done in seconds, no need to reinstall the app
  - **Full installer**: download the complete package from Lanzou or in the app and install over the existing copy

> The **main build** supports Windows 10 / 11; the **compatible build** supports Windows 7 SP1 and later. Neither needs WebView2, and neither needs the .NET runtime installed separately.
> Users on 3.1.0 can go straight to 3.1.1 through the in-app **incremental update** (the delta package is based on 3.1.0); this release only adds features, and the `AppId` / install folder / data folder
> are unchanged, so incremental upgrades and in-place installs work as before. **Users on 3.0.0 or earlier** have a mismatched delta base,
> so install the setup or portable package over the existing copy.
> **Language and upgrades**: the language picked during installation only sets the first-run default; once you have changed the language in the app, reinstalling / upgrading never overrides your choice.
> No OBS yet? The in-app *Setup* page / *Toolbox* / home welcome card all provide **official** download entries
> (`obsproject.com` and the official GitHub releases), and you can also download the official installer directly in the app;
> if GitHub is slow to reach, accelerate it with Watt Toolkit (formerly Steam++) first, or use OBS from the Microsoft Store.
> Something wrong? The **Help & feedback** page in the left navigation has the feedback form (scan the QR code with your phone), a GitHub Issue entry,
> security-reporting guidance, and the "one-click copy of error materials"; the home-page shortcut and the tray menu each have an entry too.
> **Please do not disclose security issues publicly** — use the repository's **Security → Report a vulnerability**, or email **752139192@qq.com**.

## Build

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) (building the Win7-compatible target automatically pulls the .NET 6 reference packages);
producing the installer additionally needs [Inno Setup 6](https://jrsoftware.org/isdl.php).

```powershell
# in the repository root (one level above `NOBS/`)
cd NOBS

# run it and take a look
dotnet run --project OBS_Helper.Wpf

# dual-target: main build + Win7-compatible build -> installer / portable zip / delta package / knowledge-base assets
.\build.ps1

# main build only (for quick iteration)
.\build.ps1 -SkipLegacy

# additionally produce a single-file exe (main build)
.\build.ps1 -SingleFile

# no Inno Setup installed: portable package only
.\build.ps1 -SkipInstaller

# jump-version release: pin the delta base (so users on older versions can also upgrade incrementally)
.\build.ps1 -DeltaBaseVersion 2.0.0

# after release, verify that the delta package upgrades cleanly (simulated upgrade + full-file SHA-256 diff)
python scripts\verify_delta.py --old PAKE\windows\OBS_Helper_Portable_3.0.0.zip --delta PAKE\windows\OBS_Helper_Update_3.1.1.zip --publish OBS_Helper.Wpf\bin\Release\net10.0-windows\win-x64\publish

# full unit-test run (includes zh/en content parity / assets loaded per language / INI patching / version predicates, etc.)
dotnet test OBS_Helper.Wpf.Tests\OBS_Helper.Wpf.Tests.csproj -c Release
```

Artifacts land in `NOBS\PAKE\windows\` (i.e. `PAKE\windows\` when the build commands run inside `NOBS`):

- `OBS_Helper_Setup_3.1.1.exe` — installer (main build, MinVersion 10.0, the wizard includes a language-selection page)
- `OBS_Helper_Setup_3.1.1_win7.exe` — installer (Win7-compatible build, MinVersion 6.1sp1)
- `OBS_Helper_Portable_3.1.1.zip` / `…_win7.zip` — unzip and run
- `OBS_Helper_Update_3.1.1.zip` — incremental update package (contains `update_manifest.json`, used by the in-app incremental updater)
- `OBS_Helper_Portable_3.1.1.exe` — single file (requires `-SingleFile`)
- `OBS_Helper_Knowledge_3.1.1.json` / `…3.1.1.en-US.json` — issue-database hot-update assets (one per language)
- `OBS_Helper_Plugins_3.1.1.json` / `…3.1.1.en-US.json` — plugin-square catalog hot-update assets (one per language)
- `manifests/manifest_<ver>.json` — full per-version file manifest (SHA-256, the diff base for delta packages; not published with a Release)

## Project structure

```
NOBS/
  OBS_Helper.Wpf/
    App.xaml(.cs)          App entry, global exception -> error-code dialog
    MainWindow.xaml(.cs)   Left nav + top bar + page host + first-run tutorial overlay, route registration
    AppServices.cs         Composition root: lazy singletons for all services, wired by hand
    Navigation/            Minimal router (route name -> page factory, with cache and back stack)
    Localization/          String tables (Chinese / English, pure BCL with zero WPF dependency)
                           + DataValues (cross-language display-value checks)
                           + ContentAssets (bundled content loaded per language + fallback markers)
    Compat/                Compile-time polyfills required by the Windows 7 compatible build (net6.0-windows)
    Views/                 Pages / windows (console / diagnostics / knowledge base / plugins / templates / teleprompter, etc.)
    Controls/              Shared controls and value converters (including download card / feedback card / one-click recording-environment card)
    Themes/                Palette.xaml palette + Controls.xaml style library
    Models/                Knowledge base, obs-websocket protocol, OBS config, wallpaper and teleprompter models
    Services/
      Compat/              FolderPicker (net6 / net8 branches) and other cross-TFM equivalent implementations
      Host/                HostBridge (DPAPI, log reading, AI relay), LocalStore
      Obs/                 WebSocket client, connection service, log analysis, sanitization
      ObsConfig/           OBS config discovery, backup / export / import, reset, scene-template deployment,
                           RecordingEnvCore/Service (one-click recording environment: recommendations + INI patching)
      Plugins/             Plugin square: catalog data / local health-check scan (incl. OBS 32.x plugin roots) / Releases checks / watch alerts
      Update/              Incremental update, independent knowledge-base updates (distributed per language), installer auto-cleanup,
                           ObsDownloadLinks / FeedbackLinks (outbound allow-list constants)
      Ai/                  Local / free / cloud diagnostic engines and orchestration (incl. the free-tier local rate limiter)
      Shell/               System tray, global hotkeys, auto scene switching, timers, system monitor, recording watchdog, log tailing, first-run tutorial
      Markdown/            Markdown parser for the troubleshooting guide
      OsSupport.cs         Windows version predicates (decide whether the Store entry and similar capabilities are shown)
      LocalizationService.cs  Language service: preference persistence, writes into Application.Resources, broadcasts language changes
    Assets/                problems[.en-US].json / plugins[.en-US].json / scene_templates[.en-US].json /
                           troubleshooting[.en-US].md (Chinese and English side by side, embedded seeds), feedback_qr.png, icons
  OBS_Helper.Wpf/OBS_Helper_Setup.iss  Inno Setup script (MinVersion / output name passed per TFM, UTF-8 with BOM)
  OBS_Helper.Wpf.Tests/    Unit tests (link the pure-logic source files directly, no reference to the whole WPF project)
  build.ps1                Windows build and packaging script (dual-target: installer / portable / delta package / manifests / content assets)
  scripts/verify_delta.py  Delta-package release verifier (simulated upgrade + full-file SHA-256 diff)
```

Theming and language switching take the same route: **the current theme's palette / the current language's strings are written wholesale into `Application.Resources`**,
and all XAML refers to them through `DynamicResource` (theme keys directly, string keys written as `Loc.<key>`),
so both take effect across the whole window instantly without rebuilding a window or restarting.
Strings assembled in code are refreshed by `LocalizationService` broadcasting an event and the main window replaying the current page in place.

### Repository layout

| Directory | Description |
| --- | --- |
| `NOBS/` | **The currently maintained Windows native WPF version** (the one this document describes) |
| `OBS_Helper.Client/` | Old shared frontend (Blazor WASM) — archived only |
| `OBS_Helper.Mac/` | macOS desktop shell (Tauri v2) — archived only |

## Quality baseline & known limits

- **All 1670 unit tests pass** (V3.4.0: three full rounds in a row + dual-target builds + headless self-test + static checks); the test project links the pure-logic source files directly instead of referencing the whole WPF project;
- **All 36 headless self-test items PASS** (28 route cases plus 8 non-route checks: the first-run tutorial overlay (including step-by-step navigation checks), the mini window, string-resource resolution, a language-switch round trip, per-page language replay, the content-fallback banner, the D7 connection-state guard and protocol branches for both builds), **run once for the main build and once for the Win7-compatible build**;
- CI additionally gates on XAML resource references, zh/en string-key parity, accessibility static checks, the headless self-test and an end-to-end delta-package upgrade (full-file SHA-256 comparison); [`NOBS/docs/reviews/`](NOBS/docs/reviews/) keeps per-perspective review records that honestly mark which conclusions have machine evidence and which were only checked by hand;
- **Known limits**: ① Windows 7 has not been verified on real hardware (only that the target framework compiles and the installer's MinVersion is set correctly), and the legacy protocol (obs-websocket 4.x) has not been verified against a real OBS 27; ② the English translation has not been proofread by a native speaker, nor have the English OBS menu paths been checked one by one; ③ both one-click deployment channels are implemented against the protocol / key names and covered by unit tests, but have never been run end-to-end against a real OBS; ④ accessibility stops at code-level adaptation + static checks, with no NVDA / Narrator testing (see [`ACCESSIBILITY.md`](ACCESSIBILITY.md)); ⑤ release artifacts are not code-signed, so SmartScreen may warn about an unknown publisher; ⑥ .NET 6 no longer receives security updates — the compatible build is the fallback for when you *must* run Windows 7; on a new machine use the main build.

## Version history

| Version | Highlights | Release notes |
| --- | --- | --- |
| **V3.4.0** | Phone Link (remote / monitor / teleprompter / fill light / file transfer / Wi-Fi camera / phone mic / multicam / guests), chat aggregation and captions, review/clips, relay/archive, MIDI/lighting/team/AI co-host, four-module self-check | [`RELEASE_NOTES_v3.4.0.md`](NOBS/RELEASE_NOTES_v3.4.0.md) |
| **V3.3.0** | Virtual Live workspace (Live2D / room templates / platform go-live), one-click plugin install and GitHub search, Accessibility / Elder modes, open-source attribution | [`RELEASE_NOTES_v3.3.0.md`](NOBS/RELEASE_NOTES_v3.3.0.md) |
| **V3.1.1** | Teleprompter manual up/down movement (toolbar / drag / wheel / keyboard), window nudging, scroll-end fix | [`RELEASE_NOTES_v3.1.1.md`](NOBS/RELEASE_NOTES_v3.1.1.md) |
| **V3.1.0** | Wallpaper library with slideshow, one-click OBS launch + record, teleprompter, recording-name templates / mic alerts / replay save | [`RELEASE_NOTES_v3.1.0.md`](NOBS/RELEASE_NOTES_v3.1.0.md) |
| **V3.0.0** | Transactional data safety, accessibility, Win7-compatible build, My Templates and My Knowledge Base | [`RELEASE_NOTES_v3.0.0.md`](NOBS/RELEASE_NOTES_v3.0.0.md) |
| **V2.9.6** | Renamed to 「OBS帮助助手」, unified app / repository icon, documentation sync | [`RELEASE_NOTES_v2.9.6.md`](NOBS/RELEASE_NOTES_v2.9.6.md) |
| **V2.9.5** | Private feedback channel fixed, bilingual security policy | [`RELEASE_NOTES_v2.9.5.md`](NOBS/RELEASE_NOTES_v2.9.5.md) |
| **V2.9.4** | Simple recording with three presets, Help & feedback page, repository cover and community files | [`RELEASE_NOTES_v2.9.4.md`](NOBS/RELEASE_NOTES_v2.9.4.md) |
| **V2.9.3** | Full English translation of bundled content, one-click recording-environment setup, Windows 7 SP1 compatibility | [GitHub V2.9.3](https://github.com/YYRMMAYO/OBS_Helper/releases/tag/V2.9.3) |
| **V2.9.2** | Instant Chinese / English switching, installer language selection | [`RELEASE_NOTES_v2.9.2.md`](NOBS/RELEASE_NOTES_v2.9.2.md) |
| **V2.9.1** | Knowledge-base raw channel fixed, jump-style onboarding, official OBS download entries | [`RELEASE_NOTES_v2.9.1.md`](NOBS/RELEASE_NOTES_v2.9.1.md) |

> Older releases (V2.1 – V2.9.0) also live in [`NOBS/`](NOBS/); each file covers the full details and the "not delivered / known limits" list.

## Promo video

55 seconds is all it takes to see what OBS Helper can do for you (duration as published on the Release page) — both the Chinese and English cuts include background music. Click a cover to watch the HD film:

<div align="center">

[![Promo video · Chinese (with BGM)](assets/promo/poster-zh.png)](https://github.com/YYRMMAYO/OBS_Helper/releases/download/V2.9.6/nobs-promo-zh.mp4)

▲ **Promo video · Chinese** (with BGM): pre-flight check · offline log analysis with three-way dropped-frame triage · one-click recording-environment setup · plugin square with a local health check · global hotkeys and live monitoring — [Watch / download HD](https://github.com/YYRMMAYO/OBS_Helper/releases/download/V2.9.6/nobs-promo-zh.mp4) (1920×1080 · 30fps · 55s)

<br/><br/>

[![Promo video · English (with BGM)](assets/promo/poster-en.png)](https://github.com/YYRMMAYO/OBS_Helper/releases/download/V2.9.6/nobs-promo-en.mp4)

▲ **Promo video · English** (with BGM): the same film with an English interface and English subtitles — [Watch / download HD](https://github.com/YYRMMAYO/OBS_Helper/releases/download/V2.9.6/nobs-promo-en.mp4) (1920×1080 · 30fps · 55s)

</div>

## Donate

If this tool saved you some troubleshooting time, you can buy the author a coffee — **entirely voluntary, any amount**; every feature stays free either way.

<div align="center">

| WeChat Pay (recommended) | Alipay (recommended) |
| :---: | :---: |
| <img src="assets/donate/wechat-pay.png" alt="WeChat Pay donation QR code" width="230"> | <img src="assets/donate/alipay.jpg" alt="Alipay donation QR code" width="230"> |
| Scan → WeChat Pay | Scan → Alipay |

</div>

> **One thing must be clear about donations**: donating **buys no priority whatsoever** — no queue jumping,
> no expedited handling, no effect on the order issues are processed in, and no feature unlocks, custom work or
> support promises. It is **simply a donation** that supports continued development (test hardware, signing
> certificates, documentation and time). The app stays free and MIT-licensed either way; a good bug report,
> a doc fix or a pull request is just as valuable.

## License

MIT — see [LICENSE](LICENSE).

### Open-source components

This project is built on the following open-source projects and public protocols.
See [`NOBS/docs/OPEN_SOURCE_ATTRIBUTION.md`](NOBS/docs/OPEN_SOURCE_ATTRIBUTION.md) for the full list,
licenses and boundaries.

| Project | License | Use | Boundary |
|---|---|---|---|
| [OBS Studio](https://github.com/obsproject/obs-studio) | GPL-2.0 | Target application being controlled | External integration only; not bundled or modified |
| [obs-websocket](https://github.com/obsproject/obs-websocket) | GPL-2.0 | Scene / recording / streaming / virtual camera control | Independent C# client written from the public protocol; no code copied |
| [VTube Studio API](https://github.com/DenchiSoft/VTubeStudio) | MIT | Live2D model loading / authentication | Public WebSocket API only; no process injection |
| [.NET / WPF](https://github.com/dotnet/wpf) | MIT | Desktop runtime and UI framework | Self-contained publish; runtime ships with the app |
| [xUnit](https://github.com/xunit/xunit) | Apache-2.0 | Unit tests | Test project only; not shipped |
| [DistroAV](https://github.com/DistroAV/DistroAV) / [obs-spout2-plugin](https://github.com/Off-World-Live/obs-spout2-plugin) | GPL-2.0 | NDI / Spout2 capture for Live2D | Optional external plugins; no code copied or bundled |
| [Watt Toolkit](https://github.com/BeyondDimension/SteamTools) | GPL-3.0 | GitHub acceleration | Detection / launch / guidance only; never modifies its config |

> The Live2D Cubism SDK is proprietary. This project does **not** include or redistribute any
> Cubism runtime or sample model; models provided by users remain theirs. The main application
> stays under the **MIT** license.
