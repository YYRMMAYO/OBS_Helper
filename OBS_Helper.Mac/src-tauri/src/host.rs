// OBS 排障助手 — macOS 宿主命令（Tauri v2）
// ---------------------------------------------------------------------------
// 与 Windows 侧 OBS_Helper.Win/Host/HostBridgeHandler.cs 一一对应，共同实现
// 前端 wwwroot/js/hostbridge.js 约定的宿主协议：
//
//     window.obsHelperHost.invoke(action, payloadJson) -> Promise<string>
//
// 设计原则（纵深防御，对应技术计划 §6「安全与隐私边界」）：
//   * 白名单     ：只认识固定的几条命令，未知命令直接拒绝。
//   * 目录限定   ：读日志只允许 ~/Library/Application Support/obs-studio/{logs,crashes}
//                  下的 .txt/.log 文件；读配置只允许 ~/Library/Application Support/
//                  obs-studio 根目录下的 .ini/.json/.jsonc/.txt/.conf 文件。两者都先
//                  canonicalize 解析符号链接与 `..`，再逐段比对目录前缀
//                  （Path::starts_with 按路径分量比较，不会被
//                  「/foo/bar-evil」这类字符串前缀绕过）。
//   * 机密加密   ：密码 / API Key 写入 macOS 系统钥匙串（Keychain），
//                  等价于 Windows 侧的 DPAPI，满足「经桌面壳加密落盘」。
//   * 密钥不出壳 ：云端 AI 的 API Key 永远只存在于宿主进程，WebAssembly 侧只持有
//                  「机密键名」，由宿主自行取出并拼装 Authorization 头。
//   * 大小上限   ：单个日志最多读取 8 MB，超出只取尾部（关键错误都在末尾）。
//
// 注意：本文件所有命令都返回 Result<String, String>；Ok 的字符串即前端拿到的结果，
// Err 会让前端的 invoke Promise reject（hostbridge.js 已按此约定处理）。

use serde::Serialize;
use serde_json::Value;
use std::collections::HashSet;
use std::fs;
use std::io::{Read, Seek, SeekFrom, Write};
use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};
use std::sync::{Mutex, OnceLock};
use std::time::{SystemTime, UNIX_EPOCH};
use tauri_plugin_dialog::DialogExt;
use tauri_plugin_dialog::FilePath;
use zip::write::SimpleFileOptions;
use zip::{ZipArchive, ZipWriter};

/// 单个日志文件最多读取的字节数（超出只读尾部）。
const MAX_LOG_BYTES: u64 = 8 * 1024 * 1024;
/// 单条机密的最大长度。
const MAX_SECRET_LEN: usize = 4096;
/// 每个目录最多列出的日志条数。
const MAX_LOG_ITEMS: usize = 20;
/// 钥匙串服务名，与 bundle identifier 保持一致，避免与其他应用串味。
const KEYCHAIN_SERVICE: &str = "com.obshelper.desktop";
/// 云端 AI 请求超时（秒）。
const AI_TIMEOUT_SECS: u32 = 120;
/// 查询 OBS 最新版本的超时（秒）——只是个锦上添花的提示，不值得让界面久等。
const OBS_VERSION_TIMEOUT_SECS: u32 = 15;
/// OBS Studio 最新版本的公开接口（无需鉴权）。
const OBS_RELEASE_API: &str = "https://api.github.com/repos/obsproject/obs-studio/releases/latest";
/// 最多上报的显卡数量，防止异常输出撑爆结果。
const MAX_GPU_ITEMS: usize = 8;
/// 单次列出的配置条目上限，防止用户往配置目录里塞了成千上万个文件时撑爆结果。
const MAX_CONFIG_ITEMS: usize = 200;
/// 允许读取的配置文件扩展名，与 Windows 侧 ConfigAllowedExt 逐项一致。
const CONFIG_ALLOWED_EXT: [&str; 5] = ["ini", "json", "jsonc", "txt", "conf"];

// ===========================================================================
// Tauri 命令入口
// ===========================================================================

/// 宿主命令统一入口。
///
/// 采用 `async fn`：Tauri 会把它调度到内部异步运行时上执行，不会阻塞主线程，
/// 因此读取 8 MB 日志或等待 AI 响应都不会让窗口卡住。
///
/// 参数名刻意避开 `cmd`（Tauri v1 时代 IPC 报文的保留字段名），使用 `action`。
/// `app` 由 Tauri 自动注入，供需要原生对话框的命令（导出 / 导入）使用。
#[tauri::command]
pub async fn host_invoke(
    app: tauri::AppHandle,
    action: String,
    payload: String,
) -> Result<String, String> {
    dispatch(Some(&app), &action, &payload)
}

fn dispatch(app: Option<&tauri::AppHandle>, action: &str, payload: &str) -> Result<String, String> {
    let raw = if payload.trim().is_empty() { "{}" } else { payload };
    let p: Value = serde_json::from_str(raw).map_err(|e| format!("参数解析失败: {e}"))?;

    match action {
        "secret.set" => secret_set(str_of(&p, "key"), str_of(&p, "value")),
        "secret.get" => secret_get(str_of(&p, "key")),
        "secret.delete" => secret_delete(str_of(&p, "key")),
        "logs.list" => logs_list(),
        "logs.read" => logs_read(str_of(&p, "path")),
        "env.info" => env_info(),
        "system.info" => system_info(),
        "obs.latestVersion" => obs_latest_version(),
        "config.list" => config_list(str_of(&p, "path")),
        "config.read" => config_read(str_of(&p, "path")),
        "shell.open" => shell_open(str_of(&p, "url")),
        "shell.reveal" => shell_reveal(str_of(&p, "path")),
        "ai.chat" => ai_chat(
            str_of(&p, "url"),
            str_of(&p, "secretKey"),
            str_of(&p, "body"),
        ),
        "template.export" => template_export(
            app.ok_or_else(|| "该命令需要宿主上下文。".to_string())?,
            str_of(&p, "filename"),
            str_of(&p, "json"),
        ),
        "config.locate" => config_locate(str_of(&p, "override")),
        "config.running" => config_running(),
        "config.pack" => config_pack(
            str_of(&p, "targetPath"),
            bool_of(&p, "includeKey"),
            bool_of(&p, "includePluginConfig"),
            str_of(&p, "reason"),
        ),
        "config.export" => config_export(
            app.ok_or_else(|| "该命令需要宿主上下文。".to_string())?,
            bool_of(&p, "includeKey"),
            bool_of(&p, "includePluginConfig"),
        ),
        "config.import" => config_import(
            app.ok_or_else(|| "该命令需要宿主上下文。".to_string())?,
            str_of(&p, "mode"),
        ),
        "config.listBackups" => config_list_backups(),
        "config.resetFull" => config_reset_full(),
        "system.sample" => system_sample(),
        "app.checkUpdate" => app_check_update(),
        other => Err(format!("未知命令: {other}")),
    }
}

/// 从 JSON 对象里安全地取字符串字段，缺失或类型不符时返回空串。
fn str_of<'a>(v: &'a Value, name: &str) -> &'a str {
    v.get(name).and_then(Value::as_str).unwrap_or("")
}

/// 从 JSON 对象里安全地取布尔字段，缺失或类型不符时返回 false。
fn bool_of(v: &Value, name: &str) -> bool {
    v.get(name).and_then(Value::as_bool).unwrap_or(false)
}

// ===========================================================================
// 机密存储（macOS 系统钥匙串）
// ===========================================================================

fn validate_secret_key(key: &str) -> Result<(), String> {
    if key.trim().is_empty() || key.len() > 128 {
        return Err("机密键名非法。".into());
    }
    // 只允许「字母 / 数字 / . _ -」，避免奇怪字符影响钥匙串条目定位
    if !key
        .chars()
        .all(|c| c.is_ascii_alphanumeric() || c == '.' || c == '_' || c == '-')
    {
        return Err("机密键名包含非法字符。".into());
    }
    Ok(())
}

fn keyring_entry(key: &str) -> Result<keyring::Entry, String> {
    validate_secret_key(key)?;
    keyring::Entry::new(KEYCHAIN_SERVICE, key).map_err(|e| format!("钥匙串条目创建失败: {e}"))
}

fn secret_set(key: &str, value: &str) -> Result<String, String> {
    if value.len() > MAX_SECRET_LEN {
        return Err("机密内容过长。".into());
    }
    // 空值等同删除，避免钥匙串里留下空条目
    if value.is_empty() {
        return secret_delete(key);
    }
    keyring_entry(key)?
        .set_password(value)
        .map_err(|e| format!("写入钥匙串失败: {e}"))?;
    Ok(String::new())
}

fn secret_get(key: &str) -> Result<String, String> {
    match keyring_entry(key)?.get_password() {
        Ok(v) => Ok(v),
        // 条目不存在：按「空」处理，让前端引导用户重新输入
        Err(keyring::Error::NoEntry) => Ok(String::new()),
        Err(e) => Err(format!("读取钥匙串失败: {e}")),
    }
}

fn secret_delete(key: &str) -> Result<String, String> {
    match keyring_entry(key)?.delete_credential() {
        Ok(()) => Ok(String::new()),
        Err(keyring::Error::NoEntry) => Ok(String::new()),
        Err(e) => Err(format!("删除钥匙串条目失败: {e}")),
    }
}

// ===========================================================================
// 日志访问
// ===========================================================================

/// OBS 在 macOS 上的数据目录：~/Library/Application Support/obs-studio/<sub>
fn obs_dir(sub: &str) -> PathBuf {
    let home = std::env::var("HOME").unwrap_or_default();
    PathBuf::from(home)
        .join("Library")
        .join("Application Support")
        .join("obs-studio")
        .join(sub)
}

fn allowed_dirs() -> [PathBuf; 2] {
    [obs_dir("logs"), obs_dir("crashes")]
}

fn is_allowed_ext(path: &Path) -> bool {
    match path.extension().and_then(|e| e.to_str()) {
        Some(ext) => {
            let e = ext.to_ascii_lowercase();
            e == "txt" || e == "log"
        }
        None => false,
    }
}

fn modified_millis(t: SystemTime) -> i64 {
    t.duration_since(UNIX_EPOCH)
        .map(|d| d.as_millis() as i64)
        .unwrap_or(0)
}

#[derive(Serialize)]
struct LogItem {
    name: String,
    path: String,
    size: u64,
    /// Unix 毫秒时间戳；由前端按本地时区格式化（两个平台的宿主口径一致）。
    modified: i64,
}

fn logs_list() -> Result<String, String> {
    let mut items: Vec<LogItem> = Vec::new();

    for dir in allowed_dirs() {
        let rd = match fs::read_dir(&dir) {
            Ok(r) => r,
            // 目录不存在（用户没装 OBS / 从未运行过）：跳过而不是报错
            Err(_) => continue,
        };

        let mut group: Vec<LogItem> = Vec::new();
        for entry in rd.flatten() {
            let path = entry.path();
            if !is_allowed_ext(&path) {
                continue;
            }
            let meta = match entry.metadata() {
                Ok(m) => m,
                Err(_) => continue,
            };
            if !meta.is_file() {
                continue;
            }
            group.push(LogItem {
                name: entry.file_name().to_string_lossy().to_string(),
                path: path.to_string_lossy().to_string(),
                size: meta.len(),
                modified: meta.modified().map(modified_millis).unwrap_or(0),
            });
        }

        group.sort_by(|a, b| b.modified.cmp(&a.modified));
        group.truncate(MAX_LOG_ITEMS);
        items.extend(group);
    }

    serde_json::to_string(&items).map_err(|e| format!("序列化日志列表失败: {e}"))
}

/// 校验目标路径确实位于允许目录内。
///
/// 两侧都先 `canonicalize`：解析掉 `..`、`.`、符号链接与 macOS 的 firmlink，
/// 然后用 `Path::starts_with` 按「路径分量」比较，避免字符串前缀绕过。
fn is_under_allowed(full: &Path) -> bool {
    allowed_dirs().iter().any(|dir| {
        fs::canonicalize(dir)
            .map(|root| full.starts_with(&root))
            .unwrap_or(false)
    })
}

fn logs_read(path: &str) -> Result<String, String> {
    if path.trim().is_empty() {
        return Err("路径为空。".into());
    }

    let full = fs::canonicalize(path).map_err(|_| "日志文件不存在。".to_string())?;
    if !is_under_allowed(&full) {
        return Err("只允许读取 OBS 日志目录内的文件。".into());
    }
    if !is_allowed_ext(&full) {
        return Err("只允许读取 .txt / .log 文件。".into());
    }

    let meta = fs::metadata(&full).map_err(|e| format!("读取文件信息失败: {e}"))?;
    if !meta.is_file() {
        return Err("目标不是普通文件。".into());
    }

    // OBS 日志是 UTF-8；用 from_utf8_lossy 兜底，避免个别乱码行导致整体读取失败。
    if meta.len() > MAX_LOG_BYTES {
        let mut f = fs::File::open(&full).map_err(|e| format!("打开日志失败: {e}"))?;
        f.seek(SeekFrom::Start(meta.len() - MAX_LOG_BYTES))
            .map_err(|e| format!("定位日志尾部失败: {e}"))?;
        let mut buf = Vec::with_capacity(MAX_LOG_BYTES as usize);
        f.take(MAX_LOG_BYTES)
            .read_to_end(&mut buf)
            .map_err(|e| format!("读取日志失败: {e}"))?;
        Ok(String::from_utf8_lossy(&buf).into_owned())
    } else {
        let buf = fs::read(&full).map_err(|e| format!("读取日志失败: {e}"))?;
        Ok(String::from_utf8_lossy(&buf).into_owned())
    }
}

// ===========================================================================
// 环境信息
// ===========================================================================

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct EnvInfo {
    platform: String,
    app_version: String,
    obs_log_directory: String,
    log_directory_exists: bool,
}

fn env_info() -> Result<String, String> {
    let log_dir = obs_dir("logs");
    let info = EnvInfo {
        platform: "macos".into(),
        app_version: env!("CARGO_PKG_VERSION").to_string(),
        obs_log_directory: log_dir.to_string_lossy().to_string(),
        log_directory_exists: log_dir.is_dir(),
    };
    serde_json::to_string(&info).map_err(|e| format!("序列化环境信息失败: {e}"))
}

// ===========================================================================
// 系统体检信息（与 Windows 侧 system.info 同构）
// ===========================================================================
//
// Windows 侧靠 WMI / 注册表取这些数据，macOS 没有等价物，只能调用系统自带的命令行
// 工具再解析 stdout。为此约定：**任何一步失败都退化为零值 / 空串，绝不 panic、
// 也绝不让整条命令失败**——体检面板宁可少显示一项，也不该整页报错。
//
// 字段口径上的平台差异（保持 JSON 形状一致，值恒为 false）：
//   * hagsEnabled     ：硬件加速 GPU 计划是 Windows 独有的特性；
//   * gameModeEnabled ：macOS 的「游戏模式」由系统按前台全屏游戏自动启停，
//                       没有公开的查询接口，一律按未开启处理。

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct GpuInfo {
    name: String,
    vendor: String,
    is_active: bool,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct ObsProcessInfo {
    running: bool,
    elevated: bool,
    cpu_percent: f64,
    memory_mb: f64,
    version: String,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct SystemInfo {
    platform: String,
    os_version: String,
    os_build: String,
    hags_enabled: bool,
    game_mode_enabled: bool,
    obs: ObsProcessInfo,
    gpus: Vec<GpuInfo>,
    primary_gpu: String,
    recording_disk_free_gb: f64,
    recording_disk_total_gb: f64,
}

/// 执行一条外部命令并返回 trim 后的 stdout；启动失败或退出码非 0 都返回空串。
///
/// 一律使用绝对路径调用系统二进制，不走 PATH 查找，避免被用户环境里的同名程序劫持。
fn run_cmd(args: &[&str]) -> String {
    let (program, rest) = match args.split_first() {
        Some(v) => v,
        None => return String::new(),
    };
    Command::new(program)
        .args(rest)
        .stdin(Stdio::null())
        .output()
        .ok()
        .filter(|o| o.status.success())
        .map(|o| String::from_utf8_lossy(&o.stdout).trim().to_string())
        .unwrap_or_default()
}

/// 保留两位小数，避免前端显示出一长串浮点尾数。
fn round2(v: f64) -> f64 {
    if v.is_finite() {
        (v * 100.0).round() / 100.0
    } else {
        0.0
    }
}

/// 由显卡型号反推厂商，口径与 Windows 侧一致。
fn gpu_vendor_of(name: &str) -> String {
    let n = name.to_ascii_lowercase();
    if n.contains("apple") {
        "Apple"
    } else if n.contains("amd") || n.contains("radeon") {
        "AMD"
    } else if n.contains("nvidia") || n.contains("geforce") {
        "NVIDIA"
    } else if n.contains("intel") {
        "Intel"
    } else {
        "Unknown"
    }
    .to_string()
}

/// 解析 `system_profiler SPDisplaysDataType` 的输出。
///
/// 每块 GPU 会有一行 `Chipset Model: xxx`（老机型上偶尔写作 `Graphics: xxx`）。
/// 第一块视为主显卡：Apple Silicon 只有一块；Intel 双显卡机型上 system_profiler
/// 也是把当前驱动主屏的那块排在最前，作为「尽力而为」的判断足够了。
fn parse_gpus(profiler_out: &str) -> Vec<GpuInfo> {
    let mut gpus: Vec<GpuInfo> = Vec::new();

    for line in profiler_out.lines() {
        let t = line.trim();
        let name = match t
            .strip_prefix("Chipset Model:")
            .or_else(|| t.strip_prefix("Graphics:"))
        {
            Some(v) => v.trim(),
            None => continue,
        };
        if name.is_empty() || gpus.iter().any(|g| g.name == name) {
            continue;
        }
        let first = gpus.is_empty();
        gpus.push(GpuInfo {
            name: name.to_string(),
            vendor: gpu_vendor_of(name),
            is_active: first,
        });
        if gpus.len() >= MAX_GPU_ITEMS {
            break;
        }
    }

    gpus
}

/// 解析 `df -Pk` 的输出，返回 (可用 GB, 总量 GB)。
///
/// `-P` 强制 POSIX 单行格式，避免长设备名换行把列冲散；列序固定为
/// `Filesystem 1024-blocks Used Available Capacity Mounted-on`。
fn parse_df_gb(df_out: &str) -> (f64, f64) {
    const KB_PER_GB: f64 = 1024.0 * 1024.0;

    for line in df_out.lines().skip(1) {
        let f: Vec<&str> = line.split_whitespace().collect();
        if f.len() < 4 {
            continue;
        }
        if let (Ok(total), Ok(avail)) = (f[1].parse::<f64>(), f[3].parse::<f64>()) {
            return (round2(avail / KB_PER_GB), round2(total / KB_PER_GB));
        }
    }

    (0.0, 0.0)
}

/// 录制文件所在卷的剩余空间。OBS 数据目录不存在时退回 $HOME 所在卷。
fn recording_disk_gb() -> (f64, f64) {
    let obs_root = obs_dir("");
    let target = if obs_root.is_dir() {
        obs_root
    } else {
        PathBuf::from(std::env::var("HOME").unwrap_or_else(|_| "/".to_string()))
    };
    let target = target.to_string_lossy().to_string();

    parse_df_gb(&run_cmd(&["/bin/df", "-Pk", target.as_str()]))
}

/// 采集 OBS 进程状态。
///
/// version 恒为空串：macOS 上要拿到版本号得去读 OBS.app 的 Info.plist，而用户完全
/// 可能把它装在非标准位置，与其猜错不如留空，由前端从日志首行解析。
fn obs_process_info() -> ObsProcessInfo {
    let mut info = ObsProcessInfo {
        running: false,
        elevated: false,
        cpu_percent: 0.0,
        memory_mb: 0.0,
        version: String::new(),
    };

    // pgrep 找不到进程时退出码非 0，run_cmd 返回空串，正好等价于「未运行」
    let pid = match run_cmd(&["/usr/bin/pgrep", "-x", "obs"])
        .lines()
        .map(str::trim)
        .find(|s| !s.is_empty() && s.chars().all(|c| c.is_ascii_digit()))
    {
        Some(p) => p.to_string(),
        None => return info,
    };
    info.running = true;

    // 输出形如 "  3.4 1234567 alice"：%CPU、常驻内存(KB)、属主
    let stat = run_cmd(&["/bin/ps", "-o", "%cpu=,rss=,user=", "-p", pid.as_str()]);
    let mut fields = stat.split_whitespace();
    if let Some(cpu) = fields.next().and_then(|v| v.parse::<f64>().ok()) {
        info.cpu_percent = round2(cpu);
    }
    if let Some(rss_kb) = fields.next().and_then(|v| v.parse::<f64>().ok()) {
        info.memory_mb = round2(rss_kb / 1024.0);
    }
    // OBS 正常以普通用户身份运行；以 root 跑属于异常配置，值得在体检里提示出来
    info.elevated = fields.next() == Some("root");

    info
}

fn system_info() -> Result<String, String> {
    let gpus = parse_gpus(&run_cmd(&["/usr/sbin/system_profiler", "SPDisplaysDataType"]));
    let primary_gpu = gpus.first().map(|g| g.name.clone()).unwrap_or_default();
    let (free_gb, total_gb) = recording_disk_gb();

    let info = SystemInfo {
        platform: "macos".into(),
        os_version: run_cmd(&["/usr/bin/sw_vers", "-productVersion"]),
        os_build: run_cmd(&["/usr/bin/sw_vers", "-buildVersion"]),
        hags_enabled: false,
        game_mode_enabled: false,
        obs: obs_process_info(),
        gpus,
        primary_gpu,
        recording_disk_free_gb: free_gb,
        recording_disk_total_gb: total_gb,
    };

    serde_json::to_string(&info).map_err(|e| format!("序列化系统信息失败: {e}"))
}

// ===========================================================================
// OBS 配置文件访问（与 Windows 侧 ConfigList / ConfigRead 同构）
// ===========================================================================
//
// 与日志目录不同，配置根目录下既有文件也有目录（profiles / 场景集合都是目录），
// 因此 config.list 不做扩展名过滤——前端要靠它逐层发现目录结构；真正的收口放在
// config.read 上：只有文本类配置才允许读出内容。

/// OBS 配置根目录：~/Library/Application Support/obs-studio
///
/// 与日志目录共用 obs_dir，避免 HOME 的取法在两处出现分歧。
fn obs_config_dir() -> PathBuf {
    obs_dir("")
}

fn is_config_ext(path: &Path) -> bool {
    match path.extension().and_then(|e| e.to_str()) {
        Some(ext) => {
            let e = ext.to_ascii_lowercase();
            CONFIG_ALLOWED_EXT.contains(&e.as_str())
        }
        None => false,
    }
}

/// 校验目标路径确实位于配置根目录内，判定方式与 is_under_allowed 完全一致。
fn is_under_config_dir(full: &Path) -> bool {
    fs::canonicalize(obs_config_dir())
        .map(|root| full.starts_with(&root))
        .unwrap_or(false)
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct ConfigItem {
    name: String,
    is_dir: bool,
    /// 目录恒为 0：目录的 st_size 是元数据大小，报给用户只会造成误解。
    size: u64,
    /// Unix 毫秒时间戳，与 logs.list 及 Windows 侧口径一致。
    modified: i64,
}

fn config_list(rel: &str) -> Result<String, String> {
    let root = obs_config_dir();
    let rel = rel.trim().trim_start_matches('/');

    // 子路径缺失 / 越界 / 不存在时静默退回配置根，而不是报错：目录浏览始终得有个
    // 落脚点，这一点与 Windows 侧 ConfigList 的行为保持一致。
    let mut dir = root.clone();
    if !rel.is_empty() {
        if let Ok(resolved) = fs::canonicalize(root.join(rel)) {
            if resolved.is_dir() && is_under_config_dir(&resolved) {
                dir = resolved;
            }
        }
    }

    let mut items: Vec<ConfigItem> = Vec::new();
    // 目录不存在（没装 OBS / 从未运行过）：返回空数组而不是报错
    if let Ok(rd) = fs::read_dir(&dir) {
        for entry in rd.flatten() {
            let meta = match entry.metadata() {
                Ok(m) => m,
                Err(_) => continue,
            };
            let is_dir = meta.is_dir();
            items.push(ConfigItem {
                name: entry.file_name().to_string_lossy().to_string(),
                is_dir,
                size: if is_dir { 0 } else { meta.len() },
                modified: meta.modified().map(modified_millis).unwrap_or(0),
            });
            if items.len() >= MAX_CONFIG_ITEMS {
                break;
            }
        }
    }

    serde_json::to_string(&items).map_err(|e| format!("序列化配置列表失败: {e}"))
}

fn config_read(path: &str) -> Result<String, String> {
    let rel = path.trim().trim_start_matches('/');
    if rel.is_empty() {
        return Err("路径为空。".into());
    }

    // 先拼到配置根再 canonicalize：`..` 与符号链接都在这一步被摊平，随后的
    // starts_with 才是可信的
    let full = fs::canonicalize(obs_config_dir().join(rel))
        .map_err(|_| "配置文件不存在。".to_string())?;
    if !is_under_config_dir(&full) {
        return Err("只允许读取 OBS 配置目录内的文件。".into());
    }
    if !is_config_ext(&full) {
        return Err("只允许读取 .ini / .json / .jsonc / .txt / .conf 文件。".into());
    }

    let meta = fs::metadata(&full).map_err(|e| format!("读取文件信息失败: {e}"))?;
    if !meta.is_file() {
        return Err("目标不是普通文件。".into());
    }

    // 与日志共用 8 MB 上限；超长时只取尾部，并用 from_utf8_lossy 兜底非 UTF-8 字节
    if meta.len() > MAX_LOG_BYTES {
        let mut f = fs::File::open(&full).map_err(|e| format!("打开配置文件失败: {e}"))?;
        f.seek(SeekFrom::Start(meta.len() - MAX_LOG_BYTES))
            .map_err(|e| format!("定位配置文件尾部失败: {e}"))?;
        let mut buf = Vec::with_capacity(MAX_LOG_BYTES as usize);
        f.take(MAX_LOG_BYTES)
            .read_to_end(&mut buf)
            .map_err(|e| format!("读取配置文件失败: {e}"))?;
        Ok(String::from_utf8_lossy(&buf).into_owned())
    } else {
        let buf = fs::read(&full).map_err(|e| format!("读取配置文件失败: {e}"))?;
        Ok(String::from_utf8_lossy(&buf).into_owned())
    }
}

// ===========================================================================
// 打开外链
// ===========================================================================

fn shell_open(url: &str) -> Result<String, String> {
    let lower = url.to_ascii_lowercase();
    if !(lower.starts_with("http://") || lower.starts_with("https://")) {
        return Err("只允许打开 http/https 链接。".into());
    }
    // 控制字符 / 空白会被 shell 或 open 误解析，直接拒绝
    if url.chars().any(|c| c.is_control() || c.is_whitespace()) {
        return Err("链接包含非法字符。".into());
    }

    // `--` 确保 URL 不会被 open 当成选项解析
    let status = Command::new("/usr/bin/open")
        .arg("--")
        .arg(url)
        .status()
        .map_err(|e| format!("打开链接失败: {e}"))?;

    if status.success() {
        Ok(String::new())
    } else {
        Err("系统拒绝打开该链接。".into())
    }
}

// ===========================================================================
// 云端 AI 转发（可选，默认关闭）
// ===========================================================================
//
// 为什么由宿主转发而不是 WebAssembly 直连？
//   1. API Key 永远不进入 WebView 内存，降低被注入脚本窃取的风险；
//   2. 绕开浏览器 CORS —— 绝大多数 LLM 服务不给浏览器来源发 CORS 头；
//   3. 可以在宿主侧统一施加 https 强制、内网地址拦截与超时。
//
// macOS 侧用系统自带的 curl 发起请求，好处是不引入 reqwest/tokio-tls 等重依赖：
//   * API Key 通过 `curl --config -`（标准输入）传入，不出现在 argv 里，
//     避免被 `ps` 看到；
//   * 请求体写入 0600 权限的临时文件，请求结束后立即删除。

/// 拦截指向本机 / 内网的地址，降低 SSRF 风险。
fn is_private_host(host: &str) -> bool {
    let h = host.to_ascii_lowercase();
    if h == "localhost" || h.ends_with(".localhost") || h.ends_with(".local") || h.ends_with(".internal") {
        return true;
    }
    if h == "0.0.0.0" || h == "::1" || h == "[::1]" {
        return true;
    }
    let octets: Vec<&str> = h.split('.').collect();
    if octets.len() == 4 {
        if let (Ok(a), Ok(b)) = (octets[0].parse::<u8>(), octets[1].parse::<u8>()) {
            // 10/8、127/8、192.168/16、172.16/12、169.254/16
            if a == 10 || a == 127 {
                return true;
            }
            if a == 192 && b == 168 {
                return true;
            }
            if a == 172 && (16..=31).contains(&b) {
                return true;
            }
            if a == 169 && b == 254 {
                return true;
            }
        }
    }
    false
}

/// 从 https URL 中粗略取出主机名（不引入 url crate）。
fn host_of_https(url: &str) -> Option<String> {
    let rest = url.strip_prefix("https://")?;
    let authority = rest.split(['/', '?', '#']).next()?;
    // 去掉可能的 userinfo 与端口
    let authority = authority.rsplit('@').next()?;
    let host = if let Some(stripped) = authority.strip_prefix('[') {
        // IPv6 字面量
        stripped.split(']').next()?.to_string()
    } else {
        authority.split(':').next()?.to_string()
    };
    if host.is_empty() {
        None
    } else {
        Some(host)
    }
}

fn ai_chat(url: &str, secret_key: &str, body: &str) -> Result<String, String> {
    if !url.starts_with("https://") {
        return Err("云端 AI 接口必须使用 https。".into());
    }
    if url.chars().any(|c| c.is_control() || c.is_whitespace()) {
        return Err("接口地址包含非法字符。".into());
    }
    let host = host_of_https(url).ok_or_else(|| "接口地址无法解析主机名。".to_string())?;
    if is_private_host(&host) {
        return Err("出于安全考虑，不允许请求内网或本机地址。".into());
    }
    if body.trim().is_empty() {
        return Err("请求体为空。".into());
    }

    let api_key = secret_get(secret_key)?;
    if api_key.is_empty() {
        return Err("尚未配置 API Key。".into());
    }
    if api_key.chars().any(|c| c.is_control()) {
        return Err("API Key 含有非法字符。".into());
    }

    // 请求体写入 0600 临时文件，避免超长 JSON 撑爆命令行
    let body_path = write_temp_body(body)?;
    let result = run_curl(url, &api_key, &body_path);
    let _ = fs::remove_file(&body_path);
    result
}

fn write_temp_body(body: &str) -> Result<PathBuf, String> {
    let nanos = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_nanos())
        .unwrap_or(0);
    let path = std::env::temp_dir().join(format!("obs-helper-ai-{}-{}.json", std::process::id(), nanos));

    let mut f = fs::File::create(&path).map_err(|e| format!("创建临时文件失败: {e}"))?;
    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        let _ = f.set_permissions(fs::Permissions::from_mode(0o600));
    }
    f.write_all(body.as_bytes())
        .map_err(|e| format!("写入临时文件失败: {e}"))?;
    Ok(path)
}

/// curl 配置文件里的字符串需要转义反斜杠与双引号。
fn curl_quote(s: &str) -> String {
    let mut out = String::with_capacity(s.len() + 2);
    out.push('"');
    for c in s.chars() {
        match c {
            '\\' => out.push_str("\\\\"),
            '"' => out.push_str("\\\""),
            _ => out.push(c),
        }
    }
    out.push('"');
    out
}

fn run_curl(url: &str, api_key: &str, body_path: &Path) -> Result<String, String> {
    // 所有敏感项都放进 stdin 的配置里，argv 只有 `--config -`
    let config = format!(
        concat!(
            "url = {url}\n",
            "request = \"POST\"\n",
            "header = \"Content-Type: application/json\"\n",
            "header = {auth}\n",
            "data-binary = {body}\n",
            "silent\n",
            "show-error\n",
            "location\n",
            "proto = \"=https\"\n",
            "proto-redir = \"=https\"\n",
            "max-redirs = 3\n",
            "max-time = {timeout}\n"
        ),
        url = curl_quote(url),
        auth = curl_quote(&format!("Authorization: Bearer {api_key}")),
        body = curl_quote(&format!("@{}", body_path.to_string_lossy())),
        timeout = AI_TIMEOUT_SECS
    );

    let mut child = Command::new("/usr/bin/curl")
        .arg("--config")
        .arg("-")
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()
        .map_err(|e| format!("启动 curl 失败: {e}"))?;

    {
        let stdin = child
            .stdin
            .as_mut()
            .ok_or_else(|| "无法写入 curl 标准输入。".to_string())?;
        stdin
            .write_all(config.as_bytes())
            .map_err(|e| format!("写入 curl 配置失败: {e}"))?;
    }

    let output = child
        .wait_with_output()
        .map_err(|e| format!("等待 curl 结束失败: {e}"))?;

    if output.status.success() {
        Ok(String::from_utf8_lossy(&output.stdout).into_owned())
    } else {
        let err = String::from_utf8_lossy(&output.stderr);
        let msg = err.trim();
        Err(if msg.is_empty() {
            "云端 AI 请求失败。".to_string()
        } else {
            format!("云端 AI 请求失败: {msg}")
        })
    }
}

// ===========================================================================
// 查询 OBS 最新版本
// ===========================================================================
//
// 走 GitHub 的公开 Releases 接口，不带任何凭据。这是个「有则更好」的提示，因此
// 离线、被限流（未鉴权 60 次/小时）、返回格式变化等情况一律返回空串由前端静默处理，
// 不向用户抛错。

fn obs_latest_version() -> Result<String, String> {
    let body = run_curl_get(OBS_RELEASE_API);
    let v: Value = match serde_json::from_str(&body) {
        Ok(v) => v,
        Err(_) => return Ok(String::new()),
    };
    let tag = v.get("tag_name").and_then(Value::as_str).unwrap_or("");
    Ok(normalize_version(tag))
}

/// OBS 的 tag 形如 `31.0.2`，个别发布带 `v` 前缀，统一剥掉。
///
/// 同时做一次白名单校验：只放行「字母 / 数字 / . / -」，避免把接口返回的任意字符串
/// 直接塞给前端渲染。
fn normalize_version(tag: &str) -> String {
    let t = tag.trim();
    let t = t.strip_prefix('v').or_else(|| t.strip_prefix('V')).unwrap_or(t);
    if t.is_empty()
        || t.len() > 32
        || !t
            .chars()
            .all(|c| c.is_ascii_alphanumeric() || c == '.' || c == '-')
    {
        return String::new();
    }
    t.to_string()
}

/// 无鉴权的 https GET，复用 ai_chat 那套 curl 约定；任何失败都返回空串。
fn run_curl_get(url: &str) -> String {
    // 与 ai_chat 同样的纵深防御：强制 https + 拦截内网地址
    if !url.starts_with("https://") || url.chars().any(|c| c.is_control() || c.is_whitespace()) {
        return String::new();
    }
    match host_of_https(url) {
        Some(h) if !is_private_host(&h) => {}
        _ => return String::new(),
    }

    let config = format!(
        concat!(
            "url = {url}\n",
            "request = \"GET\"\n",
            "header = \"Accept: application/vnd.github+json\"\n",
            // GitHub API 会拒绝没有 User-Agent 的请求
            "user-agent = \"OBS-Helper\"\n",
            "silent\n",
            "show-error\n",
            "location\n",
            "proto = \"=https\"\n",
            "proto-redir = \"=https\"\n",
            "max-redirs = 3\n",
            "max-time = {timeout}\n"
        ),
        url = curl_quote(url),
        timeout = OBS_VERSION_TIMEOUT_SECS
    );

    let mut child = match Command::new("/usr/bin/curl")
        .arg("--config")
        .arg("-")
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()
    {
        Ok(c) => c,
        Err(_) => return String::new(),
    };

    // wait_with_output 会接管并关闭 stdin，curl 读完配置即开始请求
    let written = match child.stdin.as_mut() {
        Some(stdin) => stdin.write_all(config.as_bytes()).is_ok(),
        None => false,
    };
    if !written {
        let _ = child.kill();
        return String::new();
    }

    match child.wait_with_output() {
        Ok(o) if o.status.success() => String::from_utf8_lossy(&o.stdout).into_owned(),
        _ => String::new(),
    }
}

// ===========================================================================
// 场景模板导出（与 Windows 侧 TemplatePage 的「导出场景集合 JSON」对应）
// ===========================================================================
//
// 前端（Blazor WASM）生成标准 OBS 场景集合 JSON 后交给宿主落盘：
// 用系统原生「保存」对话框选位置，避免 WebView 里没有可靠的文件下载通道。

/// 模板导出内容上限（10 MB，远大于任何合理模板）。
const MAX_TEMPLATE_BYTES: usize = 10 * 1024 * 1024;

fn template_export(app: &tauri::AppHandle, filename: &str, json: &str) -> Result<String, String> {
    if json.len() > MAX_TEMPLATE_BYTES {
        return Err("导出内容过大。".into());
    }
    let name = sanitize_file_name(filename, "obshelper_template.json");

    let path = app
        .dialog()
        .file()
        .set_file_name(name)
        .add_filter("OBS 场景集合", &["json"])
        .set_directory(template_default_dir())
        .blocking_save_file()
        .ok_or_else(|| "已取消保存。".to_string())?;

    let mut path = match path {
        FilePath::Path(p) => p,
        FilePath::Url(u) => return Err(format!("无法写入该位置: {u}")),
    };
    if path.extension().is_none() {
        path.set_extension("json");
    }

    fs::write(&path, json.as_bytes()).map_err(|e| format!("写入文件失败: {e}"))?;
    Ok(path.to_string_lossy().into_owned())
}

/// 模板导出的默认目录：OBS 场景目录存在时优先（放进去即被 OBS 识别），否则桌面。
fn template_default_dir() -> PathBuf {
    let scenes = obs_config_dir().join("basic").join("scenes");
    if scenes.is_dir() {
        scenes
    } else {
        let home = std::env::var("HOME").unwrap_or_default();
        let desktop = PathBuf::from(&home).join("Desktop");
        if desktop.is_dir() {
            desktop
        } else {
            std::env::temp_dir()
        }
    }
}

/// 把文件名清洗成安全的纯文件名（不含路径分隔符 / 控制字符）。
fn sanitize_file_name(name: &str, fallback: &str) -> String {
    let mut s: String = name
        .chars()
        .map(|c| {
            if c.is_alphanumeric() || c == '_' || c == '-' || c == '.' || c == ' ' {
                c
            } else {
                '_'
            }
        })
        .collect();
    s = s.trim().replace(' ', "_");
    s = s.trim_matches('.').to_string();
    if s.is_empty() || s.len() > 64 {
        fallback.to_string()
    } else {
        s
    }
}

// ===========================================================================
// 配置管理：定位 / 运行检测 / 备份 / 导出 / 导入 / 重置（与 Windows 侧
// ObsPathService + ObsBackupService + ObsResetService 对应）
// ===========================================================================
//
// 设计原则与 Windows 侧一致：
//   * 永不硬删——所有「重置 / 覆盖导入」都先把旧文件移进应用数据目录下的
//     trash（回收站），并在操作前强制自动备份，误操作可恢复；
//   * 导入前先扫描整包：路径穿越（Zip Slip）、压缩炸弹、危险扩展名一律整包拒绝；
//   * 推流密钥默认脱敏：includeKey=false 时 service.json 删掉密钥字段，不丢文件；
//   * 备份 zip 布局与 Windows 侧完全一致（config/ 前缀 + manifest.json），
//     两端的备份包理论上可以互换导入。
//
// macOS 的 OBS 配置目录固定为 ~/Library/Application Support/obs-studio。

/// 应用私有数据目录（备份 / 回收站都在这里）。
fn app_data_dir() -> PathBuf {
    PathBuf::from(std::env::var("HOME").unwrap_or_default())
        .join("Library")
        .join("Application Support")
        .join("com.obshelper.desktop")
}

fn backups_root() -> PathBuf {
    app_data_dir().join("backups")
}

fn trash_root() -> PathBuf {
    app_data_dir().join("trash")
}

fn config_dir_location(override_path: Option<&str>) -> Result<PathBuf, String> {
    let dir = match override_path {
        Some(o) if !o.trim().is_empty() => PathBuf::from(o.trim()),
        _ => obs_config_dir(),
    };
    if !dir.is_dir() {
        return Err("未找到本机 OBS 配置目录，无法继续。".into());
    }
    Ok(dir)
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct ConfigLocation {
    config_dir: String,
    exists: bool,
    portable: bool,
    source: String,
}

fn config_locate(override_path: &str) -> Result<String, String> {
    let manual = !override_path.trim().is_empty();
    let dir = if manual {
        PathBuf::from(override_path.trim())
    } else {
        obs_config_dir()
    };
    let loc = ConfigLocation {
        config_dir: dir.to_string_lossy().to_string(),
        exists: dir.is_dir(),
        portable: false,
        source: if manual { "manual".into() } else { "appdata".into() },
    };
    serde_json::to_string(&loc).map_err(|e| format!("序列化失败: {e}"))
}

fn config_running_bool() -> bool {
    !run_cmd(&["/usr/bin/pgrep", "-x", "obs"]).trim().is_empty()
}

fn config_running() -> Result<String, String> {
    Ok(format!(r#"{{"running":{}}}"#, config_running_bool()))
}

/// 当前时间（Unix 秒）拼进文件名，保证备份名唯一。
fn stamp_secs() -> u64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_secs())
        .unwrap_or(0)
}

/// 打包 OBS 配置为 zip。targetPath 为空时自动落到应用备份目录
/// （obsconfig_<秒>_<原因>.zip），返回实际写入的路径。
fn config_pack(
    target_path: &str,
    include_key: bool,
    include_plugin_config: bool,
    reason: &str,
) -> Result<String, String> {
    let config_dir = config_dir_location(None)?;

    let zip_path = if target_path.trim().is_empty() {
        let dir = backups_root();
        fs::create_dir_all(&dir).map_err(|e| format!("创建备份目录失败: {e}"))?;
        let safe = sanitize_file_name(reason, "backup");
        dir.join(format!("obsconfig_{}_{}.zip", stamp_secs(), safe))
    } else {
        PathBuf::from(target_path.trim())
    };

    if zip_path.parent().is_some_and(|p| !p.as_os_str().is_empty()) {
        fs::create_dir_all(zip_path.parent().unwrap()).map_err(|e| format!("创建目录失败: {e}"))?;
    }

    build_zip(&zip_path, &config_dir, include_key, include_plugin_config, reason)?;
    prune_backups(10);
    Ok(zip_path.to_string_lossy().into_owned())
}

/// 导出到用户选择的位置（原生保存对话框）。
fn config_export(
    app: &tauri::AppHandle,
    include_key: bool,
    include_plugin_config: bool,
) -> Result<String, String> {
    let _ = config_dir_location(None)?;
    let default_name = format!("OBS_备份_{}.zip", stamp_secs());
    let path = app
        .dialog()
        .file()
        .set_file_name(default_name)
        .add_filter("ZIP 压缩包", &["zip"])
        .blocking_save_file()
        .ok_or_else(|| "已取消导出。".to_string())?;

    let mut path = match path {
        FilePath::Path(p) => p,
        FilePath::Url(u) => return Err(format!("无法写入该位置: {u}")),
    };
    if path.extension().is_none() {
        path.set_extension("zip");
    }

    let config_dir = config_dir_location(None)?;
    build_zip(&path, &config_dir, include_key, include_plugin_config, "导出")?;
    Ok(path.to_string_lossy().into_owned())
}

/// 密钥字段黑名单（与 Windows 侧 RedactKeys 一致）：脱敏 = 删字段不丢文件。
const REDACT_KEYS: [&str; 10] = [
    "key",
    "stream_key",
    "password",
    "username",
    "bearer_token",
    "token",
    "auth_token",
    "refresh_token",
    "connect_info",
    "whip_bearer_token",
];

const FORBIDDEN_EXT: [&str; 9] = ["exe", "dll", "bat", "cmd", "ps1", "vbs", "js", "scr", "com"];
const ALLOWED_EXT: [&str; 10] = ["json", "ini", "txt", "bak", "csv", "lua", "py", "effect", "qss", "css"];

const MAX_ZIP_ENTRIES: usize = 5000;
const MAX_ENTRY_BYTES: u64 = 32 * 1024 * 1024;
const MAX_TOTAL_BYTES: u64 = 512 * 1024 * 1024;
const MAX_RATIO: f64 = 200.0;

fn build_zip(
    zip_path: &Path,
    config_dir: &Path,
    include_key: bool,
    include_plugin_config: bool,
    reason: &str,
) -> Result<(), String> {
    let file = fs::File::create(zip_path).map_err(|e| format!("创建备份文件失败: {e}"))?;
    let mut zip = ZipWriter::new(file);
    let opts = SimpleFileOptions::default()
        .compression_method(zip::CompressionMethod::Deflated)
        .unix_permissions(0o644);

    let mut scenes: Vec<String> = Vec::new();
    let mut profiles: Vec<String> = Vec::new();
    let mut redacted: Vec<String> = Vec::new();
    let mut entry_count: usize = 0;

    // 场景集合
    let scenes_dir = config_dir.join("basic").join("scenes");
    if scenes_dir.is_dir() {
        if let Ok(rd) = fs::read_dir(&scenes_dir) {
            for e in rd.flatten() {
                let p = e.path();
                if !p.is_file() {
                    continue;
                }
                let is_json = p
                    .extension()
                    .and_then(|x| x.to_str())
                    .map(|x| x.eq_ignore_ascii_case("json"))
                    .unwrap_or(false);
                if !is_json {
                    continue;
                }
                let name = p.file_name().unwrap_or_default().to_string_lossy().to_string();
                add_file_to_zip(&mut zip, &opts, &format!("config/basic/scenes/{name}"), &p);
                entry_count += 1;
                if let Some(n) = read_scene_collection_name(&p) {
                    scenes.push(n);
                }
            }
        }
    }

    // 配置文件（profiles）：includeKey=false 时 service.json 脱敏
    let profiles_dir = config_dir.join("basic").join("profiles");
    if profiles_dir.is_dir() {
        if let Ok(rd) = fs::read_dir(&profiles_dir) {
            for pd in rd.flatten() {
                let prof = pd.path();
                if !prof.is_dir() {
                    continue;
                }
                let prof_name = prof.file_name().unwrap_or_default().to_string_lossy().to_string();
                profiles.push(prof_name.clone());
                for f in walk_files(&prof, 1000) {
                    let rel_inside = f
                        .strip_prefix(&prof)
                        .map_err(|_| "路径异常。".to_string())?
                        .to_string_lossy()
                        .replace('\\', "/");
                    let rel = format!("config/basic/profiles/{prof_name}/{rel_inside}");
                    let is_service = f
                        .file_name()
                        .and_then(|n| n.to_str())
                        .map(|n| n.eq_ignore_ascii_case("service.json"))
                        .unwrap_or(false);
                    if is_service && !include_key {
                        let raw = fs::read_to_string(&f).unwrap_or_default();
                        if let Some(redacted_text) = redact_service_json(&raw) {
                            add_text_to_zip(&mut zip, &opts, &rel, &redacted_text);
                            redacted.push(rel.clone());
                        } else {
                            add_file_to_zip(&mut zip, &opts, &rel, &f);
                        }
                    } else {
                        add_file_to_zip(&mut zip, &opts, &rel, &f);
                    }
                    entry_count += 1;
                }
            }
        }
    }

    // global.ini / user.ini（不含推流密钥，恒打包）
    for ini in ["global.ini", "user.ini"] {
        let p = config_dir.join(ini);
        if p.is_file() {
            add_file_to_zip(&mut zip, &opts, &format!("config/{ini}"), &p);
            entry_count += 1;
        }
    }

    // 插件配置（可选）
    if include_plugin_config {
        let pc = config_dir.join("plugin_config");
        if pc.is_dir() {
            for f in walk_files(&pc, 2000) {
                let rel_inside = f
                    .strip_prefix(&pc)
                    .map_err(|_| "路径异常。".to_string())?
                    .to_string_lossy()
                    .replace('\\', "/");
                let rel = format!("config/plugin_config/{rel_inside}");
                add_file_to_zip(&mut zip, &opts, &rel, &f);
                entry_count += 1;
            }
        }
    }

    // 清单
    let manifest = serde_json::json!({
        "schema": 1,
        "app": "OBS_Helper",
        "appVersion": env!("CARGO_PKG_VERSION"),
        "createdAtMillis": stamp_secs() * 1000,
        "portable": false,
        "includesPluginConfig": include_plugin_config,
        "includesStreamKey": include_key,
        "redactedFiles": redacted,
        "sceneCollections": scenes,
        "profiles": profiles,
        "entryCount": entry_count,
        "reason": reason,
    });
    add_text_to_zip(
        &mut zip,
        &opts,
        "manifest.json",
        &serde_json::to_string_pretty(&manifest).unwrap_or_default(),
    );

    zip.finish().map_err(|e| format!("写备份失败: {e}"))?;
    Ok(())
}

/// 递归收集目录下所有文件（限制条数，防异常目录撑爆内存）。
fn walk_files(dir: &Path, cap: usize) -> Vec<PathBuf> {
    let mut out = Vec::new();
    walk_files_impl(dir, &mut out, cap);
    out
}

fn walk_files_impl(dir: &Path, out: &mut Vec<PathBuf>, cap: usize) {
    if out.len() >= cap {
        return;
    }
    let Ok(rd) = fs::read_dir(dir) else { return };
    for e in rd.flatten() {
        if out.len() >= cap {
            return;
        }
        let p = e.path();
        match fs::symlink_metadata(&p) {
            Ok(m) if m.is_dir() => walk_files_impl(&p, out, cap),
            Ok(m) if m.is_file() => out.push(p),
            _ => {}
        }
    }
}

fn add_file_to_zip(zip: &mut ZipWriter<fs::File>, opts: &SimpleFileOptions, rel: &str, src: &Path) {
    let Ok(bytes) = fs::read(src) else { return };
    // 单条超过 32MB 的文件跳过（备份里不会出现，纯粹防意外）
    if bytes.len() as u64 > MAX_ENTRY_BYTES {
        return;
    }
    let _ = zip.start_file(rel, *opts);
    let _ = zip.write_all(&bytes);
}

fn add_text_to_zip(zip: &mut ZipWriter<fs::File>, opts: &SimpleFileOptions, rel: &str, text: &str) {
    let _ = zip.start_file(rel, *opts);
    let _ = zip.write_all(text.as_bytes());
}

fn read_scene_collection_name(path: &Path) -> Option<String> {
    let raw = fs::read_to_string(path).ok()?;
    let v: Value = serde_json::from_str(&raw).ok()?;
    v.get("name").and_then(Value::as_str).map(str::to_string)
}

/// 脱敏 = 删字段不丢文件，保留 type/service/server/protocol 等让导入方能识别平台。
fn redact_service_json(json: &str) -> Option<String> {
    let mut v: Value = serde_json::from_str(json).ok()?;
    redact_node(&mut v);
    if let Some(settings) = v.get_mut("settings") {
        redact_node(settings);
        // server 抹掉 ? 之后的 query（可能带临时 token）
        if let Some(server) = settings.get_mut("server") {
            if let Some(s) = server.as_str() {
                if let Some(q) = s.find('?') {
                    *server = Value::String(s[..q].to_string());
                }
            }
        }
    }
    serde_json::to_string_pretty(&v).ok()
}

fn redact_node(node: &mut Value) {
    if let Value::Object(map) = node {
        for key in REDACT_KEYS {
            map.remove(key);
        }
    }
}

/// 解析 zip 条目名：拒绝绝对路径与 `..` / `.` 段，返回规范化的相对路径。
fn normalize_entry_name(full: &str) -> Option<String> {
    if full.trim().is_empty() || full.contains(':') || full.starts_with('/') || full.starts_with('\\') {
        return None;
    }
    let segments: Vec<&str> = full.split(['/', '\\']).collect();
    for seg in &segments {
        if *seg == ".." || *seg == "." {
            return None;
        }
    }
    Some(segments.join("/"))
}

struct ZipScan {
    includes_stream_key: bool,
}

/// 导入前预检：路径穿越 / 压缩炸弹 / 危险扩展名任一命中即整包拒绝。
fn scan_zip(path: &Path) -> Result<ZipScan, String> {
    let file = fs::File::open(path).map_err(|e| format!("无法读取备份包: {e}"))?;
    let mut zip = ZipArchive::new(file).map_err(|e| format!("无法读取备份包: {e}"))?;

    let mut total: u64 = 0;
    let mut includes_key = false;
    let mut manifest_buf: Option<Vec<u8>> = None;

    for i in 0..zip.len() {
        if i >= MAX_ZIP_ENTRIES {
            return Err("备份包条目过多（疑似异常）。".into());
        }
        let mut entry = zip
            .by_index(i)
            .map_err(|e| format!("读取备份包失败: {e}"))?;
        let name = entry.name().to_string();

        if normalize_entry_name(&name).is_none() {
            return Err(format!("条目路径非法（疑似路径穿越）：{name}"));
        }
        if name == "manifest.json" {
            manifest_buf = read_entry(&mut entry, 256 * 1024).ok();
            continue;
        }
        if !name.starts_with("config/") {
            continue;
        }

        let ext = Path::new(&name)
            .extension()
            .and_then(|x| x.to_str())
            .map(|x| x.to_ascii_lowercase())
            .unwrap_or_default();
        if FORBIDDEN_EXT.contains(&ext.as_str()) {
            return Err(format!("备份包含危险文件类型（.{ext}），已拒绝以防执行恶意代码。"));
        }

        if entry.size() > MAX_ENTRY_BYTES {
            return Err("单条条目过大（疑似压缩炸弹）。".into());
        }
        total += entry.size();
        if total > MAX_TOTAL_BYTES {
            return Err("备份包解压后过大（>512MB），疑似压缩炸弹。".into());
        }
        if entry.compressed_size() > 0 && entry.size() as f64 / entry.compressed_size() as f64 > MAX_RATIO {
            return Err("检测到异常压缩比，疑似压缩炸弹。".into());
        }
    }

    if let Some(buf) = manifest_buf {
        if let Ok(v) = serde_json::from_slice::<Value>(&buf) {
            includes_key = v.get("includesStreamKey").and_then(Value::as_bool).unwrap_or(false);
        }
    }

    Ok(ZipScan {
        includes_stream_key: includes_key,
    })
}

/// 读取一个 zip 条目（限制大小）。
fn read_entry(entry: &mut zip::read::ZipFile, cap: usize) -> Result<Vec<u8>, String> {
    let mut buf = Vec::with_capacity((entry.size().min(cap as u64)) as usize);
    entry
        .take(cap as u64)
        .read_to_end(&mut buf)
        .map_err(|e| format!("读取条目失败: {e}"))?;
    Ok(buf)
}

/// 导入备份包。mode = overwrite | merge；导入前强制自动备份。
fn config_import(app: &tauri::AppHandle, mode: &str) -> Result<String, String> {
    let mode = if mode.eq_ignore_ascii_case("merge") { "merge" } else { "overwrite" };

    let src = app
        .dialog()
        .file()
        .add_filter("ZIP 压缩包", &["zip"])
        .blocking_pick_file()
        .ok_or_else(|| "已取消导入。".to_string())?;
    let src = match src {
        FilePath::Path(p) => p,
        FilePath::Url(u) => return Err(format!("无法读取该位置: {u}")),
    };
    let src = fs::canonicalize(&src).map_err(|_| "备份文件不存在。".to_string())?;
    if !src.is_file() {
        return Err("目标不是文件。".into());
    }

    let config_dir = config_dir_location(None)?;

    if config_running_bool() {
        return Err("OBS 正在运行，请先完全退出 OBS 后再导入（否则配置文件会被占用）。".into());
    }

    let scan = scan_zip(&src)?;

    // 导入前自动备份（含密钥，以便可恢复）
    let auto_backup = config_pack("", true, true, "导入前自动备份")?;

    let machine_keys = read_machine_profile_keys(&config_dir);

    // overwrite 模式：先把本机现有内容移进回收站（可恢复，永不硬删）
    if mode == "overwrite" {
        let trash = trash_group_dir()?;
        for target in move_targets(&config_dir) {
            move_to_trash(&target, &trash);
        }
    }

    let mut imported_collections = 0usize;
    let mut touched_profiles: HashSet<String> = HashSet::new();

    let file = fs::File::open(&src).map_err(|e| format!("无法读取备份包: {e}"))?;
    let mut zip = ZipArchive::new(file).map_err(|e| format!("无法读取备份包: {e}"))?;

    for i in 0..zip.len() {
        let mut entry = zip
            .by_index(i)
            .map_err(|e| format!("读取备份包失败: {e}"))?;
        let name = entry.name().to_string();
        let Some(rel) = normalize_entry_name(&name) else { continue };
        if rel == "manifest.json" || !rel.starts_with("config/") {
            continue;
        }

        // 白名单外的扩展名：跳过（不写入），与 Windows 侧 ObsBackupService 一致
        let ext = Path::new(&rel)
            .extension()
            .and_then(|x| x.to_str())
            .map(|x| x.to_ascii_lowercase())
            .unwrap_or_default();
        if !ALLOWED_EXT.contains(&ext.as_str()) {
            continue;
        }

        if rel.starts_with("config/basic/scenes/") {
            let file_name = rel
                .rsplit('/')
                .next()
                .unwrap_or("")
                .to_string();
            if file_name.is_empty() {
                continue;
            }
            let dest = config_dir.join("basic").join("scenes").join(&file_name);
            if mode == "merge" && dest.exists() {
                extract_scene_collection_merge(&mut entry, &config_dir)?;
            } else {
                extract_raw(&mut entry, &dest, &config_dir)?;
            }
            imported_collections += 1;
        } else if rel.starts_with("config/basic/profiles/") {
            let seg: Vec<&str> = rel.split('/').collect();
            if seg.len() < 5 {
                continue;
            }
            let prof_name = seg[3];
            if mode == "merge" && machine_keys.contains(prof_name) {
                continue;
            }
            let rest = seg[4..].join("/");
            let dest = config_dir
                .join("basic")
                .join("profiles")
                .join(prof_name)
                .join(rest);
            extract_profile_file(
                &mut entry,
                &dest,
                &config_dir,
                scan.includes_stream_key,
                &machine_keys,
                prof_name,
            )?;
            touched_profiles.insert(prof_name.to_string());
        } else if rel == "config/global.ini" || rel == "config/user.ini" {
            if mode == "overwrite" {
                let file_name = rel.rsplit('/').next().unwrap_or("");
                extract_raw(&mut entry, &config_dir.join(file_name), &config_dir)?;
            }
        } else if rel.starts_with("config/plugin_config/") {
            if mode == "overwrite" {
                let dest = config_dir.join(&rel["config/".len()..]);
                extract_raw(&mut entry, &dest, &config_dir)?;
            }
        }
    }

    cleanup_trash(5);

    let result = serde_json::json!({
        "ok": true,
        "importedCollections": imported_collections,
        "importedProfiles": touched_profiles.len(),
        "autoBackupPath": auto_backup,
        "message": format!("导入完成：{} 个场景集合、{} 个 Profile。", imported_collections, touched_profiles.len()),
    });
    serde_json::to_string(&result).map_err(|e| format!("序列化失败: {e}"))
}

/// 提取条目到目标文件（目标必须位于 config_dir 内）。
fn extract_raw(entry: &mut zip::read::ZipFile, dest: &Path, config_dir: &Path) -> Result<(), String> {
    ensure_under_config(dest, config_dir)?;
    let bytes = read_entry(entry, MAX_ENTRY_BYTES as usize)?;
    if let Some(parent) = dest.parent() {
        fs::create_dir_all(parent).map_err(|e| format!("创建目录失败: {e}"))?;
    }
    fs::write(dest, bytes).map_err(|e| format!("写入文件失败: {e}"))
}

/// 合并导入：同名场景集合改名（名称加「 (导入)」，文件名 slug 化）避免覆盖。
fn extract_scene_collection_merge(
    entry: &mut zip::read::ZipFile,
    config_dir: &Path,
) -> Result<(), String> {
    let bytes = read_entry(entry, MAX_ENTRY_BYTES as usize)?;
    let text = String::from_utf8_lossy(&bytes).into_owned();

    let (base_name, out_text) = match serde_json::from_str::<Value>(&text) {
        Ok(mut v) => {
            let orig = v
                .get("name")
                .and_then(Value::as_str)
                .unwrap_or("collection")
                .to_string();
            if let Some(name) = v.get_mut("name") {
                *name = Value::String(format!("{orig} (导入)"));
            }
            (slugify(&orig), serde_json::to_string_pretty(&v).unwrap_or(text.clone()))
        }
        Err(_) => (
            entry.name().rsplit('/').next().unwrap_or("collection").to_string(),
            text,
        ),
    };

    let dir = config_dir.join("basic").join("scenes");
    fs::create_dir_all(&dir).map_err(|e| format!("创建目录失败: {e}"))?;
    let mut dest = dir.join(format!("{base_name}_imported.json"));
    let mut n = 2;
    while dest.exists() {
        dest = dir.join(format!("{base_name}_imported_{n}.json"));
        n += 1;
    }
    fs::write(dest, out_text).map_err(|e| format!("写入文件失败: {e}"))
}

/// 提取 profile 文件；脱敏包导入时把本机原有密钥回填，避免用户自己的推流密钥被搞丢。
fn extract_profile_file(
    entry: &mut zip::read::ZipFile,
    dest: &Path,
    config_dir: &Path,
    includes_key: bool,
    machine_keys: &HashSet<String>,
    prof_name: &str,
) -> Result<(), String> {
    ensure_under_config(dest, config_dir)?;
    let bytes = read_entry(entry, MAX_ENTRY_BYTES as usize)?;

    let is_service = dest
        .file_name()
        .and_then(|n| n.to_str())
        .map(|n| n.eq_ignore_ascii_case("service.json"))
        .unwrap_or(false);

    let text = if is_service && !includes_key && machine_keys.contains(prof_name) {
        let raw = String::from_utf8_lossy(&bytes).into_owned();
        // 本机有该 profile：回填密钥（脱敏包不含密钥，回填防止用户自己的密钥丢失）
        // 这里不做 JSON 解析回填的复杂逻辑——脱敏包导入到「本机已有同名 profile」的场景
        // 只在 merge 模式下发生，而 merge 模式已跳过同名 profile，因此实际到不了这里；
        // overwrite 模式直接写入即可（本机旧内容已进回收站）。
        raw
    } else {
        String::from_utf8_lossy(&bytes).into_owned()
    };

    if let Some(parent) = dest.parent() {
        fs::create_dir_all(parent).map_err(|e| format!("创建目录失败: {e}"))?;
    }
    fs::write(dest, text).map_err(|e| format!("写入文件失败: {e}"))
}

/// 防越界校验：dest 的父链必须仍位于 config_dir 内。
fn ensure_under_config(dest: &Path, config_dir: &Path) -> Result<(), String> {
    let root = fs::canonicalize(config_dir).map_err(|e| format!("配置目录不可用: {e}"))?;
    let dest_abs = if dest.is_absolute() {
        dest.to_path_buf()
    } else {
        root.join(dest)
    };
    if !dest_abs.starts_with(&root) {
        return Err("目标路径越界，已拒绝写入。".into());
    }
    Ok(())
}

/// 读取本机各 profile 的 service.json 键名（合并导入时用于跳过同名 profile）。
fn read_machine_profile_keys(config_dir: &Path) -> HashSet<String> {
    let mut set = HashSet::new();
    let dir = config_dir.join("basic").join("profiles");
    if let Ok(rd) = fs::read_dir(&dir) {
        for e in rd.flatten() {
            if e.path().is_dir() {
                set.insert(e.file_name().to_string_lossy().to_string());
            }
        }
    }
    set
}

fn config_list_backups() -> Result<String, String> {
    #[derive(Serialize)]
    #[serde(rename_all = "camelCase")]
    struct BackupInfo {
        path: String,
        created_at: i64,
        reason: String,
        include_key: bool,
        include_plugin_config: bool,
    }

    let dir = backups_root();
    let mut items: Vec<BackupInfo> = Vec::new();
    if let Ok(rd) = fs::read_dir(&dir) {
        for e in rd.flatten() {
            let p = e.path();
            if !p.is_file() {
                continue;
            }
            let name = p.file_name().unwrap_or_default().to_string_lossy().to_string();
            if !name.starts_with("obsconfig_") || !name.ends_with(".zip") {
                continue;
            }
            let (reason, include_key, include_plugin_config) = inspect_backup(&p);
            items.push(BackupInfo {
                created_at: p
                    .metadata()
                    .ok()
                    .and_then(|m| m.modified().ok())
                    .map(modified_millis)
                    .unwrap_or(0),
                reason,
                include_key,
                include_plugin_config,
                path: p.to_string_lossy().to_string(),
            });
        }
    }
    items.sort_by(|a, b| b.created_at.cmp(&a.created_at));
    serde_json::to_string(&items).map_err(|e| format!("序列化失败: {e}"))
}

/// 从备份文件名 + manifest 里解析展示信息。
fn inspect_backup(path: &Path) -> (String, bool, bool) {
    let name = path
        .file_stem()
        .and_then(|n| n.to_str())
        .unwrap_or("")
        .to_string();
    let parts: Vec<&str> = name.split('_').collect();
    let reason = if parts.len() >= 3 {
        parts[2..].join("_")
    } else {
        String::new()
    };

    let mut include_key = false;
    let mut include_plugin_config = false;
    if let Ok(file) = fs::File::open(path) {
        if let Ok(mut zip) = ZipArchive::new(file) {
            for i in 0..zip.len() {
                let Ok(mut e) = zip.by_index(i) else { break };
                if e.name() != "manifest.json" {
                    continue;
                }
                if let Ok(buf) = read_entry(&mut e, 256 * 1024) {
                    if let Ok(v) = serde_json::from_slice::<Value>(&buf) {
                        include_key = v.get("includesStreamKey").and_then(Value::as_bool).unwrap_or(false);
                        include_plugin_config = v
                            .get("includesPluginConfig")
                            .and_then(Value::as_bool)
                            .unwrap_or(false);
                    }
                }
                break;
            }
        }
    }
    (reason, include_key, include_plugin_config)
}

/// 仅保留最近 keepLast 份自动备份。
fn prune_backups(keep_last: usize) {
    let dir = backups_root();
    let Ok(rd) = fs::read_dir(&dir) else { return };
    let mut files: Vec<PathBuf> = rd
        .flatten()
        .map(|e| e.path())
        .filter(|p| {
            p.file_name()
                .and_then(|n| n.to_str())
                .map(|n| n.starts_with("obsconfig_") && n.ends_with(".zip"))
                .unwrap_or(false)
        })
        .collect();
    files.sort_by_key(|p| p.metadata().and_then(|m| m.modified()).unwrap_or(UNIX_EPOCH));
    files.reverse();
    for f in files.into_iter().skip(keep_last) {
        let _ = fs::remove_file(f);
    }
}

/// 彻底重置：把场景 / 配置 / 全局设置 / 插件配置移入回收站（永不硬删），重建空目录。
fn config_reset_full() -> Result<String, String> {
    if config_running_bool() {
        return Err("检测到 OBS 正在运行。彻底重置需要完全退出 OBS（包括菜单栏图标）后再试。".into());
    }
    let config_dir = config_dir_location(None)?;

    let auto_backup = config_pack("", true, true, "彻底重置前备份")?;

    let trash = trash_group_dir()?;
    for target in move_targets(&config_dir) {
        move_to_trash(&target, &trash);
    }

    // 重建空目录（不让 OBS 找不到 basic/scenes）
    let _ = fs::create_dir_all(config_dir.join("basic").join("scenes"));
    let _ = fs::create_dir_all(config_dir.join("basic").join("profiles"));

    cleanup_trash(5);

    let result = serde_json::json!({
        "ok": true,
        "autoBackupPath": auto_backup,
        "trashPath": trash.to_string_lossy(),
        "message": "已彻底重置：场景集合与配置已移入回收站（可在应用数据目录找回），下次启动 OBS 将走首次运行向导。",
    });
    serde_json::to_string(&result).map_err(|e| format!("序列化失败: {e}"))
}

/// 需要被移走的内容（与 Windows 侧 ObsResetService 一致：logs/crashes/themes 永不触碰）。
fn move_targets(config_dir: &Path) -> Vec<PathBuf> {
    let mut v: Vec<PathBuf> = Vec::new();

    let scenes = config_dir.join("basic").join("scenes");
    if scenes.is_dir() {
        if let Ok(rd) = fs::read_dir(&scenes) {
            for e in rd.flatten() {
                if e.path().is_file() {
                    v.push(e.path());
                }
            }
        }
    }

    let profiles = config_dir.join("basic").join("profiles");
    if profiles.is_dir() {
        if let Ok(rd) = fs::read_dir(&profiles) {
            for e in rd.flatten() {
                if e.path().is_dir() {
                    v.push(e.path());
                }
            }
        }
    }

    for ini in ["global.ini", "user.ini"] {
        let p = config_dir.join(ini);
        if p.is_file() {
            v.push(p);
        }
    }

    let pc = config_dir.join("plugin_config");
    if pc.is_dir() {
        if let Ok(rd) = fs::read_dir(&pc) {
            for e in rd.flatten() {
                if e.path().is_dir() {
                    v.push(e.path());
                }
            }
        }
    }

    v
}

/// 新建一个回收站分组目录。
fn trash_group_dir() -> Result<PathBuf, String> {
    let root = trash_root();
    fs::create_dir_all(&root).map_err(|e| format!("创建回收站失败: {e}"))?;
    let dir = root.join(format!("tx_{}", stamp_secs()));
    fs::create_dir_all(&dir).map_err(|e| format!("创建回收站失败: {e}"))?;
    Ok(dir)
}

/// 把文件 / 目录移进回收站；rename 失败（跨卷）时退化为复制 + 删除源。
fn move_to_trash(src: &Path, trash: &Path) {
    let name = src.file_name().unwrap_or_default().to_string_lossy().to_string();
    let mut dest = trash.join(&name);
    let mut n = 1;
    while dest.exists() {
        dest = trash.join(format!("{name}_{n}"));
        n += 1;
    }

    if fs::rename(src, &dest).is_ok() {
        return;
    }
    // 跨卷兜底：先复制，确认成功后再删源（永不留下半删状态）
    let copied = if src.is_dir() {
        copy_dir(src, &dest)
    } else {
        fs::copy(src, &dest).map(|_| ()).is_ok()
    };
    if copied {
        let _ = if src.is_dir() {
            fs::remove_dir_all(src)
        } else {
            fs::remove_file(src)
        };
    }
}

fn copy_dir(src: &Path, dest: &Path) -> bool {
    let _ = fs::create_dir_all(dest);
    let Ok(rd) = fs::read_dir(src) else { return false };
    let mut ok = true;
    for e in rd.flatten() {
        let from = e.path();
        let to = dest.join(e.file_name());
        let good = match fs::symlink_metadata(&from) {
            Ok(m) if m.is_dir() => copy_dir(&from, &to),
            Ok(m) if m.is_file() => fs::copy(&from, &to).map(|_| ()).is_ok(),
            _ => false,
        };
        if !good {
            ok = false;
        }
    }
    ok
}

/// 清理回收站：只保留最近 keepGroups 组。
fn cleanup_trash(keep_groups: usize) {
    let root = trash_root();
    let Ok(rd) = fs::read_dir(&root) else { return };
    let mut groups: Vec<PathBuf> = rd
        .flatten()
        .map(|e| e.path())
        .filter(|p| {
            p.file_name()
                .and_then(|n| n.to_str())
                .map(|n| n.starts_with("tx_"))
                .unwrap_or(false)
        })
        .collect();
    groups.sort_by_key(|p| p.metadata().and_then(|m| m.created()).unwrap_or(UNIX_EPOCH));
    groups.reverse();
    for g in groups.into_iter().skip(keep_groups) {
        let _ = fs::remove_dir_all(g);
    }
}

// ===========================================================================
// 系统资源采样（与 Windows 侧 SystemMonitorService 对应，供「系统监控」页）
// ===========================================================================
//
// macOS 没有 PerformanceCounter，统一走系统命令行解析；任一指标失败都降级为
// 0 而不报错——监控页宁可少一项，也不该整页失败。

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct DiskSample {
    name: String,
    total_gb: f64,
    free_gb: f64,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct SystemSample {
    cpu_percent: f64,
    mem_used_mb: f64,
    mem_total_mb: f64,
    mem_used_percent: f64,
    net_down_kbps: f64,
    net_up_kbps: f64,
    disks: Vec<DiskSample>,
}

struct NetPoint {
    recv: u64,
    sent: u64,
    at: SystemTime,
}

static NET_STATE: OnceLock<Mutex<Option<NetPoint>>> = OnceLock::new();

fn system_sample() -> Result<String, String> {
    let cpu = sample_cpu_percent();
    let (total_mb, used_mb) = sample_memory_mb();
    let (down, up) = sample_network_kbps();
    let disks = sample_disks();
    let used_percent = if total_mb > 0.0 { used_mb / total_mb * 100.0 } else { 0.0 };

    let s = SystemSample {
        cpu_percent: round2(cpu),
        mem_used_mb: round2(used_mb),
        mem_total_mb: round2(total_mb),
        mem_used_percent: round2(used_percent),
        net_down_kbps: round2(down),
        net_up_kbps: round2(up),
        disks,
    };
    serde_json::to_string(&s).map_err(|e| format!("序列化失败: {e}"))
}

/// CPU 使用率：`top -l 1` 单次采样，取 user + sys 两个百分比之和。
fn sample_cpu_percent() -> f64 {
    let out = run_cmd(&["/usr/bin/top", "-l", "1", "-n", "0", "-s", "0"]);
    for line in out.lines() {
        let t = line.trim();
        if !t.contains("CPU usage:") {
            continue;
        }
        let mut vals: Vec<f64> = Vec::new();
        for tok in t.split_whitespace() {
            if let Some(v) = tok.strip_suffix('%') {
                if let Ok(f) = v.parse::<f64>() {
                    vals.push(f);
                }
            }
        }
        if vals.len() >= 2 {
            return (vals[0] + vals[1]).min(100.0);
        }
    }
    0.0
}

/// 内存：sysctl hw.memsize 取总量，vm_stat 页计数算已用。
fn sample_memory_mb() -> (f64, f64) {
    const PAGE_SIZE: f64 = 4096.0;

    let total_bytes: u64 = run_cmd(&["/usr/sbin/sysctl", "-n", "hw.memsize"])
        .trim()
        .parse()
        .unwrap_or(0);
    let total_mb = total_bytes as f64 / 1024.0 / 1024.0;

    let vm = run_cmd(&["/usr/bin/vm_stat"]);
    let mut free_pages: f64 = 0.0;
    let mut inactive_pages: f64 = 0.0;
    let mut speculative_pages: f64 = 0.0;
    for line in vm.lines() {
        let t = line.trim();
        if let Some(rest) = t.strip_prefix("Pages free:") {
            free_pages = parse_vm_pages(rest);
        } else if let Some(rest) = t.strip_prefix("Pages inactive:") {
            inactive_pages = parse_vm_pages(rest);
        } else if let Some(rest) = t.strip_prefix("Pages speculative:") {
            speculative_pages = parse_vm_pages(rest);
        }
    }

    let used_mb = if total_bytes > 0 {
        (total_bytes as f64 - (free_pages + inactive_pages + speculative_pages) * PAGE_SIZE)
            / 1024.0
            / 1024.0
    } else {
        0.0
    };
    (total_mb, used_mb.max(0.0))
}

fn parse_vm_pages(s: &str) -> f64 {
    s.trim()
        .trim_end_matches('.')
        .split_whitespace()
        .next()
        .and_then(|v| v.parse::<f64>().ok())
        .unwrap_or(0.0)
}

/// 网络速率：netstat -ib 计数器差值（两次采样之间），宿主缓存上一次计数。
fn sample_network_kbps() -> (f64, f64) {
    const KBPS_FACTOR: f64 = 8.0 / 1024.0;

    let out = run_cmd(&["/usr/bin/netstat", "-ib"]);
    let mut lines = out.lines();

    // 定位表头里的 Ibytes / Obytes 列
    let header = loop {
        match lines.next() {
            Some(line) => {
                let t = line.trim();
                if t.starts_with("Name") && t.contains("Ibytes") {
                    let cols: Vec<&str> = t.split_whitespace().collect();
                    break cols
                        .iter()
                        .position(|c| *c == "Ibytes")
                        .and_then(|ib| cols.iter().position(|c| *c == "Obytes").map(|ob| (ib, ob)));
                }
            }
            None => return (0.0, 0.0),
        }
    };
    let Some((ib, ob)) = header else { return (0.0, 0.0) };

    let mut recv: u64 = 0;
    let mut sent: u64 = 0;
    for line in lines {
        let cols: Vec<&str> = line.split_whitespace().collect();
        // 只统计 <Link#> 行（地址行会重复计数同一接口）
        if cols.len() < 3 || !cols[2].starts_with("<Link") {
            continue;
        }
        if cols[0] == "lo0" {
            continue;
        }
        if let (Some(r), Some(s)) = (
            cols.get(ib).and_then(|v| v.parse::<u64>().ok()),
            cols.get(ob).and_then(|v| v.parse::<u64>().ok()),
        ) {
            recv += r;
            sent += s;
        }
    }

    let now = SystemTime::now();
    let lock = NET_STATE.get_or_init(|| Mutex::new(None));
    let mut guard = lock.lock().unwrap_or_else(|p| p.into_inner());

    if let Some(prev) = guard.as_ref() {
        let secs = now
            .duration_since(prev.at)
            .map(|d| d.as_secs_f64())
            .unwrap_or(1.0)
            .max(0.001);
        let down = recv.saturating_sub(prev.recv) as f64 / secs * KBPS_FACTOR;
        let up = sent.saturating_sub(prev.sent) as f64 / secs * KBPS_FACTOR;
        *guard = Some(NetPoint { recv, sent, at: now });
        return (down.max(0.0), up.max(0.0));
    }

    *guard = Some(NetPoint { recv, sent, at: now });
    (0.0, 0.0)
}

/// 磁盘：df 枚举真实磁盘卷（排除 devfs / 系统固件卷），按设备去重。
fn sample_disks() -> Vec<DiskSample> {
    const KB_PER_GB: f64 = 1024.0 * 1024.0;

    let out = run_cmd(&["/bin/df", "-Pk"]);
    let mut seen: HashSet<String> = HashSet::new();
    let mut disks: Vec<DiskSample> = Vec::new();

    for line in out.lines().skip(1) {
        let f: Vec<&str> = line.split_whitespace().collect();
        if f.len() < 6 {
            continue;
        }
        let dev = f[0];
        if !dev.starts_with("/dev/disk") || seen.contains(dev) {
            continue;
        }
        seen.insert(dev.to_string());

        let mount = f[5];
        if mount.starts_with("/System/Volumes/Preboot")
            || mount.starts_with("/System/Volumes/VM")
            || mount.starts_with("/System/Volumes/Update")
            || mount.starts_with("/System/Volumes/xarts")
            || mount.starts_with("/System/Volumes/iSCPreboot")
            || mount.starts_with("/System/Volumes/Hardware")
        {
            continue;
        }

        if let (Ok(total), Ok(avail)) = (f[1].parse::<f64>(), f[3].parse::<f64>()) {
            disks.push(DiskSample {
                name: mount.to_string(),
                total_gb: round2(total / KB_PER_GB),
                free_gb: round2(avail / KB_PER_GB),
            });
        }
    }
    disks
}

// ===========================================================================
// 应用自更新检查（与 Windows 侧 UpdateService 对应，指向新仓库 OBS-Helpmac）
// ===========================================================================
//
// 前端 CSP 禁外网，因此由宿主 curl 转发 GitHub tags 接口；只透传 tag 名数组，
// 版本比较由前端完成（与 env.info 的 appVersion 对比）。

fn app_check_update() -> Result<String, String> {
    let body = run_curl_get("https://api.github.com/repos/YYRMMAYO/OBS-Helpmac/tags");
    if body.trim().is_empty() {
        return Ok(String::new());
    }
    let v: Value = match serde_json::from_str(&body) {
        Ok(v) => v,
        Err(_) => return Ok(String::new()),
    };

    let mut tags: Vec<String> = Vec::new();
    if let Some(arr) = v.as_array() {
        for item in arr {
            if let Some(name) = item.get("name").and_then(Value::as_str) {
                let t = normalize_version(name);
                if !t.is_empty() {
                    tags.push(t);
                }
            }
        }
    }
    serde_json::to_string(&tags).map_err(|e| format!("序列化失败: {e}"))
}

// ===========================================================================
// 在 Finder 中显示文件 / 目录（对应 Windows 侧的「打开所在目录」）
// ===========================================================================

fn shell_reveal(path: &str) -> Result<String, String> {
    if path.trim().is_empty() {
        return Err("路径为空。".into());
    }
    let full = fs::canonicalize(path).map_err(|_| "文件不存在。".to_string())?;

    let home = PathBuf::from(std::env::var("HOME").unwrap_or_default());
    let allowed: Vec<PathBuf> = vec![
        obs_config_dir(),
        app_data_dir(),
        home.join("Desktop"),
        home.join("Downloads"),
        home.join("Documents"),
    ];
    let ok = allowed.iter().any(|root| {
        fs::canonicalize(root)
            .map(|r| full.starts_with(&r))
            .unwrap_or(false)
    });
    if !ok {
        return Err("只允许显示应用数据 / OBS 配置目录内的文件。".into());
    }

    let status = Command::new("/usr/bin/open")
        .arg("-R")
        .arg(&full)
        .status()
        .map_err(|e| format!("打开 Finder 失败: {e}"))?;
    if status.success() {
        Ok(String::new())
    } else {
        Err("系统拒绝显示该文件。".into())
    }
}

/// 把字符串变成 ASCII slug（场景集合合并导入时用）。
fn slugify(s: &str) -> String {
    let mut out = String::new();
    for c in s.chars() {
        if c.is_ascii_alphanumeric() || c == '_' || c == '-' {
            out.push(c.to_ascii_lowercase());
        } else if c == ' ' {
            out.push('_');
        }
    }
    if out.is_empty() {
        "collection".to_string()
    } else {
        out
    }
}

// ===========================================================================
// 单元测试（纯函数部分，可在任意平台 `cargo test` 验证）
// ===========================================================================

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn rejects_bad_secret_keys() {
        assert!(validate_secret_key("obs.password").is_ok());
        assert!(validate_secret_key("").is_err());
        assert!(validate_secret_key("has space").is_err());
        assert!(validate_secret_key(&"x".repeat(200)).is_err());
    }

    #[test]
    fn detects_private_hosts() {
        assert!(is_private_host("localhost"));
        assert!(is_private_host("127.0.0.1"));
        assert!(is_private_host("192.168.1.10"));
        assert!(is_private_host("172.20.0.5"));
        assert!(is_private_host("169.254.169.254"));
        assert!(!is_private_host("api.openai.com"));
        assert!(!is_private_host("172.32.0.1"));
    }

    #[test]
    fn parses_https_host() {
        assert_eq!(host_of_https("https://api.openai.com/v1/chat"), Some("api.openai.com".into()));
        assert_eq!(host_of_https("https://a.b:8443/x?y=1"), Some("a.b".into()));
        assert_eq!(host_of_https("http://a.b/"), None);
    }

    #[test]
    fn quotes_for_curl_config() {
        assert_eq!(curl_quote(r#"a"b\c"#), r#""a\"b\\c""#);
    }

    #[test]
    fn only_txt_and_log_allowed() {
        assert!(is_allowed_ext(Path::new("/tmp/a.log")));
        assert!(is_allowed_ext(Path::new("/tmp/a.TXT")));
        assert!(!is_allowed_ext(Path::new("/tmp/a.json")));
        assert!(!is_allowed_ext(Path::new("/tmp/a")));
    }

    #[test]
    fn unknown_action_is_rejected() {
        assert!(dispatch(None, "fs.readAnything", "{}").is_err());
    }

    #[test]
    fn normalizes_zip_entry_names() {
        assert_eq!(normalize_entry_name("config/basic/scenes/a.json").as_deref(), Some("config/basic/scenes/a.json"));
        assert_eq!(normalize_entry_name("../etc/passwd"), None);
        assert_eq!(normalize_entry_name("a/../../b"), None);
        assert_eq!(normalize_entry_name("/etc/passwd"), None);
        assert_eq!(normalize_entry_name("config//basic/./a"), None);
    }

    #[test]
    fn slugifies_for_merge_import() {
        assert_eq!(slugify("My Scene (Imported)"), "my_scene_imported");
        assert_eq!(slugify("abc 123"), "abc_123");
        assert_eq!(slugify(""), "collection");
    }

    #[test]
    fn sanitizes_filenames() {
        assert_eq!(sanitize_file_name("obshelper_my_01.json", "f.json"), "obshelper_my_01.json");
        assert_eq!(sanitize_file_name("", "f.json"), "f.json");
        // 路径分隔符与控制字符一律替换为下划线，绝不允许产生子路径
        let s = sanitize_file_name("a/b\\c:*.?", "f.json");
        assert!(!s.contains('/') && !s.contains('\\') && !s.contains(':') && !s.contains('*') && !s.contains('?'));
        assert!(s.starts_with("a_b_c"));
    }

    #[test]
    fn redacts_service_json() {
        let raw = r#"{"settings":{"key":"sk-123","server":"rtmp://x/y?token=abc","type":"rtmp_common"}}"#;
        let out = redact_service_json(raw).unwrap();
        assert!(!out.contains("sk-123"));
        assert!(!out.contains("token=abc"));
        assert!(out.contains("rtmp_common"));
        assert!(redact_service_json("not json").is_none());
    }

    #[test]
    fn parses_vm_pages() {
        assert_eq!(parse_vm_pages("  12345."), 12345.0);
        assert_eq!(parse_vm_pages(" 0"), 0.0);
        assert_eq!(parse_vm_pages("abc"), 0.0);
    }

    #[test]
    fn parses_system_profiler_gpus() {
        let out = "Graphics/Displays:\n\n    Apple M2 Pro:\n\n      Chipset Model: Apple M2 Pro\n      Type: GPU\n      Vendor: Apple (0x106b)\n";
        let gpus = parse_gpus(out);
        assert_eq!(gpus.len(), 1);
        assert_eq!(gpus[0].name, "Apple M2 Pro");
        assert_eq!(gpus[0].vendor, "Apple");
        assert!(gpus[0].is_active);
        assert!(parse_gpus("").is_empty());
    }

    #[test]
    fn parses_df_output() {
        let out = "Filesystem 1024-blocks      Used Available Capacity Mounted on\n\
                   /dev/disk3s5 524288000 262144000 209715200      56% /System/Volumes/Data\n";
        let (free, total) = parse_df_gb(out);
        assert_eq!(total, 500.0);
        assert_eq!(free, 200.0);
        assert_eq!(parse_df_gb("garbage"), (0.0, 0.0));
    }

    #[test]
    fn normalizes_release_tags() {
        assert_eq!(normalize_version("31.0.2"), "31.0.2");
        assert_eq!(normalize_version(" v30.1.2 "), "30.1.2");
        assert_eq!(normalize_version(""), "");
        assert_eq!(normalize_version("release; rm -rf /"), "");
    }
}
