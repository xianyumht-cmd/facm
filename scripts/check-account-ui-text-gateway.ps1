$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$gateway = 'https://ggman-d4gioqqcz434d9e4d.api.tcloudbasegateway.com'
$deviceId = [guid]::NewGuid().ToString('D')
$session = Invoke-RestMethod -Uri "$gateway/auth/v1/signin/anonymously" -Method Post `
    -Headers @{ 'x-device-id' = $deviceId } -ContentType 'application/json' `
    -Body '{}' -TimeoutSec 12 -MaximumRedirection 0
$anonymousToken = [string]$session.access_token
if ([string]::IsNullOrWhiteSpace($anonymousToken)) {
    throw 'CloudBase did not issue a temporary anonymous session.'
}
$cases = @(
    @{ name='read'; rpc='ggman_get_ui_text_profile'; body='{}' },
    @{ name='write'; rpc='ggman_set_ui_text_profile'; body='{"p_payload":{"SchemaVersion":1,"Text":{},"Replace":{}},"p_expected_version":0}' }
)
try {
    foreach ($auth in @(
        @{ name='No credentials'; headers=@{} },
        @{ name='Anonymous bearer'; headers=@{ Authorization="Bearer $anonymousToken" } }
    )) {
        foreach ($item in $cases) {
            $response = Invoke-WebRequest -Uri "$gateway/v1/rdb/rest/rpc/$($item.rpc)" `
                -Method Post -SkipHttpErrorCheck -Headers $auth.headers `
                -ContentType 'application/json' -Body $item.body `
                -TimeoutSec 12 -MaximumRedirection 0
            $code = [int]$response.StatusCode
            if ($code -notin @(401,403)) {
                throw "$($auth.name) $($item.name) was not blocked: HTTP $code."
            }
            Write-Host "$($auth.name) $($item.name): HTTP $code (blocked)"
        }
    }
}
finally {
    $anonymousToken = $null
}
