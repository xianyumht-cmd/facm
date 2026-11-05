$ErrorActionPreference = 'Stop'
$asset = 'src/FACM/Presentation/Glass/GGman-Unified-Workspace.html'
$source = Get-Content -LiteralPath $asset -Raw -Encoding utf8
if (-not $source.Contains('window.GGmanUnifiedShell={setMode:setMode') -or -not $source.Contains('</body>')) { throw 'Missing unified HTML router.' }
$programFilesX86 = [Environment]::GetFolderPath('ProgramFilesX86')
$programFiles = [Environment]::GetFolderPath('ProgramFiles')
$edge = @(
  (Join-Path $programFilesX86 'Microsoft\Edge\Application\msedge.exe'),
  (Join-Path $programFiles 'Microsoft\Edge\Application\msedge.exe')
) | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
if (-not $edge) { throw 'Edge missing from Windows visual runner.' }
$target = New-Item -ItemType Directory -Path 'artifacts/Visual-Checks' -Force
Add-Type -AssemblyName System.Drawing
$cases = @(
  @{ Mode='home'; Width=420; Height=400 },
  @{ Mode='dashboard'; Width=660; Height=680 },
  @{ Mode='quicktools'; Width=630; Height=650 },
  @{ Mode='matchmaking-settings'; Width=640; Height=740 },
  @{ Mode='auto-selection'; Width=600; Height=680 },
  @{ Mode='postgame-settings'; Width=580; Height=610 },
  @{ Mode='presence'; Width=620; Height=730 },
  @{ Mode='repair'; Width=620; Height=710 },
  @{ Mode='live'; Width=680; Height=760 },
  @{ Mode='player'; Width=680; Height=760 },
  @{ Mode='mayhem'; Width=680; Height=760 },
  @{ Mode='recommendation'; Width=680; Height=760 },
  @{ Mode='cloud-sync'; Width=630; Height=740 },
  @{ Mode='updates'; Width=640; Height=730 },
  @{ Mode='cloud-sync'; Width=420; Height=620 },
  @{ Mode='updates'; Width=420; Height=620 }
)
foreach ($scene in $cases) {
  $mode = $scene.Mode
  $htmlPath = Join-Path $target.FullName "unified-$mode.html"
  $imagePath = Join-Path $target.FullName "unified-$mode.png"
  $visualProbe = @'
<script>
window.GGmanUnifiedShell.setMode('SCENE','SCENE');
var shell = document.getElementById('shell');
var documentFits = document.documentElement.scrollWidth <= window.innerWidth + 2 &&
  shell.scrollWidth <= shell.clientWidth + 2;
var marker = document.createElement('span');
marker.style.cssText = 'position:fixed;left:0;top:0;width:10px;height:10px;z-index:2147483647;background:' +
  (documentFits ? '#00ff00' : '#ff0000');
document.body.appendChild(marker);
</script>
'@
  $visualProbe = $visualProbe.Replace('SCENE', $mode)
  $page = $source.Replace('</body>', $visualProbe + '</body>')
  Set-Content -LiteralPath $htmlPath -Value $page -Encoding utf8 -NoNewline
  $uri = ([System.Uri]::new($htmlPath)).AbsoluteUri
  $profile = Join-Path $env:RUNNER_TEMP "ggman-unified-$mode"
  $windowSize = "$($scene.Width),$($scene.Height)"
  $edgeArgs = @('--headless=new','--disable-gpu','--no-first-run','--no-default-browser-check','--hide-scrollbars',"--user-data-dir=$profile","--screenshot=$imagePath","--window-size=$windowSize",$uri)
  & $edge @edgeArgs
  $previousSize = -1
  $stable = $false
  for ($attempt=0; $attempt -lt 40; $attempt++) {
    $size = if (Test-Path -LiteralPath $imagePath) { (Get-Item $imagePath).Length } else { 0 }
    if ($size -gt 1536 -and $size -eq $previousSize) { $stable = $true; break }
    $previousSize = $size
    Start-Sleep -Milliseconds 250
  }
  if (-not $stable) { throw "Unified browser screenshot missing: $mode" }
  $image = [System.Drawing.Bitmap]::FromFile($imagePath)
  try {
    $sample = $image.GetPixel(20,70)
    if ($sample.R -ge 230 -and $sample.G -ge 230 -and $sample.B -ge 230) { throw "Unified view rendered white: $mode" }
    $layout = $image.GetPixel(4,4)
    if ($layout.G -lt 180 -or $layout.R -gt 140) { throw "Unified view horizontal overflow or inactive script: $mode" }
  } finally { $image.Dispose() }
  Write-Host "Unified browser rendered: $mode"
}
exit 0
