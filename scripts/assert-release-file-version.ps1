#requires -Version 7.3
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Path,
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$NumericVersion
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$resolved = [IO.Path]::GetFullPath($Path)
if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) { throw 'The release executable is missing.' }
$expected = $null
if ($NumericVersion -notmatch '^\d+\.\d+\.\d+\.\d+$' -or -not [Version]::TryParse($NumericVersion, [ref]$expected)) {
    throw 'Expected a four-part numeric Windows file version.'
}
$metadata = [Diagnostics.FileVersionInfo]::GetVersionInfo($resolved)
$actual = $null
$fixed = [Version]::new($metadata.FileMajorPart, $metadata.FileMinorPart, $metadata.FileBuildPart, $metadata.FilePrivatePart)
# Older released updaters parse the FileVersion STRING, not the fixed numeric
# resource. Both must agree; a semver string here causes installed rollback.
if (-not [Version]::TryParse($metadata.FileVersion, [ref]$actual) -or $actual -ne $expected -or
    $metadata.FileVersion -cne $NumericVersion -or $fixed -ne $expected -or
    ($metadata.ProductVersion -split '\+')[0] -cne $Version) {
    throw ("Release version metadata mismatch for {0}: expected FileVersion {1} and ProductVersion {2}; found text '{3}', fixed '{4}', product '{5}'." -f
        [IO.Path]::GetFileName($resolved), $NumericVersion, $Version, $metadata.FileVersion, $fixed, $metadata.ProductVersion)
}
