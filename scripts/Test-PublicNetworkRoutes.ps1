# Opt-in public endpoint probe. Run on the network being evaluated.
[CmdletBinding()]
param(
    [ValidateRange(1,5)][int]$Rounds = 1,
    [string]$NetworkLabel = 'unspecified',
    [string]$OutputDirectory = (Join-Path $env:TEMP ('GGman-PublicRoutes-' + (Get-Date -Format 'yyyyMMdd-HHmmss')))
)
$ErrorActionPreference = 'Stop'
$curl = (Get-Command curl.exe -CommandType Application -ErrorAction Stop).Source
$root = Split-Path -Parent $PSScriptRoot
$origin = 'https://raw.githubusercontent.com/xianyumht-cmd/facm/main/online/version.json'
$reference = Get-Content -Raw -LiteralPath (Join-Path $root 'online/version.json') | ConvertFrom-Json
$catalog = Get-Content -Raw -LiteralPath (Join-Path $root 'online/mirrors.json') | ConvertFrom-Json
$targets = @(
    @{Name='hexdata-ranking'; Url='https://hexdata.com.cn/heroes'; Kind='html'; Marker='Hexdata'; Budget=2.8},
    @{Name='aramgg-ranking'; Url='https://aramgg.com/en'; Kind='html'; Marker='Win Rate'; Budget=3.4},
    @{Name='aram-mayhem-ranking'; Url='https://arammayhem.com/'; Kind='html'; Marker='ARAM Mayhem'; Budget=3.4},
    @{Name='aram-mayhem-detail'; Url='https://arammayhem.com/build/ashe/'; Kind='html'; Marker='Ashe'; Budget=3.8},
    @{Name='opgg-build'; Url='https://op.gg/zh-cn/lol/modes/aram-mayhem/ashe/build'; Kind='html'; Marker='OP.GG'; Budget=2.2},
    @{Name='opgg-augments'; Url='https://op.gg/zh-cn/lol/modes/aram-mayhem/ashe/augments'; Kind='html'; Marker='OP.GG'; Budget=5.5},
    @{Name='opgg-base-aram'; Url='https://op.gg/zh-cn/lol/modes/aram/ashe/build'; Kind='html'; Marker='OP.GG'; Budget=2.2},
    @{Name='opgg-ranked-api'; Url='https://lol-api-champion.op.gg/api/champions/ranked/1'; Kind='json'; Marker=''; Budget=6},
    @{Name='tencent-news'; Url='https://lol.qq.com/news/index.shtml'; Kind='html'; Marker='lol.qq.com'; Budget=1.8},
    @{Name='ddragon-versions'; Url='https://ddragon.leagueoflegends.com/api/versions.json'; Kind='json'; Marker=''; Budget=3},
    @{Name='communitydragon-champions'; Url='https://raw.communitydragon.org/latest/plugins/rcp-be-lol-game-data/global/zh_cn/v1/champion-summary.json'; Kind='json'; Marker=''; Budget=5},
    @{Name='communitydragon-items'; Url='https://raw.communitydragon.org/latest/plugins/rcp-be-lol-game-data/global/zh_cn/v1/items.json'; Kind='json'; Marker=''; Budget=5},
    @{Name='akari-sgp-catalog'; Url='https://akari-api.yuru-yuri.com/config/v1/sgp/league-servers'; Kind='json'; Marker=''; Budget=4},
    @{Name='akari-queues'; Url='https://akari-api.yuru-yuri.com/config/v1/sgp/supported-queues'; Kind='json'; Marker=''; Budget=4},
    @{Name='akari-auto-select'; Url='https://akari-api.yuru-yuri.com/config/v1/auto-select/groups'; Kind='json'; Marker=''; Budget=4},
    @{Name='update-origin'; Url=$origin; Kind='manifest'; Marker=''; Budget=4},
    @{Name='update-catalog-gitee'; Url='https://gitee.com/xymhtcmd/facm/raw/main/online/mirrors.json'; Kind='json'; Marker=''; Budget=4}
)
foreach ($source in $catalog.sources) {
    if (-not $source.enabled -or -not $source.prefix) { continue }
    $prefix = [uri]$source.prefix
    if ($prefix.Scheme -ne 'https' -or $prefix.IsLoopback -or $prefix.UserInfo -or $prefix.Query -or $prefix.Fragment) { continue }
    $targets += @{Name=('update-' + $source.name); Url=($source.prefix.TrimEnd('/') + '/' + $origin); Kind='manifest'; Marker=''; Budget=4}
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$csv = Join-Path $OutputDirectory 'public-routes.csv'
$body = Join-Path $OutputDirectory 'temporary-response.bin'
$format = '%{http_code}|%{time_namelookup}|%{time_connect}|%{time_appconnect}|%{time_starttransfer}|%{time_total}|%{size_download}'
$culture = [Globalization.CultureInfo]::InvariantCulture
try {
    for ($round=1; $round -le $Rounds; $round++) {
        foreach ($target in $targets) {
            Remove-Item -LiteralPath $body -ErrorAction SilentlyContinue
            $started = (Get-Date).ToUniversalTime().ToString('o')
            $raw = & $curl -q --silent --noproxy '*' --location --max-redirs 3 --proto '=https' --proto-redir '=https' --connect-timeout 5 --max-time 12 --max-filesize 12582912 --compressed --user-agent 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/151.0 FACM/3.1' --header 'Accept-Language: zh-CN,zh;q=0.9,en;q=0.7' --output $body --write-out $format -- $target.Url
            $exitCode = $LASTEXITCODE
            $parts = ([string]$raw).Split('|')
            if ($parts.Count -ne 7) { $parts = @('000','','','','','','') }
            $status = 0
            [void][int]::TryParse($parts[0], [ref]$status)
            $payload = 'unavailable'
            $version = ''
            if ($exitCode -eq 0 -and $status -ge 200 -and $status -lt 300 -and (Test-Path -LiteralPath $body)) {
                try {
                    $content = Get-Content -Raw -Encoding UTF8 -LiteralPath $body
                    if ($target.Kind -eq 'html') {
                        $payload = if ($content -and $content.IndexOf($target.Marker,[StringComparison]::OrdinalIgnoreCase) -ge 0) {'html-marker'} else {'unexpected-html'}
                    } else {
                        $data = ConvertFrom-Json -InputObject $content -ErrorAction Stop
                        if ($target.Kind -eq 'manifest') {
                            $version = [string]$data.version
                            $parsedVersion = $null
                            if ([version]::TryParse($version,[ref]$parsedVersion) -and $data.sha256 -match '^[0-9a-fA-F]{64}$' -and $data.download_url -match '^https://') {
                                $payload = if ($version -eq [string]$reference.version -and $data.sha256 -eq $reference.sha256) {'manifest-matches-checkout'} else {'manifest-other-version-or-hash'}
                            } else { $payload = 'invalid-manifest' }
                        } else { $payload = if ($null -ne $data) {'json-readable'} else {'empty-json'} }
                    }
                } catch { $payload = 'unreadable-payload' }
            }
            $total = 0.0
            $hasTotal = [double]::TryParse($parts[5], [Globalization.NumberStyles]::Float, $culture, [ref]$total)
            $row = [pscustomobject]@{
                utc=$started; network=$NetworkLabel; round=$round; source=$target.Name; url=$target.Url
                curl_exit=$exitCode; http_status=$status; payload=$payload; manifest_version=$version
                dns_s=$parts[1]; connect_s=$parts[2]; tls_s=$parts[3]; ttfb_s=$parts[4]; total_s=$parts[5]; bytes=$parts[6]
                reference_budget_s=$target.Budget; within_reference_budget=($exitCode -eq 0 -and $status -ge 200 -and $status -lt 300 -and $hasTotal -and $total -le $target.Budget)
            }
            $row | Export-Csv -LiteralPath $csv -NoTypeInformation -Encoding UTF8 -Append
            Write-Host ("{0,-30} {1} {2}s {3}" -f $target.Name,$status,$parts[5],$payload)
        }
    }
} finally {
    Remove-Item -LiteralPath $body -ErrorAction SilentlyContinue
}
Write-Host "Report: $csv"
Write-Host 'Timing is for this network only. Payload markers are not production parser or signature verification.'
