[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Prepare','Observe','Restore')][string]$Action,
    [Parameter(Mandatory)][string]$SessionDirectory
)
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$package = Join-Path $project 'artifacts\VineMix'
$uiPath = Join-Path $package 'VineMix.exe'
$enginePath = Join-Path $package 'VineMix.Engine.exe'
$settingsPath = Join-Path $env:LOCALAPPDATA 'VineMix\settings.json'
$session = [IO.Path]::GetFullPath($SessionDirectory)
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $project 'artifacts')) + [IO.Path]::DirectorySeparatorChar
if (-not $session.StartsWith($artifactRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Session evidence must stay inside project artifacts.' }
$null = New-Item -ItemType Directory -Path $session -Force
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
try { $sid = $identity.User.Value } finally { $identity.Dispose() }

function Request([string]$command, $configuration = $null, $revision = $null) {
    $pipe = [IO.Pipes.NamedPipeClientStream]::new('.', ('VineMix.Control.' + $sid), [IO.Pipes.PipeDirection]::InOut, [IO.Pipes.PipeOptions]::Asynchronous)
    $reader = $null; $writer = $null
    try {
        $pipe.Connect(3000)
        $reader = [IO.StreamReader]::new($pipe, [Text.UTF8Encoding]::new($false), $false, 4096, $true)
        $writer = [IO.StreamWriter]::new($pipe, [Text.UTF8Encoding]::new($false), 4096, $true)
        $writer.AutoFlush = $true
        $id = [Guid]::NewGuid().ToString('N')
        $message = @{ protocolVersion=1; requestId=$id; command=$command }
        if ($configuration) { $message.configuration=$configuration; $message.expectedRevision=$revision }
        $writer.WriteLine(($message | ConvertTo-Json -Compress -Depth 60))
        $response = $reader.ReadLineAsync().WaitAsync([TimeSpan]::FromSeconds(3)).GetAwaiter().GetResult() | ConvertFrom-Json
        if (-not $response.ok -or $response.requestId -ne $id) { throw "Request failed: $command $($response.error)" }
        return $response.snapshot
    } finally {
        if ($writer) { try { $writer.Dispose() } catch {} }
        if ($reader) { $reader.Dispose() }
        $pipe.Dispose()
    }
}
function CopyObject($value) { return ($value | ConvertTo-Json -Depth 60 -Compress | ConvertFrom-Json) }
function WriteEvidence([string]$name, $value) { $value | ConvertTo-Json -Depth 60 | Set-Content -LiteralPath (Join-Path $session $name) -Encoding utf8 }
function Canonical($value) {
    if ($null -eq $value) { return 'null' }
    if ($value -is [double] -or $value -is [single]) { if ($value -eq 0) { return '0' }; return $value.ToString('R', [Globalization.CultureInfo]::InvariantCulture) }
    if ($value -is [System.Management.Automation.PSCustomObject]) { return '{' + ((@($value.PSObject.Properties | Sort-Object Name | ForEach-Object { ($_.Name | ConvertTo-Json -Compress) + ':' + (Canonical $_.Value) })) -join ',') + '}' }
    if ($value -is [System.Collections.IEnumerable] -and $value -isnot [string]) { return '[' + ((@(foreach ($item in $value) { Canonical $item })) -join ',') + ']' }
    return ($value | ConvertTo-Json -Compress)
}
function AssertOriginalPreserved($original, $current) {
    foreach ($node in $original.graph.nodes) {
        $match = @($current.graph.nodes | Where-Object id -eq $node.id)
        if ($match.Count -ne 1 -or (Canonical $match[0]) -cne (Canonical $node)) { throw "Original node changed during testing: $($node.id)" }
    }
    foreach ($edge in $original.graph.edges) {
        $match = @($current.graph.edges | Where-Object id -eq $edge.id)
        if ($match.Count -ne 1 -or (Canonical $match[0]) -cne (Canonical $edge)) { throw "Original route changed during testing: $($edge.id)" }
    }
    $originalIds = @($original.graph.nodes.id)
    foreach ($edge in $current.graph.edges) {
        if ($edge.id -notin @($original.graph.edges.id) -and $edge.to -in $originalIds) { throw 'A temporary route enters the original workspace.' }
    }
}

$engine = @(Get-Process VineMix.Engine -ErrorAction SilentlyContinue | Where-Object Path -eq $enginePath)
if ($engine.Count -ne 1) { throw 'Expected one updated personal engine.' }
$engineId = $engine[0].Id
if ($Action -ne 'Prepare') {
    $sessionState = Get-Content -LiteralPath (Join-Path $session 'session.json') -Raw | ConvertFrom-Json
    if ($sessionState.engineId -ne $engineId) { throw 'The engine restarted since this live test session began; investigate before continuing.' }
}
$ui = @(Get-Process VineMix -ErrorAction SilentlyContinue | Where-Object Path -eq $uiPath)
if ($ui.Count -gt 1) { throw 'Unexpected controller count.' }
$changed = $false; $original = $null
try {
    if ($ui.Count) {
        $quietHash = (Get-FileHash -LiteralPath $settingsPath).Hash
        Start-Sleep -Milliseconds 1200
        if ((Get-FileHash -LiteralPath $settingsPath).Hash -cne $quietHash) { throw 'A control edit is still committing; retry after the gesture finishes.' }
        $ui[0].Kill()
        if (-not $ui[0].WaitForExit(5000)) { throw 'Controller did not release its connection.' }
    }
    $before = Request 'snapshot'
    if ($Action -eq 'Prepare') {
        if (Test-Path -LiteralPath (Join-Path $session 'original.json')) { throw 'Original workspace backup already exists; use another session directory.' }
        $original = CopyObject $before.configuration
        WriteEvidence 'original.json' $original
        WriteEvidence 'before.json' $before
        Copy-Item -LiteralPath $settingsPath -Destination (Join-Path $session 'settings-original.json')
        if (Test-Path -LiteralPath ($settingsPath + '.bak')) { Copy-Item -LiteralPath ($settingsPath + '.bak') -Destination (Join-Path $session 'settings-original.bak') }
        $sourceIds = @($original.graph.nodes | Where-Object { $_.kind -in @('microphone','appInput','virtualInput') -and -not $_.muted } | ForEach-Object id)
        $source = $null
        for ($attempt = 0; $attempt -lt 10; $attempt++) {
            $sample = Request 'snapshot'
            $source = $sample.status.nodes | Where-Object { $_.id -in $sourceIds -and $_.peak -gt 0.000001 } | Sort-Object peak -Descending | Select-Object -First 1
            if ($source) { break }
            Start-Sleep -Milliseconds 100
        }
        if (-not $source) { throw 'No existing input currently has measurable signal. No test audio was generated.' }
        $test = CopyObject $original
        if ($test.graph.nodes.Count -gt 25) { throw 'Insufficient room for seven temporary diagnostic nodes.' }
        function Node([string]$id, [string]$kind, [string]$name, [double]$x, [double]$y) {
            return [pscustomobject]@{ id=('core-check-'+$id); kind=$kind; name=$name; x=$x; y=$y; gainDb=0; muted=$false; bypass=$false }
        }
        function Edge([string]$id, [string]$from, [string]$to, [string]$kind='audio') {
            return [pscustomobject]@{ id=('core-check-edge-'+$id); from=$from; to=$to; kind=$kind; gainDb=0; muted=$false }
        }
        $pre = Node 'source' 'mix' 'CHECK · Live input' 0 1600
        $pre.gainDb=12
        $boost = Node 'boost' 'mix' 'CHECK · Detector / boost' 400 1600
        $boost.gainDb=12
        $gate = Node 'gate' 'expander' 'CHECK · Expander / Gate' 0 2000
        $duck = Node 'duck' 'compressor' 'CHECK · Ducking' 400 2000
        $limit = Node 'limit' 'compressor' 'CHECK · Limit' 800 2000
        $delay = Node 'delay' 'alignmentDelay' 'CHECK · Alignment Delay' 1200 2000
        $silent = Node 'silent' 'mix' 'CHECK · Silent detector' 800 1600
        $test.graph.nodes = @($test.graph.nodes) + @($pre,$boost,$gate,$duck,$limit,$delay,$silent)
        $test.graph.edges = @($test.graph.edges) + @(
            (Edge 'source' $source.id $pre.id), (Edge 'boost' $pre.id $boost.id),
            (Edge 'gate' $pre.id $gate.id), (Edge 'duck-programme' $pre.id $duck.id),
            (Edge 'limit' $boost.id $limit.id), (Edge 'delay' $pre.id $delay.id)
        )
        $test.graph.panX=20; $test.graph.panY=-1470; $test.graph.zoom=.75
        AssertOriginalPreserved $original $test
        $changed=$true
        $after = Request 'configure' $test $before.configuration.revision
        WriteEvidence 'prepared.json' $after
        WriteEvidence 'session.json' ([ordered]@{ engineId=$engineId; sourceId=$source.id; sourcePeak=$source.peak; prepared=(Get-Date).ToString('o'); testIds=@($pre.id,$boost.id,$gate.id,$duck.id,$limit.id,$delay.id,$silent.id); noNewOutput=$true })
    } elseif ($Action -eq 'Observe') {
        $original = Get-Content -LiteralPath (Join-Path $session 'original.json') -Raw | ConvertFrom-Json
        AssertOriginalPreserved $original $before.configuration
        $observations = @()
        for ($i=0; $i -lt 15; $i++) { $observations += Request 'snapshot'; Start-Sleep -Milliseconds 50 }
        WriteEvidence ('observed-'+(Get-Date -Format 'HHmmss')+'.json') $observations
        $observations[-1].status.nodes | Where-Object id -like 'core-check-*' | Select-Object id,peak,gainReductionDb,detectorDb,dynamicsFrame,@{n='state';e={$_.path.state}} | Format-Table -AutoSize
    } else {
        $original = Get-Content -LiteralPath (Join-Path $session 'original.json') -Raw | ConvertFrom-Json
        AssertOriginalPreserved $original $before.configuration
        WriteEvidence 'before-restoration.json' $before
        $restore = CopyObject $original
        $restore.revision=$before.configuration.revision
        $changed=$true
        $after = Request 'configure' $restore $before.configuration.revision
        $expected=CopyObject $original; $expected.revision=$after.configuration.revision
        if ((Canonical $expected) -cne (Canonical $after.configuration)) { throw 'Restored configuration differs beyond its new committed revision.' }
        Start-Sleep -Milliseconds 250
        $after = Request 'snapshot'
        if ($after.status.audioFrames -le $before.status.audioFrames) { throw 'Audio frame counter did not advance.' }
        $initial = Get-Content -LiteralPath (Join-Path $session 'before.json') -Raw | ConvertFrom-Json
        $previouslyRunning = @($initial.status.nodes | Where-Object { $_.path.state -eq 'Running' } | ForEach-Object id)
        $restoredRunning = @($after.status.nodes | Where-Object { $_.path.state -eq 'Running' } | ForEach-Object id)
        if (@($previouslyRunning | Where-Object { $_ -notin $restoredRunning }).Count) { throw 'A previously running original path is not Running after restoration.' }
        WriteEvidence 'restored.json' $after
        WriteEvidence 'restoration-result.json' ([ordered]@{ passed=$true; engineId=$engineId; originalWorkspaceRestored=$true; previouslyRunningPathsRestored=$true; revision=$after.configuration.revision; nodeCount=$after.configuration.graph.nodes.Count; noEngineRestart=$true; finished=(Get-Date).ToString('o') })
    }
} catch {
    if ($Action -eq 'Prepare' -and $changed -and $original) {
        $current=Request 'snapshot'; $original.revision=$current.configuration.revision
        $null=Request 'configure' $original $current.configuration.revision
    }
    throw
} finally {
    if (-not @(Get-Process VineMix -ErrorAction SilentlyContinue | Where-Object Path -eq $uiPath).Count) {
        $opened = Start-Process -FilePath $uiPath -WorkingDirectory $package -PassThru
        $null=$opened.WaitForInputIdle(10000)
    }
    $currentEngine = @(Get-Process VineMix.Engine -ErrorAction SilentlyContinue | Where-Object Path -eq $enginePath)
    if ($currentEngine.Count -ne 1 -or $currentEngine[0].Id -ne $engineId) { throw 'Engine process changed during the test session.' }
}
