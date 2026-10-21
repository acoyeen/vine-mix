#requires -Version 7.3
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$InstallerPath,
    [Parameter(Mandatory)][string]$StandalonePath,
    [Parameter(Mandatory)][string]$PrivateKeyPath,
    [Parameter(Mandatory)][string]$NotesPath,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$InstallerReceiptPath = '',
    [string]$StandaloneReceiptPath = ''
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$InstallerPath = [IO.Path]::GetFullPath($InstallerPath)
$StandalonePath = [IO.Path]::GetFullPath($StandalonePath)
$OutputDirectory = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($OutputDirectory))
if (-not $InstallerReceiptPath) { $InstallerReceiptPath = $InstallerPath + '.json' }
if (-not $StandaloneReceiptPath) { $StandaloneReceiptPath = [IO.Path]::GetDirectoryName($StandalonePath) + '.build.json' }
$releaseForOutput = Get-Content -LiteralPath (Join-Path $project 'release.json') -Raw | ConvertFrom-Json
$before = & (Join-Path $PSScriptRoot 'get-source-identity.ps1')
$arguments = @('run','--project',(Join-Path $PSScriptRoot 'update-release-tools\VineMix.UpdateReleaseTools.csproj'),'-c','Release','--',
    '--project',$project,'--release',(Join-Path $project 'release.json'),'--trust',(Join-Path $project 'updates\release-trust.json'),
    '--setup',$InstallerPath,'--setup-receipt',([IO.Path]::GetFullPath($InstallerReceiptPath)),
    '--standalone',$StandalonePath,'--standalone-receipt',([IO.Path]::GetFullPath($StandaloneReceiptPath)),
    '--private-key',([IO.Path]::GetFullPath($PrivateKeyPath)),'--notes',([IO.Path]::GetFullPath($NotesPath)),
    '--output',$OutputDirectory,'--source-hash',$before.sourceSnapshotSha256,'--git-head',$before.gitHead,
    '--git-dirty',([string]$before.gitDirty).ToLowerInvariant())
& (Join-Path $project '.tools\dotnet\dotnet.exe') @arguments
if ($LASTEXITCODE -ne 0) { throw 'The release was not prepared. Existing inputs and any incomplete output have been retained for inspection.' }
$after = & (Join-Path $PSScriptRoot 'get-source-identity.ps1')
if ($before.sourceSnapshotSha256 -ne $after.sourceSnapshotSha256) {
    # Only invalidate the two exact metadata outputs this command creates. Never
    # recursively remove release candidates or touch input packages/key material.
    foreach ($name in @('preparation-receipt.json',('VineMix-' + $releaseForOutput.version + '-Updates.json'))) {
        $path = [IO.Path]::GetFullPath((Join-Path $OutputDirectory $name))
        if ([IO.Path]::GetDirectoryName($path) -ne $OutputDirectory) { throw 'The output invalidation path is unsafe.' }
        if (Test-Path -LiteralPath $path -PathType Leaf) { Move-Item -LiteralPath $path -Destination ($path + '.source-changed-unpublished') }
    }
    throw 'Release source changed during preparation. Output metadata was marked source-changed-unpublished; rebuild from stable source.'
}
Write-Host 'Preparation complete. Output contains two verified packages, the signed Updates.json and a sanitized local receipt. No upload or publication was attempted.'
