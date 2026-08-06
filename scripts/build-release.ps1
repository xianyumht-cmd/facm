param(
    [string]$Runtime = "win-x64",
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\FACM.App\FACM.App.csproj"
$output = Join-Path $root ("artifacts\" + $Runtime)
$selfContainedValue = if ($SelfContained.IsPresent) { "true" } else { "false" }

Write-Host "============================================================"
Write-Host "FACM release build"
Write-Host "Project       : $project"
Write-Host "Runtime       : $Runtime"
Write-Host "Self-contained: $selfContainedValue"
Write-Host "Output        : $output"
Write-Host "============================================================"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET 8 SDK was not found. Install it before building FACM."
}

if (Test-Path -LiteralPath $output) {
    Remove-Item -LiteralPath $output -Recurse -Force
}
New-Item -ItemType Directory -Path $output -Force | Out-Null

& dotnet restore $project
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed with exit code $LASTEXITCODE"
}

$publishArguments = @(
    "publish",
    $project,
    "-c", "Release",
    "-r", $Runtime,
    "--self-contained", $selfContainedValue,
    "-p:PublishSingleFile=false",
    "-p:DebugSymbols=false",
    "-p:DebugType=None",
    "-p:ContinuousIntegrationBuild=true",
    "-o", $output
)

& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$hashFile = Join-Path $output "SHA256SUMS.txt"
Get-ChildItem -LiteralPath $output -File -Recurse |
    Where-Object { $_.FullName -ne $hashFile } |
    Sort-Object FullName |
    ForEach-Object {
        $relative = $_.FullName.Substring($output.Length).TrimStart('\')
        $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $relative"
    } | Set-Content -LiteralPath $hashFile -Encoding UTF8

Write-Host ""
Write-Host "Build completed successfully."
Write-Host "Output: $output"
Write-Host "Hashes: $hashFile"
