param([string]$Root = (Split-Path -Parent $PSScriptRoot))

$ErrorActionPreference = 'Stop'

function Read-Required([string]$RelativePath) {
    $path = Join-Path $Root $RelativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "BOOT3-C contract file missing: $RelativePath" }
    return Get-Content -LiteralPath $path -Raw
}

function Require([string]$Text, [string]$Pattern, [string]$Message) {
    if ($Text -notmatch $Pattern) { throw $Message }
}

function Assert-PowerShellSyntax([string]$RelativePath) {
    $path = Join-Path $Root $RelativePath
    $errors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$null, [ref]$errors) | Out-Null
    if ($errors.Count -gt 0) { throw "PowerShell syntax invalid: $RelativePath" }
}

$native = Read-Required 'src/FACM.Bootstrapper/main.cpp'
$builder = Read-Required 'tools/release/Build-FacmBoot3BRelease.ps1'
$validator = Read-Required 'tools/release/Test-FacmReleaseBundle.ps1'
$origin = Read-Required 'tools/release/Start-FacmBoot3CHttpsOrigin.js'
$distribution = Read-Required 'tools/release/Test-FacmBoot3CHttpsDistribution.ps1'
$realMachine = Read-Required 'tools/release/Test-FacmBoot3CRealMachineHarness.ps1'
$readiness = Read-Required 'docs/BOOT3C-RELEASE-READINESS.md'

foreach ($marker in @(
    'componentManifestMirrors', 'manifestMirrors', 'manifest-fetch-failover', 'manifest-fetch-selected',
    'WINHTTP_OPTION_REDIRECT_POLICY_NEVER', 'provision-disk-space-check', 'provision-disk-space-rejected',
    '--check-disk-space', 'ReadActiveState', 'ReadComponentsState', 'installedSize', 'DownloadUrl'
)) {
    Require $native ([regex]::Escape($marker)) "BOOT3-C native contract marker missing: $marker"
}

foreach ($marker in @('MirrorBaseUrls', 'componentManifestMirrors', 'manifestMirrors')) {
    Require ($builder + $validator) ([regex]::Escape($marker)) "BOOT3-C release metadata marker missing: $marker"
}

foreach ($marker in @('https.createServer', 'Range', 'unavailable', 'redirect', 'corrupt-package', 'truncate-package')) {
    Require $origin ([regex]::Escape($marker)) "BOOT3-C HTTPS origin marker missing: $marker"
}

foreach ($marker in @(
    'primary-unavailable-mirror-success', 'primary-corrupt-package-mirror-success',
    'corrupt-mirror-fails-preserving-active', 'incomplete-partial-resumes',
    'redirect-never-followed', 'local-rollback-policy', 'disk-space-guard',
    'no production key used'
)) {
    Require $distribution ([regex]::Escape($marker)) "BOOT3-C distribution test marker missing: $marker"
}

Require $realMachine 'manual_required' 'BOOT3-C real-machine harness must preserve manual-required status.'
Require $realMachine 'PASS_PRODUCTION_LIKE_HTTPS' 'BOOT3-C real-machine harness must distinguish HTTPS evidence from production PASS.'
Require $realMachine 'BLOCKED_EXTERNAL_SIGNER' 'BOOT3-C real-machine harness must expose the external signer boundary.'
Require $realMachine 'acceptanceMatrix=New-AcceptanceMatrix' 'BOOT3-C real-machine harness must emit the acceptance matrix.'
Require $readiness 'controlled local TLS origin/mirror rehearsal' 'BOOT3-C readiness document must define controlled HTTPS validation.'
Require $readiness 'real-machine' 'BOOT3-C readiness document must define the real-machine boundary.'
Require $readiness 'Gate13' 'BOOT3-C readiness document must preserve the Gate13 boundary.'

foreach ($forbidden in @('NODE_TLS_REJECT_UNAUTHORIZED', 'allowSignatureBypass', 'skipSignature', 'ignoreSignature', '-----BEGIN [A-Z ]*PRIVATE KEY-----')) {
    if (($native + $builder + $validator + $origin + $distribution + $realMachine) -match $forbidden) {
        throw "BOOT3-C implementation contains forbidden trust bypass or private-key material: $forbidden"
    }
}

foreach ($script in @(
    'tools/release/Build-FacmBoot3BRelease.ps1',
    'tools/release/Test-FacmReleaseBundle.ps1',
    'tools/release/Test-FacmBoot3CHttpsDistribution.ps1',
    'tools/release/Test-FacmBoot3CRealMachineHarness.ps1'
)) { Assert-PowerShellSyntax $script }

& node --check (Join-Path $Root 'tools/release/Start-FacmBoot3CHttpsOrigin.js')
if ($LASTEXITCODE -ne 0) { throw 'BOOT3-C HTTPS origin JavaScript syntax check failed.' }

Write-Host 'FACM 4.0 BOOT3-C HTTPS distribution, recovery, disk-space, and real-machine evidence contract: SUCCESS'
