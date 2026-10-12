$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$gateway = 'https://ggman-d4gioqqcz434d9e4d.api.tcloudbasegateway.com'
$deviceId = [guid]::NewGuid().ToString('D')
$anonymous = Invoke-RestMethod -Uri "$gateway/auth/v1/signin/anonymously" `
    -Method Post -Headers @{ 'x-device-id' = $deviceId } `
    -ContentType 'application/json' -Body '{}' -TimeoutSec 12 -MaximumRedirection 0
$accessToken = [string]$anonymous.access_token
if ([string]::IsNullOrWhiteSpace($accessToken)) {
    throw 'CloudBase anonymous authentication returned no access token.'
}

try {
    $operations = @(
        @{ name = 'read'; rpc = 'ggman_get_game_profile'; body = '{}' },
        @{ name = 'write'; rpc = 'ggman_set_game_profile'; body = '{"p_payload":null,"p_expected_version":0}' }
    )
    $identities = @(
        @{ name = 'No credentials'; headers = @{} },
        @{ name = 'Anonymous bearer'; headers = @{ Authorization = "Bearer $accessToken" } }
    )
    foreach ($identity in $identities) {
        foreach ($operation in $operations) {
            $uri = "$gateway/v1/rdb/rest/rpc/$($operation.rpc)"
            $response = Invoke-WebRequest -Uri $uri -Method Post -SkipHttpErrorCheck `
                -Headers $identity.headers -ContentType 'application/json' `
                -Body $operation.body -TimeoutSec 12 -MaximumRedirection 0
            $status = [int]$response.StatusCode
            if ($status -ne 401 -and $status -ne 403) {
                throw "$($identity.name) $($operation.name) was not denied (HTTP $status)."
            }
            Write-Host "$($identity.name) $($operation.name) denied: HTTP $status"
        }
    }
}
finally {
    $accessToken = $null
}
