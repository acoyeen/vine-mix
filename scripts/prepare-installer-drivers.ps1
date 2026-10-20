[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$DestinationDirectory,
    [string]$CacheDirectory = '',
    [switch]$Offline
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$manifestPath = Join-Path $projectRoot 'installer\drivers.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1) { throw 'Unsupported installer driver manifest version.' }
if ($env:OS -ne 'Windows_NT') { throw 'Driver preparation requires Windows Authenticode verification.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.Net.Http

function Assert-NoReparsePoint([string]$Path) {
    $candidate = [IO.Path]::GetFullPath($Path)
    while ($candidate) {
        if (Test-Path -LiteralPath $candidate) {
            $attributes = (Get-Item -LiteralPath $candidate -Force).Attributes
            if (($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Refusing to prepare driver files through a reparse point: $candidate"
            }
        }
        $parent = [IO.Path]::GetDirectoryName($candidate)
        if ($parent -eq $candidate) { break }
        $candidate = $parent
    }
}

function Get-ContainedPath([string]$Root, [string]$Relative) {
    if ([string]::IsNullOrWhiteSpace($Relative) -or [IO.Path]::IsPathRooted($Relative)) {
        throw "Invalid archive-relative path: $Relative"
    }
    $normalized = $Relative.Replace('\', '/').TrimEnd('/')
    foreach ($segment in $normalized.Split('/')) {
        if ($segment -eq '' -or $segment -eq '.' -or $segment -eq '..' -or
            $segment -match '[<>:"|?*\x00-\x1f]' -or $segment -match '[. ]$' -or
            $segment -match '^(?i:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)') {
            throw "Unsafe archive path: $Relative"
        }
    }
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    $result = [IO.Path]::GetFullPath((Join-Path $rootFull $normalized))
    if (-not $result.StartsWith($rootFull, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Archive path escapes its destination: $Relative"
    }
    return $result
}

function Assert-Archive([string]$Path, $Driver) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Driver archive is missing: $Path" }
    Assert-NoReparsePoint $Path
    if ((Get-Item -LiteralPath $Path).Length -ne $Driver.archiveSizeBytes) {
        throw "Driver archive size does not match the reviewed package: $Path"
    }
    if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $Driver.archiveSha256) {
        throw "Driver archive SHA-256 does not match the reviewed package: $Path"
    }
}

function Receive-Archive([string]$Path, $Driver) {
    if ($Offline) { throw "Offline preparation requires the pinned archive in the cache: $Path" }
    $uri = [Uri]$Driver.downloadUrl
    if ($uri.Scheme -ne 'https' -or $uri.Host -notin @('download.vb-audio.com', 'software.muzychenko.net')) {
        throw "Driver download is not an approved official HTTPS location: $uri"
    }
    $partial = "$Path.$([Guid]::NewGuid().ToString('N')).partial"
    $handler = [Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $false
    $client = [Net.Http.HttpClient]::new($handler)
    $client.Timeout = [TimeSpan]::FromSeconds(120)
    $response = $null
    $source = $null
    $output = $null
    try {
        $response = $client.GetAsync($uri, [Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
        if ([int]$response.StatusCode -ne 200) {
            throw "Official driver download returned HTTP $([int]$response.StatusCode); redirects are not accepted."
        }
        if ($null -ne $response.Content.Headers.ContentLength -and
            $response.Content.Headers.ContentLength -ne $Driver.archiveSizeBytes) {
            throw 'Official driver download length changed; review and update the package pin before continuing.'
        }
        $source = $response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
        $output = [IO.File]::Open($partial, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        $buffer = New-Object byte[] 65536
        [long]$total = 0
        while (($read = $source.Read($buffer, 0, $buffer.Length)) -gt 0) {
            $total += $read
            if ($total -gt $Driver.archiveSizeBytes) { throw 'Official driver download exceeded its pinned size.' }
            $output.Write($buffer, 0, $read)
        }
        $output.Dispose(); $output = $null
        Assert-Archive $partial $Driver
        [IO.File]::Move($partial, $Path)
    }
    finally {
        if ($output) { $output.Dispose() }
        if ($source) { $source.Dispose() }
        if ($response) { $response.Dispose() }
        $client.Dispose()
        $handler.Dispose()
        if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial -Force }
    }
}

function Assert-Payload([string]$Root, $Driver) {
    Assert-NoReparsePoint $Root
    $actualFiles = @(Get-ChildItem -LiteralPath $Root -Recurse -File -Force)
    $actualEntries = @(Get-ChildItem -LiteralPath $Root -Recurse -Force)
    foreach ($entry in $actualEntries) {
        if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Driver payload contains a reparse point: $($entry.FullName)"
        }
    }
    $actualDirectories = @($actualEntries | Where-Object { $_.PSIsContainer })
    if ($actualDirectories.Count -ne $Driver.directories.Count) { throw "Driver payload folder structure changed: $Root" }
    foreach ($relative in $Driver.directories) {
        if (-not (Test-Path -LiteralPath (Get-ContainedPath $Root $relative) -PathType Container)) {
            throw "Driver payload is missing an original folder: $relative"
        }
    }
    if ($actualFiles.Count -ne $Driver.files.Count) { throw "Driver payload has missing or extra files: $Root" }
    foreach ($file in $Driver.files) {
        $path = Get-ContainedPath $Root $file.path
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or
            (Get-Item -LiteralPath $path).Length -ne $file.sizeBytes -or
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.sha256) {
            throw "Driver payload differs from the original vendor package: $path"
        }
    }
    foreach ($check in $Driver.signatureChecks) {
        $path = Get-ContainedPath $Root $check.path
        $signature = Get-AuthenticodeSignature -LiteralPath $path
        if ($signature.Status -ne 'Valid' -or -not $signature.SignerCertificate -or
            $signature.SignerCertificate.Thumbprint -ne $check.signerThumbprint) {
            throw "Driver file signature is invalid or its reviewed signer changed: $path (status $($signature.Status))."
        }
    }
}

$destinationRoot = [IO.Path]::GetFullPath($DestinationDirectory)
if (-not $CacheDirectory) { $CacheDirectory = Join-Path $projectRoot '.tools\downloads\installer-drivers' }
$cacheRoot = [IO.Path]::GetFullPath($CacheDirectory)
Assert-NoReparsePoint $destinationRoot
Assert-NoReparsePoint $cacheRoot
[IO.Directory]::CreateDirectory($destinationRoot) | Out-Null
[IO.Directory]::CreateDirectory($cacheRoot) | Out-Null

foreach ($driver in $manifest.drivers) {
    $archivePath = Get-ContainedPath $cacheRoot $driver.archiveName
    if (-not (Test-Path -LiteralPath $archivePath)) { Receive-Archive $archivePath $driver }
    Assert-Archive $archivePath $driver
    $payloadRoot = Get-ContainedPath $destinationRoot $driver.payloadDirectory
    Assert-NoReparsePoint $payloadRoot
    if (Test-Path -LiteralPath $payloadRoot) {
        Assert-Payload $payloadRoot $driver
        Write-Host "Verified existing complete $($driver.displayName) package."
        continue
    }
    $payloadParent = [IO.Path]::GetDirectoryName($payloadRoot)
    [IO.Directory]::CreateDirectory($payloadParent) | Out-Null
    $stagingRoot = Join-Path $payloadParent ('.' + $driver.id + '-' + [Guid]::NewGuid().ToString('N'))
    Assert-NoReparsePoint $stagingRoot
    [IO.Directory]::CreateDirectory($stagingRoot) | Out-Null
    $archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        if ($archive.Entries.Count -ne $driver.archiveEntryCount) { throw 'Driver archive entry count changed.' }
        $seen = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
        [long]$totalExpanded = 0
        foreach ($entry in $archive.Entries) {
            $path = Get-ContainedPath $stagingRoot $entry.FullName
            if (-not $seen.Add($path)) { throw "Duplicate archive entry: $($entry.FullName)" }
            # Reject UNIX symlinks and Windows reparse entries rather than following them.
            $unixType = ($entry.ExternalAttributes -shr 16) -band 0xF000
            if ($unixType -eq 0xA000 -or ($entry.ExternalAttributes -band 0x400) -ne 0) {
                throw "Archive contains a link: $($entry.FullName)"
            }
            $totalExpanded += $entry.Length
            if ($totalExpanded -gt $driver.uncompressedSizeBytes) { throw 'Driver archive expands beyond its reviewed size.' }
            if ($entry.FullName.EndsWith('/') -or $entry.FullName.EndsWith('\')) {
                [IO.Directory]::CreateDirectory($path) | Out-Null
            }
            else {
                [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)) | Out-Null
                [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $path, $false)
            }
        }
        if ($totalExpanded -ne $driver.uncompressedSizeBytes) { throw 'Driver archive expanded size changed.' }
        Assert-Payload $stagingRoot $driver
        [IO.Directory]::Move($stagingRoot, $payloadRoot)
        Write-Host "Prepared complete, unchanged $($driver.displayName) package."
    }
    finally {
        $archive.Dispose()
        if (Test-Path -LiteralPath $stagingRoot) {
            # The path is a unique task-created sibling of the final package; verify it before cleanup.
            $expected = Get-ContainedPath $destinationRoot ('drivers/' + [IO.Path]::GetFileName($stagingRoot))
            if ($expected -ne [IO.Path]::GetFullPath($stagingRoot)) { throw 'Refusing unsafe staging cleanup.' }
            Assert-NoReparsePoint $stagingRoot
            Remove-Item -LiteralPath $stagingRoot -Recurse -Force
        }
    }
}
Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $destinationRoot 'drivers.json') -Force
Write-Host 'Driver packages prepared. No installer was executed and no installed driver was changed.'
