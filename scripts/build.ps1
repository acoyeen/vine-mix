[CmdletBinding()]
param([ValidateSet('Release','Debug')][string]$Configuration = 'Release', [switch]$NativeOnly, [switch]$NoBootstrap, [string]$PackageDirectory = '')
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$toolsRoot = Join-Path $projectRoot '.tools'
if (-not $NoBootstrap) { & (Join-Path $PSScriptRoot 'bootstrap-tools.ps1') }
$cmake = Join-Path $toolsRoot 'cmake-4.4.4-windows-x86_64\bin\cmake.exe'
$compilerRoot = Join-Path $toolsRoot 'llvm-mingw-20261006-ucrt-x86_64\bin'
$dotnet = Join-Path $toolsRoot 'dotnet\dotnet.exe'
$buildRoot = Join-Path $projectRoot 'build\native'
$packageRoot = if ($PackageDirectory) { [IO.Path]::GetFullPath($PackageDirectory) } else { Join-Path $projectRoot 'artifacts\AudioMixer' }
# Building is separate from activation. Never overwrite either running process.
$liveFiles = @((Join-Path $packageRoot 'AudioMixer.exe'), (Join-Path $packageRoot 'AudioMixer.Engine.exe'))
function AssertDestinationStopped {
    if (@(Get-Process AudioMixer,AudioMixer.Engine -ErrorAction SilentlyContinue | Where-Object { $_.Path -in $liveFiles }).Count) {
        throw 'The destination is running. Choose -PackageDirectory for a staged build, then activate it after verification.'
    }
}
AssertDestinationStopped
# Distribution is part of a successful build. A package without mandatory
# copyright and licence notices must never be reported as ready to share.
$licenseNames = @(
    'Inter-OFL.txt', 'libsamplerate-LICENSE', 'libsamplerate-source-notices.txt',
    'SpeexDSP-COPYING.txt', 'SpeexDSP-source-notices.txt',
    'RNNoise-COPYING.txt', 'RNNoise-source-notices.txt',
    'nlohmann-json-LICENSE', 'nlohmann-json-third-party-notices.txt',
    'LLVM-runtime-LICENSE.txt', 'MinGW-w64-COPYING.txt',
    'MinGW-runtime-COPYING.txt', 'winpthreads-COPYING.txt',
    'dotnet-runtime-LICENSE.txt', 'dotnet-runtime-ThirdPartyNotices.txt',
    'dotnet-windowsdesktop-LICENSE.txt', 'dotnet-wpf-ThirdPartyNotices.txt',
    'dotnet-winforms-ThirdPartyNotices.txt', 'W3C-Audio-EQ-Cookbook-NOTICE.txt',
    'Microsoft-Windows-classic-samples-LICENSE.txt'
)
foreach ($relative in @('LICENSE', 'THIRD_PARTY_NOTICES.md', 'docs\DISTRIBUTION.md',
    'desktop\AudioMixer.Desktop\Assets\Fonts\LICENSE.txt') + @($licenseNames | ForEach-Object { 'licenses\' + $_ })) {
    $required = Join-Path $projectRoot $relative
    if (-not (Test-Path -LiteralPath $required -PathType Leaf) -or (Get-Item -LiteralPath $required).Length -eq 0) {
        throw "Required distribution notice is missing or empty: $relative"
    }
}
$oldPath = $env:PATH
$oldDotnet = $env:DOTNET_ROOT
$oldTelemetry = $env:DOTNET_CLI_TELEMETRY_OPTOUT
try {
    $env:PATH = "$compilerRoot;$env:PATH"
    $env:DOTNET_ROOT = Join-Path $toolsRoot 'dotnet'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    & $cmake -S $projectRoot -B $buildRoot -G 'MinGW Makefiles' "-DCMAKE_BUILD_TYPE=$Configuration" '-DCMAKE_C_COMPILER=clang.exe' '-DCMAKE_CXX_COMPILER=clang++.exe' '-DCMAKE_MAKE_PROGRAM=mingw32-make.exe' '-DCMAKE_POLICY_VERSION_MINIMUM=3.5' '-DBUILD_TESTING=OFF' '-DAUDIO_MIXER_OFFLINE_TESTS=OFF' '-DAUDIO_MIXER_HARDWARE_TESTS=OFF'
    if ($LASTEXITCODE -ne 0) { throw 'Native build configuration failed.' }
    & $cmake --build $buildRoot --parallel 4
    if ($LASTEXITCODE -ne 0) { throw 'Native compilation failed.' }
    AssertDestinationStopped
    New-Item -ItemType Directory -Force -Path $packageRoot | Out-Null
    if (-not $NativeOnly) {
        & $dotnet publish (Join-Path $projectRoot 'desktop\AudioMixer.Desktop\AudioMixer.Desktop.csproj') -c $Configuration -r win-x64 --self-contained true '-p:PublishSingleFile=true' '-p:IncludeNativeLibrariesForSelfExtract=true' '-p:DebugType=None' -o $packageRoot
        if ($LASTEXITCODE -ne 0) { throw 'Desktop compilation failed.' }
    }
    AssertDestinationStopped
    Copy-Item -LiteralPath (Join-Path $buildRoot 'AudioMixer.Engine.exe') -Destination $packageRoot -Force
    foreach ($runtime in @('libc++.dll','libunwind.dll','libwinpthread-1.dll')) {
        $source = Join-Path $compilerRoot $runtime
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Required native runtime is missing: $runtime" }
        Copy-Item -LiteralPath $source -Destination $packageRoot -Force
    }
    foreach ($document in @('README.md','LICENSE','CHANGELOG.md','CONTRIBUTING.md','TESTING.md','THIRD_PARTY_NOTICES.md','design-qa.md')) {
        $source = Join-Path $projectRoot $document
        if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination $packageRoot -Force }
    }
    $packageDocs = Join-Path $packageRoot 'docs'
    New-Item -ItemType Directory -Force -Path $packageDocs | Out-Null
    # Ship the guides, not the accumulated design captures and test fixtures.
    Get-ChildItem -LiteralPath (Join-Path $projectRoot 'docs') -File -Filter '*.md' |
        ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $packageDocs -Force }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\images') -Destination $packageDocs -Recurse -Force
    $licenseRoot = Join-Path $packageRoot 'licenses'
    New-Item -ItemType Directory -Force -Path $licenseRoot | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $projectRoot 'licenses') -File |
        ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $licenseRoot -Force }
    foreach ($name in $licenseNames) {
        if (-not (Test-Path -LiteralPath (Join-Path $licenseRoot $name) -PathType Leaf)) {
            throw "Package notice copy failed: $name"
        }
    }
    Write-Host "Build created: $packageRoot"
    Write-Host 'Compilation only: no tests or audio applications were run; no drivers were installed.'
} finally {
    $env:PATH = $oldPath
    $env:DOTNET_ROOT = $oldDotnet
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = $oldTelemetry
}
