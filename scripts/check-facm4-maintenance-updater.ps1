param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

function Fail([string]$Message) {
    Write-Error $Message
    exit 1
}

function Require-Text([string]$Text, [string]$Needle, [string]$Message) {
    if ($Text -notmatch [regex]::Escape($Needle)) { Fail $Message }
}

function Count-Matches([string]$Text, [string]$Pattern) {
    return @([regex]::Matches($Text, $Pattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)).Count
}

$installerPath = Join-Path $Root 'src/FACM.Infrastructure/Online/HttpPreparedUpdateInstaller.cs'
$identityPath = Join-Path $Root 'src/FACM.Platform.Windows/Runtime/WindowsUpdatePackageIdentityVerifier.cs'
$launcherPath = Join-Path $Root 'src/FACM.Platform.Windows/Runtime/WindowsUpdateReplacementLauncher.cs'
$updaterPath = Join-Path $Root 'src/FACM.Updater/Program.cs'
$downloadSmokePath = Join-Path $Root 'src/FACM.FoundationSmoke/UpdatePackageSmoke.cs'
$preparedSmokePath = Join-Path $Root 'src/FACM.FoundationSmoke/PreparedUpdateInstallerSmoke.cs'
$windowsSmokePath = Join-Path $Root 'src/FACM.WindowsSmoke/MaintenanceWindowsSmoke.cs'
$foundationProgramPath = Join-Path $Root 'src/FACM.FoundationSmoke/Program.cs'
$windowsProgramPath = Join-Path $Root 'src/FACM.WindowsSmoke/Program.cs'

foreach ($path in @(
    $installerPath, $identityPath, $launcherPath, $updaterPath,
    $downloadSmokePath, $preparedSmokePath, $windowsSmokePath,
    $foundationProgramPath, $windowsProgramPath
)) {
    if (-not (Test-Path $path)) { Fail "P6 updater contract file missing: $path" }
}

$installer = Get-Content $installerPath -Raw
$identity = Get-Content $identityPath -Raw
$launcher = Get-Content $launcherPath -Raw
$updater = Get-Content $updaterPath -Raw
$downloadSmoke = Get-Content $downloadSmokePath -Raw
$preparedSmoke = Get-Content $preparedSmokePath -Raw
$windowsSmoke = Get-Content $windowsSmokePath -Raw
$foundationProgram = Get-Content $foundationProgramPath -Raw
$windowsProgram = Get-Content $windowsProgramPath -Raw

foreach ($required in @(
    'MaximumUpdateBytes = 512L * 1024L * 1024L',
    'HeaderTimeout = TimeSpan.FromSeconds(10)',
    'InactivityTimeout = TimeSpan.FromSeconds(20)',
    'HttpUpdateManifestSource.IsValidManifest(manifest)',
    'ComputeSha256(temporary)',
    '_identityVerifier.Validate(temporary, version)',
    '_identityVerifier.Validate(receipt.Path, receipt.Version)',
    'Receipt(fullPath, version, actualHash, length)',
    'receipt-missing', 'receipt-mismatch', 'package-length-changed',
    'package-hash-changed', 'package-identity-changed',
    '_launcher.StartAsync(receipt.Path, receipt.Sha256, receipt.Version'
)) {
    Require-Text $installer $required "Prepared update installer lost security behavior: $required"
}
if ((Count-Matches $installer '_identityVerifier\.Validate\(') -ne 2) {
    Fail 'Prepared update installer must identity-verify exactly after download and immediately before replacement.'
}
foreach ($forbidden in @('Process\.Start', 'Verb\s*=\s*"runas"', 'cmd\.exe', 'powershell\.exe', 'Registry')) {
    if ($installer -match $forbidden) { Fail "Infrastructure update installer crossed Windows process/elevation boundary: $forbidden" }
}

foreach ($required in @(
    'currentSigner.GetCertHashString()', 'candidateSigner.GetCertHashString()',
    'WinVerifyTrust', 'WinTrustActionGenericVerifyV2', 'SameReleaseVersion',
    'FileVersionInfo.GetVersionInfo(candidatePath).FileVersion',
    'X509Certificate.CreateFromSignedFile(path)',
    '#pragma warning disable SYSLIB0057', '#pragma warning restore SYSLIB0057',
    'CertEUntrustedRoot', 'CertEChaining'
)) {
    Require-Text $identity $required "Windows update identity verifier lost release-identity behavior: $required"
}
if ((Count-Matches $identity '#pragma\s+warning\s+disable\s+SYSLIB0057') -ne 1 -or
    (Count-Matches $identity '#pragma\s+warning\s+restore\s+SYSLIB0057') -ne 1) {
    Fail 'SYSLIB0057 suppression must remain narrowly paired around Authenticode signer extraction.'
}
foreach ($forbidden in @('Process\.Start', 'HttpClient', 'HttpRequestMessage', 'Registry')) {
    if ($identity -match $forbidden) { Fail "Identity verifier crossed its certificate/trust boundary: $forbidden" }
}

foreach ($required in @(
    'UpdaterResourceName = "FACM.Platform.Windows.Resources.FACM.Updater.exe"',
    'UpdaterFileName = "FACM.Updater.exe"', 'IsUnderDirectory(source, updatesDirectory)',
    'ComputeSha256(source)', 'ReadEmbeddedUpdaterPayload', 'ExtractUpdater',
    'BuildUpdaterArguments(parentPid, source, destination, expectedSha256)',
    'UseShellExecute = true', 'Verb = "runas"', 'NativeErrorCode == 1223'
)) {
    Require-Text $launcher $required "Windows update launcher lost controlled UAC/helper behavior: $required"
}
foreach ($forbidden in @('Process\.Kill', 'taskkill', 'cmd\.exe', 'powershell\.exe', 'schtasks', 'sc\.exe')) {
    if ($launcher -match $forbidden) { Fail "Windows update launcher gained a forbidden process/shell path: $forbidden" }
}

foreach ($required in @(
    'WaitForParentExit(parentPid, TimeSpan.FromSeconds(120))',
    'ReplaceFiles(source, destination, expectedHash)',
    'File.Replace(staging, destination, backup, true)', 'FallbackReplace', 'TryRollback',
    'ComputeSha256(source)', 'ComputeSha256(staging)', 'ComputeSha256(destination)',
    'restarted.WaitForExit(5000)', 'TryRestartRollback(destination)',
    'TryDelete(source)', 'TryDelete(backup)',
    'string.Equals(updaterDirectory, sourceDirectory, StringComparison.OrdinalIgnoreCase)'
)) {
    Require-Text $updater $required "FACM updater helper lost bounded replacement/rollback behavior: $required"
}
foreach ($forbidden in @('Process\.Kill', 'taskkill', 'cmd\.exe', 'powershell\.exe', 'schtasks', 'sc\.exe')) {
    if ($updater -match $forbidden) { Fail "FACM updater helper gained a forbidden takeover/shell path: $forbidden" }
}

foreach ($required in @(
    'ValidateVerifiedDownloadAsync', 'ValidateBadHashNeverReplacesPackageAsync',
    'ValidateOversizedHeaderIsRejectedAsync', 'MaximumUpdateBytes + 1'
)) {
    Require-Text $downloadSmoke $required "Update package smoke is missing: $required"
}
foreach ($required in @(
    'FakeIdentityVerifier', 'verifier.Calls == 1', 'verifier.Calls == 2', 'verifier.Calls == 3',
    'package-length-changed', 'package-hash-changed', 'receipt-missing',
    'Replacement launcher ran for a tampered package'
)) {
    Require-Text $preparedSmoke $required "Prepared update smoke is missing: $required"
}
foreach ($required in @(
    'ValidateControlledUpdaterLaunchAsync', 'observed!.UseShellExecute && observed.Verb == "runas"',
    'RuntimePathLayout.UpdatesDirectory', 'Rejected outside package still reached Process.Start',
    'BaseDirectory'
)) {
    Require-Text $windowsSmoke $required "Windows updater smoke is missing: $required"
}

if ((Count-Matches $foundationProgram 'UpdatePackageSmoke\.RunAsync') -ne 1) {
    Fail 'Foundation smoke must execute UpdatePackageSmoke exactly once.'
}
if ((Count-Matches $foundationProgram 'PreparedUpdateInstallerSmoke\.RunAsync') -ne 1) {
    Fail 'Foundation smoke must execute PreparedUpdateInstallerSmoke exactly once.'
}
if ((Count-Matches $windowsProgram 'MaintenanceWindowsSmoke\.RunAsync') -ne 1) {
    Fail 'Windows smoke must execute MaintenanceWindowsSmoke exactly once.'
}

Write-Host 'P6 updater download: fixed validated manifest + 512 MiB cap + bounded header/inactivity timeouts'
Write-Host 'P6 updater receipt: SHA/length/version/path bound + identity verification after download and before launch'
Write-Host 'P6 updater identity: same Authenticode signer + WinVerifyTrust + manifest release version'
Write-Host 'P6 updater UAC: controlled embedded helper + updates-directory source + UAC cancel keeps FACM alive'
Write-Host 'P6 updater apply: 120s parent wait + staging/backup + hash checks + early-start rollback'
Write-Host 'P6 updater smoke: download/tamper/receipt/identity/UAC-launch boundaries execute in CI'
Write-Host 'FACM 4.0 P6 updater replacement contract: SUCCESS'
