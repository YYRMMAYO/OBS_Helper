# 安全策略 · Security Policy

**简体中文** · [English](#english)

「OBS帮助助手」（OBS Helper）是一个运行在 Windows 本地的 OBS Studio 排障工具。
我们重视用户的数据安全与隐私，感谢你以负责任的方式报告安全问题。

> **语言**：中文或英文都可以，**也可以用中英双语**。

## 支持的安全更新版本范围

**仅维护最新发布版本。**

- 安全修复只进入最新版本，不回溯到历史版本；历史版本请先升级到最新发布版本再复测。
- 最新版本号以 [GitHub Releases](https://github.com/YYRMMAYO/OBS_Helper/releases) 页面为准，
  应用内「设置 → 关于」也会显示当前版本。
- 如果你的问题只在旧版本上出现，请先升级并确认能否复现，这能显著缩短定位时间。

## 如何私密报告漏洞

**请不要在公开 Issue、Discussion 或评论区贴出漏洞细节。**
公开披露会影响其他用户，给修复留出时间是对整个社区负责的做法。

请使用以下任一私密渠道：

1. **GitHub 私密报告通道（推荐）**：
   打开本仓库的 **Security** 选项卡 → **Report a vulnerability**，
   在该私密表单中描述问题。通过此通道提交的内容仅项目维护者可见。
2. **邮件 <752139192@qq.com>**：
   如果 Security 页面没有显示私密报告入口，或者你更习惯用邮件沟通，请直接发到上面的邮箱。
   建议邮件标题以 `[SECURITY]` 开头，方便优先处理。
   请勿在公开渠道发布可被利用的技术细节。

> **为什么给的是邮箱**：GitHub 并没有提供面向任意用户的私信功能，
> 以前写的「GitHub 私信维护者」实际上无处可点。私密沟通统一走邮件。

### 报告时请尽量包含

- 受影响的版本（见「设置 → 关于」）与 Windows 版本；
- 漏洞类型与影响范围（能读到什么、能改什么、是否需要本地权限、是否可远程触发）；
- 最小复现步骤或概念验证（PoC），可用脱敏后的样例数据；
- 你期望的修复方向或缓解建议（可选）；
- 你希望如何被署名（可选，默认匿名致谢）。

**同样请勿在报告中附带：** OBS 流密钥、账号密码、真实身份信息，以及未脱敏的完整日志。
如需提供日志，请先移除推流地址与密钥字段。

## 我们关注的安全面

以下是本项目已经实现的纵深防御设计，也是我们最关注、最希望收到报告的领域；
若你在这些环节发现绕过或缺陷，都属于我们关心的有效漏洞：

- **本地机密存储（双层加密）**
  连接密码、API Key 等机密经 **Windows DPAPI**（`CurrentUser` 范围 + 应用附加熵）加密后
  写入本地文件；自 V1.7.0 起，每条机密值在进入 DPAPI 之前，还会先用由本机
  `MachineGuid` 派生的密钥做一层 **AES-256-GCM** 加密。换用户或换机器均无法解密。
  GCM 认证失败时按「不存在」处理（fail-closed），不降级为明文。
- **日志读取目录白名单**
  只允许读取 `%AppData%\obs-studio\logs` 与 `%AppData%\obs-studio\crashes` 下的
  `.txt` / `.log` 文件；解析出真实路径后会**二次校验**，防止 `..` 路径穿越读取白名单以外的文件。
  单个日志最多读取 8 MB，超出部分只读尾部。
- **出站请求强制 https 且限定官方域名**
  打开外链、拉取更新清单与知识库等出站行为都先校验协议必须为 `https`，
  并限制在项目声明过的官方域名/通道白名单内；地址常量被误改或被替换时宁可不开，
  也不跳转到第三方站点。
- **拒绝内网 / 回环地址（SSRF 防护）**
  云端 AI 接口地址强制 `https`，并拒绝指向本机与内网地址；除字符串判断外，
  还会在连接建立后检查对端实际 IP（应对 DNS rebinding），命中内网地址即阻断。
  （与 OBS 本机 obs-websocket 的回环连接属于预期内的正常链路。）
- **日志与诊断内容脱敏**
  日志分析、导出报告与实时预警在展示前会对公网 IP、域名、用户目录路径等做脱敏处理；
  本地回环地址与私网地址保留，因为它们对排查网络问题有用且不构成隐私泄漏。
- **配置写入的可回滚性**
  涉及用户配置写入的功能遵循「先备份、可回滚」的原则，避免异常中断留下损坏的配置。

## 响应时限

本项目由个人维护，安全响应是**尽力而为（best effort）**，没有商业 SLA。我们承诺：

- **先致谢、后修复**：收到有效报告后先确认收悉并致谢，再安排修复与发布；
- 在修复版本发布前，不公开漏洞利用细节；
- 修复发布后，如果你不反对，我们会在 Release Notes 中致谢报告者；
- 如果报告经评估不属于安全漏洞（例如依赖用户主动关闭安全设置、或需要已被攻破的本机账户），
  我们会说明判断依据。

## 不在范围内

以下通常不被视为安全漏洞：

- 需要攻击者已经拥有本机管理员权限或已登录用户权限才能实现的效果；
- 用户自行修改本地配置文件、安装包或可执行文件导致的问题；
- 第三方组件（如 OBS Studio 本体、插件、系统运行库）自身的漏洞——请向对应上游报告；
- 缺少「锦上添花」类加固（如未做代码签名、未做反调试）而无实际可利用路径的项。

---

相关文档：[README](README.md) · [贡献指南](CONTRIBUTING.md) · [行为准则](CODE_OF_CONDUCT.md)

---

<a id="english"></a>

# Security Policy

[简体中文](#安全策略security-policy) · **English**

**OBS Helper** is a local Windows troubleshooting tool for OBS Studio. We care about our users'
data security and privacy, and we appreciate responsible disclosure.

> **Language**: Chinese, English, or a mix of both.

## Supported versions

**Only the latest release is maintained.**

- Security fixes land in the latest version only; older versions are not backported. Please upgrade
  before reporting.
- See the [GitHub Releases](https://github.com/YYRMMAYO/OBS_Helper/releases) page for the current
  version; the app also shows it under **Settings → About**.
- If an issue only reproduces on an old version, please upgrade and confirm first — it saves a lot of
  time.

## How to report a vulnerability privately

**Please do not post vulnerability details in public issues, discussions or comment threads.**
Public disclosure puts other users at risk; giving us time to fix it is what keeps the community safe.

Use either private channel:

1. **GitHub private reporting (preferred)**: open the repository's **Security** tab →
   **Report a vulnerability** and describe the issue in that private form. Only the maintainer
   can see it.
2. **Email <752139192@qq.com>**: if the Security tab does not offer the private report entry, or you
   simply prefer email, write to that address. Starting the subject line with `[SECURITY]` helps us
   prioritise it. Do not publish exploitable details anywhere public.

> **Why email**: GitHub does not provide direct messages between arbitrary users, so the older
> "DM the maintainer on GitHub" instruction pointed at something that did not exist. Private
> communication now goes through email.

### Please include

- the affected version (see **Settings → About**) and your Windows version;
- the vulnerability type and impact (what can be read, what can be changed, whether local privileges
  are required, whether it can be triggered remotely);
- minimal reproduction steps or a proof of concept (redacted sample data is fine);
- any suggested fix or mitigation (optional);
- how you would like to be credited (optional; anonymous thanks by default).

**Please also do not include:** OBS stream keys, account passwords, real identity information, or an
unredacted full log. If a log is needed, strip the stream URL and key fields first.

## Areas we care about

These are the defence-in-depth measures already implemented in this project, and the areas we most
want to hear about. A bypass or flaw in any of them is a valid report:

- **Local secret storage (two layers)**
  Connection passwords and API keys are encrypted with **Windows DPAPI** (`CurrentUser` scope plus
  application entropy) before being written to disk. Since V1.7.0 each secret value is additionally
  encrypted with **AES-256-GCM** using a key derived from the machine's `MachineGuid` before it
  reaches DPAPI. Another user or another machine cannot decrypt it. A failed GCM authentication is
  treated as "absent" (fail-closed) rather than falling back to plaintext.
- **Log directory allow-list**
  Only `.txt` / `.log` files under `%AppData%\obs-studio\logs` and `%AppData%\obs-studio\crashes`
  can be read, and the resolved real path is **verified a second time** to prevent `..` traversal
  outside the allow-list. A single log is capped at 8 MB, reading only the tail beyond that.
- **Outbound requests are https-only and restricted to official domains**
  Opening external links and fetching update manifests or knowledge-base content all check that the
  scheme is `https` and that the host is on the project's declared allow-list. If a URL constant is
  tampered with or replaced, we would rather fail than send the user to a third-party site.
- **Internal / loopback addresses are rejected (SSRF protection)**
  Cloud AI endpoints must be `https` and may not point at the local machine or the LAN. Beyond string
  checks, the actual peer IP is verified after the connection is established (to defeat DNS
  rebinding); internal addresses are blocked. (The loopback connection to OBS's own obs-websocket is
  an expected, legitimate path.)
- **Log and diagnostic redaction**
  Log analysis, exported reports and live alerts redact public IPs, domains and user directory paths
  before display. Loopback and private addresses are kept, because they are useful for network
  troubleshooting and are not a privacy leak.
- **Revertible configuration writes**
  Features that write user configuration follow a "back up first, stay revertible" rule, so an
  interrupted operation cannot leave a broken configuration behind.

## Response expectations

This project is maintained by one person. Security response is **best effort** and there is no
commercial SLA. We commit to:

- **acknowledge and thank first, then fix**: we confirm receipt of a valid report and thank the
  reporter before scheduling a fix and release;
- not publishing exploitation details before a fixed version ships;
- crediting the reporter in the release notes after the fix, unless they prefer otherwise;
- explaining our reasoning if a report turns out not to be a security vulnerability (for example,
  it requires the user to disable a safety setting, or it requires an already-compromised local
  account).

## Out of scope

The following are generally not treated as security vulnerabilities:

- anything that requires the attacker to already have administrator or logged-in user access to the
  machine;
- problems caused by the user modifying local configuration files, installers or executables
  themselves;
- vulnerabilities in third-party components (OBS Studio itself, plugins, system runtimes) — please
  report those upstream;
- missing "nice to have" hardening (no code signing, no anti-debugging) without an actual
  exploitable path.

---

See also: [README](README.md) · [Contributing](CONTRIBUTING.md) · [Code of Conduct](CODE_OF_CONDUCT.md)
