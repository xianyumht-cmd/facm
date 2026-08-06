param(
    [string]$PayloadDirectory = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($PayloadDirectory)) {
    $PayloadDirectory = Join-Path $root "src\FACM.App\Payloads"
}

$payloadPath = (Resolve-Path -LiteralPath $PayloadDirectory).Path
$manifestPath = Join-Path $payloadPath "payloads.manifest.json"
if (-not (Test-Path -LiteralPath $manifestPath)) {
    throw "Manifest not found: $manifestPath"
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
foreach ($payload in $manifest.payloads) {
    $filePath = Join-Path $payloadPath $payload.fileName
    if (-not (Test-Path -LiteralPath $filePath -PathType Leaf)) {
        throw "Payload file not found: $filePath"
    }

    $hash = (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash.ToUpperInvariant()
    $payload.sha256 = $hash
    Write-Host "[HASH] $($payload.fileName) $hash"
}

$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
Write-Host "Updated: $manifestPath"
