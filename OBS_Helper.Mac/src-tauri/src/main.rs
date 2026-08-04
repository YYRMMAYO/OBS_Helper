// OBS 排障助手 macOS 宿主（Tauri v2）
// ---------------------------------------------------------------------------
// 作为 Blazor WebAssembly 站点的桌面外壳，只提供「WebView 里做不到」的能力：
//   1. 机密加密落盘（系统钥匙串 Keychain）；
//   2. 读取本机 OBS 日志目录（限定目录 + 限定扩展名）；
//   3. 用系统浏览器打开外链；
//   4. 可选的云端 AI 转发（API Key 不进入 WebView）。
// 以上全部收敛在唯一的 IPC 命令 `host_invoke` 内，见 src/host.rs。
//
// 安全说明：
// - 站点完全本地（来自 frontendDist），除用户显式开启的云端 AI 外不发起外网请求；
//   与 OBS 的通信是发往 127.0.0.1 的 WebSocket，不出本机。
// - capabilities 仍只保留 `core:default`：应用自身的命令（非插件命令）无需在 ACL 中
//   声明，写成 `allow-host-invoke` 反而会因权限标识符不存在导致构建失败。
// - 配置 tauri.conf.json 的 security.csp 作为纵深防御，限制脚本/连接来源。
// - 窗口禁用 devtools 与远程调试，避免本地静态内容被注入脚本后借助调试协议逃逸。
// - on_navigation 拦截所有导航：站点为纯本地内容，仅允许应用内资源（asset:/tauri:/ipc:）
//   与开发期 localhost，任何离开本地资源的导航一律取消，降低钓鱼/注入风险。
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

mod host;

use tauri::WebviewUrl;

fn main() {
    tauri::Builder::default()
        .invoke_handler(tauri::generate_handler![host::host_invoke])
        .setup(|app| {
            // 以编程方式创建窗口，并挂载导航白名单（替代 Tauri v1 的 set_navigation_handler）。
            let _win = tauri::WebviewWindowBuilder::new(
                app,
                "main",
                WebviewUrl::App("index.html".into()),
            )
            .title("OBS 排障助手")
            .inner_size(1180.0, 800.0)
            .min_inner_size(860.0, 600.0)
            .resizable(true)
            .fullscreen(false)
            .center()
            .on_navigation(|url| {
                // 仅允许本地资源与应用内导航；外部 http(s) 链接一律拦截，
                // 由前端通过宿主命令 shell.open 交给系统浏览器打开。
                let s = url.as_str();
                s.starts_with("asset:")
                    || s.starts_with("tauri:")
                    || s.starts_with("ipc:")
                    || s.starts_with("http://ipc.localhost")
                    || s.starts_with("http://localhost")
                    || s.starts_with("https://localhost")
            })
            .build()?;
            Ok(())
        })
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
