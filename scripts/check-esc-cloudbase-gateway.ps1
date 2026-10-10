$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$gateway = 'https://ggman-d4gioqqcz434d9e4d.api.tcloudbasegateway.com'
$endpoint = "$gateway/v1/rdb/rest/rpc/ggman_get_esc_profile"
$deviceId = [guid]::NewGuid().ToString('D')
$session = Invoke-RestMethod -Uri "$gateway/auth/v1/signin/anonymously" -Method Post `
    -Headers @{ 'x-device-id' = $deviceId } -ContentType 'application/json' `
    -Body '{}' -TimeoutSec 12 -MaximumRedirection 0

$accessToken = [string]$session.access_token
if ([string]::IsNullOrWhiteSpace($accessToken)) {
    throw 'CloudBase anonymous authentication did not return a session.'
}

try {
    $cases = @(
        @{ name = 'No credentials'; headers = @{} },
        @{ name = 'Anonymous bearer'; headers = @{ Authorization = "Bearer $accessToken" } }
    )
    foreach ($case in $cases) {
        $response = Invoke-WebRequest -Uri $endpoint -Method Post -SkipHttpErrorCheck `
            -Headers $case.headers -ContentType 'application/json' -Body '{}' `
            -TimeoutSec 12 -MaximumRedirection 0
        $status = [int]$response.StatusCode
        if ($status -ne 401 -and $status -ne 403) {
            throw "$($case.name) ESC read was not denied (HTTP $status)."
        }
        Write-Host "$($case.name) ESC read rejected: HTTP $status"
    }
}
finally {
    $accessToken = $null
}
