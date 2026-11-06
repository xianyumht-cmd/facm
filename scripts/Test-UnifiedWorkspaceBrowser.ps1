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
  @{ Mode='updates'; Width=420; Height=620 },
  @{ Mode='cleanup'; Width=680; Height=760 },
  @{ Mode='cleanup'; Width=420; Height=620 },
  @{ Mode='profile'; Width=660; Height=730 },
  @{ Mode='profile'; Width=420; Height=620 },
  @{ Mode='advanced-tools'; Width=680; Height=760 },
  @{ Mode='advanced-tools'; Width=420; Height=620 },
  @{ Mode='quick-messages'; Width=680; Height=760 },
  @{ Mode='quick-messages'; Width=420; Height=620 },
  @{ Mode='bench'; Width=660; Height=730 },
  @{ Mode='bench'; Width=420; Height=620 },
  @{ Mode='dashboard-advanced'; Width=700; Height=760 },
  @{ Mode='dashboard-advanced'; Width=420; Height=620 },
  @{ Mode='profile-customization'; Width=690; Height=760 },
  @{ Mode='profile-customization'; Width=420; Height=620 },
  @{ Mode='player-advanced'; Width=740; Height=790 },
  @{ Mode='player-advanced'; Width=420; Height=620 },
  # Emulate Windows display scale factors. Physical window dimensions remain
  # realistic while the Chromium CSS viewport is reduced at 125–200% DPI.
  @{ Mode='home'; Width=525; Height=500; Dpi=1.25; Tag='home-dpi125' },
  @{ Mode='home'; Width=630; Height=600; Dpi=1.50; Tag='home-dpi150' },
  @{ Mode='home'; Width=840; Height=800; Dpi=2.00; Tag='home-dpi200' },
  @{ Mode='bench'; Width=525; Height=775; Dpi=1.25; Tag='bench-dpi125' },
  @{ Mode='advanced-tools'; Width=630; Height=900; Dpi=1.50; Tag='advanced-dpi150' },
  @{ Mode='cleanup'; Width=840; Height=840; Dpi=2.00; Tag='cleanup-dpi200' },
  @{ Mode='quick-messages'; Width=840; Height=840; Dpi=2.00; Tag='quick-dpi200' }
)
foreach ($scene in $cases) {
  $mode = $scene.Mode
  $caseName = if ($scene.Tag) { [string]$scene.Tag } else { "$mode-w$($scene.Width)-h$($scene.Height)" }
  $htmlPath = Join-Path $target.FullName "unified-$caseName.html"
  $imagePath = Join-Path $target.FullName "unified-$caseName.png"
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
  $profile = Join-Path $env:RUNNER_TEMP "ggman-unified-$caseName"
  $windowSize = "$($scene.Width),$($scene.Height)"
  $edgeArgs = @('--headless=new','--disable-gpu','--no-first-run','--no-default-browser-check','--hide-scrollbars',"--user-data-dir=$profile","--screenshot=$imagePath","--window-size=$windowSize")
  if ($scene.Dpi) { $edgeArgs += "--force-device-scale-factor=$($scene.Dpi.ToString([System.Globalization.CultureInfo]::InvariantCulture))" }
  $edgeArgs += $uri
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
  Write-Host "Unified browser rendered: $caseName"
}
exit 0
