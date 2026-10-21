[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$RenderEndpoint,
    [string]$ProbePath = (Join-Path $PSScriptRoot '..\build\native\VineMix.LiveAudioProbe.exe'),
    [string]$ResultDirectory = (Join-Path $PSScriptRoot ('..\artifacts\qualification\process-capture-' + [Guid]::NewGuid().ToString('N')))
)

# Only a private synthetic renderer is captured. This script does not contact the
# running mixer, change defaults, launch UI, record a microphone or save samples.
# Confirm the selected virtual cable is not routed into an active conversation.
$ErrorActionPreference = 'Stop'
$probeFullPath = [IO.Path]::GetFullPath($ProbePath)
$resultFullPath = [IO.Path]::GetFullPath($ResultDirectory)
if (-not (Test-Path -LiteralPath $probeFullPath -PathType Leaf)) { throw "Probe executable missing: $probeFullPath" }
if (Test-Path -LiteralPath $resultFullPath) { throw 'Use a new result directory to prevent stale synchronization files.' }
[void][IO.Directory]::CreateDirectory($resultFullPath)
$renderGate = Join-Path $resultFullPath 'render-start.gate'
$renderMarker = Join-Path $resultFullPath 'renderer-ready.json'
$captureMarker = Join-Path $resultFullPath 'capture-ready.json'
$renderResult = Join-Path $resultFullPath 'renderer.json'
$captureResult = Join-Path $resultFullPath 'capture.json'
$children = [Collections.Generic.List[object]]::new()
$outcome = [ordered]@{ pass = $false; test = 'Synthetic sibling-process WASAPI loopback'; resultDirectory = $resultFullPath }

function Start-Probe([string]$Label, [string[]]$Arguments) {
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = $probeFullPath
    $info.WorkingDirectory = Split-Path -Parent $probeFullPath
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    foreach ($argument in $Arguments) { [void]$info.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($info)
    try { $process.PriorityClass = [Diagnostics.ProcessPriorityClass]::BelowNormal } catch { }
    $child = [pscustomobject]@{ Label = $Label; Process = $process; OutputTask = $process.StandardOutput.ReadToEndAsync(); ErrorTask = $process.StandardError.ReadToEndAsync() }
    $children.Add($child)
    return $child
}

function Wait-Marker([string]$Path, [string]$Stage, $Child) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ($watch.Elapsed.TotalSeconds -lt 22) {
        if (Test-Path -LiteralPath $Path) {
            try {
                $marker = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
                if ($marker.stage -eq $Stage) { return $marker }
            } catch { } # A marker may be observed during its brief write.
        }
        if ($Child.Process.HasExited) { throw "$($Child.Label) exited before readiness (exit $($Child.Process.ExitCode))." }
        Start-Sleep -Milliseconds 50
    }
    throw "Timed out waiting for $Stage."
}

try {
    $renderer = Start-Probe 'renderer' @('--run', '--render-only', '--render', $RenderEndpoint,
        '--start-file', $renderGate, '--ready-file', $renderMarker, '--seconds', '6', '--output', $renderResult)
    $marker = Wait-Marker $renderMarker 'validated; no audio streams opened' $renderer
    if ($marker.processId -ne $renderer.Process.Id) { throw 'Renderer identity did not match its readiness marker.' }
    $capture = Start-Probe 'capture' @('--run', '--capture-only', '--process-target', [string]$renderer.Process.Id,
        '--process-path', $probeFullPath, '--seconds', '8', '--ready-file', $captureMarker, '--output', $captureResult)
    $captureReady = Wait-Marker $captureMarker 'capture-ready' $capture
    if ($captureReady.processId -ne $capture.Process.Id) { throw 'Capture identity did not match its readiness marker.' }
    [IO.File]::WriteAllText($renderGate, 'Start this synthetic renderer only.')
    foreach ($child in $children) {
        if (-not $child.Process.WaitForExit(20000)) { throw "$($child.Label) did not exit within its bounded diagnostic duration." }
    }
    $render = Get-Content -LiteralPath $renderResult -Raw | ConvertFrom-Json
    $received = Get-Content -LiteralPath $captureResult -Raw | ConvertFrom-Json
    $outcome.renderer = $render
    $outcome.capture = $received
    $outcome.pass = [bool]($render.pass -and $received.pass -and $renderer.Process.ExitCode -eq 0 -and $capture.Process.ExitCode -eq 0)
    if ($received.captures.Count -gt 0 -and $null -ne $render.injectionClockSeconds) {
        $outcome.approximateLatencyMs = [ordered]@{
            left = ($received.captures[0].left.arrivalClockSeconds - $render.injectionClockSeconds) * 1000
            right = ($received.captures[0].right.arrivalClockSeconds - $render.injectionClockSeconds) * 1000
            note = 'Submission-to-capture-drain, includes diagnostic buffering; not calibrated acoustic latency.'
        }
    }
} catch {
    $outcome.error = $_.Exception.Message
} finally {
    foreach ($child in $children) {
        if (-not $child.Process.HasExited) {
            # Kill only the exact process objects created by this invocation.
            try { $child.Process.Kill(); [void]$child.Process.WaitForExit(5000) } catch { }
        }
        try { [IO.File]::WriteAllText((Join-Path $resultFullPath ($child.Label + '.stdout.txt')), $child.OutputTask.GetAwaiter().GetResult()) } catch { }
        try { [IO.File]::WriteAllText((Join-Path $resultFullPath ($child.Label + '.stderr.txt')), $child.ErrorTask.GetAwaiter().GetResult()) } catch { }
        $child.Process.Dispose()
    }
    $outcome | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $resultFullPath 'results.json') -Encoding utf8
}
$outcome | ConvertTo-Json -Depth 20
if (-not $outcome.pass) { throw "Process capture verification failed. See $resultFullPath\results.json" }
