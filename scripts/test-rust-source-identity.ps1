<#
.SYNOPSIS
Checks get-rust-source-identity.ps1 against disposable repositories: clean
and dirty trees, untracked and ignored files, a deleted input, a linked
worktree, packed refs, and directories that are not a checkout.

.DESCRIPTION
Windows PowerShell 5.1 compatible. Everything happens in a new temporary
directory outside this checkout; this repository is never modified.
#>
[CmdletBinding()]
param([string]$ResultDirectory = '')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$script = Join-Path $PSScriptRoot 'get-rust-source-identity.ps1'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $ResultDirectory) { $ResultDirectory = Join-Path ([IO.Path]::GetTempPath()) ('vinemix-source-identity-' + [guid]::NewGuid().ToString('N')) }
$root = [IO.Path]::GetFullPath($ResultDirectory)
if ($root.StartsWith($project + '\', [StringComparison]::OrdinalIgnoreCase) -or $root -ieq $project) { throw 'Choose a directory outside the source checkout.' }
if (Test-Path -LiteralPath $root) { throw 'Choose a new verification directory.' }
$null = New-Item -ItemType Directory -Path $root
$checks = New-Object Collections.Generic.List[string]
$passed = $false; $failure = $null
function Check([bool]$Condition, [string]$Description) {
    if (-not $Condition) { throw $Description }
    $checks.Add($Description)
}
function Reject([scriptblock]$Action, [string]$ExpectedMessage, [string]$Description) {
    $message = $null
    try { & $Action | Out-Null } catch { $message = $_.Exception.Message }
    Check ($null -ne $message -and $message -like $ExpectedMessage) $Description
}
function Invoke-FixtureGit([string]$Directory, [string[]]$Arguments) {
    $output = @(& git.exe -C $Directory -c user.name=Example -c user.email=example@example.invalid -c commit.gpgsign=false -c core.autocrlf=false @Arguments)
    if ($LASTEXITCODE -ne 0) { throw ('Fixture git ' + ($Arguments -join ' ') + ' failed.') }
    , $output
}
function Write-Fixture([string]$Path, [string]$Text) {
    $null = New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Path)
    [IO.File]::WriteAllText($Path, $Text, (New-Object Text.UTF8Encoding($false)))
}
function Identity([string]$Directory) { & $script -Project $Directory }
try {
    $fixture = Join-Path $root 'checkout'
    $null = New-Item -ItemType Directory -Path $fixture
    $null = Invoke-FixtureGit $fixture @('init', '--quiet', '--initial-branch=main')
    Write-Fixture (Join-Path $fixture 'Cargo.toml') "[workspace]`n"
    Write-Fixture (Join-Path $fixture 'Cargo.lock') "version = 4`n"
    Write-Fixture (Join-Path $fixture 'release.json') '{"version":"1.0.0"}'
    Write-Fixture (Join-Path $fixture 'crates\example\src\lib.rs') "pub fn example() {}`n"
    Write-Fixture (Join-Path $fixture 'docs\notes.md') "notes`n"
    Write-Fixture (Join-Path $fixture '.gitignore') "target/`n"
    Write-Fixture (Join-Path $fixture '.gitattributes') "* text=auto eol=lf`n"
    $null = Invoke-FixtureGit $fixture @('add', '.')
    $null = Invoke-FixtureGit $fixture @('commit', '--quiet', '-m', 'first')
    $head = (Invoke-FixtureGit $fixture @('rev-parse', 'HEAD'))[0]

    $clean = Identity $fixture
    Check ($clean.gitHead -ceq $head -and -not $clean.gitDirty -and -not $clean.inputsDirty -and -not $clean.linkedWorktree) 'A clean checkout reports its commit and no differences.'
    Check ($clean.inputsMatchCommit -and @($clean.lineEndingsConverted).Count -eq 0) 'A clean checkout with the committed line endings matches its commit byte for byte.'
    Check ($clean.fileCount -eq 4 -and @($clean.files | Where-Object { $_.path -eq 'docs/notes.md' }).Count -eq 0) 'The snapshot lists the build inputs and nothing outside their scope.'
    Check ($clean.sourceSnapshotSha256 -match '^[0-9a-f]{64}$' -and (Identity $fixture).sourceSnapshotSha256 -ceq $clean.sourceSnapshotSha256) 'The snapshot digest is stable between runs.'

    Write-Fixture (Join-Path $fixture 'crates\example\src\lib.rs') "pub fn example() { let _changed = 1; }`n"
    $edited = Identity $fixture
    Check ($edited.gitDirty -and $edited.inputsDirty -and $edited.gitHead -ceq $head -and $edited.sourceSnapshotSha256 -cne $clean.sourceSnapshotSha256) 'An edited tracked input is dirty and changes the digest while the commit stays the same.'
    Check (@($edited.changedInputs | Where-Object { $_ -like '*crates/example/src/lib.rs' }).Count -eq 1) 'The changed input is named.'
    Write-Fixture (Join-Path $fixture 'crates\example\src\lib.rs') "pub fn example() {}`n"
    Check ((Identity $fixture).sourceSnapshotSha256 -ceq $clean.sourceSnapshotSha256 -and -not (Identity $fixture).gitDirty) 'Restoring the file restores the clean identity.'

    Write-Fixture (Join-Path $fixture 'crates\example\src\extra.rs') "pub fn extra() {}`n"
    $untracked = Identity $fixture
    Check ($untracked.gitDirty -and $untracked.inputsDirty -and $untracked.fileCount -eq 5 -and $untracked.sourceSnapshotSha256 -cne $clean.sourceSnapshotSha256) 'An untracked source file is compiled, so it is dirty and part of the digest.'
    Remove-Item -LiteralPath (Join-Path $fixture 'crates\example\src\extra.rs')

    Write-Fixture (Join-Path $fixture 'crates\example\target\output.bin') 'generated'
    $ignored = Identity $fixture
    Check (-not $ignored.gitDirty -and $ignored.sourceSnapshotSha256 -ceq $clean.sourceSnapshotSha256) 'Ignored build output is neither dirty nor part of the digest.'

    Write-Fixture (Join-Path $fixture 'docs\notes.md') "changed notes`n"
    $outside = Identity $fixture
    Check ($outside.gitDirty -and -not $outside.inputsDirty -and $outside.sourceSnapshotSha256 -ceq $clean.sourceSnapshotSha256) 'A change outside the build inputs marks the tree dirty without changing the input digest.'
    $null = Invoke-FixtureGit $fixture @('checkout', '--quiet', '--', 'docs/notes.md')

    Remove-Item -LiteralPath (Join-Path $fixture 'release.json')
    $deleted = Identity $fixture
    Check ($deleted.inputsDirty -and @($deleted.files | Where-Object { $_.path -eq 'release.json' -and $_.missing }).Count -eq 1 -and $deleted.sourceSnapshotSha256 -cne $clean.sourceSnapshotSha256) 'A deleted tracked input is reported as missing and changes the digest.'
    $null = Invoke-FixtureGit $fixture @('checkout', '--quiet', '--', 'release.json')
    Check ((Identity $fixture).sourceSnapshotSha256 -ceq $clean.sourceSnapshotSha256) 'The fixture is clean again before the layout cases.'

    # Depending on its index state, Git can report a file as unmodified when
    # only its line endings differ, but the compiler reads the bytes on disk.
    # So the check does not rely on `git status` (gitDirty is not asserted).
    Write-Fixture (Join-Path $fixture 'crates\example\src\lib.rs') "pub fn example() {}`r`n"
    $converted = Identity $fixture
    Check (-not $converted.inputsMatchCommit -and @($converted.lineEndingsConverted) -contains 'crates/example/src/lib.rs' -and
        $converted.sourceSnapshotSha256 -cne $clean.sourceSnapshotSha256) 'Converted line endings are reported whatever git status says, and the inputs no longer count as matching the commit.'
    Write-Fixture (Join-Path $fixture 'crates\example\src\lib.rs') "pub fn example() {}`n"
    Check ((Identity $fixture).inputsMatchCommit -and (Identity $fixture).sourceSnapshotSha256 -ceq $clean.sourceSnapshotSha256) 'Restoring the committed line endings restores the match.'

    $linked = Join-Path $root 'linked'
    $null = Invoke-FixtureGit $fixture @('worktree', 'add', '--quiet', '-b', 'side', $linked)
    Check (Test-Path -LiteralPath (Join-Path $linked '.git') -PathType Leaf) 'The linked worktree keeps .git as a file.'
    $side = Identity $linked
    Check ($side.linkedWorktree -and $side.gitHead -ceq $head -and -not $side.gitDirty -and $side.sourceSnapshotSha256 -ceq $clean.sourceSnapshotSha256) 'A linked worktree resolves the same commit and inputs through Git.'
    Write-Fixture (Join-Path $linked 'release.json') '{"version":"1.0.1"}'
    Check ((Identity $linked).inputsDirty -and -not (Identity $fixture).gitDirty) 'A change in the linked worktree does not leak into the main checkout.'
    $null = Invoke-FixtureGit $linked @('commit', '--quiet', '-am', 'side change')
    $sideHead = (Invoke-FixtureGit $linked @('rev-parse', 'HEAD'))[0]
    $after = Identity $linked
    Check ($after.gitHead -ceq $sideHead -and $sideHead -cne $head -and -not $after.gitDirty -and (Identity $fixture).gitHead -ceq $head) 'Each worktree reports its own commit.'

    $null = Invoke-FixtureGit $fixture @('pack-refs', '--all')
    $loose = (Invoke-FixtureGit $fixture @('rev-parse', '--path-format=absolute', '--git-path', 'refs/heads/main'))[0]
    Check (-not (Test-Path -LiteralPath $loose)) 'The branch now exists only in packed-refs.'
    $packed = Identity $fixture
    Check ($packed.gitHead -ceq $head -and -not $packed.gitDirty -and $packed.sourceSnapshotSha256 -ceq $clean.sourceSnapshotSha256) 'Packed refs do not hide the commit.'

    $nested = Join-Path $fixture 'crates\example\target\exported-source'
    $null = New-Item -ItemType Directory -Force -Path $nested
    Reject { Identity $nested } '*inside another repository*' 'A directory inside some other repository is not described by that repository.'
    $plain = Join-Path $root 'plain'
    $null = New-Item -ItemType Directory -Path $plain
    Reject { Identity $plain } '*not a Git checkout*' 'A directory that is not a checkout is rejected.'
    $passed = $true
} catch { $failure = $_.Exception.Message }
finally {
    $report = [ordered]@{ passed = $passed; checks = $checks.ToArray(); error = $failure; scope = 'Disposable repositories in a private temporary directory. This checkout is not modified.' }
    [IO.File]::WriteAllText((Join-Path $root 'results.json'), ($report | ConvertTo-Json -Depth 4), (New-Object Text.UTF8Encoding($false)))
}
[pscustomobject]@{ passed = $passed; checks = $checks.Count; error = $failure; reportDirectory = $root } | ConvertTo-Json -Depth 3
if (-not $passed) { exit 1 }
