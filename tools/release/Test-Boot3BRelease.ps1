[CmdletBinding()]
param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
    [string]$Bootstrapper = 'D:\project2\facm-boot3a-native-build\FACM.exe',
    [string]$TestRoot = 'D:\project2\facm-boot3b-tests-20260831',
    [string]$LocalValidationKeyPath = 'D:\project2\facm-boot3a-signing\production-r1\production-r1.pk8.pem'
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
function Remove-Scope([string]$Path) {
    $full = Assert-DProject2Path $Path 'TestRoot'
    if (Test-Path -LiteralPath $full) { Remove-Item -LiteralPath $full -Recurse -Force }
}
function Convert-ToUnixRelative([string]$Path) { return $Path.Replace('\','/') }
function Get-Sha256([string]$Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Get-Sha256Bytes([byte[]]$Bytes) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($Bytes)) -replace '-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}
function Write-ExactJson([string]$Path, [object]$Value) {
    $json = $Value | ConvertTo-Json -Depth 20
    [IO.File]::WriteAllBytes($Path, [Text.UTF8Encoding]::new($false).GetBytes($json + "`n"))
}
function Get-SafeRelativePath([string]$Relative) {
    $normalized = $Relative.Replace('\','/')
    if ([string]::IsNullOrWhiteSpace($normalized) -or [IO.Path]::IsPathRooted($normalized) -or $normalized.Split('/') -contains '..') {
        throw "Unsafe relative path: $Relative"
    }
    return $normalized.Replace('/', '\')
}
function Copy-Bundle([string]$Source, [string]$Destination) {
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Copy-Item -Path (Join-Path $Source '*') -Destination $Destination -Recurse -Force
}
function Compare-Trees([string]$Left, [string]$Right) {
    $leftFiles = @(Get-ChildItem -LiteralPath $Left -Recurse -File | ForEach-Object { Convert-ToUnixRelative ([IO.Path]::GetRelativePath($Left, $_.FullName)) } | Sort-Object)
    $rightFiles = @(Get-ChildItem -LiteralPath $Right -Recurse -File | ForEach-Object { Convert-ToUnixRelative ([IO.Path]::GetRelativePath($Right, $_.FullName)) } | Sort-Object)
    if (($leftFiles -join "`n") -cne ($rightFiles -join "`n")) { throw 'Deterministic bundle file sets differ.' }
    foreach ($relative in $leftFiles) {
        $leftPath = Join-Path $Left ($relative -replace '/', '\')
        $rightPath = Join-Path $Right ($relative -replace '/', '\')
        if ((Get-Item -LiteralPath $leftPath).Length -ne (Get-Item -LiteralPath $rightPath).Length -or (Get-Sha256 $leftPath) -ne (Get-Sha256 $rightPath)) {
            throw "Deterministic bundle bytes differ: $relative"
        }
    }
}
function Invoke-Tool([string]$Script, [string[]]$Arguments) {
    $pwsh = (Get-Command pwsh -ErrorAction Stop).Source
    & $pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File $Script @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Tool failed ($LASTEXITCODE): $Script" }
}
function Invoke-Validator([string]$Bundle, [string]$CurrentActiveVersion = '') {
    $validator = Join-Path $PSScriptRoot 'Test-FacmReleaseBundle.ps1'
    $pwsh = (Get-Command pwsh -ErrorAction Stop).Source
    $arguments = @('-NoLogo','-NoProfile','-ExecutionPolicy','Bypass','-File',$validator,'-BundleRoot',$Bundle,'-Bootstrapper',$Bootstrapper)
    if (-not [string]::IsNullOrWhiteSpace($CurrentActiveVersion)) { $arguments += @('-CurrentActiveVersion',$CurrentActiveVersion) }
    & $pwsh @arguments | Out-Null
    return $LASTEXITCODE
}
function Assert-ValidatorPass([string]$Name, [string]$Bundle, [string]$CurrentActiveVersion = '') {
    $exitCode = Invoke-Validator $Bundle $CurrentActiveVersion
    if ($exitCode -ne 0) { throw "$Name expected validator success, got $exitCode." }
    Write-Host "${Name}: PASS"
}
function Assert-ValidatorFail([string]$Name, [string]$Bundle, [string]$CurrentActiveVersion = '') {
    $exitCode = Invoke-Validator $Bundle $CurrentActiveVersion
    if ($exitCode -eq 0) { throw "$Name unexpectedly passed validator." }
    Write-Host "${Name}: PASS"
}
function Open-Rsa([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Local validation key missing: $Path" }
    $rsa = [Security.Cryptography.RSA]::Create()
    $rsa.ImportFromPem([IO.File]::ReadAllText($Path))
    return $rsa
}
function New-Rsa {
    return [Security.Cryptography.RSA]::Create(2048)
}
function Write-Utf8NoBom([string]$Path, [string]$Text) {
    [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false))
}
function Create-SignerResponses([string]$RequestPath, [string]$BundleRoot, [string]$ResponseRoot, [Security.Cryptography.RSA]$Rsa) {
    $request = Get-Content -Raw -LiteralPath $RequestPath | ConvertFrom-Json
    New-Item -ItemType Directory -Force -Path $ResponseRoot | Out-Null
    foreach ($item in @($request.requests)) {
        $payloadPath = Join-Path $BundleRoot (Get-SafeRelativePath ([string]$item.payloadPath))
        $bytes = [IO.File]::ReadAllBytes($payloadPath)
        $digest = Get-Sha256Bytes $bytes
        if ($digest -ne [string]$item.payloadSha256.ToLowerInvariant() -or $bytes.Length -ne [int64]$item.payloadBytes) { throw "Signing request payload changed: $($item.payloadPath)" }
        $signature = $Rsa.SignData($bytes, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
        $responsePath = Join-Path $ResponseRoot (Get-SafeRelativePath ([string]$item.signaturePath))
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $responsePath) | Out-Null
        Write-Utf8NoBom $responsePath ([Convert]::ToBase64String($signature) + "`n")
    }
}
function Refresh-IndexApplicationDigest([string]$Bundle) {
    $indexPath = Join-Path $Bundle 'release-index.json'
    $manifestPath = Join-Path $Bundle 'manifest.json'
    $index = Get-Content -Raw -LiteralPath $indexPath | ConvertFrom-Json
    $index.application.manifestSha256 = Get-Sha256 $manifestPath
    $index.application.manifestBytes = [uint64](Get-Item -LiteralPath $manifestPath).Length
    Write-ExactJson $indexPath $index
}
function Sign-Application([string]$Bundle, [Security.Cryptography.RSA]$Rsa) {
    $path = Join-Path $Bundle 'manifest.json'
    $bytes = [IO.File]::ReadAllBytes($path)
    Write-Utf8NoBom (Join-Path $Bundle 'manifest.json.sig') ([Convert]::ToBase64String($Rsa.SignData($bytes, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)) + "`n")
}

$RepoRoot = (Resolve-Path $RepoRoot).Path
$Bootstrapper = Assert-DProject2Path $Bootstrapper 'Bootstrapper'
$TestRoot = Assert-DProject2Path $TestRoot 'TestRoot'
Assert-DProject2Path $LocalValidationKeyPath 'LocalValidationKeyPath' | Out-Null
if (-not (Test-Path -LiteralPath $Bootstrapper -PathType Leaf)) { throw "Bootstrapper missing: $Bootstrapper" }

Remove-Scope $TestRoot
New-Item -ItemType Directory -Force -Path $TestRoot | Out-Null
$outputA = Join-Path $TestRoot 'release-a'
$outputB = Join-Path $TestRoot 'release-b'
$version = '4.0.0-boot3b'
$buildScript = Join-Path $RepoRoot 'tools\release\Build-FacmBoot3BRelease.ps1'
$applyScript = Join-Path $RepoRoot 'tools\release\Apply-FacmSigningResponses.ps1'

Invoke-Tool $buildScript @('-RepoRoot',$RepoRoot,'-OutputRoot',$outputA,'-Version',$version,'-ManifestBaseUrl','https://updates.facm.example/facm/4.0.0-boot3b','-MirrorBaseUrls','https://cdn.facm.example/facm/4.0.0-boot3b')
$requestPath = Join-Path $outputA 'signing-request.json'
$requestText = Get-Content -Raw -LiteralPath $requestPath
if ($requestText -match '(?i)PRIVATE KEY|privateKeyPath|\.pfx|password') { throw 'Signing request contains private-key or secret material.' }
Write-Host 'ExternalSigningRequestHasNoPrivateMaterial: PASS'
Assert-ValidatorFail 'UnsignedReleaseBundleRejected' (Join-Path $outputA 'bundle')

Invoke-Tool $buildScript @('-RepoRoot',$RepoRoot,'-OutputRoot',$outputB,'-Version',$version,'-ManifestBaseUrl','https://updates.facm.example/facm/4.0.0-boot3b','-MirrorBaseUrls','https://cdn.facm.example/facm/4.0.0-boot3b','-SkipBoot2Build','-Boot2MirrorRoot',(Join-Path $outputA 'boot2-mirror'),'-Boot2BuildRoot',(Join-Path $outputA 'boot2-build'))
Compare-Trees (Join-Path $outputA 'bundle') (Join-Path $outputB 'bundle')
Compare-Trees (Join-Path $outputA 'bundle\release-index.json') (Join-Path $outputB 'bundle\release-index.json')
Compare-Trees (Join-Path $outputA 'signing-request.json') (Join-Path $outputB 'signing-request.json')
Write-Host 'DeterministicArtifactGeneration: PASS'

$productionRsa = Open-Rsa $LocalValidationKeyPath
try {
    $responses = Join-Path $TestRoot 'signer-responses'
    Create-SignerResponses $requestPath (Join-Path $outputA 'bundle') $responses $productionRsa
    Invoke-Tool $applyScript @('-RequestPath',$requestPath,'-SignatureRoot',$responses,'-BundleRoot',(Join-Path $outputA 'bundle'))
    Assert-ValidatorPass 'SignedReleaseBundleValidator' (Join-Path $outputA 'bundle')
    Assert-ValidatorPass 'ActiveVersionEqualReleaseAccepted' (Join-Path $outputA 'bundle') $version

    $manifestBytes = [IO.File]::ReadAllBytes((Join-Path $outputA 'bundle\manifest.json'))
    $alteredManifestBytes = $manifestBytes + [byte]0x0A
    $originalSignature = [Convert]::ToBase64String($productionRsa.SignData($manifestBytes, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1))
    $alteredSignature = [Convert]::ToBase64String($productionRsa.SignData($alteredManifestBytes, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1))
    if ($originalSignature -ceq $alteredSignature) { throw 'Signature did not change when signed bytes changed.' }
    Write-Host 'SignatureChangesWithSignedBytes: PASS'

    $case = Join-Path $TestRoot 'altered-application-bytes'; Copy-Bundle (Join-Path $outputA 'bundle') $case
    Add-Content -LiteralPath (Join-Path $case 'manifest.json') -Value ' ' -NoNewline
    Assert-ValidatorFail 'PostSignApplicationModificationRejected' $case

    $case = Join-Path $TestRoot 'altered-component-bytes'; Copy-Bundle (Join-Path $outputA 'bundle') $case
    Add-Content -LiteralPath (Join-Path $case 'components\facm-app-win-x64\4.0.0-boot3b\component.manifest.json') -Value ' ' -NoNewline
    Assert-ValidatorFail 'PostSignComponentModificationRejected' $case

    $case = Join-Path $TestRoot 'replayed-component-signature'; Copy-Bundle (Join-Path $outputA 'bundle') $case
    Copy-Item -LiteralPath (Join-Path $case 'components\facm-app-win-x64\4.0.0-boot3b\component.manifest.json.sig') -Destination (Join-Path $case 'components\facm-dotnet-runtime-win-x64\4.0.0-boot3b\component.manifest.json.sig') -Force
    Assert-ValidatorFail 'ComponentSignatureReplayRejected' $case

    $case = Join-Path $TestRoot 'unknown-future-key'; Copy-Bundle (Join-Path $outputA 'bundle') $case
    $unknown = Get-Content -Raw -LiteralPath (Join-Path $case 'manifest.json') | ConvertFrom-Json
    $unknown.keyId = 'facm-production-r99'; Write-ExactJson (Join-Path $case 'manifest.json') $unknown; Sign-Application $case $productionRsa; Refresh-IndexApplicationDigest $case
    Assert-ValidatorFail 'UnknownFutureKeyRejected' $case

    $case = Join-Path $TestRoot 'planned-rotation-key'; Copy-Bundle (Join-Path $outputA 'bundle') $case
    $planned = Get-Content -Raw -LiteralPath (Join-Path $case 'manifest.json') | ConvertFrom-Json
    $planned.keyId = 'facm-production-r2'; Write-ExactJson (Join-Path $case 'manifest.json') $planned; Sign-Application $case $productionRsa; Refresh-IndexApplicationDigest $case
    Assert-ValidatorFail 'PlannedRotationKeyRejected' $case

    $case = Join-Path $TestRoot 'test-only-key'; Copy-Bundle (Join-Path $outputA 'bundle') $case
    $testRsa = New-Rsa
    try {
        $testOnly = Get-Content -Raw -LiteralPath (Join-Path $case 'manifest.json') | ConvertFrom-Json
        $testOnly.keyId = 'facm-test-only-r1'; Write-ExactJson (Join-Path $case 'manifest.json') $testOnly; Sign-Application $case $testRsa; Refresh-IndexApplicationDigest $case
    } finally { $testRsa.Dispose() }
    Assert-ValidatorFail 'TestOnlyKeyRejectedByProductionPolicy' $case

    $case = Join-Path $TestRoot 'unsigned-release'; Copy-Bundle (Join-Path $outputA 'bundle') $case
    Get-ChildItem -LiteralPath $case -Recurse -Filter '*.sig' -File | Remove-Item -Force
    Assert-ValidatorFail 'UnsignedReleaseCannotBeMistakenAsProduction' $case

    $case = Join-Path $TestRoot 'metadata-mismatch'; Copy-Bundle (Join-Path $outputA 'bundle') $case
    $metadata = Get-Content -Raw -LiteralPath (Join-Path $case 'manifest.json') | ConvertFrom-Json
    $metadata.components[0].contentDigest = ('0' * 64); Write-ExactJson (Join-Path $case 'manifest.json') $metadata; Sign-Application $case $productionRsa; Refresh-IndexApplicationDigest $case
    Assert-ValidatorFail 'SignedAuthenticatedMetadataMismatchRejected' $case

    $case = Join-Path $TestRoot 'corrupted-package'; Copy-Bundle (Join-Path $outputA 'bundle') $case
    $packagePath = Join-Path $case 'components\facm-app-win-x64\4.0.0-boot3b\facm-app-win-x64-4.0.0-boot3b.cab'
    $packageBytes = [IO.File]::ReadAllBytes($packagePath); $packageBytes[0] = $packageBytes[0] -bxor 0xFF; [IO.File]::WriteAllBytes($packagePath,$packageBytes)
    Assert-ValidatorFail 'CorruptedPackageHashRejected' $case

    $case = Join-Path $TestRoot 'downgrade'; Copy-Bundle (Join-Path $outputA 'bundle') $case
    $downgrade = Get-Content -Raw -LiteralPath (Join-Path $case 'manifest.json') | ConvertFrom-Json
    $downgrade.applicationVersion = '3.9.0'; Write-ExactJson (Join-Path $case 'manifest.json') $downgrade; Sign-Application $case $productionRsa; Refresh-IndexApplicationDigest $case
    $index = Get-Content -Raw -LiteralPath (Join-Path $case 'release-index.json') | ConvertFrom-Json
    $index.releaseVersion = '3.9.0'; Write-ExactJson (Join-Path $case 'release-index.json') $index
    Assert-ValidatorFail 'DowngradeRejected' $case $version

    Write-Host 'BOOT3-B release-signing request, deterministic artifact, validator, rotation, and rejection tests: SUCCESS'
} finally {
    $productionRsa.Dispose()
}
