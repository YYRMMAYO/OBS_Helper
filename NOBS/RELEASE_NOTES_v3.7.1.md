# V3.7.1 发布说明

## 一句话摘要

V3.7.1 深化「手机当摄像头（Wi-Fi）」链路：二维码从裸 SRT 地址升级为 **Larix Grove 深链**，补齐 Larix 安装入口与 **WHIP 低延迟** 入口，并给 mediamtx 加上**回环 HTTP 鉴权**——停止机位即撤销推流 key、踢掉已建立连接；WebRTC 允许来源从 `*` 收窄到本机与当前局域网。全量 **1891 项单元测试**与 **37 项无界面自检**通过。

## 新增与改进

### 1. 扫码即导入 Larix（优先级 1）

- 摄像头 SRT 二维码不再直接放 `srt://...`，而是生成 Larix 官方 Grove 深链：
  `larix://set/v1?conn[][url]=...&conn[][name]=...`。
- 手机扫码后 Larix 会新增一条连接，用户只需点「开始」，不必手抄长地址。
- 当前二维码编码器上限为版本 10 / 纠错 M；Grove 链接超出容量时自动回退为裸 SRT，并在界面明确提示改用复制，不会画出一个扫不出来的二维码。

### 2. 未安装 Larix 的引导（优先级 2）

- 摄像头页新增「手机端安装一次 Larix Broadcaster」说明与三个官方入口：iOS App Store、Android Google Play、Softvelum 官方 APK。
- 外链仍走域名白名单，新增 `softvelum.com`；三条官方链接均有单测覆盖。
- 界面明确：扫码只负责填入地址，开始推流仍需在 Larix 里点一次「开始」。

### 3. WHIP 低延迟入口（优先级 3）

- 新增 WHIP 二维码，同样使用 Grove 深链；适合 **Larix Premium** 用户。
- 保留「复制 WHIP」按钮；OBS 30.2+ 也可把 WHIP 地址填到「设置 → 输出 → 服务 → WHIP」。
- 明确标注 WHIP 需要 Larix Premium 或 OBS 30.2+，避免用户误以为所有 Larix 版本都支持。

### 4. 推流 key 会话失效与 WebRTC 来源收口（优先级 4）

- mediamtx 启用 **回环 HTTP 鉴权**（`authMethod: http`，只绑 `127.0.0.1`）：
  - `publish` 只允许活动会话的 `live/<key>`，协议限 SRT / RTMP / WebRTC(WHIP)；
  - `read` 只允许活动会话的 path（OBS 回环 RTSP、手机 WebRTC 监看）；
  - `api` / `metrics` / `pprof` / `playback` 一律拒绝。
- 停止机位后会话立即从活动表移除，旧 key 不能再建立新连接；同时尽力通过 mediamtx Control API 踢掉已建立的 SRT / RTMP / WebRTC / RTSP 连接。
- `webrtcAllowOrigins` 不再使用 `*`，只允许 `http://127.0.0.1:<port>`、`http://localhost:<port>` 与当前局域网地址。
- 鉴权服务只绑回环，随机端口；应用退出 / 停止链路时一并释放。

### 5. 网页摄像头边界与 UI 防线（优先级 5）

- 摄像头页新增「浏览器直采（不默认提供）」说明：手机浏览器 `getUserMedia` 需要受信 HTTPS 或 localhost；`http://192.168.x.x` 会被浏览器禁用；自签证书仍需用户信任，iOS 不可靠。
- 新增 `PhoneCameraCore.IsBrowserCameraSecureContext` 纯逻辑与单测，后续若做实验性网页直采，必须先过这条边界。
- 不提供自签证书默认入口，也不把「局域网 HTTP 浏览器摄像头」写成已支持能力。

## 修复的问题

### 1. 二维码“看起来能导入”但实际只是地址文本

- **以前**：二维码内容是裸 `srt://...`，Larix Grove 的官方导入格式是 `larix://set/v1?...`，扫码后不保证能新增连接。
- **现在**：默认生成 Grove 深链；容量不足才回退裸 SRT，并明确提示复制。

### 2. 停止机位后旧推流地址仍可能可用

- **以前**：停止单个机位只从内存表移除；mediamtx 的 path 仍存在，拿到旧 key 的客户端可能继续推流/重连。
- **现在**：活动表移除 + 回环鉴权拒绝新连接 + Control API 踢掉已建立连接；下一次启动生成新 key。

### 3. WebRTC 来源过宽

- **以前**：`webrtcAllowOrigins: ['*']`。
- **现在**：只允许本机、localhost 与当前局域网地址；手机 WebRTC 监看与 OBS WHIP 不受影响。

## 兼容性与升级

- 版本号：`3.7.0` → `3.7.1`，可覆盖升级，无需卸载旧版。
- 安装目录、程序集名、数据目录（`%LocalAppData%\OBS_Helper`）不变；设置、配对记录、场景模板、提词稿、插件清单全部保留。
- 不新增持久化数据键；鉴权服务使用随机回环端口，退出即释放。
- 手机端仍需自行安装 Larix Broadcaster / OBS Camera；本项目不捆绑、不自动安装。
- WHIP 低延迟需要 Larix Premium 或 OBS 30.2+；普通 SRT/RTMP 路径不受影响。

## 已知边界

- **浏览器直采仍不默认提供**：`getUserMedia` 需要可信 HTTPS；本项目不内置证书，也不把局域网 HTTP 浏览器摄像头写成已支持能力。
- **iOS 自签证书不可靠**：即使手动信任，Safari 对摄像头权限的证书要求也更严格；不把它作为产品承诺。
- **Larix Grove 是第三方格式**：如果 Larix 未来调整深链参数，二维码可能需要同步；复制 SRT / WHIP 地址始终保留为兜底。
- **WHIP 依赖第三方能力**：Larix 的 WHIP 输出属于 Premium 功能；OBS WHIP 需要 30.2+。
- **未做 Authenticode 签名**：SmartScreen 仍可能提示未知发布者。

## 下载与校验

- 安装包：`OBS_Helper_Setup_3.7.1.exe`
- 便携包：`OBS_Helper_Portable_3.7.1.zip`
- Win7 兼容包：`OBS_Helper_Setup_3.7.1_win7.exe` / `OBS_Helper_Portable_3.7.1_win7.zip`
- 增量包：`OBS_Helper_Update_3.7.1.zip`（基准资产可得时）
- 资产 SHA-256 由 CI 在 Release 正文末尾自动追加，也可用 `SHA256SUMS.txt` 校验。
