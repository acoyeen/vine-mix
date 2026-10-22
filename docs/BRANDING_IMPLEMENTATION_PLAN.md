# Vine Mix rebrand implementation and verification

> Historical development record. Results and procedures below belong to their
> named versions; they do not qualify the current beta. See
> [current qualification](BETA_TESTING_CURRENT.md) for the active release checks.

Decision: 20 October 2026, UTC+10. Status: completed and activated.

The sections through the initial completion record describe the original rebrand.
Later header and logo refinements supersede that initial mark-only direction;
[BRANDING.md](BRANDING.md) describes the current wide logo and interface.

## Initial accepted direction

Use the selected split VM symbol, with parallel diagonals, a visible gap and no
wordmark beneath or beside it. Keep the native node workspace minimal. Use IBM
Plex Sans for interface text and IBM Plex Mono for numerical entries/readouts.
Use forest green for branding and deliberate actions; retain audio green
`#78BE98`, sidechain lavender `#B8A0D8`, neutral control cables, meaningful node
categories and green/amber/red meters. Preserve Windows high contrast.

The selected reference is the green revision of the third typography study
(`exec-8ced073a-0327-49ce-99ca-20b1f6161f3b.png` in this chat's generated images).
It establishes typography and colour while retaining the existing native UI.

## Scope and invariants

- Cover caption, taskbar/tray/executable icon, nodes, rotary/numeric controls,
  meters, EQ/Voice Color labels, menus, search, help, tooltips and public docs.
- Keep the application name in OS titles, accessible names and tray commands.
- Keep the graph architecture, controls, shortcuts, undo/redo, device assignment,
  numeric commit/cancel semantics, zoom, resize and focus behavior.
- Keep AudioMixer executable, namespace, IPC, clipboard and storage identities.
- Do not change DSP or routing to achieve a visual result. Do not imply hardware
  qualification from detached rendering or synthetic audio tests.
- The maintainer states that the current setup is disposable. Ordinary rollback backups
  and process/hash checks still apply; no repeated Saved/idle confirmation is
  needed. Use an interface-only update preserving the running engine.
- Preserve unrelated uncommitted development changes in the working baseline.

## Delivery sequence

1. **Baseline and assets:** record source/process state; bundle pinned official
   IBM Plex static faces and OFL notices; create a vector split mark and crisp
   multi-size Windows icons. Verify real font family/weight/glyph resolution.
2. **Shared system:** add explicit brand/action roles, coordinate XAML/runtime
   palettes, apply Sans to normal/detached popup text and Mono to numeric entries.
   Keep icon glyph fonts and semantic cable/meter colours independent.
3. **Interface:** replace caption text with the small mark; configure executable,
   window and tray identity; use a restrained forest-green primary Add action.
   Apply typography to nodes, charts, menus and feedback without enlarging their
   footprint or hiding controls. Use glyphs covered by the bundled faces.
4. **Public identity:** correct maintained public old-name copy, update font/
   brand provenance and package notices, refresh current images, and document
   every branding surface. Retain dated historical evidence.
5. **Verification:** run branding checks and the complete offline suite. Inspect
   actual node/menu/search/help/EQ/Voice Color/unavailable-state renders, minimum
   and default windows, fractional zoom, multiple DPIs and high contrast. Exercise
   real native popups, search, help, framing and caption controls.
6. **Local activation:** stage separately, check hashes/stability, back up the UI,
   replace only the verified UI and required notices, reopen it, inspect its
   engine connection and confirm unchanged engine identity and advancing frames.

## Acceptance evidence

Keep concrete font/asset metadata, build and verification reports, inspected
images, native interaction observations and activation evidence. Inspect actual
loaded fonts and rendered resources. Scope remaining limitations precisely.

## Initial completion record

- Four pinned Plex faces and OFL notices, native two-piece vector, transparent
  SVG and nine-resolution executable/window/tray icons are implemented.
- Shared Sans/Mono styling reaches nodes, drawing controls and detached popups.
  Forest roles, mark-only caption, primary Add and active Mono treatments,
  process-private tray font and readable checkbox outlines are implemented.
  Audio/sidechain colours, category headers and meter zones are retained.
- Release build and all 17 native suites passed. Detached WPF passed 10,012
  checks with 189 renders and zero binding errors; shown-window harness passed
  43 checks with zero binding errors. Final delivery evidence is recorded below.
- Actual workspace, Settings, EQ, Voice Color, Ducking, effect nodes, help and
  high-contrast images were inspected. Real preview Fit, Settings popup,
  Ctrl+F/filtering, Escape, F1, maximise/restore and close were exercised through
  Computer Use. The installed UI was inspected for Saved revision 5981, current
  controls, green audio and lavender sidechain wires, and its Settings popup.
- Sparse Plex tooltip rendering exposed an invalid sixteen-colour assumption
  in image validation. The fallback now checks contrasting visible ink. A review
  caught and corrected the unchecked-checkbox outline with a template assertion.
- An initial activation restored the prior UI after an external font-licence
  path was missing. The corrected installer uses the verified `licenses/` copy
  and checks every input before stopping the UI. Subsequent activation passed,
  preserving engine 26784, configuration, revision, settings bytes and path
  states. Engine frames advanced 134454480 to 134502240. The built candidate
  engine is byte-identical to the running engine.
- Public docs, credits and current images are updated. See the complete
  [branding inventory and assets](BRANDING.md).

No rebrand DSP changes, real audio test signals, device/default changes or system
font installation were performed. Audible quality, hardware latency, assistive
speech, physical monitor DPI transitions and long-duration hardware endurance
remain outside this visual update's qualification.

### Initial delivered identity

- Native suite: `artifacts/rebrand-20261020-native/results.json` — all 17 passed.
- Final detached suite: `artifacts/rebrand-20261020-ui-release/results.json` —
  10,012 checks, 189 renders, zero binding errors, passed.
- Final shown-window suite:
  `artifacts/rebrand-20261020-native-ui-release/results.json` — 43 checks,
  zero binding errors, passed.
- Final activation: `artifacts/rebrand-20261020-activation-final/result.json` —
  passed, responsive UI **52740**, engine **26784** preserved, frame count
  **148711440 → 148759680**, revision **5981**, exact settings bytes unchanged.
- Delivered UI SHA-256:
  `6227C5780BE8680C4E765427205B1277E663ECD690B619584555554A3E01F9D4`.
- Unchanged engine SHA-256 (also matches built candidate):
  `8937839894EC93A92831294DE587F9075B1DE0D223A4DD6059ABEB84E39E6875`.

The final glyph audit replaced the empty-workspace fullwidth plus with the
bundled font's ASCII plus. The complete UI suites passed again on that delivered
binary. Backups, baseline patch/source archive, failed/successful installer
records and full render evidence are retained privately outside the repository.
Any `artifacts/` paths below are historical record identifiers.

### Transparent Windows icons — 20 October 2026, 14:29

The maintainer requested a transparent taskbar icon while retaining the white M and
green V. Removed the background plate and its border from the canonical icon
generator; regenerated `app.ico`, `tray.ico` and `app.png`. The executable/window/
taskbar and default shortcut icon share `app.ico`; notification-area branding
uses `tray.ico`. The native caption mark and SVG were already transparent.
No separate named desktop/Start Menu shortcut or installer artwork was found.

All nine sizes retain their exact opaque logo colours. Packaged-icon checks now
verify clear outer margins and the gap between letters. Release publish passed
without warnings/errors; the detached suite passed **10,030 checks**, **189
renders**, **zero binding errors**:
`artifacts/transparent-icons-20261020/ui/results.json`.

Activation passed in `artifacts/transparent-icons-20261020-activation/result.json`.
Responsive UI **62504** has SHA-256
`C962AE418D7D899A0AEA47B689FF6195A5D50525E8FAB9C9AB9BDEE54D9AD2B9`.
Engine **26784** and its binary stayed unchanged. Frames advanced **173523600 →
173576400**; committed configuration, revision **5981** and exact settings bytes
were preserved. This replaces the earlier UI identity above.

### Caption spacing and Settings help - 20 October 2026, 15:06

The mark is now 20 x 20 DIPs with breathing room inside a focusable caption
button opening `https://vinemix.app`. Shared menus use a drawn tick. Settings
owns its hover-help popup so content follows adjacent and submenu rows reliably,
with pending delays cancelled on closure and keyboard help retained.

Release publish passed without warnings/errors. The delivered UI passed
10,051 detached checks, 189 renders and 77 shown-window checks, with no binding
errors. Actual pointer transitions, tick rendering and keyboard help were also
checked in native windows. Reports:
`artifacts/caption-menu-20261020/ui-final-2/results.json` and
`artifacts/caption-menu-20261020/native-ui-final/results.json`.

Activation passed in `artifacts/caption-menu-20261020-activation/result.json`.
Current responsive UI **23032** has SHA-256
`3E961A35D3A45A9E810EA3C9A253AD11FEC7A077E5B3C6DA0E38DE8CBAA9545C`.
Engine **26784** and its binary remain unchanged. Frames advanced
279190320 -> 279239040; committed configuration, revision **5983** and exact
settings bytes were preserved. This supersedes the prior UI identity above.

### Separator flash and logo underline - 20 October 2026, 15:16

Settings now retains tooltip content until its native window finishes closing,
preventing premature empty-box layout on separators. The new regression fails
against the former close order and passes after the fix. The logo uses a plain
hover/pressed template without an underline, retaining a keyboard-only outline.

Final Release passed 10,051 detached checks, 189 renders and 90 shown-window
checks with zero binding errors. Evidence is in
`artifacts/tooltip-separator-20261020/`; no Computer Use tools or desktop input
injection were used. The visual hover transition was not manually inspected.

Activation passed in `artifacts/tooltip-separator-20261020-activation/result.json`.
Current responsive UI **71480** has SHA-256
`48AECAF54FF2A48C5CA567C283A892EDE178DEF68C59DD188177FE1D4A443915`.
Engine **26784**, its binary, committed configuration, revision **5986** and exact
settings bytes were preserved; frames advanced 308150880 -> 308205120.

### Header alignment - 20 October 2026, 15:31

The website mark is centred across the complete visible header with equal
vertical space. The existing engine connection indicator is now 8 DIPs, at the
far left and vertically centred. Warnings/status remain in separate side regions.
The divider/canvas position, link, connection semantics and keyboard access stay
intact. Current specification and screenshot are in [BRANDING.md](BRANDING.md).

Release passed 10,105 detached checks, 189 renders and 90 shown-window checks,
zero binding errors. See `artifacts/header-alignment-20261020/`. Activation
passed in `artifacts/header-alignment-20261020-activation/result.json`; responsive
UI **54380** has SHA-256
`1841AB354E1CFC4CAE4BA83699CE935A810B87CA3CDBB5AFD38762ACD8EF8FA0`.
Engine **26784**, its binary, configuration, revision **5986** and exact settings
bytes were preserved, with advancing frames.

### Logo hover highlight - 20 October 2026, 15:48

Removed the website logo's hover background. Release and 10,105 detached checks
passed (189 renders, zero binding errors). Evidence:
`artifacts/logo-hover-20261020/ui-final/results.json` and
`artifacts/logo-hover-20261020-activation/result.json`. Current responsive UI
**2536** has SHA-256
`299763AEABC59C467580959E098CD51F3CA8724476E16722FF8FC0D291B00ECD`.
Engine **26784**, configuration, revision **5986** and exact settings bytes were
preserved, with advancing frames.

### Logo pressed highlight - 20 October 2026, 15:54

Removed the remaining pressed background so mouse interaction leaves the logo
transparent. Release and 10,105 detached checks passed (189 renders, no binding
errors). Reports: `artifacts/logo-press-20261020/ui-final/results.json` and
`artifacts/logo-press-20261020-activation/result.json`. Current responsive UI
**20492** has SHA-256
`8B92BD7D088B098AECDFFEFC893C966591AE2B43CDC515AB29F942D7A9FDD04A`.
Engine **26784**, configuration, revision **5986** and settings bytes were preserved.
