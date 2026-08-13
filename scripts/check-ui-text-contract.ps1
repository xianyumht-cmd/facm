param([string]$BaseRef = 'HEAD^', [string]$HeadRef = 'HEAD')

$ErrorActionPreference = 'Stop'
$diff = @(git diff --unified=0 --no-color "$BaseRef..$HeadRef" -- 'src/FACM/*.cs' 'src/FACM/**/*.cs')
if ($LASTEXITCODE -ne 0) { throw 'Unable to read source diff for UI text contract.' }

$path = ''
$newLine = 0
$failures = @()

foreach ($row in $diff) {
    $line = [string]$row
    if ($line.StartsWith('+++ b/')) { $path = $line.Substring(6); continue }
    if ($line -match '^@@.*\+(\d+)') { $newLine = [int]$Matches[1]; continue }
    if (-not $line.StartsWith('+') -or $line.StartsWith('+++')) { continue }

    $source = $line.Substring(1)
    $lineNumber = $newLine
    $newLine++

    if ($path -match 'SmokeTest\.cs$') { continue }
    if ($path -eq 'src/FACM/Services/UiTextCatalog.cs' -or $path -eq 'src/FACM/Services/UiTextKeys.cs') { continue }
    if ($source -match 'ui-text-contract:\s*allow') { continue }
    if ($source.TrimStart().StartsWith('//')) { continue }

    $uiFile = $path -match '(Form|Menu|Window|Dialog|Picker|Renderer)\.cs$' -or
              $path -match '(MainForm|CompactMenuEnhancer|LayeredFloatingBall|ContextMenuStrip)\.cs$'
    $literal = '(?:\$@|@\$|\$|@)?"'
    $directText = $source -match ("\bText\s*=\s*" + $literal) -or
                  $source -match ("new\s+ToolStrip\w*\s*\(\s*" + $literal) -or
                  $source -match ("MessageBox\.Show\s*\(\s*" + $literal) -or
                  $source -match ("SetToolTip\s*\([^,]+,\s*" + $literal) -or
                  $source -match ("DrawString\s*\(\s*" + $literal)
    $cjkLiteral = $uiFile -and $source -match '"[^"\r\n]*[\u3400-\u9fff][^"\r\n]*"'

    if ($directText -or $cjkLiteral) {
        $failures += "${path}:${lineNumber}: $($source.Trim())"
    }
}

if ($failures.Count -gt 0) {
    Write-Host 'FACM UI Text Contract failed:'
    $failures | ForEach-Object { Write-Host "  $_" }
    Write-Host 'Use UiTextKeys + UiTextRuntime.Text() and register the default in UiTextCatalog.'
    Write-Host 'Non-user-facing exceptions require an explicit ui-text-contract: allow marker.'
    exit 1
}

Write-Host 'FACM UI Text Contract passed.'
