#!/usr/bin/env bash
# OBS 排障助手 — macOS 端构建脚本（需在 macOS + Rust 工具链下运行，CI 中用 macos runner）
# 流程：
#   1) dotnet publish Blazor WASM 客户端 -> OBS_Helper.Client/bin/Release/net10.0/publish/wwwroot
#   2) tauri icon 由源图生成各尺寸图标
#   3) tauri build -> 产物落到仓库根 PAKE/macos（.app 与 .dmg）
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

# 探测 tauri CLI：优先 npm 安装的 `tauri`，回退 `cargo tauri`
if command -v tauri >/dev/null 2>&1; then
  TAURI="tauri"
elif command -v cargo >/dev/null 2>&1; then
  TAURI="cargo tauri"
else
  echo "ERROR: 未检测到 tauri / cargo，无法构建 macOS 端。" >&2
  exit 1
fi

echo "==> 1/3 发布 Blazor WASM 客户端站点"
dotnet publish OBS_Helper.Client/OBS_Helper.Client.csproj -c Release

echo "==> 2/3 生成 Tauri 图标 (使用 $TAURI)"
cd OBS_Helper.Mac/src-tauri
$TAURI icon icons/app-icon.png

echo "==> 3/3 $TAURI build"
$TAURI build

echo "==> 完成。安装包与软件位于: $ROOT/PAKE/macos"
ls -la "$ROOT/PAKE/macos"
