# VineMix website

Source for [vinemix.app](https://vinemix.app), served by Cloudflare Workers
Static Assets. This directory contains the published site source, its local
checks and the public assets needed to rebuild it. Earlier website material
under `website/` is retained as historical source.

Run `npm run build`, then `npm run check`. `npm start` serves the preview on
`http://127.0.0.1:4175/`. The optional `npm run check:browser` uses an existing
Playwright installation; set `VINEMIX_PLAYWRIGHT_PATH` to its `index.mjs` if it
is outside the project. No dependency installation is needed for the build or
offline checks.

The build preserves `dist/assets/`: these are maintained public artwork, fonts
and their licence notices. Keep that directory when preparing a deployment.
`release.json` selects the published download fallback independently of desktop
development versions. See [hosting](HOSTING.md) for deployment and rollback.

## Attribution

The footer links to `/attribution`, built from `src/attribution.html`. Keep this
page consistent with [the repository policy](../ATTRIBUTION.md), and update
`dist/assets/LICENSE.txt` alongside [the root licence](../LICENSE). The page
distinguishes required MIT notices from requested visible acknowledgement.
