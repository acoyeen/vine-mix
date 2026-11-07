<#
.SYNOPSIS
Builds the Rust VineMix release candidates: the installed payload and its
Setup, and the standalone single-file executable.

.DESCRIPTION
Windows PowerShell 5.1 compatible. Uses the pinned toolchains under .tools
(LLVM-MinGW, Inno Setup) and the verified driver archives listed in
installer\drivers.json. Nothing is installed, launched against user data or
published; every candidate stays in a new staging directory.
#>
[CmdletBinding()]
param(
    [string]$StageDirectory = '',
    [string]$OutputDirectory = '',
    [string]$InnoSetupDirectory = '',
    [string]$DriverCacheDirectory = '',
    [switch]$SkipInstaller
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$utf8 = New-Object Text.UTF8Encoding($false)

function Write-Utf8([string]$Path, [string]$Text) { [IO.File]::WriteAllText($Path, $Text, $utf8) }
function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }
function Copy-Checked([string]$Source, [string]$Destination) {
    $null = New-Item -ItemType Directory -Force -Path (Split-Path $Destination -Parent)
    Copy-Item -LiteralPath $Source -Destination $Destination
    if ((Hash $Source) -ne (Hash $Destination)) { throw "Copy integrity failed: $Destination" }
}
function Assert-FileVersion([string]$Path, [string]$Version, [string]$Numeric) {
    # Released update workers parse the FileVersion string; it must be numeric.
    $info = [Diagnostics.FileVersionInfo]::GetVersionInfo($Path)
    $fixed = '{0}.{1}.{2}.{3}' -f $info.FileMajorPart, $info.FileMinorPart, $info.FileBuildPart, $info.FilePrivatePart
    if ($info.FileVersion -cne $Numeric -or $fixed -ne $Numeric -or ($info.ProductVersion -split '\+')[0] -cne $Version) {
        throw "Release version metadata mismatch for $Path"
    }
}

$release = Get-Content -LiteralPath (Join-Path $project 'release.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$version = $release.version
$numeric = $release.numericVersion
if (-not $StageDirectory) { $StageDirectory = Join-Path $env:LOCALAPPDATA ('VineMix.BuildCache\rust-release-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff')) }
$stage = [IO.Path]::GetFullPath($StageDirectory)
if (Test-Path -LiteralPath $stage) { throw 'Choose a new StageDirectory; earlier candidates are retained.' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $stage 'output' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
$null = New-Item -ItemType Directory -Force -Path $stage, $output

# Toolchain from the checkout.
$mingw = Get-ChildItem -LiteralPath (Join-Path $project '.tools') -Directory | Where-Object Name -like 'llvm-mingw-*' | Select-Object -First 1
if (-not $mingw) { throw 'The pinned LLVM-MinGW toolchain is missing from .tools.' }
$env:PATH = (Join-Path $env:USERPROFILE '.cargo\bin') + ';' + (Join-Path $mingw.FullName 'bin') + ';' + $env:PATH
Push-Location $project
try {
    & cargo build --release --locked -p vinemix-ui -p vinemix-engine -p vinemix-setup-helper
    if ($LASTEXITCODE -ne 0) { throw 'cargo build failed.' }
    & py -3 (Join-Path $project 'tools\rust-notices.py')
    if ($LASTEXITCODE -ne 0) { throw 'Third-party notice generation failed.' }
} finally { Pop-Location }
$built = Join-Path $project 'target\release'

# Notices shipped with every distribution (Rust build allowlist).
$licenseNames = @('IBM-Plex-OFL.txt', 'libsamplerate-LICENSE', 'libsamplerate-source-notices.txt', 'SpeexDSP-COPYING.txt',
    'SpeexDSP-source-notices.txt', 'RNNoise-COPYING.txt', 'RNNoise-source-notices.txt', 'LLVM-runtime-LICENSE.txt',
    'MinGW-w64-COPYING.txt', 'MinGW-runtime-COPYING.txt', 'W3C-Audio-EQ-Cookbook-NOTICE.txt',
    'Microsoft-Windows-classic-samples-LICENSE.txt', 'Rust-crates.txt')
function Copy-Notices([string]$Destination) {
    foreach ($name in @('LICENSE', 'ATTRIBUTION.md', 'THIRD_PARTY_NOTICES.md')) { Copy-Checked (Join-Path $project $name) (Join-Path $Destination $name) }
    foreach ($name in $licenseNames) {
        $file = Join-Path $project ('licenses\' + $name)
        if (-not (Test-Path -LiteralPath $file -PathType Leaf) -or (Get-Item -LiteralPath $file).Length -eq 0) { throw "Required notice missing: $name" }
        Copy-Checked $file (Join-Path $Destination ('licenses\' + $name))
    }
}

# Installed payload.
$payload = Join-Path $stage 'payload'
Copy-Checked (Join-Path $built 'vinemix.exe') (Join-Path $payload 'VineMix.exe')
Copy-Checked (Join-Path $built 'vinemix-engine.exe') (Join-Path $payload 'VineMix.Engine.exe')
Copy-Notices $payload
Copy-Checked (Join-Path $project 'crates\vinemix-ui\assets\fonts\IBM-Plex-LICENSE.txt') (Join-Path $payload 'Assets\Fonts\IBM-Plex-LICENSE.txt')
Copy-Checked (Join-Path $project 'installer\PACKAGE_README.md') (Join-Path $payload 'README.md')
foreach ($name in @('VineMix.exe', 'VineMix.Engine.exe')) { Assert-FileVersion (Join-Path $payload $name) $version $numeric }
$engineVersion = (& (Join-Path $payload 'VineMix.Engine.exe') --version | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or -not $engineVersion.Contains($version)) { throw 'The engine release metadata does not match release.json.' }
$smoke = Join-Path $stage ('smoke-installed-' + [Guid]::NewGuid().ToString('N'))
& (Join-Path $payload 'VineMix.exe') --release-smoke $smoke | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'The installed release smoke check failed. See ' + $smoke }

# Standalone single-file executable.
$runtime = Join-Path $stage 'runtime-source'
Copy-Checked (Join-Path $built 'vinemix-engine.exe') (Join-Path $runtime 'VineMix.Engine.exe')
Copy-Notices $runtime
$packed = Join-Path $stage 'runtime'
& py -3 (Join-Path $project 'tools\pack-runtime.py') $runtime $packed
if ($LASTEXITCODE -ne 0) { throw 'Runtime packing failed.' }
$env:VINEMIX_RUNTIME_ZIP = Join-Path $packed 'VineMix.Runtime.zip'
$env:VINEMIX_RUNTIME_MANIFEST = Join-Path $packed 'VineMix.Runtime.manifest.json'
Push-Location $project
try {
    & cargo build --release --locked -p vinemix-ui --features standalone --target-dir (Join-Path $project 'target\standalone')
    if ($LASTEXITCODE -ne 0) { throw 'The standalone build failed.' }
} finally {
    Pop-Location
    Remove-Item Env:VINEMIX_RUNTIME_ZIP, Env:VINEMIX_RUNTIME_MANIFEST -ErrorAction SilentlyContinue
}
$standalone = Join-Path $output ('VineMix-' + $version + '-Standalone-x64.exe')
Copy-Checked (Join-Path $project 'target\standalone\release\vinemix.exe') $standalone
Assert-FileVersion $standalone $version $numeric
$smoke = Join-Path $stage ('smoke-standalone-' + [Guid]::NewGuid().ToString('N'))
& $standalone --release-smoke $smoke | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'The standalone release smoke check failed. See ' + $smoke }

if (-not $SkipInstaller) {
    $support = Join-Path $payload 'SetupSupport'
    foreach ($name in @('TERMS.txt', 'INSTALLATION.txt')) { Copy-Checked (Join-Path $project ('installer\' + $name)) (Join-Path $support $name) }
    Copy-Checked (Join-Path $project 'installer\notices\DRIVER-NOTICES.txt') (Join-Path $support 'DRIVER-NOTICES.txt')
    Copy-Checked (Join-Path $built 'vinemix-setup-helper.exe') (Join-Path $support 'VineMix.SetupHelper.exe')
    Assert-FileVersion (Join-Path $support 'VineMix.SetupHelper.exe') $version $numeric
    & (Join-Path $support 'VineMix.SetupHelper.exe') --self-test
    if ($LASTEXITCODE -ne 0) { throw 'Installer helper policy tests failed.' }
    Copy-Checked (Join-Path $project 'installer\drivers.json') (Join-Path $support 'drivers.json')
    if (-not $InnoSetupDirectory) { $InnoSetupDirectory = Join-Path $project '.tools\innosetup-7.1.0' }
    $compiler = Join-Path $InnoSetupDirectory 'ISCC.exe'
    if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) { throw 'Inno Setup 7.1.0 (ISCC.exe) is required; pass -InnoSetupDirectory.' }
    Copy-Checked (Join-Path $InnoSetupDirectory 'license.txt') (Join-Path $support 'Inno-Setup-LICENSE.txt')
    $drivers = Get-Content -LiteralPath (Join-Path $project 'installer\drivers.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $cache = if ($DriverCacheDirectory) { [IO.Path]::GetFullPath($DriverCacheDirectory) } else { Join-Path $project '.tools\downloads' }
    foreach ($driver in $drivers.drivers) {
        $archive = Join-Path $cache $driver.archiveName
        if (-not (Test-Path -LiteralPath $archive -PathType Leaf) -or (Hash $archive) -ne $driver.archiveSha256.ToUpperInvariant()) {
            throw ('Verified driver archive missing or changed: ' + $driver.archiveName)
        }
    }
    # The package inventory the update worker verifies (no byte-order mark).
    $files = @(Get-ChildItem -LiteralPath $payload -File -Recurse | Sort-Object FullName | ForEach-Object {
        $relative = $_.FullName.Substring($payload.Length + 1).Replace('\', '/')
        '    {"path":' + (ConvertTo-Json $relative) + ',"size":' + $_.Length + ',"sha256":"' + (Hash $_.FullName) + '"}'
    })
    $inventory = "{`n  ""version"": " + (ConvertTo-Json $version) + ",`n  ""numericVersion"": " + (ConvertTo-Json $numeric) + ",`n  ""files"": [`n" + ($files -join ",`n") + "`n  ]`n}`n"
    Write-Utf8 (Join-Path $support 'package-manifest.json') $inventory
    $installer = Join-Path $output ('VineMix-' + $version + '-Setup-x64.exe')
    if (Test-Path -LiteralPath $installer) { throw 'The output installer already exists; choose a new output directory.' }
    $vb = $drivers.drivers | Where-Object id -eq 'vb-cable'
    $vac = $drivers.drivers | Where-Object id -eq 'vac-lite'
    $arguments = @('/Qp', ('/DPayloadDir=' + $payload), ('/DAppVersion=' + $version), ('/DNumericVersion=' + $numeric),
        ('/DDisplayVersion=' + $release.displayVersion), ('/DDriverArchiveDir=' + $cache), ('/DOutputDir=' + $output),
        ('/DVbArchiveHash=' + $vb.archiveSha256), ('/DVacArchiveHash=' + $vac.archiveSha256), (Join-Path $project 'installer\VineMix.iss'))
    & $compiler @arguments 2>&1 | Tee-Object -FilePath (Join-Path $stage 'compiler.log') | Out-Null
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $installer)) { throw 'Installer compilation failed. See compiler.log in the stage directory.' }
}

foreach ($file in Get-ChildItem -LiteralPath $output -File -Filter '*.exe') {
    Write-Utf8 ($file.FullName + '.sha256') ((Hash $file.FullName) + '  ' + $file.Name + "`n")
    Write-Host ($file.Name + '  ' + (Hash $file.FullName))
}
Write-Host ('Stage: ' + $stage)
Write-Host 'Unpublished release candidates built. No application session or driver was installed or launched.'
