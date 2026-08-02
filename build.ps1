<#
  OBS 排障助手 — Windows 端构建与打包脚本
  ------------------------------------------------------------
  流程：
    1) 发布 Blazor WASM 客户端站点 -> clientWww
    2) 把站点暂存到 OBS_Helper.Win\clientdist\（Win 工程会把它拷进发布包的 wwwroot）
    3) 自包含发布 Win 工程（含 .NET 运行时）
    4) Inno Setup 生成安装包 -> PAKE\windows
    5) 同时把可独立运行的软件压缩为便携包 -> PAKE\windows
  不论系统，安装包与软件统一落到仓库根 PAKE\ 下。
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$client = Join-Path $root "OBS_Helper.Client"
$win    = Join-Path $root "OBS_Helper.Win"
$pake   = Join-Path $root "PAKE"
$pakeWin = Join-Path $pake "windows"

# 确保 dotnet 在 PATH 中
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    $dotnet = "C:\Program Files\dotnet\dotnet.exe"
    if (Test-Path $dotnet) { $env:PATH = "C:\Program Files\dotnet;" + $env:PATH }
    else { throw "找不到 dotnet，请先安装 .NET 10 SDK。" }
}

function Step($msg) { Write-Host ""; Write-Host "==> $msg" -ForegroundColor Cyan }

# 1) 发布 Blazor WASM 客户端
Step "发布 Blazor WASM 客户端站点"
dotnet publish (Join-Path $client "OBS_Helper.Client.csproj") -c $Configuration
$clientWww = Join-Path $client "bin\$Configuration\net10.0\publish\wwwroot"
if (-not (Test-Path (Join-Path $clientWww "index.html"))) {
    throw "客户端发布产物缺少 wwwroot/index.html：检查 $clientWww"
}

# 2) 暂存站点到 Win 工程的 clientdist（用 Copy-Item -Force 覆盖，避免删除受限目录）
Step "暂存站点到 OBS_Helper.Win\clientdist"
$winClientdist = Join-Path $win "clientdist"
New-Item -ItemType Directory -Force -Path $winClientdist | Out-Null
Copy-Item (Join-Path $clientWww "*") $winClientdist -Recurse -Force

# 3) 自包含发布 Win 工程
Step "自包含发布 Windows 工程 ($Runtime)"
dotnet publish (Join-Path $win "OBS_Helper.Win.csproj") -c $Configuration -r $Runtime --self-contained true
$winPub = Join-Path $win "bin\$Configuration\net10.0-windows10.0.19041.0\$Runtime\publish"
if (-not (Test-Path (Join-Path $winPub "OBS_Helper.exe"))) {
    throw "Win 发布产物缺少 OBS_Helper.exe：检查 $winPub"
}
if (-not (Test-Path (Join-Path $winPub "wwwroot\index.html"))) {
    throw "Win 发布包缺少 wwwroot/index.html（clientdist 未正确并入）。"
}

# 4) Inno Setup 安装包 -> PAKE\windows
Step "Inno Setup 生成安装包 -> PAKE\windows"
$iscc = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $iscc)) { throw "找不到 Inno Setup：$iscc" }
New-Item -ItemType Directory -Force -Path $pakeWin | Out-Null
& $iscc (Join-Path $win "OBS_Helper_Setup.iss") | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Inno Setup 构建失败。" }

# 5) 便携压缩包 -> PAKE\windows
Step "生成便携压缩包 -> PAKE\windows"
$ver = "1.0.0"
$zip = Join-Path $pakeWin "OBS_Helper_Portable_$ver.zip"
# 仅当旧包存在时才删除（避免对不存在的文件执行删除），随后重新打包。
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $winPub "*") -DestinationPath $zip

Step "完成"
Write-Host "`n产物位置：`n  - 安装包 : $pakeWin\OBS_Helper_Setup_$ver.exe`n  - 便携包 : $zip" -ForegroundColor Green
Get-ChildItem $pakeWin | ForEach-Object { Write-Host ("    " + $_.Name) }
