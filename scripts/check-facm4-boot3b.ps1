param([string]$Root = (Split-Path -Parent $PSScriptRoot))

$ErrorActionPreference = 'Stop'

function Read-Required([string]$RelativePath) {
    $path = Join-Path $Root $RelativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "BOOT3-B contract file missing: $RelativePath" }
    return Get-Content -LiteralPath $path -Raw
}
function Require([string]$Text, [string]$Pattern, [string]$Message) {
    if ($Text -notmatch $Pattern) { throw $Message }
}

$keyGovernance = Read-Required 'docs/BOOT3B-KEY-GOVERNANCE.md'
$artifactDocs = Read-Required 'docs/BOOT3B-SIGNED-ARTIFACTS.md'
$policy = Read-Required 'tools/release/facm-keyring-policy.json'
$nativeHeader = Read-Required 'src/FACM.Bootstrapper/ManifestTrust.h'
$nativeTrust = Read-Required 'src/FACM.Bootstrapper/ManifestTrust.cpp'
$nativeMain = Read-Required 'src/FACM.Bootstrapper/main.cpp'
$builder = Read-Required 'tools/release/Build-FacmBoot3BRelease.ps1'
$apply = Read-Required 'tools/release/Apply-FacmSigningResponses.ps1'
$validator = Read-Required 'tools/release/Test-FacmReleaseBundle.ps1'
$tests = Read-Required 'tools/release/Test-Boot3BRelease.ps1'

foreach ($marker in @(
    'ProductionKeyStatus', 'Active', 'Overlap', 'Planned', 'Retired', 'Revoked',
    'kProductionKeyring', 'IsProductionKeyAccepted', 'compiled.*keyring'
)) {
    Require ($nativeHeader + $nativeTrust) $marker "BOOT3-B native key lifecycle marker missing: $marker"
}
foreach ($marker in @('CompareReleaseVersions', 'manifest-downgrade-rejected')) {
    Require $nativeMain $marker "BOOT3-B downgrade guard marker missing: $marker"
}
foreach ($marker in @(
    'SkipBoot2Build', 'Build-Boot2Candidate', 'component-stages', 'facm-app-win-x64',
    'facm-dotnet-runtime-win-x64', 'facm-windows-runtime-win-x64', 'component.manifest.json',
    'release-index.json', 'signing-request.json', 'RSA-2048-PKCS1-SHA256',
    'Get-DirectoryDigest', 'sourceCommit', 'external-signer-required', 'defaultComposition',
    'MirrorBaseUrls', 'D:\project2'
)) {
    Require $builder ([regex]::Escape($marker)) "BOOT3-B release builder marker missing: $marker"
}
foreach ($marker in @('releaseIndexSha256', 'payloadSha256', 'payloadBytes', 'signaturePath', 'FromBase64String', 'never opens a private key')) {
    Require ($apply + $artifactDocs) ([regex]::Escape($marker)) "BOOT3-B external signer boundary marker missing: $marker"
}
foreach ($marker in @(
    'Assert-NoSecretMaterial', 'Invoke-NativeTrustValidation', 'unsigned-local', 'Assert-Https',
    'defaultComposition', 'componentOwnership', 'CurrentActiveVersion', 'contentDigest',
    'Desktop Pet', 'FACM BOOT3-B release bundle validator'
)) {
    Require ($validator + $artifactDocs) ([regex]::Escape($marker)) "BOOT3-B validator marker missing: $marker"
}
foreach ($marker in @(
    'ExternalSigningRequestHasNoPrivateMaterial', 'DeterministicArtifactGeneration',
    'SignatureChangesWithSignedBytes', 'ComponentSignatureReplayRejected', 'UnknownFutureKeyRejected',
    'PlannedRotationKeyRejected', 'TestOnlyKeyRejectedByProductionPolicy',
    'UnsignedReleaseCannotBeMistakenAsProduction', 'DowngradeRejected',
    'SignedAuthenticatedMetadataMismatchRejected', 'CorruptedPackageHashRejected'
)) {
    Require $tests ([regex]::Escape($marker)) "BOOT3-B focused test marker missing: $marker"
}
foreach ($forbidden in @('-----BEGIN [A-Z ]*PRIVATE KEY-----', 'FACM_PFX', 'allowSignatureBypass', 'skipSignature', 'ignoreSignature')) {
    if (($nativeTrust + $builder + $apply + $validator) -match $forbidden) { throw "BOOT3-B implementation contains forbidden secret/bypass material: $forbidden" }
}
Require $policy 'release-tooling-review-metadata-only' 'Key policy must be tooling/review metadata only.'
Require $policy 'formalProductionCredential' 'Key policy must distinguish candidate validation from formal production credential.'
Require $keyGovernance 'external signer|signing boundary' 'Key governance must define the external signer boundary.'
Require $keyGovernance 'overlap' 'Key governance must define overlap rotation.'
Require $keyGovernance 'revoked' 'Key governance must define revocation.'
Require $artifactDocs 'exact relative bundle path' 'Artifact docs must define exact signing-request payload paths.'
Require $artifactDocs 'read-only with respect to' 'Artifact docs must define validator state safety.'

Write-Host 'FACM 4.0 BOOT3-B key governance, deterministic artifact pipeline, external signer boundary, and offline validator contract: SUCCESS'
