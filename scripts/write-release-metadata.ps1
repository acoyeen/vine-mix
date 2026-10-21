[CmdletBinding()]
param([string]$OutputDirectory = '')
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$release = Get-Content -LiteralPath (Join-Path $project 'release.json') -Raw | ConvertFrom-Json
if ($release.version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$' -or
    $release.numericVersion -notmatch '^\d+\.\d+\.\d+\.\d+$' -or
    [string]::IsNullOrWhiteSpace($release.displayVersion)) { throw 'Invalid authoritative release.json.' }
foreach ($part in $release.numericVersion.Split('.')) { if ([int]$part -gt 65535) { throw 'Numeric version component exceeds Windows version limits.' } }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $project 'build\release' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$null = New-Item -ItemType Directory -Force -Path $OutputDirectory
$version = [Security.SecurityElement]::Escape($release.version)
$numeric = [Security.SecurityElement]::Escape($release.numericVersion)
$display = [Security.SecurityElement]::Escape($release.displayVersion)
$props = @"
<!-- Generated from release.json. Do not edit. -->
<Project><PropertyGroup>
  <Version>$version</Version><AssemblyVersion>$numeric</AssemblyVersion><FileVersion>$numeric</FileVersion>
  <InformationalVersion>$version</InformationalVersion><VineMixDisplayVersion>$display</VineMixDisplayVersion>
</PropertyGroup></Project>
"@
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'ReleaseVersion.props'), $props, [Text.UTF8Encoding]::new($false))
$release | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'release.json') -Encoding utf8
$release
