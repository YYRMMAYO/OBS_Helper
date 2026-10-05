# 贡献指南（Contributing Guide）

感谢你愿意为「OBS 排障助手」（OBS Helper）出一份力。
本文说明本项目的技术约定、构建与测试方式，以及提交 Issue / Pull Request 时的要求。

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
release(V2.9.3): 随包内容全量英译 + 一键部署录制环境 + 获取/反馈通道 + Win7 兼容构建
docs(review): 如实标注「安装向导语言页」的证据边界
fix(installer): 中文语言文件随仓库固定，修 CI 编不出安装包
fix(log): 日志解析适配真实 OBS 日志 + 实时预警日志扩展名修正
feat(plugins): 目录 v1.4 全量复核 + 维护状态标注 + OBS 32.x 插件目录体检
feat(onboarding): 首启四步新手教程 + 设置页可随时重看
docs: 校正构建产物清单与 Release 资产实际约定
chore: 新增 scripts/verify_delta.py 增量包发布校验工具（模拟升级+全文件SHA256比对）
refactor: 清理旧仓库结构——移除 WinForms/Tests 遗留，Windows 端统一至 NOBS/OBS_Helper.Wpf
```

**规范：`<类型>(<范围>): <中文简述>`**

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
  或 `release` 提交里的版本号（如 `release(V2.9.3)`）。
  范围可省略（`docs: …`），改动横跨多个模块时也建议省略。
- **简述用中文**，写清「改了什么」；一次提交包含多项改动时，用 `+` 或 `——` 分列要点。
  结尾不加句号。
- **不要**写 `update`、`fix bug`、`.` 这类无信息量的简述。
- 历史中早期提交存在没有前缀的写法（如 `README: …`、`问题库 v2.1：…`）。新提交请统一使用上表的前缀风格。

## 8. Pull Request 要求

提交 PR 时，描述里必须说明三件事：

1. **改了什么** —— 涉及的文件与行为变化；
2. **为什么改** —— 要解决的问题或依据（Bug 请附复现路径）；
3. **怎么验证的** —— 跑了哪些命令（构建 / `dotnet test` / `check_resources.py` / `OBS_SELFTEST=1` 自检）
   以及手工验证步骤和结果。**没有验证说明的 PR 不会被合并。**

此外：

- **涉及用户配置写入的功能，必须说明备份与回滚路径**：写入了哪些文件、写入前是否有备份、
  出现异常时用户如何恢复到原状态。
- **新增文案必须中英成对**（见第 3 节），PR 描述里请注明两份文案表都已更新。
- **新增纯逻辑必须带单元测试**（见第 2、5 节），并已登记到测试工程的 `<Compile Include>` 列表。
- 保持改动范围聚焦：一个 PR 尽量只解决一件事，不要把无关的格式化或重构混进来。
- 不要提交构建产物、密钥或含真实流密钥的日志。

## 9. 反馈问题

- 普通 Bug 与功能建议：请通过仓库的 Issue 表单提交，表单会引导你补齐环境信息。
- **安全问题：请勿在公开 Issue 中披露细节**，走 [SECURITY.md](SECURITY.md) 里的私密报告通道。

---

再次感谢你的贡献。
