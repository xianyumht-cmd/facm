[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$RequestPath,
    [Parameter(Mandatory=$true)]
    [string]$SignatureRoot,
    [string]$BundleRoot = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-FullPath([string]$Path) { return [IO.Path]::GetFullPath($Path) }
function Assert-DProject2Path([string]$Path, [string]$Label) {
    $full = Get-FullPath $Path
    if (-not $full.StartsWith('D:\project2\', [StringComparison]::OrdinalIgnoreCase) -or $full -eq 'D:\project2') {
        throw "$Label must be a specific path under D:\project2: $full"
    }
    return $full
}
function Get-Sha256Bytes([byte[]]$Bytes) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($Bytes)) -replace '-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}
function Get-SafeRelativePath([string]$Relative, [string]$Label) {
    if ([string]::IsNullOrWhiteSpace($Relative) -or [IO.Path]::IsPathRooted($Relative) -or
        $Relative.Replace('\','/').Split('/') -contains '..') { throw "$Label is not a safe relative path: $Relative" }
    return $Relative.Replace('/', '\')
}
function Write-Utf8NoBom([string]$Path, [string]$Text) {
    [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false))
}

$RequestPath = Assert-DProject2Path $RequestPath 'RequestPath'
$SignatureRoot = Assert-DProject2Path $SignatureRoot 'SignatureRoot'
if (-not (Test-Path -LiteralPath $RequestPath -PathType Leaf)) { throw "Signing request missing: $RequestPath" }
if (-not (Test-Path -LiteralPath $SignatureRoot -PathType Container)) { throw "Signer response directory missing: $SignatureRoot" }
if ([string]::IsNullOrWhiteSpace($BundleRoot)) { $BundleRoot = Join-Path (Split-Path -Parent $RequestPath) 'bundle' }
$BundleRoot = Assert-DProject2Path $BundleRoot 'BundleRoot'
if (-not (Test-Path -LiteralPath $BundleRoot -PathType Container)) { throw "Bundle root missing: $BundleRoot" }

$request = Get-Content -Raw -LiteralPath $RequestPath | ConvertFrom-Json
if ($request.schemaVersion -ne 1 -or $request.requestStatus -ne 'unsigned-external-signer-required' -or
    $request.keyId -notmatch '^facm-production-r[0-9]+$' -or $request.algorithm -ne 'RSA-2048-PKCS1-SHA256' -or
    $request.signatureEncoding -ne 'base64') {
    throw 'Signing request schema, key identity, algorithm, or status is invalid.'
}
$policyPath = Join-Path $PSScriptRoot 'facm-keyring-policy.json'
if (-not (Test-Path -LiteralPath $policyPath -PathType Leaf)) { throw 'Release key policy is missing.' }
$policy = Get-Content -Raw -LiteralPath $policyPath | ConvertFrom-Json
$acceptedKeyIds = @($policy.keys | Where-Object { $_.acceptedByCandidateBootstrapper -eq $true } | ForEach-Object { [string]$_.keyId })
if ([string]$request.keyId -notin $acceptedKeyIds) { throw "Signing request key ID is not accepted by the candidate policy: $($request.keyId)" }
$indexPath = Join-Path $BundleRoot ([string]$request.releaseIndexPath -replace '/', '\')
if (-not (Test-Path -LiteralPath $indexPath -PathType Leaf)) { throw 'Signing request release index is missing.' }
$indexBytes = [IO.File]::ReadAllBytes($indexPath)
if ((Get-Sha256Bytes $indexBytes) -ne [string]$request.releaseIndexSha256.ToLowerInvariant() -or
    $indexBytes.Length -ne [int64]$request.releaseIndexBytes) { throw 'Release index changed since signing request generation.' }

$count = 0
foreach ($item in @($request.requests)) {
    $payloadRelative = Get-SafeRelativePath ([string]$item.payloadPath) 'Payload path'
    $signatureRelative = Get-SafeRelativePath ([string]$item.signaturePath) 'Signature path'
    if ([string]$item.keyId -ne [string]$request.keyId -or [string]$item.algorithm -ne [string]$request.algorithm) {
        throw "Signing request item does not use the request key/algorithm: $($item.logicalName)"
    }
    $payloadPath = Join-Path $BundleRoot $payloadRelative
    if (-not (Test-Path -LiteralPath $payloadPath -PathType Leaf)) { throw "Signing payload missing: $payloadRelative" }
    $payload = [IO.File]::ReadAllBytes($payloadPath)
    $actualSha = Get-Sha256Bytes $payload
    if ($actualSha -ne [string]$item.payloadSha256.ToLowerInvariant() -or $payload.Length -ne [int64]$item.payloadBytes) {
        throw "Signing payload changed since request generation: $payloadRelative"
    }
    $responsePath = Join-Path $SignatureRoot $signatureRelative
    if (-not (Test-Path -LiteralPath $responsePath -PathType Leaf)) { throw "Signer response missing: $signatureRelative" }
    $encoded = (Get-Content -Raw -LiteralPath $responsePath).Trim()
    try { $signature = [Convert]::FromBase64String($encoded) } catch { throw "Signer response is not valid Base64: $signatureRelative" }
    if ($signature.Length -ne 256) { throw "Signer response is not an RSA-2048 signature: $signatureRelative" }
    $destination = Join-Path $BundleRoot $signatureRelative
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
    Write-Utf8NoBom $destination ($encoded + "`n")
    $count++
}

Write-Host "Applied $count detached signer responses without accessing a private key."
