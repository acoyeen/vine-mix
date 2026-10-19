# Audio Mixer — Blender node style review

17 October 2026, Australia/Brisbane.

## Result and scope

**Passed for the scoped visual and in-memory UI checks.** No actionable P0/P1/P2
finding remains in the reviewed node styling. The native desktop build completed
with zero warnings and errors. The final offscreen run passed its 61 assertions
with zero WPF binding errors. It generated eight images of the actual WPF controls.

This is not live audio or release qualification. No window was shown, activated
or given a native handle. No desktop input, personal settings, audio connection,
engine restart or device/default change was used. The existing personal package
and silent endurance run were left in place.

## Reference and comparison method

- [Blender's official node anatomy](https://docs.blender.org/manual/en/latest/interface/controls/nodes/parts.html)
  and unchanged [216 × 251 reference image](docs/design/blender-reference/node-parts-overview.png).
- [Official default theme](https://github.com/blender/blender/blob/main/release/datafiles/userdef/userdef_default_theme.c)
  and [category mapping](https://github.com/blender/blender/blob/main/source/blender/editors/interface/resources.cc).
- [Final workspace](docs/design/blender-final/workspace.png): 1280 × 800 at 96 DPI;
  the canvas's fit-to-content zoom is 89%.
- [Effects overview](docs/design/blender-final/effects.png) and
  [selected state](docs/design/blender-final/effects-selected.png): 120% canvas zoom.
- [Focused EQ control](docs/design/blender-final/eq-closeup.png): 316 × 488 at
  96 DPI, with 18-pixel padding, exactly one pixel per WPF DIP.

The official reference and final full workspace, focused node, collapsed node,
high-contrast, waiting and empty states were loaded together for visual review.
The reference's source zoom is unspecified; the EQ and Mix nodes also have
intentionally different controls. Comparison therefore assessed structure,
spacing, edges, hierarchy, palette and control treatment, without claiming a
pixel-identical overlay or identical contents. The focused node uses the actual
GraphNodeView class in an isolated memory-only host, preserving its dimensions.

## Reviewed details

- Charcoal canvas, subtle grid, compact integrated header and thin toolbar.
- Narrow category headers, left collapse chevrons, understated shadows and small corners.
- Upper-right outputs and lower-left labelled inputs; curved cables and outlined sockets.
- Compact label/value fields, device selectors, mute and effect controls inside nodes.
- Five-band EQ curve and controls, compressor fields and balance control.
- White selected outline; selection leaves all graph settings unchanged.
- [Collapsed state](docs/design/blender-final/effects-collapsed.png): 27-DIP EQ header,
  visible ports and non-overlapping rounded-layout target rectangles.
- [Waiting state](docs/design/blender-final/waiting.png): missing choices and readable status remain.
- [Empty state](docs/design/blender-final/empty.png): explanatory copy and an add action.
- [High contrast](docs/design/blender-final/effects-high-contrast.png): Windows colours
  reach the entire node, numeric fields, text and sockets. This is not a complete
  accessibility conformance assessment.

## Findings corrected during review

| Finding | Correction | Final evidence |
|---|---|---|
| P2: adjacent socket hit targets overlapped | Collapsed targets are 12 × 12; expanded targets are 24 × 20 for 21-DIP rows | Geometry assertions cover both states after layout rounding |
| P2: toggling Shift during numeric dragging recalculated prior movement and jumped the value | Accumulate only each new movement at the current sensitivity | Regression checks cover Shift on/off, stationary transitions, fine movement, clamping and reversal |
| Render fixture retained stale colours after high-contrast switch | Preserve the unshown logical resource owner | Root, body, field and text resource assertions plus the final high-contrast image |
| Close-up inherited canvas offset and was clipped | Render an isolated actual GraphNodeView with copied preview state | Final close-up is complete and preserves node dimensions |

[Machine-readable results](docs/design/blender-final/results.json) include exact
assertions, image sizes and expanded/collapsed socket coordinates. Numeric changes
are applied only to in-memory models; one EQ gain change is compared against the
entire expected graph and restored. The drag checks invoke the same adjustment
methods as the mouse handlers without desktop input or mouse capture.

## Intentional adaptations and remaining validation

The Windows Segoe UI font is retained; Blender uses Inter. This is a deliberate
font difference, not a pixel-perfect claim. Audio sockets use green; status,
mute, meters and connection controls retain their audio meanings. Single-node
selection, editable reroute nodes and fixed 1280 × 800 dimensions remain.
Blender group frames, point-only reroutes and multi-selection are outside this pass.

Real mouse capture, keyboard focus, typed-entry interaction, native dropdowns,
caption dragging and non-100% Windows display scaling have not been exercised in
this review because the user is using the desktop. Verify them in a later agreed
UI session using [TESTING.md](TESTING.md). Live audio and the eight-hour endurance
run are tracked separately in [the qualification report](docs/TEST_RESULTS_2026-10-17.md).

See [the design notes](docs/BLENDER_NODE_STYLE.md) for sources and implementation.
The previous console review is retained in [design-qa-console-detail.md](design-qa-console-detail.md).

## Subsequent user-requested activation

At 21:04 Brisbane on 17 October, the user requested the new interface be opened
and the endurance test deferred. Only the personal desktop executable was
replaced. Its window opened, reached input-idle and remained responsive; the
existing engine PID/start time and binary were retained. Two read-only snapshots
showed the frame counter advancing across the controller replacement, and the
settings hash was unchanged. This does not replace the remaining gesture,
accessibility or audible qualification checks above. See
[activation evidence](artifacts/ui-switch-20261017-210400/result.json).
