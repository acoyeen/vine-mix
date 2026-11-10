# Selects and verifies the exact Rust target and pinned tools for this
# checkout. Dot-source this file, then call Initialize-RustEnvironment.
#
# Windows PowerShell 5.1 compatible. It changes only this process's PATH and
# VINEMIX_WINDRES; nothing is installed, downloaded or launched.
#
# The pins live in two places and are cross-checked here:
#   rust-toolchain.toml   the exact Rust release and its installed target
#   tools\rust-pins.json  the build target and the LLVM-MinGW directory name
# Other llvm-mingw-* directories under .tools are ignored: the build never
# takes "whichever one is listed first".

function Get-RustPins {
    param([Parameter(Mandatory)][string]$Project)
    $pins = Get-Content -LiteralPath (Join-Path $Project 'tools\rust-pins.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ([string]$pins.target -notmatch '^[a-z0-9_]+(-[a-z0-9_]+){2,}$' -or [string]$pins.llvmMingw -notmatch '^llvm-mingw-[0-9A-Za-z._-]+$') {
        throw 'tools\rust-pins.json must name a target triple and one llvm-mingw-* directory.'
    }
    $toolchain = Get-Content -LiteralPath (Join-Path $Project 'rust-toolchain.toml') -Raw -Encoding UTF8
    $channel = [regex]::Match($toolchain, '(?m)^\s*channel\s*=\s*"([^"]+)"')
    if (-not $channel.Success -or $channel.Groups[1].Value -notmatch '^\d+\.\d+\.\d+$') {
        throw 'rust-toolchain.toml must pin an exact Rust release (for example 1.99.1), not a moving channel.'
    }
    $targets = [regex]::Match($toolchain, '(?m)^\s*targets\s*=\s*\[([^\]]*)\]')
    if (-not $targets.Success -or $targets.Groups[1].Value -notmatch ('"' + [regex]::Escape($pins.target) + '"')) {
        throw ('rust-toolchain.toml does not install the pinned build target ' + $pins.target + '.')
    }
    [pscustomobject]@{ Target = [string]$pins.target; LlvmMingw = [string]$pins.llvmMingw; Channel = $channel.Groups[1].Value }
}

function Initialize-RustEnvironment {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Project,
        # Where the pinned tools live; a fixture directory in tests.
        [string]$ToolsDirectory = ''
    )
    $Project = [IO.Path]::GetFullPath($Project)
    $pins = Get-RustPins $Project
    if (-not $ToolsDirectory) { $ToolsDirectory = Join-Path $Project '.tools' }
    $mingw = [IO.Path]::GetFullPath((Join-Path $ToolsDirectory $pins.LlvmMingw))
    $bin = Join-Path $mingw 'bin'
    $linkerName = 'x86_64-w64-mingw32-clang.exe'
    foreach ($tool in @('clang.exe', $linkerName, 'llvm-windres.exe', 'llvm-ar.exe')) {
        if (-not (Test-Path -LiteralPath (Join-Path $bin $tool) -PathType Leaf)) {
            throw ('The pinned LLVM-MinGW (' + $pins.LlvmMingw + ') is missing ' + $tool + '. Run scripts\bootstrap-tools.ps1; no other llvm-mingw directory is used in its place.')
        }
    }
    $cargoHome = if ($env:CARGO_HOME) { $env:CARGO_HOME } else { Join-Path $env:USERPROFILE '.cargo' }
    # The pinned tools first, so the target's linker and C compiler cannot be
    # another installation that happens to be on PATH.
    $env:PATH = $bin + ';' + (Join-Path $cargoHome 'bin') + ';' + $env:PATH
    $env:VINEMIX_WINDRES = Join-Path $bin 'llvm-windres.exe'

    Push-Location -LiteralPath $Project   # rustup reads rust-toolchain.toml from here
    try {
        $rustc = (& rustc -vV | Out-String).Trim()
        if ($LASTEXITCODE -ne 0 -or -not $rustc) { throw 'rustc could not run. Install rustup; rust-toolchain.toml selects the release.' }
        $cargo = (& cargo -V | Out-String).Trim()
        if ($LASTEXITCODE -ne 0 -or -not $cargo) { throw 'cargo could not run.' }
        $libraries = (& rustc --print target-libdir --target $pins.Target | Out-String).Trim()
        if ($LASTEXITCODE -ne 0) { $libraries = '' }
    } finally { Pop-Location }
    $release = [regex]::Match($rustc, '(?m)^release:\s*(\S+)').Groups[1].Value
    $hostTriple = [regex]::Match($rustc, '(?m)^host:\s*(\S+)').Groups[1].Value
    if ($release -cne $pins.Channel) {
        throw ('rustc ' + $release + ' is not the pinned release ' + $pins.Channel + '. Use the rustup proxies so rust-toolchain.toml is honoured.')
    }
    if (-not $libraries -or -not (Test-Path -LiteralPath $libraries -PathType Container)) {
        throw ('The Rust target ' + $pins.Target + ' is not installed for ' + $pins.Channel + '. Run: rustup target add ' + $pins.Target)
    }
    $linker = @(Get-Command $linkerName -CommandType Application -ErrorAction Stop)[0].Source
    if ([IO.Path]::GetFullPath($linker) -ine (Join-Path $bin $linkerName)) {
        throw ('Another ' + $linkerName + ' precedes the pinned one on PATH: ' + $linker)
    }
    # Read every line before taking the first: cutting the pipeline short
    # would stop clang early and leave a meaningless exit code.
    $clangLines = @(& (Join-Path $bin 'clang.exe') --version)
    if ($LASTEXITCODE -ne 0 -or $clangLines.Count -eq 0) { throw 'The pinned clang could not run.' }
    $clang = ([string]$clangLines[0]).Trim()
    [pscustomobject]@{
        target = $pins.Target
        rustRelease = $pins.Channel
        rustc = $rustc
        cargo = $cargo
        rustHost = $hostTriple
        targetLibraryDirectory = $libraries
        llvmMingw = $pins.LlvmMingw
        llvmMingwDirectory = $mingw
        clang = $clang
        linker = (Join-Path $bin $linkerName)
        windres = $env:VINEMIX_WINDRES
    }
}
