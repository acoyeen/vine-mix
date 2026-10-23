# VineMix beta test guide

For **0.3.0-beta.4**, displayed as **0.3.0 · Beta 4**. Updated 23 October 2026.
VineMix is an MIT-licensed open-source beta for **Windows 11 x64**.
Download a version from [GitHub Releases](https://github.com/acoyeen/vine-mix/releases).
The [current qualification record](BETA_TESTING_CURRENT.md) separates completed
checks from pending scenarios. Use the files and checksums attached to the
matching published release.

## Choose a package

| Download | How to use it |
| --- | --- |
| `VineMix-0.3.0-beta.4-Setup-x64.exe` | Installs the app and shortcuts for your account; can offer missing optional drivers. |
| `VineMix-0.3.0-beta.4-Standalone-x64.exe` | Open the supplied EXE in File Explorer. Its bundled app files are prepared automatically; drivers are installed separately. |

You do not need to install .NET. The standalone EXE includes the app and runtime;
it does not register an app installation. Setup installs under
`%LOCALAPPDATA%\Programs\VineMix`.

The standalone EXE keeps its bundled engine, supporting files and notices in
`%LOCALAPPDATA%\VineMix\runtime\<payload-SHA-256>`. First launch validates and
prepares that version's files; later launches validate and reuse them. A damaged
cache is preserved in an `.invalid-…` folder while a replacement is prepared.
Quit every VineMix copy before repairing or removing its cache.

The current app, helper and Setup are unsigned. Windows may show an unknown
publisher warning. Compare the file's SHA-256
with the checksum supplied with the matching release before deciding to run it. If the file
or warning differs from the matching release information, stop and report it. A vendor's signed
driver installer does not establish the publisher of VineMix.

## Optional drivers

A physical microphone and listening output work without either driver. Use the
official vendors' information and packages:

| You want to… | Driver and endpoint names |
| --- | --- |
| Receive audio sent to a virtual playback device | [VB-CABLE](https://vb-audio.com/Cable/): send from the other app to **CABLE Input (VB-Audio Virtual Cable)**; VineMix captures **CABLE Output**. |
| Give another app the mixed audio as a microphone | [Virtual Audio Cable](https://vac.muzychenko.net/en/download.htm): VineMix sends to **Line 1 (Virtual Audio Cable)** playback; select **Line 1** recording in the other app. |

Start with both driver boxes **unchecked** if you already have your microphone
and headphones available in Windows. For a Virtual Desktop microphone, connect
the headset and enable its microphone passthrough. Confirm the device appears in
**Windows Settings → System → Sound → Input**, then select it on **Physical input**.
That node also accepts virtual and streamed recording devices; the optional
bundled cables are not prerequisites. For streamed headset playback, choose its
Windows playback endpoint in **Listening output**. Actual Virtual Desktop
headset sound, reconnect and sustained-use validation remain pending for Beta 4.

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

These images are detached renders of the actual app controls with synthetic
devices. They do not access audio hardware or load a personal workspace.

Closing the main window hides it in the tray and leaves audio running.
**Quit VineMix** at the bottom of Settings or in the tray menu saves and stops
the desktop and engine. If the final save fails, retry, save a recovery copy,
cancel quitting or explicitly quit without saving. Closing alone never stops audio.
Standalone and
installed copies share one workspace and session; opening another copy should
bring the current app forward instead of starting a competing engine.

## Updating and uninstalling

Open **Settings → Check for updates**. VineMix checks the project's public GitHub
releases and verifies a signed release manifest before offering a download.
If the release service is unavailable, retry later or download the matching package
from the GitHub release page for a manual update.
Beta 1 has no updater button: install or open a newer release manually first. Beta 2 and later can
check for a strictly newer signed release from Settings.

When an update is offered:

1. Choose **Download update**. Audio keeps running while the download is checked.
2. Choose **Install and restart** when a brief audio interruption is acceptable.
   VineMix saves and confirms the workspace before asking the engine to quit.
3. Wait for VineMix to reopen. Editing stays paused until the new app confirms its
   saved workspace and engine are ready. Keep the computer on during this step.

Standalone updates replace the original EXE in place so existing shortcuts keep
working. Its filename may still contain the older version; **About VineMix**
shows the actual running version.

Automatic checking is enabled at most once daily and can be disabled in Settings.
It never downloads or installs automatically. No GitHub account or token is stored
in the app. Update files use a project signing key; the Windows executables remain
unsigned unless the build's supplied signing information says otherwise.

An interrupted update retains its prior application files and private recovery
record under `%LOCALAPPDATA%\VineMix\updates`. Reopening offers recovery when it
can be completed safely. A completed safe rollback reopens the previous version
and explains the failure. If it requests manual repair, keep that folder and use
the trusted installer or contact the maintainer. Do not delete the recovery data
or overwrite workspace files. The updater does not restore an older workspace
over newer edits. If the app or engine cannot quit normally, restart Windows
before retrying recovery when it is safe to interrupt your other work.
The complete delivered-binary update/rollback flow remains a qualification gate.

For a manual update, wait for **Saved** and use **Quit VineMix** from the tray;
closing the window alone leaves it running.

You do not need to uninstall VineMix to update it. For Setup, run the newer
downloaded installer; it preserves the saved workspace and existing installation
location. Running an old installer reinstalls that version, rather than fetching
a newer one. Keep working optional drivers installed and leave their Setup boxes
unchecked. For standalone, open the newer
downloaded EXE after quitting the old copy. Do not mix extracted app files from
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
- Check that a Virtual Desktop microphone is listed, can capture sound, retains
  its assignment after reopening and reconnects after the headset reconnects.
- Repeatedly resize, maximise and restore the window at your usual display scale.
  Report any header/footer trails; detached render checks do not prove this symptom
  is fixed on a physical display.
- Hover and keyboard-focus the Add menu choices. Each choice should explain its
  purpose, including when a node limit or unavailable engine disables it.

Full procedure and known qualification limits: [TESTING.md](../TESTING.md).
Offline tests do not replace these listening and hardware checks.

## Report a problem

Use the [repository issue form](https://github.com/acoyeen/vine-mix/issues/new/choose).
Include:

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
