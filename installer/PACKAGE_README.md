# Vine Mix

A local Windows audio mixer with a node workspace for microphones, applications,
effects, listening devices and virtual microphone output.

Open **Vine Mix** from the Start menu, or run `AudioMixer.exe` in this folder.
Keep the application files together. Closing the window hides the app to the
tray; **Quit Vine Mix** in its tray menu stops the app and audio engine.

- [User guide](docs/USER_GUIDE.md)
- [Installation, driver and uninstall help](SetupSupport/INSTALLATION.txt)
- [Installation terms](SetupSupport/TERMS.txt)
- [Third-party credits](THIRD_PARTY_NOTICES.md)
- [Application licence](LICENSE)

Saved workspaces, preferences, backups and logs remain under
`%LOCALAPPDATA%\AudioMixer`, independently of this program folder. Existing portable
and installed copies share that data and one audio engine per Windows user.
Quit an older running copy before opening this one.

The installer can offer original VB-CABLE and VAC Lite packages if missing.
Their vendor terms are separate from Vine Mix's MIT licence. VAC Lite is limited
to private home use unrelated to income generation. Details and vendor links
are retained in [the driver notices](SetupSupport/DRIVER-NOTICES.txt).

Vine Mix is experimental. New outputs start muted; check routing, levels and
device selection before relying on a setup. The current development installer
and application are unsigned. Historical technical verification links in the
extended guides may refer to evidence available only in the source checkout.
