[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'local-signing'),
    [string]$Subject = 'CN=FACM Self-Signed Code Signing, O=FACM, OU=Development',
    [int]$ValidYears = 5,
    [string]$PfxPassword = '',
    [switch]$KeepInCertificateStore
)

$ErrorActionPreference = 'Stop'

if ($ValidYears -lt 1 -or $ValidYears -gt 10) {
    throw 'ValidYears 必须在 1 到 10 之间。'
}

if ([string]::IsNullOrWhiteSpace($PfxPassword)) {
    $randomBytes = New-Object byte[] 24
    [Security.Cryptography.RandomNumberGenerator]::Fill($randomBytes)
    $PfxPassword = [Convert]::ToBase64String($randomBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$OutputDirectory = (Resolve-Path $OutputDirectory).Path

$pfxPath = Join-Path $OutputDirectory 'FACM-SelfSigned-CodeSigning.pfx'
$cerPath = Join-Path $OutputDirectory 'FACM-SelfSigned-CodeSigning.cer'
$base64Path = Join-Path $OutputDirectory 'FACM_PFX_BASE64.txt'
$passwordPath = Join-Path $OutputDirectory 'FACM_PFX_PASSWORD.txt'
$readmePath = Join-Path $OutputDirectory 'README.txt'

$certificate = New-SelfSignedCertificate `
    -Type CodeSigningCert `
    -Subject $Subject `
    -CertStoreLocation 'Cert:\CurrentUser\My' `
    -KeyAlgorithm RSA `
    -KeyLength 3072 `
    -HashAlgorithm SHA256 `
    -KeyExportPolicy Exportable `
    -NotAfter (Get-Date).AddYears($ValidYears)

try {
    $securePassword = ConvertTo-SecureString -String $PfxPassword -AsPlainText -Force
    Export-PfxCertificate -Cert $certificate -FilePath $pfxPath -Password $securePassword -CryptoAlgorithmOption AES256_SHA256 | Out-Null
    Export-Certificate -Cert $certificate -FilePath $cerPath -Type CERT | Out-Null

    [Convert]::ToBase64String([IO.File]::ReadAllBytes($pfxPath)) |
        Set-Content -LiteralPath $base64Path -Encoding ascii -NoNewline
    $PfxPassword | Set-Content -LiteralPath $passwordPath -Encoding ascii -NoNewline

    @"
FACM 自签名代码签名证书（仅用于开发与测试）

PFX：$pfxPath
公钥证书：$cerPath
PFX 密码：$passwordPath
GitHub Secret FACM_PFX_BASE64：复制 $base64Path 的完整内容
GitHub Secret FACM_PFX_PASSWORD：复制 $passwordPath 的完整内容

证书主题：$($certificate.Subject)
指纹：$($certificate.Thumbprint)
生效时间：$($certificate.NotBefore.ToString('o'))
到期时间：$($certificate.NotAfter.ToString('o'))

不要把 PFX、密码或 Base64 文本提交到公开仓库。
自签名证书通常不会建立 Windows SmartScreen 信誉。
"@ | Set-Content -LiteralPath $readmePath -Encoding utf8

    Write-Host "自签名代码签名证书已生成：$pfxPath"
    Write-Host "GitHub Secrets 文件已生成：$base64Path / $passwordPath"
}
finally {
    if (-not $KeepInCertificateStore -and $certificate) {
        Remove-Item -LiteralPath ("Cert:\CurrentUser\My\" + $certificate.Thumbprint) -Force -ErrorAction SilentlyContinue
    }
}
