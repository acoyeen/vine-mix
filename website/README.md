# VineMix local website review

A lightweight, static product page based on
[`docs/WEBSITE_DESIGN_DOSSIER.md`](../docs/WEBSITE_DESIGN_DOSSIER.md).
The opening uses the maintainer's selected **frorest-green** shader by Allan Mesquita
Brito, with the supplied standalone VM mark centered in the viewport. The mark
retains its signature green and light colours, with a larger size, soft shadow
and subtle pointer response. A short morphing headline sits above the logo;
a GitHub download link and a documentation link sit below it. The logo remains
centered in the viewport. There is no wordmark or header navigation.
A discreet “Scroll for more” prompt appears after five seconds without
scrolling, and an icon-only motion control sits in the lower-right corner.
This follows the maintainer's 23 October refinement.

Scrolling moves from that field into the real desktop workspace, then a sparse
VRChat music-sharing story and concise project information. The workspace has
one short sticky interval on sufficiently tall desktop windows; phones, short
windows and reduced-motion layouts use ordinary document flow. There is no
forced scrolling or scroll-animation library. This revision replaces the
earlier icon-and-beam treatment that the maintainer rejected.

No package installation or build step is required. The shader, fonts, images
and scripts are served locally. The site is separate from the desktop
application and audio engine.

## Local serving

Serve only the `dist` directory. For example, from this directory with Python 3:

```powershell
python -m http.server 4173 --bind 127.0.0.1 --directory dist
```

Open `http://127.0.0.1:4173/`. This is a loopback-only local review, not a
published website. Browser review evidence belongs in [`REVIEW.md`](REVIEW.md)
and [`design-qa.md`](design-qa.md).

The maintainer subsequently requested the preview be relaunched for viewing,
superseding the earlier request to keep it stopped during development. The local
preview is available at the address above during the current review.

## Design guidance applied

The upstream
[frontend-design](https://github.com/anthropics/skills/blob/main/skills/frontend-design/SKILL.md)
and
[web-design-guidelines](https://github.com/vercel-labs/agent-skills/blob/main/skills/web-design-guidelines/SKILL.md)
skill sources were read on 20 October 2026, alongside the installed Product
Design selected-reference guidance. Their instructions informed the visual
plan, implementation and review: concentrated visual emphasis, regular IBM
Plex typography, generous space, semantic controls, keyboard focus, bounded
motion and reduced-motion behavior. No new skill or framework was installed.
[`DESIGN_NOTES.md`](DESIGN_NOTES.md) records the reviewed direction and guardrails.

## Shader source and motion

The exact selected recipe is
[`frorest-green`](https://21st.dev/@allanmesquitab/components/frorest-green),
recipe `4314dd3b-67c5-4cfd-b2d2-cc1184dddc47`. Its complete published fragment
shader, original four-color palette and preset values are retained. The React
preview runtime was replaced with a small local WebGL lifecycle wrapper; no
remote preview or third-party runtime is embedded.

The decorative field is silent. Rendering is capped at 30 drawing frames per
second, a device-pixel ratio of 1.5 and a 900,000-pixel budget. Animation continues
while the opening is visible; it pauses when the opening leaves view or the page
is hidden, and starts paused under reduced motion. The earlier five-second stop
has been removed. One icon-only pause/play button controls both the shader and
the morphing headline without adding visible motion text. A captured frame of
the same shader provides a static fallback. Content and navigation remain
available without JavaScript or WebGL.

The logo's small pointer movement is limited to fine pointers and disabled under
reduced motion. The scroll prompt waits for five seconds with the opening visible
and the page unscrolled. It disappears on the first positive scroll or when
activated, then stays dismissed for the rest of that page visit.

[`SHADER_PROVENANCE.md`](SHADER_PROVENANCE.md) records the upstream bundle,
extraction method, exact shader digest, wrapper changes and license evidence.
The canonical component declares MIT; attribution, permission and warranty text
are retained at `dist/assets/licenses/frorest-green-MIT.txt` and linked from
the visible website credits. Original shader attribution comments are preserved.

## Morphing headline

The headline uses the blur and alpha-threshold technique from Magic UI's
[Morphing Text on 21st.dev](https://21st.dev/@dillionverma/components/morphing-text),
with [upstream documentation](https://magicui.design/docs/components/morphing-text).
It is adapted to local plain JavaScript, with no React dependency. Dark forest
letters in the bundled IBM Plex Sans SemiBold sit over the lighter upper shader
field. The headline shares the logo's shadow, `drop-shadow(0 8px 20px #07170e73)`.

The first phrase is “Your sound. Your rules.” A transition starts every five
seconds of active playback, cycling through a shuffled bag of five phrases:

- Your sound. Your rules.
- Choose who hears what.
- Bring music to your mic.
- Be VRChat’s unofficial DJ.
- See where sound goes.

Each phrase appears once before the bag is reshuffled, with no immediate repeat
across bag boundaries. The text pauses together with the shader when the opening
leaves view, the page is hidden or the motion button is paused. Reduced motion
starts with a static first phrase; an explicit press of Play can enable motion.
A stable accessible heading avoids announcing each visual change. Source and
adaptation details belong in
[`MORPHING_TEXT_PROVENANCE.md`](MORPHING_TEXT_PROVENANCE.md).

## Content status

The product page links to the versioned Beta 5 release and the current user
guide. Source, checksums and signed update metadata accompany each applicable
GitHub release. The original application is MIT licensed; repository visibility
is managed separately from release preparation.

VineMix 0.3.0-beta.5 targets Windows 11 x64. VRChat is an
intended Windows desktop/PCVR use case with end-to-end receiver qualification
still incomplete. Virtual audio drivers require separate installation and
have their own license terms. The workspace image is an illustrative real
desktop preview, not evidence of a live audio session. Reliability language
describes existing behavior and outstanding qualification rather than making
latency, endurance or universal compatibility guarantees.

## Supplied assets

| File | Source |
| --- | --- |
| `wordmark.svg`, `mark.svg` | `desktop/VineMix.Desktop/Assets/Branding/` |
| `workspace.png` | `docs/images/workspace.png`; illustrative detached preview |
| `IBM-Plex-Sans-Regular.ttf`, `IBM-Plex-Sans-SemiBold.ttf` | `desktop/VineMix.Desktop/Assets/Fonts/` |
| `IBM-Plex-OFL.txt` | `licenses/IBM-Plex-OFL.txt`; supplied font notice |
| `LICENSE.txt` | Root `LICENSE`; original application/project license |
| `forest-poster.jpg` | Still frame rendered from the selected local shader |
| `github.svg` | Official `primer/octicons` `icons/mark-github-16.svg`, retrieved 21 October 2026 |
| `licenses/Octicons-MIT.txt` | Official `primer/octicons` MIT notice, retrieved with the icon |

The logo lettering consists of custom vector shapes, rather than an available
font. The website retains the approved bundled IBM Plex Sans; the morphing
headline and compact opening buttons use SemiBold to echo the wordmark's weight.

Earlier icon and beam assets may remain in `dist/assets/` with their notices;
the current page does not use those assets. The preceding icon-and-beam
implementation is retained in the private development archive outside the
source checkout.
