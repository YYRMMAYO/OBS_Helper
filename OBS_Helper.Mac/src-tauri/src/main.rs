// OBS 排障助手 macOS 宿主（Tauri v2）
// 仅作为 Blazor WebAssembly 站点的桌面外壳，不自带业务逻辑。
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

fn main() {
    tauri::Builder::default()
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
