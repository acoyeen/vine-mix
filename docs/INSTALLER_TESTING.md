# Testing the Vine Mix installer on a fresh Windows machine

Prepared 20 October 2026 for testing on 21 October. Use Windows 11 on an x64 PC
with no existing VB-CABLE or VAC installation. This is an unsigned experimental
build. The installer contains the app and both original driver packages offline.

## Download and install

1. Download `VineMix-0.2.0-Setup-x64.exe` and its `.sha256` file from the private
   repository's draft **Vine Mix 0.2.0** release. Repository access is required.
2. Confirm the download's SHA-256 matches the accompanying checksum. In
   PowerShell, `Get-FileHash .\VineMix-0.2.0-Setup-x64.exe -Algorithm SHA256`
   should report
   `AC27DEBEFF500F126D87777DBCA28561C910083387C69D02AD0D10D39BAC0C1E`.
3. Run the installer. Check that Next remains unavailable until its terms are
   accepted. Keep the default destination unless testing another dedicated folder.
4. Choose whether to create a desktop shortcut. The driver page should identify
   both drivers as missing on this clean machine. Read their separate terms.
   VAC Lite permits private home use unrelated to earning income; only select it
   if that applies. Both drivers can be skipped.
5. If selecting missing drivers, accept their terms and continue. Each vendor's
   original installer should open separately and request administrator approval.
   Complete its normal installation. The app itself installs per user.
6. Restart Windows if requested. Open Vine Mix from its Start menu shortcut.
   Outputs initially require device assignment and may be muted; follow the
   installed user guide before judging routing.

Application files should be under `%LOCALAPPDATA%\Programs\Vine Mix`. Workspaces,
preferences, backups and logs should be under `%LOCALAPPDATA%\AudioMixer`.
Installed help is in `SetupSupport\INSTALLATION.txt`; the user guide is
`docs\USER_GUIDE.md` inside the application folder.

## Record these results

- [ ] Terms, all driver labels and the final summary fit the wizard and are clear.
- [ ] Both missing drivers install through their original vendor windows.
- [ ] Cancelling a vendor installer gives a clear result without losing the app.
- [ ] Setup requests a restart after a newly installed driver and does not open
      Vine Mix prematurely.
- [ ] After restarting, both drivers appear in Windows sound devices.
- [ ] Audio from another app reaches Vine Mix through VB-CABLE.
- [ ] Vine Mix audio reaches a recording application through VAC.
- [ ] Start menu, optional desktop shortcut and Windows Installed apps entry work.
- [ ] Re-running setup recognises existing drivers and offers no duplicate install.
- [ ] Re-running the same version repairs app files and preserves a saved workspace.
- [ ] A copy running from the target blocks replacement; quitting through its tray
      allows retry. Closing the window alone only hides it.
- [ ] Uninstall refuses while that installed copy is running.
- [ ] After quitting, uninstall removes application files/shortcuts while retaining
      saved workspaces and both shared audio drivers.
- [ ] Installing again restores access to the retained workspace.

Test skipping drivers and cancelling vendor setup separately from the successful
installation path. Do not remove working drivers from a daily-use computer just
to repeat these cases. Future-version upgrade and interrupted-install recovery
need separate qualification when such versions are available.

If something fails, record the Windows version, exact installer checksum,
wizard step, vendor result, restart status and observed error. To retain a setup
log, start it with `/LOG="<path-to-a-log-file>"`. Logs may contain local paths;
review them before sharing. Keep findings in the private repository until ready
for broader distribution. The [verification report](INSTALLER_VERIFICATION.md)
states exactly which local checks have already run.
