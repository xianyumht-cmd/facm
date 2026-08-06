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
$temporarilyAddedStores = New-Object System.Collections.Generic.List[string]

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
    Write-Host "Self-signed: $isSelfSigned"

    # Public CA certificates are already trusted by Windows. A self-signed certificate
    # is trusted only on this disposable runner so validation can verify the signature.
    if ($isSelfSigned) {
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
                    $temporarilyAddedStores.Add($storeName)
                }
            }
            finally {
                $store.Close()
                $store.Dispose()
            }
        }
        Write-Host "Temporarily trusted self-signed certificate $($certificate.Thumbprint) on this runner."
    }

    $arguments = @('sign', '/fd', 'SHA256')

    # A public CA release certificate benefits from an RFC 3161 timestamp. For the
    # repository's self-signed development certificate, avoid an external timestamp
    # request because it adds no public trust and can block for several minutes.
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

    Write-Host 'Verifying Authenticode signature with signtool...'
    & $signtool.FullName verify /pa /all /v $ExePath
    if ($LASTEXITCODE -ne 0) {
        throw "signtool verify failed with exit code $LASTEXITCODE"
    }

    Write-Host 'Verifying Authenticode signature with PowerShell...'
    $signature = Get-AuthenticodeSignature -LiteralPath $ExePath
    if ($signature.Status -ne 'Valid') {
        throw "PowerShell Authenticode verification failed: $($signature.Status) - $($signature.StatusMessage)"
    }

    Write-Host "Authenticode signature is valid. Signer: $($certificate.Subject)"
}
finally {
    if ($null -ne $certificate) {
        foreach ($storeName in $temporarilyAddedStores) {
            $store = [System.Security.Cryptography.X509Certificates.X509Store]::new(
                $storeName,
                [System.Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser
            )
            try {
                $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
                $matches = $store.Certificates.Find(
                    [System.Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint,
                    $certificate.Thumbprint,
                    $false
                )
                foreach ($match in $matches) {
                    $store.Remove($match)
                }
            }
            finally {
                $store.Close()
                $store.Dispose()
            }
        }
        $certificate.Dispose()
    }

    Remove-Item -LiteralPath $pfxPath -Force -ErrorAction SilentlyContinue
}