$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$package = Join-Path $project 'artifacts\VineMix'
$candidate = Join-Path $project 'artifacts\VineMix-CoreDynamics'
$evidence = Join-Path $project ('artifacts\core-dynamics-update-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$uiPath = Join-Path $package 'VineMix.exe'
$enginePath = Join-Path $package 'VineMix.Engine.exe'
$settings = Join-Path $env:LOCALAPPDATA 'VineMix\settings.json'
. (Join-Path $PSScriptRoot 'assert-desktop-data-context.ps1')
Assert-DesktopDataContext $settings
if (Get-Process AudioMixer,AudioMixer.Engine -ErrorAction SilentlyContinue) { throw 'A legacy audio session is running. No process was changed.' }
$oldUi = @(Get-Process VineMix -ErrorAction SilentlyContinue | Where-Object Path -eq $uiPath)
$oldEngine = @(Get-Process VineMix.Engine -ErrorAction SilentlyContinue | Where-Object Path -eq $enginePath)
if ($oldUi.Count -ne 1 -or $oldEngine.Count -ne 1) { throw 'Expected exactly the current personal controller and engine.' }
foreach ($file in @('VineMix.exe','VineMix.Engine.exe')) {
    if (-not (Test-Path -LiteralPath (Join-Path $candidate $file))) { throw "Candidate missing $file" }
}
$manifestPath = Join-Path $candidate 'BUILD_IDENTITY.json'
if (-not (Test-Path -LiteralPath $manifestPath)) { throw 'Candidate verification manifest is missing.' }
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if (-not $manifest.offlinePassed) { throw 'Candidate offline verification has not passed.' }
foreach ($binary in $manifest.binaries) {
    if ((Get-FileHash -LiteralPath (Join-Path $candidate $binary.name)).Hash -cne $binary.sha256) { throw 'Candidate binary changed after verification.' }
}
foreach ($runtime in @('libc++.dll','libunwind.dll','libwinpthread-1.dll')) {
    if ((Get-FileHash -LiteralPath (Join-Path $candidate $runtime)).Hash -cne (Get-FileHash -LiteralPath (Join-Path $package $runtime)).Hash) { throw 'Runtime library change requires a separate activation review.' }
}
foreach ($file in @('licenses\SpeexDSP-COPYING.txt','THIRD_PARTY_NOTICES.md','docs\EFFECTS_RESEARCH.md')) {
    if (-not (Test-Path -LiteralPath (Join-Path $candidate $file))) { throw "Candidate missing $file" }
}
New-Item -ItemType Directory -Path $evidence | Out-Null
# Complete working package backup, including all bundled guides and runtimes.
Copy-Item -LiteralPath $package -Destination (Join-Path $evidence 'package-before') -Recurse
foreach ($file in @('VineMix.exe','VineMix.Engine.exe')) {
    Copy-Item -LiteralPath (Join-Path $package $file) -Destination (Join-Path $evidence $file)
}
$result = [ordered]@{started=(Get-Date).ToString('o'); oldUiId=$oldUi[0].Id; oldEngineId=$oldEngine[0].Id; backupDirectory=$evidence; passed=$false; rolledBack=$false}
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
try { $sid = $identity.User.Value } finally { $identity.Dispose() }
function Request([string]$command) {
    $pipe = [IO.Pipes.NamedPipeClientStream]::new('.',('VineMix.Control.' + $sid),[IO.Pipes.PipeDirection]::InOut,[IO.Pipes.PipeOptions]::Asynchronous)
    $reader = $null; $writer = $null
    try {
        $pipe.Connect(3000)
        $reader = [IO.StreamReader]::new($pipe,[Text.UTF8Encoding]::new($false),$false,4096,$true)
        $writer = [IO.StreamWriter]::new($pipe,[Text.UTF8Encoding]::new($false),4096,$true); $writer.AutoFlush=$true
        $id = [Guid]::NewGuid().ToString('N')
        $writer.WriteLine((@{protocolVersion=1;requestId=$id;command=$command} | ConvertTo-Json -Compress))
        $line=$reader.ReadLineAsync().WaitAsync([TimeSpan]::FromSeconds(3)).GetAwaiter().GetResult()
        # Shutdown may close the byte pipe before its acknowledgement is read.
        # The caller verifies that the exact engine process actually exits.
        if (-not $line -and $command -eq 'shutdown') { return }
        if (-not $line) { throw 'Engine closed the connection before returning its snapshot.' }
        $response=$line | ConvertFrom-Json
        if (-not $response.ok -or $response.requestId -ne $id) { throw "Engine request failed: $command $($response.error)" }
        return $response
    } finally {
        # The engine closes its side after acknowledging shutdown. A writer's
        # final empty flush can report a broken pipe after a successful reply.
        if ($writer) { try { $writer.Dispose() } catch { } }
        if ($reader) { try { $reader.Dispose() } catch { } }
        $pipe.Dispose()
    }
}
$newEngine=$null; $newUi=$null; $replaced=$false; $stoppedEngine=$false
try {
    # Give the existing controller's pending edits a quiet interval to commit.
    $firstHash=(Get-FileHash -LiteralPath $settings).Hash
    Start-Sleep -Milliseconds 1200
    if ((Get-FileHash -LiteralPath $settings).Hash -ne $firstHash) { throw 'Settings are being edited; retry the update after the current gesture.' }
    $oldUi[0].Kill(); if (-not $oldUi[0].WaitForExit(5000)) { throw 'Controller did not stop.' }
    $before=(Request 'snapshot').snapshot
    $before | ConvertTo-Json -Depth 50 | Set-Content -LiteralPath (Join-Path $evidence 'before.json') -Encoding utf8
    Copy-Item -LiteralPath $settings -Destination (Join-Path $evidence 'settings-before.json')
    $settingsHash=(Get-FileHash -LiteralPath $settings).Hash
    # Normalize newly optional default fields only in these in-memory snapshots.
    # Never save or rewrite the user's file during activation.
    function NormalizeDefaults($configuration) {
        foreach ($node in $configuration.graph.nodes) {
            $defaults = [ordered]@{
                mono = $false
                denoiser = [pscustomobject]@{ suppressionDb = -18 }
                deEsser = [pscustomobject]@{ frequencyHz = 6000; thresholdDb = -24; reductionDb = 9 }
                plosive = [pscustomobject]@{ frequencyHz = 150; thresholdDb = -24; reductionDb = 12 }
                reverb = [pscustomobject]@{ mix = 0.2; decaySeconds = 1.2; damping = 0.5; preDelayMs = 15 }
                voiceColor = [pscustomobject]@{ tone = 0; ambience = 0 }
                expander = [pscustomobject]@{ mode = 'expander'; thresholdDb = -40; ratio = 2; attackMs = 5; holdMs = 50; releaseMs = 150; rangeDb = 60; hysteresisDb = 3 }
                alignmentDelay = [pscustomobject]@{ samples = 0 }
            }
            foreach ($name in $defaults.Keys) {
                if ($node.PSObject.Properties.Name -notcontains $name) { $node | Add-Member -NotePropertyName $name -NotePropertyValue $defaults[$name] }
            }
            foreach ($band in $node.eqBands) {
                if ($band.PSObject.Properties.Name -notcontains 'resetFrequencyHz') { $band | Add-Member -NotePropertyName resetFrequencyHz -NotePropertyValue 0 }
            }
            foreach ($field in @(@{name='mode';value='compress'},@{name='detector';value='internal'},@{name='ceilingDb';value=-1})) {
                if ($node.compressor.PSObject.Properties.Name -notcontains $field.name) { $node.compressor | Add-Member -NotePropertyName $field.name -NotePropertyValue $field.value }
            }
        }
    }
    NormalizeDefaults $before.configuration
    $beforeConfig=$before.configuration | ConvertTo-Json -Depth 50 -Compress
    $wasRunning=@($before.status.nodes | Where-Object {$_.path.state -eq 'Running'} | ForEach-Object id)
    $restart=[Diagnostics.Stopwatch]::StartNew()
    $stoppedEngine=$true; $null=Request 'shutdown'
    if (-not $oldEngine[0].WaitForExit(5000)) { throw 'Engine did not exit gracefully.' }
    $replaced=$true
    foreach ($file in @('VineMix.exe','VineMix.Engine.exe')) {
        Copy-Item -LiteralPath (Join-Path $candidate $file) -Destination (Join-Path $package $file) -Force
    }
    foreach ($folder in @('licenses','docs')) {
        $null=New-Item -ItemType Directory -Path (Join-Path $package $folder) -Force
    }
    foreach ($file in @('licenses\SpeexDSP-COPYING.txt','THIRD_PARTY_NOTICES.md','docs\EFFECTS_RESEARCH.md','docs\EQ_SPECTRUM_ANALYZER.md')) {
        Copy-Item -LiteralPath (Join-Path $candidate $file) -Destination (Join-Path $package $file) -Force
    }
    $newEngine=Start-Process -FilePath $enginePath -WorkingDirectory $package -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $evidence 'engine-stdout.log') -RedirectStandardError (Join-Path $evidence 'engine-stderr.log')
    $after=(Request 'snapshot').snapshot
    do {
        $running=@($after.status.nodes | Where-Object {$_.path.state -eq 'Running'} | ForEach-Object id)
        $missing=@($wasRunning | Where-Object {$_ -notin $running})
        if ($missing.Count -eq 0 -and $after.status.audioFrames -gt 0) { break }
        Start-Sleep -Milliseconds 100
        $after=(Request 'snapshot').snapshot
    } while ($restart.Elapsed.TotalSeconds -lt 8)
    $result.secondsToRunningSnapshot=[Math]::Round($restart.Elapsed.TotalSeconds,3)
    NormalizeDefaults $after.configuration
    $afterConfig=$after.configuration | ConvertTo-Json -Depth 50 -Compress
        # Compare objects by structure, independent of JSON property ordering.
    function Canonical($value) {
        if ($null -eq $value) { return 'null' }
        if ($value -is [double] -or $value -is [single]) {
            if ($value -eq 0) { return '0' }
            return $value.ToString('R', [Globalization.CultureInfo]::InvariantCulture)
        }
        if ($value -is [System.Management.Automation.PSCustomObject]) {
            $parts=@($value.PSObject.Properties | Sort-Object Name | ForEach-Object { ($_.Name | ConvertTo-Json -Compress) + ':' + (Canonical $_.Value) })
            return '{' + ($parts -join ',') + '}'
        }
        if ($value -is [System.Collections.IEnumerable] -and $value -isnot [string]) {
            $parts=@(foreach($item in $value) { Canonical $item }); return '[' + ($parts -join ',') + ']'
        }
        return ($value | ConvertTo-Json -Compress)
    }
    if ((Canonical $before.configuration) -cne (Canonical $after.configuration)) { throw 'Configuration changed across the restart.' }
    if ($missing.Count -gt 0) { throw 'A previously running path did not recover within eight seconds.' }
    if ((Get-FileHash -LiteralPath $settings).Hash -ne $settingsHash) { throw 'Saved settings changed during activation.' }
    if (@($after.status.nodes | Where-Object { $_.PSObject.Properties.Name -notcontains 'peakLeft' -or $_.PSObject.Properties.Name -notcontains 'peakRight' -or $_.PSObject.Properties.Name -notcontains 'channels' }).Count) { throw 'New engine did not supply stereo telemetry.' }
    $frames=$after.status.audioFrames
    Start-Sleep -Milliseconds 160
    $after=(Request 'snapshot').snapshot
    if ($after.status.audioFrames -le $frames) { throw 'New engine timeline is not advancing.' }
    if (@($after.status.nodes | Where-Object { $_.PSObject.Properties.Name -notcontains 'gainReductionDb' -or $_.PSObject.Properties.Name -notcontains 'detectorDb' -or $_.PSObject.Properties.Name -notcontains 'dynamicsFrame' }).Count) { throw 'New engine did not supply dynamics telemetry.' }
    $dynamicIds=@($after.configuration.graph.nodes | Where-Object { $_.kind -in @('compressor','deEsser','plosive') } | ForEach-Object id)
    $result.dynamicsTelemetry=@($after.status.nodes | Where-Object { $_.id -in $dynamicIds } | ForEach-Object { [ordered]@{ id=$_.id;frame=$_.dynamicsFrame;reductionDb=$_.gainReductionDb;detectorDb=$_.detectorDb } })
    $after | ConvertTo-Json -Depth 50 | Set-Content -LiteralPath (Join-Path $evidence 'after.json') -Encoding utf8
    $result.sameConfiguration=$true
    $result.settingsFileUnchanged=((Get-FileHash -LiteralPath $settings).Hash -eq $settingsHash)
    $result.revision=$after.configuration.revision
    $result.graphNodeCount=$after.configuration.graph.nodes.Count
    $result.frameCounterAdvancing=$true
    $result.previouslyRunningPathsRestored=($missing.Count -eq 0)
    $result.paths=@($after.status.nodes | ForEach-Object {[ordered]@{id=$_.id;state=$_.path.state}})
    $result.newEngineId=$newEngine.Id
    # Track the final responsive controller after its standalone self-relaunch.
    $newUi=Start-DesktopController -ExecutablePath $uiPath -WorkingDirectory $package
    $result.inputIdle=$true
    $result.newUiId=$newUi.Id
    $result.windowTitle=$newUi.MainWindowTitle
    $result.windowHandle=$newUi.MainWindowHandle.ToInt64()
    $result.uiMatchesCandidate=((Get-FileHash -LiteralPath $uiPath).Hash -eq (Get-FileHash -LiteralPath (Join-Path $candidate 'VineMix.exe')).Hash)
        $result.settingsUnchangedAfterOpen=((Get-FileHash -LiteralPath $settings).Hash -eq $settingsHash)
    $result.engineMatchesCandidate=((Get-FileHash -LiteralPath $enginePath).Hash -eq (Get-FileHash -LiteralPath (Join-Path $candidate 'VineMix.Engine.exe')).Hash)
    if (-not $result.uiMatchesCandidate -or -not $result.engineMatchesCandidate) { throw 'The installed application differs from the verified candidate.' }
    if (-not $result.settingsUnchangedAfterOpen) { throw 'Saved settings changed when opening the updated controller.' }
    # Publish guides and build identity only after the verified binaries recover.
    foreach ($document in @(Get-ChildItem -LiteralPath $candidate -File | Where-Object { $_.Extension -eq '.md' -or $_.Name -eq 'BUILD_IDENTITY.json' })) {
        Copy-Item -LiteralPath $document.FullName -Destination (Join-Path $package $document.Name) -Force
    }
    foreach ($document in @(Get-ChildItem -LiteralPath (Join-Path $candidate 'docs') -File -Filter '*.md')) {
        Copy-Item -LiteralPath $document.FullName -Destination (Join-Path (Join-Path $package 'docs') $document.Name) -Force
    }
    $result.buildId = $manifest.buildId
    $result.passed=$true
} catch {
    $result.error=$_.Exception.ToString()
    $rollbackErrors=[Collections.Generic.List[string]]::new()
    try {
        if ($replaced) {
            # Stop supervision before checking all exact-path engines; the UI
            # may have replaced the initially launched candidate process.
            foreach ($processPath in @($uiPath,$enginePath)) {
              foreach ($process in @(Get-Process VineMix,VineMix.Engine -ErrorAction SilentlyContinue | Where-Object Path -eq $processPath)) {
                try {
                    if (-not $process.HasExited) {
                        $process.Kill()
                        if (-not $process.WaitForExit(5000)) { throw 'Process did not stop within its rollback deadline.' }
                    }
                } catch { $rollbackErrors.Add('Stop '+$process.Id+': '+$_.Exception.Message) }
                finally { $process.Dispose() }
              }
            }
            $restored=0
            foreach ($file in @('VineMix.exe','VineMix.Engine.exe')) {
                try {
                    Copy-Item -LiteralPath (Join-Path $evidence $file) -Destination (Join-Path $package $file) -Force
                    $restored++
                } catch { $rollbackErrors.Add('Restore '+$file+': '+$_.Exception.Message) }
            }
            $result.rolledBack=($restored -eq 2)
            # Restore documentation/build identity too: never advertise a new
            # build after rolling its executables back.
            $backupPackage = Join-Path $evidence 'package-before'
            $metadata = @('BUILD_IDENTITY.json') + @(Get-ChildItem -LiteralPath $candidate -File -Filter '*.md' | ForEach-Object Name) + @(Get-ChildItem -LiteralPath (Join-Path $candidate 'docs') -File -Filter '*.md' | ForEach-Object { 'docs\' + $_.Name })
            foreach ($relative in $metadata) {
                try {
                    $target = [IO.Path]::GetFullPath((Join-Path $package $relative))
                    if (-not $target.StartsWith(([IO.Path]::GetFullPath($package) + [IO.Path]::DirectorySeparatorChar), [StringComparison]::OrdinalIgnoreCase)) { throw 'Rollback metadata target escaped the package.' }
                    $original = Join-Path $backupPackage $relative
                    if (Test-Path -LiteralPath $original) { Copy-Item -LiteralPath $original -Destination $target -Force }
                    elseif (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Force }
                } catch { $rollbackErrors.Add('Restore metadata '+$relative+': '+$_.Exception.Message) }
            }
            foreach ($file in @('VineMix.exe','VineMix.Engine.exe')) {
                if ((Get-FileHash -LiteralPath (Join-Path $package $file)).Hash -cne (Get-FileHash -LiteralPath (Join-Path $backupPackage $file)).Hash) { $rollbackErrors.Add('Rollback binary hash mismatch: '+$file); $result.rolledBack=$false }
            }
        }
    } finally {
        # Recovery still runs when one stop/copy operation fails. Preserve the
        # user's current settings; never overwrite an intervening edit.
        if ($stoppedEngine -and -not @(Get-Process VineMix.Engine -ErrorAction SilentlyContinue | Where-Object Path -eq $enginePath).Count) {
            try { $null=Start-Process -FilePath $enginePath -WorkingDirectory $package -WindowStyle Hidden -PassThru }
            catch { $rollbackErrors.Add('Restart engine: '+$_.Exception.Message) }
        }
        if (-not @(Get-Process VineMix -ErrorAction SilentlyContinue | Where-Object Path -eq $uiPath).Count) {
            try { $null=Start-DesktopController -ExecutablePath $uiPath -WorkingDirectory $package }
            catch { $rollbackErrors.Add('Restart controller: '+$_.Exception.Message) }
        }
        $result.rollbackErrors=@($rollbackErrors)
    }
} finally {
    $result.finished=(Get-Date).ToString('o')
    $result | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $evidence 'result.json') -Encoding utf8
}
$result | ConvertTo-Json -Depth 12
if (-not $result.passed) { exit 1 }
