[CmdletBinding()]
param(
    [ValidateSet('Release','Debug')][string]$Configuration = 'Release',
    [ValidateSet('Installed','Standalone')][string]$BuildMode = 'Installed',
    [switch]$NativeOnly,
    [switch]$NoBootstrap,
    [string]$PackageDirectory = ''
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$toolsRoot = Join-Path $projectRoot '.tools'
if (-not $NoBootstrap) { & (Join-Path $PSScriptRoot 'bootstrap-tools.ps1') }
$cmake = Join-Path $toolsRoot 'cmake-4.4.4-windows-x86_64\bin\cmake.exe'
$compilerRoot = Join-Path $toolsRoot 'llvm-mingw-20261006-ucrt-x86_64\bin'
$dotnet = Join-Path $toolsRoot 'dotnet\dotnet.exe'
$buildRoot = Join-Path $projectRoot 'build\native'
$metadataRoot = Join-Path $projectRoot 'build\release'
$release = & (Join-Path $PSScriptRoot 'write-release-metadata.ps1') -OutputDirectory $metadataRoot
$sourceIdentity = & (Join-Path $PSScriptRoot 'get-source-identity.ps1')
$buildTimestamp = [DateTimeOffset]::UtcNow.ToString('o')
$packageRoot = if ($PackageDirectory) { [IO.Path]::GetFullPath($PackageDirectory) } else { Join-Path $projectRoot ('artifacts\VineMix-' + $release.version + '-' + $BuildMode) }
$desktopProject = Join-Path $projectRoot 'desktop\VineMix.Desktop\VineMix.Desktop.csproj'
if (-not (Test-Path -LiteralPath $desktopProject)) { throw 'The VineMix desktop project is missing.' }
# Building never activates or replaces a running release, including legacy copies.
$liveFiles = @('VineMix.exe','VineMix.Engine.exe','AudioMixer.exe','AudioMixer.Engine.exe' | ForEach-Object { Join-Path $packageRoot $_ })
function AssertDestinationStopped {
    if (@(Get-Process VineMix,VineMix.Engine,AudioMixer,AudioMixer.Engine -ErrorAction SilentlyContinue | Where-Object { $_.Path -in $liveFiles }).Count) {
        throw 'The destination is running. Choose a new staged PackageDirectory.'
    }
}
AssertDestinationStopped
if ((Test-Path -LiteralPath $packageRoot) -and @(Get-ChildItem -LiteralPath $packageRoot -Force).Count) {
    throw 'Choose an empty PackageDirectory. Existing builds are retained to prevent stale or private files entering a release.'
}
$licenseNames = @(
    'IBM-Plex-OFL.txt', 'libsamplerate-LICENSE', 'libsamplerate-source-notices.txt',
    'SpeexDSP-COPYING.txt', 'SpeexDSP-source-notices.txt', 'RNNoise-COPYING.txt', 'RNNoise-source-notices.txt',
    'nlohmann-json-LICENSE', 'nlohmann-json-third-party-notices.txt', 'LLVM-runtime-LICENSE.txt', 'MinGW-w64-COPYING.txt',
    'MinGW-runtime-COPYING.txt', 'winpthreads-COPYING.txt', 'dotnet-runtime-LICENSE.txt', 'dotnet-runtime-ThirdPartyNotices.txt',
    'dotnet-windowsdesktop-LICENSE.txt', 'dotnet-wpf-ThirdPartyNotices.txt', 'dotnet-winforms-ThirdPartyNotices.txt',
    'W3C-Audio-EQ-Cookbook-NOTICE.txt', 'Microsoft-Windows-classic-samples-LICENSE.txt'
)
$noticePaths = @('LICENSE','THIRD_PARTY_NOTICES.md') + @($licenseNames | ForEach-Object { 'licenses\' + $_ })
foreach ($relative in $noticePaths) {
    $required = Join-Path $projectRoot $relative
    if (-not (Test-Path -LiteralPath $required -PathType Leaf) -or (Get-Item -LiteralPath $required).Length -eq 0) { throw "Required notice missing or empty: $relative" }
}
$oldPath = $env:PATH; $oldDotnet = $env:DOTNET_ROOT; $oldTelemetry = $env:DOTNET_CLI_TELEMETRY_OPTOUT
try {
    $env:PATH = "$compilerRoot;$env:PATH"; $env:DOTNET_ROOT = Join-Path $toolsRoot 'dotnet'; $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    & $cmake -S $projectRoot -B $buildRoot -G 'MinGW Makefiles' "-DCMAKE_BUILD_TYPE=$Configuration" '-DCMAKE_C_COMPILER=clang.exe' '-DCMAKE_CXX_COMPILER=clang++.exe' '-DCMAKE_MAKE_PROGRAM=mingw32-make.exe' '-DCMAKE_POLICY_VERSION_MINIMUM=3.5' '-DBUILD_TESTING=OFF' '-DAUDIO_MIXER_OFFLINE_TESTS=OFF' '-DAUDIO_MIXER_HARDWARE_TESTS=OFF'
    if ($LASTEXITCODE -ne 0) { throw 'Native build configuration failed.' }
    & $cmake --build $buildRoot --parallel 4 --target VineMix.Engine
    if ($LASTEXITCODE -ne 0) { throw 'Native compilation failed.' }
    AssertDestinationStopped
    $runtimeStage = Join-Path $metadataRoot ('runtime-' + [Guid]::NewGuid().ToString('N'))
    $null = New-Item -ItemType Directory -Path $runtimeStage
    Copy-Item -LiteralPath (Join-Path $buildRoot 'VineMix.Engine.exe') -Destination $runtimeStage
    foreach ($runtime in @('libc++.dll','libunwind.dll','libwinpthread-1.dll')) {
        $source = Join-Path $compilerRoot $runtime
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Required native runtime missing: $runtime" }
        Copy-Item -LiteralPath $source -Destination $runtimeStage
    }
    foreach ($relative in $noticePaths) {
        $destination = Join-Path $runtimeStage $relative
        $null = New-Item -ItemType Directory -Force -Path (Split-Path $destination -Parent)
        Copy-Item -LiteralPath (Join-Path $projectRoot $relative) -Destination $destination
    }
    $null = New-Item -ItemType Directory -Force -Path $packageRoot
    $publishArguments = @('-c',$Configuration,'-r','win-x64','--self-contained','true','-p:PublishSingleFile=true','-p:IncludeNativeLibrariesForSelfExtract=true','-p:DebugType=None','-p:UseSharedCompilation=false',('-p:VineMixBuildMode=' + $BuildMode),('-p:VineMixReleaseProps=' + (Join-Path $metadataRoot 'ReleaseVersion.props')),('-p:VineMixSourceSnapshotHash=' + $sourceIdentity.sourceSnapshotSha256),('-p:VineMixBuildTimestamp=' + $buildTimestamp),('-p:SourceRevisionId=' + $sourceIdentity.gitHead),'-o',$packageRoot,'--nologo')
    if ($BuildMode -eq 'Standalone') {
        if ($NativeOnly) { throw 'Standalone builds require the desktop executable.' }
        Add-Type -AssemblyName System.IO.Compression
        $archivePath = Join-Path $runtimeStage '..\VineMix.Runtime.zip'
        $archivePath = [IO.Path]::GetFullPath($archivePath)
        if (Test-Path -LiteralPath $archivePath) { $archivePath = Join-Path $metadataRoot ('runtime-' + [Guid]::NewGuid().ToString('N') + '.zip') }
        $zip = [IO.Compression.ZipFile]::Open($archivePath, [IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($file in Get-ChildItem -LiteralPath $runtimeStage -File -Recurse | Sort-Object FullName) {
                $entry = $zip.CreateEntry([IO.Path]::GetRelativePath($runtimeStage,$file.FullName).Replace('\','/'), [IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = [DateTimeOffset]::new(2026,1,1,0,0,0,[TimeSpan]::Zero)
                $input = [IO.File]::OpenRead($file.FullName)
                $output = $entry.Open()
                try { $input.CopyTo($output) } finally { $output.Dispose(); $input.Dispose() }
            }
        } finally { $zip.Dispose() }
        $files = @(Get-ChildItem -LiteralPath $runtimeStage -File -Recurse | Sort-Object FullName | ForEach-Object {
            [ordered]@{path=[IO.Path]::GetRelativePath($runtimeStage,$_.FullName).Replace('\','/');size=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
        })
        $manifestPath = $archivePath + '.manifest.json'
        [ordered]@{schemaVersion=1;engine='VineMix.Engine.exe';archiveSize=(Get-Item -LiteralPath $archivePath).Length;archiveSha256=(Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant();files=$files} |
            ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding utf8
        $publishArguments += @(('-p:VineMixRuntimeArchive=' + $archivePath),('-p:VineMixRuntimeManifest=' + $manifestPath))
    }
    if (-not $NativeOnly) {
        & $dotnet publish $desktopProject @publishArguments
        if ($LASTEXITCODE -ne 0) { throw 'Desktop compilation failed.' }
    }
    AssertDestinationStopped
    if ($BuildMode -eq 'Installed') {
        foreach ($file in Get-ChildItem -LiteralPath $runtimeStage -File -Recurse) {
            $destination = Join-Path $packageRoot ([IO.Path]::GetRelativePath($runtimeStage,$file.FullName))
            $null = New-Item -ItemType Directory -Force -Path (Split-Path $destination -Parent)
            Copy-Item -LiteralPath $file.FullName -Destination $destination
        }
    } else {
        $contents = @(Get-ChildItem -LiteralPath $packageRoot -File -Recurse)
        if ($contents.Count -ne 1 -or $contents[0].Name -ne 'VineMix.exe') { throw 'Standalone publish must contain exactly one VineMix.exe; all notices must be embedded.' }
        Rename-Item -LiteralPath (Join-Path $packageRoot 'VineMix.exe') -NewName ('VineMix-' + $release.version + '-Standalone-x64.exe')
    }
    $inventory = @(Get-ChildItem -LiteralPath $packageRoot -File -Recurse | Sort-Object FullName | ForEach-Object { [ordered]@{path=[IO.Path]::GetRelativePath($packageRoot,$_.FullName).Replace('\','/');size=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash} })
    $afterIdentity = & (Join-Path $PSScriptRoot 'get-source-identity.ps1')
    if ($sourceIdentity.sourceSnapshotSha256 -ne $afterIdentity.sourceSnapshotSha256) { throw 'Release source changed during the build. Retain this candidate for diagnosis and build again from stable source.' }
    [ordered]@{version=$release.version;numericVersion=$release.numericVersion;displayVersion=$release.displayVersion;buildTimestamp=$buildTimestamp;buildMode=$BuildMode;published=$false;sourceIdentity=$sourceIdentity;packageDirectory=$packageRoot;runtimeArchive=$(if($BuildMode -eq 'Standalone'){$archivePath}else{$null});runtimeManifest=$(if($BuildMode -eq 'Standalone'){$manifestPath}else{$null});files=$inventory} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath ($packageRoot + '.build.json') -Encoding utf8
    Write-Host "Build created: $packageRoot"
    Write-Host 'Staged only: no application, audio engine or driver was launched; nothing was published.'
} finally { $env:PATH=$oldPath; $env:DOTNET_ROOT=$oldDotnet; $env:DOTNET_CLI_TELEMETRY_OPTOUT=$oldTelemetry }
