[CmdletBinding()]
param([Parameter(Mandatory)][string]$Verifier, [Parameter(Mandatory)][string]$ResultDirectory)
$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $ResultDirectory) { throw 'Choose a new policy-test evidence directory.' }
$null = New-Item -ItemType Directory -Path $ResultDirectory
$checks = [Collections.Generic.List[object]]::new()
foreach ($mode in @('strict', 'report')) {
    foreach ($fixture in @('under', 'over', 'reset', 'functional', 'allocation', 'invalid')) {
        $name = "$mode-$fixture"
        $reportPath = Join-Path $ResultDirectory ($name + '.json')
        $output = & $Verifier --benchmark-policy-fixture $mode $fixture $reportPath 2>&1
        $code = $LASTEXITCODE
        $output | Set-Content -LiteralPath (Join-Path $ResultDirectory ($name + '.txt')) -Encoding utf8
        $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
        $correctnessFailure = $fixture -in @('functional', 'allocation', 'invalid')
        $over = $fixture -in @('over', 'reset') -or ($mode -eq 'report' -and $fixture -in @('functional', 'allocation'))
        $expectedCode = if ($correctnessFailure -or ($mode -eq 'strict' -and $over)) { 1 } else { 0 }
        $expectedFailure = if ($correctnessFailure) { 'correctness' } elseif ($expectedCode) { 'budget' } else { '' }
        $expectedCases = if ($fixture -eq 'invalid') { 0 } else { 1 }
        $passed = $code -eq $expectedCode -and $report.budgetUs -eq 5000 -and
            $report.mode -eq $(if ($mode -eq 'report') { 'hosted-report' } else { 'strict' }) -and
            $report.completed -eq ($expectedCode -eq 0) -and $report.functionalChecksPassed -eq (-not $correctnessFailure) -and
            $report.budgetExceeded -eq $over -and $report.failureKind -eq $expectedFailure -and $report.cases.Count -eq $expectedCases
        if ($fixture -eq 'over') {
            $passed = $passed -and $report.cases[0].combinedMedianUs -eq 5343.2 -and $report.cases[0].combinedP99Us -eq 9620.1
        }
        if ($fixture -eq 'allocation') { $passed = $passed -and $report.error -like '*allocation failure*' }
        if ($fixture -eq 'functional') { $passed = $passed -and $report.error -like '*correctness failure*' }
        $checks.Add([ordered]@{name=$name;passed=$passed;exitCode=$code;expectedExitCode=$expectedCode})
    }
    $reportFlag=if ($mode -eq 'report') { '--benchmark-report' } else { '--benchmark-json' }
    $output=& $Verifier $reportFlag '' 2>&1
    $code=$LASTEXITCODE
    $output | Set-Content -LiteralPath (Join-Path $ResultDirectory ($mode + '-empty-path.txt')) -Encoding utf8
    $checks.Add([ordered]@{name="$mode-empty-path";passed=($code -eq 2);exitCode=$code;expectedExitCode=2})
}
$passed = @($checks | Where-Object { -not $_.passed }).Count -eq 0
[ordered]@{passed=$passed;scope='Deterministic durations and deliberately injected assertion/allocation failures; no measured benchmark, audio devices or settings';checks=$checks} |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $ResultDirectory 'results.json') -Encoding utf8
Get-Content -LiteralPath (Join-Path $ResultDirectory 'results.json')
if (-not $passed) { throw 'Creative benchmark policy regression failed.' }
