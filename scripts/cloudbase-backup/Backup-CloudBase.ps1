#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
function Fail([string]$text) { throw $text }
Write-Host "GGman CloudBase PostgreSQL backup (read-only export)"
Write-Host "This tool does NOT connect with embedded credentials, modify cloud data, or migrate any users."
$dumpCmd = Get-Command pg_dump.exe -ErrorAction SilentlyContinue
$restoreCmd = Get-Command pg_restore.exe -ErrorAction SilentlyContinue
if (-not $dumpCmd -or -not $restoreCmd) {
    Fail "PostgreSQL pg_dump.exe and pg_restore.exe are required in PATH. Install the matching PostgreSQL client tools first."
}
$hostName = (Read-Host "CloudBase PostgreSQL hostname (from official console)").Trim()
$database = (Read-Host "Database name (usually postgres)").Trim()
if (-not $database) { $database = "postgres" }
$username = (Read-Host "Read-only/backup database username").Trim()
$portInput = (Read-Host "Port (press Enter for 5432)").Trim()
if (-not $portInput) { $portInput = "5432" }
if ($hostName -notmatch '^[a-zA-Z0-9._-]{2,253}$') { Fail "Invalid database host." }
if ($database -notmatch '^[a-zA-Z0-9_-]{1,64}$') { Fail "Invalid database name." }
if ($username -notmatch '^[a-zA-Z0-9_.-]{1,100}$') { Fail "Invalid account name." }
[int]$port = 0
if (-not [int]::TryParse($portInput, [ref]$port) -or $port -lt 1 -or $port -gt 65535) { Fail "Invalid port." }
$answer = Read-Host "This will export potentially sensitive production data locally. Type BACKUP to proceed"
if ($answer -cne 'BACKUP') { Fail "Canceled." }

$root = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'GGman-CloudBase-Backups'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$destination = Join-Path $root $stamp
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$dumpFile = Join-Path $destination 'cloudbase-full.dump'
$manifest = Join-Path $destination 'manifest.json'
$inventory = Join-Path $destination 'restore-list.txt'
$env:PGSSLMODE = 'require'
# pg_dump prompts for a password securely. Do not place passwords in the command line or script.
$arguments = @('--host', $hostName, '--port', [string]$port, '--username', $username,
    '--dbname', $database, '--format', 'custom', '--no-owner', '--no-acl',
    '--compress', '9', '--file', $dumpFile, '--password')
Write-Host "Exporting using the PostgreSQL client; no write query is issued..."
& $dumpCmd.Source @arguments
if ($LASTEXITCODE -ne 0) { Fail "pg_dump failed with exit code $LASTEXITCODE. Backup is NOT verified." }
if (-not (Test-Path -LiteralPath $dumpFile) -or (Get-Item -LiteralPath $dumpFile).Length -lt 1024) {
    Fail "Dump missing or implausibly small. Backup is NOT verified."
}
$items = & $restoreCmd.Source '--list' $dumpFile
if ($LASTEXITCODE -ne 0 -or -not $items) { Fail "pg_restore inventory failed. Backup is NOT verified." }
$items | Set-Content -LiteralPath $inventory -Encoding UTF8
$tableEntries = @($items | Where-Object { $_ -match 'TABLE DATA public ggman_' })
$tableNames = @($tableEntries | ForEach-Object {
    if ($_ -match 'TABLE DATA public (ggman_[A-Za-z_0-9]+)') { $matches[1] }
} | Sort-Object -Unique)
$expected = @('ggman_registered_app_settings', 'ggman_ui_text_profiles',
    'ggman_esc_profiles', 'ggman_game_profiles', 'ggman_registered_profiles',
    'ggman_registered_account_history', 'ggman_registered_active_days',
    'ggman_registered_legacy_imports')
$missing = @($expected | Where-Object { $tableNames -notcontains $_ })
$hash = (Get-FileHash -LiteralPath $dumpFile -Algorithm SHA256).Hash
$report = [ordered]@{
    backup_status = 'dump_verified_inventory_only_restore_test_pending'
    created_utc = [DateTime]::UtcNow.ToString('o')
    dump_file = 'cloudbase-full.dump'
    dump_bytes = (Get-Item -LiteralPath $dumpFile).Length
    sha256 = $hash
    ggman_tables_included = @($tableNames)
    expected_tables_not_listed = @($missing)
    auth_users_and_provider_metadata_verified = $false
    restore_into_isolated_postgresql_verified = $false
    warnings = @('pg_dump does not export CloudBase identity-provider user administration/config.',
      'Archive includes sensitive account and settings data. Encrypt before storing off-device.',
      'Confirm missing tables in the real CloudBase console. Do not cut over until a separate restore succeeds.')
}
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifest -Encoding UTF8
Write-Host ("Backup inventory created at: " + $destination)
Write-Host ("SHA-256: " + $hash)
Write-Host ("GGman tables found: " + $tableNames.Count + ". Expected not listed: " + $missing.Count)
if ($missing.Count -gt 0) { Write-Warning ("Review missing tables: " + ($missing -join ', ')) }
Write-Warning 'This is an EXPORT, not a complete migration or restore rehearsal. Identity provider users are separate.'
Write-Warning 'Treat .dump and restore-list.txt as private. Never send them to public channels or commit to git.'
