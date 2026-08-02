# OBS 排障助手 · 报错码对照表

所有报错码以 `OBS` 开头，后接 3 位数字。规则：

| 段位 | 含义 | 主要来源 |
| --- | --- | --- |
| `1xx` | 启动 / 运行时（桌面壳抛出） | Windows WebView2 壳 |
| `2xx` | 数据加载 | Blazor 客户端 |
| `3xx` | 路由 / 页面 | Blazor 客户端 |
| `4xx` | 本地存储（收藏 / 进度） | Blazor 客户端 |
| `5xx` | 助手 / 搜索 | Blazor 客户端 |
| `9xx` | 组件 / 未知 | 全局兜底 |

> 桌面壳（Windows）通过 `OBS_Helper.Win.Errors.AppError` 把报错码组装成「[报错码] 标题 + 解决方案 + 详细错误」弹窗；
> 网页端通过 `OBS_Helper.Client.Errors.ErrorCodes` 统一索引与说明。

## 对照表

| 报错码 | 名称 | 现象 | 可能原因 | 解决方案 |
| --- | --- | --- | --- | --- |
| `OBS101` | WebViewInitFailed | 启动即弹出「无法初始化内置浏览器」 | WebView2 运行时缺失 / 被禁用 | 安装 Microsoft Edge WebView2 Runtime；或用随附安装包（已内置运行时） |
| `OBS102` | SiteResourceMissing | 提示「站点资源缺失」 | `OBS_Helper.exe` 与 `wwwroot` 不在同一目录，或 `wwwroot` 被误删 | 确认安装目录下 `wwwroot` 文件夹完整存在，必要时重装 |
| `OBS103` | RuntimeMissing | 提示「WebView2 运行时缺失」 | 系统未安装 WebView2 Runtime | 到微软官网下载安装 WebView2 Runtime 后重试 |
| `OBS201` | DataLoadFailed | 首页 / 列表空白，或提示加载失败 | 网络异常（联网模式）、应用进程被拦截 | 检查网络后重启；离线模式请确保 `wwwroot/data/problems.json` 存在 |
| `OBS202` | DataParseFailed | 启动后内容错乱 / 控制台报错 | `problems.json` 损坏或格式不兼容 | 重新安装或更新应用以获取正确数据文件 |
| `OBS301` | PageNotFound | 打开链接显示 404「没有找到这个页面」 | 路由入口错误 / 旧书签指向已移除页面 | 返回首页重新进入对应分类 |
| `OBS302` | NavigationFailed | 点击后页面不跳转 / 卡住 | 客户端路由异常或脚本中断 | 刷新应用（F5 / 重启），仍异常则清缓存重装 |
| `OBS401` | LocalStorageUnavailable | 收藏 / 步骤进度不保存 | 浏览器隐私模式、存储被禁用、宿主限制 | 关闭隐私模式或允许本地存储；不影响正常浏览 |
| `OBS501` | AssistantIndexFailed | 「问我一下」离线问答不可用 | 离线索引建立失败（首次构建索引中断） | 改用「搜索」或「分类」查找；重启应用重试建索引 |
| `OBS900` | Unknown | 出现未归类的错误 | 未知异常 | 重启应用；若持续出现，记录报错码并联系支持 |

## 代码位置

- 客户端定义：`OBS_Helper.Client/Errors/ErrorCodes.cs`
- Windows 壳：`OBS_Helper.Win/Errors/AppError.cs`
- 路由兜底：客户端 `App.razor` 的 `ErrorBoundary` 默认显示 `OBS900`
- 接入点：Windows `MainForm.cs` 在「站点缺失 / WebView2 初始化失败」时弹出来自 `AppError.Format(...)` 的带码提示
