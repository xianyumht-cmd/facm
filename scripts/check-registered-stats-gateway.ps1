$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$gateway = 'https://ggman-d4gioqqcz434d9e4d.api.tcloudbasegateway.com'
$deviceId = [guid]::NewGuid().ToString('D')
$session = Invoke-RestMethod -Uri "$gateway/auth/v1/signin/anonymously" -Method Post `
    -Headers @{ 'x-device-id' = $deviceId } -ContentType 'application/json' `
    -Body '{}' -TimeoutSec 12 -MaximumRedirection 0
$anonymousToken = [string]$session.access_token
if ([string]::IsNullOrWhiteSpace($anonymousToken)) {
    throw 'Failed to acquire temporary CloudBase anonymous bearer.'
}

$requests = @(
    @{ name='read registered stats'; rpc='ggman_get_registered_personal_stats'; body='{}' },
    @{ name='touch registered profile'; rpc='ggman_registered_touch'; body='{}' },
    @{ name='record registered account'; rpc='ggman_registered_record_account'; body=('{ "p_account_key_hash": "' + ('a' * 64) + '" }') },
    @{ name='import legacy'; rpc='ggman_registered_import_legacy'; body=('{ "p_source_key": "' + ('b' * 64) + '", "p_legacy_account_count": 1, "p_active_days": [] }') }
)
try {
    foreach ($auth in @(
        @{ name='No credentials'; headers=@{} },
        @{ name='Anonymous bearer'; headers=@{ Authorization="Bearer $anonymousToken" } }
    )) {
        foreach ($case in $requests) {
            $endpoint = "$gateway/v1/rdb/rest/rpc/$($case.rpc)"
            $response = Invoke-WebRequest -Uri $endpoint -Method Post -SkipHttpErrorCheck `
                -Headers $auth.headers -ContentType 'application/json' -Body $case.body `
                -TimeoutSec 12 -MaximumRedirection 0
            $status = [int]$response.StatusCode
            if ($status -ne 401 -and $status -ne 403) {
                throw "$($auth.name) $($case.name) was not denied: HTTP $status."
            }
            Write-Host "$($auth.name) $($case.name): HTTP $status (blocked)"
        }
    }
} finally {
    $anonymousToken = $null
}
