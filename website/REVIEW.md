# Forest landing local review

## 23 October release polish

The opening links to the Beta 3 download and the current user guide. Product
copy reflects versioned releases and the current beta limitations. These are
content and link changes; the historical browser evidence below does not
qualify this revision. No preview server or browser was started for this pass.

## 21 October update — preview intentionally off

The opening now contains a standalone centered VM mark, a GitHub button with
the official Octicons mark and a disabled Documentation/Soon placeholder.
The header wordmark/navigation, sentence, scroll prompt and motion control
are removed. Existing Plex Sans is retained because the logo lettering is
custom vector artwork, not a font file.

The shader introduction settles after five seconds to avoid continuous
rendering. Reduced-motion and offscreen/hidden-tab pausing remain in source.
JavaScript syntax, local asset paths, same-page targets and whitespace checks
passed. Browser rendering and timing have not been rechecked for this revision:
the maintainer requested the lagging preview be shut down. The server was stopped and
both preview tabs closed; port 4173 has no listener. Do not relaunch automatically.

The prior reviewed version is retained in the private development archive.

## Prior browser-verified revision

Checked 20 October 2026. Preview: `http://127.0.0.1:4173/`.

The opening now uses the maintainer's exact **frorest-green** shader reference, a small
wordmark and one short use-case sentence. Scrolling transitions into the real
workspace, then the VRChat music-sharing scene and factual project information.
This replaces the rejected icon/beam composition.

The implementation follows the researched `frontend-design` and
`web-design-guidelines` skills plus Product Design reference/QA guidance.
[DESIGN_NOTES.md](DESIGN_NOTES.md) records the direction and
[SHADER_PROVENANCE.md](SHADER_PROVENANCE.md) records the exact source and licence.

## Verification

- Desktop, phone, wide/short windows, reduced motion and JavaScript-disabled
  fallback were checked in the local browser.
- Pointer/keyboard motion controls, navigation, Credits and the driver
  disclosure worked. The shader pauses outside the opening.
- Fifteen referenced local resources returned HTTP 200; six anchor references
  resolve. Both scripts passed syntax checks. No browser warnings/errors were
  reported.
- No horizontal overflow at the tested widths. Short windows and phones use
  normal content flow; reduced motion starts with a still shader frame.
- Shader source and preset match the selected reference. All runtime assets
  are local. No application/audio-engine processes or personal settings changed.

Full evidence, corrections and limits are in [design-qa.md](design-qa.md).
Screenshots and preceding website files are retained in the private development
archive outside the source checkout.

The server binds only to `127.0.0.1` and serves only `website/dist`. This remains
a local review. VineMix is experimental; no public release, receiver
qualification or long-session reliability is implied by website checks.
