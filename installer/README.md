# Vine Mix Windows installer

One Windows 11 x64 setup contains the self-contained Vine Mix app, its read-only
setup helper and both complete original driver packages. It requires acceptance
of [TERMS.txt](TERMS.txt); optional driver installation also requires consent to
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

- `VineMix-0.2.0-Setup-x64.exe` — the installer.
- The `.exe.sha256` file — checksum.
- The `.exe.json` file — source payload location, version, hash and driver pins.

To package a separately staged app, use
`-ApplicationPackageDirectory <directory>`. `-Offline` requires cached compiler
and driver downloads; .NET/application dependencies must also already be cached.
The script checks desktop/native source versions and the staged desktop version.
Only app/runtime files, required notices and selected user guides are included;
development settings, research artifacts and build tools are excluded.

## Install and remove

Default application directory: `%LOCALAPPDATA%\Programs\Vine Mix`.
Existing saved data: `%LOCALAPPDATA%\AudioMixer`.

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
.\VineMix-0.2.0-Setup-x64.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /ACCEPTTERMS=1 /DRIVERS=none
```

Driver installation is interactive only. Vendor windows and Windows elevation
prompts remain visible and user-controlled. After installing a driver, Setup
requests a restart and suppresses application launch. Cancelling a driver leaves
the app installed, with the driver result explained; re-run Setup to retry.

## Verification

```powershell
.\scripts\verify-installer.ps1 -InstallerPath <setup.exe> -PackageDirectory <payload-folder>
```

The payload path is recorded in the installer's JSON manifest. The verifier uses
a new isolated target and a distinct test AppId, with `/TESTMODE=1`, no icons,
no driver execution, no startup changes and no normal app launch. It checks:

- Silent setup rejects missing explicit terms acceptance.
- A harmless waiting fixture called AudioMixer.exe inside the target prevents
  replacement; it exits cooperatively without audio, shared settings or engine IPC.
- Install and same-version repair preserve exact packaged bytes and user files.
- A newer marked version prevents downgrade.
- The installed desktop passes its detached rendering/verification branch.
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
do not replace it. The automated online updater and journalled rollback remain
future work in [the update architecture](../docs/INSTALLATION_AND_UPDATES.md).
