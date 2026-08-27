[CmdletBinding()]
param(
    [string]$CandidatePath = '',
    [ValidateSet('General', 'MigrationBaseline', 'MigrationAfter', 'UpdaterRollback')]
    [string]$Stage = 'General',
    [string]$OutputDirectory = '',
    [switch]$SelfTest
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$script:SchemaVersion = 1
$script:CollectorVersion = '1.1.0'
$script:ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$script:RepoRoot = Split-Path -Parent $script:ScriptRoot
$script:LogLines = New-Object 'System.Collections.Generic.List[string]'

function Protect-Text {
    param([AllowNull()][string]$Text)
    if ($null -eq $Text) { return '' }
    $value = [string]$Text
    $value = [regex]::Replace($value, '(?i)\b(Bearer|Basic)\s+[A-Za-z0-9+/_=\-.]+', '$1 <redacted>')
    $value = [regex]::Replace($value, '(?i)\b(token|password|secret|cookie|authorization)\b\s*[:=]\s*[^\s,;]+', '$1=<redacted>')
    $value = [regex]::Replace($value, '(?i)[A-Z]:\\[^\r\n\t"'']+', '<path>')
    $value = [regex]::Replace($value, '\\\\[^\r\n\t"'']+', '<unc-path>')
    if (-not [string]::IsNullOrWhiteSpace($env:USERPROFILE)) { $value = $value.Replace($env:USERPROFILE, '<user-profile>') }
    if (-not [string]::IsNullOrWhiteSpace($env:USERNAME)) { $value = [regex]::Replace($value, [regex]::Escape($env:USERNAME), '<user>') }
    return $value
}

function Add-SafeLog {
    param([string]$Message)
    $script:LogLines.Add(('{0:u} {1}' -f [DateTime]::UtcNow, (Protect-Text $Message)))
}

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw ('SelfTest failed: ' + $Message) }
}

function Get-RegistryValueSafe {
    param([string]$Path, [string]$Name)
    try {
        if (-not (Test-Path -LiteralPath $Path)) { return $null }
        return (Get-ItemProperty -LiteralPath $Path -Name $Name -ErrorAction Stop).$Name
    }
    catch { return $null }
}

function Get-IsAdministrator {
    try {
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
        $principal = New-Object Security.Principal.WindowsPrincipal($identity)
        return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    }
    catch {
        Add-SafeLog ('Administrator probe unavailable: ' + $_.Exception.GetType().Name)
        return $false
    }
}

function Get-WindowsFacts {
    $caption = ''
    $version = [Environment]::OSVersion.Version.ToString()
    $build = [Environment]::OSVersion.Version.Build
    $architecture = [string]$env:PROCESSOR_ARCHITECTURE
    try {
        $os = Get-CimInstance Win32_OperatingSystem -ErrorAction Stop
        if ($null -ne $os) {
            $caption = [string]$os.Caption
            if ($os.Version) { $version = [string]$os.Version }
            if ($os.BuildNumber) { $build = [int]$os.BuildNumber }
            if ($os.OSArchitecture) { $architecture = [string]$os.OSArchitecture }
        }
    }
    catch { Add-SafeLog ('OS CIM probe unavailable: ' + $_.Exception.GetType().Name) }

    $cv = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
    $productName = [string](Get-RegistryValueSafe $cv 'ProductName')
    $displayVersion = [string](Get-RegistryValueSafe $cv 'DisplayVersion')
    $target = 'other'
    if ($build -eq 17763) { $target = 'compat.windows-10-1809' }
    elseif ($build -eq 19045) { $target = 'compat.windows-10-22h2' }
    elseif ($build -ge 22000 -and (($caption -match 'Windows 11') -or ($productName -match 'Windows 11'))) { $target = 'compat.windows-11' }

    return [ordered]@{
        caption = Protect-Text $caption
        productName = Protect-Text $productName
        displayVersion = $displayVersion
        version = $version
        build = $build
        ubr = Get-RegistryValueSafe $cv 'UBR'
        architecture = Protect-Text $architecture
        releaseEvidenceTargetCandidate = $target
    }
}

function Get-UacFacts {
    $key = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System'
    return [ordered]@{
        isAdministrator = Get-IsAdministrator
        enableLua = Get-RegistryValueSafe $key 'EnableLUA'
        consentPromptBehaviorAdmin = Get-RegistryValueSafe $key 'ConsentPromptBehaviorAdmin'
        promptOnSecureDesktop = Get-RegistryValueSafe $key 'PromptOnSecureDesktop'
    }
}

function Get-SecurityFacts {
    $defender = [ordered]@{ available = $false; antivirusEnabled = $null; realTimeProtectionEnabled = $null; behaviorMonitorEnabled = $null }
    try {
        $mp = Get-MpComputerStatus -ErrorAction Stop
        if ($null -ne $mp) {
            $defender.available = $true
            $defender.antivirusEnabled = [bool]$mp.AntivirusEnabled
            $defender.realTimeProtectionEnabled = [bool]$mp.RealTimeProtectionEnabled
            $defender.behaviorMonitorEnabled = [bool]$mp.BehaviorMonitorEnabled
        }
    }
    catch { Add-SafeLog ('Defender status probe unavailable: ' + $_.Exception.GetType().Name) }
    return [ordered]@{
        defender = $defender
        smartScreenMachine = [string](Get-RegistryValueSafe 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer' 'SmartScreenEnabled')
        smartScreenUser = [string](Get-RegistryValueSafe 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer' 'SmartScreenEnabled')
        note = 'Configuration facts only; actual SmartScreen reputation/UI requires manual evidence.'
    }
}

function Initialize-DisplayProbe {
    if ('FacmEvidence.DisplayProbe' -as [type]) { return }
    Add-Type -Language CSharp -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
namespace FacmEvidence {
  public sealed class MonitorSnapshot {
    public int Index { get; set; } public bool Primary { get; set; }
    public int X { get; set; } public int Y { get; set; }
    public int Width { get; set; } public int Height { get; set; }
    public uint DpiX { get; set; } public uint DpiY { get; set; }
    public bool DpiAvailable { get; set; } public string DeviceName { get; set; }
  }
  public static class DisplayProbe {
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L,T,R,B; }
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Auto)] struct MONITORINFOEX {
      public int cbSize; public RECT monitor; public RECT work; public uint flags;
      [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string device;
    }
    delegate bool EnumProc(IntPtr monitor, IntPtr hdc, ref RECT rect, IntPtr data);
    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, EnumProc callback, IntPtr data);
    [DllImport("user32.dll", CharSet=CharSet.Auto)] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
    public static MonitorSnapshot[] GetMonitors() {
      var result = new List<MonitorSnapshot>(); int index = 0;
      EnumProc callback = delegate(IntPtr handle, IntPtr hdc, ref RECT rect, IntPtr data) {
        var info = new MONITORINFOEX(); info.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
        if (!GetMonitorInfo(handle, ref info)) return true;
        uint dx=96, dy=96; bool ok=false; try { ok=GetDpiForMonitor(handle,0,out dx,out dy)==0; } catch { dx=96; dy=96; }
        result.Add(new MonitorSnapshot { Index=index++, Primary=(info.flags&1)!=0, X=info.monitor.L, Y=info.monitor.T,
          Width=info.monitor.R-info.monitor.L, Height=info.monitor.B-info.monitor.T, DpiX=dx, DpiY=dy,
          DpiAvailable=ok, DeviceName=info.device ?? String.Empty }); return true; };
      EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero); return result.ToArray();
    }
  }
}
'@
}

function Get-DisplayFacts {
    $items = @()
    try {
        Initialize-DisplayProbe
        foreach ($m in [FacmEvidence.DisplayProbe]::GetMonitors()) {
            $scale = $null
            if ($m.DpiAvailable -and $m.DpiX -gt 0) { $scale = [Math]::Round(($m.DpiX / 96.0) * 100.0) }
            $items += [ordered]@{
                index = $m.Index; primary = $m.Primary
                bounds = [ordered]@{ x = $m.X; y = $m.Y; width = $m.Width; height = $m.Height }
                dpiX = $m.DpiX; dpiY = $m.DpiY; scalePercent = $scale; dpiAvailable = $m.DpiAvailable
                device = Protect-Text $m.DeviceName
            }
        }
    }
    catch { Add-SafeLog ('Display probe unavailable: ' + $_.Exception.GetType().Name) }
    $dpi = @($items | Where-Object { $_.dpiAvailable } | ForEach-Object { $_.dpiX } | Sort-Object -Unique)
    return [ordered]@{
        monitorCount = $items.Count
        distinctDpiCount = $dpi.Count
        hasNegativeCoordinates = [bool](@($items | Where-Object { $_.bounds.x -lt 0 -or $_.bounds.y -lt 0 }).Count -gt 0)
        mixedDpiObserved = [bool]($dpi.Count -gt 1)
        monitors = $items
    }
}

function Get-AccessibilityFacts {
    $highContrast = $null
    try {
        Add-Type -AssemblyName System.Windows.Forms -ErrorAction Stop
        $highContrast = [System.Windows.Forms.SystemInformation]::HighContrast
    }
    catch { Add-SafeLog ('High Contrast probe unavailable: ' + $_.Exception.GetType().Name) }
    return [ordered]@{
        highContrast = $highContrast
        textScalePercent = Get-RegistryValueSafe 'HKCU:\Software\Microsoft\Accessibility' 'TextScaleFactor'
        note = 'Keyboard focus and screen-reader behavior require manual evidence.'
    }
}

function Resolve-Candidate {
    param([string]$RequestedPath)
    $paths = New-Object 'System.Collections.Generic.List[string]'
    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) { $paths.Add($RequestedPath) }
    $paths.Add((Join-Path $script:RepoRoot 'FACM.App.exe'))
    $paths.Add((Join-Path $script:RepoRoot 'FACM.exe'))
    $paths.Add((Join-Path (Get-Location).Path 'FACM.App.exe'))
    $paths.Add((Join-Path (Get-Location).Path 'FACM.exe'))
    foreach ($path in $paths) {
        try { if (Test-Path -LiteralPath $path -PathType Leaf) { return [IO.Path]::GetFullPath($path) } } catch { }
    }
    return $null
}

function Get-FileEvidence {
    param([AllowNull()][string]$Path, [string]$Role)
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return [ordered]@{ role = $Role; present = $false }
    }
    $item = Get-Item -LiteralPath $Path
    $hash = Get-FileHash -LiteralPath $Path -Algorithm SHA256
    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($Path)
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    $subject = ''; $thumbprint = ''
    if ($null -ne $signature.SignerCertificate) {
        $subject = Protect-Text ([string]$signature.SignerCertificate.Subject)
        $thumbprint = [string]$signature.SignerCertificate.Thumbprint
    }
    return [ordered]@{
        role = $Role; present = $true; fileName = $item.Name; sizeBytes = $item.Length
        sha256 = $hash.Hash.ToUpperInvariant(); fileVersion = Protect-Text ([string]$version.FileVersion)
        productVersion = Protect-Text ([string]$version.ProductVersion); authenticodeStatus = [string]$signature.Status
        signerSubject = $subject; signerThumbprint = $thumbprint
    }
}

function Get-SettingsMetadata {
    param([AllowNull()][string]$Candidate)
    $roots = @([pscustomobject]@{ Label='collector-directory'; Path=$script:RepoRoot })
    if (-not [string]::IsNullOrWhiteSpace($Candidate)) {
        $candidateRoot = Split-Path -Parent $Candidate
        if ($candidateRoot -ne $script:RepoRoot) { $roots += [pscustomobject]@{ Label='candidate-directory'; Path=$candidateRoot } }
    }
    $relative = @('settings.ini','settings.v2.json','runtime\recovery\settings.v2.lkg.json','runtime\recovery\state.json')
    $records = @()
    foreach ($root in $roots) {
        foreach ($rel in $relative) {
            $path = Join-Path $root.Path $rel
            if (Test-Path -LiteralPath $path -PathType Leaf) {
                $item = Get-Item -LiteralPath $path
                $records += [ordered]@{
                    root = $root.Label; relativePath = $rel.Replace('\','/'); sizeBytes = $item.Length
                    lastWriteTimeUtc = $item.LastWriteTimeUtc.ToString('o')
                    sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToUpperInvariant()
                }
            }
        }
    }
    return $records
}

function New-EvidenceSlots {
    param($Windows,$Uac,$Security,$Display,$Accessibility,$Candidate,[string]$CaptureStage)
    $displayStatus = 'manual_required'
    if ($Display.monitorCount -ge 2) { $displayStatus = 'observed_requires_interaction' }
    $signatureStatus = 'manual_required'
    if ($Candidate.present -and $Candidate.authenticodeStatus -eq 'Valid') { $signatureStatus = 'observed_requires_review' }
    return @(
        [ordered]@{ id='compat.non-admin-uac-cancel'; captureStatus='manual_required'; automaticObservation=[ordered]@{ isAdministrator=$Uac.isAdministrator; enableLua=$Uac.enableLua }; requiredManualAction='Run as a standard user, trigger an elevation-required FACM action, cancel UAC, and capture expected non-destructive behavior.' },
        [ordered]@{ id='security.defender-smartscreen'; captureStatus='manual_required'; automaticObservation=$Security; requiredManualAction='Run the final signed candidate through Defender/SmartScreen and record the actual UI/reputation result.' },
        [ordered]@{ id='compat.windows-target'; captureStatus='observed_requires_review'; automaticObservation=[ordered]@{ targetCandidate=$Windows.releaseEvidenceTargetCandidate; build=$Windows.build; displayVersion=$Windows.displayVersion }; requiredManualAction='Confirm this is the intended real-user Windows machine, not a hosted runner/server image.' },
        [ordered]@{ id='display.real-mixed-dpi-multimonitor'; captureStatus=$displayStatus; automaticObservation=$Display; requiredManualAction='Move Shell/F/Diagnostics across monitors at required scales and record clipping, placement and focus behavior.' },
        [ordered]@{ id='accessibility.real-machine'; captureStatus='manual_required'; automaticObservation=$Accessibility; requiredManualAction='Verify keyboard-only focus, High Contrast, text scaling and a basic screen reader.' },
        [ordered]@{ id='migration.settings-3.5.15-to-4.0'; captureStatus='manual_required'; automaticObservation=[ordered]@{ stage=$CaptureStage }; requiredManualAction='Capture MigrationBaseline before upgrade and MigrationAfter after 4.0 launch/relaunch/rollback, then compare the bundles.' },
        [ordered]@{ id='update.interrupted-replacement-rollback'; captureStatus='manual_required'; automaticObservation=[ordered]@{ stage=$CaptureStage }; requiredManualAction='Use a controlled updater interruption/failure exercise and capture old-version preservation plus rollback result.' },
        [ordered]@{ id='release.final-signature-package'; captureStatus=$signatureStatus; automaticObservation=$Candidate; requiredManualAction='Confirm final candidate identity, production signing chain, package hash and release artifact identity match.' }
    )
}

function New-Document {
    param($Windows,$Uac,$Security,$Display,$Accessibility,$Candidate,$Legacy,$Settings,$Slots,[string]$CaptureStage)
    return [ordered]@{
        schemaVersion = $script:SchemaVersion; collectorVersion = $script:CollectorVersion
        stage = $CaptureStage; collectedAtUtc = [DateTime]::UtcNow.ToString('o')
        machine = [ordered]@{ windows=$Windows; uac=$Uac; security=$Security; display=$Display; accessibility=$Accessibility }
        candidate = $Candidate; legacy = $Legacy; settingsFiles = $Settings; evidenceSlots = $Slots
        privacy = [ordered]@{
            containsComputerName=$false; containsUserName=$false; containsFullUserProfilePath=$false; containsApplicationSecrets=$false
            pathPolicy='Only stable labels, relative paths and executable file names are stored.'
        }
    }
}

function Get-Readme {
    param($Document,[string]$ZipName)
    $lines = New-Object 'System.Collections.Generic.List[string]'
    $lines.Add('FACM 4.0 Real-Machine Release Evidence Bundle')
    $lines.Add('================================================')
    $lines.Add('')
    $lines.Add('This collector is READ-ONLY. It does not deploy, update, restart, elevate, delete files, or change production pointers.')
    $lines.Add('Automatic observations are NOT release PASS decisions. Manual release blockers remain manual until reviewed.')
    $lines.Add('')
    $lines.Add(('Collector version: {0}' -f $Document.collectorVersion))
    $lines.Add(('Stage: {0}' -f $Document.stage))
    $lines.Add(('Collected UTC: {0}' -f $Document.collectedAtUtc))
    $lines.Add(('OS build: {0}' -f $Document.machine.windows.build))
    $lines.Add(('Release target candidate: {0}' -f $Document.machine.windows.releaseEvidenceTargetCandidate))
    $lines.Add(('Monitor count: {0}' -f $Document.machine.display.monitorCount))
    $lines.Add(('Mixed DPI observed: {0}' -f $Document.machine.display.mixedDpiObserved))
    $lines.Add(('Candidate present: {0}' -f $Document.candidate.present))
    if ($Document.candidate.present) {
        $lines.Add(('Candidate file: {0}' -f $Document.candidate.fileName))
        $lines.Add(('Candidate SHA-256: {0}' -f $Document.candidate.sha256))
        $lines.Add(('Authenticode: {0}' -f $Document.candidate.authenticodeStatus))
    }
    $lines.Add('')
    $lines.Add('Manual release evidence still required:')
    foreach ($slot in $Document.evidenceSlots) { $lines.Add(('- [{0}] {1}: {2}' -f $slot.captureStatus,$slot.id,$slot.requiredManualAction)) }
    $lines.Add('')
    $lines.Add(('Bundle ZIP: {0}' -f $ZipName))
    $lines.Add('Do not mark repository release evidence Passed directly from this bundle without review/import validation.')
    return ($lines -join [Environment]::NewLine)
}

function Write-EvidenceBundle {
    param($Document,[string]$Directory,[switch]$ReplaceExisting)
    [IO.Directory]::CreateDirectory($Directory) | Out-Null
    $jsonPath = Join-Path $Directory 'evidence.json'
    $readmePath = Join-Path $Directory 'README.txt'
    $logPath = Join-Path $Directory 'collector.log'
    $zipPath = $Directory + '.zip'
    if ($ReplaceExisting -and (Test-Path -LiteralPath $zipPath)) { [IO.File]::Delete($zipPath) }
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    [IO.File]::WriteAllText($jsonPath, ($Document | ConvertTo-Json -Depth 16), $utf8)
    [IO.File]::WriteAllText($readmePath, (Get-Readme $Document ([IO.Path]::GetFileName($zipPath))), $utf8)
    Add-SafeLog 'Evidence document and manual checklist written.'
    [IO.File]::WriteAllLines($logPath, $script:LogLines.ToArray(), $utf8)
    Compress-Archive -Path (Join-Path $Directory '*') -DestinationPath $zipPath -CompressionLevel Optimal
    return $zipPath
}

function Invoke-SelfTest {
    $probe = 'Authorization: Bearer abc123 token=secret-value C:\Users\Alice\private\file.txt \\server\share\x'
    $safe = Protect-Text $probe
    Assert-True ($safe -notmatch 'abc123|secret-value|C:\\Users|\\\\server') 'secret/path redaction'

    $windows = [ordered]@{ releaseEvidenceTargetCandidate='compat.windows-10-22h2'; build=19045; displayVersion='22H2' }
    $uac = [ordered]@{ isAdministrator=$false; enableLua=1 }
    $security = [ordered]@{ defender=[ordered]@{ available=$true }; note='manual' }
    $display = [ordered]@{ monitorCount=2; distinctDpiCount=2; hasNegativeCoordinates=$true; mixedDpiObserved=$true; monitors=@() }
    $accessibility = [ordered]@{ highContrast=$false; textScalePercent=125 }
    $candidate = [ordered]@{ present=$true; fileName='FACM.App.exe'; sha256=('A' * 64); authenticodeStatus='Valid' }
    $legacy = [ordered]@{ present=$false; role='facm-3.5.15-legacy' }
    $slots = New-EvidenceSlots $windows $uac $security $display $accessibility $candidate 'General'
    Assert-True ($slots.Count -eq 8) 'evidence slot count'
    Assert-True (@($slots | Where-Object { $_.captureStatus -eq 'manual_required' }).Count -ge 5) 'manual-required blockers remain explicit'

    $tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('facm4-evidence-selftest-' + [Guid]::NewGuid().ToString('N'))
    $bundle = Join-Path $tempRoot 'bundle'
    [IO.Directory]::CreateDirectory($tempRoot) | Out-Null
    try {
        $doc = New-Document $windows $uac $security $display $accessibility $candidate $legacy @() $slots 'General'
        $zip = Write-EvidenceBundle $doc $bundle -ReplaceExisting
        Assert-True (Test-Path -LiteralPath $zip -PathType Leaf) 'ZIP creation'
        $json = Get-Content -LiteralPath (Join-Path $bundle 'evidence.json') -Raw
        $parsed = $json | ConvertFrom-Json
        Assert-True ($parsed.schemaVersion -eq 1) 'schema roundtrip'
        Assert-True ($parsed.evidenceSlots.Count -eq 8) 'JSON slot roundtrip'
        Assert-True ($json -notmatch 'abc123|secret-value|C:\\Users\\Alice|\\\\server') 'bundle JSON redaction'
        Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction Stop
        $archive = [IO.Compression.ZipFile]::OpenRead($zip)
        try {
            $names = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\','/') })
            Assert-True ($names -contains 'evidence.json') 'ZIP evidence.json entry'
            Assert-True ($names -contains 'README.txt') 'ZIP README.txt entry'
            Assert-True ($names -contains 'collector.log') 'ZIP collector.log entry'
        }
        finally { $archive.Dispose() }
    }
    finally { if ([IO.Directory]::Exists($tempRoot)) { [IO.Directory]::Delete($tempRoot,$true) } }
    Write-Host 'FACM 4.0 real-machine evidence collector self-test: SUCCESS'
}

if ($SelfTest) { Invoke-SelfTest; exit 0 }

Add-SafeLog ('Collector start; stage=' + $Stage)
$candidatePathResolved = Resolve-Candidate $CandidatePath
$windows = Get-WindowsFacts
$uac = Get-UacFacts
$security = Get-SecurityFacts
$display = Get-DisplayFacts
$accessibility = Get-AccessibilityFacts
$candidate = Get-FileEvidence $candidatePathResolved 'facm4-candidate'
$legacyPath = $null
if ($candidatePathResolved) {
    $probeLegacy = Join-Path (Split-Path -Parent $candidatePathResolved) 'FACM.exe'
    if ((Test-Path -LiteralPath $probeLegacy -PathType Leaf) -and $probeLegacy -ne $candidatePathResolved) { $legacyPath = $probeLegacy }
}
$legacy = Get-FileEvidence $legacyPath 'facm-3.5.15-legacy'
$settings = Get-SettingsMetadata $candidatePathResolved
$slots = New-EvidenceSlots $windows $uac $security $display $accessibility $candidate $Stage
$document = New-Document $windows $uac $security $display $accessibility $candidate $legacy $settings $slots $Stage

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $script:RepoRoot ('FACM-4.0-Evidence-' + $stamp) }
else { $OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory) }
$zipPath = Write-EvidenceBundle $document $OutputDirectory -ReplaceExisting

Write-Host ''
Write-Host 'FACM 4.0 real-machine evidence capture: SUCCESS'
Write-Host ('Stage: {0}' -f $Stage)
Write-Host ('OS build: {0}' -f $windows.build)
Write-Host ('Target candidate: {0}' -f $windows.releaseEvidenceTargetCandidate)
Write-Host ('Monitors: {0}; mixed DPI observed: {1}' -f $display.monitorCount,$display.mixedDpiObserved)
Write-Host ('Candidate present: {0}' -f $candidate.present)
Write-Host ('Evidence folder: {0}' -f $OutputDirectory)
Write-Host ('Evidence ZIP: {0}' -f $zipPath)
Write-Host 'Automatic observations are not release PASS decisions; manual_required items still need real interaction evidence.'
