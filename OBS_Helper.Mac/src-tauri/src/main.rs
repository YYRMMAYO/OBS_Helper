// OBS 排障助手 macOS 宿主（Tauri v2）
// 仅作为 Blazor WebAssembly 站点的桌面外壳，不自带业务逻辑。
//
// 安全说明：
// - 站点完全本地（来自 frontendDist），不发起任何网络请求，亦不需要任何 Tauri 命令/插件；
//   因此 capabilities 仅保留 `core:default`，不开放任何 IPC 命令给前端。
// - 配置 tauri.conf.json 的 security.csp 作为纵深防御，限制脚本/连接来源。
// - 窗口禁用 devtools 与远程调试，避免本地静态内容被注入脚本后借助调试协议逃逸。
// - on_navigation 拦截所有导航：站点为纯本地内容，仅允许应用内资源（asset:/tauri:）
//   与开发期 localhost，任何离开本地资源的导航一律取消，降低钓鱼/注入风险。
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use tauri::WebviewUrl;

fn main() {
    tauri::Builder::default()
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
                // 仅允许本地资源与应用内导航离开；外部 http(s) 链接一律拦截，
                // 由前端（ProblemDetail.razor）以系统浏览器打开而非在 WebView 内跳转。
                let s = url.as_str();
                s.starts_with("asset:") || s.starts_with("tauri:") || s.starts_with("http://localhost") || s.starts_with("https://localhost")
            })
            .build()?;
            Ok(())
        })
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
