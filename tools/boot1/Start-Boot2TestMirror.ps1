param(
    [Parameter(Mandatory=$true)][string]$Root,
    [int]$Port = 18085,
    [string]$ReadyFile = '',
    [string]$RequestLog = ''
)

$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path $Root).Path
$rootFull = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
if (-not $rootFull.StartsWith('D:\project2\', [StringComparison]::OrdinalIgnoreCase)) { throw "Mirror must remain under D:\project2: $rootFull" }
if ([string]::IsNullOrWhiteSpace($ReadyFile)) { $ReadyFile = Join-Path $Root '.server-ready' }
if ([string]::IsNullOrWhiteSpace($RequestLog)) { $RequestLog = Join-Path $Root '.server-requests.jsonl' }
$listener = [Net.HttpListener]::new()
$listener.Prefixes.Add("http://127.0.0.1:$Port/")
$listener.Start()
Set-Content -LiteralPath $ReadyFile -Value ([DateTime]::UtcNow.ToString('o')) -Encoding ascii
Write-Output "BOOT-2 mirror listening on http://127.0.0.1:$Port/"

try {
    while ($listener.IsListening) {
        $context = $listener.GetContext()
        $response = $context.Response
        try {
            $requestPath = [Uri]::UnescapeDataString($context.Request.Url.AbsolutePath).TrimStart('/')
            $requestRange = $context.Request.Headers['Range']
            if ($requestPath.Contains('..') -or [IO.Path]::IsPathRooted($requestPath)) {
                $response.StatusCode = 400
                Add-Content -LiteralPath $RequestLog -Value ("{`"path`":`"$requestPath`",`"status`":400}" ) -Encoding utf8
                continue
            }
            $candidate = [IO.Path]::GetFullPath((Join-Path $Root ($requestPath -replace '/', '\')))
            if (-not $candidate.StartsWith($rootFull, [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
                $response.StatusCode = 404
                Add-Content -LiteralPath $RequestLog -Value ("{`"path`":`"$requestPath`",`"status`":404}" ) -Encoding utf8
                continue
            }
            $file = [IO.FileInfo]::new($candidate)
            $start = 0L
            $length = $file.Length
            $range = $context.Request.Headers['Range']
            if ($range -match '^bytes=(\d+)-$') {
                $start = [Int64]$Matches[1]
                if ($start -ge $file.Length) {
                    $response.StatusCode = 416
                    $response.Headers['Content-Range'] = "bytes */$($file.Length)"
                    Add-Content -LiteralPath $RequestLog -Value ("{`"path`":`"$requestPath`",`"status`":416,`"range`":`"$requestRange`"}" ) -Encoding utf8
                    continue
                }
                $length = $file.Length - $start
                $response.StatusCode = 206
                $response.Headers['Content-Range'] = "bytes $start-$($file.Length - 1)/$($file.Length)"
            } else {
                $response.StatusCode = 200
            }
            $response.ContentLength64 = $length
            Add-Content -LiteralPath $RequestLog -Value ("{`"path`":`"$requestPath`",`"status`":$($response.StatusCode),`"bytes`":$length,`"range`":`"$requestRange`"}" ) -Encoding utf8
            $stream = [IO.FileStream]::new($candidate, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
            try {
                $stream.Position = $start
                $buffer = New-Object byte[] (256 * 1024)
                $remaining = $length
                while ($remaining -gt 0) {
                    $read = $stream.Read($buffer, 0, [Math]::Min($buffer.Length, $remaining))
                    if ($read -le 0) { break }
                    $response.OutputStream.Write($buffer, 0, $read)
                    $remaining -= $read
                }
            } finally { $stream.Dispose() }
        } catch {
            $response.StatusCode = 500
        } finally {
            $response.Close()
        }
    }
} finally {
    if ($listener.IsListening) { $listener.Stop() }
    $listener.Close()
    if (Test-Path -LiteralPath $ReadyFile) { Remove-Item -LiteralPath $ReadyFile -Force }
}
