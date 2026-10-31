# X 系列独立版本通道技术规范

## 1. 目标

- X 系列是一个**整体大版本**，与 V 系列并列，不混更；
- X 构建只检测 GitHub 上以 `X` 开头的标签；
- V 构建只检测 `V` 开头的正式版标签；
- 两个系列共用同一套功能代码与数据目录，差异只在发布通道、图标与版本显示。

## 2. 标签与内部版本

| 系列 | GitHub tag | 内部 `Version` | 显示 |
| --- | --- | --- | --- |
| V | `V3.5.0` | `3.5.0` | `v3.5.0` |
| X | `X1031` | `1031.0.0` | `X1031` |

规则：

- X 标签格式：`X` + 4 位日期码（`MMdd`），例如 `X1031`；后续可用 `X1101`。
- 内部版本把日期码作为主版本号，保证 `X1101 > X1031`，且不会与 `3.x` 比较。
- 版本显示由 `ReleaseChannel.FormatDisplayVersion()` 统一处理，界面不再各自拼接。
- 安装包 / 便携包文件名仍使用内部版本（`OBS_Helper_Setup_1031.0.0.exe`），
  Release 标题与 tag 使用 `X1031`，避免破坏构建脚本的 `[version]` 解析。

## 3. 编译期通道

`OBS_Helper.Wpf.csproj` 增加：

```xml
<PropertyGroup Condition="'$(ReleaseChannel)' == 'X'">
  <DefineConstants>$(DefineConstants);X_SERIES</DefineConstants>
  <Version>1031.0.0</Version>
  <FileVersion>1031.0.0.0</FileVersion>
  <AssemblyVersion>1031.0.0.0</AssemblyVersion>
  <ApplicationIcon>Assets\appicon_x.ico</ApplicationIcon>
</PropertyGroup>
```

- 默认（不传 `ReleaseChannel`）为 V 系列。
- X 构建命令：`dotnet build ... -p:ReleaseChannel=X`。
- CI 在 `X*` tag 上自动传 `ReleaseChannel=X`。

## 4. 更新检查

`UpdateService` 增加 `ReleaseChannel` 静态类（纯逻辑、可单测）：

```csharp
public enum ReleaseSeries { V, X }
ReleaseSeries Current;                 // 由 X_SERIES 编译常量决定
bool IsTagForSeries(string tag);       // V: ^[vV]\d+\.\d+ ; X: ^[xX]\d{4}
Version? ParseTag(string tag);         // X1031 -> 1031.0.0
```

- `ParseLatestVersion` 先按通道过滤，再比较；
- `FindBestRelease` / `FindNamedAssetAsync` 同样按通道过滤；
- V 通道忽略 X 标签，X 通道忽略 V 标签；
- 任何无法解析的标签跳过，不影响其他标签。

## 5. 发布流程

1. V3.5.0：更新 csproj / iss 为 `3.5.0` → 提交 → `git tag V3.5.0` → 推送；
2. X1031：切到 X 通道构建参数 → 更新 csproj / iss 为 `1031.0.0` → 提交 → `git tag X1031` → 推送；
3. CI：
   - `on.push.tags` 增加 `X*`；
   - release job 不再强制 `V` 前缀解析；
   - 发布说明查找顺序：`RELEASE_NOTES_x1031.md` → `RELEASE_NOTES_vx1031.md`（大小写兼容）；
   - X tag 构建传 `ReleaseChannel=X`，跳过基于 `[version]` 的增量基准（X 系列首版不做增量）。

## 6. 验收

- V 构建：只提示 `V3.5.0`，即使仓库同时存在 `X1031`；
- X 构建：只提示 `X1101` 之类，不会提示 `V3.6.0`；
- 两个构建的版本显示、图标、Release 资产名各自正确；
- `ReleaseChannelTests` 覆盖所有过滤与解析分支。
