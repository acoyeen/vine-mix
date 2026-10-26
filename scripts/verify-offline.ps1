[CmdletBinding()]
param([switch]$SkipBuild, [switch]$NativeOnly, [string]$ResultDirectory = '', [string]$BuildDirectory = '')
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $BuildDirectory) { $BuildDirectory = Join-Path $env:LOCALAPPDATA 'VineMix.BuildCache\release\native' }
$build = [IO.Path]::GetFullPath($BuildDirectory)
$compiler = Join-Path $project '.tools\llvm-mingw-20261006-ucrt-x86_64\bin'
$cmake = Join-Path $project '.tools\cmake-4.4.4-windows-x86_64\bin\cmake.exe'
$dotnet = Join-Path $project '.tools\dotnet\dotnet.exe'
if (-not $ResultDirectory) { $ResultDirectory = Join-Path $env:LOCALAPPDATA ('VineMix.BuildCache\offline-verification-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
$ResultDirectory = [IO.Path]::GetFullPath($ResultDirectory)
if (Test-Path -LiteralPath $ResultDirectory) { throw 'Choose a new results directory; prior evidence is retained.' }
$null = New-Item -ItemType Directory -Path $ResultDirectory
$previewPackage = Join-Path $ResultDirectory 'verification-build'
$targets = @('VineMix.GraphTests','VineMix.EngineLifecycleTests','VineMix.QualificationTests',
    'VineMix.DriftQueueTests','VineMix.AdvancedEffectsTests','VineMix.DenoiserTests','VineMix.SpectrumTests',
    'VineMix.DeEsserQualityTests','VineMix.EffectsQualityTests','VineMix.GraphPerformanceTests','VineMix.WindowsCatalogTests','VineMix.ExclusiveRenderTests','VineMix.SharedAudioTests','VineMix.AudioTimingTests','VineMix.MixClockTests','VineMix.VoiceColorTests','VineMix.CoreDynamicsTests')
$oldPath=$env:PATH; $oldDotnet=$env:DOTNET_ROOT
$results=[Collections.Generic.List[object]]::new()
$failure=$null
try {
    $env:PATH="$compiler;$oldPath"; $env:DOTNET_ROOT=Join-Path $project '.tools\dotnet'
    if (-not $SkipBuild) {
        & $cmake -S $project -B $build -G 'MinGW Makefiles' '-DCMAKE_BUILD_TYPE=Release' '-DCMAKE_C_COMPILER=clang.exe' '-DCMAKE_CXX_COMPILER=clang++.exe' '-DCMAKE_MAKE_PROGRAM=mingw32-make.exe' '-DCMAKE_POLICY_VERSION_MINIMUM=3.5' '-DBUILD_TESTING=OFF' '-DVINEMIX_OFFLINE_TESTS=ON'
        if ($LASTEXITCODE -ne 0) { throw 'Native verification configuration failed.' }
        & $cmake --build $build --parallel 4 --target @targets
        if ($LASTEXITCODE -ne 0) { throw 'Native verification build failed.' }
    }
    # Explicit allow-list: none of these programs opens a real audio endpoint,
    # connects to the production control pipe or uses personal settings.
    foreach ($target in $targets) {
        $path=Join-Path $build ($target+'.exe')
        if (-not (Test-Path -LiteralPath $path)) { throw "Missing verifier: $target" }
        $arguments=@()
        if ($target -eq 'VineMix.QualificationTests') { $arguments=@((Join-Path $ResultDirectory 'configuration-fixtures')) }
        $timer=[Diagnostics.Stopwatch]::StartNew()
        $output=& $path @arguments 2>&1
        $code=$LASTEXITCODE
        $output | Set-Content -LiteralPath (Join-Path $ResultDirectory ($target+'.txt')) -Encoding utf8
        $results.Add([ordered]@{test=$target;passed=($code -eq 0);exitCode=$code;seconds=[Math]::Round($timer.Elapsed.TotalSeconds,3)})
    }
    if (-not $NativeOnly) {
        if (-not $SkipBuild) {
            & $dotnet publish (Join-Path $project 'desktop\VineMix.Desktop\VineMix.Desktop.csproj') -c Release -r win-x64 --self-contained true '-p:PublishSingleFile=true' '-p:IncludeNativeLibrariesForSelfExtract=true' '-p:DebugType=None' '-p:UseSharedCompilation=false' '-p:VineMixVerification=true' '-p:VineMixBuildMode=Installed' -o $previewPackage --nologo
            if ($LASTEXITCODE -ne 0) { throw 'Detached UI verification build failed.' }
        }
        $uiResult=Join-Path $ResultDirectory 'ui'
        $process=Start-Process -FilePath (Join-Path $previewPackage 'VineMix.Verification.exe') -ArgumentList @('--render-design','--verification-dir',('"'+$uiResult+'"')) -WindowStyle Hidden -PassThru
        if (-not $process.WaitForExit(120000)) { $process.Kill(); throw 'Detached UI verification exceeded its deadline.' }
        $report=Get-Content -LiteralPath (Join-Path $uiResult 'results.json') -Raw | ConvertFrom-Json
        $results.Add([ordered]@{test='Detached WPF';passed=($process.ExitCode -eq 0 -and $report.passed);checks=$report.checks.Count;renders=$report.images.Count;bindingErrors=$report.bindingErrors.Count})
        $process.Dispose()
    }
} catch {
    $failure=$_.Exception.Message
} finally {
    $env:PATH=$oldPath; $env:DOTNET_ROOT=$oldDotnet
    $passed=(-not $failure) -and @($results | Where-Object {-not $_.passed}).Count -eq 0
    [ordered]@{passed=$passed;error=$failure;scope='Synthetic audio, temporary configuration fixtures, fake endpoint inventory, and optional detached UI/private test pipes. No desktop input, real audio devices, production control pipe or personal settings.';tests=$results;resultDirectory=$ResultDirectory} |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $ResultDirectory 'results.json') -Encoding utf8
    Get-Content -LiteralPath (Join-Path $ResultDirectory 'results.json')
}
if (-not $passed) { throw 'Offline verification failed. See the recorded test reports.' }
