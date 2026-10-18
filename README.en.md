<div align="center">

<img src="assets/banner.svg" alt="OBS Helper — an OBS troubleshooting tool for streamers who are just getting started" width="100%"/>

**OBS Helper · OBS帮助助手** — an OBS troubleshooting / recording / streaming assistant for streamers who are just getting started (native Windows WPF)

**Stable release: V3.1.1** (stable channel) · **This branch: `dev`** (DEV test channel, `DEV 01`, feature baseline V3.1.0)

**What this branch adds (DEV 01)**: the **DEV test channel** is wired into the app — update checks only accept plain-number tags (`01`, `02`, `03`…), versions compare by build number, and the update dialog points at the dev Releases page while hiding the stable-only Lanzou mirror. The feature baseline is stable **V3.1.0**; the V3.1.1 teleprompter fixes are not merged into this branch yet. See [`NOBS/docs/DEV_CHANNEL.md`](NOBS/docs/DEV_CHANNEL.md).

[![CI](https://github.com/YYRMMAYO/OBS_Helper/actions/workflows/ci.yml/badge.svg)](https://github.com/YYRMMAYO/OBS_Helper/actions/workflows/ci.yml)
[![Platform](https://img.shields.io/badge/Platform-Windows_7_SP1%2B_%2F_10_%2F_11-0078D6.svg)]()
[![.NET](https://img.shields.io/badge/.NET-10_%2F_6-512BD4.svg)]()
[![Stack](https://img.shields.io/badge/Stack-WPF_%2F_C%23-239120.svg)]()
[![Release](https://img.shields.io/badge/Release-3.1.1-38bdf8.svg)](https://github.com/YYRMMAYO/OBS_Helper/releases)
[![OBS](https://img.shields.io/badge/OBS-32.2.2-302E31.svg)](https://github.com/obsproject/obs-studio/releases)
[![offline-first](https://img.shields.io/badge/offline--first-2ea44f.svg)]()
[![Incremental Update](https://img.shields.io/badge/Incremental%20Update-2.5MB-7dd3fc.svg)]()
[![Tests](https://img.shields.io/badge/Tests-1378%20passed-2ea44f.svg)]()
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

**English** · [简体中文](README.md)

</div>

> **The checked-out branch is `dev` (the DEV test channel)**: it is **further ahead** than the stable build and
> **may include unstable features**, behaviour changes and known bugs. DEV builds are numbered `01`, `02`, `03`…
> (**no leading V**) and only update through the **dev Releases page**; for day-to-day stability use the stable
> channel (`master` / the regular releases).

**OBS Helper** (known as 「OBS 排障助手」 before V2.9.6 — the same app on the same upgrade path) is an OBS Studio troubleshooting and recording assistant for streamers who are just getting started: **it works fully offline** — the 212-entry knowledge base, the troubleshooting guide and the log-analysis rules are all embedded in the app, so they are searchable without a connection; once connected to the local OBS you also get one-click health checks, remote control of scenes / recording / streaming, and recording-environment deployment. **Since V2.9.4** the whole journey from "configure OBS" to "start recording" fits into one card on the home page; **since V3.1.0** OBS can also be launched and recording started with one click when it is not running. This branch ships the **DEV test channel** on top of the V3.1.0 baseline; the V3.1.1 teleprompter fixes are not merged yet.

This is the **native WPF rewrite** of the original Blazor WebAssembly + WebView2 version (source in [`NOBS/`](NOBS/)): feature-for-feature parity, but the browser engine is gone — a cold start goes straight to the window, it ships as a self-contained single folder, and it needs neither WebView2 nor a .NET runtime install. **Since V2.9.2 the UI switches between Chinese and English instantly**, and **since V2.9.3 even the bundled offline content is bilingual** (the 212-entry knowledge base / 57-entry plugin catalog / 12 scene templates / the whole troubleshooting guide are translated, and follow the language switch immediately); **since V2.2 there is a built-in plugin square** (57 curated plugins plus a read-only local health check; the v1.4 catalog has been fully re-audited); **since V2.1 updates are incremental and the knowledge base updates independently**.

> **Compatibility**: the main build targets **Windows 10 / 11** (.NET 10); a separate **Windows 7 SP1 compatible build** (.NET 6, the last .NET version that supports Win7) ships as both installer and portable ZIP in Releases — pick whichever matches your system.
>
> **OBS version alignment**: currently aligned with **OBS Studio 32.2.2** (the stable version at the time of this release) — log parsing, config keys, the obs-websocket protocol and the local plugin scan (including the "one folder per plugin" plugin root added in 32.x) have all been verified against a real 32.2.2 environment. See the [V2.9.3 release notes](https://github.com/YYRMMAYO/OBS_Helper/releases/tag/V2.9.3) for details.

> **Where the docs live**: this README at the repository root is the **complete user-facing guide** (features · usage · diagnostics · privacy · installation · build); developer- and history-oriented details (quality baseline, per-release notes, code inventory, architecture overview) live in [`NOBS/README.md`](NOBS/README.md). Read [`docs/BRANCH_AND_DOC_RULES.md`](docs/BRANCH_AND_DOC_RULES.md) before editing a README or release notes.

---

**Contents**: [Start here](#start-here) · [5-minute quick start](#5-minute-quick-start) · [Highlights at a glance](#highlights-at-a-glance) · [Features](#features) · [Smart diagnostics engine](#smart-diagnostics-engine) · [Release channels: stable vs DEV](#release-channels-stable-vs-dev) · [What's new in DEV 01](#whats-new-in-dev-01) · [Version history](#version-history) · [Privacy & security](#privacy--security) · [Installation & updates](#installation--updates) · [Build](#build) · [Project structure](#project-structure) · [Quality baseline & known limits](#quality-baseline--known-limits) · [Promo video](#promo-video) · [Donate](#donate) · [License](#license)

---

## Start here

| What you want to do | Where to go |
| --- | --- |
| Download the installer / portable build, read release notes | [Releases](https://github.com/YYRMMAYO/OBS_Helper/releases) |
| See what it can do and how to use it first | Read on: [5-minute quick start](#5-minute-quick-start) and [Features](#features) |
| Read the developer-oriented full documentation (quality baseline · release history · code inventory) | [`NOBS/README.md`](NOBS/README.md) |
| Full documentation in Chinese | [`README.md`](README.md) · [`NOBS/README.md`](NOBS/README.md) |
| What this branch changes (DEV 01) | [`NOBS/docs/DEV_CHANNEL.md`](NOBS/docs/DEV_CHANNEL.md) · [Stable V3.1.1 notes](https://github.com/YYRMMAYO/OBS_Helper/blob/master/NOBS/RELEASE_NOTES_v3.1.1.md) |
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
| **Smart diagnostics + log analysis** | One-click health check once connected to OBS, with three engines to choose from (local rules / free AI / your own cloud API); results export as Markdown. Log analysis is **offline**: **34 rules + 5 derived determinations + 3 quantitative ratios (42 distinct findings in total)**, logs sanitized before analysis; dropped-frame / crash clues can be linked to a suspect plugin |
| **Simple recording: one click to start** | One card on the home page, three presets (Quick recording / Meeting / online class / High-bitrate game) — pick one and a single click runs the whole chain: pre-flight check → backup → apply → launch OBS → start recording. While recording it shows the elapsed time and the estimated time left; when you stop you can open the folder or remux to MP4 in one click |
| **One-click recording environment** | Lists the recording-oriented recommended and current values item by item (recording format / quality / folder / canvas & output / frame rate / sample rate); tick what you want and it **backs up first**, then applies, with one-click rollback. Connected to OBS it goes through obs-websocket and takes effect immediately; if OBS has exited it edits `basic.ini` and **reads it back to verify** |
| **Teleprompter (V3.1.0)** | A separate always-on-top window: auto-scroll, adjustable speed / font size / line height / opacity / margins; horizontal / vertical mirroring, click-through (closed with `Ctrl+Alt+L`), a 3-2-1 countdown and a reading guide line; the script can be edited / pasted / imported from `.txt` `.md`; the window is draggable and resizable |
| **Remote-control OBS** | Scene switching, source visibility, audio mute and volume, record / stream / virtual camera, live stats, scheduled stop, open the recording folder — always at hand through **four entry points: console, system tray, mini window and global hotkeys**; scenes can also switch automatically based on the foreground window title |
| **Plugin square + local health check** | **57 curated plugins** (8 categories, the v1.4 catalog fully re-audited); cards show maintenance status and link straight to GitHub Releases; a **read-only scan of locally installed plugins** locates the OBS install folder across drives (multi-view registry / every fixed drive / multiple Steam libraries) and covers the OBS 32.x "one folder per plugin" plugin root |
| **Scene templates and stream setup** | **12 built-in live-room templates** (Gaming Stream / Vertical Live Selling / Duo Co-Stream / Teaching Screen Recording / Radio Idle / Go-Live / BRB / End Pack / Green Screen Background / Selling Dual Camera / Esports Cast / Podcast Talk / Vertical Short Recording / Meeting/Class Multicam) — with OBS connected, one click creates the scenes and sources; when not connected you can export a standard scene-collection JSON. There is also a 6-step go-live walkthrough plus streaming presets for Bilibili / Douyin / Kuaishou / WeChat Channels / Xiaohongshu / YouTube / Twitch and other major platforms |
| **Chinese / English bilingual** | The interface, tray, notifications, errors, log rules, health-check verdicts and diagnostic reports are all bilingual (**2409 string keys**, including the DEV-channel strings added in DEV 01), **Simplified Chinese by default**, and switching takes effect instantly with no restart; bundled content (knowledge base / templates / plugin catalog / troubleshooting guide) is bilingual too |
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

## Release channels: stable vs DEV

| Channel | Version style | What it is | Where updates come from |
| --- | --- | --- | --- |
| **Stable** (`master`) | `V3.1.1` | **Stable first**: settled features, full regression and release verification — the right choice for everyday streaming / recording | The regular Releases page · the Lanzou mirror · in-app incremental updates |
| **DEV (test)** (`dev`, **this branch**) | `01`, `02`, `03` … (**no leading V**, currently `DEV 01`) | **Further ahead**: work in progress lands here first and **may include unstable features**, behaviour changes and known bugs | **Only** the **dev Releases page** |

- **Why DEV has its own channel**: DEV numbering is an auto-incrementing **build number** (+1 for every dev build, maintained by the dev branch's channel implementation) and is not comparable to the stable `Vmajor.minor.patch` scheme. A DEV build therefore **only recognises plain-number tags such as `01`**, and a stable build **only recognises tags such as `V3.1.1`** — even though both kinds of tag live in the same repository, neither channel mistakes the other for a newer version;
- **DEV builds are not packaged and do not use the Lanzou mirror yet**: the update prompt only points you at the dev Releases page (`/releases/tag/0X`); the stable channel's incremental updates, Lanzou mirror and installers keep serving stable users exactly as before;
- **Which one should you pick?** Use stable if you want stability; take DEV if you are happy to help test early features and can live with a build that may break. For DEV issues, go through *Help & feedback* as usual and put `DEV 0X` (e.g. `DEV 03`) in the description so it is easier to locate.

> **About this branch**: `dev` is based on the stable **V3.1.0** feature set (`DEV 01` is the first build that ships the DEV-channel plumbing); the V3.1.1 fixes are not merged yet. DEV builds update only through the **dev Releases page** — no installers, no Lanzou mirror.

## What's new in DEV 01

> This branch is not a feature release: it is the first build that wires the **DEV test channel** into the app.
> The feature baseline matches stable **V3.1.0**; the V3.1.1 teleprompter fixes are not merged into this branch yet.

- **Update checks are channel-aware**: DEV builds parse only plain-number tags (`01`, `02`, `03`…) and compare by **build number**;
  stable builds only accept `Vmajor.minor.patch` tags, so the two kinds of tag never mistake each other for a newer version.
- **Update dialog (DEV build)**: shows "current DEV version / latest DEV version" plus a channel note, hides the **Lanzou** mirror
  (stable-only), and turns the release-page button into the **dev Releases page**.
- **In-app download / incremental update**: looks for `OBS_Helper_Setup_*.exe` / `OBS_Helper_Update_*.zip` in dev Releases;
  when DEV has not published packages yet it says so clearly instead of failing or falling back to stable assets.
- **Version numbering**: the assembly version stays at the stable baseline `3.1.0`; the About page shows `3.1.0 (DEV 01)` —
  the stable version says which release this build is based on, and `DEV 01` says which DEV build it is.
- **Knowledge base / plugin catalog still come from stable Releases**: those assets ship with the stable channel,
  so DEV users are never cut off.
- **Tests**: a new `DevChannelTests` suite covers tag parsing (`01` / `02` / `V3.1.0` / `V01` / `01-beta` / empty), build-number
  formatting, DEV-vs-stable version comparison and Release-asset filtering.

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
> **About this branch**: DEV builds are **not packaged and do not use the Lanzou mirror**; they update only through the **dev Releases page**
> (tags `01`, `02`, `03`…). For stability, download **V3.1.1** from the regular Releases page (main build for Win10/11 · compatible build for Win7 SP1+;
> neither needs WebView2 or a separate .NET runtime). Stable users can upgrade incrementally from 3.1.0 to 3.1.1; users on 3.0.0 or earlier
> should install the setup or portable package over the existing copy.
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
python scripts\verify_delta.py --old PAKE\windows\OBS_Helper_Portable_3.0.0.zip --delta PAKE\windows\OBS_Helper_Update_3.1.0.zip --publish OBS_Helper.Wpf\bin\Release\net10.0-windows\win-x64\publish

# full unit-test run (includes zh/en content parity / assets loaded per language / INI patching / version predicates, etc.)
dotnet test OBS_Helper.Wpf.Tests\OBS_Helper.Wpf.Tests.csproj -c Release
```

Artifacts land in `NOBS\PAKE\windows\` (i.e. `PAKE\windows\` when the build commands run inside `NOBS`):

- `OBS_Helper_Setup_3.1.0.exe` — installer (main build, MinVersion 10.0, the wizard includes a language-selection page)
- `OBS_Helper_Setup_3.1.0_win7.exe` — installer (Win7-compatible build, MinVersion 6.1sp1)
- `OBS_Helper_Portable_3.1.0.zip` / `…_win7.zip` — unzip and run
- `OBS_Helper_Update_3.1.0.zip` — incremental update package (contains `update_manifest.json`, used by the in-app incremental updater)
- `OBS_Helper_Portable_3.1.0.exe` — single file (requires `-SingleFile`)
- `OBS_Helper_Knowledge_3.1.0.json` / `…3.1.0.en-US.json` — issue-database hot-update assets (one per language)
- `OBS_Helper_Plugins_3.1.0.json` / `…3.1.0.en-US.json` — plugin-square catalog hot-update assets (one per language)
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

- **All 1378 unit tests pass** (in the DEV 01 build: three full rounds in a row + dual-target builds + headless self-test + static checks); the test project links the pure-logic source files directly instead of referencing the whole WPF project;
- **All 26 headless self-test items PASS** (18 route cases across 17 routes plus 8 non-route checks: the first-run tutorial overlay (including step-by-step navigation checks), the mini window, string-resource resolution, a language-switch round trip, per-page language replay, the content-fallback banner, the D7 connection-state guard and protocol branches for both builds), **run once for the main build and once for the Win7-compatible build**;
- CI additionally gates on XAML resource references, zh/en string-key parity, accessibility static checks, the headless self-test and an end-to-end delta-package upgrade (full-file SHA-256 comparison); [`NOBS/docs/reviews/`](NOBS/docs/reviews/) keeps per-perspective review records that honestly mark which conclusions have machine evidence and which were only checked by hand;
- **Known limits**: ① Windows 7 has not been verified on real hardware (only that the target framework compiles and the installer's MinVersion is set correctly), and the legacy protocol (obs-websocket 4.x) has not been verified against a real OBS 27; ② the English translation has not been proofread by a native speaker, nor have the English OBS menu paths been checked one by one; ③ both one-click deployment channels are implemented against the protocol / key names and covered by unit tests, but have never been run end-to-end against a real OBS; ④ accessibility stops at code-level adaptation + static checks, with no NVDA / Narrator testing (see [`ACCESSIBILITY.md`](ACCESSIBILITY.md)); ⑤ release artifacts are not code-signed, so SmartScreen may warn about an unknown publisher; ⑥ .NET 6 no longer receives security updates — the compatible build is the fallback for when you *must* run Windows 7; on a new machine use the main build.

## Version history

| Version | Highlights | Release notes |
| --- | --- | --- |
| **V3.1.1** | Teleprompter manual up/down movement (toolbar / drag / wheel / keyboard), window nudging, scroll-end fix | [Stable release notes](https://github.com/YYRMMAYO/OBS_Helper/blob/master/NOBS/RELEASE_NOTES_v3.1.1.md) |
| **V3.1.0** | Wallpaper library with slideshow, one-click OBS launch + record, teleprompter, recording-name templates / mic alerts / replay save | [`RELEASE_NOTES_v3.1.0.md`](NOBS/RELEASE_NOTES_v3.1.0.md) |
| **V3.0.0** | Transactional data safety, accessibility, Win7-compatible build, My Templates and My Knowledge Base | [`RELEASE_NOTES_v3.0.0.md`](NOBS/RELEASE_NOTES_v3.0.0.md) |
| **V2.9.6** | Renamed to 「OBS帮助助手」, unified app / repository icon, documentation sync | [`RELEASE_NOTES_v2.9.6.md`](NOBS/RELEASE_NOTES_v2.9.6.md) |
| **V2.9.5** | Private feedback channel fixed, bilingual security policy | [`RELEASE_NOTES_v2.9.5.md`](NOBS/RELEASE_NOTES_v2.9.5.md) |
| **V2.9.4** | Simple recording with three presets, Help & feedback page, repository cover and community files | [`RELEASE_NOTES_v2.9.4.md`](NOBS/RELEASE_NOTES_v2.9.4.md) |
| **V2.9.3** | Full English translation of bundled content, one-click recording-environment setup, Windows 7 SP1 compatibility | [GitHub V2.9.3](https://github.com/YYRMMAYO/OBS_Helper/releases/tag/V2.9.3) |
| **V2.9.2** | Instant Chinese / English switching, installer language selection | [`RELEASE_NOTES_v2.9.2.md`](NOBS/RELEASE_NOTES_v2.9.2.md) |
| **V2.9.1** | Knowledge-base raw channel fixed, jump-style onboarding, official OBS download entries | [`RELEASE_NOTES_v2.9.1.md`](NOBS/RELEASE_NOTES_v2.9.1.md) |

> Older releases (V2.1 – V2.9.0) also live in [`NOBS/`](NOBS/); each file covers the full details and the "not delivered / known limits" list.
>
> **Note**: this branch is based on V3.1.0; the V3.1.1 entry above is a stable release and is not merged into `dev` yet.

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
