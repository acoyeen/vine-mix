# Vine Mix setup detection helper

This Windows x64 helper inspects the system. It never installs/removes drivers,
changes devices, terminates processes, or changes application settings. Only the
explicitly requested output INI is written.

```
VineMix.SetupHelper.exe detect --output <file> [--install-dir <directory>]
VineMix.SetupHelper.exe --self-test
```

Exit codes: `0` means complete detection; `2` means conservative partial detection
(the INI is still available); `1` means invalid arguments or failure writing the
report. Setup must require **both** `DetectSucceeded=1` and `Installed=0` before
offering an automatic driver installation. A failed helper invocation must never
be treated as evidence that a driver is missing.

`[VBCable]` and `[VAC]` contain `DetectSucceeded`, `Installed`, `State`, `Detail`,
and `Version`. Possible states are `DevicePresent`, `DeviceDisabled`,
`DeviceUnavailable`, `DeviceNeedsAttention`, `DriverStaged`,
`PackageNeedsAttention`, `Missing`, and `DetectionFailed`. Disabled, nonpresent,
problematic, and staged devices/packages all prevent automatic duplicate
installation. A missing/disconnected audio endpoint is not treated as a missing
driver. Device names can be renamed without affecting detection.

The device check enumerates all Windows SetupAPI devnodes, including nonpresent
ones, and compares exact hardware IDs (`VBAudioVACWDM` and
`VirtualAudioCable_83ed7f0e`, including `ROOT\` forms). Published OEM INFs are
checked independently for staged-only packages, using the vendor, Media class,
actual Manufacturer/model declarations, and the exact hardware ID. INF `%Strings%`
tokens, model section decorations, quoted fields, comments, and UTF-16 encodings
are handled. SetupAPI verifies the corresponding driver store location. Text in
a display name or comment alone does not establish a driver identity. VAC is
detected as a family, so existing full/trial editions are preserved; the helper
does not certify edition or permitted use. Unknown hardware identities from future
driver releases need explicit review before adding support.

`[Application]` contains `DetectSucceeded`, `Running`, `RunningInstalled`,
`InstallPathSafe`, `State`, and `Detail`. `Running=1` means the current Windows user's `AudioMixer.exe` or
`AudioMixer.Engine.exe` is running anywhere. `RunningInstalled=1` additionally
means a process is using a file beneath `--install-dir`. Another folder's running
session prevents a second launch but does not prevent copying files into an unused
install folder. Existing path components are resolved by Windows file handles, so
junction/symlink aliases cannot bypass the running-target guard.
`InstallPathSafe=0` rejects the saved-data folder or its descendants, including
aliases, and rejects system/user folder roots. Setup must require
`InstallPathSafe=1` before copying files. An unreadable possible session sets both flags and
`DetectSucceeded=0`, which prevents unsafe overwrite or launch. Another user's
identifiable process is ignored. Reports contain no process IDs, user IDs, or
personal executable paths. Setup must repeat the check immediately before
replacing files and before optional launch, because a report is a snapshot.

The helper is self-contained, runs with the caller's privileges, and has no
PowerShell/WMI dependency. Build using the repository-local .NET SDK:

```
.tools\dotnet\dotnet.exe publish installer\VineMix.SetupHelper\VineMix.SetupHelper.csproj -c Release --output <output-directory>
```

The self-test covers positive and negative package identity fixtures; staged,
disabled, stale and failed enumeration policy; current-user and target-folder
process isolation; permission ambiguity; canonical-path and saved-data protection;
and INI output sanitization. It runs
without querying or modifying actual devices or processes. A real read-only
detection check supplements it during installer verification.
