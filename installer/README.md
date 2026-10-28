# VineMix Windows installer

One Windows 11 x64 setup contains the self-contained VineMix app, its read-only
setup helper and both complete original driver packages. It requires acceptance
of [TERMS.txt](TERMS.txt); driver installation also requires consent to
the vendor terms. This is the current free, non-commercial distribution. Future
monetisation and signing are separate release decisions.

## Build

From PowerShell 7.3 or later:

```powershell
.\scripts\build-installer.ps1
```

The application build uses the project's existing local build tools. The setup
compiler is pinned to Inno Setup 7.1.0, SHA-256 and its valid Pyrsys B.V. publisher
signature. Its own documented portable mode avoids global tool installation,
shortcuts and file associations. Official driver archives, full contents and
reviewed signatures are pinned by [drivers.json](drivers.json); altered packages
fail preparation. No build step installs or runs the audio drivers.

Outputs under `artifacts/installers/`:

- `VineMix-0.4.0-beta-Setup-x64.exe` — the installer.
- The `.exe.sha256` file — checksum.
- The `.exe.json` file — source payload location, version, hash and driver pins.

To package a separately staged app, use
`-ApplicationPackageDirectory <directory>`. `-Offline` requires cached compiler
and driver downloads; .NET/application dependencies must also already be cached.
Use `-DriverCacheDirectory <physical-cache>` to reuse verified archives from
another checkout. Driver preparation still rejects reparse points and verifies
the pinned hashes, archive contents and vendor signatures.
The script checks desktop/native source versions and the staged desktop version.
Private staging and compiler evidence default to `%LOCALAPPDATA%\VineMix.BuildCache`;
use a new `-StageDirectory` to choose another location outside the checkout.
Only app/runtime files, required notices and selected user guides are included;
development settings, research artifacts and build tools are excluded.

## Install and remove

Default application directory: `%LOCALAPPDATA%\Programs\VineMix`.
Existing saved data: `%LOCALAPPDATA%\VineMix`.

The virtual-audio page selects both missing eligible drivers by default on
interactive fresh install and upgrade. VB-CABLE is required for Virtual input;
a compatible VAC driver is required for Virtual output, with VAC Lite bundled.
The user can clear either selection. A separate vendor-terms page requires
explicit acceptance; preselection never accepts those terms. Returning and
changing the selection clears the previous driver consent. Existing
microphone/headphone routes and Virtual Desktop microphone input do not require
either bundled cable. Existing or uncertain driver installations are disabled
and unselected, preserving them without an automatic reinstall.

Setup is per-user. It requests administrator rights only when the user chooses
a missing driver, using that vendor's original installer. Existing driver names,
formats and installations are not reset. Disabled/nonpresent/staged packages
count as installed to avoid duplicates; the wizard explains they may need
enabling, repair or restart. Failed detection disables automatic installation.
The driver family check also preserves an existing full/trial VAC installation;
it does not certify its edition, compatibility or licence.

The desktop and engine are installed in one stable folder. Manual upgrades and
repair require the target copy to be quit via its tray menu; Setup never kills
it. A copy running elsewhere stays alive and prevents a second launch. Shared
data, the per-user engine pipe and singleton identity mean that different file
locations do not permit independent normal sessions.

Start menu shortcuts are created; a desktop shortcut is optional. Start-at-login
remains controlled by the app. Windows Installed apps provides uninstall.
Uninstall removes only registered app files, shortcuts and an exactly matching
owned startup value. It preserves saved data, unknown files and both drivers.

Application-only unattended setup requires explicit terms acknowledgement:

```powershell
.\VineMix-0.4.0-beta-Setup-x64.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /ACCEPTTERMS=1 /DRIVERS=none
```

Selected drivers install in the order **VAC Lite, then VB-CABLE**. Setup explains
that any restart must wait until VineMix Setup finishes. Windows elevation and
vendor consent remain explicit. VB-CABLE requires a restart; VAC normally does
not, although its installer can report one. Setup suppresses application launch
while a restart is pending. Cancelling a driver leaves the app installed, with
the driver result explained; re-run Setup to retry.

## Verification

```powershell
.\scripts\verify-installer.ps1 -InstallerPath <setup.exe> -PackageDirectory <payload-folder>
```

The payload path is recorded in the installer's JSON manifest. The verifier uses
a new isolated target and a distinct test AppId, with `/TESTMODE=1`, no icons,
no driver execution, no startup changes and no normal app launch. It checks:

- Silent setup rejects missing explicit terms acceptance.
- Missing drivers start selected in interactive policy fixtures; installed,
  uncertain, unattended and test-mode choices remain unselected. Vendor terms
  start unaccepted. Measured hidden wizard text/control bounds fit on both
  pages. This is geometry evidence, not a visual check of interactive wizard
  keyboard/screen-reader behavior.
- A harmless waiting fixture called VineMix.exe inside the target prevents
  replacement; it exits cooperatively without audio, shared settings or engine IPC.
- Install and same-version repair preserve exact packaged bytes and user files.
- A newer marked version prevents downgrade.
- The installed production desktop passes its inert `--release-smoke` checks,
  including offline notices. Full rendering verification uses a separate build.
- A harmless fixture below the installed folder prevents uninstall; setup keeps
  the app files and registration until that fixture exits cooperatively.
- Uninstall removes owned files and registration while preserving unknown files.
- The original mixer PIDs, executable hashes, saved JSON, startup values and
  shortcuts remain unchanged before and after.

`VineMix.SetupHelper --self-test` covers detection and path policies with synthetic
fixtures. Real read-only detection supplements those tests. Archive preparation
has tampering, missing-cache and unsafe-path tests. Full evidence is retained
under `artifacts/` and summarised in [the verification report](../docs/INSTALLER_VERIFICATION.md).
See [fresh Windows test instructions](../docs/INSTALLER_TESTING.md) for the
remaining driver, shortcut and first-launch qualification.

Fresh driver installation, UAC acceptance, reboot completion and driver audio
routing still require a disposable clean Windows target. Existing drivers are
not removed from the development machine to manufacture that test. The app and
installer currently have no publisher signature; the original driver signatures
do not replace it. See [the update architecture](../docs/INSTALLATION_AND_UPDATES.md)
for application update behavior and its separate qualification requirements.
