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
use std::fs;
use std::io::{Read, Seek, SeekFrom, Write};
use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};
use std::time::{SystemTime, UNIX_EPOCH};

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
#[tauri::command]
pub async fn host_invoke(action: String, payload: String) -> Result<String, String> {
    dispatch(&action, &payload)
}

fn dispatch(action: &str, payload: &str) -> Result<String, String> {
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
        "ai.chat" => ai_chat(
            str_of(&p, "url"),
            str_of(&p, "secretKey"),
            str_of(&p, "body"),
        ),
        other => Err(format!("未知命令: {other}")),
    }
}

/// 从 JSON 对象里安全地取字符串字段，缺失或类型不符时返回空串。
fn str_of<'a>(v: &'a Value, name: &str) -> &'a str {
    v.get(name).and_then(Value::as_str).unwrap_or("")
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
        assert!(dispatch("fs.readAnything", "{}").is_err());
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
