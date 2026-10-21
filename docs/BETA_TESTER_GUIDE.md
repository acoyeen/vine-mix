# VineMix beta test guide

For **0.3.0-beta.1**, displayed as **0.3.0 · Beta 1**. Updated 21 October 2026.
This is a private test build for **Windows 11 x64**, supplied directly by the maintainer.
Release qualification remains in progress. Do not treat a generated package as a
qualified release until the [beta qualification record](BETA_IMPLEMENTATION.md)
records its checks.

This guide describes the supplied Beta 1 files. Their application changes are
still being integrated into the source on GitHub; the current checkout does not
yet reproduce this beta. See [source status](../README.md#source-status).

## Choose a package

| File supplied by the maintainer | How to use it |
| --- | --- |
| `VineMix-0.3.0-beta.1-Setup-x64.exe` | Installs the app and shortcuts for your account; can offer missing optional drivers. |
| `VineMix-0.3.0-beta.1-Standalone-x64.exe` | Open the supplied EXE in File Explorer. Its bundled app files are prepared automatically; drivers are installed separately. |

You do not need to install .NET. The standalone EXE includes the app and runtime;
it does not register an app installation. Setup installs under
`%LOCALAPPDATA%\Programs\VineMix`.

The standalone EXE keeps its bundled engine, supporting files and notices in
`%LOCALAPPDATA%\VineMix\runtime\<payload-SHA-256>`. First launch validates and
prepares that version's files; later launches validate and reuse them. A damaged
cache is preserved in an `.invalid-…` folder while a replacement is prepared.
Quit every VineMix copy before repairing or removing its cache.

The current app, helper and Setup are unsigned. Windows may show an unknown
publisher warning. Verify the exact file came from the maintainer and compare its SHA-256
with the checksum supplied with that build before deciding to run it. If the file
or warning differs from what you were sent, stop and report it. A vendor's signed
driver installer does not establish the publisher of VineMix.

### Install with Setup

1. If the files arrived in a ZIP, use **Extract all** in File Explorer first.
   Compare `VineMix-0.3.0-beta.1-Setup-x64.exe` with the supplied checksum.
2. Open that Setup file in File Explorer and follow its installation steps.
   Review the app terms and any separate optional driver terms before accepting.
3. Install only the optional drivers you need. Follow any vendor restart request,
   then open **VineMix** from its Start menu shortcut.
4. Continue with [First session](#first-session) below.

### Use the standalone EXE

1. If the files arrived in a ZIP, use **Extract all** first. Keep
   `VineMix-0.3.0-beta.1-Standalone-x64.exe` in a folder where you intend to keep
   launching it, and compare it with the supplied checksum.
2. Install optional virtual audio drivers separately only if you need virtual
   routes; see [Optional drivers](#optional-drivers).
3. Open the standalone EXE in File Explorer. The first launch prepares its bundled
   runtime automatically; keep the EXE as your launcher for future sessions.
4. Continue with [First session](#first-session) below.

## Optional drivers

A physical microphone and listening output work without either driver. Use the
official vendors' information and packages:

| You want to… | Driver and endpoint names |
| --- | --- |
| Receive audio sent to a virtual playback device | [VB-CABLE](https://vb-audio.com/Cable/): send from the other app to **CABLE Input (VB-Audio Virtual Cable)**; VineMix captures **CABLE Output**. |
| Give another app the mixed audio as a microphone | [Virtual Audio Cable](https://vac.muzychenko.net/en/download.htm): VineMix sends to **Line 1 (Virtual Audio Cable)** playback; select **Line 1** recording in the other app. |

| Driver | Official Windows ZIP | Vendor information |
| --- | --- | --- |
| VB-CABLE | [VBCABLE Driver Pack 45](https://download.vb-audio.com/Download_CABLE/VBCABLE_Driver_Pack45.zip) | [VB-CABLE installation and information](https://vb-audio.com/Cable/) |
| VAC Lite | [VAC 4.71 Lite](https://software.muzychenko.net/freeware/vac471lite.zip) | [VAC editions and downloads](https://vac.muzychenko.net/en/download.htm) |

Extract a downloaded driver ZIP into its own folder before running its vendor
setup. Follow the included vendor instructions and keep the package files together.

Setup offers original driver installers only when it detects the corresponding
package is missing. A disabled or unplugged endpoint is not a reason to install a
duplicate driver. Driver installation requires separate acceptance and may need
administrator permission and a restart. Follow the vendor instructions.

VAC **Lite** is allowed only in a private home environment without income
generation. A work, commercial or income-generating setup needs an appropriate
licence; do not assume the free Lite package permits it. Review
[VAC's licence](https://vac.muzychenko.net/en/license.txt) and
[VB-Audio's licensing terms](https://vb-audio.com/Services/licensing.htm).
The trial VAC package is different from Lite and adds a voice reminder after
30 minutes. Vendor information was checked on **21 October 2026**.

## First session

![Fresh starter with physical devices unassigned and no available VAC output](images/first-run.png)

This fresh-workspace example has no assigned microphone or headphones and no
available VAC output. The red **Line 1** warning clears when an eligible virtual
output is available; physical microphone and headphone routes can still be used
without it. Both outputs are muted.

1. Open VineMix from its installed shortcut or through File Explorer. Do not use
   a developer render or verification executable as your personal mixer.
2. The fresh workspace has exactly three nodes: **Physical input**, **Virtual
   output**, and **Listening output**. Two cables connect the input to the outputs.
   Your physical devices are unassigned. Both outputs are muted, with Listening
   output at −24 dB.
3. Choose the microphone and headphones explicitly. Use headphones at a low
   volume, then unmute Listening output and increase it gradually. Mute again
   before changing a route you are unsure about.
4. If you need a virtual microphone, confirm Virtual output shows the intended
   Line 1 device, select Line 1 recording in the receiving app, then unmute that
   output. Use the receiving app's test or recording function to check it.
5. Make one harmless change, wait for **Saved**, quit from the tray and reopen
   from the same shortcut. Confirm the assignment, level, mute and layout return.

![A configured example starter, with both outputs still muted](images/workspace.png)

The configured example uses **Example USB Microphone** and **Example Headphones**;
your device names will differ. Both images are detached renders of actual app
controls with synthetic devices. They do not access audio hardware or load a
personal workspace.

Closing the main window hides it in the tray and leaves audio running.
**Quit VineMix** in the tray menu stops the desktop and engine. Standalone and
installed copies share one workspace and session; opening another copy should
bring the current app forward instead of starting a competing engine.

## Updating and uninstalling

There is no automatic update service for this beta. Wait for a replacement file
from the maintainer. Before updating, wait for Saved and use **Quit VineMix** from the
tray; closing the window alone leaves it running.

For Setup, run the newer supplied installer. For standalone, open the newer
supplied EXE after quitting the old copy. Do not mix extracted app files from
different builds. Keep the old EXE until the new one restores your workspace
correctly. An older build may not understand
newer saved data; ask the maintainer before attempting a downgrade.

Setup's uninstall entry removes application files and shortcuts while retaining
personal settings, backups and logs. Standalone removal consists of quitting and
deleting its downloaded EXE. Its extracted runtime cache can be removed separately
by deleting `%LOCALAPPDATA%\VineMix\runtime` after all copies have quit; there is
no automatic cache uninstaller. Keep the rest of the VineMix data folder if you
want to retain the workspace. Both packages leave shared audio drivers in place. Remove a
driver only with its vendor's uninstall instructions after
checking that other apps do not need it. Retained personal data remains in
`%LOCALAPPDATA%\VineMix`.

## Recovery

VineMix stores the workspace in `%LOCALAPPDATA%\VineMix\settings.json`, its
last-good backup in `settings.json.bak`, and interface preferences in `ui.json`.
Logs in that folder can help explain a failure. Back up the whole data folder
before manual recovery, with the app quit.

On first use of this renamed build, existing `AudioMixer` data is copied into
`VineMix`; the original files are retained. If different legacy copies exist,
the app asks which workspace to keep using. A workspace already established in
VineMix takes precedence. Interrupted migration resumes using its saved journal;
do not delete migration files while investigating a problem.

An unreadable, damaged or missing established workspace opens **Workspace
recovery** before normal editing or audio starts:

- **Retry** checks the existing files again after a temporary problem is resolved.
- **Restore backup** uses the available backup and preserves the current files.
- **Start fresh · preserve originals** archives existing files, then creates the
  muted three-node starter. It does not silently replace a failed workspace.

Recovery preserves originals in a timestamped `settings.json.recovery-…` folder
inside the data folder. If the buttons cannot complete recovery, keep those files
and contact the maintainer. Do not delete settings or overwrite a newer workspace with
an older copy to get past the banner.

For a missing audio device, reconnect it, check that Windows sees it, then use
**Settings → Retry assigned devices**. Use **Restart audio engine** only when a
brief interruption is acceptable. Settings and graph changes should survive it.

## Useful beta checks

Test only when an interruption will not disrupt a call, recording or other work.
Start quietly. Record what you actually tried; leave unavailable scenarios as
**Not run**.

- Confirm first-run output mutes, explicit physical assignments and saved state
  after quitting and reopening from the shortcut.
- Listen through each intended output separately, then together; check that
  muting headphones leaves the virtual microphone independent, and vice versa.
- Disconnect and reconnect an assigned device; confirm its warning clears and
  the intended route returns without a different device being chosen.
- Check a normal Windows sleep and resume, and an ordinary restart when practical.
- Use your normal mix for a sustained session; note duration, clicks, missing
  sound, drift, unexpected closure, and CPU/memory changes. The eight-hour gate
  is still pending until recorded with evidence.

Full procedure and known qualification limits: [TESTING.md](../TESTING.md).
Offline tests do not replace these listening and hardware checks.

## Report a problem

Send the report through the private channel the maintainer used to supply the build, or
use the repository issue form if you have access. Include:

- **Settings → About VineMix → Copy version information**, plus your Windows
  version and whether this is Setup or standalone.
- What you were doing, numbered reproduction steps, expected result and actual
  result; whether sound stopped and which output was affected.
- Whether the problem followed an update, device reconnect, sleep or restart;
  the approximate time and timezone; frequency and any reliable workaround.
- A cropped screenshot and only the relevant log lines, after reviewing them.

Device names and identifiers, app names, executable paths, account paths and
workspace files may be private. Remove those before sharing. A complete settings
file is not required for the first report. Keep original logs locally if the maintainer
needs a private follow-up. Do not describe a workaround as a verified fix.
