# VineMix tweaks

Small refinements to existing controls, layout and behaviour live here. New
capabilities belong in [development ideas](IDEAS.md); broken behaviour and
interface defects belong in [known issues](KNOWN_ISSUES.md), even when the fix
is small.

Use stable IDs such as `TWEAK-001`; never reuse them. Record the date, intended
change, reason, status and next step. Use **Captured**, **Planned**, **In progress**,
**Implemented locally**, or **Released**, with verification and the release when
available. Recording a tweak alone does not authorize implementation.

**Release update — 28 October 2026:** TWEAK-001–004 and TWEAK-013–020 are included
in published [0.4.0 Beta](releases/0.4.0-beta.md), using the latest corrections
recorded in each entry. Earlier local evidence is retained below; the
[combined release qualification](RELEASE_0.4.0_QUALIFICATION.md) records final
package results and remaining hardware/interaction limits. Other captured tweaks
are unchanged.

**Classification review — 2026-10-25:** Small presentation refinements were
separated from the ideas and known-issues logs. Original IDs, dates, prior
priorities, decisions and verification limits are retained below; old entries
link here. This review changes documentation only. Captured items remain
proposals, and previously local or released work keeps its recorded status.

| ID | Tweak | Status |
| --- | --- | --- |
| [TWEAK-001](#tweak-001) | Reorder What's New and About in Settings | Released in 0.4.0 Beta |
| [TWEAK-002](#tweak-002) | Centre the node count beneath the logo | Released in 0.4.0 Beta |
| [TWEAK-003](#tweak-003) | Move Activity into Settings | Released in 0.4.0 Beta |
| [TWEAK-004](#tweak-004) | Consistent lightweight information windows | Released in 0.4.0 Beta |
| [TWEAK-005](#tweak-005) | Animated homepage wordmark | Implemented locally |
| [TWEAK-006](#tweak-006) | Compact Bypass control and clearer state cues | Captured |
| [TWEAK-007](#tweak-007) | Discoverable fine adjustment and reset | Captured |
| [TWEAK-008](#tweak-008) | Optional longer moving-peak hold | Captured |
| [TWEAK-009](#tweak-009) | Clearer website route layout and copy | Implemented locally |
| [TWEAK-010](#tweak-010) | Preferred website domains | Captured |
| [TWEAK-011](#tweak-011) | Windows Volume Mixer branding consistency | Captured |
| [TWEAK-012](#tweak-012) | Landing actions and removed pause control | Implemented locally |
| [TWEAK-013](#tweak-013) | Monochrome app branding to match the website | Released in 0.4.0 Beta |
| [TWEAK-014](#tweak-014) | Clearer driver setup, checked defaults and VAC-first order | Released in 0.4.0 Beta |
| [TWEAK-015](#tweak-015) | Optional action notifications in the footer | Released in 0.4.0 Beta |
| [TWEAK-016](#tweak-016) | Group Settings by purpose | Released in 0.4.0 Beta |
| [TWEAK-017](#tweak-017) | Stronger group colors and direct background interaction | Released in 0.4.0 Beta |
| [TWEAK-018](#tweak-018) | Focused Level control sockets and percentage Linked control | Released in 0.4.0 Beta |
| [TWEAK-019](#tweak-019) | Short Ducking Trigger label | Released in 0.4.0 Beta |
| [TWEAK-020](#tweak-020) | Centered text-only group heading | Released in 0.4.0 Beta |

<a id="tweak-001"></a>
## TWEAK-001 Settings release links

- **Requested:** 2026-10-24. **Status:** Implemented locally on 2026-10-24.
- **Change:** Put **What's new?** immediately below **Help & shortcuts** in the
  top section. Put **About VineMix** above **Quit VineMix**, in its own section
  with separators. This reflects the maintainer's correction: the top position
  is for What's New, not About.
- **Reason:** Keep useful release highlights easy to reach and place product
  information near the bottom of the menu.
- **Verification:** Menu order, section boundaries and existing callbacks passed
  detached checks; the rendered Settings menu was reviewed.
- **Next step:** Include in the next release after maintainer review.

<a id="tweak-002"></a>
## TWEAK-002 Centred node count

- **Requested:** 2026-10-24. **Status:** Implemented locally on 2026-10-24.
- **Change:** Keep the node count on its current row and anchor it to the middle
  of the workspace, underneath the VineMix logo.
- **Reason:** Give the count a predictable position independent of surrounding
  controls.
- **Verification:** Alignment under the logo and separation from controls passed
  at 900, 1280 and 1920 pixel logical widths and 96, 144 and 192 DPI.
- **Next step:** Include in the next release after maintainer review.

<a id="tweak-003"></a>
## TWEAK-003 Activity in Settings

- **Requested:** 2026-10-24. **Status:** Implemented locally on 2026-10-24.
- **Change:** Move the Activity action from the workspace toolbar into Settings,
  immediately after What's New. The maintainer delegated its exact placement;
  this groups it with the other reference tools while keeping the toolbar clear.
- **Verification:** The Settings action invokes the existing session-history
  popup; detached checks cover its callback and removal from the toolbar.
- **Next step:** Include in the next release after maintainer review.

<a id="tweak-004"></a>
## TWEAK-004 Consistent information windows

- **Requested:** 2026-10-24. **Status:** Implemented locally on 2026-10-24.
- **Change:** Use the Beta 5 update window's minimal styling for **What's New**
  and **About VineMix**. Remove the native title-bar controls from these windows
  and the **Back to workspace** button from What's New. Keep a clear close action.
- **Interaction:** Clicking back on the main app dismisses What's New, Updates
  or About; Escape also closes them. An update installation already in progress
  must retain its close protection. Preserve release reminders, version copying,
  update cancellation and existing accessibility.
- **Reason:** Make related windows feel consistent and easy to leave without
  extra navigation controls.
- **Refinement requested 2026-10-24:** Keep What's New, About and Updates fixed
  at their existing centred opening position. Disable header dragging and
  system-menu movement; retain their non-resizable size and existing dismissal.
  Implemented and applied locally the same day. The rebuilt candidate passed
  12,943 detached checks with 237 renders and no binding errors.
- **Verification:** Detached checks cover owner clicks, caption messages,
  Escape, handler cleanup, installation protection, scrolling, update states
  and About's three actions at 440/520 widths and 96/144/192 DPI. Rendered windows
  were reviewed. Real pointer delivery on the installed desktop remains for
  maintainer review; these interaction checks did not operate the live UI.
- **Next step:** Review the installed windows and include in the next release.

### Released foundation, retained from ISSUE-009

Reclassified on 2026-10-25 as presentation polish. This Beta 5 redesign predates
the local What's New/About and fixed-position refinements above. Its release
status does not make those later refinements part of the published download.

- **Reported and resolved:** 2026-10-24. **Status:** Released in
  Beta 5; source and final-package checks passed.
- **Reported / verified:** The maintainer requested a redesign. A detached render
  confirmed the then-current window had a large blank area and a wrapped row of
  check, download, install, cancel and defer buttons, most disabled when idle.
- **Expected:** Match the application's typography, theme, spacing and quiet
  hierarchy, distinguish installed and available versions, and show relevant
  actions for checking, downloading, ready, installing and failure states.
- **Scope:** Compact themed content, selective action visibility, readable
  release notes, explicit automatic-check behavior and accessible status updates.
- **Verification:** Seven synthetic update-state previews were visually reviewed.
  Bindings, action visibility, preference behavior, editor validation, six
  width/DPI combinations and high contrast passed in detached WPF verification.
  The complete suite passed 12,905 checks and 236 renders with no binding errors;
  the production build passed with no warnings or errors.
- **Verification limit at release:** Native keyboard/pointer qualification on a
  separate test installation remained outstanding. That detached verification
  did not change a personal application or audio-engine process. Later local
  refinements and activation results are recorded in this entry.

## Local verification on 24 October 2026

The local Beta 5 UI candidate passed 12,943 detached checks, produced 237 renders
and reported no binding errors. The activation helpers passed 97 UI activation
checks and 40 shared checks, including installed-inventory preservation, bounded
file-lock retries and the short-lived desktop-launcher race.

The first replacement attempt was rolled back after Windows denied a file move;
the retried activation completed successfully. The actual Windows shortcut
opened a responsive workspace. The original engine process, start time, binary,
configuration and running audio paths were retained; frames advanced, physical
settings paths were verified, and saved workspace copies and preferences stayed
unchanged. The installed inventory was updated with the local desktop hash so
future updates can validate the installation. Private recovery evidence remains
outside the checkout.

TWEAK-001 through TWEAK-004 are local refinements to Beta 5. The published Beta 5
downloads and release version have not been replaced.

The fixed-position refinement was subsequently installed. Its activation helper
reported a startup identity-check failure, but separate read-only verification
under the Windows shell confirmed the expected installed UI and package
inventory, a responsive workspace, the original engine process and binary,
physical settings paths, and unchanged saved workspaces and preferences.
No further restart or recovery was needed. The helper issue is tracked as
[ISSUE-011](KNOWN_ISSUES.md#issue-011).

<a id="tweak-005"></a>
## TWEAK-005 Animated homepage wordmark

- **Requested:** 2026-10-24. **Status:** Implemented locally.
- **A tiny bit more focus, 2026-10-26:** Following review, reduce surrounding
  weight 580→570 and opacity 88%→86%, and increase the Bold 700 focus letter's
  CSS stroke from 0.0015 to 0.002 em (0.168 px at 84 px). All other behavior
  remains unchanged. Build, syntax, value/font-readiness assertions and
  served-source checks passed. No new browser visual verification is available.
  Next: local review.
- **Very slight focus refinement, 2026-10-26:** Reduce surrounding letters from
  weight 600 to 580 and opacity 90% to 88%. Keep the existing Bold 700 focus
  face at full opacity, adding only a 0.0015 em stroke for a tiny ink increase
  (about 0.126 px at the largest 84 px size). This does not create a heavier
  font weight. Bundle the official IBM Plex Sans Variable Latin1 font only for
  the surrounding spans, since the static face cannot express weight 580;
  preload it and await it alongside the existing Bold font. Preserve the anchor,
  copy, word/logo timing and marker removal. Forced colours restore surrounding
  opacity and remove the stroke. Build, syntax and font/style assertions passed;
  served code, font and OFL notice match the local files. Browser access remains
  unavailable, so no new visual check is claimed.
  Next: review this small adjustment locally; no public deployment.
- **Subtle typographic focus, 2026-10-26:** Remove both dots and slightly soften
  the surrounding letters. Keep the focus letter in white IBM Plex Sans Bold
  700 at full opacity; use the bundled SemiBold 600 at 90% opacity before and
  after it. Wait for both fonts before playback. Remove the unused guide CSS
  and opacity lifecycle instead of leaving hidden markers. The horizontal
  anchor, word/logo timing and logo artwork remain unchanged. This supersedes
  the paired-dot treatment. High contrast restores full opacity to the
  surrounding letters. Build, syntax, removal/font assertions and served-source
  checks passed; both font assets return successfully. Browser access remains
  unavailable, so no new visual check is claimed.
  Next: review the softer surrounding letters locally; no public deployment.
- **Paired logo-style square dots, 2026-10-26:** After the image-only exploration,
  the maintainer explicitly requested the earlier white rounded-square dot
  treatment above and below the focus letter, replacing the bars. Both use
  equal 6–13 px dimensions and 12% corner rounding. Keep their horizontal anchor
  fixed and centre the pair around the lowercase body with a constant 0.1125 em
  optical offset derived from the bundled font; do not move them per letter.
  Capitals and descenders therefore have different ink gaps. Preserve the
  existing opacity lifecycle and all JavaScript/timings. Build, source geometry
  and served-source checks passed. Browser access remains unavailable,
  so no new visual verification is claimed. Next: review the paired dots locally;
  no public deployment.
- **Focus-treatment exploration, 2026-10-26 — proposals only:** The maintainer
  requested at least five image concepts and explicitly no implementation.
  Six independently generated images use the same word, `sound`, with `u` as
  the anchor, grounded in the existing landing-page screenshot and styling.
  Displayed order: [1 — mint letter](../website-redesign/reference-assets/focus-concepts/01-mint-letter.png),
  [2 — anchor underline](../website-redesign/reference-assets/focus-concepts/02-anchor-underline.png),
  [3 — inward pointers](../website-redesign/reference-assets/focus-concepts/03-inward-pointers.png),
  [4 — corner focus](../website-redesign/reference-assets/focus-concepts/04-corner-focus.png),
  [5 — reverse focus](../website-redesign/reference-assets/focus-concepts/05-reverse-focus.png),
  and [6 — typographic focus](../website-redesign/reference-assets/focus-concepts/06-typographic-focus.png).
  These compare colour, external markers, framing, inversion and type weight;
  they are static visual proposals, not tested reading improvements. Stronger
  cues add visual prominence; quieter ones preserve more of the existing word.
  Website source and running behaviour are unchanged. Next: select or refine
  a treatment; implementation requires a subsequent request.
- **Thicker, opaque reading guides, 2026-10-26:** Increase bar width from 3 to
  4 px and remove the colour's 60% alpha, making the bars fully opaque during
  steady reading. Preserve their lengths, fixed positions and existing entry/
  exit fades. Build and source assertions passed; served CSS returns HTTP 200
  and matches source exactly.
  Browser access remains unavailable, so no new visual verification is claimed.
  Next: review the stronger guides in local playback; no public deployment.
- **Stronger guides and quicker crossfades, 2026-10-26:** Make the bars thicker
  and slightly longer: width 2→3 px and height 10–18 px to 12–24 px, retaining their fixed
  inner ends 0.7 em from the reading centre. Shorten ordinary additive word
  crossfades from 70 to 50 ms. Keep the first-word fade, 350 WPM word slots and
  all logo timings unchanged. Build, syntax, dimensions/timing and served-source
  checks passed. Browser access remains unavailable, so no new visual check is
  claimed. Next: review the revised size and transition pace locally.
- **Fixed vertical reading guides, 2026-10-26:** Add two thin, muted vertical
  bars above and below the fixed reading anchor. They fade in with the first
  word, remain stationary through the phrase and fade out with the return
  morph, using the reader's existing clock. Keep them outside the word/logo
  effect layers and hidden during the initial logo, blank interval, reset and
  static/reduced-motion/high-contrast views. This refines the earlier no-marker
  direction only by adding these two guides; every letter remains white Bold
  with no colour or highlight. The 350 WPM punctuation rhythm, 3.8-second logo
  hold and 250 ms gap are unchanged. Build, syntax and direct paint-state checks
  passed: guide opacity moves 0→1 on entry, stays at 1 through later words,
  then returns to 0 during the morph; reset and destruction clear it. Served
  JavaScript/CSS match source. Browser access remains unavailable, so no new
  visual verification is claimed.
  Next: review the guides in local playback; no public deployment.
- **More breathing room after the shimmer, 2026-10-26:** The maintainer asked
  the large wordmark to linger slightly longer after its shimmer and the blank
  interval after its fade to last a little longer. Extend the expanded hold
  from 3,000 to 3,800 ms and the post-fade gap from 120 to 250 ms. All other
  logo behavior and word-reading timing remain unchanged. Implemented locally;
  build, syntax and served-source checks passed. No new browser visual check is claimed.
  Next: review the revised pacing in the local preview; no public deployment.
- **Match the reference RSVP behavior, 2026-10-26:** The maintainer requested
  matching [snowfluke/rsvp-speed-reader](https://github.com/snowfluke/rsvp-speed-reader).
  Adapt its raw-string-length focus positions and punctuation weights, keeping
  its default optional length variability off. Sentence punctuation receives
  twice the base weight and comma/semicolon/colon receive 1.5 times, normalized
  within each phrase to retain the existing 350 WPM average. This supersedes
  the older equal-duration word rule and removes the separate final-word
  120 ms allowance. Limit each word fade to 75% of its slot so at least the last
  quarter is fully visible. Preserve the all-white Bold styling, invisible
  alignment, logo sequence and existing controls; add no reader controls.
  Source inspected on 2026-10-26 in
  [`utils/textProcessor.ts` at the pinned commit](https://github.com/snowfluke/rsvp-speed-reader/blob/755230109665b1a5d7f59f038e773f99a193c251/utils/textProcessor.ts);
  its MIT notice is retained in
  [the bundled attribution](../website-redesign/dist/assets/licenses/SnowflukeRSVP-MIT.txt)
  and linked from the credits. Build and source checks pass, including eight
  focus cases and all 24 phrases/242 word slots at exactly 350 WPM per phrase.
  Slot lengths range from 131.868 to 316.484 ms; each keeps at least 25% fully
  visible. Served reader code matches local source, and the homepage, credits
  and MIT notice return successfully. Browser access is currently unavailable,
  so no new visual or browser-lifecycle check is claimed. Next: review playback
  when browser access returns.
  No public deployment or desktop release is implied.
- **Remove focus-letter decoration, 2026-10-25:** The maintainer requested a
  clean slate with no dot, square, highlight, colour or other defining treatment
  on the focus letter. Remove the marker and its animation/CSS implementation;
  all letters share white Bold 700 styling. Retain invisible reading alignment
  and the existing word/logo sequence. Local desktop and phone checks confirmed
  uniform styles, no marker and working playback. Build/syntax checks passed.
  Next: review the refreshed local preview. This supersedes the marker trials.
- **Bolder animated words, 2026-10-25:** Use real IBM Plex Sans Bold 700 for
  the animated words to bring their weight closer to the VineMix wordmark.
  Bundle the existing licensed Bold font, preload it and await it before
  playback. Keep the current font size, fixed white square and timing. Local
  Chrome checks confirmed the loaded bold face, descender clearance and longer
  word fit on a 320 px phone view; build and syntax checks passed. Next: review
  the heavier type in the refreshed local preview.
- **Rounded square marker, 2026-10-25:** Change the reading dot to a rounded
  square resembling the dots above the VineMix wordmark's i letters. Use 12%
  corner rounding, retaining the fixed lower position, 13 px desktop / 6 px
  minimum size, white colour and fades. Desktop and narrow-phone checks passed.
  Next: review the refreshed local preview.
- **Fixed white dot below the letter, 2026-10-25:** Restore the dot below the
  reading letter with one fixed position and enough clearance for letter tails.
  Keep its 13 px desktop / 6 px minimum size and make it pure white at full
  reading opacity; preserve existing entry/exit fades. Local Chrome checks
  confirmed stationary coordinates across word changes, white colour and
  descender clearance on desktop and a 320 px phone view. Next: review the
  refreshed local preview. This supersedes the upper-marker trial below.
- **Fixed dot above the letter, 2026-10-25:** The maintainer requested trying
  the dot above the reading letter, close to the text but without any movement.
  Keep the 13 px desktop / 6 px minimum sizing and move it to one fixed upper
  position. Remove the per-letter descender offsets and their interpolation;
  only opacity changes with the sequence. Local checks confirmed one x/y
  position across ten words and the logo fade, separation above the `i` dot
  at desktop and 320 px phone width, and working mid-fade pause. Next: review
  the upper marker in the local preview; no public deployment.
- **Dot size and spacing correction, 2026-10-25:** The maintainer found the
  implementation much smaller and farther from the letter than the approved
  image. Increase the dot from 6 px to 13 px on desktop (6 px minimum on phones)
  and bring it to roughly a 7 px gap beneath ordinary desktop letters. Preserve
  the horizontal anchor, with a small eased vertical adjustment for descenders
  on the existing word-crossfade clock. The revised desktop view was compared
  with the original concept; desktop/mobile descender clearance and pause/cleanup
  were checked. Next: review the corrected local preview.
- **Stationary focus dot, 2026-10-25:** After reviewing three image concepts,
  the maintainer selected a small silver dot beneath the focus letter. Replace
  the gray selection-style highlight with one fixed dot, 6 px on desktop and
  at least 4 px on smaller screens. Fade it in with the first word, hold it
  steady through the sentence, and fade it out during the existing logo morph.
  Remove the old highlight styling and copied exit glyph. A lower vertical
  offset keeps letter tails clear. Local Chrome checks covered desktop/mobile
  spacing, pause/resume, fade completion, reduced motion and forced colors;
  see [website QA](../website-redesign/QA.md). Next: review the dot in the
  local homepage. No public deployment or desktop-app release.
- **Softer highlight and morph exit, 2026-10-25:** Lighten the flat highlight
  to RGB 180 gray at 28% opacity. Keep ordinary word changes additive; as the
  final word morphs into the logo, fade the highlight and increase its blur over
  the same 400 ms clock instead of removing it abruptly. A matching transparent
  glyph preserves the highlight's exact geometry outside the text's threshold
  filter. Desktop and mobile alignment, pause/resume, completion and reduced-motion
  cleanup were checked locally. Next: review the smoother transition in Chrome.
- **Focus-highlight correction, 2026-10-25:** The maintainer specified a flat
  Microsoft Word-style text highlight, not a rounded card. Replace the padded,
  bordered pseudo-element with the focus letter's plain inline gray background.
  It shares the outgoing/incoming text layers' existing additive crossfade;
  no independent transition or layout spacing is added. Live Chrome inspection
  confirmed complementary layer opacities and `plus-lighter` on both tokens.
  The highlight remains disabled during the logo morph. Next: review locally.
- **Monochrome foreground and mint backdrop, 2026-10-25:** The maintainer found
  the green foreground distracting despite liking the page and controls.
  Keep the landing logo and every word white, use a muted mint Orb palette,
  and identify each focus letter with a rounded neutral-gray highlight instead
  of a coloured glyph. The highlight disappears before the word-to-logo morph.
  Remove RGB parsing, per-frame tint interpolation and obsolete green overrides;
  retain the timing, geometry, silver shimmer and 40% shadow (now neutral black).
  The favicon and branding elsewhere keep their original colours. Local browser
  checks and 3,602 matched timing/state samples are recorded in
  [website QA](../website-redesign/QA.md). Next: review the highlighted-word
  treatment on the local homepage; no public deployment or app release.
- **Background and controls, 2026-10-25:** The maintainer replaced the smoky
  direction with [P Shader by Diego Biaggioni](https://21st.dev/@digogois2/components/p-shader)
  and delegated the colours. Use an ink/plum/violet orb with a 46% black overlay
  so the signature-green letters remain distinct. Replace the cream/olive hero
  buttons with silver and slate, including coordinated supporting controls.
  The approved inverted reader, layout and shadow are unchanged. The selected
  Orb equations match the source pixel-for-pixel in eight fixed-frame checks;
  responsive layouts, pause, reduced motion, high contrast and static fallbacks
  were checked locally. Retire the active smoke renderer/assets and retain MIT
  attribution. See [website QA](../website-redesign/QA.md). Next: review the
  local homepage; no public deployment or app release.
- **Latest integration, 2026-10-25:** The maintainer approved the refined
  inverted prototype for the full homepage. The final word now morphs into the
  centred green/light VM symbol; after 700 ms, it expands over 390 ms into a
  pure-white wordmark. Retain the three-second hold, single 550 ms silver sweep,
  fine sparkle, 220 ms exit fade and 120 ms pause before the next sentence.
  The selected charcoal backdrop, overlay, placement and shadow are retained.
  Cleanup keeps only this sequence, caches SVG geometry/colour references,
  removes unused shader calculations and prevents paused pointer measurements.
  System high-contrast mode now stops the hidden background and uses readable
  SVG colours. All 12,001 sampled states match the approved prototype; shader
  cleanup is pixel-identical in three fixed frames. Responsive, pause,
  visibility, reduced-motion and fallback checks passed; see
  [website QA](../website-redesign/QA.md). The prototype and lower page are
  preserved. Next: review the full local homepage; no publication or app release.
- **Origin:** Earlier website study captured 2026-10-23 as IDEA-046. Its dated
  decisions moved here on 2026-10-25; the original ID remains a redirect.
- **Change:** Replace the landing-page mark with the approved
  [word-reader prototype](../prototypes/word-reader/HANDOFF.md), keeping its
  artwork, copy and timing over the forest shader. Retain the shadow's offset
  and blur, reducing its opacity from about 45% to 40%. The first follow-up added
  a 16% black layer above the shader and below all foreground content. The next
  refinement increases that layer to 30% and changes the forest to darker greens
  and mint around the logo's `#3B8C66`, with a matching fallback poster.
- **Selected refinement, 2026-10-24:** After comparing backgrounds, retain the
  off-white wordmark and lower M, with the upper V fading to signature green
  as the symbol centres. The focus letter in each word uses that same green;
  the trial mint accent was reverted. Replace the forest with Backroom charcoal
  clouds. Soften its bright anchor from `#6A7274` to `#596164` over `#080B0E`,
  retaining more visible smoke than the original. Use the selected 16% overlay,
  matching static poster, 48% vertical placement and existing 40% shadow.
- **Integration:** Applied to the full local homepage with its shared motion
  controller and lower sections preserved. Comparison controls and alternative
  shaders remain in the separate prototype. Cleanup removes obsolete route
  styles, unused motion properties, an unused shader helper and scroll-triggered
  renderer redraws. The build retires stale generated HTML/CSS/JavaScript;
  Backroom's Apache-2.0 license and attribution accompany the implementation.
- **Reason:** Use the selected animated identity on the actual homepage, then
  improve its contrast. The charcoal background avoids blending the green
  identity into green scenery while retaining subtle movement.
- **Earlier verification:** Chrome playback, pause, reduced motion, static fallback and
  responsive layout checked; see [website QA](../website-redesign/QA.md).
  The prototype and forest hashes were unchanged for the initial animation
  update. The palette refinement changes the forest color data, preserving its
  GLSL, lifecycle and word reader. Chrome inspection confirmed the new colors,
  overlay and clickable download button; the animated opening and regenerated
  fallback were visually checked. Build and syntax checks pass.
- **Verification of selected integration:** Build and syntax checks pass.
  Chrome checks cover the logo transition, pause, offscreen rendering, FAQ
  keyboard interaction, reduced motion, JavaScript-disabled rendering and
  context-loss fallback. Lower-page geometry and typography match the baseline
  at five viewport sizes without horizontal overflow. Unused screenshots are
  removed from public output while source captures remain in reference assets.
  See the [current verification record](../website-redesign/QA.md).
- **Next step:** Review the full local homepage before the next website refinement.
  This change has not been publicly deployed or included in an app release.

### Design history retained from IDEA-046

- **Single faster shimmer, 2026-10-25:** The two silver shoulders looked like
  duplicate sweeps. Replace them with a single soft silver band and reduce the
  inverted sweep from 850 to 550 ms, synchronizing the final sparkle to the new
  duration. **Status:** Implemented locally and checked in Chrome. Sparkle
  placement/size, logo holds and original preview unchanged. Next: visual review.

- **Visible white-logo shimmer, 2026-10-25:** The maintainer found the shine
  indistinct after the white expansion change. Give the moving highlight soft
  silver shoulders for contrast, and place the fine sparkle just outside the
  final x's upper-right corner. **Status:** Implemented and visually checked in
  local Chrome; sparkle size and sequence timings unchanged. Next: maintainer
  review of the inverted preview.

- **Fine sparkle and white expansion, 2026-10-25:** In the inverted prototype,
  retain the brighter sparkle's width/height but thin its rays and tighten its
  glow after the maintainer found the thick star too cartoonish. Blend the symbol
  and lettering from their original green/light colours to pure white as the
  wordmark expands. **Status:** Implemented locally. Colour endpoints, intermediate
  blend, unchanged sparkle bounds, restart, pause and reduced motion checked in
  Chrome; original-page regression remains identical. Holds and exit fade/gap
  stay unchanged. **Next:** maintainer visual review; no main-site deployment.

- **Sparkle and logo exit trial, 2026-10-25:** On the inverted local prototype,
  strengthen the wordmark's final sparkle (white, full opacity, 1.5 times its
  prior peak size, soft glow). Retain the three-second large-logo hold, then
  try a 220 ms fade out and 120 ms black pause before the first word fades in.
  Rationale: emphasize the finished identity and give the sentence a distinct
  entrance. **Status:** Implemented locally; timing, pause/resume, reduced motion
  and original-page regression checked. The outgoing logo stays hidden during
  the first-word fade. **Next:** maintainer review of this trial at
  `http://127.0.0.1:4174/inverted.html`; no main-website change or publication.

- **Inverted preview refinement, 2026-10-25:** Move the glimmer from the small
  standalone symbol to the complete revealed wordmark on
  `prototypes/word-reader/dist/inverted.html`, and reduce the small-symbol hold
  from 2.5 seconds to 700 ms. The maintainer withdrew the initial request to
  lengthen the large-logo hold; it remains three seconds. Rationale: reach the
  full identity sooner and give it the shine. **Status:** Implemented locally;
  timing, pause and reduced-motion checks passed, and the sweep was visually
  checked in Chrome. Original-page regression matches its 26,813 sampled states.
  The original comparison and main website are unchanged. **Next:** review the
  [updated local experiment](../prototypes/word-reader/HANDOFF.md#inverted-reveal-experiment--24-october-2026).

The dated notes below preserve earlier trials, superseded choices and the
prototype handoff. Statements about proposed integration describe their point
in time; the current local integration and verification are recorded above.
Earlier linked research is retained, not refreshed by this classification review.

- **Website text direction (2026-10-23):** Build a minimal local study with a
  black background and white text: open on "Vine Mix", hold for a few seconds,
  show a descriptive phrase one word at a time with a highlighted letter held
  in place, then return to the brand and repeat with the next phrase. Rationale:
  give the homepage an engaging rhythm while conveying short product messages.
  **Status at the time:** Local prototype authorized; homepage integration proposed.
  Assumption: "start up with Vine Mix" means show the brand when the page opens,
  rather than launch a browser with the desktop app. Prototype defaults to three
  seconds for the brand and 230 ms per word, with punctuation holds. It uses the
  existing website's headline copy. No reading-performance research has been
  done and no comprehension benefit is claimed. Scope is a small standalone
  HTML/CSS/JS study with no dependencies; rapid text trades impact for reading
  comfort, so pause and reduced-motion support are included. Next step: review
  [the local study](../prototypes/word-reader/README.md), choose pace and emphasis,
  then decide whether to incorporate it into the main website.
- **Prototype refinement (2026-10-23):** Maintainer replaced underline emphasis
  with the signature #3B8C66 green and requested the entire word at the original
  focus letter's heavy weight, plus substantially faster playback. Revised trial:
  600 WPM base, 60 ms punctuation allowance, with the three-second brand holds
  retained. This supersedes the initial timing and underline treatment above.
  [Dated research and rationale](../prototypes/word-reader/README.md#speed-research-and-revision--23-october-2026)
  distinguish short-sentence RSVP evidence from the design choice; no universal
  ideal speed or improved comprehension is claimed.
- **Pacing review (2026-10-23):** After viewing 600 WPM, the maintainer requested
  slightly slower text, a blur/morph-style transition and longer brand holds.
  Current local trial uses 500 WPM, 4.5-second brand holds, and a 140 ms blur-out
  plus 220 ms blur-in between brand and phrase. Within-phrase words stay sharp;
  reduced motion omits the blur. This supersedes the prior trial settings.
- **Longer-copy refinement:** Maintainer requested slower playback again, longer
  sentences, a less wide/blocky bold font, and a short blur between words. Current
  local study uses 380 WPM, four 10–11-word sentences, IBM Plex Sans Condensed
  Bold, and a 35 ms light blur on subsequent words. The green anchor, 4.5-second
  brand hold and longer brand/phrase transitions remain. These settings supersede
  the 500 WPM trial; font attribution and licensing are in the prototype README.
- **Comparison trial (2026-10-24):** Maintainer requested 350 WPM, the program's
  font, a larger unspaced VineMix title with green Vine, equal word durations,
  and a second labelled 21st.dev morph example below Blur. Current prototype uses
  the desktop IBM Plex Sans SemiBold and synchronized 171 ms word slots, including
  all transitions. First words now use the same transition as subsequent words;
  punctuation and word length no longer add time. Brand holds remain 4.5 seconds.
  The lower example adapts Magic UI's MIT-licensed blur/opacity/threshold morph;
  compare both locally before choosing a homepage direction.
- **Hybrid refinement (2026-10-24):** Maintainer requested morph only from the
  last word back to VineMix, with a slightly longer blur for every other change.
  The lower comparison now uses 55 ms word blurs (including the first word) and
  a 400 ms return morph; the upper 35 ms Blur reference remains for comparison.
  Equal 350 WPM word slots and the 4.5-second brand interval are unchanged.
- **Selected hybrid (2026-10-24):** Maintainer requested the hybrid alone, a
  slightly longer word blur, and the genuine vector used in the app header.
  Preview now shows one centered hybrid with 70 ms word blur and the unchanged
  packaged `wordmark.svg` (split VM symbol plus VineMix lettering). The 400 ms
  return morph, equal 350 WPM slots and 4.5-second identity hold remain.
- **Crossfade refinement (2026-10-24):** After noticing a dark dip between words,
  the maintainer requested a crossfade. The 70 ms word transition now overlaps
  outgoing and incoming layers with complementary opacity weights and opposite
  blur ramps. The vector-return morph and reading/identity timing are unchanged.
- **Flashing follow-up (2026-10-24):** The maintainer still perceived flashing.
  Trial removes per-word blur and uses isolated additive (`plus-lighter`)
  crossfades to avoid the shared-pixel dimming of ordinary opacity compositing.
  The 70 ms transition, 350 WPM rate and return morph remain. Research source
  and the distinction between compositing brightness and perceived word-shape
  changes are recorded in the prototype README; visual acceptance remains open.
- **Logo handoff (2026-10-24):** Maintainer preferred the sharp additive fade,
  but found the full-wordmark-to-first-word handoff harsh. Requested five seconds
  with the full identity, then collapse lettering into the VM symbol and centre
  the symbol before a longer additive fade into the first word. Local trial:
  650 ms collapse, 180 ms centred-symbol rest, 150 ms first-word fade inside the
  existing 350 WPM slot. Later words retain 70 ms fades; the last-word return
  morph remains. Original vector paths are preserved in inline animation groups.
- **Masked slide correction (2026-10-24):** Maintainer rejected shrinking/fading
  lettering. The revised 650 ms motion translates full-size, opaque text left
  with increasing speed, clipping it at the symbol's moving right boundary while
  the symbol centres in the same interval. The centred-symbol rest is extended
  from 180 to 700 ms. Five-second wordmark hold and additive reading transitions
  remain. This supersedes the earlier collapse treatment.
- **Faster slide (2026-10-24):** Maintainer requested quicker acceleration.
  Reduced the shared slide/centring duration from 650 to 450 ms, retaining the
  accelerating text motion, moving mask and 700 ms centred-symbol rest.
- **Timing balance (2026-10-24):** Maintainer retained the 450 ms slide but
  requested a slower start and stronger acceleration. Text now uses cubic
  acceleration and coordinated symbol centring starts more slowly. Full-wordmark
  hold changes from five to three seconds; centred-symbol hold changes from
  700 ms to two seconds. Reading cadence and crossfade durations are unchanged.
- **Preserved reference (2026-10-24):** Maintainer approved the resulting visual
  direction and requested safe storage for later use. Saved as word-reader-v1
  with a self-contained local archive and checksums. Future agents should start
  with [the short handoff](../prototypes/word-reader/HANDOFF.md). Website
  integration was still a separate request at that point; the later local
  integration is recorded above. Retain the reference when experimenting.
- **Inverted reveal experiment (2026-10-24):** Maintainer explicitly authorized a
  separate local page to compare a reversed brand sequence: final word morphs
  into the centred VM symbol, then the existing masked slide runs backward to
  reveal the full VineMix wordmark, which holds before the next sentence.
  **Need/rationale:** Explore a brand reveal instead of a collapse while retaining
  the established rapid-word treatment. **Status:** Implemented locally for review
  at `/inverted.html` on port 4174; the original page remains at `/`.
  **Scope/dependencies:** Shared controller, artwork, fonts and 24-phrase pool;
  no added dependencies or changes to the main website/redesign. **Assumption:**
  "Reveal the text" means the VineMix lettering. Retain the 2.5-second symbol
  hold/shine, reverse the smooth 390 ms slide, and hold the full wordmark for
  three seconds. **Tradeoff:** The full wordmark now hands off to the first word
  instead of the smaller symbol; compare that visual handoff before integration.
  **Evidence:** Original behavior regression and new-sequence Chrome checks pass;
  no new external research was needed. **Next:** maintainer visual comparison;
  [handoff and verification](../prototypes/word-reader/HANDOFF.md#inverted-reveal-experiment--24-october-2026).

<a id="tweak-006"></a>
## TWEAK-006 Compact Bypass control and clearer state cues

- **Originally captured:** 2026-10-20 in [IDEA-011](IDEAS.md#idea-011).
  **Moved:** 2026-10-25. **Status:** Captured. **Previous priority:** P2.
- **Change and reason:** Replace the full-width effect Bypass button with a
  compact control and improve its placement and active-state presentation. A
  circular button, switch, header position or position beside effect controls
  are alternatives; no shape or placement has been selected.
- **Evidence:** Source reviewed on 2026-10-24: `GraphNodeView.cs` still builds
  the full-width bottom button with theme colours, state tooltips and accessible
  status. This is a presentation preference, not a reproduced Bypass failure.
- **Scope and tradeoffs:** Preserve current Bypass behavior, saved state, undo,
  legacy-mute handling, keyboard focus, accessible state, contrast and a usable
  click target. A power icon or Enabled switch could invert today's Bypass On
  meaning or wrongly imply that processing stops.
- **Wording draft retained:** "Passes audio through without this effect's
  treatment while keeping its processing running for a quick return. Does not
  generally save CPU." Relevant effects still need contextual delay explanation;
  final copy is undecided. Dated engine findings and effect-specific exceptions
  remain in IDEA-011, along with the separate proposed Disable capability.
- **Dependency and next step:** Compare compact layouts and state wording using
  the existing behavior. Coordinate space for a possible future Disable control
  without requiring its engine changes. No prototype, live audio check or new
  external research was performed for this reclassification.

<a id="tweak-007"></a>
## TWEAK-007 Discoverable fine adjustment and reset

- **Originally captured:** 2026-10-23 as optional parts of IDEA-027, later
  retained in [ISSUE-004](KNOWN_ISSUES.md#issue-004). **Moved:** 2026-10-25.
  **Status:** Captured. **Previous priority:** P2. Origin is an earlier UI-review
  suggestion, not an approved control design.
- **Change and reason:** Make existing fine adjustment and default reset easier
  to discover and use with a pointer, especially when Shift or double-click is
  inconvenient. Consider compact contextual affordances rather than additional
  permanent controls on every node.
- **Evidence:** `RotaryValueControl.cs`, reviewed on 2026-10-24, already supports
  Shift fine adjustment and Home/double-click reset. This proposal exposes those
  existing operations; discarded edits and pointer Apply/Cancel remain ISSUE-004.
- **Scope and tradeoffs:** Keep keyboard gestures, deliberate wheel behavior,
  default values and one undoable edit per completed gesture. Balance pointer
  reachability against node clutter; exact placement and visibility are open.
- **Next step:** Compare one minimal contextual design with the existing control
  using pointer-only fine adjustment and reset. No live verification or external
  research was performed, and logging this split does not authorize a change.

<a id="tweak-008"></a>
## TWEAK-008 Optional longer moving-peak hold

- **Originally captured:** 2026-10-23 as an optional part of
  [IDEA-032](IDEAS.md#idea-032). **Moved:** 2026-10-25. **Status:** Captured.
  **Previous priority:** P3. Origin is an earlier UI-review suggestion.
- **Change and reason:** Evaluate a slightly longer moving-peak hold to make
  short peaks easier to read. No replacement duration or user preference has
  been chosen; retaining the current timing is a valid review outcome.
- **Evidence:** `Controls/StereoNodeMeter.cs` currently holds the moving peak
  for 0.1 seconds before decay; [meter behavior](NODE_WORKSPACE.md) documents it.
  Source checked on 2026-10-24; no visual comparison was performed.
- **Scope and tradeoffs:** Presentation timing only, using existing samples.
  A longer hold can be easier to read but feel less responsive. Preserve passive
  hover readings, fading overload caps and reduced-animation behavior. Do not
  revive the rejected permanent clipping latch or click-to-reset interaction.
- **Boundary and next step:** Compare a small range of hold durations before
  choosing any change. New stage/channel/above-zero telemetry remains IDEA-032;
  LUFS and true-peak measurement remain IDEA-001. No external research refreshed.

<a id="tweak-009"></a>
## TWEAK-009 Clearer website route layout and copy

- **Originally reported and locally completed:** 2026-10-24 as
  [ISSUE-006](KNOWN_ISSUES.md#issue-006). **Reclassified:** 2026-10-25.
  **Status:** Implemented locally in `website-redesign`; no public deployment
  or application release was changed by that work.
- **Original observation:** The second website section's lower connection
  detoured beneath a staggered middle node. A browser screenshot confirmed the
  layout and repeated slogan text beneath it.
- **Change and reason:** Align "What you hear" and "What you share" vertically
  so both connections are easier to follow, and remove filler copy throughout
  the site. This refines diagram presentation and wording.
- **Recorded implementation:** Stacked Listening output above Virtual output,
  connected each audio socket directly from Physical input, and simplified the
  page and credits copy.
- **Retained verification:** Desktop, tablet, phone and short-window layouts
  checked without horizontal overflow; static/reduced-motion routes and FAQ
  keyboard behavior checked. Local evidence remains in
  [website QA](../website-redesign/QA.md) and
  [node preview](../website-redesign/nodes-preview.jpg), outside the released
  application source. These checks were not rerun for this documentation move.
- **Next step:** Review the local website revision before any public deployment.

### Scroll-story refinement — 2026-10-25

- **Status:** Implemented locally on 2026-10-25, explicitly requested during
  review of the local [cumulative node story](IDEAS_IMPLEMENTED.md#idea-049).
- **Requested behavior:** Restore a closer initial three-node arrangement that
  spreads while its cables draw in with scrolling. Retain the approved
  Low / Mid / High insertion. Move Listening output diagonally instead of in
  two perpendicular legs, with a pace consistent with the other transitions
  and acceleration/deceleration at the ends. Keep the original nodes throughout
  the progression and leave enough room for their cables.
- **Copy and atmosphere:** Remove the small text above all three main headings.
  Add restrained background depth and detail without distracting from the
  routing. The separately reported clipped main heading is tracked as
  [ISSUE-012](KNOWN_ISSUES.md#issue-012).
- **Initial meter direction:** Make the synthetic microphone levels feel more like
  speech: quick attacks, pauses and occasional more energetic peaks. Continue
  animating while viewed without scrolling. Listening output remains muted and
  still; Virtual output remains unmuted. This is visual simulation only and
  must not play or capture audio.
- **First local result:** The three nodes begin closer together and spread as the
  first two cables draw. Both outputs follow one eased diagonal during the
  ducking transition, with offset starting columns keeping their cards clear.
  The first meter revision used speech-like bursts and rests. Its background
  used faint mint/violet glows and contour lines, shifting by at most 36 px with
  scroll without another animation loop. The maintainer rejected both this meter
  character and this background in the follow-up below. Main headings size from
  their content, and the small chapter headings are removed.
- **First-pass verification:** Build and existing checks passed. Geometric checks cover
  1,001 poses and 145,856 cable segments without observed overlaps or crossings.
  Forward/reverse browser samples confirm cable drawing, diagonal movement and
  retained original nodes. Stationary viewing showed 71 distinct meter states
  over four seconds; pause held them unchanged. Listening has no meter overlay,
  and the page has no audio/video elements. Desktop and fallback layout checks
  found no horizontal overflow; the reviewed desktop screenshot shows all
  heading text. Browser console had no warnings or errors. See
  [scroll QA](../website-redesign/SCROLL_QA.md) for scope and limits.
- **Reason and next step:** Make the sequence feel more natural while preserving
  legibility and cumulative routing. Maintainer visual review remains. No public
  deployment or desktop application update is part of this refinement.

#### Meter and background correction — 2026-10-25

- **Status:** Implemented and verified locally; maintainer visual review remains.
- **Correction:** The bursts felt like repeated “stabbing.” The maintainer wants
  a quick arrival at normal speaking volume, a fairly steady level with small
  variations, then a pause. The mint/violet contour background was rejected;
  a different background direction was requested.
- **Further review:** The maintainer approved the revised transition between
  speaking and silence, then requested tiny, quick random flutter both during
  the speaking plateau and near the quiet bottom. Preserve the approved phrase
  timing and overall level; this is a small variation around it.
- **Revised local behavior:** The meter now sustains phrases rather than
  restarting a large peak for each syllable, with phrase timing of 1.58–2.73
  seconds and gaps of 0.52–0.93 seconds. Before the flutter addition, checks
  measured a first speaking plateau from −17.4 to −15.0 dB and a sampled maximum
  of −13.24 dB. The final revision adds irregular flutter bounded to ±0.9 dB
  while speaking and ±0.5 dB at the quiet floor, tapering it away from the approved
  transitions. The first speaking level still arrives in roughly 125 ms.
  These are synthetic visual values, not audio measurements. The 24 fps ceiling,
  muted Listening output and silent operation remain.
- **Interim background:** A neutral charcoal drafting surface appeared only behind the
  diagram, with a fine 24 px dot field, sparse 144 px cross marks and faded edges.
  It had no animation or parallax. This echoed the application's dotted canvas
  in `desktop/VineMix.Desktop/GraphWorkspace.cs` and its dark grid palette in
  `desktop/VineMix.Desktop/Theme.xaml`.
- **Research — 2026-10-25:** [React Flow's background documentation](https://reactflow.dev/api-reference/components/background)
  describes dot, line and cross patterns and combining background layers.
  [Linear's redesign account](https://linear.app/now/how-we-redesigned-the-linear-ui)
  describes using black/white opacity and restrained colour to establish surface
  hierarchy. That interim treatment was design judgment informed by these examples
  and the native canvas; no usability study establishes it as preferable.
- **Verification:** Build and full local checks passed. An eight-second browser
  sample recorded levels from −57.20 to −14.77 dB, an uninterrupted 2.3-second
  speaking run, and 13 quiet samples with 13 distinct values. Pause held the
  levels unchanged for 450 ms. Listening has no meter overlay and the page has
  no audio/video elements. The background has no animation and stays behind
  the diagram; the 974×873 view had clear headings and no horizontal overflow.
  See [scroll QA](../website-redesign/SCROLL_QA.md) for verification limits.
- **Next step:** Maintainer review of the completed meter rhythm and background
  in the local preview. No publication is implied.

#### Native canvas, music pulse and cable drawing — 2026-10-25

- **Status:** Implemented and verified locally; maintainer visual review remains.
- **Correction and scope:** Use the application's actual dot-only canvas across
  the entire animation section, replacing the interim charcoal shading, cross
  marks and faded edges. This applies to the node animation section, not the
  whole site. Keep the approved voice meter unchanged, give the music meter a
  clear beat-heavy “duff-duff” rhythm, and draw every cable as its route appears,
  extending the opening's drawing treatment to the later stages.
- **Canvas evidence and implementation:** Local source inspection on 2026-10-25
  confirmed `CanvasGrid` in `desktop/VineMix.Desktop/GraphWorkspace.cs` draws
  1 DIP square dots at nominal 20 DIP spacing with 0.8 opacity, using the black
  canvas and `#1B1F23` grid colours in `desktop/VineMix.Desktop/Theme.xaml`.
  The website uses the matching 1 px squares, 20 px spacing, colour and opacity
  on black. It fills the full-width pinned scene, including the heading area,
  without shading, cross marks, fading or background motion.
- **Music and voice:** The music illustration now follows a 120 BPM pulse with
  a 22 ms target rise, short hold and rounded 240 ms decay. Its target floor
  varies from −29 to −27.8 dB, with four-beat accents from −10.5 to −8.5 dB.
  Checks measured 16.4–19.2 dB of painted beat contrast. The voice formula,
  flutter and attack/release response remain unchanged; its first speaking
  arrival remains about 125 ms. These remain silent synthetic meter values.
- **Cables:** All ten connections now reveal along their paths, including the
  dashed sidechain trigger. The Low / Mid / High connections draw during
  progress 0.36–0.47, and the final ducking routes during 0.86–0.97. The final
  pause state settles at 0.97 so all connections are complete. Reversing the
  scroll reverses the same drawing progress.
- **Verification:** Build and full checks pass, including unchanged approved
  voice samples. Browser checks found partial cable reveals at progress 0.075,
  0.41 and 0.90, with all ten complete by 0.98 and identical values on reversal.
  The sidechain retained its 6 px / 7 px dash pattern. A 4.6-second live sample
  observed nine music peaks spaced 0.46–0.51 seconds apart, ranging from −27.91
  to −8.67 dB. Listening has no meter overlay, and the page has no audio/video
  elements. The dot background filled the available width at 1440 px and 900 px
  viewport sizes, with clear headings and no horizontal overflow; the 390×844
  static fallback retained all three chapters without horizontal overflow.
- **Next step:** Maintainer review in the local preview. No public deployment
  or desktop application update is implied. Current evidence and limits are in
  [scroll QA](../website-redesign/SCROLL_QA.md).

#### Stronger ducking demonstration and softer cable exit — 2026-10-25

- **Request:** Make the Ducking node's music reduction more obvious and adjust
  its displayed controls accordingly. Also soften the second-to-third-stage
  cable exit, which appeared to vanish compared with the first transition.
- **Local implementation:** The native Ducking illustration uses Dynamic mode,
  a −42 dBFS threshold and 8:1 ratio, replacing −30 dBFS / 4:1. Attack remains
  10 ms and Release 250 ms. The synthetic processed-music level uses the same
  threshold and ratio; approved voice and music-source rhythms remain intact.
  The two outgoing tone cables fade over progress 0.56–0.62, three times the
  former interval, and disappear fully before the nodes move. The microphone
  to tone connection stays visible; new cables still draw in afterwards.
- **Verification, 2026-10-26:** Build and full checks pass. The simulated first
  speaking plateau produces 21.0–23.2 dB of painted music reduction. In 140 live
  browser samples, sustained speech showed 18.7–24.5 dB reduction and quiet
  recovery came within 0.001 dB of the source. Both outgoing cables faded
  smoothly from full opacity to zero across the new interval while their draw
  masks stayed complete and the microphone-to-tone cable remained visible.
  All five animated meters moved; no media elements or console errors appeared.
- **Status:** Implemented and verified locally. Next: maintainer visual review
  of the running preview. No public deployment or desktop application update
  is implied.

#### Landing shader to canvas blend — 2026-10-26

- **Request and reason:** Soften the stark boundary between the landing shader
  and the dark node showcase into a smooth transition while scrolling.
- **Change:** Blend the bottom of the shader and fallback poster into black,
  then ease the decorative surface's opacity down over the first 80% of the
  landing section's scroll distance. The logo and links remain clear. Reverse
  scrolling restores the shader from the current position. The existing shared
  scroll scheduler handles the fade; no extra animation loop is added.
- **Verification:** Build, syntax and existing story/meter checks pass. Live
  browser checks cover forward/reverse scroll, pause, 390×844 layout and reduced
  motion. The section edges meet with no gap or horizontal overflow. Reduced
  motion and JavaScript-disabled views retain the static gradient. Browser
  warnings/errors were absent; preview settings were restored.
- **Status:** Implemented and verified locally; no public deployment. Next:
  maintainer review of the blend in the running preview. See
  [the verification record](../website-redesign/QA.md#landing-to-canvas-blend--26-october-2026).
- **Perimeter refinement, 2026-10-26:** The maintainer found the bottom too dark
  relative to the other edges. Add a soft elliptical shade at the top, sides and
  corners while keeping its centre transparent. Preserve the lower black seam,
  base shade and scroll fade. **Status:** Implemented locally; build, desktop
  visual review, 390×844 layout measurements and forward/reverse fade checks
  pass. No horizontal overflow or browser warnings/errors. Next: maintainer
  review of the balanced edges; no public deployment.

#### Module order and neutral separation — 2026-10-27

- **Status:** Implemented and checked locally on port 4175; maintainer visual
  review pending. No public deployment or application release.
- **Direction:** Keep How It Works → audio pipeline → open source → downloads,
  with the existing opening and footer. The website remains silent; listening
  examples and all website audio are deferred.
- **Correction retained:** The initial separation used thin green-gray rules
  before the pipeline and downloads, plus a green-tinted download background.
  The maintainer rejected both lines and the green surface as inconsistent
  with the site's grays. These replace the earlier separation treatment.
- **Change and rationale:** Remove both section borders. Use #141517 for
  downloads, a uniformly lighter version of the existing #0c0d0f charcoal,
  while retaining mint actions. Existing section padding supplies breathing
  room. Fade the entire story progress track during the final 4% so it does
  not remain as a short divider; preserve its layout space and restore it
  when scrolling back.
- **Verification:** Build, syntax and existing story/meter/brand checks pass.
  Desktop captures confirm border-free transitions. Browser samples confirm
  progress opacity 1 → about 0.5 → 0 → 1 on forward/reverse scrolling, with
  unchanged track height. The 390 px view has no page overflow. See
  [website QA](../website-redesign/QA.md#neutral-section-separation--27-october-2026).
- **Next step:** Maintainer review of the neutral surfaces in the local preview.

#### Continuous canvas and balanced ending — 2026-10-27

- **Status:** Implemented and verified locally on port 4175; no public deployment.
- **Requested refinement:** Make the dotted background continuous between How
  It Works and the pipeline. Match module headings to “A clear path for your
  voice,” slightly enlarge effect explanations, and give the pipeline more room.
  Differentiate downloads from the adjacent open-source module by centring its
  title above the downloads, echoing the symmetrical opening.
- **Correction and rationale:** Independent grids were restarting at different
  origins. One shared background now spans both demonstrations. Main headings
  share the pipeline's Semibold font treatment; effect descriptions are 14 px
  with extra caption and canvas space. Downloads has equal desktop choices
  under a centred title. Older versions moves into secondary resource links,
  avoiding another prominent GitHub action immediately after open source.
  Neutral surfaces, module order, silent demonstrations and the footer remain.
- **Verification:** Existing build/story/meter/brand checks pass. Browser checks
  confirm the continuous boundary, matching heading metrics, reverse scrolling,
  desktop/mobile layouts without page overflow, and keyboard pipeline scrolling.
  Reduced motion and no-JavaScript fallbacks remain readable. See
  [website QA](../website-redesign/QA.md#continuous-canvas-and-balanced-downloads--27-october-2026).
- **Next step:** Maintainer review of the new rhythm and spacing in the local preview.

- **Download-choice refinement, 2026-10-27:** The maintainer requested clearer
  separation between Installer and Standalone and useful content in the blank
  area below Installer. Added a 1 px neutral gray divider between the choices:
  vertical on desktop, horizontal in the stacked phone layout. Added concise
  guidance that Setup installs VineMix, creates shortcuts and offers optional
  audio drivers, checked against the installation guide and installer source.
  This is an internal comparison divider; module boundaries retain their
  border-free treatment. Build and desktop/mobile browser review pass locally;
  buttons remain aligned and the page does not overflow. **Next:** maintainer
  visual review; no publication or installer behavior changes.

- **Installer-copy correction, 2026-10-27:** The maintainer clarified that
  describing the drivers as optional understated their role in the main
  app-to-app audio workflow. The download note now describes installation in
  VineMix's own folder and guided setup for the drivers needed to send audio
  to other apps. This reflects the dedicated app directory and user-guided
  vendor installation flow without implying every physical route needs a
  virtual driver. **Status:** Implemented and checked locally; no installer
  behavior or public release changed. **Further wording refinement:** The
  maintainer specified “drivers required for virtual inputs and outputs” in
  place of “drivers needed to send audio to other apps.” Updated and checked
  locally. **Next:** maintainer visual review.

#### Download module height — 2026-10-27

- **Status:** Implemented and verified locally on port 4175; no public deployment.
- **Request and correction:** Give Downloads more room. Background-depth ideas
  were explored as images only, then explicitly abandoned by the maintainer.
  Retain only the increased module height and the existing flat #141517 surface.
- **Change and rationale:** Increase outer vertical padding to 176/160 px on
  desktop, 144/128 px at tablet widths and 104/96 px on phones. This adds
  128/88/40 px respectively without imposing a fixed height or enlarging controls.
  Keep internal spacing, typography, divider, copy and link destinations intact.
- **Verification:** Build passed and the local server returns the exact rebuilt
  stylesheet. In the current 780 px browser viewport, section height increased
  from approximately 725.5 to 813.5 px, with no horizontal overflow. Both download
  buttons remain 200 × 52 px; computed background is #141517 with no image.
  Other breakpoint padding values were checked in source, not visually retested.
- **Next step:** Maintainer review of the [local capture](../website-redesign/review/roomier-downloads.png).

#### Downloads grain background — 2026-10-27

- **Status:** Implemented and verified locally on port 4175; no publication.
- **Request and direction change:** Incorporate the background noise from the
  selected [Music Reactive Hero Section](https://21st.dev/@lovesickfromthe6ix/components/music-reactive-hero-section)
  behind Downloads. This later reference supersedes the earlier flat-background
  direction while keeping the approved increase in module height. The earlier
  generated depth concepts remain unused.
- **Research, 2026-10-27:** The public preview uses Canvas 2D waves and film grain,
  rather than a shader. Its optional audio and post-processing features are
  outside this request. Source evidence and attribution are recorded in
  [provenance](../website-redesign/PROVENANCE.md#downloads-grain-and-waves).
- **Change and rationale:** Original, low-contrast mint-grey wave ribbons and
  fine animated grain add texture behind the existing content. No new dependencies
  or sound. Canvas rendering is limited to 22 fps, DPR 1.25 and 650,000 pixels,
  with offscreen/hidden-page suspension and a local static fallback for reduced
  motion or unavailable canvas. High contrast hides the decorative layer.
- **Verification:** Desktop/tablet/phone review, narrow-layout checks, navigation,
  motion preferences, exact served files and all nine offline suites pass. The
  existing 780 px module height, primary button sizes and download destinations
  are unchanged. See [website QA](../website-redesign/QA.md#downloads-grain-background--27-october-2026).
- **Next step:** Maintainer visual review of the local moving texture; no public
  deployment or desktop application release is included.
- **Speed and darkness correction, 2026-10-27:** The maintainer clarified that
  the movement was too slow compared with the reference, rather than actually
  static, and requested a much darker background. Wave phase now advances at
  six times the initial speed (approximately 6–9 seconds per primary cycle).
  A 50% black overlay darkens both the moving canvas and static SVG, beneath
  the unchanged foreground content. Rendering limits and motion fallbacks stay
  intact. Build, renderer lifecycle checks and local browser review passed.
- **Separate background/effect correction, 2026-10-27:** The maintainer requested
  a darker base, brighter effect and still faster movement. The prior 50% overlay
  darkened both together and is removed. The base is now #030405; brighter mint
  contours reveal a horizontal field with multiplicative grain, larger breathing
  amplitudes and approximately 2–5 second cycles at a 30 fps draw cap. Secondary
  copy is lighter to preserve at least 5:1 contrast against the bounded wave peak.
  Public preview structure/timing informed this original implementation. The
  Copy prompt action required sign-in; no gated prompt/source was accessed.
  Lifecycle and visible-motion checks pass; updated browser evidence is in
  [website QA](../website-redesign/QA.md#downloads-contrast-and-motion-correction--27-october-2026).
- **Warm accents and clearer actions, 2026-10-27:** Add a little yellow/red
  variation while keeping green dominant, and make the buttons more prominent.
  Two lower-opacity gold/red wave layers sit behind two stronger green layers;
  the static SVG follows the same palette. Primary downloads use a brighter mint
  gradient, light border, dark separation shadow and stronger keyboard focus.
  Driver links gain dark solid surfaces and clearer borders. Primary controls
  remain 200 × 52 px with the same destinations; motion timing is retained.
  Build/renderer checks, browser colour phases, keyboard focus, 320 px layout
  and forced-colour checks pass locally. See the
  [updated capture](../website-redesign/review/download-warm-accents.png).

#### Section navigation and simplified header — 2026-10-27

- **Status:** Implemented and verified locally on port 4175; no deployment.
- **Request and rationale:** Return-to-top should bypass How It Works rather
  than replaying its scroll animation. Remove its header link, leave logo and
  Docs, restyle Scroll for more, and add a matching control that stays with
  How It Works and skips to the following audio pipeline.
- **Change:** Explicit shortcuts crossing the pinned timeline use a 240 ms
  crossfade with one direct destination jump. Reduced motion/high contrast and
  unsupported browsers bypass it immediately. Normal scrolling, story height,
  node geometry and animation timing stay intact. Keyboard focus moves to the
  destination and native anchors remain usable without JavaScript.
- **Presentation:** Header is logo plus Docs at every width; the mobile menu is
  removed. Scroll for more and Skip to audio pipeline share a neutral charcoal
  pill, subtle outline and mint arrow. The skip control follows the story's
  lower edge, then leaves with the module. The landing link returns with the
  hero and no longer needs the earlier five-second delay.
- **Verification:** Build and existing story/meter/brand checks pass, along with
  new navigation checks for bypass, focus, preferences, history and interrupted
  requests. Browser clicks verified direct top/pipeline arrival, full-panel
  story entry, desktop/mobile layout, keyboard activation and static fallbacks.
  See [website QA](../website-redesign/QA.md#section-navigation--27-october-2026).
- **Label refinement:** The maintainer requested simply **Skip** on the story
  control. The visible label is shortened; its accessible name still identifies
  the audio pipeline destination.
- **Next step:** Maintainer review of the controls and short transition locally.

#### Header section links — 2026-10-27

- **Status:** Implemented and verified locally on port 4175; no publication.
- **Request and correction:** Add **How it works**, **FAQ** and **Downloads**
  as plain header text, using the existing fade navigation. This supersedes
  the earlier logo-and-Docs-only direction; the existing Docs pill remains.
- **Change and rationale:** Give visitors direct access to the main sections.
  Each new link requests the existing 240 ms crossfade, including on static
  layouts. Reduced motion, high contrast and unsupported browsers jump directly;
  native anchors, destination focus and cancellation behavior are retained.
- **Responsive layout:** Keep the desktop header in one row. At 640 px and below,
  place the navigation beneath the wordmark so all links remain visible.
- **Verification:** Build and all eight offline suites pass. Local browser
  checks confirm each destination and focus, keyboard FAQ activation and no
  navigation overlap or horizontal overflow at 1440/768/640/390/320 px widths.
  Desktop and phone screenshots were reviewed; no console warnings or errors.
- **Next step:** Maintainer review of the [local header](../website-redesign/review/header-section-links.png).

#### Minimal, centred section controls — 2026-10-27

- **Status:** Implemented and verified locally on port 4175; no publication.
- **Refinement:** The maintainer replaced the outlined pill treatment above
  with centred text and a mint arrow for both **Scroll for more** and **Skip**.
  Backgrounds and borders are removed, including on hover. Both keep a 44 px
  interaction area and a visible keyboard focus indicator.
- **Layout:** Skip follows the lower centre of the walkthrough. Its separate
  row sits below the progress track, with room reserved when fitting the scene
  in short desktop windows. Mobile retains the centred sticky control.
- **Related defect:** The landing cue lingering after scrolling is corrected
  separately in [ISSUE-015](KNOWN_ISSUES.md#issue-015).
- **Verification:** Build and existing checks pass. Browser checks at 1280 ×
  720, 900 × 680 and 390 × 844 confirm centred controls, no horizontal overflow,
  and 20 px clearance below the desktop progress track. Skip reaches the
  pipeline and transfers keyboard focus. **Next:** maintainer visual review.

#### Fade the section controls — 2026-10-27

- **Status:** Implemented and verified locally on port 4175; no publication.
- **Correction to the previous refinement:** The maintainer found the landing
  cue's immediate disappearance abrupt and wanted Skip to fade away with the
  walkthrough's progress bar. Both retain their centred text-and-arrow style.
- **Behavior:** Scroll for more follows an eased fade over the first 120 px.
  Skip shares the exact progress-bar opacity over the last 4% of the pinned
  walkthrough. Static/mobile chapters fade Skip over the final 120 px before
  the following module enters. Reverse scrolling restores both controls.
- **Accessibility:** Fully transparent controls become invisible and inert
  while retaining their layout space. Reduced motion and high contrast use
  immediate visibility changes. Native link destinations remain unchanged.
- **Verification:** Browser samples confirm gradual opacity and restoration,
  exact Skip/progress-bar matching, mobile fading, reduced-motion fallback,
  and removal of focus from a fully faded landing cue. Build and existing
  checks pass. **Next:** maintainer review of the locally hosted fades.

#### Complete the logo collapse after its scroll trigger — 2026-10-27

- **Status:** Implemented and verified locally on port 4175; no publication.
- **Request and reason:** A partial scroll could leave the floating logo half
  collapsed. The maintainer wants one complete collapse while preserving its
  existing animation timing.
- **Change:** Crossing the original start threshold triggers the complete
  move and retraction to the compact symbol. The compact state persists through
  small reverse movements; returning within 1 px of the top restores the full
  wordmark. Existing easing curves, 65 ms smoothing, anchor action and
  reduced-motion behavior are retained.
- **Verification:** Before correction, stopping at y=150 left the desktop
  wordmark 156.821 px wide. Afterwards, y=30 and y=150 both complete to 44 px
  with the same approximately 0.53 s visible settling time as the previous
  full-scroll transition in the local browser. Automated comparisons confirm
  identical animation samples and settling frame counts for partial and full
  scroll triggers. Desktop/mobile return, small-scroll jitter, resize,
  restored positions, reduced motion and lifecycle checks pass.
- **Next:** Maintainer review of the completed transition locally. See the
  [website QA](../website-redesign/QA.md#complete-logo-collapse--27-october-2026).
- **Return-trigger correction, 2026-10-27:** The maintainer found the wait until
  1 px from the top unresponsive. Expansion now begins when upward scrolling
  crosses 2 px below the original collapse threshold, then completes even if
  scrolling stops. This supersedes the top-only restoration described above;
  the animation pace and subsequent vertical-continuity correction remain.
  Desktop/mobile browser and regression checks passed locally. Tracked with
  the related logo corrections in [ISSUE-017](KNOWN_ISSUES.md#issue-017).

#### Integrate the pre-install FAQ — 2026-10-27

- **Status:** Implemented and verified locally on port 4175; no publication.
- **Request:** Bring the module from the “Design pre-install FAQ module” chat
  into the main website between Open source and Downloads.
- **Change:** Integrated the eight-question **Before you install.** section
  and its scoped stylesheet from `prototypes/faq`. It retains the charcoal
  background, shared heading typography, mint interaction colour and native
  expandable answers. The standalone preview remains separate.
- **Integration details:** Reused the site's existing fonts; kept the prototype
  answer width despite global paragraph styles. Driver copy follows the earlier
  correction: setup guides installation of drivers required for virtual inputs
  and outputs, while physical microphone/headphone routes need no virtual driver.
- **Verification:** Confirmed section order, unique IDs, eight questions and
  current served files. Mouse and keyboard opening/closing, visible focus and
  one-open-answer behavior passed. Desktop 1280/1440 px and mobile 390/320 px
  layouts have no horizontal overflow. Build and existing checks pass.
- **Next:** Maintainer review of the [integrated FAQ](../website-redesign/review/integrated-faq.jpg).
- **Surface refinement, 2026-10-27:** The maintainer approved the module but
  requested clearer colour separation from Open source. FAQ now uses #202224,
  a lighter neutral charcoal between Open source's #0c0d0f and Downloads'
  #141517 surfaces. Typography, spacing, answers and mint accents remain as
  reviewed. Desktop boundaries and an expanded mobile answer were visually
  checked; text and focus contrast remain strong. Build and served-source
  checks passed locally. See the [updated boundary](../website-redesign/review/faq-surface-separation.jpg).
- **Surface swap and fade, 2026-10-27:** The maintainer requested swapping
  Open source and FAQ backgrounds. Open source now uses #202224 across the full
  width while retaining its previous centred content width. FAQ uses #0c0d0f
  and fades over its bottom 180–320 px to Downloads' exact #030405 surface.
  The fade follows the section bottom when answers expand; only the background
  changes. Desktop/phone layouts, mouse opening and keyboard closing, continuous
  section boundaries, build and served styles were checked locally. See the
  [FAQ-to-Downloads capture](../website-redesign/review/faq-download-fade.png).
- **Keep FAQ above the fade, 2026-10-27:** The maintainer clarified that all FAQ
  content should remain on the solid surface before fading begins. The fade is
  now confined to the last 80 px of desktop bottom padding and 56 px on phones,
  leaving respectively 32/24 px of solid space after the final item. Native
  answer expansion carries the fade down with the section. Existing padding and
  total height are retained. Browser measurements confirm this clearance with
  the final answer open at 1280/390 px, with no phone overflow. Build and served
  CSS checks pass. See the [contained FAQ capture](../website-redesign/review/faq-contained-fade.png).
- **Longer fade below the content, 2026-10-27:** The maintainer requested more
  room for the fade while keeping it below all FAQ content. The fade now spans
  180–320 px, including 180 px on phones. Bottom padding grows to the fade height
  plus the retained 32/24 px solid clearance; question and answer positions stay
  unchanged. Measurements with the final answer expanded confirm 320/180 px
  fades and 32/24 px clearance at 1280/390 px. Build and served CSS checks pass.
  [Updated transition](../website-redesign/review/faq-long-fade.png); local only.
- **Answer-motion refinement, 2026-10-27:** The maintainer requested smooth
  opening and closing. Native answer wrappers now expand/collapse over 260 ms,
  including the previous answer closing when another opens. Rapid clicks
  reverse the transition; native mouse/keyboard operation and grouping remain.
  Reduced motion uses immediate toggling. Desktop/mobile checks verified
  intermediate heights, complete closure, uncut long answers, repeated toggles
  and visible keyboard focus. Build and served CSS checks passed locally. See
  the [expanded answer](../website-redesign/review/smooth-faq-answer.jpg).

#### Footer on the download canvas — 2026-10-27

- **Status:** Implemented and verified locally on port 4175; no publication.
- **Request and rationale:** Make the footer feel part of Downloads, using the
  same black canvas and a thin separator like the header.
- **Change:** Full-width #030405 background, inset header-matched divider,
  white native wordmark and plain Credits, License and Back to top links. Compact
  desktop row becomes two rows on phones. Return links use the existing fade;
  system contrast mode provides a text brand fallback.
- **Verification:** Build and existing navigation, floating-brand and build
  checks pass. Desktop 1280 px and phone 320 px layouts fit without horizontal
  overflow. Pointer/keyboard return-to-top, visible focus, 44 px minimum targets,
  forced colours and local link responses were checked. Served HTML/CSS match.
- **Next:** Maintainer visual review of the
  [local footer](../website-redesign/review/footer-black-canvas.png).
- **Monochrome favicon, 2026-10-27:** The maintainer requested that the browser
  icon match the white website branding. Added `src/favicon.svg` with the
  existing VM geometry in white and updated the main and credits pages to its
  new URL, avoiding the previously cached two-colour asset. Build and both
  served page references pass; the SVG returns HTTP 200 with the correct type
  and matching contents. The local preview was refreshed and its icon URL
  verified. Implemented locally; next step is maintainer tab-icon review.

#### Clean section URLs and quieter download buttons — 2026-10-27

- **Request:** Section shortcuts and return-to-top should not leave `#top`,
  `#faq` or other fragments in the address bar. Download buttons should be less
  bright and have no gradient. This supersedes the earlier bright-mint treatment.
- **Change:** Navigation retains the existing fades, story bypass, focus and
  motion-preference behavior, without pushing section fragments or history entries.
  Old section fragments are removed on the next shortcut while preserving the
  path, query parameters and existing history state. Native no-JavaScript and
  modified-click anchor behavior remains available.
- **Button treatment:** Flat `#315941`, pale `#edf3ee` text and a muted border;
  flat `#3b654c` hover. Removed gradients, inset highlights, shadows and glow.
  Text contrast is 7.08:1 normally and 5.92:1 on hover; keyboard focus and system
  high-contrast colors remain supported.
- **Status:** Published and verified at `https://vinemix.app/` on 2026-10-27,
  Cloudflare deployment `541cf4c7`. Local build and all nine check suites pass.
  Local browser checks confirm the logo, header shortcuts and keyboard footer
  return preserve a clean URL, remove a legacy fragment and preserve query
  parameters. The two changed public files match the verified local build.
  Live header/logo navigation keeps the root URL, both buttons have no background
  image or shadow, no browser errors were captured, and the `.dev` redirect works.
- **Next step:** Maintainer visual review of the published muted button treatment.

### Softer separation between download choices — 2026-10-27

- **Request:** Rework the line between Installer and Standalone now that the
  animated background runs behind both options.
- **Change and rationale:** Replace the vertical rule with a small muted `or`,
  aligned with the download buttons. The background remains continuous through
  the space between choices. On phones the same label sits midway between the
  stacked choices, replacing the horizontal rule. The decorative label is hidden
  from assistive technology; download names and links retain their semantics.
- **Verification:** Ten check suites pass. Browser checks confirm button/label
  alignment on desktop, centered spacing in the 390px stacked layout, no
  horizontal overflow, and removal of the previous rule.
- **Status:** Published at `vinemix.app` on 2026-10-27, deployment `9f13e5ab`.
  All 84 public files match the verified build. Live inspection confirms the
  new separator, unchanged installer/standalone destinations and no captured
  browser warnings/errors.
- **Next step:** Maintainer visual review of the live treatment.

### Plain website title — 2026-10-28

- **Request and rationale:** Use only `VineMix` in the browser tab; remove the
  descriptive suffix the maintainer found too generic.
- **Change:** Set the homepage title and matching social-preview title to
  `VineMix`. Page content and navigation remain unchanged.
- **Status:** Published at `vinemix.app` on 2026-10-28 through the authenticated
  Cloudflare plugin, version `8a6e2a5f-e63f-45ed-b8dc-81387e20d331` at 100%.
  Build and all ten check suites passed. Before publication, all 83 other public
  files matched production and the homepage differed only in the two titles.
  After publication, all 84 public files match the prepared snapshot, the live
  title is `VineMix`, security headers remain present and the `.dev` redirect
  preserves its query. The previous deployment remains available for rollback.
- **Alternative publishing check (2026-10-28):** The existing environment API
  credential verifies as active, but Cloudflare rejects access to this Worker's
  settings with HTTP 403. Wrangler has no separate authenticated session. No
  deployment was attempted with insufficient access. This earlier blocker was
  resolved when the relinked plugin's account tools became available.
- **Next step:** Complete; no further implementation required.

<a id="tweak-010"></a>
## TWEAK-010 Preferred website domains

- **Originally captured:** 2026-10-23 in IDEA-046. **Moved:** 2026-10-25.
  **Status:** Captured. **Previous priority:** P4 — Later.
- **Change and reason:** Retain the maintainer's preference for **vinemix.app**
  as the primary website, with **vinemix.dev** redirecting there, for consistent
  product links.
- **Evidence and assumptions:** [Branding](BRANDING.md) and the current desktop
  link use `.app`. The [website dossier](WEBSITE_DESIGN_DOSSIER.md) records
  previously unverified destinations; it does not establish current ownership,
  hosting or redirect behavior. No broken redirect is confirmed.
- **Scope and dependencies:** A bounded website configuration follow-up, dependent
  on verified ownership/hosting and separately authorized configuration changes.
  No domain purchase, DNS change or deployment is authorized by this note.
- **Next step:** Check live domain destinations when website work resumes, then
  identify any needed alignment. No live domain research was performed for this
  classification review.

<a id="tweak-011"></a>
## TWEAK-011 Windows Volume Mixer branding consistency

- **Originally captured:** 2026-10-23 in IDEA-046. **Moved:** 2026-10-25.
  **Status:** Captured. **Previous priority:** P4 — Later.
- **Change and reason:** Review VineMix's displayed name and icon in Windows
  Volume Mixer for consistency with the application's existing identity.
- **Evidence and assumptions:** [Branding](BRANDING.md) documents application
  and tray artwork. Those icons alone do not establish how the live audio
  session appears in Windows; no display-name or icon defect is confirmed.
- **Scope and dependencies:** Presentation only if a change is needed. Inspect
  through an appropriate ordinary desktop session while preserving audio and
  settings; do not infer that a running audio engine must be restarted.
- **Next step:** Inspect the current mixer branding in a later authorized
  verification session, then record whether any refinement is actually needed.
  No live inspection or external research was performed for this move.

<a id="tweak-012"></a>
## TWEAK-012 Landing actions and removed pause control

- **Requested:** 2026-10-26. **Status:** Implemented and checked locally;
  maintainer visual review pending. No public deployment.
- **Current direction, 2026-10-26:** The maintainer replaces Interactive Hover
  with the selected **Flow Hover Button** treatment: transparent at rest, white
  fill sweeping in, and the current download symbol replacing the reference's
  cat. The original local HTML/CSS keeps one stationary Download label in a
  160 × 54 px action, with motion/contrast fallbacks. Header Docs stays compact
  white/black. The floating VM's circular backing is also removed in favour of
  a tight SVG drop shadow; see [IDEA-051](IDEAS.md#idea-051). Build, source and
  served-output checks pass; visual review remains pending. Reference and
  license-status details are in [provenance](../website-redesign/PROVENANCE.md).
- **Fill-speed refinement, 2026-10-26:** The maintainer requested a faster white
  fill. Shorten its transition from 1,000 to 500 ms, matching the existing
  scale timing while preserving the flow path, colours and motion preferences.
  Implemented locally; next step is review in the local preview.
- **Earlier single-action direction, 2026-10-26:** The maintainer superseded the two hero
  actions with one centred **Download** link, using the selected 21st.dev
  Interactive Hover Button behavior and the existing download icon in place
  of its dot. The local 160 × 54 px action uses HTML/CSS, works on hover and
  keyboard focus, and retains motion/contrast fallbacks. Header **Docs** is a
  white pill with black text and arrow, superseding the initial subdued fill.
  The latest correction makes it 36 px tall with 13 px text and a 12 px arrow,
  because the maintainer found it too large. Download's interim mint hover is
  now superseded by a transparent surface with incoming white text and arrow.
  The maintainer then clarified that the existing 1 px light outline must remain
  on hover/focus; only the background fades. Its silver-white default, size and
  300 ms sliding labels remain; the
  unused expanding fill is removed. The mint variant's calculated 12.36:1
  contrast is historical evidence only. Destinations remain unchanged.
  The later request also removes the platform/beta note beneath Download and
  its unused styles. Build and served HTML/CSS checks pass for both changes.
  The earlier sizing/outline iterations below are history. Source attribution
  and verification are recorded in [provenance](../website-redesign/PROVENANCE.md)
  and [QA](../website-redesign/QA.md); visual review remains pending.
- **Request and correction:** The initial request explored matching landing
  buttons and a quick-access header. The maintainer then explicitly requested
  removing the pause button and fixing the existing uneven action sizes.
  The subsequent selection of example 1 authorized the quiet-frame header,
  recorded separately in [IDEA-051](IDEAS.md#idea-051).
- **Initial change and reason:** Give **Download VineMix** and **Documentation** equal
  216 × 54 px boxes in a two-column grid with a 12 px gap. Narrow two-column
  layouts shrink both equally; at 460 px and below they stack centrally at
  216 × 54 px. A 560 px minimum hero height in that stacked layout leaves room
  for the actions and scroll hint. Colours, icons and type styling are retained.
  Remove the manual pause button, its styles and its controller state/listener.
  Automatic reduced-motion, hidden-page and offscreen suspension remain.
- **Verification:** Build, story/meter, syntax and motion lifecycle checks pass.
  Bundled-font advances and CSS geometry confirm action fit across 320–1440 px
  viewport fixtures, including the narrow stack. Homepage, CSS, motion script
  and credits returned HTTP 200 and match the built files byte-for-byte.
  These are source/font/lifecycle checks; browser control is unavailable, so
  no new visual verification is claimed. Earlier pause-control checks remain
  historical evidence only.
- **Earlier follow-up:** The selected quiet-frame header retained these equal actions
  and the removed manual pause control. Its implementation and outstanding
  visual review are recorded in IDEA-051.
- **Reported visual mismatch, 2026-10-26:** A subsequent maintainer screenshot
  confirms equal outer boxes but shows uneven contents: Download has an icon,
  Documentation does not, and the platform note appears as a filled pill rather
  than the selected plain text. The earlier source/font checks did not prove
  browser appearance. The concrete defect and correction are tracked separately
  as [ISSUE-014](KNOWN_ISSUES.md#issue-014); the screenshot is pre-fix evidence only.
- **Further correction, 2026-10-26:** After an interim matched icon/label grid,
  the maintainer requested a download-arrow-to-line and open-book pair, rounder
  edges, and reported that Documentation looked thinner. The local response used
  matching 20 px icons in 160 px content grids, pill rounding and a stronger
  outline. The maintainer then explicitly requested removing that outline,
  identifying it as the reason Documentation looked smaller. Its default/hover
  border became transparent; equal outer geometry, icons, plain metadata and
  keyboard focus remained. Build and served-CSS checks passed; post-correction
  visual review is still pending under ISSUE-014.
- **Next step:** Maintainer review of the local preview.

<a id="tweak-013"></a>
## TWEAK-013 Monochrome app branding

- **Requested:** 2026-10-27. **Target:** Beta 6. **Status:** Implemented and
  applied locally on 2026-10-27; not published.
- **Need and change:** Match the website's monochrome identity throughout the
  app. Preserve the VM geometry, transparent gap, custom lettering and caption
  layout; paint the entire in-app logo with one live foreground brush. Keep
  functional action, routing, meter and warning colours unchanged.
- **Reference:** The active website redesign's header/hero use `currentColor`
  and its favicon is white. Portable SVGs now use white; application/window,
  taskbar, tray and inherited installer/shortcut icons use neutral `#808080`
  ink, which remains visible on light and dark Windows backgrounds. No new
  background plate is added. High-contrast logos follow the system foreground.
- **Scope:** Shared logo controls, packaged SVG/ICO/PNG assets, deterministic
  asset generation, existing detached branding checks and the branding guide.
  About, Updates, What's New and recovery dialogs have no separate coloured
  logo artwork. Windows Volume Mixer session identity remains TWEAK-011.
- **Local/release distinction:** The maintainer authorizes using the installed
  app to test verified development changes. This preview retains the current
  engine and release identity so a UI-only update can preserve running audio.
  It does not publish Beta 6 or replace Beta 5's GitHub downloads.
- **Earlier verification (2026-10-27):** The final detached suite passed 12,975 checks with 237
  renders and zero binding errors. Actual vector paint and every ICO frame
  passed monochrome, geometry, padding and contrast checks, including theme
  restoration and 96/144/192 DPI symbol fixtures. Asset regeneration was
  byte-identical. The workspace and icons on light/dark surfaces were visually
  reviewed using synthetic fixtures. The initial standalone-symbol test needed
  a never-shown logical Window owner for WPF theme notifications; its colour
  assertions were preserved. Production and verification builds passed, source
  identity remained stable, and the 35-file installed-mode candidate passed its
  inert smoke check. Activation safeguard suites passed 97 and 40 checks.
- **Local activation:** Passed through the trusted Windows shell and actual
  existing Explorer shortcut. The original engine retained its process identity,
  start time, path and hash; frames advanced and every previously running audio
  path was retained. Configuration, saved workspace copies and preferences were
  unchanged, with physical settings identity verified through an open handle.
  Ordinary and redirected data views were backed up outside the checkout. No
  settings were restored. The installation inventory matches the new UI; the
  reopened workspace was responsive and the engine stayed unchanged through
  the eight-second observation.
- **Earlier build identity (2026-10-27):** Source snapshot
  `9529a8e0d60483b3094fbe037a178304c9c94922ad851421b24d72834c6a9285`;
  installed UI SHA-256
  `2af92922cb7b4644896f50dc20cef587cab16f480021e570d70e2505e420db4d`.
- **Icon refinement, 2026-10-28:** The maintainer requested lighter white tray
  and desktop icons matching the in-app logo and explicitly targeted Beta 6.
  This supersedes the initial `#808080` shell-icon choice above. Regenerated
  `app.ico`, `tray.ico` and `app.png` with `#E8EBF2`, retaining all nine sizes,
  existing geometry, transparent margins and the split-symbol gap. In the same
  request, the maintainer asked for a slightly larger mark without filling all
  available space. Increased its canvas-height allocation from 72% to 80%
  (about 11% larger), retaining clear outer margins. Desktop,
  taskbar, window and inherited installer icons share the application asset.
  Updated the generator, branding guide and existing packaged-icon checks.
  This refinement was initially prepared in source without changing the running
  installation or published downloads. The revised production and verification builds
  passed; production reported zero warnings/errors. The detached suite passed
  13,352 checks with 268 renders and zero binding errors, including all nine
  sizes of both packaged icons, exact soft-white solid pixels, transparent
  margins and the symbol gap. Generation was byte-identical across repeated
  runs, and the larger PNG was visually reviewed. Actual Windows shell display
  remains for Beta 6 release verification. The earlier results and build
  identity above describe the grey-icon candidate.
- **Live icon preview, 2026-10-28:** The maintainer subsequently requested the
  brighter, slightly larger icons in the running app. A production Installed
  candidate passed its inert smoke check and was applied through the trusted
  Windows shell and existing Explorer shortcut using interface-only activation.
  Safeguard suites passed 97 and 40 checks. The original engine retained its
  process identity, start time, executable path and hash; audio frames advanced
  and running paths remained active during replacement. Configuration, saved
  workspace copies and preferences were unchanged, with the physical settings
  path verified through an open handle. Both ordinary and redirected data views
  were backed up outside the checkout; no settings were restored. The reopened
  workspace remained responsive through the eight-second engine observation,
  its installed inventory is consistent, and an independent process/hash check
  passed. Source snapshot:
  `5accde644089524aec35d65341bb85d1096194ec0827300c5d736320ee0d2edc`;
  installed UI SHA-256:
  `280c6ee004f5e148f1590fccbec2d35f0e919d9390f390dee29ace9236fa72d0`.
  This is a local Beta 6 feature preview retaining the current release identity
  and engine; Beta 6 has not been published and existing downloads are unchanged.
  Actual tray appearance remains for maintainer visual review.
- **Terminology correction and taskbar refresh, 2026-10-28:** The maintainer
  clarified that the remaining darker, smaller icon was on the taskbar, not in
  the system tray; keep the tray white. Direct extraction confirmed the running
  window and installed executable already contained the soft-white, enlarged
  icon (32px frame, visible artwork spanning coordinates 3 through 28). The
  existing taskbar pin still used the executable's long-standing icon location;
  stale taskbar caching was suspected, not visually reproduced by these checks.
  Backed up the pin outside the checkout, gave its icon a persistent,
  content-addressed copy of the reviewed application ICO, and sent targeted
  Windows shell item-change notifications. The pin's target, arguments, working
  directory and taskbar identity properties were preserved. The app and engine
  retained their process identities and binary hashes; settings/preferences and
  the physical settings location remained unchanged. No process was restarted.
  Shell extraction resolves the updated artwork; the taskbar's actual appearance
  remains for maintainer visual confirmation. This is a local pin refresh, not
  a product release or a change to the tray artwork. Beta 6 qualification should
  also check an existing taskbar pin after upgrading.
- **Beta 6 live integration, 2026-10-28:** The complete verified Beta 6 app and
  engine now run in the installed testing app, including this soft-white artwork
  and the concurrent taskbar-pin refinement. The maintainer approved the brief
  engine restart; the real shortcut launch, matching binary/inventory identities,
  preserved workspace/preferences and recovered running audio paths passed
  verification. See [the applied Beta 6 record](BETA6_DEVELOPMENT.md) for exact
  source and executable identities. This supersedes the preview's Beta 5 runtime
  identity without claiming final packaging or a published Beta 6 release.
- **Next step:** Include the verified refresh in Beta 6's matching source,
  installer/standalone downloads, release notes, checksums and signed update
  metadata when that release is prepared. Final Beta 6 packaging, delivery and
  clean-machine verification remain release work; local activation is not a
  published release. Earlier tags and downloads remain intact.

<a id="tweak-014"></a>
## TWEAK-014 Clearer driver setup and installation order

- **Requested:** 2026-10-27. **Target:** Beta 6. **Status:** Implemented and
  verified in an unpublished installer candidate on 2026-10-27.
- **Change:** Select both missing eligible drivers by default in interactive
  Setup, remove the optional labels, and state that Virtual input requires
  VB-CABLE while Virtual output requires a compatible VAC driver (Lite is
  bundled). Existing drivers stay disabled and retained; physical microphone,
  headphone and Virtual Desktop microphone routing still work without them.
  Users may clear a selection, and vendor consent remains explicitly unchecked.
- **Order and rationale:** Display and install VAC Lite first, then VB-CABLE.
  Tell users to finish VineMix Setup before restarting, so a VB-CABLE restart
  prompt does not interrupt the remaining driver setup. Confirmed VAC success
  does not by itself require a restart; VB-CABLE and explicit vendor restart
  outcomes do. An unconfirmed successful installation remains restart-gated.
- **Integration clarification:** Both complete original driver packages were
  already embedded in Setup. Removing their separate windows needs supported
  vendor invocation and clean-machine qualification. The maintainer clarified
  that this extra integration should be deferred if it requires considerable
  work; retain the vendor installers and prioritize VAC-before-VB-CABLE.
  Current evidence and primary sources are in the dated
  [driver integration review](../installer/notices/DISTRIBUTION-REVIEW.md).
- **Scope:** Installer page text/defaults, shared selection/consent and ordered
  dispatch policy, restart results, shipped installation help and isolated Setup
  verification. Verification retains the development machine's existing
  application session and audio drivers.
- **Verification:** Production app and Setup builds passed with a matching
  source receipt. Both complete driver archives passed their pinned file/hash
  and signature checks; the setup helper passed 31 detection checks. The final
  compiled Setup passed 215 isolated checks across eight operations, including
  default selections, installed/unknown/mixed detection, consent invalidation,
  preserved deselections, VAC-first mock dispatch, restart policy, silent-driver
  rejection, measured wizard layout, install/repair/downgrade guards, payload
  smoke and uninstall. The actual vendor installers were not run by these
  fixtures. The tests ran under the ordinary Windows shell and recorded physical
  settings paths through open handles. Personal settings, original process and
  binary identities, startup entries and shortcuts remained unchanged.
- **Candidate identity:** Setup SHA-256
  `de4016b70d30645b239e3c4057c72db9ac0ee0b8d630bfd604cb56914bd5c49b`;
  build source snapshot
  `dbb4de702dae8145e239df4fddcb1986606bf7eae09fc0d9380bab61addee136`.
  The candidate retains the current Beta 5 metadata for local qualification;
  no published Beta 5 download was replaced and Beta 6 was not published.
- **Next step:** Include the change in the final Beta 6 build, matching source,
  downloads, checksums, release notes and signed update metadata. Fresh driver
  installation, elevation, cancellation and reboot behaviour require a clean
  Windows qualification run; hidden wizard geometry checks do not establish
  visual or keyboard/screen-reader qualification.

<a id="tweak-015"></a>
## TWEAK-015 Optional action notifications in the footer

- **Requested:** 2026-10-28. **Target:** Beta 6. **Status:** Implemented and
  applied to the installed live testing app on 2026-10-28; not published.
- **Change and rationale:** Add a saved **Show action notifications** toggle to
  Settings so users can hide routine timed action messages at the bottom of the
  workspace. Default to on to preserve existing behavior. Switching off dismisses
  the current routine message and stops its countdown; switching on only affects
  future messages. Errors and save status remain visible. Activity retains recent
  edits and the latest message even when routine notifications are hidden.
- **Scope:** Local interface preference, Settings menu, footer presentation,
  focused regression coverage, user guidance and bundled release highlights.
  Audio routing and undo history are unaffected. The request authorizes
  implementation; default-on and preserving error feedback are implementation
  assumptions based on the request to hide routine action notifications.
- **Verification:** Detached WPF verification passed 13,367 checks, 268 renders
  and zero binding errors. Focused checks cover preference reload and legacy
  defaults, immediate dismissal and timer cleanup, retained error visibility,
  Activity retention, future messages after re-enabling, and unchanged routing,
  save status and undo history. The rendered Settings menu was visually reviewed.
  Production build and inert delivered-app smoke passed; activation safeguard
  suites passed 97 interface-only and 40 shared checks.
- **Local activation:** The trusted Windows-shell activation and actual existing
  shortcut launch passed without rollback. The same engine process and binary
  were retained, frames advanced and running paths were preserved. Saved settings
  and preferences stayed unchanged; physical settings identity was verified from
  an open file handle. Private backups and exact provenance remain outside the
  checkout; see [the development record](BETA6_DEVELOPMENT.md#action-notification-toggle--28-october-2026).
- **Next step:** Maintainer use of the installed toggle, followed by final Beta 6
  package qualification when authorized. Detached interaction checks are not a
  claim of operating the toggle with a real pointer or VR input. Beta 6 remains
  in development and nothing was published.

### Tooltip refinement — 2026-10-28

- **Requested change:** Shorten the **Show action notifications** hover tooltip.
  The maintainer finds the current explanation excessive; use brief,
  purpose-focused wording while retaining detailed guidance in Help and
  accessibility text where useful.
- **Evidence and scope:** The earlier Settings tooltip repeated how disabling
  notifications affects the footer, errors, save status and Activity. The toggle
  already works; this is a wording refinement, not a behavior change. The group
  hover tooltip receives the same treatment under [TWEAK-017](#tweak-017).
- **Implemented wording:** “Show brief messages after workspace actions.” The
  same concise text supplies accessible help; the toggle's behavior is unchanged.
- **Status and evidence:** Implemented and locally integrated on 2026-10-28 with
  the group refinements. The combined detached suite passed 13,526 checks with
  273 renders and zero binding errors; production build, inert app smoke and
  guarded UI-only activation also passed. No real-pointer verification of this
  tooltip refinement is claimed. See [the development record](BETA6_DEVELOPMENT.md#group-refinements-and-node-stacking--28-october-2026).
- **Next step:** Maintainer review in the installed Beta 6 app, followed by
  release qualification when explicitly authorized. Nothing was published.

<a id="tweak-016"></a>
## TWEAK-016 Group Settings by purpose

- **Requested:** 2026-10-28. **Status:** Implemented and applied to the live
  Beta 6 testing app on 2026-10-28; not published.
- **Need:** The maintainer finds the Settings dropdown scattered, particularly
  as more preferences are added. Make related controls easier to find without
  expanding the menu into a larger settings redesign.
- **Evidence:** Reviewed `MainWindow.CreateSettingsMenu`, Settings search and
  existing TWEAK-001, TWEAK-003 and TWEAK-015. The current menu separates device
  visibility from device actions, puts startup among workspace preferences, and
  distributes reference and release actions across different sections. This is
  a source-based grouping review, not a fresh live visual/accessibility audit.
  No external research was needed or performed.
- **Implemented order:** Keep search first. Group existing entries with separators:
  1. Workspace: Find node, Activity, right-click drag to pan, reduced meter
     animation and action notifications.
  2. Audio: device visibility, Windows sound settings, retry assigned devices
     and restart audio engine (last in this group, retaining interruption help).
  3. Application: start at sign-in, check for updates and automatic checking.
  4. Help and information: Help & shortcuts, What's new?, About VineMix.
  5. Quit VineMix, isolated at the bottom.
  These group names describe the proposal; adding extra heading rows is not
  assumed. Keep the existing device-visibility submenu and direct access to
  everyday controls. Avoid adding further nested menus in the initial refinement.
- **Related direction:** Keep What's new? immediately after Help and About near
  Quit, preserving TWEAK-001's intent. Moving Activity beside Find node would
  revise TWEAK-003's placement after What's New, while keeping it in Settings.
  Earlier implemented behavior remains recorded in those entries.
- **Scope and tradeoffs:** Menu order/separators, search and keyboard-order
  verification, and matching guidance. Grouping improves predictability but does
  not by itself eliminate scrolling; avoid new heading rows that make it taller.
  Frequent users must adjust to the new order. No preference, action or audio
  behavior should change.
- **Accepted direction, 2026-10-28:** The maintainer explicitly approved this
  order and retaining the existing separators, with no additional heading rows.
  Implement it in the live Beta 6 app and leave the app running for review.
  This supersedes the earlier proposal-only status and Activity/About placement
  in TWEAK-003/TWEAK-001; their earlier implemented states remain historical.
- **Verification:** Production build passed, and the detached WPF suite passed
  13,367 checks with 268 renders and zero binding errors. Existing Activity and
  What's New placement checks were updated for the accepted order; search,
  keyboard-routing and callback checks passed. The menu render was visually
  reviewed. No real pointer or VR interaction is claimed. Interface-only and
  shared activation safeguards passed 97 and 40 checks respectively.
- **Local integration:** Guarded Windows-shell activation and the actual existing
  shortcut launch passed without rollback. The app reopened and remained
  responsive. The original engine process, binary and running audio paths were
  preserved, with advancing frames; saved settings and preferences were unchanged.
  Physical settings identity and matching installed inventory were verified.
  See [the development record](BETA6_DEVELOPMENT.md#settings-grouping--28-october-2026)
  for the applied source and binary identities. Recovery evidence remains private.
- **Next step:** Maintainer review of the running app, then final release
  qualification when authorized. Beta 6 remains unpublished development work.

<a id="tweak-017"></a>
## TWEAK-017 Stronger group colors and direct background interaction

- **Requested:** 2026-10-28, after local integration of
  [IDEA-023](IDEAS_IMPLEMENTED.md#idea-023). **Target:** Beta 6. **Status:**
  Implemented and locally integrated on 2026-10-28; not published.
- **Change and rationale:** Make the group frames brighter, more saturated and
  less transparent so their organization is easier to see. Allow a group to be
  selected from exposed areas across its background, rather than requiring a
  title click. Shorten the group's hover tooltip to concise, useful guidance.
- **Interaction decision:** Click an exposed group background to select the
  group; drag that background or its heading to move the members together.
  Nodes, sockets and cables retain input precedence. Shift/Ctrl-drag on a group
  background provides box selection, and middle-drag or Space+drag still pans.
  The context menu remains available from the heading or exposed background.
- **Retained correction:** The first group implementation deliberately left
  interiors transparent to ordinary canvas selection and used a faint tint.
  This request supersedes those presentation and input choices while preserving
  automatic bounds, explicit membership, readable titles and the seven-color
  palette. Groups remain purely organizational with no audio behavior.
- **Scope and tradeoffs:** Frame appearance, hit testing, concise help and
  focused interaction checks. Background dragging makes groups easier to grab;
  modifier-assisted box selection preserves selection within their bounds.
  Layering of overlapping nodes is a separate defect tracked as
  [ISSUE-022](KNOWN_ISSUES.md#issue-022).
- **Source evidence:** Reviewed the frame visual and workspace input handlers on
  2026-10-28. The original body was non-interactive and the tint intentionally
  faint. No external research was needed.
- **Verification:** The combined detached WPF suite passed 13,526 checks with
  273 renders and zero binding errors. New coverage checks background hit
  testing, Shift/Ctrl pass-through and shared movement. The automated frame
  render was visually reviewed. Real-pointer or VR checks of this refinement
  were not completed; the original group's pointer checks do not establish this
  changed interaction's qualification. Production build and inert controller
  smoke passed, along with 97 UI-only and 40 shared activation safeguard checks.
- **Local integration:** Trusted Windows-shell activation and an actual Explorer
  shortcut launch passed without rollback. The same engine process, start time,
  path and binary were retained; frame progress and running paths remained
  healthy through an additional eight-second check. Current saved settings and
  preferences were unchanged, with no recovery restoration. Physical settings
  identity from open handles and the installed inventory were verified. See
  [the development record](BETA6_DEVELOPMENT.md#group-refinements-and-node-stacking--28-october-2026).
- **Next step:** Maintainer use of the installed refinements, including real
  pointer/VR interaction, then final package qualification when explicitly
  authorized. Beta 6 remains in development and nothing was published.

<a id="tweak-018"></a>
## TWEAK-018 Focused Level control sockets and percentage Linked control

- **Requested:** 2026-10-28. **Status:** Implemented and locally integrated on
  2026-10-28; not published. **Target:** local Beta 6.
- **Intent:** Keep Level control inputs only on nodes with dedicated Level knobs.
  Effects and other mixing utilities feed a Mix when their output needs level
  control. Retain the professional Level/Level control names; only the Linked
  control knob becomes Multiplier, 0–200%, with 100% at the centre and as default.
- **Correction retained:** The initial suggestion to rename Level to Volume was
  declined. 400% was discussed but not selected. Tooltips should remain short,
  with interaction instructions in Help rather than repeated on each control.
- **Scope:** Node socket eligibility, percentage display and exact-zero engine
  scaling, reset/copy/history, concise labels/help, compatibility and local
  integration. A zero multiplier is silent; bypass restores the normal level.
- **Compatibility:** Preserve existing effect-control connections and older
  decibel values without changing sound. Legacy connected sockets stay visible
  until removed; new connections target Level-knob nodes only. Values outside
  the new range require an explicit reset rather than silently clamping.
- **Evidence:** Reviewed the node catalog, socket construction, graph validation,
  gain processing, bypass and stored settings on 2026-10-28. No external research
  was needed. All 17 native suites passed with 281,251 graph assertions; detached
  WPF passed 14,103 checks and 283 renders with zero binding errors. Production
  build, inert smoke and guarded paired activation passed. Settings/preferences
  were unchanged and existing audio paths recovered. Source and installed
  identities are recorded in [Beta 6 development](BETA6_DEVELOPMENT.md#level-control-cleanup-and-linked-control-multiplier--28-october-2026).
- **Next step:** Maintainer testing of the reopened app. This is local integration,
  not final release qualification or publication.

<a id="tweak-019"></a>
## TWEAK-019 Short Ducking Trigger label

- **Requested:** 2026-10-28. **Status:** Implemented and locally integrated on
  2026-10-28; not published. **Target:** local Beta 6.
- **Change:** Replace the Ducking input's long label with **Trigger**. Match its
  tooltip title, accessible name and connection guidance; keep the tooltip to
  one short sentence. The internal sidechain identifier and audio behavior stay
  unchanged.
- **Rationale and evidence:** Reduce visual clutter. Reviewed node/socket labels
  and existing layout checks; no external research was needed.
- **Verification:** Existing detached checks passed (14,103 checks, 283 renders,
  zero binding errors); the Ducking render was inspected. Production build and
  inert smoke passed. Guarded interface-only activation retained the engine's
  process, start time and binary, with advancing frames and preserved audio
  paths. Settings/preferences were unchanged. See the [development record](BETA6_DEVELOPMENT.md#ducking-trigger-label--28-october-2026).
- **Next step:** Maintainer review in the reopened app; no publication performed.

<a id="tweak-020"></a>
## TWEAK-020 Centered text-only group heading

- **Requested and corrected:** 2026-10-28. **Target:** local Beta 6. **Status:**
  The superseding text-only heading is implemented and locally integrated on
  2026-10-28; not published. Earlier swatch implementation remains recorded below.
- **Current direction:** The maintainer reports that the replacement swatch still
  appears to change with scale and explicitly requests removing it, along with
  any other top-left heading element. Center the group name and show just the
  name: remove the visible node count and center the inline rename editor too.
  This supersedes the rounded-square design; no replacement icon or decoration
  is requested.
- **Original request:** Replace the small vertical color bar beside a group
  name with a larger, different shape that does not change screen size as the
  canvas zooms. The maintainer dislikes the narrow bar and perceives its size
  changing with zoom.
- **Earlier implementation:** A 14 × 14 screen-DIP rounded-square color swatch,
  with a 3-DIP corner radius and an 8-DIP gap before the title, designed to keep
  its screen dimensions fixed throughout 30–200% canvas zoom. It retained the
  selected palette color and system colors in high contrast.
- **Earlier source evidence:** Reviewed `GraphFrameView` on 2026-10-28. The old bar's
  width already compensated for zoom at 3 screen DIPs. Its height used
  `max(16, 14 / zoom)` world DIPs: it stayed at 14 screen DIPs through 87.5% zoom,
  then grew with further zoom. The earlier swatch refinement addressed that
  height scaling as well as changing the shape.
- **Scope:** Group-heading content, alignment and spacing only. Group membership,
  selection, movement, naming and audio behavior stay unchanged. No external
  research was needed.
- **Earlier swatch verification:** Detached WPF checks passed (14,115 checks,
  285 renders, zero binding errors). Actual 30%, 100% and 200% frame renders were visually
  reviewed: the swatch remained 14 screen DIPs with a clear 8-DIP title gap.
  Production build and inert smoke passed. No computer-use tools were used.
  These checks describe the earlier candidate and do not establish the
  text-only correction's behavior or resolve the maintainer's later report.
- **Earlier swatch integration:** Trusted-shell UI-only activation and the actual
  Explorer shortcut launch passed without rollback. Engine process, start time,
  path and binary were preserved, with advancing frames and running paths;
  the additional eight-second check passed. Saved settings/preferences were
  unchanged, physical settings identity and installed inventory were verified,
  and no settings recovery was performed. See the
  [development record](BETA6_DEVELOPMENT.md#group-color-swatch--28-october-2026).
- **Text-only correction verification:** The final detached suite passed 14,115
  checks with 285 renders and zero binding errors. The 30%, 50% and 100% renders
  were visually reviewed and showed a centered name without a swatch or count;
  200% was also rendered. Production build and inert smoke passed. No
  computer-use tools were used.
- **Text-only correction integration:** Trusted-shell UI-only activation and
  the actual Explorer shortcut launch passed without rollback. The original
  engine process, start time, path and binary were preserved, with advancing
  frames and running paths before reopening; the additional eight-second check
  passed. Saved settings/preferences stayed unchanged, physical settings identity
  and installed inventory were verified, and no recovery was performed. See
  [the correction record](BETA6_DEVELOPMENT.md#centered-group-heading--28-october-2026).
- **Next step:** Maintainer review of the centered name in the installed app.
  Beta 6 finalization and publication remain separate.
