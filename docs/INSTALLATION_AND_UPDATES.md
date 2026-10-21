# VineMix installation and updates

For **0.3.0-beta.2**, Windows 11 x64. Updated 22 October 2026.
Download packages and read release notes on [GitHub Releases](https://github.com/acoyeen/vine-mix/releases).
For a first installation, follow the [installation steps](BETA_TESTER_GUIDE.md#choose-a-package).

## Packages and saved data

**Setup** installs the desktop, engine and runtime under
`%LOCALAPPDATA%\Programs\VineMix`, creates shortcuts and registers an uninstall
entry. **Standalone** launches from one EXE and prepares its bundled runtime under
`%LOCALAPPDATA%\VineMix\runtime\<payload-SHA-256>`. Neither needs a separate .NET
installation.

Both formats use `%LOCALAPPDATA%\VineMix` for the workspace, preferences, backups
and logs. They coordinate one mixer session. An existing workspace is retained;
legacy AudioMixer data is copied while keeping the originals. Conflicting legacy
stores require a choice, and damaged or missing established data enters recovery.

## Optional drivers

Physical microphone and listening routes, including an existing Virtual Desktop
microphone, do not need either bundled cable driver. Setup makes the app-only
route available and starts both optional driver choices unchecked. A selected
driver has a separate consent step; vendor setup can need administrator
permission and a restart. Installed but disabled devices do not require another
driver copy.

VB-CABLE supplies virtual input; VAC supplies virtual output. Standalone users
install these separately. **VAC Lite is limited to private home use without
income generation.** Keep vendor packages and notices intact; see
[driver downloads and terms](BETA_TESTER_GUIDE.md#optional-drivers).

## Check, download and install

1. Open **Settings → Check for updates**, or use the update button in About.
   Review the available version and release notes.
2. Choose **Download update**. VineMix verifies signed release information,
   the intended package format, file size and SHA-256 hash. Audio can keep running
   during the download.
3. Choose **Install and restart** when a brief audio interruption is acceptable.
   VineMix saves edits, stops its engine cooperatively, updates the app and
   reopens the saved workspace.

**Automatically check for updates** in Settings controls at-most-daily checks
after startup. Checking never downloads or installs an update without your choice.
An unreachable feed is reported as unavailable, rather than as “up to date.”

The update worker verifies the application identity, preserves owned application
files for recovery and checks the reopened workspace and engine before completing
the update. It does not restore an old settings file over newer user data.
If a safe rollback succeeds, the previous version reopens with a failure notice.
Interrupted installation or unfamiliar file changes can require manual repair;
retain the update journal and backups rather than deleting them.

**Upgrading from Beta 1:** download Beta 2 from
[Releases](https://github.com/acoyeen/vine-mix/releases), wait for **Saved**, use
**Quit VineMix** from the tray, then run Setup or open the newer standalone EXE.
That first upgrade is manual. Older installed copies missing an owned-file
inventory may also need a normal Setup repair before in-app installation works.

VineMix and Setup are currently unsigned Windows applications. The signed update
metadata authenticates release file hashes; it is separate from Windows
Authenticode, SmartScreen reputation and vendor driver signatures.

## Removal

Quit VineMix before removing it; closing the window only hides it in the tray.
Setup's uninstall entry removes its owned app files and shortcuts while keeping
settings, backups, logs and shared audio drivers. Unknown user files are retained.

For standalone, delete the launcher EXE after quitting. Its runtime cache can be
removed separately from `%LOCALAPPDATA%\VineMix\runtime` after all copies have
quit. Keep the rest of the data folder to retain your workspace. Remove shared
drivers only through their vendor instructions after checking other apps' needs.

## Developer references

- [Signed release preparation](UPDATE_RELEASES.md)
- [Update transaction and recovery limits](UPDATE_WORKER.md)
- [Installer development](../installer/README.md)
- [Earlier architecture proposals](INSTALLATION_UPDATE_DESIGN_HISTORY.md)
