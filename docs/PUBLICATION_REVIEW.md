# Initial repository review — 19 October 2026

Scope: a once-over for obvious bugs, unnecessary work and unused code, followed
by source publication and distribution preparation. Existing audio processing,
node types, saved graphs, routing and visual behavior remain intact.

## Changes

- Fixed saving a bare relative settings filename such as `--settings settings.json`.
  The empty parent path previously caused directory creation to fail. Settings
  replacement and backup behavior now work for relative filenames too.
- Fixed the same issue in engine logging. Both failures were reproduced before
  the fix and covered with temporary-directory / simulated-worker regressions.
- Reserved node status storage before constructing telemetry snapshots, avoiding
  repeated vector growth during desktop polling.
- Compared serialized history/configuration values as UTF-8 bytes instead of
  allocating two UTF-16 JSON strings. The serializer, ordering and equality
  semantics remain the same. Regression cases include Unicode, nulls, precise
  numbers, nested values, collection order and exclusion of the canvas camera
  from history comparisons.
- Removed an unused gain formatter and an unused dial-group label parameter.
  Kept compatibility code used by saved graphs and independent migration tests.
- Made the synthetic lifecycle workers maintain bounded capture buffering and
  consume queued rendered blocks when their threads wake. Voice Color checks
  deliberately use coarse 20 ms capture/render wakes and require fresh rendered
  blocks and engine progress, preserving the strict expected endpoint values.
  This prevents the test fixture's scheduling from starving its steady signal;
  production audio processing is unchanged.
- Added missing source/runtime notices, including component-specific copyrights
  omitted by some upstream top-level licence files. Packaging now requires
  notices instead of silently skipping missing files.
- Added checksums for the remaining tool/library archives, pinned the .NET SDK,
  added Windows CI and prepared build, contribution and distribution guidance.
- Kept generated tools, packages and about 200 MB of historical renders outside
  Git. A current detached workspace screenshot is included in the README.

## Verification

All 14 offline native suites passed after the changes, including graph processing,
engine lifecycle, configuration qualification, drift/queues, effects, denoisers,
spectrum, Windows catalog simulation, exclusive render simulation and dynamics.
Qualification passed 7,914 assertions / 4,527,360 synthetic frames.

The desktop Release build passed with zero warnings/errors. Detached verification
passed 9,139 checks, produced 166 renders and reported zero binding errors.
The staged self-contained package built successfully; all 20 licence files were
verified byte-for-byte against the source notices, and the required runtimes,
font licence and guides were present. A test fixture confirmed packaging rejects
missing notices. The packaged engine's `--help` command also ran successfully.

In 100 maximum-size graph equality comparisons, allocated bytes fell from
20,281,640 to 10,177,640 (49.8%). Diagnostic elapsed time was 71.6 ms before and
63.8 ms after on this machine; timings vary with load and are not an audio latency
claim.

Local evidence, excluded from Git:

- `artifacts/repository-native-review-20261019/results.json`
- `artifacts/repository-desktop-review-20261019-222017/results.json`
- `artifacts/repository-desktop-review-20261019-222017/model-copy-performance.json`
- `artifacts/vine-mix-brand-verification-20261019-224422/results.json`
- `artifacts/lifecycle-coarse-verification/` (four full lifecycle passes)

## Distribution and limits

See [DISTRIBUTION.md](DISTRIBUTION.md) and
[THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md). Vine Mix's original application
is licensed under MIT, as selected by the owner. Vendor driver installers
are not distributed. Their licence restrictions remain separate from this app.

This was a targeted maintenance and provenance review, not an exhaustive security,
patent or legal clearance. The review does not establish audible quality, physical
device recovery, end-to-end latency or long-duration hardware reliability. No live
personal configuration, driver installation, audio-device tests or package
activation was performed during this review.
