[CmdletBinding()]
param(
    [string]$Bootstrapper = 'D:\project2\facm-boot3c-native-build-20260831\FACM.exe',
    [string]$BundleRoot = 'D:\project2\facm-free-dist-final-candidate-flat4-20260901\bundle',
    [string]$ReviewRoot = 'D:\project2\facm4-single-launcher-review-20260901',
    [string]$TestRoot = 'D:\project2\facm4-single-launcher-tests-20260901',
    [string]$LocalValidationKeyPath = 'D:\project2\facm-boot3a-signing\production-r1\production-r1.pk8.pem',
    [string]$UnsignedManifestRoot = 'D:\project2\facm-boot3c-boot3b-regression-20260831\release-a\boot2-mirror'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Require([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Get-FullPath([string]$Path) { return [IO.Path]::GetFullPath($Path) }
function Assert-DProject2Path([string]$Path, [string]$Label) {
    $full = Get-FullPath $Path
    Require ($full.StartsWith('D:\project2\', [StringComparison]::OrdinalIgnoreCase) -and $full -ne 'D:\project2') "$Label must be a specific path under D:\project2: $full"
    return $full
}
function Write-Utf8NoBom([string]$Path, [string]$Text) {
    [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false))
}
function Write-ExactJson([string]$Path, [object]$Value) {
    Write-Utf8NoBom $Path (($Value | ConvertTo-Json -Depth 30) + "`n")
}
function New-CleanDirectory([string]$Path) {
    if (Test-Path -LiteralPath $Path) { Remove-Item -LiteralPath $Path -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $Path | Out-Null
}
function Copy-Bootstrapper([string]$Destination) {
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Copy-Item -LiteralPath $Bootstrapper -Destination (Join-Path $Destination 'FACM.exe') -Force
}
function Read-Events([string]$Root) {
    $path = Join-Path $Root '.facm\logs\bootstrapper.jsonl'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return @() }
    return @(Get-Content -LiteralPath $path | ForEach-Object { $_ | ConvertFrom-Json })
}
function Invoke-Boot([string]$Root, [string[]]$Arguments) {
    $process = Start-Process -FilePath (Join-Path $Root 'FACM.exe') -ArgumentList $Arguments -WorkingDirectory $Root -WindowStyle Hidden -Wait -PassThru
    return $process.ExitCode
}
function Start-Boot([string]$Root, [string[]]$Arguments) {
    return Start-Process -FilePath (Join-Path $Root 'FACM.exe') -ArgumentList $Arguments -WorkingDirectory $Root -WindowStyle Hidden -PassThru
}
function Open-Rsa([string]$Path) {
    Require (Test-Path -LiteralPath $Path -PathType Leaf) "Local validation key missing: $Path"
    $rsa = [Security.Cryptography.RSA]::Create()
    $rsa.ImportFromPem([IO.File]::ReadAllText($Path))
    return $rsa
}
function Get-Sha256Bytes([byte[]]$Bytes) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($Bytes)) -replace '-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}
function Sign-File([string]$Path, [Security.Cryptography.RSA]$Rsa) {
    $bytes = [IO.File]::ReadAllBytes($Path)
    $signature = $Rsa.SignData($bytes, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
    Write-Utf8NoBom "$Path.sig" ([Convert]::ToBase64String($signature) + "`n")
    return Get-Sha256Bytes $bytes
}
function Get-LocalPath([string]$Root, [string]$Relative) {
    $normalized = $Relative.Replace('/', '\')
    Require (-not [IO.Path]::IsPathRooted($normalized) -and $normalized.Split('\') -notcontains '..') "Unsafe release path: $Relative"
    return Join-Path $Root $normalized
}
function Prepare-SignedLocalBundle([string]$Source, [string]$Destination, [int]$PrimaryPort, [int]$MirrorPort, [Security.Cryptography.RSA]$Rsa) {
    New-CleanDirectory $Destination
    Copy-Item -Path (Join-Path $Source '*') -Destination $Destination -Recurse -Force
    $primaryBase = "https://127.0.0.1:$PrimaryPort"
    $mirrorBase = "https://127.0.0.1:$MirrorPort"
    $applicationPath = Join-Path $Destination 'manifest.json'
    $application = Get-Content -Raw -LiteralPath $applicationPath | ConvertFrom-Json
    $releaseIndex = Get-Content -Raw -LiteralPath (Join-Path $Destination 'release-index.json') | ConvertFrom-Json
    $application.manifestMirrors = @("$mirrorBase/manifest.json")
    $components = @($application.components)
    for ($index = 0; $index -lt $components.Count; $index++) {
        $appComponent = $components[$index]
        $id = [string]$appComponent.componentId
        $releaseComponent = @($releaseIndex.components | Where-Object { [string]$_.componentId -ceq $id }) | Select-Object -First 1
        Require ($null -ne $releaseComponent) "Release index component missing: $id"
        $manifestRelative = [string]$releaseComponent.manifestPath
        $packageRelative = [string]$releaseComponent.packagePath
        $componentPath = Get-LocalPath $Destination $manifestRelative
        $component = Get-Content -Raw -LiteralPath $componentPath | ConvertFrom-Json
        $appComponent.primaryUrl = "$primaryBase/$packageRelative"
        $appComponent.mirrors = @("$mirrorBase/$packageRelative")
        $appComponent.componentManifestUrl = "$primaryBase/$manifestRelative"
        $appComponent.componentManifestMirrors = @("$mirrorBase/$manifestRelative")
        $component.primaryUrl = $appComponent.primaryUrl
        $component.mirrors = @($appComponent.mirrors)
        $component.componentManifestMirrors = @($appComponent.componentManifestMirrors)
        Write-ExactJson $componentPath $component
        $appComponent.componentManifestSha256 = Sign-File $componentPath $Rsa
    }
    $application.components = $components
    Write-ExactJson $applicationPath $application
    [void](Sign-File $applicationPath $Rsa)
    return [ordered]@{ primaryBase=$primaryBase; mirrorBase=$mirrorBase; version=[string]$application.applicationVersion }
}
function New-TestCertificate([string]$Directory) {
    New-Item -ItemType Directory -Force -Path $Directory | Out-Null
    $rsa = [Security.Cryptography.RSA]::Create(2048)
    $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new(
        'CN=FACM FREE-DIST single-launcher local test', $rsa,
        [Security.Cryptography.HashAlgorithmName]::SHA256,
        [Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $san = [Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder]::new()
    $san.AddDnsName('localhost')
    $san.AddIpAddress([Net.IPAddress]::Parse('127.0.0.1'))
    $request.CertificateExtensions.Add($san.Build())
    $certificate = $request.CreateSelfSigned([DateTimeOffset]::Now.AddMinutes(-5), [DateTimeOffset]::Now.AddDays(1))
    $keyPath = Join-Path $Directory 'local-test-key.pem'
    $certPath = Join-Path $Directory 'local-test-cert.pem'
    Write-Utf8NoBom $keyPath $rsa.ExportPkcs8PrivateKeyPem()
    Write-Utf8NoBom $certPath $certificate.ExportCertificatePem()
    $store = [Security.Cryptography.X509Certificates.X509Store]::new('Root', 'CurrentUser')
    $store.Open([Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
    $store.Add($certificate)
    $thumbprint = $certificate.Thumbprint
    $store.Close(); $certificate.Dispose(); $rsa.Dispose()
    return [ordered]@{ key=$keyPath; cert=$certPath; thumbprint=$thumbprint }
}
function Remove-TestCertificate([string]$Thumbprint) {
    if ([string]::IsNullOrWhiteSpace($Thumbprint)) { return }
    $store = [Security.Cryptography.X509Certificates.X509Store]::new('Root', 'CurrentUser')
    $store.Open([Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
    foreach ($certificate in @($store.Certificates | Where-Object { $_.Thumbprint -eq $Thumbprint })) { $store.Remove($certificate) }
    $store.Close()
}
function Start-Origin([string]$Node, [string]$Script, [string]$Root, [int]$Port, [string]$Key, [string]$Cert,
                      [string]$Ready, [string]$RequestLog, [string]$Mode) {
    $arguments = @($Script, '--root', $Root, '--port', "$Port", '--key', $Key, '--cert', $Cert,
                   '--ready', $Ready, '--request-log', $RequestLog, '--mode', $Mode)
    $process = Start-Process -FilePath $Node -ArgumentList $arguments -WindowStyle Hidden -PassThru
    for ($attempt = 0; $attempt -lt 100; $attempt++) {
        if (Test-Path -LiteralPath $Ready -PathType Leaf) { return $process }
        if ($process.HasExited) { throw "HTTPS origin exited before readiness: $Mode" }
        Start-Sleep -Milliseconds 50
    }
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    throw "HTTPS origin readiness timeout: $Mode"
}
function Stop-Origin($Process) {
    if ($null -ne $Process -and -not $Process.HasExited) {
        Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue
        $Process.WaitForExit(2000) | Out-Null
    }
}
function Find-CandidateApp([string]$Root, [string]$Version) {
    $expected = Get-FullPath (Join-Path $Root ".facm\versions\$Version\FACM.App.exe")
    return @(Get-CimInstance Win32_Process -Filter "Name = 'FACM.App.exe'" | Where-Object {
        $_.ExecutablePath -and (Get-FullPath ([string]$_.ExecutablePath)) -ieq $expected
    })
}
function Close-CandidateApp([string]$Root, [string]$Version) {
    foreach ($info in @(Find-CandidateApp $Root $Version)) {
        $process = Get-Process -Id $info.ProcessId -ErrorAction SilentlyContinue
        if ($null -eq $process) { continue }
        if (-not $process.HasExited -and $process.MainWindowHandle -ne 0) { [void]$process.CloseMainWindow() }
        try { [void]$process.WaitForExit(10000) } catch { }
        if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
    }
}
function Write-Bootstrap([string]$Root, [string]$ManifestUrl, [bool]$AllowUnsignedLocal=$false, [bool]$AllowInsecureLocal=$false, [object]$Extra=$null) {
    $config = [ordered]@{
        schemaVersion=1
        manifestUrl=$ManifestUrl
        manifestMirrors=@()
        allowUnsignedLocal=$AllowUnsignedLocal
        allowInsecureLocal=$AllowInsecureLocal
    }
    if ($null -ne $Extra) { $config.trustedKeys = $Extra }
    Write-ExactJson (Join-Path $Root 'bootstrap.json') $config
}

$Bootstrapper = Assert-DProject2Path $Bootstrapper 'Bootstrapper'
$BundleRoot = Assert-DProject2Path $BundleRoot 'BundleRoot'
$ReviewRoot = Assert-DProject2Path $ReviewRoot 'ReviewRoot'
$TestRoot = Assert-DProject2Path $TestRoot 'TestRoot'
$LocalValidationKeyPath = Assert-DProject2Path $LocalValidationKeyPath 'LocalValidationKeyPath'
$UnsignedManifestRoot = Assert-DProject2Path $UnsignedManifestRoot 'UnsignedManifestRoot'
Require (Test-Path -LiteralPath $Bootstrapper -PathType Leaf) "Bootstrapper missing: $Bootstrapper"
Require (Test-Path -LiteralPath $BundleRoot -PathType Container) "Bundle missing: $BundleRoot"
Require (Test-Path -LiteralPath (Join-Path $UnsignedManifestRoot 'manifest.json') -PathType Leaf) "Unsigned manifest missing: $UnsignedManifestRoot"

$canonicalUrl = 'https://github.com/xianyumht-cmd/facm/releases/download/v4.0.0-free-dist-test.2/manifest.json'
$transportProbeUrl = 'https://github.com/cli/cli/releases/download/v2.62.0/gh_2.62.0_checksums.txt'
$serverScript = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'Start-FacmBoot3CHttpsOrigin.js'
$node = (Get-Command node -ErrorAction Stop).Source
New-CleanDirectory $TestRoot
New-CleanDirectory $ReviewRoot
Copy-Bootstrapper $ReviewRoot
$reviewFilesBefore = @(Get-ChildItem -LiteralPath $ReviewRoot -Recurse -File)
Require ($reviewFilesBefore.Count -eq 1 -and $reviewFilesBefore[0].Name -ceq 'FACM.exe') 'Single-launcher review root is not exactly one FACM.exe before first launch.'
Write-Host 'SingleLauncherBeforeFirstLaunch: PASS (1 file: FACM.exe)'

$binaryWideText = [Text.Encoding]::Unicode.GetString([IO.File]::ReadAllBytes($Bootstrapper))
Require $binaryWideText.Contains($canonicalUrl) 'Compiled bootstrapper does not contain the canonical test manifest URL.'
Write-Host 'EmbeddedCanonicalBootstrap: PASS'

$certificate = $null
$rsa = $null
$origins = @()
$candidateAppRoot = Join-Path $TestRoot 'single-no-bootstrap'
$probeRoot = Join-Path $TestRoot 'single-transport-probe'
$validOverrideRoot = Join-Path $TestRoot 'valid-override'
$malformedRoot = Join-Path $TestRoot 'malformed-bootstrap'
$httpDowngradeRoot = Join-Path $TestRoot 'http-downgrade'
$keyInjectionRoot = Join-Path $TestRoot 'key-injection'
$unsignedRoot = Join-Path $TestRoot 'unsigned-manifest'
$distribution = $null
try {
    $certificate = New-TestCertificate (Join-Path $TestRoot 'tls')
    $rsa = Open-Rsa $LocalValidationKeyPath
    $primaryPort = Get-Random -Minimum 20000 -Maximum 26000
    $mirrorPort = Get-Random -Minimum 26001 -Maximum 30000
    $unsignedPort = Get-Random -Minimum 30001 -Maximum 34000
    $preparedRoot = Join-Path $TestRoot 'prepared-origin'
    $distribution = Prepare-SignedLocalBundle $BundleRoot $preparedRoot $primaryPort $mirrorPort $rsa
    $origins += Start-Origin $node $serverScript $preparedRoot $primaryPort $certificate.key $certificate.cert (Join-Path $TestRoot 'primary.ready') (Join-Path $TestRoot 'primary.requests.jsonl') 'normal'
    $origins += Start-Origin $node $serverScript $preparedRoot $mirrorPort $certificate.key $certificate.cert (Join-Path $TestRoot 'mirror.ready') (Join-Path $TestRoot 'mirror.requests.jsonl') 'normal'

    Copy-Bootstrapper $candidateAppRoot
    $beforeFiles = @(Get-ChildItem -LiteralPath $candidateAppRoot -Recurse -File)
    Require ($beforeFiles.Count -eq 1 -and $beforeFiles[0].Name -ceq 'FACM.exe') 'No-bootstrap candidate was not exactly one file before first launch.'
    $localManifest = "$($distribution.primaryBase)/manifest.json"
    Require ((Invoke-Boot $candidateAppRoot @('--update', "--manifest-url=$localManifest", '--dry-run', '--no-ui')) -eq 0) 'Single launcher local provisioning failed.'
    Require (-not (Test-Path -LiteralPath (Join-Path $candidateAppRoot 'bootstrap.json'))) 'Single launcher unexpectedly created bootstrap.json.'
    $activePath = Join-Path $candidateAppRoot ".facm\versions\$($distribution.version)\FACM.App.exe"
    Require (Test-Path -LiteralPath $activePath -PathType Leaf) 'Single launcher did not create the active FACM.App.exe.'
    $componentState = Get-Content -Raw -LiteralPath (Join-Path $candidateAppRoot '.facm\state\components.json') | ConvertFrom-Json
    Require (@($componentState.components).Count -eq 3) 'Single launcher did not compose all three required components.'
    $defaultEvents = @(Read-Events $candidateAppRoot | Where-Object { $_.event -eq 'bootstrap-default-selected' -and $_.detail -ceq $canonicalUrl })
    Require ($defaultEvents.Count -ge 1) 'Missing compiled-default bootstrap evidence when bootstrap.json was absent.'
    Write-Host 'NoBootstrapLocalProvisioning: PASS (single file before launch; 3 components; no bootstrap.json)'

    $launchProcess = Start-Boot $candidateAppRoot @('--no-ui')
    $appProcess = $null
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        $appProcess = @(Find-CandidateApp $candidateAppRoot $distribution.version)
        if ($appProcess.Count -gt 0) { break }
        Start-Sleep -Milliseconds 250
    }
    Require ($launchProcess.HasExited -or $appProcess.Count -gt 0) 'Single launcher bootstrap process did not create a child or exit cleanly.'
    Require (@($appProcess).Count -gt 0) 'Single launcher did not start the real FACM.App Orb process.'
    Write-Host 'RealFacmOrbLaunch: PASS'
    Close-CandidateApp $candidateAppRoot $distribution.version
    $remainingApp = @()
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        $remainingApp = @(Find-CandidateApp $candidateAppRoot $distribution.version)
        if ($remainingApp.Count -eq 0) { break }
        Start-Sleep -Milliseconds 250
    }
    Require ($remainingApp.Count -eq 0) 'Candidate FACM.App process remained after graceful close.'

    Copy-Bootstrapper $probeRoot
    Require ((Invoke-Boot $probeRoot @('--no-ui', "--probe-github-transport=$transportProbeUrl")) -eq 0) 'Single-file transport probe failed.'
    $probeEvents = @(Read-Events $probeRoot | Where-Object { $_.event -in @('free-dist-transport-probe-pass','free-dist-transport-probe-fail') })
    $probeIds = @($probeEvents | Select-Object -ExpandProperty detail -Unique)
    Require ($probeIds.Count -eq 4) "Expected four transport candidates without bootstrap.json, found $($probeIds -join ',')."
    Require (@($probeEvents | Where-Object { $_.event -eq 'free-dist-transport-probe-pass' }).Count -ge 1) 'No single-file transport candidate passed.'
    Write-Host "SingleFileTransportCandidates: PASS ($($probeIds -join ', '))"

    Copy-Bootstrapper $validOverrideRoot
    Write-Bootstrap $validOverrideRoot $localManifest
    Require ((Invoke-Boot $validOverrideRoot @('--update','--dry-run','--no-ui')) -eq 0) 'Valid explicit bootstrap.json override failed.'
    Write-Host 'OptionalValidBootstrapOverride: PASS'

    Copy-Bootstrapper $malformedRoot
    Write-Utf8NoBom (Join-Path $malformedRoot 'bootstrap.json') '{"schemaVersion":1,"manifestUrl":"https://127.0.0.1:1/manifest.json"'
    Require ((Invoke-Boot $malformedRoot @('--update', "--manifest-url=$localManifest", '--dry-run', '--no-ui')) -eq 0) 'Malformed bootstrap.json did not safely fall back while using an explicit test override.'
    $fallbackEvents = @(Read-Events $malformedRoot | Where-Object { $_.event -eq 'bootstrap-config-invalid-fallback' -and $_.detail -ceq $canonicalUrl })
    Require ($fallbackEvents.Count -ge 1) 'Malformed bootstrap.json did not produce compiled-default fallback evidence.'
    Write-Host 'MalformedBootstrapSafeFallback: PASS'

    Copy-Bootstrapper $httpDowngradeRoot
    Write-Bootstrap $httpDowngradeRoot 'http://127.0.0.1:1/manifest.json'
    Require ((Invoke-Boot $httpDowngradeRoot @('--update','--dry-run','--no-ui')) -eq 14) 'HTTP bootstrap downgrade was not rejected.'
    Write-Host 'BootstrapHttpDowngradeRejected: PASS'

    Copy-Bootstrapper $keyInjectionRoot
    Write-Bootstrap $keyInjectionRoot $localManifest $false $false @([ordered]@{ keyId='attacker-key'; publicKey='not-trusted' })
    Require ((Invoke-Boot $keyInjectionRoot @('--update','--dry-run','--no-ui')) -eq 0) 'Signed provisioning failed with an ignored arbitrary trust-key field.'
    Write-Host 'BootstrapTrustKeyInjectionIgnored: PASS'

    $unsignedOrigin = Start-Origin $node $serverScript $UnsignedManifestRoot $unsignedPort $certificate.key $certificate.cert (Join-Path $TestRoot 'unsigned.ready') (Join-Path $TestRoot 'unsigned.requests.jsonl') 'normal'
    $origins += $unsignedOrigin
    Copy-Bootstrapper $unsignedRoot
    Write-Bootstrap $unsignedRoot "https://127.0.0.1:$unsignedPort/manifest.json"
    Require ((Invoke-Boot $unsignedRoot @('--update','--dry-run','--no-ui')) -eq 14) 'Unsigned production manifest was not rejected.'
    Write-Host 'UnsignedProductionManifestRejected: PASS'

    Write-ExactJson (Join-Path $TestRoot 'results.json') ([ordered]@{
        schemaVersion=1
        harness='FACM FREE-DIST-3 single launcher'
        candidateVersion=$distribution.version
        canonicalManifestUrl=$canonicalUrl
        transportProbeUrl=$transportProbeUrl
        beforeFirstLaunchFileCount=$reviewFilesBefore.Count
        beforeFirstLaunchFiles=@($reviewFilesBefore.Name)
        embeddedDefaultResolved=$true
        componentCount=@($componentState.components).Count
        transportCandidates=$probeIds
        trustBoundary=@('valid signed production manifest','malformed config fallback','HTTP downgrade rejected','unsigned manifest rejected','arbitrary trust-key field ignored')
        realFacmOrbLaunch=$true
    })
    Write-Host 'FACM FREE-DIST-3 single-launcher tests: SUCCESS'
} finally {
    if ($null -ne $distribution) { Close-CandidateApp $candidateAppRoot $distribution.version }
    foreach ($origin in $origins) { Stop-Origin $origin }
    if ($null -ne $rsa) { $rsa.Dispose() }
    if ($null -ne $certificate) {
        try { Remove-TestCertificate $certificate.thumbprint } catch { }
        foreach ($path in @($certificate.key, $certificate.cert)) { if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue } }
    }
}
