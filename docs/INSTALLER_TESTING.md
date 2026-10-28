# Testing the VineMix beta on a fresh Windows machine

Updated 23 October 2026 for **0.3.0-beta.3**. These are instructions, not a record
of completed tests. Use Windows 11 x64 on a spare PC or VM with no existing
VB-CABLE or VAC package for the clean-driver cases. The current app and packages
are not Windows Authenticode signed. Get the exact versioned files and SHA-256
checksums from the matching [GitHub release](https://github.com/acoyeen/vine-mix/releases).
See the [qualification record](BETA_TESTING_CURRENT.md) for completed checks.

The next beta's installer candidate changes missing-driver defaults and order:
test the checked defaults, explicit vendor consent and VAC-before-VB-CABLE flow
below against that candidate. Published Beta 5 retains its original defaults.

## Setup

1. Check the supplied `VineMix-0.3.0-beta.3-Setup-x64.exe` against the checksum
   supplied for that exact file. Record the checksum and Windows build.
2. Open Setup. Confirm terms must be accepted and the wizard text fits. Use its
   default per-user application folder unless deliberately testing another one.
3. Record how its driver page classifies VB-CABLE and VAC. On a genuinely clean
   machine both should be missing and checked by default. VB-CABLE enables
   Virtual input; a compatible VAC driver enables Virtual output. Read the
   separate vendor terms; consent must remain unchecked. VAC Lite is
   restricted to private home use without income generation; select it only if
   that applies. Both drivers can be skipped.
4. With both selected, confirm the original VAC Lite installer opens first,
   followed by VB-CABLE, with required administrator prompts. If asked to
   restart, defer it until VineMix Setup finishes. Record each driver's result.
   Successful VAC installation normally needs no restart; VB-CABLE does.
5. Restart Windows when required. Confirm Setup does not launch VineMix early
   after a driver-triggered restart. Open the Start menu or desktop shortcut.
6. Confirm the three-node starter, both outputs muted, physical devices
   unassigned and Listening output at −24 dB. Follow the
   [beta guide](BETA_TESTER_GUIDE.md) to assign headphones and test quietly.

Application files should be in `%LOCALAPPDATA%\Programs\VineMix`; personal data
belongs in `%LOCALAPPDATA%\VineMix`. Installed support text and licence notices
must be present and readable. Neither the verification executable nor detached
render/test modes belong in the release payload.

## Standalone

Use a separate clean-user test with `VineMix-0.3.0-beta.3-Standalone-x64.exe`.
Check its supplied checksum, open it in File Explorer and confirm it prepares
its bundled files and starts without installing .NET or registering an app
uninstall entry. Confirm a useful error for unavailable/unwritable extraction
storage. Test physical routing without virtual drivers, then optional virtual
routes after using official vendor installers. Inspect the extracted payload's
notice files. Do not mix files from different builds.

## Record each case

- [ ] Setup and standalone open from Explorer and retain saved assignments,
      levels, mutes, layout and preferences after full quit and reopen.
- [ ] Fresh state has exactly the three starter nodes and two audio cables.
- [ ] Missing drivers can be skipped; physical routes remain usable.
- [ ] Missing eligible drivers start checked, existing/uncertain packages are
      disabled, and returning to the page preserves a user's cleared choice.
- [ ] Driver consent is unchecked initially and clears when selection changes.
- [ ] VAC Lite runs before VB-CABLE; restart guidance waits for Setup completion.
- [ ] Original driver installation, vendor cancellation and required restart
      each produce an understandable result.
- [ ] Re-running Setup recognises existing/disabled/staged drivers and avoids a
      duplicate installation.
- [ ] Start menu, optional desktop shortcut and Windows Installed apps entry work.
- [ ] App-to-VineMix VB-CABLE and VineMix-to-recording-app VAC routing work.
- [ ] Opening another installed/standalone copy activates the current session
      instead of creating a competing desktop or engine.
- [ ] Same-version repair and newer-version upgrade preserve saved state.
- [ ] Replacement and uninstall refuse a target in use; tray Quit enables retry.
- [ ] Uninstall removes owned files/shortcuts while retaining personal data,
      unknown user files and shared drivers; reinstall uses retained state.
- [ ] Legacy AudioMixer data is copied without deleting originals; conflict
      selection and an interrupted migration recover predictably.
- [ ] Recovery offers Retry/Restore/Start fresh for damaged or missing established
      settings and preserves originals before changing them.
- [ ] Device reconnect, sleep/resume and eight-hour audio use are recorded with
      actual observations, including any audible gap or closure.

Test skipped drivers and cancelled vendor setup separately from success. Do not
remove working drivers from a daily-use computer for a clean-machine case.
An isolated installer policy test is different from a real driver installation.

Record exact app version from **Settings → About VineMix**, package checksum,
Windows version, wizard step, driver result and restart status. Setup accepts
`/LOG="<path-to-a-log-file>"`; logs can contain personal paths. Review them before
sharing. Current evidence belongs in [the beta record](BETA_IMPLEMENTATION.md);
[the earlier installer report](INSTALLER_VERIFICATION.md) covers 0.2.0 only.
