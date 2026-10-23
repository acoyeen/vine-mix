#requires -Version 7.3
[CmdletBinding()]
param([string]$ResultDirectory = '')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'assert-desktop-data-context.ps1')
. (Join-Path $PSScriptRoot 'local-activation-support.ps1')
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $ResultDirectory) { $ResultDirectory = Join-Path $env:LOCALAPPDATA ('VineMix.BuildCache\activation-verification-' + [guid]::NewGuid().ToString('N')) }
$root = [IO.Path]::GetFullPath($ResultDirectory)
Assert-ActivationEvidenceLocation $root @($project)
if (Test-Path -LiteralPath $root) { throw 'Choose a new verification directory.' }
$null = New-Item -ItemType Directory -Path $root
# Newly created files can be redirected even when existing settings are not.
# These are disposable fixtures: use their actual physical location explicitly
# in both parent and shell child, without relaxing the activation settings guard.
Initialize-DesktopDataContext
$locator = Join-Path $root 'fixture-location.txt'
'Isolated activation test' | Set-Content -LiteralPath $locator -Encoding utf8
$root = Split-Path -Parent (Get-ActivationPhysicalPath $locator)
Assert-ActivationEvidenceLocation $root @($project)
$checks = [Collections.Generic.List[string]]::new()
function Check([bool]$condition, [string]$description) {
    if (-not $condition) { throw $description }
    $checks.Add($description)
}
function Reject([scriptblock]$action, [string]$description) {
    $rejected = $false
    try { & $action } catch { $rejected = $true }
    Check $rejected $description
}
$previousLocal = $env:LOCALAPPDATA
$passed = $false
try {
    $local = Join-Path $root 'example-local-data'
    $ordinary = Join-Path $local 'VineMix'
    $redirected = Join-Path $local 'Packages\Example.App\LocalCache\Local\AudioMixer'
    $redirectedNew = Join-Path $local 'Packages\Example.App\LocalCache\Local\VineMix'
    foreach ($path in @($ordinary,$redirected,$redirectedNew)) { $null = New-Item -ItemType Directory -Path $path -Force }
    $settings = Join-Path $ordinary 'settings.json'
    '{"revision":71}' | Set-Content -LiteralPath $settings -Encoding utf8
    '{"revision":13}' | Set-Content -LiteralPath (Join-Path $redirected 'settings.json') -Encoding utf8
    '{"revision":17}' | Set-Content -LiteralPath (Join-Path $redirectedNew 'settings.json') -Encoding utf8
    '{}' | Set-Content -LiteralPath (Join-Path $ordinary 'ui.json') -Encoding utf8
    Assert-DesktopDataContext $settings
    $physical = Get-ActivationPhysicalPath $settings
    Check ($physical -ieq $settings) 'Fixture workspace identity is resolved through an actual open file handle.'
    Check ([VineMix.DesktopDataContext]::MatchesWorkspaceIdentity('VineMix · Patch workspace','HwndWrapper[VineMix;;example]',$true,123,123)) 'Readiness recognizes a visible WPF workspace owned by the selected process.'
    Check (-not [VineMix.DesktopDataContext]::MatchesWorkspaceIdentity('VineMix · Could not start','#32770',$true,123,123)) 'A responsive startup error dialog is not a ready workspace.'
    Check (-not [VineMix.DesktopDataContext]::MatchesWorkspaceIdentity('VineMix · Patch workspace','HwndWrapper[VineMix;;example]',$false,123,123) -and
        -not [VineMix.DesktopDataContext]::MatchesWorkspaceIdentity('VineMix · Patch workspace','HwndWrapper[VineMix;;example]',$true,124,123)) 'Hidden windows and another process cannot satisfy workspace readiness.'
    $captured = [pscustomobject]@{Id=123;StartTime=[DateTime]::UtcNow;Path=(Join-Path $root 'Example.exe');SessionId=2}
    Check (Test-ActivationProcessIdentity $captured $captured) 'Rollback identity accepts the captured exact process token.'
    $replacement = [pscustomobject]@{Id=123;StartTime=$captured.StartTime.AddSeconds(1);Path=$captured.Path;SessionId=2}
    Check (-not (Test-ActivationProcessIdentity $replacement $captured)) 'A reused PID with a different start time cannot be stopped by rollback.'
    Reject { Assert-ActivationEvidenceLocation (Join-Path $project 'artifacts\private-backup') @($project) } 'Evidence cannot be saved inside the source checkout.'
    Reject { Assert-ActivationEvidenceLocation $root @($root) } 'Evidence cannot be the application package itself.'
    Assert-ActivationEvidenceLocation ($root + '-sibling') @($root)
    Check $true 'Path containment uses directory boundaries rather than string-prefix siblings.'
    $snapshot = [pscustomobject]@{engineProtocolVersion=1;enginePid=123;engineVersion='0.3.0-beta.3';executablePath=(Join-Path $root 'Example.Engine.exe');settingsPath=$settings;physicalSettingsPath=('\\?\'+$settings);settingsState='loaded'}
    Assert-ActivationSnapshotIdentity $snapshot 123 $snapshot.executablePath $settings $physical '0.3.0-beta.3'
    Check $true 'Snapshot identity accepts matching exact process, release and normalized physical file identity.'
    foreach ($field in @('engineProtocolVersion','enginePid','engineVersion','executablePath','settingsPath','physicalSettingsPath','settingsState')) {
        $bad = $snapshot | ConvertTo-Json | ConvertFrom-Json
        $bad.$field = if ($field -in @('engineProtocolVersion','enginePid')) { 999 } elseif ($field -like '*Path') { Join-Path $root 'other.json' } else { 'different' }
        Reject { Assert-ActivationSnapshotIdentity $bad 123 $snapshot.executablePath $settings $physical '0.3.0-beta.3' } ('Snapshot rejects a mismatched ' + $field + ' before shutdown or copy.')
    }
    $env:LOCALAPPDATA = $local
    $records = @(Backup-ActivationDataViews $ordinary (Join-Path $root 'preserved'))
    $files = @($records | Where-Object kind -eq 'file')
    Check ($files.Count -eq 4 -and @($files | Where-Object { $_.source.StartsWith((Join-Path $local 'Packages'), [StringComparison]::OrdinalIgnoreCase) }).Count -eq 2) 'Ordinary and both legacy/current redirected fixture views are preserved independently.'
    Assert-ActivationDataStable $records
    Check (@($files | Where-Object { (Get-FileHash -LiteralPath $_.backup).Hash -cne $_.sha256 }).Count -eq 0) 'Every preserved file retains its SHA-256 digest.'
    '{}' | Set-Content -LiteralPath (Join-Path $ordinary 'settings.json.new') -Encoding utf8
    Reject { Assert-ActivationDataStable $records } 'New settings files appearing after preservation fail the directory-manifest stability check.'
    Remove-Item -LiteralPath (Join-Path $ordinary 'settings.json.new')
    '{"revision":72}' | Set-Content -LiteralPath $settings -Encoding utf8
    Reject { Assert-ActivationDataStable $records } 'A newer workspace causes a safe stop instead of restoring an older snapshot.'
    Check ((Get-Content -LiteralPath $settings -Raw).Contains('72')) 'Failed stability verification leaves the newer workspace intact.'
    $env:LOCALAPPDATA = $previousLocal
    $sourceBinary = Join-Path $root 'example-candidate.bin'
    $targetBinary = Join-Path $root 'example-installed.bin'
    'candidate' | Set-Content -LiteralPath $sourceBinary -Encoding utf8
    'original' | Set-Content -LiteralPath $targetBinary -Encoding utf8
    $sourceHash = (Get-FileHash -LiteralPath $sourceBinary).Hash
    $originalHash = (Get-FileHash -LiteralPath $targetBinary).Hash
    Reject { Replace-ActivationBinary $sourceBinary $targetBinary ('0' * 64) } 'A staged binary with the wrong digest never replaces the installed fixture.'
    Check ((Get-FileHash -LiteralPath $targetBinary).Hash -ceq $originalHash) 'Hash rejection leaves the exact original binary intact.'
    $locked = [IO.File]::Open($targetBinary,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
    try { Reject { Replace-ActivationBinary $sourceBinary $targetBinary $sourceHash } 'A locked destination rejects replacement before damaging the original binary.' }
    finally { $locked.Dispose() }
    Check ((Get-FileHash -LiteralPath $targetBinary).Hash -ceq $originalHash) 'Failed replacement preserves the original destination bytes.'
    Replace-ActivationBinary $sourceBinary $targetBinary $sourceHash
    Check ((Get-FileHash -LiteralPath $targetBinary).Hash -ceq $sourceHash -and @(Get-ChildItem -LiteralPath $root -Filter '.activation-*.new').Count -eq 0) 'Verified atomic replacement installs the expected bytes and cleans only its own staging file.'

    $executable = (Get-Process -Id $PID).Path
    $shortcutPath = Join-Path $root 'Example shortcut.lnk'
    $shell = New-Object -ComObject WScript.Shell
    $link = $null
    try {
        $link = $shell.CreateShortcut($shortcutPath); $link.TargetPath = $executable; $link.Arguments = ''; $link.Save()
        Assert-ActivationShortcut $shortcutPath $executable
        Check $true 'An existing shortcut is checked against the exact executable without opening it.'
        Reject { Assert-ActivationShortcut $shortcutPath (Join-Path $root 'different.exe') } 'A shortcut targeting another application is rejected.'
        $link.Arguments = '--tray'; $link.Save()
        Reject { Assert-ActivationShortcut $shortcutPath $executable } 'Background or alternate-workspace shortcut arguments are rejected.'
    } finally {
        if ($link) { $null = [Runtime.InteropServices.Marshal]::FinalReleaseComObject($link) }
        $null = [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
    }

    Initialize-ActivationShellLauncher
    $probeScript = Join-Path $root 'inert shell probe.ps1'
    $probeOutput = Join-Path $root 'inert-result.json'
    @'
param([string]$SupportDirectory,[string]$SettingsFile,[string]$OutputFile)
$ErrorActionPreference='Stop'
. (Join-Path $SupportDirectory 'assert-desktop-data-context.ps1')
. (Join-Path $SupportDirectory 'local-activation-support.ps1')
Assert-DesktopDataContext $SettingsFile
$self = Get-CimInstance Win32_Process -Filter ('ProcessId = ' + $PID)
$parent = Get-Process -Id $self.ParentProcessId
[ordered]@{pid=$PID;parentName=$parent.ProcessName;parentSession=$parent.SessionId;session=(Get-Process -Id $PID).SessionId;physicalPath=(Get-ActivationPhysicalPath $SettingsFile);shellParentVerified=(Test-ActivationShellParent)} | ConvertTo-Json | Set-Content -LiteralPath $OutputFile -Encoding utf8
'@ | Set-Content -LiteralPath $probeScript -Encoding utf8
    [VineMix.Desktop.ActivationShellLauncher]::Start($executable,
        [string[]]@('-NoProfile','-NonInteractive','-File',$probeScript,'-SupportDirectory',$PSScriptRoot,'-SettingsFile',$settings,'-OutputFile',$probeOutput), $root)
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while (-not (Test-Path -LiteralPath $probeOutput) -and $timer.ElapsedMilliseconds -lt 15000) { Start-Sleep -Milliseconds 100 }
    if (-not (Test-Path -LiteralPath $probeOutput)) { throw 'Inert trusted-shell probe did not produce a result.' }
    $probe = Get-Content -LiteralPath $probeOutput -Raw | ConvertFrom-Json
    Check ($probe.parentName -ieq 'explorer' -and $probe.parentSession -eq $probe.session) 'The production launcher starts an inert child with the same-session Windows shell as its actual parent.'
    Check $probe.shellParentVerified 'Installed activation verifies the active Windows shell is its own parent before backing up any data view.'
    Check ($probe.physicalPath -ieq $physical) 'The inert shell child resolves the same physical fixture file through an open handle.'
    Check ((Get-Content -LiteralPath $settings -Raw).Contains('72')) 'The shell-launch probe does not alter fixture settings.'
    $passed = $true
} catch { $failure = $_.Exception.ToString() }
finally {
    $env:LOCALAPPDATA = $previousLocal
    [ordered]@{passed=$passed;checks=$checks;error=$failure;scope='Temporary fixture files, synthetic identity checks and one hidden inert PowerShell child through the production shell launcher. No real VineMix processes stopped or launched, no real settings modified, no audio endpoints opened, no desktop interaction.'} |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $root 'results.json') -Encoding utf8
}
[pscustomobject]@{passed=$passed;checks=$checks.Count;error=$failure;reportDirectory=$root} | ConvertTo-Json -Depth 3
if (-not $passed) { exit 1 }
