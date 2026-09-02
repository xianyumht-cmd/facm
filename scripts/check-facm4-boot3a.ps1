param([string]$Root = (Split-Path -Parent $PSScriptRoot))

$ErrorActionPreference = 'Stop'

function Read-Required([string]$RelativePath) {
    $path = Join-Path $Root $RelativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "BOOT3-A contract file missing: $RelativePath" }
    return Get-Content -LiteralPath $path -Raw
}

function Require([string]$Text, [string]$Pattern, [string]$Message) {
    if ($Text -notmatch $Pattern) { throw $Message }
}

$cmake = Read-Required 'src/FACM.Bootstrapper/CMakeLists.txt'
$native = Read-Required 'src/FACM.Bootstrapper/main.cpp'
$trust = Read-Required 'src/FACM.Bootstrapper/ManifestTrust.cpp'
$trustHeader = Read-Required 'src/FACM.Bootstrapper/ManifestTrust.h'
$test = Read-Required 'tools/boot1/Test-Boot3A.ps1'
$docs = Read-Required 'docs/BOOT3A-TRUST.md'

Require $cmake 'ManifestTrust\.cpp' 'BOOT3-A native target must compile the bootstrapper-local trust module.'
Require $cmake 'bcrypt' 'BOOT3-A native target must link Windows CNG.'
Require $cmake 'crypt32' 'BOOT3-A native target must link native Base64 decoding support.'
foreach ($marker in @(
    'VerifyProductionSignature', 'BCRYPT_RSAPUBLIC_BLOB', 'BCRYPT_PAD_PKCS1', 'BCRYPT_SHA256_ALGORITHM',
    'facm-production-r1', 'keyId', 'trustMode', 'production', 'unsigned-local', 'componentManifestUrl',
    'componentManifestSha256', 'DetachedSignatureUrl', 'VerifyProductionApplicationManifest',
    'VerifyProductionComponentManifest', '--verify-trust-bundle', 'DirectoryDigest(stagingDirectory)',
    'authenticated metadata', 'production ? IsHttpsUrl(url)'
)) {
    Require ($native + $trust + $trustHeader) ([regex]::Escape($marker)) "BOOT3-A native trust boundary missing: $marker"
}
foreach ($forbidden in @('-----BEGIN [A-Z ]*PRIVATE KEY-----', '\.pfx', 'FACM_PFX', 'allowSignatureBypass', 'skipSignature', 'ignoreSignature')) {
    if ($trust -match $forbidden) { throw "BOOT3-A trust module contains forbidden private-key or signature-bypass material: $forbidden" }
}
foreach ($marker in @(
    'ValidSignedTrustBundle', 'AlteredApplicationManifestBytes', 'AlteredComponentManifestBytes',
    'InvalidSignature', 'UnknownKeyIdentityAndTestOnlyKeyRejection',
    'UnsignedManifestRejectedInProductionVerification', 'UnsignedLocalDowngradeRejected',
    'AlteredAuthenticatedComponentMetadata', 'CorruptedPackageHash',
    'PreviousActiveCompositionRemainsLaunchable', 'facm-test-only-r1',
    'D:\project2\facm-boot3a-signing'
)) {
    Require $test ([regex]::Escape($marker)) "BOOT3-A focused smoke missing: $marker"
}
Require $test 'RSASignaturePadding\]::Pkcs1' 'BOOT3-A fixture signer must use the documented RSA-PKCS1-SHA256 format.'
Require $docs 'Existing signing infrastructure audit' 'BOOT3-A trust contract must record the repository signing audit.'
Require $docs 'unsigned-local' 'BOOT3-A trust contract must document the local development boundary.'
Require $docs 'Gate13' 'BOOT3-A trust contract must preserve the Gate13 boundary.'

Write-Host 'BOOT3-A native CNG trust boundary, exact-byte signatures, local isolation, and negative-path coverage: SUCCESS'
