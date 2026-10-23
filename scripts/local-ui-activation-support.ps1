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

function Restore-UiActivationBinary([string]$Backup, [string]$Target, [string]$OriginalHash, [string]$CandidateHash) {
    if ((Get-FileHash -LiteralPath $Backup -Algorithm SHA256).Hash -cne $OriginalHash.ToUpperInvariant() -or
        (Get-FileHash -LiteralPath $Target -Algorithm SHA256).Hash -notin @($OriginalHash.ToUpperInvariant(),$CandidateHash.ToUpperInvariant())) {
        throw 'A desktop executable changed independently; rollback will not overwrite it.'
    }
    Replace-ActivationBinary $Backup $Target $OriginalHash
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
    } catch {
        $TransactionState.error = $_.Exception.ToString()
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
            } catch { $recoveryErrors.Add($_.Exception.Message) }
        }
        $TransactionState.recoveryErrors = @($recoveryErrors)
    }
}
