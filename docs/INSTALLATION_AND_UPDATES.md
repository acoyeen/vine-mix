# VineMix installation and updates

Updated 23 October 2026 for **0.3.0 · Beta 4**. VineMix is an
MIT-licensed open-source beta for Windows 11 x64. Download versioned packages,
checksums and matching source from [GitHub Releases](https://github.com/acoyeen/vine-mix/releases).
See the [beta test guide](BETA_TESTER_GUIDE.md) for installation instructions and
[current qualification](BETA_TESTING_CURRENT.md) for outstanding gates.

## Packages and files

The Windows 11 x64 Setup installs the matched desktop and engine together under
`%LOCALAPPDATA%\Programs\VineMix`, creates shortcuts, and registers an uninstall
entry. The single standalone EXE prepares its bundled runtime files automatically
without registering an application installation. Both include .NET and preserve
required third-party notices.

Both launch the same app and use `%LOCALAPPDATA%\VineMix` for workspace,
preferences, backups and logs. They coordinate one desktop and engine session for
the Windows user. Opening another copy should activate the current desktop rather
than run a competing mixer. The app normalizes startup through the Windows shell
before loading settings or starting an engine, including a launch inherited from
a packaged tool. This prevents redirected AppData from selecting a different
personal workspace.

At first launch, existing AudioMixer data is copied to the new data directory
without deleting its sources. A journal makes interrupted copying restartable;
conflicting legacy copies require a deliberate source choice. An existing VineMix
workspace takes precedence. A malformed or missing established workspace requires
visible recovery before normal editing and audio starts. Recovery archives the
originals before restoring a backup or creating the muted starter.

## Optional drivers

Setup detects VB-CABLE and VAC by their driver/package identities, including
installed but disabled or nonpresent devices. It offers original vendor setup
only for confirmed missing packages, with separate acceptance and usage terms.
Driver installation can require elevation and a restart. Setup does not remove
shared audio drivers during app uninstall. The standalone EXE requires separate
driver installation for virtual routes. Physical microphone and listening routes
do not require these virtual drivers.

VB-Audio bundling permission has been obtained for the current integrated flow.
VAC Lite still carries private-home/non-income usage restrictions. Preserve the
complete original vendor packages and their notices; see
[distribution conditions](DISTRIBUTION.md) and [installer implementation](../installer/README.md).

## Manual updates and removal

Use **Quit VineMix** at the bottom of Settings or from the tray. It saves first
and offers retry/recovery/cancel choices if that save fails. Closing the main
window only hides it and leaves audio running. Run the newer supplied Setup, or
open the newer standalone EXE after the older copy quits. Do not combine payload
files from separate versions or force replacement while a target is in use.

Setup repairs or upgrades its application files while retaining personal data.
Uninstall removes its owned application files and shortcuts, retaining workspace,
preferences, backups, logs and shared drivers. Unknown user files are not owned
by the installer. Start-at-login is an app preference and should only be removed
when its value belongs to the removed copy.

The current app, helper and Setup are not Windows Authenticode signed. Verified
vendor driver signatures do not sign or certify VineMix. Signing, package
checksums, clean-machine installation and audible reliability are separate
qualification checks. Follow only checksums supplied for the exact beta file;
historical installer hashes do not describe the current package.

## In-app updates

Beta 2 and later provide **Check for updates** in Settings. Checking, downloading
and installation are separate actions. Optional daily checks never install an
update without your choice. Beta 1 needs a manual upgrade to gain this feature.

**Download update** verifies signed release metadata and the exact package hash
while the current session remains available. **Install and restart** first saves
pending edits and confirms the physical workspace, then stops the verified
engine cooperatively. The worker retains recovery files, installs the matching
format and reopens the saved workspace. Audio pauses during installation.

A successful restart requires the expected version, workspace identity and
advancing engine frames. A safe rollback reopens the previous application;
ambiguous or interrupted replacement can require manual repair. The updater
never restores an older workspace over newer data. See [worker recovery limits](UPDATE_WORKER.md).

The project signature authenticates update metadata and package hashes; it is
separate from Windows Authenticode signing. If the update service is unavailable,
retry later or use the matching GitHub release for a manual update. The client
never contains a repository credential.

[Current qualification](BETA_TESTING_CURRENT.md) records the scope of update
checks. Component fixtures do not prove the complete delivered upgrade and
rollback flow. [Earlier design notes](INSTALLATION_UPDATE_DESIGN_HISTORY.md)
remain historical research, not current installation instructions.
