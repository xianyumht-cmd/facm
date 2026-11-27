$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$source = Join-Path $PSScriptRoot '../online/control.json'
$bytes = [IO.File]::ReadAllBytes($source)
if ($bytes.Length -gt 16384) { throw 'Control payload too large.' }
$config = [Text.Encoding]::UTF8.GetString($bytes) | ConvertFrom-Json
if ($config.schema -ne 'ggman-control.v1' -or $config.revision -le 0 -or @($config.sources).Count -lt 1 -or @($config.sources).Count -gt 16) { throw 'Invalid control catalog.' }
$names = @{}; $prefixes = @{}
foreach ($item in $config.sources) {
    $uri = $null
    if ($item.name -notmatch '^[a-z0-9][a-z0-9-]{0,47}$' -or $item.name -in @('github','gitee') -or
        -not [Uri]::TryCreate($item.prefix,[UriKind]::Absolute,[ref]$uri) -or $uri.Scheme -ne 'https' -or
        $uri.Port -ne 443 -or $uri.IsLoopback -or $uri.HostNameType -ne [UriHostNameType]::Dns -or
        $uri.UserInfo -or $uri.Query -or $uri.Fragment -or $uri.AbsolutePath -ne '/' -or
        $item.enabled -isnot [bool] -or $item.priority -lt 0 -or $item.priority -gt 1000 -or
        $names.ContainsKey($item.name) -or $prefixes.ContainsKey($uri.AbsoluteUri)) { throw 'Invalid or duplicate control route.' }
    $names[$item.name] = $true; $prefixes[$uri.AbsoluteUri] = $true
}
if (-not @($config.sources | Where-Object enabled).Count) { throw 'At least one enabled route is required.' }
if (-not $env:FACM_PFX_BASE64) { throw 'Release signing secret unavailable.' }
$cert = $null; $rsa = $null
try {
    $cert = [Security.Cryptography.X509Certificates.X509Certificate2]::new(
        [Convert]::FromBase64String($env:FACM_PFX_BASE64), $env:FACM_PFX_PASSWORD,
        [Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
    $rsa = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($cert)
    if ($null -eq $rsa) { throw 'Release certificate does not contain an RSA key.' }
    $signature = $rsa.SignData($bytes, [Security.Cryptography.HashAlgorithmName]::SHA256,
        [Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $public = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPublicKey($cert)
    try {
        if (-not $public.VerifyData($bytes,$signature,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1)) { throw 'Signature self-check failed.' }
    } finally { $public.Dispose() }
    $envelope = @{payload=[Convert]::ToBase64String($bytes);signature=[Convert]::ToBase64String($signature)} | ConvertTo-Json -Compress
    [IO.File]::WriteAllText((Join-Path $env:RUNNER_TEMP 'control.json'),$envelope,[Text.UTF8Encoding]::new($false))
    Write-Host "Signed control revision $($config.revision); signer $($cert.Thumbprint)"
} finally { if ($rsa) { $rsa.Dispose() }; if ($cert) { $cert.Dispose() } }
