#requires -Version 7.3
[CmdletBinding()]
param(
    [string]$ApplicationPackageDirectory = '',
    [string]$OutputDirectory = '',
    [switch]$Offline
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$stage = Join-Path $project ('artifacts\installer-build-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$payload = Join-Path $stage 'payload'
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $project 'artifacts\installers' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $stage,$payload,$OutputDirectory | Out-Null

[xml]$desktopProject = Get-Content -LiteralPath (Join-Path $project 'desktop\AudioMixer.Desktop\AudioMixer.Desktop.csproj') -Raw
$version = [string]$desktopProject.Project.PropertyGroup.Version
$nativeProject = Get-Content -LiteralPath (Join-Path $project 'CMakeLists.txt') -Raw
if ($version -notmatch '^\d+\.\d+\.\d+$' -or $nativeProject -notmatch ('project\(AudioMixer VERSION ' + [regex]::Escape($version) + ' LANGUAGES')) {
    throw 'Desktop and native project versions must match before packaging.'
}
$compiler = & (Join-Path $PSScriptRoot 'bootstrap-installer-tools.ps1') -Offline:$Offline
if (-not $ApplicationPackageDirectory) {
    $ApplicationPackageDirectory = Join-Path $stage 'application-build'
    & (Join-Path $PSScriptRoot 'build.ps1') -NoBootstrap -PackageDirectory $ApplicationPackageDirectory
    if (-not $?) { throw 'Application build failed.' }
}
$source = [IO.Path]::GetFullPath($ApplicationPackageDirectory)
foreach ($name in @('AudioMixer.exe','AudioMixer.Engine.exe','libc++.dll','libunwind.dll','libwinpthread-1.dll',
        'LICENSE','THIRD_PARTY_NOTICES.md')) {
    $file = Join-Path $source $name
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Required staged application file missing: $name" }
    Copy-Item -LiteralPath $file -Destination $payload
}
$sourceVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $source 'AudioMixer.exe')).ProductVersion.Split('+')[0]
if ($sourceVersion -ne $version) { throw "Staged desktop version $sourceVersion does not match source $version." }
foreach ($directory in @('licenses','Assets\Fonts')) {
    $from = Join-Path $source $directory
    if (-not (Test-Path -LiteralPath $from -PathType Container)) { throw "Required licence folder missing: $directory" }
    $to = Join-Path $payload $directory
    New-Item -ItemType Directory -Force -Path $to | Out-Null
    Get-ChildItem -LiteralPath $from -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $to }
}
# Only deliberately selected user documentation ships; research logs and machine paths stay in the checkout.
$guides = @('USER_GUIDE.md','NODE_WORKSPACE.md','APP_AUDIO.md','DEVICE_VISIBILITY.md','VIRTUAL_DEVICE_UI.md',
    'INPUT_AVAILABILITY.md','AUDIO_LATENCY.md','EFFECTS_RESEARCH.md','DISTRIBUTION.md','IBM_PLEX_FONTS.md',
    'BLENDER_NODE_STYLE.md','CONTROL_PROTOCOL.md','CORE_DYNAMICS.md','DEESSER_QUALITY_AUDIT.md',
    'EFFECTS_QUALITY_AUDIT.md','EQ_SPECTRUM_ANALYZER.md','MAINTENANCE_REVIEW.md','NODE_VERIFICATION.md',
    'SHURE_DENOISER.md','VOICE_COLOR.md')
$docs = Join-Path $payload 'docs'
New-Item -ItemType Directory -Force -Path $docs | Out-Null
foreach ($name in $guides) { Copy-Item -LiteralPath (Join-Path $project ('docs\' + $name)) -Destination $docs }
Copy-Item -LiteralPath (Join-Path $project 'docs\images') -Destination $docs -Recurse
foreach ($name in @('CHANGELOG.md','TESTING.md','design-qa.md')) { Copy-Item -LiteralPath (Join-Path $project $name) -Destination $payload }
Copy-Item -LiteralPath (Join-Path $project 'installer\PACKAGE_README.md') -Destination (Join-Path $payload 'README.md')
$support = Join-Path $payload 'SetupSupport'
New-Item -ItemType Directory -Force -Path $support | Out-Null
foreach ($name in @('TERMS.txt','INSTALLATION.txt')) {
    Copy-Item -LiteralPath (Join-Path $project ('installer\' + $name)) -Destination $support
}
Copy-Item -LiteralPath (Join-Path $project 'installer\notices\DRIVER-NOTICES.txt') -Destination $support
$dotnet = Join-Path $project '.tools\dotnet\dotnet.exe'
$helperPublish = Join-Path $stage 'helper'
& $dotnet publish (Join-Path $project 'installer\VineMix.SetupHelper\VineMix.SetupHelper.csproj') -c Release -r win-x64 `
    --self-contained true '-p:PublishSingleFile=true' '-p:IncludeNativeLibrariesForSelfExtract=true' '-p:DebugType=None' `
    '-p:UseSharedCompilation=false' -o $helperPublish --nologo
if ($LASTEXITCODE -ne 0) { throw 'Installer helper publish failed.' }
Copy-Item -LiteralPath (Join-Path $helperPublish 'VineMix.SetupHelper.exe') -Destination $support
& (Join-Path $support 'VineMix.SetupHelper.exe') --self-test
if ($LASTEXITCODE -ne 0) { throw 'Installer helper policy tests failed.' }

$dependencies = Join-Path $stage 'dependencies'
$cache = Join-Path $project '.tools\downloads'
& (Join-Path $PSScriptRoot 'prepare-installer-drivers.ps1') -DestinationDirectory $dependencies -CacheDirectory $cache -Offline:$Offline
if (-not $?) { throw 'Driver preparation failed.' }
$drivers = Get-Content -LiteralPath (Join-Path $project 'installer\drivers.json') -Raw | ConvertFrom-Json
Copy-Item -LiteralPath (Join-Path $project 'installer\drivers.json') -Destination $support
Copy-Item -LiteralPath (Join-Path $project '.tools\innosetup-7.1.0\license.txt') -Destination (Join-Path $support 'Inno-Setup-LICENSE.txt')
$inventory = @(Get-ChildItem -LiteralPath $payload -File -Recurse | Sort-Object FullName | ForEach-Object {
    [ordered]@{ path = [IO.Path]::GetRelativePath($payload, $_.FullName).Replace('\','/');
        size = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
})
[ordered]@{version=$version;files=$inventory} | ConvertTo-Json -Depth 5 |
    Set-Content -LiteralPath (Join-Path $support 'package-manifest.json') -Encoding utf8
$installer = Join-Path $OutputDirectory ('VineMix-' + $version + '-Setup-x64.exe')
if (@(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $installer }).Count) {
    throw 'The output installer is running; choose a new output directory.'
}
$vb = $drivers.drivers | Where-Object id -eq 'vb-cable'
$vac = $drivers.drivers | Where-Object id -eq 'vac-lite'
$arguments = @('/Qp',('/DPayloadDir=' + $payload),('/DAppVersion=' + $version),('/DDriverArchiveDir=' + $cache),
    ('/DOutputDir=' + $OutputDirectory),('/DVbArchiveHash=' + $vb.archiveSha256),('/DVacArchiveHash=' + $vac.archiveSha256),
    (Join-Path $project 'installer\VineMix.iss'))
& $compiler @arguments 2>&1 | Tee-Object -LiteralPath (Join-Path $stage 'compiler.log')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed. See compiler.log in the staging folder.' }
if (-not (Test-Path -LiteralPath $installer)) { throw 'The compiler did not produce the expected installer.' }
$manifest = [ordered]@{
    product='Vine Mix'; version=$version; architecture='x64'; minimumWindows='Windows 11 build 22000';
    distributionChannel='free-noncommercial'; signed=$false; stageDirectory=$stage; payloadDirectory=$payload;
    installer=$installer; size=(Get-Item -LiteralPath $installer).Length;
    sha256=(Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash;
    applicationDirectory='%LOCALAPPDATA%\Programs\Vine Mix'; dataDirectory='%LOCALAPPDATA%\AudioMixer';
    drivers=@($drivers.drivers | Select-Object id,version,downloadUrl,archiveSha256)
}
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath ($installer + '.json') -Encoding utf8
($manifest.sha256 + '  ' + [IO.Path]::GetFileName($installer)) | Set-Content -LiteralPath ($installer + '.sha256') -Encoding ascii
$manifest | ConvertTo-Json -Depth 6
Write-Host 'Installer built. No application or driver was installed or launched.'
