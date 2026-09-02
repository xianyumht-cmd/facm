[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$BundleRoot,
    [string]$Bootstrapper = 'D:\project2\facm-boot3a-native-build\FACM.exe',
    [string]$KeyPolicyPath = (Join-Path $PSScriptRoot 'facm-keyring-policy.json'),
    [string]$CurrentActiveVersion = ''
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
function Require([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Read-Json([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Required release file missing: $Path" }
    return Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json
}
function Get-Sha256([string]$Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Get-SafeRelativePath([string]$Relative, [string]$Label) {
    $normalized = $Relative.Replace('\','/')
    Require (-not [string]::IsNullOrWhiteSpace($normalized) -and -not [IO.Path]::IsPathRooted($normalized) -and
        $normalized.Split('/') -notcontains '..') "$Label is not a safe relative path: $Relative"
    return $normalized.Replace('/', '\')
}
function Assert-Https([string]$Url, [string]$Label) {
    $uri = [Uri]$Url
    Require ($uri.Scheme -eq 'https' -and -not [string]::IsNullOrWhiteSpace($uri.Host)) "$Label must be HTTPS: $Url"
}
function Parse-NumericVersionBase([string]$Base) {
    $parts = @($Base.Split('.'))
    if ($parts.Count -eq 0 -or @($parts | Where-Object { $_ -notmatch '^\d+$' }).Count -ne 0) { return $null }
    return @($parts | ForEach-Object { [uint64]$_ })
}
function Compare-ReleaseVersion([string]$Left, [string]$Right) {
    $leftSeparator = $Left.IndexOfAny([char[]]'-_')
    $rightSeparator = $Right.IndexOfAny([char[]]'-_')
    $leftBase = if ($leftSeparator -lt 0) { $Left } else { $Left.Substring(0, $leftSeparator) }
    $rightBase = if ($rightSeparator -lt 0) { $Right } else { $Right.Substring(0, $rightSeparator) }
    $leftSuffix = if ($leftSeparator -lt 0) { '' } else { $Left.Substring($leftSeparator + 1) }
    $rightSuffix = if ($rightSeparator -lt 0) { '' } else { $Right.Substring($rightSeparator + 1) }
    $leftParts = Parse-NumericVersionBase $leftBase
    $rightParts = Parse-NumericVersionBase $rightBase
    if ($null -eq $leftParts -or $null -eq $rightParts) { return [string]::Compare($Left, $Right, $true) }
    $count = [Math]::Max($leftParts.Count, $rightParts.Count)
    for ($i=0; $i -lt $count; $i++) {
        $leftPart = if ($i -lt $leftParts.Count) { $leftParts[$i] } else { [uint64]0 }
        $rightPart = if ($i -lt $rightParts.Count) { $rightParts[$i] } else { [uint64]0 }
        if ($leftPart -lt $rightPart) { return -1 }
        if ($leftPart -gt $rightPart) { return 1 }
    }
    if ([string]::IsNullOrEmpty($leftSuffix) -ne [string]::IsNullOrEmpty($rightSuffix)) {
        if ([string]::IsNullOrEmpty($leftSuffix)) { return 1 }
        return -1
    }
    return [string]::Compare($leftSuffix, $rightSuffix, $true)
}
function Get-StringArray([object]$Value) { return @($Value | ForEach-Object { [string]$_ }) }
function Same-Array([object]$Left, [object]$Right) {
    return ((Get-StringArray $Left) -join "`n") -ceq ((Get-StringArray $Right) -join "`n")
}
function Assert-ComponentMetadata([object]$App, [object]$Signed, [string]$Id) {
    foreach ($field in @('componentId','version','architecture','required','packageSize','installedSize','sha256','contentDigest','fileCount','packageFormat','entryPoint','primaryUrl')) {
        $left = [string]$App.$field
        $right = [string]$Signed.$field
        if ($field -in @('sha256','contentDigest')) {
            Require ($left -ieq $right) "Authenticated component metadata mismatch ($Id): $field"
        } else {
            Require ($left -ceq $right) "Authenticated component metadata mismatch ($Id): $field"
        }
    }
    Require (Same-Array $App.mirrors $Signed.mirrors) "Authenticated component metadata mismatch ($Id): mirrors"
    Require (Same-Array $App.componentManifestMirrors $Signed.componentManifestMirrors) "Authenticated component metadata mismatch ($Id): componentManifestMirrors"
    Require (Same-Array $App.dependencies $Signed.dependencies) "Authenticated component metadata mismatch ($Id): dependencies"
}
function Assert-NoSecretMaterial([string]$Root) {
    foreach ($file in @(Get-ChildItem -LiteralPath $Root -Recurse -File)) {
        Require ($file.Extension.ToLowerInvariant() -notin @('.pem','.pfx','.p12','.key','.ppk','.snk')) "Release bundle contains a private-key-like file: $($file.Name)"
        if ($file.Extension.ToLowerInvariant() -in @('.json','.sig','.txt','.md')) {
            $text = Get-Content -Raw -LiteralPath $file.FullName
            Require ($text -notmatch '-----BEGIN [A-Z ]*PRIVATE KEY-----') "Release bundle contains PEM private-key material: $($file.FullName)"
            Require ($text -notmatch '(?i)(password|token|cookie|bearer)\s*[:=]') "Release bundle contains obvious secret material: $($file.FullName)"
        }
    }
}
function Invoke-NativeTrustValidation([string]$Path, [string]$Root) {
    Require (Test-Path -LiteralPath $Path -PathType Leaf) "Native bootstrapper missing: $Path"
    $process = Start-Process -FilePath $Path -ArgumentList @('--verify-trust-bundle',$Root,'--no-ui') -Wait -PassThru -WindowStyle Hidden
    Require ($process.ExitCode -eq 0) "Native BOOT3-A trust validation failed with exit $($process.ExitCode)."
}

$BundleRoot = Assert-DProject2Path $BundleRoot 'BundleRoot'
$Bootstrapper = Assert-DProject2Path $Bootstrapper 'Bootstrapper'
$KeyPolicyPath = Assert-DProject2Path $KeyPolicyPath 'KeyPolicyPath'
Require (Test-Path -LiteralPath $BundleRoot -PathType Container) "Bundle root missing: $BundleRoot"

$policy = Read-Json $KeyPolicyPath
Require ($policy.schemaVersion -eq 1 -and $policy.runtimeTrustSource -eq 'compiled-bootstrapper-keyring') 'Key policy is not the expected review-only schema.'
$acceptedKeys = @($policy.keys | Where-Object { $_.acceptedByCandidateBootstrapper -eq $true } | ForEach-Object { [string]$_.keyId })
Require ($acceptedKeys.Count -gt 0) 'Key policy has no candidate-accepted key.'

$index = Read-Json (Join-Path $BundleRoot 'release-index.json')
$application = Read-Json (Join-Path $BundleRoot 'manifest.json')
Require ($index.schemaVersion -eq 1 -and $index.trustMode -eq 'production') 'Release index is not a production schema-1 index.'
Require ($application.schemaVersion -eq 3 -and $application.trustMode -eq 'production') 'Application manifest is not a signed production schema-3 manifest.'
Require ([string]$application.keyId -in $acceptedKeys) "Application key ID is not currently accepted: $($application.keyId)"
Require ([string]$index.keyId -ceq [string]$application.keyId) 'Release index and application key IDs differ.'
Require ([string]$index.releaseVersion -ceq [string]$application.applicationVersion) 'Release index and application versions differ.'
if (-not [string]::IsNullOrWhiteSpace($CurrentActiveVersion) -and
    (Compare-ReleaseVersion ([string]$application.applicationVersion) $CurrentActiveVersion) -lt 0) {
    throw "Release version is a downgrade from the current active version: $($application.applicationVersion) < $CurrentActiveVersion"
}
Require ('unsigned-local' -notin @([string]$application.trustMode,[string]$index.trustMode)) 'Unsigned-local trust mode is present in a release bundle.'
Require (Same-Array $index.defaultComposition @('facm-app-win-x64','facm-dotnet-runtime-win-x64','facm-windows-runtime-win-x64')) 'Default BOOT composition is not exactly the three core components.'
foreach ($manifestMirror in (Get-StringArray $application.manifestMirrors)) { Assert-Https $manifestMirror 'Application manifest mirror URL' }

$expectedIds = @('facm-app-win-x64','facm-dotnet-runtime-win-x64','facm-windows-runtime-win-x64')
$appComponents = @($application.components)
Require ($appComponents.Count -eq $expectedIds.Count) 'Application manifest component count is not the BOOT3-B core count.'
Require (Same-Array (@($appComponents | ForEach-Object { [string]$_.componentId }) | Sort-Object) ($expectedIds | Sort-Object)) 'Application manifest component IDs are not the BOOT3-B core set.'
Require (@($appComponents | Where-Object { [string]$_.componentId -match '(?i)pet' }).Count -eq 0) 'Desktop Pet is present in the default release composition.'
Require ([string]$index.application.manifestSha256 -ieq (Get-Sha256 (Join-Path $BundleRoot 'manifest.json'))) 'Application manifest digest does not match release index.'
Require ([int64]$index.application.manifestBytes -eq (Get-Item -LiteralPath (Join-Path $BundleRoot 'manifest.json')).Length) 'Application manifest byte count does not match release index.'

$ownership = Read-Json (Join-Path $BundleRoot 'ownership-report.json')
Require ($ownership.schemaVersion -eq 1 -and [string]$ownership.releaseVersion -ceq [string]$application.applicationVersion) 'Ownership report schema/version is invalid.'
$ownedPaths = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($owner in @($ownership.componentOwnership)) {
    Require ([string]$owner.componentId -in $expectedIds) "Ownership report contains an unexpected component: $($owner.componentId)"
    $ownerFiles = @($owner.files)
    Require ($ownerFiles.Count -eq [int64]$owner.fileCount) "Ownership file count mismatch: $($owner.componentId)"
    foreach ($file in $ownerFiles) {
        $relative = ([string]$file.path).Replace('\','/')
        Get-SafeRelativePath $relative "Ownership path for $($owner.componentId)" | Out-Null
        Require ($ownedPaths.Add($relative)) "Overlapping component ownership path: $relative"
    }
}
Require (@($ownership.componentOwnership).Count -eq $expectedIds.Count) 'Ownership report does not cover all core components.'

$indexComponents = @($index.components)
Require ($indexComponents.Count -eq $expectedIds.Count) 'Release index component count is invalid.'
foreach ($appComponent in $appComponents) {
    $id = [string]$appComponent.componentId
    $indexComponent = @($indexComponents | Where-Object { [string]$_.componentId -ceq $id }) | Select-Object -First 1
    Require ($null -ne $indexComponent) "Release index is missing component: $id"
    Require ([string]$appComponent.schemaVersion -eq '3' -and [string]$appComponent.keyId -ceq [string]$application.keyId) "Component $id is not schema 3 with the application key ID."
    Assert-Https ([string]$appComponent.primaryUrl) "$id package URL"
    foreach ($mirror in (Get-StringArray $appComponent.mirrors)) { Assert-Https $mirror "$id mirror URL" }
    Assert-Https ([string]$appComponent.componentManifestUrl) "$id component manifest URL"
    foreach ($mirror in (Get-StringArray $appComponent.componentManifestMirrors)) { Assert-Https $mirror "$id component manifest mirror URL" }
    $manifestRelative = Get-SafeRelativePath ([string]$indexComponent.manifestPath) "$id manifest path"
    $signatureRelative = Get-SafeRelativePath ([string]$indexComponent.signaturePath) "$id signature path"
    $packageRelative = Get-SafeRelativePath ([string]$indexComponent.packagePath) "$id package path"
    Require ([string]$appComponent.componentManifestSha256 -ieq [string]$indexComponent.manifestSha256) "$id application/index manifest digest mismatch."
    $manifestPath = Join-Path $BundleRoot $manifestRelative
    $signaturePath = Join-Path $BundleRoot $signatureRelative
    $packagePath = Join-Path $BundleRoot $packageRelative
    Require (Test-Path -LiteralPath $manifestPath -PathType Leaf) "$id component manifest missing."
    Require (Test-Path -LiteralPath $signaturePath -PathType Leaf) "$id component signature missing."
    Require (Test-Path -LiteralPath $packagePath -PathType Leaf) "$id CAB package missing."
    $manifestInfo = Get-Item -LiteralPath $manifestPath
    $packageInfo = Get-Item -LiteralPath $packagePath
    Require ([string]$indexComponent.manifestSha256 -ieq (Get-Sha256 $manifestPath)) "$id component manifest digest mismatch."
    Require ([int64]$indexComponent.manifestBytes -eq $manifestInfo.Length) "$id component manifest byte count mismatch."
    Require ([string]$indexComponent.packageSha256 -ieq (Get-Sha256 $packagePath)) "$id CAB package digest mismatch."
    Require ([int64]$indexComponent.packageBytes -eq $packageInfo.Length) "$id CAB package byte count mismatch."
    Require ([int64]$appComponent.packageSize -eq $packageInfo.Length) "$id application package size mismatch."
    Require ([string]$appComponent.sha256 -ieq (Get-Sha256 $packagePath)) "$id application package digest mismatch."
    $signedComponent = Read-Json $manifestPath
    Require ($signedComponent.schemaVersion -eq 3 -and [string]$signedComponent.keyId -ceq [string]$application.keyId) "$id signed component key/schema mismatch."
    Assert-ComponentMetadata $appComponent $signedComponent $id
    Require ([int64]$indexComponent.installedSize -eq [int64]$appComponent.installedSize) "$id installed-size index mismatch."
    Require ([int64]$indexComponent.fileCount -eq [int64]$appComponent.fileCount) "$id file-count index mismatch."
    Require ([string]$indexComponent.contentDigest -ieq [string]$appComponent.contentDigest) "$id content digest index mismatch."
    $signatureText = (Get-Content -Raw -LiteralPath $signaturePath).Trim()
    try { $signature = [Convert]::FromBase64String($signatureText) } catch { throw "$id component signature is not Base64." }
    Require ($signature.Length -eq 256) "$id component signature is not RSA-2048 sized."
}

$applicationSignaturePath = Join-Path $BundleRoot 'manifest.json.sig'
Require (Test-Path -LiteralPath $applicationSignaturePath -PathType Leaf) 'Application detached signature is missing.'
try { $applicationSignature = [Convert]::FromBase64String((Get-Content -Raw -LiteralPath $applicationSignaturePath).Trim()) } catch { throw 'Application detached signature is not Base64.' }
Require ($applicationSignature.Length -eq 256) 'Application detached signature is not RSA-2048 sized.'
Assert-NoSecretMaterial $BundleRoot
Invoke-NativeTrustValidation $Bootstrapper $BundleRoot
Write-Host 'FACM BOOT3-B release bundle validator: SUCCESS'
