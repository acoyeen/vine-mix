# VineMix known issues

Reported defects, known limitations and their verification status are recorded below.

<a id="issue-061"></a>
## ISSUE-061 · The 0.6.0 standalone updater can stop before replacing its launcher

**Fixed in 0.6.2 and verified on Windows 11, 20 November 2026.** The released 0.6.0 standalone client discovered and downloaded the signed 0.6.1 update. After **Install and restart**, its worker reported “The standalone application could not be replaced” and requested manual repair. The original launcher kept its 0.6.0 hash. The workspace on disk matched the saved handoff checkpoint.

The expected result is a restart into the verified 0.6.1 launcher. The failure reproduced twice. After the second failure, replacing the launcher and choosing **Yes** to resume recovery started 0.6.1. The engine health check passed and the saved workspace and preferences matched. Follow the [standalone update instructions](INSTALLATION_AND_UPDATES.md#upgrading-from-060-to-061). The affected launcher inherited the Windows Read-only attribute from the test media. Windows returned access denied (5) while replacing a read-only file. Clearing that single attribute let the unmodified 0.6.0 client complete the signed update from inside VineMix. Version 0.6.2 handles this attribute in the original worker during future updates and records the Windows error on other failures. See [updating earlier standalone versions](INSTALLATION_AND_UPDATES.md#standalone-updates-from-060-or-061).

<a id="issue-056"></a>
## ISSUE-056 · Website release discovery can replace the saved download with an older version

**Initial report · Reproduced in isolation on 18 November 2026.** The published 0.6.0
download controller received a successful feed containing only the genuine
0.5.0 Beta record. It replaced the saved 0.6.0 installer and labelled the
older release “Latest Release”. The live GitHub response selected 0.6.0.
Routine refreshes should preserve a newer saved or verified release. Add a
version-floor regression test and define how an intentional withdrawal is handled.
[Website audit](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/website-redesign/AUDIT_2026-11-18.md)

**Resolved on the public site · 19 November 2026.** Stable downloads preserve the saved or last verified release floor. Older, equal, newer, incomplete and prerelease cases passed the release checks.

<a id="issue-057"></a>
## ISSUE-057 · Website driver download names differ from their visible labels

**Initial report · Confirmed on the public site on 18 November 2026.** The visible labels
“VB-CABLE ZIP” and “VAC Lite ZIP” do not occur intact in the corresponding
accessible names. Lighthouse flagged both links. Speech-input users should
be able to use the words on screen. Start each name with its visible label
and verify both commands with speech input. [Website audit](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/website-redesign/AUDIT_2026-11-18.md)

**Resolved on the public site · 19 November 2026.** Both ZIP links now begin with their visible labels. The published page passed the label-in-name check. OS speech input remains untested.

<a id="issue-058"></a>
## ISSUE-058 · Website share description still calls the stable release a beta

**Initial report · Confirmed in public HTML on 18 November 2026.** The homepage Open
Graph description calls VineMix a beta while the download panel and GitHub
offer regular release 0.6.0. Share metadata should describe the current
product. Use evergreen copy or derive the wording from the release record,
then check a fresh share preview. [Website audit](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/website-redesign/AUDIT_2026-11-18.md)

**Resolved on the public site · 19 November 2026.** Evergreen share metadata and its 1200 × 630 preview image are served publicly. Third-party preview cache refresh timing is external.

<a id="issue-059"></a>
## ISSUE-059 · Missing website pages offer no way back

**Initial report · Confirmed over HTTPS on 18 November 2026.** A nonexistent public
page returned HTTP 404 with an empty body for an HTML request. A visitor
following an old link should see where to find the current site. Supply a
small recovery page and check its status and links on the live host.
[Website audit](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/website-redesign/AUDIT_2026-11-18.md)

**Resolved on the public site · 19 November 2026.** The canonical domain and a redirect alias both served the recovery page with HTTP 404 for a missing nested path.

<a id="issue-060"></a>
## ISSUE-060 · Website visitors cannot pause the opening word sequence on the page

**Initial report · Observed on the public site and confirmed in source on 18 November
2026.** Words change automatically at 350 words per minute alongside the
navigation and download action. The system reduced-motion preference selects
a static wordmark. Visitors using normal motion should also be able to stop
the words while continuing to use the page. Check a stationary presentation
or a keyboard-operable pause, including persistence after focus moves away.
[Website audit](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/website-redesign/AUDIT_2026-11-18.md)

**Resolved on the public site · 19 November 2026.** The word sequence is optional. Keyboard play/pause and continued pause after moving focus passed in all three tested browser engines.

**Maintainer decision · 19 November 2026.** Retired the visible playback
control and added hero copy. The opening again plays automatically and
respects the system reduced-motion preference.

## ISSUE-055: A 0.5 update can cancel before installing 0.6

**Confirmed · 18 November 2026.** On disposable Windows 11 installations,
0.5 discovered the public 0.6 release and verified its download. During
Install and restart, the old client could report that the audio engine closed
its control connection. The update journal recorded cancellation before file
replacement. The expected result is a completed restart into 0.6.

Use the [manual upgrade instructions](INSTALLATION_AND_UPDATES.md) if this
occurs. Both matching packages preserved the saved workspace in testing.
The 0.5 client and its engine coordinate shutdown before the new package
runs. A follow-up should distinguish a lost shutdown acknowledgement from a
failed shutdown using the original engine identity and the settled workspace.

**Release update — 6 November 2026:** Published [0.5.0 Beta](releases/0.5.0-beta.md)
includes the accepted keyboard/zoom improvements (ISSUE-003/005), restart
hardening (ISSUE-011), decimal history correction (ISSUE-024), Settings sizing
(ISSUE-025), Echo correction (ISSUE-026), Distortion optimization (ISSUE-027),
short-cable correction (ISSUE-028) and malformed-identity validation (ISSUE-029).
Broader accessibility, the original unreplayed incidents and CPU-capacity limits
remain explicitly distinguished from resolved defects. ISSUE-030 records the
extreme multi-RNNoise limit. [Final qualification](RELEASE_0.5.0_QUALIFICATION.md)
supersedes the dated local-only checkpoints below for release status.

**Release update — 28 October 2026:** ISSUE-001, ISSUE-004 and ISSUE-022 are
included in published [0.4.0 Beta](releases/0.4.0-beta.md). Their dated local
integration evidence below remains historical; final combined qualification is
recorded [separately](RELEASE_0.4.0_QUALIFICATION.md). Real pointer/VR limits remain.
The 2026-10-25 classification review moved website layout/copy polish (ISSUE-006)
and update-window styling (ISSUE-009) there, retaining redirects below. Optional
fine-adjust/reset affordances were split from ISSUE-004. Broken behavior remains
here regardless of how small a correction might be.

Use stable IDs such as `ISSUE-001`; never reuse them. Update related entries
instead of adding duplicates. Track status as **Reported**, **Confirmed**,
**In progress**, or **Resolved**, and record verification and the fixing version
when an issue is resolved. Keep reports distinct from suspected causes.
A documented limitation may be confirmed in source while its proposed remedy
remains undecided; record that distinction explicitly.

**Current manual-feedback checkpoint — 2026-11-04:** The maintainer reports
successful Tab/Shift+Tab, Ctrl+Tab pipeline travel, arrow nudge/undo and pan, F2
rename, Find by custom name/type, direct dial entry/Page keys and decimal-edit
undo. Minimap navigation/visibility persistence and focused Trigger/Reverb checks
are recorded in [the release plan](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/IDEAS.md#release-0-5-0-plan). These are
maintainer-reported results, not independently observed qualification. The new
ISSUE-003/005 keyboard/zoom/detail refinements, Settings fix (ISSUE-025) and final
meter corrections passed source review and the final offline suite: 14,726
detached checks, 314 renders and zero binding errors. Guarded interface-only
integration through the trusted shell and actual shortcut passed, preserving
the engine identity/hash, current configuration and settings/preferences, frames
and running paths through eight seconds of observation without rollback.
Final audit verified all 36 payload hashes and matching source/build receipt/
installed inventory identity. Maintainer follow-up review
remains pending; these are local corrections, not a published 0.5.0 release. Broader
accessibility, IME, Narrator and pointer/DPI coverage remain open.

**Subsequent corrections — 2026-11-04:** Node-header colour is retained while
only its small text hides (ISSUE-005). The meter's full border and original peak/
idle indicator are restored; an inset gradient removes only the tiny red seam
([TWEAK-021](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/TWEAKS.md#tweak-021)). Both corrections are implemented, offline
verified and locally integrated: the latest suite passed 14,748 checks / 314
renders / zero binding errors, with 525 focused checks. Guarded
interface-only activation preserved the same engine identity/hash and unchanged
settings/preferences through eight seconds without rollback. Maintainer follow-up
remains pending. The qualification above belongs to the preceding candidate;
group headers are unchanged and the work remains unreleased.

**Earlier source and local integration checkpoint — 2026-11-04:** The then-accepted 0.5.0
corrections below are implemented, offline verified and locally integrated,
**not released**. Combined qualification passed
18 native suites, 14,583 detached UI checks / 305 previews / zero binding errors,
712 production-boundary checks, 51 controller-identity checks and 97 UI-activation
fixtures. Authorized guarded paired and interface-only activation passed, with
physical settings verified and all 36 installed payload hashes matching the
aligned source receipt/inventory. Each update preserved its then-current saved
settings/preferences, including newer workspace edits between updates. Native
keyboard/IME, Narrator, pointer/DPI usability and listening tests of new
Trigger/Wet routes remain unperformed. The release identity remains 0.4.0 Beta.
See [the execution report](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/IMPLEMENTATION_0.5.0.md) for evidence and remaining work.

**Rust review corrections — 10 November 2026:** The findings of the
[Rust review](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/RUST_REVIEW_2026-11-10.md) were worked through on the `rust-rewrite` branch.
ISSUE-031 to ISSUE-044 below keep their original evidence and each gains a
dated correction paragraph. ISSUE-045 to ISSUE-048 record further defects found
while doing that work. All of it is uncommitted source on top of `55e77c7`,
verified by `scripts/verify-rust.ps1` (290 tests in 28 suites, the 11 C/C++
parity comparisons and the update-record cross-check with the 0.5 serializer),
on synthetic data only. Nothing here was checked in the installed application,
on physical devices, by listening or with a screen reader, and nothing is
released. The review tracker records what remains open.

| ID | Issue | Status |
| --- | --- | --- |
| [ISSUE-054](#issue-054) | Audio load details show several percentages that read as a disagreement | Reported by the maintainer; replaced in the Rust source by one figure on a bar; seen in the design preview only; unreleased |
| [ISSUE-053](#issue-053) | A middle-button drag that starts over a Rust dial turns the dial as well as panning | Reported by the maintainer; resolved in source; pointer regression passes; unreleased |
| [ISSUE-052](#issue-052) | Dragging a control writes the settings file about ten times a second | Reported by the maintainer; resolved in the Rust source; engine and desktop tests pass; unreleased |
| [ISSUE-051](#issue-051) | The redesigned Distortion starts far too loud on a quiet source | Found by measurement and resolved in source before any release; unreleased |
| [ISSUE-050](#issue-050) | Distortion's strength and loudness depend on the input level, and it delays the signal | Confirmed by measurement in 0.5; redesigned in the Rust source; Drive extended to 60 dB on 12 November; listening comparison pending; unreleased |
| [ISSUE-049](#issue-049) | Voice Color grows quieter, not steadier, and sharper in sibilants as Character rises | Confirmed by measurement in 0.5; redesigned in the Rust source; listening comparison pending; unreleased |
| [ISSUE-045](#issue-045) | Typing on a focused Rust dial doubles the first digit | Resolved in source; typing regression passes; unreleased |
| [ISSUE-046](#issue-046) | Escape in Rust Settings search closes the menu instead of clearing the search | Resolved in source; search tests pass; unreleased |
| [ISSUE-047](#issue-047) | Arrow keys leave the Rust Voice Color pad and EQ plot after one step | Resolved in source; keyboard regressions pass; unreleased |
| [ISSUE-048](#issue-048) | Rust crate notices state MIT terms and an invented MIT text for crates that publish no licence file | Generator corrected in source; replacement supplements await confirmation |
| [ISSUE-031](#issue-031) | Rust effect configuration/adoption allocates on the mixer path | Resolved in source; allocation guard and all 11 parity comparisons pass; unreleased |
| [ISSUE-032](#issue-032) | Rust live app/device selector retains an invalid highlighted index | Resolved in source; changing-inventory regressions pass; unreleased |
| [ISSUE-033](#issue-033) | Rust damaged interface preferences are overwritten without preservation | Resolved in source; damaged file preserved and groups recovered in tests; unreleased |
| [ISSUE-034](#issue-034) | Rust update checks/downloads block Quit and cancel only between network operations | Resolved in source; stalled-server and quit tests pass; not exercised from the tray; unreleased |
| [ISSUE-035](#issue-035) | Returning assigned Rust output reportedly flashes Unassigned | Reproduced with a controlled status sequence and resolved in source; physical return not exercised |
| [ISSUE-036](#issue-036) | Open Rust node dropdown grows with zoom but does not shrink on zoom-out | Resolved in source; bidirectional zoom regressions pass; unreleased |
| [ISSUE-037](#issue-037) | Rust advertises keyboard tooltip access without implementing it | Resolved in source; keyboard help tests pass; screen readers not run |
| [ISSUE-038](#issue-038) | Rust practice EQ tooltip describes measured output audio | Resolved in source; practice wording test passes |
| [ISSUE-039](#issue-039) | Rust hover copy reintroduces lengthy control instructions and cable explanations | Application-wide audit done in source: 689 texts, none outside the standard; visual review pending |
| [ISSUE-040](#issue-040) | Backwards drag selection collapses in an initially unfocused Rust dial entry | Resolved in source; selection regressions pass in both directions |
| [ISSUE-041](#issue-041) | Cable tooltip is positioned at the canvas edge instead of the hovered cable | Resolved in source; verified in the real canvas at four size, scale and zoom combinations |
| [ISSUE-042](#issue-042) | Clearing Rust Settings search leaves a roughly two-row scrollable menu | Reproduced in the real application code and resolved in source; unreleased |
| [ISSUE-043](#issue-043) | Rust Settings search insertion caret is misaligned | 2.5-point vertical offset confirmed in the real application code and resolved in source; native appearance not observed |
| [ISSUE-044](#issue-044) | Reconnected input/output appears separately while saved device remains Missing | In progress: shared identity rules and stale-row handling in source; physical off/on trace still needed |
| [ISSUE-029](#issue-029) | Malformed channel/process numbers silently narrow to other identities | Resolved in 0.5.0 Beta; recovery regressions pass |
| [ISSUE-030](#issue-030) | Many RNNoise nodes exceed the real-time processing budget | Confirmed synthetic capacity limitation; documented for 0.5.0 |
| [ISSUE-001](#issue-001) | Settings search caret overlaps placeholder | Resolved in 0.4.0 Beta |
| [ISSUE-002](#issue-002) | Duplicate application monitoring | Reported limitation |
| [ISSUE-003](#issue-003) | Graph keyboard/accessibility gaps | Feedback refinements offline verified and integrated; maintainer follow-up and broader accessibility open |
| [ISSUE-004](#issue-004) | Precise edits discarded on focus loss | Resolved in 0.4.0 Beta |
| [ISSUE-005](#issue-005) | Dense-graph navigation and Fit limits | Navigation, detail guards and Quiet Blocks released in 0.5.0 Beta; broader pointer/DPI review remains |
| [ISSUE-006](#issue-006) | Website node route is hard to follow | Moved to [TWEAK-009](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/TWEAKS.md#tweak-009) |
| [ISSUE-007](#issue-007) | Automatic update checks can appear inactive and suppress failed retries | Resolved in Beta 5 |
| [ISSUE-008](#issue-008) | Installed Beta 2 reopens after an attempted Beta 4 update | Metadata defect resolved in Beta 5; tester incident not independently confirmed |
| [ISSUE-009](#issue-009) | Update window does not match the minimal application interface | Moved into [TWEAK-004](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/TWEAKS.md#tweak-004) |
| [ISSUE-010](#issue-010) | Word-reader prototype logo jitters at collapse finish | Local correction; visual review pending |
| [ISSUE-011](#issue-011) | Local UI activation helper rejects a newly opened controller | Hardening verified offline and in guarded live activation; original incident cause unknown |
| [ISSUE-012](#issue-012) | Scroll-story main heading is partly clipped | Local correction verified; maintainer review pending |
| [ISSUE-013](#issue-013) | Low / Mid / High and Ducking website meters do not animate | Resolved locally; maintainer review pending |
| [ISSUE-014](#issue-014) | Landing action contents and metadata drift from the selected design | Earlier layout superseded locally; visual review pending |
| [ISSUE-015](#issue-015) | Scroll for more remains visible after scrolling | Resolved locally; maintainer review pending |
| [ISSUE-016](#issue-016) | Open-source button GitHub icon does not invert on hover | Resolved locally; maintainer review pending |
| [ISSUE-017](#issue-017) | Header logo jitters when expanding | Resolved locally; maintainer review pending |
| [ISSUE-018](#issue-018) | Website release refresh stalls after reconnecting | Resolved and verified locally |
| [ISSUE-019](#issue-019) | Pipeline loading can outlive a disconnected component | Resolved and verified locally |
| [ISSUE-020](#issue-020) | Website Download and keyboard skip miss destination focus | Resolved and verified locally |
| [ISSUE-021](#issue-021) | Refresh flashes the expanded logo and resizes the scrollbar | Published and verified live |
| [ISSUE-022](#issue-022) | Selected nodes fall behind overlapping nodes | Resolved in 0.4.0 Beta |
| [ISSUE-023](#issue-023) | Update rolls back after a successful package check | Resolved in corrected 0.4.0 Beta; old workers need direct Setup |
| [ISSUE-024](#issue-024) | Undo history resets after a virtual-input Level adjustment | Precision defect corrected, boundary verified and locally integrated; original live incident unreplayed |
| [ISSUE-025](#issue-025) | Settings menu scrolls and cuts off lower options despite available space | Resolved in 0.5.0 Beta |
| [ISSUE-026](#issue-026) | Echo Time changes replay stored repeat mixtures | Resolved in 0.5.0 Beta; listening refinement confirmed by maintainer |
| [ISSUE-027](#issue-027) | Maximum Distortion graph has insufficient routing-fade timing margin | Optimized, offline verified and integrated; maximum-graph margin remains limited |
| [ISSUE-028](#issue-028) | Short cables fold back instead of flowing directly between sockets | Resolved in 0.5.0 Beta; detached geometry and rendering verified |

<a id="issue-054"></a>
## ISSUE-054: Audio load details show several percentages that read as a disagreement

- **Reported:** 2026-11-12, by the maintainer, in the running Rust edition:
  the details window listed an average load of 5% and a recent peak of 15%
  among other figures, and it was not clear which to trust. They asked for a
  bar that fills, a realistic percentage and a simpler presentation.
  **Status:** Replaced in the Rust source; unreleased. The 0.5 WPF desktop
  keeps the earlier window.
- **Cause:** Not a measurement fault. The two figures answer different
  questions. The average is the share of each 5 ms of sound that processing
  takes, over the last second. The peak is the single slowest 5 ms of that
  second, which one interruption by another program decides. The window
  listed both as plain percentages, with a steady peak, a routing-change
  peak, overruns and missed deadlines, and long qualifications.
- **Correction:** The footer shows a bar and one figure, the average. The
  peak is a mark on the same bar, not a second number. The bar is green,
  amber from 70% and red at risk, following the engine's own states. Clicking
  it opens the same bar, larger, with three or four plain sentences and one
  count of moments that ran late (a late start and an overrun are counted
  once). The engine's telemetry and its warning policy are unchanged.
  `crates/vinemix-ui/src/processing_load.rs` and the footer and dialog in
  `crates/vinemix-ui/src/ui/app.rs`.
- **Verification:** Unit tests for the wording, the single late count and
  the stale and invalid cases. The footer and the details window were
  captured from the design preview (`--preview --preview-fixture`, no engine
  and no settings) at 5% and at 78% with late moments, and looked at. A
  preview's fixture reading no longer goes stale after two seconds.
- **Not verified:** Against the running engine, at other display scales, or
  with a screen reader. Whether the average is the figure the maintainer
  wants as the headline is their call; the peak is one line of code away.

<a id="issue-053"></a>
## ISSUE-053: A middle-button drag that starts over a Rust dial turns the dial as well as panning

- **Reported:** 2026-11-12, by the maintainer, in the running Rust edition.
  **Status:** Resolved in source; unreleased.
- **Cause verified:** The dial's face reacted to a drag by any pointer
  button. The canvas pans on the middle button whatever is under the
  pointer, so both happened.
- **Correction:** Only the primary button starts, continues and ends a dial
  drag (`crates/vinemix-ui/src/ui/dial.rs`). The Voice Color pad, the EQ plot
  and node dragging already asked for the primary button.
- **Verification:** `only_the_primary_button_turns_a_dial` drags over a dial
  face with the middle, secondary and primary buttons at three zoom levels:
  the first two leave the value and the edit history alone, the third turns
  the dial. Not tried with a physical mouse.

<a id="issue-052"></a>
## ISSUE-052: Dragging a control writes the settings file about ten times a second

- **Reported:** 2026-11-12, by the maintainer, who saw the save status
  flicker while dragging the Voice Color pad and asked for one save when the
  control is released. **Status:** Resolved in the Rust source; unreleased.
- **Cause verified:** The desktop sends the whole configuration to the
  engine every 100 ms while a control moves, so the change is heard, and the
  engine wrote the settings file, durably, for each one. This applied to
  every dial, the pad, EQ bands and node positions.
- **Correction:** A `configure` request may carry `persist: false`
  ([protocol](CONTROL_PROTOCOL.md)). The engine applies it and leaves the
  file alone. The desktop sends that while an edit gesture is open and the
  control has moved in the last two seconds, and an ordinary `configure`
  when the gesture ends. One entry stays in the desktop's queue of unsaved
  edits until that save is confirmed, so quitting, restarting the engine and
  updating still wait for it, and the footer reads "Adjusting · saves when
  you let go" in the meantime. The engine also writes an unsaved
  configuration when its client disconnects, on shutdown and when it stops.
- **Verification:** `a_drag_is_applied_live_and_saved_on_release` and
  `single_edits_and_stalled_drags_are_saved` in the desktop;
  `edit_cost.rs` in the engine, which checks that 20 dragged configures
  leave the file byte-identical, that the release writes the final value,
  and that a flush writes what a departed client left. Edits made with the
  keyboard or by typing are saved as before, one write per 100 ms at most.
- **What it costs:** If the engine process is killed during a drag, that
  drag is lost; before, at most the last 100 ms was. Not exercised against
  the running application, or with a 0.5 engine (which ignores the field and
  saves every time).

<a id="issue-051"></a>
## ISSUE-051: The redesigned Distortion starts far too loud on a quiet source

- **Found:** 2026-11-12, by measurement, while extending the Drive range. It
  was present in the redesign committed the day before
  ([ISSUE-050](#issue-050)) and in the build the maintainer was running.
  **Status:** Resolved in source; unreleased.
- **Evidence:** A synthetic voice at −44 dBFS left the node 38 dB too loud in
  its first 5 ms and several decibels too loud for the next 10 ms; at
  −24 dBFS the first 5 ms were 18 dB too loud. This happened at any Drive,
  including 0 dB. It showed first as a 20 dB quieter voice measuring 1.4 LU
  louder than expected at Drive 60 dB, because the loudness measure includes
  the start.
- **Cause verified:** The gain that brings the sound to the standard level
  and the gain that takes it back were set by separate rules. At the first
  sound the level estimate is still very low, so the makeup allowed for a
  drive the curve was not yet receiving while the gain back was still at
  rest. The earlier tests started the node on a voice near the standard
  level and looked no earlier than the first 50 ms.
- **Correction:** The gain after the curve is now the small-signal gain for
  the drive in effect divided by the gain into the curve, on every frame, so
  the two cannot disagree. `crates/vinemix-dsp/src/effects/distortion.rs`.
- **Verification:** `the_first_sound_leaves_at_its_own_level` (voices at
  +12, 0, −20 and −30 dB, three Drive settings, in 20 ms steps) and
  `a_quieter_source_gets_the_same_treatment`, now at 20 and 30 dB quieter
  and within 1 LU. In the trace that showed the fault, every 5 ms step of
  the first 100 ms is within 0.2 dB of the input at Drive 0. Synthetic
  signals only.

<a id="issue-050"></a>
## ISSUE-050: Distortion's strength and loudness depend on the input level, and it delays the signal

- **Found:** 2026-11-11, by measurement, after the maintainer said they had
  been hesitant to use the node and asked for it to be redesigned after Voice
  Color. **Status:** Confirmed in the 0.5 design, which the published 0.5.0
  Beta and the 0.5 C++ sources keep. Redesigned in the Rust source;
  unreleased. No listening comparison has been made.
- **Evidence:** The Rust port of the 0.5 design (commit `20bf8d8`) was
  rendered offline on two synthetic speech voices from the Windows speech
  engine, two generated test voices and steady tones, at Mix 100% and Output
  0 dB. Speech at −24 dBFS came out 5.4 LU louder at Drive 6 dB and 22 LU
  louder at 36 dB (ITU-R BS.1770 integrated loudness); the same voice 20 dB
  quieter came out 33.5 LU louder at 36 dB. The harmonics of a 997 Hz tone at
  Drive 12 dB measured −15 dB with the tone at −6 dBFS and −70 dB with it at
  −36 dBFS, so the dial alone does not set the amount of distortion. Speech
  with peaks at −7 dBFS left with peaks at +4.2 dBFS at Drive 36 dB. Room
  noise 40 dB below the voice was raised by 34 dB at Drive 36 dB. The output
  is delayed by 32 frames, bypassed or not. The curve adds odd harmonics only.
  One node cost 160 to 200 ns per frame. Its aliasing was very low: below
  −110 dB for every tone and Drive tested except 5 kHz at 36 dB (−75 dB).
- **Not established:** That the earlier sound was worse to listen to.
- **Correction (Rust edition):** A new design in
  `crates/vinemix-dsp/src/effects/distortion.rs`, with the compiled amounts
  in `crates/vinemix-model/src/distortion.rs`. The four controls, the saved
  fields, their ranges and the protocol are unchanged, and nothing is
  migrated. The default Output changes from −6 dB to 0 dB, because Drive no
  longer adds level; saved nodes keep their value. On the same signals the
  loudness is 0.1 to 1.0 LU below dry at every Drive, the harmonics are the
  same at every input level, there is no delay, noise 40 dB below the voice
  is not raised, and gentle settings add mostly second harmonic. One node
  costs about 28 ns per frame. [DISTORTION.md](DISTORTION.md) describes the
  design and its limits.
- **What is worse:** Aliasing at extreme settings. It is below −100 dB up to
  Drive 12 dB for tones up to 3 kHz, but about −35 dB for 3 and 5 kHz tones
  at Drive 36 dB, where the earlier design measured −116 and −75 dB. The
  earlier design bought that with eight-times oversampling and its delay.
- **Verification:** `crates/vinemix-dsp/tests/distortion.rs` (18 checks) and
  the unit tests beside both modules; the allocation guard and the retarget
  comparison include the node. The C/C++ parity comparison no longer covers
  this node: a Gain node holds its place in the parity graph, because the
  0.5 node delays even its bypass path, and all five graph comparisons still
  pass. Voice Color's filters moved to a file the two nodes share; its 56
  reference renders are byte-identical before and after. Synthetic signals
  only.
- **Also changed:** The desktop no longer warns that adding a Distortion
  needs substantial processing; the advice remains for RNNoise. The routing
  fade margin recorded in [ISSUE-027](#issue-027) belongs to the 0.5 node.
- **Open:** The maintainer's listening comparison of the earlier and new
  designs (`scripts/render-distortion.ps1`, and the node in the running
  application). The standard level, the working point, the split and the
  emphasis are constants in the model file and are expected to be adjusted
  after that. The defaults for Drive (6 dB) and Mix (50%) are unchanged and
  are now deliberately gentle; whether a new node should start stronger is
  the maintainer's choice.
- **12 November 2026:** After trying the node the maintainer asked for more
  at the top: two nodes in a row at full Drive and Mix gave the strength they
  wanted. Drive now reaches 60 dB; 0 to 36 dB is unchanged. One node at
  60 dB changes the test voices about as much as those two nodes (−2.4 dB of
  the output against −2.2 dB; −3.8 dB at 36 dB). At 60 dB aliasing is about
  −30 dB for tones from 1 to 5 kHz and −48 dB at 220 Hz, and noise 30 dB
  below the voice rises by 23 dB (18 dB through the two nodes); noise 40 dB
  below is still not raised. The makeup became a table read for the Drive of
  each frame. 20 checks. A fault at the start of the node was found and
  corrected on the way ([ISSUE-051](#issue-051)). Not compared by ear.

<a id="issue-049"></a>
## ISSUE-049: Voice Color grows quieter, not steadier, and sharper in sibilants as Character rises

- **Found:** 2026-11-11, by measurement, after the maintainer said they had
  been hesitant to use the node and asked for a redesign. **Status:** Confirmed
  in the 0.5 design, which the published 0.5.0 Beta and the 0.5 C++ sources
  keep. Redesigned in the Rust source; unreleased. No listening comparison has
  been made.
- **Evidence:** The 0.5 design and the Rust port of it (commit `9f77d14`) were
  rendered offline on two synthetic speech voices from the Windows speech
  engine and two generated test voices. At full Character and centred Tone the
  output was 5.0 to 6.9 LU quieter than the input (ITU-R BS.1770 integrated
  loudness), and up to 10.5 LU quieter at Warm. The spread of momentary
  loudness on the two speech-engine voices did not narrow (5.69 to 5.73 dB and
  6.35 to 6.49 dB). The loudest 10 ms of the 5 to 10 kHz band rose 3 to 6 dB
  against the speech level, and 9 to 12 dB at the Bright edge. In source: the
  compressor's threshold is fixed at −28 dBFS, so its effect depends on
  microphone gain; the "double" is two fixed delays of 12 and 19 ms, a comb
  filter that also turns a mono voice into stereo; the density stage shapes the
  full band without oversampling; and the room is mixed at 2%.
- **Not established:** That the earlier sound was worse to listen to. Earlier
  rounds increased its strength at the maintainer's request after listening;
  those requests are consistent with a node that grew quieter as it was turned
  up, but that is an inference.
- **Correction (Rust edition):** A new design in
  `crates/vinemix-dsp/src/effects/voice.rs`, with the pad mapping in
  `crates/vinemix-model/src/voice.rs`; the pad, the saved `tone` and `ambience`
  values and the protocol are unchanged, and nothing is migrated. On the same
  files at full Character the loudness is 0.2 LU below to 2.2 LU above dry,
  the generated voices' spread narrows from 9.2 to 6.1 dB and from 9.0 to
  5.0 dB, and sibilants stay within 1 dB. One node costs about 40 ns per frame
  against about 120 ns. [VOICE_COLOR.md](VOICE_COLOR.md) describes the design
  and its limits.
- **Verification:** `crates/vinemix-dsp/tests/voice_color.rs` (17 checks) and
  the unit tests beside both modules; the allocation guard and the
  retarget comparison include the node. The C/C++ parity comparison no longer
  covers this node's sound: it sits bypassed in the parity graphs, and all
  five graph comparisons still pass. Synthetic signals only.
- **Open:** The maintainer's listening comparison, on their own voice and
  microphone, of the earlier and new designs
  (`scripts/render-voice-color.ps1`, and the node in the running application).
  The strengths at the edges of the pad are constants in the model file and
  are expected to be adjusted after that. The harmonics are generated per
  channel, so an off-centre voice shifts by a fraction of a decibel.
- **12 November 2026:** After trying the node the maintainer asked for its
  extremes to go further. Each axis now bends upward towards its edge: the
  contour at an edge is one and a half times what it was, in decibels (+9 dB
  of tilt, +10.5 dB of body and presence), and full Character is 1.3 times as
  deep (about 14:1). Half Character moves by 12% and 7%. On the test voices
  full Character narrows the spread of loudness from 9.2 to 5.0 dB (6.1
  before), loudness stays within about 2 LU quieter and 2.5 LU louder than
  dry across the pad, and the Bright, full-Character corner makes sibilants
  stand out about 6 dB more than dry (under 4 before), with the guard
  starting a little earlier. 17 checks. Not compared by ear.

<a id="issue-048"></a>
## ISSUE-048: Rust crate notices state MIT terms for crates that publish no licence file

- **Found:** 2026-11-10 while correcting the notice generator
  ([RUST-REV-025](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/RUST_REVIEW_2026-11-10.md#rust-rev-025)). **Status:** Generator corrected in
  source; the replacement supplements await confirmation. Unreleased.
- **Evidence:** The committed `licenses/Rust-crates.txt` said of 16 crates that
  the crate "is used under the MIT licence named in its manifest" and followed
  that with a standard MIT text whose copyright line was built from the
  manifest's author list. One of them, `accesskit_winit 0.34.2`, names only
  Apache-2.0 in its manifest. Two others, `enum-map` and `enum-map-derive`,
  do publish licence texts, in a `LICENSES` directory the generator did not read.
  The review had reported the generator's behaviour as a risk; this is an
  inaccuracy in the file it produced. It is a notice-accuracy defect, not a
  legal assessment.
- **Correction:** `tools/rust-notices.py` reads the manifest's licence file and
  `LICENSES` directories, never writes a text of its own, and stops when a
  crate has no licence text and no reviewed supplement. The 14 crates that
  publish none (the egui and eframe family, AccessKit, `profiling`) now have
  supplements in `licenses/rust-supplements/` that use them under Apache-2.0,
  which every one of their manifests offers and whose text is the same for
  every work. `licenses/Rust-crates.txt` was regenerated; exactly those 16
  entries changed. Fixture tests: `py -3 tools/test_rust_notices.py` (21).
- **Open:** Each supplement is marked `Review: Pending`. Nobody has compared
  them with the upstream repositories, which could not be reached offline.
  The maintainer should confirm that Apache-2.0 is the intended choice (the
  earlier header promised MIT where offered), or replace a supplement with the
  upstream MIT text, and check whether AccessKit's repository carries further
  notices that binaries must reproduce. The generator lists pending supplements
  on every run.

<a id="issue-047"></a>
## ISSUE-047: Arrow keys leave the Rust Voice Color pad and EQ plot after one step

- **Found:** 2026-11-10 while adding accessibility semantics
  ([RUST-REV-027](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/RUST_REVIEW_2026-11-10.md#rust-rev-027)). **Status:** Resolved in source;
  unreleased.
- **Evidence:** With the pad or the EQ plot focused, the first arrow key moved
  the value and also moved keyboard focus to a neighbouring control, so the
  second press went elsewhere. Reproduced through the real application code
  running without a window.
- **Correction:** Both controls keep focus while handling arrow keys. Tests
  `voice_color_pad_is_keyboard_operable` and
  `equalizer_bands_follow_repeated_arrow_keys`.

<a id="issue-046"></a>
## ISSUE-046: Escape in Rust Settings search closes the menu instead of clearing the search

- **Found:** 2026-11-10 while reproducing ISSUE-042. **Status:** Resolved in
  source; unreleased.
- **Evidence:** The search field's help says Escape clears the search. With a
  query present, Escape closed the whole Settings menu, because focus left the
  field before the field saw the key.
- **Correction:** Escape with a query clears it and keeps the menu open; Escape
  with an empty field closes the menu as before. Exercised by the Settings
  search tests in `crates/vinemix-ui/src/ui/app_tests.rs`.

<a id="issue-045"></a>
## ISSUE-045: Typing on a focused Rust dial doubles the first digit

- **Found:** 2026-11-10 while fixing ISSUE-040. **Status:** Resolved in source;
  unreleased.
- **Evidence:** With a dial focused, typing a digit started exact entry with
  that digit entered twice, and the next digit replaced the text instead of
  following it. The dial face and the new entry field both consumed the same
  key event.
- **Correction:** The face consumes the text event that starts entry and the
  entry continues from it. Test
  `typing_on_the_dial_face_starts_exact_entry_and_continues_it`.

<a id="issue-044"></a>
## ISSUE-044: Reconnected input/output appears separately while the saved device remains Missing

- **Reported and investigated:** 2026-11-10, Rust commit `55e77c7`.
  **Status:** Source-inspected; synthetic chooser/worker-policy reproduction;
  actual physical off/on identity transition unverified. No fix applied.
- **Reported behavior:** After turning an assigned input/output device off and
  back on, a second version sometimes appears in the dropdown while the original
  remains Missing. The maintainer asks whether Windows priority is involved.
- **Evidence:** With a changed endpoint ID and absent/nonmatching saved stable
  ID, production chooser code lists the active endpoint beside a retained or
  synthesized saved Missing entry. The worker policy has no active identity match.
  Same-ID return and unique matching-stable-ID return recover in the probe.
  Six scenarios for both capture/render, with reversed inventory ordering, were
  checked. The separate output resolver was inspected but not exercised directly.
- **Important distinction:** With disconnected devices visible, an old Missing
  record can remain listed even when the new matching Active endpoint is correctly
  selected and resolved. Record whether recovery actually fails or only the old
  row remains; neither a second row nor a matching friendly name proves identity.
- **Priority assessment:** Explicit assignments resolve by exact ID then unique
  stable ID, not default-device priority. Default-role changes can refresh the
  catalog but do not substitute a new default into this resolution path.
- **Expected:** A proven same endpoint reconnects consistently across chooser and
  engine. Ambiguous or unidentified replacements preserve the saved assignment
  and offer clear recovery instead of silently choosing another same-name device.
- **Next step:** Capture an anonymized identity/state timeline and property-read
  availability around a controlled off/on; examine stable-ID acquisition and safe
  enrichment of existing bindings. Test unchanged/changed/missing/delayed IDs,
  visibility settings, identical names, ambiguity and default-role changes.
  Keep raw device evidence outside the checkout. Do not delete devices, merge
  names or change Windows priority speculatively.
- **References:** [RUST-REV-041](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/RUST_REVIEW_2026-11-10.md#rust-rev-041) contains
  the synthetic matrix, source references and Microsoft documentation researched
  on 2026-11-10. Distinct from the selector-index crash (ISSUE-032) and returning
  output's transient Unassigned status (ISSUE-035).
- **Correction and verification — 2026-11-10:** Partly resolved. The chooser, the output resolver and the worker now share one rule set (`vinemix_model::identity`): exact active ID, then a unique active stable ID, then the stable ID of the saved ID's own record; never the name, never default-device priority. When identity proves the active endpoint is the saved device, the superseded Missing record is hidden, which removes the stale-row case. When it cannot be proven, the same-name device stays separate and the saved row says how to recover. The engine logs an anonymised identity summary when a binding's resolution changes, to capture the next incident. Not done: a physical off/on trace, which needs the maintainer's hardware; and automatic enrichment of a saved binding, which was deliberately left out because it would open a new stream and reset undo history without the user asking. Working tree on `rust-rewrite` above `55e77c7`; not committed, not released, not checked in the installed application.

<a id="issue-042"></a>
## ISSUE-042: Clearing Settings search leaves a roughly two-row scrollable menu

- **Reported and investigated:** 2026-11-10, Rust commit `55e77c7`.
  **Status:** Reproduced in an isolated layout reconstruction; no fix applied.
- **Reported behavior:** After entering and then removing a Settings search,
  the menu collapses to a scrollable list showing at most about two options.
- **Evidence:** The Settings Area/Frame/TextEdit/ScrollArea layout was recreated
  with production theme/menu helpers and synthetic rows. Clearing a no-results
  query restored 504 points of row content but left a 64-point scroll viewport;
  the menu was 122 points high instead of its initial 562. The full application
  and its search dispatch were not run.
- **Likely cause:** The menu's remembered smaller height constrains subsequent
  layout. ScrollArea's maximum alone does not expand the available parent area.
  See `ui/app/settings_menu.rs:308–387`.
- **Expected:** Clearing the query restores the normal menu height within the
  visible window, with all settings present and no unnecessary two-row limit.
- **Proposed fix and next step:** Recompute the parent maximum layout height
  from available window space before laying out the menu, retaining content-driven
  shrinking. This restored expansion in the isolated comparison. Verify real
  search/clear paths, repeated queries, scroll offsets, submenus and small/DPI-
  scaled windows before integration. Related historical WPF issue: ISSUE-025;
  its resolution remains historical, not a qualification of this Rust layout.
  See [RUST-REV-039](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/RUST_REVIEW_2026-11-10.md#rust-rev-039).
- **Correction and verification — 2026-11-10:** Reproduced in the real application code, not only the reconstruction: after a search the menu stayed 95 points high instead of 578. The menu now takes its maximum height from the available window each frame. Tests `clearing_the_settings_search_restores_the_menu_height`, `repeated_searches_keep_restoring_the_full_menu` and `small_windows_and_display_scales_restore_to_the_available_height`. Working tree on `rust-rewrite` above `55e77c7`; not committed, not released, not checked in the installed application.

<a id="issue-043"></a>
## ISSUE-043: Settings search insertion caret is misaligned in Rust

- **Reported and investigated:** 2026-11-10, Rust commit `55e77c7`.
  **Status:** Reported visual defect; isolated vertical offset measured;
  exact installed appearance and complete cause unverified. No fix applied.
- **Reported behavior:** The typing line inside the Settings search box is not
  aligned properly. Recorded as the insertion caret; the report does not specify
  horizontal versus vertical offset or empty versus populated state.
- **Evidence:** `ui/app/settings_menu.rs:319–327` sets a 30-point field with
  padding but no vertical centring. An isolated reconstruction placed the
  17-point text/caret layout 2.5 points above the field centre. Explicit left/
  centre alignment removed that measured offset. This is a candidate cause and
  correction, not confirmation of the exact reported native visual defect.
- **Expected:** Caret, placeholder and typed text share the intended insertion
  origin and alignment in empty and populated states.
- **Next step:** Inspect focused empty/typed states, selection, long-query
  horizontal scrolling and clearing across DPI scales; correct shared alignment
  and investigate any remaining horizontal mismatch. Related historical WPF
  issue: ISSUE-001; do not assume its old placeholder-offset cause applies here.
  See [RUST-REV-040](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/RUST_REVIEW_2026-11-10.md#rust-rev-040).
- **Correction and verification — 2026-11-10:** The 2.5-point vertical offset was confirmed in the real application code and removed by centring the field content. `search_caret_placeholder_and_typed_text_share_one_line` checks the caret, placeholder and typed text at display scales 1.0, 1.25, 1.5 and 2.0, including after clearing a long query. The native blinking caret was not observed, so whether this was the whole of the reported defect is unconfirmed. Working tree on `rust-rewrite` above `55e77c7`; not committed, not released, not checked in the installed application.

<a id="issue-040"></a>
## ISSUE-040: Backwards text selection collapses on first focus in a Rust dial entry

- **Reported and verified:** 2026-11-10, Rust commit `55e77c7`.
  **Status:** Reproduced with the unchanged production component in isolation;
  proposed fix logged, not implemented.
- **Reported behavior:** Selecting a dial's value from back to front removes
  the highlight, disrupting familiar drag-to-replace interaction.
- **Evidence:** With a synthetic `-12.3 dB` entry, a first-focus right-to-left
  drag ended with both selection endpoints at zero. A left-to-right drag
  selected all eight characters; a backwards drag also worked once focused.
  `ui/dial.rs:469–475` overrides the pointer selection on gained focus with
  select-all anchored at the beginning. Subsequent backwards movement collapses
  that selection. Focus and the draft remain present.
- **Expected:** Full and partial text selection works naturally in either
  direction, whether the entry already has focus or not, across shared dials.
- **Proposed fix:** Preserve the pointer-down anchor during selection. Separate
  keyboard focus from pointer gestures; if retaining single-click select-all,
  apply it after a completed click without a drag. Simply swapping the forced
  selection endpoints would only reverse the defective direction.
- **Next step:** Implement the gesture-aware behavior when requested and verify
  forward/backward selection, click and keyboard focus, replacement typing,
  release outside the entry, double-click reset and validation at different zoom
  levels. No installed app, settings or audio engine was changed.
  See [RUST-REV-037](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/RUST_REVIEW_2026-11-10.md#rust-rev-037).
- **Correction and verification — 2026-11-10:** The entry keeps the pointer-down anchor and selects everything only after a click without a drag. Eleven interaction tests cover forward and backward, full and partial selection, in unfocused and already focused entries, release outside the entry, keyboard focus, double-click reset and rejected values. Working tree on `rust-rewrite` above `55e77c7`; not committed, not released, not checked in the installed application.

<a id="issue-041"></a>
## ISSUE-041: Cable tooltip is anchored to the canvas rather than the hovered cable

- **Reported and investigated:** 2026-11-10, Rust commit `55e77c7`.
  **Status:** Wrong source anchor and distant placement confirmed in isolation;
  exact reported bottom-right corner not reproduced in the installed app.
- **Reported behavior:** Noodle/cable tooltip appears at the bottom right,
  rather than at the noodle being inspected.
- **Evidence:** Cable hit-testing uses the pointer, but `ui/canvas/frame.rs:159`
  attaches help to the whole-canvas background response. The normal tooltip
  helper positions relative to that rectangle. A synthetic pointer at `(320,250)`
  produced a tooltip at the lower canvas/window edge, near `(20,624)`; using a
  pointer anchor placed it next to the pointer, near `(320,254)`.
- **Expected:** Cable help appears at the hovered cable with a small readable
  offset and stays within the visible window.
- **Proposed fix:** Use a pointer or small hit-point anchor and per-edge tooltip
  identity. Preserve hit-testing, dragging, dismissal and noninteractive help;
  keep ordinary control placement separate.
- **Next step:** Integrate when requested and verify centre/edge positions,
  zoom/pan, DPI, overlapping cables and hover transfer. The alternative anchor
  was checked only in an isolated harness. This placement issue is separate from
  the application-wide tooltip-copy audit in ISSUE-039.
  See [RUST-REV-038](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/RUST_REVIEW_2026-11-10.md#rust-rev-038).
- **Correction and verification — 2026-11-10:** Cable help is anchored at the hovered point on the cable and has an identity per cable. `cable_help_appears_at_the_hovered_cable` runs the real canvas at four window size, display scale and zoom combinations and checks that dragging dismisses it. The exact bottom-right placement originally reported was not reproduced in the installed application. Working tree on `rust-rewrite` above `55e77c7`; not committed, not released, not checked in the installed application.

<a id="issue-039"></a>
## ISSUE-039: Rust hover copy reintroduces previously separated instructions

- **Reported and investigated:** 2026-11-10, Rust commit `55e77c7`.
  **Status:** Confirmed by source comparison; implementation unchanged.
- **Maintainer clarification:** The tooltip concern is excessive explanation in
  the content, following earlier work to keep the original version concise.
  The required follow-up is a full application-wide tooltip-content audit and
  refactor; Level and the shared controls are examples, not the scope limit.
- **Evidence:** Rust's shared dial and numeric-entry helpers append range and
  interaction instructions to the short purpose text. Cable hover adds connection
  type, state and purpose. The retained C# version keeps ordinary hover to the
  control title/purpose or cable route, with detailed instructions separately
  exposed through accessible help. This contradicts the established concise-copy
  direction in [TWEAK-018](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/TWEAKS.md#tweak-018).
- **Expected:** Short, scannable control names and purpose; longer interaction
  guidance in Help and accessible descriptions. Invalid-value guidance remains
  available when relevant.
- **Limits:** Existing long Echo/Reverb/Distortion headings and the Trigger
  silent-source explanation also occur in retained C# source; they are not
  established as new Rust regressions. No installed-app visual comparison was
  performed for this content follow-up.
- **Required coverage:** Inventory every tooltip family, including nodes,
  controls, sockets/cables, pickers, meters, canvas/groups, menus, settings,
  dialogs, updates/status and Help/practice surfaces. Include dynamic, disabled,
  unavailable and invalid states. Review inherited wording as well as Rust-added
  text; retain already-concise, accurate content.
- **Next step and completion:** Check and refactor shared helpers and individual
  wording against the concise, neutral content standard. Keep a coverage checklist
  recording reviewed/revised/retained families and exceptions, verify relevant UI
  states, and preserve detailed Help/accessibility guidance and meaningful errors.
  A single-node or shared-dial-only change is insufficient. The full audit and
  refactor remain pending; the current investigation verified specific examples.
  See [RUST-REV-036](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/RUST_REVIEW_2026-11-10.md#rust-rev-036).
  Recording this defect does not authorize implementation.
- **Correction and verification — 2026-11-10:** The application-wide audit and refactor is done in source. A sweep test renders every surface and state and records each hover text: 588 texts with 205 outside the standard before, 689 texts with none outside it now, with two justified exceptions. Removed instructions are in Help (about 22 new entries) and in accessible descriptions. The coverage checklist is [TOOLTIP_AUDIT_2026-11-10.md](TOOLTIP_AUDIT_2026-11-10.md). Still open: the maintainer has not reviewed the wording or its appearance in the installed application. Working tree on `rust-rewrite` above `55e77c7`; not committed, not released, not checked in the installed application.

<a id="issue-037"></a>
## ISSUE-037: Rust advertises keyboard tooltip access without implementing it

- **Found:** 2026-11-10 tooltip comparison at Rust commit `55e77c7`.
  **Status:** Confirmed in source and isolated helper test; unfixed.
- **Evidence:** Help advertises Ctrl+Shift+F10 for focused-menu help, but no
  corresponding Rust handler was found. The production tooltip helper uses
  pointer-hover methods. In a headless probe, focus without a pointer and then
  the modifier chord produced no tooltip. Native menu navigation was not run.
- **Expected:** Focused control/menu explanations are available through the
  documented keyboard action, with the original focus/lifecycle safeguards.
- **Next step:** Implement keyboard-owned help, test toggle/dismissal/handoff,
  and preserve accessible help independently of visible hover content.
  See [RUST-REV-034](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/RUST_REVIEW_2026-11-10.md#rust-rev-034).
- **Correction and verification — 2026-11-10:** Help for the focused control appears after a short delay and at once with Ctrl+Shift+F10, hands over with focus, works on menu rows and is dismissed by Escape or pointer input. Covered in `ui/widgets_tests.rs`. Narrator and NVDA were not run. Working tree on `rust-rewrite` above `55e77c7`; not committed, not released, not checked in the installed application.

<a id="issue-038"></a>
## ISSUE-038: Rust practice EQ tooltip describes measured output audio

- **Found:** 2026-11-10 tooltip comparison at Rust commit `55e77c7`.
  **Status:** Confirmed in source; live lesson interaction not replayed; unfixed.
- **Evidence:** The practice lesson uses the normal EQ component with no audio
  status/frames. Its general tooltip unconditionally describes a measured output
  spectrum. The C# lesson supplies explicit practice-only wording instead.
- **Expected:** General practice-EQ help says the response is calculated from
  sample values and no audio is measured or changed.
- **Next step:** Add an explicit practice/live tooltip presentation choice and
  verify both general and selected-band help. This is misleading copy, not
  evidence that the practice lesson changes the user's running audio.
  See [RUST-REV-035](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/RUST_REVIEW_2026-11-10.md#rust-rev-035).
- **Correction and verification — 2026-11-10:** `eq::plot_help` takes a practice flag; the practice plot says the response is calculated and no audio is measured or changed, and practice meters say `Sample peaks`. Test `practice_help_never_claims_to_measure_audio`. Working tree on `rust-rewrite` above `55e77c7`; not committed, not released, not checked in the installed application.

<a id="issue-036"></a>
## ISSUE-036: Open Rust node dropdown retains its larger width after zooming out

- **Reported and verified:** 2026-11-10, Rust commit `55e77c7`. **Status:**
  Confirmed in an isolated headless probe; no implementation change made.
- **Maintainer report:** A dropdown opened on a node appears to scale correctly
  when zooming in, but does not scale properly when zooming out.
- **Independent evidence:** The unchanged production selector grew from 250 to
  500 logical points across 100% → 200% zoom. Kept open, it remained 500 points
  wide through zoom-out to 50%, where the selector face was only 125 points.
  Closing/reopening at 50% restored 125 points for the short example labels.
  Eight frames were allowed at each scale. No live app/audio interaction was used.
- **Cause:** Popup width supplies an egui default, while the area retains its
  previous width. The content applies only a minimum width and fills available
  width, permitting growth without enforcing shrinkage.
- **Related distinction:** Popup text/row height remain fixed in logical points,
  unlike the scaled selector face. This is separate from the retained-width bug;
  any readable minimum/proportional scaling policy should be explicit.
- **Expected:** The open dropdown responds to zoom in both directions, subject
  to deliberate label/viewport constraints, without requiring close/reopen.
- **Next step:** Correct actual-width constraints or sizing invalidation while
  preserving selection, focus and scroll; add bidirectional zoom regressions.
  Full evidence: [RUST-REV-033](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/RUST_REVIEW_2026-11-10.md#rust-rev-033).
- **Correction and verification — 2026-11-10:** An open list is laid out at an explicit width each frame, so it shrinks as well as grows. `open_list_shrinks_as_well_as_grows_with_zoom` reproduces the retained 500-point width against the reviewed code and passes now. Rows keep a fixed 13-point text size with a readable minimum width bounded by the window; that is a recorded decision, not a defect. Working tree on `rust-rewrite` above `55e77c7`; not committed, not released, not checked in the installed application.

<a id="issue-035"></a>
## ISSUE-035: Returning assigned Rust output reportedly flashes Unassigned

- **Reported:** 2026-11-10 through supplied Claude Code review notes. **Status:**
  Reported; not independently reproduced, and no correction implemented.
- **Reported behavior:** A returning assigned output briefly follows
  `Waiting → Unassigned → Running`, misleadingly suggesting loss of assignment.
  Raw logs were not supplied to this review.
- **Source evidence / suspected cause:** At Rust commit `55e77c7`, a fresh
  worker defaults to Unassigned and updates its status asynchronously after
  configuration. A resolved output exposes that worker status. This is a
  plausible transition window, not proof of the reported incident's cause.
- **Expected:** Preserve a truthful Waiting/Opening/Priming status for an
  assigned output until it is Running.
- **Next step:** Reproduce with a controlled endpoint-return/status sequence
  and retain sanitized evidence. See
  [RUST-REV-032](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/RUST_REVIEW_2026-11-10.md#rust-rev-032).
- **Correction and verification — 2026-11-10:** A worker configured with an assignment now reports Resolving until its first real status, so an assigned path never shows Unassigned. `assigned_worker_is_pending_never_unassigned` reproduces the reported sequence against the unchanged code path and passes now. The physical device return that prompted the report was not reproduced. Working tree on `rust-rewrite` above `55e77c7`; not committed, not released, not checked in the installed application.

<a id="issue-031"></a>
## ISSUE-031: Rust effect configuration/adoption allocates on the mixer path

- **Found:** 2026-11-10 Rust review at commit `55e77c7`. **Status:** Confirmed;
  no implementation or release correction made.
- **Verified:** A device-free microphone → reverb → output probe counted two
  allocations and two frees on first processing, unchanged-effect adoption and
  first processing after reset. Steady processing counted zero. String-bearing
  effect settings are cloned on these paths; no audible dropout was reproduced.
- **Expected:** Effect processing, adoption and reset boundaries honor the
  documented allocation-free mixer contract.
- **Next step:** Use compiled runtime settings and add boundary allocation guards.
  See [RUST-REV-001](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/RUST_REVIEW_2026-11-10.md#rust-rev-001) for locations and evidence.
- **Correction and verification — 2026-11-10:** Effect settings are now plain copyable values, so configuring, adopting and resetting an effect no longer clones strings. `crates/vinemix-dsp/tests/realtime_alloc.rs` counts allocations on the calling thread: against the reviewed code it fails with 2 allocations and 2 frees for the reverb fixture and 20 of each across every effect kind; it now counts none for first processing, steady processing, edited settings, adoption, in-place retargeting and reset. The 11 C/C++ parity comparisons still pass. Limits: device-free fixtures; the running engine's mixer loop is not itself instrumented, and no listening test was done. Working tree on `rust-rewrite` above `55e77c7`; not committed, not released, not checked in the installed application.

<a id="issue-032"></a>
## ISSUE-032: Rust live app/device selector retains an invalid highlighted index

- **Found:** 2026-11-10 Rust review at commit `55e77c7`. **Status:** Confirmed;
  unfixed in the reviewed Rust source.
- **Verified:** An open selector with highlight 2, reduced from three choices to
  one, panicked on Enter at `ui/widgets.rs:316` in an isolated headless test.
  Release panic-abort behavior would terminate the interface. Inventory reorder
  can also change the identity represented by a retained index; that consequence
  is source-derived, not a physical-device reproduction.
- **Expected:** Live inventory updates preserve or safely clear highlighted
  identity and never cause out-of-bounds access.
- **Next step:** Reconcile by stable identity, bound index use and test changing
  inventories. See [RUST-REV-002](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/RUST_REVIEW_2026-11-10.md#rust-rev-002).
- **Correction and verification — 2026-11-10:** The open list now tracks its highlight by identity, bounds every index and never selects a disabled row. Regressions in `crates/vinemix-ui/src/ui/widgets_tests.rs` cover shrinking then Enter (the reproduced panic), reordering, an emptied list, a highlighted row that becomes disabled and backwards navigation after a shrink. Working tree on `rust-rewrite` above `55e77c7`; not committed, not released, not checked in the installed application.

<a id="issue-033"></a>
## ISSUE-033: Rust damaged interface preferences are overwritten without preservation

- **Found:** 2026-11-10 Rust review at commit `55e77c7`. **Status:** Confirmed;
  unfixed. Personal preferences were not used or modified.
- **Verified:** After a disposable malformed `ui.json` failed to load, a minimap
  toggle replaced it with defaults/current preferences. The original example
  group was lost and no preserved file was created.
- **Expected:** Preserve unreadable preferences, including group data, before
  permitting a recovery replacement.
- **Next step:** Add explicit preference recovery and preservation regression
  tests. See [RUST-REV-003](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/RUST_REVIEW_2026-11-10.md#rust-rev-003).
- **Correction and verification — 2026-11-10:** Unreadable preferences are copied to `ui.json.damaged-<time>` before anything is written, the last good `ui.json.bak` is used to recover groups, and a notice stays until dismissed. Tests `damaged_preferences_are_preserved_before_any_replacement`, `damaged_preferences_recover_groups_from_the_last_good_backup` and `damaged_copies_never_overwrite_each_other` use disposable files only. The same change bounds the file size read and moves writes to a background writer ([RUST-REV-014](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/RUST_REVIEW_2026-11-10.md#rust-rev-014)). Working tree on `rust-rewrite` above `55e77c7`; not committed, not released, not checked in the installed application.

<a id="issue-034"></a>
## ISSUE-034: Rust update checks/downloads block Quit and cancel slowly

- **Found:** 2026-11-10 Rust review at commit `55e77c7`. **Status:** Confirmed
  in source; stalled-network and live tray interaction reproduction pending.
- **Verified in source:** `begin_quit` silently returns for all busy update jobs,
  including automatic checks and downloads. Cancel sets a flag that blocking
  WinHTTP operations do not observe until control returns to cancellation checks.
- **Expected:** Checking/downloading should cancel promptly and permit the normal
  save-and-quit flow. Installation handoff may require a distinct guarded state.
- **Next step:** Separate installation from background work, make transport
  cancellation/deadlines effective, and test stalled responses plus Quit.
  See [RUST-REV-004](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/RUST_REVIEW_2026-11-10.md#rust-rev-004) and
  [RUST-REV-005](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/RUST_REVIEW_2026-11-10.md#rust-rev-005).
- **Correction and verification — 2026-11-10:** Quit now cancels a check or download and proceeds; only the installation handoff still holds it, with a message. Network calls run on a worker and are aborted by closing the request handle, with a total deadline. Tests against a local stalled server confirm cancellation and the deadline before headers and during the body; `quit_cancels_checks_and_downloads_but_not_an_install` covers the quit path. Limits: not exercised from the tray of a running application or against the real update service; a stalled redirect was not tested separately. Working tree on `rust-rewrite` above `55e77c7`; not committed, not released, not checked in the installed application.

<a id="issue-029"></a>
## ISSUE-029: Malformed channel/process numbers silently narrow to other identities

- **Found:** 2026-11-06 final release audit. **Status:** Resolved in 0.5.0 Beta;
  focused validation/recovery regressions and all 19 native suites pass.
- **Verified:** Native JSON arithmetic conversion accepted a channel value of
  `4294967295` as `-1` (all channels), and `1.9` as `1`, before range validation.
  Process identifiers had the same narrowing risk. This was reproduced with
  inert configuration fixtures; no personal workspace or live process was changed.
- **Expected:** Invalid identities must be rejected before conversion. Preserve
  the original settings and backup for recovery; never select another identity.
- **Correction:** Require integer JSON tokens and check the original range
  before narrowing. Valid saved integers and old workspaces remain compatible.
  Fractional, overflowing, nonnumeric and negative process identifiers reject.
- **Verification:** Regression fixtures cover channel/process boundaries,
  read-only recovery, exact preservation of malformed originals and backups,
  older settings without new effect fields, and unknown node/port rejection.
- **Next step:** Retain the regressions. Final package qualification is recorded
  in [the release record](RELEASE_0.5.0_QUALIFICATION.md).

<a id="issue-030"></a>
## ISSUE-030: Many RNNoise nodes exceed the real-time processing budget

- **Found:** 2026-11-06 final audit. **Status:** Confirmed synthetic capacity
  limitation; no live dropout was reported or reproduced by this check.
- **Verified:** A private memory-only diagnostic with production Pro Audio
  scheduling and a 5-ms cadence tested 1, 4 and 30 RNNoise nodes. The 1- and
  4-node cases stayed within budget on the test machine. The 30-node case had
  a 9.20-ms median / 13.56-ms p99 during parameter transitions and an 11.59-ms
  median / 15.12-ms p99 during old/new routing fades. These measurements depend
  on the host and workload; they do not define a universal safe node count.
- **Expected/limit:** The 32-node graph limit is routing capacity. It cannot
  guarantee real-time operation for arbitrary expensive effect combinations.
  Earlier maximum-denoiser tests used SpeexDSP; a single-node RNNoise benchmark
  did not establish maximum-graph capacity.
- **Practical guidance:** Share one denoised signal before branching where the
  cleanup is the same; reduce expensive processing if the graph overruns.
- **Next step:** Investigate measured processing-budget feedback and scheduling
  before promising larger neural-effect workloads. Do not silently reduce the
  accepted graph limit or claim the current tests qualify every legal graph.

<a id="issue-028"></a>
## ISSUE-028: Short cables fold back instead of flowing directly between sockets

- **Reported/authorized:** 2026-11-06. **Status:** Resolved locally, offline
  verified and integrated for unreleased 0.5.0; maintainer visual follow-up pending.
- **Maintainer report:** A close Echo-to-Audio connection forms a tight zigzag
  even though a short, gently curved route is available. The supplied screenshot
  supports the visible defect; personal workspace imagery is not retained here.
- **Confirmed mechanism:** Three curve consumers independently force horizontal
  Bezier handles to reach at least 70 canvas DIPs. When a forward socket gap is
  less than that reach, the curve reverses horizontally near its midpoint.
- **Expected behavior:** Short cables should use proportionately short handles,
  changing continuously as nodes move. Preserve socket alignment, colour
  gradients, midpoint placement, occlusion and existing editing behavior.
- **Research, 2026-11-06:** ComfyUI's
  [Litegraph link adapter](https://github.com/Comfy-Org/ComfyUI_frontend/blob/4b24c6368cd9e713baf81ee5a02d7752e87e4ffd/src/renderer/core/canvas/litegraph/litegraphLinkAdapter.ts#L238-L287)
  uses one quarter of the Euclidean endpoint distance for ordinary spline
  handles, with no minimum. That is source evidence, not an interactive ComfyUI
  comparison. Apply the geometric principle independently in VineMix; no
  external code or dependency is imported.
- **Correction scope:** Share symmetric, distance-scaled control points between
  visible/preview geometry, cable-cut sampling and node-insertion sampling. Keep
  the recently selected 7.5-canvas-DIP midpoint diameter. Reverse and steep routes
  still need bends to meet the horizontal socket directions; this does not add
  obstacle avoidance or reroute nodes.
- **Verification:** 1,527 focused checks passed across cable presentation, hover
  bounds and connection model/interaction suites. Coverage includes close,
  aligned, vertical, reversed, degenerate and long routes; continuous movement;
  midpoint alignment; the removed fold no longer receiving hits/cuts; matching
  rewire previews; and existing insertion/rounding behavior. Detached close,
  tall and reverse screenshots were reviewed. Production build passed with an
  unchanged engine binary and a matching source/build receipt.
- **Local integration:** Guarded interface-only activation passed its eight-second
  observation with the same running engine and unchanged settings/preferences/
  routing. All 36 installed and 29 candidate payloads were verified, with matching
  source, build receipt and installed inventory. No engine restart or rollback.
- **Next step:** Maintainer visual follow-up and final release qualification.
  Related presentation work:
  [TWEAK-026](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/TWEAKS.md#tweak-026); [execution evidence](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/IMPLEMENTATION_0.5.0.md#short-cable-flow-correction--6-november-2026).

<a id="issue-027"></a>
## ISSUE-027: Maximum Distortion graph has insufficient routing-fade timing margin

- **Found/authorized:** 2026-11-06. **Status:** Optimized, offline verified and
  locally integrated for unreleased 0.5.0; maximum-graph performance remains
  dependent on the computer and background workload.
- **Context:** The maintainer requested a Distortion review after the Echo
  refinement. No live Distortion replay or dropout was reported or reproduced.
- **Verified audit gap:** Earlier maximum-graph timing tests excluded Distortion
  routing transitions, when both old and new graphs process during the 10-ms
  fade. New synthetic runs with 30 parallel Distortion nodes exceeded the 5-ms
  quantum in some measurements. Execution cost and host scheduling both vary;
  retain raw timings and report the scheduling context rather than interpreting
  every wall-time outlier as DSP work alone.
- **Expected behavior:** Bound processing work during normal processing and
  graph adoption/routing fades, preserve the selected sound/filter/latency, and
  qualify complete parameter transitions rather than just their first sample.
- **Implemented correction:** Reorder independent FIR work to reduce dependency
  stalls while retaining the `tanh` curve, all filter coefficients/taps, 8-times
  oversampling and 32-frame latency. Extend state/automation/alias/headroom tests,
  before/after waveform comparisons and combined transition timing. Measure with
  the production Pro Audio scheduling class as well as retain earlier raw runs.
- **Verification:** All 19 native suites pass, including 461 creative checks;
  one million stereo frames remain bit-identical to the prior implementation.
  The final 30-Distortion routing-fade combined p99 was 4.1845 ms; an earlier
  targeted pass measured 4.9125 ms against the same 5-ms budget. Retain this tight
  margin as a practical limit, not a guarantee for every device/workload. Local
  guarded integration passed without altering saved volumes or routing.
- **Next step:** Maintainer listening follow-up and final release qualification.
  See [Distortion research](EFFECTS_RESEARCH.md#distortion-refinement-2026-11-06)
  and the [execution report](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/IMPLEMENTATION_0.5.0.md). A synthetic pass does not
  establish hardware dropout-free behavior on every computer.

### Follow-up cloud capacity result — 6 November 2026

The released source's strict cloud run passed the 30-Distortion routing-fade
benchmark at 3.8406-ms median / 3.9621-ms p99. A later documentation-only main
commit, with identical native source and tests, failed that same 5-ms gate at
5.3432-ms median / 9.6201-ms p99. All other suites and the preceding 460 creative
checks passed. The median itself exceeds budget: this is a real capacity limit
on that host, not a single scheduling outlier or evidence of a code regression.
The exact CPU/contention cause is not established. Both complete reports remain
in private evidence; the failed run is not replaced by retries until green.

### Rust edition — 11 November 2026

The Rust edition's Distortion was redesigned ([ISSUE-050](#issue-050)) and no
longer uses this processor. One node measures about 28 ns per frame offline,
against 160 to 200 ns for the Rust port of the processor described here. The
30-node routing-fade benchmark above has not been repeated with the new node.

The 0.5.0 files and source tag remain unchanged. Hosted CI separates measured
capacity from correctness; strict 5-ms qualification remains the local default.
The report mode still executes every creative benchmark and all functional and
allocation assertions, and exposes each budget overrun explicitly. A passing
hosted functional run does not mean the measured real-time budget passed.

<a id="issue-026"></a>
## ISSUE-026: Echo Time changes replay stored repeat mixtures

- **Reported/authorized:** 2026-11-05. **Status:** Resolved locally, offline
  verified and integrated; maintainer listening follow-up pending for unreleased 0.5.0.
- **Maintainer report:** Increasing Time after speech can replay the already
  repeated phrase at the new interval, as though the earlier echo sequence were
  fresh input. Some settings feel laggy and nearly clip. Those listening
  descriptions are reported observations, not independently measured clipping
  or a confirmed audio-thread deadline miss.
- **Confirmed source mechanism:** The initial buffer stores a normalized mixture
  of original audio and previous repeats. Time edits crossfade two read positions
  in that same history, so the new tap can revisit earlier repeat mixtures.
  Avoiding a pitch sweep does not prevent that replay. Prior tests established
  finite/peak-bounded output and allocation behavior, not satisfactory audible
  retiming of an existing phrase.
- **Expected behavior:** A simple Echo should produce predictable repeats. A
  Time/Repeats edit should briefly fade the old effect and start fresh without
  replaying the previous tail; the original audio path remains immediate.
  Mix edits should not reset the delay. Preserve ordinary graph-edit continuity,
  stereo separation, bypass/mute behavior and real-time processing bounds.
- **Related refinement:** [IDEA-055](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/IDEAS.md#idea-055) owns the requested exact
  repeat count, clearer Mix guidance and separate echo-only output. Short delay
  coloration and Mix at 100% must be distinguished from processing lag.
- **Reproduction/correction:** A synthetic impulse reappears with the earlier
  Feedback zero implementation after changing Time 100→400 ms at 200 ms;
  Feedback 0.5 also replays an existing repeat sequence after a 100→500 ms edit.
  The refined implementation generates finite original-fed copies and fades/
  invalidates the old history on timing/count changes. Those regressions pass.
  The separate Echo output is independent of Mix. No hardware clipping was
  established from the original description; short-delay coloration and an
  echoes-only Mix setting remain distinct from processing lag.
- **Verification:** All 19 native suites pass, including 436 creative checks,
  maximum populated-history timing, no audio-thread allocation and deterministic
  engine budget/adoption coverage. UI, strict compatibility/acknowledgement,
  undo/routing and activation-comparator checks also pass. Large history copies
  were removed from the engine handoff and queued history is bounded.
  [Research and decisions](EFFECTS_RESEARCH.md#echo-refinement-2026-11-05);
  [measured evidence and limits](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/IMPLEMENTATION_0.5.0.md#echo-refinement-after-listening-feedback--2026-11-05).
- **Local integration:** Guarded paired update and actual shortcut launch passed
  the eight-second observation with settings/preferences/physical identity and
  running paths preserved, documented Echo migration only, and no rollback.
- **Next step:** Maintainer listening follow-up, followed by final combined
  0.5.0 release qualification.

<a id="issue-025"></a>
## ISSUE-025: Settings menu clips lower options despite available space

- **Reported:** 2026-11-04. **Status:** Resolved locally; offline verified and
  integrated for unreleased 0.5.0. Maintainer follow-up review remains pending.
  Accepted as a follow-up in the [0.5.0 plan](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/IDEAS.md#release-0-5-0-plan).
- **Maintainer report:** The Settings menu introduces unnecessary scrolling
  and leaves lower options out of view. This report is separate from the
  implementation pass's source and detached-render evidence.
- **Confirmed pre-fix evidence:** `MainWindow.CreateSettingsMenu` imposed a fixed
  `MaxHeight = 560` even when the available work area is taller. The detached
  `settings-menu.png` preview also visibly cut off lower options. Source review
  and the detached preview established the artificial height restriction; they
  were not independent live reproduction on the maintainer's desktop.
- **Correction:** Removed the unnecessary fixed cap. The ordinary Settings menu
  uses available vertical space; genuinely small work areas retain normal
  scrolling. Settings search, focus/keyboard navigation and option actions remain.
- **Verification, 2026-11-04:** The new regression failed before the fix. After
  correction, the full menu measured 380 × 585 DIPs with no scrollbar when
  720 DIPs were available. A constrained 360-DIP menu retained scrolling and made
  Quit fully reachable at 96 and 192 DPI. The final combined suite passed 14,726
  checks / 314 renders / zero binding errors. Guarded interface-only activation
  passed without restarting the engine or changing current settings/preferences;
  eight-second observation and installed-inventory guards passed without rollback.
- **Next step:** Obtain maintainer follow-up on the integrated menu and retain
  both ordinary/constrained-height regressions. No settings values, audio behavior,
  release identity or publication change is part of this layout correction.

<a id="issue-024"></a>
## ISSUE-024: Undo history resets after a virtual-input Level adjustment

### Maintainer-reported manual result — 2026-11-04

The maintainer reports that decimal-edit undo passed during the installed
candidate's manual check. This is a reported result, not independently observed
reproduction of the original warning or its exact Level value. Retain the
production-boundary regression and monitor recurrence; the original incident's
precise input remains unconfirmed.

### Implementation and verification — 2026-11-04

**Current status:** Precision defect corrected, offline verified and locally
integrated for unreleased 0.5.0; original live incident unreplayed. Stored native
node/cable gains now retain double precision, with explicit float conversion at
the compiled DSP boundary. The
production serializer/desktop acknowledgement-history regression executed 710
checks: 156 failed before the fix, and all passed after. Expanded status coverage
passes 712 checks; 48,030 synthetic comparisons confirm unchanged DSP plans and
rendered samples. Genuine conflicting edits still invalidate incompatible history.
CI runs the new regression. The precision mechanism was reproduced through the
real production boundary; the original personal live incident and its exact
Level value have not been replayed. Guarded local installation passed without
tests adjusting personal volume controls or routing. Combined
qualification is recorded in [the execution report](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/IMPLEMENTATION_0.5.0.md).
Earlier investigation and acceptance notes below are retained as dated evidence.
Next step: retain the production-boundary regression and monitor ordinary
editing for recurrence. Local integration does not establish the original
incident's exact input or publish a release.

### 0.5.0 acceptance criteria clarified — 2026-11-04

- **Status at planning:** Planned; the maintainer requested inclusion of the investigated
  fixes and qualification work in 0.5.0. This extends the plan, not authorization
  to implement or a claim that a correction has passed.
- **Preferred implementation:** Preserve double precision for stored native
  node and cable gain settings through configuration, serialization and history;
  convert explicitly only where DSP requires float. The earlier canonicalization
  alternative remains historical design context, not a shortcut to broad
  approximate node comparisons. Any change of approach needs an explicit
  precision/legacy-data rationale and the same acceptance evidence.
- **Completion criteria:** Ordinary decimal Level edits must survive actual
  native acknowledgement, save/reload and undo/redo without losing compatible
  history. Cover node and cable gains, precise entry and drag edits, limits,
  older workspaces, repeated round trips, queued edits, undo while saving, lost
  acknowledgements and reconnects. Genuine independent name/device/gain changes
  must still be detected, preserving the latest state. Audit other mismatched
  numeric fields and qualify DSP conversion/sound compatibility; changing the
  two field types alone is not sufficient completion evidence.
- **Required regression:** Use production C# and C++ serialization and the real
  acknowledgement/history path, with a failure on the current defect and a pass
  after correction. A C# copy or simulated float cast alone does not establish
  that boundary. Add coverage to CI and retain separate isolated engine IPC and
  live interaction qualification. Supporting test and refactoring work is in
  [IDEA-054](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/IDEAS.md#idea-054). Preserve personal-engine isolation safeguards.

### Investigation and planned correction — 2026-11-04

- **Status at investigation:** A concrete precision-conflict mechanism is reproduced in
  isolation; correction planned for 0.5.0. The original live incident has not
  been replayed and its exact Level value is unknown. Earlier report below is
  retained as the evidence available before investigation.
- **Finding:** Desktop `GraphNode.GainDb` and `GraphEdge.GainDb` are `double`,
  while native `GraphNode::gainDb` and `GraphEdge::gainDb` in `types.hpp` are
  `float`. `configuration.hpp` serializes those native values. History compares
  complete serialized node/edge values exactly. On acknowledgement,
  `AcknowledgePending` removes accepted edits before `Accept` checks the returned
  graph against history. A harmless precision conversion can therefore fail
  compatibility and produce the reported warning without another editor.
- **Isolated evidence:** A temporary harness outside the checkout compiled the
  unchanged production models, history, graph validation, frame and virtual
  binding helpers. It simulated the native float conversion with
  `(double)(float)value`, without opening the app or contacting an engine.
  Across Virtual input, Physical input and Mix, -6 and -6.5 remained compatible;
  -6.1, -0.3 and +1.2 became incompatible in all three cases (9 failures out of
  15 returned-value cases). All 15 unconverted controls remained compatible;
  a real node-name conflict was correctly rejected. For example, -6.1 becomes
  -6.099999904632568. This establishes a history-layer defect, not a full native
  JSON/IPC or pointer-driven reproduction of the maintainer's session.
- **Proposed fix:** Align gain precision across configuration, serialization
  and history. Prefer retaining double precision in stored native node/edge
  gain settings, with explicit conversion only where DSP needs float, subject
  to round-trip and sound-compatibility checks. An alternative is narrowly
  defined canonicalization to the existing native representation; that needs
  explicit treatment of precise entry and legacy settings. Do not introduce
  broad approximate comparison of whole nodes or disable conflict checks.
- **Scope:** Node and cable gains, configuration read/write and acknowledgement
  history; audit other mismatched numeric fields before claiming full coverage.
  No evidence points specifically to the virtual cable driver. The UI's generic
  conflict wording also needs clearer differentiation from an unsaved edit.
- **Qualification/next step:** Reproduce through real native serialization and
  an isolated queued-edit/acknowledgement path, then cover decimal drags, precise
  entry, cable gains, undo/redo, in-flight edits, reconnect and genuine concurrent
  node/device changes. Retain latest state on conflict. Verify saved values and
  old workspace compatibility. No fix, installation or live test in this pass.

- **Reported:** 2026-11-04. **Status:** Reported; warning path confirmed in
  source, exact incident not independently reproduced.
- **Reported behaviour:** The maintainer adjusted only the level of the virtual
  audio cable input and saw: "Attention: Undo and redo were reset because the
  workspace changed. Another change affected a node or connection in the edit
  history." The supplied cropped screenshot confirms the displayed warning;
  it does not establish the preceding edit sequence or saved level.
- **Expected behaviour:** An ordinary Level adjustment should remain undoable
  and preserve earlier compatible history. Genuine conflicting changes should
  be handled safely with an accurate explanation.
- **Source evidence:** `MixerViewModel.Accept` checks history compatibility after
  refreshing/rebasing configuration and handling automatic device assignments.
  A failed `EditHistory.IsCompatible` check emits this exact detail through
  `ResetHistoryAfterConflict`, which clears undo/redo history and sets the warning.
  That reset method does not reset the graph or stop the audio engine.
  `WorkspaceGraphRestore` compares the complete affected node, including its
  device binding, rather than just the Level value. The normal Level control
  participates in edit gestures and history recording.
- **Suspected area, not established cause:** Reconciliation between a local level
  edit, its history snapshot and an engine configuration refresh. The wording
  alone does not prove another user/application changed anything, a device was
  reassigned, or the engine rejected the level. Whether the level persisted is
  unverified. No live settings or processes were inspected or changed.
- **Next step:** Reproduce in an isolated workspace with a virtual-input node;
  capture sanitized field differences when compatibility fails, covering edits
  during engine acknowledgements and device discovery. Determine why an ordinary
  edit is classified as incompatible before changing history safeguards.
  Logging this report does not authorize implementation or live activation.

<a id="issue-023"></a>
## ISSUE-023: Update rolls back after a successful package check

- **Reported and verified:** 2026-10-28. **Status:** Resolved in the corrected
  0.4.0 Beta worker; older installed workers require direct Setup once.
- **Reported behavior:** An installed Beta 6 downloads 0.4.0 Beta, restarts and
  still displays Beta 6.
- **Expected behavior:** A successful installation and package check continue
  through the candidate health check and retain the new release.
- **Verified evidence:** The installer completed successfully and the inert
  package check reported `passed: true`, version `0.4.0-beta` and Installed mode.
  The old update worker then reported “The process image cannot be identified”
  before recording the check process, and restored the previous application
  payload. The journal confirms that personal settings were not replaced.
- **Source-confirmed defect:** The worker reads `Process.MainModule` immediately
  after launching a short-lived child. The image can be unavailable during
  startup or after exit. The initial evidence did not distinguish those timings;
  subsequent legacy-worker checks demonstrated an early startup failure.
  Existing fake-platform tests did not exercise this production process lookup.
  The same pattern was present in the original published 0.4.0 worker.
- **Workaround:** Quit VineMix from its tray menu, run the published 0.4.0 Setup,
  then reopen VineMix normally. This briefly interrupts audio. Keep the current
  workspace and recovery backups; do not replace settings or alter update
  transaction records. Package checks and rollback safeguards remain enabled.
- **Release decision:** The
  maintainer explicitly requested replacement 0.4.0 Beta assets and matching
  source on 2026-10-28 rather than a 0.4.1 release. The original assets and source
  identity are preserved in private release evidence. Existing 0.4.0 installs
  require a manual reinstall because their semantic version remains unchanged.
- **Local recovery:** The original published Setup subsequently installed all
  37 expected payload files successfully after a graceful Quit. A real Explorer
  shortcut launch reopened app and engine version `0.4.0-beta` / `0.4.0.0`;
  saved settings retained their hash and physical file identity. This confirms
  manual recovery, not a successful automatic update or delivery of the fix.
- **Source correction:** Process identity now comes from the retained process
  handle and the native image-path query, including immediate health/recovery
  checks. A child proven to have exited keeps its real exit code and required
  result checks; a candidate that exits before health confirmation still fails.
  The focused suite passes **327 checks** with real live and completed children,
  image/PID/start-time/hash guards and rollback coverage. Successful inert package
  checks remain alive for at least two seconds for older workers; this bounded
  compatibility window does not alter ordinary startup.
- **Published and installed:** The corrected same-version release uses source
  `21516dcf827dc157fb154287c016f02a014fc3b2`. All five replacement assets and
  signed metadata, matching tag, source CI and published-asset CI passed. Three
  production-worker signed package smoke runs passed. Manual installation
  verified all 37 payload hashes and an actual Explorer launch of the corrected
  app and engine with unchanged settings hashes and physical file identity.
- **Legacy limitation / next step:** The old worker passed 11 of 12 corrected
  installed-package smoke attempts; the remaining failure occurred at 4.56 ms,
  before child initialization. The two-second lifetime is a bounded mitigation,
  not a complete fix for an already installed old worker. Use direct corrected
  Setup once for Beta 5, Beta 6 and original 0.4.0 installations; the latter also
  cannot discover a same-version correction as newer. Full delivered automatic
  update, health, rollback and interruption scenarios on a separate Windows
  account remain to be qualified. See the
  [corrected qualification record](RELEASE_0.4.0_QUALIFICATION.md).

<a id="issue-022"></a>
## ISSUE-022: Selected nodes fall behind overlapping nodes

- **Reported:** 2026-10-28. **Target:** Beta 6. **Status:** Resolved locally and
  integrated into the installed testing app on 2026-10-28; not published.
- **Reported behavior:** The most recently selected node does not reliably
  remain above overlapping nodes, making its controls harder to see or reach.
- **Expected behavior:** Selecting a node brings it above other nodes and keeps
  it there after a click or drag. Selecting another node then brings that node
  to the front. Groups remain behind node and cable controls; selection order
  must not alter audio, node positions or routing.
- **Source-confirmed cause (2026-10-28):** `GraphWorkspace.BeginNodeMove` raised
  dragged members to Z-index 2, while `FinishNodeMove` reset them to 0. Ordinary
  selection did not retain a front order, so after release overlapping nodes
  returned to their canvas child order. This identifies the implementation cause
  separately from live pointer reproduction.
- **Implemented correction:** Retain a consistent session selection order for node visuals,
  including click, movement and selection changes, without changing saved audio
  configuration or automatic frame bounds. This is separate from the requested
  group appearance/background refinement in [TWEAK-017](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/TWEAKS.md#tweak-017).
- **Verification:** The final detached suite passed 13,526 checks with 273 renders
  and zero binding errors. Focused checks retain recent selection and drag/drop
  order through telemetry, view recreation, zoom, cancellation, multi-selection
  and group movement. Real-pointer checks of this correction were not completed.
- **Local integration:** Production build, inert controller smoke, 97 UI-only
  and 40 shared safeguard checks passed. Trusted-shell activation and the actual
  Explorer shortcut launch passed without rollback, preserving the same engine
  process and binary, advancing frames and running paths. The additional
  eight-second check passed. Saved settings/preferences were unchanged; physical
  settings identity and installed inventory were verified. Evidence is in
  [Beta 6 development](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/BETA6_DEVELOPMENT.md#group-refinements-and-node-stacking--28-october-2026).
- **Next step:** Maintainer review with overlapping nodes in the running app,
  including real pointer/VR use, then final Beta 6 release qualification when
  authorized. Local resolution does not imply publication or finalization.

## ISSUE-021: Refresh flashes the expanded logo and resizes the scrollbar

- **Date:** 2026-10-27.
- **Reported and reproduced:** Refresh briefly displayed the expanded hero logo
  before the compact-symbol/RSVP sequence. The scrollbar thumb changed as the
  desktop story and pipeline initialized.
- **Expected:** The first rendered logo matches the animation's starting pose;
  initialization does not change the sections' reserved space.
- **Cause verified:** Deferred reader setup changed the SVG pose, the story
  acquired its 480svh desktop height after load, and the pipeline replaced a
  short fallback with an asynchronously styled and populated shadow tree.
- **Resolution:** Early CSS supplies the compact pose for normal-motion script
  users and reserves the eligible desktop story height. The build includes the
  styled pipeline shell in the initial HTML; the controller adopts it and fills
  its existing stage. No-script and reduced-motion views retain static content.
- **Verification:** All nine check suites pass, including a new shell-adoption
  regression. At 1280×720, refresh samples kept document height at 7732px,
  story height at 3456px and pipeline height at 770.1875px across initialization
  and node loading. Mobile, reduced-motion and no-script views were checked.
- **Status:** Published to `vinemix.app` on 2026-10-27, Cloudflare deployment
  `7768eb10`. All 84 public files match the reviewed local build; the previous
  deployment remains available. Live refresh samples confirm stable styled
  geometry across reader setup and pipeline loading, and the compact initial
  logo was visually verified. Section navigation, pipeline rendering and the
  `vinemix.dev` redirect passed, with no captured browser warnings/errors.
- **Next step:** Maintainer visual review on the live website.

### Shader startup follow-up — 2026-10-27

- **Reported:** The background effect also appears large and then shrinks on
  refresh. The static preview uses `background-size: cover`, whereas the live
  shader scales its orb by the smaller canvas dimension; their handoff changes
  the apparent size at different screen proportions.
- **Correction:** Script-enabled startup keeps the plain background until the
  measured first GPU frame is drawn, then reveals it with a 180ms opacity fade.
  The shader geometry, speed and palette are unchanged. No-script and WebGL
  failure retain the poster; reduced motion disables the reveal transition.
- **Verification:** Ten check suites pass, including first-frame readiness,
  matching viewport/uniforms and missing/lost WebGL context regressions. Browser
  refresh samples show a constant 1257×715 drawing surface throughout the
  desktop fade. Mobile, reduced motion and no-script fallback checks pass.
- **Status:** Published and verified at `vinemix.app`, deployment `278ccbdd`.
  Both changed public assets match the local build. The live shader reports
  readiness at its measured size and fades in without displaying the poster.

### Header startup follow-up — 2026-10-27

- **Reported:** The top header logo briefly appears compact and centered before
  expanding and moving left on refresh.
- **Evidence:** Chrome refresh sampling exposed an intermediate 26px scroll
  offset before restoring 0px. The floating controller initialized immediately
  during module evaluation and could treat that offset as real scrolling.
  The exact visible race varies with resource timing; a regression fixture
  reproduces the transient offset and subsequent top restoration explicitly.
- **Correction:** Preserve the normal left-aligned header during loading. Mount
  the floating enhancement on the first animation frame after `pageshow`, when
  native scroll restoration has completed. Start at the settled pose without
  changing the user's scroll position. Navigation initializes immediately.
- **Verification:** All ten suites pass, including transient startup scroll,
  restored deep positions and cancellation before initialization. Three Chrome
  cold/cached refresh checks retain the expanded pose at the top. Refreshing at
  Downloads restores the compact pose; return-to-top still works. No captured
  browser warnings/errors.
- **Status:** Published at `vinemix.app`, deployment `2aed1065`. All 84 public
  files match the verified build. Live Chrome sampling reproduced the temporary
  26px offset, then 0px; the SVG retained its original expanded pose until the
  floating controller initialized expanded at the left after load. No captured
  browser warnings/errors. Mobile startup was also checked at 390px width.

### Page movement on refresh — 2026-10-27

- **Reported:** The page still moves up and down during refresh after the header
  pose correction.
- **Verified cause:** Before external styles apply, the browser's default body
  margin and the in-flow skip link place `main` 26px below the top. Chrome samples
  show a temporary 26px scroll position and then `main` at -26px before native
  restoration returns to 0. The header-only fix did not remove that page offset.
- **Correction:** Inline the small body-margin/skip-link positioning reset in
  the head. Disable scroll anchoring only during startup so initial layout
  changes do not compete with native history restoration; restore ordinary
  anchoring on the first animation frame after `pageshow`. No forced scroll or
  change to history-restoration settings is used.
- **Verification:** Four cold/cached Chrome refresh runs keep both scrollY and
  the main top at 0 throughout. A refresh at Downloads restores the same
  9823.333px position, mobile starts at 0 without horizontal overflow, and the
  no-script keyboard skip link reaches its native destination. Ordinary scroll
  anchoring returns to `auto`. All ten existing check suites pass.
- **Status:** Published at `vinemix.app`, deployment `035c051a`. All 84 public
  files match the verified build. Three live Chrome refresh runs, including a
  cache-bypassing refresh, keep both scrollY and the main top at 0 throughout.
  Startup anchoring protection clears correctly; no browser warnings/errors
  were captured. Maintainer visual review remains the next step.

<a id="issue-018"></a>
## ISSUE-018: Website release refresh stalls after reconnecting

- **Found:** 2026-10-27 during the authorized website audit. **Status:** Resolved
  in local website source/output; no published release or deployment.
- **Expected:** Restoring connectivity should promptly retry failed release
  discovery while retaining functional saved or previously checked downloads.
- **Verified:** Regression fixtures reproduced the attempt throttle discarding
  a reconnect, and a reconnect being lost during a still-pending failed request.
- **Correction:** Bypass the throttle on reconnect and retain one pending retry
  after failure. Successful pending requests avoid duplicate discovery; hidden
  pages defer the retry until visible. A failed retry does not loop.
- **Verification/next:** Release regression and browser fallback checks pass.
  Keep these checks in future changes; live service availability remains separate.
  See the local website audit.

<a id="issue-019"></a>
## ISSUE-019: Pipeline loading can outlive a disconnected component

- **Found:** 2026-10-27 during the authorized website audit. **Status:** Resolved
  and verified locally; no public deployment.
- **Expected:** Removing or reconnecting the pipeline must cancel obsolete work
  and keep only the current instance's observers and animation clock.
- **Verified:** A fixture reproduced an already-resolved fetch finishing JSON
  parsing after disconnect and mounting observers. Old queued observer callbacks
  could also act after cleanup. Hidden width could produce infinite SVG sizing.
- **Correction:** Check connection-specific cancellation at asynchronous and
  observer boundaries; defer sizing until the scale is finite and positive.
- **Verification/next:** Lifecycle, stale success/error, reconnect, observer and
  hidden-layout regressions pass; the normal pipeline loads in browser checks.
  Preserve these checks with future embedding changes. See the
  local website audit.

<a id="issue-020"></a>
## ISSUE-020: Website Download and keyboard skip miss destination focus

- **Found:** 2026-10-27 during the authorized website audit. **Status:** Resolved
  and verified locally; no public deployment.
- **Expected:** Explicit section shortcuts should use the shared navigation
  policy, including destination focus and the pinned-story bypass.
- **Verified:** The Download and keyboard skip anchors lacked the navigation
  marker. A browser Download activation reached the section but left active
  focus on the document body rather than the destination.
- **Correction:** Connect both anchors to the existing handler. Their real
  fragment destinations remain available without JavaScript.
- **Verification/next:** Browser checks cover both destination focus paths,
  history, other section shortcuts and JavaScript-disabled navigation. Keep
  equivalent checks when adding shortcuts. See the local website audit.

<a id="issue-017"></a>
## ISSUE-017: Header logo jitters when expanding

- **Reported and investigated:** 2026-10-27. **Status:** Corrected and verified
  locally on port 4175; no public deployment.
- **Report and expected behavior:** Collapse looks correct, but the header logo
  jitters slightly when expanding. Expansion should return smoothly to the
  header without changing the approved collapse motion or timing.
- **Verified evidence:** A local browser frame trace of a direct return from
  y=400 to y=0 showed a roughly 19 px vertical jump in the symbol at the first
  animated paint. Horizontal anchor/artwork positions remained monotonic. The
  anchor's vertical position followed raw scroll while its horizontal movement
  and retraction were eased. Small reverse scrolls also lifted the compact
  symbol before expansion began. This is a confirmed position discontinuity;
  it does not establish the cause of every possible rendering artifact.
- **Correction:** Keep the compact symbol docked until expansion starts, then
  ease from its last painted height to the header using the existing animation
  clock. Resizing preserves an expansion already underway; reduced motion and
  restored pages still resolve directly to their intended position.
- **Verification:** A comparable browser trace reduced the first visible
  vertical step from about 19 px to 4.6 px, followed by a monotonic return.
  Desktop/mobile checks confirm that a pause at y=12 keeps the symbol docked
  and y=0 restores the complete wordmark. Regression tests preserve the exact
  collapse and horizontal expansion samples, including their settling time;
  interrupted movement, resize and lifecycle checks pass. Build and the full
  website check suite pass.
- **Expansion-delay follow-up, 2026-10-27:** The maintainer then reported that
  expansion started too late. The previous return trigger waited until the
  page was within 1 px of the top. It now starts 2 px below the existing
  collapse threshold, preserving a small stable gap while responding as soon
  as the header trigger is crossed upward. This supersedes the earlier
  requirement to remain compact at y=12. The smooth vertical return and
  animation timing are retained.
- **Follow-up verification:** Desktop and mobile browser checks show expansion
  beginning on the first animation frame before reaching the top, then
  completing without further scrolling. Regression checks cover both sides of
  the trigger gap and retain the exact collapse/expansion timing samples.
- **Next:** Maintainer visual review of the earlier expansion capture.

<a id="issue-016"></a>
## ISSUE-016: Open-source button GitHub icon does not invert on hover

- **Reported and verified:** 2026-10-27. **Status:** Resolved locally on port
  4175; no public deployment.
- **Expected:** The GitHub icon should invert with the button label during the
  white hover fill, retaining its existing size and scale.
- **Confirmed cause:** The icon kept a fixed dark backing and white filter,
  while only the label used difference blending. Browser hover reproduced the
  unchanged white icon on its dark disc.
- **Correction and verification:** The icon wrapper now uses a transparent
  backing and the same difference blending as the label. Browser checks confirm
  inversion on hover and keyboard focus. The 20 px artwork, 26 px wrapper and
  194.15625 × 54 px desktop button dimensions are unchanged. Build passed.
- **Next:** Maintainer visual review of the local hover capture.

<a id="issue-015"></a>
## ISSUE-015: Scroll for more remains visible after scrolling

- **Reported and verified:** 2026-10-27. **Status:** Resolved in the local
  website preview on port 4175; no public deployment or desktop release.
- **Report and expected behavior:** The landing cue remained visible while
  scrolling. It should disappear when leaving the top and return at the top.
- **Reproduction and cause:** Before correction, browser scrolling to y=108
  left the cue visible. Its visibility depended on the hero remaining more
  than 20% visible, so it persisted during the beginning of the scroll.
- **Correction:** Visibility now follows actual page position, allowing 1 px
  for fractional settling at the top. The scroll handler updates it immediately;
  page restoration and visibility changes also refresh it. Native anchors,
  click/keyboard navigation and destination focus are retained.
- **Verification:** Desktop and mobile browser scrolling hides the cue, and
  returning to y=0 restores it. The landing control still reaches How It Works;
  Skip reaches the pipeline. Build and existing navigation checks pass.
- **Next:** Maintainer review of the locally hosted result. The associated
  visual refinement is recorded in [TWEAK-009](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/TWEAKS.md#tweak-009).
- **Fade refinement, 2026-10-27:** The maintainer subsequently found the
  immediate disappearance too abrupt. The landing cue now eases to zero
  opacity over the first 120 px, restoring on reverse scrolling. Skip fades
  with the progress bar at the walkthrough's end. Both retain layout and
  become inert when fully faded. Local browser checks verified intermediate
  opacity, restoration and focus removal; reduced motion/high contrast retain
  immediate visibility changes. This supersedes the 1 px visibility threshold
  above for ordinary motion settings.

<a id="issue-014"></a>
## ISSUE-014: Landing action contents and metadata drift from the selected design

- **Reported and reviewed:** 2026-10-26. **Status:** Earlier two-action layout
  superseded locally; current visual review pending. No public deployment.
- **Latest direction:** The maintainer removed hero Documentation and selected
  one centred **Download** action, now using the selected Flow Hover treatment:
  transparent at rest with a sweeping white fill and stationary download icon/
  label. This supersedes the earlier sliding-label variants. Docs
  remains in the header, now a compact 36 px white pill with black text and arrow.
  The platform/beta note beneath Download remains removed. The original
  two-button mismatch and its corrections below are historical; the new layout
  has no post-change visual verification yet. See [TWEAK-012](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/TWEAKS.md#tweak-012).
- **Report:** The maintainer still found the landing buttons uneven after the
  equal-size change in [TWEAK-012](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/TWEAKS.md#tweak-012), and supplied a Chrome
  screenshot of the implemented page.
- **Verified pre-correction evidence:** The screenshot shows equal 216 × 54 px
  outer action boxes at the same vertical position. Their contents differ:
  Download includes a GitHub icon, while Documentation has no icon. The Windows/
  beta note has a filled pill treatment that can read as a third, smaller
  button; the selected concept uses plain text. Source confirms those omissions
  from the selected composition. The current header is visible, so a stale-page
  explanation is not established. The user's broader uneven-appearance report
  is not dismissed by equal outer dimensions.
- **Earlier expected behavior and follow-up:** Both actions use a matching icon-and-label
  arrangement inside equal boxes, with plain platform/beta text beneath them.
  The maintainer subsequently said Documentation still looked thinner and
  supplied a reference requesting a download arrow ending at a line, an open
  book, and rounder edges. This supersedes the initial rectangular/GitHub-icon
  treatment; perceived thinness is not proof of unequal outer dimensions.
- **Earlier local corrections:** An interim shared 18 px icon/154 px content grid was
  followed by the requested 20 px download-arrow and book icons in matching
  160 px content grids. Both buttons retain equal outer sizing and use 2 px
  border geometry with pill rounding. An interim stronger Documentation outline
  was superseded by the maintainer's explicit correction: remove that outline,
  because it made the action look smaller. Its default and hover borders are now
  transparent, retaining equal dimensions and the keyboard-focus indicator.
  The platform/beta note is plain text.
- **Earlier verification:** Build and source checks passed for equal icon/content
  tracks, button dimensions, borders and absence of a pause control. Homepage,
  CSS and both icons returned HTTP 200 and match built files on both local host
  addresses at port 4175. Earlier CSS/font checks
  established calculated fit only; they did not establish browser visual
  balance. The supplied screenshot documents the pre-fix state, not the
  corrected result. Keep only this sanitized evidence summary in the repository;
  private screenshots and reference crops are not copied or linked here.
- **Outline correction checks:** Rebuild passed; CSS served through both local
  host addresses matches the build. Default/hover border transparency, unchanged
  sizing and retained focus-visible styling were checked. No new visual check.
- **Next step:** Capture and compare the corrected desktop and phone views
  when browser access returns. Track the outstanding visual review in
  the local website QA notes and
  design review notes.

<a id="issue-013"></a>
## ISSUE-013: Low / Mid / High and Ducking website meters do not animate

- **Reported, confirmed and locally resolved:** 2026-10-25. **Status:** Resolved
  locally in `website-redesign`; maintainer review remains. The maintainer
  reported that the Low / Mid / High and Ducking nodes lacked the animated
  meters shown on the other active nodes.
- **Confirmed cause before correction:** The website markup included meter
  overlays only for Physical input, Running app and Virtual output. Its meter
  controller likewise produced and painted only those three signals. Low / Mid /
  High and Ducking remained static native images, so the missing motion was an
  implementation omission rather than evidence of missing audio.
- **Expected:** Low / Mid / High follows the illustrated voice signal. Ducking
  shows the processed music level, falling under speech and recovering in the
  gaps. Both use silent visual simulation while visible. Listening output stays
  muted and still; preserve the approved voice rhythm and music pulse.
- **Local correction:** Added native meter overlays to the two effect nodes
  and connected them to the existing visual meter clock and routing signals.
  The Low / Mid / High meter follows the microphone; the Ducking meter shows
  music after the simulated reduction. Native WPF meter strips were rendered
  without opening application windows, starting the engine or loading settings;
  all 33 existing assets remained byte-identical.
- **Verification:** Build and full checks pass, including the unchanged voice
  and 120 BPM music samples, five overlays, and pause/offscreen/hidden-effect
  behavior. In 100 browser samples over five seconds, all five active meters
  moved. Tone differed from the microphone by at most 0.0056 dB; Ducking was
  4.22–12.07 dB below the music meter during speech and recovered to within
  0.0002 dB in a sampled quiet interval. These are simulated display values,
  not live audio measurements. Both new images loaded, with alignment error
  below 0.12 CSS px; all five meters held unchanged for 450 ms when paused.
  Existing tone-frame changes remained intact. Listening has no meter overlay,
  and the page has no audio/video elements.
- **Next step:** Maintainer review in the local preview. This correction has
  not been published as a website or desktop release. Current evidence and
  limits are in the local scroll QA notes.

<a id="issue-012"></a>
## ISSUE-012: Scroll-story main heading is partly clipped

- **Reported and locally corrected:** 2026-10-25. **Status:** Local correction
  verified in `website-redesign`; maintainer review remains.
  During local review, the maintainer reported that “See where your sound goes”
  was partially cut off as the second section appeared.
- **Expected:** Each main heading and its description remain fully readable
  throughout entry, pinned scrolling and responsive resizing, including after
  fonts load.
- **Evidence before correction:** The source reserved a fixed 130 px heading
  region and positioned all chapter copies absolutely inside it. The initial 974×873 local
  browser view did not reproduce the reported clipping. The fixed allocation
  is a plausible sizing weakness, not a confirmed cause of the reported case.
- **Local correction:** Size the shared heading region from its chapter
  content and use that measured height when fitting the node scene. Remove the
  three small chapter headings as separately requested in
  [TWEAK-009](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/TWEAKS.md#tweak-009).
- **Verification:** All three headings remained fully inside their heading
  region and clear of the diagram at 1440×900, 1280×720 and 900×680. The region
  now measured 115, 102 and 98 px respectively, fitting its actual text instead
  of reserving a fixed height. The actual 974×873 desktop screenshot showed
  full headings; 390×844 and 1280×560 fallback checks found no horizontal
  overflow. Forward/reverse scrolling and build checks passed. These results
  verify the revised local layout, not the cause of the original clipping.
- **Next step:** Maintainer review at the originally affected viewport. No
  published website or desktop release has been changed. See
  the local scroll QA notes for current evidence and limits.

<a id="issue-011"></a>
## ISSUE-011: Local UI activation helper rejects a newly opened controller

### Implementation and verification — 2026-11-04

**Current status:** Hardening implemented and verified offline and through
guarded live activation for unreleased 0.5.0; the original incident cause remains unknown.
Source hardening now uses a retained native process handle and one verified
identity token, bounded retries for temporarily unidentified candidates, and
immediate rejection of genuine mismatches. Callback and rollback decisions use
the captured identity with revalidation. All 51 new synthetic/inert-child checks
and 97 existing fixture-only UI activation checks pass. Authorized paired
activation through the trusted Windows shell and actual shortcut passed: engine
readiness took 0.247 seconds, all 10 previously running paths returned, and
frames advanced. That timing is not an end-to-end audio interruption measurement.
The later interface-only correction preserved the same engine process and hash;
its eight-second observation passed. Physical settings and each update's
then-current saved settings/preferences were verified, with backups of both
settings views retained outside the checkout. Newer workspace edits between updates
were preserved. Final payload hashes pass 36/36 and source receipt/inventory agree.
See [the execution report](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/IMPLEMENTATION_0.5.0.md) for full evidence.
Next step: retain the guarded identity checks and capture sanitized field-level
evidence if rejection recurs. Successful activation does not identify the
original incident's exact failing field.

### Investigation and planned hardening — 2026-11-04

- **Status at investigation:** Investigated; hardening planned for 0.5.0. The original
  incident's exact failing identity field remains unknown. This is development
  activation tooling, not a new defect in the corrected published updater.
- **Source finding:** `Test-DesktopControllerCandidate` in
  `scripts/assert-desktop-data-context.ps1` already rechecks exit state when
  `.Path` is missing/mismatched, skipping a normalization launcher that exited.
  However, a still-live candidate with an unavailable path is immediately
  rejected as unexpected. Session and launch-time mismatches share that message.
  The verified-process callback also reads `.Path` again, rather than retaining
  one verified identity. `activate-local-ui.ps1` has additional process captures
  that should be audited as part of the same change.
- **Isolated evidence:** Called the unchanged candidate function with six fake
  process objects, without launching or inspecting personal processes. A matching
  candidate passed; an exit during path read was skipped; missing live path,
  wrong path, wrong session and old start time were rejected. This confirms
  current branches and the missing-path failure, not that a real startup race
  caused the original incident. Existing activation tests already cover these
  branches; they do not prove real startup image-query reliability.
- **Proposed fix:** Obtain executable identity through a retained process handle
  and `QueryFullProcessImageNameW`, following the existing corrected pattern in
  `desktop/VineMix.Desktop/UpdateTransaction.cs`. Return one verified token for
  callbacks and rollback. Distinguish exited, temporarily unidentified and
  positively mismatched candidates. Permit only bounded reinspection of an
  unidentified candidate within the existing startup deadline; never accept it
  or register it for stopping/rollback until fully verified. Real mismatches
  continue to fail immediately.
- **Safeguards:** Preserve executable/hash, PID/start-time/session, unique
  controller and real workspace-window checks. Keep backup/stability checks,
  physical settings identity from an open handle, release identity and actual
  Explorer/shortcut launch. Do not restart the engine for UI-only activation.
  Sanitized diagnostics should identify the failing field and native error,
  while raw paths and process evidence remain outside the checkout.
- **Primary source checked 2026-11-04:** Microsoft documents
  [QueryFullProcessImageNameW](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-queryfullprocessimagenamew)
  as a process-handle image query supporting limited-query access. The API is a
  suitable alternative to module enumeration, not a guarantee every query will
  succeed. Proposal needs explicit failure and exit handling.
- **Qualification/next step:** Isolated real child-process tests for immediate
  startup, fast exit, unavailable identity, multiple candidates and PID reuse;
  wrong-path/session/start-time checks must still reject. Qualify actual shell
  launch separately under existing live-update safeguards when implementing.
  No activation script was run and no application process was changed here.

- **Observed:** 2026-10-24. **Status:** Confirmed local tooling failure; exact
  startup race remains unconfirmed. This helper is separate from the released
  in-app updater.
- **Verified behavior:** The helper replaced the UI and installed inventory,
  then reported an unexpected controller while reopening through the actual
  Windows shortcut. It correctly refused to stop an unrecorded process during
  recovery. Independent read-only checks confirmed the expected new UI was
  responsive, its files matched the candidate, and the original audio engine,
  physical settings location, saved workspace copies and preferences survived.
- **Expected:** Recognize the intended new controller during startup while
  continuing to reject another executable, session or pre-existing process.
- **Suspected cause:** The helper reads PowerShell's `Process.Path`, which
  depends on `MainModule.FileName`. Image information may be unavailable during
  early startup or launcher exit. The rejected PID and mismatching field were
  not captured, so this explanation is not a confirmed reproduction.
- **Next step:** Capture sanitized field-level diagnostics and exercise early
  startup/launcher exit. Evaluate querying the process image by its handle,
  preserving all session, start-time, live-process and rollback guards. Keep raw
  evidence outside the checkout; no additional live restart is required now.

<a id="issue-010"></a>
## ISSUE-010: Word-reader prototype logo jitters at collapse finish

- **Reported:** 2026-10-24. After synchronizing the lettering and symbol, the
  maintainer reported jitter at the end of the standalone homepage prototype's
  collapse. Expected: both finish together with a clean arrival.
- **Source evidence:** The shared `progress ** 6` curve ended at maximum speed
  before abruptly stopping. At the largest preview size, the final nominal
  60 Hz frame covered about 62 px. This explains a likely abrupt stop; the
  subjective jitter was not independently reproduced.
- **Local correction:** Use shared `smootherstep(progress²)` over the same 390 ms.
  It retains a slow start and acceleration but reaches zero velocity and
  acceleration at the hold. Final nominal frame travel is about 1.4 px.
- **Verification:** Syntax and served-source checks passed. Browser samples
  confirmed synchronized, monotonic movement to the correct endpoints with no
  console errors. **Next:** maintainer visual review. Scope is only
  `prototypes/word-reader`, not the main website or desktop application.

<a id="issue-007"></a>
## ISSUE-007: Automatic update checks can appear inactive and suppress failed retries

- **Reported and resolved:** 2026-10-24. **Status:** Resolved in published
  Beta 5; source and final-package checks passed.
- **Reported:** A remote tester found the automatic-check control ineffective,
  while a manual check found Beta 4.
- **Verified in released source:** The checkbox only saves a preference. A
  single startup check waits 30 seconds, records an attempt before contacting
  GitHub, and suppresses subsequent startup checks for a day even after failure.
  There is no periodic check while the application remains open.
- **Expected:** Clear preference feedback, prompt checking when enabled, daily
  checks while running, and bounded retries after failure without downloading
  or installing automatically.
- **Local correction:** Record only successful checks, retry failures after 15
  minutes, defer to active manual operations, and cancel background work when
  disabled or quitting. Ignore the old attempt timestamp because it cannot prove
  success.
- **Verification:** Deterministic scheduler checks cover failure/retry, daily
  cadence, stored timestamps, busy manual operations and cancellation. The full
  detached desktop suite passes 12,905 checks with zero binding errors.
- **Next step:** Qualify the delivered
  correction on a separate test installation.
  The tester's exact automatic-check timing and network conditions remain unknown.
- **Evidence:** [Update investigation](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/UPDATE_INVESTIGATION_2026-10-24.md).

<a id="issue-008"></a>
## ISSUE-008: Installed Beta 2 reopens after an attempted Beta 4 update

- **Reported and locally corrected:** 2026-10-24. **Status:** Confirmed package
  metadata defect; isolated rollback reproduced. Corrected in published Beta 5
  source and packages. The tester's own journal has not been
  inspected.
- **Reported:** A remote tester using the installed distribution manually found
  Beta 4, downloaded and installed it, then saw Beta 2 after the application
  restarted.
- **Expected:** A verified successful update reopens Beta 4 in the existing
  installation and saved workspace; a failed update clearly reports recovery.
- **Verified:** The exact Beta 2 discovery code and signing trust accept the live
  Beta 4 release anonymously. Direct Beta 2-to-4 updates are allowed; Beta 3 is
  not a prerequisite. The displayed version comes from the running executable.
- **Confirmed cause:** The delivered engine's textual Windows `FileVersion`
  contains `0.3.0-beta.4`; the worker requires numeric `0.3.0.5`. All 35 local
  installed files match the package manifest. The exact original Beta 2 checker
  rejects that engine, despite its correct fixed numeric fields. The installed
  worker then rolls back before smoke/health. A synthetic transaction reproduces
  rollback and reopening; the same defect also affects standalone extraction.
- **Correction / verification:** Use numeric `FileVersion` in the native resource
  while retaining semantic `ProductVersion`. The unchanged old checker accepts
  the rebuilt engine. Real metadata and isolated rollback tests pass: 111 worker
  checks, five native metadata fixtures and nine release-preparation checks.
  New build and packaging gates verify actual delivered version metadata.
  The exact original Beta 2 core/trust accepts the final signed Beta 5 envelope
  and both package hashes; its unchanged version checker accepts the installed
  and standalone-extracted engine at numeric version `0.3.0.6`. Both engines
  have identical bytes. Final-package checks pass: both inert smoke checks,
  18 standalone-runtime checks and 167 isolated Setup checks.
- **Diagnostic improvement:** Local source preserves the Setup exit code and
  writes its log in the private transaction folder. This does not retrofit the
  tester's already installed Beta 2 worker; the native metadata correction is
  what enables old workers to accept a future package.
- **Release decision (2026-10-24):** Prepare Beta 5 with corrected engine
  metadata, preserve the Beta 2/3/4 tags and downloadable bytes, and add notices
  to their release notes. Beta 2 remains provisional. Rebuilding older versions
  would create different bytes under the same version and erase useful evidence.
- **Recovery:** Save, **Quit VineMix**, then run Beta 5 Setup against the existing
  installation without uninstalling; standalone users replace the old EXE after
  quitting. Keep settings and recovery evidence. Intermediate betas are not
  required. Manual Setup bypasses the older worker's metadata check.
- **Publication:** [Beta 5](https://github.com/acoyeen/vine-mix/releases/tag/v0.3.0-beta.5)
  was published on 2026-10-24. All five uploaded assets were downloaded and
  rehashed, and the pinned-key signature passed. The original tags and packages
  remain unchanged. Beta 2/3/4's live release notes now include update notices
  and recovery guidance, preserving their original bodies.
- **Next step:** Qualify an
  actual delivered update on a separate test machine/account. Existing Beta 3/4
  packages remain affected. A sanitized tester journal can confirm the exact
  incident. Retain raw evidence outside the repository.
- **Evidence and workaround:** [Update investigation](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/UPDATE_INVESTIGATION_2026-10-24.md).

<a id="issue-009"></a>
## ISSUE-009: Update window does not match the minimal application interface

Reclassified on 2026-10-25 into [TWEAK-004](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/TWEAKS.md#tweak-004): the Beta 5
update-window redesign is the released foundation for the later information-window
refinements. Its original report, scope and verification are retained there.
Update-check and installation failures remain ISSUE-007 and ISSUE-008. This ID
and heading remain reserved for existing links.

<a id="issue-006"></a>
## ISSUE-006: Website node route is hard to follow

Reclassified on 2026-10-25 as [TWEAK-009](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/TWEAKS.md#tweak-009): website diagram
layout and copy polish. The original report and local verification are retained
there; no public deployment is implied. This ID and heading remain reserved for
existing links.

<a id="issue-001"></a>
## ISSUE-001: Settings search caret overlaps the placeholder word

- **Reported:** 2026-10-24. **Resolved locally:** 2026-10-27. **Status:** Resolved;
  active in the live Beta 6 testing build from 2026-10-28. Not yet published.
- **Area:** Settings dropdown search field.
- **Observed:** The maintainer reports that the blinking text insertion caret
  appears in the middle of the first placeholder word within the search box.
- **Expected:** Adjust positioning so the empty field's caret aligns with the
  start of the placeholder and typed text, rather than sitting inside the word.
- **Impact:** Confusing visual alignment when beginning a settings search.
- **Original reproduction steps:** Open Settings, leave the search empty, and focus
  the search box. Inspect the caret against “Search settings and help”.
  Empty-field focus was inferred from the report; the original affected build
  was not confirmed. The layout defect was subsequently reproduced below.
- **Investigation:** Source inspected on 2026-10-24. The search box and its
  separate placeholder are built in
  [SettingsSearchView.cs](../desktop/VineMix.Desktop/SettingsSearchView.cs);
  the shared text-box template is in
  [App.xaml](../desktop/VineMix.Desktop/App.xaml). Their padding, margins and
  content alignment are investigation points, not a confirmed root cause.
  No live reproduction or external research has been performed.
- **Confirmed cause, 2026-10-27:** Detached WPF layout reproduced a 9-DIP
  horizontal mismatch: the actual insertion rectangle began at x=18 while the
  placeholder began at x=9. The shared template applies content-host margin
  and padding; the separately positioned placeholder did not account for both.
- **Correction:** Position the placeholder from WPF's actual insertion rectangle
  after layout and match the search field's font metrics. Preserve the shared
  text-box template, field dimensions and existing search/keyboard behavior.
- **Verification:** Actual Settings-menu checks compare insertion and placeholder
  text rectangles, typing, and clearing after a horizontally scrolled query at
  100/125/150/175/200% scaling. Normal and high-contrast resource checks include
  the actual displayed brushes; representative rendered results were reviewed.
  The combined desktop suite passed **13,352 checks, 268 renders and zero binding
  errors**. Production and verification builds passed; production reported no
  compiler warnings or errors.
- **Verification boundary / next step (2026-10-27):** These are detached WPF layout and input
  route checks, not a native blinking-caret observation in the installed app.
  No personal app, engine or settings were changed, and no release was published.
  Retain these regressions and include the fix in the next qualified release;
  confirm native focus/caret behavior during its desktop qualification.
- **Beta 6 preparation, 2026-10-28:** Reverified under the canonical
  `0.3.0-beta.6` / `0.3.0.7` identity: **13,356 desktop checks, 268 renders and
  zero binding errors**. The live development transition is tracked in
  [the Beta 6 record](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/BETA6_DEVELOPMENT.md); this result does not claim a native
  caret observation or a published release.
- **Live integration, 2026-10-28:** Applied to the installed Beta 6 testing app
  after the maintainer approved the required engine restart. Matching binaries
  and inventory, preserved workspace/preferences and recovered running audio
  paths were verified. The native blinking-caret observation remains a testing
  step; see [the integration record](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/BETA6_DEVELOPMENT.md#live-activation--28-october-2026).

<a id="issue-002"></a>
## ISSUE-002: Duplicate audio when monitoring an application

- **Originally captured:** 2026-10-20 as IDEA-012.
  **Moved to this log:** 2026-10-24. Original ID remains reserved.
- **Status:** Reported limitation. **Previous priority:** P3.
- **Problem:** Original application playback and the captured, processed signal
  can reach the same headphones together. Users wanting processed-only listening
  can hear duplicate audio.
- **Expected outcome:** Provide a deliberate, understandable way to hear only the
  processed path while preserving capture and the user's Windows audio choices.
- **Evidence review (2026-10-24):** APP_AUDIO.md documents this routing limitation
  and the virtual-input workaround. This is expected loopback behavior with a
  usability consequence, not a newly confirmed capture-engine fault. No live
  audio check was performed. Earlier external research below was not refreshed.
- **Workaround:** Explicitly route the source app through the installed virtual
  input and use one capture path, as described in [application audio](APP_AUDIO.md).
- **Next step:** Qualify the proposed original-playback suppression in isolation;
  retain guided virtual-cable routing as an alternative if capture is also muted.

### Retained proposal and research from IDEA-012

These earlier notes preserve alternatives, tradeoffs and unresolved decisions.
They do not establish a chosen fix or implementation approval.

- **User need:** Apply EQ or other effects to a running application's audio and
  hear only the processed result. Currently the app's original playback and Vine
  Mix's listening output can both reach the same headphones, producing duplicate
  audio.
- **Requested direction:** Add a button on running-app input nodes to silence
  the application's direct local playback while retaining capture and the
  processed listening route. This differs from the node's existing Mute, which
  silences its signal inside VineMix. Suggested label: **Mute original playback**,
  with a tooltip explaining the distinction; wording is not yet chosen.
- **Project fit:** [Application audio](APP_AUDIO.md) documents that process
  loopback copies the selected process tree's audio without suppressing original
  playback. `native/src/process_capture.cpp` implements that capture path, and
  application discovery already inspects Windows render sessions. Existing code
  deliberately does not alter application mute, volume, or endpoint assignments.
  This proposal would introduce an explicit user-controlled exception.
- **Primary-source research (2026-10-20):**
  [Microsoft ISimpleAudioVolume::SetMute](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-isimpleaudiovolume-setmute)
  can mute a Windows audio session, and
  [Audio Sessions](https://learn.microsoft.com/en-us/windows/win32/coreaudio/audio-sessions)
  explains that a session groups streams and render-session mute can persist
  across restarts. The
  [application loopback sample](https://learn.microsoft.com/en-us/samples/microsoft/windows-classic-samples/applicationloopbackaudio-sample/)
  documents endpoint-independent process-tree capture, but does not guarantee
  that muting the target's render session leaves this capture audible. Do not
  infer that guarantee from endpoint-master-volume or AEC loopback behavior.
- **Feasibility conclusion:** A session-mute control is technically available;
  whether it achieves original-playback suppression while retaining usable
  process-loopback audio must be tested with this engine and representative
  applications. No application was muted, Windows routing changed, or live audio
  test performed for this research. Do not present a simple mute API call as a
  proven solution to duplicate monitoring.
- **Alternative:** Explicitly route the source application to an installed virtual
  cable, then capture one path and listen through VineMix. This is already
  described in APP_AUDIO.md and in the
  [OBS application capture guide](https://obsproject.com/kb/application-audio-capture-guide).
  A guided routing action could be explored if session mute also silences capture,
  but introduces device/driver requirements and a separate Windows routing change.
  Do not enable Windows "Listen to this device" or another simultaneous capture
  route that restores a duplicate path.
- **Scope and tradeoffs:** Match all relevant render sessions to the selected
  process tree, including child processes and new sessions after app restarts or
  endpoint changes. A session may affect more than one stream; a browser target
  may include multiple tabs. Original playback mute affects the Windows session,
  not just the VineMix node, and might affect other consumers. Account for
  multiple nodes targeting the same app, external mute changes, and preserving a
  pre-existing user mute. Keep control and session discovery off the audio thread.
- **Open questions:** Reliable capture-after-session-mute behavior; intended scope
  when the app plays on several devices; ownership and restoration on unselection,
  node removal, shutdown, crash, or engine failure; and whether the button should
  require a healthy listening route. Avoid leaving the app unexpectedly muted or
  silently unmuting it over a later user change. Recovery policy remains undecided.
- **Next step:** Define an isolated, user-authorized local-audio qualification:
  compare capture signal presence and direct playback before/after Windows session
  mute using known non-protected material. Then check child sessions, relaunch,
  multiple nodes, and restoration. If capture is silenced, investigate guided
  virtual-cable routing. This entry authorizes research notes only.

<a id="issue-003"></a>
## ISSUE-003: Incomplete graph keyboard and accessibility workflows

### Rust review follow-up — 2026-11-10

The Rust replacement at `55e77c7` has explicit accessibility metadata for several
standard/custom widgets, but comparable semantics for the Voice Color pad and
EQ interactions remain incomplete in source. Assistive-technology behavior was
not exercised in this review. See
[RUST-REV-027](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/RUST_REVIEW_2026-11-10.md#rust-rev-027). Next step: inspect and test
the custom controls' accessible names, values, actions and focus behavior with
Narrator/NVDA. This does not change the status of dated legacy UI verification.

### Current feedback and accepted refinement — 2026-11-04

**Status: Accepted refinements resolved locally, offline verified and integrated;
broader accessibility remains open.** The maintainer reports successful Tab/Shift+Tab control
navigation, Ctrl+Tab pipeline travel, selected-node arrow nudging with undo,
unselected-canvas arrow panning, F2 rename, Find by custom name and original node
type, direct dial entry and Page Up/Down. These are maintainer-reported manual
results; they were not independently observed and do not close broader IME,
Narrator, branch/focus or cable-inspection coverage.

The implementation removes **Escape cancellation of arrow nudges** at the
maintainer's request. Ordinary Escape cancellation of numeric/text drafts stays. Preserve one undo
step per held-key nudge, editor focus precedence, layout-lock protection and
safe cleanup when graph targets disappear. This supersedes the earlier
Escape-to-cancel nudge contract and its historical verification below.
The refinement passed source review and the final combined offline run of 14,726
detached checks / 314 renders / zero binding errors, then guarded interface-only
integration preserving the engine and current settings. Next obtain maintainer
follow-up review; this does not close the broader accessibility issue.
Ctrl+plus/minus zoom is tracked with
[ISSUE-005](#issue-005).

### Earlier 0.5.0 implementation checkpoint — 2026-11-04

**Status at that checkpoint:** Accepted keyboard and name-metadata changes were implemented,
offline verified and locally integrated for unreleased 0.5.0; the broader accessibility issue remains open.
The accepted pipeline keys, canvas nudge/pan, single-target F2, direct numeric
entry and accessible-name refresh are implemented in source. Combined detached
qualification is in [the execution report](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/IMPLEMENTATION_0.5.0.md). Ordinary
Tab/Shift+Tab, Page Up/Down and Find name/type matching are preserved. This does
not close the broader issue: actual keyboard-focus/IME, Narrator and remaining
cable-inspection workflows have not been fully qualified. Historical proposals
below describe the earlier planning state, not current authorization.
Final review corrected held-arrow autorepeat after Escape cancellation; the
50 focused keyboard checks and the combined detached suite pass. The correction
was locally installed through an interface-only update preserving the engine.
Next step: retain the automated regressions and arrange separate actual
keyboard/IME/Narrator checks. Guarded installation does not establish those
interaction results. Cable inspection and comprehensive accessibility remain
unresolved, not implicitly completed or added
to the accepted 0.5.0 scope.

### Accepted contracts and scope — 2026-11-04

The [0.5.0 release plan](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/IDEAS.md#release-0-5-0-plan) contains accepted scope,
focus/draft/branch contracts and completion criteria. The symmetric
Tab and Ctrl+Tab mappings, arrow nudge/pan, single-target F2 and direct numeric
entry are implemented source scope; historical suggestions below do not supersede
them, except for the later removal of nudge Escape cancellation recorded above.
Detached rename/undo/redo metadata checks also pass. Cable inspection
and a comprehensive accessibility audit remain unresolved broader work, not
additional implicit release commitments.

Pipeline navigation from a value field uses the existing pending-entry validation:
valid drafts commit, invalid/incomplete drafts retain focus, and Escape cancels.
Ordinary Tab/Shift+Tab still traverse controls. Reduced-detail navigation reveals
the control before focus. These contracts passed detached checks; native focus,
IME and screen-reader announcements still need separate qualification.

### Revised shortcut proposal and authorized name fix — 2026-11-04

- **Subsequent decision, 2026-11-04:** The maintainer accepted the recommended
  mapping and requested inclusion in the 0.5.0 plan: Tab/Shift+Tab move between
  controls, Ctrl+Tab/Ctrl+Shift+Tab navigate forward/backward along a pipeline.
  Include the earlier canvas-focus arrow-key nudge/pan and single-target F2
  behaviours, plus direct numeric entry from a focused dial. Page Up/Down
  adjustment already exists and is retained. This is planned release scope;
  only the separately authorized accessible-name fix was implemented at that stage. Earlier
  alternatives below are retained as history, not competing current decisions.

- **Maintainer revision:** Tab moves forward between a node's controls only;
  Ctrl+Tab moves forward along the pipeline and Shift+Tab moves backward along
  it. This supersedes the maintainer's earlier plain-Tab pipeline suggestion.
  The maintainer also proposes Page Up/Down for dial adjustment and typing a
  number on a focused control to enter its manual value field.
- **Assessment, subsequently accepted above:** Retain Tab/Shift+Tab for forward/backward
  control focus and use Ctrl+Tab/Ctrl+Shift+Tab for forward/backward pipeline
  navigation. Removing reverse control traversal makes an overshot dial harder
  to reach and makes the forward/backward pipeline shortcuts asymmetric. This
  alternative was initially an assistant recommendation and now replaces the
  earlier mapping by agreement. Branch choice, endpoint behaviour and a clear route
  out of canvas focus remain to be specified; do not create a focus trap.
- **Primary source checked 2026-11-04:** Microsoft's
  [keyboard interaction guidance](https://learn.microsoft.com/en-us/windows/apps/develop/input/keyboard-interactions)
  describes Tab and Shift+Tab as forward/reverse control navigation. This supports
  preserving that familiar pairing; it does not mandate a VineMix pipeline key.
- **Existing dial behaviour:** `RotaryValueControl.AdjustWithKey` already makes
  Page Up increase and Page Down decrease a focused dial by its larger step.
  Arrow keys make smaller changes. No implementation is needed for that basic
  request. Direct numeric typing from dial focus is new, recorded separately as
  [TWEAK-023](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/TWEAKS.md#tweak-023).
- **Narrow implementation authorization:** The maintainer explicitly asked to
  fix stale screen-reader naming. `GraphNodeView.Apply` now refreshes the attached
  accessible Name alongside the visible title, including names restored through
  undo/redo. This preserves the existing naming format and Find node behaviour.
  It does not introduce a new automation peer or claim to resolve all screen
  reader exposure/announcement gaps.
- **Verification/status at the name-fix stage:** Implemented in local source. Production desktop build
  succeeded with zero warnings and errors, using output outside the checkout.
  No live Narrator test, installed-app update or publication was performed.
  All other keyboard changes were then planned but unimplemented. The subsequent
  implementation above includes them; actual rename announcements remain unverified.

### Maintainer keyboard proposals and search clarification — 2026-11-04

Historical proposal stage: accepted mappings and scope are stated above.

- **Status:** Captured for 0.5.0 discussion; feedback and planning only. The
  maintainer suggested pipeline Tab traversal, selection-dependent arrow keys,
  search by both renamed and original node name, and consistent F2 renaming.
  These refine the remedies below; they are not implementation authorization.
- **Pipeline navigation:** Tab should travel forward and Shift+Tab backward
  along a node pipeline. Proposed focus rule: use this only while the canvas or
  a node's navigation surface has focus; keep ordinary Tab/Shift+Tab traversal
  inside controls, text fields, menus and dialogs. Provide an explicit Enter-to-
  controls and return-to-node interaction, plus a clear way to leave the canvas
  without trapping keyboard focus. Exact exit key/behaviour needs review against
  existing Escape cancellation. Automatically reveal the destination node.
  At a split or merge, propose a small keyboard-selectable list of connected
  nodes instead of silently choosing a branch. Main audio paths are the initial
  assumption; treatment of Trigger/control links, endpoints and disconnected
  nodes remains to be decided. Stable ordering and reversing the last route
  should make navigation predictable. Do not change routing or audio.
- **Arrow keys:** The maintainer proposes nudging selected nodes, or panning
  when no nodes are selected. Recommend this in canvas/navigation focus only;
  arrows in dials, text, dropdowns, search results and the future minimap retain
  their own purpose. Move a multi-selection together, respecting existing bounds
  and layout lock; never substitute pan when a selected-node move is locked.
  A selected group should use its existing member-movement rules. Proposed view
  direction: Right pans the viewport toward the right of the graph, making nodes
  move left on screen. Choose nudge/pan increments separately; combine held-key
  repeats into one undoable movement, while panning remains outside edit history.
- **Search clarification:** The stale accessible name is separate from Find
  node. `NodeSearch.Match` already searches the current custom name, original
  node type label and assigned device/app name. For a Reverb renamed "Room",
  both "Room" and "Reverb" match. Assumption: "original node name" means its
  type/default identity, not every previous custom name; historical rename
  aliases are not currently stored or proposed without clarification. Preserve
  both custom name and type in results. The accessible metadata still needs
  refreshing on rename independently of search.
- **F2:** Track the small shortcut refinement in
  [TWEAK-022](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/TWEAKS.md#tweak-022): rename exactly one selected node or group,
  ignore multiple selections. One selected group remains one logical target
  even though its member nodes participate in selection internally.
- **Evidence/next step:** Reviewed `NodeSearch`, `WorkspaceKeyDown`,
  `IsEditingControl` and group-selection code. Resolve pipeline branch and
  focus-exit rules, then qualify keyboard-only add/find/traverse/edit/rename and
  locked/multi-selected/zoomed-out cases. No external research or new runtime
  verification was performed for these proposals.

### Source review — 2026-11-04

Historical pre-correction findings: the stale Name assignment, pipeline traversal,
canvas arrows and F2 gaps described below have since been addressed in source.
The broader cable-inspection and live accessibility audit remain pending.

- **Status at source review:** Specific source gaps identified; no keyboard-only or screen
  reader session performed. This review updates evidence, not the confirmed
  0.5.0 scope or implementation authorization.
- **Existing support:** Add-node shortcuts/search, Find node with keyboard result
  selection, Fit/Frame, history shortcuts, keyboard-adjustable dials and exact
  value entry already exist. Sockets are buttons with a Click handler supporting
  a two-step connection, accessible names and connected state; do not classify
  all cable creation as mouse-only. High contrast, socket shapes and reduced
  meter animation provide existing visual accommodations.
- **Source gaps:** `WorkspaceKeyDown` has no next/previous or connected-node
  traversal, directional node nudging or keyboard-only free pan. Alignment
  shortcuts and Find/Frame do not cover those workflows. Cable hit surfaces are
  non-focusable Paths with mouse handlers; there is no reviewed keyboard cable
  selection/inspection path or connection list. F2 renames a selected group only;
  node Rename exists in its context menu, so missing F2 is an inconsistency,
  not evidence that renaming is impossible without a mouse.
- **Rename metadata defect:** `GraphNodeView` sets its accessible Name only in
  the constructor. `Apply` updates `_title.Text` after a rename but does not
  refresh that Name. The attached accessible metadata can therefore be stale;
  how a screen reader exposes the Border and its children needs live inspection.
- **Context/focus gaps to qualify:** Socket names identify input/output and type
  but not their owning node or connected destination. A screen reader may rely
  on surrounding structure to provide that missing context. No custom node
  selection automation peer or automatic keyboard-focus reveal handler was
  found in the reviewed graph classes. Find/Frame explicitly reveals nodes,
  but tabbing into an off-screen control still needs a real test. Existing
  Mute/Bypass ItemStatus text does not establish correct toggle announcements.
- **Next step:** Exercise add/find/select/rename/connect/inspect/disconnect and
  parameter editing with keyboard and Narrator; inspect the automation tree,
  focus order, off-screen focus and state changes. Cover the planned simplified
  nodes/minimap too. Distinguish proven failures from usability enhancements;
  do not assume labels alone make a complete accessible graph.

- **Originally captured:** 2026-10-23 as IDEA-026.
  **Moved to this log:** 2026-10-24. Original ID remains reserved.
- **Status:** Reported; workflow audit pending. **Previous priority:** P2.
- **Problem:** Earlier UI review identified gaps in operating and understanding
  a complete graph without a mouse, including traversal and accessible state.
- **Expected outcome:** Users can navigate, edit and inspect the graph with the
  keyboard and assistive technology, with current names and clear focus/state.
- **Evidence review (2026-10-24):** GraphWorkspace.cs and GraphNodeView.cs provide
  existing shortcuts, focus and automation labels. Their presence does not prove
  complete workflow coverage. Missing actions and rename/state announcements
  still need a focused audit; no live accessibility verification was performed.
- **Scope and next step:** Walk through create/select/connect/rename/inspect,
  document the exact failures, then choose the smallest corrections. The optional
  outline and traversal designs below remain candidate remedies.

### Retained proposal and research from IDEA-026

These earlier notes preserve alternatives, tradeoffs and unresolved decisions.
They do not establish a chosen fix or implementation approval.

- **Origin and need:** Prior UI-review suggestion. Existing keyboard controls
  should extend to navigating and understanding a complete graph without a mouse.
- **Proposal:** Next/previous and connected-node traversal, keyboard positioning,
  F2 rename, automatic reveal of focused nodes, and an optional node/connection
  outline. Keep accessible node names current after rename; describe controls and
  real toggle states in context.
- **Project fit / research:** `GraphWorkspace.cs` and `GraphNodeView.cs` already
  provide shortcuts, focusable controls and sockets; [the guide](USER_GUIDE.md)
  records Find node, dial editing and high contrast. This completes remaining
  interactions rather than replacing existing support. Local review only.
- **Scope, tradeoffs and dependencies:** Medium; avoid shortcut conflicts and
  unexpected parameter edits during navigation. Coordinate lock (IDEA-017),
  groups and presets. Define traversal for branched graphs and editing fields.
- **Next step:** Walk through create/select/connect/rename/inspect using only a
  keyboard, then specify missing actions and assistive-technology expectations.

<a id="issue-004"></a>
## ISSUE-004: Precise value edits can be discarded on focus loss

- **Originally captured:** 2026-10-23 as IDEA-027.
  **Moved to this log:** 2026-10-24. Original ID remains reserved.
- **Status:** Resolved in local development source on 2026-10-27; active in the
  live Beta 6 testing build from 2026-10-28. Not yet published.
  **Previous priority:** P2.
- **Problem:** Pointer/VR users lack a convenient commit/cancel flow for precise
  values; clicking away can discard a pending edit without applying it.
- **Expected outcome:** Make pending values and their apply/cancel behavior clear
  and usable with a pointer, while preserving keyboard editing and validation.
- **Evidence review (2026-10-24):** RotaryValueControl.cs redraws the current value
  on LostKeyboardFocus. HandleEntryKey supports Enter and Tab to commit, and
  Escape to cancel; validation feedback already exists. This narrows the earlier
  proposal to remaining pointer usability and focus-loss behavior, rather than
  treating keyboard commit or validation as missing. No live check was run.
- **Earlier workaround:** Commit a valid value with Enter or Tab before clicking away.
- **Confirmed reproduction, 2026-10-27:** A regression using the real preview
  node and its routed focus-loss event failed against the earlier editor: a
  pending exact value did not reach the graph or create its expected Undo entry.
- **Correction:** Clicking away applies a valid pending value once, retaining
  its full precision. Invalid text stays visible with a validation outline and
  accessible "Not applied" feedback, without pulling focus back. Right-click
  offers **Apply value** and **Cancel edit**, alongside standard text commands.
  Enter/Tab/Shift+Tab and Escape retain their existing apply/cancel behavior.
  Opening the menu preserves the draft until its action or genuine departure;
  Cancel cannot first apply the value through menu focus loss.
- **Related safeguards:** Unchanged model/theme refreshes preserve rejected
  drafts. Changed values, EQ target changes, explicit dial adjustments, disabling
  and unloading cancel obsolete drafts. Switching effect modes disables hidden
  editors so rejected text cannot become an invisible blocker. Quit and update
  preparation check pending editors throughout the live workspace. Help examples
  retain their separate temporary state. Help text and the user guide describe
  the new behavior. Optional fine-adjustment/reset affordances remain separate
  in [TWEAK-007](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/TWEAKS.md#tweak-007).
- **Verification:** Regression checks cover exact and localized values, invalid
  and whole-sample input, preserved text/selection, menu Apply/Cancel and deferred
  closure, unchanged layout, theme feedback, disabled/read-only/unloaded controls,
  EQ target/removal and mode changes, preparation, and one-step graph Undo/Redo.
  The combined desktop suite passed **13,352 checks, 268 renders and zero binding
  errors**; production and verification builds passed. The original focus-loss
  regression now passes, including protection against a second blur rounding or
  applying the displayed value again.
- **Verification boundary / next step (2026-10-27):** Tests raise real WPF input routes in
  detached synthetic controls; they do not establish native pointer/VR or popup
  focus timing in the installed app. No personal app, engine or settings were
  changed, and no release was published. Include the correction in the next
  qualified release and confirm those native interactions during desktop checks.
- **Beta 6 preparation, 2026-10-28:** The canonical Beta 6 detached suite passed
  **13,356 checks, 268 renders and zero binding errors**, including exact-value
  focus loss, invalid drafts and menu Apply/Cancel. See
  [the Beta 6 record](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/BETA6_DEVELOPMENT.md) for live integration status. Native
  pointer/VR interaction remains a separate testing boundary.
- **Live integration, 2026-10-28:** The verified exact-entry implementation is
  active in the installed Beta 6 testing app. The authorized transition preserved
  saved settings/preferences and recovered previously running audio paths with
  matching app/engine versions and inventory. Native pointer/VR and popup-focus
  testing remains separate; see
  [the integration record](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/BETA6_DEVELOPMENT.md#live-activation--28-october-2026).

### Retained proposal and research from IDEA-027

These earlier notes preserve alternatives, tradeoffs and unresolved decisions.
The local resolution above supersedes their proposed next step; optional
affordances outside that resolution remain proposals.

- **Origin and need:** Prior UI-review suggestion for mouse/VR users who cannot
  conveniently use keyboard commit gestures for precise edits. The original
  Shift/double-click discoverability suggestions are retained in TWEAK-007.
- **Proposal:** Explore compact pointer Apply/Cancel, visible pending text and
  inline validation. Preserve today's keyboard commit/cancel and deliberate
  wheel behavior. This issue concerns completion of an edit, not adjustment/reset
  presentation.
- **Project fit / research:** `Controls/RotaryValueControl.cs` supports exact
  fields and keyboard gestures; pending text can be discarded on focus loss.
  Local review only. Controls and accepted compact styling already exist.
- **Scope, tradeoffs and dependencies:** Small to medium; the extra affordances
  must not overwhelm every node. Decide whether they appear only during editing
  or in a contextual editor. Keep one undoable edit per completed gesture.
- **Next step:** Compare one minimal contextual-edit design with the current
  control, using pointer-only entry, invalid text, cancellation and focus changes.

<a id="issue-005"></a>
## ISSUE-005: Dense-graph navigation and misleading Fit expectations

### Current presentation selection — 2026-11-05

The maintainer selected **Quiet Blocks** (concept 1) and requested implementation
under [TWEAK-025](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/TWEAKS.md#tweak-025). Below the existing roughly 30% detail
threshold, keep plain solid charcoal node bodies and category-coloured header
strips. Hide decorative type symbols, name/control text, miniature control shapes
and audio meters; retain existing selection and device-warning indicators.
Preserve node bounds, header height, ports/cables and edit guards; full
controls and meters return on zoom-in. No new hover or selection behavior is
requested. Implementation passed 195 focused detached checks across all 34 real
kinds and existing zoom/draft/reveal guards, normal/high-contrast screenshot
review and a production build with unchanged engine. Guarded interface-only
activation through the actual shortcut passed eight seconds, preserving the
same engine and physical settings/preferences/configuration, advancing frames
and running paths without rollback or restart. This supersedes the earlier
bright/dark symbols and control shapes. Maintainer visual follow-up remains;
the latest evidence is linked from TWEAK-025.

### Earlier symbol presentation correction — 2026-11-05, now superseded

The maintainer rejected the low-fidelity control shapes and requested a simple
white symbol for each node type. Implementation is offline verified and locally integrated under
[TWEAK-025](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/TWEAKS.md#tweak-025), superseding the dark silhouettes and equal-size
dial requirements below. Keep the zoom/navigation fixes, coloured headers,
hidden simplified meters and stable geometry. Earlier checks and installation
evidence do not qualify this new presentation. The new suite passes 15,164 checks/
317 renders/zero binding errors; all 34 real kinds pass the separate geometry,
zoom and dynamic-theme probe. Reviewed screenshots and guarded paired integration
are recorded in [the execution report](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/IMPLEMENTATION_0.5.0.md). Maintainer visual
follow-up and final release qualification remain open.

### Current manual feedback and accepted refinements — 2026-11-04

**Simplified meters and centring, 2026-11-04 — implemented, offline verified and
locally integrated:** Hide meters in
both Compact and Overview at the same low-detail transition below 30%, respecting
existing hysteresis and active-edit guards. Centre passive content with symmetric
16-world-DIP horizontal padding and no reserved meter column. Keep the global
72-DIP dial size, dark shade, coloured headers, full-detail meters and node/socket
geometry. The earlier Compact-meter policy and its reserved column are
superseded; earlier screenshots remain evidence for that historical version.
The suite passed 14,752 checks / 314 renders / zero binding errors. Updated
mixed-node checks verify both simplified meter modes, full-detail restoration,
horizontal centring, shared dial size and unchanged geometry/history. The
25%, 10%, named-frame and 100% screenshots were reviewed. Guarded interface-only
activation preserved the same engine process/start time/path/hash and settings
through eight seconds without rollback. Next obtain maintainer follow-up review;
this remains unreleased 0.5.0 work.

**Corrected dial sizing, 2026-11-04 — implemented, offline verified and
locally integrated:** Use one fixed, modestly
larger 72-world-DIP diameter for every dial circle across all simplified nodes,
with regular spacing and no growth based on node area. The maintainer clarified
that filling the whole box was not the request; the oversized layout below
records that superseded misunderstanding. Retain minimal shape types, dark
shade, full-detail geometry, coloured headers, sockets and connections. The
suite passed 14,752 checks / 314 renders / zero binding errors, including the
updated mixed 32-node fixture checking every simplified circle against the shared
72-DIP diameter across node types. Reviewed 25%/10% and named-frame screenshots
confirm matching single- and multi-dial sizes with regular compact spacing.
Guarded interface-only activation preserved the same engine process/start
time/path/hash and settings/preferences through eight seconds without rollback.
Next obtain maintainer follow-up review; this remains unreleased 0.5.0 work.

**Earlier interior-filling layout, 2026-11-04 — locally integrated, now
superseded:** Simplified controls filled the padded node interior, with
all dial circles within a node the same size, independently of their full-detail
sizes. The minimal shape types, darker shade, full-detail geometry, coloured
headers, sockets and connections remain unchanged. The existing suite passed
14,752 checks / 314 renders / zero binding errors. Reviewed 25%/10% and
varied-control screenshots show five equal dynamics circles in a 2/2/1 layout;
this is visual evidence, not new automated multi-dial coverage. Guarded
interface-only activation preserved the same engine process/start time/path/hash
and settings/preferences through eight seconds without rollback. Next obtain
maintainer follow-up review; this remains unreleased 0.5.0 work.

**Minimal dark silhouettes, 2026-11-04 — implemented, offline verified and
locally integrated:** Keep only dial circles and
main selector/plot blocks in the zoomed-out silhouettes. Remove numeric-entry
bars and all button/Bypass blocks. Use a dedicated `NodeSimplifiedBrush`, normally
`#0B0D11` against the `#15181E` node background, so shapes are darker than the node;
retain `WindowTextBrush` in high contrast. The minimap remains `#4A5058`.
Header colours, geometry, connections, hover/Find names and full controls on
zoom-in remain unchanged. The prior brighter shared shade and extra blocks
below are superseded. The existing detached suite passed 14,752 checks / 314
renders / zero binding errors; 25%/10% and varied-control screenshots were
reviewed. Rendered pixels confirmed `#0B0D11` shapes against `#15181E` node
backgrounds. Guarded interface-only activation and all 36 inventory entries
passed, preserving the same engine identity/hash and settings/preferences
through eight seconds without rollback. This remains unreleased 0.5.0 work;
the subsequent sizing refinement is recorded above.

**Earlier silhouette direction, 2026-11-04 — locally integrated, now superseded:**
The maintainer superseded the
fitted-name approach with low-detail silhouettes of each node's actual controls,
using the supplied ComfyUI screenshot as reference: dial circles and field/button
rectangles replace the zoomed-out centred name. Preserve coloured headers, node
bounds, cable/socket geometry, connections, selection and active drafts. Names
remain available through tooltips and Find; zooming in restores the full controls.
Port text also hides; the shapes are passive. Group-header behavior and the
minimap's `#4A5058` shade are unchanged.
The detached suite passed 14,752 checks / 314 renders / zero binding errors;
25%/10% and varied-control screenshots were reviewed. Mode-handling review is
source evidence, not additional live interaction proof. Guarded interface-only
activation preserved the same engine process/start time/path/hash and unchanged
settings/preferences through eight seconds without rollback. Maintainer follow-up
remains pending; this is locally integrated, unreleased 0.5.0 work.

**Earlier fitted-name approach, 2026-11-04 — superseded:** The
maintainer reported that simplified-node names were too small. Names were sized
and wrapped within each node's padded box, using world-space dimensions independent
of zoom, while preserving the matching dark shade, centring, coloured header
and node/socket geometry. The existing detached suite passed 14,748 checks /
314 renders / zero binding errors; 25% and 10% screenshots and varied existing
names were reviewed. Long unbroken/Unicode-name handling received source review,
not new test coverage. Guarded interface-only activation preserved the same
engine process/start time/path/hash and unchanged settings/preferences through
eight seconds without rollback. Maintainer follow-up remains pending;
the refinement is locally integrated, not released.

**Earlier matching-shade refinement, 2026-11-04 — text direction superseded:**
Centred simplified-node names and darker minimap node silhouettes shared the
solid `CanvasOverviewBrush` (`#4A5058` normally), preserving selection
highlighting and system high contrast. The maintainer found the previous
`#626974` still too light. The suite passed 14,748 checks / 314
renders / zero binding errors, with screenshots reviewed. Guarded interface-only
activation preserved the same engine process/hash and unchanged settings/preferences
through eight seconds of observation. See [IDEA-053](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/IDEAS.md#idea-053).
The minimap shade remains current. The centred-name presentation and its results
are historical; the work remains unreleased under the installed `0.4.0-beta` identity.

Group headings must not grow and overlap as the canvas zooms out. Pre-fix source
review confirmed inverse-zoom header height, minimum frame width and title size
in `GraphFrameView`; group bounds inherited that expansion. The correction uses
fixed world-space header geometry and hides group headings at the same
simplification stage as node details. Group membership, movement and rename
drafts are preserved, revealing the editor before keyboard rename at reduced detail.

Added **Ctrl+plus / Ctrl+minus** canvas zoom using existing camera limits and
interaction guards, alongside wheel zoom, Fit and Ctrl+0. Focused text/control
editors retain their native shortcuts. Related minimap navigation and visibility
persistence were reported successful by the maintainer; its darker, title-free
refinement is tracked in [IDEA-053](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/IDEAS.md#idea-053).

The preceding refinements passed source review and the combined offline run
of 14,726 detached checks, 314 renders and zero binding errors, including the
final Settings and meter corrections. Guarded interface-only integration passed
while preserving the engine and current workspace/settings. Next obtain
maintainer follow-up review. Graph limits and audio behavior remain unchanged.

### Earlier 0.5.0 implementation checkpoint — 2026-11-04

**Status at that checkpoint:** Selected remedy implemented, offline verified and locally integrated for
unreleased 0.5.0; actual pointer/DPI usability remains to be qualified.
Source now supports 10–200% desktop zoom, native acceptance of the 10% minimum,
two simplified detail levels, shared camera policy and minimap navigation.
Full node dimensions and cable anchors stay fixed; wide overview sockets cannot
be snapped to accidentally. Drafts defer simplification, and control navigation
reveals readable controls. Pan remains within the native supported bounds.
The 32-node/96-cable limits are unchanged. Combined detached geometry/render
evidence and guarded installation are recorded in
[the execution report](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/IMPLEMENTATION_0.5.0.md); real pointer/DPI usability
remains separate qualification. Next step: maintainer interaction review.
The native upper compatibility bound remains 400%; the desktop maximum is 200%.
Older builds may reject new low-zoom values, so downgrade compatibility is not assumed.

### Selected remedy and ComfyUI research before implementation — 2026-11-04

- **Status at research:** Research complete for an initial plan; selected for 0.5.0,
  not implemented. The maintainer requests more zoom-out, progressive removal
  of node detail, and a bottom-right overview showing the visible canvas region.
  The new minimap capability is tracked in [IDEA-053](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/IDEAS.md#idea-053).
  This supersedes the earlier undecided hand-mode/overview remedy below.
- **Confirmed local constraints:** `GraphWorkspace` clamps normal zoom, restored
  views, Fit and Frame selection at 30%; its discrete stops also start there.
  `GraphModels.Validate`, `GraphNodeView.SetViewScale` and
  `GraphFrameView.SafeZoom` independently assume that minimum. Nodes currently
  keep fixed world-space geometry and scale all details together. Changing only
  the wheel clamp would leave validation and interaction inconsistencies.
- **Additional source finding from the 2026-11-04 plan audit:** Native
  `compileGraph` in `native/src/graph.cpp` accepts only 15%–400% zoom and bounds
  pan coordinates to +/-100,000 world units. Thus a 10% interface-only change
  could be rejected when a later edit saves the view. The new minimum must be
  coordinated across native validation, desktop validation and navigation;
  minimap/pan calculations must respect supported coordinates. Define old-build
  read/rollback behaviour for lower zoom and new ports before release.
- **ComfyUI evidence, checked 2026-11-04:** Reviewed official frontend source at
  commit `59bde61e054ce44873e7b6267726ca27f5476482`.
  [LGraphCanvas](https://github.com/Comfy-Org/ComfyUI_frontend/blob/59bde61e054ce44873e7b6267726ca27f5476482/src/lib/litegraph/src/LGraphCanvas.ts)
  derives simplified rendering from minimum readable text size, zoom and pixel
  density; it suppresses shadows/badges and simplifies shapes/link decoration.
  [LOD tests](https://github.com/Comfy-Org/ComfyUI_frontend/blob/59bde61e054ce44873e7b6267726ca27f5476482/browser_tests/tests/lodThreshold.spec.ts)
  exercise threshold changes and restoration of detail when zooming back in.
  This is source/test inspection, not an interactive review of ComfyUI or a
  claim that its different renderers hide exactly the same elements.
- **Proposed VineMix behaviour:** At normal zoom retain the complete node. As
  controls become unreadable, suppress dial labels/readouts, detailed plots and
  meter animation while retaining node silhouettes, category colour, readable
  titles, connections and compact mute/bypass/error/clip cues. At overview scale,
  retain only useful structure and state. Restore detail automatically on zoom-in.
  Use screen readability and DPI to choose thresholds, with hysteresis to avoid
  flicker; exact cutoffs await visual qualification. Preserve node bounds and
  socket anchors across transitions so cables and Fit do not jump.
- **Zoom proposal:** Start qualification with a 10% minimum (versus 30%), retaining
  the existing 200% maximum and 100% reset. The percentage is an initial design
  proposal, not a confirmed requirement or performance result. Centralize bounds
  across load/save/validation, Fit/Frame, wheel/buttons and node/group adornments;
  audit native acceptance and older-version loading before changing persistence.
  Very spread-out graphs can still exceed a finite minimum: Fit must report
  that limitation honestly, with minimap navigation as the fallback.
- **Interaction/performance:** Hidden controls must not receive accidental input
  or trap focus. Keep navigation and selection available in lock mode; propose
  framing a node before fine editing. Defer detail transitions during an active
  control gesture. Avoid overlapping screen-sized socket/cable hit zones at low
  zoom. Pause unnecessary hidden visuals without suspending audio processing or
  losing retained peaks. Rendering simplification alone is not a measured speedup.
- **Qualification/next step:** Plan sparse and dense examples up to the existing
  32-node/96-connection limits; test 10/20/30/50/100/200%, DPI/high contrast,
  groups, negative coordinates, empty layouts, off-screen nodes, zoom anchoring,
  focus and precise entry, saved views, and Fit beyond its limit. Verify graph
  geometry, undo history and audio settings stay unchanged during navigation.
  No visual prototype, performance benchmark or running-app change was made.

- **Originally captured:** 2026-10-23 as IDEA-028.
  **Moved to this log:** 2026-10-24. Original ID remains reserved.
- **Status:** Fit limitation confirmed in source; pointer workflow needs verification.
  **Previous priority:** P2.
- **Problem:** Fit promises to bring every node into view, but sufficiently large
  layouts exceed the minimum zoom. Earlier review also identified difficulty
  panning a full window of nodes using only the available pointer controls.
- **Expected outcome:** Explain when the graph cannot fully fit and provide a
  discoverable way to reach off-screen nodes in dense layouts.
- **Evidence review (2026-10-24):** GraphWorkspace.cs clamps FitAll to a minimum
  zoom of 0.3 while its tooltip says it brings every node into view. Middle-button
  and Space+drag panning already exist, so the earlier empty-canvas/right-drag
  note must not be read as the only panning route. Pointer-only accessibility
  and event handling over controls still need reproduction; no live check ran.
- **Workaround:** Use existing middle-button or Space+drag panning, Find node,
  and Frame selection as appropriate to the input device.
- **Scope and next step:** Check a dense graph beyond Fit's zoom limit and a
  pointer-only workflow; correct feedback and choose the smallest remaining
  navigation remedy. A hand mode or overview is a proposal, not a decided fix.

### Retained proposal and research from IDEA-028

These earlier notes preserve alternatives, tradeoffs and unresolved decisions.
They do not establish a chosen fix or implementation approval.

- **Origin and need:** Prior UI-review suggestion. Users need to pan even when
  nodes fill the window, and understand a Fit action that cannot show everything.
- **Proposal:** Explore a hand/pan mode or reserved navigation area, with an
  optional overview navigator. Make the minimum-zoom fit limit understandable
  rather than implying every graph will fit. Existing Find and Frame selection
  remain available.
- **Project fit / research:** `GraphWorkspace.cs` clamps zoom and initiates
  right-drag panning on empty space; [workspace controls](NODE_WORKSPACE.md)
  document existing navigation. Local inspection only; no external research.
- **Scope and tradeoffs:** Medium UI work; avoid intercepting dials, cable edits
  or selection. No decision to change supported zoom bounds or renderer. A hand
  mode may solve the main problem without an always-visible overview panel.
- **Next step:** Review a dense example at minimum zoom and select the smallest
  navigation addition that works with lock mode and pointer-only use.
