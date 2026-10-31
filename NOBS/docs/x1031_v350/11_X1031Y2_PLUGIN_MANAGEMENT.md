# X1031Y2 插件与依赖管理增强（技术说明）

> 版本：`X1031Y2`（内部版本 `1031.2.0`，2026-10-31）
> 范围：在 V3.5.2 全部内容之上（X 系列专属应用图标除外），补齐插件 / 开源依赖的「应用内拉取」与本机插件管理。

## 1. 为什么做

V3.5.2 的插件广场已经能粘贴 `owner/repo` 查询最新 Release，但用户实际使用时仍有三处断点：

1. **精选插件卡片只有「下载」**：点开的是 GitHub Releases 网页，安装包仍要用户自己在浏览器里下载、解压、找目录。
2. **开源依赖只有 mediamtx 能自动装**：FFmpeg / rclone / whisper 的「一键下载」只是打开官方页面，「一键配置」只能选本机已下载的 exe。
3. **本机检测到的插件只读**：用户用 OBS 自己装的插件能看到，但不能卸载、迁移或纳入管理，页面文案还写着「不会卸载或改动」。

X1031Y2 把这三处改成「在软件内完成」，同时保留手动渠道，避免把网络问题变成死路。

## 2. 两条渠道

### 渠道 A：用户自己安装，应用负责配置

- 插件：用户把 zip 解压到 OBS 插件目录后，在「已安装」列表点「接管管理」。
- 依赖：用户自己下载 exe 后，在依赖中心点「选择已下载文件」。
- 两条路径都只登记 / 记录路径，不移动用户文件、不修改系统 PATH。

### 渠道 B：应用内一键拉取

- 插件卡片：`一键配置` → 查最新 Release → 选 Windows x64 zip → 下载 → SHA-256 校验（有摘要时）→ 解压 → 安装 → 刷新列表。
- 仓库输入框：粘贴 `owner/repo` 或完整 GitHub 地址，按回车或点「一键配置」直接拉取。
- 开源依赖：`一键配置` → 查最新 Release → 按依赖规则挑 Windows x64 zip → 下载 → 校验 → 安全解压 → 找 exe → 落到 `%LocalAppData%\OBS_Helper\dependencies\<id>\<tag>` → 写入设置并立即生效。
- 每次自动拉取前都先弹网络确认：提示先打开 Watt Toolkit（Steam++）的 GitHub 加速，或确认网络可用；失败时重新露出加速引导。

## 3. 本机插件管理

### 3.1 插件单元识别

`LocalPluginManageCore.Describe` 只认两种摆放：

- **一插件一目录**（OBS 32.x）：`<插件根>\<插件>\bin\64bit\<插件>.dll` → 插件目录是 `<插件根>\<插件>`；
- **扁平**：DLL 直接放在 `plugins` / `obs-plugins\64bit` → 只能操作 DLL 本身，绝不移动整个插件根。

插件根只认名为 `plugins` 或 `obs-plugins` 的目录；任何不满足条件的路径都返回 `null`，界面不显示管理操作。

### 3.2 卸载

- OBS 运行中一律拒绝（`PLUGIN_OBS_RUNNING`）。
- 一插件一目录：整目录移入 `%LocalAppData%\OBS_Helper\plugins\local-backups\<时间戳>-<插件名>`。
- 扁平：只移 DLL 与同名 `.pdb` / `.xml`，保留插件根里的其他插件。
- **不硬删**：用户之后可以从备份目录手动放回。

### 3.3 迁移

- 仅支持一插件一目录布局。
- 用户级 `%AppData%\obs-studio\plugins` ⇄ 全局级 `%ProgramData%\obs-studio\plugins`。
- OBS 安装目录下的 `obs-plugins\<插件>` 可以迁到用户级，避免继续写 Program Files。
- 目标已存在、无写权限、扁平布局都会返回明确的错误码；全局级需要管理员身份。

### 3.4 接管管理

- 把本机插件以现有目录登记到 `installed.json`，**不移动文件**。
- 目录名与条目 id 可能不同（例如 `local-<stem>`），卸载 / 回滚的路径护栏同时接受两种 slug。
- 接管后可以使用广场的「一键配置」更新，也可以在本应用内回滚 / 卸载。

## 4. 安全加固

- `installed.json` 的卸载 / 回滚路径新增 `PluginInstallCore.IsSafeManagedDirectory`：目录名必须匹配条目 slug，父目录必须是本应用安装根、用户 / 全局插件根，或 `obs-studio\obs-plugins`。
- 插件与依赖下载只接受 HTTPS + GitHub 官方资产域，重定向后的最终域名同样校验。
- GitHub 提供 `sha256:` 摘要时强制匹配；依赖下载写入磁盘前限制 512 MB，解压限制 2 GB / 20000 条目。
- sidecar 与增量包解压同样增加条目数 / 解压总量上限，保留 zip-slip 与符号链接拒绝。
- 所有解压都拒绝绝对路径、`..`、盘符与控制字符。

## 5. 问题库

- 条数：212 → **208**（删除 9 条重复 / 低价值条目，新增 5 条）。
- 版本：`2.2` → `2.3`，更新日期 `2026-10-31`。
- 补齐 `sources` / `performance` / `virtualcam` / `encoder` 4 个此前只被条目引用、未在分类表声明的分类，分类数 10 → **14**。
- 新增条目：插件一键配置失败、依赖一键配置后仍提示未就绪、录制状态不同步、录制文件被占用、本机插件迁移 / 卸载失败。
- 中英资产逐条对齐（id 顺序、数组长度、related、链接 URL）。

## 6. 验证

- `dotnet test OBS_Helper.slnx -c Release`：**1790 / 1790** 通过。
- 新增测试：`LocalPluginManageTests`（13）、`LocalPluginManageServiceTests`（7）、`DependencyInstallCoreTests`（13）。
- `python scripts/check_resources.py`：49 个 XAML、2718 处引用、3459 条中英文案键、14 个分类语义色全部可解析。
- 三轮校验记录见 [`10_IMPLEMENTATION_STATUS.md`](10_IMPLEMENTATION_STATUS.md)。

## 7. 已知边界

- 扁平布局插件不能整目录迁移或接管，只能卸载单个 DLL；建议卸载后用「一键配置」重装成新布局。
- 依赖一键配置依赖 GitHub API 最新 Release；上游没有 Windows 成品包（例如 whisper.cpp 某些版本）时会提示改用「下载页」。
- GitHub 未提供摘要的旧资产仍可下载，但会在结果里明确标记「未校验 SHA-256」。
- 全局级迁移 / 卸载需要管理员权限；应用不会自动提权。
