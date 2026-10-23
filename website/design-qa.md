# Forest landing design QA

**Release review: static checks passed for Beta 4, 23 October 2026.**
All three JavaScript modules pass syntax checks and the main page's nine local
resource references resolve. Version text and the download link identify Beta 4.
This pass did not repeat browser interaction or visual inspection of the
morphing headline; the earlier results below apply to the revision they describe.

**Earlier status: passed for local design review, 23 October 2026.**
The preview was relaunched at the maintainer's request. The current revision
keeps the enlarged centered VM mark in its original colours, adds a soft shadow
and subtle pointer response, restores continuous visible shader motion, and
adds a delayed scroll prompt. See [REVIEW.md](REVIEW.md) for the current summary.

## 23 October checks

| Condition | Observed result |
| --- | --- |
| 1280 × 800 | Original-colour 192px mark centered within the page width and viewport height; separated from the shader by a shadow. No horizontal overflow. |
| 390 × 844 | 150px mark and both links fit; supporting content remains in normal flow. No horizontal overflow. |
| 320 × 440 | Compact button padding and a reduced vertical gap keep links within the page and clear of the bottom controls. No horizontal overflow. |
| Shader motion | A temporary renderer diagnostic advanced from 0.071 to 14.036 and 31.571 seconds; unobstructed background pixels differed between captures. Diagnostic removed. The old five-second cutoff is absent. |
| Scroll prompt | Hidden on initial load; visible after the five-second timer. Pointer activation reached the workspace. Scrolling dismissed it; it remained hidden after returning to the top. |
| Motion controls | Pointer pause and Enter resume worked. At the exact workspace anchor, the shader pauses; returning to the top resumes it. |
| Pointer response | Fine-pointer movement updated the logo's small translation/tilt. No autonomous logo animation loop runs while the pointer rests. |
| Reduced motion | Starts paused, removes logo transformation and keeps product layout static. Restoring normal motion resumes animation. |
| Resources and diagnostics | Nine local HTML resource references returned HTTP 200; three same-page targets resolve. Both modules pass syntax checks. No browser warnings or errors reported. |

Review corrected cached assets through versioned URLs and added a small positive
intersection threshold so the shader pauses even when the hero merely touches
the workspace boundary. Rendering is limited to 30 draws per second, DPR 1.5
and 900,000 pixels. This limits work per frame; it does not establish performance
across hardware. The fragment-source digest and palette remain unchanged.
Screenshots and backups are retained outside the checkout. Browser viewport,
media and cache overrides were restored. No desktop audio engine or settings
were changed. This pass did not repeat the historical JavaScript-disabled check
or force a GPU context loss.

## Historical 20 October review

**20 October status: passed for local design review.** Checked 20 October 2026 in the
Codex in-app Chromium browser at `http://127.0.0.1:4173/`.

## Target and evidence

The maintainer selected [frorest-green on 21st.dev](https://21st.dev/community/shaders?preview=%2Fcommunity%2Fshaders%2Ffrorest-green-4314dd3b-67c5-4cfd-b2d2-cc1184dddc47)
and requested minimal copy, elegant presentation and adaptive scroll reveals.
The reference supplies the atmospheric asset; it is not a complete website
mockup. The page hierarchy follows the brief and the existing product dossier.

The live reference was captured, then reviewed beside the implemented hero in
one comparison image. The original shader, palette and preset are retained;
the fragment-source digest matches the extracted published source. See
[SHADER_PROVENANCE.md](SHADER_PROVENANCE.md) for the source, hash and licence.
Animation phase and aspect ratio differ between captures, so pixel equality
between individual frames is neither expected nor claimed.

Historical evidence is retained privately outside the source checkout:

- `reference.jpg`, `local-shader.jpg`, `reference-comparison.jpg`.
- `hero-desktop.jpg`, `hero-mobile.jpg`, `product-desktop.jpg`,
  `product-mobile.jpg`, `product-wide.jpg`.
- `music-desktop.jpg`, `project-desktop.jpg`, `reduced-motion.jpg`,
  `no-javascript.jpg`.

## Five visual surfaces

| Surface | Review and result |
| --- | --- |
| Color and atmosphere | The selected cream, lime and forest Waves recipe supplies the full opening. Grain and soft moving contours match the reference's character. A lower scrim keeps the pale sentence readable. Passed. |
| Typography | Supplied IBM Plex Sans Regular, supplied vector wordmark, short left-aligned lines and restrained navigation. No new display face or decorative badges. Phone heading wraps into two intentional lines. Passed. |
| Layout and spacing | One viewport of atmosphere, one product scene, one spacious VRChat scene, then compact project information. Desktop uses one short sticky interval; phone and short windows use natural flow. Passed. |
| Imagery and detail | Real supplied workspace image, correct proportions and full-size link; caption explicitly identifies an illustrative preview. No invented UI or floating icon diagram. Shader attribution is visible on Credits. Passed. |
| Copy and density | Seven-word use-case sentence on entry. Subsequent sections explain routing and music-through-mic use. Experimental status, source availability and driver boundaries remain below the opening. Passed. |

## Browser checks

| Condition | Observed result |
| --- | --- |
| 1280 × 800 | Desktop hero and product scene inspected. Regular and semibold local fonts and workspace image load. No horizontal overflow. |
| 390 × 844 | Full-height hero, two-line sentence, normal-flow product scene, readable supporting text and no horizontal overflow. |
| 1920 × 720 | Sticky scene remains enabled. Product frame fits vertically and horizontally. Its width is limited by available height, preserving image/caption alignment. |
| 1366 × 600 | Sticky interval is disabled; normal flow prevents content clipping. No horizontal overflow. |
| Reduced motion, 1280 × 800 | Shader starts paused; product pin is static and follow-up copy has opacity 1. No scroll translation/scale effects. |
| JavaScript disabled | Actual shader still supplies the background. Text and navigation remain present, motion control stays hidden and product information is in normal flow. |
| Motion control | Pointer pause changed the canvas motion state to paused; Enter resumed it. Leaving the hero pauses the shader. |
| Navigation/disclosure | Explore and About reached their sections. Driver disclosure opened by pointer and closed with Enter. Credits and its return link worked. |
| Resources | All 15 referenced local page/style/script/image/font/licence resources returned HTTP 200; all six same-page anchor references resolve. |
| Console and parsing | Browser returned no warnings or errors. Both JavaScript modules passed syntax checks. |

## Corrections made during review

- Simplified the opening to avoid an awkward phone line break.
- Disabled sticky layout in short desktop windows and limited the workspace
  image to the available vertical space. Bound the figure width to its image
  so the caption stays aligned at wide aspect ratios.
- Preserved a word separator when the product heading's line break is hidden
  on phones.
- Paused the shader at a zero intersection ratio. Cached its CSS dimensions
  rather than reading layout on every animation frame.

The native wrapper caps rendering at 30 draws per second, DPR 2 and two million
pixels. Failure/context-loss fallback is implemented in source; this browser
review did not force a real GPU context loss or measure performance across a
device fleet. It is not a complete accessibility certification or audio
qualification. Desktop application processes and settings were not changed.

## Skills applied

[frontend-design](https://github.com/anthropics/skills/blob/main/skills/frontend-design/SKILL.md),
[web-design-guidelines](https://github.com/vercel-labs/agent-skills/blob/main/skills/web-design-guidelines/SKILL.md)
and the installed Product Design selected-reference/QA guidance were read and
applied. The direction, pre-build plan and guardrails are retained in
[DESIGN_NOTES.md](DESIGN_NOTES.md). No additional frontend dependency was needed.
