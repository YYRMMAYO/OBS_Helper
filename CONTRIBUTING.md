# 贡献指南 · Contributing Guide

**简体中文** · [English](#english)

感谢你愿意为「OBS帮助助手」（OBS Helper）出一份力。
本文说明本项目的技术约定、构建与测试方式，以及提交 Issue / Pull Request 时的要求。

> **语言**：中文或英文都可以，**也可以用中英双语**。用你最舒服的语言写就行 ——
> 表达清楚比语言正确重要得多，不必担心语法或措辞。

提交贡献即表示你同意遵守本仓库的 [行为准则](CODE_OF_CONDUCT.md)。
本项目代码以 [MIT 许可](LICENSE) 发布，你的贡献将以同一许可授权。

---

## 1. 当前维护的代码在哪里

- **当前维护的代码在 `NOBS/` 目录**：Windows 原生 WPF 桌面应用。
- 项目采用 **.NET 10 + net6.0 双目标**（`net10.0-windows` 为主构建，面向 Windows 10 / 11；
  `net6.0-windows` 为兼容构建，面向 Windows 7 SP1 ~ Windows 11）。两个目标框架共用同一份源码，
  差异只体现在发布产物与安装包的最低系统版本要求上。
- **主程序全程零第三方 NuGet 包（纯 BCL）**：`OBS_Helper.Wpf` 工程**没有任何 `PackageReference`**。
  DPAPI（`System.Security.Cryptography.ProtectedData`）、注册表、`SystemEvents` 等能力
  在 Windows 桌面工作负载中已内置，额外引包反而会触发 `NU1510`。
  请在提交前确认你没有给主工程新增任何 NuGet 依赖。
  （例外：单元测试工程 `OBS_Helper.Wpf.Tests` 使用 xUnit 与 `Microsoft.NET.Test.Sdk`，
  这属于仅测试期依赖，不影响主程序。）
- 仓库根目录下的其它目录不属于当前 Windows 端维护主线，改动请聚焦 `NOBS/`。

## 2. 代码组织：纯逻辑放 `*Core.cs`

- **纯逻辑代码放 `*Core.cs`，且必须零 WPF 依赖**（只用 BCL）。
  现有例子：`Services/Tools/*Core.cs`、`Services/Obs/*Core.cs`、`Services/Shell/RecordWatchdogCore.cs` 等。
- 这样做的原因是：单元测试工程 `NOBS/OBS_Helper.Wpf.Tests/` **通过 `<Compile Include>`
  直接链接这些源文件**，而不是引用整个 WPF 工程（避免 RID / `UseWPF` / SDK 冲突）。
  被链接的文件必须只依赖 BCL，才能在普通 `net10.0` 测试运行时中编译与执行。
- 因此：**新增一个纯逻辑文件后，要同步在
  `NOBS/OBS_Helper.Wpf.Tests/OBS_Helper.Wpf.Tests.csproj` 的 `<Compile Include>` 列表里登记**，
  否则测试工程看不到它。
- 涉及 WPF 控件、导航、窗口生命周期的代码不要放进 `*Core.cs`。

## 3. 界面文案必须中英成对

- 所有界面文案定义在两份文案表里：
  - `NOBS/OBS_Helper.Wpf/Localization/StringTableZhHans.cs`
  - `NOBS/OBS_Helper.Wpf/Localization/StringTableEnUs.cs`
- **新增或修改文案时，两份必须同步**：键名一致、键集由单元测试钉死，
  只改一份会导致测试失败。
- XAML 中引用文案的键以 `Loc.` 前缀标识。
- **占位符个数要对得上**：`Strings.T("键", 实参…)` 的实参个数必须与该键在文案表里的
  `{0}`/`{1}` 个数一致。少传会让界面直接显示字面量 `{0}`，多传则参数被静默丢弃 ——
  这两种错误编译器都看不见，所以有一条常驻测试（`StringsCallSiteTests`）静态扫描全部调用点。
- **拼接键要留兜底**：像 `Strings.T("env.item." + key + ".label")` 这种拼接，缺键时
  `Strings.T` 会**原样返回键名**（非空），因此不能用「取到非空就用」来判断，
  必须比较「取到的值是否等于键名」。

## 4. 构建与打包

构建与打包统一走脚本：`NOBS/build.ps1`。它会完成：

1. 自包含发布（含 .NET 运行时，目标机无需另装运行时）；
2. 调用 Inno Setup 生成安装包（未安装 Inno Setup 时会警告并跳过安装包，便携包仍可用；
   可用 `-SkipInstaller` 显式跳过）；
3. 打便携压缩包（免安装解压即用）；
4. 基于上一版本清单生成**增量更新包**（仅含变更文件 + `update_manifest.json`；
   跳版本发布可用 `-DeltaBaseVersion <版本>` 指定基准清单）。

脚本开头的注释块写明了各参数的用途，动手前请先读一遍，不要绕过脚本手工拼发布产物。

## 5. 跑测试

```powershell
dotnet test NOBS/OBS_Helper.Wpf.Tests/OBS_Helper.Wpf.Tests.csproj -c Release
```

- 新增纯逻辑（`*Core.cs`）**必须附带单元测试**，且在同一个 PR 内提交。
- 修 Bug 时优先补一条能复现该 Bug 的测试，再修实现。

## 6. 其它自检手段

- **XAML 资源引用体检**：

  ```powershell
  python NOBS/scripts/check_resources.py
  ```

  `{StaticResource X}` / `{DynamicResource X}` 里的键名拼错在编译期不会报错，
  要等运行时导航到该页面才抛异常；该脚本会把 `Themes/` 下的全局资源键、
  页面内的局部 `x:Key` 以及文案表里的 `Loc.` 键一并解析后做交叉检查。

- **无界面自检**：

  ```powershell
  $env:OBS_SELFTEST = "1"
  ```

  以 `OBS_SELFTEST=1` 启动应用可在无界面环境下走完所有路由并输出自检结果，
  适合拦截「编译通过但一运行就炸」的问题（例如新页面资源缺失、路由与页面名不一致）。

## 7. 提交信息规范

以下是本仓库既有提交的实际写法（可用 `git log --oneline` 自行查看）：

```
release(V2.9.6): 更名 OBS帮助助手 + 应用图标与仓库图标统一 + 文档同步
docs(review): 如实标注「安装向导语言页」的证据边界
fix(installer): 中文语言文件随仓库固定，修 CI 编不出安装包
fix(log): 日志解析适配真实 OBS 日志 + 实时预警日志扩展名修正
feat(plugins): 目录 v1.4 全量复核 + 维护状态标注 + OBS 32.x 插件目录体检
feat(onboarding): 首启四步新手教程 + 设置页可随时重看
docs: 校正构建产物清单与 Release 资产实际约定
chore: 新增 scripts/verify_delta.py 增量包发布校验工具（模拟升级+全文件SHA256比对）
refactor: 清理旧仓库结构——移除 WinForms/Tests 遗留，Windows 端统一至 NOBS/OBS_Helper.Wpf
```

**规范：`<类型>(<范围>): <简述>`**

- **类型**（项目已在用的）：
  | 类型 | 适用场景 |
  | --- | --- |
  | `feat` | 新功能 |
  | `fix` | 修 Bug |
  | `docs` | 文档、说明、审查记录 |
  | `release` | 版本发布（版本号与文档同步、打包发布） |
  | `chore` | 构建脚本、工具链、仓库杂项 |
  | `refactor` | 不改变行为的代码重构 |

- **范围**：模块名（如 `installer` / `log` / `toolbox` / `plugins` / `onboarding` / `ci` / `theme`），
  或 `release` 提交里的版本号（如 `release(V2.9.6)`）。
  范围可省略（`docs: …`），改动横跨多个模块时也建议省略。
- **简述**写清「改了什么」；一次提交包含多项改动时，用 `+` 或 `——` 分列要点，结尾不加句号。
  用中文或英文都可以，**同一仓库里保持一致即可**（既有历史以中文为主）。
- **不要**写 `update`、`fix bug`、`.` 这类无信息量的简述。
- 历史中早期提交存在没有前缀的写法（如 `README: …`、`问题库 v2.1：…`）。新提交请统一使用上表的前缀风格。

## 8. Pull Request 要求

提交 PR 时，描述里必须说明三件事：

1. **改了什么** —— 涉及的文件与行为变化；
2. **为什么改** —— 要解决的问题或依据（Bug 请附复现路径）；
3. **怎么验证的** —— 跑了哪些命令（构建 / `dotnet test` / `check_resources.py` / `OBS_SELFTEST=1` 自检）
   以及手工验证步骤和结果。**没有验证说明的 PR 不会被合并。**

仓库自带 [PR 模板](.github/pull_request_template.md)，会把上面几项与下面的约定列成勾选项。

此外：

- **涉及用户配置写入的功能，必须说明备份与回滚路径**：写入了哪些文件、写入前是否有备份、
  出现异常时用户如何恢复到原状态。
- **新增文案必须中英成对**（见第 3 节），PR 描述里请注明两份文案表都已更新。
- **新增纯逻辑必须带单元测试**（见第 2、5 节），并已登记到测试工程的 `<Compile Include>` 列表。
- 保持改动范围聚焦：一个 PR 尽量只解决一件事，不要把无关的格式化或重构混进来。
- 不要提交构建产物、密钥或含真实流密钥的日志。

## 9. 反馈与联系

| 场景 | 渠道 |
| --- | --- |
| 普通 Bug / 功能建议 | 仓库的 [Issue 表单](https://github.com/YYRMMAYO/OBS_Helper/issues/new/choose)（表单会引导你补齐环境信息） |
| **不适合公开讨论的问题**（个人隐私、人身安全、行为准则事件等） | 邮件 <752139192@qq.com> |
| **贡献 / 发布相关的问题**（提交信息、打包流程、能否合并、如何署名等） | 邮件 <752139192@qq.com> |
| 安全问题（漏洞） | **请勿在公开 Issue 中披露细节**，走 [SECURITY.md](SECURITY.md) 的私密通道（GitHub Security 私密报告或邮件） |

> **为什么给的是邮箱**：GitHub 并没有提供面向任意用户的私信功能 ——
> 以前文档里写的「GitHub 私信维护者」实际上无处可点，等于给了一条走不通的路。
> 现在统一改成邮件，收到后会在方便的时候尽快回复。
>
> 邮件也可以用中文或英文写。

---

再次感谢你的贡献。

---

<a id="english"></a>

# Contributing Guide

[简体中文](#贡献指南--contributing-guide) · **English**

Thanks for taking the time to help with **OBS Helper**, a native Windows troubleshooting app for
OBS Studio.

> **Language**: Chinese, English, or a mix of both — use whichever you are most comfortable with.
> Being clear matters far more than being grammatically perfect.

By contributing you agree to follow this repository's [Code of Conduct](CODE_OF_CONDUCT.md).
The project is released under the [MIT License](LICENSE); your contribution is licensed the same way.

## 1. Where the maintained code lives

- **The actively maintained code is in `NOBS/`** — a native Windows WPF desktop app.
- The project targets **.NET 10 and net6.0 at once** (`net10.0-windows` is the primary build for
  Windows 10 / 11; `net6.0-windows` is the compatibility build for Windows 7 SP1 and later).
  Both target frameworks share the same source; they differ only in the published artifacts and the
  minimum OS version required by the installer.
- **The main app has zero third-party NuGet packages (pure BCL)**: the `OBS_Helper.Wpf` project has
  **no `PackageReference` at all**. DPAPI (`System.Security.Cryptography.ProtectedData`), the
  registry and `SystemEvents` are already available in the Windows desktop workload; adding a
  package would only trigger `NU1510`. Please make sure your change does not add a NuGet dependency
  to the main project. (Exception: the test project `OBS_Helper.Wpf.Tests` uses xUnit and
  `Microsoft.NET.Test.Sdk`; those are test-time only and do not affect the app.)
- Other directories in the repository root are not part of the current Windows mainline. Keep
  changes focused on `NOBS/`.

## 2. Code organization: pure logic goes in `*Core.cs`

- **Pure logic belongs in `*Core.cs` files with zero WPF dependencies** (BCL only). Existing
  examples: `Services/Tools/*Core.cs`, `Services/Obs/*Core.cs`,
  `Services/Shell/RecordWatchdogCore.cs`.
- The reason: the test project `NOBS/OBS_Helper.Wpf.Tests/` **links those source files directly via
  `<Compile Include>`** instead of referencing the whole WPF project (which would drag in RID /
  `UseWPF` / SDK conflicts). Linked files must depend on the BCL only so they can compile and run in
  a plain `net10.0` test host.
- Therefore: **when you add a pure-logic file, register it in the `<Compile Include>` list of
  `NOBS/OBS_Helper.Wpf.Tests/OBS_Helper.Wpf.Tests.csproj`** — otherwise the test project cannot see it.
- Code that touches WPF controls, navigation or window lifetimes does not belong in `*Core.cs`.

## 3. UI text must exist in both Chinese and English

- All UI text lives in two tables:
  - `NOBS/OBS_Helper.Wpf/Localization/StringTableZhHans.cs`
  - `NOBS/OBS_Helper.Wpf/Localization/StringTableEnUs.cs`
- **Every new or changed string must be updated in both**: identical keys, and the key sets are
  pinned by unit tests — updating only one table fails the build's tests.
- XAML references text keys with the `Loc.` prefix.
- **Placeholder counts must match**: the number of arguments passed to `Strings.T("key", …)` must
  equal the number of `{0}` / `{1}` slots in that key. Too few shows a literal `{0}` in the UI; too
  many silently drops arguments. Neither is visible to the compiler, which is why a dedicated test
  (`StringsCallSiteTests`) statically scans every call site.
- **Give concatenated keys a real fallback**: with something like
  `Strings.T("env.item." + key + ".label")`, a missing key makes `Strings.T` **return the key name
  itself** (non-empty), so "use it when it is non-empty" does not work — compare the result against
  the key name instead.

## 4. Build and packaging

Use the script: `NOBS/build.ps1`. It performs:

1. a self-contained publish (the .NET runtime is bundled; the target machine needs nothing installed);
2. the Inno Setup installer (if Inno Setup is missing it warns and skips the installer while the
   portable zip is still produced; pass `-SkipInstaller` to skip it explicitly);
3. the portable zip;
4. an **incremental update package** built by diffing against the previous version's manifest
   (changed files only, plus `update_manifest.json`; use `-DeltaBaseVersion <version>` to publish
   across several versions at once).

The comment block at the top of the script documents every parameter. Read it first, and do not
hand-assemble release artifacts around the script.

## 5. Running the tests

```powershell
dotnet test NOBS/OBS_Helper.Wpf.Tests/OBS_Helper.Wpf.Tests.csproj -c Release
```

- New pure logic (`*Core.cs`) **must come with unit tests**, in the same pull request.
- When fixing a bug, add a test that reproduces it first, then fix the implementation.

## 6. Other self-checks

- **XAML resource reference check**:

  ```powershell
  python NOBS/scripts/check_resources.py
  ```

  A mistyped key in `{StaticResource X}` / `{DynamicResource X}` does not fail the build — it throws
  only when that page is navigated to at runtime. The script resolves the global resource keys under
  `Themes/`, the per-page `x:Key` entries and the `Loc.` keys from the text tables, then cross-checks
  every reference.

- **Headless self-test**:

  ```powershell
  $env:OBS_SELFTEST = "1"
  ```

  Starting the app with `OBS_SELFTEST=1` walks every route without a UI and writes a result file.
  It catches the "compiles fine, blows up on launch" class of problems (missing page resources,
  route/page name mismatches).

## 7. Commit message conventions

These are real messages from this repository (see `git log --oneline`):

```
release(V2.9.6): 更名 OBS帮助助手 + 应用图标与仓库图标统一 + 文档同步
docs(review): 如实标注「安装向导语言页」的证据边界
fix(installer): 中文语言文件随仓库固定，修 CI 编不出安装包
fix(log): 日志解析适配真实 OBS 日志 + 实时预警日志扩展名修正
feat(plugins): 目录 v1.4 全量复核 + 维护状态标注 + OBS 32.x 插件目录体检
feat(onboarding): 首启四步新手教程 + 设置页可随时重看
docs: 校正构建产物清单与 Release 资产实际约定
chore: 新增 scripts/verify_delta.py 增量包发布校验工具（模拟升级+全文件SHA256比对）
refactor: 清理旧仓库结构——移除 WinForms/Tests 遗留，Windows 端统一至 NOBS/OBS_Helper.Wpf
```

**Format: `<type>(<scope>): <summary>`**

- **Types** already in use:

  | Type | Use for |
  | --- | --- |
  | `feat` | New feature |
  | `fix` | Bug fix |
  | `docs` | Documentation, notes, review records |
  | `release` | Version release (version bump, docs, packaging) |
  | `chore` | Build scripts, tooling, repository housekeeping |
  | `refactor` | Behaviour-preserving code restructuring |

- **Scope**: a module name (`installer` / `log` / `toolbox` / `plugins` / `onboarding` / `ci` /
  `theme`) or the version for `release` commits (`release(V2.9.6)`). The scope may be omitted
  (`docs: …`), and should be omitted when a change spans several modules.
- **The summary** should say what changed; for multi-part changes separate the points with `+` or
  `——`, and do not end with a period. Chinese or English are both fine — just stay consistent within
  the repository (the existing history is mostly Chinese).
- Do **not** use empty summaries like `update`, `fix bug` or `.`.
- Very early history has messages without a prefix (e.g. `README: …`). New commits should use the
  prefixed style above.

## 8. Pull request requirements

A pull request description must cover three things:

1. **What changed** — the files and behaviour affected;
2. **Why** — the problem solved or the reasoning (for bugs, include the reproduction path);
3. **How you verified it** — which commands you ran (build / `dotnet test` / `check_resources.py` /
   `OBS_SELFTEST=1`) plus your manual steps and their results. **PRs without verification notes are
   not merged.**

The repository ships a [PR template](.github/pull_request_template.md) that turns the points above
and the conventions below into a checklist.

Additionally:

- **Features that write user configuration must document backup and rollback**: which files are
  written, whether a backup is taken first, and how a user restores the previous state if something
  goes wrong.
- **New UI text must exist in both languages** (section 3); mention in the PR that both tables were updated.
- **New pure logic must ship with unit tests** (sections 2 and 5) and be registered in the test
  project's `<Compile Include>` list.
- Keep the change focused: one pull request should solve one thing — avoid unrelated formatting or
  refactoring.
- Do not commit build artifacts, secrets, or logs containing real stream keys.

## 9. Feedback and contact

| Situation | Channel |
| --- | --- |
| Ordinary bugs / feature requests | The repository's [issue forms](https://github.com/YYRMMAYO/OBS_Helper/issues/new/choose), which prompt you for the environment details |
| **Anything not suitable for a public thread** (personal privacy, safety, Code of Conduct incidents) | Email <752139192@qq.com> |
| **Contribution / release questions** (commit conventions, packaging, whether something can be merged, how you will be credited) | Email <752139192@qq.com> |
| Security vulnerabilities | **Do not disclose details in a public issue** — use the private channels in [SECURITY.md](SECURITY.md) (GitHub Security advisories or email) |

> **Why email**: GitHub does not offer direct messages to arbitrary users, so the older guidance
> ("DM the maintainer on GitHub") pointed at something that does not exist. Everything private now
> goes through email, and you will get a reply as soon as reasonably possible.
>
> Emails may be written in Chinese or English.

---

Thanks again for contributing.
