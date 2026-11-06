#requires -Version 7.3
[CmdletBinding()]
param([string]$ResultDirectory = '', [string]$DotnetPath = '')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'assert-desktop-data-context.ps1')
. (Join-Path $PSScriptRoot 'local-activation-support.ps1')
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $ResultDirectory) { $ResultDirectory = Join-Path $env:LOCALAPPDATA ('VineMix.BuildCache\controller-identity-' + [guid]::NewGuid().ToString('N')) }
$root = [IO.Path]::GetFullPath($ResultDirectory)
Assert-ActivationEvidenceLocation $root @($project)
if (Test-Path -LiteralPath $root) { throw 'Choose a new verification directory.' }
$null = New-Item -ItemType Directory -Path $root
Initialize-DesktopDataContext
$locator = Join-Path $root 'fixture-location.txt'
'Isolated controller identity test' | Set-Content -LiteralPath $locator -Encoding utf8
$root = Split-Path -Parent (Get-ActivationPhysicalPath $locator)
Assert-ActivationEvidenceLocation $root @($project)
$checks = [Collections.Generic.List[string]]::new()
$passed = $false; $failure = $null
function Check([bool]$Condition, [string]$Description) {
    if (-not $Condition) { throw $Description }
    $checks.Add($Description)
}
function Reject([scriptblock]$Action, [string]$ExpectedMessage, [string]$Description) {
    $message = $null
    try { & $Action | Out-Null } catch { $message = $_.Exception.Message }
    Check ($null -ne $message -and $message -like $ExpectedMessage) $Description
}
function NewCandidate {
    $fixture = [pscustomobject]@{Id=123;SafeHandle=1;HasExited=$false;SessionId=2;StartTime=$started.AddSeconds(1);
        ImagePath=$expected;ImageReads=0;ExitOnRead=$false;QueryError=0;Ready=$true;Disposed=0}
    $fixture | Add-Member ScriptMethod Refresh { }
    $fixture | Add-Member ScriptMethod Dispose { $this.Disposed++ }
    $fixture | Add-Member ScriptProperty Path { throw 'The legacy Path accessor must not be used.' }
    $fixture
}
function InvokeFixtureWait([object[]]$Batches, [int]$Deadline = 300) {
    # These overrides are local to this function. No process enumeration, launch,
    # workspace-window inspection or application connection can escape the test.
    $fixtureState = @{Poll=0;Tokens=[Collections.Generic.List[object]]::new();Batches=$Batches}
    function Get-Process {
        param($Name, $ErrorAction)
        $index = [Math]::Min($fixtureState.Poll, $fixtureState.Batches.Count - 1)
        $fixtureState.Poll++
        $fixtureState.Batches[$index]
    }
    function Test-DesktopWorkspaceReady($Process) { $Process.Ready }
    $launcher = [pscustomobject]@{HasExited=$false;ExitCode=0}
    $launcher | Add-Member ScriptMethod Refresh { }
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $selected = $null; $message = $null
    try {
        $selected = Wait-DesktopController $launcher $expected 2 $started $timer $Deadline $false {
            param($identity) $fixtureState.Tokens.Add($identity)
        }
    } catch { $message = $_.Exception.Message }
    [pscustomobject]@{Selected=$selected;Error=$message;Tokens=$fixtureState.Tokens;Polls=$fixtureState.Poll;Elapsed=$timer.ElapsedMilliseconds}
}
$productionImageReader = ${function:Get-DesktopProcessImagePath}
try {
    $started = [DateTime]::UtcNow
    $expected = Join-Path $root 'Example.Controller.exe'
    function Get-DesktopProcessImagePath($Process) {
        $Process.ImageReads++
        if ($Process.ExitOnRead) { $Process.HasExited=$true; return $null }
        if ($Process.QueryError) { throw [ComponentModel.Win32Exception]::new($Process.QueryError) }
        $Process.ImagePath
    }
    $matching = NewCandidate
    $observation = Get-DesktopControllerCandidate $matching $expected 2 $started
    Check ($observation.State -eq 'Identified' -and $observation.Identity.Path -ceq $expected -and $matching.ImageReads -eq 1) 'A matching candidate yields one captured identity without the legacy Path accessor.'
    $exiting = NewCandidate; $exiting.ExitOnRead = $true
    Check ((Get-DesktopControllerCandidate $exiting $expected 2 $started).State -eq 'Exited') 'Exit during image inspection is skipped as a normalizing launcher.'
    $unknown = NewCandidate; $unknown.ImagePath = $null
    $observation = Get-DesktopControllerCandidate $unknown $expected 2 $started
    Check ($observation.State -eq 'Unidentified' -and $observation.Field -eq 'image-path') 'Missing live image identity remains unidentified rather than mismatched.'
    $denied = NewCandidate; $denied.QueryError = 5
    $observation = Get-DesktopControllerCandidate $denied $expected 2 $started
    Check ($observation.State -eq 'Unidentified' -and $observation.NativeError -eq 5 -and $observation.Field -eq 'image-path') 'Temporary native image-query failure retains a sanitized field and error code.'
    foreach ($field in @('image-path','session','start-time')) {
        $bad = NewCandidate
        switch ($field) {
            'image-path' { $bad.ImagePath = Join-Path $root 'Other.Controller.exe' }
            'session' { $bad.SessionId = 3; $bad.ImagePath = $null }
            'start-time' { $bad.StartTime = $started.AddSeconds(-1); $bad.ImagePath = $null }
        }
        Reject { Get-DesktopControllerCandidate $bad $expected 2 $started } ('*identity field: ' + $field + '*') ('A positively mismatched ' + $field + ' is rejected, including when another field is unavailable.')
    }
    $later = NewCandidate
    $result = InvokeFixtureWait @(@($unknown), @($later))
    Check ($null -eq $result.Error -and $result.Polls -eq 2 -and $result.Tokens.Count -eq 1 -and [object]::ReferenceEquals($later,$result.Selected)) 'An unidentified startup candidate can become verified within the original deadline.'
    Check ($later.ImageReads -eq 1 -and $result.Tokens[0].Path -ceq $expected) 'Readiness and rollback callback share the captured token without a second image lookup.'
    $result = InvokeFixtureWait @(@($denied)) 120
    Check ($result.Error -like '*startup deadline*image-path*native error: 5*' -and $result.Tokens.Count -eq 0 -and $result.Elapsed -lt 1500) 'A persistently unidentified process times out with a diagnostic and is never registered for rollback.'
    $result = InvokeFixtureWait -Batches (, @((NewCandidate),$unknown)) -Deadline 120
    Check ($null -eq $result.Selected -and $result.Tokens.Count -eq 0) 'An unknown second candidate prevents a known candidate from being accepted as unique.'
    $result = InvokeFixtureWait -Batches (, @((NewCandidate),(NewCandidate))) -Deadline 120
    Check ($null -eq $result.Selected -and $result.Tokens.Count -eq 0 -and $result.Error -like '*one responsive workspace*') 'Two matching live candidates cannot satisfy the unique-controller guard.'
    $result = InvokeFixtureWait @(@($exiting),@((NewCandidate)))
    Check ($null -eq $result.Error -and $result.Tokens.Count -eq 1) 'An exited normalization launcher can be followed by the verified final controller.'
    $wrong = NewCandidate; $wrong.ImagePath = Join-Path $root 'Other.Controller.exe'
    $result = InvokeFixtureWait @(@($wrong))
    Check ($result.Error -like '*identity field: image-path*' -and $result.Polls -eq 1 -and $result.Tokens.Count -eq 0) 'A genuine image mismatch fails on the first poll and is never registered for rollback.'
    $notReady = NewCandidate; $notReady.Ready = $false
    $result = InvokeFixtureWait @(@($notReady)) 120
    Check ($null -eq $result.Selected -and $result.Error -like '*one responsive workspace*' -and $result.Tokens.Count -gt 0) 'Identity alone cannot satisfy workspace readiness, but the verified candidate remains eligible for exact rollback.'
    $captured = (Get-DesktopControllerCandidate (NewCandidate) $expected 2 $started).Identity
    Check (Test-ActivationProcessIdentity $captured $captured) 'Rollback accepts the exact verified token.'
    foreach ($field in @('Id','StartTime','SessionId','Path')) {
        $different = [pscustomobject]@{Id=$captured.Id;StartTime=$captured.StartTime;SessionId=$captured.SessionId;Path=$captured.Path}
        switch ($field) {
            'Id' { $different.Id++ }
            'StartTime' { $different.StartTime = $different.StartTime.AddTicks(1) }
            'SessionId' { $different.SessionId++ }
            'Path' { $different.Path = Join-Path $root 'Different.exe' }
        }
        Check (-not (Test-ActivationProcessIdentity $different $captured)) ('Rollback rejects a changed ' + $field + ', including PID reuse with a new start time.')
    }
    Check (-not (Test-ActivationProcessIdentity $captured $null)) 'An unidentified process cannot gain rollback authority from a missing captured token.'
    Set-Item Function:Get-DesktopProcessImagePath $productionImageReader

    # A distinctly named, private console fixture waits for stdin and exits. It
    # has no audio APIs, settings, windows, shell launch or application linkage.
    if (-not $DotnetPath) { $DotnetPath = Join-Path $project '.tools\dotnet\dotnet.exe' }
    if (-not (Test-Path -LiteralPath $DotnetPath -PathType Leaf)) { throw 'Provide the local .NET SDK path for the inert native-process test.' }
    $source = Join-Path $root 'probe'
    $null = New-Item -ItemType Directory -Path $source
    '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><AssemblyName>VineMix.ActivationIdentityProbe</AssemblyName><UseAppHost>true</UseAppHost></PropertyGroup></Project>' | Set-Content -LiteralPath (Join-Path $source 'Probe.csproj') -Encoding utf8
    'System.Console.ReadLine();' | Set-Content -LiteralPath (Join-Path $source 'Program.cs') -Encoding utf8
    '<configuration><packageSources><clear /></packageSources></configuration>' | Set-Content -LiteralPath (Join-Path $source 'NuGet.Config') -Encoding utf8
    $output = Join-Path $source 'out'
    & $DotnetPath build (Join-Path $source 'Probe.csproj') --configuration Release --output $output --nologo --verbosity quiet *> (Join-Path $root 'probe-build.log')
    if ($LASTEXITCODE -ne 0) { throw 'The inert identity fixture failed to build; inspect the private build log.' }
    $probePath = Join-Path $output 'VineMix.ActivationIdentityProbe.exe'
    $self = [Diagnostics.Process]::GetCurrentProcess()
    try { $probeSession = $self.SessionId } finally { $self.Dispose() }
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        $info = [Diagnostics.ProcessStartInfo]::new($probePath)
        $info.UseShellExecute = $false; $info.CreateNoWindow = $true; $info.RedirectStandardInput = $true
        $info.Environment['DOTNET_ROOT'] = Split-Path -Parent ([IO.Path]::GetFullPath($DotnetPath))
        $launchTime = [DateTime]::UtcNow
        $child = [Diagnostics.Process]::Start($info)
        $observed = $null
        try {
            $observation = Get-DesktopControllerCandidate $child $probePath $probeSession $launchTime
            Check ($observation.State -eq 'Identified' -and $observation.Identity.Id -eq $child.Id -and $observation.Identity.Path -ieq $probePath) ('Real native image query identifies the inert child immediately after startup: ' + $attempt + '.')
            Check (Test-ActivationProcessIdentity $child $observation.Identity) ('The retained native handle revalidates the exact inert child for rollback: ' + $attempt + '.')
            $observed = [Diagnostics.Process]::GetProcessById($child.Id)
            $enumeratedIdentity = Get-ActivationProcessIdentity $observed
            Check (Test-ActivationProcessIdentity $enumeratedIdentity $observation.Identity) ('An independently observed process binds a handle to the same child identity: ' + $attempt + '.')
            $child.StandardInput.WriteLine(); $child.StandardInput.Close()
            Check ($child.WaitForExit(5000) -and (Read-DesktopProcessIdentity $child).State -eq 'Exited') ('The retained handle reports child exit without identifying a replacement process: ' + $attempt + '.')
            Check (-not (Test-ActivationProcessIdentity $child $observation.Identity)) ('An exited child cannot pass rollback identity: ' + $attempt + '.')
            Check ((Read-DesktopProcessIdentity $observed).State -eq 'Exited') ('The separately retained observation also reports the original child exit: ' + $attempt + '.')
        } finally {
            # This is exclusively the directly created inert child, bound by its
            # retained handle. No name lookup or personal process can be stopped.
            if (-not $child.HasExited) { $child.StandardInput.Close(); if (-not $child.WaitForExit(5000)) { $child.Kill(); $child.WaitForExit() } }
            if ($null -ne $observed) { $observed.Dispose() }
            $child.Dispose()
        }
    }
    $passed = $true
} catch { $failure = $_.Exception.ToString() }
finally {
    Set-Item Function:Get-DesktopProcessImagePath $productionImageReader
    [ordered]@{passed=$passed;checks=$checks;error=$failure;scope='Synthetic candidate polling and distinctly named inert child processes in a private fixture directory. No personal app processes, activation, shell launch, audio, saved settings or device access.'} |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $root 'results.json') -Encoding utf8
}
[pscustomobject]@{passed=$passed;checks=$checks.Count;error=$failure;reportDirectory=$root} | ConvertTo-Json -Depth 4
if (-not $passed) { exit 1 }
