#requires -Version 7.3
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$InstallerPath,
    [Parameter(Mandatory)][string]$PackageDirectory,
    [string]$ResultDirectory = '',
    [Alias('SkipDetachedUi')][switch]$SkipReleaseSmoke
)

$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$installer = [IO.Path]::GetFullPath($InstallerPath)
$package = [IO.Path]::GetFullPath($PackageDirectory)
$release = Get-Content -LiteralPath (Join-Path $project 'release.json') -Raw | ConvertFrom-Json
if (-not $ResultDirectory) {
    $ResultDirectory = Join-Path $env:LOCALAPPDATA ('VineMix.BuildCache\installer-verification-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
$evidence = [IO.Path]::GetFullPath($ResultDirectory)
$target = [IO.Path]::GetFullPath((Join-Path $evidence 'installed-app'))
$personalRoots = [ordered]@{VineMix=(Join-Path $env:LOCALAPPDATA 'VineMix');AudioMixer=(Join-Path $env:LOCALAPPDATA 'AudioMixer')}
$checks = [Collections.Generic.List[object]]::new()
$operations = [Collections.Generic.List[object]]::new()
$failure = $null
$before = $null
$after = $null
$started = [DateTimeOffset]::Now.ToString('o')
if (-not ('VineMix.VerificationPhysicalPath' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
namespace VineMix {
    public static class VerificationPhysicalPath {
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
        private static extern uint GetFinalPathNameByHandleW(SafeFileHandle file, StringBuilder path, uint capacity, uint flags);
        public static string Read(SafeFileHandle file) {
            var path = new StringBuilder(32768);
            uint count = GetFinalPathNameByHandleW(file, path, (uint)path.Capacity, 0);
            if (count == 0 || count >= path.Capacity) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            return path.ToString();
        }
    }
}
'@
}

function Write-Evidence([string]$Name, $Value) {
    $Value | ConvertTo-Json -Depth 32 | Set-Content -LiteralPath (Join-Path $evidence $Name) -Encoding utf8
}
function Require([bool]$Passed, [string]$Description) {
    $checks.Add([ordered]@{ passed = $Passed; description = $Description })
    if (-not $Passed) { throw $Description }
}
function File-Hash([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}
function Is-Within([string]$Path, [string]$Root) {
    $resolvedPath = [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    $resolvedRoot = [IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
    $resolvedPath.StartsWith($resolvedRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
}
function Get-PersonalFiles {
    $files = [ordered]@{}
    foreach ($brand in $personalRoots.Keys) {
        $personalData = $personalRoots[$brand]
        if (-not (Test-Path -LiteralPath $personalData -PathType Container)) { continue }
        # Logs change naturally while audio runs. Hash saved state, not telemetry.
        foreach ($file in @(Get-ChildItem -LiteralPath $personalData -File | Where-Object { $_.Name -match '\.json(?:\..+)?$' } | Sort-Object Name)) {
            $stream = [IO.File]::Open($file.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
            try {
                $physicalPath = [VineMix.VerificationPhysicalPath]::Read($stream.SafeFileHandle)
                $files[$brand + '/' + $file.Name] = [ordered]@{hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream));length=$stream.Length;physicalPath=$physicalPath;logicalPath=$file.FullName}
            } finally { $stream.Dispose() }
        }
    }
    $files
}
function Get-StartupValues {
    $values = [ordered]@{}
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run', $false)
    if ($key) {
        try {
            foreach ($name in @($key.GetValueNames() | Sort-Object)) {
                $values[$name] = [ordered]@{
                    kind = $key.GetValueKind($name).ToString()
                    value = $key.GetValue($name, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
                }
            }
        } finally { $key.Dispose() }
    }
    $values
}
function Get-ShortcutFiles {
    $files = [ordered]@{}
    foreach ($folder in @([Environment]::GetFolderPath('DesktopDirectory'), [Environment]::GetFolderPath('Programs'))) {
        if (-not $folder -or -not (Test-Path -LiteralPath $folder -PathType Container)) { continue }
        foreach ($file in @(Get-ChildItem -LiteralPath $folder -Filter '*Vine*Mix*' -File -Recurse -ErrorAction SilentlyContinue | Sort-Object FullName)) {
            $files[$file.FullName] = File-Hash $file.FullName
        }
    }
    $files
}
function Get-LiveProcesses {
    @(Get-CimInstance Win32_Process -Filter "Name='VineMix.exe' OR Name='VineMix.Engine.exe' OR Name='AudioMixer.exe' OR Name='AudioMixer.Engine.exe'" |
        Sort-Object ProcessId | ForEach-Object {
            [ordered]@{
                id = $_.ProcessId
                created = $_.CreationDate.ToUniversalTime().ToString('o')
                path = $_.ExecutablePath
                hash = if ($_.ExecutablePath -and (Test-Path -LiteralPath $_.ExecutablePath -PathType Leaf)) { File-Hash $_.ExecutablePath } else { $null }
            }
        })
}
function Get-ProtectedState {
    [ordered]@{
        personalFiles = Get-PersonalFiles
        processes = @(Get-LiveProcesses)
        startup = Get-StartupValues
        shortcuts = Get-ShortcutFiles
    }
}
function To-CanonicalJson($Value) { ConvertTo-Json -InputObject $Value -Depth 32 -Compress }
function Get-UserUninstallEntries {
    $entries = [ordered]@{}
    $registryPaths = @('Software\Microsoft\Windows\CurrentVersion\Uninstall', 'Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall')
    foreach ($registryPath in $registryPaths) {
        $baseKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($registryPath, $false)
        if (-not $baseKey) { continue }
        try {
            foreach ($name in @($baseKey.GetSubKeyNames() | Sort-Object)) {
                $key = $baseKey.OpenSubKey($name, $false)
                if (-not $key) { continue }
                try {
                    if ($key.GetValue('DisplayName', '') -notmatch 'Vine\s*Mix') { continue }
                    $values = [ordered]@{}
                    foreach ($valueName in @($key.GetValueNames() | Sort-Object)) {
                        $values[$valueName] = $key.GetValue($valueName, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
                    }
                    $entries[$registryPath + '\' + $name] = $values
                } finally { $key.Dispose() }
            }
        } finally { $baseKey.Dispose() }
    }
    $entries
}
function Quote-Argument([string]$Value) {
    if ($Value.Contains('"') -or $Value.Contains("`r") -or $Value.Contains("`n")) { throw 'Unexpected quote or newline in an installer argument.' }
    '"' + $Value + '"'
}
function Invoke-Setup([string]$Name, [bool]$AcceptTerms) {
    $log = Join-Path $evidence ($Name + '.log')
    $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/TESTMODE=1', '/DRIVERS=none', '/NOICONS', '/TASKS=""',
        ('/DIR=' + (Quote-Argument $target)), ('/LOG=' + (Quote-Argument $log)))
    if ($AcceptTerms) { $arguments += '/ACCEPTTERMS=1' }
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $process = Start-Process -FilePath $installer -ArgumentList ($arguments -join ' ') -WindowStyle Hidden -PassThru
    try {
        # A hung setup is retained for diagnosis; never kill a parent or live mixer.
        if (-not $process.WaitForExit(180000)) { throw "Installer exceeded its deadline: $Name (PID $($process.Id))." }
        $code = $process.ExitCode
    } finally { $process.Dispose() }
    $operations.Add([ordered]@{ name = $Name; exitCode = $code; seconds = [Math]::Round($timer.Elapsed.TotalSeconds, 3); log = $log })
    $code
}
function Require-LogMarker([string]$Name, [string]$Marker, [string]$Description) {
    $log = Join-Path $evidence ($Name + '.log')
    Require (Test-Path -LiteralPath $log -PathType Leaf) "Setup created its diagnostic log: $Name."
    Require ((Get-Content -LiteralPath $log -Raw).Contains($Marker)) $Description
}
function Require-DriverPagePolicy([string]$Name) {
    $logText = Get-Content -LiteralPath (Join-Path $evidence ($Name + '.log')) -Raw
    Require ($logText.Contains('DRIVER_CHOICES_OPT_IN: VB=0 VAC=0 terms=0;')) 'Both optional drivers and their consent begin unselected, including during repair.'
    $layout = [regex]::Match($logText, 'DRIVER_PAGE_LAYOUT: surface=(\d+)x(\d+); options bottom=(\d+); status top=(\d+) height=(\d+); refresh top=(\d+) bottom=(\d+); terms surface=(\d+)x(\d+) bottom=(\d+)')
    Require $layout.Success 'Setup reports its actual hidden wizard surface and measured text geometry.'
    if (-not $layout.Success) { return }
    $size = @(1..10 | ForEach-Object { [int]$layout.Groups[$_].Value })
    Require ($size[0] -gt 0 -and $size[1] -gt 0 -and $size[2] -lt $size[3] -and $size[4] -gt 0 -and
        $size[3] + $size[4] -lt $size[5] -and $size[5] -lt $size[6] -and $size[6] -le $size[1]) 'Measured optional-feature text, detection status and refresh action fit without overlapping on the hidden wizard surface.'
    Require ($size[7] -gt 0 -and $size[8] -gt 0 -and $size[9] -le $size[8]) 'The separately measured driver notice, terms actions and acceptance control fit on their hidden wizard surface.'
}
function Invoke-Uninstall([string]$Name, [string]$UninstallerPath) {
    Require (Is-Within $UninstallerPath $target) 'The selected uninstaller resolves inside the verified test destination.'
    $log = Join-Path $evidence ($Name + '.log')
    $arguments = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /TESTMODE=1 /LOG=' + (Quote-Argument $log)
    $process = Start-Process -FilePath $UninstallerPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
    try {
        if (-not $process.WaitForExit(180000)) { throw "Uninstall exceeded its deadline: $Name (PID $($process.Id))." }
        $code = $process.ExitCode
    } finally { $process.Dispose() }
    $operations.Add([ordered]@{ name = $Name; exitCode = $code; log = $log })
    $code
}

if (-not (Test-Path -LiteralPath $installer -PathType Leaf)) { throw "Installer not found: $installer" }
if (-not (Test-Path -LiteralPath (Join-Path $package 'VineMix.exe') -PathType Leaf)) { throw "Staged desktop not found in: $package" }
if (-not (Test-Path -LiteralPath (Join-Path $package 'VineMix.Engine.exe') -PathType Leaf)) { throw "Staged engine not found in: $package" }
if (Test-Path -LiteralPath $evidence) { throw 'ResultDirectory must be new; prior evidence and installations are never overwritten.' }
if (-not (Is-Within $target $evidence)) { throw 'The test installation must remain inside the new evidence directory.' }
if ((Is-Within $package $evidence) -or (Is-Within $installer $evidence)) { throw 'Evidence cannot contain either input package.' }
$null = New-Item -ItemType Directory -Path $evidence

try {
    $before = Get-ProtectedState
    Write-Evidence 'protected-before.json' $before
    $registryBefore = Get-UserUninstallEntries
    Write-Evidence 'uninstall-registry-before.json' $registryBefore
    $testRegistryKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\119d9882-c04b-4825-b61a-d50798c13ed8_is1'
    $existingTestKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($testRegistryKey, $false)
    try { Require ($null -eq $existingTestKey) 'No previous installer verification uses the dedicated test identity.' }
    finally { if ($existingTestKey) { $existingTestKey.Dispose() } }
    Require (@($before.processes | Where-Object { $_.path -and (Is-Within $_.path $target) }).Count -eq 0) 'No running mixer is inside the test destination.'

    $initialSavedState = To-CanonicalJson $before.personalFiles
    Start-Sleep -Milliseconds 1500
    Require ((To-CanonicalJson (Get-PersonalFiles)) -ceq $initialSavedState) 'Personal saved files were stable before verification began.'
    $backupDirectory = Join-Path $evidence 'personal-state-backup'
    $null = New-Item -ItemType Directory -Path $backupDirectory
    foreach ($name in $before.personalFiles.Keys) {
        $backup = Join-Path $backupDirectory $name
        $null = New-Item -ItemType Directory -Force -Path (Split-Path $backup -Parent)
        Copy-Item -LiteralPath $before.personalFiles[$name].logicalPath -Destination $backup
        Require ((File-Hash $backup) -ceq $before.personalFiles[$name].hash) "Verified read-only backup: $name."
    }

    $declinedCode = Invoke-Setup 'terms-required' $false
    Require ($declinedCode -ne 0) 'Silent installation refuses to continue without explicit terms acceptance.'
    Require-LogMarker 'terms-required' 'TERMS_NOT_ACCEPTED:' 'The rejection specifically identifies missing terms acceptance.'
    Require (-not (Test-Path -LiteralPath $target)) 'Declining terms did not create the app destination.'
    Require ((To-CanonicalJson (Get-UserUninstallEntries)) -ceq (To-CanonicalJson $registryBefore)) 'Declining terms did not register an installation.'

    # Test the real process guard without launching either desktop or engine.
    # This tiny process owns no application resources and exits via our event.
    $compilerDirectory = Join-Path $project '.tools\llvm-mingw-20261006-ucrt-x86_64\bin'
    $compiler = Join-Path $compilerDirectory 'clang.exe'
    Require (Test-Path -LiteralPath $compiler -PathType Leaf) 'The local C compiler is available for the isolated running-process fixture.'
    $fixtureBuild = Join-Path $evidence 'RunningAppFixture.exe'
    $previousPath = $env:PATH
    try {
        $env:PATH = $compilerDirectory + ';' + $previousPath
        & $compiler (Join-Path $project 'installer\tests\RunningAppFixture.c') -O2 -municode -o $fixtureBuild 2>&1 |
            Set-Content -LiteralPath (Join-Path $evidence 'running-fixture-build.log') -Encoding utf8
        Require ($LASTEXITCODE -eq 0) 'The no-audio process fixture compiled successfully.'
    } finally { $env:PATH = $previousPath }
    $null = New-Item -ItemType Directory -Path $target
    $fixtureTarget = Join-Path $target 'VineMix.exe'
    Copy-Item -LiteralPath $fixtureBuild -Destination $fixtureTarget
    $fixtureHash = File-Hash $fixtureTarget
    $eventName = 'Local\VineMix.InstallerVerification.' + [Guid]::NewGuid().ToString('N')
    $fixtureEvent = [Threading.EventWaitHandle]::new($false, [Threading.EventResetMode]::ManualReset, $eventName)
    $fixtureProcess = $null
    try {
        $fixtureProcess = Start-Process -FilePath $fixtureTarget -ArgumentList (Quote-Argument $eventName) -WindowStyle Hidden -PassThru
        Start-Sleep -Milliseconds 250
        Require (-not $fixtureProcess.HasExited) 'The isolated no-audio process is running inside the test destination.'
        $inUseCode = Invoke-Setup 'destination-in-use' $true
        Require ($inUseCode -ne 0) 'Setup refuses to replace files beneath a running application.'
        Require-LogMarker 'destination-in-use' 'TARGET_RUNNING:' 'The rejection specifically identifies a process inside the destination.'
        Require ((File-Hash $fixtureTarget) -ceq $fixtureHash) 'Setup did not alter the running fixture executable.'
        Require (-not (Test-Path -LiteralPath (Join-Path $target 'VineMix.Engine.exe'))) 'A blocked replacement did not copy an engine.'
        Require ((To-CanonicalJson (Get-UserUninstallEntries)) -ceq (To-CanonicalJson $registryBefore)) 'A blocked replacement did not register an installation.'
    } finally {
        $null = $fixtureEvent.Set()
        if ($fixtureProcess) {
            if (-not $fixtureProcess.WaitForExit(5000)) { throw "The isolated fixture did not exit after its event was signalled (PID $($fixtureProcess.Id))." }
            $fixtureProcess.Dispose()
        }
        $fixtureEvent.Dispose()
    }
    # Delete only the exact file we created after verifying its absolute target
    # and content. No directory is recursively removed by this verifier.
    Require ((Is-Within $fixtureTarget $evidence) -and ((File-Hash $fixtureTarget) -ceq $fixtureHash)) 'The completed fixture is still the known file inside this test.'
    Remove-Item -LiteralPath $fixtureTarget

    $installCode = Invoke-Setup 'first-install' $true
    Require ($installCode -eq 0) 'Isolated first installation completed without requesting a reboot.'
    Require-LogMarker 'first-install' 'DRIVER_INSTALLATION_SKIPPED' 'First installation explicitly skipped both vendor installers.'
    Require-DriverPagePolicy 'first-install'
    $desktopMatches = @(Get-ChildItem -LiteralPath $target -Filter 'VineMix.exe' -File -Recurse)
    Require ($desktopMatches.Count -eq 1) 'The installation contains exactly one desktop payload.'
    $payloadDirectory = $desktopMatches[0].Directory.FullName
    Require ($payloadDirectory.TrimEnd('\', '/') -ieq $target.TrimEnd('\', '/')) 'The desktop and engine use the documented stable installation folder.'
    $sourceFiles = @(Get-ChildItem -LiteralPath $package -File -Recurse)
    $payloadManifest = [Collections.Generic.List[object]]::new()
    foreach ($source in $sourceFiles) {
        $relative = [IO.Path]::GetRelativePath($package, $source.FullName)
        $destination = Join-Path $payloadDirectory $relative
        Require (Test-Path -LiteralPath $destination -PathType Leaf) "Installed package includes: $relative."
        $expectedHash = File-Hash $source.FullName
        $actualHash = File-Hash $destination
        Require ($actualHash -ceq $expectedHash) "Installed package matches staged bytes: $relative."
        $payloadManifest.Add([ordered]@{ path = $relative; length = $source.Length; hash = $actualHash })
    }
    Write-Evidence 'installed-payload-manifest.json' $payloadManifest
    foreach ($name in @('VineMix.exe','VineMix.Engine.exe')) {
        & (Join-Path $PSScriptRoot 'assert-release-file-version.ps1') -Path (Join-Path $payloadDirectory $name) -Version $release.version -NumericVersion $release.numericVersion
        Require $true "Installed $name has matching numeric FileVersion text and fixed fields, with the expected product version."
    }
    $uninstallers = @(Get-ChildItem -LiteralPath $target -Filter 'unins*.exe' -File)
    Require ($uninstallers.Count -eq 1) 'A single uninstaller was created inside the isolated destination.'
    $registryInstalled = Get-UserUninstallEntries
    Write-Evidence 'uninstall-registry-installed.json' $registryInstalled
    $newKeys = @($registryInstalled.Keys | Where-Object { -not $registryBefore.Contains($_) })
    Require ($newKeys.Count -eq 1) 'The test installation uses one new uninstall registration.'
    Require ($newKeys[0] -ieq $testRegistryKey) 'The test installation uses the dedicated verification identity.'
    $registeredLocation = [string]$registryInstalled[$newKeys[0]]['InstallLocation']
    Require ($registeredLocation.TrimEnd('\', '/') -ieq $target.TrimEnd('\', '/')) 'Uninstall registration points only at the isolated test destination.'
    foreach ($key in $registryBefore.Keys) {
        Require ((To-CanonicalJson $registryBefore[$key]) -ceq (To-CanonicalJson $registryInstalled[$key])) "Existing install registration was preserved: $key."
    }

    # Unknown local files must survive both an upgrade and ordinary uninstall.
    # This sentinel is entirely inside the task's isolated destination.
    $sentinel = Join-Path $target 'preserve-user-file.txt'
    [IO.File]::WriteAllText($sentinel, 'Installer verification sentinel: ' + [Guid]::NewGuid().ToString('D'))
    $sentinelHash = File-Hash $sentinel
    $reinstallCode = Invoke-Setup 'same-version-reinstall' $true
    Require ($reinstallCode -eq 0) 'Reinstall completed without requesting a reboot.'
    Require-LogMarker 'same-version-reinstall' 'DRIVER_INSTALLATION_SKIPPED' 'Reinstall explicitly skipped both vendor installers.'
    Require-DriverPagePolicy 'same-version-reinstall'
    Require ((File-Hash $sentinel) -ceq $sentinelHash) 'Reinstall preserved an unknown user file.'
    foreach ($file in $payloadManifest) {
        Require ((File-Hash (Join-Path $payloadDirectory $file.path)) -ceq $file.hash) "Reinstall retained complete package bytes: $($file.path)."
    }

    # A future-version marker is an isolated fixture for the actual setup guard.
    # Restore the exact installed bytes before any later check or uninstall.
    $installationRecord = Join-Path $target 'installation.ini'
    Require (Test-Path -LiteralPath $installationRecord -PathType Leaf) 'Setup records its installed application version.'
    $installationBytes = [IO.File]::ReadAllBytes($installationRecord)
    [IO.File]::WriteAllBytes((Join-Path $evidence 'installation-before-downgrade.ini'), $installationBytes)
    try {
        $recordText = [IO.File]::ReadAllText($installationRecord)
        $futureRecord = [regex]::Replace($recordText, '(?im)^Version=[^\r\n]+', 'Version=999.0.0')
        Require ($futureRecord -cne $recordText) 'The isolated downgrade fixture has an explicit future version.'
        [IO.File]::WriteAllText($installationRecord, $futureRecord, [Text.UTF8Encoding]::new($false))
        $downgradeCode = Invoke-Setup 'downgrade-refused' $true
        Require ($downgradeCode -ne 0) 'Setup refuses to overwrite a newer installed version.'
        Require-LogMarker 'downgrade-refused' 'DOWNGRADE_BLOCKED:' 'The rejection specifically identifies a newer installed version.'
        Require ((File-Hash $sentinel) -ceq $sentinelHash) 'A blocked downgrade preserved the user file.'
        Require ((File-Hash (Join-Path $payloadDirectory 'VineMix.exe')) -ceq (File-Hash (Join-Path $package 'VineMix.exe'))) 'A blocked downgrade preserved desktop bytes.'
        Require ((File-Hash (Join-Path $payloadDirectory 'VineMix.Engine.exe')) -ceq (File-Hash (Join-Path $package 'VineMix.Engine.exe'))) 'A blocked downgrade preserved engine bytes.'
    } finally { [IO.File]::WriteAllBytes($installationRecord, $installationBytes) }

    if (-not $SkipReleaseSmoke) {
        # This specific app mode returns before the singleton, show event, tray,
        # engine connection and personal settings. Ordinary startup is forbidden.
        $uiEvidence = Join-Path $evidence 'installed-release-smoke'
        $ui = Start-Process -FilePath $desktopMatches[0].FullName -ArgumentList ('--release-smoke ' + (Quote-Argument $uiEvidence)) -WindowStyle Hidden -PassThru
        try {
            if (-not $ui.WaitForExit(180000)) { throw "Detached UI verification exceeded its deadline (PID $($ui.Id))." }
            Require ($ui.ExitCode -eq 0) 'Installed release passed its inert smoke check.'
        } finally { $ui.Dispose() }
        $uiReport = Get-Content -LiteralPath (Join-Path $uiEvidence 'results.json') -Raw | ConvertFrom-Json -Depth 32
        Require ([bool]$uiReport.passed) 'Installed release reported successful smoke checks.'
    }

    $uninstaller = $uninstallers[0].FullName
    $uninstallGuardDirectory = [IO.Path]::GetFullPath((Join-Path $target 'guard-fixture'))
    Require (Is-Within $uninstallGuardDirectory $evidence) 'The uninstall process fixture remains inside this test destination.'
    $null = New-Item -ItemType Directory -Path $uninstallGuardDirectory
    $uninstallFixture = Join-Path $uninstallGuardDirectory 'VineMix.exe'
    Copy-Item -LiteralPath $fixtureBuild -Destination $uninstallFixture
    $uninstallFixtureHash = File-Hash $uninstallFixture
    $uninstallEventName = 'Local\VineMix.InstallerVerification.' + [Guid]::NewGuid().ToString('N')
    $uninstallEvent = [Threading.EventWaitHandle]::new($false, [Threading.EventResetMode]::ManualReset, $uninstallEventName)
    $uninstallFixtureProcess = $null
    try {
        $uninstallFixtureProcess = Start-Process -FilePath $uninstallFixture -ArgumentList (Quote-Argument $uninstallEventName) -WindowStyle Hidden -PassThru
        Start-Sleep -Milliseconds 250
        Require (-not $uninstallFixtureProcess.HasExited) 'The no-audio fixture is running below the installed application folder.'
        $blockedUninstallCode = Invoke-Uninstall 'uninstall-destination-in-use' $uninstaller
        Require ($blockedUninstallCode -ne 0) 'Uninstall refuses to remove an application with a process inside its folder.'
        Require-LogMarker 'uninstall-destination-in-use' 'UNINSTALL_TARGET_RUNNING:' 'Uninstall specifically identifies the running destination as its reason for refusal.'
        Require ((File-Hash (Join-Path $target 'VineMix.exe')) -ceq (File-Hash (Join-Path $package 'VineMix.exe'))) 'A blocked uninstall preserved the desktop bytes.'
        Require ((File-Hash (Join-Path $target 'VineMix.Engine.exe')) -ceq (File-Hash (Join-Path $package 'VineMix.Engine.exe'))) 'A blocked uninstall preserved the engine bytes.'
        Require ((Get-UserUninstallEntries).Contains($testRegistryKey)) 'A blocked uninstall preserved its installation registration.'
    } finally {
        $null = $uninstallEvent.Set()
        if ($uninstallFixtureProcess) {
            if (-not $uninstallFixtureProcess.WaitForExit(5000)) { throw "The isolated uninstall fixture did not exit after its event was signalled (PID $($uninstallFixtureProcess.Id))." }
            $uninstallFixtureProcess.Dispose()
        }
        $uninstallEvent.Dispose()
    }
    Require ((Is-Within $uninstallFixture $evidence) -and ((File-Hash $uninstallFixture) -ceq $uninstallFixtureHash)) 'The completed uninstall fixture is the known file created by this test.'
    Remove-Item -LiteralPath $uninstallFixture
    Remove-Item -LiteralPath $uninstallGuardDirectory
    $uninstallCode = Invoke-Uninstall 'uninstall' $uninstaller
    Require ($uninstallCode -eq 0) 'The isolated app uninstalled without requesting a reboot.'
    # Inno can finish the final removal from a temporary child. Give only this
    # known test destination time to settle; never change another installation.
    $uninstallTimer = [Diagnostics.Stopwatch]::StartNew()
    while ((Test-Path -LiteralPath (Join-Path $payloadDirectory 'VineMix.exe')) -and $uninstallTimer.ElapsedMilliseconds -lt 10000) {
        Start-Sleep -Milliseconds 100
    }
    Require (Test-Path -LiteralPath $sentinel -PathType Leaf) 'Uninstall preserved an unknown user file.'
    Require ((File-Hash $sentinel) -ceq $sentinelHash) 'Uninstall did not change the preserved user file.'
    Require (-not (Test-Path -LiteralPath (Join-Path $payloadDirectory 'VineMix.exe'))) 'Uninstall removed the installed desktop payload.'
    Require (-not (Test-Path -LiteralPath (Join-Path $payloadDirectory 'VineMix.Engine.exe'))) 'Uninstall removed the installed engine payload.'
    Require ((To-CanonicalJson (Get-UserUninstallEntries)) -ceq (To-CanonicalJson $registryBefore)) 'Uninstall removed only its isolated registration.'
} catch {
    $failure = $_.Exception.Message
} finally {
    try {
        $after = Get-ProtectedState
        Write-Evidence 'protected-after.json' $after
        if ($before) {
            foreach ($field in @('personalFiles', 'processes', 'startup', 'shortcuts')) {
                $unchanged = (To-CanonicalJson $before[$field]) -ceq (To-CanonicalJson $after[$field])
                $checks.Add([ordered]@{ passed = $unchanged; description = "Protected state unchanged: $field." })
                if (-not $unchanged -and -not $failure) { $failure = "Protected state changed during verification: $field. No automatic restoration was attempted." }
            }
        }
    } catch {
        if (-not $failure) { $failure = 'Could not verify protected state after testing: ' + $_.Exception.Message }
    }
    $passed = -not $failure -and @($checks | Where-Object { -not $_.passed }).Count -eq 0
    $report = [ordered]@{
        started = $started
        finished = [DateTimeOffset]::Now.ToString('o')
        passed = $passed
        error = $failure
        installer = $installer
        installerHash = File-Hash $installer
        stagedPackage = $package
        testDestination = $target
        scope = 'An isolated per-user test installation, harmless waiting processes that exercise replacement and uninstall guards, same-version reinstall, downgrade refusal, uninstall, and optional inert installed-release smoke. Driver installation and normal app launch are disabled. The existing mixer is never stopped or relaunched. Personal saved files are read and backed up; any detected concurrent changes are reported without restoration.'
        checks = $checks
        operations = $operations
        resultDirectory = $evidence
    }
    Write-Evidence 'results.json' $report
    Write-Host ("Installer verification {0}: {1} checks; evidence: {2}" -f $(if ($passed) { 'passed' } else { 'failed' }), $checks.Count, $evidence)
}
if (-not $passed) { throw "Installer verification failed: $failure" }
