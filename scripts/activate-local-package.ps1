#requires -Version 7.3
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CandidateDirectory,
    [Parameter(Mandatory)][string]$EvidenceDirectory,
    [Parameter(Mandatory)][ValidatePattern('^[A-Fa-f0-9]{64}$')][string]$ExpectedEngineHash,
    [Parameter(Mandatory)][ValidatePattern('^[A-Fa-f0-9]{64}$')][string]$ExpectedUiHash,
    [string]$PackageDirectory = '',
    [ValidatePattern('^$|^[A-Fa-f0-9]{64}$')][string]$ExpectedCurrentEngineHash = '',
    [ValidatePattern('^$|^[A-Fa-f0-9]{64}$')][string]$ExpectedCurrentUiHash = '',
    [string]$ShortcutPath = ''
)
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$packageOverride = -not [string]::IsNullOrWhiteSpace($PackageDirectory)
if ($packageOverride -and (-not $ExpectedCurrentEngineHash -or -not $ExpectedCurrentUiHash -or -not $ShortcutPath)) {
    throw 'An explicit PackageDirectory requires both expected current binary hashes and its existing Windows ShortcutPath.'
}
$package = if ($packageOverride) { [IO.Path]::GetFullPath($PackageDirectory) } else { Join-Path $project 'artifacts\VineMix' }
$candidate = [IO.Path]::GetFullPath($CandidateDirectory)
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$uiPath = Join-Path $package 'VineMix.exe'
$enginePath = Join-Path $package 'VineMix.Engine.exe'
$candidateEngine = Join-Path $candidate 'VineMix.Engine.exe'
$candidateUi = Join-Path $candidate 'VineMix.exe'
$settingsPath = Join-Path $env:LOCALAPPDATA 'VineMix\settings.json'
. (Join-Path $PSScriptRoot 'assert-desktop-data-context.ps1')
. (Join-Path $PSScriptRoot 'local-activation-support.ps1')
. (Join-Path $PSScriptRoot 'local-package-activation-support.ps1')
Assert-DesktopDataContext $settingsPath
$physicalSettingsPath = Get-ActivationPhysicalPath $settingsPath
Assert-ActivationEvidenceLocation $evidence @($project,$package,$candidate)
if ($ShortcutPath) { Assert-ActivationShortcut $ShortcutPath $uiPath }
Initialize-ActivationShellLauncher
$shellParentVerified = Test-ActivationShellParent
if ($packageOverride -and -not $shellParentVerified) {
    throw 'Installed-package activation must itself run under the trusted Windows shell parent so ordinary and redirected backup views are distinct. No process was changed.'
}
$backupEngine = Join-Path $evidence 'VineMix.Engine.before.exe'
$backupUi = Join-Path $evidence 'VineMix.before.exe'
$expectedHash = $ExpectedEngineHash.ToUpperInvariant()
$expectedUiHash = $ExpectedUiHash.ToUpperInvariant()
if (Test-Path -LiteralPath $evidence) { throw 'EvidenceDirectory must be a new directory.' }
if ($candidate.TrimEnd('\', '/') -ieq $package.TrimEnd('\', '/')) { throw 'CandidateDirectory must differ from the installed package.' }
$null = New-Item -ItemType Directory -Path $evidence
$result = [ordered]@{
    started = [DateTimeOffset]::Now.ToString('o')
    candidateDirectory = $candidate
    packageDirectory = $package
    evidenceDirectory = $evidence
    expectedEngineHash = $expectedHash
    expectedUiHash = $expectedUiHash
    launchMethod = if ($ShortcutPath) { 'Trusted Windows shell-parent engine and Explorer shortcut controller' } else { 'Trusted Windows shell-parent engine and normalized desktop controller' }
    activationShellParentVerified = $shellParentVerified
    passed = $false
    rolledBack = $false
    settingsRestoredFromBackup = $false
}
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
try { $sid = $identity.User.Value } finally { $identity.Dispose() }
$sessionId = (Get-Process -Id $PID).SessionId
$controlEngineId = 0
$updateRoot = Join-Path (Split-Path -Parent $settingsPath) 'updates'
$updateMutex = $null
$inventoryPlan = $null
$replacements = @()

function Get-ExactProcess([string]$Name, [string]$Path) {
    @(Get-Process -Name $Name -ErrorAction SilentlyContinue | Where-Object { $_.Path -ieq $Path -and $_.SessionId -eq $sessionId })
}
function FileHash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }
function WriteEvidence([string]$Name, $Value) {
    $Value | ConvertTo-Json -Depth 80 | Set-Content -LiteralPath (Join-Path $evidence $Name) -Encoding utf8
}
function Canonical($Value) {
    if ($null -eq $Value) { return 'null' }
    if ($Value -is [double] -or $Value -is [single]) {
        if ($Value -eq 0) { return '0' }
        return $Value.ToString('R', [Globalization.CultureInfo]::InvariantCulture)
    }
    if ($Value -is [System.Management.Automation.PSCustomObject]) {
        $parts = @($Value.PSObject.Properties | Sort-Object Name | ForEach-Object {
            ($_.Name | ConvertTo-Json -Compress) + ':' + (Canonical $_.Value)
        })
        return '{' + ($parts -join ',') + '}'
    }
    if ($Value -is [System.Collections.IEnumerable] -and $Value -isnot [string]) {
        $parts = @(foreach ($item in $Value) { Canonical $item })
        return '[' + ($parts -join ',') + ']'
    }
    return ($Value | ConvertTo-Json -Compress)
}
function Request([ValidateSet('snapshot', 'shutdown')][string]$Command, [int]$TimeoutMilliseconds = 3000) {
    if ($TimeoutMilliseconds -lt 1) { throw 'The engine request deadline has expired.' }
    $deadline = [Threading.CancellationTokenSource]::new()
    $deadline.CancelAfter($TimeoutMilliseconds)
    $pipe = [IO.Pipes.NamedPipeClientStream]::new('.', ('VineMix.Control.' + $sid),
        [IO.Pipes.PipeDirection]::InOut, [IO.Pipes.PipeOptions]::Asynchronous)
    $reader = $null
    try {
        $pipe.ConnectAsync($TimeoutMilliseconds, $deadline.Token).GetAwaiter().GetResult()
        if ($controlEngineId -le 0 -or [VineMix.Desktop.ActivationShellLauncher]::ServerProcessId($pipe.SafePipeHandle) -ne $controlEngineId) {
            throw 'The control pipe belongs to a different engine process; no request was sent.'
        }
        $id = [Guid]::NewGuid().ToString('N')
        $message = @{ protocolVersion = 1; requestId = $id; command = $Command } | ConvertTo-Json -Compress
        $bytes = [Text.UTF8Encoding]::new($false).GetBytes($message + "`n")
        $pipe.WriteAsync($bytes, 0, $bytes.Length, $deadline.Token).GetAwaiter().GetResult()
        $reader = [IO.StreamReader]::new($pipe, [Text.UTF8Encoding]::new($false), $false, 4096, $true)
        $line = $reader.ReadLineAsync($deadline.Token).AsTask().GetAwaiter().GetResult()
        # The byte pipe can close before the shutdown reply is consumed. The
        # caller must still verify that the exact process exits before copying.
        if (-not $line -and $Command -eq 'shutdown') { return }
        if (-not $line) { throw 'The engine closed its connection before returning a snapshot.' }
        $response = $line | ConvertFrom-Json -Depth 80
        if ($response.protocolVersion -ne 1 -or $response.requestId -ne $id -or -not $response.ok) {
            throw "Engine request failed: $Command $($response.error)"
        }
        if ($Command -eq 'snapshot' -and $null -eq $response.snapshot) { throw 'Engine response omitted its snapshot.' }
        return $response
    } finally {
        # Close transport first, including when cancellation interrupts a read.
        $pipe.Dispose()
        if ($reader) { $reader.Dispose() }
        $deadline.Dispose()
    }
}
function VerifyCandidate {
    if ((FileHash $candidateUi) -cne $expectedUiHash) { throw 'Candidate desktop does not match ExpectedUiHash.' }
    if ((FileHash $candidateEngine) -cne $expectedHash) { throw 'Candidate engine does not match ExpectedEngineHash.' }
    $engineVersion = (Get-Item -LiteralPath $candidateEngine).VersionInfo.ProductVersion
    $uiVersion = ((Get-Item -LiteralPath $candidateUi).VersionInfo.ProductVersion -split '\+')[0]
    if (-not $engineVersion -or $engineVersion -cne $uiVersion) { throw 'Candidate controller and engine have different release identities.' }
    $engineNumeric = (Get-Item -LiteralPath $candidateEngine).VersionInfo.FileVersion
    $uiNumeric = (Get-Item -LiteralPath $candidateUi).VersionInfo.FileVersion
    if (-not $engineNumeric -or $engineNumeric -cne $uiNumeric) { throw 'Candidate controller and engine have different Windows file versions.' }
    foreach ($runtime in @('libc++.dll', 'libunwind.dll', 'libwinpthread-1.dll')) {
        if ((FileHash (Join-Path $candidate $runtime)) -cne (FileHash (Join-Path $package $runtime))) {
            throw "Candidate runtime differs from the installed package: $runtime. This update preserves the installed runtime libraries."
        }
    }
}
function HoldUpdateGuard {
    if (-not $script:updateMutex) {
        $script:updateMutex = Enter-PackageActivationUpdateGuard ('Global\VineMix.Update.' + $sid) $updateRoot
    }
    Assert-UiActivationUpdateHistory $updateRoot
}
function ReleaseUpdateGuard {
    if ($script:updateMutex) {
        try { $script:updateMutex.ReleaseMutex() }
        finally { $script:updateMutex.Dispose(); $script:updateMutex = $null }
    }
}
function AssertReplacementProcessGuard {
    if (@(Get-Process -Name VineMix,VineMix.Engine,AudioMixer,AudioMixer.Engine -ErrorAction SilentlyContinue).Count) {
        throw 'An application process appeared during package replacement. Its process and files were preserved.'
    }
    HoldUpdateGuard
    Assert-DesktopDataContext $settingsPath
    if ((Get-ActivationPhysicalPath $settingsPath) -ine $physicalSettingsPath) { throw 'The physical workspace changed during replacement.' }
}
function Start-PackagedEngine([string]$LogPrefix) {
    # The tested production shell-parent launcher removes inherited AppData
    # redirection before the engine opens preferences or creates audio clients.
    Start-ActivationEngine -ExecutablePath $enginePath -WorkingDirectory $package -SettingsPath $settingsPath
}
function Start-PackagedUi {
    # Normal startup checks the same update mutex. No old controller remains to
    # prepare an update; keep history checked until the actual shortcut launch.
    Assert-UiActivationUpdateHistory $updateRoot
    ReleaseUpdateGuard
    Start-DesktopController -ExecutablePath $uiPath -WorkingDirectory $package -ShortcutPath $ShortcutPath `
        -OnVerifiedProcess { param($identity) $script:newUiIdentity = $identity }
}

$controllerStopped = $false
$shutdownRequested = $false
$replacementAttempted = $false
$oldEngineHash = $null
$settingsHash = $null
$newEngine = $null
$newUi = $null
$newUiIdentity = $null
try {
    VerifyCandidate
    Assert-UiActivationUpdateHistory $updateRoot
    foreach ($path in @($uiPath, $enginePath, $settingsPath)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required file is missing: $path" }
    }
    $allProcesses = @(Get-Process -Name VineMix, VineMix.Engine, AudioMixer, AudioMixer.Engine -ErrorAction SilentlyContinue)
    if (@($allProcesses | Where-Object { ($_.Path -ine $uiPath -and $_.Path -ine $enginePath) -or $_.SessionId -ne $sessionId }).Count) {
        throw 'A VineMix process is running from another package. No process was stopped.'
    }
    $oldUi = @(Get-ExactProcess 'VineMix' $uiPath)
    $oldEngine = @(Get-ExactProcess 'VineMix.Engine' $enginePath)
    if ($oldUi.Count -ne 1 -or $oldEngine.Count -ne 1) { throw 'Expected exactly one installed controller and one installed engine.' }
    if ((Split-Path -Parent $oldUi[0].Path) -ine $package -or (Split-Path -Parent $oldEngine[0].Path) -ine $package) {
        throw 'The current controller and engine are not in the same verified package.'
    }
    $overridePath = $env:AUDIOMIXER_ENGINE_PATH
    if ($overridePath -and (Test-Path -LiteralPath $overridePath) -and [IO.Path]::GetFullPath($overridePath) -ine $enginePath) {
        throw 'AUDIOMIXER_ENGINE_PATH points to a different engine; reopening would supervise another package.'
    }
    $result.oldUiId = $oldUi[0].Id
    $result.oldEngineId = $oldEngine[0].Id
    $controlEngineId = $oldEngine[0].Id
    $result.originalUiHash = FileHash $uiPath
    $oldEngineHash = FileHash $enginePath
    if (($ExpectedCurrentUiHash -and $result.originalUiHash -cne $ExpectedCurrentUiHash.ToUpperInvariant()) -or
        ($ExpectedCurrentEngineHash -and $oldEngineHash -cne $ExpectedCurrentEngineHash.ToUpperInvariant())) {
        throw 'The running package differs from its reviewed current hashes. No process was stopped.'
    }
    $oldVersion = (Get-Item -LiteralPath $enginePath).VersionInfo.ProductVersion
    $oldUiVersion = ((Get-Item -LiteralPath $uiPath).VersionInfo.ProductVersion -split '\+')[0]
    if (-not $oldVersion -or $oldVersion -cne $oldUiVersion) { throw 'The current controller and engine have different release identities.' }
    $candidateVersion = (Get-Item -LiteralPath $candidateEngine).VersionInfo.ProductVersion
    $candidateNumericVersion = (Get-Item -LiteralPath $candidateEngine).VersionInfo.FileVersion
    $result.originalVersion = $oldVersion
    $result.candidateVersion = $candidateVersion
    $inventoryPlan = New-PackageActivationInventoryPlan $package $evidence $candidate `
        $result.originalUiHash $oldEngineHash $expectedUiHash $expectedHash $oldVersion $candidateVersion `
        $candidateNumericVersion -RequireInventory:$packageOverride
    $result.installedInventoryPresent = $inventoryPlan.Present
    if ($inventoryPlan.Present) {
        $result.originalInventoryHash = $inventoryPlan.OriginalHash
        $result.candidateInventoryHash = $inventoryPlan.CandidateHash
    }
    # Preserve every known ordinary and redirected workspace view before the
    # first process change. No backup is ever restored over a newer workspace.
    $dataBackups = @(Backup-ActivationDataViews (Split-Path -Parent $settingsPath) (Join-Path $evidence 'data-views'))
    WriteEvidence 'preserved-data-views.json' $dataBackups
    Copy-Item -LiteralPath $uiPath -Destination $backupUi
    Copy-Item -LiteralPath $enginePath -Destination $backupEngine
    if ((FileHash $backupUi) -cne $result.originalUiHash -or (FileHash $backupEngine) -cne $oldEngineHash) {
        throw 'A current binary changed while creating its recovery backup. No process was stopped.'
    }
    $replacements = @(
        [pscustomobject]@{Source=$candidateEngine;Target=$enginePath;Backup=$backupEngine;OriginalHash=$oldEngineHash;CandidateHash=$expectedHash},
        [pscustomobject]@{Source=$candidateUi;Target=$uiPath;Backup=$backupUi;OriginalHash=$result.originalUiHash;CandidateHash=$expectedUiHash})
    $quietHash = FileHash $settingsPath
    $preferencesPath = Join-Path (Split-Path -Parent $settingsPath) 'ui.json'
    $preferencesExisted = Test-Path -LiteralPath $preferencesPath -PathType Leaf
    $preferencesHash = if ($preferencesExisted) { FileHash $preferencesPath } else { $null }
    Start-Sleep -Milliseconds 1500
    if ((FileHash $settingsPath) -cne $quietHash) { throw 'Settings changed during the quiet interval. Retry after the current edit finishes.' }
    Assert-ActivationDataStable $dataBackups
    if ((FileHash $uiPath) -cne $result.originalUiHash -or (FileHash $enginePath) -cne $oldEngineHash) { throw 'The current binaries changed before activation.' }
    Assert-DesktopDataContext $settingsPath
    if ((Get-ActivationPhysicalPath $settingsPath) -ine $physicalSettingsPath) { throw 'The physical workspace changed before activation.' }
    HoldUpdateGuard
    Assert-UiActivationInventoryState $inventoryPlan Original

    # Window close hides the UI. There is no quit IPC, and the tray is unavailable
    # to this script. Release its sole pipe connection and stop engine supervision.
    $oldUi[0].Kill()
    $controllerStopped = $true
    if (-not $oldUi[0].WaitForExit(5000)) { throw 'The exact controller process did not stop.' }
    Assert-UiActivationUpdateHistory $updateRoot
    $before = (Request 'snapshot').snapshot
    WriteEvidence 'before.json' $before
    Assert-ActivationSnapshotIdentity $before $oldEngine[0].Id $enginePath $settingsPath $physicalSettingsPath $oldVersion
    Assert-ActivationDataStable $dataBackups
    $settingsHash = FileHash $settingsPath
    Copy-Item -LiteralPath $settingsPath -Destination (Join-Path $evidence 'settings-before.json')
    if ((FileHash (Join-Path $evidence 'settings-before.json')) -cne $settingsHash) { throw 'Settings changed while creating their backup.' }
    foreach ($file in @('settings.json.bak', 'ui.json')) {
        $source = Join-Path (Split-Path -Parent $settingsPath) $file
        if (Test-Path -LiteralPath $source -PathType Leaf) { Copy-Item -LiteralPath $source -Destination (Join-Path $evidence $file) }
    }
    $result.originalEngineHash = $oldEngineHash
    $result.settingsHashBefore = $settingsHash
    $result.revisionBefore = $before.configuration.revision
    $beforeConfig = Canonical (Get-PackageActivationComparableConfiguration $before.configuration)
    # Application capture has an app binding rather than a device binding.
    # Require every previously running path, including applications, to recover.
    $wasRunning = @($before.status.nodes | Where-Object { $_.path.state -eq 'Running' } | ForEach-Object id)
    $result.previouslyRunningNodeIds = $wasRunning

    # Recheck the staged artifacts immediately before the interruption/copy.
    VerifyCandidate
    Assert-DesktopDataContext $settingsPath
    Assert-ActivationDataStable $dataBackups
    Assert-UiActivationInventoryState $inventoryPlan Original
    HoldUpdateGuard
    $shutdownRequested = $true
    try { $null = Request 'shutdown' } catch {
        $result.shutdownReplyWarning = $_.Exception.Message
    }
    if (-not $oldEngine[0].WaitForExit(5000)) { throw 'The exact engine process did not exit gracefully.' }
    if (@(Get-ExactProcess 'VineMix' $uiPath).Count -or @(Get-ExactProcess 'VineMix.Engine' $enginePath).Count) {
        throw 'The installed controller or engine restarted before replacement.'
    }
    Assert-ActivationDataStable $dataBackups
    $replacementAttempted = $true
    Install-PackageActivationPayload $inventoryPlan $replacements { AssertReplacementProcessGuard }
    if ((FileHash $uiPath) -cne $expectedUiHash) { throw 'Installed desktop hash differs from verified candidate.' }
    if ((FileHash $enginePath) -cne $expectedHash) { throw 'The installed engine does not match the verified candidate.' }
    Assert-UiActivationInventoryState $inventoryPlan Candidate
    $newEngine = Start-PackagedEngine 'candidate-engine'
    $controlEngineId = $newEngine.Id
    $result.newEngineId = $newEngine.Id
    $readiness = [Diagnostics.Stopwatch]::StartNew()
    $firstFrames = $null
    $ready = $false
    $lastProbeError = ''
    do {
        $newEngine.Refresh()
        if ($newEngine.HasExited) { throw 'The candidate engine exited during startup.' }
        $remaining = 12000 - [int]$readiness.ElapsedMilliseconds
        if ($remaining -le 0) { break }
        try { $after = (Request 'snapshot' ([Math]::Min(3000, $remaining))).snapshot }
        catch { $lastProbeError = $_.Exception.Message; continue }
        Assert-ActivationSnapshotIdentity $after $newEngine.Id $enginePath $settingsPath (Get-ActivationPhysicalPath $settingsPath) $candidateVersion
        if ((Get-ActivationPhysicalPath $settingsPath) -ine $physicalSettingsPath) { throw 'The candidate opened a different physical settings location.' }
        if ((Canonical (Get-PackageActivationComparableConfiguration $after.configuration)) -cne $beforeConfig) { throw 'Configuration changed across the engine restart.' }
        if ($null -eq $firstFrames) { $firstFrames = [ulong]$after.status.audioFrames }
        $running = @($after.status.nodes | Where-Object { $_.path.state -eq 'Running' } | ForEach-Object id)
        $missing = @($wasRunning | Where-Object { $_ -notin $running })
        if ($missing.Count -eq 0 -and [ulong]$after.status.audioFrames -gt $firstFrames) { $ready = $true; break }
        $remaining = 12000 - [int]$readiness.ElapsedMilliseconds
        if ($remaining -gt 0) { Start-Sleep -Milliseconds ([Math]::Min(100, $remaining)) }
    } while ($readiness.ElapsedMilliseconds -lt 12000)
    $result.secondsToReady = [Math]::Round($readiness.Elapsed.TotalSeconds, 3)
    if (-not $ready) { throw "The engine did not recover all previously running assigned nodes with advancing frames within 12 seconds. $lastProbeError" }
    WriteEvidence 'after.json' $after
    if ((FileHash $settingsPath) -cne $settingsHash) { throw 'Saved settings changed during the restart.' }
    if ((FileHash $uiPath) -cne $expectedUiHash) { throw 'The desktop executable differs from the verified candidate.' }
    Assert-UiActivationInventoryState $inventoryPlan Candidate
    $newUi = Start-PackagedUi
    $result.newUiId = $newUi.Id
    $result.windowTitle = $newUi.MainWindowTitle
    $result.windowHandle = $newUi.MainWindowHandle.ToInt64()
    # Observe longer than the desktop's six-second stalled-audio threshold.
    # The UI now owns the control pipe; inspect identities and saved files only.
    Invoke-PackageActivationObservation {
        $liveControllers = @(Get-Process -Name VineMix,AudioMixer -ErrorAction SilentlyContinue)
        $liveEngines = @(Get-Process -Name VineMix.Engine,AudioMixer.Engine -ErrorAction SilentlyContinue)
        try { Assert-PackageActivationRunningProcesses $liveControllers $liveEngines $newUiIdentity $newEngine }
        finally { foreach ($process in @($liveControllers) + @($liveEngines)) { $process.Dispose() } }
        Assert-DesktopDataContext $settingsPath
        if ((Get-ActivationPhysicalPath $settingsPath) -ine $physicalSettingsPath) { throw 'The physical workspace changed after reopening.' }
        if ((FileHash $settingsPath) -cne $settingsHash) { throw 'Saved settings changed when reopening the controller.' }
        if ((Test-Path -LiteralPath $preferencesPath -PathType Leaf) -ne $preferencesExisted -or
            ($preferencesExisted -and (FileHash $preferencesPath) -cne $preferencesHash)) {
            throw 'Workspace preferences changed when reopening the controller; the newer preferences will be preserved.'
        }
        if ((FileHash $uiPath) -cne $expectedUiHash -or (FileHash $enginePath) -cne $expectedHash) {
            throw 'A candidate executable changed after reopening the controller.'
        }
        Assert-UiActivationInventoryState $inventoryPlan Candidate
        Assert-UiActivationPreservedWorkspaces $dataBackups
    }
    $result.postLaunchObservationMilliseconds = 8000
    Assert-UiActivationInventoryState $inventoryPlan Candidate
    Assert-UiActivationPreservedWorkspaces $dataBackups
    # This local candidate replaces only the verified executables. Distribution
    # documentation and installer files belong to a later packaging/release step.
    $result.copiedDocuments = @()
    $result.installedInventoryConsistent = $true
    $result.installedEngineHash = FileHash $enginePath
    $result.installedUiHash = FileHash $uiPath
    $result.settingsHashAfter = FileHash $settingsPath
    $result.sameConfiguration = $true
    $result.settingsFileUnchanged = $true
    $result.workspacePreferencesUnchanged = $true
    $result.physicalSettingsPathVerified = $true
    $result.physicalSettingsPath = $physicalSettingsPath
    $result.processAndVersionIdentityVerified = $true
    $result.frameCounterAdvancing = $true
    $result.previouslyRunningAssignedPathsRestored = $true
    $result.paths = @($after.status.nodes | ForEach-Object { [ordered]@{ id = $_.id; state = $_.path.state } })
    $result.passed = $true
} catch {
    $result.error = $_.Exception.ToString()
    $recoveryErrors = [Collections.Generic.List[string]]::new()
    if ($controllerStopped) {
        # No settings backup is ever written back. A user's newer configuration
        # remains authoritative even if candidate activation must be rolled back.
        if ($replacementAttempted) {
            $safeToRestore = $true
            try {
                HoldUpdateGuard
                foreach ($process in @(Get-ExactProcess 'VineMix' $uiPath)) {
                    if (-not (Test-ActivationProcessIdentity $process $newUiIdentity)) { throw 'A different controller appeared; rollback will not stop it.' }
                    $process.Kill()
                    if (-not $process.WaitForExit(5000)) { throw 'The replacement controller did not stop for rollback.' }
                }
                $engines = @(Get-ExactProcess 'VineMix.Engine' $enginePath)
                foreach ($process in $engines) {
                    if (-not (Test-ActivationProcessIdentity $process $newEngine)) { throw 'A different engine appeared; rollback will not stop it.' }
                }
                if ($engines.Count -eq 1) { $controlEngineId = $engines[0].Id; try { $null = Request 'shutdown' } catch { } }
                foreach ($process in $engines) {
                    if (-not $process.WaitForExit(5000)) {
                        $process.Kill()
                        if (-not $process.WaitForExit(5000)) { throw 'The replacement engine did not stop for rollback.' }
                    }
                }
                if (@(Get-ExactProcess 'VineMix' $uiPath).Count -or @(Get-ExactProcess 'VineMix.Engine' $enginePath).Count) {
                    throw 'A packaged process remains active; rollback will not overwrite a running executable.'
                }
            } catch { $safeToRestore = $false; $recoveryErrors.Add($_.Exception.Message) }
            if ($safeToRestore) {
                try {
                    Restore-PackageActivationPayload $inventoryPlan $replacements { AssertReplacementProcessGuard }
                    if ((FileHash $uiPath) -cne $result.originalUiHash) { throw 'Restored desktop hash differs.' }
                    if ((FileHash $enginePath) -cne $oldEngineHash) { throw 'The restored engine hash does not match the original.' }
                    $result.rolledBack = $true
                } catch { $recoveryErrors.Add('Restore engine: ' + $_.Exception.Message) }
            }
        }
        $canRestart = -not $replacementAttempted -or $result.rolledBack
        if ($canRestart) {
            try {
                if ($shutdownRequested -and -not @(Get-ExactProcess 'VineMix.Engine' $enginePath).Count) {
                    $recoveredEngine = Start-PackagedEngine 'recovered-engine'
                    $result.recoveredEngineId = $recoveredEngine.Id
                }
            } catch { $recoveryErrors.Add('Restart original engine: ' + $_.Exception.Message) }
            try {
                if (-not @(Get-ExactProcess 'VineMix' $uiPath).Count) {
                    $recoveredUi = Start-PackagedUi
                    $result.recoveredUiId = $recoveredUi.Id
                    $result.recoveredUiResponsive = $true
                }
            } catch { $recoveryErrors.Add('Reopen original controller: ' + $_.Exception.Message) }
        } else { $recoveryErrors.Add('Automatic restart was withheld because the original engine could not be safely restored.') }
    }
    $result.recoveryErrors = @($recoveryErrors)
} finally {
    ReleaseUpdateGuard
    $result.finished = [DateTimeOffset]::Now.ToString('o')
    WriteEvidence 'result.json' $result
}
$result | ConvertTo-Json -Depth 20
if (-not $result.passed) { exit 1 }
