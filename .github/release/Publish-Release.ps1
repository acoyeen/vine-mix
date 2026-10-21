#requires -Version 7.3
[CmdletBinding(DefaultParameterSetName = 'Validate')]
param(
    [Parameter(Mandatory)][string]$AssetDirectory,
    [Parameter(Mandatory)][string]$NotesPath,
    [Parameter(ParameterSetName = 'Validate')][switch]$ValidateOnly,
    [Parameter(Mandatory, ParameterSetName = 'Publish')][switch]$Publish,
    [Parameter(Mandatory, ParameterSetName = 'Publish')][string]$SourceCommit,
    [string]$Version = '',
    [string]$SourceArchivePath = '',
    [switch]$LegacyWithoutUpdater
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = 'acoyeen/vine-mix'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$release = Get-Content -LiteralPath (Join-Path $project 'release.json') -Raw | ConvertFrom-Json
if (-not $Version) { $Version = $release.version }
if ($Version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-(?:alpha|beta|rc)\.(0|[1-9][0-9]*))?$') { throw 'Use a canonical release version.' }
if ($LegacyWithoutUpdater -and $Version -ne '0.3.0-beta.1') { throw 'The unsigned-update legacy exception is only for 0.3.0-beta.1.' }
if (-not $LegacyWithoutUpdater -and $Version -ne $release.version) { throw 'The version must match release.json.' }
$numericVersion = if ($LegacyWithoutUpdater) { '0.3.0.1' } else { $release.numericVersion }
$tag = 'v' + $Version
$assetsRoot = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($AssetDirectory))
$notesFile = [IO.Path]::GetFullPath($NotesPath)
$notes = [IO.File]::ReadAllText($notesFile, [Text.UTF8Encoding]::new($false, $true))
if ([string]::IsNullOrWhiteSpace($notes) -or $notes.Length -gt 32768 -or $notes -match '(?i)(?<![a-z0-9])[a-z]:[\\/]|-----BEGIN (?:RSA )?PRIVATE KEY-----') { throw 'Release notes must be concise UTF-8 text without local paths or signing material.' }

function Get-Digest([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Get-Asset([string]$Name) {
    $path = Join-Path $assetsRoot $Name
    $file = Get-Item -LiteralPath $path
    if ($file.PSIsContainer -or ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $file.Length -le 0 -or $file.Length -gt 1GB) { throw 'An asset is missing, empty, linked or too large.' }
    [ordered]@{name=$Name;path=$file.FullName;size=$file.Length;sha256=(Get-Digest $file.FullName)}
}
function Assert-Properties($Object, [string[]]$Names) {
    if ($Object -isnot [Collections.IDictionary] -or $Object.Count -ne $Names.Count -or @($Object.Keys | Where-Object { $_ -cnotin $Names }).Count) { throw 'Signed update information has an unexpected shape.' }
}
function Assert-UniqueJson([System.Text.Json.JsonElement]$Element) {
    if ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Object) {
        $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($property in $Element.EnumerateObject()) {
            if (-not $names.Add($property.Name)) { throw 'Duplicate update JSON property.' }
            Assert-UniqueJson $property.Value
        }
    } elseif ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Array) {
        foreach ($item in $Element.EnumerateArray()) { Assert-UniqueJson $item }
    }
}
function Read-JsonBytes([byte[]]$Bytes, [int]$Limit) {
    if ($Bytes.Length -eq 0 -or $Bytes.Length -gt $Limit) { throw 'Signed update information exceeds its size limit.' }
    $text = [Text.UTF8Encoding]::new($false, $true).GetString($Bytes)
    $options = [System.Text.Json.JsonDocumentOptions]::new(); $options.MaxDepth = 12
    $document = [System.Text.Json.JsonDocument]::Parse($text, $options)
    try { Assert-UniqueJson $document.RootElement } finally { $document.Dispose() }
    $text | ConvertFrom-Json -AsHashtable -Depth 12
}
function Assert-Executable($Asset) {
    $metadata = [Diagnostics.FileVersionInfo]::GetVersionInfo($Asset.path)
    if ($metadata.ProductName.Trim([char]0).Trim() -ne 'VineMix' -or $metadata.ProductVersion.Trim([char]0).Trim().Split('+')[0] -ne $Version -or $metadata.FileVersion.Trim([char]0).Trim() -ne $numericVersion) { throw 'Executable version metadata does not match the release.' }
    $stream = [IO.File]::OpenRead($Asset.path)
    $reader = [IO.BinaryReader]::new($stream)
    try {
        if ($reader.ReadUInt16() -ne 0x5a4d) { throw 'An asset is not a Windows executable.' }
        $stream.Position = 0x3c; $offset = $reader.ReadInt32()
        if ($offset -lt 0x40 -or $offset -gt $stream.Length - 6) { throw 'Invalid PE header.' }
        $stream.Position = $offset
        if ($reader.ReadUInt32() -ne 0x4550 -or $reader.ReadUInt16() -ne 0x8664) { throw 'Release executables must be Windows x64.' }
    } finally { $reader.Dispose(); $stream.Dispose() }
}

$assets = [Collections.Generic.List[object]]::new()
foreach ($kind in @('Setup', 'Standalone')) {
    $asset = Get-Asset ('VineMix-' + $Version + '-' + $kind + '-x64.exe')
    Assert-Executable $asset
    $assets.Add($asset)
}
$signatureVerified = $false
if (-not $LegacyWithoutUpdater) {
    $envelopeAsset = Get-Asset ('VineMix-' + $Version + '-Updates.json')
    $envelope = Read-JsonBytes ([IO.File]::ReadAllBytes($envelopeAsset.path)) (256KB)
    Assert-Properties $envelope @('formatVersion','keyId','algorithm','payload','signature')
    if ($envelope.formatVersion -ne 1 -or $envelope.algorithm -cne 'RSA-PSS-SHA256' -or $envelope.keyId -cnotmatch '^[A-Za-z0-9_-]{1,80}$' -or $envelope.signature.Length -gt 1400 -or $envelope.payload.Length -gt 174764) { throw 'Invalid update envelope.' }
    $trust = Get-Content -LiteralPath (Join-Path $project 'updates/release-trust.json') -Raw | ConvertFrom-Json
    $keys = @($trust.keys | Where-Object { $_.keyId -ceq $envelope.keyId -and $_.algorithm -ceq $envelope.algorithm })
    if ($trust.formatVersion -ne 1 -or $keys.Count -ne 1) { throw 'The update signature key is not uniquely pinned.' }
    $payload = [Convert]::FromBase64String($envelope.payload)
    $signature = [Convert]::FromBase64String($envelope.signature)
    $rsa = [Security.Cryptography.RSA]::Create()
    try {
        $rsa.ImportFromPem($keys[0].publicKeyPem)
        if ($rsa.KeySize -lt 3072 -or $rsa.KeySize -gt 8192 -or -not $rsa.VerifyData($payload, $signature, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pss)) { throw 'The update signature is invalid.' }
    } finally { $rsa.Dispose() }
    $manifest = Read-JsonBytes $payload (128KB)
    Assert-Properties $manifest @('formatVersion','product','version','numericVersion','architecture','minimumWindowsBuild','settingsSchema','protocolVersion','notes','installer','standalone')
    if ($manifest.formatVersion -ne 1 -or $manifest.product -cne 'VineMix' -or $manifest.version -cne $Version -or $manifest.numericVersion -cne $numericVersion -or $manifest.architecture -cne 'x64' -or $manifest.minimumWindowsBuild -lt 22000 -or $manifest.settingsSchema -ne 2 -or $manifest.protocolVersion -ne 1 -or $manifest.notes -isnot [string] -or $manifest.notes.Length -gt 32768) { throw 'Update identity or compatibility does not match this release.' }
    for ($index = 0; $index -lt 2; $index++) {
        $asset = $assets[$index]
        $signed = if ($index -eq 0) { $manifest.installer } else { $manifest.standalone }
        Assert-Properties $signed @('fileName','url','size','sha256')
        $url = 'https://github.com/' + $repository + '/releases/download/' + $tag + '/' + $asset.name
        if ($signed.fileName -cne $asset.name -or $signed.url -cne $url -or $signed.size -ne $asset.size -or $signed.sha256 -notmatch '^[a-fA-F0-9]{64}$' -or $signed.sha256.ToLowerInvariant() -ne $asset.sha256) { throw 'The signed update does not match the exact downloadable packages.' }
    }
    $assets.Add($envelopeAsset); $signatureVerified = $true
}
if ($SourceArchivePath) {
    $sourcePath = [IO.Path]::GetFullPath($SourceArchivePath)
    if ([IO.Path]::GetFileName($sourcePath) -cne ('VineMix-' + $Version + '-Source.zip') -or [IO.Path]::GetDirectoryName($sourcePath) -ne $assetsRoot) { throw 'Use the canonical source ZIP inside AssetDirectory.' }
    $sourceAsset = Get-Asset ([IO.Path]::GetFileName($sourcePath))
    $archive = [IO.Compression.ZipFile]::OpenRead($sourcePath)
    try {
        if ($archive.Entries.Count -eq 0 -or $archive.Entries.Count -gt 10000) { throw 'The source archive has an invalid entry count.' }
        foreach ($entry in $archive.Entries) {
            $name = $entry.FullName
            if ($name -match '(^/|\\|(^|/)\.\.(/|$)|:|[\x00-\x1f]|(^|/)(\.git|\.tools|artifacts|bin|obj)(/|$)|\.(pem|pfx|p12|key|exe|dll|pdb|bak|dmp)$|(^|/)(settings\.json|ui\.json|preparation-receipt\.json)$)') { throw 'The source ZIP contains an unsafe path, generated file or private artifact.' }
        }
    } finally { $archive.Dispose() }
    $assets.Add($sourceAsset)
}
$checksumPath = Join-Path $assetsRoot 'SHA256SUMS.txt'
$lines = @($assets | ForEach-Object { $_.sha256 + '  ' + $_.name })
[IO.File]::WriteAllText($checksumPath, ($lines -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
$assets.Add((Get-Asset 'SHA256SUMS.txt'))
$summary = [ordered]@{version=$Version;tag=$tag;signatureVerified=$signatureVerified;assets=@($assets | ForEach-Object { [ordered]@{name=$_.name;size=$_.size;sha256=$_.sha256} });published=$false}
if (-not $Publish) { $summary | ConvertTo-Json -Depth 5; return }

if ($SourceCommit -cnotmatch '^[0-9a-f]{40}$') { throw 'SourceCommit must be the full source commit SHA.' }
function Invoke-Gh([string[]]$Arguments) {
    $output = & gh @Arguments
    if ($LASTEXITCODE -ne 0) { throw 'A GitHub release operation failed; any created draft is retained.' }
    $output
}
$remoteContent = Invoke-Gh @('api',('repos/' + $repository + '/contents/release.json?ref=' + $SourceCommit),'--jq','.content')
$remoteRelease = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String(($remoteContent -join ''))) | ConvertFrom-Json
if ($remoteRelease.version -cne $Version -or $remoteRelease.numericVersion -cne $numericVersion) { throw 'The remote source commit has different release metadata.' }
$refs = (Invoke-Gh @('api',('repos/' + $repository + '/git/matching-refs/tags/' + $tag))) | ConvertFrom-Json
if (@($refs | Where-Object ref -ceq ('refs/tags/' + $tag)).Count) { throw 'The release tag already exists; never overwrite a version.' }
$existing = (Invoke-Gh @('release','list','--repo',$repository,'--limit','1000','--json','tagName')) | ConvertFrom-Json
if (@($existing | Where-Object tagName -ceq $tag).Count) { throw 'The release already exists; never overwrite its assets.' }
$pre = if ($Version.Contains('-')) { 'true' } else { 'false' }
$latest = if ($Version.Contains('-')) { 'false' } else { 'true' }
$create = @('release','create',$tag,'--repo',$repository,'--draft',('--prerelease=' + $pre),'--target',$SourceCommit,'--title',('VineMix ' + $Version),'--notes-file',$notesFile,('--latest=' + $latest))
$null = Invoke-Gh $create
$null = Invoke-Gh (@('release','upload',$tag,'--repo',$repository) + @($assets | ForEach-Object path))
$downloadRoot = Join-Path ([IO.Path]::GetTempPath()) ('VineMix-release-verify-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($downloadRoot) | Out-Null
try {
    foreach ($asset in $assets) {
        if ((Get-Digest $asset.path) -ne $asset.sha256) { throw 'A local release asset changed during upload.' }
        $null = Invoke-Gh @('release','download',$tag,'--repo',$repository,'--pattern',$asset.name,'--dir',$downloadRoot)
        $download = Join-Path $downloadRoot $asset.name
        if ((Get-Item -LiteralPath $download).Length -ne $asset.size -or (Get-Digest $download) -ne $asset.sha256) { throw 'A downloaded GitHub asset differs from its validated original.' }
    }
    $null = Invoke-Gh @('release','edit',$tag,'--repo',$repository,'--draft=false',('--prerelease=' + $pre),('--latest=' + $latest))
    $published = (Invoke-Gh @('release','view',$tag,'--repo',$repository,'--json','isDraft,isPrerelease,url')) | ConvertFrom-Json
    if ($published.isDraft -or $published.isPrerelease -ne $Version.Contains('-')) { throw 'Release publication state did not match the version.' }
    $summary.published = $true; $summary.url = $published.url
    $summary | ConvertTo-Json -Depth 5
} finally {
    foreach ($asset in $assets) {
        $download = [IO.Path]::GetFullPath((Join-Path $downloadRoot $asset.name))
        if ([IO.Path]::GetDirectoryName($download) -ne $downloadRoot) { throw 'Unsafe verification cleanup path.' }
        if (Test-Path -LiteralPath $download -PathType Leaf) { Remove-Item -LiteralPath $download }
    }
    if ((Test-Path -LiteralPath $downloadRoot) -and -not @(Get-ChildItem -LiteralPath $downloadRoot -Force).Count) { Remove-Item -LiteralPath $downloadRoot }
}
