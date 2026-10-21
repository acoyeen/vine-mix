#requires -Version 7.3
[CmdletBinding()]
param([string]$EvidencePath = '')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$dotnet = Join-Path $project '.tools\dotnet\dotnet.exe'
$tool = Join-Path $PSScriptRoot 'update-release-tools\VineMix.UpdateReleaseTools.csproj'
$tokens = $null; $errors = $null
[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'prepare-update-release.ps1'),[ref]$tokens,[ref]$errors) | Out-Null
if ($errors.Count) { throw 'The preparation wrapper did not parse.' }
& $dotnet run --project $tool -c Release -- --self-test
if ($LASTEXITCODE) { throw 'The signing parser roundtrip fixture failed.' }
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('VineMix.ReleaseTool.Tests-' + [Guid]::NewGuid().ToString('N'))
$source = Join-Path $fixture 'source'; $private = Join-Path $fixture 'private'; $candidate = Join-Path $fixture 'candidates'
$null = New-Item -ItemType Directory -Path $source,$private,$candidate
$checks = [Collections.Generic.List[string]]::new()
$checks.Add('PowerShell preparation wrapper parses; exact UpdateCore signature roundtrip and tamper rejection pass.')
try {
    $version = '1.2.3-beta.2'; $numeric = '1.2.3.2'; $sourceHash = 'a' * 64; $head = 'b' * 40
    $fixtureProject = Join-Path $source 'Fixture.csproj'
    '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><Product>VineMix</Product><Version>1.2.3-beta.2</Version><InformationalVersion>1.2.3-beta.2</InformationalVersion><FileVersion>1.2.3.2</FileVersion><AssemblyVersion>1.2.3.2</AssemblyVersion></PropertyGroup></Project>' | Set-Content -LiteralPath $fixtureProject -Encoding utf8
    'System.Console.WriteLine("Synthetic fixture. Never run as a package.");' | Set-Content -LiteralPath (Join-Path $source 'Program.cs') -Encoding utf8
    & $dotnet publish $fixtureProject -c Release -r win-x64 --self-contained false -o (Join-Path $fixture 'compiled') --nologo | Out-Null
    if ($LASTEXITCODE) { throw 'The external inert executable fixture could not be compiled.' }
    $setup = Join-Path $candidate ('VineMix-' + $version + '-Setup-x64.exe')
    $standalone = Join-Path $candidate ('VineMix-' + $version + '-Standalone-x64.exe')
    Copy-Item -LiteralPath (Join-Path $fixture 'compiled\Fixture.exe') -Destination $setup
    Copy-Item -LiteralPath $setup -Destination $standalone
    $hash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant(); $size = (Get-Item -LiteralPath $setup).Length
    $release = Join-Path $source 'release.json'; $trust = Join-Path $source 'trust.json'; $keyPath = Join-Path $private 'fixture-key.pem'
    $notes = Join-Path $private 'notes.txt'; $setupReceipt = Join-Path $private 'setup-receipt.json'; $standaloneReceipt = Join-Path $private 'standalone-receipt.json'
    @{version=$version;numericVersion=$numeric} | ConvertTo-Json | Set-Content -LiteralPath $release -Encoding utf8
    'Synthetic release notes.' | Set-Content -LiteralPath $notes -Encoding utf8
    $key = [Security.Cryptography.RSA]::Create(3072)
    try {
        [IO.File]::WriteAllText($keyPath,$key.ExportPkcs8PrivateKeyPem())
        $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User
        $acl = [Security.AccessControl.FileSecurity]::new(); $acl.SetOwner($sid); $acl.SetAccessRuleProtection($true,$false)
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid,[Security.AccessControl.FileSystemRights]::FullControl,[Security.AccessControl.AccessControlType]::Allow))
        [IO.FileSystemAclExtensions]::SetAccessControl([IO.FileInfo]::new($keyPath),$acl)
        @{formatVersion=1;keys=@(@{keyId='fixture-key';algorithm='RSA-PSS-SHA256';publicKeyPem=$key.ExportSubjectPublicKeyInfoPem()})} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $trust -Encoding utf8
    } finally { $key.Dispose() }
    $setupData = @{product='VineMix';architecture='x64';version=$version;numericVersion=$numeric;published=$false;sourceIdentity=@{sourceSnapshotSha256=$sourceHash;gitHead=$head};size=$size;sha256=$hash}
    $standaloneData = @{buildMode='Standalone';version=$version;numericVersion=$numeric;published=$false;sourceIdentity=@{sourceSnapshotSha256=$sourceHash;gitHead=$head};files=@(@{path=[IO.Path]::GetFileName($standalone);size=$size;sha256=$hash})}
    $setupData | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $setupReceipt -Encoding utf8
    $standaloneData | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $standaloneReceipt -Encoding utf8
    function InvokeFixture([string]$outputName,[bool]$expectSuccess) {
        $output = Join-Path $fixture $outputName
        $arguments = @('run','--no-build','--project',$tool,'-c','Release','--','--project',$source,'--release',$release,'--trust',$trust,
            '--setup',$setup,'--setup-receipt',$setupReceipt,'--standalone',$standalone,'--standalone-receipt',$standaloneReceipt,
            '--private-key',$keyPath,'--notes',$notes,'--output',$output,'--source-hash',$sourceHash,'--git-head',$head,'--git-dirty','true')
        $diagnostic = & $dotnet @arguments 2>&1 | Out-String
        if (($LASTEXITCODE -eq 0) -ne $expectSuccess) { throw ('Unexpected release-tool fixture outcome: ' + $outputName) }
        return $output
    }
    $output = InvokeFixture 'valid' $true
    $names = @(Get-ChildItem -LiteralPath $output -File | ForEach-Object Name)
    if ($names.Count -ne 4 -or -not ($names -contains ('VineMix-' + $version + '-Updates.json'))) { throw 'Fixture output allowlist failed.' }
    foreach ($metadata in Get-ChildItem -LiteralPath $output -Filter '*.json') {
        $text = [IO.File]::ReadAllText($metadata.FullName)
        if ($text.Contains('PRIVATE KEY') -or $text.Contains($fixture) -or $text.Contains($private)) { throw 'Fixture output contains private material or local paths.' }
    }
    $checks.Add('External x64 inert candidates with matching receipts prepare exactly two packages, signed Updates.json and a sanitized receipt; no executable was run.')
    $checks.Add('Output metadata contains no private PEM material or local candidate/signing paths.')
    $null = InvokeFixture 'valid' $false; $checks.Add('Existing release output cannot be overwritten.')
    $setupData.sourceIdentity.sourceSnapshotSha256 = 'c' * 64
    $setupData | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $setupReceipt -Encoding utf8
    $null = InvokeFixture 'mixed-source' $false; $checks.Add('Mismatched source receipts are rejected.')
    $setupData.sourceIdentity.sourceSnapshotSha256 = $sourceHash; $setupData.sha256 = 'd' * 64
    $setupData | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $setupReceipt -Encoding utf8
    $null = InvokeFixture 'changed-package' $false; $checks.Add('Package bytes that differ from a receipt are rejected.')
    $setupData.sha256 = $hash; $setupData.version = '1.2.4'
    $setupData | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $setupReceipt -Encoding utf8
    $null = InvokeFixture 'mixed-version' $false; $checks.Add('Mixed package release versions are rejected.')
    $setupData.version = $version
    $setupData | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $setupReceipt -Encoding utf8
    $untrusted = [Security.Cryptography.RSA]::Create(3072)
    try { [IO.File]::WriteAllText($keyPath,$untrusted.ExportPkcs8PrivateKeyPem()) } finally { $untrusted.Dispose() }
    $null = InvokeFixture 'untrusted-key' $false; $checks.Add('An unpinned private signing key cannot prepare an update.')
    $result = [ordered]@{passed=$true;checks=$checks.Count;scope='External synthetic signing/preparation fixtures only; no production key, network, release publication, app, engine or personal settings used.';results=@($checks)}
    if ($EvidencePath) { $result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $EvidencePath -Encoding utf8 }
    Write-Output ('PASS: ' + $checks.Count + ' release preparation checks; synthetic external candidates only.')
} finally {
    $resolved = [IO.Path]::GetFullPath($fixture)
    if ([IO.Path]::GetDirectoryName($resolved).TrimEnd('\') -ne [IO.Path]::GetTempPath().TrimEnd('\') -or [IO.Path]::GetFileName($resolved) -notlike 'VineMix.ReleaseTool.Tests-*') { throw 'Fixture cleanup escaped its named temporary root.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
