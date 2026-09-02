[CmdletBinding()]
param(
    [string]$CandidatePath = '',
    [ValidateSet('Windows10-22H2', 'Controlled-Windows11', 'General')]
    [string]$Target = 'General',
    [string]$OutputDirectory = 'D:\project2\facm-boot3c-real-machine-evidence-20260831',
    [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Require([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Write-ExactJson([string]$Path, [object]$Value) {
    $json = $Value | ConvertTo-Json -Depth 30
    [IO.File]::WriteAllText($Path, $json + "`n", [Text.UTF8Encoding]::new($false))
}
function Assert-DProject2Path([string]$Path, [string]$Label) {
    $full = [IO.Path]::GetFullPath($Path)
    Require ($full.StartsWith('D:\project2\', [StringComparison]::OrdinalIgnoreCase) -and $full -ne 'D:\project2') "$Label must remain under D:\project2: $full"
    return $full
}
function New-AcceptanceMatrix([string]$TargetName) {
    $cases = @(
        'clean-directory', 'standard-user-uac', 'fresh-provisioning', 'orb-launch', 'second-launch-no-download',
        'offline-launch', 'app-only-install', 'runtime-only-install', 'failed-package', 'interrupted-resume',
        'rollback', 'low-disk', 'defender', 'smartscreen', 'shutdown-relaunch', 'data-root-persistence'
    )
    return @($cases | ForEach-Object {
        [ordered]@{
            id=$_; status='manual_required'; target=$TargetName; observedAtUtc=$null; evidenceFiles=@()
            passCriteria='Record the actual user-visible result and attach a redacted evidence reference.'
        }
    })
}

if ($SelfTest) {
    $matrix = New-AcceptanceMatrix 'General'
    Require ($matrix.Count -eq 16) 'Acceptance matrix count changed unexpectedly.'
    Require (@($matrix | Where-Object { $_.status -eq 'manual_required' }).Count -eq 16) 'Real-machine cases must remain manual-required.'
    Write-Host 'FACM BOOT3-C real-machine harness self-test: SUCCESS'
    exit 0
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent (Split-Path -Parent $scriptRoot)
$collector = Join-Path $repoRoot 'scripts\collect-facm4-real-machine-evidence.ps1'
$output = Assert-DProject2Path $OutputDirectory 'OutputDirectory'
Require (Test-Path -LiteralPath $collector -PathType Leaf) "Canonical evidence collector missing: $collector"
New-Item -ItemType Directory -Force -Path $output | Out-Null
$collectorDirectory = Join-Path $output 'collector'
$collectorArguments = @('-NoLogo','-NoProfile','-ExecutionPolicy','Bypass','-File',$collector,'-Stage','General','-OutputDirectory',$collectorDirectory)
if (-not [string]::IsNullOrWhiteSpace($CandidatePath)) { $collectorArguments += @('-CandidatePath',$CandidatePath) }
$pwsh = (Get-Command pwsh -ErrorAction Stop).Source
& $pwsh @collectorArguments
Require ($LASTEXITCODE -eq 0) "Canonical real-machine collector failed with exit $LASTEXITCODE."
$evidencePath = Join-Path $collectorDirectory 'evidence.json'
Require (Test-Path -LiteralPath $evidencePath -PathType Leaf) "Collector evidence document missing: $evidencePath"
$automatic = Get-Content -Raw -LiteralPath $evidencePath | ConvertFrom-Json
$document = [ordered]@{
    schemaVersion=1; harnessVersion='1.0.0'; target=$Target; createdAtUtc=[DateTime]::UtcNow.ToString('o')
    mode='read-only-evidence-and-manual-checklist'; automaticEvidencePath='collector/evidence.json'
    automaticEvidence=$automatic.machine; candidate=$automatic.candidate
    acceptanceMatrix=New-AcceptanceMatrix $Target
    statusPolicy=[ordered]@{
        PASS_LOCAL_AUTOMATED='Only for deterministic local checks recorded by the repository test harness.'
        PASS_PRODUCTION_LIKE_HTTPS='Only for the controlled TLS origin/mirror test; not a production CDN claim.'
        PASS_REAL_MACHINE='Requires reviewed evidence from the named real Windows target.'
        BLOCKED_EXTERNAL_SIGNER='Production private-key signing response is not present in this repository.'
        BLOCKED_RELEASE_OWNER_AUTHORIZATION='Release owner has not authorized publication/landing.'
        BLOCKED_PRODUCTION_INFRA='Production CDN/DNS/HSM/publishing controls are not available to this task.'
        NOT_RUN_GATE13='Formal P7/Gate13 is intentionally outside BOOT3-C.'
    }
    privacy=[ordered]@{ containsSecrets=$false; containsPrivateKeys=$false; manualEvidenceMustBeRedacted=$true }
}
$outputJson = Join-Path $output 'boot3c-acceptance.json'
Write-ExactJson $outputJson $document
Write-Host 'FACM BOOT3-C real-machine harness: SUCCESS'
Write-Host "Acceptance document: $outputJson"
Write-Host 'All real-machine acceptance cases remain manual_required until reviewed on the named target.'
