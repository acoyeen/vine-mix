# Shared by the effect render scripts. Dot-source this file.

function Initialize-SpeechFixtures {
    <#
    .SYNOPSIS
    Makes sure a directory holds at least one 48 kHz WAV file to render.

    .DESCRIPTION
    When the directory has no WAV files, synthetic speech is generated into it
    with the Windows speech voices installed on this machine. Nothing is played
    and no audio device is opened.
    #>
    param([Parameter(Mandatory)][string]$Directory)
    New-Item -ItemType Directory -Path $Directory -Force | Out-Null
    if (Get-ChildItem -LiteralPath $Directory -Filter '*.wav') { return }
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
            $synthesizer.SetOutputToWaveFile((Join-Path $Directory "$name.wav"), $format)
            $synthesizer.Speak($phrase)
            $synthesizer.SetOutputToNull()
        }
    } finally { $synthesizer.Dispose() }
    if (-not (Get-ChildItem -LiteralPath $Directory -Filter '*.wav')) {
        throw 'No speech voice is installed; put a 48 kHz WAV file in the source directory.'
    }
}
