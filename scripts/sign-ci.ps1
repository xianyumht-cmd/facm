param(
    [Parameter(Mandatory = $true)]
    [string]$ExePath,

    [Parameter(Mandatory = $true)]
    [string]$PfxBase64,

    [string]$PfxPassword = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not (Test-Path -LiteralPath $ExePath -PathType Leaf)) {
    throw "EXE not found: $ExePath"
}

$pfxPath = Join-Path $env:RUNNER_TEMP 'facm-signing.pfx'
$certificate = $null

Write-Host 'Preparing signing certificate...'
[IO.File]::WriteAllBytes($pfxPath, [Convert]::FromBase64String($PfxBase64))

try {
    $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if (-not $signtool) {
        throw 'signtool.exe was not found on the runner.'
    }

    $password = if ([string]::IsNullOrEmpty($PfxPassword)) {
        $null
    }
    else {
        ConvertTo-SecureString $PfxPassword -AsPlainText -Force
    }

    $flags = [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet
    $certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
        $pfxPath,
        $password,
        $flags
    )
    $isSelfSigned = $certificate.Subject -eq $certificate.Issuer

    Write-Host "Loaded certificate: $($certificate.Subject)"
    Write-Host "Certificate thumbprint: $($certificate.Thumbprint)"
    Write-Host "Self-signed: $isSelfSigned"

    $arguments = @('sign', '/fd', 'SHA256')

    # A trusted public release certificate receives an RFC 3161 timestamp. The
    # repository's self-signed development certificate deliberately avoids external
    # timestamp services, because they add no public trust and can block for minutes.
    if (-not $isSelfSigned) {
        $arguments += @('/td', 'SHA256', '/tr', 'http://timestamp.digicert.com')
        Write-Host 'Signing with RFC 3161 timestamp...'
    }
    else {
        Write-Host 'Signing self-signed development build without external timestamp...'
    }

    $arguments += @('/f', $pfxPath)
    if (-not [string]::IsNullOrEmpty($PfxPassword)) {
        $arguments += @('/p', $PfxPassword)
    }
    $arguments += $ExePath

    & $signtool.FullName @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "signtool sign failed with exit code $LASTEXITCODE"
    }

    Write-Host 'Checking Authenticode file digest with signtool...'
    $verifyOutput = & $signtool.FullName verify /pa /all /v $ExePath 2>&1
    $verifyExitCode = $LASTEXITCODE
    $verifyText = ($verifyOutput | Out-String)
    $normalizedVerifyText = [regex]::Replace($verifyText, '\s+', ' ').Trim()
    $verifyOutput | ForEach-Object { Write-Host $_ }

    if ($verifyExitCode -ne 0) {
        # SignTool splits this message across multiple lines. Normalize whitespace before
        # matching so the expected self-signed trust warning is not mistaken for a bad
        # Authenticode digest.
        $expectedUntrustedRoot = $isSelfSigned -and
            $normalizedVerifyText -match '(?i)certificate chain processed.*terminated in a root certificate which is not trusted by the trust provider' -and
            $normalizedVerifyText -notmatch '(?i)(no signature found|hash mismatch|bad digest|invalid signature)'

        if (-not $expectedUntrustedRoot) {
            throw "signtool verify failed with exit code $verifyExitCode"
        }

        Write-Host 'The Authenticode digest is intact; the only verification warning is the expected untrusted self-signed root.'
    }

    Write-Host 'Checking embedded signer certificate with PowerShell...'
    $signature = Get-AuthenticodeSignature -LiteralPath $ExePath
    if ($null -eq $signature.SignerCertificate) {
        throw 'PowerShell did not find an embedded signer certificate.'
    }

    if ($signature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint) {
        throw "Signer thumbprint mismatch. Expected $($certificate.Thumbprint), actual $($signature.SignerCertificate.Thumbprint)"
    }

    if ($isSelfSigned) {
        if ($signature.Status -in @('NotSigned', 'HashMismatch', 'NotSupportedFileFormat')) {
            throw "PowerShell Authenticode verification failed: $($signature.Status) - $($signature.StatusMessage)"
        }
        Write-Host "Self-signed Authenticode signature verified. PowerShell trust status: $($signature.Status)"
    }
    elseif ($signature.Status -ne 'Valid') {
        throw "PowerShell Authenticode verification failed: $($signature.Status) - $($signature.StatusMessage)"
    }

    Write-Host "Authenticode signer verified: $($certificate.Subject)"

    # A successfully handled self-signed trust warning leaves signtool's native exit code
    # at 1. Reset it so GitHub Actions does not mark the completed signing step as failed.
    $global:LASTEXITCODE = 0
}
finally {
    if ($null -ne $certificate) {
        $certificate.Dispose()
    }
    Remove-Item -LiteralPath $pfxPath -Force -ErrorAction SilentlyContinue
}
