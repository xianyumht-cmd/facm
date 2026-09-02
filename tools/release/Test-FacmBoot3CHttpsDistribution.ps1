[CmdletBinding()]
param(
    [string]$BundleRoot = 'D:\project2\facm-boot3b-tests-final5-20260831\release-a\bundle',
    [string]$Bootstrapper = 'D:\project2\facm-boot3c-native-build-20260831\FACM.exe',
    [string]$LocalValidationKeyPath = 'D:\project2\facm-boot3a-signing\production-r1\production-r1.pk8.pem',
    [string]$TestRoot = 'D:\project2\facm-boot3c-https-tests-20260831'
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
function Get-Sha256Bytes([byte[]]$Bytes) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($Bytes)) -replace '-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}
function Write-ExactJson([string]$Path, [object]$Value) {
    $json = $Value | ConvertTo-Json -Depth 30
    [IO.File]::WriteAllBytes($Path, [Text.UTF8Encoding]::new($false).GetBytes($json + "`n"))
}
function Write-Utf8NoBom([string]$Path, [string]$Text) {
    [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false))
}
function Copy-Bundle([string]$Source, [string]$Destination) {
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Copy-Item -Path (Join-Path $Source '*') -Destination $Destination -Recurse -Force
}
function Open-Rsa([string]$Path) {
    Require (Test-Path -LiteralPath $Path -PathType Leaf) "Local validation key missing: $Path"
    $rsa = [Security.Cryptography.RSA]::Create()
    $rsa.ImportFromPem([IO.File]::ReadAllText($Path))
    return $rsa
}
function Sign-File([string]$Path, [Security.Cryptography.RSA]$Rsa) {
    $bytes = [IO.File]::ReadAllBytes($Path)
    $signature = $Rsa.SignData($bytes, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
    Write-Utf8NoBom "$Path.sig" ([Convert]::ToBase64String($signature) + "`n")
    return Get-Sha256Bytes $bytes
}
function Get-LocalPath([string]$Root, [string]$Relative) {
    $normalized = $Relative.Replace('/', '\')
    Require (-not [IO.Path]::IsPathRooted($normalized) -and $normalized.Split('\') -notcontains '..') "Unsafe origin path: $Relative"
    return Join-Path $Root $normalized
}
function Prepare-Bundle([string]$Source, [string]$Destination, [int]$PrimaryPort, [int]$MirrorPort, [Security.Cryptography.RSA]$Rsa) {
    Copy-Bundle $Source $Destination
    $primaryBase = "https://127.0.0.1:$PrimaryPort"
    $mirrorBase = "https://127.0.0.1:$MirrorPort"
    $applicationPath = Join-Path $Destination 'manifest.json'
    $application = Get-Content -Raw -LiteralPath $applicationPath | ConvertFrom-Json
    $releaseIndex = Get-Content -Raw -LiteralPath (Join-Path $Destination 'release-index.json') | ConvertFrom-Json
    if ($null -eq $application.PSObject.Properties['manifestMirrors']) {
        $application | Add-Member -MemberType NoteProperty -Name manifestMirrors -Value @()
    }
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
        if ($null -eq $appComponent.PSObject.Properties['componentManifestMirrors']) {
            $appComponent | Add-Member -MemberType NoteProperty -Name componentManifestMirrors -Value @()
        }
        if ($null -eq $component.PSObject.Properties['componentManifestMirrors']) {
            $component | Add-Member -MemberType NoteProperty -Name componentManifestMirrors -Value @()
        }
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
    return [ordered]@{ version=[string]$application.applicationVersion; primaryBase=$primaryBase; mirrorBase=$mirrorBase }
}
function New-TestCertificate([string]$Directory) {
    New-Item -ItemType Directory -Force -Path $Directory | Out-Null
    $rsa = [Security.Cryptography.RSA]::Create(2048)
    $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new(
        'CN=FACM BOOT3-C local test', $rsa, [Security.Cryptography.HashAlgorithmName]::SHA256,
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
                      [string]$Ready, [string]$RequestLog, [string]$Mode, [string]$RedirectLocation = '') {
    $arguments = @($Script, '--root', $Root, '--port', "$Port", '--key', $Key, '--cert', $Cert,
                   '--ready', $Ready, '--request-log', $RequestLog, '--mode', $Mode)
    if (-not [string]::IsNullOrWhiteSpace($RedirectLocation)) { $arguments += @('--redirect-location', $RedirectLocation) }
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
    if ($null -ne $Process -and -not $Process.HasExited) { Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue; $Process.WaitForExit(2000) | Out-Null }
}
function New-Candidate([string]$CandidateRoot, [string]$Bootstrapper, [string]$ManifestUrl, [string]$ManifestMirror) {
    New-Item -ItemType Directory -Force -Path $CandidateRoot | Out-Null
    Copy-Item -LiteralPath $Bootstrapper -Destination (Join-Path $CandidateRoot 'FACM.exe') -Force
    Write-ExactJson (Join-Path $CandidateRoot 'bootstrap.json') ([ordered]@{
        schemaVersion=1; manifestUrl=$ManifestUrl; manifestMirrors=@($ManifestMirror)
        allowUnsignedLocal=$false; allowInsecureLocal=$false
    })
}
function Invoke-Boot([string]$CandidateRoot, [string[]]$Arguments) {
    $process = Start-Process -FilePath (Join-Path $CandidateRoot 'FACM.exe') -ArgumentList $Arguments -WorkingDirectory $CandidateRoot -WindowStyle Hidden -Wait -PassThru
    return $process.ExitCode
}
function Assert-ActiveVersion([string]$CandidateRoot, [string]$Expected) {
    $statePath = Join-Path $CandidateRoot '.facm\state\active.json'
    Require (Test-Path -LiteralPath $statePath -PathType Leaf) "active.json missing: $CandidateRoot"
    $state = Get-Content -Raw -LiteralPath $statePath | ConvertFrom-Json
    Require ([string]$state.activeVersion -ceq $Expected) "Unexpected active version: $($state.activeVersion)"
    return $state
}
function Read-Events([string]$CandidateRoot) {
    $path = Join-Path $CandidateRoot '.facm\logs\bootstrapper.jsonl'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return @() }
    return @(Get-Content -LiteralPath $path | ForEach-Object { $_ | ConvertFrom-Json })
}
function Read-Requests([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return @() }
    return @(Get-Content -LiteralPath $Path | Where-Object { $_.Trim() } | ForEach-Object { $_ | ConvertFrom-Json })
}
function Get-PreparedPackage([string]$PreparedRoot, [string]$ComponentId) {
    $application = Get-Content -Raw -LiteralPath (Join-Path $PreparedRoot 'manifest.json') | ConvertFrom-Json
    $releaseIndex = Get-Content -Raw -LiteralPath (Join-Path $PreparedRoot 'release-index.json') | ConvertFrom-Json
    $appComponent = @($application.components | Where-Object { [string]$_.componentId -ceq $ComponentId }) | Select-Object -First 1
    Require ($null -ne $appComponent) "Application component missing: $ComponentId"
    $releaseComponent = @($releaseIndex.components | Where-Object { [string]$_.componentId -ceq $ComponentId }) | Select-Object -First 1
    Require ($null -ne $releaseComponent) "Release index component missing: $ComponentId"
    $packageRelative = [string]$releaseComponent.packagePath
    $packagePath = Get-LocalPath $PreparedRoot $packageRelative
    Require (Test-Path -LiteralPath $packagePath -PathType Leaf) "Prepared package missing: $packagePath"
    $package = Get-Item -LiteralPath $packagePath
    return [ordered]@{
        componentId = $ComponentId
        version = [string]$appComponent.version
        packagePath = $packagePath
        packageName = [IO.Path]::GetFileName($packageRelative)
        packageSize = [uint64]$package.Length
        packageSha256 = Get-Sha256 $packagePath
    }
}
function Corrupt-OneByte([string]$Path) {
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        $value = $stream.ReadByte()
        Require ($value -ge 0) "Cannot corrupt empty file: $Path"
        $stream.Position = 0
        $stream.WriteByte([byte]($value -bxor 0xff))
        $stream.Flush($true)
    } finally { $stream.Dispose() }
}
function Seed-OldActive([string]$CandidateRoot, [string]$Root) {
    $seed = Join-Path $Root 'seed-old'
    New-Item -ItemType Directory -Force -Path $seed | Out-Null
    Copy-Item -LiteralPath (Join-Path $CandidateRoot 'FACM.exe') -Destination (Join-Path $seed 'FACM.App.exe') -Force
    Copy-Item -LiteralPath (Join-Path $CandidateRoot 'FACM.exe') -Destination (Join-Path $seed 'FACM.App.dll') -Force
    Require ((Invoke-Boot $CandidateRoot @('--provision-source',$seed,'--version','3.5.15','--dry-run','--no-ui')) -eq 0) 'Unable to seed old active version.'
}
function New-ScenarioPorts {
    $primary = Get-Random -Minimum 20000 -Maximum 30000
    $mirror = Get-Random -Minimum 30001 -Maximum 40000
    return @($primary, $mirror)
}

$BundleRoot = Assert-DProject2Path $BundleRoot 'BundleRoot'
$Bootstrapper = Assert-DProject2Path $Bootstrapper 'Bootstrapper'
$LocalValidationKeyPath = Assert-DProject2Path $LocalValidationKeyPath 'LocalValidationKeyPath'
$TestRoot = Assert-DProject2Path $TestRoot 'TestRoot'
$Node = (Get-Command node -ErrorAction Stop).Source
$serverScript = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'Start-FacmBoot3CHttpsOrigin.js'
Require (Test-Path -LiteralPath (Join-Path $BundleRoot 'manifest.json') -PathType Leaf) "Bundle root missing: $BundleRoot"
Require (Test-Path -LiteralPath $Bootstrapper -PathType Leaf) "Bootstrapper missing: $Bootstrapper"

if (Test-Path -LiteralPath $TestRoot) { Remove-Item -LiteralPath $TestRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $TestRoot | Out-Null
$tlsDirectory = Join-Path $TestRoot 'tls'
$preparedDirectory = Join-Path $TestRoot 'prepared-origin'
$certificate = $null
$rsa = $null
$origins = @()
$results = [System.Collections.Generic.List[object]]::new()
try {
    $certificate = New-TestCertificate $tlsDirectory
    $rsa = Open-Rsa $LocalValidationKeyPath
    $ports = New-ScenarioPorts
    $distribution = Prepare-Bundle $BundleRoot $preparedDirectory $ports[0] $ports[1] $rsa
    $version = [string]$distribution.version
    Write-Host "Prepared signed local candidate for $version (test key only; no production key used)."

    function Invoke-Scenario([string]$Name, [string]$PrimaryMode, [string]$MirrorMode, [int]$ExpectedExit,
                              [bool]$SeedActive = $false, [bool]$Resume = $false, [bool]$StaleStage = $false,
                              [bool]$SecondRun = $false) {
        $scenarioRoot = Join-Path $TestRoot $Name
        New-Item -ItemType Directory -Force -Path $scenarioRoot | Out-Null
        New-Candidate $scenarioRoot $Bootstrapper "$($distribution.primaryBase)/manifest.json" "$($distribution.mirrorBase)/manifest.json"
        if ($SeedActive) { Seed-OldActive $scenarioRoot $scenarioRoot }
        if ($StaleStage) {
            New-Item -ItemType Directory -Force -Path (Join-Path $scenarioRoot ".facm\staging\facm-app-win-x64-$version") | Out-Null
            Set-Content -LiteralPath (Join-Path $scenarioRoot ".facm\staging\facm-app-win-x64-$version\stale.txt") -Value 'stale' -Encoding ascii
        }
        $primaryReady = Join-Path $scenarioRoot 'primary.ready.json'; $mirrorReady = Join-Path $scenarioRoot 'mirror.ready.json'
        $primaryLog = Join-Path $scenarioRoot 'primary.requests.jsonl'; $mirrorLog = Join-Path $scenarioRoot 'mirror.requests.jsonl'
        $localOrigins = @()
        try {
            $localOrigins += Start-Origin $Node $serverScript $preparedDirectory $ports[0] $certificate.key $certificate.cert $primaryReady $primaryLog $PrimaryMode 'http://127.0.0.1:1/unauthorized'
            $localOrigins += Start-Origin $Node $serverScript $preparedDirectory $ports[1] $certificate.key $certificate.cert $mirrorReady $mirrorLog $MirrorMode 'http://127.0.0.1:1/unauthorized'
            $before = Read-Events $scenarioRoot
            $exit = Invoke-Boot $scenarioRoot @('--update','--dry-run','--no-ui')
            Require ($exit -eq $ExpectedExit) "$Name expected exit $ExpectedExit, got $exit"
            if ($ExpectedExit -eq 0) {
                Assert-ActiveVersion $scenarioRoot $version | Out-Null
                $after = Read-Events $scenarioRoot
                if ($SecondRun) {
                    $secondExit = Invoke-Boot $scenarioRoot @('--update','--dry-run','--no-ui')
                    Require ($secondExit -eq 0) "$Name second run expected success, got $secondExit"
                    $second = Read-Events $scenarioRoot
                    Require (@($second | Where-Object event -eq 'component-download-start').Count -eq @($after | Where-Object event -eq 'component-download-start').Count) "$Name downloaded again on second run"
                    Require (@($second | Where-Object event -eq 'component-extraction-complete').Count -eq @($after | Where-Object event -eq 'component-extraction-complete').Count) "$Name extracted again on second run"
                }
            } elseif ($SeedActive) {
                Assert-ActiveVersion $scenarioRoot '3.5.15' | Out-Null
            }
            if ($Resume) {
                $partial = @(Get-ChildItem -LiteralPath (Join-Path $scenarioRoot '.facm\cache\downloads') -Filter '*.partial' -File -ErrorAction SilentlyContinue)
                Require ($partial.Count -gt 0) "$Name did not preserve an incomplete .partial package"
                Stop-Origin $localOrigins[0]; Stop-Origin $localOrigins[1]; $localOrigins = @()
                $localOrigins += Start-Origin $Node $serverScript $preparedDirectory $ports[0] $certificate.key $certificate.cert $primaryReady $primaryLog 'normal' ''
                $localOrigins += Start-Origin $Node $serverScript $preparedDirectory $ports[1] $certificate.key $certificate.cert $mirrorReady $mirrorLog 'normal' ''
                $resumeExit = Invoke-Boot $scenarioRoot @('--update','--dry-run','--no-ui')
                Require ($resumeExit -eq 0) "$Name resume expected success, got $resumeExit"
                Assert-ActiveVersion $scenarioRoot $version | Out-Null
            }
            [void]$results.Add([ordered]@{ name=$Name; status='PASS'; expectedExit=$ExpectedExit; primaryMode=$PrimaryMode; mirrorMode=$MirrorMode })
            Write-Host "${Name}: PASS"
        } finally {
            foreach ($origin in $localOrigins) { Stop-Origin $origin }
        }
    }

    Invoke-Scenario 'primary-success-and-idempotent' 'normal' 'normal' 0 $false $false $true $true
    Invoke-Scenario 'primary-unavailable-mirror-success' 'unavailable' 'normal' 0
    Invoke-Scenario 'primary-corrupt-package-mirror-success' 'corrupt-package' 'normal' 0
    Invoke-Scenario 'corrupt-mirror-fails-preserving-active' 'unavailable' 'corrupt-package' 14 $true
    Invoke-Scenario 'incomplete-partial-resumes' 'truncate-package' 'truncate-package' 14 $false $true
    Invoke-Scenario 'redirect-never-followed' 'redirect' 'redirect' 14

    function Invoke-FullSizePartialScenario([string]$Name, [bool]$Invalid) {
        $scenarioRoot = Join-Path $TestRoot $Name
        New-Item -ItemType Directory -Force -Path $scenarioRoot | Out-Null
        New-Candidate $scenarioRoot $Bootstrapper "$($distribution.primaryBase)/manifest.json" "$($distribution.mirrorBase)/manifest.json"
        $component = Get-PreparedPackage $preparedDirectory 'facm-app-win-x64'
        $partialPath = Join-Path $scenarioRoot ".facm\cache\downloads\$($component.packageName).partial"
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $partialPath) | Out-Null
        Copy-Item -LiteralPath $component.packagePath -Destination $partialPath -Force
        if ($Invalid) { Corrupt-OneByte $partialPath }
        $partialFile = Get-Item -LiteralPath $partialPath
        Require ([uint64]$partialFile.Length -eq $component.packageSize) "$Name did not seed a full-size partial"
        if (-not $Invalid) { Require ((Get-Sha256 $partialPath) -ceq $component.packageSha256) "$Name did not seed the authenticated full package" }

        $primaryReady = Join-Path $scenarioRoot 'primary.ready.json'; $mirrorReady = Join-Path $scenarioRoot 'mirror.ready.json'
        $primaryLog = Join-Path $scenarioRoot 'primary.requests.jsonl'; $mirrorLog = Join-Path $scenarioRoot 'mirror.requests.jsonl'
        $localOrigins = @()
        try {
            $localOrigins += Start-Origin $Node $serverScript $preparedDirectory $ports[0] $certificate.key $certificate.cert $primaryReady $primaryLog 'normal' ''
            $localOrigins += Start-Origin $Node $serverScript $preparedDirectory $ports[1] $certificate.key $certificate.cert $mirrorReady $mirrorLog 'normal' ''
            $exit = Invoke-Boot $scenarioRoot @('--update','--dry-run','--no-ui')
            Require ($exit -eq 0) "$Name expected success, got $exit"
            Assert-ActiveVersion $scenarioRoot $version | Out-Null
            $packageRequests = @((Read-Requests $primaryLog) + (Read-Requests $mirrorLog) | Where-Object { $_.path -eq "/$($component.packageName)" })
            $completePath = Join-Path $scenarioRoot ".facm\cache\downloads\$($component.packageName)"
            Require (Test-Path -LiteralPath $completePath -PathType Leaf) "$Name did not create the complete package cache"
            Require ((Get-Item -LiteralPath $completePath).Length -eq $component.packageSize) "$Name complete cache size mismatch"
            Require ((Get-Sha256 $completePath) -ceq $component.packageSha256) "$Name complete cache hash mismatch"
            Require (-not (Test-Path -LiteralPath $partialPath -PathType Leaf)) "$Name left the full-size partial behind"
            $events = Read-Events $scenarioRoot
            if ($Invalid) {
                Require (@($events | Where-Object event -eq 'component-partial-full-size-invalid').Count -eq 1) "$Name did not reject the invalid full-size partial"
                Require ($packageRequests.Count -ge 1) "$Name did not download a replacement package"
                Require (@($packageRequests | Where-Object { $null -eq $_.range }).Count -ge 1) "$Name replacement package did not start from byte zero"
            } else {
                Require (@($events | Where-Object event -eq 'component-partial-full-size-recovered').Count -eq 1) "$Name did not record full-size partial recovery"
                Require ($packageRequests.Count -eq 0) "$Name re-downloaded the already-complete package"
            }
            [void]$results.Add([ordered]@{
                name=$Name; status='PASS'; expectedExit=0; fullSizePartial=$true; invalidPartial=$Invalid
                packageName=$component.packageName; packageBytes=$component.packageSize; packageRequests=$packageRequests.Count
            })
            Write-Host "${Name}: PASS"
        } finally { foreach ($origin in $localOrigins) { Stop-Origin $origin } }
    }

    Invoke-FullSizePartialScenario 'full-size-valid-partial-recovery' $false
    Invoke-FullSizePartialScenario 'full-size-invalid-partial-restart' $true

    $rollbackRoot = Join-Path $TestRoot 'local-rollback'
    New-Item -ItemType Directory -Force -Path $rollbackRoot | Out-Null
    New-Candidate $rollbackRoot $Bootstrapper "$($distribution.primaryBase)/manifest.json" "$($distribution.mirrorBase)/manifest.json"
    Seed-OldActive $rollbackRoot $rollbackRoot
    $rollbackPrimaryReady = Join-Path $rollbackRoot 'primary.ready.json'; $rollbackMirrorReady = Join-Path $rollbackRoot 'mirror.ready.json'
    $rollbackOrigins = @()
    try {
        $rollbackOrigins += Start-Origin $Node $serverScript $preparedDirectory $ports[0] $certificate.key $certificate.cert $rollbackPrimaryReady (Join-Path $rollbackRoot 'primary.requests.jsonl') 'normal' ''
        $rollbackOrigins += Start-Origin $Node $serverScript $preparedDirectory $ports[1] $certificate.key $certificate.cert $rollbackMirrorReady (Join-Path $rollbackRoot 'mirror.requests.jsonl') 'normal' ''
        Require ((Invoke-Boot $rollbackRoot @('--update','--dry-run','--no-ui')) -eq 0) 'Forward update for rollback scenario failed.'
        Assert-ActiveVersion $rollbackRoot $version | Out-Null
        Require ((Invoke-Boot $rollbackRoot @('--activate-version','3.5.15','--dry-run','--no-ui')) -eq 0) 'Local rollback failed.'
        Assert-ActiveVersion $rollbackRoot '3.5.15' | Out-Null
        [void]$results.Add([ordered]@{ name='local-rollback-policy'; status='PASS' })
        Write-Host 'local-rollback-policy: PASS'
    } finally { foreach ($origin in $rollbackOrigins) { Stop-Origin $origin } }

    $drive = [IO.DriveInfo]::new((Split-Path -Qualifier $TestRoot))
    $tooMuch = [uint64]$drive.AvailableFreeSpace + 1
    $enough = [uint64]([int64]$drive.AvailableFreeSpace - 256MB)
    $diagnosticRoot = Join-Path $TestRoot 'disk-space-diagnostic'
    New-Candidate $diagnosticRoot $Bootstrapper "$($distribution.primaryBase)/manifest.json" "$($distribution.mirrorBase)/manifest.json"
    Require ((Invoke-Boot $diagnosticRoot @('--check-disk-space',"$tooMuch",'--no-ui')) -eq 15) 'Low-disk diagnostic did not reject insufficient space.'
    Require ((Invoke-Boot $diagnosticRoot @('--check-disk-space',"$enough",'--no-ui')) -eq 0) 'Disk-space diagnostic rejected available space.'
    [void]$results.Add([ordered]@{ name='disk-space-guard'; status='PASS' })
    Write-Host 'disk-space-guard: PASS'

    Write-ExactJson (Join-Path $TestRoot 'results.json') ([ordered]@{
        schemaVersion=1; harness='FACM BOOT3-C HTTPS distribution'; generatedAtUtc=[DateTime]::UtcNow.ToString('o')
        candidateVersion=$version; trustMaterial='local validation key outside repository; no production private key'
        tlsTrust='CurrentUser Root test certificate; removed in finally'; scenarios=@($results)
    })
    Write-Host 'FACM BOOT3-C production-like HTTPS distribution tests: SUCCESS'
} finally {
    foreach ($origin in $origins) { Stop-Origin $origin }
    if ($null -ne $rsa) { $rsa.Dispose() }
    if ($null -ne $certificate) {
        try { Remove-TestCertificate $certificate.thumbprint } catch { }
        foreach ($path in @($certificate.key, $certificate.cert)) { if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue } }
    }
}
