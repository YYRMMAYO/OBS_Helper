# 插件：GitHub 加速引导与已安装列表

## 1. 背景

插件目录、GitHub 搜索、Release 下载全部依赖 GitHub。国内网络下常见现象：
搜索一直转圈、安装卡在下载、提示 `PLUGIN_DOWNLOAD_FAILED`。用户不知道这是网络问题。

## 2. 加速器引导

- 位置：插件广场顶部（资源入口之前）固定一张 `SoftPanel`。
- 内容：
  1. 说明「插件广场需要访问 GitHub；国内网络可先开启加速器」；
  2. 一键打开 Watt Toolkit / Steam++ 官网（`ObsDownloadLinks.WattToolkitSite`）；
  3. 「我已开启，重新检测」按钮：对 `api.github.com` 发一次轻量 HEAD/GET，显示可达 / 超时；
  4. 「不再提示」写入 `prefs.json`，只隐藏提示，不改变功能。
- 失败时的插件安装错误文案补一句「可先开启 GitHub 加速器后重试」。

技术落点：

| 文件 | 变更 |
| --- | --- |
| `Views/PluginsPage.xaml` | 新增加速器引导卡（`AcceleratorPanel`） |
| `Views/PluginsPage.xaml.cs` | `CheckGitHubAsync` / 打开官网 / 关闭提示 |
| `Services/Plugins/GitHubReachability.cs` | 纯逻辑：URL、超时、结果分类（可单测） |
| 文案表 | 中英文案 |

## 3. 已安装列表

### 3.1 现状

`RefreshManagedList()` 只读 `PluginInstallService.Installed`（`installed.json`）。
用户用 OBS 自己安装的插件、或用旧版本安装的插件都不在清单里，于是页面显示
「还没有由本应用安装的插件」，与用户认知冲突。

### 3.2 修复方案

合并两个来源：

1. **应用管理**：`installed.json`，可卸载 / 回滚 / 显示来源与 SHA-256；
2. **本机检测**：`LocalPluginScanner.Scan()` 的结果，只读，不可卸载。

去重规则（纯逻辑，可单测）：

- 以规范化后的文件路径为键（`Path.GetFullPath` + 小写）；
- 应用管理记录优先，保留其可操作按钮；
- 本机记录显示「本机检测 · 非本应用安装」徽标；
- 管理记录目录不存在时显示「目录缺失」并禁用回滚；
- 扫描失败 / 未找到 OBS 时显示明确原因，不显示空列表。

### 3.3 界面

```
已安装（本应用管理 + 本机检测）
├─ [管理] 插件名 · v1.2.3  [更新] [回滚] [卸载]
│    路径 / 来源 / SHA-256 前 12 位
├─ [本机] 插件名 · 文件版本
│    路径 / 「由 OBS 或手动安装，本应用不改动」
└─ 空态：分两种文案 —— 「未检测到 OBS」/「OBS 中暂无插件」
```

### 3.4 验收

- 用 OBS 手动放入一个插件 → 页面立即能看到；
- 用本应用安装 → 出现管理徽标与卸载按钮；
- 删除管理目录 → 显示「目录缺失」且不会误删其他文件。
