param(
    [string]$OutputDirectory = '',
    [switch]$UseBaseline,
    [string]$SourceWave = '',
    [string]$ComparisonBaselineDirectory = '',
    [string]$ComparisonOutputPath = '',
    [switch]$SkipComparison
)
$ErrorActionPreference = 'Stop'
$project = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$fixture = Join-Path $project 'artifacts/voice-character-speech'
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
$customOutputDirectory = [bool]$OutputDirectory
$source = if ($SourceWave) { [System.IO.Path]::GetFullPath($SourceWave) } else { Join-Path $fixture 'source.wav' }
if ($SourceWave -and !(Test-Path -LiteralPath $source -PathType Leaf)) { throw "Requested source WAV does not exist: $source" }
if (!(Test-Path -LiteralPath $source)) {
    Add-Type -AssemblyName System.Speech
    $voice = New-Object System.Speech.Synthesis.SpeechSynthesizer
    try {
        $voice.SelectVoice('Microsoft George')
        $voice.Rate = 0
        $format = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(48000, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, [System.Speech.AudioFormat.AudioChannel]::Mono)
        $voice.SetOutputToWaveFile($source, $format)
        $phrase = 'Good evening. This is your daily update. A clear voice carries every word, from the quietest detail to the strongest announcement. Thanks for listening.'
        $voice.Speak($phrase)
        @{ Voice = 'Microsoft George'; Phrase = $phrase; Purpose = 'Offline synthetic speech fixture; written to a file only, never played'; SampleRate = 48000 } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $fixture 'source.json')
    } finally { $voice.Dispose() }
}
if (!$OutputDirectory) { $OutputDirectory = Join-Path $fixture $(if ($UseBaseline) { 'baseline' } else { 'candidate' }) }
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$compiler = Join-Path $project '.tools/llvm-mingw-20261006-ucrt-x86_64/bin/clang++.exe'
$sourceDirectory = if ($UseBaseline) { Join-Path $fixture 'baseline-source' } else { Join-Path $project 'native/src' }
$includeDirectory = if ($UseBaseline) { Join-Path $fixture 'baseline-source' } else { Join-Path $project 'native/include' }
$speex = if ($UseBaseline) { Join-Path $fixture 'baseline-source/libAudioMixer.Speex.a' } else { Join-Path $project 'build/native/libAudioMixer.Speex.a' }
$executable = Join-Path $OutputDirectory 'VoiceColorSpeech.exe'
& $compiler -std=c++20 -O3 -DNOMINMAX -static-libgcc -static-libstdc++ "-I$includeDirectory" "-I$(Join-Path $project 'build/native/_deps/json-src/include')" "-I$(Join-Path $project 'build/native/_deps/speexdsp-src/include')" (Join-Path $PSScriptRoot 'voice_color_speech.cpp') (Join-Path $sourceDirectory 'graph.cpp') (Join-Path $sourceDirectory 'advanced_effects.cpp') $speex -o $executable
if ($LASTEXITCODE -ne 0) { throw "Offline speech harness compilation failed: $LASTEXITCODE" }
& $executable $source $OutputDirectory $(if ($UseBaseline) { 'baseline-before-character-revision' } else { 'candidate-current-sources' })
if ($LASTEXITCODE -ne 0) { throw "Offline speech processing failed: $LASTEXITCODE" }
$provenance = [ordered]@{
    RunUtc = [DateTime]::UtcNow.ToString('o')
    Fixture = $source
    FixtureSha256 = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
    ExecutableSha256 = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash
    GraphSha256 = (Get-FileHash -LiteralPath (Join-Path $sourceDirectory 'graph.cpp') -Algorithm SHA256).Hash
    EffectsSha256 = (Get-FileHash -LiteralPath (Join-Path $sourceDirectory 'advanced_effects.cpp') -Algorithm SHA256).Hash
    Baseline = [bool]$UseBaseline
    DeviceAccess = $false
    Playback = $false
    Levels = 'Whole-clip RMS. Cases with inputOverFullScale=true are overload stress cases; do not treat them as ordinary microphone operating levels.'
}
$provenance | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'provenance.json')
if (!$UseBaseline -and !$SkipComparison) {
    $beforeDirectory = if ($ComparisonBaselineDirectory) { [System.IO.Path]::GetFullPath($ComparisonBaselineDirectory) } else { Join-Path $fixture 'baseline' }
    $before = Join-Path $beforeDirectory 'plain_24db_broadcast_max_matched.wav'
    if ($ComparisonBaselineDirectory -and !(Test-Path -LiteralPath $before -PathType Leaf)) { throw "Requested comparison baseline is missing: $before" }
    if (Test-Path -LiteralPath $before) {
        $beforeProvenancePath = Join-Path $beforeDirectory 'provenance.json'
        if (Test-Path -LiteralPath $beforeProvenancePath) {
            $beforeProvenance = Get-Content -LiteralPath $beforeProvenancePath -Raw | ConvertFrom-Json
            if ($beforeProvenance.FixtureSha256 -ne $provenance.FixtureSha256) { throw 'Comparison baseline uses different source audio; choose the matching baseline directory or use -SkipComparison.' }
        }
        # Preserve the historical no-argument output, while an explicitly
        # named candidate directory keeps its comparison in that directory.
        # Later revisions can choose both baseline and output explicitly.
        $comparison = if ($ComparisonOutputPath) { [System.IO.Path]::GetFullPath($ComparisonOutputPath) } elseif ($customOutputDirectory) { Join-Path $OutputDirectory 'comparison.wav' } else { Join-Path $fixture 'dry-previous-revised.wav' }
        New-Item -ItemType Directory -Path ([System.IO.Path]::GetDirectoryName($comparison)) -Force | Out-Null
        & $executable --compare (Join-Path $OutputDirectory 'plain_24db_dry_matched.wav') $before (Join-Path $OutputDirectory 'plain_24db_broadcast_max_matched.wav') $comparison
        if ($LASTEXITCODE -ne 0) { throw "Offline comparison creation failed: $LASTEXITCODE" }
        $comparisonInfoPath = [System.IO.Path]::ChangeExtension($comparison, '.json')
        $comparisonInfo = Get-Content -LiteralPath $comparisonInfoPath -Raw | ConvertFrom-Json
        $comparisonInfo | Add-Member -NotePropertyName baselineDirectory -NotePropertyValue $beforeDirectory
        $comparisonInfo | Add-Member -NotePropertyName candidateDirectory -NotePropertyValue $OutputDirectory
        $comparisonInfo | Add-Member -NotePropertyName fixtureSha256 -NotePropertyValue $provenance.FixtureSha256
        $comparisonInfo | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $comparisonInfoPath
    }
}
