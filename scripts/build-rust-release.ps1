<#
.SYNOPSIS
Builds the Rust VineMix release candidates: the installed payload and its
Setup, and the standalone single-file executable.

.DESCRIPTION
Windows PowerShell 5.1 compatible. Uses the exact Rust release, build target
and LLVM-MinGW named by rust-toolchain.toml and tools\rust-pins.json (see
scripts\rust-env.ps1), Inno Setup under .tools, and the verified driver
archives listed in installer\drivers.json. Nothing is installed, launched
against user data or published; every candidate stays in a new staging
directory.

A release candidate is built from a source tree that matches its commit. The
tree is checked before compiling and again afterwards, and the output
directory receives a build receipt: the commit, a digest of every build
input, the toolchain identities and the SHA-256 of every produced file. Each
desktop executable reports the same commit and digest under About and in its
inert smoke report, and the script checks that they agree.
#>
[CmdletBinding()]
param(
    [string]$StageDirectory = '',
    [string]$OutputDirectory = '',
    [string]$InnoSetupDirectory = '',
    [string]$DriverCacheDirectory = '',
    [string]$ToolsDirectory = '',
    [string]$PythonPath = '',
    [switch]$SkipInstaller,
    # Builds from a tree that differs from its commit. Every identity then says
    # "dirty" and the receipt is not a release-candidate receipt.
    [switch]$AllowDirty
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
    # Inno Setup pads its editable string resources. Older workers parse the
    # numeric text; verify it as well as the fixed file version.
    $padding = [char[]]@(' ', [char]0)
    $fileText = ([string]$info.FileVersion).TrimEnd($padding)
    $productText = ([string]$info.ProductVersion).TrimEnd($padding)
    if ($fileText -cne $Numeric -or $fixed -ne $Numeric -or ($productText -split '\+')[0] -cne $Version) {
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
$finalOutput = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\')
if ($finalOutput -ieq $stage -or (Test-Path -LiteralPath $finalOutput)) { throw 'Choose a fresh output directory separate from the stage.' }
if (-not $ToolsDirectory) { $ToolsDirectory = Join-Path $project '.tools' }
$ToolsDirectory = [IO.Path]::GetFullPath($ToolsDirectory)
if ($PythonPath) { $python = $PythonPath; $pythonArgs = @() } else { $python = 'py'; $pythonArgs = @('-3') }

# The exact Rust release, target and LLVM-MinGW; never the first llvm-mingw-*
# directory found, nor whatever target the installed Rust defaults to.
. (Join-Path $PSScriptRoot 'rust-env.ps1')
$toolchain = Initialize-RustEnvironment -Project $project -ToolsDirectory $ToolsDirectory
$target = $toolchain.target

# Source identity, before anything is compiled or created.
$sourceScript = Join-Path $PSScriptRoot 'get-rust-source-identity.ps1'
$source = & $sourceScript -Project $project
if (-not $source.inputsMatchCommit -and -not $AllowDirty) {
    if (-not $source.gitDirty) {
        throw ('The build inputs are not byte for byte those of the commit: ' + @($source.lineEndingsConverted).Count + ' file(s) have converted line endings in this work tree (for example ' + @($source.lineEndingsConverted)[0] + '). Build from a checkout that follows .gitattributes, or pass -AllowDirty for a candidate that is labelled dirty.')
    }
    throw 'The source tree differs from its commit, so the result could not be tied to that commit. Commit or stash the changes, or pass -AllowDirty for a candidate that is labelled dirty.'
}
$revision = if ($source.inputsMatchCommit) { $source.gitHead } else { $source.gitHead + '-dirty' }
$buildTime = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
$null = New-Item -ItemType Directory -Path $stage
$finalParent = Split-Path -Parent $finalOutput
$null = New-Item -ItemType Directory -Force -Path $finalParent
# The final directory appears in one rename, only after every check has passed.
$output = Join-Path $finalParent ('.vinemix-building-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $output
$targetDirectory = Join-Path $stage 'cargo'
$cargoHome = if ($env:CARGO_HOME) { $env:CARGO_HOME } else { Join-Path $env:USERPROFILE '.cargo' }
$rustupHome = if ($env:RUSTUP_HOME) { $env:RUSTUP_HOME } else { Join-Path $env:USERPROFILE '.rustup' }
$mappings = [ordered]@{}
# rustc applies the last matching prefix, so place specific mappings last.
foreach ($pair in @(
    ,@($env:USERPROFILE, '/build/home')
    ,@($cargoHome, '/build/cargo')
    ,@($rustupHome, '/build/rustup')
    ,@($project, '/build/vinemix')
    ,@($stage, '/build/stage')
    ,@($ToolsDirectory, '/build/tools')
)) { $mappings[[IO.Path]::GetFullPath($pair[0])] = $pair[1] }
# Cargo's encoded flags replace target.rustflags. Preserve the checked-in
# CRT linkage explicitly and refuse additional flags supplied by the caller.
if ($env:RUSTFLAGS -or $env:CARGO_ENCODED_RUSTFLAGS) { throw 'Clear inherited Rust compiler flags for a release build.' }
$rustFlags = @('-C', 'target-feature=+crt-static') + @(
    foreach ($prefix in $mappings.Keys) { '--remap-path-prefix=' + $prefix + '=' + $mappings[$prefix] }
)
$oldVars = @{}
foreach ($name in @('VINEMIX_SOURCE_REVISION','VINEMIX_SOURCE_SNAPSHOT_SHA256','VINEMIX_BUILD_TIMESTAMP',
                     'VINEMIX_RUNTIME_ZIP','VINEMIX_RUNTIME_MANIFEST','CARGO_ENCODED_RUSTFLAGS')) {
    $oldVars[$name] = [Environment]::GetEnvironmentVariable($name)
}
try {
$env:CARGO_ENCODED_RUSTFLAGS = $rustFlags -join [char]31

function Assert-BuildIdentity([string]$SmokeDirectory, [string]$Mode) {
    # The executable that just ran reports what was compiled into it.
    $report = Get-Content -LiteralPath (Join-Path $SmokeDirectory 'results.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $build = if ($report.PSObject.Properties['build']) { $report.build } else { $null }
    # Newer PowerShell turns ISO timestamp strings into DateTime objects.
    $reportedTime = $build.builtUtc
    if ($reportedTime -is [DateTime]) { $reportedTime = $reportedTime.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ') }
    if (-not $report.passed -or $report.mode -cne $Mode -or $report.version -cne $version -or $null -eq $build -or
        [string]$build.sourceRevision -cne $revision -or [string]$build.sourceSnapshotSha256 -cne $source.sourceSnapshotSha256 -or
        [string]$reportedTime -cne $buildTime) {
        throw ($Mode + ' desktop did not pass the expected mode and source-identity checks.')
    }
}

$env:VINEMIX_SOURCE_REVISION = $revision
$env:VINEMIX_SOURCE_SNAPSHOT_SHA256 = $source.sourceSnapshotSha256
$env:VINEMIX_BUILD_TIMESTAMP = $buildTime
Push-Location $project
try {
    & cargo build --release --locked --target $target --target-dir $targetDirectory -p vinemix-ui -p vinemix-engine -p vinemix-setup-helper
    if ($LASTEXITCODE -ne 0) { throw 'cargo build failed.' }
    # Checked, not rewritten: a release never modifies its own source tree.
    & $python @pythonArgs (Join-Path $project 'tools\rust-notices.py') --check --require-reviewed
    if ($LASTEXITCODE -ne 0) { throw 'licenses\Rust-crates.txt does not match the locked dependencies. Run "py -3 tools\rust-notices.py", review and commit the result.' }
} finally { Pop-Location }
$built = Join-Path $targetDirectory ($target + '\release')

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
Assert-BuildIdentity $smoke 'Installed'

# Standalone single-file executable.
$runtime = Join-Path $stage 'runtime-source'
Copy-Checked (Join-Path $built 'vinemix-engine.exe') (Join-Path $runtime 'VineMix.Engine.exe')
Copy-Notices $runtime
$packed = Join-Path $stage 'runtime'
& $python @pythonArgs (Join-Path $project 'tools\pack-runtime.py') $runtime $packed
if ($LASTEXITCODE -ne 0) { throw 'Runtime packing failed.' }
$env:VINEMIX_RUNTIME_ZIP = Join-Path $packed 'VineMix.Runtime.zip'
$env:VINEMIX_RUNTIME_MANIFEST = Join-Path $packed 'VineMix.Runtime.manifest.json'
Push-Location $project
try {
    $features = 'standalone'
    & cargo build --release --locked --target $target -p vinemix-ui --features $features --target-dir $targetDirectory
    if ($LASTEXITCODE -ne 0) { throw 'The standalone build failed.' }
} finally {
    Pop-Location
    Remove-Item Env:VINEMIX_RUNTIME_ZIP, Env:VINEMIX_RUNTIME_MANIFEST -ErrorAction SilentlyContinue
}
$standalone = Join-Path $output ('VineMix-' + $version + '-Standalone-x64.exe')
Copy-Checked (Join-Path $built 'vinemix.exe') $standalone
Assert-FileVersion $standalone $version $numeric
$smoke = Join-Path $stage ('smoke-standalone-' + [Guid]::NewGuid().ToString('N'))
& $standalone --release-smoke $smoke | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'The standalone release smoke check failed. See ' + $smoke }
Assert-BuildIdentity $smoke 'Standalone'

if (-not $SkipInstaller) {
    $support = Join-Path $payload 'SetupSupport'
    foreach ($name in @('TERMS.txt', 'INSTALLATION.txt')) { Copy-Checked (Join-Path $project ('installer\' + $name)) (Join-Path $support $name) }
    Copy-Checked (Join-Path $project 'installer\notices\DRIVER-NOTICES.txt') (Join-Path $support 'DRIVER-NOTICES.txt')
    Copy-Checked (Join-Path $built 'vinemix-setup-helper.exe') (Join-Path $support 'VineMix.SetupHelper.exe')
    Assert-FileVersion (Join-Path $support 'VineMix.SetupHelper.exe') $version $numeric
    & (Join-Path $support 'VineMix.SetupHelper.exe') --self-test
    if ($LASTEXITCODE -ne 0) { throw 'Installer helper policy tests failed.' }
    Copy-Checked (Join-Path $project 'installer\drivers.json') (Join-Path $support 'drivers.json')
    if (-not $InnoSetupDirectory) { $InnoSetupDirectory = Join-Path $ToolsDirectory 'innosetup-7.1.0' }
    $compiler = Join-Path $InnoSetupDirectory 'ISCC.exe'
    if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) { throw 'Inno Setup 7.1.0 (ISCC.exe) is required; pass -InnoSetupDirectory.' }
    Copy-Checked (Join-Path $InnoSetupDirectory 'license.txt') (Join-Path $support 'Inno-Setup-LICENSE.txt')
    $drivers = Get-Content -LiteralPath (Join-Path $project 'installer\drivers.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $cache = if ($DriverCacheDirectory) { [IO.Path]::GetFullPath($DriverCacheDirectory) } else { Join-Path $ToolsDirectory 'downloads\installer-drivers' }
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
    Assert-FileVersion $installer $version $numeric
}

$packages = @((Get-Item -LiteralPath $standalone))
if (-not $SkipInstaller) { $packages += Get-Item -LiteralPath $installer }
# Scan uncompressed inputs before treating an opaque installer as distributable.
$scanFiles = @((Get-ChildItem -LiteralPath $payload -File -Recurse).FullName) + @($packages.FullName)
$scanArgs = @((Join-Path $project 'tools\check-release-paths.py'))
foreach ($prefix in $mappings.Keys) { $scanArgs += @('--prefix', $prefix) }
& $python @pythonArgs @scanArgs -- @scanFiles
if ($LASTEXITCODE -ne 0) { throw 'A release artifact contains a private build path.' }

# The receipt describes one source state only if nothing changed meanwhile.
$sourceAfter = & $sourceScript -Project $project
if ($sourceAfter.gitHead -cne $source.gitHead -or $sourceAfter.gitDirty -ne $source.gitDirty -or $sourceAfter.inputsMatchCommit -ne $source.inputsMatchCommit -or
    $sourceAfter.sourceSnapshotSha256 -cne $source.sourceSnapshotSha256) {
    throw 'The source tree changed while building. These candidates do not correspond to one source state; build again.'
}

function Get-Sha256Lower([string]$Path) { (Hash $Path).ToLowerInvariant() }
function Get-OutputRecord([string]$Root, [string]$Label, $File) {
    [ordered]@{ path = ($Label + '/' + $File.FullName.Substring($Root.Length + 1).Replace('\', '/')); size = $File.Length; sha256 = (Get-Sha256Lower $File.FullName) }
}
$produced = New-Object Collections.Generic.List[object]
foreach ($file in Get-ChildItem -LiteralPath $payload -File -Recurse | Sort-Object FullName) { $produced.Add((Get-OutputRecord $payload 'payload' $file)) }
foreach ($file in Get-ChildItem -LiteralPath $packed -File -Recurse | Sort-Object FullName) { $produced.Add((Get-OutputRecord $packed 'runtime' $file)) }
foreach ($file in $packages) { $produced.Add((Get-OutputRecord $output 'output' $file)) }
$inputs = [ordered]@{}
foreach ($name in @('Cargo.lock', 'Cargo.toml', 'rust-toolchain.toml', '.cargo\config.toml', 'tools\rust-pins.json', 'release.json', 'installer\drivers.json')) {
    $inputs[$name.Replace('\', '/')] = Get-Sha256Lower (Join-Path $project $name)
}
$receipt = [ordered]@{
    format = 1
    product = 'VineMix'
    version = $version
    numericVersion = $numeric
    builtUtc = $buildTime
    # False for -AllowDirty builds: the commit alone does not describe them.
    releaseCandidate = [bool]$source.inputsMatchCommit
    embeddedSourceRevision = $revision
    source = [ordered]@{
        gitHead = $source.gitHead
        gitDirty = $source.gitDirty
        inputsDirty = $source.inputsDirty
        changedInputs = @($source.changedInputs)
        lineEndingsConverted = @($source.lineEndingsConverted)
        inputsMatchCommit = $source.inputsMatchCommit
        linkedWorktree = $source.linkedWorktree
        sourceSnapshotSha256 = $source.sourceSnapshotSha256
        snapshotFormat = $source.snapshotFormat
        scope = $source.scope
        fileCount = $source.fileCount
        files = $source.files
    }
    toolchain = [ordered]@{
        target = $toolchain.target
        rustRelease = $toolchain.rustRelease
        rustc = $toolchain.rustc
        cargo = $toolchain.cargo
        rustHost = $toolchain.rustHost
        llvmMingw = $toolchain.llvmMingw
        clang = $toolchain.clang
    }
    inputs = $inputs
    build = [ordered]@{
        profile = 'release'
        cargoArguments = ('build --release --locked --target ' + $target)
        packages = @('vinemix-ui', 'vinemix-engine', 'vinemix-setup-helper')
        standaloneFeatures = $features
        installerBuilt = (-not $SkipInstaller)
    }
    verified = @(
        'Source tree identical before and after the build',
        'Version resources of every executable match release.json',
        'Installed and standalone desktop executables report this revision, snapshot digest and build time',
        'Final packages and uncompressed payload checked for private build paths',
        'Third-party notices match the locked dependencies'
    )
    outputs = $produced.ToArray()
}
$receiptPath = Join-Path $output ('VineMix-' + $version + '-build-receipt.json')
Write-Utf8 $receiptPath (($receipt | ConvertTo-Json -Depth 8) + "`n")

foreach ($file in $packages) {
    Write-Utf8 ($file.FullName + '.sha256') ((Hash $file.FullName) + '  ' + $file.Name + "`n")
    Write-Host ($file.Name + '  ' + (Hash $file.FullName))
}
[IO.Directory]::Move($output, $finalOutput)
$receiptPath = Join-Path $finalOutput (Split-Path -Leaf $receiptPath)
Write-Host ('Output: ' + $finalOutput)
Write-Host ('Source: ' + $revision + '  snapshot ' + $source.sourceSnapshotSha256)
Write-Host ('Receipt: ' + (Split-Path -Leaf $receiptPath) + '  ' + (Hash $receiptPath))
Write-Host ('Stage: ' + $stage)
if (-not $source.inputsMatchCommit) { Write-Host 'This build came from inputs that differ from its commit (-AllowDirty). It is not a release candidate.' }
Write-Host 'Unpublished release candidates built. No application session or driver was installed or launched.'
} finally {
    foreach ($name in $oldVars.Keys) { [Environment]::SetEnvironmentVariable($name, $oldVars[$name]) }
}
