# VineMix testing

Current beta: **0.3.0-beta.2**, Windows 11 x64.
The [Beta 2 test record](docs/TESTER_FIXES.md) describes implementation evidence
and remaining checks. [GitHub release notes](https://github.com/acoyeen/vine-mix/releases/tag/v0.3.0-beta.2)
identify the downloadable build; use the [tester guide](docs/BETA_TESTER_GUIDE.md)
for installation, updates and reporting.

## Automated verification

From PowerShell 7.3 or later at the repository root:

```powershell
pwsh -File .\scripts\verify-offline.ps1
```

The suite builds native offline programs and a separate desktop verification
executable. Synthetic audio, simulated devices, temporary settings and detached
controls cover graph editing, processing, persistence, recovery and update
behavior without changing a personal workspace or opening real audio hardware.

Update preparation and worker fixtures cover signatures, altered downloads,
process exit gates, recovery checkpoints, conflicting files and rollback. Their
results do not replace installation and recovery exercises with the actual
release packages. See [release preparation](docs/UPDATE_RELEASES.md) and
[worker limits](docs/UPDATE_WORKER.md).

Each result belongs to its exact source and files. Documentation changes do not
rerun earlier tests, and a passing fixture does not establish hardware reliability.

## Beta checks on real systems

Use a test installation or back up the data folder before testing changes that
can interrupt audio. Start with low headphone volume and record the version,
package format, Windows build, steps and result. Mark unavailable scenarios
**Not run**.

1. Install Setup and standalone on clean Windows accounts without a preinstalled
   .NET runtime. Exercise the app-only path and optional driver consent separately.
2. Assign devices, make a workspace edit, wait for **Saved**, quit from the tray
   and reopen through the installed shortcut or standalone launcher. Compare
   routes, assignments, levels, mutes and layout.
3. Test the Virtual Desktop microphone on actual hardware: enumeration, recording,
   reconnect and saved assignment after reopening.
4. Resize continuously, maximise/restore and change display scales. Check header,
   footer, cables, Add Node help, keyboard access and focus.
5. Listen through both outputs, test independent mutes, reconnect assigned devices
   and exercise sleep/resume. Record dropouts, drift or unexpected device changes.
6. Test a newer signed release through check, download and explicit installation
   for both formats. Use isolated copies for failure, rollback and interrupted
   update exercises; confirm the latest workspace is retained.
7. Run a normal eight-hour session with the interface and audio active, observing
   processing, memory, lost routes and unexpected exits.

These hardware and end-to-end checks remain subject to recorded results; they
are not all established by the automated suite. Earlier interrupted endurance
monitoring is not an eight-hour pass. An unexplained earlier application closure
remains under investigation. Report any new exit or workspace reset with the
relevant logs and reproduction steps.

## Historical evidence

The [pre-beta testing log](docs/history/TESTING_PRE_BETA1.md) and
[Beta 1 qualification record](docs/BETA_IMPLEMENTATION.md) retain earlier results.
Their commands, versions, file hashes and limits apply to those snapshots.
