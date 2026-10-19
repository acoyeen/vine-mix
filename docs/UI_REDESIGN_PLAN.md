# Audio Mixer — UI overhaul plan

Date: 17 October 2026. Status: control-detail refinement completed and running; final 37 UI-only checks passed. The earlier flat slate build passed 33 checks. The latest direction keeps one medium slate-grey canvas and integrated header actions, adding clearer recessed/beveled controls and larger fader details without rounded strip cards. Both buses retain the same muted green; blue/yellow coding remains superseded. Native audio and reliability qualification remains deferred. See [design-qa.md](../design-qa.md).

## 1. Direction and scope

Turn the existing application into a compact, fixed-size desktop mixing console. A user should immediately see **what is coming in, where it is being sent, how loud each send is, and what is preventing sound**.

Use Voicemeeter as a reference for legible channel strips, vertical faders, meters, and illuminated state buttons. Give this application its own arrangement and visual language. Retain exactly two microphone slots, one combined App Audio strip, two physical listening outputs on A, and one virtual microphone output on B.

This is a presentation overhaul of the current simple mixer. It includes source and master mute, independent A/B routing and levels, device selection, connection states, setup and existing diagnostics. EQ, audibility, compression, gates, echo, effects, recording and extra buses are outside this plan.

The reference's **Mono** and **S / Solo** controls inform the desired physical-button treatment. They do not justify adding controls with no engine behavior: this build has neither a solo function nor a stereo-summing mono switch. Keep those out of the operational surface. Preserve the existing microphone channel selector under device details, accurately labelled **Input channel**. Selecting one channel already feeds it to both sides; that is different from summing a stereo input to mono.

## 2. Evidence and current-screen review

The screenshots below record the **pre-redesign** applications during the initial design review. Only window navigation and scrolling were used. No route, gain, mute, assignment or Windows audio setting was changed. Saved configuration remained revision 1, with A muted at −24 dB. No sound or test tone was started. Current implementation captures and the completed review are linked from [design-qa.md](../design-qa.md).

The official [Anthropic frontend-design skill](https://github.com/anthropics/skills/blob/main/skills/frontend-design/SKILL.md) was reviewed on 17 October 2026. Its brief-first approach supports this purpose-built fixed console: intentional typography, restrained colour, meaningful visual state and plain labels. The user's monochromatic green preference and the approved audio-control scope take priority over generic visual flourishes or web-specific implementation advice. This remains a native WPF interface.

### Step 1 — Main mixer: functional structure, weak operating layout

![Current mixer window](design/evidence/01-current-mixer.png)

**Strengths:** three sources are clearly named; A/B destinations have plain-language explanations; independent send levels are already present; the A mute state is visible.

**Problems visible in this capture:**

- Large rounded cards and generous gaps dominate the window. The controls resemble a settings dashboard rather than an instrument for watching and changing audio.
- A/B routing uses small checkboxes. The tiny tick carries the state, while the adjacent coloured label looks almost identical whether routing is on or off.
- Horizontal gain controls, separated by long labels, make relative levels harder to scan across sources.
- The thin meters have little visual weight, no visible scale, and substantial distance from device selection. A silent source, disabled route and muted master are difficult to distinguish at a glance.
- Physical output assignments are below the fold even in the current large window. A user cannot see the full source-to-output relationship together.
- Large title and explanatory copy consume space needed by the actual mixer. Global Retry has more prominence than normal operation needs.

These are screenshot-based observations, not measured usability-test results. Keyboard behavior, screen-reader output and actual contrast compliance have not been established by these captures.

### Step 2 — Setup: complete information, excessive initial burden

![Current setup window](design/evidence/02-current-setup.png)

**Strengths:** exact endpoint identities are available; both sides of each virtual transport are explicit; availability and connection status are distinguished.

**Problems visible in this capture:** driver explanations, licensing guidance, four technical endpoint roles and raw endpoint IDs compete with the task of getting audio connected. The lower two roles require scrolling. Ordinary configuration and troubleshooting need different levels of detail.

### Comparison — What to take from Voicemeeter

![Installed Voicemeeter reference, inspected without changing controls](design/evidence/03-voicemeeter-reference.png)

The useful visual pattern is repeated strip structure with faders, meters and clearly filled/unfilled state controls. In this capture, enabled B routes and the active red M button are conspicuous without reading a paragraph. Its effects panels and extra processing are irrelevant to our scope.

Official screenshots were also located on the [Voicemeeter Standard](https://vb-audio.com/Voicemeeter/VoicemeeterAudioMixer.jpg) and [Banana](https://vb-audio.com/Voicemeeter/VoicemeeterBanana.jpg) product pages. The [publisher's Standard overview](https://vb-audio.com/Voicemeeter/index.htm) documents its three-input, A/B bus structure and mute/solo controls. The recommendation here is our design judgment, not evidence that copying its entire UI would improve this product.

## 3. Fixed dimensions and layout

**Specify one outer window size: 1120 × 680 Windows device-independent pixels (DIP).** Disable edge resizing and maximise; keep minimise, move and close-to-tray behavior. Integrate minimise and close into the app header using WPF `WindowChrome`, without a separate Windows title bar. Retain a fixed opaque window, native drag/system-menu behavior, `ResizeMode="CanMinimize"`, `SizeToContent="Manual"` and explicit fixed Width/Height with matching Min/Max bounds. No responsive breakpoints, alternate arrangements, transparency or user-adjustable panel sizes.

WPF/Windows display scaling still applies: the intended outer size is about 1400 × 850 physical pixels at 125% and 1680 × 1020 at 150%. This preserves proportions and readable controls; it is not a resizable layout. The desktop work area must be large enough. Check the current display and those scaling cases; do not quietly shrink text or reflow the mixer to force a fit. The integrated header remains inside the fixed size. Current 125%/150% captures simulate render density, so actual OS scaling remains a later check.

Use one horizontal console, with three source strips on the left and a visibly separated two-strip master section on the right:

| Area, left to right | Purpose | Proposed content width |
|---|---|---:|
| Microphone 1 | Physical input, input meter, A/B sends, source mute | 200 DIP |
| Microphone 2 | Same structure, even when unassigned | 200 DIP |
| App Audio | Combined virtual input, same A/B controls | 200 DIP |
| A · Listen | A master, meter, both physical output slots | 208 DIP |
| B · Send | B master, meter, virtual microphone destination | 208 DIP |

Allow three 8-DIP gaps between adjacent strips and one 20-DIP gap before the master section: 1016 DIP of strips + 24 DIP + 20 DIP = a central grid 1060 DIP wide. Use thin vertical separators in these gaps on one continuous slate canvas rather than rounded strip containers. Allocate the remaining width to outer margins. The measured grid remains authoritative; the removed OS title bar provides more usable fader height.

Above the console: a compact app name, aggregate engine status, **Devices**, application menu and integrated minimise/close actions. Below it: unobtrusive save/error status. Put Quit in the application/tray menu. The main header close action hides to tray; explicit Quit stops the engine. Remove the slogan and repeated teaching paragraphs from normal operation.

All three sources, six route buttons, six send faders, five meters, five mute buttons, two A device slots and the B destination must fit together without scrolling. Shorten long device names visually with an ellipsis; provide the full name through an accessible details view, not only hover text.

## 4. Source-strip anatomy

Every strip uses the same vertical alignment:

1. **Header:** Microphone 1, Microphone 2 or App Audio.
2. **Device row:** selected device or app destination, followed by a short connection label. Clicking it opens a compact picker/details view. Microphone channel details are secondary.
3. **Route row:** two substantial buttons, **A · Listen** and **B · Send**, directly above their corresponding faders.
4. **Level section:** two vertical faders, one for A and one for B, beside one narrow, segmented **Input** meter. Start with roughly 250–270 DIP of fader travel; prove the final height in the fixed-size mockup.
5. **Readouts:** separate editable 18-DIP dB values for A and B, aligned below their tracks; 10-DIP scale labels and explicit A/B lane labels keep their meaning visible.
6. **Source mute:** one wide **M · Mute** button. Its scope is both sends.
7. **Context line:** concise effective state when useful: “Muted”, “No routes enabled”, “Waiting for microphone”, or “A mix is muted”.

There is deliberately no extra source-wide volume fader. The current engine has independent A/B gains; inventing a single common fader would conceal or change that behavior. Both send controls remain visible at once, with no A/B editing mode to remember.

## 5. Illuminated controls and state rules

Use actual square toggle-button surfaces, not a coloured label beside a standard checkbox. Light top edges and dark lower edges give functional buttons a restrained bevel. Give a button a clear filled face when on, with explicit ON/OFF/MUTED wording and a separate visible focus indicator. Keep the surrounding chassis flat; avoid rounded card framing, broad bloom, glass effects or animated glowing panels.

| Control/state | Visual treatment | Meaning |
|---|---|---|
| A on | Filled muted-green face, dark high-contrast label, visible “ON” mark | This source is assigned to A |
| B on | The same muted-green face, dark label, “ON” mark | This source is assigned to B; label and position distinguish A/B |
| A/B off | Dark face, defined border, readable label, “OFF” mark | Saved route is disabled; still clickable |
| M on | Filled coral/red face, “M · Muted” | Source or named bus is muted |
| M off | Neutral face, “M · Mute” | Press to mute |
| Focus | Separate high-contrast outline | Keyboard target; must not resemble an active route |
| Editing unavailable | Distinct subdued surface plus explanation | Engine is disconnected; this is not a route-off state |

Button lettering, fill/luminance and explicit state wording reinforce colour. This follows [W3C's guidance on using more than colour to convey state](https://www.w3.org/WAI/WCAG22/Understanding/use-of-color.html).

**Routing intent and actual audibility are separate:**

- If a source is muted, its enabled A/B buttons stay on. The red M button explains why it is silent. Unmuting restores the existing routing.
- If the A master is muted, the source A routes stay on; the A master says **Muted**.
- If an assigned device disappears, preserve the device name, route lights and levels. Show **Waiting for device** at that path. Do not turn a selected route off to indicate disconnection.
- An A send can remain enabled with both listening devices absent. The master shows both missing devices; B remains visually and operationally independent.
- Route-off faders retain their values and can be adjusted in advance. Their lanes are subdued, not disabled. The off button makes clear that moving a fader does not enable the route.
- Hover, focus and wheel input do not activate route buttons. There is no special double-click shortcut; explicit clicks and keyboard activation use normal toggle behavior. Do not delay a mute command to detect double-clicks.

## 6. Fader behavior

Retain **−60 to +12 dB**, in **0.5 dB** steps. Mark unity (0 dB) prominently and show major ticks at +12, +6, 0, −6, −12, −24, −36, −48 and −60. A low position is attenuation; it is not a mute switch. Do not label −60 as −∞.

Use 40 × 24 DIP horizontal grooved fader caps on straight 14-DIP outer tracks with 12-DIP filled travel. Caps have a small 2-DIP corner radius limited to that functional control; strip-card frames remain absent. Keep sufficient space between A and B caps to prevent grabbing the wrong send. Label each source's lanes A and B. Active travel uses green #79B99A, distinct from light #B9E3CB caps. Off routes retain fully visible grey caps rather than faded controls. Colour the lane accent, not the entire strip; filled travel represents gain position, not signal activity.

- Drag the cap to adjust. Track clicks move by a controlled page step rather than jumping directly to a potentially loud value.
- Keyboard arrows change 0.5 dB; Page Up/Down change 3 dB. Support standard range endpoints for keyboard operation.
- Accept a typed dB value with Enter; Escape restores the prior value. Validate the range without clamping silently or committing partial typing.
- Provide an explicit **Reset to 0 dB** action in the fader context menu. Do not bind an easy-to-trigger double-click gesture to a large level increase.
- Mouse wheel changes a fader only when that control has keyboard focus. Scrolling over the mixer must not change volume accidentally.
- The dB readout and automation value are authoritative. The knob should not suggest an extra pan, balance or linked-level function.

Preserve the engine's existing gain ramps. UI animation must never delay a mute command or become part of audio processing.

## 7. Meters that explain the signal

Replace each thin progress bar with a readable vertical segmented peak meter and small dBFS scale. Keep one meter per source and one per bus: the existing telemetry supplies one peak value, so two decorative L/R columns would be misleading.

**Source meters:** label **Input**. They show incoming activity before source mute, routing and send gains. A muted microphone can therefore have a moving input meter. Put the mute indication beside it so this is understandable.

**Master meters:** label **Output**. They show the mix after master gain/mute. A source meter moving while A's meter is silent can be explained by A mute or routing; it is not automatically a driver error. A moving bus meter shows engine mix activity, not proof that a disconnected device is playing sound.

Proposed presentation defaults, to be tuned visually using inert sample data:

- Logarithmic dBFS display, floor at −60; silent input reads −∞ dBFS.
- Muted-green lower region, lighter greens from −12 and −3 dBFS. Reserve red for actual clip indications and mute/errors. These are visual level zones, not hearing-safety guarantees.
- Fast attack, approximately 300 ms visual release; one-second peak hold.
- Each received bus clip flag holds a labelled **CLIP** indicator for three seconds after the last observed flag; clicking it clears the visible hold. Source meters must not invent a separate clip flag the engine does not expose.
- No blinking meter frames. Reduced-animation mode can remove the smooth fall without hiding values.

The current clip telemetry can miss events between UI polls. A presentation hold improves visibility only for observed flags. If complete clip-event reporting is later required, add an engine-side counter/latch as a separately scoped telemetry change.

## 8. A and B master sections

### A · Listen — what I hear

One master fader, output meter and **M · Mute** button. Beneath them, exactly two permanently visible rows: **Output 1** and **Output 2**, each with selected name and independent status. Both receive the same A mix; their numbering does not mean separate source routes.

Current state should read **Listening device 1 — Connected** and **Output 2 — Not assigned**. If one drops out, identify that slot as waiting while the other retains its connected state. Do not replace the whole A section with a global error.

### B · Send — what others hear

One master fader, output meter and its own **M · Mute** button. Beneath them, show **Use as microphone in Discord/games**, then the exact Windows recording endpoint name. Distinguish the actual name from a suggested future name: currently it is **Line 1 (Virtual Audio Cable)**.

Keep a small **Copy device name** action and **Device details** entry. Do not suggest the app can choose Discord's microphone on the user's behalf. Expose missing virtual transport status here without presenting the internal send endpoint as the consumer's microphone.

## 9. Device setup and recovery

Replace the long single setup document with a fixed-size **Devices** dialog containing three pages. The everyday mixer stays visible behind it and continues processing audio.

| Page | Primary content |
|---|---|
| Inputs & outputs | Two microphones with channel details; the two A output assignments; selected names and per-slot states |
| App connections | Two clear tasks: “Send app audio here” and “Choose this microphone in other apps”; exact Windows names, copy actions and Windows Volume Mixer link |
| Troubleshooting | Four underlying transport bindings, endpoint IDs, formats, queues, error details, logs, driver documentation and retry |

Dialog size: **880 × 600 DIP**, also non-resizable, with the same integrated app-header minimise/close treatment and no separate OS title bar. Its ordinary pages fit without scrolling. Long catalog/log content may scroll inside its own troubleshooting pane; the live mixer never scrolls. Keep navigation and Done/Close at fixed positions.

For an unconfigured cable pair, guide a two-step task for each cable: select/confirm the expected playback and recording partners, then show where the user uses it. Pre-filter candidates by family and direction, reject the VB 16-channel alternate for the app destination, and require explicit user selection when identity is ambiguous. Do not bind devices merely because friendly names match.

Use **Not assigned**, **Connecting**, **Connected**, **Waiting for device**, **Device busy**, **Microphone access blocked** and **Audio service unavailable** consistently. Keep saved missing devices in the picker, with their names and state. Windows availability alone must say **Available to Windows**, not **Connected**.

Successful reconnection updates the affected slot quietly. Make retry a local recovery action where the existing API permits it; the current global retry API must be labelled **Retry assigned devices** and live in Troubleshooting until a path-specific API exists. Do not promise a local retry while invoking a global operation.

On the main window, shorten errors to an actionable sentence such as “Microphone 1 is busy. Close the app holding it exclusively, then retry.” Put HRESULTs and raw identities in details. Preserve useful distinctions rather than turning every error into “Disconnected”.

This uses familiar task names and visible selections to reduce memory burden, following the [recognition principle in Nielsen Norman Group's heuristics](https://www.nngroup.com/articles/ten-usability-heuristics/). Status belongs with the affected path so users can understand consequences, consistent with [NN/g's visibility-of-status guidance](https://www.nngroup.com/articles/visibility-system-status/).

## 10. Visual specification

Aim for a quiet, precise audio tool: aligned rails, short labels, sturdy controls and restrained surfaces.

| Element | Slate revision specification |
|---|---|
| Main / strip background | Continuous medium slate-grey #566168; no complete strip outlines or rounded cards |
| Strip separation | Thin vertical rules; retain the measured five-column grid |
| Inactive control | Recessed square well #081014; visible focus treatment; about 3.02:1 separation from the chassis |
| Functional button detail | Square beveled face #273A44, hover #344A56, light top edge and dark bottom edge |
| Channel / device names | Full-width #3E4A52 channel-name band; recessed device nameplate with right-hand navigation chevron |
| Main / secondary text | #F0F3F2 / #DAE1DF |
| A / B accents | The same restrained green #B9E3CB; distinguish buses with labels and position |
| Mute / error | Coral #F28B81, with readable alert text #FFD4CE and wording/icon |
| Type | Segoe UI; 13–14 DIP body, 16 DIP strip names, 18 DIP tabular gain readouts and 10 DIP scale labels |
| Geometry | Square operational surfaces; 4/8/12/16 DIP spacing system; no floating or pill panels |
| Buttons | At least 36 DIP tall; A/B faces around 80–88 DIP wide; wide source M button |

The inert harness checks the normal palette against the specified targets: normal text at 4.5:1 and essential control/state indicators at 3:1 against adjacent colours. W3C explains [text contrast](https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum) and [non-text contrast](https://www.w3.org/WAI/WCAG22/Understanding/non-text-contrast.html); use these as design targets for this desktop UI, not an unearned web-conformance claim.

Reserve saturated colour for state and meters. Avoid decorative gradients, oversized branding, repeating slogan text, generic dashboard cards and dense technical labels in the operating surface. The result should look like a purposeful small console without reproducing Voicemeeter's panel artwork, logo, effects areas or exact geometry.

## 11. Accessibility and predictable interaction

Use WPF ToggleButton/Slider semantics beneath the custom appearance. Expose names such as “Microphone 1, route to A Listen, on” and “Microphone 1, B Send level, −6 decibels”. Mute toggles need explicit checked state and source/bus scope.

Tab order follows the left-to-right strips, then Devices/menu. Space activates the focused toggle; arrows operate the focused fader. Show a focus outline independent from active-state illumination. Avoid unmodified global A/B/M keyboard shortcuts that could change live routing while the user types.

Do not send continuous meter readings to screen-reader live regions. Announce important connection/error changes politely. Keep a readable numeric level available on focus/request. Supply visible full-name details for truncated devices and tooltips for abbreviations without relying on hover alone.

Custom colours must respect Windows high-contrast mode. Fixed dimensions limit how much enlarged text fits; validate supported scaling and disclose the effective desktop-size requirement. Never hide a mute, route or assigned output to retain an aesthetic screenshot.

## 12. Implementation sequence

| Pass | Deliverable | Review gate |
|---|---|---|
| 1. Fixed-size visual mockup | Full console at 1120 × 680, normal and waiting/muted states, plus Devices pages | Every control fits; route/mute states are legible without colour alone; no hidden outputs |
| 2. WPF control library | Theme resources, route/mute toggle templates, vertical dB fader, segmented peak meter, device picker/status row | Keyboard, focus, checked/off/unavailable and typed dB behavior work using inert data |
| 3. Main-window recomposition | Three source strips plus A/B master section | Existing configuration bindings retained; no replacement controls with changed meaning |
| 4. Device workflow | Three-page Devices dialog and compact details | Ordinary configuration needs no raw IDs or driver knowledge; diagnostics remain reachable |
| 5. Presentation state | Meter smoothing/holds, save feedback and actionable local statuses | No stale state masquerades as live data; rendering does not block audio/control communication |
| 6. Integration review | Read-only comparison with saved settings, then a separately scheduled live check | No unintended gain, mute, route or device change on opening, navigation or reconnection |

Use WPF resources and templates rather than replacing the desktop stack. [Microsoft's WPF styling guidance](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/controls/styles-templates-overview) describes separating appearance from control logic and reusing templates. Refactor MainWindow.cs and SetupWindow.cs composition into maintainable XAML/views and reusable controls; preserve MixerViewModel, configuration revision handling, named-pipe transport and native audio workers.

UI-only state includes focus, expanded details and peak holds. Native configuration remains the authority for mutes, gains, routes and assignments. Rebinding must not emit default values before the full engine snapshot arrives. Keep read-only meters/labels OneWay-bound so the prior startup failure cannot recur.

No driver replacement, Windows endpoint renaming, audio-service restart or new DSP is needed for this redesign. Startup/tray behavior and optional start-at-login remain accessible in the application menu/preferences.

## 13. Implementation acceptance checklist

The checklist defines the design contract. Implementation and inert UI results are recorded in section 14 and [design-qa.md](../design-qa.md); actual Windows scaling changes, full screen-reader speech behavior and native audio qualification remain later checks.

- Window size is fixed at 1120 × 680 DIP; no maximise, resize handles, responsive layout or main-view scrolling. Devices is fixed at 880 × 600 DIP. Both have integrated named minimise/close actions and no separate OS title bar.
- The console is a continuous medium slate canvas with thin column rules, square beveled buttons and clearly recessed wells, without rounded strip-card outlines.
- All eight faders have identifiable 40 × 24 DIP caps. Off routes remain fully visible; source lanes have explicit A/B labels; gain-position fill is not presented as metering.
- All existing source, send, bus and device controls are present with correct scope; two A outputs and the single B destination are visible together.
- A/B off, A/B on, muted, waiting, busy, focused and editing-unavailable are visually distinguishable. Route intent survives mute and missing hardware.
- The three sources have two independent send faders each. No phantom solo, mono-summing, pan, linked gain, stereo meter or extra output is displayed.
- All four A/B combinations per source have understandable states; the completed inert-state review covers all 64 route combinations across the three sources.
- A and B master mutes have independent indications. One missing A output is not rendered as a global engine failure.
- Meter movement has correct pre-/post-mute meaning; scale/readout/peak hold/clip display match supplied telemetry and contain no simulated live activity.
- Device labels remain correct when names are long, devices are missing or Windows names have not been renamed.
- Keyboard-only operation, screen-reader names, focus, text/control contrast, high-contrast theme and agreed DPI cases pass review. Screenshots alone are insufficient proof.
- Launching the redesigned UI against the current saved configuration preserves revision/state: A stays muted at −24 dB, mic monitoring stays off, microphone B remains enabled, and Listening device 1 remains assigned. A visual refresh must not write defaults.
- No test sound, playback start, automatic unmute or gain reset occurs as part of UI startup or navigation. The user controls the first audible test.
- UI state checks use inert design data first. Live audio, latency and reliability checks remain a separate activity under [TESTING.md](../TESTING.md), with the agreed reboot before reliability qualification.

## 14. Implementation and verification status

The [initial static mockup](design/console-mockup.png) and [mockup review](design/MOCKUP_REVIEW.md) preceded the first WPF overhaul. The [slate mockup](design/light-console-mockup.png) and [revision review](design/LIGHT_CONSOLE_REVIEW.md) then established the continuous medium slate canvas, thin separators and integrated header actions. The latest [control-detail mockup](design/detailed-console-mockup.png) and [review](design/CONTROL_DETAIL_REVIEW.md) add stronger control contrast, functional bevels, recessed device nameplates, a channel-name band and clearer fader/readout details. Generated illustrative gains and meter activity are not adopted. The approved five-strip layout and local destination rows remain: three sources, independent dual A/B sends, two masters, two physical A slots and one B endpoint. The Devices dialog has three fixed pages, with scrolling limited to detailed troubleshooting content and native text editors.

The final 18-DIP-readout build's inert `--verify-ui` run in `artifacts/ui-verification/detail-reviewed/results.json` passed **37 checks** and exited successfully, including four added assertions for cap size, off-route visibility, A/B labels and measured control separation. Existing coverage includes all 64 route-state combinations, independent gains/mutes, missing-device labels, picker filtering, fixed bounds, keyboard/automation semantics, contrast targets, clip-hold/offline behavior and **zero WPF binding errors**. Its retained [result copy](design/detail-verification/results.json) accompanies the review. The older 33-check slate and 28-check charcoal results are historical. Separate silent-preview interactions confirmed embedded minimise/restore, Devices open/close, preview main-window close, caption right-click system-menu access, mouse/keyboard route control and independent typed levels without connecting to an engine or audio devices. In the final preview, clicking an A fader thumb and pressing Up showed +0.5 dB while B remained 0 dB and A routing remained off; the preview was closed without applying its changes to live settings.

The 125% and 150% captures are render-density simulations, not changes to Windows display scaling. Actual display-scaling transitions and full screen-reader speech behavior remain to be checked. Alt+Space did not produce an observed system-menu popup in this environment and remains unverified; caption right-click access worked. Source/input and bus/output meter semantics were checked with supplied inert telemetry; no synthetic activity is shown as live data in normal operation.

The completed detailed slate controller is running against the original native engine. Only the desktop executable was replaced; the engine process and start time are unchanged. The **current** saved baseline is revision 3, following user changes between UI requests, with A muted at −24 dB and existing routes retained. Its before/after SHA-256 is unchanged: `D082E5A80406C58E7ACD25EFD8B56FE57234C89F6780314F095177429EB1E0F5`. Preserve this baseline rather than restoring the historical revision-1 example. The prior charcoal update preserved revision 1 at that earlier point. No driver installation, Windows default, global display setting or other audio application was changed by this UI refinement. No test sound or playback was started.

See [design-qa.md](../design-qa.md) for current captures, resolved review findings and limits. The next qualification work is the separately authorized native/audio/hardware procedure in [TESTING.md](../TESTING.md), including the agreed reboot before reliability validation. UI verification does not establish audio correctness, latency, reconnection or daily-use reliability.
