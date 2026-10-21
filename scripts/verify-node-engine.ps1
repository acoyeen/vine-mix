[CmdletBinding()]
param([string]$EnginePath = '', [string]$ResultDirectory = '')
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $EnginePath) { $EnginePath = Join-Path $projectRoot 'build\native\VineMix.Engine.exe' }
if (-not $ResultDirectory) { $ResultDirectory = Join-Path $projectRoot ('artifacts\graph-engine-verification\' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
$EnginePath = [IO.Path]::GetFullPath($EnginePath)
$ResultDirectory = [IO.Path]::GetFullPath($ResultDirectory)
# Never configure a live personal engine. The test refuses to run alongside one
# and checks the connected engine's explicit temporary settings path before edits.
if (Get-Process -Name VineMix.Engine,AudioMixer.Engine -ErrorAction SilentlyContinue) { throw 'Close the personal engine before running this isolated, silent check.' }
New-Item -ItemType Directory -Path $ResultDirectory -Force | Out-Null
$settingsPath = Join-Path $ResultDirectory ('unassigned-' + [Guid]::NewGuid().ToString('N') + '.json')
$checks = [Collections.Generic.List[string]]::new()
$testEngine = $null
$pipe = $null
$reader = $null
$writer = $null
$identified = $false
$failure = $null
function Request([string]$command, $configuration = $null, $revision = $null) {
    $id = [Guid]::NewGuid().ToString('N')
    $request = @{ protocolVersion=1; requestId=$id; command=$command; configuration=$configuration; expectedRevision=$revision }
    $writer.WriteLine(($request | ConvertTo-Json -Depth 40 -Compress))
    $line = $reader.ReadLineAsync().WaitAsync([TimeSpan]::FromSeconds(5)).GetAwaiter().GetResult()
    if (-not $line) { throw 'The test engine closed its control connection.' }
    $response = $line | ConvertFrom-Json -AsHashtable
    if ($response.requestId -ne $id) { throw 'Unexpected response identity.' }
    return $response
}
function Require([bool]$condition, [string]$description) {
    if (-not $condition) { throw $description }
    $checks.Add($description)
}
try {
    $testEngine = Start-Process -FilePath $EnginePath -ArgumentList @('--settings', ('"' + $settingsPath + '"')) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $ResultDirectory 'stdout.log') -RedirectStandardError (Join-Path $ResultDirectory 'stderr.log')
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $pipe = [IO.Pipes.NamedPipeClientStream]::new('.', ('VineMix.Control.' + $sid), [IO.Pipes.PipeDirection]::InOut, [IO.Pipes.PipeOptions]::Asynchronous)
    $pipe.Connect(10000)
    $reader = [IO.StreamReader]::new($pipe, [Text.UTF8Encoding]::new($false), $false, 4096, $true)
    $writer = [IO.StreamWriter]::new($pipe, [Text.UTF8Encoding]::new($false), 4096, $true)
    $writer.AutoFlush = $true
    $snapshot = Request 'snapshot'
    Require ($snapshot.ok -and [IO.Path]::GetFullPath($snapshot.snapshot.settingsPath) -eq $settingsPath) 'Connected only to the isolated test configuration'
    $identified = $true
    $configuration = $snapshot.snapshot.configuration
    Require ($configuration.version -eq 2) 'Engine supplies configuration version 2'
    Require (@($configuration.graph.nodes | Where-Object { $_.device.id -or $_.device.stableId -or $_.app.executablePath -or $_.app.processId }).Count -eq 0) 'Every test source and output is unassigned; no audio stream is requested'
    $firstFrames = [ulong]$snapshot.snapshot.status.audioFrames
    Start-Sleep -Milliseconds 80
    $next = Request 'snapshot'
    Require ([ulong]$next.snapshot.status.audioFrames -gt $firstFrames) 'Hardware-independent processing advances with all devices absent'
    $configuration = $next.snapshot.configuration
    $oldRevision = [ulong]$configuration.revision
    $configuration.graph.nodes[0].x = 160
    $configuration.graph.nodes[0].gainDb = -9
    $saved = Request 'configure' $configuration $oldRevision
    Require ($saved.ok -and [ulong]$saved.snapshot.configuration.revision -eq $oldRevision + 1) 'A valid graph edit commits and advances its revision'
    Require (Test-Path -LiteralPath $settingsPath) 'The isolated graph is saved to its requested file'
    $disk = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json -AsHashtable
    Require ($disk.graph.nodes[0].x -eq 160 -and $disk.graph.nodes[0].gainDb -eq -9) 'Node layout and gain persist together'
    $stale = Request 'configure' $configuration $oldRevision
    Require (-not $stale.ok -and $stale.error -match 'revision') 'Stale edits are rejected'
    $configuration = $saved.snapshot.configuration
    $savedEdgeCount = $configuration.graph.edges.Count
    $savedRevision = $configuration.revision
    # A muted self-loop must be rejected too; enabling it later cannot create feedback.
    $configuration.graph.edges += @{id='invalid-self';from='mix-a';to='mix-a';kind='audio';gainDb=0;muted=$true}
    $invalid = Request 'configure' $configuration $configuration.revision
    Require (-not $invalid.ok) 'A cyclic graph is rejected even when its cable is muted'
    $unchanged = Request 'snapshot'
    Require ($unchanged.snapshot.configuration.graph.edges.Count -eq $savedEdgeCount -and $unchanged.snapshot.configuration.revision -eq $savedRevision) 'Rejected graph leaves the committed graph intact'
    Require ([ulong]$unchanged.snapshot.status.audioFrames -gt [ulong]$saved.snapshot.status.audioFrames) 'Processing continues across valid and rejected edits'
    $reader.Dispose(); $writer.Dispose(); $pipe.Dispose(); $reader=$null; $writer=$null; $pipe=$null
    $pipe = [IO.Pipes.NamedPipeClientStream]::new('.', ('VineMix.Control.' + $sid), [IO.Pipes.PipeDirection]::InOut, [IO.Pipes.PipeOptions]::Asynchronous)
    $pipe.Connect(5000)
    $reader = [IO.StreamReader]::new($pipe, [Text.UTF8Encoding]::new($false), $false, 4096, $true)
    $writer = [IO.StreamWriter]::new($pipe, [Text.UTF8Encoding]::new($false), 4096, $true); $writer.AutoFlush=$true
    $reconnected = Request 'snapshot'
    Require ($reconnected.ok -and $reconnected.snapshot.configuration.graph.nodes[0].gainDb -eq -9) 'A fresh control connection receives the complete committed graph'
} catch {
    $failure = $_.Exception.ToString()
} finally {
    if ($identified -and $writer -and $reader) { try { $null = Request 'shutdown' } catch {} }
    if ($reader) { try { $reader.Dispose() } catch {} }
    if ($writer) { try { $writer.Dispose() } catch {} }
    if ($pipe) { try { $pipe.Dispose() } catch {} }
    if ($testEngine) {
        if (-not $testEngine.WaitForExit(5000)) { $testEngine.Kill(); $testEngine.WaitForExit() }
        $testEngine.Dispose()
    }
    @{ passed=($null -eq $failure); checks=$checks.ToArray(); error=$failure; settingsPath=$settingsPath; scope='Silent control/graph integration with unassigned workers; no live audio qualification.' } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $ResultDirectory 'results.json') -Encoding utf8
}
Get-Content -LiteralPath (Join-Path $ResultDirectory 'results.json') -Raw
if ($failure) { exit 1 }
