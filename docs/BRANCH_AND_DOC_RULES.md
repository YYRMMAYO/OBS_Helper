# 分支与文档分工规则（master / dev）

> 这份文件是**规则**，不是建议。以后在这个仓库里新增或修改 README、发布说明、设计文档时，先按这里的表格判断
> 「该放哪条分支」，再动手。**正式版的文档永远属于 `master`；`dev` 只放 DEV 测试通道自己的东西。**

- 适用范围：`README.md`、`README.en.md`、`NOBS/README.md`、`NOBS/README.en.md`、`NOBS/RELEASE_NOTES_*.md`、
  `NOBS/docs/**`、根目录 `docs/**`，以及以后新增的任何用户文档 / 发布说明 / 设计稿。
- 违反规则的典型后果：GitHub 首页（默认分支 `master`）看不到最新介绍；用户装的是正式版、读到的却是 DEV 的说明；
  两条分支的 README 越走越远，合并时全是冲突。

---

## 1. 两条分支的定位

| 分支 | 定位 | 版本号 | 谁能看到 |
| --- | --- | --- | --- |
| `master` | **正式版 / 稳定通道**：功能收敛、完整回归、可直接发 Release | `V3.1.1` 这类 `V主.次.修` | 所有用户（GitHub 默认分支、正式 Release、蓝奏云、应用内更新） |
| `dev` | **DEV 测试通道**：更超前，可能不稳定 | `01`、`02`、`03` …（**纯数字，不带 V**） | 只想尝鲜、能接受「可能坏」的用户（dev Release 页） |

---

## 2. 文档归属表（硬性）

| 文档 | 放哪条分支 | 说明 |
| --- | --- | --- |
| 根 `README.md` / `README.en.md` | **master + dev 都放** | 两条分支的**模块结构必须一致**：导航、版本通道、宣传视频、捐赠、许可。**唯一允许的差异**是 dev 多一段「DEV 测试通道」说明，且版本号 / 发布说明链接指向 DEV 自己的编号 |
| `NOBS/README.md` / `NOBS/README.en.md`（完整文档） | **master + dev 都放** | 同上：功能、诊断、隐私、安装、构建、工程结构、质量基线、已知边界、捐赠等模块保持一致；dev 只多 DEV 专属段落 |
| `NOBS/RELEASE_NOTES_vX.Y.Z.md`（正式版发布说明） | **只放 master** | 例如 `RELEASE_NOTES_v3.1.1.md`。**不得只写在 dev 上**，也不得把正式版发布说明提交到 dev 后就当完成了 |
| DEV 版本说明（`01` / `02` / `03` …） | **只放 dev** | 文件名建议 `RELEASE_NOTES_DEV_0X.md`；不得混进 master |
| `NOBS/docs/DEV_CHANNEL.md` | **master + dev 都放** | 它描述「两条通道的编号与更新机制」，是机制文档，不是 DEV 版本说明，两条分支都需要 |
| 根 `docs/BRANCH_AND_DOC_RULES.md`（本文件） | **master + dev 都放** | 规则本身要跟着两条分支走，内容保持一致 |
| 用户文档（功能、安装、隐私、FAQ、架构、代码清单…） | **master** | 面向所有用户/贡献者；写完后再同步到 dev |
| dev 专属设计稿、实验记录、未发布特性的说明 | **dev** | 例如 `NOBS/docs/DEV_*_DESIGN.md`；功能转正时把必要内容整理进 master 的用户文档，原稿可以留在 dev |
| 安全策略 / 行为准则 / 贡献指南 / 许可 | **master**（dev 同步） | 社区文件属于正式仓库门面 |

> **一句话判断**：**「正式版用户会不会需要读它？」** 会 → master；只在 DEV 通道有意义 → dev。

---

## 3. 三条硬规则

1. **正式版文档只能先落 `master`。**
   不允许出现「README / 发布说明只提交到 dev」的情况；如果已经发生，先在 master 补齐，再处理 dev。
2. **dev 专属内容不得混入 `master`。**
   DEV 编号、DEV 更新规则、未发布功能的实验说明，只留在 dev；master 只在「版本通道」一节里说明 DEV 的存在与规则。
3. **共享章节先改 master，再同步 dev。**
   改 README 的功能介绍、安装说明、捐赠、许可等共享模块时，**先在 master 提交**，再用 `merge` / `rebase`
   或按文件拷贝同步到 dev。禁止在 dev 单独改写共享章节而不回流 master —— 这正是 V3.1.0 文档一度只在 dev 上的原因。

---

## 4. README 的「同模块」约定

`master` 与 `dev` 的 README 是**同一份文档的两个通道版本**，不是两份各自生长的文档。允许的差异只有：

| 位置 | master | dev |
| --- | --- | --- |
| 版本号 | `V3.1.1` | `DEV 01` / `02` …（当前构建号） |
| 发布说明链接 | `NOBS/RELEASE_NOTES_v3.1.1.md` | `NOBS/RELEASE_NOTES_DEV_0X.md` |
| 通道说明块 | 默认分支是 master（正式版） | 默认分支是 master，当前 `dev` 为 DEV 测试通道 |
| 新增功能段 | 已发布的正式功能 | 可在正式内容之前加「DEV 0X 新增」（未发布内容） |

除此之外的标题顺序、章节名、表格列、徽章、捐赠区块、许可说明**保持一致**；
以后新增章节时，两条分支一起加，不要只加在一条上。

---

## 5. 发布流程（master）

1. **改代码 + 单元测试**：纯逻辑进 `*Core.cs`，同步补测试。
2. **升版本号**：`NOBS/OBS_Helper.Wpf/OBS_Helper.Wpf.csproj` 的 `Version` / `FileVersion` / `AssemblyVersion`，
   以及 `NOBS/OBS_Helper.Wpf/OBS_Helper_Setup.iss` 的 `MyAppVersion`。
3. **写发布说明**：`NOBS/RELEASE_NOTES_vX.Y.Z.md`（新功能、修复、兼容性、已知边界、下载清单）。
4. **更新 README**：根 README / `NOBS/README.md` 的版本号、徽章、功能段、发布说明链接；英文两份同步。
5. **验证**：`dotnet test` 至少 3 轮 + 双目标构建 + `OBS_SELFTEST=1` 自检 +
   `scripts/check_accessibility.py` / `check_docs.py` / `check_resources.py`。
6. **出包**：`NOBS/build.ps1`（安装包 / 便携包 / 增量包 / 知识库资产）。
7. **提交 + 打 tag + Release**：`git tag VX.Y.Z`，推送 master 与 tag，在 Release 页上传 `PAKE/windows/` 的产物。
8. **同步 dev**：把 master 的共享文档同步到 dev，避免两条分支的 README 分叉。

## 6. DEV 流程（dev）

1. DEV 构建编号自增（`01`、`02` …），**不带字母 V**；只从 dev Release 页更新。
2. DEV 专属说明放 `NOBS/RELEASE_NOTES_DEV_0X.md`，只在 dev 提交。
3. 共享 README 章节从 master 同步，不单独改写；dev 只额外增加「DEV 0X 新增」与通道说明。
4. 功能转正时：代码与测试进 master，用户文档改写进 master，DEV 说明可留在 dev 作历史记录。

---

## 7. 提交前检查清单

- [ ] 这次新增/修改的文档，是否按第 2 节表格放对了分支？
- [ ] 正式版 README / 发布说明是否已经在 `master` 上（而不是只在 dev）？
- [ ] dev 是否只剩 DEV 专属内容（编号、未发布特性、DEV 发布说明）？
- [ ] 两条分支的 README 共享模块是否一致（标题顺序、章节、徽章、捐赠、许可）？
- [ ] 版本号、徽章、安装包文件名、发布说明链接是否全部指向同一个版本？
- [ ] `check_docs.py` 是否通过（文档里的数字与代码一致）？

## 8. 同步命令示例

```bash
# 把 master 的共享文档同步到 dev（在 dev 分支、工作区干净时执行）
git checkout dev
git checkout master -- README.md README.en.md NOBS/README.md NOBS/README.en.md docs/BRANCH_AND_DOC_RULES.md
# 然后只在 dev 上补「DEV 0X 新增」与 DEV 发布说明，再提交
git commit -m "docs(dev): 同步 master 共享文档 + DEV 0X 说明"
```

```bash
# 把正式版文档从 dev 搬回 master（发现文档只在 dev 时）
git checkout master
git checkout dev -- NOBS/README.md NOBS/README.en.md   # 仅在确认内容属于正式版时使用
# 再按 master 的版本号与发布说明改写，提交到 master
```

---

## 9. 已发生的纠正（V3.1.1）

- V3.1.0 的完整 README 与捐赠入口曾只提交在 `dev`（提交 `7350425`），导致 GitHub 默认分支的首页仍是 V3.0.0。
- V3.1.1 已把完整文档、英文文档、捐赠素材与 `DEV_CHANNEL.md` **搬回 `master`**，并在 master 写出 V3.1.1 的介绍与发布说明。
- 从 V3.1.1 起，按本文件执行：**正式版文档先落 master；dev 只保留 DEV 专属内容。**
