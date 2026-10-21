[CmdletBinding()]
param([string]$OutputPath = '')
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location -LiteralPath $project
try {
    $relativeFiles = @(& rg --files --hidden --glob '!bin/**' --glob '!obj/**' --glob '!**/bin/**' --glob '!**/obj/**' --glob '!**/*_wpftmp.csproj' native desktop installer scripts licenses updates)
    if ($LASTEXITCODE -gt 1) { throw 'Could not enumerate release source inputs.' }
} finally { Pop-Location }
$relativeFiles += @('CMakeLists.txt','Directory.Build.props','release.json','LICENSE','THIRD_PARTY_NOTICES.md')
$inventory = @($relativeFiles | Sort-Object -Unique | ForEach-Object {
    $path = Join-Path $project $_
    [ordered]@{path=$_.Replace('\','/');size=(Get-Item -LiteralPath $path).Length;sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()}
})
$canonical = ConvertTo-Json -InputObject $inventory -Depth 4 -Compress
$hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($canonical))).ToLowerInvariant()
$head = (& git -C $project rev-parse HEAD | Out-String).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Could not read source revision.' }
$dirty = @(& git -C $project status --porcelain=v1 --untracked-files=normal)
if ($LASTEXITCODE -ne 0) { throw 'Could not read source status.' }
$identity = [ordered]@{gitHead=$head;gitDirty=($dirty.Count -gt 0);sourceSnapshotSha256=$hash;scope='Native, desktop, installer, scripts, licences, update trust, CMakeLists.txt, Directory.Build.props, release.json and root distribution notices; excludes generated bin/obj, artifacts, tools, docs and website.';files=$inventory}
if ($OutputPath) { $identity | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $OutputPath -Encoding utf8 }
$identity
