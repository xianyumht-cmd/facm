#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$config = Join-Path $root 'wrangler.single-owner.toml'
$private = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'GGman-D1-Setup'
New-Item -ItemType Directory -Path $private -Force | Out-Null
$keyPath = Join-Path $private 'Personal-Recovery-Key.txt'
$endpointPath = Join-Path $private 'D1-Endpoint.txt'
if (-not (Get-Command node.exe -ErrorAction SilentlyContinue) -or
    -not (Get-Command npx.cmd -ErrorAction SilentlyContinue)) {
    throw 'Node.js LTS and npm/npx are required for the official Cloudflare Wrangler CLI.'
}
if (-not (Test-Path $config)) { throw 'Missing single-owner Worker configuration.' }
Write-Host 'Cloudflare GGman single-owner setup. New empty D1; no CloudBase export or deletion.'
Write-Host 'Cloudflare login opens your browser. Only authorize your own Cloudflare account.'
$confirmation = Read-Host 'Type NEWDB to create and deploy a fresh isolated database'
if ($confirmation -cne 'NEWDB') { throw 'Canceled before remote changes.' }
& npx.cmd --yes wrangler login
if ($LASTEXITCODE -ne 0) { throw 'Cloudflare login failed.' }
$cfg = [IO.File]::ReadAllText($config)
if ($cfg.Contains('REPLACE_WITH_YOUR_NEW_D1_DATABASE_ID')) {
    Write-Host 'Creating brand-new D1 database ggman-d1-primary ...'
    $lines = & npx.cmd --yes wrangler d1 create ggman-d1-primary --location apac 2>&1
    $status = $LASTEXITCODE
    $raw = $lines -join [Environment]::NewLine
    Write-Host $raw
    if ($status -ne 0) {
        throw 'D1 creation did not complete. If name already exists, open the Cloudflare dashboard and provide its database UUID in the config manually.'
    }
    $match = [regex]::Match($raw, 'database_id\s*=\s*"([0-9a-fA-F-]{36})"')
    if (-not $match.Success) {
        $inputId = (Read-Host 'Paste the new D1 UUID shown above').Trim()
        if ($inputId -notmatch '^[0-9a-fA-F-]{36}$') { throw 'Cannot identify the new D1 database UUID.' }
        $id = $inputId
    } else { $id = $match.Groups[1].Value }
    $cfg = $cfg.Replace('REPLACE_WITH_YOUR_NEW_D1_DATABASE_ID', $id)
    [IO.File]::WriteAllText($config, $cfg, (New-Object System.Text.UTF8Encoding($false)))
}
Write-Host 'Applying schema to newly provisioned D1 ...'
& npx.cmd --yes wrangler d1 migrations apply ggman-d1-primary --remote --config $config
if ($LASTEXITCODE -ne 0) { throw 'D1 schema migration failed. No Worker published.' }
if (Test-Path $keyPath) {
    $key = ([IO.File]::ReadAllText($keyPath)).Trim()
    if ($key -notmatch '^[A-Za-z0-9_-]{43}$') { throw 'Existing recovery key file is invalid; not rotating automatically.' }
} else {
    $random = New-Object byte[] 32
    $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $generator.GetBytes($random) } finally { $generator.Dispose() }
    $key = [Convert]::ToBase64String($random).TrimEnd('=').Replace('+', '-').Replace('/', '_')
    [IO.File]::WriteAllText($keyPath, $key, [Text.Encoding]::ASCII)
}
$sha = [Security.Cryptography.SHA256]::Create()
try { $rawHash = $sha.ComputeHash([Text.Encoding]::ASCII.GetBytes($key)) }
finally { $sha.Dispose() }
$hash = ([BitConverter]::ToString($rawHash)).Replace('-', '').ToLowerInvariant()
Write-Host 'Publishing fail-closed Worker (before adding the secret).'
$deployment = & npx.cmd --yes wrangler deploy --config $config 2>&1
$deploymentText = $deployment -join [Environment]::NewLine
Write-Host $deploymentText
if ($LASTEXITCODE -ne 0) { throw 'Worker deployment failed. Recovery key retained locally for next attempt.' }
Write-Host 'Configuring server-side SHA256 secret. The recovery key itself is never uploaded.'
$hash | & npx.cmd --yes wrangler secret put OWNER_KEY_SHA256 --config $config
if ($LASTEXITCODE -ne 0) { throw 'Secret update failed; Worker rejects all requests until repaired.' }
$urlMatch = [regex]::Match($deploymentText, 'https://[A-Za-z0-9._-]+\.workers\.dev/?')
if ($urlMatch.Success) {
    $workerUrl = $urlMatch.Value.TrimEnd('/') + '/'
} else {
    $workerUrl = (Read-Host 'Paste your HTTPS workers.dev URL').TrimEnd('/') + '/'
}
if ($workerUrl -notmatch '^https://[a-zA-Z0-9._-]+\.workers\.dev/$') {
    throw 'Workers URL invalid. Check dashboard deployment before configuring the GGman candidate.'
}
[IO.File]::WriteAllText($endpointPath, $workerUrl, [Text.Encoding]::ASCII)
Write-Host "SUCCESS: Worker endpoint saved to $endpointPath"
Write-Host "KEEP PRIVATE: recovery key stored at $keyPath"
Write-Host 'Copy only D1-Endpoint.txt to the GGman D1 candidate folder.'
Write-Host 'Do not publish the recovery key. Losing it makes the new cloud inaccessible until explicit key rotation.'
