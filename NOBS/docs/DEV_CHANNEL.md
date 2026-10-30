> **已停用（V3.3.0，2026-10-30）**：`dev` 分支已删除，正式版只走 `master`。
> 本文保留下来仅作为历史规则说明；历史 `01` / `02` / `03` 标签仍可在 Release 页查到，
> 但不再产生新的 DEV 构建，也不再维护 DEV 文档。

# DEV 测试通道（`dev` 分支）

> 本文只描述 **dev 分支**的版本编号与更新逻辑。正式版（`master`）的版本体系与更新链路不变。

## 1. 两条通道的分工

| | 正式版（`master`） | DEV 测试版（`dev`，本分支） |
| --- | --- | --- |
| 版本号 | `V3.3.0`（`V主.次.修`） | `01`、`02`、`03` …（纯数字、**不带字母 V**） |
| 定位 | 稳定优先，经过完整回归与发布验证 | **更超前**，开发中的功能先落地，**可能含不稳定功能** |
| tag / Release | `V3.3.0` | `01` / `02` / `03` |
| 更新来源 | 正式 Release 页 · 蓝奏云 · 应用内增量更新 | **只**从 dev Release 页检查更新 |
| 出包 | 安装包 + 便携包 + 增量包 | 目前**不出包**（后续会出，命名规则与正式版一致） |

## 2. 编号规则

- dev tag 只接受 1~3 位纯数字：`01`、`02`、`03`…；`V01`、`3.1.0`、`01-beta` 一律**不是** dev tag；
- 「当前是第几个 dev 版」由 `UpdateChannel.DevBuildNumber`（`OBS_Helper.Wpf/Services/Update/UpdateChannel.cs`）给出，
  **每出一个 dev 构建 +1**。忘记加 1 的后果是：已经装了上一个 dev 版的用户收不到更新提示；
- 两类 tag 可以同时存在于同一个仓库：dev 只解析纯数字 tag，正式版只解析 `V3.3.0` 这类 tag，互不干扰。

## 3. 更新模块（dev 分支）

| 环节 | 行为 |
| --- | --- |
| 检查更新 | 先查 dev Release 列表（`/releases`，取 `tag_name` 为纯数字的最大序号）；查不到再回退 tag 列表 —— 只推了 tag 没建 Release 也能提示 |
| 版本比较 | 按构建序号比较（`CurrentChannelVersion` = `DevBuildNumber`），绝不拿 `3.1.0` 和 `01` 比大小 |
| 应用内下载（安装包） | 只在 dev tag 的 Release 里找 `OBS_Helper_Setup_*.exe`；dev 还没出包时提示「还没有带安装包的 dev 版本」 |
| 增量更新 | 只在 dev tag 的 Release 里找 `OBS_Helper_Update_*.zip`；清单里的 `baseVersion` 同时接受 dev tag（`01`）与正式版本（`3.3.0`）两种写法 |
| 知识库 / 插件目录 | **仍走正式 Release**（这两类资产跟着正式版发布），dev 用户不会因此拉不到内容 |
| 蓝奏云 | dev 弹窗里整段隐藏：蓝奏云只放正式版安装包 |
| Release 页入口 | 直达 `/releases/tag/0X`（拿不到 tag 时退到 `/releases`） |

## 4. 出一版 dev 的步骤

1. 改 `UpdateChannel.DevBuildNumber`（`01` → `02` …）；
2. 提交并推送 `dev` 分支；
3. 在 GitHub 上建一个 **tag = `02`**（纯数字、不带 V）的 Release，标题建议 `DEV 02`，正文写这一版变更；
4. 出包后把 `OBS_Helper_Setup_*.exe` / `OBS_Helper_Update_*.zip` 挂到同一个 Release 上，
   应用内下载与增量更新会自动认出它们。

## 5. 已知限制

- 现在 dev 版**还没有安装包**，应用内下载与增量更新会提示「暂无」—— 这是预期行为，不是故障；
- dev 构建的程序集版本仍沿用基准正式版本（如 `3.1.0`），因此「关于」页显示 `3.1.0（DEV 01）`：
  正式版本号表示它基于哪个正式版，`DEV 0X` 表示是第几个 dev 构建；
- 测试数据与正式版共用 `%LocalAppData%\OBS_Helper`：同一台机器上装/跑 dev 版会沿用正式版的偏好与凭据，
  需要隔离时请用不同的 Windows 用户，或先备份数据目录。
