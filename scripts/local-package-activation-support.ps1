# Pure preparation and guarded file replacement for full local development builds.
# Reuse the inventory/path/recovery checks already exercised by UI activation.
# Dot-sourcing either support file performs no activation or workspace changes.
. (Join-Path $PSScriptRoot 'local-ui-activation-support.ps1')

function Get-PackageActivationComparableConfiguration($Configuration) {
    # Compare old/new engine snapshots with exactly the documented missing-field
    # defaults. This is an in-memory copy; never rewrite saved user settings.
    $copy = $Configuration | ConvertTo-Json -Depth 80 | ConvertFrom-Json -Depth 80
    foreach ($node in @($copy.graph.nodes)) {
        foreach ($field in @(
            @{Name='stereoWidth';Value=1}, @{Name='panPosition';Value=0},
            @{Name='crossfadePosition';Value=0}, @{Name='crossfadeCurve';Value='equalPower'},
            @{Name='controlMultiplier';Value=-1}
        )) {
            if (-not $node.PSObject.Properties[$field.Name]) {
                $node | Add-Member -NotePropertyName $field.Name -NotePropertyValue $field.Value
            }
        }
    }
    foreach ($edge in @($copy.graph.edges)) {
        foreach ($name in @('fromPort','toPort')) {
            if (-not $edge.PSObject.Properties[$name]) {
                $edge | Add-Member -NotePropertyName $name -NotePropertyValue 'audio'
            }
        }
    }
    return $copy
}

function Enter-PackageActivationUpdateGuard([string]$MutexName, [string]$UpdateRoot) {
    $mutex = [Threading.Mutex]::new($false, $MutexName)
    $owned = $false
    try {
        try { $owned = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $owned = $true }
        if (-not $owned) { throw 'An update worker owns the application. Local package activation was withheld.' }
        Assert-UiActivationUpdateHistory $UpdateRoot
        return $mutex
    } catch {
        if ($owned) { $mutex.ReleaseMutex() }
        $mutex.Dispose()
        throw
    }
}

function Assert-PackageActivationRunningProcesses([object[]]$Controllers, [object[]]$Engines,
        $ControllerIdentity, $EngineIdentity) {
    if ($Controllers.Count -ne 1 -or $Engines.Count -ne 1 -or
        -not (Test-ActivationProcessIdentity $Controllers[0] $ControllerIdentity) -or
        -not (Test-ActivationProcessIdentity $Engines[0] $EngineIdentity) -or
        $Controllers[0].HasExited -or $Engines[0].HasExited -or -not $Controllers[0].Responding) {
        throw 'The candidate desktop or engine exited, changed identity or became unresponsive after shortcut launch.'
    }
}

function Invoke-PackageActivationObservation([scriptblock]$Probe,
        [ValidateRange(1,30000)][int]$DurationMilliseconds = 8000,
        [ValidateRange(1,1000)][int]$PollMilliseconds = 250) {
    # Include a final probe after the observation interval. No engine request is
    # sent here: the reopened controller owns the engine's control connection.
    $watch = [Diagnostics.Stopwatch]::StartNew()
    do {
        & $Probe
        $remaining = $DurationMilliseconds - $watch.ElapsedMilliseconds
        if ($remaining -le 0) { break }
        Start-Sleep -Milliseconds ([Math]::Min($PollMilliseconds, $remaining))
    } while ($true)
}

function New-PackageActivationInventoryPlan([string]$Package, [string]$Evidence, [string]$Candidate,
        [string]$OriginalUiHash, [string]$OriginalEngineHash, [string]$CandidateUiHash,
        [string]$CandidateEngineHash, [string]$OriginalVersion, [string]$CandidateVersion,
        [string]$CandidateNumericVersion, [switch]$RequireInventory) {
    $path = Join-Path $Package 'SetupSupport\package-manifest.json'
    if ($RequireInventory -and -not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw 'Installed-package activation requires its existing package inventory.'
    }
    $plan = New-UiActivationInventoryPlan $Package $Evidence (Join-Path $Candidate 'VineMix.exe') `
        $OriginalUiHash $CandidateUiHash $OriginalVersion
    if (-not $plan.Present) { return $plan }
    $document = [Text.Json.Nodes.JsonNode]::Parse([IO.File]::ReadAllText($plan.Candidate))
    $engineEntry = @($document['files'] | Where-Object { $_['path'].GetValue[string]() -ieq 'VineMix.Engine.exe' })[0]
    if ($engineEntry['sha256'].GetValue[string]() -ine $OriginalEngineHash) {
        throw 'The installed inventory does not match the reviewed current engine.'
    }
    $buildPath = $Candidate.TrimEnd('\','/') + '.build.json'
    Assert-UiActivationInventoryPath $buildPath
    if (-not (Test-Path -LiteralPath $buildPath -PathType Leaf) -or (Get-Item -LiteralPath $buildPath).Length -gt 8MB) {
        throw 'The candidate build record is missing or too large to validate.'
    }
    $build = Get-Content -LiteralPath $buildPath -Raw | ConvertFrom-Json -Depth 20
    if ($build.version -cne $CandidateVersion -or $build.numericVersion -cne $CandidateNumericVersion -or
        $build.buildMode -cne 'Installed' -or $build.published -isnot [bool] -or $build.published -or
        (Normalize-ActivationPath $build.packageDirectory) -ine (Normalize-ActivationPath $Candidate) -or
        $build.sourceIdentity.sourceSnapshotSha256 -cnotmatch '^[A-Fa-f0-9]{64}$' -or
        $build.sourceIdentity.gitHead -cnotmatch '^[A-Fa-f0-9]{40}$' -or $build.sourceIdentity.gitDirty -isnot [bool]) {
        throw 'The candidate build record does not describe this unpublished Installed-mode release and source.'
    }
    foreach ($binary in @(@{Name='VineMix.exe';Hash=$CandidateUiHash}, @{Name='VineMix.Engine.exe';Hash=$CandidateEngineHash})) {
        $file = Join-Path $Candidate $binary.Name
        Assert-UiActivationInventoryPath $file
        $entries = @($build.files | Where-Object path -ieq $binary.Name)
        if ($entries.Count -ne 1 -or $entries[0].sha256 -ine $binary.Hash -or
            $entries[0].size -ne (Get-Item -LiteralPath $file).Length -or
            (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ine $binary.Hash) {
            throw 'The candidate executable does not match its reviewed build inventory.'
        }
    }
    $engineEntry['size'] = [Text.Json.Nodes.JsonValue]::Create([long](Get-Item -LiteralPath (Join-Path $Candidate 'VineMix.Engine.exe')).Length)
    $engineEntry['sha256'] = [Text.Json.Nodes.JsonValue]::Create($CandidateEngineHash.ToUpperInvariant())
    $document['version'] = [Text.Json.Nodes.JsonValue]::Create($CandidateVersion)
    $document['numericVersion'] = [Text.Json.Nodes.JsonValue]::Create($CandidateNumericVersion)
    $document['sourceSnapshotSha256'] = [Text.Json.Nodes.JsonValue]::Create([string]$build.sourceIdentity.sourceSnapshotSha256)
    $document['gitHead'] = [Text.Json.Nodes.JsonValue]::Create([string]$build.sourceIdentity.gitHead)
    $document['gitDirty'] = [Text.Json.Nodes.JsonValue]::Create([bool]$build.sourceIdentity.gitDirty)
    # Other installed files retain their measured entries; they are not claimed
    # to be newly packaged. Preserve the original release provenance explicitly.
    $original = [Text.Json.Nodes.JsonNode]::Parse([IO.File]::ReadAllText($plan.Backup))
    $overlay = [Text.Json.Nodes.JsonObject]::new()
    $overlay['kind'] = [Text.Json.Nodes.JsonValue]::Create('local-development')
    $overlay['baseVersion'] = $original['version'].DeepClone()
    $overlay['baseInventorySha256'] = [Text.Json.Nodes.JsonValue]::Create($plan.OriginalHash)
    $overlay['replacedFiles'] = [Text.Json.Nodes.JsonNode]::Parse('["VineMix.exe","VineMix.Engine.exe"]')
    $document['localActivation'] = $overlay
    [IO.File]::WriteAllText($plan.Candidate, $document.ToJsonString([Text.Json.JsonSerializerOptions]@{WriteIndented=$true}), [Text.UTF8Encoding]::new($false))
    $plan.CandidateHash = (Get-FileHash -LiteralPath $plan.Candidate -Algorithm SHA256).Hash
    return $plan
}

function Install-PackageActivationPayload($Plan, [object[]]$Replacements, [scriptblock]$BeforeReplace) {
    Assert-UiActivationInventoryState $Plan Original
    foreach ($file in $Replacements) {
        if ((Get-FileHash -LiteralPath $file.Target -Algorithm SHA256).Hash -cne $file.OriginalHash -or
            (Get-FileHash -LiteralPath $file.Source -Algorithm SHA256).Hash -cne $file.CandidateHash) {
            throw 'An executable changed before package replacement. Its contents were preserved.'
        }
    }
    foreach ($file in $Replacements) {
        Replace-ActivationBinary $file.Source $file.Target $file.CandidateHash @($file.OriginalHash) $BeforeReplace 5000
    }
    Install-UiActivationInventory $Plan $BeforeReplace 5000
}

function Restore-PackageActivationPayload($Plan, [object[]]$Replacements, [scriptblock]$BeforeReplace) {
    Assert-UiActivationInventoryState $Plan Recoverable
    # Validate the whole recovery set before the first write; an independent
    # edit or damaged backup must not produce a mixed engine/controller pair.
    foreach ($file in $Replacements) {
        if ((Get-FileHash -LiteralPath $file.Backup -Algorithm SHA256).Hash -cne $file.OriginalHash -or
            (Get-FileHash -LiteralPath $file.Target -Algorithm SHA256).Hash -cnotin @($file.OriginalHash,$file.CandidateHash)) {
            throw 'An executable or recovery copy changed independently. Package recovery was withheld.'
        }
    }
    foreach ($file in $Replacements) {
        Replace-ActivationBinary $file.Backup $file.Target $file.OriginalHash @($file.OriginalHash,$file.CandidateHash) $BeforeReplace 5000
    }
    Restore-UiActivationInventory $Plan $BeforeReplace 5000
}
