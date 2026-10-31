# 手机当摄像头：排障与一键媒体服务

## 1. 为什么「无法正常运行」

代码路径：`MediaServerManager.StartAsync` → 检查 mediamtx → 启动 sidecar → 在 OBS 建媒体源。

发现的硬阻断：

1. `sidecars.json` 中 mediamtx 为 `manual: true`，`InstallAsync` 直接返回 `CAM_SIDECAR_MISSING`；
   用户必须自己找 GitHub Release、解压、放到 `%LocalAppData%\OBS_Helper\sidecars\mediamtx\manual\mediamtx.exe`。
2. 未连接 OBS 时 `EnsureObsMediaSourceAsync` 返回 false，整个启动失败；但用户此时往往只是想先开媒体服务。
3. 失败提示只有错误码，没有「下一步做什么」。
4. 手机端浏览器 `getUserMedia` 在局域网 HTTP 下被浏览器禁止，**不能**用网页直接当摄像头；
   必须用 RTMP/SRT 推流 App（如 Larix Broadcaster / 相机类 App）。界面需要把这个前提讲清楚。

## 2. 修复

### 2.1 一键安装 mediamtx

- 固定版本：`v1.21.3`，资产 `mediamtx_v1.21.3_windows_amd64.zip`；
- URL / SHA-256 / 体积写入 `sidecars.json`，`manual: false`；
- 下载后按现有 `SidecarManager.InstallAsync` 校验 SHA-256 并安全解压；
- 界面按钮：`安装媒体服务` → 进度 → 成功后可「启动摄像头」。

> 若后续升级 mediamtx，必须同步更新版本、URL、SHA-256、体积四项，缺一不可。

### 2.2 启动顺序解耦

`StartAsync` 改为：

1. 校验 OS / 私网地址 / sidecar；
2. 启动 mediamtx；
3. 创建会话并返回成功；
4. 若 OBS 已连接，再创建场景 / 媒体源并应用布局；未连接则记录 `ObsPending = true`，
   在下次连接后由页面刷新时补建。

### 2.3 手机端推流步骤

界面按顺序展示，并提供复制按钮：

1. 手机与电脑连同一 Wi-Fi；
2. 手机安装支持 SRT/RTMP 的推流 App（Larix Broadcaster 等，用户自行安装）；
3. App 里填入 SRT 或 RTMP 地址（含随机 key）；
4. 点开始推流 → 电脑端状态变为「已连接」→ OBS 自动出现「手机摄像头 cam1」来源。

### 2.4 排障提示

| 现象 | 提示 |
| --- | --- |
| `MEDIAMTX_MISSING` | 点「安装媒体服务」 |
| 启动后无画面 | 检查手机推流 App 是否已开始、地址是否完整 |
| 端口占用 | 重启媒体服务（自动重新分配端口） |
| 防火墙 | 允许 `OBS_Helper.exe` / `mediamtx.exe` 访问专用网络 |
| Win7 | 明确不支持，引导 USB 采集卡方案 |

## 3. 验收

- 未安装 mediamtx 时按钮可见且能完成安装；
- 未连接 OBS 也能启动媒体服务；
- 连接 OBS 后自动出现媒体源；
- 地址在重启后变化，旧地址失效是预期行为（随机端口 + 随机 key）。
