[CmdletBinding()]
param([string]$ToolsDirectory = (Join-Path $PSScriptRoot '..\.tools'))
$ErrorActionPreference = 'Stop'
$toolsRoot = [IO.Path]::GetFullPath($ToolsDirectory)
New-Item -ItemType Directory -Force -Path $toolsRoot | Out-Null
$downloadRoot = Join-Path $toolsRoot 'downloads'
New-Item -ItemType Directory -Force -Path $downloadRoot | Out-Null

function Get-ToolArchive([string]$Url, [string]$Name, [string]$Sha512 = '', [string]$Sha256 = '') {
    $archive = Join-Path $downloadRoot $Name
    if (-not (Test-Path -LiteralPath $archive)) {
        Write-Host "Downloading $Name"
        $oldProgress = $ProgressPreference
        $ProgressPreference = 'SilentlyContinue'
        try { Invoke-WebRequest -UseBasicParsing -Uri $Url -OutFile $archive } finally { $ProgressPreference = $oldProgress }
    }
    if ($Sha512 -and (Get-FileHash -LiteralPath $archive -Algorithm SHA512).Hash -ne $Sha512) {
        throw "Checksum mismatch for $Name; delete the archive and obtain it again from the official source."
    }
    if ($Sha256 -and (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $Sha256) {
        throw "Checksum mismatch for $Name; delete the archive and obtain it again from the official source."
    }
    return $archive
}

$sdkRoot = Join-Path $toolsRoot 'dotnet'
if (-not (Test-Path -LiteralPath (Join-Path $sdkRoot 'sdk\10.0.402'))) {
    $sdkArchive = Get-ToolArchive 'https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.402/dotnet-sdk-10.0.402-win-x64.zip' 'dotnet-sdk-10.0.402-win-x64.zip' 'ae4fd528cb7e9f6af6aaf7de36715a69f761f183744f1bb3025d19f5a92447090ec561410e46266c1d10a63781ed6f3de771760bc76c876d87daabc367b898cd'
    Expand-Archive -LiteralPath $sdkArchive -DestinationPath $sdkRoot -Force
}
$llvmRoot = Join-Path $toolsRoot 'llvm-mingw-20261006-ucrt-x86_64'
if (-not (Test-Path -LiteralPath (Join-Path $llvmRoot 'bin\clang++.exe'))) {
    $llvmArchive = Get-ToolArchive -Url 'https://github.com/mstorsjo/llvm-mingw/releases/download/20261006/llvm-mingw-20261006-ucrt-x86_64.zip' -Name 'llvm-mingw-20261006-ucrt-x86_64.zip' -Sha256 '317492c456aa27ee607a5919f1d2d38dcdc1112516a24d0bf4b00d078f52d17a'
    Expand-Archive -LiteralPath $llvmArchive -DestinationPath $toolsRoot -Force
}
$cmakeRoot = Join-Path $toolsRoot 'cmake-4.4.4-windows-x86_64'
if (-not (Test-Path -LiteralPath (Join-Path $cmakeRoot 'bin\cmake.exe'))) {
    $cmakeArchive = Get-ToolArchive -Url 'https://github.com/Kitware/CMake/releases/download/v4.4.4/cmake-4.4.4-windows-x86_64.zip' -Name 'cmake-4.4.4-windows-x86_64.zip' -Sha256 'bace36e94b31c68ab6fa295f26dfa11219e0701cf7c94b0284a7d1cb13dac536'
    Expand-Archive -LiteralPath $cmakeArchive -DestinationPath $toolsRoot -Force
}
Write-Host 'Local build tools are ready. No audio drivers or Windows audio settings were changed.'
