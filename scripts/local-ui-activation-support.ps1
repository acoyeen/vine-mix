# Pure checks and transaction ordering for UI-only local activation.
# Dot-sourcing this file never reads personal data or changes a process.
function ConvertTo-UiActivationCanonical($Value) {
    if ($null -eq $Value) { return 'null' }
    if ($Value -is [double] -or $Value -is [single]) {
        if ($Value -eq 0) { return '0' }
        return $Value.ToString('R', [Globalization.CultureInfo]::InvariantCulture)
    }
    if ($Value -is [System.Management.Automation.PSCustomObject]) {
        $parts = @($Value.PSObject.Properties | Sort-Object Name | ForEach-Object {
            ($_.Name | ConvertTo-Json -Compress) + ':' + (ConvertTo-UiActivationCanonical $_.Value)
        })
        return '{' + ($parts -join ',') + '}'
    }
    if ($Value -is [System.Collections.IEnumerable] -and $Value -isnot [string]) {
        return '[' + (@(foreach ($item in $Value) { ConvertTo-UiActivationCanonical $item }) -join ',') + ']'
    }
    return ($Value | ConvertTo-Json -Compress)
}

function Assert-UiActivationRelease([string]$CandidateVersion, [string]$CurrentVersion, [string]$EngineVersion) {
    if (-not $EngineVersion -or (($CandidateVersion -split '\+')[0]) -cne $EngineVersion -or
        (($CurrentVersion -split '\+')[0]) -cne $EngineVersion) {
        throw 'UI-only activation requires the candidate, current desktop and unchanged engine to have exactly the same release identity.'
    }
}

function Assert-UiActivationSnapshotStable($Before, $After) {
    if ($null -eq $Before.configuration -or $null -eq $After.configuration -or
        (ConvertTo-UiActivationCanonical $Before.configuration) -cne (ConvertTo-UiActivationCanonical $After.configuration)) {
        throw 'The engine configuration changed during the UI-only replacement. The current workspace remains authoritative.'
    }
    if ($null -eq $Before.status.audioFrames -or $null -eq $After.status.audioFrames -or
        [ulong]$After.status.audioFrames -le [ulong]$Before.status.audioFrames) {
        throw 'The unchanged audio engine did not advance its frame counter during the UI-only replacement.'
    }
    $running = @($After.status.nodes | Where-Object { $_.path.state -eq 'Running' } | ForEach-Object id)
    $missing = @($Before.status.nodes | Where-Object { $_.path.state -eq 'Running' -and $_.id -notin $running })
    if ($missing.Count) { throw 'A previously running audio path stopped during the UI-only replacement.' }
}

function Assert-UiActivationUpdateHistory([string]$Root) {
    if (-not (Test-Path -LiteralPath $Root)) { return }
    foreach ($directory in Get-ChildItem -LiteralPath $Root -Directory -ErrorAction Stop) {
        $id = [guid]::Empty
        if ($directory.Name.StartsWith('.preparing-', [StringComparison]::OrdinalIgnoreCase)) {
            throw 'An update is being prepared. UI-only activation cannot overlap an update.'
        }
        if (-not [guid]::TryParseExact($directory.Name, 'N', [ref]$id)) { continue }
        $journalPath = Join-Path $directory.FullName 'journal.json'
        if (-not (Test-Path -LiteralPath $journalPath -PathType Leaf) -or (Get-Item -LiteralPath $journalPath).Length -gt 8MB) {
            throw 'An update journal is missing or cannot be safely inspected. UI-only activation was withheld.'
        }
        $journal = Get-Content -LiteralPath $journalPath -Raw | ConvertFrom-Json -Depth 32
        # UpdatePhase serializes as an integer: Completed=9, RolledBack=11,
        # Cancelled=12. Every other phase, including Prepared, is active here.
        if ($journal.phase -isnot [long] -and $journal.phase -isnot [int] -or $journal.phase -notin @(9,11,12)) {
            throw 'An unfinished update must be resolved before UI-only activation. Its files were left untouched.'
        }
    }
}

function Assert-UiActivationPreservedWorkspaces($Records) {
    # Opening the desktop may legitimately update its update-check timestamp or
    # What's new dismissal metadata. Saved graphs and UI preferences, including
    # every retained legacy/redirected copy, must remain byte-identical.
    $workspaces = @(foreach ($record in $Records) {
        if ($record.kind -eq 'directory') {
            $names = @($record.names | Where-Object { $_ -like 'settings.json*' -or $_ -eq 'ui.json' })
            $actual = @(Get-ChildItem -LiteralPath $record.source -File | Where-Object {
                $_.Name -like 'settings.json*' -or $_.Name -eq 'ui.json'
            } | Select-Object -ExpandProperty Name | Sort-Object)
            if (($names -join "`n") -cne ($actual -join "`n")) { throw 'Saved workspace copies appeared or disappeared after reopening. Current files were preserved.' }
        } elseif ((Split-Path -Leaf $record.source) -like 'settings.json*' -or (Split-Path -Leaf $record.source) -eq 'ui.json') {
            $record
        }
    })
    Assert-ActivationDataStable $workspaces
}

function Restore-UiActivationBinary([string]$Backup, [string]$Target, [string]$OriginalHash, [string]$CandidateHash,
        [scriptblock]$BeforeReplace = $null, [int]$RetryTimeoutMilliseconds = 0) {
    if ((Get-FileHash -LiteralPath $Backup -Algorithm SHA256).Hash -cne $OriginalHash.ToUpperInvariant() -or
        (Get-FileHash -LiteralPath $Target -Algorithm SHA256).Hash -notin @($OriginalHash.ToUpperInvariant(),$CandidateHash.ToUpperInvariant())) {
        throw 'A desktop executable changed independently; rollback will not overwrite it.'
    }
    Replace-ActivationBinary $Backup $Target $OriginalHash @($OriginalHash,$CandidateHash) $BeforeReplace $RetryTimeoutMilliseconds
}

# Installed update recovery validates the package inventory before taking its
# backup. Keep its desktop entry consistent with a local UI-only replacement,
# without changing the engine, release metadata or any other payload entry.
function Assert-UiActivationInventoryPath([string]$Path) {
    $current = [IO.Path]::GetFullPath($Path)
    while ($current) {
        if (Test-Path -LiteralPath $current) {
            if ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw 'Installed inventory paths must not pass through a link or junction.'
            }
        }
        $current = [IO.Path]::GetDirectoryName($current)
    }
}

function New-UiActivationInventoryPlan([string]$Package, [string]$Evidence, [string]$CandidateUi,
        [string]$OriginalUiHash, [string]$CandidateUiHash, [string]$Version) {
    $path = Join-Path $Package 'SetupSupport\package-manifest.json'
    Assert-UiActivationInventoryPath $path
    $plan = [pscustomobject]@{Present=$false;Path=$path;OriginalHash=$null;CandidateHash=$null;Backup=$null;Candidate=$null}
    if (-not (Test-Path -LiteralPath $path)) { return $plan }
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -gt 2MB) {
        throw 'The installed package inventory is missing or too large to validate.'
    }
    $originalHash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    $document = [Text.Json.Nodes.JsonNode]::Parse([IO.File]::ReadAllText($path))
    if ($document -isnot [Text.Json.Nodes.JsonObject] -or $document['version'].GetValue[string]() -cne $Version -or
        $document['files'] -isnot [Text.Json.Nodes.JsonArray] -or $document['files'].Count -gt 2048) {
        throw 'The installed package inventory does not describe the current release.'
    }
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $desktopEntry = $null
    $root = [IO.Path]::GetFullPath($Package).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
    foreach ($entry in $document['files']) {
        $relative = $entry['path'].GetValue[string]()
        $size = $entry['size'].GetValue[long]()
        $hash = $entry['sha256'].GetValue[string]()
        if ([string]::IsNullOrWhiteSpace($relative) -or [IO.Path]::IsPathRooted($relative) -or
            $relative -match '[:\\]' -or @($relative.Split('/') | Where-Object { $_ -in @('','.','..') }).Count -or
            -not $names.Add($relative) -or $size -le 0 -or $hash -cnotmatch '^[a-fA-F0-9]{64}$') {
            throw 'The installed package inventory contains an invalid or duplicate payload entry.'
        }
        $file = [IO.Path]::GetFullPath((Join-Path $Package $relative))
        if (-not $file.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { throw 'An inventory path escapes the installed package.' }
        Assert-UiActivationInventoryPath $file
        if (-not (Test-Path -LiteralPath $file -PathType Leaf) -or (Get-Item -LiteralPath $file).Length -ne $size -or
            (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ine $hash) {
            throw 'An installed payload file differs from its inventory. No desktop was stopped.'
        }
        if ($relative -ieq 'VineMix.exe') {
            if ($hash -ine $OriginalUiHash) { throw 'The installed inventory does not match the reviewed current desktop.' }
            $desktopEntry = $entry
        }
    }
    if ($null -eq $desktopEntry -or -not $names.Contains('VineMix.Engine.exe')) {
        throw 'The installed inventory does not own both the desktop and unchanged engine.'
    }
    if ((Get-FileHash -LiteralPath $CandidateUi -Algorithm SHA256).Hash -ine $CandidateUiHash) {
        throw 'The candidate desktop changed before its inventory was prepared.'
    }
    $backup = Join-Path $Evidence 'package-manifest.before.json'
    $candidate = Join-Path $Evidence 'package-manifest.candidate.json'
    if ((Test-Path -LiteralPath $backup) -or (Test-Path -LiteralPath $candidate)) { throw 'Inventory evidence must use fresh private paths.' }
    Copy-Item -LiteralPath $path -Destination $backup
    if ((Get-FileHash -LiteralPath $backup -Algorithm SHA256).Hash -cne $originalHash -or
        (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -cne $originalHash) { throw 'The installed inventory changed during backup.' }
    $desktopEntry['size'] = [Text.Json.Nodes.JsonValue]::Create([long](Get-Item -LiteralPath $CandidateUi).Length)
    $desktopEntry['sha256'] = [Text.Json.Nodes.JsonValue]::Create($CandidateUiHash.ToUpperInvariant())
    [IO.File]::WriteAllText($candidate, $document.ToJsonString([Text.Json.JsonSerializerOptions]@{WriteIndented=$true}), [Text.UTF8Encoding]::new($false))
    $plan.Present = $true; $plan.OriginalHash = $originalHash
    $plan.CandidateHash = (Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash
    $plan.Backup = $backup; $plan.Candidate = $candidate
    return $plan
}

function Assert-UiActivationInventoryState($Plan, [ValidateSet('Original','Candidate','Recoverable')][string]$State) {
    if ($null -eq $Plan) { throw 'Inventory preservation was not prepared.' }
    Assert-UiActivationInventoryPath $Plan.Path
    if (-not $Plan.Present) {
        if (Test-Path -LiteralPath $Plan.Path) { throw 'An installed inventory appeared independently. Its contents were preserved.' }
        return
    }
    $allowed = switch ($State) {
        'Original' { @($Plan.OriginalHash) }
        'Candidate' { @($Plan.CandidateHash) }
        'Recoverable' { @($Plan.OriginalHash, $Plan.CandidateHash) }
    }
    if (-not (Test-Path -LiteralPath $Plan.Path -PathType Leaf) -or
        (Get-FileHash -LiteralPath $Plan.Path -Algorithm SHA256).Hash -cnotin $allowed) {
        throw 'The installed inventory changed independently. Neither it nor the desktop will be overwritten during recovery.'
    }
    if ($State -eq 'Recoverable' -and
        (-not (Test-Path -LiteralPath $Plan.Backup -PathType Leaf) -or
        (Get-FileHash -LiteralPath $Plan.Backup -Algorithm SHA256).Hash -cne $Plan.OriginalHash)) {
        throw 'The inventory recovery copy changed. Desktop and inventory recovery were withheld.'
    }
}

function Install-UiActivationInventory($Plan, [scriptblock]$BeforeReplace = $null, [int]$RetryTimeoutMilliseconds = 0) {
    Assert-UiActivationInventoryState $Plan Original
    if ($Plan.Present) { Replace-ActivationBinary $Plan.Candidate $Plan.Path $Plan.CandidateHash @($Plan.OriginalHash) $BeforeReplace $RetryTimeoutMilliseconds }
    Assert-UiActivationInventoryState $Plan Candidate
}

function Restore-UiActivationInventory($Plan, [scriptblock]$BeforeReplace = $null, [int]$RetryTimeoutMilliseconds = 0) {
    Assert-UiActivationInventoryState $Plan Recoverable
    if ($Plan.Present) { Replace-ActivationBinary $Plan.Backup $Plan.Path $Plan.OriginalHash @($Plan.OriginalHash,$Plan.CandidateHash) $BeforeReplace $RetryTimeoutMilliseconds }
    Assert-UiActivationInventoryState $Plan Original
}

function Invoke-UiActivationTransaction([System.Collections.IDictionary]$Actions, [System.Collections.IDictionary]$TransactionState) {
    # There are deliberately no engine stop/start/replace operations in this
    # contract. Injected operations let isolated tests exercise every failure.
    $TransactionState.controllerStopAttempted = $false
    $TransactionState.replacementAttempted = $false
    $TransactionState.passed = $false
    $TransactionState.rolledBack = $false
    try {
        & $Actions.VerifyBeforeStop
        $TransactionState.controllerStopAttempted = $true
        & $Actions.StopOriginalUi
        & $Actions.CaptureBaseline
        & $Actions.VerifyBeforeReplace
        $TransactionState.replacementAttempted = $true
        & $Actions.InstallUi
        & $Actions.VerifyBeforeLaunch
        & $Actions.StartUi
        & $Actions.VerifyAfterLaunch
        $TransactionState.passed = $true
        $TransactionState.stage = 'complete'
    } catch {
        $TransactionState.error = $_.Exception.ToString()
        $TransactionState.scriptStackTrace = $_.ScriptStackTrace
        $TransactionState.failureStage = $TransactionState.stage
        $recoveryErrors = [Collections.Generic.List[string]]::new()
        if ($TransactionState.controllerStopAttempted) {
            try {
                # Stop only a positively captured candidate UI, even if the
                # engine has independently failed. Never stop an engine here.
                & $Actions.StopCandidateUi
                if ($TransactionState.replacementAttempted) {
                    & $Actions.RestoreUi
                    $TransactionState.rolledBack = $true
                }
                # A missing/replaced engine withholds reopening: the ordinary
                # desktop supervisor must not cold-start an engine for recovery.
                & $Actions.VerifyRecovery
                & $Actions.ReopenOriginalUi
            } catch {
                $recoveryErrors.Add($_.Exception.Message)
                $TransactionState.recoveryFailureStage = $TransactionState.stage
                $TransactionState.recoveryScriptStackTrace = $_.ScriptStackTrace
            }
        }
        $TransactionState.recoveryErrors = @($recoveryErrors)
    }
}
