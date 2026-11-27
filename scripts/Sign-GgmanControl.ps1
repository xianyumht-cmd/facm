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
    if ($item.PSObject.Properties.Name -contains 'package_priority') {
        $priority = $item.package_priority
        if (($priority -isnot [int] -and $priority -isnot [long]) -or $priority -lt 0 -or $priority -gt 1000) { throw 'Invalid package priority.' }
    }
    $names[$item.name] = $true; $prefixes[$uri.AbsoluteUri] = $true
}
if (-not @($config.sources | Where-Object enabled).Count) { throw 'At least one enabled route is required.' }
if ($config.PSObject.Properties.Name -contains 'announcement') {
    $a = $config.announcement
    if ($null -eq $a -or $a.enabled -isnot [bool] -or $a.popup -isnot [bool] -or
        $a.level -cnotin @('info','warning','critical') -or
        ([string]$a.id).Length -gt 96 -or ([string]$a.title).Length -gt 120 -or
        ([string]$a.body).Length -gt 2000 -or ([string]$a.updated_at).Length -gt 64 -or
        ([string]$a.link_url).Length -gt 512) { throw 'Invalid control announcement.' }
    if ($a.enabled -and ([string]::IsNullOrWhiteSpace($a.id) -or
        [string]::IsNullOrWhiteSpace($a.title) -or [string]::IsNullOrWhiteSpace($a.body))) { throw 'Incomplete enabled announcement.' }
    if (-not [string]::IsNullOrWhiteSpace($a.link_url)) {
        $link = $null
        if (-not [Uri]::TryCreate($a.link_url,[UriKind]::Absolute,[ref]$link) -or
            $link.Scheme -ne 'https' -or $link.Port -ne 443 -or $link.IsLoopback -or
            $link.HostNameType -ne [UriHostNameType]::Dns -or $link.UserInfo -or $link.Host.IndexOf('.') -lt 0) { throw 'Invalid announcement link.' }
    }
}
if ($config.PSObject.Properties.Name -contains 'public_sources') {
    $known = @('hexdata','aramgg','arammayhem','opgg','tencent-news','communitydragon','akari-catalog','opgg-api','ddragon')
    $seen = @{}
    if (@($config.public_sources).Count -gt 9) { throw 'Too many public source policies.' }
    foreach ($policy in $config.public_sources) {
        if ($policy.id -cnotin $known -or $seen.ContainsKey($policy.id) -or $policy.enabled -isnot [bool] -or
            ($policy.timeout_ms -ne 0 -and ($policy.timeout_ms -lt 500 -or $policy.timeout_ms -gt 5500))) { throw 'Invalid public source policy.' }
        $seen[$policy.id] = $true
    }
}
if ($config.PSObject.Properties.Name -contains 'features') {
    foreach ($flag in $config.features.PSObject.Properties) {
        if ($flag.Name -cnotin @('history_timeline','history_radar','guide_ranked_details','aram_balance') -or $flag.Value -isnot [bool]) { throw 'Invalid feature policy.' }
    }
}
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
