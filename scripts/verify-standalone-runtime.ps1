[CmdletBinding()]
param([Parameter(Mandatory)][string]$PackageDirectory, [string]$ResultDirectory = '')
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$package = [IO.Path]::GetFullPath($PackageDirectory)
$receipt = Get-Content -LiteralPath ($package + '.build.json') -Raw | ConvertFrom-Json -Depth 12
if ($receipt.buildMode -ne 'Standalone') { throw 'Select a standalone package with its build receipt.' }
if (-not $ResultDirectory) { $ResultDirectory = Join-Path $env:LOCALAPPDATA ('VineMix.BuildCache\runtime-verification-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff')) }
$result = [IO.Path]::GetFullPath($ResultDirectory)
if (Test-Path -LiteralPath $result) { throw 'Choose a fresh evidence directory.' }
$null = New-Item -ItemType Directory -Path $result
$current = & (Join-Path $PSScriptRoot 'get-source-identity.ps1')
if ($current.sourceSnapshotSha256 -ne $receipt.sourceIdentity.sourceSnapshotSha256) { throw 'Runtime verification source differs from the packaged source snapshot.' }
foreach ($inputPath in @($receipt.runtimeArchive,$receipt.runtimeManifest)) { if (-not (Test-Path -LiteralPath $inputPath -PathType Leaf)) { throw 'The original built runtime ZIP and manifest must be retained for qualification.' } }
function Xml-Escape([string]$Value) { [Security.SecurityElement]::Escape($Value) }
$runtimeSource = Xml-Escape (Join-Path $project 'desktop\VineMix.Desktop\EmbeddedRuntime.cs')
$fixtureSource = Xml-Escape (Join-Path $project 'installer\tests\EmbeddedRuntimeFixture.cs')
$archive = Xml-Escape $receipt.runtimeArchive
$manifest = Xml-Escape $receipt.runtimeManifest
$harness = @"
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
<OutputType>Exe</OutputType><TargetFramework>net10.0-windows</TargetFramework><RuntimeIdentifier>win-x64</RuntimeIdentifier>
<AssemblyName>VineMix.RuntimeVerification</AssemblyName><Nullable>enable</Nullable><ImplicitUsings>disable</ImplicitUsings>
<DefineConstants>VINEMIX_STANDALONE</DefineConstants><EnableDefaultCompileItems>false</EnableDefaultCompileItems>
</PropertyGroup><ItemGroup>
<Compile Include="$runtimeSource"/><Compile Include="$fixtureSource"/>
<EmbeddedResource Include="$archive" LogicalName="VineMix.Runtime.zip"/>
<EmbeddedResource Include="$manifest" LogicalName="VineMix.Runtime.manifest.json"/>
</ItemGroup></Project>
"@
$harnessPath = Join-Path $result 'RuntimeVerification.csproj'
[IO.File]::WriteAllText($harnessPath,$harness,[Text.UTF8Encoding]::new($false))
$dotnet = Join-Path $project '.tools\dotnet\dotnet.exe'
$output = Join-Path $result 'harness'
& $dotnet build $harnessPath -c Release -o $output '-p:UseSharedCompilation=false' --nologo 2>&1 | Tee-Object -LiteralPath (Join-Path $result 'build.log')
if ($LASTEXITCODE -ne 0) { throw 'Standalone runtime qualification harness did not build.' }
& $dotnet (Join-Path $output 'VineMix.RuntimeVerification.dll') $result 2>&1 | Tee-Object -LiteralPath (Join-Path $result 'checks.log')
if ($LASTEXITCODE -ne 0) { throw 'Standalone runtime integrity qualification failed.' }
