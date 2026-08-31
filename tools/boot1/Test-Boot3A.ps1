[CmdletBinding()]
param(
    [string]$Bootstrapper = 'D:\project2\facm-boot3a-native-build\FACM.exe',
    [string]$TestRoot = 'D:\project2\facm-boot3a-tests-20260831',
    [int]$Port = 18086,
    [string]$ProductionPrivateKeyPath = 'D:\project2\facm-boot3a-signing\production-r1\production-r1.pk8.pem'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Remove-Scope([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith('D:\project2\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove outside D:\project2: $full"
    }
    if (Test-Path -LiteralPath $full) { Remove-Item -LiteralPath $full -Recurse -Force }
}

function Write-Utf8NoBom([string]$Path, [string]$Text) {
    [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false))
}

function Write-JsonBytes([string]$Path, [object]$Value) {
    $json = $Value | ConvertTo-Json -Depth 12
    [IO.File]::WriteAllBytes($Path, [Text.UTF8Encoding]::new($false).GetBytes($json + "`n"))
}

function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-DirectoryDigest([string]$Root) {
    $descriptor = [Text.StringBuilder]::new()
    $files = @(Get-ChildItem -LiteralPath $Root -Recurse -File | Sort-Object {
        ([IO.Path]::GetRelativePath($Root, $_.FullName)).Replace('\', '/')
    })
    foreach ($file in $files) {
        $relative = ([IO.Path]::GetRelativePath($Root, $file.FullName)).Replace('\', '/')
        [void]$descriptor.Append($relative).Append("`n").Append((Get-Sha256 $file.FullName)).Append("`n")
    }
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash([Text.UTF8Encoding]::new($false).GetBytes($descriptor.ToString())))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}

function New-RsaKey([string]$Path) {
    $parent = Split-Path -Parent $Path
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
    $rsa = [Security.Cryptography.RSA]::Create(2048)
    try { Write-Utf8NoBom $Path $rsa.ExportPkcs8PrivateKeyPem() }
    finally { $rsa.Dispose() }
}

function Open-RsaKey([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Signing key is missing: $Path" }
    $rsa = [Security.Cryptography.RSA]::Create()
    $rsa.ImportFromPem([IO.File]::ReadAllText($Path))
    return $rsa
}

function Write-SignedJson([string]$Path, [object]$Value, [Security.Cryptography.RSA]$Rsa) {
    $json = $Value | ConvertTo-Json -Depth 12
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes($json + "`n")
    [IO.File]::WriteAllBytes($Path, $bytes)
    $signature = $Rsa.SignData(
        $bytes,
        [Security.Cryptography.HashAlgorithmName]::SHA256,
        [Security.Cryptography.RSASignaturePadding]::Pkcs1)
    Write-Utf8NoBom ($Path + '.sig') ([Convert]::ToBase64String($signature) + "`n")
}

function New-Cab([string]$Stage, [string]$OutputDirectory, [string]$CabinetName, [string]$WorkDirectory) {
    New-Item -ItemType Directory -Force -Path $OutputDirectory,$WorkDirectory | Out-Null
    $ddf = Join-Path $WorkDirectory ($CabinetName + '.ddf')
    $lines = @(
        '.OPTION EXPLICIT',
        ('.Set CabinetNameTemplate=' + $CabinetName),
        ('.Set DiskDirectoryTemplate=' + $OutputDirectory),
        '.Set Cabinet=on',
        '.Set Compress=on',
        '.Set CompressionType=MSZIP'
    )
    foreach ($file in @(Get-ChildItem -LiteralPath $Stage -Recurse -File | Sort-Object FullName)) {
        $relative = ([IO.Path]::GetRelativePath($Stage, $file.FullName)).Replace('/', '\')
        $lines += ('"' + $file.FullName + '" "' + $relative + '"')
    }
    Set-Content -LiteralPath $ddf -Value $lines -Encoding ascii
    $makecab = Join-Path $env:WINDIR 'System32\makecab.exe'
    if (-not (Test-Path -LiteralPath $makecab)) { throw 'makecab.exe is required for BOOT3-A trust fixtures.' }
    Push-Location $WorkDirectory
    try { & $makecab /F $ddf | Out-Null } finally { Pop-Location }
    if ($LASTEXITCODE -ne 0) { throw "makecab failed for $CabinetName ($LASTEXITCODE)." }
    $cab = Join-Path $OutputDirectory $CabinetName
    if (-not (Test-Path -LiteralPath $cab -PathType Leaf)) { throw "CAB output missing: $cab" }
    return Get-Item -LiteralPath $cab
}

function Invoke-Boot([string]$Path, [string[]]$Arguments) {
    $process = Start-Process -FilePath $Path -ArgumentList $Arguments -Wait -PassThru -WindowStyle Hidden
    return $process.ExitCode
}

function Copy-Case([string]$Base, [string]$Root, [string]$Name) {
    $case = Join-Path $Root $Name
    New-Item -ItemType Directory -Force -Path $case | Out-Null
    Copy-Item -Path (Join-Path $Base '*') -Destination $case -Recurse -Force
    return $case
}

function Remove-Property([object]$Value, [string]$Name) {
    if ($null -ne $Value.PSObject.Properties[$Name]) { $Value.PSObject.Properties.Remove($Name) }
}

function Start-Mirror([string]$Root, [int]$MirrorPort, [string]$Ready, [string]$Requests, [string]$Log) {
    $script = Join-Path $PSScriptRoot 'Start-Boot2TestMirror.ps1'
    $pwsh = (Get-Command pwsh -ErrorAction Stop).Source
    $process = Start-Process -FilePath $pwsh -ArgumentList @(
        '-NoLogo','-NoProfile','-ExecutionPolicy','Bypass','-File',$script,
        '-Root',$Root,'-Port',$MirrorPort,'-ReadyFile',$Ready,'-RequestLog',$Requests
    ) -WindowStyle Hidden -PassThru -RedirectStandardOutput $Log -RedirectStandardError ($Log + '.err')
    for ($attempt = 0; $attempt -lt 80 -and -not (Test-Path -LiteralPath $Ready); $attempt++) {
        Start-Sleep -Milliseconds 100
    }
    if (-not (Test-Path -LiteralPath $Ready)) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        throw 'BOOT3-A local mirror did not become ready.'
    }
    return $process
}

function Stop-Mirror($Process, [string]$Ready) {
    if ($Process -and -not $Process.HasExited) { Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue }
    if (Test-Path -LiteralPath $Ready) { Remove-Item -LiteralPath $Ready -Force -ErrorAction SilentlyContinue }
}

function Assert-Exit([string]$Name, [int]$Expected, [int]$Actual) {
    if ($Expected -ne $Actual) { throw "$Name expected exit $Expected, got $Actual." }
    Write-Host ($Name + ': PASS')
}

if (-not (Test-Path -LiteralPath $Bootstrapper -PathType Leaf)) { throw "Bootstrapper is missing: $Bootstrapper" }
if (-not (Test-Path -LiteralPath $ProductionPrivateKeyPath -PathType Leaf)) {
    throw "External production fixture signing key is missing: $ProductionPrivateKeyPath"
}

Remove-Scope $TestRoot
New-Item -ItemType Directory -Force -Path $TestRoot | Out-Null
$fixtureRoot = Join-Path $TestRoot 'valid-signed-bundle'
$componentRoot = Join-Path $fixtureRoot 'components'
$sourceRoot = Join-Path $TestRoot 'package-sources'
$cabWorkRoot = Join-Path $TestRoot 'makecab'
New-Item -ItemType Directory -Force -Path $componentRoot,$sourceRoot,$cabWorkRoot | Out-Null

$productionKeyId = 'facm-production-r1'
$testOnlyKeyId = 'facm-test-only-r1'
$productionRsa = Open-RsaKey $ProductionPrivateKeyPath
$testOnlyKeyPath = Join-Path $TestRoot 'keys\facm-test-only-r1.pk8.pem'
New-RsaKey $testOnlyKeyPath
$testOnlyRsa = Open-RsaKey $testOnlyKeyPath

try {
    $version = '4.0.0-boot3a'
    $componentRecords = @()
    foreach ($componentId in @('facm-app-win-x64','facm-dotnet-runtime-win-x64','facm-windows-runtime-win-x64')) {
        $stage = Join-Path $sourceRoot $componentId
        New-Item -ItemType Directory -Force -Path $stage | Out-Null
        if ($componentId -eq 'facm-app-win-x64') {
            [IO.File]::WriteAllBytes((Join-Path $stage 'FACM.App.exe'), [Text.UTF8Encoding]::new($false).GetBytes('BOOT3-A app payload'))
        } elseif ($componentId -eq 'facm-dotnet-runtime-win-x64') {
            [IO.File]::WriteAllBytes((Join-Path $stage 'hostfxr.dll'), [Text.UTF8Encoding]::new($false).GetBytes('BOOT3-A managed runtime payload'))
        } else {
            [IO.File]::WriteAllBytes((Join-Path $stage 'Microsoft.UI.Xaml.dll'), [Text.UTF8Encoding]::new($false).GetBytes('BOOT3-A Windows runtime payload'))
        }
        $componentDirectory = Join-Path (Join-Path $componentRoot $componentId) $version
        $cabName = "$componentId-$version.cab"
        $cab = New-Cab $stage $componentDirectory $cabName (Join-Path $cabWorkRoot $componentId)
        $files = @(Get-ChildItem -LiteralPath $stage -Recurse -File)
        $entryPoint = if ($componentId -eq 'facm-app-win-x64') { 'FACM.App.exe' } else { '' }
        $dependencies = if ($componentId -eq 'facm-app-win-x64') { @('facm-dotnet-runtime-win-x64','facm-windows-runtime-win-x64') } else { @() }
        $component = [ordered]@{
            schemaVersion = 3
            componentId = $componentId
            version = $version
            architecture = 'win-x64'
            keyId = $productionKeyId
            required = $true
            packageSize = [int64]$cab.Length
            installedSize = [int64](($files | Measure-Object -Property Length -Sum).Sum)
            sha256 = Get-Sha256 $cab.FullName
            contentDigest = Get-DirectoryDigest $stage
            fileCount = [int64]$files.Count
            packageFormat = 'cab'
            entryPoint = $entryPoint
            primaryUrl = "https://updates.facm.example/components/$componentId/$version/$cabName"
            mirrors = @("https://cdn.facm.example/components/$componentId/$version/$cabName")
            dependencies = @($dependencies)
        }
        $componentManifestPath = Join-Path $componentDirectory 'component.manifest.json'
        Write-SignedJson $componentManifestPath $component $productionRsa
        $component.componentManifestUrl = "https://updates.facm.example/components/$componentId/$version/component.manifest.json"
        $component.componentManifestSha256 = Get-Sha256 $componentManifestPath
        $componentRecords += $component
    }

    $application = [ordered]@{
        schemaVersion = 3
        applicationId = 'FACM'
        applicationVersion = $version
        architecture = 'win-x64'
        trustMode = 'production'
        keyId = $productionKeyId
        components = $componentRecords
    }
    $applicationPath = Join-Path $fixtureRoot 'manifest.json'
    Write-SignedJson $applicationPath $application $productionRsa
    Write-Host 'Valid signed application/component manifest path: PASS'
    Assert-Exit 'ValidSignedTrustBundle' 0 (Invoke-Boot $Bootstrapper @('--verify-trust-bundle',$fixtureRoot,'--no-ui'))

    $case = Copy-Case $fixtureRoot $TestRoot 'altered-application-manifest'
    $path = Join-Path $case 'manifest.json'
    $bytes = [IO.File]::ReadAllBytes($path) + [byte]0x0A
    [IO.File]::WriteAllBytes($path, $bytes)
    Assert-Exit 'AlteredApplicationManifestBytes' 21 (Invoke-Boot $Bootstrapper @('--verify-trust-bundle',$case,'--no-ui'))

    $case = Copy-Case $fixtureRoot $TestRoot 'altered-component-manifest'
    $path = Join-Path $case "components\facm-app-win-x64\$version\component.manifest.json"
    [IO.File]::WriteAllBytes($path, [IO.File]::ReadAllBytes($path) + [byte]0x0A)
    Assert-Exit 'AlteredComponentManifestBytes' 21 (Invoke-Boot $Bootstrapper @('--verify-trust-bundle',$case,'--no-ui'))

    $case = Copy-Case $fixtureRoot $TestRoot 'invalid-signature'
    Write-Utf8NoBom (Join-Path $case 'manifest.json.sig') "AAAA`n"
    Assert-Exit 'InvalidSignature' 21 (Invoke-Boot $Bootstrapper @('--verify-trust-bundle',$case,'--no-ui'))

    $case = Copy-Case $fixtureRoot $TestRoot 'unknown-key-identity'
    $unknown = Get-Content -Raw -LiteralPath (Join-Path $case 'manifest.json') | ConvertFrom-Json
    $unknown.keyId = $testOnlyKeyId
    Write-SignedJson (Join-Path $case 'manifest.json') $unknown $testOnlyRsa
    Assert-Exit 'UnknownKeyIdentityAndTestOnlyKeyRejection' 21 (Invoke-Boot $Bootstrapper @('--verify-trust-bundle',$case,'--no-ui'))

    $case = Copy-Case $fixtureRoot $TestRoot 'unsigned-production-manifest'
    $unsigned = Get-Content -Raw -LiteralPath (Join-Path $case 'manifest.json') | ConvertFrom-Json
    $unsigned.schemaVersion = 2
    $unsigned.trustMode = 'unsigned-local'
    Remove-Property $unsigned 'keyId'
    Write-SignedJson (Join-Path $case 'manifest.json') $unsigned $productionRsa
    Assert-Exit 'UnsignedManifestRejectedInProductionVerification' 21 (Invoke-Boot $Bootstrapper @('--verify-trust-bundle',$case,'--no-ui'))

    $case = Copy-Case $fixtureRoot $TestRoot 'unsigned-local-downgrade'
    $downgrade = Get-Content -Raw -LiteralPath (Join-Path $case 'manifest.json') | ConvertFrom-Json
    $downgrade.schemaVersion = 2
    $downgrade.trustMode = 'unsigned-local'
    Write-SignedJson (Join-Path $case 'manifest.json') $downgrade $productionRsa
    Assert-Exit 'UnsignedLocalDowngradeRejected' 21 (Invoke-Boot $Bootstrapper @('--verify-trust-bundle',$case,'--no-ui'))

    $case = Copy-Case $fixtureRoot $TestRoot 'altered-authenticated-component-metadata'
    $metadata = Get-Content -Raw -LiteralPath (Join-Path $case 'manifest.json') | ConvertFrom-Json
    $metadata.components[0].contentDigest = ('0' * 64)
    Write-SignedJson (Join-Path $case 'manifest.json') $metadata $productionRsa
    Assert-Exit 'AlteredAuthenticatedComponentMetadata' 21 (Invoke-Boot $Bootstrapper @('--verify-trust-bundle',$case,'--no-ui'))

    $case = Copy-Case $fixtureRoot $TestRoot 'corrupted-package-hash'
    $path = Join-Path $case "components\facm-app-win-x64\$version\facm-app-win-x64-$version.cab"
    $bytes = [IO.File]::ReadAllBytes($path)
    $bytes[0] = $bytes[0] -bxor 0xFF
    [IO.File]::WriteAllBytes($path, $bytes)
    Assert-Exit 'CorruptedPackageHash' 21 (Invoke-Boot $Bootstrapper @('--verify-trust-bundle',$case,'--no-ui'))

    $failureRoot = Join-Path $TestRoot 'failed-update-preservation'
    $localSource = Join-Path $failureRoot 'source'
    $localMirror = Join-Path $failureRoot 'mirror'
    New-Item -ItemType Directory -Force -Path $localSource,$localMirror | Out-Null
    [IO.File]::WriteAllBytes((Join-Path $localSource 'FACM.App.exe'), [Text.UTF8Encoding]::new($false).GetBytes('known-good active app'))
    [IO.File]::WriteAllBytes((Join-Path $localSource 'FACM.App.dll'), [Text.UTF8Encoding]::new($false).GetBytes('known-good managed payload'))
    Copy-Item -LiteralPath (Join-Path $fixtureRoot 'components') -Destination $localMirror -Recurse -Force
    $localManifest = Get-Content -Raw -LiteralPath $applicationPath | ConvertFrom-Json
    $localManifest.schemaVersion = 2
    $localManifest.applicationVersion = '4.0.0-boot3a-bad-update'
    $localManifest.trustMode = 'unsigned-local'
    Remove-Property $localManifest 'keyId'
    foreach ($component in @($localManifest.components)) {
        $component.schemaVersion = 2
        Remove-Property $component 'keyId'
        Remove-Property $component 'componentManifestUrl'
        Remove-Property $component 'componentManifestSha256'
        $component.primaryUrl = "http://127.0.0.1:$Port/components/$($component.componentId)/$($component.version)/$($component.componentId)-$($component.version).cab"
        $component.mirrors = @()
    }
    Write-JsonBytes (Join-Path $localMirror 'manifest.json') $localManifest
    $corruptPath = Join-Path $localMirror "components\facm-app-win-x64\$version\facm-app-win-x64-$version.cab"
    $bytes = [IO.File]::ReadAllBytes($corruptPath)
    $bytes[0] = $bytes[0] -bxor 0xFF
    [IO.File]::WriteAllBytes($corruptPath, $bytes)
    $failureBootstrap = Join-Path $failureRoot 'FACM.exe'
    Copy-Item -LiteralPath $Bootstrapper -Destination $failureBootstrap -Force
    Assert-Exit 'CreateKnownGoodActiveComposition' 0 (Invoke-Boot $failureBootstrap @('--provision-source',$localSource,'--version','1.0.0','--dry-run','--no-ui'))
    $ready = Join-Path $failureRoot 'mirror-ready'
    $requests = Join-Path $failureRoot 'mirror-requests.jsonl'
    $mirrorLog = Join-Path $failureRoot 'mirror.log'
    $mirror = Start-Mirror $localMirror $Port $ready $requests $mirrorLog
    try {
        $failedUpdateExit = Invoke-Boot $failureBootstrap @(
            '--update','--manifest-url',"http://127.0.0.1:$Port/manifest.json",
            '--allow-unsigned-local','--allow-insecure-local','--no-ui')
        if ($failedUpdateExit -eq 0) { throw 'Corrupted update unexpectedly succeeded.' }
        Write-Host 'FailedUpdateWithCorruptPackage: PASS'
    } finally { Stop-Mirror $mirror $ready }
    Assert-Exit 'PreviousActiveCompositionRemainsLaunchable' 0 (Invoke-Boot $failureBootstrap @('--resolve-only','--no-ui'))
    $active = Get-Content -Raw -LiteralPath (Join-Path $failureRoot '.facm\state\active.json') | ConvertFrom-Json
    if ($active.activeVersion -ne '1.0.0') { throw 'Failed update changed the previous active composition.' }
    Write-Host 'FailedUpdatePreservesPreviousActiveVersion: PASS'

    Write-Host 'BOOT3-A signed trust, negative-path, and failed-update-preservation tests: SUCCESS'
}
finally {
    $productionRsa.Dispose()
    $testOnlyRsa.Dispose()
}
