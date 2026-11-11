<#
.SYNOPSIS
Renders Distortion listening files and measurements, offline.

.DESCRIPTION
Runs the ignored `distortion_render` test of vinemix-dsp over every 48 kHz WAV
file in the source directory and writes, for each setting, the node's actual
output and a copy matched to the dry loudness, with a measurement table.

When the source directory has no WAV files, synthetic speech is generated into
it with the Windows speech voices installed on this machine. Nothing is played
and no audio device is opened. To hear the effect on a real voice, put your own
48 kHz recording in the source directory; keep such recordings out of the
repository (the default directory is under the git-ignored `artifacts`).

.PARAMETER Label
A short name for this render, used in file names, so renders of different
revisions can sit side by side.
#>
param(
    [string]$SourceDirectory = '',
    [string]$OutputDirectory = '',
    [string]$Label = 'distortion'
)
$ErrorActionPreference = 'Stop'
$project = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $PSScriptRoot 'rust-env.ps1')
. (Join-Path $PSScriptRoot 'listening-fixtures.ps1')
$null = Initialize-RustEnvironment -Project $project
if (-not $SourceDirectory) { $SourceDirectory = Join-Path $project 'artifacts\distortion\source' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $project 'artifacts\distortion\render' }
if ($Label -notmatch '^[A-Za-z0-9][A-Za-z0-9-]*$') { throw 'Label may contain letters, digits and hyphens only.' }
$SourceDirectory = [System.IO.Path]::GetFullPath($SourceDirectory)
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
Initialize-SpeechFixtures -Directory $SourceDirectory
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

$env:VINEMIX_DISTORTION_SOURCE = $SourceDirectory
$env:VINEMIX_DISTORTION_OUTPUT = $OutputDirectory
$env:VINEMIX_DISTORTION_LABEL = $Label
Push-Location $project
try {
    & cargo test -p vinemix-dsp --release --test distortion -- --ignored --nocapture --exact distortion_render
    if ($LASTEXITCODE -ne 0) { throw "The render failed with exit code $LASTEXITCODE." }
} finally {
    Pop-Location
    Remove-Item Env:VINEMIX_DISTORTION_SOURCE, Env:VINEMIX_DISTORTION_OUTPUT, Env:VINEMIX_DISTORTION_LABEL -ErrorAction SilentlyContinue
}
Write-Host "Listening files and measurements are in $OutputDirectory"
