<div align="center">

<img src="assets/banner.svg" alt="OBS Helper — offline-first troubleshooting copilot for OBS Studio" width="100%"/>

# OBS Helper · OBS帮助助手

**The offline-first troubleshooting companion for [OBS Studio](https://obsproject.com/) — built for streamers who are just getting started.**

**Current release: V3.1.1** (stable) · DEV test builds: `01`, `02`, `03` …

[![CI](https://github.com/YYRMMAYO/OBS_Helper/actions/workflows/ci.yml/badge.svg)](https://github.com/YYRMMAYO/OBS_Helper/actions/workflows/ci.yml)
[![Platform](https://img.shields.io/badge/Platform-Windows_7_SP1%2B-0078D6.svg)]()
[![.NET](https://img.shields.io/badge/.NET-10_%2F_6-512BD4.svg)]()
[![Release](https://img.shields.io/badge/Release-3.1.1-38bdf8.svg)](https://github.com/YYRMMAYO/OBS_Helper/releases)
[![Tests](https://img.shields.io/badge/Tests-1345%20passed-2ea44f.svg)]()
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

**English** · [简体中文](README.md)

</div>

> **The default branch of this repository is `master` (stable channel)**: fully regression-tested and verified
> before release — the right choice for everyday streaming and recording. For early features, see the DEV test
> builds on the `dev` branch (`01`, `02`, `03`…, **no leading V**); they are further ahead and may be unstable.

OBS Helper is a native **Windows (WPF)** desktop app for people who are getting started with OBS Studio: health-check a running OBS, analyze its logs, deploy a recording environment, and control scenes / recording / streaming — and when OBS is not running at all, the entire **212-entry knowledge base still works fully offline**. Zero third-party dependencies, self-contained single folder, no WebView2 and no .NET runtime install.

> **The full documentation now ships with the source inside [`NOBS/`](NOBS/README.en.md)** — features, smart
> diagnostics, privacy & security, installation & updates, building, project layout and per-release notes all live
> there. This file at the repository root is only a landing page.

## Start here

| What you want | Where to go |
| --- | --- |
| Download an installer / portable ZIP, read release notes | [Releases](https://github.com/YYRMMAYO/OBS_Helper/releases) |
| Full documentation (features · privacy · install · build · history) | [`NOBS/README.en.md`](NOBS/README.en.md) |
| 中文完整文档 | [`NOBS/README.md`](NOBS/README.md) |
| What changed in this release | [`NOBS/RELEASE_NOTES_v3.1.1.md`](NOBS/RELEASE_NOTES_v3.1.1.md) (Chinese) |
| DEV channel numbering and update rules | [`NOBS/docs/DEV_CHANNEL.md`](NOBS/docs/DEV_CHANNEL.md) (Chinese) |
| Which branch a document belongs to (read before editing docs) | [`docs/BRANCH_AND_DOC_RULES.md`](docs/BRANCH_AND_DOC_RULES.md) (Chinese) |
| Report a bug / disclose a vulnerability | [Issues](https://github.com/YYRMMAYO/OBS_Helper/issues) · [SECURITY.md](SECURITY.md) |
| License | [LICENSE](LICENSE) (MIT) |

## V3.1.1: the teleprompter can now be moved up and down

> The focus of this patch is the teleprompter's vertical movement: toolbar, drag, wheel and keyboard all work,
> the window itself can be nudged with `Ctrl + arrow keys`, and auto-scroll no longer ends on a blank screen.

- **New toolbar buttons `⤒` / `▲` / `▼`**: back to start, one line up, one line down — no keyboard focus needed;
- **Drag the text**: hold and drag vertically in the body, the content follows your hand; auto-scroll pauses while dragging;
- **Keyboard & wheel**: `↑` / `↓` one line, `PgUp` / `PgDn` one screen, `Home` / `End`, wheel for fine steps;
  clicking the body activates the window so the keys work immediately (they used to be ignored right after opening);
- **Window position**: `Ctrl + ↑ / ↓ / ← / →` moves the window by 10 px, clamped to the virtual screen;
  position and size are saved automatically after a drag;
- **Scroll end**: the last line stops at the reading guide instead of scrolling into the bottom padding;
  trailing blank lines in the script are trimmed.

Everything else — data formats and the upgrade path — is unchanged from V3.1.0, so incremental updates just work.
See [`NOBS/RELEASE_NOTES_v3.1.1.md`](NOBS/RELEASE_NOTES_v3.1.1.md) (Chinese).

## Release channels: stable vs DEV

| Channel | Version style | What it is | Where updates come from |
| --- | --- | --- | --- |
| **Stable** (`master`) | `V3.1.1` | **Stable first**: settled features, full regression and release verification — the right choice for everyday streaming / recording | The regular Releases page · the Lanzou mirror · in-app incremental updates |
| **DEV (test)** (`dev`) | `01`, `02`, `03` … (**no leading V**) | **Further ahead**: work in progress lands here first and **may include unstable features**, behaviour changes and known bugs | **Only** the **dev Releases page** |

- DEV builds are numbered by an auto-incrementing **build number**, which is not comparable to the stable
  `Vmajor.minor.patch` scheme. A DEV build therefore **only recognises plain-number tags (`01`)**, and a stable
  build **only recognises `V3.1.1`-style tags** — even though both kinds of tag live in the same repository,
  neither channel mistakes the other for a newer version.
- **DEV builds are not packaged and do not use the Lanzou mirror yet**: the update prompt only points you at the
  dev Releases page. Incremental updates, the Lanzou mirror and installers keep serving stable users as before.
- **Which one should you pick?** Use stable for day-to-day work; take DEV if you want early features and can live
  with the occasional broken build. Please put `DEV 0X` (e.g. `DEV 03`) in any DEV issue you file.

## Promo video

[![Promo video · English (with BGM)](assets/promo/poster-en.png)](https://github.com/YYRMMAYO/OBS_Helper/releases/download/V2.9.6/nobs-promo-en.mp4)

55 seconds on what it does for you (Chinese and English cuts, both with background music) — click the cover, or
[download the HD cut](https://github.com/YYRMMAYO/OBS_Helper/releases/download/V2.9.6/nobs-promo-en.mp4).

## Donate

If the app saved you some troubleshooting time, you can buy the author a coffee — **entirely voluntary, any amount**; every feature stays free either way.

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
