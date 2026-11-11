<#
.SYNOPSIS
Renders Voice Color listening files and measurements, offline.

.DESCRIPTION
Runs the ignored `voice_color_render` test of vinemix-dsp over every 48 kHz WAV
file in the source directory and writes, for each pad position, the node's
actual output and a copy matched to the dry loudness, with a measurement table.

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
    [string]$Label = 'voice-color'
)
$ErrorActionPreference = 'Stop'
$project = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $PSScriptRoot 'rust-env.ps1')
$null = Initialize-RustEnvironment -Project $project
if (-not $SourceDirectory) { $SourceDirectory = Join-Path $project 'artifacts\voice-color\source' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $project 'artifacts\voice-color\render' }
if ($Label -notmatch '^[A-Za-z0-9][A-Za-z0-9-]*$') { throw 'Label may contain letters, digits and hyphens only.' }
$SourceDirectory = [System.IO.Path]::GetFullPath($SourceDirectory)
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $SourceDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

if (-not (Get-ChildItem -LiteralPath $SourceDirectory -Filter '*.wav')) {
    Add-Type -AssemblyName System.Speech
    $phrase = 'Good evening, and welcome back. Six silver ships slipped past the pier this morning. ' +
        'Please keep the big boat by the dock. A clear voice carries every word, from the quietest detail ' +
        'to the strongest announcement. Thanks for listening.'
    $synthesizer = New-Object System.Speech.Synthesis.SpeechSynthesizer
    try {
        $format = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(
            48000, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, [System.Speech.AudioFormat.AudioChannel]::Mono)
        foreach ($voice in $synthesizer.GetInstalledVoices()) {
            if (-not $voice.Enabled) { continue }
            $name = ($voice.VoiceInfo.Name -replace '[^A-Za-z0-9]+', '-').Trim('-').ToLowerInvariant()
            $synthesizer.SelectVoice($voice.VoiceInfo.Name)
            $synthesizer.Rate = 0
            $synthesizer.SetOutputToWaveFile((Join-Path $SourceDirectory "$name.wav"), $format)
            $synthesizer.Speak($phrase)
            $synthesizer.SetOutputToNull()
        }
    } finally { $synthesizer.Dispose() }
    if (-not (Get-ChildItem -LiteralPath $SourceDirectory -Filter '*.wav')) {
        throw 'No speech voice is installed; put a 48 kHz WAV file in the source directory.'
    }
}

$env:VINEMIX_VOICE_SOURCE = $SourceDirectory
$env:VINEMIX_VOICE_OUTPUT = $OutputDirectory
$env:VINEMIX_VOICE_LABEL = $Label
Push-Location $project
try {
    & cargo test -p vinemix-dsp --release --test voice_color -- --ignored --nocapture --exact voice_color_render
    if ($LASTEXITCODE -ne 0) { throw "The render failed with exit code $LASTEXITCODE." }
} finally {
    Pop-Location
    Remove-Item Env:VINEMIX_VOICE_SOURCE, Env:VINEMIX_VOICE_OUTPUT, Env:VINEMIX_VOICE_LABEL -ErrorAction SilentlyContinue
}
Write-Host "Listening files and measurements are in $OutputDirectory"
