#requires -Version 7.3
[CmdletBinding()]
param([string]$ResultDirectory = '')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'assert-desktop-data-context.ps1')
. (Join-Path $PSScriptRoot 'local-activation-support.ps1')
. (Join-Path $PSScriptRoot 'local-ui-activation-support.ps1')
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $ResultDirectory) { $ResultDirectory = Join-Path $env:LOCALAPPDATA ('VineMix.BuildCache\ui-activation-verification-' + [guid]::NewGuid().ToString('N')) }
$root = [IO.Path]::GetFullPath($ResultDirectory)
Assert-ActivationEvidenceLocation $root @($project)
if (Test-Path -LiteralPath $root) { throw 'Choose a new verification directory.' }
$null = New-Item -ItemType Directory -Path $root
Initialize-DesktopDataContext
$locator = Join-Path $root 'fixture-location.txt'
'Isolated UI activation test' | Set-Content -LiteralPath $locator -Encoding utf8
$root = Split-Path -Parent (Get-ActivationPhysicalPath $locator)
Assert-ActivationEvidenceLocation $root @($project)
$checks = [Collections.Generic.List[string]]::new()
$passed = $false
function Check([bool]$Condition, [string]$Description) {
    if (-not $Condition) { throw $Description }
    $checks.Add($Description)
}
function Reject([scriptblock]$Action, [string]$Description) {
    $rejected = $false
    try { & $Action } catch { $rejected = $true }
    Check $rejected $Description
}
function Clone($Value) { $Value | ConvertTo-Json -Depth 20 | ConvertFrom-Json -Depth 20 }
function FileHash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }
try {
    Assert-UiActivationRelease '0.3.0-beta.3.1-local+source' '0.3.0-beta.3.1-local+previous' '0.3.0-beta.3.1-local'
    Check $true 'Source metadata may differ while the UI and unchanged engine share the exact same release.'
    Reject { Assert-UiActivationRelease '0.3.0-beta.4' '0.3.0-beta.3.1-local' '0.3.0-beta.3.1-local' } 'A changed candidate release cannot be activated over the running engine.'
    Reject { Assert-UiActivationRelease '0.3.0-beta.3.1-local' '0.3.0-beta.3' '0.3.0-beta.3.1-local' } 'An inconsistent original desktop and engine is rejected.'
    Reject { Assert-UiActivationRelease '' '' '' } 'Missing release identity is rejected.'

    $before = [pscustomobject]@{configuration=[pscustomobject]@{revision=71;graph=[pscustomobject]@{nodes=@('example-input','example-output');gain=0.5}};status=[pscustomobject]@{audioFrames=100000;nodes=@([pscustomobject]@{id='example-output';path=[pscustomobject]@{state='Running'}},[pscustomobject]@{id='example-unavailable';path=[pscustomobject]@{state='Unavailable'}})}}
    $after = Clone $before
    $after.status.audioFrames = 105000
    Assert-UiActivationSnapshotStable $before $after
    Check $true 'Unchanged graph and advancing audio retain existing running paths without requiring unavailable devices.'
    $reordered = Clone $after
    $reordered.configuration = [pscustomobject]@{graph=$reordered.configuration.graph;revision=71}
    Assert-UiActivationSnapshotStable $before $reordered
    Check $true 'Snapshot comparison is independent of JSON object property order.'
    $bad = Clone $after; $bad.configuration.revision = 72
    Reject { Assert-UiActivationSnapshotStable $before $bad } 'A newer engine configuration is never silently accepted or restored.'
    $bad = Clone $after; $bad.configuration.graph.gain = 0.7
    Reject { Assert-UiActivationSnapshotStable $before $bad } 'A sound-control change is detected even if revision is unchanged.'
    $bad = Clone $after; $bad.status.audioFrames = 100000
    Reject { Assert-UiActivationSnapshotStable $before $bad } 'A stalled frame counter fails readiness.'
    $bad.status.audioFrames = 4
    Reject { Assert-UiActivationSnapshotStable $before $bad } 'A reset frame counter cannot pass UI-only continuity.'
    $bad = Clone $after; $bad.status.nodes[0].path.state = 'Unavailable'
    Reject { Assert-UiActivationSnapshotStable $before $bad } 'Loss of a previously running audio path fails readiness.'

    $updates = Join-Path $root 'updates'
    Assert-UiActivationUpdateHistory $updates
    $transaction = Join-Path $updates ([guid]::NewGuid().ToString('N'))
    $null = New-Item -ItemType Directory -Path $transaction -Force
    $journal = Join-Path $transaction 'journal.json'
    foreach ($phase in 0..13) {
        @{phase=$phase} | ConvertTo-Json | Set-Content -LiteralPath $journal -Encoding utf8
        if ($phase -in @(9,11,12)) {
            Assert-UiActivationUpdateHistory $updates
            Check $true ('Terminal update phase ' + $phase + ' permits activation without changing history.')
        } else { Reject { Assert-UiActivationUpdateHistory $updates } ('Unfinished update phase ' + $phase + ' prevents activation.') }
    }
    '{broken' | Set-Content -LiteralPath $journal -Encoding utf8
    Reject { Assert-UiActivationUpdateHistory $updates } 'Malformed update history fails closed.'
    Remove-Item -LiteralPath $journal
    Reject { Assert-UiActivationUpdateHistory $updates } 'Missing update history fails closed.'
    '{"phase":9}' | Set-Content -LiteralPath $journal -Encoding utf8
    $null = New-Item -ItemType Directory -Path (Join-Path $updates '.preparing-example')
    Reject { Assert-UiActivationUpdateHistory $updates } 'A preparing update blocks activation before it acquires an update worker.'

    $forward = @('VerifyBeforeStop','StopOriginalUi','CaptureBaseline','VerifyBeforeReplace','InstallUi','VerifyBeforeLaunch','StartUi','VerifyAfterLaunch')
    $recovery = @('StopCandidateUi','RestoreUi','VerifyRecovery','ReopenOriginalUi')
    foreach ($failurePoint in @('none') + $forward) {
        $trace = [Collections.Generic.List[string]]::new()
        $actions = @{}
        foreach ($name in $forward + $recovery) {
            $stepName = $name
            $injectedFailure = $failurePoint
            $recordedTrace = $trace
            $actions[$name] = { $recordedTrace.Add($stepName); if ($stepName -eq $injectedFailure) { throw ('Injected failure: ' + $stepName) } }.GetNewClosure()
        }
        $outcome = [ordered]@{}
        Invoke-UiActivationTransaction $actions $outcome
        if ($failurePoint -eq 'none') {
            Check ($outcome.passed -and ($trace -join ',') -ceq ($forward -join ',')) 'Success changes UI once, probes audio before launch, and performs no rollback.'
        } elseif ($failurePoint -eq 'VerifyBeforeStop') {
            Check (-not $outcome.passed -and $trace.Count -eq 1 -and -not $outcome.controllerStopAttempted) 'Failed preflight leaves the original desktop and engine untouched.'
        } else {
            $expectedRecovery = if ($forward.IndexOf($failurePoint) -ge $forward.IndexOf('InstallUi')) { $recovery } else { @('StopCandidateUi','VerifyRecovery','ReopenOriginalUi') }
            $tail = @($trace | Select-Object -Last $expectedRecovery.Count)
            Check (-not $outcome.passed -and ($tail -join ',') -ceq ($expectedRecovery -join ',')) ('Failure at ' + $failurePoint + ' restores only the UI when required and verifies engine identity before reopening.')
        }
    }
    foreach ($rollbackFailure in $recovery) {
        $trace = [Collections.Generic.List[string]]::new()
        $actions = @{}
        foreach ($name in $forward + $recovery) {
            $stepName = $name; $injectedRecoveryFailure = $rollbackFailure; $recordedTrace = $trace
            $actions[$name] = {
                $recordedTrace.Add($stepName)
                if ($stepName -eq 'VerifyAfterLaunch' -or $stepName -eq $injectedRecoveryFailure) { throw ('Injected failure: ' + $stepName) }
            }.GetNewClosure()
        }
        $outcome = [ordered]@{}
        Invoke-UiActivationTransaction $actions $outcome
        Check (-not $outcome.passed -and $outcome.recoveryErrors.Count -eq 1 -and $trace[$trace.Count-1] -eq $rollbackFailure) ('Rollback failure at ' + $rollbackFailure + ' stops recovery instead of continuing with an unverified UI or engine.')
    }

    $target = Join-Path $root 'installed-ui.bin'
    $backup = Join-Path $root 'backup-ui.bin'
    $candidateFile = Join-Path $root 'candidate-ui.bin'
    $engineFile = Join-Path $root 'untouched-engine.bin'
    $workspace = Join-Path $root 'workspace.json'
    'original-ui' | Set-Content -LiteralPath $target -Encoding utf8
    Copy-Item -LiteralPath $target -Destination $backup
    'candidate-ui' | Set-Content -LiteralPath $candidateFile -Encoding utf8
    'original-running-engine' | Set-Content -LiteralPath $engineFile -Encoding utf8
    '{"revision":71}' | Set-Content -LiteralPath $workspace -Encoding utf8
    $originalHash = FileHash $target; $candidateHash = FileHash $candidateFile; $engineHash = FileHash $engineFile
    $actions = @{}
    foreach ($name in $forward + $recovery) { $actions[$name] = { } }
    $actions.InstallUi = { Replace-ActivationBinary $candidateFile $target $candidateHash }
    $actions.VerifyAfterLaunch = { '{"revision":72}' | Set-Content -LiteralPath $workspace -Encoding utf8; throw 'A newer workspace appeared.' }
    $actions.RestoreUi = { Restore-UiActivationBinary $backup $target $originalHash $candidateHash }
    $actions.VerifyRecovery = { if ((FileHash $engineFile) -cne $engineHash) { throw 'Engine changed.' } }
    $outcome = [ordered]@{}
    Invoke-UiActivationTransaction $actions $outcome
    Check (-not $outcome.passed -and $outcome.rolledBack -and (FileHash $target) -ceq $originalHash) 'A post-launch failure restores the exact original UI bytes.'
    Check ((Get-Content -LiteralPath $workspace -Raw).Contains('72') -and (FileHash $engineFile) -ceq $engineHash) 'Rollback preserves newer workspace contents and the untouched engine bytes.'
    'independent-ui' | Set-Content -LiteralPath $target -Encoding utf8
    $independentHash = FileHash $target
    Reject { Restore-UiActivationBinary $backup $target $originalHash $candidateHash } 'Rollback refuses an independently changed desktop executable.'
    Check ((FileHash $target) -ceq $independentHash) 'An independent desktop edit is retained after rollback refusal.'
    Copy-Item -LiteralPath $candidateFile -Destination $target
    'damaged-backup' | Set-Content -LiteralPath $backup -Encoding utf8
    Reject { Restore-UiActivationBinary $backup $target $originalHash $candidateHash } 'Rollback refuses an altered backup.'
    Check ((FileHash $target) -ceq $candidateHash) 'A damaged recovery copy cannot overwrite the verified candidate.'

    # Exercise real Windows rename locks in temporary files. The delay runs in
    # a managed task; no controller, engine or desktop input is involved.
    Add-Type -TypeDefinition @'
public static class UiActivationFixtureLock {
    public static System.Threading.Tasks.Task Hold(string path, int milliseconds) {
        var stream = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read,
            System.IO.FileShare.ReadWrite);
        return System.Threading.Tasks.Task.Delay(milliseconds).ContinueWith(_ => stream.Dispose());
    }
}
'@
    'original-ui' | Set-Content -LiteralPath $target -Encoding utf8
    Copy-Item -LiteralPath $target -Destination $backup
    $retryState = @{attempts=0}
    $held = [UiActivationFixtureLock]::Hold($target, 400)
    Replace-ActivationBinary $candidateFile $target $candidateHash @($originalHash) { $retryState.attempts++ } 2000
    $held.GetAwaiter().GetResult()
    Check ($retryState.attempts -gt 1 -and (FileHash $target) -ceq $candidateHash) 'A transient Windows rename lock is retried within a bounded interval after rechecking the process and file guards.'
    $retryState.attempts = 0
    $held = [UiActivationFixtureLock]::Hold($target, 400)
    Restore-UiActivationBinary $backup $target $originalHash $candidateHash { $retryState.attempts++ } 2000
    $held.GetAwaiter().GetResult()
    Check ($retryState.attempts -gt 1 -and (FileHash $target) -ceq $originalHash) 'Rollback uses the same bounded guarded retry for transient file locks.'
    $held = [UiActivationFixtureLock]::Hold($target, 600)
    $timeout = [Diagnostics.Stopwatch]::StartNew()
    Reject { Replace-ActivationBinary $candidateFile $target $candidateHash @($originalHash) { } 100 } 'A lock that outlasts the retry deadline reports failure instead of waiting indefinitely.'
    $held.GetAwaiter().GetResult()
    Check ($timeout.ElapsedMilliseconds -lt 3000 -and (FileHash $target) -ceq $originalHash) 'A timed-out rename leaves the original destination bytes intact.'
    $retryState.attempts = 0
    $held = [UiActivationFixtureLock]::Hold($target, 600)
    Reject { Replace-ActivationBinary $candidateFile $target $candidateHash @($originalHash) {
        $retryState.attempts++
        if ($retryState.attempts -gt 1) {
            $writer = [IO.FileStream]::new($target, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::ReadWrite)
            try { $writer.Write([Text.Encoding]::UTF8.GetBytes('independent-destination')) } finally { $writer.Dispose() }
        }
    } 2000 } 'A destination changed between rename attempts is never overwritten.'
    $held.GetAwaiter().GetResult()
    Check ((Get-Content -LiteralPath $target -Raw).Contains('independent-destination')) 'A refused retry preserves the independent destination edit.'
    Copy-Item -LiteralPath $backup -Destination $target
    $retryState.attempts = 0
    $held = [UiActivationFixtureLock]::Hold($target, 600)
    Reject { Replace-ActivationBinary $candidateFile $target $candidateHash @($originalHash) {
        $retryState.attempts++
        if ($retryState.attempts -gt 1) { throw 'An unrelated controller appeared.' }
    } 2000 } 'A process guard refusing a later attempt stops retry before any replacement.'
    $held.GetAwaiter().GetResult()
    Check ((FileHash $target) -ceq $originalHash) 'A refused process guard leaves the original desktop bytes intact.'
    $retryState.attempts = 0
    $held = [UiActivationFixtureLock]::Hold($target, 600)
    Reject { Replace-ActivationBinary $candidateFile $target $candidateHash @($originalHash) {
        $retryState.attempts++
        if ($retryState.attempts -gt 1) { 'independent-source' | Set-Content -LiteralPath $candidateFile -Encoding utf8 }
    } 2000 } 'A source changed between rename attempts is rejected even when the staged copy is unchanged.'
    $held.GetAwaiter().GetResult()
    Check ((FileHash $target) -ceq $originalHash) 'A changed replacement source cannot alter the original desktop.'
    'candidate-ui' | Set-Content -LiteralPath $candidateFile -Encoding utf8
    Reject { Replace-ActivationBinary $candidateFile $target $candidateHash @($originalHash) $null 100 } 'Retries cannot be enabled without an explicit process guard.'

    $package = Join-Path $root 'inventory-package'
    $support = Join-Path $package 'SetupSupport'
    $null = New-Item -ItemType Directory -Path $support -Force
    $installedUi = Join-Path $package 'VineMix.exe'
    $installedEngine = Join-Path $package 'VineMix.Engine.exe'
    $inventoryPath = Join-Path $support 'package-manifest.json'
    'original-inventoried-ui' | Set-Content -LiteralPath $installedUi -Encoding utf8
    Copy-Item -LiteralPath $engineFile -Destination $installedEngine
    $installedUiBackup = Join-Path $root 'inventoried-ui.before'
    Copy-Item -LiteralPath $installedUi -Destination $installedUiBackup
    $installedUiHash = FileHash $installedUi
    $noInventory = New-UiActivationInventoryPlan $package $root $candidateFile $installedUiHash $candidateHash 'example-version'
    Install-UiActivationInventory $noInventory
    Restore-UiActivationInventory $noInventory
    Check (-not $noInventory.Present -and -not (Test-Path -LiteralPath $inventoryPath)) 'A development package without an installed inventory keeps the original UI-only behavior.'
    '{}' | Set-Content -LiteralPath $inventoryPath -Encoding utf8
    Reject { Assert-UiActivationInventoryState $noInventory Original } 'An independently appearing inventory is preserved and blocks replacement.'
    $inventory = [ordered]@{
        version='example-version';numericVersion='1.2.3.4';sourceSnapshotSha256=('b'*64);gitDirty=$false
        extra=[ordered]@{name='Example fixture';enabled=$true;values=@(1,2,3)}
        files=@(
            [ordered]@{path='VineMix.exe';size=(Get-Item -LiteralPath $installedUi).Length;sha256=$installedUiHash;custom='retain-desktop-metadata'},
            [ordered]@{path='VineMix.Engine.exe';size=(Get-Item -LiteralPath $installedEngine).Length;sha256=$engineHash;custom='retain-engine-metadata'})
    }
    function ResetInventory {
        Copy-Item -LiteralPath $installedUiBackup -Destination $installedUi
        $inventory | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $inventoryPath -Encoding utf8
    }
    function InventoryPlan {
        $privateEvidence = Join-Path $root ('inventory-evidence-' + [guid]::NewGuid().ToString('N'))
        $null = New-Item -ItemType Directory -Path $privateEvidence
        New-UiActivationInventoryPlan $package $privateEvidence $candidateFile $installedUiHash $candidateHash 'example-version'
    }
    ResetInventory
    $inventoryOriginalHash = FileHash $inventoryPath
    $plan = InventoryPlan
    $stagedInventory = Get-Content -LiteralPath $plan.Candidate -Raw | ConvertFrom-Json -Depth 20
    Check ($plan.Present -and $plan.OriginalHash -ceq $inventoryOriginalHash -and (FileHash $plan.Backup) -ceq $inventoryOriginalHash) 'Installed inventory preparation retains a byte-identical private recovery copy.'
    Check ($stagedInventory.files[0].size -eq (Get-Item -LiteralPath $candidateFile).Length -and $stagedInventory.files[0].sha256 -ceq $candidateHash) 'The staged inventory records the reviewed candidate desktop size and hash.'
    $expectedInventory = Clone $inventory
    $expectedInventory.files[0].size = (Get-Item -LiteralPath $candidateFile).Length
    $expectedInventory.files[0].sha256 = $candidateHash
    Check ((ConvertTo-UiActivationCanonical $stagedInventory) -ceq (ConvertTo-UiActivationCanonical $expectedInventory)) 'Only the desktop size and hash change; release metadata, unknown properties and other payload entries are preserved.'
    Check ((FileHash $installedUi) -ceq $installedUiHash -and (FileHash $inventoryPath) -ceq $inventoryOriginalHash) 'Inventory preparation does not mutate the installed desktop or inventory.'
    foreach ($invalid in @('wrong-release','wrong-ui-hash','wrong-size','missing-engine','duplicate-path','traversal','absolute-path','backslash-path')) {
        $brokenInventory = Clone $inventory
        switch ($invalid) {
            'wrong-release' { $brokenInventory.version = 'another-version' }
            'wrong-ui-hash' { $brokenInventory.files[0].sha256 = 'a'*64 }
            'wrong-size' { $brokenInventory.files[0].size++ }
            'missing-engine' { $brokenInventory.files = @($brokenInventory.files[0]) }
            'duplicate-path' { $brokenInventory.files[1].path = 'VINEMIX.EXE' }
            'traversal' { $brokenInventory.files[1].path = '../untouched-engine.bin' }
            'absolute-path' { $brokenInventory.files[1].path = $installedEngine }
            'backslash-path' { $brokenInventory.files[1].path = 'directory\VineMix.Engine.exe' }
        }
        $brokenInventory | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $inventoryPath -Encoding utf8
        Reject { InventoryPlan } ('Invalid installed inventory is rejected before replacement: ' + $invalid + '.')
    }
    ResetInventory
    Reject { New-UiActivationInventoryPlan $package $root $candidateFile ('c'*64) $candidateHash 'example-version' } 'An inventory matching the file still cannot bypass the reviewed original desktop hash.'
    Reject { New-UiActivationInventoryPlan $package $root $candidateFile $installedUiHash ('c'*64) 'example-version' } 'The candidate hash is independently verified before preparing its inventory.'
    foreach ($failurePoint in @('none','manifest-write','post-launch','independent-inventory','independent-ui','damaged-inventory-backup')) {
        ResetInventory
        $plan = InventoryPlan
        $actions = @{}
        foreach ($name in $forward + $recovery) { $actions[$name] = { } }
        $actions.InstallUi = {
            Assert-UiActivationInventoryState $plan Original
            Replace-ActivationBinary $candidateFile $installedUi $candidateHash
            if ($failurePoint -eq 'manifest-write') { 'damaged-stage' | Set-Content -LiteralPath $plan.Candidate -Encoding utf8 }
            Install-UiActivationInventory $plan
        }
        $actions.VerifyBeforeLaunch = {
            Assert-UiActivationInventoryState $plan Candidate
            $currentInventory = Get-Content -LiteralPath $inventoryPath -Raw | ConvertFrom-Json
            if ($currentInventory.files[0].sha256 -cne (FileHash $installedUi) -or
                $currentInventory.files[0].size -ne (Get-Item -LiteralPath $installedUi).Length) { throw 'Mismatched inventory before launch.' }
        }
        $actions.VerifyAfterLaunch = {
            if ($failurePoint -eq 'none') { return }
            '{"revision":73}' | Set-Content -LiteralPath $workspace -Encoding utf8
            if ($failurePoint -eq 'independent-inventory') { '{"independent":true}' | Set-Content -LiteralPath $inventoryPath -Encoding utf8 }
            if ($failurePoint -eq 'independent-ui') { 'independent-ui' | Set-Content -LiteralPath $installedUi -Encoding utf8 }
            if ($failurePoint -eq 'damaged-inventory-backup') { 'damaged-backup' | Set-Content -LiteralPath $plan.Backup -Encoding utf8 }
            throw 'Injected failure after UI launch.'
        }
        $actions.RestoreUi = {
            Assert-UiActivationInventoryState $plan Recoverable
            Restore-UiActivationBinary $installedUiBackup $installedUi $installedUiHash $candidateHash
            Restore-UiActivationInventory $plan
        }
        $actions.VerifyRecovery = { Assert-UiActivationInventoryState $plan Original }
        $outcome = [ordered]@{}
        Invoke-UiActivationTransaction $actions $outcome
        switch ($failurePoint) {
            'none' {
                Check ($outcome.passed -and (FileHash $installedUi) -ceq $candidateHash -and (FileHash $inventoryPath) -ceq $plan.CandidateHash) 'Successful UI-only activation keeps the installed inventory consistent before relaunch.'
            }
            { $_ -in @('manifest-write','post-launch') } {
                Check (-not $outcome.passed -and $outcome.rolledBack -and (FileHash $installedUi) -ceq $installedUiHash -and
                    (FileHash $inventoryPath) -ceq $inventoryOriginalHash) ('Failure at ' + $failurePoint + ' restores both original UI and exact inventory bytes.')
            }
            'independent-inventory' {
                Check (-not $outcome.passed -and -not $outcome.rolledBack -and $outcome.recoveryErrors.Count -eq 1 -and
                    (FileHash $installedUi) -ceq $candidateHash -and (Get-Content -LiteralPath $inventoryPath -Raw).Contains('independent')) 'An independent inventory edit blocks both recovery writes and is never overwritten.'
            }
            'independent-ui' {
                Check (-not $outcome.passed -and -not $outcome.rolledBack -and $outcome.recoveryErrors.Count -eq 1 -and
                    (Get-Content -LiteralPath $installedUi -Raw).Contains('independent') -and (FileHash $inventoryPath) -ceq $plan.CandidateHash) 'An independent desktop edit blocks recovery before the inventory can be restored.'
            }
            'damaged-inventory-backup' {
                Check (-not $outcome.passed -and -not $outcome.rolledBack -and $outcome.recoveryErrors.Count -eq 1 -and
                    (FileHash $installedUi) -ceq $candidateHash -and (FileHash $inventoryPath) -ceq $plan.CandidateHash) 'An altered inventory backup prevents any paired recovery write.'
            }
        }
        Check ((FileHash $installedEngine) -ceq $engineHash) ('Inventory activation and recovery leave the engine unchanged: ' + $failurePoint + '.')
        if ($failurePoint -notin @('none','manifest-write')) {
            Check ((Get-Content -LiteralPath $workspace -Raw).Contains('73')) ('Inventory recovery preserves newer saved workspace contents: ' + $failurePoint + '.')
        }
    }

    $view = Join-Path $root 'preserved-view'
    $null = New-Item -ItemType Directory -Path $view
    $viewSettings = Join-Path $view 'settings.json'
    $viewUpdates = Join-Path $view 'updates.json'
    '{}' | Set-Content -LiteralPath $viewSettings -Encoding utf8
    '{}' | Set-Content -LiteralPath $viewUpdates -Encoding utf8
    $records = @([pscustomobject]@{kind='directory';source=$view;names=@('settings.json','updates.json')},
        [pscustomobject]@{kind='file';source=$viewSettings;physicalPath=(Get-ActivationPhysicalPath $viewSettings);sha256=(FileHash $viewSettings)},
        [pscustomobject]@{kind='file';source=$viewUpdates;physicalPath=(Get-ActivationPhysicalPath $viewUpdates);sha256=(FileHash $viewUpdates)})
    '{"lastCheck":"example"}' | Set-Content -LiteralPath $viewUpdates -Encoding utf8
    Assert-UiActivationPreservedWorkspaces $records
    Check $true 'Routine update metadata can change without weakening saved workspace preservation.'
    '{"revision":72}' | Set-Content -LiteralPath $viewSettings -Encoding utf8
    Reject { Assert-UiActivationPreservedWorkspaces $records } 'Changes in any retained ordinary or redirected workspace file are detected after reopening.'
    '{}' | Set-Content -LiteralPath (Join-Path $view 'settings.json.new') -Encoding utf8
    Reject { Assert-UiActivationPreservedWorkspaces $records } 'New saved workspace copies are detected after reopening.'

    $tokens = $null; $errors = $null
    $scriptPath = Join-Path $PSScriptRoot 'activate-local-ui.ps1'
    $ast = [Management.Automation.Language.Parser]::ParseFile($scriptPath,[ref]$tokens,[ref]$errors)
    Check ($errors.Count -eq 0) 'The production UI-only script parses without executing it.'
    $commands = @($ast.FindAll({param($node) $node -is [Management.Automation.Language.CommandAst]},$true))
    Check (@($commands | Where-Object { $_.GetCommandName() -in @('Start-ActivationEngine','Stop-Process','Start-PackagedEngine') }).Count -eq 0) 'The UI-only script has no engine launcher or generic process-stop command.'
    $strings = @($ast.FindAll({param($node) $node -is [Management.Automation.Language.StringConstantExpressionAst]},$true))
    Check (@($strings | Where-Object Value -in @('shutdown','configure','retry','recover')).Count -eq 0 -and
        @($strings | Where-Object { $_.Value -eq 'snapshot' -and $_.Parent -isnot [Management.Automation.Language.MemberExpressionAst] }).Count -eq 1) 'Snapshot is the sole literal engine command in the UI-only script.'
    $replacements = @($commands | Where-Object { $_.GetCommandName() -in @('Replace-ActivationBinary','Restore-UiActivationBinary') })
    Check ($replacements.Count -eq 2 -and @($replacements | Where-Object { $_.CommandElements[2].Extent.Text -cne '$uiPath' }).Count -eq 0) 'Every production binary replacement and rollback targets only the desktop path.'
    $passed = $true
} catch { $failure = $_.Exception.ToString() }
finally {
    [ordered]@{passed=$passed;checks=$checks;error=$failure;scope='Temporary synthetic files, pure snapshot/update checks, injected transaction failures and production-script AST checks. No personal app processes inspected, stopped or launched; no live engine connection, settings mutation or audio endpoints.'} |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $root 'results.json') -Encoding utf8
}
[pscustomobject]@{passed=$passed;checks=$checks.Count;error=$failure;reportDirectory=$root} | ConvertTo-Json -Depth 4
if (-not $passed) { exit 1 }
