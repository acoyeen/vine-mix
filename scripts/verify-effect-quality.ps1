[CmdletBinding()]
param([switch]$SkipBuild, [string]$ResultDirectory = '')
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$buildRoot = Join-Path $projectRoot 'build\native'
$compilerRoot = Join-Path $projectRoot '.tools\llvm-mingw-20261006-ucrt-x86_64\bin'
$cmake = Join-Path $projectRoot '.tools\cmake-4.4.4-windows-x86_64\bin\cmake.exe'
if (-not $ResultDirectory) {
    $ResultDirectory = Join-Path $projectRoot ('artifacts\effect-quality-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
$ResultDirectory = [IO.Path]::GetFullPath($ResultDirectory)
if (Test-Path -LiteralPath $ResultDirectory) { throw 'Choose a new results directory; existing evidence is never overwritten.' }
$null = New-Item -ItemType Directory -Path $ResultDirectory
$targets = @('VineMix.DeEsserQualityTests','VineMix.EffectsQualityTests')
$previousPath = $env:PATH
$results = [Collections.Generic.List[object]]::new()
try {
    $env:PATH = "$compilerRoot;$previousPath"
    if (-not $SkipBuild) {
        & $cmake -S $projectRoot -B $buildRoot -G 'MinGW Makefiles' '-DCMAKE_BUILD_TYPE=Release' '-DCMAKE_C_COMPILER=clang.exe' '-DCMAKE_CXX_COMPILER=clang++.exe' '-DCMAKE_MAKE_PROGRAM=mingw32-make.exe' '-DCMAKE_POLICY_VERSION_MINIMUM=3.5' '-DBUILD_TESTING=OFF' '-DAUDIO_MIXER_OFFLINE_TESTS=ON'
        if ($LASTEXITCODE -ne 0) { throw 'Quality-test configuration failed.' }
        & $cmake --build $buildRoot --parallel 4 --target @targets
        if ($LASTEXITCODE -ne 0) { throw 'Quality-test build failed.' }
    }
    foreach ($target in $targets) {
        $path = Join-Path $buildRoot ($target + '.exe')
        if (-not (Test-Path -LiteralPath $path)) { throw "Missing test executable: $target" }
        # These targets link only memory-based processors, never Windows audio
        # endpoints, the production control pipe, or the user's settings store.
        $output = & $path 2>&1
        $code = $LASTEXITCODE
        $output | Set-Content -LiteralPath (Join-Path $ResultDirectory ($target + '.txt')) -Encoding utf8
        $results.Add([ordered]@{test=$target;passed=($code -eq 0);exitCode=$code})
    }
    $passed = @($results | Where-Object { -not $_.passed }).Count -eq 0
    [ordered]@{passed=$passed;scope='Silent synthetic audio quality; no playback, recording, devices or personal settings.';tests=$results;resultDirectory=$ResultDirectory} |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $ResultDirectory 'results.json') -Encoding utf8
    Get-Content -LiteralPath (Join-Path $ResultDirectory 'results.json')
    if (-not $passed) { throw 'An effect quality criterion failed. See the recorded measurements.' }
} finally {
    $env:PATH = $previousPath
}
