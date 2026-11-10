# Pure helpers for scripts\verify-rust.ps1: running a tool with its output
# recorded, reading a Rust test run, and judging it against the checked-in
# expectations. Dot-sourcing this file runs nothing.
#
# Windows PowerShell 5.1 compatible.

function ConvertTo-ProcessArgument([string]$Value) {
    # Windows command-line quoting: quote when needed, double the backslashes
    # that precede a quote, escape embedded quotes.
    if ($Value -ne '' -and $Value -notmatch '[\s"]') { return $Value }
    '"' + ($Value -replace '(\\*)"', '$1$1\"' -replace '(\\+)$', '$1$1') + '"'
}

function Invoke-RecordedProcess {
    <# Runs a program to completion and returns its exit code and both output
       streams as lines. Neither stream passes through PowerShell's error
       handling, so a tool that writes progress to standard error (Cargo does)
       cannot abort the caller. #>
    param([Parameter(Mandatory)][string]$FilePath, [string[]]$Arguments = @(), [string]$WorkingDirectory = '')
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = $FilePath
    $info.Arguments = (@($Arguments | ForEach-Object { ConvertTo-ProcessArgument $_ }) -join ' ')
    if ($WorkingDirectory) { $info.WorkingDirectory = $WorkingDirectory }
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.StandardOutputEncoding = New-Object Text.UTF8Encoding($false)
    $info.StandardErrorEncoding = New-Object Text.UTF8Encoding($false)
    $process = [Diagnostics.Process]::Start($info)
    try {
        $standardOutput = $process.StandardOutput.ReadToEndAsync()
        $standardError = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $split = { param($text) if ($text) { @($text -split "\r?\n") } else { @() } }
        [pscustomobject]@{
            ExitCode = $process.ExitCode
            Output = @(& $split $standardOutput.Result)
            Error = @(& $split $standardError.Result)
        }
    } finally { $process.Dispose() }
}

function ConvertFrom-RustTestOutput {
    <# Reads the standard output of one Rust test executable (or of
       `cargo test` restricted to one target). #>
    param([string[]]$Lines = @())
    $summary = [ordered]@{ resultLines = 0; ok = $true; passed = 0; failed = 0; ignored = 0; measured = 0; filteredOut = 0; ignoredTests = @(); failedTests = @() }
    $ignoredTests = New-Object Collections.Generic.List[string]
    $failedTests = New-Object Collections.Generic.List[string]
    foreach ($line in $Lines) {
        $result = [regex]::Match($line, '^test result: (\w+)\. (\d+) passed; (\d+) failed; (\d+) ignored; (\d+) measured; (\d+) filtered out')
        if ($result.Success) {
            $summary.resultLines++
            if ($result.Groups[1].Value -cne 'ok') { $summary.ok = $false }
            $summary.passed += [int]$result.Groups[2].Value
            $summary.failed += [int]$result.Groups[3].Value
            $summary.ignored += [int]$result.Groups[4].Value
            $summary.measured += [int]$result.Groups[5].Value
            $summary.filteredOut += [int]$result.Groups[6].Value
            continue
        }
        $test = [regex]::Match($line, '^test (\S+)(?: - should panic)? \.\.\. (ok|FAILED|ignored)\b')
        if ($test.Success) {
            if ($test.Groups[2].Value -ceq 'ignored') { $ignoredTests.Add($test.Groups[1].Value) }
            elseif ($test.Groups[2].Value -ceq 'FAILED') { $failedTests.Add($test.Groups[1].Value) }
        }
    }
    $summary.ignoredTests = $ignoredTests.ToArray()
    $summary.failedTests = $failedTests.ToArray()
    [pscustomobject]$summary
}

function Get-ExpectationProperty($Object, [string]$Name) {
    if ($null -ne $Object -and $Object.PSObject.Properties[$Name]) { return $Object.PSObject.Properties[$Name].Value }
    $null
}

function Test-RustSuiteExpectations {
    <# Returns the reasons a test run does not qualify; none means it does.
       $Observed: suite key -> object with exitCode and the fields
       ConvertFrom-RustTestOutput returns. $Expected: the `suites` object of
       tools\rust-test-expectations.json.

       A suite that vanished, printed no result, failed, ran fewer tests than
       its floor, or skipped a test that is not accounted for by name, each
       fails the run. So does a suite nobody listed: the expectations are
       the inventory of what qualification covers. #>
    param([Parameter(Mandatory)][hashtable]$Observed, [Parameter(Mandatory)]$Expected)
    $problems = New-Object Collections.Generic.List[string]
    $expectedKeys = @($Expected.PSObject.Properties | ForEach-Object { $_.Name })
    foreach ($key in $expectedKeys) {
        if (-not $Observed.ContainsKey($key)) { $problems.Add("$key was expected but did not run (removed, renamed or compiled out).") }
    }
    foreach ($key in @($Observed.Keys | Sort-Object)) {
        $suite = $Observed[$key]
        if ($expectedKeys -cnotcontains $key) {
            $problems.Add("$key ran but is not listed in tools\rust-test-expectations.json; add it with its test count.")
            continue
        }
        $expectation = $Expected.PSObject.Properties[$key].Value
        if ($suite.exitCode -ne 0) { $problems.Add("$key exited with code $($suite.exitCode).") }
        if ($suite.resultLines -ne 1) { $problems.Add("$key printed $($suite.resultLines) test results instead of one; it did not run as a single suite.") ; continue }
        if (-not $suite.ok -or $suite.failed -gt 0) { $problems.Add("$key failed $($suite.failed) test(s): " + (@($suite.failedTests) -join ', ')) }
        if ($suite.filteredOut -ne 0) { $problems.Add("$key filtered out $($suite.filteredOut) test(s); qualification runs every test.") }
        $exact = Get-ExpectationProperty $expectation 'passed'
        $minimum = Get-ExpectationProperty $expectation 'minimumPassed'
        if ($null -ne $exact) {
            if ($suite.passed -ne [int]$exact) { $problems.Add("$key passed $($suite.passed) test(s); exactly $exact are required.") }
        } elseif ($null -ne $minimum) {
            if ($suite.passed -lt [int]$minimum) { $problems.Add("$key passed $($suite.passed) test(s); at least $minimum are required.") }
        } else {
            $problems.Add("$key has neither passed nor minimumPassed in the expectations.")
        }
        $allowed = @(Get-ExpectationProperty $expectation 'ignored')
        if ($allowed.Count -eq 1 -and $null -eq $allowed[0]) { $allowed = @() }
        foreach ($name in @($suite.ignoredTests)) {
            if ($allowed -cnotcontains $name) { $problems.Add("$key skipped $name, which is not an accounted-for ignored test.") }
        }
        foreach ($name in $allowed) {
            if (@($suite.ignoredTests) -cnotcontains $name) { $problems.Add("$key no longer has the ignored test $name; update the expectations.") }
        }
        if ($suite.ignored -ne @($suite.ignoredTests).Count) { $problems.Add("$key reported $($suite.ignored) ignored test(s) but named $(@($suite.ignoredTests).Count).") }
    }
    , $problems.ToArray()
}

function Get-LintSummary {
    <# Counts the diagnostics in `cargo clippy --message-format=json` output by
       lint and level, each distinct location once. #>
    param([string[]]$Lines = @())
    $seen = @{}
    $counts = @{}
    $levels = @{ warning = 0; error = 0 }
    foreach ($line in $Lines) {
        if (-not $line.StartsWith('{"reason":"compiler-message"')) { continue }
        try { $record = $line | ConvertFrom-Json } catch { continue }
        $message = $record.message
        if ($null -eq $message -or $null -eq $message.code) { continue }
        $code = [string]$message.code.code
        $span = @($message.spans | Where-Object { $_.is_primary })
        $where = if ($span.Count) { '{0}:{1}' -f $span[0].file_name, $span[0].line_start } else { '?' }
        $key = $code + '|' + $where + '|' + $message.message
        if ($seen.ContainsKey($key)) { continue }
        $seen[$key] = $true
        $counts[$code] = 1 + $(if ($counts.ContainsKey($code)) { $counts[$code] } else { 0 })
        if ($levels.ContainsKey([string]$message.level)) { $levels[[string]$message.level]++ }
    }
    $lints = [ordered]@{}
    foreach ($entry in ($counts.GetEnumerator() | Sort-Object @{ Expression = 'Value'; Descending = $true }, Name)) { $lints[$entry.Name] = $entry.Value }
    [pscustomobject]@{ warnings = $levels.warning; errors = $levels.error; lints = $lints }
}
