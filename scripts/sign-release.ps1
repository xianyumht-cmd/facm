param(
    [Parameter(Mandatory = $true)]
    [string]$InputDirectory,

    [Parameter(Mandatory = $true)]
    [string]$CertificateThumbprint,

    [Parameter(Mandatory = $true)]
    [string]$TimestampUrl,

    [ValidateSet("CurrentUser", "LocalMachine")]
    [string]$StoreLocation = "CurrentUser"
)

$ErrorActionPreference = "Stop"
$inputPath = (Resolve-Path -LiteralPath $InputDirectory).Path
$thumbprint = ($CertificateThumbprint -replace "\s", "").ToUpperInvariant()

function Find-SignTool {
    $command = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $kitRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    if (Test-Path -LiteralPath $kitRoot) {
        $candidate = Get-ChildItem -LiteralPath $kitRoot -Directory |
            Sort-Object Name -Descending |
            ForEach-Object { Join-Path $_.FullName "x64\signtool.exe" } |
            Where-Object { Test-Path -LiteralPath $_ } |
            Select-Object -First 1
        if ($candidate) {
            return $candidate
        }
    }

    throw "signtool.exe was not found. Install the Windows SDK."
}

$certificatePath = "Cert:\$StoreLocation\My\$thumbprint"
if (-not (Test-Path -LiteralPath $certificatePath)) {
    throw "Code-signing certificate was not found at $certificatePath"
}

$signTool = Find-SignTool
$files = Get-ChildItem -LiteralPath $inputPath -Recurse -File |
    Where-Object { $_.Extension -in @(".exe", ".dll") } |
    Sort-Object FullName

if (-not $files) {
    throw "No .exe or .dll files were found under $inputPath"
}

Write-Host "============================================================"
Write-Host "FACM Authenticode signing"
Write-Host "Input       : $inputPath"
Write-Host "Certificate : $thumbprint"
Write-Host "Store       : $StoreLocation\My"
Write-Host "Timestamp   : $TimestampUrl"
Write-Host "SignTool    : $signTool"
Write-Host "Files       : $($files.Count)"
Write-Host "============================================================"

foreach ($file in $files) {
    $arguments = @(
        "sign",
        "/v",
        "/fd", "SHA256",
        "/sha1", $thumbprint,
        "/s", "My",
        "/tr", $TimestampUrl,
        "/td", "SHA256"
    )
    if ($StoreLocation -eq "LocalMachine") {
        $arguments += "/sm"
    }
    $arguments += $file.FullName

    Write-Host "[SIGN] $($file.FullName)"
    & $signTool @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "SignTool failed for $($file.FullName) with exit code $LASTEXITCODE"
    }
}

foreach ($file in $files) {
    Write-Host "[VERIFY] $($file.FullName)"
    & $signTool verify /pa /all /v $file.FullName
    if ($LASTEXITCODE -ne 0) {
        throw "Signature verification failed for $($file.FullName)"
    }
}

Write-Host ""
Write-Host "All files were signed and verified successfully."
