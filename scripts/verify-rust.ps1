<#
.SYNOPSIS
Qualifies the Rust VineMix candidate: the pinned toolchain and target, a lint
gate, every test suite against its expected counts, the C/C++ parity oracle
and the update-record cross-check with the 0.5 serializer.

.DESCRIPTION
Windows PowerShell 5.1 and PowerShell 7 compatible. This is the Rust
counterpart of verify-offline.ps1 (which qualifies the 0.5 .NET/native
application and does not compile any Rust).

A run qualifies only if nothing was skipped. Each test target runs on its own
and must print exactly one result; its passed count is checked against
tools\rust-test-expectations.json, every ignored test must be accounted for
there by name, and a suite that disappears or appears unlisted fails the run.
Missing parity reference sources fail the build (VINEMIX_REQUIRE_ORACLE), and
the two cross-language update tests run for real against records written by
the 0.5 C# serializer.

The -Skip*/-Allow* switches exist for developer machines without the .NET SDK
or the reference sources. A run that uses one is reported as a developer run,
never as a qualification.

Everything runs on synthetic data: no audio endpoint is opened, no personal
settings are read and no application session is started. Reports go to a new
directory outside the checkout.
#>
[CmdletBinding()]
param(
    [string]$ResultDirectory = '',
    # Cargo's --target-dir; default is the workspace's own target directory.
    [string]$TargetDirectory = '',
    # Never touch the network (all crates must already be in Cargo's cache).
    [switch]$Offline,
    [switch]$SkipLint,
    [switch]$SkipSerializerInterop,
    [switch]$AllowMissingReferenceSources,
    [string]$DotnetPath = '',
    # Prints the observed suites as an expectations skeleton and stops.
    [switch]$PrintObserved
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $PSScriptRoot 'rust-env.ps1')
. (Join-Path $PSScriptRoot 'rust-verification-support.ps1')
if (-not $ResultDirectory) { $ResultDirectory = Join-Path $env:LOCALAPPDATA ('VineMix.BuildCache\rust-verification-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
$results = [IO.Path]::GetFullPath($ResultDirectory)
if ($results -ieq $project -or $results.StartsWith($project + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Choose a results directory outside the source checkout.' }
if (Test-Path -LiteralPath $results) { throw 'Choose a new results directory; prior evidence is retained.' }
$null = New-Item -ItemType Directory -Path $results
$utf8 = New-Object Text.UTF8Encoding($false)
$shell = (Get-Process -Id $PID).Path
$started = [DateTime]::UtcNow
$problems = New-Object Collections.Generic.List[string]
$skipped = New-Object Collections.Generic.List[string]
$steps = New-Object Collections.Generic.List[object]
$suites = @{}
$lint = $null; $interop = $null; $documentation = $null; $source = $null; $toolchain = $null
$oldPath = $env:PATH
$oldEnvironment = @{}
foreach ($name in @('VINEMIX_REQUIRE_ORACLE', 'VINEMIX_INTEROP_DIR', 'CARGO_NET_OFFLINE', 'VINEMIX_WINDRES')) { $oldEnvironment[$name] = [Environment]::GetEnvironmentVariable($name) }

function Save-Log([string]$Name, $Run) {
    $path = Join-Path $results $Name
    $null = New-Item -ItemType Directory -Force -Path (Split-Path -Parent $path)
    [IO.File]::WriteAllLines($path, [string[]](@($Run.Output) + @('--- standard error ---') + @($Run.Error)), $utf8)
}
function Invoke-Step([string]$Name, [scriptblock]$Action) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $before = $problems.Count
    try { & $Action } catch { $problems.Add($Name + ': ' + $_.Exception.Message) }
    $steps.Add([ordered]@{ step = $Name; passed = ($problems.Count -eq $before); seconds = [Math]::Round($timer.Elapsed.TotalSeconds, 1) })
    Write-Host ('{0,-34} {1}  {2,6:N1} s' -f $Name, $(if ($problems.Count -eq $before) { 'ok    ' } else { 'FAILED' }), $timer.Elapsed.TotalSeconds)
}

try {
    $toolchain = Initialize-RustEnvironment -Project $project
    $source = & (Join-Path $PSScriptRoot 'get-rust-source-identity.ps1') -Project $project
    $common = @('--locked', '--target', $toolchain.target)
    if ($Offline) { $common += '--offline'; $env:CARGO_NET_OFFLINE = 'true' }
    if ($TargetDirectory) { $common += @('--target-dir', [IO.Path]::GetFullPath($TargetDirectory)) }
    if ($AllowMissingReferenceSources) {
        Remove-Item Env:VINEMIX_REQUIRE_ORACLE -ErrorAction SilentlyContinue
        $skipped.Add('Parity oracle requirement (-AllowMissingReferenceSources)')
    } else { $env:VINEMIX_REQUIRE_ORACLE = '1' }
    Remove-Item Env:VINEMIX_INTEROP_DIR -ErrorAction SilentlyContinue

    Invoke-Step 'Verification tool self-tests' {
        # The gate itself must reject injected failures before it is trusted.
        $run = Invoke-RecordedProcess $shell @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $PSScriptRoot 'test-verify-rust.ps1')) $project
        Save-Log 'tools\test-verify-rust.txt' $run
        if ($run.ExitCode -ne 0) { throw 'The verification helpers failed their own checks.' }
        $run = Invoke-RecordedProcess $shell @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $PSScriptRoot 'test-rust-source-identity.ps1'), '-ResultDirectory', (Join-Path $results 'tools\source-identity')) $project
        Save-Log 'tools\test-rust-source-identity.txt' $run
        if ($run.ExitCode -ne 0) { throw 'The source identity script failed its fixture checks.' }
        $run = Invoke-RecordedProcess 'py' @('-3', (Join-Path $project 'tools\test_rust_notices.py')) $project
        Save-Log 'tools\test_rust_notices.txt' $run
        if ($run.ExitCode -ne 0) { throw 'The notice generator failed its fixture checks.' }
    }
    Invoke-Step 'Third-party notices are current' {
        $run = Invoke-RecordedProcess 'py' @('-3', (Join-Path $project 'tools\rust-notices.py'), '--check') $project
        Save-Log 'tools\rust-notices-check.txt' $run
        if ($run.ExitCode -ne 0) { throw 'licenses\Rust-crates.txt does not match the locked dependencies, or a licence could not be resolved.' }
    }
    if ($SkipLint) { $skipped.Add('Lint gate (-SkipLint)') } else {
        Invoke-Step 'Lint gate' {
            # Correctness is denied by Clippy itself. Suspicious and performance
            # lints, undocumented unsafe blocks and unused code are denied here.
            # Style and complexity lints are counted in the report, not denied.
            $denied = @('clippy::correctness', 'clippy::suspicious', 'clippy::perf', 'clippy::undocumented_unsafe_blocks', 'unused')
            $arguments = @('clippy') + $common + @('--workspace', '--all-targets', '--message-format=json', '--')
            foreach ($name in $denied) { $arguments += @('-D', $name) }
            $run = Invoke-RecordedProcess 'cargo' $arguments $project
            $summary = Get-LintSummary $run.Output
            $script:lint = [ordered]@{ command = ('cargo ' + (($arguments | Where-Object { $_ -ne '--message-format=json' }) -join ' ')); denied = $denied; exitCode = $run.ExitCode; warnings = $summary.warnings; errors = $summary.errors; lints = $summary.lints }
            [IO.File]::WriteAllLines((Join-Path $results 'lint-stderr.txt'), [string[]]@($run.Error), $utf8)
            if ($run.ExitCode -ne 0 -or $summary.errors -gt 0) { throw ('Clippy reported ' + $summary.errors + ' denied diagnostic(s) (exit code ' + $run.ExitCode + ').') }
        }
    }
    Invoke-Step 'Build every test target' {
        $run = Invoke-RecordedProcess 'cargo' (@('test') + $common + @('--workspace', '--no-run')) $project
        Save-Log 'build-tests.txt' $run
        if ($run.ExitCode -ne 0) { throw 'The workspace test build failed. See build-tests.txt.' }
    }
    Invoke-Step 'Test suites' {
        $arguments = @('metadata', '--no-deps', '--format-version', '1', '--locked')
        if ($Offline) { $arguments += '--offline' }
        $run = Invoke-RecordedProcess 'cargo' $arguments $project
        if ($run.ExitCode -ne 0) { throw 'cargo metadata failed.' }
        $metadata = ($run.Output -join "`n") | ConvertFrom-Json
        foreach ($package in @($metadata.packages | Sort-Object name)) {
            foreach ($target in @($package.targets | Sort-Object name)) {
                if (-not $target.test) { continue }
                $kinds = @($target.kind)
                $selector = if ($kinds -contains 'test') { @('test', '--test', $target.name) }
                    elseif ($kinds -contains 'bin') { @('bin', '--bin', $target.name) }
                    elseif (@($kinds | Where-Object { $_ -match 'lib$' }).Count) { @('lib', '--lib') }
                    else { $null }
                if (-not $selector) { continue }
                $key = '{0}/{1}/{2}' -f $package.name, $selector[0], $target.name
                $run = Invoke-RecordedProcess 'cargo' (@('test') + $common + @('-p', $package.name) + $selector[1..($selector.Count - 1)]) $project
                Save-Log ('suites\' + ($key -replace '[\\/]', '.') + '.txt') $run
                $observed = ConvertFrom-RustTestOutput $run.Output
                $observed | Add-Member -NotePropertyName exitCode -NotePropertyValue $run.ExitCode
                $suites[$key] = $observed
            }
        }
        if ($suites.Count -eq 0) { throw 'No test target was found.' }
    }
    if ($PrintObserved) {
        $skeleton = [ordered]@{}
        foreach ($key in @($suites.Keys | Sort-Object)) {
            $entry = [ordered]@{ minimumPassed = $suites[$key].passed }
            if (@($suites[$key].ignoredTests).Count) { $entry.ignored = @($suites[$key].ignoredTests) }
            if ($suites[$key].exitCode -ne 0 -or $suites[$key].failed -gt 0) { $entry.note = 'FAILED in this run' }
            $skeleton[$key] = $entry
        }
        [ordered]@{ suites = $skeleton } | ConvertTo-Json -Depth 5
        throw 'Observed suites were printed; nothing was qualified.'
    }
    Invoke-Step 'Expected suites and counts' {
        $expectations = Get-Content -LiteralPath (Join-Path $project 'tools\rust-test-expectations.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        foreach ($problem in (Test-RustSuiteExpectations -Observed $suites -Expected $expectations.suites)) {
            if ($AllowMissingReferenceSources -and $problem.StartsWith('vinemix-oracle/')) { $skipped.Add('Parity oracle: ' + $problem) }
            else { $problems.Add($problem) }
        }
    }
    Invoke-Step 'Documentation tests' {
        $run = Invoke-RecordedProcess 'cargo' (@('test') + $common + @('--workspace', '--doc')) $project
        Save-Log 'doc-tests.txt' $run
        $observed = ConvertFrom-RustTestOutput $run.Output
        $script:documentation = [ordered]@{ exitCode = $run.ExitCode; passed = $observed.passed; failed = $observed.failed; ignored = $observed.ignored }
        if ($run.ExitCode -ne 0 -or $observed.failed -gt 0) { throw 'A documentation test failed. See doc-tests.txt.' }
    }
    if ($SkipSerializerInterop) { $skipped.Add('0.5 serializer cross-check (-SkipSerializerInterop)') } else {
        Invoke-Step '0.5 serializer cross-check' {
            if (-not $DotnetPath) { $DotnetPath = Join-Path $project '.tools\dotnet\dotnet.exe' }
            if (-not (Test-Path -LiteralPath $DotnetPath -PathType Leaf)) { throw 'The pinned .NET SDK is missing (scripts\bootstrap-tools.ps1). Qualification needs the 0.5 serializer; -SkipSerializerInterop makes this a developer run.' }
            $exchange = Join-Path $results 'interop\exchange'
            $binaries = Join-Path $results 'interop\bin'
            $null = New-Item -ItemType Directory -Force -Path $exchange
            $oldRoot = $env:DOTNET_ROOT
            try {
                $env:DOTNET_ROOT = Split-Path -Parent ([IO.Path]::GetFullPath($DotnetPath))
                $run = Invoke-RecordedProcess $DotnetPath @('build', (Join-Path $project 'desktop\VineMix.UpdateWorkerTests\VineMix.UpdateWorkerTests.csproj'), '-c', 'Release', '--nologo', '-v', 'q',
                    ('-p:VineMixBuildCacheRoot=' + (Join-Path $results 'interop\cache')), '-o', $binaries) $project
                Save-Log 'interop\build.txt' $run
                if ($run.ExitCode -ne 0) { throw 'The 0.5 update worker tests did not build.' }
                $tool = Join-Path $binaries 'VineMix.UpdateWorkerTests.exe'
                $write = Invoke-RecordedProcess $tool @('--serializer-interop-write', $exchange) $project
                Save-Log 'interop\write.txt' $write
                if ($write.ExitCode -ne 0 -or -not (@($write.Output) -match '^PASS: ')) { throw 'The 0.5 serializer could not write its records.' }
                $env:VINEMIX_INTEROP_DIR = $exchange
                $rust = Invoke-RecordedProcess 'cargo' (@('test') + $common + @('-p', 'vinemix-ui', '--lib', 'update::tests::interop', '--', '--ignored')) $project
                Remove-Item Env:VINEMIX_INTEROP_DIR -ErrorAction SilentlyContinue
                Save-Log 'interop\rust.txt' $rust
                $observed = ConvertFrom-RustTestOutput $rust.Output
                if ($rust.ExitCode -ne 0 -or $observed.resultLines -ne 1 -or $observed.passed -ne 2 -or $observed.failed -ne 0 -or $observed.ignored -ne 0) {
                    throw ('The Rust side must run exactly its two cross-language tests; it passed ' + $observed.passed + ', failed ' + $observed.failed + ', ignored ' + $observed.ignored + '.')
                }
                $read = Invoke-RecordedProcess $tool @('--serializer-interop-read', $exchange) $project
                Save-Log 'interop\read.txt' $read
                if ($read.ExitCode -ne 0 -or -not (@($read.Output) -match '^PASS: ')) { throw 'The 0.5 reader rejected a record written by the Rust serializer.' }
                $script:interop = [ordered]@{ rustTestsPassed = $observed.passed; legacyWrite = (@($write.Output) -match '^PASS: ')[0]; legacyRead = (@($read.Output) -match '^PASS: ')[0] }
            } finally { $env:DOTNET_ROOT = $oldRoot; Remove-Item Env:VINEMIX_INTEROP_DIR -ErrorAction SilentlyContinue }
        }
    }
} catch {
    $problems.Add('Verification could not complete: ' + $_.Exception.Message)
} finally {
    $env:PATH = $oldPath
    foreach ($name in $oldEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $oldEnvironment[$name]) }
}
if ($PrintObserved) {
    # The skeleton is a starting point for tools\rust-test-expectations.json,
    # never a result. Anything that went wrong is still reported.
    $real = @($problems | Where-Object { $_ -notlike '*Observed suites were printed*' })
    foreach ($problem in $real) { Write-Host ('PROBLEM: ' + $problem) }
    if ($real.Count) { exit 1 }
    return
}

$passed = ($problems.Count -eq 0)
$suiteReport = [ordered]@{}
$totalPassed = 0; $totalIgnored = 0
foreach ($key in @($suites.Keys | Sort-Object)) {
    $suite = $suites[$key]
    $totalPassed += $suite.passed; $totalIgnored += $suite.ignored
    $suiteReport[$key] = [ordered]@{ exitCode = $suite.exitCode; passed = $suite.passed; failed = $suite.failed; ignored = $suite.ignored; ignoredTests = @($suite.ignoredTests); failedTests = @($suite.failedTests) }
}
$report = [ordered]@{
    passed = $passed
    # True only when every gate ran. A developer run can pass without qualifying.
    qualification = ($passed -and $skipped.Count -eq 0)
    startedUtc = $started.ToString('o')
    finishedUtc = [DateTime]::UtcNow.ToString('o')
    scope = 'Rust workspace: pinned toolchain and target, lint gate, every test target with expected counts, C/C++ parity oracle, 0.5 update-serializer cross-check. Synthetic data only: no audio endpoint, personal settings, application session, installer or published release. Physical devices, installed upgrades, listening and assistive technology are outside this script.'
    source = $(if ($source) { [ordered]@{ gitHead = $source.gitHead; gitDirty = $source.gitDirty; inputsDirty = $source.inputsDirty; sourceSnapshotSha256 = $source.sourceSnapshotSha256; fileCount = $source.fileCount } } else { $null })
    toolchain = $(if ($toolchain) { [ordered]@{ target = $toolchain.target; rustRelease = $toolchain.rustRelease; rustc = $toolchain.rustc; cargo = $toolchain.cargo; rustHost = $toolchain.rustHost; llvmMingw = $toolchain.llvmMingw; clang = $toolchain.clang } } else { $null })
    offline = [bool]$Offline
    steps = $steps.ToArray()
    lint = $lint
    testsPassed = $totalPassed
    testsIgnored = $totalIgnored
    suites = $suiteReport
    documentationTests = $documentation
    serializerInterop = $interop
    skipped = $skipped.ToArray()
    problems = $problems.ToArray()
}
[IO.File]::WriteAllText((Join-Path $results 'results.json'), ($report | ConvertTo-Json -Depth 8), $utf8)
if ($env:GITHUB_STEP_SUMMARY) {
    $summary = New-Object Collections.Generic.List[string]
    $summary.Add('### Rust candidate verification')
    $summary.Add('')
    $summary.Add("Passed: **$passed**. Qualification (nothing skipped): **$($report.qualification)**. Tests passed: **$totalPassed**; accounted-for ignored tests: **$totalIgnored**.")
    if ($source) { $summary.Add("Source ``$($source.gitHead)``, dirty: $($source.gitDirty), input snapshot ``$($source.sourceSnapshotSha256)``.") }
    if ($toolchain) { $summary.Add("Rust $($toolchain.rustRelease), target ``$($toolchain.target)``, $($toolchain.llvmMingw).") }
    foreach ($problem in $problems) { $summary.Add('- ' + $problem) }
    [IO.File]::AppendAllLines($env:GITHUB_STEP_SUMMARY, [string[]]$summary.ToArray(), $utf8)
}
Write-Host ''
Write-Host ('Tests passed: {0}; ignored and accounted for: {1}; suites: {2}' -f $totalPassed, $totalIgnored, $suites.Count)
foreach ($entry in $skipped) { Write-Host ('SKIPPED: ' + $entry) }
foreach ($problem in $problems) { Write-Host ('PROBLEM: ' + $problem) }
Write-Host ('Report: ' + (Join-Path $results 'results.json'))
if (-not $passed) { throw 'Rust verification failed. See the problems above and the recorded reports.' }
if ($skipped.Count) { Write-Host 'Passed as a developer run. It is not a qualification because steps were skipped.' }
else { Write-Host 'Rust candidate verification passed with every gate exercised.' }
