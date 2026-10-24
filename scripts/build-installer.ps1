#requires -Version 7.3
[CmdletBinding()]
param([string]$ApplicationPackageDirectory = '', [string]$OutputDirectory = '', [switch]$Offline, [string]$StageDirectory = '')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $StageDirectory) { $StageDirectory = Join-Path $env:LOCALAPPDATA ('VineMix.BuildCache\installer-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff')) }
$stage = [IO.Path]::GetFullPath($StageDirectory)
if (Test-Path -LiteralPath $stage) { throw 'Choose a new StageDirectory; private build evidence is retained.' }
$release = & (Join-Path $PSScriptRoot 'write-release-metadata.ps1') -OutputDirectory (Join-Path $stage 'metadata')
$payload = Join-Path $stage 'payload'
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $project 'artifacts\installers' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$null = New-Item -ItemType Directory -Force -Path $stage,$payload,$OutputDirectory
$version = $release.version
$compiler = & (Join-Path $PSScriptRoot 'bootstrap-installer-tools.ps1') -Offline:$Offline
if (-not $ApplicationPackageDirectory) {
    $ApplicationPackageDirectory = Join-Path $stage 'application-build'
    & (Join-Path $PSScriptRoot 'build.ps1') -NoBootstrap -BuildMode Installed -PackageDirectory $ApplicationPackageDirectory
    if (-not $?) { throw 'Application build failed.' }
}
$source = [IO.Path]::GetFullPath($ApplicationPackageDirectory)
$receiptPath = $source + '.build.json'
if (-not (Test-Path -LiteralPath $receiptPath -PathType Leaf)) { throw 'The application build receipt is required; rebuild the installed package with scripts/build.ps1.' }
$receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json -Depth 12
$sourceIdentity = & (Join-Path $PSScriptRoot 'get-source-identity.ps1')
if ($receipt.buildMode -ne 'Installed' -or $receipt.version -ne $release.version -or $receipt.sourceIdentity.sourceSnapshotSha256 -ne $sourceIdentity.sourceSnapshotSha256) { throw 'The installed package source receipt does not match the current release source.' }
# Deliberate release allowlist. Checkout guides, fixtures, images and research
# notes do not enter the package, even if present in a caller-supplied folder.
$required = @('VineMix.exe','VineMix.Engine.exe','libc++.dll','libunwind.dll','libwinpthread-1.dll','LICENSE','THIRD_PARTY_NOTICES.md')
$licenseNames = @('IBM-Plex-OFL.txt','libsamplerate-LICENSE','libsamplerate-source-notices.txt',
    'SpeexDSP-COPYING.txt','SpeexDSP-source-notices.txt','RNNoise-COPYING.txt','RNNoise-source-notices.txt',
    'nlohmann-json-LICENSE','nlohmann-json-third-party-notices.txt','LLVM-runtime-LICENSE.txt','MinGW-w64-COPYING.txt',
    'MinGW-runtime-COPYING.txt','winpthreads-COPYING.txt','dotnet-runtime-LICENSE.txt','dotnet-runtime-ThirdPartyNotices.txt',
    'dotnet-windowsdesktop-LICENSE.txt','dotnet-wpf-ThirdPartyNotices.txt','dotnet-winforms-ThirdPartyNotices.txt',
    'W3C-Audio-EQ-Cookbook-NOTICE.txt','Microsoft-Windows-classic-samples-LICENSE.txt')
$required += @($licenseNames | ForEach-Object { 'licenses\' + $_ })
foreach ($name in $required) {
    $file = Join-Path $source $name
    if (-not (Test-Path -LiteralPath $file -PathType Leaf) -or (Get-Item -LiteralPath $file).Length -eq 0) { throw "Required staged file missing or empty: $name" }
    $entry = @($receipt.files | Where-Object path -CEQ $name.Replace('\','/'))
    if ($entry.Count -ne 1 -or (Get-Item -LiteralPath $file).Length -ne $entry[0].size -or
        (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $entry[0].sha256) { throw "Staged file no longer matches its build receipt: $name" }
    $destination = Join-Path $payload $name
    $null = New-Item -ItemType Directory -Force -Path (Split-Path $destination -Parent)
    Copy-Item -LiteralPath $file -Destination $destination
    if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $entry[0].sha256) { throw "Staged copy integrity failed: $name" }
}
foreach ($name in @('Assets\Fonts\IBM-Plex-LICENSE.txt')) {
    if (Test-Path -LiteralPath (Join-Path $source $name) -PathType Leaf) {
        $destination = Join-Path $payload $name
        $null = New-Item -ItemType Directory -Force -Path (Split-Path $destination -Parent)
        Copy-Item -LiteralPath (Join-Path $source $name) -Destination $destination
    }
}
foreach ($name in @('VineMix.exe','VineMix.Engine.exe')) {
    & (Join-Path $PSScriptRoot 'assert-release-file-version.ps1') -Path (Join-Path $payload $name) -Version $version -NumericVersion $release.numericVersion
}
$nativeVersion = (& (Join-Path $source 'VineMix.Engine.exe') --version | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or -not $nativeVersion.Contains($version)) { throw 'Staged engine release metadata does not match release.json.' }
$support = Join-Path $payload 'SetupSupport'
$null = New-Item -ItemType Directory -Path $support
foreach ($name in @('TERMS.txt','INSTALLATION.txt')) { Copy-Item -LiteralPath (Join-Path $project ('installer\' + $name)) -Destination $support }
Copy-Item -LiteralPath (Join-Path $project 'installer\notices\DRIVER-NOTICES.txt') -Destination $support
Copy-Item -LiteralPath (Join-Path $project 'installer\PACKAGE_README.md') -Destination (Join-Path $payload 'README.md')
$dotnet = Join-Path $project '.tools\dotnet\dotnet.exe'
$helperPublish = Join-Path $stage 'helper'
& $dotnet publish (Join-Path $project 'installer\VineMix.SetupHelper\VineMix.SetupHelper.csproj') -c Release -r win-x64 --self-contained true '-p:PublishSingleFile=true' '-p:IncludeNativeLibrariesForSelfExtract=true' '-p:DebugType=None' '-p:UseSharedCompilation=false' ('-p:Version=' + $version) ('-p:FileVersion=' + $release.numericVersion) ('-p:AssemblyVersion=' + $release.numericVersion) -o $helperPublish --nologo
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
    [ordered]@{path=[IO.Path]::GetRelativePath($payload,$_.FullName).Replace('\','/');size=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
})
[ordered]@{version=$version;numericVersion=$release.numericVersion;sourceSnapshotSha256=$sourceIdentity.sourceSnapshotSha256;gitHead=$sourceIdentity.gitHead;gitDirty=$sourceIdentity.gitDirty;files=$inventory} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $support 'package-manifest.json') -Encoding utf8
$installer = Join-Path $OutputDirectory ('VineMix-' + $version + '-Setup-x64.exe')
if (Test-Path -LiteralPath $installer) { throw 'The output installer already exists. Choose a new output directory; prior candidates are retained.' }
$vb = $drivers.drivers | Where-Object id -eq 'vb-cable'
$vac = $drivers.drivers | Where-Object id -eq 'vac-lite'
$arguments = @('/Qp',('/DPayloadDir=' + $payload),('/DAppVersion=' + $version),('/DNumericVersion=' + $release.numericVersion),('/DDisplayVersion=' + $release.displayVersion),('/DDriverArchiveDir=' + $cache),('/DOutputDir=' + $OutputDirectory),('/DVbArchiveHash=' + $vb.archiveSha256),('/DVacArchiveHash=' + $vac.archiveSha256),(Join-Path $project 'installer\VineMix.iss'))
& $compiler @arguments 2>&1 | Tee-Object -LiteralPath (Join-Path $stage 'compiler.log')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed. See staging compiler.log.' }
if (-not (Test-Path -LiteralPath $installer)) { throw 'The compiler did not produce the expected installer.' }
$afterIdentity = & (Join-Path $PSScriptRoot 'get-source-identity.ps1')
if ($sourceIdentity.sourceSnapshotSha256 -ne $afterIdentity.sourceSnapshotSha256) { throw 'Release source changed while packaging. Retain this unpublished candidate and repeat from stable source.' }
$manifest = [ordered]@{product='VineMix';version=$version;numericVersion=$release.numericVersion;displayVersion=$release.displayVersion;sourceIdentity=$sourceIdentity;architecture='x64';minimumWindows='Windows 11 build 22000';distributionChannel='github-prerelease';published=$false;signed=$false;stageDirectory=$stage;payloadDirectory=$payload;installer=$installer;size=(Get-Item -LiteralPath $installer).Length;sha256=(Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash;applicationDirectory='%LOCALAPPDATA%\Programs\VineMix';dataDirectory='%LOCALAPPDATA%\VineMix';legacyDataDirectory='%LOCALAPPDATA%\AudioMixer';drivers=@($drivers.drivers | Select-Object id,version,downloadUrl,archiveSha256)}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath ($installer + '.json') -Encoding utf8
($manifest.sha256 + '  ' + [IO.Path]::GetFileName($installer)) | Set-Content -LiteralPath ($installer + '.sha256') -Encoding ascii
[ordered]@{researchDate='2026-10-21';vbAudioPermission='Maintainer attested in this session on 2026-10-21 that VB-Audio integration permission was already obtained; agreement document not independently reviewed.';vendorReview=Join-Path $project 'installer\notices\DISTRIBUTION-REVIEW.md';driversExecuted=$false;published=$false} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stage 'private-qualification-notes.json') -Encoding utf8
Write-Host ("Installer SHA-256: " + $manifest.sha256)
Write-Host ("Source snapshot SHA-256: " + $sourceIdentity.sourceSnapshotSha256)
Write-Host 'Unpublished installer candidate built. No application session or driver was installed or launched.'
