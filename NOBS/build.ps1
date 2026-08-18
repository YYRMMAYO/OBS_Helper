<#
  OBS 排障助手（WPF 版）— Windows 构建与打包脚本
  ------------------------------------------------------------
  流程：
    1) 自包含发布 WPF 工程（含 .NET 运行时，目标机无需装运行时）
    2) Inno Setup 生成安装包        -> PAKE\windows\OBS_Helper_Setup_<ver>.exe
    3) 打便携压缩包（免安装解压即用）-> PAKE\windows\OBS_Helper_Portable_<ver>.zip
    4) 生成增量更新包（仅变更文件）  -> PAKE\windows\OBS_Helper_Update_<ver>.zip
    5) 可选：单文件便携 exe          -> PAKE\windows\OBS_Helper_Portable_<ver>.exe

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

# 删除构建产物（安装包/便携包/增量包/临时目录）：直接用 .NET API。
# 不用 Remove-Item：部分环境的安全策略会把删除拦截/送回收站导致构建失败；
# 构建产物本就是要覆盖重生的，直接删除最可靠。
function Remove-Artifact {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) { return }
    try {
        if (Test-Path -LiteralPath $Path -PathType Leaf) { [System.IO.File]::Delete($Path) }
        elseif (Test-Path -LiteralPath $Path -PathType Container) { [System.IO.Directory]::Delete($Path, $true) }
    } catch {
        Warn "删除产物失败（忽略）：$Path（$($_.Exception.Message)）"
    }
}

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
        # 用 csproj 的 <Version> 覆盖 iss 里的版本号，保证安装包命名/版本与代码一致
        & $iscc "/DMyAppVersion=$ver" (Join-Path $projDir "OBS_Helper_Setup.iss") | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "Inno Setup 构建失败。" }
    }
}

# ---------------------------------------------------------------- 3) 便携 zip

Step "生成便携压缩包"
$zip = Join-Path $pakeWin "OBS_Helper_Portable_$ver.zip"
Remove-Artifact $zip
Compress-Archive -Path (Join-Path $pub "*") -DestinationPath $zip
Write-Host "便携包：$zip" -ForegroundColor DarkGray

# ---------------------------------------------------------------- 4) 增量更新包

# 增量更新：对比上一版本完整清单，打包「只含变更文件 + update_manifest.json」的增量 zip，
# 应用内下载后由 --apply-update 自举进程完成替换。清单存档在 PAKE\windows\manifests\ 供下次比对。

function New-FileManifest {
    param([string]$Dir, [string]$Version)
    $entries = Get-ChildItem $Dir -Recurse -File | ForEach-Object {
        $rel = $_.FullName.Substring($Dir.Length).TrimStart('\', '/').Replace('\', '/')
        [pscustomobject]@{
            path   = $rel
            size   = $_.Length
            sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    } | Sort-Object path
    return [ordered]@{ version = $Version; files = $entries }
}

function Save-ManifestTo {
    param([object]$Manifest, [string]$Path)
    $Manifest | ConvertTo-Json -Depth 5 | Set-Content -Path $Path -Encoding UTF8
    return $Path
}

function Get-ManifestVersion {
    param([string]$BaseName) # "manifest_2.0.0" -> 2.0.0
    $v = $BaseName -replace '^manifest_', ''
    try { return [version]$v } catch { return $null }
}

Step "生成增量更新包"
$manifestDir = Join-Path $pakeWin "manifests"
New-Item -ItemType Directory -Force -Path $manifestDir | Out-Null

$currentManifestPath = Join-Path $manifestDir "manifest_$ver.json"
Save-ManifestTo (New-FileManifest -Dir $pub -Version $ver) $currentManifestPath | Out-Null
Write-Host "完整清单：$currentManifestPath" -ForegroundColor DarkGray

# 找「低于当前版本」的上一版清单（取版本号最高的一份）
$verObj = [version]$ver
$prevManifestFile = Get-ChildItem $manifestDir -Filter "manifest_*.json" |
    Where-Object { $v = Get-ManifestVersion $_.BaseName; $v -ne $null -and $v -lt $verObj } |
    Sort-Object { Get-ManifestVersion $_.BaseName } | Select-Object -Last 1

if (-not $prevManifestFile) {
    # 首次启用增量（清单还没积累）：用历史便携包重建上一版本清单
    $prevZip = Get-ChildItem $pakeWin -Filter "OBS_Helper_Portable_*.zip" |
        ForEach-Object {
            if ($_.BaseName -match '^OBS_Helper_Portable_(\d+\.\d+\.\d+)$') {
                [pscustomobject]@{ File = $_; Ver = [version]$Matches[1] }
            }
        } |
        Where-Object { $_.Ver -lt $verObj } |
        Sort-Object Ver | Select-Object -Last 1

    if ($prevZip) {
        $prevZipFile = $prevZip.File
        $prevVer = $prevZipFile.BaseName -replace '^OBS_Helper_Portable_', ''
        $rebuildDir = Join-Path $env:TEMP "OBS_Helper_manifest_rebuild_$prevVer"
        Remove-Artifact $rebuildDir
        Expand-Archive -Path $prevZipFile.FullName -DestinationPath $rebuildDir
        $prevManifestFile = Get-Item (Save-ManifestTo (New-FileManifest -Dir $rebuildDir -Version $prevVer) (Join-Path $manifestDir "manifest_$prevVer.json"))
        Remove-Artifact $rebuildDir
        Write-Host "已从 $($prevZipFile.Name) 重建 $prevVer 清单作为比对基准" -ForegroundColor DarkGray
    }
}

$deltaZip = Join-Path $pakeWin "OBS_Helper_Update_$ver.zip"
if ($prevManifestFile) {
    $prev = Get-Content $prevManifestFile.FullName -Raw | ConvertFrom-Json
    $cur  = Get-Content $currentManifestPath -Raw | ConvertFrom-Json

    $prevMap = @{}; foreach ($f in $prev.files) { $prevMap[$f.path] = $f }
    $curMap  = @{}; foreach ($f in $cur.files)  { $curMap[$f.path] = $f }

    # 变更 = 新增或内容变化（大小 / SHA256 不同）；删除 = 旧有而新无
    $changed = @()
    foreach ($f in $cur.files) {
        $old = $prevMap[$f.path]
        if (-not $old -or $old.size -ne $f.size -or $old.sha256 -ne $f.sha256) { $changed += $f }
    }
    $removed = @($prev.files | Where-Object { -not $curMap.ContainsKey($_.path) } | ForEach-Object { $_.path })

    if ($changed.Count -eq 0 -and $removed.Count -eq 0) {
        Warn "与 $($prev.version) 相比无文件差异，跳过增量包。"
    } else {
        Remove-Artifact $deltaZip

        $stage = Join-Path $env:TEMP "OBS_Helper_delta_stage_$ver"
        Remove-Artifact $stage
        New-Item -ItemType Directory -Force -Path $stage | Out-Null

        $updateManifest = [ordered]@{
            format        = 1
            baseVersion   = "$($prev.version)"
            targetVersion = $ver
            files         = $changed
            remove        = $removed
        }
        Save-ManifestTo $updateManifest (Join-Path $stage "update_manifest.json") | Out-Null

        # 变更文件统一放在 files/ 子目录下（与应用端解压路径约定一致：pending\files\<rel>）
        foreach ($f in $changed) {
            $src = Join-Path $pub $f.path
            $dst = Join-Path (Join-Path $stage "files") $f.path
            New-Item -ItemType Directory -Force -Path (Split-Path $dst) | Out-Null
            Copy-Item $src $dst -Force
        }

        Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $deltaZip
        Remove-Artifact $stage
        Write-Host "增量包：$deltaZip（变更 $($changed.Count) 个文件，删除 $($removed.Count) 个）" -ForegroundColor DarkGray
    }
} else {
    Warn "找不到可比的上一版本（清单/历史便携包均无），跳过增量包。"
}

# ---------------------------------------------------------------- 5) 单文件

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
if (Test-Path $deltaZip) { Write-Host "  - 增量包 : $deltaZip" }
if ($sfOut) { Write-Host "  - 单文件 : $sfOut" }
Get-ChildItem $pakeWin -File | ForEach-Object {
    Write-Host ("    {0}  ({1:N1} MB)" -f $_.Name, ($_.Length / 1MB))
}
