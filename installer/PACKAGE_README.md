# VineMix

A local Windows audio mixer with a node workspace for microphones, applications,
effects, listening devices and virtual microphone output.

Open **VineMix** from the Start menu, or run `VineMix.exe` in this folder.
Keep the application files together. Closing the window hides the app to the
tray; **Quit VineMix** in its tray menu stops the app and audio engine.

- [Installation, driver and uninstall help](SetupSupport/INSTALLATION.txt)
- [Installation terms](SetupSupport/TERMS.txt)
- [Third-party credits](THIRD_PARTY_NOTICES.md)
- [Application licence](LICENSE)

Saved workspaces, preferences, backups and logs remain under
`%LOCALAPPDATA%\VineMix`, independently of this program folder. VineMix copies
share that data and one audio engine per Windows user. Existing AudioMixer
workspaces are copied during migration when needed, and the originals are kept.
Quit an older running copy before opening this one.

For a first setup, install VineMix with both optional driver boxes clear. Choose
your Windows microphone on **Physical input** and headphones on **Listening
output**. New outputs start muted; check the device and level before unmuting.
For a **Virtual Desktop microphone**, connect the headset, enable **Microphone
passthrough**, confirm it appears in **Windows Settings > System > Sound >
Input**, then select that device on **Physical input**. Its starter name does not
limit the picker to physical microphones.

The installer can offer original VB-CABLE and VAC Lite packages if missing;
neither is required for an existing microphone or Virtual Desktop input.
**VB-CABLE** brings other apps' audio into VineMix's **Virtual input**.
**VAC Lite** sends **Virtual output** to another app as its microphone.
Both start unchecked and require separate consent. You can run Setup later
when you need either feature.
Their vendor terms are separate from VineMix's MIT licence. VAC Lite is limited
to private home use unrelated to income generation. Details and vendor links
are retained in [the driver notices](SetupSupport/DRIVER-NOTICES.txt).

VineMix is experimental. New outputs start muted; check routing, levels and
device selection before relying on a setup. The current development installer
and application are unsigned.
