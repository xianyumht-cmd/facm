[CmdletBinding()]
param(
    [string]$SeedRoot = 'D:\project2\facm-release-4.0.1-selfsigned\release-assets',
    [string]$OutputRoot = 'D:\project2\facm-release-4.0.2-gitee\release-assets',
    [string]$Version = '4.0.2',
    [string]$ReleaseTag = 'v4.0.2',
    [string]$BootstrapperPath = 'D:\project2\facm-boot2-gitee-4.0.2-build\bootstrap\FACM.exe',
    [string]$PrivateKeyPath = 'D:\project2\Facm\local-signing\FACM4-MANIFEST-SIGNING-PRIVATE.pem',
    [string]$KeyId = 'facm-production-r1',
    [string]$ManifestBaseUrl = 'https://gitee.com/xymhtcmd/facm/releases/download/v4.0.2',
    [string]$PackageSourceRoot = '',
    [string]$PackageSourceVersion = ''
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
function Get-Sha256([string]$Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Get-DirectoryDigest([string]$Root) {
    $paths = [System.Collections.Generic.List[string]]::new()
    foreach ($file in @(Get-ChildItem -LiteralPath $Root -Recurse -File)) {
        [void]$paths.Add(([IO.Path]::GetRelativePath($Root, $file.FullName)).Replace('\','/'))
    }
    $paths.Sort([StringComparer]::Ordinal)
    $rows = @()
    foreach ($relative in $paths) {
        $file = Get-Item -LiteralPath ([IO.Path]::Combine($Root, $relative.Replace('/', [IO.Path]::DirectorySeparatorChar)))
        $rows += $relative
        $rows += (Get-Sha256 $file.FullName)
    }
    $bytes = [Text.Encoding]::UTF8.GetBytes(($rows -join "`n") + "`n")
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($bytes)) -replace '-','').ToLowerInvariant() }
    finally { $sha.Dispose() }
}
function Write-ExactJson([string]$Path, [object]$Value) {
    $json = $Value | ConvertTo-Json -Depth 30
    [IO.File]::WriteAllText($Path, $json + "`n", [Text.UTF8Encoding]::new($false))
}
function Read-Json([string]$Path) {
    Require (Test-Path -LiteralPath $Path -PathType Leaf) "Missing JSON seed: $Path"
    return Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json
}
function Copy-Seed([string]$Name, [string]$Destination) {
    $source = Join-Path $SeedRoot $Name
    Require (Test-Path -LiteralPath $source -PathType Leaf) "Missing seed asset: $source"
    Copy-Item -LiteralPath $source -Destination $Destination -Force
}
function Open-Rsa([string]$Path) {
    Require (Test-Path -LiteralPath $Path -PathType Leaf) "Detached signing private key missing: $Path"
    $rsa = [Security.Cryptography.RSA]::Create()
    $rsa.ImportFromPem([IO.File]::ReadAllText($Path))
    Require ($rsa.KeySize -eq 2048) "Detached signing key must be RSA-2048."
    return $rsa
}
function Sign-ExactJson([string]$PayloadPath, [string]$SignaturePath, [Security.Cryptography.RSA]$Rsa) {
    $bytes = [IO.File]::ReadAllBytes($PayloadPath)
    $signature = $Rsa.SignData($bytes, [Security.Cryptography.HashAlgorithmName]::SHA256,
        [Security.Cryptography.RSASignaturePadding]::Pkcs1)
    [IO.File]::WriteAllText($SignaturePath, [Convert]::ToBase64String($signature) + "`n", [Text.UTF8Encoding]::new($false))
}
function Get-ComponentFiles([string]$Id) {
    return [ordered]@{
        Id = $Id
        PackageSeed = "$Id-4.0.1.cab"
        PackageName = "$Id-$Version.cab"
        ManifestName = "$Id-component-manifest.json"
        SignatureName = "$Id-component-manifest.json.sig"
    }
}
function Get-CabContentMetadata([string]$PackagePath, [string]$VerificationRoot) {
    if (Test-Path -LiteralPath $VerificationRoot) { Remove-Item -LiteralPath $VerificationRoot -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $VerificationRoot | Out-Null
    $expand = Join-Path $env:WINDIR 'System32\expand.exe'
    Require (Test-Path -LiteralPath $expand -PathType Leaf) "Windows expand.exe missing: $expand"
    & $expand '-F:*' $PackagePath $VerificationRoot 2>&1 | Out-Null
    Require ($LASTEXITCODE -eq 0) "CAB extraction failed during release metadata verification: $PackagePath"
    $files = @(Get-ChildItem -LiteralPath $VerificationRoot -Recurse -File)
    Require ($files.Count -gt 0) "CAB contains no files: $PackagePath"
    $records = @($files | ForEach-Object {
        [ordered]@{
            path = ([IO.Path]::GetRelativePath($VerificationRoot, $_.FullName)).Replace('\','/')
            size = [uint64]$_.Length
            sha256 = Get-Sha256 $_.FullName
        }
    } | Sort-Object path)
    [pscustomobject]@{
        fileCount = [uint64]$files.Count
        installedSize = [uint64](($files | Measure-Object -Property Length -Sum).Sum)
        contentDigest = Get-DirectoryDigest $VerificationRoot
        files = $records
    }
}

$SeedRoot = Assert-DProject2Path $SeedRoot 'SeedRoot'
$OutputRoot = Assert-DProject2Path $OutputRoot 'OutputRoot'
$BootstrapperPath = Assert-DProject2Path $BootstrapperPath 'BootstrapperPath'
$PrivateKeyPath = Assert-DProject2Path $PrivateKeyPath 'PrivateKeyPath'
if (-not [string]::IsNullOrWhiteSpace($PackageSourceRoot)) {
    $PackageSourceRoot = Assert-DProject2Path $PackageSourceRoot 'PackageSourceRoot'
    Require (Test-Path -LiteralPath $PackageSourceRoot -PathType Container) "Package source root missing: $PackageSourceRoot"
    Require (-not [string]::IsNullOrWhiteSpace($PackageSourceVersion)) 'PackageSourceVersion is required when PackageSourceRoot is provided.'
}
Require ($Version -match '^4\.0\.[0-9]+$') 'Version must be a 4.0.x version.'
Require ($ReleaseTag -ceq "v$Version") 'ReleaseTag must match Version so release URLs remain deterministic.'
$manifestUri = $null
Require ([Uri]::TryCreate($ManifestBaseUrl, [UriKind]::Absolute, [ref]$manifestUri) -and $manifestUri.Scheme -eq 'https') 'ManifestBaseUrl must be an HTTPS URL.'
Require (Test-Path -LiteralPath $BootstrapperPath -PathType Leaf) "Bootstrapper missing: $BootstrapperPath"

if (Test-Path -LiteralPath $OutputRoot) { Remove-Item -LiteralPath $OutputRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

$baseUrl = $ManifestBaseUrl.TrimEnd('/')
$sourceCommit = (& git -C (Get-Location) rev-parse HEAD).Trim()
Require ($sourceCommit -match '^[0-9a-f]{40}$') 'Unable to record source commit.'
$rsa = Open-Rsa $PrivateKeyPath
try {
    $componentIds = @('facm-app-win-x64', 'facm-dotnet-runtime-win-x64', 'facm-windows-runtime-win-x64')
    $appComponents = [System.Collections.Generic.List[object]]::new()
    $indexComponents = [System.Collections.Generic.List[object]]::new()
    $ownership = Read-Json (Join-Path $SeedRoot 'ownership-report.json')
    $verificationRoot = Join-Path $OutputRoot '.cab-verify'

    foreach ($id in $componentIds) {
        $meta = Get-ComponentFiles $id
        $packagePath = Join-Path $OutputRoot $meta.PackageName
        $manifestPath = Join-Path $OutputRoot $meta.ManifestName
        $signaturePath = Join-Path $OutputRoot $meta.SignatureName
        $packageSource = if ([string]::IsNullOrWhiteSpace($PackageSourceRoot)) {
            Join-Path $SeedRoot $meta.PackageSeed
        } else {
            Join-Path $PackageSourceRoot "$id-$PackageSourceVersion.cab"
        }
        Require (Test-Path -LiteralPath $packageSource -PathType Leaf) "Missing package source: $packageSource"
        Copy-Item -LiteralPath $packageSource -Destination $packagePath -Force

        $actual = Get-CabContentMetadata $packagePath (Join-Path $verificationRoot $id)

        $component = Read-Json (Join-Path $SeedRoot $meta.ManifestName)
        $component.version = $Version
        $component.keyId = $KeyId
        $component.packageSize = [uint64](Get-Item -LiteralPath $packagePath).Length
        $component.sha256 = Get-Sha256 $packagePath
        $component.installedSize = [uint64]$actual.installedSize
        $component.fileCount = [uint64]$actual.fileCount
        $component.contentDigest = [string]$actual.contentDigest
        $component.primaryUrl = "$baseUrl/$($meta.PackageName)"
        $component.mirrors = @()
        if ($component.PSObject.Properties.Name -contains 'componentManifestUrl') {
            $component.componentManifestUrl = "$baseUrl/$($meta.ManifestName)"
        } else {
            $component | Add-Member -NotePropertyName componentManifestUrl -NotePropertyValue "$baseUrl/$($meta.ManifestName)"
        }
        $component.componentManifestMirrors = @()
        Write-ExactJson $manifestPath $component
        Sign-ExactJson $manifestPath $signaturePath $rsa

        $appComponent = [ordered]@{}
        foreach ($property in $component.PSObject.Properties) { $appComponent[$property.Name] = $property.Value }
        $appComponent['componentManifestSha256'] = Get-Sha256 $manifestPath
        [void]$appComponents.Add($appComponent)

        [void]$indexComponents.Add([ordered]@{
            componentId = $id
            version = $Version
            manifestPath = $meta.ManifestName
            manifestSha256 = Get-Sha256 $manifestPath
            manifestBytes = [uint64](Get-Item -LiteralPath $manifestPath).Length
            signaturePath = $meta.SignatureName
            packagePath = $meta.PackageName
            packageSha256 = Get-Sha256 $packagePath
            packageBytes = [uint64](Get-Item -LiteralPath $packagePath).Length
            installedSize = [uint64]$component.installedSize
            fileCount = [uint64]$component.fileCount
            contentDigest = [string]$component.contentDigest
        })

        $owner = @($ownership.componentOwnership | Where-Object { [string]$_.componentId -ceq $id }) | Select-Object -First 1
        if ($null -ne $owner) {
            $owner.installedSize = [uint64]$actual.installedSize
            $owner.fileCount = [uint64]$actual.fileCount
            $owner.files = @($actual.files)
        }
    }

    if (Test-Path -LiteralPath $verificationRoot) { Remove-Item -LiteralPath $verificationRoot -Recurse -Force }

    $application = [ordered]@{
        schemaVersion = 3
        applicationId = 'FACM'
        applicationVersion = $Version
        architecture = 'win-x64'
        trustMode = 'production'
        keyId = $KeyId
        manifestMirrors = @()
        components = @($appComponents)
    }
    $applicationPath = Join-Path $OutputRoot 'manifest.json'
    $applicationSignaturePath = Join-Path $OutputRoot 'manifest.json.sig'
    Write-ExactJson $applicationPath $application
    Sign-ExactJson $applicationPath $applicationSignaturePath $rsa

    $index = [ordered]@{
        schemaVersion = 1
        releaseVersion = $Version
        architecture = 'win-x64'
        trustMode = 'production'
        keyId = $KeyId
        sourceCommit = $sourceCommit
        packageFormat = 'cab'
        defaultComposition = $componentIds
        application = [ordered]@{
            manifestPath = 'manifest.json'
            manifestSha256 = Get-Sha256 $applicationPath
            manifestBytes = [uint64](Get-Item -LiteralPath $applicationPath).Length
            signaturePath = 'manifest.json.sig'
        }
        components = @($indexComponents)
        signing = [ordered]@{
            algorithm = 'RSA-2048-PKCS1-SHA256'
            signatureEncoding = 'base64'
            requestPath = 'local-self-signed'
            signaturesRequired = 4
            status = 'local-self-signed'
        }
    }
    Write-ExactJson (Join-Path $OutputRoot 'release-index.json') $index

    $ownership.releaseVersion = $Version
    $ownership.sourceCommit = $sourceCommit
    Write-ExactJson (Join-Path $OutputRoot 'ownership-report.json') $ownership

    Write-ExactJson (Join-Path $OutputRoot 'bootstrap.json') ([ordered]@{
        schemaVersion = 1
        manifestUrl = "$baseUrl/manifest.json"
        manifestMirrors = @()
        allowUnsignedLocal = $false
        allowInsecureLocal = $false
    })
    Copy-Item -LiteralPath $BootstrapperPath -Destination (Join-Path $OutputRoot 'FACM.exe') -Force

    $evidence = [ordered]@{
        schemaVersion = 1
        releaseTag = $ReleaseTag
        releaseVersion = $Version
        sourceCommit = $sourceCommit
        keyId = $KeyId
        signing = [ordered]@{
            algorithm = 'RSA-2048-PKCS1-SHA256'
            method = 'local self-signed detached key'
            privateKeyCopied = $false
        }
        bootstrapperSha256 = Get-Sha256 (Join-Path $OutputRoot 'FACM.exe')
        applicationManifestSha256 = Get-Sha256 $applicationPath
        assets = @(Get-ChildItem -LiteralPath $OutputRoot -File | ForEach-Object {
            [ordered]@{ name = $_.Name; bytes = [uint64]$_.Length; sha256 = Get-Sha256 $_.FullName }
        } | Sort-Object name)
    }
    Write-ExactJson (Join-Path $OutputRoot 'self-signed-release-evidence.json') $evidence
} finally {
    $rsa.Dispose()
}

Write-Host "FACM 4.0 self-signed release bundle: $OutputRoot"
Write-Host "Bootstrapper SHA-256: $((Get-FileHash (Join-Path $OutputRoot 'FACM.exe') -Algorithm SHA256).Hash)"
Write-Host "Manifest SHA-256: $((Get-FileHash (Join-Path $OutputRoot 'manifest.json') -Algorithm SHA256).Hash)"
