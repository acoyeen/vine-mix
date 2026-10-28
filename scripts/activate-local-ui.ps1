#requires -Version 7.3
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CandidateDirectory,
    [Parameter(Mandatory)][string]$PackageDirectory,
    [Parameter(Mandatory)][string]$EvidenceDirectory,
    [Parameter(Mandatory)][ValidatePattern('^[A-Fa-f0-9]{64}$')][string]$ExpectedUiHash,
    [Parameter(Mandatory)][ValidatePattern('^[A-Fa-f0-9]{64}$')][string]$ExpectedCurrentUiHash,
    [Parameter(Mandatory)][ValidatePattern('^[A-Fa-f0-9]{64}$')][string]$ExpectedEngineHash,
    [Parameter(Mandatory)][string]$ShortcutPath
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'assert-desktop-data-context.ps1')
. (Join-Path $PSScriptRoot 'local-activation-support.ps1')
. (Join-Path $PSScriptRoot 'local-ui-activation-support.ps1')
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$package = [IO.Path]::GetFullPath($PackageDirectory)
$candidate = [IO.Path]::GetFullPath($CandidateDirectory)
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$uiPath = Join-Path $package 'VineMix.exe'
$enginePath = Join-Path $package 'VineMix.Engine.exe'
$candidateUi = Join-Path $candidate 'VineMix.exe'
$settingsPath = Join-Path $env:LOCALAPPDATA 'VineMix\settings.json'
$preferencesPath = Join-Path $env:LOCALAPPDATA 'VineMix\ui.json'
$updateRoot = Join-Path $env:LOCALAPPDATA 'VineMix\updates'
$expectedUi = $ExpectedUiHash.ToUpperInvariant()
$expectedOldUi = $ExpectedCurrentUiHash.ToUpperInvariant()
$expectedEngine = $ExpectedEngineHash.ToUpperInvariant()
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
try { $sid = $identity.User.Value } finally { $identity.Dispose() }
$session = (Get-Process -Id $PID).SessionId

# All context checks precede evidence creation, backups and process changes.
Assert-DesktopDataContext $settingsPath
Initialize-ActivationShellLauncher
if (-not (Test-ActivationShellParent)) {
    throw 'UI-only activation must itself run under the active same-session Windows shell parent. No application process was changed.'
}
Assert-ActivationEvidenceLocation $evidence @($project,$package,$candidate)
if ((Normalize-ActivationPath $candidate) -ieq (Normalize-ActivationPath $package)) { throw 'Use a separate staged candidate directory.' }
Assert-ActivationShortcut $ShortcutPath $uiPath
if (Test-Path -LiteralPath $evidence) { throw 'EvidenceDirectory must be a new directory outside both packages and the source checkout.' }
$null = New-Item -ItemType Directory -Path $evidence
$result = [ordered]@{
    started = [DateTimeOffset]::Now.ToString('o'); passed = $false; rolledBack = $false
    packageDirectory = $package; candidateDirectory = $candidate; evidenceDirectory = $evidence
    expectedUiHash = $expectedUi; originalUiHash = $expectedOldUi; originalEngineHash = $expectedEngine
    launchMethod = 'Trusted Windows shell-parent activation and actual Explorer shortcut controller'
    engineRestartRequested = $false; engineBinaryReplaced = $false; settingsRestoredFromBackup = $false
    pendingEditsIndependentlyChecked = $false
}
$state = @{ NewUiIdentity = $null; OriginalStillRunning = $false; UpdateMutex = $null; Inventory = $null }
$oldUi = $null
$oldEngine = $null
$engineIdentity = $null
$oldUiIdentity = $null
$backupUi = Join-Path $evidence 'VineMix.before.exe'
$physicalSettings = Get-ActivationPhysicalPath $settingsPath

function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }
function SaveEvidence([string]$Name, $Value) {
    $Value | ConvertTo-Json -Depth 80 | Set-Content -LiteralPath (Join-Path $evidence $Name) -Encoding utf8
}
function CaptureIdentity($Process) {
    $null = $Process.Handle # Bind the process object before any later stop.
    [pscustomobject]@{Id=$Process.Id; StartTime=$Process.StartTime; Path=$Process.Path; SessionId=$Process.SessionId}
}
function AssertNoUpdate {
    $mutex = $null
    try {
        try { $mutex = [Threading.Mutex]::OpenExisting('Global\VineMix.Update.' + $sid) }
        catch [Threading.WaitHandleCannotBeOpenedException] { }
        if ($mutex) {
            $owned = $false
            try {
                try { $owned = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $owned = $true }
                if (-not $owned) { throw 'An update worker owns the application. UI-only activation was withheld.' }
            } finally { if ($owned) { $mutex.ReleaseMutex() } }
        }
        Assert-UiActivationUpdateHistory $updateRoot
    } finally { if ($mutex) { $mutex.Dispose() } }
}
function HoldUpdateGuard {
    if ($state.UpdateMutex) { return }
    $mutex = [Threading.Mutex]::new($false, ('Global\VineMix.Update.' + $sid))
    $owned = $false
    try {
        try { $owned = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $owned = $true }
        if (-not $owned) { throw 'An update worker became active. No desktop was stopped.' }
        $state.UpdateMutex = $mutex
    } finally { if (-not $owned) { $mutex.Dispose() } }
}
function ReleaseUpdateGuard {
    if ($state.UpdateMutex) {
        try { $state.UpdateMutex.ReleaseMutex() }
        finally { $state.UpdateMutex.Dispose(); $state.UpdateMutex = $null }
    }
}
function AssertEngineUnchanged {
    $engines = @(Get-Process -Name VineMix.Engine,AudioMixer.Engine -ErrorAction SilentlyContinue)
    try {
        if ($engines.Count -ne 1 -or -not (Test-ActivationProcessIdentity $engines[0] $engineIdentity) -or
            (Hash $enginePath) -cne $expectedEngine) {
            throw 'The original audio engine changed or exited. UI-only activation will not stop, launch, replace or recover an engine.'
        }
        foreach ($runtime in $state.RuntimeHashes.GetEnumerator()) {
            if ((Hash (Join-Path $package $runtime.Key)) -cne $runtime.Value) { throw 'An installed runtime library changed independently.' }
        }
    } finally { foreach ($process in $engines) { $process.Dispose() } }
}
function AssertSettingsIdentity {
    Assert-DesktopDataContext $settingsPath
    if ((Get-ActivationPhysicalPath $settingsPath) -ine $physicalSettings) { throw 'The physical settings location changed; the current workspace was left untouched.' }
}
function AssertReplacementProcessGuard {
    AssertEngineUnchanged
    if (@(Get-Process -Name VineMix,AudioMixer -ErrorAction SilentlyContinue).Count) {
        throw 'A desktop appeared during file replacement. Its process and files were left untouched.'
    }
}
function AssertSavedWorkspaceUnchanged {
    AssertSettingsIdentity
    if ((Hash $settingsPath) -cne $state.SettingsHash) { throw 'Saved settings changed. The newer workspace will be preserved.' }
    if ((Test-Path -LiteralPath $preferencesPath -PathType Leaf) -ne $state.PreferencesExisted -or
        ($state.PreferencesExisted -and (Hash $preferencesPath) -cne $state.PreferencesHash)) {
        throw 'Workspace preferences changed. The newer preferences will be preserved.'
    }
}
function AssertCandidate {
    if ((Hash $candidateUi) -cne $expectedUi) { throw 'The staged desktop does not match its reviewed hash.' }
    if ((Hash (Join-Path $candidate 'VineMix.Engine.exe')) -cne $expectedEngine) {
        throw 'The staged runtime must be byte-identical to the unchanged installed engine for a UI-only activation.'
    }
    Assert-UiActivationRelease (Get-Item -LiteralPath $candidateUi).VersionInfo.ProductVersion `
        (Get-Item -LiteralPath $uiPath).VersionInfo.ProductVersion $state.Version
    foreach ($runtime in $state.RuntimeHashes.GetEnumerator()) {
        if ((Hash (Join-Path $candidate $runtime.Key)) -cne $runtime.Value) { throw 'The staged runtime libraries differ from the unchanged installed runtime.' }
    }
}
function GetSnapshot {
    # The normal desktop owns the only persistent control connection. Request
    # snapshots only while no UI is running, never compete after reopening it.
    if (@(Get-Process -Name VineMix,AudioMixer -ErrorAction SilentlyContinue).Count) { throw 'The desktop still owns the engine connection; no snapshot request was sent.' }
    AssertEngineUnchanged
    $deadline = [Threading.CancellationTokenSource]::new(3000)
    $pipe = [IO.Pipes.NamedPipeClientStream]::new('.', ('VineMix.Control.' + $sid),
        [IO.Pipes.PipeDirection]::InOut, [IO.Pipes.PipeOptions]::Asynchronous)
    $reader = $null
    try {
        $pipe.ConnectAsync(2500, $deadline.Token).GetAwaiter().GetResult()
        if ([VineMix.Desktop.ActivationShellLauncher]::ServerProcessId($pipe.SafePipeHandle) -ne $engineIdentity.Id) {
            throw 'The control pipe belongs to another engine; no request was sent.'
        }
        AssertEngineUnchanged
        $requestId = [guid]::NewGuid().ToString('N')
        # No mutation command is accepted by this function or its callers.
        $bytes = [Text.UTF8Encoding]::new($false).GetBytes((@{protocolVersion=1;requestId=$requestId;command='snapshot'} | ConvertTo-Json -Compress) + "`n")
        $pipe.WriteAsync($bytes, 0, $bytes.Length, $deadline.Token).GetAwaiter().GetResult()
        $reader = [IO.StreamReader]::new($pipe, [Text.UTF8Encoding]::new($false), $false, 4096, $true)
        $line = $reader.ReadLineAsync($deadline.Token).AsTask().GetAwaiter().GetResult()
        if (-not $line -or [Text.Encoding]::UTF8.GetByteCount($line) -ge 256KB) { throw 'The engine snapshot response is missing or too large.' }
        $reply = $line | ConvertFrom-Json -Depth 80
        if ($reply.protocolVersion -ne 1 -or $reply.requestId -cne $requestId -or -not $reply.ok -or $null -eq $reply.snapshot) {
            throw 'The engine did not return the requested read-only snapshot.'
        }
        AssertEngineUnchanged
        AssertSettingsIdentity
        Assert-ActivationSnapshotIdentity $reply.snapshot $engineIdentity.Id $enginePath $settingsPath $physicalSettings $state.Version
        return $reply.snapshot
    } finally {
        $pipe.Dispose()
        if ($reader) { $reader.Dispose() }
        $deadline.Dispose()
    }
}
function StartUi {
    AssertEngineUnchanged
    AssertSettingsIdentity
    AssertNoUpdate
    # The previous desktop is gone and unfinished update history was rejected.
    # Release before normal startup, which checks this same update mutex.
    ReleaseUpdateGuard
    $process = Start-DesktopController -ExecutablePath $uiPath -WorkingDirectory $package -ShortcutPath $ShortcutPath `
        -OnVerifiedProcess { param($identity) $state.NewUiIdentity = $identity }
    try { $result.newUiId = $process.Id; $result.windowTitle = $process.MainWindowTitle }
    finally { $process.Dispose() }
}

try {
    $all = @(Get-Process -Name VineMix,VineMix.Engine,AudioMixer,AudioMixer.Engine -ErrorAction SilentlyContinue)
    if ($all.Count -ne 2 -or @($all | Where-Object { $_.SessionId -ne $session -or $_.Path -notin @($uiPath,$enginePath) }).Count) {
        throw 'Expected only the selected installed desktop and engine in this Windows session. No process was stopped.'
    }
    $oldUi = @($all | Where-Object Path -eq $uiPath)[0]
    $oldEngine = @($all | Where-Object Path -eq $enginePath)[0]
    if ($null -eq $oldUi -or $null -eq $oldEngine) { throw 'Expected exactly one installed desktop and one installed engine.' }
    $oldUiIdentity = CaptureIdentity $oldUi
    $engineIdentity = CaptureIdentity $oldEngine
    $state.Version = (Get-Item -LiteralPath $enginePath).VersionInfo.ProductVersion
    $state.RuntimeHashes = @{}
    foreach ($name in @('libc++.dll','libunwind.dll','libwinpthread-1.dll')) { $state.RuntimeHashes[$name] = Hash (Join-Path $package $name) }
    $result.originalEngineIdentity = $engineIdentity
    $result.originalUiIdentity = $oldUiIdentity
    $result.version = $state.Version
    if ((Hash $uiPath) -cne $expectedOldUi) { throw 'The current desktop differs from its reviewed hash. No process was stopped.' }
    AssertEngineUnchanged
    AssertCandidate
    AssertNoUpdate
    # This inert branch precedes ordinary app startup, preferences and engines.
    # Check the actual candidate hash again after its Installed-mode smoke run.
    $smokeDirectory = Join-Path $evidence 'candidate-smoke'
    $smoke = Start-Process -FilePath $candidateUi -ArgumentList @('--release-smoke', ('"' + $smokeDirectory + '"')) `
        -WorkingDirectory $candidate -WindowStyle Hidden -PassThru
    try {
        if (-not $smoke.WaitForExit(30000)) {
            $smoke.Kill(); $null = $smoke.WaitForExit(5000)
            throw 'The inert candidate smoke check exceeded its deadline.'
        }
        if ($smoke.ExitCode -ne 0) { throw 'The inert candidate smoke check failed.' }
    } finally { $smoke.Dispose() }
    $smokeResult = Get-Content -LiteralPath (Join-Path $smokeDirectory 'results.json') -Raw | ConvertFrom-Json
    if (-not $smokeResult.passed -or $smokeResult.mode -cne 'Installed' -or $smokeResult.version -cne $state.Version) {
        throw 'The candidate must pass its actual Installed-mode smoke check with the unchanged engine release identity.'
    }
    AssertCandidate
    AssertEngineUnchanged
    $state.Inventory = New-UiActivationInventoryPlan $package $evidence $candidateUi $expectedOldUi $expectedUi $state.Version
    $result.installedInventoryPresent = $state.Inventory.Present
    if ($state.Inventory.Present) {
        $result.originalInventoryHash = $state.Inventory.OriginalHash
        $result.candidateInventoryHash = $state.Inventory.CandidateHash
    }
    # Preserve every known ordinary/redirected view, including legacy copies.
    # Disk stability is not a claim to have inspected pending in-memory edits.
    $state.DataBackups = @(Backup-ActivationDataViews (Split-Path -Parent $settingsPath) (Join-Path $evidence 'data-views'))
    SaveEvidence 'preserved-data-views.json' $state.DataBackups
    Copy-Item -LiteralPath $uiPath -Destination $backupUi
    if ((Hash $backupUi) -cne $expectedOldUi) { throw 'The current desktop changed during its recovery backup.' }
    $state.SettingsHash = Hash $settingsPath
    $state.PreferencesExisted = Test-Path -LiteralPath $preferencesPath -PathType Leaf
    $state.PreferencesHash = if ($state.PreferencesExisted) { Hash $preferencesPath } else { $null }
    Start-Sleep -Milliseconds 1500
    $actions = @{
        VerifyBeforeStop = {
            $result.stage = 'verifying-before-controller-stop'
            # Update workers must own this mutex before they signal Ready, and
            # the desktop never shuts down audio before that signal. Hold it
            # through replacement to close a concurrent install-click race.
            HoldUpdateGuard
            AssertEngineUnchanged; AssertCandidate; AssertNoUpdate; AssertSavedWorkspaceUnchanged
            Assert-UiActivationInventoryState $state.Inventory Original
            Assert-ActivationDataStable $state.DataBackups
            Assert-ActivationShortcut $ShortcutPath $uiPath
            if ((Hash $uiPath) -cne $expectedOldUi -or -not (Test-ActivationProcessIdentity $oldUi $oldUiIdentity)) {
                throw 'The original desktop changed before replacement.'
            }
        }
        StopOriginalUi = {
            $result.stage = 'stopping-controller'
            # Close hides the window; normal Quit intentionally stops audio.
            # Stop only this exact controller, leaving its independent engine.
            $oldUi.Kill()
            if (-not $oldUi.WaitForExit(5000)) { throw 'The exact original desktop did not exit.' }
        }
        CaptureBaseline = {
            $result.stage = 'capturing-engine-baseline'
            $state.Before = GetSnapshot
            SaveEvidence 'before.json' $state.Before
        }
        VerifyBeforeReplace = {
            $result.stage = 'verifying-before-replacement'
            AssertEngineUnchanged; AssertCandidate; AssertNoUpdate; AssertSavedWorkspaceUnchanged
            Assert-UiActivationInventoryState $state.Inventory Original
            Assert-ActivationDataStable $state.DataBackups
            if ((Hash $uiPath) -cne $expectedOldUi) { throw 'The installed desktop changed independently before replacement.' }
        }
        InstallUi = {
            $result.stage = 'replacing-desktop'
            Assert-UiActivationInventoryState $state.Inventory Original
            Replace-ActivationBinary $candidateUi $uiPath $expectedUi @($expectedOldUi) { AssertReplacementProcessGuard } 5000
            $result.stage = 'replacing-installed-inventory'
            Install-UiActivationInventory $state.Inventory { AssertReplacementProcessGuard } 5000
        }
        VerifyBeforeLaunch = {
            $result.stage = 'verifying-before-shortcut-launch'
            Start-Sleep -Milliseconds 200
            $state.After = GetSnapshot
            Assert-UiActivationSnapshotStable $state.Before $state.After
            SaveEvidence 'after-ui-replacement.json' $state.After
            AssertSavedWorkspaceUnchanged; Assert-ActivationDataStable $state.DataBackups
            Assert-UiActivationInventoryState $state.Inventory Candidate
            if ((Hash $uiPath) -cne $expectedUi) { throw 'The installed desktop no longer matches the verified candidate.' }
        }
        StartUi = { $result.stage = 'opening-workspace'; StartUi }
        VerifyAfterLaunch = {
            $result.stage = 'verifying-reopened-workspace'
            # The new UI owns the pipe. Check the original engine identity for
            # longer than the desktop's six-second stalled-audio threshold,
            # without competing for its connection or restarting anything.
            $watch = [Diagnostics.Stopwatch]::StartNew()
            do {
                AssertEngineUnchanged; AssertSavedWorkspaceUnchanged
                Assert-UiActivationInventoryState $state.Inventory Candidate
                $uis = @(Get-Process -Name VineMix,AudioMixer -ErrorAction SilentlyContinue)
                try {
                    if ($uis.Count -ne 1 -or -not (Test-ActivationProcessIdentity $uis[0] $state.NewUiIdentity) -or
                        -not $uis[0].Responding) { throw 'The candidate desktop exited or changed after shortcut launch.' }
                } finally { foreach ($ui in $uis) { $ui.Dispose() } }
                Start-Sleep -Milliseconds 250
            } while ($watch.ElapsedMilliseconds -lt 8000)
            AssertEngineUnchanged; AssertSavedWorkspaceUnchanged
            Assert-UiActivationPreservedWorkspaces $state.DataBackups
            if ((Hash $uiPath) -cne $expectedUi) { throw 'The installed desktop changed after reopening.' }
        }
        StopCandidateUi = {
            $result.stage = 'recovery-stopping-controller'
            # The launch path releases the update guard. Reacquire it before
            # any recovery so a newly prepared update cannot race restoration.
            HoldUpdateGuard
            AssertNoUpdate
            $uis = @(Get-Process -Name VineMix,AudioMixer -ErrorAction SilentlyContinue)
            try {
                foreach ($ui in $uis) {
                    if (-not $result.replacementAttempted -and (Test-ActivationProcessIdentity $ui $oldUiIdentity)) {
                        $state.OriginalStillRunning = $true; continue
                    }
                    if (-not (Test-ActivationProcessIdentity $ui $state.NewUiIdentity)) {
                        throw 'A different desktop appeared. UI-only rollback will not stop it or overwrite its executable.'
                    }
                    $ui.Kill()
                    if (-not $ui.WaitForExit(5000)) { throw 'The exact replacement desktop did not stop for rollback.' }
                }
            } finally { foreach ($ui in $uis) { $ui.Dispose() } }
        }
        RestoreUi = {
            $result.stage = 'recovery-restoring-desktop'
            if (@(Get-Process -Name VineMix,AudioMixer -ErrorAction SilentlyContinue).Count) { throw 'A desktop remains active; rollback will not overwrite it.' }
            # Validate the companion recovery file before restoring either.
            # An independent inventory edit must not leave an old desktop
            # paired with a newer package inventory.
            Assert-UiActivationInventoryState $state.Inventory Recoverable
            Restore-UiActivationBinary $backupUi $uiPath $expectedOldUi $expectedUi { AssertReplacementProcessGuard } 5000
            $result.stage = 'recovery-restoring-inventory'
            Restore-UiActivationInventory $state.Inventory { AssertReplacementProcessGuard } 5000
        }
        VerifyRecovery = {
            $result.stage = 'recovery-verifying-original'
            AssertEngineUnchanged; AssertSettingsIdentity; AssertNoUpdate
            Assert-UiActivationInventoryState $state.Inventory Original
            if ((Hash $uiPath) -cne $expectedOldUi) { throw 'The original desktop was not restored safely; reopening was withheld.' }
        }
        ReopenOriginalUi = {
            $result.stage = 'recovery-opening-original'
            if (-not $state.OriginalStillRunning) { StartUi }
            else { ReleaseUpdateGuard }
            AssertEngineUnchanged; AssertSettingsIdentity
            $result.originalUiReopened = $true
        }
    }
    Invoke-UiActivationTransaction $actions $result
    if ($result.passed) {
        $result.installedUiHash = Hash $uiPath
        $result.installedEngineHash = Hash $enginePath
        $result.settingsHashBefore = $state.SettingsHash
        $result.settingsHashAfter = Hash $settingsPath
        $result.physicalSettingsPath = $physicalSettings
        $result.sameEngineProcessStartTimePathAndHash = $true
        $result.sameConfigurationBeforeUiReopen = $true
        $result.frameCounterAdvancingBeforeUiReopen = $true
        $result.originalRunningPathsPreservedBeforeUiReopen = $true
        $result.settingsAndPreferencesUnchanged = $true
        $result.installedInventoryConsistent = $true
        $result.engineObservedUnchangedAfterUiReopenSeconds = 8
    }
} catch { $result.passed = $false; $result.error = $_.Exception.ToString(); $result.scriptStackTrace = $_.ScriptStackTrace; $result.failureStage = $result.stage }
finally {
    ReleaseUpdateGuard
    if ($oldUi) { $oldUi.Dispose() }
    if ($oldEngine) { $oldEngine.Dispose() }
    $result.finished = [DateTimeOffset]::Now.ToString('o')
    SaveEvidence 'result.json' $result
}
$result | ConvertTo-Json -Depth 12
if (-not $result.passed) { exit 1 }
