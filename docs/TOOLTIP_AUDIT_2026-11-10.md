# Hover-help audit of the Rust desktop — 10 November 2026

This is the coverage record for RUST-REV-036 (ISSUE-039): every family of
hover help in the Rust desktop was inventoried, checked against the concise
content standard and either revised or kept. It also notes the separately
tracked defects that were fixed alongside it: keyboard access (RUST-REV-034),
practice EQ wording (035), cable placement (038) and accessibility of the
custom controls (027).

The work is in the working tree on `rust-rewrite` above commit `55e77c7`. It is
verified by automated tests against the real application code running without
a window. Nobody has yet looked at it in the installed application, and no
screen reader was used; see [What was not checked](#what-was-not-checked).

## The standard

From [TWEAK-018](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/TWEAKS.md#tweak-018) and the review:

- Hover help is a short control name and a brief purpose, or the state that
  matters right now.
- At most three lines and 170 characters.
- No interaction tutorial: nothing that teaches a gesture or a key (drag,
  double-click, Shift, arrow keys, Page Up, "Enter applies", "type a number",
  right-click, "press"). Those instructions live in Help and in each control's
  accessible description.
- Actionable errors, the reason a control is disabled, and limitations a user
  must know before acting stay in the hover help.
- A practice control never says it measures audio.

Two texts are deliberate exceptions, each for a stated reason:

| Text | Why it is kept |
|---|---|
| `Right-click drag to pan` | It is the setting's own name; the name contains the gesture it enables. |
| `Choose device · right-click` | A status line naming where a missing assignment is made. |

## How the inventory was taken

`crates/vinemix-ui/src/ui/tooltip_audit.rs` renders the real `App` headlessly,
visits every surface and state listed below, and records each text handed to
the one shared hover-help helper (`Tip` in `ui/widgets.rs`). Because all hover
help goes through that helper, a text that is shown is a text that is
recorded. The test fails if any recorded text breaks the standard, if a family
is not reached at all, or if a representative text for a family is missing.

```powershell
cargo test -p vinemix-ui --lib tooltip_audit -- --nocapture
```

`--nocapture` prints the whole inventory by family.

| | Before | After |
|---|---:|---:|
| Distinct texts recorded | 588 | 689 |
| Outside the standard | 205 | 0 |

Of the 205: 154 were over 170 characters, 39 were over three lines and 12 were
within both limits but taught a gesture. More texts are recorded now because
the sweep was widened after the first pass to add the device and application
pickers and 146 state variants (below); none of the reduction comes from
looking at less.

## Coverage checklist

"Revised" means the wording or the shared helper that produces it changed.
"Kept" means it was reviewed and already met the standard.

| Family | What was visited | Texts | Outcome |
|---|---|---:|---|
| Window chrome, standard workspace, groups | Title bar, toolbar, status items, the starter workspace's nodes, a named group | 41 | Revised: dials, sockets, meters, node headings, minimap. Kept: toolbar buttons, lock layout, node and connection counts, group heading |
| Cables | Hovering audio, trigger and switched-off cables in the canvas | 8 | Revised |
| Overview zoom | The same workspace zoomed out to the simplified node presentation | 41 | Revised with the shared helpers |
| Device and application pickers | Open device and application lists, including unavailable, disabled, missing and same-name rows | 45 | Revised: closed-picker help and row help. Kept: device names and interface lines, which are data |
| Settings menu | Every row, the search field, search results, submenus | 62 | Revised: search field and several rows that explained gestures. Kept: most rows |
| Canvas menus and node library | Canvas, node, cable and group context menus; the Add menu with all 32 node kinds | 82 | Revised: node library entries now use the one-line node summary. Kept: commands |
| Dialogs, updates and status | About, Credits, Updates, What's new, recovery and quit dialogs, processing-load details, status notices | 54 | Revised: a few long explanations. Kept: buttons and status lines |
| Help and practice views | Help topics, shortcut reference, all five practice lessons | 64 | Revised: practice EQ and practice meters; reference rows no longer repeat their own text as hover help |
| Every node kind | A workspace per node kind: headings, every dial and numeric entry, sockets, meters, the EQ plot, the Voice Color pad | 146 | Revised |
| State variants | Texts built by the production functions for states a static scene does not show: rejected numeric entries, disabled controls, every dial of every kind, cable types and states, meter states, EQ band states, unavailable devices | 146 | Revised with the shared helpers; error and disabled wording kept |

The families named in the task map onto these rows as follows.

| Requested family | Covered by | Decision |
|---|---|---|
| Node headings | Every node kind; node library | Revised. A heading is now the kind's name and one line saying what it does (`Reverb` / `Adds room-like decay. Wet carries the reverb alone.`). The longer explanations moved to Help |
| Dials | Every node kind; state variants | Revised. Name and purpose only (`Level` / `Adjusts this node's audio level. 0 dB leaves it unchanged.`). The range, drag, Shift, arrow, Page, typing and reset instructions were removed from hover help |
| Numeric entries | Every node kind; state variants | Revised. Same text as the dial. A rejected value still says what to enter (`Value not applied` / `Enter a number from -60.0 dB to 12.0 dB.`) |
| Sockets | Every node kind | Revised. `Audio output` / `Carries this node's audio.` The Trigger socket keeps its limitation: it is never heard, and a silent or unavailable source is a silent trigger |
| Noodles and cables | Cables | Revised. The route only (`Microphone 1 → What others hear`), plus `Off` when the cable is switched off. Connection type and saved level were removed |
| Device and application pickers | Device and application pickers | Revised. Unavailable rows keep why they cannot be chosen and what to do |
| Meters | Workspace; practice views; state variants | Revised. The repeated definition line was removed. Practice meters say `Sample peaks` and that no audio is measured |
| EQ | Every node kind; practice views; state variants | Revised. Legend and band state. The practice plot says the response is calculated and no audio is measured or changed |
| Voice Color | Every node kind; practice views | Revised. The two axes and the current position; the gestures moved to the accessible description and Help |
| Canvas controls | Workspace | Revised (minimap). Kept (fit, lock layout, zoom) |
| Groups | Workspace | Kept: name and node count |
| Menus | Canvas menus and node library | Kept, apart from the node library entries |
| Settings | Settings menu | Mostly kept; rows that described gestures were revised |
| Dialogs | Dialogs, updates and status | Mostly kept |
| Updates and status | Dialogs, updates and status | Mostly kept. Errors and what to do next are unchanged |
| Help and practice views | Help and practice views | Revised as above |

### Wording inherited from the 0.5 application

The review noted that some long texts also exist in the 0.5 C# source: the
Echo, Reverb and Distortion headings and the Trigger silent-source
explanation. They were reviewed too, not exempted. The three headings now use
the same one-line form as every other node, with their detail in Help. The
Trigger explanation is kept, shortened to three lines, because it states a
limitation that changes what the user hears.

## Where the removed detail went

- **Help.** About 22 entries were added to `ui/help.rs` so that every
  instruction removed from hover help can be found by searching: control
  gestures, the minimap, Settings search, device lists, application capture,
  trigger sockets, cable details, the Voice Color pad, peak meters, dynamics
  status, the EQ display, and a "Nodes & effects" section with one entry per
  node kind. The test `details_removed_from_hover_help_are_in_help` checks
  them.
- **Accessible descriptions.** Each control still exposes its full
  instructions to assistive technology (`widgets::describe`,
  `Tip::tip_described`, `face_description` and `entry_description` in
  `ui/dial.rs`), separately from what is shown on hover.

## Related fixes with their own acceptance checks

| Finding | What changed | Check |
|---|---|---|
| RUST-REV-034, keyboard access | Help for the focused control appears after a short delay and with Ctrl+Shift+F10, hands over between controls, works in menus and is dismissed by Escape or a pointer action | `ui/widgets_tests.rs` |
| RUST-REV-035, practice EQ | `eq::plot_help` takes a practice flag | `practice_help_never_claims_to_measure_audio` |
| RUST-REV-038, cable placement | Cable help is anchored at the hovered point on the cable, with an identity per cable | `cable_help_appears_at_the_hovered_cable`, at four window size, scale and zoom combinations |
| RUST-REV-027, custom controls | The Voice Color pad, EQ plot and dial entry have accessible names, values and descriptions; arrow keys no longer move focus off the pad or plot | `ui/app_tests.rs` |

## What was not checked

- The appearance in the installed application: wrapping, placement near screen
  edges and timing at real display scales. The tests check text, anchor
  position and identity, not pixels.
- Narrator or NVDA. The accessibility tree was inspected by tests; no screen
  reader was run.
- Native menu navigation outside the headless harness.
- Whether the shortened wording reads well to the maintainer. Every text is in
  the printed inventory for review.
