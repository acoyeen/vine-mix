[CmdletBinding()]
param([switch]$Offline)
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$directory = Join-Path $project '.tools\innosetup-7.1.0'
$archive = Join-Path $project '.tools\downloads\innosetup-7.1.0-x64.exe'
$expected = '0362A383ED217D4C4239B5933866DD96D3EB2102737DA92F80F6057A4B40DF2F'
if (-not (Test-Path -LiteralPath $archive)) {
    if ($Offline) { throw 'The pinned Inno Setup download is not cached.' }
    New-Item -ItemType Directory -Force -Path (Split-Path $archive) | Out-Null
    Invoke-WebRequest -Uri 'https://github.com/jrsoftware/issrc/releases/download/is-7_1_0/innosetup-7.1.0-x64.exe' -OutFile $archive
}
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expected) { throw 'Inno Setup archive checksum mismatch.' }
$signature = Get-AuthenticodeSignature -LiteralPath $archive
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'CN=Pyrsys B\.V\.,') {
    throw 'The pinned Inno Setup archive does not have the expected valid publisher signature.'
}
if (-not (Test-Path -LiteralPath (Join-Path $directory 'ISCC.exe'))) {
    # The upstream /PORTABLE=1 mode creates no uninstall registration, shortcuts or associations.
    $process = Start-Process -FilePath $archive -ArgumentList @('/PORTABLE=1','/CURRENTUSER','/VERYSILENT',
        '/SUPPRESSMSGBOXES','/NORESTART','/NOICONS','/TASKS=""',('/DIR="' + $directory + '"')) -WindowStyle Hidden -PassThru
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "Portable compiler preparation failed: $($process.ExitCode)" }
    $process.Dispose()
}
foreach ($name in @('ISCC.exe','ISCmplr.dll','ISPP.dll')) {
    $file = Join-Path $directory $name
    $signature = Get-AuthenticodeSignature -LiteralPath $file
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'CN=Pyrsys B\.V\.,') {
        throw "Unexpected compiler signature: $name"
    }
}
Write-Output (Join-Path $directory 'ISCC.exe')
