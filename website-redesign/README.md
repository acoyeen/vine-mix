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

## Website assets and checks

The three static font weights use the unmodified Latin1 WOFF2 files from
[IBM Plex](https://github.com/IBM/plex/tree/c5f949677f6f163e8dfe98ca2c326bd48b42fa1b/packages/plex-sans/fonts/split/woff2).
Their filenames contain the first 12 hexadecimal digits of their SHA-256
digest. Use a new digest when changing a file in the immutable fonts directory.
The existing OFL notice covers these fonts. The share image combines the
project wordmark and the credited hero poster. Half-resolution WebP node images
provide a smaller alternative to the full PNGs with the same geometry.

To repeat the browser suite on additional engines, set
`VINEMIX_BROWSER_ENGINE` to `firefox` or `webkit` and install the
matching test browser in the external Playwright environment.
