<#
  OBS帮助助手（WPF 版，原名 OBS 排障助手）— Windows 构建与打包脚本
  ------------------------------------------------------------
  版本号以 csproj 的 <Version> 为准（本文件从工程里读，不硬编码，见「前置检查」一节）。

  V2.9.3 起是**双目标**构建（同一份源码，两个 TFM）：
    · net10.0-windows —— 主构建，安装包 MinVersion=10.0，面向 Windows 10 / 11；
    · net6.0-windows  —— Win7 兼容构建，MinVersion=6.1sp1，面向 Windows 7 SP1 及以上
      （.NET 6 是最后一个官方支持 Windows 7 SP1 的版本）。
      V3.0 起该构建额外启用**旧协议 obs-websocket 4.x**（Win7 上只能跑 OBS 27 + 4.9 插件），
      并使用独立的数据目录 %LocalAppData%\OBS_Helper_Win7（见 HostBridge.AppDataDirectory）。

  流程：
    1) 自包含发布主构建 / 兼容构建（含 .NET 运行时，目标机无需装运行时）
    2) Inno Setup 生成安装包
         -> PAKE\windows\OBS_Helper_Setup_<ver>.exe            （主构建）
         -> PAKE\windows\OBS_Helper_Setup_<ver>_win7.exe       （兼容构建）
    3) 打便携压缩包（免安装解压即用）
         -> PAKE\windows\OBS_Helper_Portable_<ver>.zip / _win7.zip
    4) 生成增量更新包（仅变更文件，基于主构建清单）-> OBS_Helper_Update_<ver>.zip
    5) 随包知识库 / 插件目录的中英四份资产 -> PAKE\windows\OBS_Helper_Knowledge_<ver>[_en].json
       等（供应用内「raw 主通道失败时的 Release 资产兜底」按语言取用）
    6) 可选：单文件便携 exe（主构建）

  用法：
    .\build.ps1                 # 双目标：安装包 + 便携 zip + 资产
    .\build.ps1 -SkipLegacy     # 只出主构建（快速迭代 / 不关心 Win7 时）
    .\build.ps1 -SingleFile     # 额外产出单文件 exe
    .\build.ps1 -SkipInstaller  # 只出便携包（没装 Inno Setup 时用）
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SingleFile,
    [switch]$SkipInstaller,
    # V2.9.3：跳过 Win7 兼容构建（net6.0-windows）。默认是构建的。
    [switch]$SkipLegacy,
    # 指定增量包的基准版本（如 -DeltaBaseVersion 2.0.0）：强制以该版本清单做 diff，
    # 用于「跳版本发布」——让仍停留在更早版本的用户也能直接增量升级。
    # 不指定时默认取「低于当前版本的最近一份清单」。
    [string]$DeltaBaseVersion = "",
    # ---- 可选：代码签名（V3.0）----
    # 提供 .pfx 与其密码即对 exe / 安装包 / 便携包内主程序签名；不提供则跳过并打印提示。
    # 为什么这一步重要：没有 Authenticode 签名时，客户端只能靠「MZ 头 + GitHub 摘要」判断
    # 下载到的安装包是不是我们的（见 UpdateService 的摘要校验）；有签名才能让 Windows 自己作证。
    [string]$SignPfxPath = "",
    [string]$SignPfxPassword = "",
    [string]$SignTimestampUrl = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"

$root    = $PSScriptRoot
# 脚本放在 NOBS/ 下，工程在 NOBS\OBS_Helper.Wpf\；旧布局（脚本与工程同层）也认。
$projDir = Join-Path $root "OBS_Helper.Wpf"
$proj    = Join-Path $projDir "OBS_Helper.Wpf.csproj"
if (-not (Test-Path $proj)) {
    $projDir = $root
    $proj    = Join-Path $root "OBS_Helper.Wpf.csproj"
}
$pakeWin = Join-Path $root "PAKE\windows"

$tfmModern = "net10.0-windows"
$tfmLegacy = "net6.0-windows"

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

$iscc = $null
foreach ($c in @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)) { if (Test-Path $c) { $iscc = $c; break } }

# ---------------------------------------------------------------- 发布（一个 TFM 一轮）

# ---- 可选：代码签名（V3.0）----
# 为什么需要：没有 Authenticode 签名时，客户端只能靠「MZ 头 + GitHub 资产摘要」判断
# 下载到的安装包是不是我们的；有签名才能让 Windows 自己作证（SmartScreen 与「发布者」一栏）。
# 用法：.\build.ps1 -SignPfxPath D:\cert.pfx -SignPfxPassword ***
function Find-Signtool {
    $kits = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
    if (Test-Path $kits) {
        $st = Get-ChildItem $kits -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -like "*\x64\*" } |
            Sort-Object FullName -Descending | Select-Object -First 1
        if ($st) { return $st.FullName }
    }
    return (Get-Command signtool -ErrorAction SilentlyContinue).Source
}

function Invoke-Sign {
    param([string[]]$Paths)
    if (-not $SignPfxPath -or -not (Test-Path $SignPfxPath) -or -not $SignPfxPassword) {
        Warn "未提供签名证书（-SignPfxPath / -SignPfxPassword），本次产物**未签名**。"
        return
    }
    $signtool = Find-Signtool
    if (-not $signtool) {
        Warn "未找到 signtool.exe（Windows SDK），跳过签名。"
        return
    }
    foreach ($p in $Paths) {
        if (-not $p -or -not (Test-Path $p)) { continue }
        Step "代码签名：$(Split-Path $p -Leaf)"
        & $signtool sign /f "$SignPfxPath" /p "$SignPfxPassword" /fd SHA256 `
            /tr $SignTimestampUrl /td SHA256 "$p" | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "签名失败：$p" }
    }
}

function Publish-Wpf {
    param([string]$Tfm, [switch]$ReadyToRun)
    Step "自包含发布 WPF 工程（$Tfm / $Runtime / $Configuration）"
    $args = @(
        "publish", $proj, "-c", $Configuration, "-r", $Runtime, "--self-contained", "true",
        "-p:PublishSingleFile=false", "-p:TargetFramework=$Tfm"
    )
    # ReadyToRun 对 net6.0 需要 6.0 的 crossgen 包；拿不到就退回非 R2R（只影响启动速度，不影响功能）。
    if ($ReadyToRun) { $args += "-p:PublishReadyToRun=true" }
    else { $args += "-p:PublishReadyToRun=false" }

    & dotnet @args | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败（$Tfm）。" }

    $pubDir = Join-Path $projDir "bin\$Configuration\$Tfm\$Runtime\publish"
    $exe = Join-Path $pubDir "OBS_Helper.exe"
    if (-not (Test-Path $exe)) { throw "发布产物缺少 OBS_Helper.exe：检查 $pubDir" }

    $sizeMb = [math]::Round((Get-ChildItem $pubDir -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 1)
    Write-Host "发布目录：$pubDir（$sizeMb MB）" -ForegroundColor DarkGray
    return $pubDir
}

# ---------------------------------------------------------------- 安装包 / 便携包

function New-Installer {
    param([string]$Tfm, [string]$MinVersion, [string]$Suffix)
    $setupPath = Join-Path $pakeWin "OBS_Helper_Setup_$ver$Suffix.exe"

    if ($SkipInstaller) {
        Warn "已指定 -SkipInstaller，跳过 Inno Setup。"
        return $null
    }
    if (-not $iscc) {
        # 没装 Inno 不该让整个构建失败——便携包仍然是可交付的产物
        Warn "找不到 Inno Setup 6（ISCC.exe），跳过安装包。下载：https://jrsoftware.org/isdl.php"
        return $null
    }

    Step "Inno Setup 生成安装包（$Tfm，MinVersion=$MinVersion）"
    & $iscc "/DMyAppVersion=$ver" "/DMyAppTfm=$Tfm" "/DMyAppMinVersion=$MinVersion" `
        "/DMyAppOutputSuffix=$Suffix" (Join-Path $projDir "OBS_Helper_Setup.iss") | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup 构建失败（$Tfm）。" }
    return $setupPath
}

function New-PortableZip {
    param([string]$PubDir, [string]$Suffix)
    $zip = Join-Path $pakeWin "OBS_Helper_Portable_$ver$Suffix.zip"
    Remove-Artifact $zip

    # V3.0（F10）：便携包排除调试符号与自检产物（与 .iss 的 [Files] 口径一致）。
    # 用 robocopy 而不是逐个文件传给 Compress-Archive：后者会把文件拍平到压缩包根目录，
    # 丢掉 en-US / zh-Hans 这类子目录（卫星资源程序集就在里面）。
    $stage = Join-Path ([System.IO.Path]::GetTempPath()) ("obshelper_portable_" + [guid]::NewGuid().ToString('N'))
    try {
        robocopy $PubDir $stage /E /XF *.pdb selftest_result.txt /NFL /NDL /NJH /NJS /NP | Out-Null
        # robocopy 约定：0-7 都算成功，>=8 才是失败
        if ($LASTEXITCODE -ge 8) { throw "robocopy 暂存失败（exit $LASTEXITCODE）" }
        Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip
    } finally {
        Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
    }

    Write-Host "便携包：$zip" -ForegroundColor DarkGray
    return $zip
}

# ---------------------------------------------------------------- 1) 主构建

$pub = Publish-Wpf -Tfm $tfmModern -ReadyToRun
# 签名必须在打包**之前**：便携 zip 与安装包都要装进已签名的 exe
Invoke-Sign @((Join-Path $pub "OBS_Helper.exe"))
$setupPath = New-Installer -Tfm $tfmModern -MinVersion "10.0" -Suffix ""
Invoke-Sign @($setupPath)

# ---------------------------------------------------------------- 2) Win7 兼容构建

$setupLegacy = $null
$zipLegacy = $null
if (-not $SkipLegacy) {
    $pubLegacy = Publish-Wpf -Tfm $tfmLegacy -ReadyToRun
    Invoke-Sign @((Join-Path $pubLegacy "OBS_Helper.exe"))
    $setupLegacy = New-Installer -Tfm $tfmLegacy -MinVersion "6.1sp1" -Suffix "_win7"
    Invoke-Sign @($setupLegacy)
    $zipLegacy = New-PortableZip -PubDir $pubLegacy -Suffix "_win7"
} else {
    Warn "已指定 -SkipLegacy，跳过 Win7 兼容构建（net6.0-windows）。"
}

# ---------------------------------------------------------------- 3) 便携 zip（主构建）

Step "生成便携压缩包"
$zip = New-PortableZip -PubDir $pub -Suffix ""

# ---------------------------------------------------------------- 4) 增量更新包
#
# 增量更新：对比上一版本完整清单，打包「只含变更文件 + update_manifest.json」的增量 zip，
# 应用内下载后由 --apply-update 自举进程完成替换。清单存档在 PAKE\windows\manifests\ 供下次比对。
# 基准与目标都取**主构建** —— 兼容构建与主构建的运行时文件完全不同，混进同一份清单没有意义。

function New-FileManifest {
    param([string]$Dir, [string]$Version)
    # 排除运行时产物与调试符号：它们不属于发布内容。
    # 「排除规则」必须与安装包（.iss 的 Excludes）和便携包（New-PortableZip 的 /XF）**同一口径** ——
    # 否则增量包会把 .pdb 当成「新增文件」反复带上（清单取自已发布的便携包时更明显）。
    $entries = Get-ChildItem $Dir -Recurse -File |
        Where-Object { $_.Name -ne 'selftest_result.txt' -and $_.Extension -ne '.pdb' } |
        ForEach-Object {
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
    # 显式写「无 BOM 的 UTF-8」：PS 5.1 的 Set-Content -Encoding UTF8 会带 BOM，
    # 导致 Python / 严格解析器读 JSON 报错（应用端 ReadAllText 虽能容忍，但产物不干净）。
    $json = $Manifest | ConvertTo-Json -Depth 5
    [System.IO.File]::WriteAllText($Path, $json, (New-Object System.Text.UTF8Encoding($false)))
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

# 找「低于当前版本」的上一版清单（取版本号最高的一份）；
# 指定 -DeltaBaseVersion 时强制用该版本（跳版本发布场景）。
$verObj = [version]$ver
$prevManifestFile = $null
if ($DeltaBaseVersion) {
    $prevManifestFile = Get-ChildItem $manifestDir -Filter "manifest_$DeltaBaseVersion.json" |
        Where-Object { (Get-ManifestVersion $_.BaseName) -lt $verObj } |
        Select-Object -First 1
    if ($prevManifestFile) {
        Write-Host "按 -DeltaBaseVersion $DeltaBaseVersion 指定基准清单" -ForegroundColor DarkGray
    } else {
        Warn "找不到指定基准清单 manifest_$DeltaBaseVersion.json，回退默认（最近一份）。"
    }
}
if (-not $prevManifestFile) {
    $prevManifestFile = Get-ChildItem $manifestDir -Filter "manifest_*.json" |
        Where-Object { $v = Get-ManifestVersion $_.BaseName; $v -ne $null -and $v -lt $verObj } |
        Sort-Object { Get-ManifestVersion $_.BaseName } | Select-Object -Last 1
}

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

# ---------------------------------------------------------------- 5) 知识库 / 插件目录资产（中英各一份）
#
# 应用内的知识库热更新是「raw 主通道 → Release 资产兜底」两级。V2.9.3 把内容做成了中英并列，
# 兜底这一级也必须是**四份**（中文 / 英文 × 知识库 / 插件目录），否则英文用户拿不到兜底内容。
# 资产名带上版本号：应用按「版本号最高且带对应语言资产」的 Release 取，不会串语言。

Step "导出知识库 / 插件目录资产（中英）"
$assetsSrc = Join-Path $projDir "Assets"
$assetMap = @(
    @{ Src = "problems.json";             Dst = "OBS_Helper_Knowledge_$ver.json" },
    @{ Src = "problems.en-US.json";       Dst = "OBS_Helper_Knowledge_$ver.en-US.json" },
    @{ Src = "plugins.json";              Dst = "OBS_Helper_Plugins_$ver.json" },
    @{ Src = "plugins.en-US.json";        Dst = "OBS_Helper_Plugins_$ver.en-US.json" }
)
$assetOut = @()
foreach ($a in $assetMap) {
    $src = Join-Path $assetsSrc $a.Src
    if (-not (Test-Path $src)) { Warn "缺少资产源文件，跳过：$src"; continue }
    $dst = Join-Path $pakeWin $a.Dst
    Copy-Item $src $dst -Force
    $assetOut += $dst
    Write-Host "资产：$dst" -ForegroundColor DarkGray
}

# ---------------------------------------------------------------- 6) 单文件（主构建）

$sfOut = $null
if ($SingleFile) {
    Step "生成单文件便携 exe"
    # 单独发到 publish-single，避免和上面的多文件产物混在同一目录
    $sfDir = Join-Path $projDir "bin\$Configuration\$tfmModern\$Runtime\publish-single"
    dotnet publish $proj -c $Configuration -r $Runtime --self-contained true `
        -p:TargetFramework=$tfmModern `
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
if ($setupPath -and (Test-Path $setupPath)) { Write-Host "  - 安装包（Win10+）   : $setupPath" }
if ($setupLegacy -and (Test-Path $setupLegacy)) { Write-Host "  - 安装包（Win7 SP1+）: $setupLegacy" }
Write-Host "  - 便携包             : $zip"
if ($zipLegacy -and (Test-Path $zipLegacy)) { Write-Host "  - 便携包（Win7）     : $zipLegacy" }
if (Test-Path $deltaZip) { Write-Host "  - 增量包             : $deltaZip" }
if ($sfOut) { Write-Host "  - 单文件             : $sfOut" }
foreach ($a in $assetOut) { Write-Host "  - 知识库/插件资产    : $a" }
Get-ChildItem $pakeWin -File | ForEach-Object {
    Write-Host ("    {0}  ({1:N1} MB)" -f $_.Name, ($_.Length / 1MB))
}
