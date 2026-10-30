# 分支与文档分工规则（只维护 `master`）

> **V3.3.0 起 `dev` 分支已删除。** 本文保留为「文档该放哪里、怎么命名」的规则；
> 历史 `01` / `02` / `03` 标签仍在 Release 页可查，但不再产生新的 DEV 构建，也不再维护 DEV 文档。

- 适用范围：`README.md`、`README.en.md`、`NOBS/README.md`、`NOBS/README.en.md`、
  `RELEASE_NOTES_v*.md`、`NOBS/docs/**`、根目录 `docs/**`，以及以后新增的任何用户文档 /
  发布说明 / 设计稿。
- 违反规则的典型后果：GitHub 首页（默认分支 `master`）看不到最新介绍；
  用户装的是正式版、读到的却是过期说明；同一份文档出现两个互相矛盾的版本。

---

## 1. 分支定位

| 分支 | 定位 | 版本号 | 谁能看到 |
| --- | --- | --- | --- |
| `master` | **正式版 / 稳定通道**：功能收敛、完整回归、可直接发 Release | `V3.3.0` 这类 `V主.次.修` | 所有用户（GitHub 默认分支、正式 Release、网盘、应用内更新） |

> DEV 测试通道已停用；不再接受「先提交到 dev、之后再回流」的工作方式。

## 2. 文档归属

| 文档 | 位置 | 要求 |
| --- | --- | --- |
| 根 `README.md` / `README.en.md` | 仓库根 | 面向用户的完整说明（功能 · 使用 · 安装 · 质量基线 · 许可 / 开源署名）；中英同步 |
| `NOBS/README.md` / `NOBS/README.en.md` | `NOBS/` | 开发向完整文档（架构 · 质量基线 · 逐版本说明 · 代码清单）；中英同步 |
| `RELEASE_NOTES_vX.Y.Z.md` | `NOBS/` | 正式版发布说明；版本号与 `csproj` / `.iss` 一致 |
| `NOBS/docs/**` | `NOBS/docs/` | 架构、代码清单、设计稿、实现状态、QA 记录 |
| 根 `docs/**` | 仓库根 | 无障碍、贡献、安全、行为准则、发版规则等社区文档 |
| 开源署名 | `NOBS/docs/OPEN_SOURCE_ATTRIBUTION.md` | 第三方项目 / 许可证 / 用途边界；README 与「关于」界面同步链接 |

## 3. 修改规则

1. **所有文档只改 `master`。**
2. **中英成对**：`README.md` 与 `README.en.md`、`NOBS/README.md` 与 `NOBS/README.en.md`
   的结构、数字与链接必须一致。
3. **数字以脚本实测为准**：版本号、单测数、自检项数、文案键数、路由数等由
   `NOBS/scripts/check_docs.py` 校验；改完代码先跑脚本，再改文档。
4. **每个 Release 必须写清「新增了什么 / 修复了什么」**，结构与验收见
   [`RELEASE_RULES.md`](RELEASE_RULES.md)。
5. **不回填历史版本说明**：`V3.1.1` 及更早的发布说明按原样保留；新版本另起文件。
6. **文档里的「已验证 / 未验证」必须如实**：没有实测的能力写「未验证」，
   不把静态检查写成读屏实测。

## 4. 发布前最小检查

```powershell
dotnet test NOBS/OBS_Helper.Wpf.Tests/OBS_Helper.Wpf.Tests.csproj
python NOBS/scripts/check_resources.py
python NOBS/scripts/check_accessibility.py
python NOBS/scripts/check_docs.py
$env:OBS_SELFTEST=1; NOBS/OBS_Helper.Wpf/bin/Release/net10.0-windows/win-x64/publish/OBS_Helper.exe
```
