# VineMix installation and beta guide

For **0.3.0-beta.2**, displayed as **0.3.0 · Beta 2**, on Windows 11 x64.
Updated 22 October 2026. Read the [release notes](https://github.com/acoyeen/vine-mix/releases/tag/v0.3.0-beta.2)
for changes and download the format you prefer.

## Choose a package

| Download | How it works |
| --- | --- |
| [VineMix-0.3.0-beta.2-Setup-x64.exe](https://github.com/acoyeen/vine-mix/releases/download/v0.3.0-beta.2/VineMix-0.3.0-beta.2-Setup-x64.exe) | Installs the app and shortcuts for your account; optional virtual audio drivers are separate choices. |
| [VineMix-0.3.0-beta.2-Standalone-x64.exe](https://github.com/acoyeen/vine-mix/releases/download/v0.3.0-beta.2/VineMix-0.3.0-beta.2-Standalone-x64.exe) | Launches from one EXE without registering an app installation; virtual audio drivers are installed separately. |

Both include .NET. Setup installs under `%LOCALAPPDATA%\Programs\VineMix`.
Standalone prepares its verified bundled runtime under
`%LOCALAPPDATA%\VineMix\runtime\<payload-SHA-256>` and reuses it on later launches.
Keep the downloaded EXE as your standalone launcher.

The app, helper and Setup are currently unsigned. Windows may show an
unknown-publisher warning. Verify the release filename and checksum before
running it. The signed update metadata and vendor driver signatures do not
provide a Windows Authenticode signature for VineMix.

### Install with Setup

1. Download Setup from the release page. If using a ZIP bundle, choose
   **Extract all** in File Explorer before launching its contents.
2. Open `VineMix-0.3.0-beta.2-Setup-x64.exe` and follow the installation steps.
3. Use the app-only installation unless you need a virtual cable route. Both
   optional driver boxes start unchecked. Selected drivers have separate terms,
   administrator prompts and possible restart requests.
4. Open **VineMix** from the Start menu and continue with
   [First session](#first-session).

### Use the standalone EXE

1. Download `VineMix-0.3.0-beta.2-Standalone-x64.exe` and keep it in the folder
   where you intend to launch it. Extract any ZIP bundle first.
2. Install optional virtual audio drivers separately if your routes need them.
3. Open the EXE in File Explorer. First launch prepares the bundled runtime
   automatically; continue with [First session](#first-session).

## Optional drivers

Physical microphone and headphone routes work without virtual cable drivers.
An existing streamed microphone, such as Virtual Desktop's microphone, should be
selected in **Physical input** and does not need either bundled cable.

| You want to… | Driver and endpoint names |
| --- | --- |
| Receive another app's virtual playback | [VB-CABLE](https://vb-audio.com/Cable/): the app plays to **CABLE Input (VB-Audio Virtual Cable)**; VineMix captures **CABLE Output**. |
| Give another app the mix as a microphone | [Virtual Audio Cable](https://vac.muzychenko.net/en/download.htm): VineMix sends to **Line 1 (Virtual Audio Cable)** playback; the other app records from **Line 1**. |

| Driver | Official Windows ZIP | Vendor information |
| --- | --- | --- |
| VB-CABLE | [Driver Pack 45](https://download.vb-audio.com/Download_CABLE/VBCABLE_Driver_Pack45.zip) | [VB-CABLE installation and information](https://vb-audio.com/Cable/) |
| VAC Lite | [VAC 4.71 Lite](https://software.muzychenko.net/freeware/vac471lite.zip) | [VAC editions and downloads](https://vac.muzychenko.net/en/download.htm) |

Extract each driver ZIP into its own folder, keep its files together and follow
the vendor setup instructions. A disabled or unplugged endpoint is not a reason
to install a duplicate driver.

**VAC Lite permits private home use without income generation.** Other uses
need an appropriate licence. Review [VAC's licence](https://vac.muzychenko.net/en/license.txt)
and [VB-Audio's licensing terms](https://vb-audio.com/Services/licensing.htm).
The VAC trial is different from Lite and adds a voice reminder after 30 minutes.
Vendor links and terms were checked on 21 October 2026.

## First session

![Fresh starter with physical devices unassigned and no available VAC output](images/first-run.png)

The fresh workspace has **Physical input** connected separately to **Virtual
output** and **Listening output**. Physical devices are unassigned and both
outputs are muted. This example has no available VAC output, so **Line 1** shows
a warning; physical listening can still be configured.

1. Choose your microphone in Physical input and headphones in Listening output.
2. Begin at low headphone volume. Unmute Listening output, which starts at
   **−24 dB**, and raise it gradually. Headphones help avoid microphone feedback.
3. For a virtual microphone, confirm Virtual output shows the intended Line 1
   device. Select Line 1 recording in the receiving app, then unmute Virtual
   output and use that app's recording test.
4. Make a small edit, wait for **Saved**, use **Quit VineMix** from the tray and
   reopen. Confirm the assignments, levels, mutes and layout return.

![Configured starter with example devices and both outputs muted](images/workspace.png)

The images use real app controls with synthetic devices. “Example USB Microphone”
and “Example Headphones” are placeholders; no personal workspace or hardware is
loaded to produce them.

Closing the window hides it in the tray and leaves audio running. **Quit
VineMix** stops the desktop and engine. Installed and standalone copies share a
workspace and coordinate one session; opening another copy brings the active app
forward.

## Updates

1. Open **Settings → Check for updates**, or use the button in About.
2. Review the release notes and choose **Download update**. VineMix verifies
   signed release metadata and the package size and SHA-256 hash.
3. Choose **Install and restart** when you can briefly interrupt audio. The app
   saves pending edits, updates its files and reopens the saved workspace.

Daily checks can be turned on or off with **Automatically check for updates**
in Settings. A check never downloads or installs silently. You can keep using
audio while downloading; installation is a separate action.

**Beta 1 users:** make the first upgrade manually from
[GitHub Releases](https://github.com/acoyeen/vine-mix/releases). Wait for Saved,
quit from the tray, then run the newer Setup or open the newer standalone EXE.
Do not combine files from different builds or replace an app while it is running.

If an update needs manual repair, keep its journal and backups and report the
message shown. The updater does not replace newer settings with an older backup.
See [update behavior and recovery limits](INSTALLATION_AND_UPDATES.md).

## Recovery

Workspace settings live in `%LOCALAPPDATA%\VineMix\settings.json`, the last-good
backup in `settings.json.bak`, and interface preferences in `ui.json`. Back up
the whole data folder with the app quit before attempting manual recovery.

Legacy AudioMixer data is copied while retaining its originals. If different
legacy stores exist, choose the workspace you intend to keep. An established
VineMix workspace takes precedence.

An unreadable, damaged or missing established workspace opens **Workspace
recovery** before normal editing or audio starts:

- **Retry** checks the existing files again after a temporary issue is resolved.
- **Restore backup** preserves current files before using the available backup.
- **Start fresh · preserve originals** archives existing data and creates a muted
  starter.

Preserved originals are kept in a timestamped `settings.json.recovery-…` folder.
Keep these files if recovery cannot complete. Do not overwrite a newer workspace
with an older copy to bypass the banner.

For a missing device, reconnect it, check Windows sound settings and use
**Settings → Retry assigned devices**. **Restart audio engine** causes a brief
interruption while reconnecting saved routes.

## Uninstalling

Setup's uninstall entry removes app files and shortcuts while retaining settings,
backups, logs and shared drivers. For standalone, quit and delete the launcher
EXE. Its runtime cache can be removed separately from
`%LOCALAPPDATA%\VineMix\runtime` once every copy is closed. Keep the rest of the
data folder to retain your workspace.

Remove shared drivers only through their vendor instructions after checking that
other apps do not need them.

## Useful beta checks

Test first-run mutes, workspace retention after Quit/reopen, independent outputs,
device reconnect and sleep/resume. For Virtual Desktop, check microphone
enumeration, recording and saved assignment on the actual headset setup.
Observe resizing and Add Node help at your normal display scale.

Record duration, glitches, unexpected exits and CPU/memory changes during longer
sessions. Full hardware and end-to-end update checks are documented in
[TESTING.md](../TESTING.md). Mark scenarios you could not perform **Not run**.

## Report a problem

Use the [bug report form](https://github.com/acoyeen/vine-mix/issues/new/choose).
Include:

- **Settings → About VineMix → Copy version information**, Windows version and
  whether you use Setup or standalone.
- Numbered reproduction steps, expected and actual results, and whether audio
  stopped or only one output was affected.
- Frequency, recent update/reconnect/sleep, approximate failure time and any
  reliable workaround.
- A cropped screenshot and relevant log lines after reviewing them.

Remove personal device identifiers, app names, executable/account paths and
workspace data before sharing diagnostics. A complete settings file is not
required for the first report. Keep original evidence locally for follow-up.
