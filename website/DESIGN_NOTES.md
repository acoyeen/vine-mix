# VineMix forest landing direction

## 23 October Beta 4 release links

The download action and product version now point to `0.3.0-beta.4`. This
release update keeps the existing layout, shader, headline and motion behavior.
Earlier dated entries below retain their original version references.

## 23 October morphing headline refinement

The maintainer requested a Magic UI morphing headline above the centered logo.
The chosen source is [Morphing Text on 21st.dev](https://21st.dev/@dillionverma/components/morphing-text),
with [Magic UI's upstream documentation](https://magicui.design/docs/components/morphing-text).
Its blur and alpha-threshold technique is adapted to local plain JavaScript,
without a React dependency. The logo remains centered in the viewport. The new
headline uses dark forest letters over the lighter upper shader field, bundled
IBM Plex Sans SemiBold and the same `drop-shadow(0 8px 20px #07170e73)` as the logo.

The sequence starts with “Your sound. Your rules.” and begins a transition every
five seconds of active playback. The five phrases are:

- Your sound. Your rules.
- Choose who hears what.
- Bring music to your mic.
- Be VRChat’s unofficial DJ.
- See where sound goes.

Each appears once per shuffled bag; a reshuffle cannot immediately repeat the
last phrase of the previous bag. The shared motion button pauses both the
headline and shader. Both pause while the opening is offscreen or the tab is
hidden. Reduced motion starts with a static first phrase, with explicit Play
available to enable motion. A stable accessible heading prevents repeated
announcements as the visual text changes.

This refinement supersedes the earlier no-headline direction below. Source and
adaptation details are recorded in `MORPHING_TEXT_PROVENANCE.md`; browser review
results belong in `REVIEW.md` and `design-qa.md`.

## 23 October logo and motion refinement

The maintainer requested a stronger central logo that retains its signature
colours, a delayed scroll prompt and an animated shader in the preview. The VM
mark now keeps its original green `#3B8C66` and light `#E8EBF2` fills. Its size is
`clamp(150px, 15vw, 220px)`, with a soft shadow to separate it from the moving
field. Its center remains at the center of the viewport; the two existing links
remain below it. The minimum landing height is now 440px.
The obvious dark edge was first softened to a 28px blur at 20% opacity; after
feedback that this became invisible, the shadow is now slightly tighter and
stronger: `drop-shadow(0 8px 20px #07170e73)`, approximately 45% opacity.

Small pointer movement and tilt give the mark a restrained response on devices
with a fine pointer. Reduced motion disables that response. The opening retains
the sparse composition: no headline, wordmark, header navigation or visible
motion label. The Beta 3 download and current documentation links remain in place.

“Scroll for more” appears at the bottom center after five seconds with the
opening visible and the page unscrolled. It disappears on the first positive
scroll or when activated and stays dismissed for that page visit. The prompt's
entrance is brief; it does not repeatedly bounce.

The forest shader now continues animating while the opening and tab are visible.
The previous five-second stop is removed. A small icon-only pause/play button in
the lower-right corner provides manual control, and reduced motion starts the
field paused. Rendering remains capped at 30 frames per second, with a reduced
900,000-pixel budget and maximum device-pixel ratio of 1.5. The exact selected
shader, palette and preset are preserved; this budget is a rendering limit, not
a measured performance claim.

The maintainer's request to relaunch the preview for viewing supersedes the
earlier instruction to keep it stopped during development. The current local
review uses `http://127.0.0.1:4173/`. This note records the implementation policy;
browser verification results belong in `REVIEW.md` and `design-qa.md`.

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
