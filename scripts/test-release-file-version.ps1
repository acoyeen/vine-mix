#requires -Version 7.3
[CmdletBinding()]
param([string]$EvidencePath = '')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$compilerDirectory = Join-Path $project '.tools\llvm-mingw-20261006-ucrt-x86_64\bin'
$compiler = Join-Path $compilerDirectory 'clang.exe'
$resourceCompiler = Join-Path $compilerDirectory 'windres.exe'
$gate = Join-Path $PSScriptRoot 'assert-release-file-version.ps1'
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('VineMix.FileVersion.Tests-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $fixture
$checks = [Collections.Generic.List[string]]::new()
$previousPath = $env:PATH
try {
    $env:PATH = $compilerDirectory + ';' + $previousPath
    $version = '1.2.3-beta.4'; $numeric = '1.2.3.5'
    $template = Get-Content -LiteralPath (Join-Path $project 'native\src\version.rc.in') -Raw
    $resource = $template.Replace('@VINEMIX_RESOURCE_VERSION@', '1,2,3,5').Replace('@VINEMIX_NUMERIC_VERSION@', $numeric).Replace('@VINEMIX_VERSION@', $version)
    $main = Join-Path $fixture 'inert.c'
    [IO.File]::WriteAllText($main, 'int main(void) { return 0; }')
    function Build-Fixture([string]$name, [string]$text) {
        $rc = Join-Path $fixture ($name + '.rc'); $object = Join-Path $fixture ($name + '.o'); $exe = Join-Path $fixture ($name + '.exe')
        [IO.File]::WriteAllText($rc, $text)
        & $resourceCompiler $rc -O coff -o $object
        if ($LASTEXITCODE) { throw 'Synthetic Windows version resource compilation failed.' }
        & $compiler $main $object -o $exe
        if ($LASTEXITCODE) { throw 'Synthetic inert executable compilation failed.' }
        return $exe
    }
    function Reject-Fixture([string]$path, [string]$description) {
        $rejected = $false
        try { & $gate -Path $path -Version $version -NumericVersion $numeric }
        catch { $rejected = $_.Exception.Message.StartsWith('Release version metadata mismatch', [StringComparison]::Ordinal) }
        if (-not $rejected) { throw $description }
        $checks.Add($description)
    }
    $valid = Build-Fixture 'current-template' $resource
    & $gate -Path $valid -Version $version -NumericVersion $numeric
    $legacyParsed = $null
    if (-not [Version]::TryParse([Diagnostics.FileVersionInfo]::GetVersionInfo($valid).FileVersion, [ref]$legacyParsed) -or $legacyParsed -ne [Version]$numeric) {
        throw 'The native template must also pass the numeric string comparison used by released updaters.'
    }
    $checks.Add('The actual native resource template produces numeric FileVersion text and fixed fields accepted by released updaters, while ProductVersion retains semver.')
    $legacy = Build-Fixture 'legacy-semver-fileversion' ($resource.Replace('"FileVersion", "' + $numeric + '\0"', '"FileVersion", "' + $version + '\0"'))
    Reject-Fixture $legacy 'A legacy semver FileVersion string is rejected even when its fixed numeric resource is correct.'
    $wrongText = Build-Fixture 'wrong-fileversion-text' ($resource.Replace('"FileVersion", "' + $numeric + '\0"', '"FileVersion", "1.2.3.9\0"'))
    Reject-Fixture $wrongText 'Incorrect numeric FileVersion text is rejected independently of correct fixed fields.'
    $wrongFixed = Build-Fixture 'wrong-fileversion-fixed' ($resource.Replace(' FILEVERSION 1,2,3,5', ' FILEVERSION 1,2,3,9'))
    Reject-Fixture $wrongFixed 'Incorrect fixed FileVersion fields are rejected independently of correct numeric text.'
    $wrongProduct = Build-Fixture 'wrong-productversion' ($resource.Replace('"ProductVersion", "' + $version + '\0"', '"ProductVersion", "1.2.3-beta.9\0"'))
    Reject-Fixture $wrongProduct 'A mismatched ProductVersion is rejected even when both FileVersion representations agree.'
    $result = [ordered]@{passed=$true;checks=$checks.Count;scope='Compiled inert native PE fixtures only. Executables were inspected, never launched. No app, engine, installation, personal settings or release assets were changed.';results=@($checks)}
    if ($EvidencePath) { $result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $EvidencePath -Encoding utf8 }
    Write-Output ('PASS: ' + $checks.Count + ' native release file-version checks; inert PE metadata only.')
} finally {
    $env:PATH = $previousPath
    $resolved = [IO.Path]::GetFullPath($fixture)
    if ([IO.Path]::GetDirectoryName($resolved).TrimEnd('\') -ne [IO.Path]::GetTempPath().TrimEnd('\') -or [IO.Path]::GetFileName($resolved) -notlike 'VineMix.FileVersion.Tests-*') {
        throw 'Fixture cleanup escaped its named temporary root.'
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
$global:LASTEXITCODE = 0
