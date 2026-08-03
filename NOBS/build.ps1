<#
  OBS 排障助手（WPF 版）— Windows 构建与打包脚本
  ------------------------------------------------------------
  流程：
    1) 自包含发布 WPF 工程（含 .NET 运行时，目标机无需装运行时）
    2) Inno Setup 生成安装包        -> PAKE\windows\OBS_Helper_Setup_<ver>.exe
    3) 打便携压缩包（免安装解压即用）-> PAKE\windows\OBS_Helper_Portable_<ver>.zip
    4) 可选：单文件便携 exe          -> PAKE\windows\OBS_Helper_Portable_<ver>.exe

  与旧的 Blazor + WebView2 版相比，这里没有「先发布站点再塞进壳工程」那一步了：
  WPF 版的界面就在程序集里，problems.json / troubleshooting.md 也是内嵌资源，
  所以一次 publish 就是完整产物。

  用法：
    .\build.ps1                 # 安装包 + 便携 zip
    .\build.ps1 -SingleFile     # 额外产出单文件 exe
    .\build.ps1 -SkipInstaller  # 只出便携包（没装 Inno Setup 时用）
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SingleFile,
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"

$root    = $PSScriptRoot
$proj    = Join-Path $root "OBS_Helper.Wpf\OBS_Helper.Wpf.csproj"
$projDir = Join-Path $root "OBS_Helper.Wpf"
$pakeWin = Join-Path $root "PAKE\windows"
$tfm     = "net10.0-windows"

function Step($msg) { Write-Host ""; Write-Host "==> $msg" -ForegroundColor Cyan }
function Warn($msg) { Write-Host "[!] $msg" -ForegroundColor Yellow }

# ---------------------------------------------------------------- 前置检查

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    $dotnetDir = "C:\Program Files\dotnet"
    if (Test-Path (Join-Path $dotnetDir "dotnet.exe")) {
        $env:PATH = "$dotnetDir;" + $env:PATH
    } else {
        throw "找不到 dotnet，请先安装 .NET 10 SDK。"
    }
}

if (-not (Test-Path $proj)) { throw "找不到工程文件：$proj" }

# 版本号以 csproj 里的 <Version> 为准，避免脚本和工程各写一套对不上
$ver = "1.0.0"
$csprojXml = [xml](Get-Content $proj -Raw)
$verNode = $csprojXml.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if ($verNode) { $ver = "$verNode".Trim() }
Write-Host "版本号：$ver" -ForegroundColor DarkGray

New-Item -ItemType Directory -Force -Path $pakeWin | Out-Null

# ---------------------------------------------------------------- 1) 发布

Step "自包含发布 WPF 工程 ($Runtime / $Configuration)"
dotnet publish $proj -c $Configuration -r $Runtime --self-contained true `
    -p:PublishSingleFile=false -p:PublishReadyToRun=true | Out-Host
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败。" }

$pub = Join-Path $projDir "bin\$Configuration\$tfm\$Runtime\publish"
$exe = Join-Path $pub "OBS_Helper.exe"
if (-not (Test-Path $exe)) { throw "发布产物缺少 OBS_Helper.exe：检查 $pub" }

$sizeMb = [math]::Round((Get-ChildItem $pub -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 1)
Write-Host "发布目录：$pub（$sizeMb MB）" -ForegroundColor DarkGray

# ---------------------------------------------------------------- 2) 安装包

$setupPath = Join-Path $pakeWin "OBS_Helper_Setup_$ver.exe"
if ($SkipInstaller) {
    Warn "已指定 -SkipInstaller，跳过 Inno Setup。"
    $setupPath = $null
} else {
    Step "Inno Setup 生成安装包"
    $iscc = $null
    foreach ($c in @(
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe"
    )) { if (Test-Path $c) { $iscc = $c; break } }

    if (-not $iscc) {
        # 没装 Inno 不该让整个构建失败——便携包仍然是可交付的产物
        Warn "找不到 Inno Setup 6（ISCC.exe），跳过安装包。下载：https://jrsoftware.org/isdl.php"
        $setupPath = $null
    } else {
        & $iscc (Join-Path $projDir "OBS_Helper_Setup.iss") | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "Inno Setup 构建失败。" }
    }
}

# ---------------------------------------------------------------- 3) 便携 zip

Step "生成便携压缩包"
$zip = Join-Path $pakeWin "OBS_Helper_Portable_$ver.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $pub "*") -DestinationPath $zip
Write-Host "便携包：$zip" -ForegroundColor DarkGray

# ---------------------------------------------------------------- 4) 单文件

$sfOut = $null
if ($SingleFile) {
    Step "生成单文件便携 exe"
    # 单独发到 publish-single，避免和上面的多文件产物混在同一目录
    $sfDir = Join-Path $projDir "bin\$Configuration\$tfm\$Runtime\publish-single"
    dotnet publish $proj -c $Configuration -r $Runtime --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true -p:PublishReadyToRun=true `
        -o $sfDir | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "单文件发布失败。" }

    $sfExe = Join-Path $sfDir "OBS_Helper.exe"
    if (-not (Test-Path $sfExe)) { throw "单文件发布产物缺少 OBS_Helper.exe：检查 $sfDir" }

    $sfOut = Join-Path $pakeWin "OBS_Helper_Portable_$ver.exe"
    Copy-Item $sfExe $sfOut -Force
    Write-Host "单文件：$sfOut" -ForegroundColor DarkGray
}

# ---------------------------------------------------------------- 汇总

Step "完成"
Write-Host "产物目录：$pakeWin" -ForegroundColor Green
if ($setupPath -and (Test-Path $setupPath)) { Write-Host "  - 安装包 : $setupPath" }
Write-Host "  - 便携包 : $zip"
if ($sfOut) { Write-Host "  - 单文件 : $sfOut" }
Get-ChildItem $pakeWin -File | ForEach-Object {
    Write-Host ("    {0}  ({1:N1} MB)" -f $_.Name, ($_.Length / 1MB))
}
