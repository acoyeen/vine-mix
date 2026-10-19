#requires -Version 7.3
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CandidateDirectory,
    [Parameter(Mandatory)][string]$EvidenceDirectory,
    [Parameter(Mandatory)][ValidatePattern('^[A-Fa-f0-9]{64}$')][string]$ExpectedEngineHash,
    [Parameter(Mandatory)][ValidatePattern('^[A-Fa-f0-9]{64}$')][string]$ExpectedUiHash
)
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$package = Join-Path $project 'artifacts\AudioMixer'
$candidate = [IO.Path]::GetFullPath($CandidateDirectory)
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$uiPath = Join-Path $package 'AudioMixer.exe'
$enginePath = Join-Path $package 'AudioMixer.Engine.exe'
$candidateEngine = Join-Path $candidate 'AudioMixer.Engine.exe'
$candidateUi = Join-Path $candidate 'AudioMixer.exe'
$settingsPath = Join-Path $env:LOCALAPPDATA 'AudioMixer\settings.json'
$backupEngine = Join-Path $evidence 'AudioMixer.Engine.before.exe'
$backupUi = Join-Path $evidence 'AudioMixer.before.exe'
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
    passed = $false
    rolledBack = $false
    settingsRestoredFromBackup = $false
}
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
try { $sid = $identity.User.Value } finally { $identity.Dispose() }

function Get-ExactProcess([string]$Name, [string]$Path) {
    @(Get-Process -Name $Name -ErrorAction SilentlyContinue | Where-Object { $_.Path -ieq $Path })
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
    $pipe = [IO.Pipes.NamedPipeClientStream]::new('.', ('AudioMixer.Control.' + $sid),
        [IO.Pipes.PipeDirection]::InOut, [IO.Pipes.PipeOptions]::Asynchronous)
    $reader = $null
    try {
        $pipe.ConnectAsync($TimeoutMilliseconds, $deadline.Token).GetAwaiter().GetResult()
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
    foreach ($runtime in @('libc++.dll', 'libunwind.dll', 'libwinpthread-1.dll')) {
        if ((FileHash (Join-Path $candidate $runtime)) -cne (FileHash (Join-Path $package $runtime))) {
            throw "Candidate runtime differs from the installed package: $runtime. This update preserves the installed runtime libraries."
        }
    }
}
function Start-PackagedEngine([string]$LogPrefix) {
    Start-Process -FilePath $enginePath -WorkingDirectory $package -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $evidence ($LogPrefix + '-stdout.log')) `
        -RedirectStandardError (Join-Path $evidence ($LogPrefix + '-stderr.log'))
}
function Start-PackagedUi {
    # The user requested reopening the interactive application.
    $process = Start-Process -FilePath $uiPath -WorkingDirectory $package -PassThru
    if (-not $process.WaitForInputIdle(15000)) { throw 'The controller did not become idle within 15 seconds.' }
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do {
        $process.Refresh()
        if ($process.HasExited) { throw 'The controller exited before opening its window.' }
        if ($process.MainWindowHandle -ne 0 -and $process.Responding) { return $process }
        Start-Sleep -Milliseconds 100
    } while ($timer.ElapsedMilliseconds -lt 5000)
    throw 'The controller did not open a responsive window.'
}

$controllerStopped = $false
$shutdownRequested = $false
$replacementAttempted = $false
$oldEngineHash = $null
$settingsHash = $null
$newEngine = $null
$newUi = $null
try {
    VerifyCandidate
    foreach ($path in @($uiPath, $enginePath, $settingsPath)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required file is missing: $path" }
    }
    $allProcesses = @(Get-Process -Name AudioMixer, AudioMixer.Engine -ErrorAction SilentlyContinue)
    if (@($allProcesses | Where-Object { $_.Path -ine $uiPath -and $_.Path -ine $enginePath }).Count) {
        throw 'An Vine Mix process is running from another package. No process was stopped.'
    }
    $oldUi = @(Get-ExactProcess 'AudioMixer' $uiPath)
    $oldEngine = @(Get-ExactProcess 'AudioMixer.Engine' $enginePath)
    if ($oldUi.Count -ne 1 -or $oldEngine.Count -ne 1) { throw 'Expected exactly one installed controller and one installed engine.' }
    $overridePath = $env:AUDIOMIXER_ENGINE_PATH
    if ($overridePath -and (Test-Path -LiteralPath $overridePath) -and [IO.Path]::GetFullPath($overridePath) -ine $enginePath) {
        throw 'AUDIOMIXER_ENGINE_PATH points to a different engine; reopening would supervise another package.'
    }
    $result.oldUiId = $oldUi[0].Id
    $result.oldEngineId = $oldEngine[0].Id
    $result.originalUiHash = FileHash $uiPath
    $quietHash = FileHash $settingsPath
    Start-Sleep -Milliseconds 1500
    if ((FileHash $settingsPath) -cne $quietHash) { throw 'Settings changed during the quiet interval. Retry after the current edit finishes.' }

    # Window close hides the UI. There is no quit IPC, and the tray is unavailable
    # to this script. Release its sole pipe connection and stop engine supervision.
    $oldUi[0].Kill()
    $controllerStopped = $true
    if (-not $oldUi[0].WaitForExit(5000)) { throw 'The exact controller process did not stop.' }
    $before = (Request 'snapshot').snapshot
    WriteEvidence 'before.json' $before
    if ([IO.Path]::GetFullPath($before.settingsPath) -ine $settingsPath) { throw 'The running engine uses an unexpected settings file.' }
    $settingsHash = FileHash $settingsPath
    Copy-Item -LiteralPath $settingsPath -Destination (Join-Path $evidence 'settings-before.json')
    if ((FileHash (Join-Path $evidence 'settings-before.json')) -cne $settingsHash) { throw 'Settings changed while creating their backup.' }
    foreach ($file in @('settings.json.bak', 'ui.json')) {
        $source = Join-Path (Split-Path -Parent $settingsPath) $file
        if (Test-Path -LiteralPath $source -PathType Leaf) { Copy-Item -LiteralPath $source -Destination (Join-Path $evidence $file) }
    }
    $oldEngineHash = FileHash $enginePath
    Copy-Item -LiteralPath $uiPath -Destination $backupUi
    if ((FileHash $backupUi) -cne $result.originalUiHash) { throw 'Desktop backup hash differs.' }
    Copy-Item -LiteralPath $enginePath -Destination $backupEngine
    if ((FileHash $backupEngine) -cne $oldEngineHash) { throw 'The engine backup does not match the installed binary.' }
    $result.originalEngineHash = $oldEngineHash
    $result.settingsHashBefore = $settingsHash
    $result.revisionBefore = $before.configuration.revision
    $beforeConfig = Canonical $before.configuration
    # Application capture has an app binding rather than a device binding.
    # Require every previously running path, including applications, to recover.
    $wasRunning = @($before.status.nodes | Where-Object { $_.path.state -eq 'Running' } | ForEach-Object id)
    $result.previouslyRunningNodeIds = $wasRunning

    # Recheck the staged artifacts immediately before the interruption/copy.
    VerifyCandidate
    $shutdownRequested = $true
    try { $null = Request 'shutdown' } catch {
        $result.shutdownReplyWarning = $_.Exception.Message
    }
    if (-not $oldEngine[0].WaitForExit(5000)) { throw 'The exact engine process did not exit gracefully.' }
    if (@(Get-ExactProcess 'AudioMixer' $uiPath).Count -or @(Get-ExactProcess 'AudioMixer.Engine' $enginePath).Count) {
        throw 'The installed controller or engine restarted before replacement.'
    }
    $replacementAttempted = $true
    Copy-Item -LiteralPath $candidateEngine -Destination $enginePath -Force
    Copy-Item -LiteralPath $candidateUi -Destination $uiPath -Force
    if ((FileHash $uiPath) -cne $expectedUiHash) { throw 'Installed desktop hash differs from verified candidate.' }
    if ((FileHash $enginePath) -cne $expectedHash) { throw 'The installed engine does not match the verified candidate.' }
    $newEngine = Start-PackagedEngine 'candidate-engine'
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
        if ((Canonical $after.configuration) -cne $beforeConfig) { throw 'Configuration changed across the engine restart.' }
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
    $newUi = Start-PackagedUi
    $result.newUiId = $newUi.Id
    $result.windowTitle = $newUi.MainWindowTitle
    $result.windowHandle = $newUi.MainWindowHandle.ToInt64()
    # The UI owns the pipe now; verify persistence without competing for it.
    Start-Sleep -Milliseconds 300
    if ((FileHash $settingsPath) -cne $settingsHash) { throw 'Saved settings changed when reopening the controller.' }
    $liveEngines = @(Get-ExactProcess 'AudioMixer.Engine' $enginePath)
    if ($liveEngines.Count -ne 1 -or $liveEngines[0].Id -ne $newEngine.Id -or $newEngine.HasExited) {
        throw 'The candidate engine changed or exited after reopening the controller.'
    }
    $copiedDocuments = @()
    foreach ($relative in @('README.md', 'TESTING.md', 'THIRD_PARTY_NOTICES.md', 'docs\APP_AUDIO.md', 'docs\DEVICE_VISIBILITY.md')) {
        $source = Join-Path $candidate $relative
        if (Test-Path -LiteralPath $source -PathType Leaf) {
            $destination = Join-Path $package $relative
            $null = New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force
            Copy-Item -LiteralPath $source -Destination $destination -Force
            $copiedDocuments += $relative
        }
    }
    $result.copiedDocuments = $copiedDocuments
    $result.installedEngineHash = FileHash $enginePath
    $result.installedUiHash = FileHash $uiPath
    $result.settingsHashAfter = FileHash $settingsPath
    $result.sameConfiguration = $true
    $result.settingsFileUnchanged = $true
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
                foreach ($process in @(Get-ExactProcess 'AudioMixer' $uiPath)) {
                    $process.Kill()
                    if (-not $process.WaitForExit(5000)) { throw 'The replacement controller did not stop for rollback.' }
                }
                $engines = @(Get-ExactProcess 'AudioMixer.Engine' $enginePath)
                if ($engines.Count) { try { $null = Request 'shutdown' } catch { } }
                foreach ($process in $engines) {
                    if (-not $process.WaitForExit(5000)) {
                        $process.Kill()
                        if (-not $process.WaitForExit(5000)) { throw 'The replacement engine did not stop for rollback.' }
                    }
                }
                if (@(Get-ExactProcess 'AudioMixer' $uiPath).Count -or @(Get-ExactProcess 'AudioMixer.Engine' $enginePath).Count) {
                    throw 'A packaged process remains active; rollback will not overwrite a running executable.'
                }
            } catch { $safeToRestore = $false; $recoveryErrors.Add($_.Exception.Message) }
            if ($safeToRestore) {
                try {
                    if ((FileHash $backupEngine) -cne $oldEngineHash) { throw 'The original engine backup hash no longer matches.' }
                    if ((FileHash $backupUi) -cne $result.originalUiHash) { throw 'Original desktop backup hash differs.' }
                    Copy-Item -LiteralPath $backupEngine -Destination $enginePath -Force
                    Copy-Item -LiteralPath $backupUi -Destination $uiPath -Force
                    if ((FileHash $uiPath) -cne $result.originalUiHash) { throw 'Restored desktop hash differs.' }
                    if ((FileHash $enginePath) -cne $oldEngineHash) { throw 'The restored engine hash does not match the original.' }
                    $result.rolledBack = $true
                } catch { $recoveryErrors.Add('Restore engine: ' + $_.Exception.Message) }
            }
        }
        $canRestart = -not $replacementAttempted -or $result.rolledBack
        if ($canRestart) {
            try {
                if ($shutdownRequested -and -not @(Get-ExactProcess 'AudioMixer.Engine' $enginePath).Count) {
                    $recoveredEngine = Start-PackagedEngine 'recovered-engine'
                    $result.recoveredEngineId = $recoveredEngine.Id
                }
            } catch { $recoveryErrors.Add('Restart original engine: ' + $_.Exception.Message) }
            try {
                if (-not @(Get-ExactProcess 'AudioMixer' $uiPath).Count) {
                    $recoveredUi = Start-PackagedUi
                    $result.recoveredUiId = $recoveredUi.Id
                    $result.recoveredUiResponsive = $true
                }
            } catch { $recoveryErrors.Add('Reopen original controller: ' + $_.Exception.Message) }
        } else { $recoveryErrors.Add('Automatic restart was withheld because the original engine could not be safely restored.') }
    }
    $result.recoveryErrors = @($recoveryErrors)
} finally {
    $result.finished = [DateTimeOffset]::Now.ToString('o')
    WriteEvidence 'result.json' $result
}
$result | ConvertTo-Json -Depth 20
if (-not $result.passed) { exit 1 }
