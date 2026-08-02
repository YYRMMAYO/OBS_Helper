#!/usr/bin/env bash
# OBS 排障助手 — macOS 端构建脚本（需在 macOS + Rust 工具链下运行，CI 中用 macos runner）
# 流程：
#   1) dotnet publish Blazor WASM 客户端 -> OBS_Helper.Client/bin/Release/net10.0/publish/wwwroot
#   2) tauri icon 由源图生成各尺寸图标
#   3) tauri build -> 产物落到 src-tauri/target/release/bundle/（Tauri v2 内置路径，outDir 已移除）
#   4) 将 .app / .dmg 复制到仓库根 PAKE/macos 供 CI 上传产物与本地取回
#
# 可选：通过环境变量开启代码签名 / 公证（未设置则产出未签名包，仅适合自测与内部分发）
#   MAC_SIGN_IDENTITY="Developer ID Application: ..."   # 不传则 --no-sign
#   MAC_NOTARY_KEYCHAIN_PROFILE="notarytool-profile"    # 不传则跳过公证
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

# Tauri 默认产物目录（相对仓库根的 src-tauri 工程下）
BUNDLE_DIR="$ROOT/OBS_Helper.Mac/src-tauri/target/release/bundle"
OUT_DIR="$ROOT/PAKE/macos"

# 探测 tauri CLI：优先 npm 安装的 `tauri`，回退 `cargo tauri`
if command -v tauri >/dev/null 2>&1; then
  TAURI="tauri"
elif command -v cargo >/dev/null 2>&1; then
  TAURI="cargo tauri"
else
  echo "ERROR: 未检测到 tauri / cargo，无法构建 macOS 端。" >&2
  exit 1
fi

echo "==> 1/4 发布 Blazor WASM 客户端站点"
dotnet publish OBS_Helper.Client/OBS_Helper.Client.csproj -c Release

echo "==> 2/4 生成 Tauri 图标 (使用 $TAURI)"
cd OBS_Helper.Mac/src-tauri
$TAURI icon icons/app-icon.png

echo "==> 3/4 $TAURI build"
if [ -n "${MAC_SIGN_IDENTITY:-}" ]; then
  echo "   使用签名身份: $MAC_SIGN_IDENTITY"
  $TAURI build
  # 对 .app 做 Developer ID 签名（深度签名，含运行时）
  APP_BUNDLE=$(find "$BUNDLE_DIR" -maxdepth 2 -name "*.app" -type d | head -1 || true)
  if [ -n "$APP_BUNDLE" ]; then
    codesign --force --options runtime --timestamp --sign "$MAC_SIGN_IDENTITY" "$APP_BUNDLE"
  fi
  # 公证（可选）
  if [ -n "${MAC_NOTARY_KEYCHAIN_PROFILE:-}" ]; then
    DMG_PATH=$(find "$BUNDLE_DIR" -maxdepth 2 -name "*.dmg" | head -1 || true)
    if [ -n "$DMG_PATH" ]; then
      echo "   提交公证: $DMG_PATH"
      xcrun notarytool submit "$DMG_PATH" --keychain-profile "$MAC_NOTARY_KEYCHAIN_PROFILE" --wait
      xcrun stapler staple "$DMG_PATH"
    fi
  fi
else
  echo "   未设置 MAC_SIGN_IDENTITY，产出未签名包（自测 / 内部分发用）"
  $TAURI build
fi

echo "==> 4/4 复制产物到 $OUT_DIR"
if [ ! -d "$BUNDLE_DIR" ]; then
  echo "ERROR: Tauri 产物目录不存在: $BUNDLE_DIR" >&2
  exit 1
fi
mkdir -p "$OUT_DIR"
# 复制 .app 与 .dmg（保留结构，覆盖已有）
find "$BUNDLE_DIR" -maxdepth 2 \( -name "*.app" -o -name "*.dmg" \) -print0 | while IFS= read -r -d '' item; do
  cp -R "$item" "$OUT_DIR"/
done

echo "==> 完成。安装包与软件位于: $OUT_DIR"
ls -la "$OUT_DIR"
