# VineMix local website review

A lightweight, static product page based on
[`docs/WEBSITE_DESIGN_DOSSIER.md`](../docs/WEBSITE_DESIGN_DOSSIER.md).
The opening is now a visual audio-routing scene with one descriptive sentence;
the real desktop preview and explanatory content sit below the fold.
No package installation or build step is required. A small native script draws
responsive connections and supports pausing decorative motion. All runtime
assets are served locally, including the supplied fonts.
The site is separate from the desktop application and audio engine.

Serve only the `dist` directory. For example, from this directory with Python 3:

```powershell
python -m http.server 4173 --bind 127.0.0.1 --directory dist
```

Open `http://127.0.0.1:4173/`. This is a local review, not a published website.

## Content status

On 20 October 2026, read-only GitHub metadata still reported the repository as
private with no published release. The page therefore uses an on-page explanation
as its main action and includes no inaccessible source or download button.
VineMix v0.2.0 remains experimental. VRChat is an intended use case with receiver
qualification still incomplete; virtual drivers are separately installed/licensed.

## Asset provenance

Files in `dist/assets/` are copies of existing project assets:

| File | Source |
| --- | --- |
| `wordmark.svg`, `mark.svg` | `desktop/AudioMixer.Desktop/Assets/Branding/` |
| `workspace.png` | `docs/images/workspace.png`; illustrative detached preview |
| `IBM-Plex-Sans-Regular.ttf`, `IBM-Plex-Sans-SemiBold.ttf` | `desktop/AudioMixer.Desktop/Assets/Fonts/` |
| `IBM-Plex-OFL.txt` | `licenses/IBM-Plex-OFL.txt` |
| `LICENSE.txt` | Root `LICENSE`; original application/project license |
| `mic.svg`, `app-window.svg`, `headphones.svg`, `audio-lines.svg`, `pause.svg`, `play.svg`, `arrow-down.svg` | Official `lucide-icons/lucide` repository, same filenames under `icons/` |
| `music.svg` | Official Lucide `icons/eighth-notes.svg`, locally named for its purpose |
| `Lucide-LICENSE.txt` | Official Lucide repository license, including its upstream notices |
| `Magic-UI-LICENSE.txt` | Official Magic UI MIT license |

The supplied fonts retain their SIL Open Font License notice. The wordmark and
screenshot are real project assets; no new application behavior is depicted.

## Visual reference and motion

The maintainer requested a memorable, almost purely visual opening on 20 October 2026
and allowed resources such as 21st.dev. The connection treatment is informed by
[Magic UI Animated Beam on 21st.dev](https://preview.21st.dev/@dillionverma/components/animated-beam)
and its [canonical source](https://github.com/magicuidesign/magicui/blob/main/apps/www/registry/magicui/animated-beam.tsx).
This implementation uses native SVG paths, CSS motion and resize observation;
the React component and its animation dependencies were not installed.

Icons were fetched from the official
[Lucide source](https://github.com/lucide-icons/lucide/tree/main/icons) on
20 October 2026, with their stroke color changed to the site palette. Full
license notices and visible website credits are included. The routing scene is
illustrative, silent and does not connect to the application or microphone.
It starts paused when reduced motion is requested and has a pause/play control.

The preceding static layout is backed up at
`artifacts/website-before-visual-20261020/` (ignored local artifacts).
