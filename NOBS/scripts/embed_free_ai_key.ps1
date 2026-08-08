# ============================================================
# 内置免费 AI 密钥打包脚本（智谱 GLM-4.7-Flash 通道）
#
# 用途：把智谱开放平台的 API Key 加密后写入
#   OBS_Helper.Wpf\Assets\free_ai_key.json（已 gitignore，绝不进仓库），
#   该文件随构建打进安装包 / 便携包的内嵌资源，用户开箱即用。
#
# 加密参数与运行时 OBS_Helper.Wpf\Services\Ai\FreeAiKeyProvider.cs 严格一致，
# 修改任一侧必须同步另一侧，否则运行时解不开（免费通道显示「未内置」）。
#
# 算法（PowerShell 5.1 / .NET Framework 与 .NET 10 两端一致）：
#   PBKDF2-SHA256(pepper, fixedSalt+perBuildSalt, 200000) -> 64 字节
#   前 32 字节做 AES-256-CBC（随机 IV），后 32 字节做 HMAC-SHA256（encrypt-then-MAC）
#   输出 JSON：{ v:2, d:base64(密文), i:base64(IV), m:base64(HMAC), x:base64(perBuildSalt) }
#
# 用法（任选其一）：
#   .\scripts\embed_free_ai_key.ps1 -ApiKey "你的智谱Key"
#   $env:ZHIPU_API_KEY = "..." ; .\scripts\embed_free_ai_key.ps1
#   .\scripts\embed_free_ai_key.ps1 -KeyFile ".\local\free_ai_key.txt"
#   （默认还会尝试读 local\free_ai_key.txt）
#
# 换 Key / 被滥用后重新生成：改 Key 再跑一次本脚本，然后重新 build.ps1 出包。
# ============================================================
param(
    [string]$ApiKey = "",
    [string]$KeyFile = ""
)

$ErrorActionPreference = 'Stop'

if (-not $ApiKey -and $KeyFile) {
    $ApiKey = (Get-Content $KeyFile -Raw -Encoding UTF8).Trim()
}
if (-not $ApiKey) { $ApiKey = $env:ZHIPU_API_KEY }
if (-not $ApiKey -and (Test-Path (Join-Path (Split-Path $PSScriptRoot -Parent) 'local\free_ai_key.txt'))) {
    $ApiKey = (Get-Content (Join-Path (Split-Path $PSScriptRoot -Parent) 'local\free_ai_key.txt') -Raw -Encoding UTF8).Trim()
}
if (-not $ApiKey) {
    throw "未提供智谱 API Key：用 -ApiKey 参数、-KeyFile 参数、ZHIPU_API_KEY 环境变量，或 local\free_ai_key.txt"
}

# ---- 派生参数（必须与 FreeAiKeyProvider 一致）----
$pepper     = [System.Text.Encoding]::UTF8.GetBytes('OBS_Helper.freeai.' + 'glm-4.7-flash.2026.zp')
$fixedSalt  = [System.Text.Encoding]::UTF8.GetBytes('obs-helper-freeai-v1')
$rng        = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$perBuild   = New-Object byte[] 16
$rng.GetBytes($perBuild)
$salt       = $fixedSalt + $perBuild
$iterations = 200000

$derive = [System.Security.Cryptography.Rfc2898DeriveBytes]::new(
    $pepper, $salt, $iterations,
    [System.Security.Cryptography.HashAlgorithmName]::SHA256)
$key    = $derive.GetBytes(64)
$derive.Dispose()

$keyAes = New-Object byte[] 32
$keyMac = New-Object byte[] 32
[System.Array]::Copy($key, 0, $keyAes, 0, 32)
[System.Array]::Copy($key, 32, $keyMac, 0, 32)
[System.Array]::Clear($key, 0, 64)

$iv = New-Object byte[] 16
$rng.GetBytes($iv)

$plain = [System.Text.Encoding]::UTF8.GetBytes($ApiKey)
$aes   = [System.Security.Cryptography.Aes]::Create()
$aes.KeySize = 256
$aes.BlockSize = 128
$aes.Mode = [System.Security.Cryptography.CipherMode]::CBC
$aes.Padding = [System.Security.Cryptography.PaddingMode]::PKCS7
$aes.Key = $keyAes
$aes.IV  = $iv
try {
    $enc = $aes.CreateEncryptor()
    $cipher = $enc.TransformFinalBlock($plain, 0, $plain.Length)
}
finally { $aes.Dispose() }

$hmac = ([System.Security.Cryptography.HMACSHA256]::new($keyMac)).ComputeHash($iv + $cipher)
[System.Array]::Clear($keyAes, 0, 32)
[System.Array]::Clear($keyMac, 0, 32)

$json = @{
    v = 2
    d = [Convert]::ToBase64String($cipher)
    i = [Convert]::ToBase64String($iv)
    m = [Convert]::ToBase64String($hmac)
    x = [Convert]::ToBase64String($perBuild)
} | ConvertTo-Json -Compress

$root = Split-Path $PSScriptRoot -Parent
$out  = Join-Path $root 'OBS_Helper.Wpf\Assets\free_ai_key.json'
[System.IO.File]::WriteAllText($out, $json, [System.Text.UTF8Encoding]::new($false))

# ---- 自校验：按同一套参数解回来比对 ----
$backKey = $null
$backKeyAes = $null
try {
    $backDerive = [System.Security.Cryptography.Rfc2898DeriveBytes]::new(
        $pepper, $salt, $iterations,
        [System.Security.Cryptography.HashAlgorithmName]::SHA256)
    $backKey = $backDerive.GetBytes(64)
    $backDerive.Dispose()

    $backKeyAes = New-Object byte[] 32
    $backKeyMac = New-Object byte[] 32
    [System.Array]::Copy($backKey, 0, $backKeyAes, 0, 32)
    [System.Array]::Copy($backKey, 32, $backKeyMac, 0, 32)

    $backHmac = ([System.Security.Cryptography.HMACSHA256]::new($backKeyMac)).ComputeHash($iv + $cipher)
    if (-not [System.Linq.Enumerable]::SequenceEqual($hmac, $backHmac)) {
        throw "自校验失败：HMAC 不一致！"
    }

    $backAes = [System.Security.Cryptography.Aes]::Create()
    $backAes.KeySize = 256
    $backAes.BlockSize = 128
    $backAes.Mode = [System.Security.Cryptography.CipherMode]::CBC
    $backAes.Padding = [System.Security.Cryptography.PaddingMode]::PKCS7
    $backAes.Key = $backKeyAes
    $backAes.IV  = $iv
    try {
        $dec = $backAes.CreateDecryptor()
        $backPlain = $dec.TransformFinalBlock($cipher, 0, $cipher.Length)
    }
    finally { $backAes.Dispose() }

    $decoded = [System.Text.Encoding]::UTF8.GetString($backPlain)
    if ($decoded -ne $ApiKey) {
        throw "自校验失败：解密结果与原始 Key 不一致！"
    }
}
finally {
    if ($backKey)      { [System.Array]::Clear($backKey, 0, $backKey.Length) }
    if ($backKeyAes)   { [System.Array]::Clear($backKeyAes, 0, $backKeyAes.Length) }
}

Write-Host "OK  已写入 $out（自校验通过；free_ai_key.json 在 .gitignore 中，绝不入库）"
Write-Host "    接下来运行 .\build.ps1 重新出包，即可把密钥打进安装包 / 便携包。"
