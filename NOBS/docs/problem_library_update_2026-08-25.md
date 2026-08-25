# 问题库更新方案（2026-08-25）— 新增 63 条排障指引（待审阅）

> 现状：`OBS_Helper.Wpf/Assets/problems.json` 共 **149** 条。本方案经网络检索
> （OBS 官方论坛、官方知识库、GitHub Issues 及社区教程）收集常见问题，
> 已与本地 149 条逐一去重，新增 **63** 条（含必须增加的「录制重音」）。
>
> 审阅通过后，将以 `scripts/add_problems_*.py` 同款脚本合并写入 problems.json，
> 并同步更新 `note` 字段与 `updated` 时间戳。
>
> 图例：【必】= 用户点名必须收录；【新】= 官方论坛未集中讨论但社区高频的问题。

---

## 一、音频类（10 条）

### 1.【必】`au-rec-double-voice`｜录制重音：录制文件里出现双重人声 / 回声（直播监听时正常）
- **分类**: audio ｜ **严重度**: 常见 ｜ **平台**: Windows / macOS / Linux
- **症状**：
  - 回放录像时人声「一前一后叠了两遍」，像两个人同时说话；
  - 直播时自己戴着耳机听一切正常，观众或录出来的文件才有重音；
  - 游戏声音 / 音乐也出现轻微的「合唱感」延迟叠加。
- **原因**：
  - 同一路声音走了两条采集路径进混音器：最典型的是麦克风既在「设置 → 音频 → 麦克风」全局启用，又被手动添加为「音频输入采集」来源；
  - 「监听并输出」（Monitor and Output）陷阱：麦克风开了监听后，人声被送到播放设备，而「桌面音频」是回环采集整个播放设备，于是把监听的人声又录了一遍——所以录制时听不出来，回放才发现；
  - 摄像头 / 采集卡来源默认带了自己的拾音麦，与主麦重复采集。
- **步骤**：
  1. **按听感先分型** — 紧贴的双人声=同源双采；只有文件里有、现场没有=监听回环；越来越宽的漂移=采样率时钟问题（另见 av-drift）。约 5 秒可定位方向。
  2. **清理重复设备** — 设置 → 音频：Mic/Aux 2/3/4 全部禁用；若场景里已添加「音频输入采集」，就把全局麦克风设为禁用，二者只留一条路径。
  3. **关闭监听回环** — 右键混音器 → 高级音频属性：除告警浏览器源外全部设为「监视器关闭（Monitor Off）」；确需监听的用「仅监视器（静音输出）」。这一项能解决约八成「直播没事、录像有重音」。
  4. **掐掉摄像头自带麦** — 双击视频捕获设备属性，把「音频输出模式」改为仅捕获且在混音器静音，或直接停用该设备的音频。
- **提示**：
  - 排查口诀：混音器里逐个独奏（Solo），谁开口两次谁是元凶；
  - 正式录制前录 30 秒试听样片，是成本最低的保险。
- **关联**：au-echo, au-monitor, au-sample-mismatch, au-mic, rc-audio-tracks-missing
- **链接**：[Cubix: How to Fix Audio Echo in OBS Recording](https://cubix.design/resources/how-to-fix-audio-echo-in-obs-recording)；[OBS 论坛 · DOUBLED AUDIO 类帖汇总](https://obsproject.com/forum/threads/audio-is-doubled-or-tripled-when-i-stream-please-help-and-assist.192248/)

### 2. `au-exclusive-mode`｜音频独占模式冲突：初始化麦克风报错 / 声卡被其他程序占用
- **分类**: audio ｜ **严重度**: 一般 ｜ **平台**: Windows
- **症状**：
  - 提示「初始化麦克风时出现错误——可能另一个应用程序在独占模式下使用麦克风」；
  - 其他语音软件（QQ/微信/Discord）开着时 OBS 就采不到声；
  - 桌面音频偶发整段丢失，重启 OBS 才恢复。
- **原因**：
  - Windows 声卡属性勾选了「允许应用程序独占控制该设备」，先到的程序锁死设备；
  - 部分 ASIO / 专业声卡驱动同一时刻只允许一个客户端访问。
- **步骤**：
  1. 关闭独占模式 — 控制面板 → 声音 → 录制（及播放）设备 → 属性 → 高级：取消「允许应用程序独占控制该设备」与「给予独占模式应用程序优先级」。
  2. 排查占用方 — 先退出 DAW、语音软件、直播伴侣等再启动 OBS 验证。
  3. 统一采样率 — 设备高级页与 OBS 都设为 48000Hz 共享模式（另见 au-sample-mismatch）。
  4. 专业声卡走 ASIO 插件 — 需要 DAW 与 OBS 同时使用时，选支持多客户端的驱动或 Voicemeeter 中转。
- **提示**：笔记本自带麦克风阵列常被厂商音效软件（Waves MaxxAudio 等）默认独占，卸载/禁用其服务即可。
- **关联**：au-mic, au-default-device-change, cr-env-interference
- **链接**：[OBS 官方知识库](https://obsproject.com/zh-cn/kb)

### 3. `au-mono-channel`｜录音只有一边有声 / 单声道设备被当立体声
- **分类**: audio ｜ **严重度**: 一般 ｜ **平台**: Windows / macOS
- **症状**：
  - 回放时只有左耳（或右耳）有人声；
  - 观众反映「声音偏一边」「戴耳机难受」；
  - 单声道麦克风音量比正常小一半。
- **原因**：
  - 大量 USB / 3.5mm 麦克风只输出左声道，OBS 默认按立体声记录；
  - 平衡旋钮 / Windows 音量合成器左右不平衡；
  - 转封装或剪辑环节误删了一个声道。
- **步骤**：
  1. 混音器定位 — 高级音频属性里先确认是哪个源缺边，排除播放端耳机问题。
  2. 加声道转换滤镜 — 对该源加「增益」滤镜无用于此题，正确做法是右键来源 → 滤镜 → 添加 VST 或使用「上混至单声道」类滤镜；32.x 可直接用音频滤波器里的 Downstream Keyer 替代品不适用——推荐用免费 VST「Mono Input」或 Balance 插件居中。
  3. 系统侧修正 — Windows 声音设置开启「单声道音频」作为快速兜底。
  4. 导出前检查 — 用 ffprobe / 播放器确认成品轨道声道数。
- **提示**：多轨录制时每条轨都要单独处理单声道合并，混音后再导出。
- **关联**：au-mic, au-track-split, au-rec-double-voice
- **链接**：[OBS 官方知识库](https://obsproject.com/zh-cn/kb)

### 4. `au-ptt`｜按键说话（PTT）失效 / 松键仍收音
- **分类**: audio ｜ **严重度**: 一般 ｜ **平台**: Windows / macOS / Linux
- **症状**：
  - 设置了 PTT 但按下没反应，或一直处于收音状态；
  - PTT 键与游戏内语音键冲突，两边同时触发；
  - 切出 OBS 后热键失灵。
- **原因**：
  - PTT 依赖全局热键钩子，游戏以管理员运行时低权限 OBS 收不到按键；
  - 锁定键（NumLock/CapsLock）参与组合后状态不符；
  - 「PTT 延迟释放」设置为 0 导致尾字被切，误判为失效。
- **步骤**：
  1. 以管理员身份运行 OBS（见 os-admin-rights）；
  2. 设置 → 高级 → 热键焦点行为改为「从不放弃热键」；
  3. 避开系统与游戏常用组合（Win 组合键、Alt+Tab 等），优先用小键盘键位；
  4. 把「按键说话释放延迟」调到 200~500ms，防止句尾吞音。
- **提示**：推流场景更建议用噪声门限代替 PTT（au-mic-chain），避免手忙脚乱。
- **关联**：cfg-hotkey-conflict, os-admin-rights, au-mic-chain, au-comm-lower
- **链接**：[社区指南 · Global Hotkeys Not Registering](https://salivity.github.io/obs-studio/article/how-to-fix-obs-studio-global-hotkeys-not-registering)

### 5. `au-track-split`｜多轨音频录制：人声 / 游戏 / 音乐分开保存便于后期
- **分类**: audio ｜ **严重度**: 常用技巧 ｜ **平台**: Windows / macOS / Linux
- **症状**：
  - 录完想单独调人声或去掉背景乐，发现所有声音混在一轨里无法拆分；
  - 不清楚 OBS 最多支持几条音轨、怎么分配；
  - 分轨后推流轨忘了勾选导致直播没声音。
- **原因**：
  - 高级输出模式才提供最多 6 条音频轨；简单模式永远只混一轨；
  - 每个源的「轨道」复选框决定它进哪些轨，漏勾 = 该轨无声。
- **步骤**：
  1. 设置 → 输出切「高级」→ 录像 → 音频轨勾选 1~4（按需）并为各轨设码率（160kbps 起）；
  2. 混音器 → 高级音频属性：麦克风勾轨 1+2，桌面音频勾轨 1+3，音乐源只勾轨 3（不进推流）；轨 1 保持「推流+监看总混」；
  3. 录像格式用 Hybrid MP4/MKV 保证多轨封装；
  4. 试录 30 秒，在剪辑软件里确认各轨独立可调。
- **提示**：轨 1 必须包含你想让观众听到的全部内容；纯后期素材轨不要勾轨 1。
- **关联**：rc-audio-tracks-missing, au-rec-double-voice, rc-mkv, perf-dual-encode
- **链接**：[OBS 官方知识库](https://obsproject.com/zh-cn/kb)

### 6. `au-game-quiet`｜观众说游戏声音太小 / 桌面音频增益不足
- **分类**: audio ｜ **严重度**: 常见 ｜ **平台**: Windows / macOS
- **症状**：
  - 本地听着正常，观众端要开很大音量才能听到游戏声；
  - 人声与游戏声比例失衡，说话时游戏声像消失；
  - 拉满桌面音频滑条还是不够响。
- **原因**：
  - 系统输出音量、应用内音量、OBS 滑条三级衰减叠加；
  - 游戏以较低电平输出（尤其主机经采集卡 HDMI 进来的音量普遍偏低）；
  - 只加了压缩器没做增益补偿，整体响度被压低。
- **步骤**：
  1. 先把系统/游戏端音量拉高到 80% 以上，再在 OBS 里微调——源头电平不足时后期增益只会放大底噪；
  2. 对桌面音频源加「增益」滤镜，每次 +3dB 递增，混音器峰值控制在 -6dB 左右留余量；
  3. 加压缩器（阈值 -18dB、比例 3:1）拉平起伏，再用「限制器 -1dB」封顶防爆音；
  4. 用试录片段 + 耳机回放验证，参考同类主播的响度观感。
- **提示**：目标响度参考：直播综合 -14 LUFS 左右；别追求推满红表，削波比音量小更伤体验。
- **关联**：au-mic-chain, au-loudness-normalize, au-mute, av-desync
- **链接**：[OBS 2026 直播设置指南（社区汇总）](https://techtippr.com/obs-settings-guide-for-streaming/)

### 7. `src-camera-busy`｜摄像头被其他程序占用（Camera in use / 黑屏）
- **分类**: sources ｜ **严重度**: 常见 ｜ **平台**: Windows / macOS
- **症状**：
  - OBS 里摄像头来源显示感叹号或黑屏，提示设备不可用；
  - 关掉某视频软件后摄像头才恢复；
  - 多个程序同时想开摄像头时互相抢。
- **原因**：
  - 传统 MJPG/YUY2 摄像头同一时刻只允许一个消费者；Zoom/Teams/浏览器标签页后台仍持有句柄；
  - 厂商相机助手（Dell/联想/罗技 G HUB）驻留占用；
  - Windows 隐私开关关闭后设备枚举异常。
- **步骤**：
  1. 任务管理器结束残留进程（Zoom/Camera/App 内嵌 webview），或直接重启电脑最快归零；
  2. 让其他软件退出摄像头后再刷新 OBS 来源属性重新选择设备；
  3. 设置 → 隐私和安全性 → 相机：允许桌面应用访问摄像头；
  4. 长期方案：让 OBS 虚拟摄像头对外供流，其他软件都吃虚拟摄像头，物理头只归 OBS 一家。
- **提示**：会议软件「预览窗口」即使关了通话也可能持续占用，彻底退出而非最小化。
- **关联**：cf-webcam, vc-virtualcam-app, rc-device-disconnect
- **链接**：[OBS 论坛 · Windows 支持](https://obsproject.com/forum/list/windows-support.32/)

### 8.【新】`bs-cursor`｜捕获画面里看不到鼠标指针 / 指针闪烁
- **分类**: black-screen ｜ **严重度**: 一般 ｜ **平台**: Windows
- **症状**：
  - 教程录制里鼠标操作完全不可见；
  - 显示器捕获有指针、窗口/游戏捕获没有；
  - 指针在某些游戏里忽隐忽现。
- **原因**：
  - 窗口捕获与游戏捕获默认不合成光标层（尤其独占全屏/硬件渲染的游戏自绘指针）；
  - 来源属性中「捕获鼠标指针 / Capture cursor」被取消勾选；
  - 游戏内隐藏了系统光标（FPS 常见）。
- **步骤**：
  1. 检查来源属性里的「捕获鼠标」选项并勾选；
  2. 游戏捕获拿不到指针时改用显示器捕获（一定含光标层）；
  3. 需要放大指针演示：Windows 设置 → 辅助功能 → 鼠标指针调大并换高对比配色；
  4. 录教学可加装光标高亮工具（如 PointerFocus 类）或后期在剪辑软件补光标特效。
- **提示**：双机演示时，副屏捕获 + 大指针是最省事的网课方案。
- **关联**：bs-window, bs-display, bs-game, rc-meeting
- **链接**：[OBS Windows 排障指南（2026 版）](https://obs-studio-app.github.io/obs-studio-troubleshooting-windows.html)

### 9. `vc-virtualcam-app`｜虚拟摄像头在 Zoom / Discord / Teams 里不出现或黑屏
- **分类**: virtualcam ｜ **严重度**: 常见 ｜ **平台**: Windows / macOS
- **症状**：
  - 会议软件的相机列表里没有「OBS Virtual Camera」；
  - 能选中但画面全黑或停在旧帧；
  - 重启 OBS 好一阵子后又复发。
- **原因**：
  - 会议软件只在启动时扫描一次相机列表，OBS 后开就看不见；
  - 上次 OBS 未正常退出，虚拟相机驱动被锁死未注销；
  - Windows 相机隐私权限拦截桌面应用；虚拟相机注册损坏。
- **步骤**：
  1. 顺序很重要：先在 OBS 点「启动虚拟摄像机」，再打开 Zoom/Discord；
  2. 任务管理器结束全部 obs64.exe 残留进程后重开；
  3. 设置 → 隐私和安全性 → 相机：开启「相机访问」与「允许桌面应用访问」；
  4. 仍无效则重装虚拟相机：进入 OBS 安装目录 `data\obs-plugins\win-dshow\`，管理员运行 `virtualcam-install.bat`（先 uninstall 再 install）。
- **提示**：Discord 里若画面异常，关掉其「回声消除/降噪」视频处理选项；浏览器版会议需关浏览器硬件加速。
- **关联**：st-virtualcam, src-camera-busy, vc-mac-virtualcam-permission, os-win-update
- **链接**：[OBS 论坛 · SOLVED Virtual Camera not showing up in Zoom/Discord](https://obsproject.com/forum/threads/solved-virtual-camera-not-showing-up-in-zoom-discord.183371/)；[Appuals · Fix OBS Virtual Camera Not Working](https://appuals.com/obs-virtual-camera-not-working/)

### 10. `cfg-hotkey-conflict`｜全局快捷键失效 / 与反作弊、输入法、其他软件冲突
- **分类**: config ｜ **严重度**: 常见 ｜ **平台**: Windows / Linux
- **症状**：
  - 游戏内按开始/停止录制毫无反应，回到桌面又正常；
  - 快捷键触发了别的软件的功能（截图/覆盖层/输入法切换）；
  - Linux(Wayland) 下 OBS 非前台时热键完全无效。
- **原因**：
  - 以管理员运行的反作弊游戏权限高于 OBS，Windows 拦截低权限进程的全局键盘钩子；
  - 显卡覆盖层（GeForce/NVIDIA App）、Steam/Discord 覆盖层抢先消费按键；
  - Wayland 安全模型不允许应用监听全局按键（官方已知能力缺口）。
- **步骤**：
  1. 兼容性选项勾选「以管理员身份运行此程序」重启 OBS（对 Valorant/CS2 等尤其必要）；
  2. 设置 → 高级 → 热键焦点行为设为「从不放弃」；避免单键热键，改用 Ctrl+Alt+X 这类复杂组合；
  3. 逐个关闭 Discord/GFE/Steam 覆盖层做排除法；
  4. Wayland 用户变通：会话退回 X11，或用 WebSocket + 桌面级快捷方式触发（obs-cli）。
- **提示**：给关键动作（开始/停止录制）配第二套备用热键，冲突时还有后手。
- **关联**：cf-hotkeys, au-ptt, os-admin-rights, cfg-websocket-remote
- **链接**：[社区指南 · Global Hotkeys Not Registering](https://salivity.github.io/obs-studio/article/how-to-fix-obs-studio-global-hotkeys-not-registering)；[GitHub Issue #10538 · Wayland 全局快捷键](https://github.com/obsproject/obs-studio/issues/10538)

---

## 二、黑屏 / 捕获类（7 条）

### 11. `bs-uwp-apps`｜UWP 应用（微软商店应用 / Xbox / 计算器）捕获黑屏
- **分类**: black-screen ｜ **严重度**: 一般 ｜ **平台**: Windows
- **症状**：
  - 微软商店版应用、Xbox Game Bar、部分系统组件捕获出来是黑屏或只有背景；
  - 窗口捕获列表里找不到该应用的真实窗口；
  - 同一网页版正常、商店版黑屏。
- **原因**：
  - UWP 应用由 ApplicationFrameHost 托管渲染，传统窗口捕获抓不到其内容层；
  - 应用开启了受保护图形路径。
- **步骤**：
  1. 优先用「游戏捕获」勾选「采集任意全屏应用程序」试试；
  2. 改用「窗口捕获」→ Windows 10(1903+) 捕获方法 → 「Windows 10 (April Update)」，可直接枚举 UWP 窗口；
  3. 仍黑屏则用显示器捕获兜底，配合裁剪变换只保留应用区域；
  4. 能装普通 Win32 版的应用尽量替代商店版。
- **提示**：「设置」等系统应用本身禁止捕获属正常，不是故障。
- **关联**：bs-window, bs-display, bs-win11, bs-protected
- **链接**：[OBS 官方知识库](https://obsproject.com/zh-cn/kb)

### 12. `bs-chrome-hwaccel`｜Chrome / Edge 浏览器窗口捕获黑屏
- **分类**: black-screen ｜ **严重度**: 常见 ｜ **平台**: Windows / macOS
- **症状**：
  - 浏览器窗口在 OBS 里黑屏或只剩标题栏；
  - 网页视频区域单独黑块（DRM 内容除外）；
  - 切硬件加速后恢复正常，但浏览器变卡。
- **原因**：
  - 浏览器 GPU 合成把页面画在独立交换链上，老版本 OBS 的 BitBlt 方式取不到；
  - 显卡驱动与 Chromium 版本组合的已知兼容性问题。
- **步骤**：
  1. 窗口捕获属性的捕获方法依次尝试：BitBlt → Windows Graphics Capture（WGC）；
  2. 浏览器设置关闭「使用硬件加速」（chrome://settings → 系统）；
  3. 启动参数法：给浏览器快捷方式加 `--disable-gpu-compositing` 保留部分加速；
  4. 全屏演示场景干脆用显示器捕获。
- **提示**：WGC 方法会在窗口顶部加黄色描边（系统安全标识），正式录制介意的话用 BitBlt+关加速组合。
- **关联**：bs-window, bs-browser-src, cf-capture-conflict
- **链接**：[OBS Windows 排障指南（2026 版）](https://obs-studio-app.github.io/obs-studio-troubleshooting-windows.html)

### 13. `bs-office-slideshow`｜PowerPoint / WPS 放映模式捕获不到幻灯片
- **分类**: black-screen ｜ **严重度**: 一般 ｜ **平台**: Windows
- **症状**：
  - 编辑界面能看到，一点「放映」OBS 就黑屏或画面不变；
  - 窗口列表里找不到放映窗口；
  - 双屏演讲者视图只捕到备注页。
- **原因**：
  - 放映窗口是无边框特殊类名窗口，默认捕获方法枚举不到；
  - 放映默认在副屏输出，主屏是演讲者视图，捕获对象选错屏幕。
- **步骤**：
  1. 幻灯片放映设置 → 「使用演示者视图」关闭或多显示器配置确认放映所在屏；
  2. 窗口捕获里选「PowerPoint 幻灯片放映」条目；若无，捕获方法切 WGC；
  3. 单屏录制时把放映设为「窗口化放映」（放映设置 → 浏览过 individual window），再对该窗口做窗口捕获；
  4. 兜底：显示器捕获 + 裁剪。
- **提示**：网课录制建议提前放映一遍验证，别等上课才发现黑屏。
- **关联**：bs-window, rc-meeting, bs-chrome-hwaccel
- **链接**：[OBS 官方知识库](https://obsproject.com/zh-cn/kb)

### 14. `bs-gsync-flicker`｜G-Sync / FreeSync 下窗口捕获闪烁
- **分类**: black-screen ｜ **严重度**: 偶发 ｜ **平台**: Windows
- **症状**：
  - 开启可变刷新率后，窗口/显示器捕获画面周期性闪黑或撕裂；
  - 仅在游戏全屏切换时闪，桌面稳定；
  - 录像成片有规律性亮度跳变。
- **原因**：
  - VRR 切换刷新率时捕获 API 抓到半帧/空白帧；
  - NVIDIA「针对窗口化的 G-SYNC」与 WGC 捕获相互干扰。
- **步骤**：
  1. NVIDIA 控制面板 → 设置 G-SYNC：取消「启用窗口显示模式的设置」仅保留全屏；
  2. 捕获方法在 BitBlt 与 WGC 之间互换验证；
  3. 游戏内锁定与刷新率匹配的帧数（如 144Hz 锁 141）减少 VRR 频繁变频；
  4. 顽固案例在驱动面板为 obs64.exe 指定「禁用」可变刷新率。
- **提示**：闪烁问题优先怀疑驱动大版本更新后的回归，回退驱动也是有效手段。
- **关联**：lag-multi-refresh, bs-game, enc-device-removed
- **链接**：[OBS 论坛 · Windows 支持](https://obsproject.com/forum/list/windows-support.32/)

### 15. `bs-fullscreen-opt`｜禁用全屏优化（FSO）解决独占全屏捕获黑屏 / 闪烁
- **分类**: black-screen ｜ **严重度**: 偶发 ｜ **平台**: Windows
- **症状**：
  - 个别老游戏独占全屏时捕获黑屏或游戏掉帧明显；
  - Alt+Tab 后画面才出现；
  - 叠加层（FPS 计数）也不显示。
- **原因**：
  - Windows 的全屏优化（Fullscreen Optimizations）介于独占与无边框之间，部分老游戏与其兼容差；
  - 独占模式本身绕过桌面合成器，任何捕获都拿不到。
- **步骤**：
  1. 游戏 exe 右键 → 属性 → 兼容性 → 勾选「禁用全屏优化」；
  2. 游戏内显示模式改为「无边框窗口」——这是对捕获最友好的模式；
  3. 若必须独占全屏，接受显示器捕获无法工作的现实，用游戏内录制或第二台机器采集；
  4. 同页可顺手勾「以管理员身份运行」一并解决热键问题。
- **提示**：2020 年后的游戏基本都默认无边框，此条主要服务老游戏与模拟器玩家。
- **关联**：bs-game, bs-game-anticheat, lag-gamemode
- **链接**：[OBS Windows 排障指南（2026 版）](https://obs-studio-app.github.io/obs-studio-troubleshooting-windows.html)

### 16. `nv-overlay-conflict`｜NVIDIA App / GeForce Experience 覆盖层与 ShadowPlay 冲突
- **分类**: crash ｜ **严重度**: 一般 ｜ **平台**: Windows
- **症状**：
  - 开着 NVIDIA 覆盖层（Instant Replay/性能监控）时 OBS 捕获黑屏或崩溃；
  - 两套软件同时录像互相抢 NVENC 编码器；
  - 游戏内 FPS 浮窗被录进画面。
- **原因**：
  - NVIDIA App 的覆盖层同样注入游戏进程挂钩图形 API，与 OBS 游戏捕获钩子冲突；
  - NVENC 会话总数有限（消费卡并发会话上限），ShadowPlay 后台录制占掉名额。
- **步骤**：
  1. NVIDIA App → 设置 → 关闭「游戏内覆盖层」（或至少关 Instant Replay）；
  2. 若想保留 ShadowPlay 做备份录制，在编码顾问里核对 NVENC 会话预算，必要时 OBS 改 x264；
  3. 性能浮窗改为 RTSS 且按 cr-env-interference 处理好兼容；
  4. 更新 NVIDIA App 到最新版（早期版本与 OBS 冲突已被修复多轮）。
- **提示**：二选一原则：要么 NVIDIA 全家桶管录制、要么 OBS 管，别两套同时录。
- **关联**：cr-env-interference, en-nvenc, enc-nvenc, enc-vram
- **链接**：[OBS 论坛 · Windows 支持](https://obsproject.com/forum/list/windows-support.32/)

### 17. `perf-idle-cpu`｜OBS 挂后台什么都不做 CPU 占用也很高
- **分类**: performance ｜ **严重度**: 一般 ｜ **平台**: Windows / macOS / Linux
- **症状**：
  - 未推流未录制时风扇狂转，任务管理器里 OBS 占 15%+ CPU；
  - 预览画布明明静止却持续高负载；
  - 笔记本耗电发热加剧。
- **原因**：
  - 预览渲染以输出帧率持续跑，高刷屏 + 高分辨率画布开销可观；
  - 某些来源（浏览器源动画、媒体源循环）在后台持续解码；
  - 第三方插件（转写、面捕）空闲轮询。
- **步骤**：
  1. 设置 → 高级 → 打开「 sources 不可见时释放资源」相关选项；隐藏场景里的动态来源统一处理；
  2. 待机时段直接退出 OBS 而非最小化；需要常驻只开「最低性能」的回放缓冲；
  3. 安全模式对照实验定位是否插件所致（cr-safe-mode）；
  4. 笔记本接电源 + 高性能计划，避免节能降频反而拉长渲染时间。
- **提示**：CPU 占用要看绝对值趋势而不是瞬时值，混音器/统计面板结合判断。
- **关联**：perf-obs-overhead, enc-cpu, cr-slow-start
- **链接**：[OBS Studio 深度优化与故障排查指南（2026 版）](https://tsight.io/articles/10161700)

---

## 三、编码 / 性能类（6 条）

### 18. `enc-amf`｜AMD 显卡 AMF 硬件编码不可用 / 画质发糊
- **分类**: encoding ｜ **严重度**: 一般 ｜ **平台**: Windows
- **症状**：
  - 编码器下拉里没有 AMD HW AV1/H.264/HEVC，或选择后报错；
  - AMF 编码画质明显弱于同代 N 卡；
  - 驱动更新后突然不可用。
- **原因**：
  - 驱动未正确安装 AMF 运行时（精简版驱动常见）；
  - 老卡（GCN 架构）不支持新编码器，H.264 编码单元本身就弱于 NVENC；
  - 显存被 iGPU/Hyper-R 分走。
- **步骤**：
  1. 用 DDU 干净卸载后装完整版官方 Adrenalin 驱动；
  2. 核对显卡代际：RX 7000 系才有 AV1 编码；RX 5000/6000 只有 H.264/HEVC；
  3. AMF 预设选「质量」，并用 CQP 18~22 录像弥补码率控制弱势；
  4. 画质仍不满意的老卡直接换 x264 veryfast 或升级硬件。
- **提示**：AMD 平台别忘了检查「可切换显卡」设置，OBS 要指到独显。
- **关联**：en-nvenc, enc-qsv-error, enc-preset-guide, bs-dualgpu
- **链接**：[OBS 论坛 · Windows 支持](https://obsproject.com/forum/list/windows-support.32/)

### 19. `enc-qsv-error`｜Intel QSV 编码不可用 / 报驱动过旧
- **分类**: encoding ｜ **严重度**: 一般 ｜ **平台**: Windows
- **症状**：
  - 选择 QuickSync 后提示「不支持」或初始化失败；
  - 核显平台上 QSV 反而比 x264 卡顿；
  - 报错信息提到 driver/API version。
- **原因**：
  - 核显驱动过旧，媒体 SDK 版本不满足 OBS 要求；
  - BIOS 里核显被禁用（插了独显自动屏蔽）；
  - 虚拟机 / 远程环境没有直通核显。
- **步骤**：
  1. 到笔记本/主板厂商官网或 Intel DSA 更新核显驱动（OEM 机别只用通用驱动）；
  2. BIOS 启用 iGPU 多显示器输出；
  3. QSV 预设从「平衡」起步，新驱动可试「质量」；
  4. 虚拟机环境放弃 QSV，用 x264。
- **提示**：QSV 的价值在于给「双机位串流机的低功耗编码」或双编码第二路（perf-dual-encode），单机旗舰 U 场景不如 NVENC。
- **关联**：enc-amf, en-nvenc, perf-dual-encode
- **链接**：[OBS 官方知识库](https://obsproject.com/zh-cn/kb)

### 20. `enc-bitrate-guide`｜码率速查：不同分辨率 / 帧率 / 平台该填多少
- **分类**: encoder ｜ **严重度**: 常用技巧 ｜ **平台**: 全平台
- **症状**：
  - 不知道 1080p60 / 1440p 各该填多少码率，随手抄教程结果糊或浪费上行；
  - Twitch/B站/抖音上限不同，跨平台不知道如何取舍；
  - 提升码率画质没变化，怀疑白填。
- **原因**：
  - 画质由「码率 ÷ 像素量 ÷ 动态复杂度」共同决定，分辨率翻倍码率需近似翻倍以上；
  - 各平台转码档位不同，超出平台承载上限的部分纯属浪费。
- **步骤**：
  1. 常规档位速查（H.264 CBR）：720p60≈4500~6000k；1080p60≈6000~9000k；1440p60≈12000~16000k；4K60≈30000k+；
  2. HEVC/AV1 可在同观感下降 20~35%（Twitch 增强广播 / B站 HDR 等支持场景）；
  3. 以上行为硬约束倒推：可用上行 × 0.75 = 最大码率，不够就降分辨率而不是硬撑（lag-upload）；
  4. 改动后用平台回放实测观感，别只看本地预览。
- **提示**：码率翻倍收益递减：6000k→9000k 提升明显，9000k→14000k 就很有限了，优先保稳。
- **关联**：rc-fps-specs, lag-upload, lag-bitrate-mosaic, enc-preset-guide, enc-recording-cqp
- **链接**：[Twitch 广播规范](https://help.twitch.tv/s/article/broadcasting-guidelines)；[B站投稿规范](https://member.bilibili.com/platform/upload/video/frame)

### 21. `tr-stinger`｜Stinger 转场卡顿 / 黑帧 / 前后画面跳变
- **分类**: config ｜ **严重度**: 一般 ｜ **平台**: 全平台
- **症状**：
  - 转场视频播完后闪一下黑帧才切过去；
  - Stinger 起播有明显延迟，节奏对不上点；
  - 过渡期间主场景音量突变。
- **原因**：
  - 转场视频透明通道格式不对（VP9/WebM 带 alpha 在低端机上解码慢）；
  - 「过渡时机」设置为「过渡完成后切换场景」但视频尾部有多余空帧；
  - 转场点（Transition Point）百分比没对准遮罩合拢帧。
- **步骤**：
  1. 转场素材用 ProRes 4444（macOS）或 WebM/VP9 alpha（Windows），分辨率压到 1080p 减轻解码压力；
  2. 右键过渡 → 属性：精确调整「过渡点」，在遮罩完全盖住画面的那一帧设为切换点；
  3. 裁掉视频首尾多余帧（ffmpeg -to 或剪辑软件）；
  4. 首次使用预热：开播前手动触发一次转场，让解码器缓存就绪。
- **提示**：转场音频轨会被一起播出，做素材时把音效放在遮罩合拢之后。
- **关联**：cf-transition, rc-media-source, src-media-loop
- **链接**：[OBS 官方知识库](https://obsproject.com/zh-cn/kb)

### 22. `perf-nested-scenes`｜嵌套场景滥用导致的性能与同步坑
- **分类**: performance ｜ **严重度**: 小众 ｜ **平台**: 全平台
- **症状**：
  - 场景层层嵌套后渲染延迟上涨、切场卡顿；
  - 嵌套场景里的滤镜/变换在外层看起来“不生效”或坐标错乱；
  - 同一嵌套体在多个场景复用时音画出现重复。
- **原因**：
  - 每层嵌套都是一次完整的离屏渲染 pass，深度嵌套乘法效应明显；
  - 嵌套场景默认「作为来源合成」，边界框变换会重置内部布局；
  - 勾选了「显示仅激活的嵌套场景来源」语义易误解。
- **步骤**：
  1. 嵌套控制在 2 层以内，能用分组（Group）表达的别建子场景；
  2. 复用画面统一走「引用式」思路：一个基础场景 + 外层少量差异元素；
  3. 检查嵌套场景属性的「边界框类型」，用「缩放到边界内」保持原布局；
  4. 用统计面板对比重构前后渲染延迟验证收益。
- **提示**：分组适合“一起拖动”的静态组织，嵌套场景适合“整套复用”，别反着用。
- **关联**：perf-obs-overhead, cf-transition, st-scene
- **链接**：[OBS Studio 深度优化与故障排查指南（2026 版）](https://tsight.io/articles/10161700)

### 23. `log-file-size`｜日志文件巨大 / 占满磁盘 / 无法上传分析
- **分类**: config ｜ **严重度**: 小众 ｜ **平台**: 全平台
- **症状**：
  - %AppData%\obs-studio\logs 里单个日志几百 MB；
  - 上传日志分析器超时失败；
  - 系统盘被悄悄吃掉几十 GB。
- **原因**：
  - 长时间挂机 + 反复重连产生海量重连日志；
  - 某些插件/脚本高频打印调试信息；
  - 从不清理历史日志。
- **步骤**：
  1. 定期清理 logs 目录（保留最近几次即可）；本助手日志页可一键直达目录；
  2. 复现问题后尽快停止并上传当前日志，避免超长日志稀释关键行；
  3. 怀疑插件刷屏时安全模式对照，锁定后向插件仓库反馈；
  4. 崩溃转储目录 crashes 同理定期清理。
- **提示**：求助发日志前先看大小，几十 MB 的日志没人愿意读，截取问题时间段前后即可。
- **关联**：cfg-log-analyzer, cr-plugin, cr-safe-mode
- **链接**：[OBS 官方日志分析器](https://obsproject.com/analyzer)

---

## 四、推流 / 平台接入类（7 条）

### 24. `sf-service-list-outdated`｜平台改了推流地址后连不上（services.json 过期）
- **分类**: streamfail ｜ **严重度**: 一般 ｜ **平台**: 全平台
- **症状**：
  - 平台公告换了服务器地址，OBS 下拉里还是旧的；
  - 自定义填写新地址能连，选内置服务就连不上；
  - 日志提示 Failed to connect to server。
- **原因**：
  - OBS 内置服务列表来自官方 services.json，随安装包分发，平台临时变更时本地列表滞后；
  - CDN 节点调整期间旧地址短暂可用又失效。
- **步骤**：
  1. 服务选「自定义…」手工填平台最新服务器地址与串流密钥应急；
  2. 升级 OBS 到最新版获取新 services.json；
  3. 关注平台创作者公告，节点迁移期优先用自定义地址；
  4. 连通性用工具箱 ingest 探测验证端口与 RTT。
- **提示**：把平台最终地址存在配置文件（Profile）注释里，换机重建快。
- **关联**：sf-server, sf-timeout, sf-ingest-ping, st-general
- **链接**：[OBS 官方知识库](https://obsproject.com/zh-cn/kb)

### 25. `sf-relay-proxy`｜经中转 / 加速代理推流的正确姿势与失败排查
- **分类**: streamfail ｜ **严重度**: 进阶 ｜ **平台**: 全平台
- **症状**：
  - 直接连平台丢帧严重，听说可以推到云服务器中转但不会配；
  - 配了 HTTP/SOCKS 代理后 OBS 完全连不上；
  - 中转链路延迟增大导致互动卡。
- **原因**：
  - OBS 推流不走系统代理，HTTP 代理软件对 RTMP 无效；
  - 中转方案本质是「推到你的 VPS，VPS 转发平台」，需要服务器端 SRS/nginx-rtmp；
  - 跨境线路抖动在中转段叠加。
- **步骤**：
  1. 明确架构：OBS → 中转服务器（SRS/nginx-rtmp）→ 平台，OBS 里服务填自定义、地址填 VPS；
  2. 服务器端配置转发并开启转发带宽监控；OBS 端按 sf-ingest-ping 实测到 VPS 的丢帧；
  3. 不要指望 HTTP 代理加速 RTMP；VPN 型方案参见 sf-vpn；
  4. 中转引入的固定延迟计入互动预期，问答类直播慎用长链路。
- **提示**：中转服务器带宽 ≥ 推流码率 × 2（入+出），小水管 VPS 别拿来转 1080p60。
- **关联**：sf-vpn, lag-network, sf-ingest-ping, setup-dual-pc
- **链接**：[OBS 官方知识库](https://obsproject.com/zh-cn/kb)

### 26. `st-youtube-token`｜YouTube 直播授权过期 / 提示重新登录 / 预定活动无法开播
- **分类**: setup ｜ **严重度**: 一般 ｜ **平台**: 全平台
- **症状**：
  - 内置 YouTube 登录后过几天提示 token 失效；
  - 预定的直播到点 OBS 里仍是「未开始」；
  - 切换 Google 账号后推流到了错误的频道。
- **原因**：
  - OAuth 令牌过期或被 Google 安全策略撤销（改密码/异地登录）；
  - 预定活动要求通过「活动 ID」绑定推流，随便推会进不了预定场次；
  - 一个 Chrome Profile 缓存了多个账号。
- **步骤**：
  1. 设置 → 推流 → YouTube/RTMPS：断开连接后重新登录授权；
  2. 预定活动：在直播控制台复制该活动的专用串流密钥填入，或登录后在 OBS 里选择对应活动；
  3. 检查频道选择，确保推的是目标频道而非测试频道；
  4. 频繁掉线检查是否有其他设备/软件在共用同一密钥开播（sf-auth）。
- **提示**：YouTube 密钥与活动绑定，改期后记得同步更新 OBS 里的活动。
- **关联**：st-youtube, sf-auth, sf-key-leak
- **链接**：[YouTube 直播帮助](https://support.google.com/youtube/answer/2474026)

### 27.【新】`sf-enhanced-broadcast`｜Twitch 增强广播（Enhanced Broadcasting）：开启条件与常见故障
- **分类**: streamfail ｜ **严重度**: 小众 ｜ **平台**: Windows
- **症状**：
  - 听说 Twitch 支持「按观众带宽自适应画质」，开启后反而掉帧/画质不稳；
  - 开关灰着选不了；
  - 开启后 GPU 占用大涨、游戏掉帧。
- **原因**：
  - 增强广播会让 OBS 同时编码多档画质（HEVC/AV1 + H.264），NVENC 会话与算力需求成倍增长；
  - 需要 Twitch 侧白名单逐步放开 + OBS 31+/新版驱动支持，条件不满足时入口不可用。
- **步骤**：
  1. 确认 OBS 为最新稳定版、驱动较新、Twitch 账号已获得该功能灰度；
  2. 在 Twitch 创作仪表盘开启增强广播后，OBS 推流设置会出现对应选项，档位数量保守起步（2 档）；
  3. 盯紧编码滞后与 GPU 占用，超载就减档或回退普通 RTMP 单档；
  4. 观众端兼容性：老客户端只吃 H.264 基础档属正常。
- **提示**：国内平台暂无对应能力，此功能目前只影响 Twitch 场景。
- **关联**：sf-webrtc-simulcast, en-nvenc, enc-preset-guide, lag-upload
- **链接**：[Twitch 帮助中心 · Enhanced Broadcasting](https://help.twitch.tv/s/article/enhanced-broadcasting)

### 28. `lag-srt-output`｜SRT / RIST 输出：远程导播与内网传输的配置要点
- **分类**: streamfail ｜ **严重度**: 进阶 ｜ **平台**: 全平台
- **症状**：
  - 想把画面低延迟传到另一台机器/导播台，不知用什么协议；
  - 自定义服务器填 srt:// 后连不上或马赛克；
  - 跨公网 SRT 丢包重传导致延迟累积。
- **原因**：
  - SRT/RIST 输出走「自定义服务器」字段（srt://host:port?... 参数），参数拼错最常见；
  - latency/buffer 参数与链路 RTT 不匹配导致花屏或延迟爆炸。
- **步骤**：
  1. 确认用途：内网导播分发用 SRT 很合适；对公网观众分发仍应走平台 RTMP；
  2. 接收端先用 VLC/ffplay 验证 srt 流可达，再排 OBS；
  3. 按 RTT 设置 latency（建议 ≥ 3×RTT，跨公网起步 120ms），花屏加大、延迟过高减小；
  4. 公网链路先测 UDP 丢包率，>1% 的线路先修网络再谈协议。
- **提示**：SRT 是「点对点推流」不是魔法加速器，烂线路上它只是优雅地劣化。
- **关联**：sf-relay-proxy, setup-dual-pc, st-ndi, sf-timeout
- **链接**：[OBS 官方知识库](https://obsproject.com/zh-cn/kb)

### 29. `cfg-multi-instance`｜同时开两个 OBS：多实例与便携目录的正确用法
- **分类**: config ｜ **严重度**: 小众 ｜ **平台**: Windows / macOS / Linux
- **症状**：
  - 想一边直播一边另开一个 OBS 录制别的窗口，第二次启动没反应；
  - 强行多开后两个实例互相改配置、场景集合打架；
  - 想给不同项目用不同配置但总是串。
- **原因**：
  - OBS 默认单实例互斥，防呆设计；
  - 多实例共享同一配置目录时会争抢 profile/场景集合与音频设备。
- **步骤**：
  1. 给第二个 OBS 建便携目录：文件夹里放 `portable_mode` 空文件（或 `portable_mode_username`/数据隔离变体），实例各自独立配置；
  2. 以命令行参数 `-m`（--multi）启动允许多开；
  3. 两个实例绝不能共用同一个音频输入设备，第二个实例改走虚拟声卡或不同设备；
  4. 明确分工：一台实例推流、一台实例录制，避免双编码挤在同一实例。
- **提示**：多数「想多开」的需求其实用「多场景集合 + 热键切换」就能满足，先试简单的。
- **关联**：cf-portable-mode, cf-profiles, perf-dual-encode
- **链接**：[OBS 官方知识库](https://obsproject.com/zh-cn/kb)

### 30. `cf-portable-mode`｜便携版配置丢失 / 换电脑后设置不跟随
- **分类**: config ｜ **严重度**: 小众 ｜ **平台**: Windows
- **症状**：
  - 绿色版拷到新机器后场景集合全没了；
  - 安装版与便携版的插件互相找不到；
  - 便携目录里多了 global.ini 却没带上场景数据。
- **原因**：
  - portable_mode 标记文件缺失时回落到 %AppData%，看起来就是「配置丢了」；
  - 便携目录结构被改动，basic 目录不在预期位置。
- **步骤**：
  1. 检查 OBS 主程序同级目录下是否存在 `config/obs-studio` 数据目录；portable_mode 文件必须在 exe 同级；
  2. 迁移整机时连 config 子目录一起拷贝；
  3. 插件放 `<便携目录>\obs-plugins\64bit`，data 同步放置；
  4. 混用需求者用 `--profile`/`--collection` 启动参数指定加载项。
- **提示**：便携版更新 = 覆盖 exe 时千万别删 config 目录。
- **关联**：cfg-multi-instance, cf-profiles, cf-reset, cf-steam-plugins
- **链接**：[OBS 官方知识库](https://obsproject.com/zh-cn/kb)

---

## 五、来源 / 场景创作类（9 条）

### 31. `cfg-browser-cookies`｜浏览器源登录状态不保留 / Cookie 清空
- **分类**: sources ｜ **严重度**: 一般 ｜ **平台**: 全平台
- **症状**：
  - 在浏览器源里登录过的网站，重启 OBS 后又要重新登录；
  - 弹幕/后台面板频繁掉登录态；
  - 某站点检测到「自动化环境」拒绝登录。
- **原因**：
  - OBS 内置 CEF 的存储按「浏览器源名称 + URL」隔离，改名/改 URL 即丢；
  - 「关闭不可见时刷新」或重启清缓存策略清掉了会话；
  - 部分站点对嵌入式 WebView 有风控。
- **步骤**：
  1. 固定来源命名与 URL，不要为了整洁随意重命名；
  2. 关闭该来源的「不可见时刷新」，避免切场景即掉登录；
  3. 需要稳定登录态的面板优先用「自定义浏览器 Dock」或外部浏览器窗口替代；
  4. 风控严的站点（如某些后台）改用 OBS Dock + 二维码扫码登录。
- **提示**：重要面板登录后先重启 OBS 验证会话确实持久化了，再投入直播使用。
- **关联**：bs-browser-src, src-browser-alert, cfg-browser-dock-refresh, st-chat
- **链接**：[OBS 论坛 · Windows 支持](https://obsproject.com/forum/list/windows-support.32/)

### 32. `src-media-loop`｜媒体源循环播放时卡一下 / 结尾跳帧
- **分类**: sources ｜ **严重度**: 一般 ｜ **平台**: 全平台
- **症状**：
  - 循环视频每次回到开头顿挫一下；
  - 长视频循环几轮后音画开始漂移；
  - 素材明明无缝剪辑，循环处仍有黑帧。
- **原因**：
  - 解码器循环重定位的固有开销，编码参数非闭环（缺尾部参考帧）时更明显；
  - 素材首尾 GOP 不闭合；
  - 硬解 + 低端核显的组合在循环瞬间掉帧。
- **步骤**：
  1. 素材重编码为「闭合 GOP、关键帧间隔 1~2 秒、恒定帧率」的 H.264/HEVC；
  2. 媒体源属性勾选「本地文件 + 循环 + 重启时从头播放」按需取舍，网络素材一律先下载到本地；
  3. 硬解异常就关掉「使用硬件解码」试软解对比；
  4. 无缝背景视频建议做成 10 秒内短循环并预压一遍验证。
- **提示**：直播挂机背景视频务必开播前让它完整循环三五分钟观察稳定性。
- **关联**：rc-media-source, av-drift-long, tr-stinger
- **链接**：[OBS 官方知识库](https://obsproject.com/zh-cn/kb)

### 33. `src-vlc-source`｜VLC 视频列表源：播放顺序 / 循环 / 缺插件问题
- **分类**: sources ｜ **严重度**: 小众 ｜ **平台**: Windows
- **症状**：
  - 添加「VLC 视频源」后列表为空或不播放；
  - 想顺序播放歌回切片但总是随机；
  - 切歌间隙黑屏难看。
- **原因**：
  - 依赖本机安装的 VLC 及其解码器，未装或版本不匹配时源不可用；
  - 播放模式（顺序/循环/随机）藏在源属性底部，默认随机；
  - 列表项之间天然有空隙。
- **步骤**：
  1. 安装与本机架构匹配的 VLC 后重启 OBS；
  2. 属性 → 播放列表填入本地文件，勾选「循环列表」并按需关闭随机；
  3. 用「场景内的图片源垫底」承接间隙，或把素材拼接成一个长视频规避间隙；
  4. 配合 Advance Scene Switcher 插件实现按媒体切换场景。
- **提示**：新版 OBS 媒体源已支持播放列表场景不多，VLC 源仍是批量歌回/影片马拉松的首选。
- **关联**：rc-media-source, src-media-loop, cf-hotkeys
- **链接**：[OBS 官方知识库](https://obsproject.com/zh-cn/kb)

### 34. `src-slideshow`｜图像幻灯片放映：轮播间隔 / 乱序 / 内存占用
- **分类**: sources ｜ **严重度**: 小众 ｜ **平台**: 全平台
- **症状**：
  - 幻灯片不按文件夹顺序播放；
  - 换图间隔与设定不符，偶尔连闪两张；
  - 塞入上百张高清图后内存暴涨。
- **原因**：
  - 排序按文件名而非拍摄时间，数字编号未补零（1,10,2…）；
  - 「随机播放」被勾选；间隔计时基于渲染帧，掉帧时表现漂移；
  - 全尺寸原图全部驻留显存/内存。
- **步骤**：
  1. 文件名统一补零编号（001.jpg…999.jpg）保证排序；
  2. 属性里明确「按顺序/随机」与切换间隔；需要精确控场的用多图片源 + 热键替代；
  3. 预先把图缩放到展示分辨率（1920 宽足够）再导入；
  4. 大图库拆分成多个幻灯片源分场景调用。
- **提示**：相册回放场景建议直接剪成视频用媒体源播，比实时轮播稳定得多。
- **关联**：rc-media-source, perf-obs-overhead, cf-canvas
- **链接**：[OBS 官方知识库](https://obsproject.com/zh-cn/kb)

### 35. `cf-color-correction`｜颜色校正滤镜：修正偏色 / 统一多机位色彩
- **分类**: config ｜ **严重度**: 常用技巧 ｜ **平台**: 全平台
- **症状**：
  - 采集卡画面发灰 / 发绿，想校正但没有思路；
  - 双机位肤色不一致，观众一眼看出色温差；
  - 加了滤镜后暗部细节丢失。
- **原因**：
  - 采集链路色彩范围/空间错配（先查 cf-colorrange/cf-colorspace，滤镜救不了链路错）；
  - 校正顺序不当：伽马拉太高再压对比会夹死暗部；
  - LUT 强度 100% 直接套用电影级 LUT 过猛。
- **步骤**：
  1. 先修链路：确认色彩空间 709、范围与信号源一致（见相关条目），再谈滤镜；
  2. 来源 → 滤镜 → 颜色校正：先伽马提亮/压暗，再饱和度，最后对比度，每步 ±5 小幅迭代；
  3. 多机位统一：以主机位为基准，副机位套相同 LUT 并微调色温对齐肤色；
  4. 用「分割画面对照」截图到看图软件比对，别凭肉眼在动图上校色。
- **提示**：校色滤镜链保持 2 个以内，层层叠加必然劣化。
- **关联**：cf-colorrange, cf-colorspace, cf-chroma, cfg-sdr2hdr
- **链接**：[OBS 官方知识库](https://obsproject.com/zh-cn/kb)

### 36. `src-text-scroll`｜滚动字幕滤镜卡顿 / 边缘锯齿
- **分类**: sources ｜ **严重度**: 小众 ｜ **平台**: 全平台
- **症状**：
  - 「滚动」滤镜跑起来一顿一顿；
  - 文字滚过边缘有锯齿或残影；
  - 长文本滚完出现空白区。
- **原因**：
  - 滚动由 GPU 每帧平移实现，画布帧率低于 60 时观感天然卡；
  - 文本源宽度小于画布宽度，平移周期内露出底色；
  - 文本源分辨率过低被拉伸。
- **步骤**：
  1. 文本源宽度做到 ≥ 画布宽度 ×2（横向滚动），字号相应加大保证清晰度；
  2. 滚动速度用「像素/秒」心算：宽度 ÷ 期望循环秒数，别凭感觉拉满；
  3. 底部加同色底板遮挡边缘瑕疵；垂直滚动同理加高；
  4. 对流畅度要求高的跑马灯改用浏览器源 CSS 动画实现（GPU 合成更顺滑）。
- **提示**：跑马灯文字保持 ≤2 行，观众阅读速度远比你想象的慢。
- **关联**：cf-text-cjk, bs-browser-src, cf-canvas
- **链接**：[OBS 官方知识库](https://obsproject.com/zh-cn/kb)

### 37. `cfg-group-vs-nested`｜分组(Group)与嵌套场景的选择、变换陷阱
- **分类**: config ｜ **严重度**: 一般 ｜ **平台**: 全平台
- **症状**：
  - 把一组来源打组后单个元素没法独立加滤镜；
  - 组复制到另一场景后修改会「联动」影响所有副本；
  - 嵌套场景缩放后内部模糊。
- **原因**：
  - 分组只是「打包移动」，组内元素仍属于当前场景；复制组是深拷贝但滤镜归属易混乱；
  - 嵌套场景被外层缩放时经历二次重采样。
- **步骤**：
  1. 选型：一起拖动/一起显隐 → 分组；整套复用到多场景 → 嵌套场景；
  2. 分组内元素需要独立滤镜时，先解散组再加滤镜再重组；
  3. 嵌套场景属性勾「缩放到边界内」并把嵌套画布做成与外层一致的整数倍分辨率，避免模糊；
  4. 结构大改前导出场景集合备份（cfg-backup-before-upgrade）。
- **提示**：团队协作时用「引用命名规范」（base_/overlay_/guest_）管理嵌套层级，半年后还看得懂。
- **关联**：perf-nested-scenes, st-scene, cfg-copy-between-collections
- **链接**：[OBS 官方知识库](https://obsproject.com/zh-cn/kb)

### 38. `cfg-copy-between-collections`｜在两个场景集合之间复制场景 / 来源的方法
- **分类**: config ｜ **严重度**: 一般 ｜ **平台**: 全平台
- **症状**：
  - 新建了干净的场景集合，想把旧集合里的几个场景搬过来；
  - 复制粘贴来源后滤镜、热键丢了；
  - 手工编辑 JSON 后场景集合直接损坏打不开。
- **原因**：
  - OBS 的复制粘贴支持跨场景集合，但「过滤器/热键」分别挂在场景集合与全局配置上，行为不一致；
  - 手改 JSON 时 id 冲突或引用了不存在的来源。
- **步骤**：
  1. 首选 UI 操作：源列表右键 → 复制 → 切到目标集合 → 粘贴（引用可选，引用会联动改动）；
  2. 整场景迁移用「场景 → 复制场景（含来源）」，热键在新集合里重新绑定；
  3. 真要改 JSON：先导出备份，用文本编辑器整段搬运 scene 与 sources 节点并保证 uuid 唯一；
  4. 迁移完逐个场景点击验证滤镜/变换完整。
- **提示**：「粘贴（引用）」适合做模板联动，「普通粘贴」适合独立演化，想清楚再选。
- **关联**：cf-profiles, cfg-backup-before-upgrade, cf-reset
- **链接**：[OBS 官方知识库](https://obsproject.com/zh-cn/kb)

### 39. `st-ndi`｜NDI 接入：局域网多机位 / 手机作第二摄像头
- **分类**: setup ｜ **严重度**: 常用技巧 ｜ **平台**: Windows / macOS
- **症状**：
  - 装了 obs-ndi/DistroAV 插件但看不到 NDI 来源；
  - NDI 画面延迟大或周期性冻结；
  - 手机 NDI 相机搜不到。
- **原因**：
  - 缺少 NDI 运行时或插件与 OBS 版本不匹配；
  - NDI 依赖组播发现，路由器开启 AP 隔离 / 跨 VLAN 时不可达；
  - WiFi 链路带宽不足以承载高清 NDI 流。
- **步骤**：
  1. 安装 DistroAV（原 obs-ndi）+ NDI Runtime，版本与 OBS 大版本对应；
  2. 确认发送端与接收端在同一网段、路由器未开 AP 隔离；有线优先；
  3. 手机端用 NDI Camera 类 App 与 PC 同网段，OBS 里 NDI 来源下拉选择；
  4. 延迟敏感的多机位导播考虑降 NDI 分辨率或改走采集卡（setup-dual-pc）。
- **提示**：NDI 是局域网技术，公网连线请用 SRT（lag-srt-output）。
- **关联**：lag-srt-output, st-phone-as-camera, setup-dual-pc
- **链接**：[DistroAV 项目主页](https://github.com/DistroAV/DistroAV)

### 40. `st-phone-as-camera`｜手机当电脑摄像头（DroidCam / Iriun / NDI Camera）
- **分类**: setup ｜ **严重度**: 常用技巧 ｜ **平台**: Windows / macOS
- **症状**：
  - 手机相机 App 连上了但 OBS 里黑屏 / 帧率极低；
  - USB 连接识别不出设备（ADB 驱动问题）；
  - 画面颜色发灰、自动曝光乱跳。
- **原因**：
  - WiFi 传输受路由器负载影响，码率被压很低；
  - USB 方案依赖 ADB/itunes 驱动，未装或签名失败；
  - 手机端开了夜间模式/美颜导致色彩异常。
- **步骤**：
  1. 有 USB 线优先 USB：装齐驱动（Android 装 ADB 驱动，iPhone 装 iTunes）后选 USB 模式；
  2. WiFi 模式让手机与 PC 连同一路由 5GHz，避开拥挤信道；
  3. 手机端锁定曝光/对焦、关闭夜景与滤镜，分辨率设 1080p；
  4. 进阶玩法：手机 NDI 推流进 OBS（st-ndi），画质与稳定性优于多数虚拟摄像头方案。
- **提示**：手机长时间推流注意散热，摘掉壳 + 小风扇是直播标配。
- **关联**：vc-virtualcam-app, st-ndi, cf-webcam
- **链接**：[OBS 论坛 · Windows 支持](https://obsproject.com/forum/list/windows-support.32/)

---

## 六、录制专项（6 条）

### 41. `rc-audio-tracks-missing`｜录制文件缺少某些音轨 / 剪辑软件里看不到分轨
- **分类**: recording ｜ **严重度**: 常见 ｜ **平台**: 全平台
- **症状**：
  - 明明设置了分轨，导出的 MP4 在剪辑软件里只有一轨；
  - 播放器播放 MKV 正常有声，转 MP4 后少轨；
  - 第二轨以后全是静音。
- **原因**：
  - 简单输出模式只录单轨，分轨设置根本没生效；
  - 录像设置的「音频轨」复选框没勾全（只勾了轨 1）；
  - 转封装/剪辑导入时软件只读了第一条轨。
- **步骤**：
  1. 设置 → 输出 → 高级 → 录像：勾选需要的全部音频轨并为每轨设码率；
  2. 高级音频属性里给每个源勾对应轨道（见 au-track-split）；
  3. 格式用 Hybrid MP4/MKV；转 MP4 后用 ffprobe 或剪辑软件确认轨数；
  4. 剪辑软件导入时检查「音轨 2/3」是否被折叠隐藏（PR/AE 默认全导入，剪映需手动添加轨道）。
- **提示**：交付给别人剪辑的素材，附一行说明告知各轨内容，能省一半沟通成本。
- **关联**：au-track-split, rc-mkv, rc-hybrid-mp4, rc-audio-missing
- **链接**：[OBS 官方知识库](https://obsproject.com/zh-cn/kb)

### 42. `rc-chapter-markers`｜章节标记（Chapter Markers）：直播中途打点与后期定位
- **分类**: recording ｜ **严重度**: 常用技巧 ｜ **平台**: 全平台（OBS 31+）
- **症状**：
  - 长录像想快速回溯精彩片段，只能拖进度条盲找；
  - 不知道新版 OBS 已支持一键打章节标记；
  - 打了标记的文件在某些播放器里看不到章节。
- **原因**：
  - 章节元数据仅 Hybrid MP4/MOV 与 MKV 容器完整支持，旧格式或转封装会丢；
  - 部分播放器/平台不解析章节轨。
- **步骤**：
  1. 设置 → 热键 → 为「添加章节标记」绑定快捷键；直播/录制中关键时刻按一下；
  2. 录制格式确认为 Hybrid MP4 或 MKV（31+ 默认即是）；
  3. 后期在支持的剪辑软件/播放器里按章节跳转；导出到平台前用 ffmpeg 保留 metadata 映射；
  4. 旧版本用户用 Source Copy / 章节插件方案替代。
- **提示**：团战/开箱/公布结果前提前 1 秒打点，后期效率天差地别。
- **关联**：rc-hybrid-mp4, rc-mkv, rc-schedule, rc-replay-buffer
- **链接**：[OBS 官方 · 发布说明（31.x 章节标记）](https://obsproject.com/blog/obs-studio-31-0-release-notes)

### 43. `rc-replay-hotkey`｜回放缓冲保存失败：快捷键「按下 vs 松开」与时长设置
- **分类**: recording ｜ **严重度**: 一般 ｜ **平台**: 全平台
- **症状**：
  - 按了保存热键但输出目录没有新文件；
  - 存出的片段比预期短很多；
  - 回放缓冲开着却时不时自动停用。
- **原因**：
  - 热键绑成了「按住型」语义理解错误，或与游戏键冲突没触发；
  - 「重播缓冲时长」短于精彩片段实际长度；
  - 磁盘剩余空间不足时 OBS 自动停止缓冲保护。
- **步骤**：
  1. 热键设置里区分「开始回放保存」单击绑定，避免绑定到需要组合状态的键；
  2. 回放缓冲时长按内容类型设：FPS 高光 60~90 秒，日常 30 秒足够；时长越大内存占用越高；
  3. 检查录像目录磁盘余量与写入速度（rc-disk-speed）；
  4. 保存的片段落在录像目录下名为 Replay Buffers 的子目录（或自设路径），先找对地方再说没保存。
- **提示**：给「保存回放」配一个顺手的鼠标侧键，团战瞬间手不用离开准星。
- **关联**：rc-replay-buffer, cfg-hotkey-conflict, rc-disk-speed
- **链接**：[OBS 官方知识库](https://obsproject.com/zh-cn/kb)

### 44. `rc-filename-pattern`｜录制文件名格式化符与中文/特殊字符路径问题
- **分类**: recording ｜ **严重度**: 小众 ｜ **平台**: Windows
- **症状**：
  - 文件名里想要日期时间/场景名，不知道格式化语法；
  - 保存路径含中文或空格时报「无法写入文件」；
  - 自动分段文件名重叠覆盖旧文件。
- **原因**：
  - 文件名格式化符（%CCYY-%MM-%DD 等）拼写错误时按字面输出或为空；
  - 老版本/特定解码链路对非 ASCII 路径兼容差；
  - 格式串不含唯一后缀导致同名覆盖。
- **步骤**：
  1. 设置 → 高级 → 录像文件名格式：用官方格式化符拼装，例如 `%CCYY-%MM-%DD %hh-%mm-%ss`；
  2. 录像路径改为纯英文无空格目录（如 D:\OBSRec），兼容性最好；
  3. 确认「无空格覆盖」逻辑：格式里必须含时间戳类变量；
  4. 网络盘/NAS 路径先做本地写入再同步，避免 IO 抖动（rc-disk-speed）。
- **提示**：文件名里加 `%RN`(场景集合) 或 `%SN`(场景名)，素材检索效率翻倍。
- **关联**：rc-schedule, rc-disk-space, rc-disk-speed
- **链接**：[OBS 官方知识库](https://obsproject.com/zh-cn/kb)

### 45. `rc-mac-sleep`｜macOS 合盖 / 休眠中断录制
- **分类**: recording ｜ **严重度**: 一般 ｜ **平台**: macOS
- **症状**：
  - 长录制中途 Mac 休眠，文件戛然而止；
  - 合盖外接显示器时录制中断；
  - 屏幕保护触发后画面定格。
- **原因**：
  - macOS 电源管理在判定无交互时入睡，OBS 默认断言不一定覆盖所有场景；
  - 电池模式下节能策略更激进。
- **步骤**：
  1. 系统设置 → 显示器 → 高级：关闭「在显示器关闭时防止自动进入睡眠」的歧义项，锁定「防止自动进入睡眠」；
  2. 长录制接电源 + 使用 caffeinate 命令或 Amphetamine 类工具维持唤醒；
  3. 合盖录制需外接供电，并确认外接屏为主输出；
  4. 关闭屏幕保护与锁定延时，或在隐私安全性里临时放宽。
- **提示**：录制前用「电池/能源」设置确认睡眠时间为「永不」（录制时段）。
- **关联**：rc-schedule, av-drift-long, bs-mac-perm
- **链接**：[Apple 支持 · Mac 睡眠设置](https://support.apple.com/zh-cn/102839)

### 46. `vc-mac-virtualcam-permission`｜macOS 虚拟摄像头需批准系统扩展 / 重启后失效
- **分类**: virtualcam ｜ **严重度**: 一般 ｜ **平台**: macOS
- **症状**：
  - 点「启动虚拟摄像机」提示需要在系统设置批准；
  - 批准后 Zoom 里仍看不到 OBS 相机；
  - macOS 大版本更新后虚拟相机再次失效。
- **原因**：
  - 虚拟相机以 Core Media I/O DAL 插件形式注册，受系统扩展审批与 SIP 策略约束；
  - 系统更新会重置第三方扩展审批状态。
- **步骤**：
  1. 系统设置 → 隐私与安全性：找到 OBS 相关提示点「允许」，按要求重启；
  2. 会议软件完全退出重开（相机列表启动时扫描，见 vc-virtualcam-app）；
  3. 仍无效时重装 OBS（覆盖安装会重新注册 DAL 插件）；
  4. Safari/浏览器版会议额外检查网页相机权限。
- **提示**：每次 macOS 大版本更新后第一时间验证虚拟相机，别等到开会前。
- **关联**：vc-virtualcam-app, st-virtualcam, bs-mac-perm
- **链接**：[OBS 论坛 · macOS 支持](https://obsproject.com/forum/list/mac-support.33/)

---

## 七、界面 / 维护杂项（7 条）

### 47. `ui-highdpi`｜4K / 高分屏下 OBS 界面字体过小 / 界面错位
- **分类**: config ｜ **严重度**: 一般 ｜ **平台**: Windows
- **症状**：
  - 4K 屏上菜单和按钮小得看不清；
  - 缩放调大后部分面板溢出/重叠；
  - 多屏不同 DPI 时窗口在屏间拖动后模糊。
- **原因**：
  - OBS 界面缩放独立于系统 DPI，默认 100%；
  - 混合 DPI 多屏是 Qt 应用的老大难；
  - 系统缩放 >150% 时个别皮肤/主题适配不佳。
- **步骤**：
  1. 设置 → 界面 → 界面大小（UI Scale）按屏调至 125%~200%；
  2. 混合 DPI 环境：把 OBS 主窗口固定在高分屏，辅助 Dock 放低分屏；
  3. Windows 显示设置里对 obs64.exe 单独设置「高 DPI 缩放替代 → 应用程序」；
  4. 主题异常换回默认主题验证是否第三方主题未适配。
- **提示**：改完缩放重启 OBS 才完全生效，别急着下结论。
- **关联**：bs-dsr, cf-reset, os-win-update
- **链接**：[OBS 官方知识库](https://obsproject.com/zh-cn/kb)

### 48. `cf-ui-language`｜语言切换不生效 / 界面英文中文混杂
- **分类**: config ｜ **严重度**: 小众 ｜ **平台**: 全平台
- **症状**：
  - 设置里选了中文，重启后部分菜单仍是英文；
  - 语言列表里根本没有中文选项；
  - 升级后语言被重置回英文。
- **原因**：
  - 部分插件/新功能文案尚未翻译，属正常现象；
  - 便携版/绿色版缺 locale 文件；
  - 配置文件里 language 字段被重置。
- **步骤**：
  1. 设置 → 一般 → 语言 → 简体中文，完全退出重启 OBS；
  2. 少数英文词条是未翻译的新功能/第三方插件，等翻译更新即可；
  3. 绿色版确认 data\locale 目录完整，缺文件就重装；
  4. 反复重置则检查 global.ini 是否被杀软/同步盘回滚。
- **提示**：搜索教程时用英文术语（如 Game Capture）命中率更高，界面语言不影响功能。
- **关联**：cf-text-cjk, cf-portable-mode, cf-reset
- **链接**：[OBS 中文翻译平台](https://crowdin.com/project/obs-studio)

### 49. `os-update-fail`｜自动更新失败 / 下载缓慢 / 手动更新方法
- **分类**: crash ｜ **严重度**: 一般 ｜ **平台**: Windows
- **症状**：
  - 提示有新版本但更新进度条卡住或报错；
  - 下载速度极慢甚至 0KB；
  - 更新后版本号没变。
- **原因**：
  - 更新服务器在国内访问慢/不稳定；
  - 杀软或公司代理拦截更新器；
  - 安装目录权限不足（Program Files 写入被拒）。
- **步骤**：
  1. 官网或 GitHub Releases 手动下载完整安装包覆盖安装（配置不会丢）；
  2. 更新前退出杀软实时防护或把 OBS 加入白名单（cr-antivirus）；
  3. 安装时以管理员身份运行安装器；
  4. 更新失败反复发生时先跑 cfg-backup-before-upgrade 备份，再卸载重装最新版。
- **提示**：覆盖安装保留 %AppData% 配置；除非排障需要，否则不要勾选「删除用户配置」。
- **关联**：cr-downgrade, cr-antivirus, cfg-backup-before-upgrade, os-win-update
- **链接**：[OBS 官网下载](https://obsproject.com/download)；[GitHub Releases](https://github.com/obsproject/obs-studio/releases)

### 50. `os-admin-rights`｜要不要以管理员身份运行 OBS：收益与代价
- **分类**: crash ｜ **严重度**: 常用技巧 ｜ **平台**: Windows
- **症状**：
  - 游戏内热键失灵 / 游戏捕获黑屏，听说要管理员运行但不清楚副作用；
  - 管理员运行后某些拖拽/文件关联行为变化；
  - 开机自启的管理员 OBS 被 UAC 拦住。
- **原因**：
  - UIPI 机制阻止低权限进程接收高权限窗口的消息，管理员游戏因此「看不见」普通 OBS 的热键与钩子；
  - 管理员运行后 OBS 的文件对话框以提升权限工作，拖拽来源受限。
- **步骤**：
  1. 需要管理员的三种典型情况：玩管理员级反作弊游戏（Valorant/CS2）、游戏捕获黑屏、全局热键失效；
  2. 设置方法：快捷方式 → 属性 → 兼容性 → 以管理员身份运行；或计划任务实现免 UAC 自启；
  3. 不需要就不开：纯录制浏览器/办公场景收益为零，还可能带来拖拽异常；
  4. 开启后验证热键、捕获、虚拟相机三项核心功能均正常。
- **提示**：给「管理员运行的 OBS」单独做一个快捷方式，日常用普通图标，玩游戏再切。
- **关联**：cfg-hotkey-conflict, au-ptt, bs-game, cr-env-interference
- **链接**：[社区指南 · Global Hotkeys Not Registering](https://salivity.github.io/obs-studio/article/how-to-fix-obs-studio-global-hotkeys-not-registering)

### 51. `linux-flatpak-plugins`｜Linux Flatpak/Snap/Nix 版 OBS：插件装不上或全都不加载
- **分类**: crash ｜ **严重度**: 小众 ｜ **平台**: Linux
- **症状**：
  - 按教程装了插件但 OBS 里完全不出现；
  - Flatpak 版看不到系统目录装的插件；
  - NixOS 上 wrapOBS 定义后插件神秘失踪。
- **原因**：
  - Flatpak/Snap 沙箱看不到宿主 ~/.local/share/obs-plugins 等路径，要用 flatpak 版专属命令安装；
  - NixOS 中 obs-studio 与 wrapOBS 同时声明时后者被覆盖。
  - 发行版仓库版 OBS 通常过旧，插件 API 不匹配。
- **步骤**：
  1. Flatpak 插件安装用 `flatpak install --from` 或把插件放入 `~/.var/app/com.obsproject.Studio/data/obs-studio` 对应目录；
  2. NixOS 检查 systemPackages 不要重复声明 obs-studio，只保留 wrapOBS 定义；
  3. 优先使用官方 PPA/Flatpak 获取新版 OBS，发行版仓库版仅作兜底；
  4. 插件加载失败看启动终端输出与 Help → Log Files。
- **提示**：Linux 排障第一步永远是确认「你装的是哪个发行渠道的 OBS」，三渠道配置互不相通。
- **关联**：cr-plugin-load, cf-steam-plugins, cr-plugin-manager
- **链接**：[NixOS Discourse · Fixing OBS Studio not seeing any plugins](https://discourse.nixos.org/t/fixing-obs-studio-not-seeing-any-plugins/47702)；[OBS 官方 Flatpak](https://flathub.org/en/apps/com.obsproject.Studio)

### 52. `cr-font-cache`｜启动慢在字体扫描 / 文本源加载字体失败
- **分类**: crash ｜ **严重度**: 小众 ｜ **平台**: Windows
- **症状**：
  - 启动卡在某一步很久，日志末尾停在字体相关行；
  - 文本(GDI+) 源显示方块或默认字体；
  - 刚安装/删除了一批字体后爆发。
- **原因**：
  - 系统字体缓存损坏，GDI/DirectWrite 枚举阻塞；
  - 安装的坏字体文件（0 字节/签名异常）拖垮枚举；
  - 云字体/字体管理工具的虚拟字体卷宗不可用。
- **步骤**：
  1. 清理字体缓存：删除 %WinDir%\ServiceProfiles\LocalService\AppData\Local\FontCache 内容并重启；
  2. 排查最近新装字体：移出系统字体目录后逐批回归；
  3. 文本源改用 FreeType2 类型绕开 GDI 枚举问题；
  4. 字体管理类软件（NexusFont 等）的临时加载字体在重启后失效，直播机改常规安装。
- **提示**：直播专用机保持字体库精简（≤200 个），审美字体留给剪辑机。
- **关联**：cr-slow-start, cf-text-cjk, os-win-update
- **链接**：[OBS 论坛 · Windows 支持](https://obsproject.com/forum/list/windows-support.32/)

### 53. `cfg-websocket-remote`｜WebSocket 远程控制连不上（obs-websocket 端口 / 密码）
- **分类**: config ｜ **严重度**: 一般 ｜ **平台**: 全平台
- **症状**：
  - Stream Deck / 手机遥控 App / TouchPortal 连不上 OBS；
  - 提示认证失败；
  - 本机能连、局域网其他设备连不上。
- **原因**：
  - OBS 28+ 已内置 obs-websocket，但默认未启用或密码未设置；
  - 端口 4455 被防火墙/其他程序占用；
  - 远程端填了 127.0.0.1 而不是 OBS 所在机器 IP。
- **步骤**：
  1. 工具 → WebSocket 服务器设置：启用、记下端口（默认 4455）与密码；
  2. 客户端填 OBS 主机局域网 IP（ipconfig 查看），不是 localhost；
  3. Windows 防火墙为 obs64.exe 放行专用网络入站；
  4. 认证失败重置密码；公网暴露务必强密码或走 VPN/SSH 隧道。
- **提示**：WebSocket 也是实现「Wayland 热键变通」「手机提词器翻页」的基础设施，值得一学。
- **关联**：cfg-hotkey-conflict, st-chat, sf-firewall
- **链接**：[obs-websocket 官方文档](https://github.com/obsproject/obs-websocket/blob/master/docs/generated/protocol.md)

---

## 八、其余补充（10 条）

### 54. `au-default-device-change`｜切换蓝牙/USB 声卡后 OBS 没声音或录到空音轨
- **分类**: audio ｜ **严重度**: 常见 ｜ **平台**: Windows / macOS
- **症状**：
  - 拔插耳机后 OBS 混音器桌面音频条不动了；
  - 系统默认设备变了但 OBS 还盯着旧设备录空气；
  - 蓝牙耳机重连后延迟突增。
- **原因**：
  - OBS 的「桌面音频」默认绑定「默认设备」，但部分驱动切换瞬间会话迁移失败；
  - 显式指定了具体设备名的来源在该设备消失后不会自动转移。
- **步骤**：
  1. 桌面音频保持「默认」可获得跟随能力；需要固定设备的高级玩法再显式指定；
  2. 设备切换后右键来源 → 属性重新选择一次设备立即恢复；
  3. 蓝牙设备参见 au-bluetooth（编解码与延迟专题）；
  4. 直播中尽量避免热插拔声卡；必须切换时先暂停推流。
- **提示**：开播前固定好音频拓扑，直播中换设备是事故高发操作。
- **关联**：au-bluetooth, au-mute, au-exclusive-mode, au-usb-headset-switch
- **链接**：[OBS 官方知识库](https://obsproject.com/zh-cn/kb)

### 55. `au-usb-headset-switch`｜USB 耳麦插拔后混音器设备失效（设备名变成灰色）
- **分类**: audio ｜ **严重度**: 一般 ｜ **平台**: Windows
- **症状**：
  - 混音器里麦克风/桌面音频条目变灰或显示「设备未激活」；
  - 重新插拔后才恢复，直播中发生很致命；
  - USB Hub 上的设备经常集体掉线。
- **原因**：
  - USB 选择性暂停 / Hub 供电波动导致设备重新枚举，OBS 持有的设备句柄失效；
  - 前置面板接口与劣质延长线接触不良。
- **步骤**：
  1. 关闭 USB 选择性暂停与设备省电（同 rc-device-disconnect 步骤）；
  2. 耳麦直插主板后置 USB 口，不与其他大功率设备共 Hub；
  3. 直播中失效的应急恢复：来源属性里重选设备（不必重启 OBS）；
  4. 反复发作换设备/换线定位是线材还是设备固件问题。
- **提示**：重要直播用有线 3.5mm 或独立声卡的耳麦，USB 掉线概率显著更低。
- **关联**：rc-device-disconnect, au-default-device-change, au-bluetooth
- **链接**：[OBS Windows 排障指南（2026 版）](https://obs-studio-app.github.io/obs-studio-troubleshooting-windows.html)

### 56. `au-loudness-normalize`｜观众反馈声音忽大忽小：响度一致性治理
- **分类**: audio ｜ **严重度**: 一般 ｜ **平台**: 全平台
- **症状**：
  - 安静段落听不清、喊叫段落炸耳；
  - 剪辑拼接多段素材后响度参差；
  - 与连麦嘉宾音量差距明显。
- **原因**：
  - 只用了限幅器没有压缩，动态范围原样保留；
  - 不同素材/嘉宾的输入电平基线不同；
  - 目标响度没有量化标准，全靠耳朵。
- **步骤**：
  1. 每个语音源建立统一链路：增益定基线（峰值 -12dB）→ 压缩（3:1）→ 限制（-1dB）（详见 au-mic-chain）;
  2. 剪辑交付物用响度目标归一：-14 LUFS（流媒体通用）/ -16 LUFS（播客），ffmpeg loudnorm 一条命令搞定；
  3. 连麦嘉宾让其先说 20 秒样本，按其峰值调增益到与你一致；
  4. BGM 走侧链闪避（au-ducking）避免与人声抢响度。
- **提示**：买不起响度表的先用耳朵执行「最响处不刺耳、最轻处听得清」二分标准。
- **关联**：au-mic-chain, au-ducking, au-game-quiet, au-track-split
- **链接**：[OBS 2026 直播设置指南（社区汇总）](https://techtippr.com/obs-settings-guide-for-streaming/)

### 57. `lag-power-plan`｜电源计划 / 现代待机导致掉帧与编码波动
- **分类**: lag ｜ **严重度**: 一般 ｜ **平台**: Windows
- **症状**：
  - 笔记本用电池直播帧率周期性下跌；
  - 接电源但计划为「平衡」时编码耗时曲线锯齿明显；
  - 睡眠唤醒后 OBS 性能久久不恢复。
- **原因**：
  - Windows 电源调度对后台进程激进降频（EPP/Power Throttling）；
  - 现代 standby 唤醒后设备电源状态恢复慢；
  - 混合显卡笔记本调度进一步复杂化。
- **步骤**：
  1. 推流/录制时接电源并将电源计划设为「高性能/卓越性能」；
  2. 设置 → 系统 → 电源：关闭「节省电源模式下降低部分视觉效果」，把 OBS 排除在节流外（图形设置 → obs64.exe → 高性能 GPU）；
  3. 电池直播是伪需求，坚持插电；
  4. 唤醒后性能异常就重启 OBS，必要时重启机器。
- **提示**：笔记本直播三件套：插电、高性能、独显直连（MUX）。
- **关联**：bs-dualgpu, lag-gamemode, lag-gpu-cap, perf-idle-cpu
- **链接**：[OBS Windows 排障指南（2026 版）](https://obs-studio-app.github.io/obs-studio-troubleshooting-windows.html)

### 58. `lag-background-io`｜后台程序抢占：网盘同步 / 杀毒扫描 / 系统索引拖垮直播
- **分类**: lag ｜ **严重度**: 常见 ｜ **平台**: Windows / macOS
- **症状**：
  - 直播中莫名周期性掉帧，时间点与 OneDrive/iCloud 同步、Windows Defender 扫描吻合；
  - 录像文件写入速率忽高忽低；
  - 开机后半小时内特别卡。
- **原因**：
  - 网盘同步与录像写同一块盘时 IO 争抢；
  - 杀毒实时扫描对录像大文件的写入钩子；
  - Windows Search 索引器扫到录像目录时疯狂读盘。
- **步骤**：
  1. 录像/推流期间暂停网盘同步（OneDrive 暂停 2 小时按钮），或把录像目录移出同步范围；
  2. 杀软把 OBS 目录与录像目录加入排除列表；
  3. 录像目录关闭 Windows Search 索引（文件夹属性 → 高级）；
  4. 用资源监视器确认直播时的磁盘队列长度接近 0 为健康。
- **提示**：直播机的「干净开机」（msconfig 精简启动项）能一次性消掉大半隐形杀手。
- **关联**：rc-disk-speed, cr-antivirus, perf-obs-overhead, lag-power-plan
- **链接**：[OBS Windows 排障指南（2026 版）](https://obs-studio-app.github.io/obs-studio-troubleshooting-windows.html)

### 59. `lag-router-qos`｜家庭网络抢带宽：家人看视频 / 下载把上行吃满
- **分类**: lag ｜ **严重度**: 常见 ｜ **平台**: 全平台
- **症状**：
  - 晚高峰准时掉帧，白天没事；
  - 家里有人看 4K 流媒体 / 打网盘时直播立刻劣化；
  - 有线直连测速正常，一到直播就崩。
- **原因**：
  - 家宽上行本就只有 30~50Mbps，一台设备云备份就能吃光；
  - 路由器未做 QoS，直播流量与下载流量平等竞争；
  - WiFi 下多设备竞争空口时间进一步恶化。
- **步骤**：
  1. 测明家宽实际上行（speedtest），预留推流码率 ×1.3 的余量给直播；
  2. 路由器开启 QoS/设备限速，把直播机设为最高优先级，限制其他设备上行；
  3. 直播机坚持有线；其他大流量任务（备份/下载）安排在非直播时段；
  4. 顽固拥塞考虑运营商上行提速或双线（直播走独立宽带）。
- **提示**：掉帧时间规律性强（每晚 8 点）几乎可以确诊为家庭带宽竞争而非 OBS 设置。
- **关联**：lag-upload, lag-wifi, lag-network, lag-dynamic-bitrate
- **链接**：[OBS 2026 直播设置指南（社区汇总）](https://techtippr.com/obs-settings-guide-for-streaming/)

### 60. `cf-mobile-safe-area`｜手机端观看被裁切：竖屏 / 横屏的安全区规划
- **分类**: config ｜ **严重度**: 一般 ｜ **平台**: 全平台
- **症状**：
  - 电脑上构图完美，手机竖屏观看时两侧人物被切；
  - 弹幕/点赞气泡挡住了字幕和脸；
  - 平台 UI（关注按钮、进度条）盖住关键信息。
- **原因**：
  - 竖屏客户端对横屏内容做放大裁切填充；
  - 平台悬浮控件占据画面四角与下三分之一；
  - 字幕位置低于安全区下沿。
- **步骤**：
  1. 竖屏直播直接用 1080×1920 画布制作原生竖屏内容（st-vertical），别指望横屏转竖屏；
  2. 横屏内容的关键信息（人脸、字幕、Logo）保持在中央 4:3 区域内，四周各留 ~10% 缓冲；
  3. 字幕放在画面高度 70%~85% 区间，避开弹幕密集区与平台进度条；
  4. 用手机实机预览 5 分钟再定稿构图。
- **提示**：B站/抖音的手机全屏播放裁切规则不同，主投平台用哪个客户端就以哪个为准。
- **关联**：st-vertical, rc-fps-specs, cf-canvas, st-general
- **链接**：[OBS 官方知识库](https://obsproject.com/zh-cn/kb)

### 61. `vc-vtuber-tracking`｜VTuber 面捕链路：追踪软件与 OBS 的配合与性能
- **分类**: virtualcam ｜ **严重度**: 小众 ｜ **平台**: Windows / macOS
- **症状**：
  - VSeeFace/PrprLive 追踪卡顿、表情延迟；
  - 面捕窗口捕获进 OBS 后背景不透明白块；
  - 面捕 + 游戏同时跑 GPU 爆炸。
- **原因**：
  - 面捕推理（尤其摄像头方案）持续占用 CPU/GPU；
  - 透明背景需要 Spout2(NVIDIA/Windows) 或 Syphon(macOS) 传递，用窗口捕获拿不到 alpha；
  - 模型面数过高在低端机上掉帧。
- **步骤**：
  1. 传输方式：Windows 用 Spout2 插件直收面捕软件的带透明通道画面；macOS 用 Syphon；
  2. 没有条件时用色键抠掉纯色背景（cf-chroma），精度略差但通用；
  3. 面捕软件降采样率/简化模型档位，把 GPU 预算留给游戏与编码；
  4. iPhone 面捕（ARKit）精度最好且不吃 PC 算力，值得配一根线。
- **提示**：皮套直播的三大性能黑洞：面捕、Live2D 高模、浏览器挂件——逐个做预算。
- **关联**：cf-chroma, perf-obs-overhead, vc-caption-plugins, st-phone-as-camera
- **链接**：[OBS 论坛 · Windows 支持](https://obsproject.com/forum/list/windows-support.32/)

### 62. `st-tiktok-live-studio`｜TikTok LIVE Studio / 海外抖音开播：资格与 OBS 接入
- **分类**: setup ｜ **严重度**: 一般 ｜ **平台**: Windows
- **症状**：
  - 找不到 TikTok 的推流密钥，不确定能否用 OBS；
  - LIVE Studio 客户端与 OBS 功能重复不知道选哪个；
  - 开播后地区限制提示。
- **原因**：
  - TikTok 对 OBS 直推实行粉丝数门槛（历史上 ≥1000 粉）与地区准入，资格不满足时后台不显示密钥入口；
  - LIVE Studio 是官方客户端，自带美颜/礼物互动，但灵活性低于 OBS。
- **步骤**：
  1. 达标用户在 TikTok 直播后台（或 LIVE Studio 设置）获取 RTMP 服务器与密钥，OBS 服务选「自定义」填入；
  2. 不达标只能先用 LIVE Studio 或手机开播攒资格；
  3. 注意账号注册地区与实际 IP 地区一致，跨境开播易触发风控；
  4. 竖屏直播用 1080×1920 画布（st-vertical），横屏内容会被裁切。
- **提示**：平台政策变动频繁，开播前以当日后台说明为准（同 setup-more-platforms 原则）；资格与入口以 TikTok 应用内「LIVE」页及创作者后台当日说明为准，此处不提供固定链接以免失效。
- **关联**：st-vertical, setup-more-platforms, st-general, sf-auth

### 63. `cf-autowizard`｜自动配置向导的参数不适合：何时该信、何时该推翻
- **分类**: config ｜ **严重度**: 一般 ｜ **平台**: 全平台
- **症状**：
  - 用向导「优化」后直播反而糊了 / 开始掉帧；
  - 向导给出的码率远低于平台推荐值；
  - 分辨率被自动降到 720p 心有不甘。
- **原因**：
  - 向导基于一次性的带宽测速与估算帧率做保守决策，测速瞬时值偏低就会全面保守；
  - 它不理解「游戏负载波动」「平台转码档位」这些上下文。
- **步骤**：
  1. 把向导结果当起点而非终点：先跑一场，再看日志分析器的丢帧/编码滞后数据；
  2. 码率按 enc-bitrate-guide 速查表人工复核，通常可上调 20%~50%；
  3. 分辨率/帧率按内容类型定（竞技游戏 1080p60 优先于 1440p30）；
  4. 每次只改一项并复测，避免多项同时改无法归因。
- **提示**：老手流程：向导打底 → 日志复盘 → 手工微调三步走，比裸调参数省一半时间。
- **关联**：enc-bitrate-guide, lag-stats, cfg-log-analyzer, rc-fps-specs
- **链接**：[OBS 官方日志分析器](https://obsproject.com/analyzer)

---

## 附：执行计划（审阅通过后）

1. 将上述 63 条按现有 JSON schema（Problem.cs 模型）写入
   `scripts/add_problems_2026_08c.py`（沿用 add_problems_2026_08b.py 的合并逻辑：
   读取 → 按 id 去重追加 → 更新 `updated` 与 `note` → UTF-8 写回）。
2. 校验：JSON 可解析、id 无冲突（149 → 212 条）、related 引用的 id 均存在。
3. 运行 `dotnet build`（及既有测试，如有涉及 ProblemData 的用例）确认资源加载正常。

**请审阅以上条目（标题、分类、严重度、内容口径均可增删改），
确认后回复"执行"我即开始写入 problems.json。**
