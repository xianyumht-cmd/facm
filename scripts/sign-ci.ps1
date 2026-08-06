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

    # A public CA certificate is already trusted by Windows. A self-signed certificate
    # must be trusted temporarily on this disposable runner so Authenticode verification
    # can distinguish an invalid signature from an intentionally self-signed chain.
    if ($certificate.Subject -eq $certificate.Issuer) {
        foreach ($storeName in @('Root', 'TrustedPublisher')) {
            $store = [System.Security.Cryptography.X509Certificates.X509Store]::new(
                $storeName,
                [System.Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser
            )
            try {
                $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
                $alreadyPresent = $store.Certificates |
                    Where-Object Thumbprint -eq $certificate.Thumbprint |
                    Select-Object -First 1
                if (-not $alreadyPresent) {
                    $store.Add($certificate)
                }
            }
            finally {
                $store.Close()
                $store.Dispose()
            }
        }
        Write-Host "Temporarily trusted self-signed certificate $($certificate.Thumbprint) on this runner."
    }

    $arguments = @(
        'sign',
        '/fd', 'SHA256',
        '/td', 'SHA256',
        '/tr', 'http://timestamp.digicert.com',
        '/f', $pfxPath
    )
    if (-not [string]::IsNullOrEmpty($PfxPassword)) {
        $arguments += @('/p', $PfxPassword)
    }
    $arguments += $ExePath

    & $signtool.FullName @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "signtool sign failed with exit code $LASTEXITCODE"
    }

    & $signtool.FullName verify /pa /all /v $ExePath
    if ($LASTEXITCODE -ne 0) {
        throw "signtool verify failed with exit code $LASTEXITCODE"
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $ExePath
    if ($signature.Status -ne 'Valid') {
        throw "PowerShell Authenticode verification failed: $($signature.Status) - $($signature.StatusMessage)"
    }

    Write-Host "Authenticode signature is valid. Signer: $($certificate.Subject)"
}
finally {
    Remove-Item -LiteralPath $pfxPath -Force -ErrorAction SilentlyContinue
}
