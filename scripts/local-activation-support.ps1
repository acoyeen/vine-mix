# Read-only validation and trusted-shell launch helpers for local activation.
# Dot-sourcing this file never starts a process or changes application data.
function Initialize-ActivationShellLauncher {
    if ('VineMix.Desktop.ActivationShellLauncher' -as [type]) { return }
    # Reuse the production launcher, including its session check, argument
    # quoting and shell-parent attribute. Do not maintain a weaker second copy.
    $source = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\desktop\VineMix.Desktop\StandaloneLaunch.cs') -Raw
    $bridge = @'
public static class ActivationShellLauncher {
    public static void Start(string executable, string[] arguments, string directory)
        => StandaloneLaunch.StartProcess(executable, arguments, directory);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint id);
    public static int ServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe) {
        if (!GetNamedPipeServerProcessId(pipe, out uint id))
            throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
        return checked((int)id);
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern System.IntPtr GetShellWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(System.IntPtr window, out uint id);
    public static int ShellProcessId() {
        if (GetWindowThreadProcessId(GetShellWindow(), out uint id) == 0)
            throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
        return checked((int)id);
    }
}
'@
    Add-Type -TypeDefinition ($source + "`n" + $bridge) -CompilerOptions '/nullable:enable'
}

function Normalize-ActivationPath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { throw 'An expected identity path is missing.' }
    if ($Path.StartsWith('\\?\UNC\', [StringComparison]::OrdinalIgnoreCase)) { $Path = '\\' + $Path.Substring(8) }
    elseif ($Path.StartsWith('\\?\', [StringComparison]::Ordinal)) { $Path = $Path.Substring(4) }
    [IO.Path]::GetFullPath($Path).TrimEnd('\','/')
}

function Assert-ActivationEvidenceLocation([string]$Evidence, [string[]]$ExcludedRoots) {
    $path = Normalize-ActivationPath $Evidence
    foreach ($root in $ExcludedRoots) {
        $normalized = Normalize-ActivationPath $root
        if ($path -ieq $normalized -or $path.StartsWith($normalized + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Activation evidence and recovery backups must be outside the source checkout and both application packages.'
        }
    }
}

function Get-ActivationPhysicalPath([string]$Path) {
    # Assert-DesktopDataContext defines the handle resolver without changing data.
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read,
        [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
    try { Normalize-ActivationPath ([VineMix.DesktopDataContext]::Resolve($stream.SafeFileHandle)) }
    finally { $stream.Dispose() }
}

function Test-ActivationShellParent {
    Initialize-ActivationShellLauncher
    $desktopShellId = [VineMix.Desktop.ActivationShellLauncher]::ShellProcessId()
    $self = Get-CimInstance Win32_Process -Filter ('ProcessId = ' + $PID)
    $shell = Get-Process -Id $desktopShellId
    try { $self.ParentProcessId -eq $desktopShellId -and $shell.SessionId -eq (Get-Process -Id $PID).SessionId }
    finally { $shell.Dispose() }
}

function Test-ActivationProcessIdentity($Actual, $Captured) {
    if ($null -eq $Actual -or $null -eq $Captured) { return $false }
    try {
        $Actual.Id -eq $Captured.Id -and $Actual.StartTime.ToUniversalTime().Ticks -eq $Captured.StartTime.ToUniversalTime().Ticks -and
            $Actual.Path -ieq $Captured.Path -and $Actual.SessionId -eq $Captured.SessionId
    } catch { $false }
}

function Replace-ActivationBinary([string]$Source, [string]$Destination, [string]$ExpectedHash,
        [string[]]$ExpectedDestinationHashes = @(), [scriptblock]$BeforeReplace = $null,
        [ValidateRange(0,10000)][int]$RetryTimeoutMilliseconds = 0) {
    $target = [IO.Path]::GetFullPath($Destination)
    $stage = Join-Path (Split-Path -Parent $target) ('.activation-' + [guid]::NewGuid().ToString('N') + '.new')
    if ($RetryTimeoutMilliseconds -gt 0 -and (-not $BeforeReplace -or -not $ExpectedDestinationHashes.Count)) {
        throw 'Replacement retries require an explicit process guard and known destination hashes.'
    }
    try {
        Copy-Item -LiteralPath $Source -Destination $stage
        if ((Get-FileHash -LiteralPath $stage -Algorithm SHA256).Hash -cne $ExpectedHash.ToUpperInvariant()) {
            throw 'The staged replacement does not match the verified binary hash.'
        }
        # An interrupted copy only affects our fresh staging file. The existing
        # executable remains intact until the same-directory atomic replacement.
        $timer = [Diagnostics.Stopwatch]::StartNew()
        $attempt = 0
        do {
            # A stopped process can briefly leave a mapped-image/scanner lock.
            # Never treat a retry as permission to overwrite a newer file or
            # a controller that appeared independently while waiting.
            if ($BeforeReplace) { & $BeforeReplace }
            if ($ExpectedDestinationHashes.Count -and
                ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -notin $ExpectedDestinationHashes)) {
                throw ('The destination changed before replacing ' + [IO.Path]::GetFileName($target) + '. Its newer contents were preserved.')
            }
            if ((Get-FileHash -LiteralPath $Source -Algorithm SHA256).Hash -cne $ExpectedHash.ToUpperInvariant() -or
                (Get-FileHash -LiteralPath $stage -Algorithm SHA256).Hash -cne $ExpectedHash.ToUpperInvariant()) {
                throw ('The replacement source changed for ' + [IO.Path]::GetFileName($target) + '.')
            }
            $attempt++
            try { [IO.File]::Move($stage, $target, $true); break }
            catch {
                $cause = $_.Exception
                while ($cause.InnerException) { $cause = $cause.InnerException }
                $code = $cause.HResult -band 0xffff
                $transient = ($cause -is [IO.IOException] -or $cause -is [UnauthorizedAccessException]) -and $code -in @(5,32,33)
                if (-not $transient -or $timer.ElapsedMilliseconds -ge $RetryTimeoutMilliseconds) {
                    throw [IO.IOException]::new(('Could not replace {0} after {1} attempt(s); Windows error {2}.' -f
                        [IO.Path]::GetFileName($target), $attempt, $code), $cause)
                }
                Start-Sleep -Milliseconds ([Math]::Min(150, [Math]::Max(1, $RetryTimeoutMilliseconds - $timer.ElapsedMilliseconds)))
            }
        } while ($true)
        if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -cne $ExpectedHash.ToUpperInvariant()) {
            throw 'The installed executable no longer matches the staged replacement.'
        }
    } finally {
        if (Test-Path -LiteralPath $stage -PathType Leaf) { Remove-Item -LiteralPath $stage }
    }
}

function Assert-ActivationSnapshotIdentity($Snapshot, [int]$ProcessId, [string]$Executable,
        [string]$Settings, [string]$PhysicalSettings, [string]$Version) {
    if ($Snapshot.engineProtocolVersion -ne 1 -or $Snapshot.enginePid -ne $ProcessId -or
        $Snapshot.engineVersion -cne $Version -or
        (Normalize-ActivationPath $Snapshot.executablePath) -ine (Normalize-ActivationPath $Executable) -or
        (Normalize-ActivationPath $Snapshot.settingsPath) -ine (Normalize-ActivationPath $Settings) -or
        (Normalize-ActivationPath $Snapshot.physicalSettingsPath) -ine (Normalize-ActivationPath $PhysicalSettings)) {
        throw 'The engine snapshot does not match the verified process, release or physical workspace.'
    }
    if ($Snapshot.settingsState -notin @('loaded','new')) {
        throw 'The current workspace requires recovery; local activation will preserve it without choosing a recovery action.'
    }
}

function Get-ActivationDataDirectories([string]$SettingsDirectory) {
    $directories = [Collections.Generic.List[string]]::new()
    $directories.Add([IO.Path]::GetFullPath($SettingsDirectory))
    $directories.Add((Join-Path $env:LOCALAPPDATA 'AudioMixer'))
    $packages = Join-Path $env:LOCALAPPDATA 'Packages'
    if (Test-Path -LiteralPath $packages -PathType Container) {
        foreach ($packageDirectory in Get-ChildItem -LiteralPath $packages -Directory -ErrorAction Stop) {
            foreach ($product in @('VineMix','AudioMixer')) {
                $directories.Add((Join-Path $packageDirectory.FullName ('LocalCache\Local\' + $product)))
            }
        }
    }
    @($directories | Sort-Object -Unique | Where-Object { Test-Path -LiteralPath $_ -PathType Container })
}

function Backup-ActivationDataViews([string]$SettingsDirectory, [string]$Destination) {
    $records = [Collections.Generic.List[object]]::new()
    $view = 0
    foreach ($directory in Get-ActivationDataDirectories $SettingsDirectory) {
        $files = @(Get-ChildItem -LiteralPath $directory -File | Where-Object {
            $_.Name -like 'settings.json*' -or $_.Name -in @('ui.json','whats-new.json','updates.json','migration.complete.json')
        })
        if (-not $files.Count) { continue }
        $records.Add([pscustomobject]@{kind='directory';source=$directory;names=@($files.Name | Sort-Object)})
        $folder = Join-Path $Destination ('view-' + (++$view).ToString('000'))
        $null = New-Item -ItemType Directory -Path $folder -Force
        foreach ($file in $files) {
            $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
            $physical = Get-ActivationPhysicalPath $file.FullName
            $copy = Join-Path $folder $file.Name
            Copy-Item -LiteralPath $file.FullName -Destination $copy
            if ((Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash -cne $hash -or
                (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -cne $hash -or
                (Get-ActivationPhysicalPath $file.FullName) -ine $physical) {
                throw 'A workspace view changed while its recovery backup was being preserved. No application process was stopped.'
            }
            $records.Add([pscustomobject]@{kind='file';source=$file.FullName; physicalPath=$physical; backup=$copy; sha256=$hash; lastWriteTimeUtc=$file.LastWriteTimeUtc})
        }
    }
    @($records)
}

function Assert-ActivationDataStable($Records) {
    foreach ($record in $Records) {
        if ($record.kind -eq 'directory') {
            $names = @(Get-ChildItem -LiteralPath $record.source -File | Where-Object {
                $_.Name -like 'settings.json*' -or $_.Name -in @('ui.json','whats-new.json','updates.json','migration.complete.json')
            } | Select-Object -ExpandProperty Name | Sort-Object)
            if (($names -join "`n") -cne ($record.names -join "`n")) {
                throw 'Workspace files appeared or disappeared during activation preparation. The current files remain authoritative.'
            }
            continue
        }
        if (-not (Test-Path -LiteralPath $record.source -PathType Leaf) -or
            (Get-FileHash -LiteralPath $record.source -Algorithm SHA256).Hash -cne $record.sha256 -or
            (Get-ActivationPhysicalPath $record.source) -ine $record.physicalPath) {
            throw 'Saved workspace data changed during activation preparation. The newer files remain authoritative.'
        }
    }
}

function Assert-ActivationShortcut([string]$ShortcutPath, [string]$ExecutablePath) {
    if ([IO.Path]::GetExtension($ShortcutPath) -ine '.lnk' -or -not (Test-Path -LiteralPath $ShortcutPath -PathType Leaf)) {
        throw 'Local activation requires an existing Windows shortcut for the selected application.'
    }
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $null
    try {
        $shortcut = $shell.CreateShortcut([IO.Path]::GetFullPath($ShortcutPath))
        if ((Normalize-ActivationPath $shortcut.TargetPath) -ine (Normalize-ActivationPath $ExecutablePath) -or
            -not [string]::IsNullOrWhiteSpace($shortcut.Arguments)) {
            throw 'The shortcut must point directly to the selected VineMix desktop without alternate settings or background-start arguments.'
        }
    } finally {
        if ($shortcut) { $null = [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut) }
        $null = [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
    }
}

function Start-ActivationEngine([string]$ExecutablePath, [string]$WorkingDirectory, [string]$SettingsPath) {
    Initialize-ActivationShellLauncher
    $expected = [IO.Path]::GetFullPath($ExecutablePath)
    $name = [IO.Path]::GetFileNameWithoutExtension($expected)
    if (@(Get-Process -Name $name -ErrorAction SilentlyContinue).Count) { throw 'An engine is already running; no replacement was launched.' }
    $session = (Get-Process -Id $PID).SessionId
    $started = [DateTime]::UtcNow
    [VineMix.Desktop.ActivationShellLauncher]::Start($expected, [string[]]@('--settings',[IO.Path]::GetFullPath($SettingsPath)), [IO.Path]::GetFullPath($WorkingDirectory))
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do {
        $processes = @(Get-Process -Name $name -ErrorAction SilentlyContinue)
        if ($processes.Count -gt 1) { throw 'More than one candidate engine appeared during shell launch.' }
        if ($processes.Count -eq 1) {
            $process = $processes[0]
            if ($process.Path -ine $expected -or $process.SessionId -ne $session -or $process.StartTime.ToUniversalTime() -lt $started) {
                $process.Dispose(); throw 'An unexpected engine appeared during trusted-shell launch.'
            }
            return $process
        }
        Start-Sleep -Milliseconds 50
    } while ($timer.ElapsedMilliseconds -lt 5000)
    throw 'The candidate engine did not appear after trusted-shell launch.'
}
