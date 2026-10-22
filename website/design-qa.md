# Forest landing design QA

**Current status: visual recheck pending for the centered-mark design.**
The local server and browser previews are intentionally off at the maintainer's request
because the preview caused lag. Source/syntax/resource-path checks passed for
that edit. Beta 3 download/documentation links and copy received source, syntax
and resource-path checks on 23 October without computer use. The following
visual evidence covers the preceding 20 October revision.

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
