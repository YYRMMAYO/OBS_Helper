<#
  Render repository cover + icon (V2.9.4)
  ------------------------------------------------------------
  Why a script: GitHub's social preview only accepts bitmaps, while the README
  cover should stay vector. So the SVG stays the single source of truth and this
  script renders the PNGs -- a manual screenshot would silently go stale.

  The renderer is the bundled Edge/Chrome headless mode: no extra dependency.
  --window-size must equal the SVG's width/height, otherwise the shot gets a
  white border.

  NOTE: this file is intentionally ASCII-only. Windows PowerShell 5.1 reads a
  BOM-less .ps1 as ANSI/GBK, and non-ASCII comments then break the parser.

  Usage:
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts\render_assets.ps1
  Output (repo root assets/):
    icon.png            512x512    app icon / social fallback
    banner.png          1280x320   README cover
    social-preview.png  1280x640   GitHub repository social preview
#>
[CmdletBinding()]
param(
    # Render scale: 2 means twice the resolution (social previews care about crispness)
    [int]$Scale = 2
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $root "assets"
if (-not (Test-Path $assets)) { throw "assets directory not found: $assets" }

$browser = @(
    "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
    "C:\Program Files\Microsoft\Edge\Application\msedge.exe",
    "C:\Program Files\Google\Chrome\Application\chrome.exe",
    "C:\Program Files (x86)\Google\Chrome\Application\chrome.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $browser) {
    throw "Edge/Chrome not found. This script deliberately downloads nothing; install a browser first."
}

function Render {
    param([string]$Svg, [int]$Width, [int]$Height, [string]$Out)

    $uri = "file:///" + ($Svg -replace '\\', '/')

    # The browser writes "N bytes written" to stderr; with ErrorActionPreference=Stop
    # an otherwise successful render would abort the whole script, so isolate it.
    $prev = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        & $browser --headless=new --disable-gpu --hide-scrollbars --no-first-run `
            "--force-device-scale-factor=$Scale" `
            "--window-size=$Width,$Height" "--screenshot=$Out" $uri 2>&1 | Out-Null
    }
    finally {
        $ErrorActionPreference = $prev
    }

    if (-not (Test-Path $Out)) { throw "render failed, no output: $Out" }
    $kb = (Get-Item $Out).Length / 1KB
    Write-Host ("  {0}  {1}x{2} @{3}x  ({4:N0} KB)" -f (Split-Path -Leaf $Out), $Width, $Height, $Scale, $kb) -ForegroundColor DarkGray
}

Write-Host "Rendering cover and icon (engine: $(Split-Path -Leaf $browser), scale $Scale)" -ForegroundColor Cyan
Render -Svg (Join-Path $assets "icon.svg")   -Width 256  -Height 256 -Out (Join-Path $assets "icon.png")
Render -Svg (Join-Path $assets "banner.svg") -Width 1280 -Height 320 -Out (Join-Path $assets "banner.png")

$social = Join-Path $assets "social-preview.svg"
if (Test-Path $social) {
    Render -Svg $social -Width 1280 -Height 640 -Out (Join-Path $assets "social-preview.png")
}

Write-Host "Done. Output in $assets" -ForegroundColor Green
