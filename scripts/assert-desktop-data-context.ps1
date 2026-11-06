# This guard is read-only. Activation must inspect the same physical settings
# that the ordinary desktop app uses, before stopping or replacing any process.
function Initialize-DesktopDataContext {
    if (-not ('VineMix.DesktopDataContext' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
namespace VineMix {
    public static class DesktopDataContext {
        [DllImport("kernel32.dll", EntryPoint="QueryFullProcessImageNameW", CharSet=CharSet.Unicode, SetLastError=true)]
        private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder path, ref int length);
        public static string ReadImagePath(SafeProcessHandle process) {
            var path = new StringBuilder(32768);
            int length = path.Capacity;
            if (!QueryFullProcessImageName(process, 0, path, ref length))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            return System.IO.Path.GetFullPath(path.ToString());
        }
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
        private static extern uint GetFinalPathNameByHandleW(SafeFileHandle file, StringBuilder path, uint capacity, uint flags);
        public static string Resolve(SafeFileHandle file) {
            var path = new StringBuilder(32768);
            uint count = GetFinalPathNameByHandleW(file, path, (uint)path.Capacity, 0);
            if (count == 0 || count >= path.Capacity)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            return path.ToString();
        }
        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)]
        private static extern int GetWindowTextW(IntPtr window, StringBuilder text, int count);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)]
        private static extern int GetClassNameW(IntPtr window, StringBuilder text, int count);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        public static bool MatchesWorkspaceIdentity(string title, string className, bool visible, uint actualProcess, uint expectedProcess)
            => visible && actualProcess == expectedProcess && title == "VineMix · Patch workspace"
                && className.StartsWith("HwndWrapper[", StringComparison.Ordinal);
        public static bool IsWorkspaceWindow(IntPtr window, uint expectedProcess) {
            var title = new StringBuilder(1024); var className = new StringBuilder(256);
            GetWindowTextW(window, title, title.Capacity); GetClassNameW(window, className, className.Capacity);
            GetWindowThreadProcessId(window, out uint actualProcess);
            return MatchesWorkspaceIdentity(title.ToString(), className.ToString(), IsWindowVisible(window), actualProcess, expectedProcess);
        }
    }
}

'@
    }
}
function Assert-DesktopDataContext([string]$SettingsPath) {
    Initialize-DesktopDataContext
    $expected = [IO.Path]::GetFullPath($SettingsPath)
    $stream = [IO.File]::Open($expected, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
    try { $actual = [VineMix.DesktopDataContext]::Resolve($stream.SafeFileHandle) }
    finally { $stream.Dispose() }
    if ($actual.StartsWith('\\?\', [StringComparison]::Ordinal)) { $actual = $actual.Substring(4) }
    if (-not [string]::Equals($actual, $expected, [StringComparison]::OrdinalIgnoreCase)) {
        throw "The settings path is redirected to '$actual'. Run activation from an ordinary Windows PowerShell session so it uses the same saved workspace as VineMix. No application process was changed."
    }
}

function Get-DesktopProcessImagePath($Process) {
    Initialize-DesktopDataContext
    [VineMix.DesktopDataContext]::ReadImagePath($Process.SafeHandle)
}

function Read-DesktopProcessIdentity($Process) {
    # Bind before reading any identity fields. The Process owns this handle until
    # disposed, so a recycled PID cannot redirect later inspection or rollback.
    $identity = [pscustomobject]@{Id=$null; StartTime=$null; Path=$null; SessionId=$null}
    $field = 'handle'; $nativeError = 0
    try {
        $null = $Process.SafeHandle
        $Process.Refresh()
        if ($Process.HasExited) { return [pscustomobject]@{State='Exited';Identity=$null;Field=$null;NativeError=0} }
        $field = 'process-id'; $identity.Id = $Process.Id
        $field = 'start-time'; $identity.StartTime = $Process.StartTime
        $field = 'session'; $identity.SessionId = $Process.SessionId
        $field = 'image-path'; $identity.Path = Get-DesktopProcessImagePath $Process
        $Process.Refresh()
        if ($Process.HasExited) { return [pscustomobject]@{State='Exited';Identity=$null;Field=$null;NativeError=0} }
        if (-not [string]::IsNullOrWhiteSpace($identity.Path)) {
            return [pscustomobject]@{State='Identified';Identity=$identity;Field=$null;NativeError=0}
        }
    } catch {
        $failure = $_.Exception
        while ($failure.InnerException) { $failure = $failure.InnerException }
        if ($failure -isnot [ComponentModel.Win32Exception] -and $failure -isnot [InvalidOperationException]) { throw }
        if ($failure -is [ComponentModel.Win32Exception]) { $nativeError = $failure.NativeErrorCode }
        $exited = $false
        try { $Process.Refresh(); $exited = $Process.HasExited } catch { }
        if ($exited) { return [pscustomobject]@{State='Exited';Identity=$null;Field=$null;NativeError=0} }
    }
    [pscustomobject]@{State='Unidentified';Identity=$identity;Field=$field;NativeError=$nativeError}
}

function Get-DesktopControllerCandidate($Process, [string]$ExpectedPath, [int]$Session, [DateTime]$Started) {
    $observation = Read-DesktopProcessIdentity $Process
    if ($observation.State -eq 'Exited') { return $observation }
    $identity = $observation.Identity
    # Reject a positively known mismatch even if another field is unavailable.
    $mismatch = if ($null -ne $identity.SessionId -and $identity.SessionId -ne $Session) { 'session' }
        elseif ($null -ne $identity.StartTime -and $identity.StartTime.ToUniversalTime() -lt $Started) { 'start-time' }
        elseif (-not [string]::IsNullOrWhiteSpace($identity.Path) -and $identity.Path -ine $ExpectedPath) { 'image-path' }
    if ($mismatch) { throw "An unexpected controller instance appeared while reopening the application (identity field: $mismatch)." }
    return $observation
}

function Test-DesktopControllerCandidate($Process, [string]$ExpectedPath, [int]$Session, [DateTime]$Started) {
    $observation = Get-DesktopControllerCandidate $Process $ExpectedPath $Session $Started
    if ($observation.State -eq 'Unidentified') {
        throw "A running controller could not be identified safely (identity field: $($observation.Field); native error: $($observation.NativeError))."
    }
    $observation.State -eq 'Identified'
}

function Test-DesktopWorkspaceReady($Process) {
    try {
        $Process.MainWindowHandle -ne 0 -and $Process.Responding -and $Process.WaitForInputIdle(0) -and
            [VineMix.DesktopDataContext]::IsWorkspaceWindow($Process.MainWindowHandle, $Process.Id)
    } catch [ComponentModel.Win32Exception], [InvalidOperationException] { $false }
}

function Wait-DesktopController($Launcher, [string]$ExpectedPath, [int]$Session, [DateTime]$Started,
        [Diagnostics.Stopwatch]$Timer, [int]$TimeoutMilliseconds, [bool]$ShortcutLaunch, [scriptblock]$OnVerifiedProcess) {
    $name = [IO.Path]::GetFileNameWithoutExtension($ExpectedPath)
    do {
        $Launcher.Refresh()
        # Explorer can exit after handing the shortcut to its existing shell.
        if (-not $ShortcutLaunch -and $Launcher.HasExited -and $Launcher.ExitCode -ne 0) {
            throw "The desktop controller launcher exited with code $($Launcher.ExitCode)."
        }
        $candidates = @(Get-Process -Name $name -ErrorAction SilentlyContinue)
        $selected = $null; $unidentified = $null
        try {
            $live = @()
            foreach ($process in $candidates) {
                $observation = Get-DesktopControllerCandidate $process $ExpectedPath $Session $Started
                if ($observation.State -eq 'Identified') { $live += [pscustomobject]@{Process=$process;Identity=$observation.Identity} }
                elseif ($observation.State -eq 'Unidentified') { $unidentified = $observation }
            }
            # An unknown candidate also prevents uniqueness being established.
            # Never register it for stopping or recovery while waiting to retry.
            if ($live.Count -eq 1 -and $null -eq $unidentified) {
                $process = $live[0].Process
                if ($OnVerifiedProcess) { & $OnVerifiedProcess $live[0].Identity }
                if (Test-DesktopWorkspaceReady $process) {
                    $selected = $process
                    return $process
                }
            }
        } finally {
            foreach ($process in $candidates) {
                if (-not [object]::ReferenceEquals($process, $selected)) { $process.Dispose() }
            }
        }
        $remaining = $TimeoutMilliseconds - $Timer.ElapsedMilliseconds
        if ($remaining -gt 0) { Start-Sleep -Milliseconds ([Math]::Min(100, $remaining)) }
    } while ($Timer.ElapsedMilliseconds -lt $TimeoutMilliseconds)
    if ($null -ne $unidentified) {
        throw "A running controller remained unidentified at the startup deadline (identity field: $($unidentified.Field); native error: $($unidentified.NativeError)). No unidentified process was registered for rollback."
    }
    throw 'The desktop controller did not open one responsive workspace window before the startup deadline.'
}

function Start-DesktopController {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ExecutablePath,
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [string]$ShortcutPath = '',
        [scriptblock]$OnVerifiedProcess,
        [ValidateRange(1, 60000)][int]$TimeoutMilliseconds = 20000
    )
    Initialize-DesktopDataContext
    $expected = [IO.Path]::GetFullPath($ExecutablePath)
    $directory = [IO.Path]::GetFullPath($WorkingDirectory)
    $name = [IO.Path]::GetFileNameWithoutExtension($expected)
    if (-not (Test-Path -LiteralPath $expected -PathType Leaf)) { throw 'The desktop controller executable is missing.' }
    $existing = @(Get-Process -Name $name -ErrorAction SilentlyContinue)
    try {
        if ($existing.Count) { throw 'A desktop controller is already running; no second controller was launched.' }
    } finally { foreach ($process in $existing) { $process.Dispose() } }
    $current = Get-Process -Id $PID
    try { $session = $current.SessionId } finally { $current.Dispose() }
    $started = [DateTime]::UtcNow
    $timer = [Diagnostics.Stopwatch]::StartNew()
    # The existing activation workflow explicitly reopens the interactive app.
    # Its first process now relaunches into the ordinary desktop data context;
    # only the final process owns the responsive workspace window.
    if ($ShortcutPath) {
        # Activation validates the link target before changing any process and
        # again here. Explorer opens the actual shortcut used by the maintainer.
        Assert-ActivationShortcut $ShortcutPath $expected
        $shortcut = [IO.Path]::GetFullPath($ShortcutPath)
        if ($shortcut.Contains('"')) { throw 'The shortcut path contains an invalid quote.' }
        $launcher = Start-Process -FilePath (Join-Path $env:WINDIR 'explorer.exe') -ArgumentList ('"' + $shortcut + '"') -WindowStyle Hidden -PassThru
    } else {
        $launcher = Start-Process -FilePath $expected -WorkingDirectory $directory -PassThru
    }
    try {
        Wait-DesktopController $launcher $expected $session $started $timer $TimeoutMilliseconds ([bool]$ShortcutPath) $OnVerifiedProcess
    } finally { $launcher.Dispose() }
}
