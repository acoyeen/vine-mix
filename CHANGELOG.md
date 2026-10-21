# Changelog

## 0.3.0-beta.2 — 22 October 2026

- Accept streamed and virtual Windows microphones in Physical input.
- Make Setup's app-only route clear and optional driver choices unchecked.
- Address stale header/footer redraw after resizing.
- Explain every Add Node choice/category, including filtered and disabled rows.
- Add signed release discovery, verified downloads and explicit install/restart,
  with daily-check settings, preserved workspaces and recovery checkpoints.

[Downloads, checksums and known issues](docs/releases/0.3.0-beta.2.md)

## 0.3.0-beta.1 — 22 October 2026

- Introduce VineMix naming, beta identification and copyable build information.
- Start fresh workspaces with one input wired to virtual and listening outputs;
  preserve existing graphs and start new outputs muted.
- Add explicit recovery and restartable legacy-data migration.
- Provide installed and standalone EXEs with bundled .NET and session ownership.
- Add beginner routing, installation, recovery and reporting guidance.

[Downloads, checksums and known issues](docs/releases/0.3.0-beta.1.md)

## Earlier development history


- Distinguish Controls section headers from their options with flat shaded bars,
  larger chevrons on the right, and indented child rows with a vertical guide.
  Section headers have no hover tooltips; individual options retain their help.
- Make tooltip handoff follow the currently hovered ordinary control, including
  every Controls reference row and dropdown choice. Cover Help, buttons, dial
  entries, meters and status help through one application-wide policy, with live
  content updates and cleanup when controls scroll, hide or are rebuilt.
- Make menu tooltip handoff respond to row entry and departure, and recover when
  the pointer reaches another row before pending help appears. Apply the shared
  handling to Add, node, cable and EQ menus as well as Settings and Help examples.
- Simplify Help to six topics and replace Quick reference with grouped Controls
  and individually separated action/key rows. Remove the Voice Color and canvas
  practice topics. Add Settings search for options and control instructions,
  including direct links to the matching Controls entry.
  Dials, menus, search, EQ and meters offer resettable silent examples using
  real controls and temporary values while the live mix keeps running.

- Flatten Help, Find node, Settings and shared popup menus to match the compact
  mixer interface. Replace lesson cards with open sections, reduce repeated
  headings and copy, and align menu rows, shortcuts and keyboard-focus indicators.

- Remove the central website logo's hover tooltip and background highlighting
  when hovering or pressing it. Retain its accessible name and keyboard focus.

- Centre the website logo across the full header with equal vertical spacing.
  Move the engine connection dot to the far left, enlarge it to 8 DIPs and
  vertically centre it while keeping warnings and status clear of the logo.

- Keep Settings tooltip content intact until its popup finishes closing, avoiding
  the brief empty box when crossing a separator. Remove the website logo's
  persistent underline while retaining a keyboard-only focus outline.

- Apply the Vine Mix split VM identity and outlined VineMix caption wordmark,
  IBM Plex Sans/Mono,
  forest-green actions and multi-size application/tray icons. Keep green audio
  cables, lavender Ducking sidechains, category colours and Windows high contrast.
- Place the 106 × 20 DIP caption artwork inside a keyboard-focusable website
  button that opens [vinemix.app](https://vinemix.app), while preserving caption
  dragging and window resizing.
- Draw menu checkmarks in the shared template instead of using a font glyph.
- Keep Settings help synchronized when moving between rows and nested menus,
  with one popup owned by Settings and cleared when its owner or menu closes.

- Simplify tooltips throughout the workspace to describe controls, audio behavior
  and current state. Keep gesture and keyboard instructions in Help & shortcuts.
- Separate Expander and Gate into dedicated nodes and controls. Add Static and
  Dynamic Ducking modes, with fixed Amount or Ratio controls. Compressor now
  measures only its own audio; external trigger connections belong to Ducking.
- Start newly added EQ nodes with no bands, and clear bands when resetting an EQ
  to flat or resetting its node to defaults. Centre the empty hint and band
  information within the plot, clear of its decibel scales; preserve saved bands.
- Give unavailable listening and virtual outputs the same red glow, warning
  triangle and flashing device text as unavailable inputs, with matching
  recovery help, reduced-animation behavior and accessible status.
- Reduce avoidable audio delay with shorter supported shared WASAPI periods,
  device-aware queue targets, capture pre-roll and resampler-aware playback
  priming. Preserve effect and converter quality, with bounded buffering growth
  and default-timing fallback for repeatedly interrupted streams.
- Preserve mixer cadence across short scheduling delays with bounded catch-up.
- Add timing diagnostics and offline shared-device, asynchronous buffering and
  mixer-clock regression checks. Hardware latency remains to be measured; see
  [audio latency](docs/AUDIO_LATENCY.md).

## 0.2.0 — initial repository publication

- Windows node workspace and separate audio engine, with 23 creatable node types,
  local effects, per-application capture, independent outputs and saved-graph
  compatibility.
- Synthetic native and detached WPF verification tools, build and packaging scripts,
  and Windows continuous integration.
- Publication review: relative-path persistence fixes, reduced desktop history
  allocation, pinned download hashes, and complete third-party distribution notices.

Hardware and audible qualification remain incomplete. This entry records the
initial source publication; it does not declare a fully qualified binary release.
