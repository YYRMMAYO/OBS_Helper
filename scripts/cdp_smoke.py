#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
OBS 排障助手 — 无头冒烟测试（Chrome DevTools Protocol）
托管已发布的 Blazor WASM 站点，用无头 Edge 打开页面，
检查 #app 是否渲染，并收集未捕获异常 / 控制台错误。

用法：
    python scripts/cdp_smoke.py --root "F:/OBS/OBS_Helper.Win/bin/Release/net10.0-windows10.0.19041.0/publish/wwwroot"
退出码：0=通过，1=失败，2=环境缺失。
"""
import json, time, threading, http.server, os, sys, subprocess, urllib.request, argparse

# GitHub Windows runner 控制台默认 cp1252，直接输出中文会抛 UnicodeEncodeError。
# 在启动时把 stdout/stderr 固定为 UTF-8 + replace，本地与 CI 都不会因编码崩溃。
try:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    if hasattr(sys.stderr, "reconfigure"):
        sys.stderr.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

def _safe_print(*args, **kwargs):
    """Print 兜底：极少数无法 reconfigure 的环境，再次出现编码错误时降级输出。"""
    try:
        print(*args, **kwargs)
    except UnicodeEncodeError:
        parts = " ".join(str(a) for a in args)
        safe = parts.encode("utf-8", "replace").decode("utf-8", "replace")
        print(safe, **kwargs)

try:
    from websocket import create_connection
except ImportError:
    print("缺少依赖 websocket-client，请先安装：pip install websocket-client")
    sys.exit(2)

DEFAULT_ROOT = r"F:/OBS/OBS_Helper.Win/bin/Release/net10.0-windows10.0.19041.0/win-x64/publish/wwwroot"

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--root", default=os.environ.get("CDP_ROOT") or DEFAULT_ROOT)
    args = ap.parse_args()

    ROOT = args.root
    if not os.path.isdir(ROOT):
        _safe_print("站点目录不存在:", ROOT)
        sys.exit(2)

    DEBUG_PORT = 9466
    PAGE_PORT = 8790
    UD = r"C:/tmp/cdp_ud_%d" % DEBUG_PORT
    os.makedirs(UD, exist_ok=True)

    os.chdir(ROOT)
    httpd = http.server.ThreadingHTTPServer(("127.0.0.1", PAGE_PORT), http.server.SimpleHTTPRequestHandler)
    threading.Thread(target=httpd.serve_forever, daemon=True).start()

    edge = r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
    if not os.path.exists(edge):
        edge = r"C:\Program Files\Microsoft\Edge\Application\msedge.exe"
    if not os.path.exists(edge):
        _safe_print("未找到 Edge 浏览器，无法执行无头测试。")
        sys.exit(2)

    proc = subprocess.Popen([edge, "--headless=new",
        "--remote-debugging-port=%d" % DEBUG_PORT,
        "--remote-allow-origins=*",
        "--user-data-dir=" + UD,
        "--no-first-run", "--no-default-browser-check", "--disable-gpu", "--disable-dev-shm-usage",
        "about:blank"])

    def get_ws():
        for _ in range(30):
            try:
                data = urllib.request.urlopen("http://127.0.0.1:%d/json" % DEBUG_PORT, timeout=2).read()
                for t in json.loads(data):
                    if t.get("type") == "page" and t.get("webSocketDebuggerUrl"):
                        return t["webSocketDebuggerUrl"]
            except Exception:
                time.sleep(1)
        return None

    wsurl = get_ws()
    if not wsurl:
        _safe_print("FAILED to get ws url")
        proc.terminate()
        sys.exit(1)

    ws = create_connection(wsurl, timeout=60)
    ws.send(json.dumps({"id": 1, "method": "Runtime.enable"}))
    ws.send(json.dumps({"id": 2, "method": "Log.enable"}))
    ws.send(json.dumps({"id": 3, "method": "Page.enable"}))

    events = []
    holder = {}

    def reader():
        while True:
            try:
                msg = ws.recv()
            except Exception:
                break
            if not msg:
                break
            try:
                obj = json.loads(msg)
            except Exception:
                continue
            m = obj.get("method", "")
            rid = obj.get("id")
            if rid == 20:
                holder["len"] = obj.get("result", {}).get("result", {}).get("value")
                continue
            if rid == 21:
                holder["text"] = obj.get("result", {}).get("result", {}).get("value")
                continue
            if m in ("Runtime.exceptionThrown", "Runtime.consoleAPICalled", "Log.entryAdded"):
                events.append(obj)

    threading.Thread(target=reader, daemon=True).start()

    ws.send(json.dumps({"id": 10, "method": "Page.navigate",
                        "params": {"url": "http://127.0.0.1:%d/index.html" % PAGE_PORT}}))

    ok = False
    for _ in range(60):
        time.sleep(1)
        ws.send(json.dumps({"id": 20, "method": "Runtime.evaluate",
                            "params": {"expression": "document.getElementById('app')?document.getElementById('app').innerText.length:0",
                                       "returnByValue": True}}))
        time.sleep(0.3)
        v = holder.get("len")
        try:
            if v is not None and int(v) > 20:
                ok = True
                break
        except Exception:
            pass

    # 抓取真实渲染文本
    ws.send(json.dumps({"id": 21, "method": "Runtime.evaluate",
                        "params": {"expression": "document.getElementById('app')?document.getElementById('app').innerText:'NO #app'",
                                   "returnByValue": True}}))
    time.sleep(1)

    _safe_print("=== APP RENDERED:", ok)
    _safe_print("=== EXCEPTIONS / CONSOLE / LOG ===")
    err_count = 0
    for e in events:
        m = e["method"]
        if m == "Runtime.exceptionThrown":
            err_count += 1
            ex = e["params"]["exceptionDetails"]
            _safe_print("EXCEPTION:", (ex.get("exception") or {}).get("description") or ex.get("text"))
        elif m == "Runtime.consoleAPICalled":
            a = [x.get("value", x.get("description", "")) for x in e["params"].get("args", [])]
            _safe_print("CONSOLE[%s]:" % e["params"].get("type"), " ".join(str(x) for x in a))
        elif m == "Log.entryAdded":
            _safe_print("LOG[%s]:" % e["params"].get("level"), e["params"].get("text"))

    _safe_print("=== APP INNERTEXT (first 600 chars) ===")
    text = holder.get("text") or holder.get("inner") or "NONE"
    _safe_print(text[:600])
    _safe_print("=== EXCEPTION COUNT:", err_count)

    proc.terminate()
    try:
        httpd.shutdown()
    except Exception:
        pass

    if ok and err_count == 0:
        _safe_print("SMOKE TEST PASSED")
        sys.exit(0)
    _safe_print("SMOKE TEST FAILED")
    sys.exit(1)

if __name__ == "__main__":
    main()
