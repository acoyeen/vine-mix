<#
.SYNOPSIS
Provides the pinned C/C++ reference sources the parity oracle
(crates\vinemix-oracle) is compiled from.

.DESCRIPTION
Windows PowerShell 5.1 compatible. The archive addresses and SHA-256 digests
are read from CMakeLists.txt, the same pins the 0.5 native build uses, so
there is one list to maintain. An archive already present in the archive
directory is reused; a missing one is downloaded unless -Offline is given.
Every archive is verified before anything is extracted.

Each component is extracted to a temporary directory first. If the
destination already holds that component, the two are compared file by file:
an identical tree is kept, a different one stops the run unless -Force
replaces it. Nothing outside the destination and archive directories is
written. VineMix itself does not need these sources; only the parity tests do.
#>
[CmdletBinding()]
param(
    [string]$Destination = '',
    [string]$ArchiveDirectory = '',
    # Never download; fail if an archive is not already present.
    [switch]$Offline,
    # Replace an existing component that differs from its pinned archive.
    [switch]$Force
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $Destination) { $Destination = Join-Path $project 'third_party\src' }
if (-not $ArchiveDirectory) { $ArchiveDirectory = Join-Path $project 'third_party\archives' }
$destination = [IO.Path]::GetFullPath($Destination)
$archives = [IO.Path]::GetFullPath($ArchiveDirectory)
$null = New-Item -ItemType Directory -Force -Path $destination, $archives

# name in CMakeLists.txt -> archive file, a version marker its URL must carry,
# and where each top-level entry of the archive belongs under the destination
# (the layout crates\vinemix-oracle\build.rs reads).
$components = @(
    @{ name = 'samplerate'; file = 'libsamplerate-0.2.2.tar.gz'; marker = '0.2.2'; place = @{ 'libsamplerate-0.2.2' = 'libsamplerate-0.2.2' }; sentinel = 'libsamplerate-0.2.2\src\samplerate.c' },
    @{ name = 'json'; file = 'json-3.12.0.tar.xz'; marker = 'v3.12.0'; place = @{ 'json' = 'json-3.12.0\json' }; sentinel = 'json-3.12.0\json\include\nlohmann\json.hpp' },
    @{ name = 'speexdsp'; file = 'speexdsp-1.2.1.tar.gz'; marker = '1.2.1'; place = @{ 'speexdsp-1.2.1' = 'speexdsp-1.2.1' }; sentinel = 'speexdsp-1.2.1\libspeexdsp\mdf.c' },
    @{ name = 'rnnoise'; file = 'rnnoise-0.2.tar.gz'; marker = 'v0.2'; place = @{ 'rnnoise-0.2' = 'rnnoise-0.2' }; sentinel = 'rnnoise-0.2\src\denoise.c' },
    @{ name = 'rnnoise_model'; file = 'rnnoise_data-0b50c45.tar.gz'; marker = '0b50c45'; place = @{ 'src' = 'src'; 'models' = 'models' }; sentinel = 'src\rnnoise_data.c' }
)

$cmake = Get-Content -LiteralPath (Join-Path $project 'CMakeLists.txt') -Raw -Encoding UTF8
$pins = @{}
foreach ($match in [regex]::Matches($cmake, 'FetchContent_Declare\(\s*(\w+)\s+URL\s+(\S+)\s+URL_HASH\s+SHA256=([0-9a-fA-F]{64})')) {
    $pins[$match.Groups[1].Value] = @{ url = $match.Groups[2].Value; sha256 = $match.Groups[3].Value.ToLowerInvariant() }
}

function Get-Sha256([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Get-TreeListing([string]$Root) {
    # relative path -> "size sha256", for comparing two extracted trees.
    $listing = @{}
    if (Test-Path -LiteralPath $Root) {
        foreach ($file in Get-ChildItem -LiteralPath $Root -File -Recurse -Force) {
            $listing[$file.FullName.Substring($Root.Length + 1)] = ('{0} {1}' -f $file.Length, (Get-Sha256 $file.FullName))
        }
    }
    $listing
}
function Test-SameTree($Left, $Right) {
    if ($Left.Count -ne $Right.Count) { return $false }
    foreach ($key in $Left.Keys) { if (-not $Right.ContainsKey($key) -or $Right[$key] -cne $Left[$key]) { return $false } }
    $true
}
function Expand-TarArchive([string]$Archive, [string]$Target) {
    # Windows' own tar first; Git's tar as the fallback for older systems whose
    # tar cannot read .xz. Git's tar needs --force-local for drive letters.
    $candidates = New-Object Collections.Generic.List[object]
    $system = Join-Path $env:SystemRoot 'System32\tar.exe'
    if (Test-Path -LiteralPath $system) { $candidates.Add(@{ path = $system; arguments = @('-xf', $Archive, '-C', $Target) }) }
    $git = Get-Command git.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($git) {
        $bundled = Join-Path (Split-Path -Parent (Split-Path -Parent $git.Source)) 'usr\bin\tar.exe'
        if (Test-Path -LiteralPath $bundled) { $candidates.Add(@{ path = $bundled; arguments = @('--force-local', '-xf', $Archive, '-C', $Target) }) }
    }
    foreach ($candidate in $candidates) {
        Get-ChildItem -LiteralPath $Target -Force | Remove-Item -Recurse -Force
        & $candidate.path @($candidate.arguments)
        if ($LASTEXITCODE -eq 0 -and @(Get-ChildItem -LiteralPath $Target -Force).Count -gt 0) { return }
    }
    throw ('No available tar could extract ' + (Split-Path -Leaf $Archive) + '.')
}

$results = New-Object Collections.Generic.List[object]
foreach ($component in $components) {
    $pin = $pins[$component.name]
    if (-not $pin) { throw ('CMakeLists.txt no longer declares ' + $component.name + '.') }
    if (-not $pin.url.Contains($component.marker)) {
        throw ('CMakeLists.txt pins a different ' + $component.name + ' release than this script and crates\vinemix-oracle\build.rs expect (' + $component.marker + '). Update all three together.')
    }
    $archive = Join-Path $archives $component.file
    $downloaded = $false
    if (-not (Test-Path -LiteralPath $archive -PathType Leaf)) {
        if ($Offline) { throw ('The pinned archive ' + $component.file + ' is not in the archive directory and -Offline forbids downloading it.') }
        Write-Host ('Downloading ' + $component.file)
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        $progress = $ProgressPreference; $ProgressPreference = 'SilentlyContinue'
        try { Invoke-WebRequest -UseBasicParsing -Uri $pin.url -OutFile $archive } finally { $ProgressPreference = $progress }
        $downloaded = $true
    }
    if ((Get-Sha256 $archive) -cne $pin.sha256) {
        throw ('Checksum mismatch for ' + $component.file + '. It was left in place for inspection; delete it and obtain it again from the pinned address.')
    }
    $temporary = Join-Path $destination ('.extract-' + [guid]::NewGuid().ToString('N'))
    $null = New-Item -ItemType Directory -Path $temporary
    try {
        Expand-TarArchive $archive $temporary
        $action = 'verified existing copy'
        foreach ($top in $component.place.Keys) {
            $extracted = Join-Path $temporary $top
            if (-not (Test-Path -LiteralPath $extracted -PathType Container)) { throw ($component.file + ' does not contain the expected ' + $top + ' directory.') }
            $final = Join-Path $destination $component.place[$top]
            if (Test-Path -LiteralPath $final) {
                if (Test-SameTree (Get-TreeListing $extracted) (Get-TreeListing $final)) { continue }
                if (-not $Force) {
                    throw ($final + ' exists but differs from the pinned ' + $component.file + '. Move it away, or pass -Force to replace it.')
                }
                Remove-Item -LiteralPath $final -Recurse -Force
            }
            $null = New-Item -ItemType Directory -Force -Path (Split-Path -Parent $final)
            Move-Item -LiteralPath $extracted -Destination $final
            $action = 'extracted'
        }
    } finally { Remove-Item -LiteralPath $temporary -Recurse -Force -ErrorAction SilentlyContinue }
    if (-not (Test-Path -LiteralPath (Join-Path $destination $component.sentinel) -PathType Leaf)) {
        throw ('The extracted ' + $component.name + ' sources lack ' + $component.sentinel + '.')
    }
    $results.Add([pscustomobject]@{ component = $component.name; archive = $component.file; sha256 = $pin.sha256; downloaded = $downloaded; result = $action })
}
$results
Write-Host 'Reference sources for the parity oracle are in place and match their pinned archives.'
