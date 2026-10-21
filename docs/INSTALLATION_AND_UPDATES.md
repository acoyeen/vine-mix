# VineMix installation and updates

Current beta: **0.3.0-beta.1**. Updated 21 October 2026. The maintainer supplies private
beta files directly; no public release channel or automatic updater is enabled.
See the [beta test guide](BETA_TESTER_GUIDE.md) for installation instructions and
[the qualification record](BETA_IMPLEMENTATION.md) for outstanding gates.

This page describes the supplied beta files. The source currently committed on
GitHub is the earlier development snapshot; see [source status](../README.md#source-status).

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
[the beta driver guidance](BETA_TESTER_GUIDE.md#optional-drivers). The
[installer development guide](../installer/README.md) describes the older
committed implementation until beta source integration is complete.

## Manual updates and removal

Wait for **Saved**, then use **Quit VineMix** from the tray. Closing the main
window only hides it and leaves audio running. Run the newer supplied Setup, or
open the newer standalone EXE after the older copy quits. Do not combine payload
files from separate versions or force replacement while a target is in use.

Setup repairs or upgrades its application files while retaining personal data.
Uninstall removes its owned application files and shortcuts, retaining workspace,
preferences, backups, logs and shared drivers. Unknown user files are not owned
by the installer. Start-at-login is an app preference and should only be removed
when its value belongs to the removed copy.

The current app, helper and Setup are unsigned development artifacts. Verified
vendor driver signatures do not sign or certify VineMix. Signing, package
checksums, clean-machine installation and audible reliability are separate
qualification checks. Follow only checksums supplied for the exact beta file;
historical installer hashes do not describe the current package.

## Future updater proposals

An in-app checker, signed release manifest, downloaded upgrades, a stable
version-selecting launcher, automatic rollback and post-update release highlights
remain proposals. Their older design sketches are not current app behavior.
Implementing them needs a configured release channel, exact-version signature and
hash validation, an explicit save/stop handshake, crash-safe replacement, retained
rollback data and separate qualification. There is no anonymous production
release endpoint for this private beta, and no developer account token should be
embedded in a client.

The [earlier architecture record](INSTALLATION_UPDATE_DESIGN_HISTORY.md) retains
the detailed proposed updater design and pre-beta implementation notes.
