[CmdletBinding()]
param(
    [string]$OutputDirectory = 'D:\project2\Facm\local-signing',
    [string]$KeyId = 'facm-production-r1'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($OutputDirectory) -or
    [IO.Path]::GetFullPath($OutputDirectory) -eq 'D:\project2') {
    throw 'OutputDirectory must be a specific directory under D:\project2.'
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$privatePath = Join-Path $OutputDirectory 'FACM4-MANIFEST-SIGNING-PRIVATE.pem'
$publicPath = Join-Path $OutputDirectory 'FACM4-MANIFEST-SIGNING-PUBLIC.pem'
$metadataPath = Join-Path $OutputDirectory 'FACM4-MANIFEST-SIGNING-README.txt'

if ((Test-Path -LiteralPath $privatePath) -or (Test-Path -LiteralPath $publicPath)) {
    throw "Refusing to overwrite an existing FACM 4.0 key pair: $OutputDirectory"
}

$rsa = [Security.Cryptography.RSA]::Create(2048)
try {
    [IO.File]::WriteAllText($privatePath, $rsa.ExportPkcs8PrivateKeyPem(), [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText($publicPath, $rsa.ExportSubjectPublicKeyInfoPem(), [Text.UTF8Encoding]::new($false))
    $parameters = $rsa.ExportParameters($false)
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $publicDigest = ([BitConverter]::ToString($sha.ComputeHash($parameters.Modulus)) -replace '-', '').ToLowerInvariant()
    } finally { $sha.Dispose() }
    $metadata = @"
FACM 4.0 detached manifest signing key

Key ID: $KeyId
Algorithm: RSA-2048-PKCS1-SHA256
Public key SHA-256 (modulus): $publicDigest
Generated UTC: $([DateTime]::UtcNow.ToString('o'))

The PRIVATE PEM signs manifest.json and component manifests. Keep it outside Git,
back it up securely, and never paste its contents into chat or release metadata.
The public key is compiled into src/FACM.Bootstrapper/ManifestTrust.cpp.
"@
    [IO.File]::WriteAllText($metadataPath, $metadata, [Text.UTF8Encoding]::new($false))
    Write-Host "Generated FACM 4.0 detached signing key: $KeyId"
    Write-Host "Private key: $privatePath"
    Write-Host "Public key: $publicPath"
    Write-Host "Public modulus SHA-256: $publicDigest"
} finally {
    $rsa.Dispose()
}
