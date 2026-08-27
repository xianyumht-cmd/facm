[CmdletBinding()]
param(
    [string]$CandidatePath = "",
    [ValidateSet("General", "MigrationBaseline", "MigrationAfter", "UpdaterRollback")]
    [string]$Stage = "General",
    [string]$OutputDirectory = "",
    [switch]$SelfTest
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

$script:CollectorVersion = "1.0.0"
$script:SchemaVersion = 1
$script:ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$script:RepoRoot = Split-Path -Parent $script:ScriptRoot
$script:LogLines = New-Object System.Collections.Generic.List[string]

function Add-SafeLog {
    param([string]$Message)
    $safe = Protect-Text $Message
    $script:LogLines.Add(("{0:u} {1}" -f [DateTime]::UtcNow, $safe))
}

function Protect-Text {
    param([AllowNull()][string]$Text)
    if ($null -eq $Text) { return "" }

    $value = [string]$Text
    $value = [regex]::Replace($value, '(?i)\b(Bearer|Basic)\s+[A-Za-z0-9+/_=\-.]+', '$1 <redacted>')
    $value = [regex]::Replace($value, '(?i)\b(token|password|secret|cookie|authorization)\b\s*[:=]\s*[^\s,;]+', '$1=<redacted>')
    $value = [regex]::Replace($value, '(?i)([A-Z]):\\[^\r\n\t"'']+', '<path>')
    $value = [regex]::Replace($value, '\\\\[^\r\n\t"'']+', '<unc-path>')

    if (-not [string]::IsNullOrWhiteSpace($env:USERPROFILE)) {
        $value = $value.Replace($env:USERPROFILE, '<user-profile>')
    }
    if (-not [string]::IsNullOrWhiteSpace($env:USERNAME)) {
        $value = [regex]::Replace($value, [regex]::Escape($env:USERNAME), '<user>')
    }
    return $value
}

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw "SelfTest failed: $Message" }
}

function Get-IsAdministrator {
    try {
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
        $principal = New-Object Security.Principal.WindowsPrincipal($identity)
        return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    }
    catch {
        Add-SafeLog "Administrator probe failed: $($_.Exception.GetType().Name)"
        return $false
    }
}

function Get-RegistryValueSafe {
    param([string]$Path, [string]$Name)
    try {
        if (-not (Test-Path -LiteralPath $Path)) { return $null }
        $item = Get-ItemProperty -LiteralPath $Path -Name $Name -ErrorAction Stop
        return $item.$Name
    }
    catch {
        return $null
    }
}

function Get-WindowsFacts {
    $caption = ""
    $version = [Environment]::OSVersion.Version.ToString()
    $build = [Environment]::OSVersion.Version.Build
    $architecture = $env:PROCESSOR_ARCHITECTURE

    try {
        $os = Get-CimInstance Win32_OperatingSystem -ErrorAction Stop
        if ($null -ne $os) {
            $caption = [string]$os.Caption
            if (-not [string]::IsNullOrWhiteSpace([string]$os.Version)) { $version = [string]$os.Version }
            if (-not [string]::IsNullOrWhiteSpace([string]$os.BuildNumber)) { $build = [int]$os.BuildNumber }
            if (-not [string]::IsNullOrWhiteSpace([string]$os.OSArchitecture)) { $architecture = [string]$os.OSArchitecture }
        }
    }
    catch {
        Add-SafeLog "Win32_OperatingSystem probe unavailable: $($_.Exception.GetType().Name)"
    }

    $currentVersionKey = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
    $displayVersion = Get-RegistryValueSafe $currentVersionKey 'DisplayVersion'
    $productName = Get-RegistryValueSafe $currentVersionKey 'ProductName'
    $ubr = Get-RegistryValueSafe $currentVersionKey 'UBR'

    $target = "other"
    if ($build -eq 17763) {
        $target = "compat.windows-10-1809"
    }
    elseif ($build -eq 19045) {
        $target = "compat.windows-10-22h2"
    }
    elseif ($build -ge 22000 -and ([string]$productName -match 'Windows 11|Windows Server 2025' -or [string]$caption -match 'Windows 11')) {
        $target = "compat.windows-11"
    }

    return [ordered]@{
        caption = Protect-Text $caption
        productName = Protect-Text ([string]$productName)
        displayVersion = [string]$displayVersion
        version = $version
        build = $build
        ubr = $ubr
        architecture = Protect-Text ([string]$architecture)
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
    $defender = [ordered]@{
        available = $false
        antivirusEnabled = $null
        realTimeProtectionEnabled = $null
        behaviorMonitorEnabled = $null
    }

    try {
        $mp = Get-MpComputerStatus -ErrorAction Stop
        if ($null -ne $mp) {
            $defender.available = $true
            $defender.antivirusEnabled = [bool]$mp.AntivirusEnabled
            $defender.realTimeProtectionEnabled = [bool]$mp.RealTimeProtectionEnabled
            $defender.behaviorMonitorEnabled = [bool]$mp.BehaviorMonitorEnabled
        }
    }
    catch {
        Add-SafeLog "Defender status probe unavailable: $($_.Exception.GetType().Name)"
    }

    return [ordered]@{
        defender = $defender
        smartScreenMachine = [string](Get-RegistryValueSafe 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer' 'SmartScreenEnabled')
        smartScreenUser = [string](Get-RegistryValueSafe 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer' 'SmartScreenEnabled')
        note = "Configuration facts only; actual SmartScreen reputation/UI requires manual evidence."
    }
}

function Initialize-DisplayProbe {
    if ('FacmEvidence.DisplayProbe' -as [type]) { return }

    $source = @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace FacmEvidence
{
    public sealed class MonitorSnapshot
    {
        public int Index { get; set; }
        public bool Primary { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public uint DpiX { get; set; }
        public uint DpiY { get; set; }
        public bool DpiAvailable { get; set; }
        public string DeviceName { get; set; }
    }

    public static class DisplayProbe
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
        }

        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT rect, IntPtr data);

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);

        public static MonitorSnapshot[] GetMonitors()
        {
            var result = new List<MonitorSnapshot>();
            int index = 0;
            MonitorEnumProc callback = delegate(IntPtr monitor, IntPtr hdc, ref RECT rect, IntPtr data)
            {
                var info = new MONITORINFOEX();
                info.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
                if (!GetMonitorInfo(monitor, ref info)) return true;

                uint x = 96, y = 96;
                bool dpiAvailable = false;
                try
                {
                    dpiAvailable = GetDpiForMonitor(monitor, 0, out x, out y) == 0;
                }
                catch
                {
                    x = 96;
                    y = 96;
                }

                result.Add(new MonitorSnapshot
                {
                    Index = index++,
                    Primary = (info.dwFlags & 1) != 0,
                    X = info.rcMonitor.Left,
                    Y = info.rcMonitor.Top,
                    Width = info.rcMonitor.Right - info.rcMonitor.Left,
                    Height = info.rcMonitor.Bottom - info.rcMonitor.Top,
                    DpiX = x,
                    DpiY = y,
                    DpiAvailable = dpiAvailable,
                    DeviceName = info.szDevice ?? string.Empty
                });
                return true;
            };
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
            return result.ToArray();
        }
    }
}
'@
    Add-Type -TypeDefinition $source -Language CSharp -ErrorAction Stop
}

function Get-DisplayFacts {
    $items = @()
    try {
        Initialize-DisplayProbe
        $raw = [FacmEvidence.DisplayProbe]::GetMonitors()
        foreach ($monitor in $raw) {
            $scale = $null
            if ($monitor.DpiAvailable -and $monitor.DpiX -gt 0) {
                $scale = [Math]::Round(($monitor.DpiX / 96.0) * 100.0)
            }
            $items += [ordered]@{
                index = $monitor.Index
                primary = $monitor.Primary
                bounds = [ordered]@{ x = $monitor.X; y = $monitor.Y; width = $monitor.Width; height = $monitor.Height }
                dpiX = $monitor.DpiX
                dpiY = $monitor.DpiY
                scalePercent = $scale
                dpiAvailable = $monitor.DpiAvailable
                device = Protect-Text $monitor.DeviceName
            }
        }
    }
    catch {
        Add-SafeLog "Display probe failed: $($_.Exception.GetType().Name)"
    }

    $distinctDpi = @($items | Where-Object { $_.dpiAvailable } | ForEach-Object { $_.dpiX } | Sort-Object -Unique)
    return [ordered]@{
        monitorCount = $items.Count
        distinctDpiCount = $distinctDpi.Count
        hasNegativeCoordinates = [bool](@($items | Where-Object { $_.bounds.x -lt 0 -or $_.bounds.y -lt 0 }).Count -gt 0)
        mixedDpiObserved = [bool]($distinctDpi.Count -gt 1)
        monitors = $items
    }
}

function Get-AccessibilityFacts {
    $highContrast = $null
    try {
        Add-Type -AssemblyName System.Windows.Forms -ErrorAction Stop
        $highContrast = [System.Windows.Forms.SystemInformation]::HighContrast
    }
    catch {
        Add-SafeLog "High Contrast probe unavailable: $($_.Exception.GetType().Name)"
    }

    $textScale = Get-RegistryValueSafe 'HKCU:\Software\Microsoft\Accessibility' 'TextScaleFactor'
    return [ordered]@{
        highContrast = $highContrast
        textScalePercent = $textScale
        note = "Keyboard focus and screen-reader behavior require manual evidence."
    }
}

function Resolve-Candidate {
    param([string]$RequestedPath)

    $candidates = New-Object System.Collections.Generic.List[string]
    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) { $candidates.Add($RequestedPath) }
    $candidates.Add((Join-Path $script:RepoRoot 'FACM.App.exe'))
    $candidates.Add((Join-Path $script:RepoRoot 'FACM.exe'))
    $candidates.Add((Join-Path (Get-Location).Path 'FACM.App.exe'))
    $candidates.Add((Join-Path (Get-Location).Path 'FACM.exe'))

    foreach ($candidate in $candidates) {
        try {
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                return [System.IO.Path]::GetFullPath($candidate)
            }
        }
        catch { }
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
    $versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($Path)
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    $subject = ""
    $thumbprint = ""
    if ($null -ne $signature.SignerCertificate) {
        $subject = Protect-Text ([string]$signature.SignerCertificate.Subject)
        $thumbprint = [string]$signature.SignerCertificate.Thumbprint
    }

    return [ordered]@{
        role = $Role
        present = $true
        fileName = $item.Name
        sizeBytes = $item.Length
        sha256 = $hash.Hash.ToUpperInvariant()
        fileVersion = Protect-Text ([string]$versionInfo.FileVersion)
        productVersion = Protect-Text ([string]$versionInfo.ProductVersion)
        authenticodeStatus = [string]$signature.Status
        signerSubject = $subject
        signerThumbprint = $thumbprint
    }
}

function Get-SettingsMetadata {
    param([AllowNull()][string]$Candidate)

    $roots = New-Object System.Collections.Generic.List[object]
    $roots.Add([pscustomobject]@{ Label = 'collector-directory'; Path = $script:RepoRoot })
    if (-not [string]::IsNullOrWhiteSpace($Candidate)) {
        $candidateDirectory = Split-Path -Parent $Candidate
        if ($candidateDirectory -ne $script:RepoRoot) {
            $roots.Add([pscustomobject]@{ Label = 'candidate-directory'; Path = $candidateDirectory })
        }
    }

    $relativePaths = @(
        'settings.ini',
        'settings.v2.json',
        'runtime\recovery\settings.v2.lkg.json',
        'runtime\recovery\state.json'
    )

    $records = @()
    foreach ($root in $roots) {
        foreach ($relative in $relativePaths) {
            $path = Join-Path $root.Path $relative
            if (Test-Path -LiteralPath $path -PathType Leaf) {
                $file = Get-Item -LiteralPath $path
                $hash = Get-FileHash -LiteralPath $path -Algorithm SHA256
                $records += [ordered]@{
                    root = $root.Label
                    relativePath = $relative.Replace('\', '/')
                    sizeBytes = $file.Length
                    lastWriteTimeUtc = $file.LastWriteTimeUtc.ToString('o')
                    sha256 = $hash.Hash.ToUpperInvariant()
                }
            }
        }
    }
    return $records
}

function New-EvidenceSlots {
    param($Windows, $Uac, $Security, $Display, $Accessibility, $Candidate, [string]$CaptureStage)

    $displayStatus = 'manual_required'
    if ($Display.monitorCount -ge 2) {
        $displayStatus = 'observed_requires_interaction'
    }

    $signatureStatus = 'manual_required'
    if ($Candidate.present -and $Candidate.authenticodeStatus -eq 'Valid') {
        $signatureStatus = 'observed_requires_review'
    }

    return @(
        [ordered]@{ id = 'compat.non-admin-uac-cancel'; captureStatus = 'manual_required'; automaticObservation = [ordered]@{ isAdministrator = $Uac.isAdministrator; enableLua = $Uac.enableLua }; requiredManualAction = 'Run as a standard user, trigger an elevation-required FACM action, cancel UAC, and capture expected non-destructive behavior.' },
        [ordered]@{ id = 'security.defender-smartscreen'; captureStatus = 'manual_required'; automaticObservation = $Security; requiredManualAction = 'Run the final signed candidate through Defender/SmartScreen and record the actual UI/reputation result.' },
        [ordered]@{ id = 'compat.windows-target'; captureStatus = 'observed_requires_review'; automaticObservation = [ordered]@{ targetCandidate = $Windows.releaseEvidenceTargetCandidate; build = $Windows.build; displayVersion = $Windows.displayVersion }; requiredManualAction = 'Confirm this is the intended real-user Windows machine, not a hosted runner/server image.' },
        [ordered]@{ id = 'display.real-mixed-dpi-multimonitor'; captureStatus = $displayStatus; automaticObservation = $Display; requiredManualAction = 'Move Shell/F/Diagnostics across monitors at the required scale set and record clipping, placement and focus behavior.' },
        [ordered]@{ id = 'accessibility.real-machine'; captureStatus = 'manual_required'; automaticObservation = $Accessibility; requiredManualAction = 'Verify keyboard-only focus, High Contrast, text scaling and a basic screen reader.' },
        [ordered]@{ id = 'migration.settings-3.5.15-to-4.0'; captureStatus = 'manual_required'; automaticObservation = [ordered]@{ stage = $CaptureStage }; requiredManualAction = 'Capture MigrationBaseline before upgrade and MigrationAfter after 4.0 launch/relaunch/rollback, then compare the bundles.' },
        [ordered]@{ id = 'update.interrupted-replacement-rollback'; captureStatus = 'manual_required'; automaticObservation = [ordered]@{ stage = $CaptureStage }; requiredManualAction = 'Use a controlled updater interruption/failure exercise and capture old-version preservation plus rollback result.' },
        [ordered]@{ id = 'release.final-signature-package'; captureStatus = $signatureStatus; automaticObservation = $Candidate; requiredManualAction = 'Confirm final candidate identity, production signing chain, package hash and release artifact identity match.' }
    )
}

function New-ReadmeText {
    param($Document, [string]$ZipName)

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add('FACM 4.0 Real-Machine Release Evidence Bundle')
    $lines.Add('================================================')
    $lines.Add('')
    $lines.Add('This collector is READ-ONLY. It does not deploy, update, restart, elevate, delete files, or change production pointers.')
    $lines.Add('Automatic observations are NOT release PASS decisions. Manual release blockers remain manual until reviewed.')
    $lines.Add('')
    $lines.Add("Collector version: $($Document.collectorVersion)")
    $lines.Add("Stage: $($Document.stage)")
    $lines.Add("Collected UTC: $($Document.collectedAtUtc)")
    $lines.Add("OS build: $($Document.machine.windows.build)")
    $lines.Add("Release target candidate: $($Document.machine.windows.releaseEvidenceTargetCandidate)")
    $lines.Add("Monitor count: $($Document.machine.display.monitorCount)")
    $lines.Add("Mixed DPI observed: $($Document.machine.display.mixedDpiObserved)")
    $lines.Add("Candidate present: $($Document.candidate.present)")
    if ($Document.candidate.present) {
        $lines.Add("Candidate file: $($Document.candidate.fileName)")
        $lines.Add("Candidate SHA-256: $($Document.candidate.sha256)")
        $lines.Add("Authenticode: $($Document.candidate.authenticodeStatus)")
    }
    $lines.Add('')
    $lines.Add('Manual release evidence still required:')
    foreach ($slot in $Document.evidenceSlots) {
        if ($slot.captureStatus -ne 'automatic_complete') {
            $lines.Add("- [$($slot.captureStatus)] $($slot.id): $($slot.requiredManualAction)")
        }
    }
    $lines.Add('')
    $lines.Add("Bundle ZIP: $ZipName")
    $lines.Add('Do not mark repository release evidence Passed directly from this bundle without review/import validation.')
    return ($lines -join [Environment]::NewLine)
}

function Invoke-SelfTest {
    $probe = 'Authorization: Bearer abc123 token=secret-value C:\Users\Alice\private\file.txt \\server\share\x'
    $safe = Protect-Text $probe
    Assert-True ($safe -notmatch 'abc123') 'Bearer secret must be removed.'
    Assert-True ($safe -notmatch 'secret-value') 'token value must be removed.'
    Assert-True ($safe -notmatch 'C:\\Users') 'Windows path must be removed.'
    Assert-True ($safe -notmatch '\\\\server') 'UNC path must be removed.'

    $fakeCandidate = [ordered]@{ present = $true; fileName = 'FACM.App.exe'; sha256 = ('A' * 64); authenticodeStatus = 'Valid' }
    $fakeWindows = [ordered]@{ releaseEvidenceTargetCandidate = 'compat.windows-10-22h2'; build = 19045; displayVersion = '22H2' }
    $fakeUac = [ordered]@{ isAdministrator = $false; enableLua = 1 }
    $fakeSecurity = [ordered]@{ defender = [ordered]@{ available = $true }; note = 'manual' }
    $fakeDisplay = [ordered]@{ monitorCount = 2; distinctDpiCount = 2; hasNegativeCoordinates = $true; mixedDpiObserved = $true; monitors = @() }
    $fakeAccessibility = [ordered]@{ highContrast = $false; textScalePercent = 125 }
    $slots = New-EvidenceSlots $fakeWindows $fakeUac $fakeSecurity $fakeDisplay $fakeAccessibility $fakeCandidate 'General'

    $requiredIds = @(
        'compat.non-admin-uac-cancel',
        'security.defender-smartscreen',
        'compat.windows-target',
        'display.real-mixed-dpi-multimonitor',
        'accessibility.real-machine',
        'migration.settings-3.5.15-to-4.0',
        'update.interrupted-replacement-rollback',
        'release.final-signature-package'
    )
    foreach ($id in $requiredIds) {
        Assert-True (@($slots | Where-Object { $_.id -eq $id }).Count -eq 1) "missing evidence slot $id"
    }
    Assert-True (@($slots | Where-Object { $_.captureStatus -eq 'manual_required' }).Count -ge 5) 'manual-required blockers must remain explicit.'

    $temp = Join-Path ([System.IO.Path]::GetTempPath()) ('facm4-evidence-selftest-' + [Guid]::NewGuid().ToString('N'))
    [System.IO.Directory]::CreateDirectory($temp) | Out-Null
    try {
        $doc = [ordered]@{
            schemaVersion = $script:SchemaVersion
            collectorVersion = $script:CollectorVersion
            stage = 'General'
            collectedAtUtc = [DateTime]::UtcNow.ToString('o')
            machine = [ordered]@{ windows = $fakeWindows; uac = $fakeUac; security = $fakeSecurity; display = $fakeDisplay; accessibility = $fakeAccessibility }
            candidate = $fakeCandidate
            settingsFiles = @()
            evidenceSlots = $slots
            privacy = [ordered]@{ containsUserName = $false; containsFullUserProfilePath = $false; containsSecrets = $false }
        }
        $jsonPath = Join-Path $temp 'evidence.json'
        $json = $doc | ConvertTo-Json -Depth 12
        [System.IO.File]::WriteAllText($jsonPath, $json, (New-Object System.Text.UTF8Encoding($false)))
        $parsed = Get-Content -LiteralPath $jsonPath -Raw | ConvertFrom-Json
        Assert-True ($parsed.schemaVersion -eq 1) 'schemaVersion roundtrip'
        Assert-True ($parsed.evidenceSlots.Count -eq 8) 'evidence slot count'
        Assert-True ((Get-Content -LiteralPath $jsonPath -Raw) -notmatch 'secret-value|abc123|C:\\Users\\Alice') 'self-test JSON must be sanitized.'
    }
    finally {
        if ([System.IO.Directory]::Exists($temp)) { [System.IO.Directory]::Delete($temp, $true) }
    }

    Write-Host 'FACM 4.0 real-machine evidence collector self-test: SUCCESS'
}

if ($SelfTest) {
    Invoke-SelfTest
    exit 0
}

Add-SafeLog "Collector start; stage=$Stage"
$candidate = Resolve-Candidate $CandidatePath
$windows = Get-WindowsFacts
$uac = Get-UacFacts
$security = Get-SecurityFacts
$display = Get-DisplayFacts
$accessibility = Get-AccessibilityFacts
$candidateEvidence = Get-FileEvidence $candidate 'facm4-candidate'
$legacyPath = $null
if (-not [string]::IsNullOrWhiteSpace($candidate)) {
    $legacyCandidate = Join-Path (Split-Path -Parent $candidate) 'FACM.exe'
    if ((Test-Path -LiteralPath $legacyCandidate -PathType Leaf) -and $legacyCandidate -ne $candidate) { $legacyPath = $legacyCandidate }
}
$legacyEvidence = Get-FileEvidence $legacyPath 'facm-3.5.15-legacy'
$settingsFiles = Get-SettingsMetadata $candidate
$slots = New-EvidenceSlots $windows $uac $security $display $accessibility $candidateEvidence $Stage

$document = [ordered]@{
    schemaVersion = $script:SchemaVersion
    collectorVersion = $script:CollectorVersion
    stage = $Stage
    collectedAtUtc = [DateTime]::UtcNow.ToString('o')
    machine = [ordered]@{
        windows = $windows
        uac = $uac
        security = $security
        display = $display
        accessibility = $accessibility
    }
    candidate = $candidateEvidence
    legacy = $legacyEvidence
    settingsFiles = $settingsFiles
    evidenceSlots = $slots
    privacy = [ordered]@{
        containsComputerName = $false
        containsUserName = $false
        containsFullUserProfilePath = $false
        containsApplicationSecrets = $false
        pathPolicy = 'Only stable labels, relative paths and executable file names are stored.'
    }
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $script:RepoRoot ("FACM-4.0-Evidence-$stamp")
}
else {
    $OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
}
[System.IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null

$jsonPath = Join-Path $OutputDirectory 'evidence.json'
$readmePath = Join-Path $OutputDirectory 'README.txt'
$logPath = Join-Path $OutputDirectory 'collector.log'
$zipPath = "$OutputDirectory.zip"

$json = $document | ConvertTo-Json -Depth 16
[System.IO.File]::WriteAllText($jsonPath, $json, (New-Object System.Text.UTF8Encoding($false)))
$readme = New-ReadmeText $document ([System.IO.Path]::GetFileName($zipPath))
[System.IO.File]::WriteAllText($readmePath, $readme, (New-Object System.Text.UTF8Encoding($false)))
Add-SafeLog 'Evidence document and manual checklist written.'
[System.IO.File]::WriteAllLines($logPath, $script:LogLines.ToArray(), (New-Object System.Text.UTF8Encoding($false)))

if (Test-Path -LiteralPath $zipPath) { [System.IO.File]::Delete($zipPath) }
Compress-Archive -LiteralPath (Join-Path $OutputDirectory '*') -DestinationPath $zipPath -CompressionLevel Optimal

Write-Host ''
Write-Host 'FACM 4.0 real-machine evidence capture: SUCCESS'
Write-Host ("Stage: {0}" -f $Stage)
Write-Host ("OS build: {0}" -f $windows.build)
Write-Host ("Target candidate: {0}" -f $windows.releaseEvidenceTargetCandidate)
Write-Host ("Monitors: {0}; mixed DPI observed: {1}" -f $display.monitorCount, $display.mixedDpiObserved)
Write-Host ("Candidate present: {0}" -f $candidateEvidence.present)
Write-Host ("Evidence folder: {0}" -f $OutputDirectory)
Write-Host ("Evidence ZIP: {0}" -f $zipPath)
Write-Host 'Automatic observations are not release PASS decisions; manual_required items still need real interaction evidence.'
