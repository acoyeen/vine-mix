# This guard is read-only. Activation must inspect the same physical settings
# that the ordinary desktop app uses, before stopping or replacing any process.
function Assert-DesktopDataContext([string]$SettingsPath) {
    if (-not ('VineMix.DesktopDataContext' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
namespace VineMix {
    public static class DesktopDataContext {
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
        private static extern uint GetFinalPathNameByHandleW(SafeFileHandle file, StringBuilder path, uint capacity, uint flags);
        public static string Resolve(SafeFileHandle file) {
            var path = new StringBuilder(32768);
            uint count = GetFinalPathNameByHandleW(file, path, (uint)path.Capacity, 0);
            if (count == 0 || count >= path.Capacity)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            return path.ToString();
        }
    }
}

'@
    }
    $expected = [IO.Path]::GetFullPath($SettingsPath)
    $stream = [IO.File]::Open($expected, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
    try { $actual = [VineMix.DesktopDataContext]::Resolve($stream.SafeFileHandle) }
    finally { $stream.Dispose() }
    if ($actual.StartsWith('\\?\', [StringComparison]::Ordinal)) { $actual = $actual.Substring(4) }
    if (-not [string]::Equals($actual, $expected, [StringComparison]::OrdinalIgnoreCase)) {
        throw "The settings path is redirected to '$actual'. Run activation from an ordinary Windows PowerShell session so it uses the same saved workspace as VineMix. No application process was changed."
    }
}

function Start-DesktopController {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ExecutablePath,
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [ValidateRange(1, 60000)][int]$TimeoutMilliseconds = 20000
    )
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
    $launcher = Start-Process -FilePath $expected -WorkingDirectory $directory -PassThru
    try {
        do {
            $launcher.Refresh()
            if ($launcher.HasExited -and $launcher.ExitCode -ne 0) {
                throw "The desktop controller launcher exited with code $($launcher.ExitCode)."
            }
            $candidates = @(Get-Process -Name $name -ErrorAction SilentlyContinue)
            $selected = $null
            try {
                $live = @()
                foreach ($process in $candidates) {
                    try {
                        $process.Refresh()
                        if ($process.HasExited) { continue }
                        if ($process.Path -ine $expected -or $process.SessionId -ne $session -or
                            $process.StartTime.ToUniversalTime() -lt $started) {
                            throw 'An unexpected controller instance appeared while reopening the application.'
                        }
                        $live += $process
                    } catch [System.ComponentModel.Win32Exception], [InvalidOperationException] {
                        # A short-lived launcher may exit while its properties
                        # are inspected. Never attach to an unverified process.
                        $exited = $false
                        try { $process.Refresh(); $exited = $process.HasExited } catch { }
                        if ($exited) { continue }
                        throw 'A running controller could not be identified safely while reopening the application.'
                    }
                }
                if ($live.Count -eq 1) {
                    $process = $live[0]
                    try {
                        if ($process.MainWindowHandle -ne 0 -and $process.Responding -and $process.WaitForInputIdle(0)) {
                            $selected = $process
                            return $process
                        }
                    } catch [System.ComponentModel.Win32Exception], [InvalidOperationException] { }
                }
            } finally {
                foreach ($process in $candidates) {
                    if (-not [object]::ReferenceEquals($process, $selected)) { $process.Dispose() }
                }
            }
            if ($timer.ElapsedMilliseconds -lt $TimeoutMilliseconds) { Start-Sleep -Milliseconds 100 }
        } while ($timer.ElapsedMilliseconds -lt $TimeoutMilliseconds)
        throw 'The desktop controller did not open one responsive workspace window before the startup deadline.'
    } finally { $launcher.Dispose() }
}
