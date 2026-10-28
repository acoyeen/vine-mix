# Driver distribution and integration review

## 21 October 2026 — original review

Research date: 2026-10-21. Build preparation only; driver installers were not run.

VB-CABLE Pack45 uses the official base-cable ZIP linked from [the vendor product page](https://vb-audio.com/Cable/), downloaded from https://download.vb-audio.com/Download_CABLE/VBCABLE_Driver_Pack45.zip. The 1,318,877-byte ZIP SHA-256 is `B950E39F01AF1D04EA623C8F6D8EB9B6EA5C477C637295FABF20631C85116BFB`. Its original readme permits redistribution unchanged but states, “It is not allowed to integrate the VB-CABLE package in another software installation procedure without Author agreement.” The [current licensing page](https://vb-audio.com/Services/licensing.htm) also distinguishes distribution and professional licensing. Permission evidence belongs in the private qualification record, not in a public release claim. Preserve the complete original ZIP, vendor origin, donation notice and interactive vendor terms.

[VAC's distribution policy](https://vac.muzychenko.net/en/manual/distrib.htm) permits unchanged Lite and Trial packages for noncommercial distribution. Its [March 10, 2026 licence](https://vac.muzychenko.net/en/license.txt) limits Lite usage to private homes unrelated to income generation. Use only the official Lite download linked by [the vendor download page](https://vac.muzychenko.net/en/download.htm); retain the original package, structure, licence and edition. The Lite ZIP is 1,385,091 bytes with SHA-256 `1ED42A7E763BF6237438FB62191AE88B2922F014B37658B29D7801D72C9D5357`. Archive size, SHA-256 and complete file allowlists remain pinned in `installer/drivers.json` and are verified during preparation, including expected Authenticode publishers.

These are separate proprietary components. The application MIT licence does not cover them. The maintainer attested on 21 October 2026 that the required VB-Audio integration permission was obtained; the agreement itself was not independently reviewed. Versioned beta releases retain the original vendor packages and notices. This record does not grant permission for a different or paid distribution. Existing drivers are preserved and vendor installation remains optional and interactive.

## 27 October 2026 — selection, installation order and integration

The maintainer requested selected-by-default driver installation for VineMix's
Virtual input and Virtual output features, clearer prerequisite wording, and
VAC Lite before VB-CABLE. This supersedes the earlier unchecked-default policy;
separate acceptance of the selected vendors' terms remains required. Existing
driver installations remain protected from automatic reinstallation. VAC Lite's
private-home/non-income restriction and VB-Audio's origin and donation notices
remain visible.

Both complete original ZIPs are already embedded in VineMix Setup. The proposed
further integration would replace separate vendor setup windows with a unified
installation flow; it is not a request to bundle previously absent files.

The [current VB-Audio licensing page](https://vb-audio.com/Services/licensing.htm)
explicitly permits embedding the base VB-CABLE package, including silent
installation, when its donationware conditions are met. The existing maintainer
permission remains recorded above; no renewed permission is inferred to be
necessary. However, neither the pinned Pack45 readme nor the
[official reference manual](https://vb-audio.com/Cable/VBCABLE_ReferenceManual.pdf)
documents a supported unattended command, exit-code contract or restart
suppression method. Public third-party command examples were not adopted as
verified instructions for the pinned package.

For VAC Lite 4.71, the pinned readme, setup configuration and extracted bundled
manual, together with the [current installation guide](https://vac.muzychenko.net/en/manual/install.htm),
do not document an unattended installation command. In a
[firsthand vendor comment](https://stackoverflow.com/questions/58463743/is-there-any-way-to-install-virtual-audio-cable-application-without-prompt-messa),
Eugene Muzychenko offers a silent installer on receipt of order information.
That establishes a vendor-supported route in principle, not a current command
or package entitlement for this Lite bundle. An older third-party invocation
reported for 4.64 was not treated as support for 4.71.

The vendor guides support the requested order: VAC normally works immediately
after successful installation, although replacement of in-use system files can
require a restart; VB-CABLE requires a restart. Setup should run VAC first and
VB-CABLE last, and tell users to finish VineMix Setup before restarting.

Direct native-driver installation was not implemented. Microsoft's
[PnPUtil documentation](https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/pnputil-command-syntax)
describes installation onto matching devices; creating a root-enumerated device
is a [separate operation](https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/devcon-install).
That alone would not reproduce the vendors' utility, uninstaller and other
setup work. A unified flow remains feasible work once current vendor-supported
invocations and completion/restart behaviour can be verified on a clean Windows
target. This review ran no vendor installer and changed no installed driver.
