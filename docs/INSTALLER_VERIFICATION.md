# Historical installer verification — 20 October 2026

> This is evidence for the older 0.2.0 artifacts only. It does not qualify
> the current beta. See [the beta record](BETA_IMPLEMENTATION.md) and
> [current clean-machine instructions](INSTALLER_TESTING.md).

The Windows 11 x64 installer was built for private review and fresh-machine
testing. It is an unsigned development build, not a completed driver/hardware
qualification. Fresh Windows testing is deferred to 21 October at the maintainer's
request. See [the test instructions](INSTALLER_TESTING.md).

## Final artifact

| Item | Value |
|---|---|
| File | `VineMix-0.2.0-Setup-x64.exe` |
| Size | 103,718,827 bytes |
| SHA-256 | `AC27DEBEFF500F126D87777DBCA28561C910083387C69D02AD0D10D39BAC0C1E` |
| Platform | Windows 11, native x64 |
| Default application folder | `%LOCALAPPDATA%\Programs\Vine Mix` |
| Retained data folder | `%LOCALAPPDATA%\AudioMixer` |
| Distribution | Free, non-commercial driver bundle; separate vendor terms apply |

Compilation succeeded with pinned Inno Setup 7.1.0. The final build ran all
29 setup-helper policy checks successfully and verified both complete original
driver packages, per-file hashes and reviewed x64 signatures. Application and
helper are self-contained; users do not need a separate .NET installation.

## Completed local verification

The preceding installer, SHA-256
`89DE0C5096133D5E84A5AF93CE1794A5EFE05A71FC7D809DAA43591B5EF30DDC`,
passed **258 installer checks** in an isolated per-user installation:

- Unattended installation without explicit terms acceptance was rejected.
- A harmless process in the destination prevented replacement and uninstall.
- Installation and same-version repair matched every staged payload hash.
- A marked newer installation prevented downgrade.
- Both positive installs explicitly skipped driver execution.
- Uninstall removed owned files and its isolated registration, retaining an
  unknown user file.
- Personal saved JSON, original mixer/engine process identities and executable
  hashes, startup values and shortcuts remained unchanged throughout.
- The installed desktop's detached verification passed **10,806 checks**, with
  **215 renders** and **zero binding errors**. This branch never starts audio or
  connects to the normal engine.

The final artifact differs by one reviewed setup correction: a driver installer
returning Windows restart code `1641` now suppresses application launch even if
post-install detection has not yet confirmed the new driver. Code `3010` was
already handled. The complete installer sequence was **not rerun after this
correction**, following the maintainer's request to finish building/uploading and defer
further testing. The preceding results are not an exact-artifact end-to-end pass
for the final checksum above.

Driver preparation separately passed 21 boundary checks, including altered
archives/files, missing offline caches and unsafe extraction paths. Fresh
official downloads, cached reuse and PowerShell 5.1 extraction were checked.
Read-only detection identified the actual existing VB-CABLE and VAC devices;
helper fixtures cover missing, disabled, staged and failed detection states.

The wizard was visually reviewed before the final correction: mandatory terms,
destination, detected driver versions, disabled duplicate-install choices and
summary were checked. Long summary text was subsequently changed to wrap;
visual rechecking of that last layout change remains pending.

Local detailed evidence remains in ignored directories:
`artifacts/installer-verification-20261020-final/` and
`artifacts/installer-driver-preparation/`. Raw machine-state snapshots and saved
settings are intentionally not uploaded.

## Remaining qualification

The isolated verifier uses a distinct test identity, disables shortcuts and
normal launch, and never executes driver installers. It does not establish:

- Fresh driver installation, vendor cancellation, elevation, restart completion
  or live audio routing on a clean Windows machine.
- Production Start menu/desktop shortcut creation, normal first launch or cleanup
  of this installed copy's actual startup entry.
- Real-world repair/upgrade across future released versions or interrupted power.
- Publisher signing, SmartScreen reputation or long-duration audio reliability.

Original driver signatures do not sign or certify Vine Mix. The current bundle
preserves vendor packages and terms; monetised distribution still requires a
separate review. See [distribution conditions](DISTRIBUTION.md). Automatic online
updates and journalled rollback remain future work, not installer features.

Vine Mix was left running during automated verification. The maintainer subsequently
quit it manually; a read-only process check confirmed that both desktop and
engine had stopped. No normal installation was made at the default destination.
