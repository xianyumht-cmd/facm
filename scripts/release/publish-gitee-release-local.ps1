[CmdletBinding()]
param(
    [string]$RepoOwner = 'xymhtcmd',
    [string]$RepoName = 'facm',
    [Parameter(Mandatory = $true)]
    [string]$ReleaseTag,
    [string]$TargetCommit = '',
    [Parameter(Mandatory = $true)]
    [string]$BundleRoot,
    [string[]]$AssetNames = @(),
    [switch]$Publish
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Require([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Get-GiteeToken {
    $credentialInput = "protocol=https`nhost=gitee.com`n`n"
    $credential = @($credentialInput | git credential fill 2>$null)
    $passwordLine = $credential | Where-Object { $_ -like 'password=*' } | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($passwordLine)) {
        throw 'No Gitee Git credential is available in the local credential manager.'
    }
    return $passwordLine.Substring(9)
}

function Invoke-GiteeJson([string]$Method, [string]$Path, [object]$Payload, [string]$Token) {
    $client = [Net.Http.HttpClient]::new()
    try {
        $client.DefaultRequestHeaders.UserAgent.ParseAdd('FACM-local-release')
        $client.DefaultRequestHeaders.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new('token', $Token)
        $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::$Method, "https://gitee.com/api/v5$Path")
        try {
            if ($null -ne $Payload) {
                $json = $Payload | ConvertTo-Json -Depth 20 -Compress
                $request.Content = [Net.Http.StringContent]::new($json, [Text.Encoding]::UTF8, 'application/json')
            }
            $response = $client.SendAsync($request).GetAwaiter().GetResult()
            $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            if (-not $response.IsSuccessStatusCode) {
                throw "Gitee API $Method $Path failed with HTTP $([int]$response.StatusCode)."
            }
            if ([string]::IsNullOrWhiteSpace($text)) { return $null }
            return $text | ConvertFrom-Json
        } finally {
            $request.Dispose()
        }
    } finally {
        $client.Dispose()
    }
}

function New-GiteeRelease([string]$Tag, [string]$Commit, [string]$Token) {
    $path = "/repos/$RepoOwner/$RepoName/releases"
    $payload = [ordered]@{
        tag_name = $Tag
        name = "FACM $($Tag.TrimStart('v'))"
        target_commitish = $Commit
        prerelease = $false
        body = "FACM $($Tag.TrimStart('v')) 本地签名发布。下载文件包含自校验清单和组件签名。"
    }
    return Invoke-GiteeJson 'Post' $path $payload $Token
}

function Get-GiteeRelease([string]$Tag, [string]$Token) {
    $path = "/repos/$RepoOwner/$RepoName/releases/tags/$Tag"
    try { return Invoke-GiteeJson 'Get' $path $null $Token }
    catch { if ($_.Exception.Message -match 'HTTP 404') { return $null }; throw }
}

function Get-GiteeAssetNames([object]$Release, [string]$Token) {
    $path = "/repos/$RepoOwner/$RepoName/releases/$($Release.id)/attach_files"
    $assets = Invoke-GiteeJson 'Get' $path $null $Token
    return @($assets | ForEach-Object { [string]$_.name })
}

function Upload-GiteeAsset([object]$Release, [string]$Path, [string]$Token) {
    $client = [Net.Http.HttpClient]::new()
    try {
        $client.DefaultRequestHeaders.UserAgent.ParseAdd('FACM-local-release')
        $client.DefaultRequestHeaders.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new('token', $Token)
        $requestPath = "/repos/$RepoOwner/$RepoName/releases/$($Release.id)/attach_files"
        $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post, "https://gitee.com/api/v5$requestPath")
        try {
            $form = [Net.Http.MultipartFormDataContent]::new()
            try {
                $stream = [IO.File]::OpenRead($Path)
                try {
                    $content = [Net.Http.StreamContent]::new($stream)
                    $content.Headers.ContentType = [Net.Http.Headers.MediaTypeHeaderValue]::new('application/octet-stream')
                    $form.Add($content, 'file', [IO.Path]::GetFileName($Path))
                    $request.Content = $form
                    $response = $client.SendAsync($request).GetAwaiter().GetResult()
                    if (-not $response.IsSuccessStatusCode) {
                        throw "Gitee asset upload failed for $([IO.Path]::GetFileName($Path)) with HTTP $([int]$response.StatusCode)."
                    }
                    $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | Out-Null
                } finally { $stream.Dispose() }
            } finally { $form.Dispose() }
        } finally { $request.Dispose() }
    } finally { $client.Dispose() }
}

Require (-not [string]::IsNullOrWhiteSpace($ReleaseTag)) 'ReleaseTag is required.'
$BundleRoot = [IO.Path]::GetFullPath($BundleRoot)
Require (Test-Path -LiteralPath $BundleRoot -PathType Container) "BundleRoot does not exist: $BundleRoot"

$allAssets = @(Get-ChildItem -LiteralPath $BundleRoot -File | Where-Object { $_.Name -ne 'self-signed-release-evidence.json' })
if ($AssetNames.Count -gt 0) {
    $selectedNames = @($AssetNames | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    $allAssets = @($selectedNames | ForEach-Object {
        $asset = Join-Path $BundleRoot $_
        Require (Test-Path -LiteralPath $asset -PathType Leaf) "Release asset does not exist: $asset"
        Get-Item -LiteralPath $asset
    })
}
Require ($allAssets.Count -gt 0) 'No release assets were selected.'

$target = if ([string]::IsNullOrWhiteSpace($TargetCommit)) { (& git rev-parse HEAD).Trim() } else { $TargetCommit.Trim() }
Require ($target -match '^[0-9a-f]{40}$') "TargetCommit is not a full commit SHA: $target"
$token = Get-GiteeToken
$release = Get-GiteeRelease $ReleaseTag $token

Write-Host "Gitee release preview: $RepoOwner/$RepoName $ReleaseTag"
Write-Host "Target commit: $target"
Write-Host "Assets: $($allAssets.Count)"
$allAssets | ForEach-Object { Write-Host ("  {0} ({1} bytes)" -f $_.Name, $_.Length) }

if (-not $Publish) {
    Write-Host 'Preview only. Re-run with -Publish to create the Release and upload assets.'
    exit 0
}

if ($null -eq $release) {
    $release = New-GiteeRelease $ReleaseTag $target $token
    Write-Host "Created Gitee release id=$($release.id)."
} else {
    Write-Host "Using existing Gitee release id=$($release.id)."
}

foreach ($asset in $allAssets) {
    # Refresh immediately before each upload. This makes a rerun safe if a previous
    # long upload was interrupted locally but continued on the server.
    $existingNames = Get-GiteeAssetNames $release $token
    if ($existingNames -contains $asset.Name) {
        Write-Host "Skipped existing asset: $($asset.Name)"
        continue
    }
    Upload-GiteeAsset $release $asset.FullName $token
    Write-Host "Uploaded: $($asset.Name)"
}

Write-Host "Gitee release published: https://gitee.com/$RepoOwner/$RepoName/releases/tag/$ReleaseTag"
