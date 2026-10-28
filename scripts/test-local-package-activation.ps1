#requires -Version 7.3
[CmdletBinding()]
param([string]$ResultDirectory = '')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'assert-desktop-data-context.ps1')
. (Join-Path $PSScriptRoot 'local-activation-support.ps1')
. (Join-Path $PSScriptRoot 'local-package-activation-support.ps1')
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $ResultDirectory) { $ResultDirectory = Join-Path $env:LOCALAPPDATA ('VineMix.BuildCache\package-activation-verification-' + [guid]::NewGuid().ToString('N')) }
$root = [IO.Path]::GetFullPath($ResultDirectory)
Assert-ActivationEvidenceLocation $root @($project)
if (Test-Path -LiteralPath $root) { throw 'Choose a new verification directory.' }
$null = New-Item -ItemType Directory -Path $root
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
function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }
function Clone($Value) { $Value | ConvertTo-Json -Depth 20 | ConvertFrom-Json -Depth 20 }
try {
    $package = Join-Path $root 'package'
    $candidate = Join-Path $root 'candidate'
    $support = Join-Path $package 'SetupSupport'
    $null = New-Item -ItemType Directory -Path $support,$candidate
    $replacements = @(foreach ($name in @('VineMix.Engine.exe','VineMix.exe')) {
        $target = Join-Path $package $name; $source = Join-Path $candidate $name; $backup = Join-Path $root ($name + '.before')
        ('original-' + $name) | Set-Content -LiteralPath $target -Encoding utf8
        ('candidate-' + $name) | Set-Content -LiteralPath $source -Encoding utf8
        Copy-Item -LiteralPath $target -Destination $backup
        [pscustomobject]@{Target=$target;Source=$source;Backup=$backup;OriginalHash=(Hash $target);CandidateHash=(Hash $source)}
    })
    $retainedFile = Join-Path $support 'retained.txt'
    'Retained installation support' | Set-Content -LiteralPath $retainedFile -Encoding utf8
    $retainedHash = Hash $retainedFile
    $inventoryPath = Join-Path $support 'package-manifest.json'
    $buildPath = $candidate + '.build.json'
    $inventory = [ordered]@{version='0.3.0-beta.5';numericVersion='0.3.0.6';sourceSnapshotSha256=('a'*64);gitHead=('a'*40);gitDirty=$false;custom='preserve';files=@(
        @($replacements | ForEach-Object { [ordered]@{path=(Split-Path -Leaf $_.Target);size=(Get-Item -LiteralPath $_.Target).Length;sha256=$_.OriginalHash} }) +
        @([ordered]@{path='SetupSupport/retained.txt';size=(Get-Item -LiteralPath $retainedFile).Length;sha256=$retainedHash}))}
    $build = [ordered]@{version='0.4.0-beta';numericVersion='0.4.0.0';buildMode='Installed';published=$false;packageDirectory=$candidate;
        sourceIdentity=@{sourceSnapshotSha256=('b'*64);gitHead=('b'*40);gitDirty=$true};files=@($replacements | ForEach-Object {
            [ordered]@{path=(Split-Path -Leaf $_.Source);size=(Get-Item -LiteralPath $_.Source).Length;sha256=$_.CandidateHash}
        })}
    function ResetFixture {
        foreach ($file in $replacements) { Copy-Item -LiteralPath $file.Backup -Destination $file.Target }
        $inventory | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $inventoryPath -Encoding utf8
        $build | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $buildPath -Encoding utf8
    }
    function Plan([switch]$RequireInventory) {
        $evidence = Join-Path $root ('evidence-' + [guid]::NewGuid().ToString('N'))
        $null = New-Item -ItemType Directory -Path $evidence
        New-PackageActivationInventoryPlan $package $evidence $candidate $replacements[1].OriginalHash $replacements[0].OriginalHash `
            $replacements[1].CandidateHash $replacements[0].CandidateHash '0.3.0-beta.5' '0.4.0-beta' '0.4.0.0' -RequireInventory:$RequireInventory
    }
    ResetFixture
    $originalInventoryHash = Hash $inventoryPath
    $plan = Plan -RequireInventory
    $prepared = Get-Content -LiteralPath $plan.Candidate -Raw | ConvertFrom-Json -Depth 20
    Check ($prepared.version -ceq $build.version -and $prepared.numericVersion -ceq $build.numericVersion -and
        $prepared.sourceSnapshotSha256 -ceq $build.sourceIdentity.sourceSnapshotSha256 -and $prepared.gitHead -ceq $build.sourceIdentity.gitHead -and $prepared.gitDirty) 'Prepared inventory carries the exact candidate release, numeric version and source identity.'
    Check ($prepared.custom -ceq 'preserve' -and $prepared.files[2].sha256 -ceq $retainedHash -and $prepared.localActivation.baseVersion -ceq $inventory.version -and
        $prepared.localActivation.baseInventorySha256 -ceq $originalInventoryHash) 'Retained support files and unknown fields keep their values, with explicit local-development provenance.'
    foreach ($file in $replacements) {
        $entry = @($prepared.files | Where-Object path -eq (Split-Path -Leaf $file.Target))[0]
        Check ($entry.sha256 -ceq $file.CandidateHash -and $entry.size -eq (Get-Item -LiteralPath $file.Source).Length) ('Prepared inventory records the actual candidate ' + (Split-Path -Leaf $file.Target) + '.')
    }
    Check ((Hash $inventoryPath) -ceq $originalInventoryHash -and (Hash $plan.Backup) -ceq $originalInventoryHash -and
        (Hash $replacements[0].Target) -ceq $replacements[0].OriginalHash -and (Hash $replacements[1].Target) -ceq $replacements[1].OriginalHash) 'Preparation preserves installed files and a byte-identical private inventory backup.'
    foreach ($field in @('version','numericVersion','buildMode','published','packageDirectory','sourceSnapshot','gitHead','gitDirty','ui-hash','engine-size','duplicate-engine','missing-engine')) {
        ResetFixture
        $bad = Clone $build
        switch ($field) {
            'version' { $bad.version = '0.3.0-beta.5' }
            'numericVersion' { $bad.numericVersion = '0.3.0.6' }
            'buildMode' { $bad.buildMode = 'Standalone' }
            'published' { $bad.published = $true }
            'packageDirectory' { $bad.packageDirectory = $package }
            'sourceSnapshot' { $bad.sourceIdentity.sourceSnapshotSha256 = 'bad' }
            'gitHead' { $bad.sourceIdentity.gitHead = 'bad' }
            'gitDirty' { $bad.sourceIdentity.gitDirty = 'true' }
            'ui-hash' { $bad.files[1].sha256 = 'c'*64 }
            'engine-size' { $bad.files[0].size++ }
            'duplicate-engine' { $bad.files += $bad.files[0] }
            'missing-engine' { $bad.files = @($bad.files[1]) }
        }
        $bad | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $buildPath -Encoding utf8
        Reject { Plan -RequireInventory } ('A mismatched candidate build record fails before any installed mutation: ' + $field + '.')
    }
    ResetFixture
    Remove-Item -LiteralPath $buildPath
    Reject { Plan -RequireInventory } 'An installed upgrade requires the candidate build record.'
    ResetFixture
    Remove-Item -LiteralPath $inventoryPath
    Reject { Plan -RequireInventory } 'An explicit installed upgrade cannot silently skip a missing package inventory.'
    $noInventory = Plan
    Check (-not $noInventory.Present) 'An ordinary development folder retains the existing inventory-free activation path.'
    ResetFixture
    foreach ($failurePoint in @('none','engine-replaced','both-replaced','post-launch','independent-inventory','independent-ui','independent-engine','damaged-backup')) {
        ResetFixture
        $plan = Plan -RequireInventory
        $guard = @{calls=0}
        $failed = $false; $restored = $false
        try {
            Install-PackageActivationPayload $plan $replacements {
                $guard.calls++
                if (($failurePoint -eq 'engine-replaced' -and $guard.calls -eq 2) -or
                    ($failurePoint -eq 'both-replaced' -and $guard.calls -eq 3)) { throw 'Injected replacement interruption.' }
            }
            if ($failurePoint -ne 'none') {
                switch ($failurePoint) {
                    'independent-inventory' { 'independent-inventory' | Set-Content -LiteralPath $inventoryPath -Encoding utf8 }
                    'independent-ui' { 'independent-ui' | Set-Content -LiteralPath $replacements[1].Target -Encoding utf8 }
                    'independent-engine' { 'independent-engine' | Set-Content -LiteralPath $replacements[0].Target -Encoding utf8 }
                    'damaged-backup' { 'damaged-backup' | Set-Content -LiteralPath $plan.Backup -Encoding utf8 }
                }
                throw 'Injected failure after launch.'
            }
        } catch {
            $failed = $true
            try { Restore-PackageActivationPayload $plan $replacements { }; $restored = $true } catch { }
        }
        if ($failurePoint -eq 'none') {
            $live = Get-Content -LiteralPath $inventoryPath -Raw | ConvertFrom-Json
            Check (-not $failed -and (Hash $inventoryPath) -ceq $plan.CandidateHash -and $live.version -ceq $build.version -and
                @($live.files | Where-Object { (Hash (Join-Path $package $_.path)) -ine $_.sha256 -or (Get-Item -LiteralPath (Join-Path $package $_.path)).Length -ne $_.size }).Count -eq 0) 'Successful full activation yields a complete release-matching payload inventory accepted by the updater rules.'
        } elseif ($failurePoint -in @('engine-replaced','both-replaced','post-launch')) {
            Check ($failed -and $restored -and (Hash $inventoryPath) -ceq $originalInventoryHash -and
                (Hash $replacements[0].Target) -ceq $replacements[0].OriginalHash -and (Hash $replacements[1].Target) -ceq $replacements[1].OriginalHash) ('Failure at ' + $failurePoint + ' restores the exact original executable pair and inventory.')
        } else {
            Check ($failed -and -not $restored) ('Recovery refuses an independently changed file or damaged backup: ' + $failurePoint + '.')
            $unchanged = if ($failurePoint -eq 'independent-engine') { $replacements[1] } else { $replacements[0] }
            Check ((Hash $unchanged.Target) -ceq $unchanged.CandidateHash) ('Recovery validates the complete set before its first write: ' + $failurePoint + '.')
        }
        Check ((Hash $retainedFile) -ceq $retainedHash) ('Installation support bytes remain unchanged: ' + $failurePoint + '.')
    }
    ResetFixture
    $updates = Join-Path $root 'updates'
    $transaction = Join-Path $updates ([guid]::NewGuid().ToString('N'))
    $null = New-Item -ItemType Directory -Path $transaction -Force
    $journal = Join-Path $transaction 'journal.json'
    $mutexName = 'Local\VineMix.PackageActivation.Fixture.' + [guid]::NewGuid().ToString('N')
    foreach ($phase in 0..13) {
        @{phase=$phase} | ConvertTo-Json | Set-Content -LiteralPath $journal -Encoding utf8
        if ($phase -in @(9,11,12)) {
            $lease = Enter-PackageActivationUpdateGuard $mutexName $updates
            try { Check $true ('Terminal update phase ' + $phase + ' permits a guarded activation.') }
            finally { $lease.ReleaseMutex(); $lease.Dispose() }
        } else { Reject { Enter-PackageActivationUpdateGuard $mutexName $updates } ('Unfinished update phase ' + $phase + ' blocks full activation.') }
    }
    '{broken' | Set-Content -LiteralPath $journal -Encoding utf8
    Reject { Enter-PackageActivationUpdateGuard $mutexName $updates } 'Malformed update history fails closed and releases its mutex.'
    '{"phase":9}' | Set-Content -LiteralPath $journal -Encoding utf8
    $null = New-Item -ItemType Directory -Path (Join-Path $updates '.preparing-fixture')
    Reject { Enter-PackageActivationUpdateGuard $mutexName $updates } 'An update preparing before worker startup prevents full activation.'
    Remove-Item -LiteralPath (Join-Path $updates '.preparing-fixture')
    Add-Type -TypeDefinition @'
using System;
using System.Threading;
using System.Threading.Tasks;
public static class PackageActivationMutexFixture {
    public static readonly ManualResetEventSlim Held = new(false), Release = new(false);
    public static Task Hold(string name) => Task.Run(() => {
        using var mutex = new Mutex(false, name);
        mutex.WaitOne(); Held.Set();
        try { Release.Wait(TimeSpan.FromSeconds(10)); } finally { mutex.ReleaseMutex(); }
    });
}
'@
    $holder = [PackageActivationMutexFixture]::Hold($mutexName)
    try {
        Check ([PackageActivationMutexFixture]::Held.Wait(5000)) 'The isolated competing worker owns the fixture mutex.'
        Reject { Enter-PackageActivationUpdateGuard $mutexName $updates } 'A competing update worker prevents full activation before any process change.'
    } finally { [PackageActivationMutexFixture]::Release.Set(); $holder.GetAwaiter().GetResult() }
    $lease = Enter-PackageActivationUpdateGuard $mutexName $updates
    try { Check $true 'Activation can acquire the mutex once the competing worker releases it.' }
    finally { $lease.ReleaseMutex(); $lease.Dispose() }

    $uiIdentity = [pscustomobject]@{Id=101;StartTime=[DateTime]::UtcNow;Path='C:\Example\VineMix.exe';SessionId=2;HasExited=$false;Responding=$true}
    $engineIdentity = [pscustomobject]@{Id=102;StartTime=$uiIdentity.StartTime;Path='C:\Example\VineMix.Engine.exe';SessionId=2;HasExited=$false}
    Assert-PackageActivationRunningProcesses @($uiIdentity) @($engineIdentity) $uiIdentity $engineIdentity
    Check $true 'Post-launch inspection accepts only the captured responsive controller and engine pair.'
    foreach ($mismatch in @('missing-ui','missing-engine','extra-ui','extra-engine','reused-ui-pid','reused-engine-pid','unresponsive-ui','exited-ui','exited-engine')) {
        $uis = @(Clone $uiIdentity); $engines = @(Clone $engineIdentity)
        switch ($mismatch) {
            'missing-ui' { $uis = @() }
            'missing-engine' { $engines = @() }
            'extra-ui' { $uis += Clone $uiIdentity }
            'extra-engine' { $engines += Clone $engineIdentity }
            'reused-ui-pid' { $uis[0].StartTime = $uiIdentity.StartTime.AddSeconds(1) }
            'reused-engine-pid' { $engines[0].StartTime = $engineIdentity.StartTime.AddSeconds(1) }
            'unresponsive-ui' { $uis[0].Responding = $false }
            'exited-ui' { $uis[0].HasExited = $true }
            'exited-engine' { $engines[0].HasExited = $true }
        }
        Reject { Assert-PackageActivationRunningProcesses $uis $engines $uiIdentity $engineIdentity } ('Post-launch observation rejects ' + $mismatch + '.')
    }
    $observation = @{calls=0;timer=[Diagnostics.Stopwatch]::StartNew()}
    Invoke-PackageActivationObservation { $observation.calls++ } -DurationMilliseconds 30 -PollMilliseconds 5
    Check ($observation.calls -ge 2 -and $observation.timer.ElapsedMilliseconds -ge 30) 'Observation checks again after its complete interval rather than accepting immediate startup alone.'
    $observation.calls = 0
    Reject {
        Invoke-PackageActivationObservation {
            $observation.calls++
            if ($observation.calls -eq 2) { throw 'Injected delayed process or settings change.' }
        } -DurationMilliseconds 30 -PollMilliseconds 5
    } 'A delayed process or settings failure aborts the post-launch observation.'

    $legacyConfiguration = [pscustomobject]@{graph=[pscustomobject]@{
        nodes=@([pscustomobject]@{id='example';kind='mix';gainDb=-7})
        edges=@([pscustomobject]@{id='example-route';from='example';to='output';kind='audio'})}}
    $legacyJson = $legacyConfiguration | ConvertTo-Json -Depth 20 -Compress
    $comparable = Get-PackageActivationComparableConfiguration $legacyConfiguration
    Check ($comparable.graph.nodes[0].stereoWidth -eq 1 -and $comparable.graph.nodes[0].panPosition -eq 0 -and
        $comparable.graph.nodes[0].crossfadePosition -eq 0 -and $comparable.graph.nodes[0].crossfadeCurve -ceq 'equalPower' -and
        $comparable.graph.nodes[0].controlMultiplier -eq -1 -and
        $comparable.graph.edges[0].fromPort -ceq 'audio' -and $comparable.graph.edges[0].toPort -ceq 'audio') 'Legacy snapshots receive only documented missing mixing defaults for comparison.'
    Check (($legacyConfiguration | ConvertTo-Json -Depth 20 -Compress) -ceq $legacyJson) 'Compatibility comparison never mutates the original configuration.'
    $comparable.graph.nodes[0].controlMultiplier = 0
    Check ((Get-PackageActivationComparableConfiguration $comparable).graph.nodes[0].controlMultiplier -eq 0) 'An explicit zero multiplier retains exact silence in activation comparisons.'
    $comparable.graph.nodes[0].controlMultiplier = 1.5
    $comparable.graph.nodes[0].stereoWidth = 1.7
    $comparable.graph.nodes[0].panPosition = -.4
    $comparable.graph.nodes[0].crossfadePosition = .6
    $comparable.graph.nodes[0].crossfadeCurve = 'linear'
    $comparable.graph.edges[0].fromPort = 'left'
    $comparable.graph.edges[0].toPort = 'b'
    $preserved = Get-PackageActivationComparableConfiguration $comparable
    Check (($comparable | ConvertTo-Json -Depth 20 -Compress) -ceq ($preserved | ConvertTo-Json -Depth 20 -Compress)) 'Explicit non-default mixing controls and named ports remain detectable across activation.'
    $comparable.graph.nodes[0].stereoWidth = $null
    Check ($null -eq (Get-PackageActivationComparableConfiguration $comparable).graph.nodes[0].stereoWidth) 'An explicit null is not silently replaced by a compatibility default.'

    $tokens = $null; $errors = $null
    $scriptPath = Join-Path $PSScriptRoot 'activate-local-package.ps1'
    $ast = [Management.Automation.Language.Parser]::ParseFile($scriptPath,[ref]$tokens,[ref]$errors)
    Check ($errors.Count -eq 0) 'The full activation entry point parses without running it.'
    $scriptText = [IO.File]::ReadAllText($scriptPath).Replace("`r`n", "`n")
    Check ($scriptText.Contains('Install-PackageActivationPayload $inventoryPlan $replacements') -and
        $scriptText.Contains('Restore-PackageActivationPayload $inventoryPlan $replacements') -and
        $scriptText.Contains('-RequireInventory:$packageOverride')) 'Production activation uses the tested paired replacement/recovery and mandatory installed inventory.'
    $mainStart = $scriptText.IndexOf('try {' + "`n" + '    VerifyCandidate')
    $guardStart = if ($mainStart -ge 0) { $scriptText.IndexOf('    HoldUpdateGuard', $mainStart) } else { -1 }
    $stopStart = $scriptText.IndexOf('    $oldUi[0].Kill()')
    Check ($mainStart -ge 0 -and $guardStart -gt $mainStart -and $stopStart -gt $guardStart -and
        $scriptText.Contains('finally {' + "`n" + '    ReleaseUpdateGuard')) 'Production activation holds the update guard before controller shutdown and releases it on every exit.'
    Check ($scriptText.Contains('Invoke-PackageActivationObservation {') -and
        $scriptText.Contains('Assert-PackageActivationRunningProcesses $liveControllers $liveEngines $newUiIdentity $newEngine') -and
        $scriptText.Contains('$result.postLaunchObservationMilliseconds = 8000')) 'Production activation observes the exact reopened pair and saved workspace for eight seconds before reporting success.'
    $passed = $true
} catch { $failure = $_.Exception.ToString(); $stack = $_.ScriptStackTrace }
finally {
    [ordered]@{passed=$passed;checks=$checks;error=$failure;stack=$stack;scope='Synthetic package files, injected replacement/recovery failures, temporary update journals, isolated mutex contention and production script parsing. No real app/engine process, settings, audio endpoint or registry changes.'} |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $root 'results.json') -Encoding utf8
}
[pscustomobject]@{passed=$passed;checks=$checks.Count;error=$failure;reportDirectory=$root} | ConvertTo-Json -Depth 3
if (-not $passed) { exit 1 }
