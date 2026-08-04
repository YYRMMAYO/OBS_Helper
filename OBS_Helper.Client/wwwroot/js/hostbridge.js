/*
 * OBS 排障助手 — 桌面壳桥接层（Host Bridge）
 * ---------------------------------------------------------------------------
 * Blazor WebAssembly 运行在 WebView 沙箱内，无法访问文件系统与系统钥匙串。
 * 本文件把两个桌面壳的原生能力抽象成同一个接口，供 C# 侧通过 JS 互操作调用：
 *
 *   window.obsHelperHost.invoke(cmd, payloadJson) -> Promise<string>
 *
 * 平台实现：
 *   - Windows：WebView2 的 postMessage / message 事件（宿主 MainForm.cs 处理，
 *              密码等机密用 DPAPI（CurrentUser 范围）加密后落盘）
 *   - macOS  ：Tauri v2 IPC 命令 host_invoke（宿主 main.rs 处理，
 *              机密写入系统钥匙串 Keychain）
 *   - 浏览器 ：无宿主时 available=false，调用方需降级（仅内存保存，不落盘）
 *
 * 安全约束：宿主侧只接受固定命令白名单，且对路径做目录限定，避免任意文件读写。
 */
(function () {
    'use strict';

    var PENDING = {};
    var SEQ = 0;
    /** 宿主命令超时（毫秒）。读日志文件可能稍慢，给到 20s。 */
    var TIMEOUT_MS = 20000;

    function newId() {
        SEQ += 1;
        return 'h' + SEQ + '_' + Date.now().toString(36);
    }

    function settle(id, ok, result, error) {
        var p = PENDING[id];
        if (!p) return;
        delete PENDING[id];
        clearTimeout(p.timer);
        if (ok) p.resolve(result || '');
        else p.reject(new Error(error || '宿主命令执行失败'));
    }

    // ---------------------------------------------------------------- Windows
    function createWebView2Bridge() {
        window.chrome.webview.addEventListener('message', function (e) {
            var msg = e.data;
            if (typeof msg === 'string') {
                try { msg = JSON.parse(msg); } catch (err) { return; }
            }
            if (!msg || !msg.id) return;
            settle(msg.id, !!msg.ok, msg.result, msg.error);
        });

        return {
            available: true,
            platform: 'windows',
            invoke: function (cmd, payloadJson) {
                return new Promise(function (resolve, reject) {
                    var id = newId();
                    PENDING[id] = {
                        resolve: resolve,
                        reject: reject,
                        timer: setTimeout(function () {
                            settle(id, false, null, '宿主命令超时: ' + cmd);
                        }, TIMEOUT_MS)
                    };
                    try {
                        window.chrome.webview.postMessage(JSON.stringify({
                            id: id, cmd: cmd, payload: payloadJson || '{}'
                        }));
                    } catch (err) {
                        settle(id, false, null, String(err));
                    }
                });
            }
        };
    }

    // ------------------------------------------------------------------ macOS
    function createTauriBridge(core) {
        return {
            available: true,
            platform: 'macos',
            // 注意：参数名用 action 而非 cmd —— cmd 是 Tauri IPC 报文的历史保留字段名。
            invoke: function (cmd, payloadJson) {
                return core.invoke('host_invoke', { action: cmd, payload: payloadJson || '{}' })
                    .then(function (r) { return r == null ? '' : String(r); });
            }
        };
    }

    // ------------------------------------------------------------- 无宿主降级
    function createNullBridge() {
        return {
            available: false,
            platform: 'none',
            invoke: function (cmd) {
                return Promise.reject(new Error('当前环境没有桌面宿主，命令不可用: ' + cmd));
            }
        };
    }

    function detect() {
        try {
            if (window.chrome && window.chrome.webview && typeof window.chrome.webview.postMessage === 'function') {
                return createWebView2Bridge();
            }
        } catch (err) { /* 非 WebView2 环境 */ }

        try {
            var t = window.__TAURI__;
            if (t && t.core && typeof t.core.invoke === 'function') {
                return createTauriBridge(t.core);
            }
        } catch (err) { /* 非 Tauri 环境 */ }

        return createNullBridge();
    }

    window.obsHelperHost = detect();

    /* -------------------------------------------------------------------
     * 无障碍与外观：由 C# 设置服务调用，作用在 <html> 根元素上。
     * 主题 / 字号 / 高对比度 都通过 data-* 属性驱动 CSS 变量，避免内联样式。
     * ----------------------------------------------------------------- */
    window.obsHelperUi = {
        applyAppearance: function (theme, fontScale, highContrast, reduceMotion) {
            var el = document.documentElement;
            el.setAttribute('data-theme', theme || 'system');
            el.setAttribute('data-font-scale', fontScale || 'md');
            if (highContrast) el.setAttribute('data-contrast', 'high');
            else el.removeAttribute('data-contrast');
            if (reduceMotion) el.setAttribute('data-motion', 'reduce');
            else el.removeAttribute('data-motion');
        },
        prefersDark: function () {
            try {
                return window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
            } catch (err) { return false; }
        },
        /** 读取拖入的日志文件为文本（浏览器 File API，不经过宿主）。 */
        readDroppedFile: function (inputElement) {
            return new Promise(function (resolve, reject) {
                var f = inputElement && inputElement.files && inputElement.files[0];
                if (!f) { reject(new Error('未选择文件')); return; }
                var reader = new FileReader();
                reader.onload = function () { resolve(String(reader.result || '')); };
                reader.onerror = function () { reject(new Error('读取文件失败')); };
                reader.readAsText(f, 'utf-8');
            });
        }
    };
})();
