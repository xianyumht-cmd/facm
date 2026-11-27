# Full public package audit. Never executes a downloaded file.
[CmdletBinding()]
param(
    [string]$NetworkLabel = 'unspecified',
    [ValidateRange(1,3)][int]$Rounds = 1,
    [string]$OutputDirectory = (Join-Path $env:TEMP ('GGman-PackageRoutes-' + (Get-Date -Format 'yyyyMMdd-HHmmss')))
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$manifest = Get-Content -Raw -LiteralPath (Join-Path $root 'online/version.json') | ConvertFrom-Json
$control = Get-Content -Raw -LiteralPath (Join-Path $root 'online/control.json') | ConvertFrom-Json
if ($manifest.sha256 -notmatch '^[a-fA-F0-9]{64}$' -or $manifest.version -notmatch '^\d+\.\d+\.\d+$' -or
    $manifest.download_url -notmatch '^https://github\.com/xianyumht-cmd/facm/releases/download/v[0-9.]+/(FACM|GGman)\.exe$') {
    throw 'Invalid release manifest.'
}
$expectedSigner = 'A5E4FC54FBD6B5EC2E1002D3DD2E465D533B3568'
$targets = @()
foreach ($source in $control.sources) {
    if (-not $source.enabled) { continue }
    $uri = [uri]$source.prefix
    if ($uri.Scheme -ne 'https' -or $uri.IsLoopback -or $uri.Port -ne 443 -or
        $uri.HostNameType -ne [UriHostNameType]::Dns -or $uri.UserInfo -or $uri.Query -or $uri.Fragment -or $uri.AbsolutePath -ne '/') {
        throw 'Invalid proxy origin.'
    }
    $targets += @{name=$source.name; url=($source.prefix.TrimEnd('/') + '/' + $manifest.download_url)}
}
$targets += @{name='github-direct'; url=$manifest.download_url}
$curl = Join-Path $env:SystemRoot 'System32/curl.exe'
if (-not (Test-Path -LiteralPath $curl -PathType Leaf)) {
    $curl = (Get-Command curl.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
}
if (-not (Test-Path -LiteralPath $curl -PathType Leaf)) { throw 'No usable curl.exe found.' }
Write-Host "Using curl: $curl"
& $curl --version
if ($LASTEXITCODE -ne 0) { throw 'curl preflight failed.' }
# Expected network errors are recorded per route, not terminating script errors.
$PSNativeCommandUseErrorActionPreference = $false
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$csv = Join-Path $OutputDirectory 'package-routes.csv'
$temporary = Join-Path $OutputDirectory ('candidate-' + [Guid]::NewGuid().ToString('N') + '.exe')
$format = '%{http_code}|%{time_starttransfer}|%{time_total}|%{size_download}|%{speed_download}|%{num_redirects}'
try {
    foreach ($round in 1..$Rounds) {
        foreach ($target in $targets) {
            Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
            $raw = & $curl -q --silent --noproxy '*' --location --max-redirs 5 --proto '=https' --proto-redir '=https' --connect-timeout 10 --max-time 90 --max-filesize 67108864 --user-agent 'GGman-Windows-Updater/3.5' --header 'Accept-Encoding: identity' --output $temporary --write-out $format -- $target.url
            $exitCode = $LASTEXITCODE
            $parts = ([string]$raw).Split('|')
            if ($parts.Count -ne 6) { $parts = @('000','','','','','') }
            $hash = ''; $signer = ''; $signatureStatus = ''; $actualVersion = ''; $evidence = 'download-failed'
            if ($exitCode -eq 0 -and $parts[0] -eq '200' -and (Test-Path -LiteralPath $temporary)) {
                $hash = (Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash
                if ($hash -ne $manifest.sha256) { $evidence = 'hash-mismatch' }
                else {
                    $sig = Get-AuthenticodeSignature -LiteralPath $temporary
                    $signatureStatus = [string]$sig.Status
                    if ($sig.SignerCertificate) { $signer = $sig.SignerCertificate.Thumbprint }
                    $actualVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($temporary).FileVersion
                    $parsedVersion = $null
                    $expectedVersion = [version]$manifest.version
                    $matches = [version]::TryParse($actualVersion,[ref]$parsedVersion) -and
                        $parsedVersion.Major -eq $expectedVersion.Major -and
                        $parsedVersion.Minor -eq $expectedVersion.Minor -and $parsedVersion.Build -eq $expectedVersion.Build
                    $acceptableTrust = $sig.Status -eq 'Valid' -or ($sig.Status -eq 'NotTrusted' -and $sig.SignerCertificate -and $sig.SignerCertificate.Subject -eq $sig.SignerCertificate.Issuer)
                    $evidence = if ($signer -eq $expectedSigner -and $matches -and $acceptableTrust) {'verified-package'} else {'hash-match-signer-or-version-unverified'}
                }
            }
            $row = [pscustomobject]@{
                utc=(Get-Date).ToUniversalTime().ToString('o'); network=$NetworkLabel; round=$round; source=$target.name
                version=$manifest.version; curl_exit=$exitCode; http_status=$parts[0]; ttfb_s=$parts[1]; total_s=$parts[2]
                bytes=$parts[3]; bytes_per_second=$parts[4]; redirects=$parts[5]; sha256=$hash; signer=$signer
                signature_status=$signatureStatus; file_version=$actualVersion; evidence=$evidence
            }
            $row | Export-Csv -LiteralPath $csv -NoTypeInformation -Encoding UTF8 -Append
            Write-Host ($row | ConvertTo-Json -Compress)
            Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
        }
    }
} finally { Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue }
Write-Host "Report: $csv"
$results = @(Import-Csv -LiteralPath $csv)
if ($results.Count -ne ($targets.Count * $Rounds)) { throw 'Incomplete package route evidence.' }
if (-not @($results | Where-Object evidence -eq 'verified-package').Count) { throw 'No route delivered a verified package.' }
