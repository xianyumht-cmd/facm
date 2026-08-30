$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$collectorPath = Join-Path $root 'scripts\collect-facm4-real-machine-evidence.ps1'
$batPath = Join-Path $root 'FACM-4.0-真机证据采集.bat'
$matrixPath = Join-Path $root 'evidence\facm4-release-evidence.json'

foreach ($path in @($collectorPath, $batPath, $matrixPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required real-machine evidence file missing: $path" }
}

$collector = Get-Content -LiteralPath $collectorPath -Raw
$bat = Get-Content -LiteralPath $batPath -Raw

$requiredSlots = @(
    'compat.non-admin-uac-cancel',
    'security.defender-smartscreen',
    'compat.windows-target',
    'display.real-mixed-dpi-multimonitor',
    'accessibility.real-machine',
    'migration.settings-3.5.15-to-4.0',
    'update.interrupted-replacement-rollback',
    'release.final-signature-package'
)
foreach ($id in $requiredSlots) {
    if ($collector -notmatch [regex]::Escape($id)) { throw "Collector evidence slot missing: $id" }
}

if ($collector -notmatch 'manual_required') { throw 'Collector must preserve explicit manual_required evidence states.' }
if ($collector -notmatch '\[switch\]\$SelfTest') { throw 'Collector must expose deterministic -SelfTest.' }
if ($collector -notmatch 'Protect-Text') { throw 'Collector redaction boundary is missing.' }
if ($collector -notmatch 'containsApplicationSecrets') { throw 'Collector privacy declaration is missing.' }
if ($collector -notmatch 'Automatic observations are not release PASS decisions') { throw 'Collector must state automatic observations are not release PASS decisions.' }

$forbiddenCollectorPatterns = @(
    '(?i)Start-Process\s+[^\r\n]*-Verb\s+RunAs',
    '(?i)Set-ItemProperty',
    '(?i)New-ItemProperty',
    '(?i)Remove-Item',
    '(?i)Stop-Process',
    '(?i)Restart-Computer',
    '(?i)shutdown\.exe',
    '(?i)Invoke-WebRequest',
    '(?i)Invoke-RestMethod',
    '(?i)System\.Net\.Http\.HttpClient',
    '(?i)online\\version\.json',
    '(?i)release\\request\.json'
)
foreach ($pattern in $forbiddenCollectorPatterns) {
    if ($collector -match $pattern) { throw "Collector contains forbidden mutation/network/production-control pattern: $pattern" }
}

if ($bat -notmatch '(?i)WindowsPowerShell\\v1\.0\\powershell\.exe') { throw 'One-click BAT must explicitly use Windows PowerShell 5.1.' }
if ($bat -match '(?i)RunAs|Start-Process|powershell\.exe[^\r\n]*-Command[^\r\n]*(Start-Process|RunAs)') { throw 'One-click BAT must not request elevation.' }
if ($bat -notmatch 'collect-facm4-real-machine-evidence\.ps1') { throw 'One-click BAT does not invoke the canonical collector.' }

$diffBase = & git rev-parse --verify 'HEAD^' 2>$null
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($diffBase)) { throw 'Unable to compare real-machine evidence branch with the PR base.' }
$diff = @(& git diff --name-only $diffBase HEAD -- evidence/facm4-release-evidence.json online/version.json release/request.json 2>$null)
if ($LASTEXITCODE -ne 0) { throw 'Unable to compare real-machine evidence branch with the PR base.' }
if ($diff.Count -gt 0) { throw "Real-machine evidence harness must not modify release matrix or production controls: $($diff -join ', ')" }

Write-Host 'Real-machine evidence slots: 8'
Write-Host 'Collector mode: read-only observations + explicit manual_required review'
Write-Host 'PowerShell target: Windows PowerShell 5.1'
Write-Host 'FACM 4.0 real-machine evidence collector contract: SUCCESS'
