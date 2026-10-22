# VineMix forest landing direction

## 23 October release polish

The opening now links to the Beta 3 downloads and current user guide. The
documentation placeholder and private-repository tooltip are retired. Historical
layout decisions below retain their dates; they do not describe these new links.
Source, syntax and local asset references were checked without restarting a
browser preview.

## 21 October refinement

The maintainer requested only the standalone logo centered on the screen, a GitHub
button with its symbol and a prepared documentation button. The opening's
sentence, scroll prompt and pause text are removed, along with the header
wordmark/navigation. The VM mark itself is centered at 50% of the viewport;
the restrained button pair sits beneath it without shifting that center.
A 320px minimum landing height keeps the buttons clear of the following
section in very short or heavily zoomed windows.

The packaged logo letters are custom paths, not a font. Per `docs/BRANDING.md`,
keep IBM Plex Sans and use its supplied SemiBold weight for the compact buttons.
The GitHub icon is from official Octicons with its MIT notice. The GitHub link
uses the known remote at the maintainer's explicit request; its private status is
identified in a tooltip. Documentation is a disabled “Soon” placeholder.

The maintainer then reported lag and requested the preview be stopped during
development. The local server and both preview tabs were closed. The shader
now settles after five seconds rather than rendering continuously; reduced
motion still starts with a still frame. The underlying shader recipe is
unchanged. This change was checked through source, syntax and asset-path
validation only; the preview has not been restarted for a new visual review.
The Sites skill's local-only workflow was also read and applied to this edit.

The initial 20 October plan below records the previous revision, superseded
by these requested opening changes.

20 October 2026. The maintainer rejected the floating icon/beam diagram as tacky and
generic, and selected Allan Mesquita Brito's **frorest-green** shader as the
visual reference. The exact selected recipe and palette govern this revision.

## Skills applied

- [frontend-design](https://github.com/anthropics/skills/blob/main/skills/frontend-design/SKILL.md): deliberate subject-specific visual direction, restrained typography and concentrated visual emphasis. Its plan/review/build/critique process informs this page.
- Product Design context and selected-reference implementation guidance: current dossier, brand assets, captured live reference and responsive browser verification.
- [web-design-guidelines](https://github.com/vercel-labs/agent-skills/blob/main/skills/web-design-guidelines/SKILL.md): current web interface rules used for semantic controls, focus, media, scroll behavior, reduced motion and resource handling.

The upstream skill sources were reread on 20 October. No new skill installation
or frontend framework is required. The existing website is the implementation
target; the user's exact shader reference supplies the chosen visual direction.

## Plan reviewed before implementation

Palette: deep forest `#0D1B0F`, leaf `#3E8E41`, lime `#C7F464`, cream `#FFFDE1`,
charcoal `#0C0D0F`. The animated field uses the actual recipe rather than a
different gradient or a recreation of its appearance.

Typography: existing IBM Plex Sans, primarily regular weight. Preserve the
supplied vector wordmark; use a dark monochrome treatment on the bright field.
Large left-aligned copy, short line lengths, ample open space, no status eyebrow
or catalog of graphic components in the opening.

Composition: full-screen field and a single plain-language use-case sentence;
one continuous scroll transition into the actual desktop workspace; a spacious
music-sharing scene; compact, honest project information and native disclosures.
The desktop product view has one short sticky interval; phone and reduced-motion
layouts use ordinary document flow. No forced scrolling or inertial library.

The old diagram, repeated green accents, icon tiles, dot grid, logo halo and
manual-like feature grid are removed from the visual system. The shader is the
single expressive asset. Subsequent motion explains the transition into the
product rather than decorating every heading with the same entrance animation.

## Guardrails

All audio/release claims remain governed by the dossier. No public release or
source download is invented. VRChat is an intended Windows/PCVR use case with a
separate driver and incomplete receiver qualification. Actual screenshots are
identified as illustrative desktop previews.

The shader is decorative and silent. Rendering is bounded, paused offscreen or
when the page is hidden, and starts as a still frame under reduced motion.
Scrolling, navigation, product copy and disclosures remain available with
JavaScript disabled or WebGL unavailable. A real shader frame supplies the
static fallback. Source and licence provenance are recorded separately.
