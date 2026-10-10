$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$gateway = 'https://ggman-d4gioqqcz434d9e4d.api.tcloudbasegateway.com'
$deviceId = [guid]::NewGuid().ToString('D')
$session = Invoke-RestMethod -Uri "$gateway/auth/v1/signin/anonymously" -Method Post `
    -Headers @{ 'x-device-id' = $deviceId } -ContentType 'application/json' `
    -Body '{}' -TimeoutSec 12 -MaximumRedirection 0
$anonymousToken = [string]$session.access_token
if ([string]::IsNullOrWhiteSpace($anonymousToken)) {
    throw 'Temporary anonymous session is unavailable.'
}
$requests = @(
    @{ name='read'; rpc='ggman_get_registered_app_settings'; body='{}' },
    @{ name='write'; rpc='ggman_set_registered_app_settings'; body='{"p_payload":{"SchemaVersion":1,"Settings":{}},"p_expected_version":0}' }
)
try {
    foreach ($identity in @(
        @{ name='No credentials'; headers=@{} },
        @{ name='Anonymous bearer'; headers=@{ Authorization="Bearer $anonymousToken" } }
    )) {
        foreach ($operation in $requests) {
            $response = Invoke-WebRequest -Uri "$gateway/v1/rdb/rest/rpc/$($operation.rpc)" `
                -Method Post -SkipHttpErrorCheck -Headers $identity.headers `
                -ContentType 'application/json' -Body $operation.body `
                -TimeoutSec 12 -MaximumRedirection 0
            $status = [int]$response.StatusCode
            if ($status -notin @(401, 403)) {
                throw "$($identity.name) $($operation.name) returned HTTP $status, expected access denial."
            }
            Write-Host "$($identity.name) $($operation.name): HTTP $status (blocked)"
        }
    }
}
finally {
    $anonymousToken = $null
}
