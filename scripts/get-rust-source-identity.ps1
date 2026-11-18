<#
.SYNOPSIS
Describes the Rust build inputs of a checkout: its commit, whether the tree
matches that commit, and a digest of every input file.

.DESCRIPTION
Windows PowerShell 5.1 compatible and read-only. Git resolves the commit,
the tree state and the file list, so linked worktrees (where .git is a file)
and packed refs need no special handling. A directory that is not the top of
a Git work tree is rejected rather than described by a parent repository.

The snapshot digest covers what a Rust release is built from: the workspace
manifests and lock file, the toolchain pins, every crate, the build helpers,
release metadata, the notices and installer inputs, the update trust file and
the release scripts, signing tool and compatible update verifier. Tracked files and untracked files that are
not ignored are both included, because Cargo compiles either.
#>
[CmdletBinding()]
param([string]$Project = '', [string]$OutputPath = '')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $Project) { $Project = Join-Path $PSScriptRoot '..' }
$project = [IO.Path]::GetFullPath($Project).TrimEnd('\')
$scope = @('Cargo.toml', 'Cargo.lock', 'rust-toolchain.toml', '.cargo', 'crates', 'tools', 'release.json', 'LICENSE',
    'ATTRIBUTION.md', 'THIRD_PARTY_NOTICES.md', 'licenses', 'installer', 'updates', 'scripts/build-rust-release.ps1',
    'scripts/rust-env.ps1', 'scripts/get-rust-source-identity.ps1', 'scripts/prepare-update-release.ps1',
    'scripts/update-release-tools', 'desktop/VineMix.UpdateCore')

function Invoke-SourceGit([string[]]$Arguments) {
    # File names are reported as UTF-8, unquoted, one per line.
    $previous = [Console]::OutputEncoding
    try {
        [Console]::OutputEncoding = New-Object Text.UTF8Encoding($false)
        $lines = @(& git --no-optional-locks -C $project -c core.quotepath=off @Arguments)
        if ($LASTEXITCODE -ne 0) { throw ('git ' + ($Arguments -join ' ') + ' failed.') }
        return , $lines
    } finally { [Console]::OutputEncoding = $previous }
}
function Get-PhysicalDirectory([string]$Path) {
    (Get-Item -LiteralPath $Path -Force).FullName.TrimEnd('\')
}

# No stream redirection here: Windows PowerShell turns redirected native error
# output into a terminating error. Git's own message is left on the console.
$top = @(& git --no-optional-locks -C $project rev-parse --show-toplevel)
if ($LASTEXITCODE -ne 0 -or $top.Count -ne 1) { throw 'The source directory is not a Git checkout; its inputs cannot be tied to a commit.' }
if ((Get-PhysicalDirectory ([IO.Path]::GetFullPath($top[0]))) -ine (Get-PhysicalDirectory $project)) {
    throw 'The source directory is inside another repository, not the top of its own checkout.'
}
$head = (Invoke-SourceGit @('rev-parse', '--verify', 'HEAD'))[0]
if ($head -notmatch '^[0-9a-f]{40}$') { throw 'Could not read the source revision.' }
$changes = Invoke-SourceGit @('status', '--porcelain=v1', '--untracked-files=normal')
$inputChanges = Invoke-SourceGit (@('status', '--porcelain=v1', '--untracked-files=normal', '--') + $scope)
$linked = (Invoke-SourceGit @('rev-parse', '--absolute-git-dir'))[0] -ine (Invoke-SourceGit @('rev-parse', '--path-format=absolute', '--git-common-dir'))[0]
# Git calls a file unmodified when only its line endings were converted on
# the way to the work tree. The compiler reads the work tree's bytes, though
# (and some files are embedded verbatim), so such a tree is not byte-identical
# to its commit and must not be presented as if it were.
$converted = New-Object Collections.Generic.List[string]
foreach ($line in (Invoke-SourceGit (@('ls-files', '--eol', '--') + $scope))) {
    $entry = [regex]::Match($line, '^i/(\S*)\s+w/(\S*)\s+attr/[^\t]*\t(.+)$')
    if ($entry.Success -and $entry.Groups[1].Value -and $entry.Groups[2].Value -and $entry.Groups[1].Value -cne $entry.Groups[2].Value) {
        $converted.Add($entry.Groups[3].Value)
    }
}

$listed = Invoke-SourceGit (@('ls-files', '--cached', '--others', '--exclude-standard', '--') + $scope)
$names = [string[]]@($listed | Where-Object { $_ })
$names = [string[]]@($names | Select-Object -Unique)
[Array]::Sort($names, [StringComparer]::Ordinal)
$sha = [Security.Cryptography.SHA256]::Create()
try {
    $files = New-Object Collections.Generic.List[object]
    $canonical = New-Object Text.StringBuilder
    foreach ($name in $names) {
        $path = Join-Path $project ($name.Replace('/', '\'))
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            $stream = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
            try {
                $digest = ([BitConverter]::ToString($sha.ComputeHash($stream)) -replace '-', '').ToLowerInvariant()
                $size = $stream.Length
            } finally { $stream.Dispose() }
            $files.Add([ordered]@{ path = $name; size = $size; sha256 = $digest })
            $null = $canonical.Append($digest).Append('  ').Append($size).Append('  ').Append($name).Append("`n")
        } else {
            # Tracked but deleted from the work tree: part of the difference.
            $files.Add([ordered]@{ path = $name; missing = $true })
            $null = $canonical.Append('missing  ').Append($name).Append("`n")
        }
    }
    $bytes = (New-Object Text.UTF8Encoding($false)).GetBytes($canonical.ToString())
    $snapshot = ([BitConverter]::ToString($sha.ComputeHash($bytes)) -replace '-', '').ToLowerInvariant()
} finally { $sha.Dispose() }

$identity = [ordered]@{
    gitHead = $head
    gitDirty = ($changes.Count -gt 0)
    inputsDirty = ($inputChanges.Count -gt 0)
    changedInputs = @($inputChanges)
    # Inputs whose line endings differ between the commit and the work tree.
    lineEndingsConverted = $converted.ToArray()
    # True only when the build inputs are byte for byte those of the commit.
    inputsMatchCommit = ($changes.Count -eq 0 -and $converted.Count -eq 0)
    linkedWorktree = $linked
    sourceSnapshotSha256 = $snapshot
    snapshotFormat = 'SHA-256 of UTF-8 lines "<sha256>  <size>  <path>" (or "missing  <path>"), ordinal path order, LF line ends'
    scope = 'Workspace manifests and lock file, toolchain pins, crates, tools, release.json, root notices, licenses, installer, updates, Rust release scripts, signing tool and update verifier; tracked files plus untracked files that are not ignored.'
    fileCount = $files.Count
    files = $files.ToArray()
}
if ($OutputPath) {
    [IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputPath), ($identity | ConvertTo-Json -Depth 6), (New-Object Text.UTF8Encoding($false)))
}
[pscustomobject]$identity
