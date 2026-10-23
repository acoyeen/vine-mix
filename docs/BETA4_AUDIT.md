# Beta 4 release review

Date: 23 October 2026. Scope: changes since Beta 3, desktop editing and save
lifecycle, native endpoint selection and routing, release provenance, packaging,
privacy and current documentation. This was an independent source review with
isolated verification; it is not an exhaustive security or hardware audit.

## Reviewed behavior

- Desktop review traced structural edit and history guards through the session
  layout lock, quit retries and recovery copies, deferred release highlights,
  footer timers, keyboard focus and updater health gating. No release-blocking
  defect was found. The complete detached suite passed.
- Native review checked agreement between device choices, configuration
  validation, worker opening and catalog refresh. Ordinary and streamed playback
  are accepted consistently; dedicated cable output and exclusive VAC behavior
  remain intact. Exact/stable identity recovery preserves unavailable assignments
  and rejects ambiguous replacements. Synthetic tests cover mute/gain isolation,
  device loss/return, persistence, reopening and output ownership.
- Release preparation promoted the local version to `0.3.0-beta.4` with numeric
  version `0.3.0.5`. Bundled highlights and their version fixtures were promoted
  together so users of the preceding local candidate see the new release's
  highlights. The footer improvement is included in those highlights.
- Release documentation and website download links now identify Beta 4. Earlier
  release notes and the local candidate evidence remain historical. Website
  scripts passed syntax checks and local resource checks; this does not claim a
  new interactive browser test. The morphing headline's upstream MIT notice and
  attribution are included.
- The privacy review checked source files and new documentation for personal
  paths, names, signing material and private workspace data. Only labelled
  example account paths were found by the targeted scan. Release archives and
  metadata receive a separate final contents audit. Commit identity uses the
  project's pseudonym.

## Verification

All 17 native suites passed. Detached desktop verification passed 12,826 checks
with 229 renders and zero binding errors. Isolated updater-core, update-worker
and signing/preparation suites passed 71, 97 and eight checks respectively.
Activation safeguard fixtures passed 34 checks; interface-only activation
fixtures passed 54. These fixtures do not activate the personal application.

Nineteen detached PNGs were visually inspected, covering release highlights,
quit recovery, normal and high-contrast lock states, button feedback, six footer
states, the small workspace, 192-DPI dynamics, Help and the fitted graph. No
clipping, legibility or privacy blocker was found. This was render review,
not live pointer or keyboard interaction.

The final packages must pass inert release smoke checks, standalone runtime
integrity checks and isolated Setup checks. Both build receipts, the source
archive and signed update preparation must agree on the frozen build inputs.
Sanitized results for the exact shipped bytes are included in
`SOURCE-MANIFEST.json`; raw evidence remains outside the checkout.

The published package assets are downloaded and hashed before publication. The
release workflow then verifies the published assets independently. Repository
visibility and previous release tags/downloads are preserved.

## Limits

See [current qualification](BETA_TESTING_CURRENT.md) and [Beta 4's release notes](releases/0.3.0-beta.4.md).
Real streamed-headset sound/reconnect, physical-display behavior, clean-machine
installation, sleep/resume, sustained real-audio use and complete delivered-app
update/rollback scenarios remain unqualified. The earlier unexpected-closure
report remains unresolved. Normal WPF resizing remains; no resize experiment
was reintroduced. No computer-use tools were used for this release.
