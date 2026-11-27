$ErrorActionPreference = 'Stop'
$asset = 'src/FACM/Presentation/Glass/GGman-Unified-Workspace.html'
$source = Get-Content -LiteralPath $asset -Raw -Encoding utf8
# The compact interface must be action-first without losing bridge controls.
$tiles = @([regex]::Matches($source, '<button class="tile"[\s\S]*?</button>'))
if ($tiles.Count -ne 12 -or @($tiles | Where-Object { $_.Value.Contains('<small>') }).Count) {
  throw 'Launcher or browse cards regressed to redundant subcaptions.'
}
$repairActions = @([regex]::Matches($source, '<div class="tool-row"><button[^>]*data-repair-action='))
if ($repairActions.Count -ne 5) {
  throw 'Repair page lost its five direct-action controls.'
}
if (-not $source.Contains('main>section:not(#home)>h2{display:none}') -or
    $source.Contains('轻触即可进入') -or
    $source.Contains('游戏运行时禁止上传')) {
  throw 'Compact title, instruction, or game-lobby upload copy contract regressed.'
}
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
  @{ Mode='more'; Width=440; Height=560; Tag='browse-compact' },
  @{ Mode='client-status'; Width=440; Height=370; Tag='status-compact' },
  @{ Mode='repair'; Width=520; Height=530; Tag='repair-compact' },
  @{ Mode='postgame-settings'; Width=490; Height=450; Tag='postgame-compact' },
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
  @{ Mode='player-advanced'; Width=680; Height=820; Tag='player-advanced-extras' },
  @{ Mode='player-advanced'; Width=420; Height=680; Tag='player-region-narrow' },
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
var main = document.querySelector('main');
var secondaryScrollOwners = main ? Array.from(main.querySelectorAll('*')).filter(function(node) {
  var overflow = window.getComputedStyle(node).overflowY;
  return (overflow === 'auto' || overflow === 'scroll') && node.scrollHeight > node.clientHeight + 2;
}) : [];
var documentFits = document.documentElement.scrollWidth <= window.innerWidth + 2 &&
  document.documentElement.scrollHeight <= window.innerHeight + 2 &&
  document.body.scrollHeight <= window.innerHeight + 2 &&
  shell.scrollWidth <= shell.clientWidth + 2 &&
  main && main.scrollWidth <= main.clientWidth + 2 &&
  secondaryScrollOwners.length === 0;
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

# Exercise the separate Champion Select overlay in a real Edge renderer. The
# mock is a local bridge only; no LCU/network fixture or account data is used.
$companion = Get-Content -LiteralPath 'src/FACM/Presentation/Glass/GGman-Runtime-Companion.html' -Raw -Encoding utf8
if (-not $companion.Contains('shell-ready') -or -not $companion.Contains('swap:') -or
    -not $companion.Contains('assets-reset')) { throw 'Champion Select HTML bridge missing.' }
$companionMock = @'
<script>
window.chrome={webview:{
  postMessage:function(value){(window.__companionCommands||(window.__companionCommands=[])).push(value);},
  addEventListener:function(kind,cb){if(kind==='message')window.__companionReceive=cb;}
}};
</script>
'@
$companionProbe = @'
<script>
var sample={type:'snapshot',connected:true,championId:22,name:'Ashe',timer:14,phase:'FINALIZATION',
  bench:true,benchSurface:true,guideKind:'Mayhem',allowsRunes:false,canApply:true,
  choices:[99,81,51,12,15,1,2,3,4,5],selectable:[99,81,51,12,15,1,2,3,4,5],
  initialPick:false,choiceRevision:2,teams:[],build:{
    status:'ready',source:'local-test',mode:'ARAM',tier:'S',rows:[
      {category:'runes',title:'Rune fixture',evidence:'local'},
      {category:'core',title:'Build fixture',evidence:'local'}]},
  guide:{champion:'Ashe',tier:'S',winRate:0.52,core:['A','B'],augments:[
    {id:'fixture',assetKey:'augment:0',name:'Augment fixture',rank:1,rarity:'gold',winRate:54,pickRate:0.5}]}};
window.__companionReceive({data:sample});
window.__companionReceive({data:{type:'skin',id:'cloud-light',light:true,
 background:'#F4F7FD',surface:'#FFFFFF',surfaceRaised:'#EBF2FF',
 text:'#1A2743',muted:'#667797',accent:'#4E7EFF',border:'#D3DCEE',warning:'#EA8B39'}});
window.__companionReceive({data:{type:'asset',key:'augment:0',value:
  'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9WW3h8kAAAAASUVORK5CYII='}});
var slots=Array.from(document.querySelectorAll('#bench button'));
var gridPass=slots.length===10 && slots[0].offsetTop===slots[4].offsetTop &&
 slots[5].offsetTop===slots[9].offsetTop && slots[5].offsetTop>slots[0].offsetTop &&
 slots.every(x=>x.getBoundingClientRect().right<=document.querySelector('#content').getBoundingClientRect().right) &&
 slots[9].getBoundingClientRect().bottom<=document.querySelector('#content').getBoundingClientRect().bottom;
window.__companionReceive({data:Object.assign({},sample,{choices:[]})});
var emptyPass=document.querySelectorAll('#bench button').length===10 &&
 Array.from(document.querySelectorAll('#bench button')).every(x=>x.disabled);
window.__companionReceive({data:Object.assign({},sample,{choices:[99,81],selectable:[99,81]})});
var partialPass=document.querySelectorAll('#bench button').length===10 &&
 document.querySelectorAll('#bench button:not([disabled])').length===2;
window.__companionReceive({data:Object.assign({},sample,{phase:'BAN_PICK',championId:0,name:'',build:null,guide:null,
 initialPick:true,choiceRevision:3,choices:[22,55,99,88],selectable:[22,55],canApply:false})});
var cardPass=document.querySelector('#champTitle').textContent==='选择英雄' &&
 document.querySelectorAll('#bench button:not([disabled])').length===2 &&
 document.querySelector('[data-champion-id="88"]').disabled;
document.querySelector('[data-champion-id="22"]').click();
document.querySelector('[data-champion-id="88"]').click();
cardPass=cardPass&&window.__companionCommands.includes('pick:22:3')&&
 !window.__companionCommands.includes('pick:88:3');
window.__companionReceive({data:Object.assign({},sample,{guideKind:'None',bench:false,benchSurface:false,choices:[],allowsRunes:true,guide:null})});
var normalPass=document.querySelector('#benchSection').hidden &&
 document.querySelector('[data-tab="augments"]').hidden && !document.querySelector('#runes').hidden;
window.__companionReceive({data:sample});
document.querySelector('[data-champion-id="99"]').click();
var swapPass=window.__companionCommands.includes('swap:99:2');
var extra=Array.from({length:26},(_,i)=>({id:'id'+i,assetKey:'augment:'+i,name:'Augment '+i,rank:i+1,rarity:i<24?'gold':'silver',winRate:54,pickRate:0.5}));
window.__companionReceive({data:Object.assign({},sample,{guide:Object.assign({},sample.guide,{augments:extra})})});
var morePass=!document.querySelector('#augmentMore').hidden&&document.querySelectorAll('#augments .augment').length===20;
document.querySelector('[data-filter="silver"]').click();
var filterPass=document.querySelectorAll('#augments .augment').length===2&&document.querySelector('#augments').textContent.includes('0.5%');
var search=document.querySelector('#augmentSearch');search.value='25';search.dispatchEvent(new Event('input'));
var searchPass=document.querySelectorAll('#augments .augment').length===1&&document.querySelector('#augments').textContent.includes('Augment 25');
search.value='';search.dispatchEvent(new Event('input'));document.querySelector('[data-filter="all"]').click();
window.__companionReceive({data:sample});
var firstRow=document.querySelector('#augments .augment');
var defaultVisible=!document.querySelector('#augmentSection').hidden &&
 document.querySelector('#buildSection').hidden && firstRow &&
 firstRow.getBoundingClientRect().bottom<=document.querySelector('#content').getBoundingClientRect().bottom &&
 document.querySelector('#status').classList.contains('connected');
document.querySelector('[data-tab="build"]').click();
window.__companionReceive({data:sample});
var explicitTabKept=!document.querySelector('#buildSection').hidden &&
 document.querySelector('#augmentSection').hidden &&
 document.querySelector('#runes').hidden &&
 document.querySelector('#items').getBoundingClientRect().bottom<=document.querySelector('#content').getBoundingClientRect().bottom;
document.querySelector('[data-tab="augments"]').click();
var pass=morePass && filterPass && searchPass && gridPass && emptyPass && partialPass && cardPass && swapPass && normalPass && defaultVisible && explicitTabKept &&
 document.querySelectorAll('#bench button').length===10 &&
 document.querySelectorAll('#recommend .rec').length===1 &&
 document.querySelectorAll('#augments .augment').length===1 &&
 document.querySelectorAll('#augmentFilters button').length===4 &&
 document.querySelector('#timer').textContent==='14s' &&
 getComputedStyle(document.documentElement).getPropertyValue('--bg').trim()==='#F4F7FD' &&
 document.documentElement.dataset.skin==='cloud-light';
var mark=document.createElement('div');
mark.style.cssText='position:fixed;left:0;top:0;width:12px;height:12px;z-index:2147483647;background:'+
 (pass?'#00ff00':'#ff0000');
document.body.appendChild(mark);
</script>
'@
$companionPage=$companion.Replace('<script>', $companionMock + '<script>').Replace('</body>', $companionProbe + '</body>')
$companionPath=Join-Path $target.FullName 'champselect-companion.html'
$companionPng=Join-Path $target.FullName 'champselect-companion.png'
Set-Content -LiteralPath $companionPath -Value $companionPage -Encoding utf8 -NoNewline
$companionUri=([System.Uri]::new($companionPath)).AbsoluteUri
$companionProfile=Join-Path $env:RUNNER_TEMP 'ggman-companion-browser-fixture'
& $edge '--headless=new' '--disable-gpu' '--no-first-run' '--no-default-browser-check' '--hide-scrollbars' "--user-data-dir=$companionProfile" "--screenshot=$companionPng" '--window-size=380,480' $companionUri
$companionPrevious=-1
$companionStable=$false
for ($attempt=0; $attempt -lt 40; $attempt++) {
  $size=if (Test-Path -LiteralPath $companionPng) { (Get-Item -LiteralPath $companionPng).Length } else { 0 }
  if ($size -gt 1536 -and $size -eq $companionPrevious) { $companionStable=$true; break }
  $companionPrevious=$size
  Start-Sleep -Milliseconds 250
}
if (-not $companionStable) { throw 'Champion Select overlay screenshot missing or incomplete.' }
$picture=[System.Drawing.Bitmap]::FromFile($companionPng)
try {
  $mark=$picture.GetPixel(4,4)
  if ($mark.G -lt 180 -or $mark.R -gt 140) { throw 'Champion Select overlay JS fixture or visual rendering failed.' }
  $background=$picture.GetPixel(25,80)
  if ($background.R -lt 200 -or $background.G -lt 200 -or $background.B -lt 200) {
    throw 'Champion Select overlay ignored the light skin palette.'
  }
} finally { $picture.Dispose() }
Write-Host 'Champion Select WebView2 overlay HTML fixture rendered.'


# Verify home skin messages actually recolor the running page, without
# navigating to the skin chooser. The marker detects a broken JS bridge
# and a palette that was accepted without changing computed CSS values.
$skinMock = @'
<script>
window.chrome={webview:{
  postMessage:function(value){(window.__sceneCommands||(window.__sceneCommands=[])).push(value);},
  addEventListener:function(kind,cb){if(kind==='message')window.__skinReceive=cb;}
}};
</script>
'@
$skinProbe = @'
<script>
var palette={id:'SKIN',name:'Skin fixture',background:'BACKGROUND',
 surface:'SURFACE',surfaceRaised:'RAISED',text:'TEXT',
 muted:'MUTED',accent:'ACCENT',border:'BORDER',light:ISLIGHT};
window.__skinReceive({data:{type:'themes',selected:palette.id,items:[palette]}});
var actual=getComputedStyle(document.documentElement);
var pass=document.body.dataset.mode==='home' &&
 document.documentElement.dataset.skin===palette.id &&
 document.documentElement.dataset.skinLight===String(palette.light) &&
 actual.getPropertyValue('--canvas').trim().toLowerCase()===palette.background.toLowerCase() &&
 actual.getPropertyValue('--accent').trim().toLowerCase()===palette.accent.toLowerCase() &&
 document.querySelectorAll('#themeList .theme-card.selected').length===1 &&
 document.getElementById('themeResult').textContent.indexOf('当前皮肤：')===0;
var sendFixture=function(data){window.__skinReceive({data:data});};
var tiles=Array.from(document.querySelectorAll('#home .tile')).map(x=>x.dataset.route).join(',');
var scenePass=tiles==='live,player,mayhem,efficiency';
var cases=[['Lobby','matchmaking-settings'],['Matchmaking','efficiency'],['ReadyCheck','efficiency'],
 ['ChampSelect','recommendation'],['InProgress','live'],['EndOfGame','player']];
cases.forEach(function(pair){
 sendFixture({type:'status',connected:true,phase:pair[0]});
 scenePass=scenePass&&document.querySelector('#sceneShortcut').dataset.route===pair[1]&&
  Array.from(document.querySelectorAll('#home .tile')).map(x=>x.dataset.route).join(',')===tiles;
});
sendFixture({type:'status',connected:false,observed:true,clientProcess:true,gameProcess:false});
scenePass=scenePass&&document.querySelector('#sceneShortcut').dataset.route==='repair';
sendFixture('detail:efficiency');
sendFixture({type:'quick-tools',values:{autoSearch:true,autoAccept:true,autoReconnect:false,autoHonor:false,autoReturn:false},exitHotkey:'Ctrl+F8',lobbyHotkey:'Ctrl+F9'});
var hotkey=document.querySelector('#exitGameHotkey');
hotkey.dispatchEvent(new KeyboardEvent('keydown',{key:'F7',code:'F7',ctrlKey:true,bubbles:true,cancelable:true}));
sendFixture({type:'quick-tools',values:{autoSearch:true,autoAccept:true},exitHotkey:'Ctrl+F8',lobbyHotkey:'Ctrl+F9'});
var keysPass=hotkey.value==='Ctrl+F7';
sendFixture({type:'status',connected:true,phase:'ReadyCheck'});
var automationPass=document.querySelector('[data-phase-note="autoSearch"]').textContent==='等待组队大厅'&&
 document.querySelector('[data-phase-note="autoAccept"]').textContent==='当前阶段 · 等待条件';
sendFixture('detail:player');
var gameId='9007199254740993';
sendFixture({type:'history-details',name:'Fixture',level:30,rows:[{gameId:gameId,champion:'Ashe',outcome:'win',kills:3,deaths:2,assists:8}]});
document.querySelector('[data-recent-game-id]').click();
var historyPass=window.__sceneCommands.includes('history-open-detail:'+gameId);
sendFixture('detail:player-advanced');
sendFixture({type:'history-advanced-list',name:'Fixture',rows:[{id:gameId,champion:'Ashe',outcome:'win',queue:450}]});
sendFixture({type:'history-select-local',id:gameId});
historyPass=historyPass&&!document.querySelector('#advancedHistoryDetail').hidden&&
 document.querySelector('#advancedHistoryDetailTitle').textContent.includes(gameId)&&
 !document.querySelector('#advancedHistoryPage details.history-extra').open;
var extended=Array.from({length:30},(_,i)=>({id:String(100+i),champion:'Ashe',championId:22,outcome:i===29?'unknown':i%2?'win':'loss',queue:450,kills:4,deaths:2,assists:6}));
sendFixture({type:'history-advanced-list',name:'Fixture',rows:extended,hasMore:true});
historyPass=historyPass&&document.querySelectorAll('#advancedHistoryRows [data-advanced-game-id]').length===30&&
 document.querySelector('#historyChampionStatsRows').textContent.includes('29 场')&&
 !document.querySelector('#advancedHistoryMore').hidden;
document.querySelector('#advancedHistoryMore').click();
historyPass=historyPass&&window.__sceneCommands.includes('history-advanced-more');
sendFixture('detail:mayhem');
sendFixture({type:'status',connected:true,phase:'InProgress'});
var currentGuide=document.querySelector('#mayhemCurrent');
var guidePass=!currentGuide.disabled;
currentGuide.click();guidePass=guidePass&&currentGuide.disabled&&window.__sceneCommands.includes('mayhem-current');
sendFixture({type:'mayhem-failed',status:'current-unavailable'});
guidePass=guidePass&&!currentGuide.disabled;
sendFixture({type:'status',connected:true,phase:'Lobby'});
guidePass=guidePass&&currentGuide.disabled;
sendFixture('detail:recommendation');
sendFixture({type:'status',connected:true,phase:'ChampSelect'});
sendFixture({type:'recommendation-details',status:'ready',canApply:true,allowsRunes:true,rows:[{category:'runes',title:'Fixture',evidence:'Fixture evidence'}]});
var apply=document.querySelector('#recommendationApply');
var recommendationPass=!apply.disabled&&apply.getBoundingClientRect().top<document.querySelector('#recommendationRows').getBoundingClientRect().top;
apply.click();document.querySelector('#applyItems').dispatchEvent(new Event('change'));
recommendationPass=recommendationPass&&apply.disabled;
sendFixture({type:'recommendation-result',status:'failed'});
recommendationPass=recommendationPass&&!apply.disabled;
sendFixture({type:'status',connected:true,phase:'InProgress'});
recommendationPass=recommendationPass&&apply.disabled;
sendFixture('home');
pass=pass&&scenePass&&keysPass&&automationPass&&historyPass&&recommendationPass&&guidePass;
var marker=document.createElement('span');
marker.style.cssText='position:fixed;left:0;top:0;width:12px;height:12px;z-index:2147483647;background:'+
 (pass?'#00ff00':'#ff0000');
document.body.appendChild(marker);
</script>
'@
$skins=@(
  @{ Id='cloud-light'; Light='true'; Background='#F4F7FD'; Surface='#FFFFFF'; Raised='#EBF2FF'; Text='#1A2743'; Muted='#667797'; Accent='#4E7EFF'; Border='#D3DCEE' },
  @{ Id='obsidian-gold'; Light='false'; Background='#090A0A'; Surface='#181816'; Raised='#231F18'; Text='#F8DEA1'; Muted='#B09B6F'; Accent='#CC9636'; Border='#B18132' }
)
foreach ($skin in $skins) {
  $probe=$skinProbe.Replace('SKIN',$skin.Id).Replace('ISLIGHT',$skin.Light)
  foreach ($field in @('Background','Surface','Raised','Text','Muted','Accent','Border')) {
    $probe=$probe.Replace($field.ToUpperInvariant(),[string]$skin[$field])
  }
  $page=$source.Replace('<script>', $skinMock + '<script>').Replace('</body>', $probe + '</body>')
  $htmlPath=Join-Path $target.FullName "skin-$($skin.Id).html"
  $imagePath=Join-Path $target.FullName "skin-$($skin.Id).png"
  Set-Content -LiteralPath $htmlPath -Value $page -Encoding utf8 -NoNewline
  $uri=([System.Uri]::new($htmlPath)).AbsoluteUri
  $profile=Join-Path $env:RUNNER_TEMP "ggman-skin-$($skin.Id)"
  & $edge '--headless=new' '--disable-gpu' '--no-first-run' '--no-default-browser-check' '--hide-scrollbars' "--user-data-dir=$profile" "--screenshot=$imagePath" '--window-size=420,400' $uri
  $previous=-1;$stable=$false
  for($attempt=0;$attempt -lt 40;$attempt++){
    $size=if(Test-Path -LiteralPath $imagePath){(Get-Item -LiteralPath $imagePath).Length}else{0}
    if($size -gt 1536 -and $size -eq $previous){$stable=$true;break}
    $previous=$size;Start-Sleep -Milliseconds 250
  }
  if(-not $stable){throw "Skin screenshot missing: $($skin.Id)"}
  $bitmap=[System.Drawing.Bitmap]::FromFile($imagePath)
  try{
    $marker=$bitmap.GetPixel(4,4)
    if($marker.G -lt 180 -or $marker.R -gt 140){throw "Saved skin did not recolor unified home: $($skin.Id)"}
  }finally{$bitmap.Dispose()}
  Write-Host "GGman skin rendered from native bridge: $($skin.Id)"
}

exit 0
