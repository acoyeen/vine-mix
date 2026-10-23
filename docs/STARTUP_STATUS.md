# Startup and driver-installation status

Historical package/evidence paths and descriptions of the running session below
refer to the original test date. Raw records and backups are retained privately
outside this checkout.

> Historical development record. Results and procedures below belong to their
> named versions; they do not qualify the current beta. See
> [current qualification](BETA_TESTING_CURRENT.md) for the active release checks.

Evidence note: raw reports, historical renders and reference captures cited
here are retained privately outside the repository. Their labels below identify
archived evidence, not downloadable files; recorded results apply only to the
versions and checks described.

> Personal hardware names and settings fingerprints are omitted from this public
> record; neutral role labels preserve the observed path distinctions.

> **Historical fixed-console record.** The node workspace supersedes this build.
> Statements below about running processes, VoiceMeeter installation and saved
> assignments describe earlier sessions, not the current machine. VoiceMeeter was
> subsequently removed at the user's request. See [NODE_VERIFICATION.md](NODE_VERIFICATION.md)
> for the node release and [TESTING.md](../TESTING.md) for current qualification.

Date: 17 October 2026, UTC+10.

## Startup check

The desktop window opens, remains responsive, and displays **Audio engine
connected**. The native engine is running separately. The startup check revealed
read-only properties using WPF's default TwoWay binding; meters and Setup text now
use OneWay bindings. Explicit window and selector colors also correct unreadable
default backgrounds.

No physical microphones or outputs were assigned during the initial startup
check. A subsequent authorized setup on the same date is recorded below.
Voicemeeter remains installed and untouched.

## Drivers

The official packages are downloaded and extracted without modification:

| Package | Local installer | Modern catalog signature |
|---|---|---|
| Base VB-CABLE Pack45 | `downloads/drivers/VB-CABLE/VBCABLE_Setup_x64.exe` | Valid — Microsoft Windows Hardware Compatibility Publisher |
| VAC 4.71 Lite | `downloads/drivers/VAC-Lite/setup64.exe` | Valid — Microsoft Windows Hardware Compatibility Publisher |

SHA-256 archives:

```text
VBCABLE_Driver_Pack45.zip
B950E39F01AF1D04EA623C8F6D8EB9B6EA5C477C637295FABF20631C85116BFB

vac471lite.zip
1ED42A7E763BF6237438FB62191AE88B2922F014B37658B29D7801D72C9D5357
```

The initial native Windows installation attempt for both modern INF packages
returned **Access is denied**, exit code **5**, and **zero packages added** from
the non-elevated session. No UAC prompt was triggered and no Windows security
setting was changed during that attempt. This is historical: **the user has since
run both vendor installers**, and Windows now reports both devices healthy.

## First setup — historical saved configuration revision 1

The user authorized setup for an informal first test. These assignments and
controls record revision 1 at that time; the current baseline is revision 3, as
recorded below:

| Role | Installed device / setting |
|---|---|
| Microphone 1 | Physical USB microphone, channel 0; A off, B on |
| Microphone 2 | Unassigned |
| App Audio playback destination | Ordinary VB-CABLE **CABLE Input**, not the 16-channel alternate |
| App Audio engine capture | VB-CABLE **CABLE Output**; A on, B off |
| A1 listening output | Listening output 1 |
| A2 listening output | Unassigned |
| B engine playback / client recording | **Line 1 (Virtual Audio Cable)**, playback and recording partners |
| A master | Muted, -24 dB |
| B master | Unmuted, 0 dB |

The native log reports all four assigned workers—Microphone 1, App Audio, A1, and
B—**Running at 48 kHz**. The desktop shows **Audio engine connected**. This confirms
that the selected streams opened; it does not establish audible audio flow or
audio quality. No tone, application playback, or audio recording was started.

Windows endpoints retain their original names. The unused VB-CABLE 16-channel
playback alternate remains enabled. Voicemeeter, Windows default devices, Spotify,
and Discord have not been changed. Both virtual helper endpoints remain enabled.

## First fixed-size desktop update — historical charcoal build

The first updated controller used the fixed **1120 × 680 DIP** mixing console and
**880 × 600 DIP** Devices dialog. The ordinary interface used neutral charcoal and
the same muted green for A and B. Two send faders per source, illuminated routes
and mutes, input/output meters and both A device slots fit without main-window
scrolling. Devices separates Inputs & outputs, App connections and Troubleshooting.

The updated controller reconnected to the existing native engine **without an
engine restart**. The native engine was not changed. Saved revision 1 remained
byte-for-byte unchanged during that update; A remained muted at −24 dB and
microphone monitoring remained off. The historical settings-file SHA-256 was:

```text
<private-revision-1-settings-sha256-omitted>
```

At that update's read-only UI observation, Listening output A1 showed **Waiting for device**;
its saved name and assignment remained present. Microphone 1, App Audio and B showed
Connected. No hardware change was induced and no route was changed. This is a
historical status observation, not evidence of a controlled loss/reconnection test;
the four-Running result above records the earlier startup.

The self-contained desktop publish completed with zero warnings and errors.
The inert `--verify-ui` harness passed 28 checks, including all 64 route-state
combinations and zero WPF binding errors. It does not connect to the engine,
open devices or write the user's mixer settings. Preview mouse/keyboard checks
also used inert data. No test sound, playback or recording was started during
this update.

See [the design review](../design-qa.md), [UI implementation plan](UI_REDESIGN_PLAN.md)
and [current UI verification procedure](BETA_TESTING_CURRENT.md#automated-checks).
The 125%/150% images simulate render density; actual Windows scaling changes and
full screen-reader speech behavior remain unverified. These UI results do not
qualify the native audio paths or replace the deferred hardware tests.

## First flat slate desktop revision — historical build

The user subsequently requested a lighter, VoiceMeeter-like surface, flat square
controls and integrated window controls. The new build uses one medium slate-grey
canvas (#566168), thin column separators and no rounded strip cards. Minimise and
close are in the app header for both the **1120 × 680 DIP** mixer and **880 × 600 DIP**
Devices dialog, with no separate OS title bars. Extra client height is used for
the faders. Native drag/system-menu behavior and the existing tray/explicit-Quit
meanings are retained. This revision changes presentation, not native audio.

The current saved configuration is **revision 3**, following user changes between
requests. It retains A muted at −24 dB and the existing routes. Its baseline SHA-256
is:

```text
<private-settings-sha256-omitted>
```

The new build must preserve this current baseline; revision 1 and its earlier hash
above are historical records. The existing native engine continues running and
was not rebuilt or restarted for this presentation change. No mixer setting,
Windows default, driver or global display setting was changed by this revision.

The final self-contained desktop publish completed cleanly. The latest inert harness passed
**33 UI checks**, including five added chrome/card assertions, all 64 route-state
combinations and zero WPF binding errors. Its result is
`artifacts/ui-verification/light-console/results.json`, with a retained copy in
docs/design/light-verification/results.json.
Embedded minimise/restore, Devices open/close, preview main-window close and the
caption's right-click system menu were also verified in a silent preview.

The slate controller is now running against the original native engine, whose
process and start time were unchanged. Only the desktop executable was replaced.
The before/after settings hashes match the revision-3 baseline above; A remains
muted at −24 dB. Final client captures measure 1120 × 680 and 880 × 600 pixels at
the current 100% density, without a separate OS title bar.

See [the revised mockup and review](design/LIGHT_CONSOLE_REVIEW.md) and
[design-qa.md](../design-qa.md). Render-density simulations do not establish actual
Windows DPI transitions. Alt+Space did not produce an observed system-menu popup
in this environment and remains unverified; caption right-click access worked.
Native audio, full screen-reader speech behavior and the
deferred hardware/reliability qualification remain unverified. No sound or audio
test was started as part of this revision.

## Control-detail refinement — completed and running

The user requested clearer controls and more physical-console detail. The completed
build keeps the continuous #566168 slate canvas, thin column separators,
fixed dimensions and integrated window controls. Recessed wells now use #081014,
giving about 3.02:1 separation from the chassis, compared with about 1.51:1 for
the previous #39474E well. Square beveled buttons, recessed device nameplates
with chevrons and a full-width #3E4A52 channel-name band improve identification.

Faders have 40 × 24 DIP grooved caps, a 14-DIP outer track with 12-DIP filled
travel, explicit source A/B lane labels, 18-DIP gain readouts and 10-DIP scale
text. Off-route controls retain fully visible grey caps. Gain-position fill is
not audio activity. Active travel uses green #79B99A, distinct from the light
#B9E3CB caps; the caps' small 2-DIP corner radius belongs only to that functional
control, while strip-card outlines remain absent. Incorrect illustrative levels and meter activity in the
generated mockup are not adopted.

The final 18-DIP-readout build passed **37 inert UI checks**, exited successfully
and reported zero WPF binding errors. Results are in
`artifacts/ui-verification/detail-reviewed/results.json`; the retained copy is
docs/design/detail-verification/results.json.
The 33-check installed slate result above is historical evidence for that build.

The detailed slate controller is now running against the original native engine;
the engine process and start time are unchanged. Only the desktop executable was
replaced. The before/after settings hashes are identical, preserving revision 3:
`<private-settings-sha256-omitted>`, A muted at −24 dB
and existing routes retained. A silent-preview fader-thumb/Up check changed A to
+0.5 dB while B remained 0 dB and A routing stayed off; the preview was closed
without applying those edits to the live engine. No new rounded cards, audio features, driver changes,
Windows setting changes or test sounds are part of this refinement. See the
[control-detail review](design/CONTROL_DETAIL_REVIEW.md) and [design-qa.md](../design-qa.md).

## First informal check

1. Start the source application's volume low. In Windows Volume Mixer,
   choose **CABLE Input (VB-Audio Virtual Cable)** for
   Spotify. Leave App Audio B off. Confirm its meter moves in Audio Mixer.
2. Confirm the assigned listening device is the desired output and ready to use,
   then manually unmute A. Its saved master level starts at -24 dB. Raise levels
   gradually while listening.
3. In Discord, explicitly select **Line 1 (Virtual Audio Cable)** as the input
   microphone. Speak into the selected microphone and check the input indicator. Microphone A
   remains off to avoid unexpected live monitoring.
4. Keep Discord's received conversation on a physical output, outside CABLE
   Input, whenever App Audio is sent to B. For this first check App Audio B stays
   off.

Windows **Listen to this device** settings and the exact VAC **16-bit stereo
48 kHz** transport setting still need verification before testing the complete
path. A shared-mode float format or a Running status alone does not verify the
underlying transport bit depth. Leave all four required cable endpoints enabled.

## Reboot and remaining qualification

VB-CABLE requested a reboot after installation. The user does not want to reboot
yet and has agreed to do so before reliability validation. Setup and an informal
first check can proceed with the active endpoints; a successful check before
reboot does not qualify post-reboot operation.

Actual listening and capture, microphone channel mapping, simultaneous consumers,
latency, clock drift, reconnection, and long-running stability remain unqualified.
Use [TESTING.md](../TESTING.md) for the later qualification pass. Do not remove
Voicemeeter or disable the unused alternate until the relevant checks pass.

Official sources:

- https://vb-audio.com/Cable/
- https://vac.muzychenko.net/en/download.htm
- https://vac.muzychenko.net/en/manual/install.htm
