<#
.SYNOPSIS
Proves that the Rust verification gate rejects what it must: a failing test,
a suite that vanished or was compiled out, a test count below its floor, an
unaccounted skipped test, a crashed test program and an unlisted suite.

.DESCRIPTION
Windows PowerShell 5.1 compatible. Pure: synthetic test transcripts are fed
to the helpers in rust-verification-support.ps1. Nothing is compiled, no file
is written and no application is started (one inert `cmd /c echo` checks that
a program's error output and exit code are recorded without aborting).
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'rust-verification-support.ps1')
$checks = New-Object Collections.Generic.List[string]
$passed = $false; $failure = $null
function Check([bool]$Condition, [string]$Description) {
    if (-not $Condition) { throw $Description }
    $checks.Add($Description)
}
function Transcript([int]$Passed, [int]$Failed = 0, [string[]]$Ignored = @(), [string[]]$FailedNames = @(), [int]$Filtered = 0) {
    $lines = New-Object Collections.Generic.List[string]
    $lines.Add('')
    $lines.Add('running ' + ($Passed + $Failed + $Ignored.Count) + ' tests')
    for ($index = 0; $index -lt $Passed; $index++) { $lines.Add("test module::passes_$index ... ok") }
    foreach ($name in $FailedNames) { $lines.Add("test $name ... FAILED") }
    foreach ($name in $Ignored) { $lines.Add("test $name ... ignored, a stated reason") }
    $state = if ($Failed) { 'FAILED' } else { 'ok' }
    $lines.Add('')
    $lines.Add("test result: $state. $Passed passed; $Failed failed; $($Ignored.Count) ignored; 0 measured; $Filtered filtered out; finished in 0.01s")
    , $lines.ToArray()
}
function Suite([string[]]$Lines, [int]$ExitCode = 0) {
    $observed = ConvertFrom-RustTestOutput $Lines
    $observed | Add-Member -NotePropertyName exitCode -NotePropertyValue $ExitCode
    $observed
}
function Problems([hashtable]$Observed, [string]$Expected) {
    Test-RustSuiteExpectations -Observed $Observed -Expected ($Expected | ConvertFrom-Json)
}
try {
    $parsed = ConvertFrom-RustTestOutput (Transcript 3 1 @('bench::cost') @('module::breaks'))
    Check ($parsed.resultLines -eq 1 -and -not $parsed.ok -and $parsed.passed -eq 3 -and $parsed.failed -eq 1 -and $parsed.ignored -eq 1) 'A transcript yields its result counts.'
    Check (($parsed.ignoredTests -join ',') -ceq 'bench::cost' -and ($parsed.failedTests -join ',') -ceq 'module::breaks') 'Ignored and failed tests are named.'
    Check ((ConvertFrom-RustTestOutput @()).resultLines -eq 0) 'Empty output has no result.'

    $expected = '{"model/lib/model":{"minimumPassed":3},"oracle/test/parity":{"passed":5,"ignored":["benchmark"]}}'
    $good = @{ 'model/lib/model' = (Suite (Transcript 4)); 'oracle/test/parity' = (Suite (Transcript 5 0 @('benchmark'))) }
    Check ((Problems $good $expected).Count -eq 0) 'A complete run with the expected counts and accounted-for ignored tests qualifies.'

    $bad = $good.Clone(); $bad['model/lib/model'] = Suite (Transcript 3 1 @() @('module::breaks')) 101
    $found = Problems $bad $expected
    Check (@($found -match 'model/lib/model failed 1 test').Count -eq 1 -and @($found -match 'exited with code 101').Count -eq 1) 'An injected failing test fails the run and is named.'

    $bad = $good.Clone(); $bad.Remove('oracle/test/parity')
    Check (@((Problems $bad $expected) -match 'oracle/test/parity was expected but did not run').Count -eq 1) 'A suite that vanished or was compiled out fails the run.'

    $bad = $good.Clone(); $bad['oracle/test/parity'] = Suite (Transcript 0)
    Check (@((Problems $bad $expected) -match 'oracle/test/parity passed 0 test\(s\); exactly 5 are required').Count -eq 1) 'A suite that ran zero comparisons does not count as success.'

    $bad = $good.Clone(); $bad['oracle/test/parity'] = Suite (Transcript 0 0 @('reference_sources_are_present_and_the_comparisons_are_built'))
    $found = Problems $bad $expected
    Check (@($found -match 'skipped reference_sources_are_present').Count -eq 1 -and @($found -match 'exactly 5 are required').Count -eq 1) 'The placeholder a missing oracle leaves behind is an unaccounted skip, not a pass.'

    $bad = $good.Clone(); $bad['model/lib/model'] = Suite (Transcript 2)
    Check (@((Problems $bad $expected) -match 'model/lib/model passed 2 test\(s\); at least 3 are required').Count -eq 1) 'A test count below its floor fails the run.'

    $bad = $good.Clone(); $bad['oracle/test/parity'] = Suite (Transcript 6 0 @('benchmark'))
    Check (@((Problems $bad $expected) -match 'exactly 5 are required').Count -eq 1) 'An exact count rejects more tests as well as fewer.'

    $bad = $good.Clone(); $bad['model/lib/model'] = Suite (Transcript 4 0 @('module::quietly_skipped'))
    Check (@((Problems $bad $expected) -match 'skipped module::quietly_skipped').Count -eq 1) 'A newly ignored test must be accounted for by name.'

    $bad = $good.Clone(); $bad['oracle/test/parity'] = Suite (Transcript 5)
    Check (@((Problems $bad $expected) -match 'no longer has the ignored test benchmark').Count -eq 1) 'A stale accounted-for skip is reported.'

    $bad = $good.Clone(); $bad['model/lib/model'] = Suite @('thread main panicked') 3
    $found = Problems $bad $expected
    Check (@($found -match 'printed 0 test results').Count -eq 1 -and @($found -match 'exited with code 3').Count -eq 1) 'A test program that crashed before reporting fails the run.'

    $bad = $good.Clone(); $bad['model/lib/model'] = Suite ((Transcript 4) + (Transcript 4))
    Check (@((Problems $bad $expected) -match 'printed 2 test results').Count -eq 1) 'Two results under one suite are not accepted as one.'

    $bad = $good.Clone(); $bad['model/lib/model'] = Suite (Transcript 4) 1
    Check (@((Problems $bad $expected) -match 'exited with code 1').Count -eq 1) 'A non-zero exit code fails the run even when the text looks clean.'

    $bad = $good.Clone(); $bad['model/lib/model'] = Suite (Transcript 4 0 @() @() 2)
    Check (@((Problems $bad $expected) -match 'filtered out 2 test').Count -eq 1) 'A filtered run is not a full run.'

    $bad = $good.Clone(); $bad['extra/test/new_suite'] = Suite (Transcript 1)
    Check (@((Problems $bad $expected) -match 'extra/test/new_suite ran but is not listed').Count -eq 1) 'An unlisted suite must be added to the expectations.'

    Check (@((Problems $good '{"model/lib/model":{},"oracle/test/parity":{"passed":5,"ignored":["benchmark"]}}') -match 'neither passed nor minimumPassed').Count -eq 1) 'An expectation without a count is rejected.'

    $warning = '{"reason":"compiler-message","message":{"code":{"code":"clippy::collapsible_if"},"level":"warning","message":"collapse","spans":[{"is_primary":true,"file_name":"a.rs","line_start":3}]}}'
    $denied = '{"reason":"compiler-message","message":{"code":{"code":"clippy::undocumented_unsafe_blocks"},"level":"error","message":"unsafe","spans":[{"is_primary":true,"file_name":"b.rs","line_start":9}]}}'
    $summary = Get-LintSummary @($warning, $warning, $denied, '{"reason":"build-finished","success":false}', 'not json')
    Check ($summary.warnings -eq 1 -and $summary.errors -eq 1 -and $summary.lints['clippy::collapsible_if'] -eq 1 -and $summary.lints['clippy::undocumented_unsafe_blocks'] -eq 1) 'Lint diagnostics are counted once per location, by lint and level.'

    Check ((ConvertTo-ProcessArgument 'plain') -ceq 'plain' -and (ConvertTo-ProcessArgument 'two words') -ceq '"two words"') 'Arguments with spaces are quoted.'
    Check ((ConvertTo-ProcessArgument 'C:\with space\') -ceq '"C:\with space\\"' -and (ConvertTo-ProcessArgument 'say "hi"') -ceq '"say \"hi\""') 'Trailing backslashes and quotes survive quoting.'
    $run = Invoke-RecordedProcess (Join-Path $env:SystemRoot 'System32\cmd.exe') @('/d', '/c', 'echo to-output& echo to-error 1>&2& exit 3')
    Check ($run.ExitCode -eq 3 -and @($run.Output -match 'to-output').Count -eq 1 -and @($run.Error -match 'to-error').Count -eq 1) 'A program''s error output and exit code are recorded without aborting the caller.'
    $passed = $true
} catch { $failure = $_.Exception.Message }
[pscustomobject]@{ passed = $passed; checks = $checks.Count; error = $failure } | ConvertTo-Json -Depth 3
if (-not $passed) { exit 1 }
