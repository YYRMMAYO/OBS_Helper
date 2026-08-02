// OBS 排障助手 macOS 宿主（Tauri v2）
// 仅作为 Blazor WebAssembly 站点的桌面外壳，不自带业务逻辑。
//
// 安全说明：
// - 站点完全本地（来自 frontendDist），不发起任何网络请求，亦不需要任何 Tauri 命令/插件；
//   因此 capabilities 仅保留 `core:default`，不开放任何 IPC 命令给前端。
// - 配置 tauri.conf.json 的 security.csp 作为纵深防御，限制脚本/连接来源。
// - 窗口禁用 devtools 与远程调试，避免本地静态内容被注入脚本后借助调试协议逃逸。
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

fn main() {
    tauri::Builder::default()
        .setup(|app| {
            if let Some(win) = app.get_webview_window("main") {
                // 站点为纯本地内容，不允许任何导航离开本地资源，降低钓鱼/注入风险。
                let _ = win.set_navigation_handler(|url: String| {
                    let s = url.as_str();
                    s.starts_with("http://localhost") || s.starts_with("https://localhost") || s.starts_with("asset:")
                });
            }
            Ok(())
        })
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
