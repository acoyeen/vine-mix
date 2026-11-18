#requires -Version 7.3
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$InstallerPath,
    [Parameter(Mandatory)][string]$StandalonePath,
    [Parameter(Mandatory)][string]$PrivateKeyPath,
    [Parameter(Mandatory)][string]$NotesPath,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$RustReceiptPath = '',
    [string]$DotnetPath = '',
    [string]$InstallerReceiptPath = '',
    [string]$StandaloneReceiptPath = ''
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$InstallerPath = [IO.Path]::GetFullPath($InstallerPath)
$StandalonePath = [IO.Path]::GetFullPath($StandalonePath)
$OutputDirectory = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($OutputDirectory))
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Choose a new signed output directory.' }
# Keep the validated output private until the source identity has been
# checked again. Directory.Move publishes it as a unit and never overwrites.
$stagedOutput = Join-Path ([IO.Path]::GetDirectoryName($OutputDirectory)) ('.vinemix-signing-' + [guid]::NewGuid().ToString('N'))
if ($RustReceiptPath -and ($InstallerReceiptPath -or $StandaloneReceiptPath)) { throw 'Choose a Rust receipt or the two legacy build receipts.' }
if (-not $DotnetPath) { $DotnetPath = Join-Path $project '.tools\dotnet\dotnet.exe' }
$identityScript = if ($RustReceiptPath) { 'get-rust-source-identity.ps1' } else { 'get-source-identity.ps1' }
$before = & (Join-Path $PSScriptRoot $identityScript)
$arguments = @('run','--project',(Join-Path $PSScriptRoot 'update-release-tools\VineMix.UpdateReleaseTools.csproj'),'-c','Release','--',
    '--project',$project,'--release',(Join-Path $project 'release.json'),'--trust',(Join-Path $project 'updates\release-trust.json'),
    '--setup',$InstallerPath,'--standalone',$StandalonePath,
    '--private-key',([IO.Path]::GetFullPath($PrivateKeyPath)),'--notes',([IO.Path]::GetFullPath($NotesPath)),
    '--output',$stagedOutput,'--source-hash',$before.sourceSnapshotSha256,'--git-head',$before.gitHead,
    '--git-dirty',([string]$before.gitDirty).ToLowerInvariant())
if ($RustReceiptPath) {
    if (-not $before.inputsMatchCommit) { throw 'Rust signing requires a clean source checkout matching the receipt.' }
    $arguments += @('--rust-receipt', [IO.Path]::GetFullPath($RustReceiptPath))
} else {
    if (-not $InstallerReceiptPath) { $InstallerReceiptPath = $InstallerPath + '.json' }
    if (-not $StandaloneReceiptPath) { $StandaloneReceiptPath = [IO.Path]::GetDirectoryName($StandalonePath) + '.build.json' }
    $arguments += @('--setup-receipt', [IO.Path]::GetFullPath($InstallerReceiptPath),
                    '--standalone-receipt', [IO.Path]::GetFullPath($StandaloneReceiptPath))
}
& $DotnetPath @arguments
if ($LASTEXITCODE -ne 0) { throw 'The release was not prepared. Existing inputs and any incomplete output have been retained for inspection.' }
$after = & (Join-Path $PSScriptRoot $identityScript)
if ($before.sourceSnapshotSha256 -ne $after.sourceSnapshotSha256 -or $before.gitHead -ne $after.gitHead -or ($RustReceiptPath -and -not $after.inputsMatchCommit)) {
    throw 'Release source changed during preparation. The signed output was withheld. Build again from stable source.'
}
[IO.Directory]::Move($stagedOutput, $OutputDirectory)
Write-Host 'Preparation complete. Output contains two verified packages, the signed Updates.json and a sanitized local receipt. No upload or publication was attempted.'
