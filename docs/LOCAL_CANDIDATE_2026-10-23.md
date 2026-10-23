# Local development candidate — 23 October 2026

> Historical development record. These results and identities describe the
> unpublished local builds that preceded Beta 4. They do not qualify the final
> Beta 4 downloads. See [Beta 4's release notes](releases/0.3.0-beta.4.md) and
> [current qualification](BETA_TESTING_CURRENT.md) for the released version.

Target: **0.3.0-beta.3.1-local**, displayed as **0.3.0 · Local candidate
(after Beta 3)**. This is an unpublished candidate for maintainer testing.
It does not replace or finalize Beta 2, change Beta 3's downloads, or change
repository visibility. The existing uncommitted backlog review was retained.

## Implemented behavior

- **Virtual Desktop output:** the earlier input fix concerned the VR headset
  streaming provider. Listening output now accepts ordinary Windows streamed or
  software playback endpoints, including remote-network devices by default.
  Discovery, selection, native validation and the audio worker agree. Explicit
  saved assignments, unavailable-device presentation, exact/stable identity,
  mute/level and one-output-owner protection remain. VAC retains its dedicated
  exclusive output; no driver was installed or alternative cable support added.
- **IDEA-017:** top-right red closed / muted grey open padlock with accessible state
  and distinct high-contrast shapes. The session lock blocks structural node,
  cable and history changes while retaining sound controls and navigation.
- **IDEA-018:** red Quit VineMix at the bottom of Settings uses the tray's same
  flow. Close retains the established minimise-to-notification-area behavior;
  audio continues. No behavior chooser was added.
- **IDEA-019:** SpeexDSP · 20 ms and RNNoise · 30 ms in the algorithm menu, with
  concise help comparing cleanup and explaining added audio delay. These remain
  node delays, not CPU measurements or total system latency. DSP, defaults and
  saved values are unchanged.
- **IDEA-020:** bundled What's new highlights after healthy updated startup,
  deferred during background/tray use, inactive windows, editing, pending saves
  and other dialogs. Settings can reopen it above About VineMix. Fresh installs establish a baseline;
  legacy installs get one transition notice; available skipped-version highlights
  are included. Dismissal state lives outside the graph and cannot regress on a
  downgrade. No remote HTML or automatic tutorial is involved.
- **IDEA-021:** final save runs before teardown. Failure offers Retry, recovery
  copy, Cancel, or explicit discard. Complete recovery JSON retains queued edits
  and refuses existing filenames. Failed copy writes remain recoverable; lost
  save replies reconcile before replay; conflicting replay retains desired work.
  Quit/update operations cannot race, and a failed update-health session can still
  quit safely without enabling edits to its protected workspace.

The five P1 entries are retained with their original proposals and stable IDs in
[the implemented archive](IDEAS_IMPLEMENTED.md). Broader driver compatibility
remains under [IDEA-015](IDEAS.md#idea-015).

## Verification

All checks below are unattended. No computer-use tools, driver changes, real
microphone recordings, unsolicited test playback or external publication were used.

| Check | Result and boundary |
| --- | --- |
| Native offline suites | All 17 passed together, including 7,959 configuration/graph qualification assertions and the existing denoiser signal checks. Synthetic audio and temporary fixtures only. |
| Streamed output | Real engine graph processing through synthetic workers verifies nonzero routed output, gain/mute isolation, unavailable/return lifecycle, duplicate and cable rejection, saved binding and full reopen. Fake Windows catalog tests cover metadata, direction, exact/stable replacement and ambiguous identity. This is not a live headset test. |
| Detached WPF | 12,291 checks, 221 renders and zero binding errors in the final combined pass. Includes real selectors, both delay labels, layout lock and stale-action guards, three-scale caption bounds, quit failure decisions, failed-update quit recovery, What's new state/content/readiness and Help reopening. |
| Update regression | 71 update-core and 97 worker checks passed with isolated signed fixtures and fake process/registry/Setup operations. No release service was contacted. |
| Activation safeguards | 34 isolated checks passed, including physical file identity, distinct ordinary/redirected backups, newer-file protection, exact process identity, atomic replacement failures, real workspace readiness and an inert child launched through the production Windows shell mechanism. |
| Final executable smoke check | Passed on the packaged executable: release identity, installed runtime integrity and detached starter layout. This check starts no personal engine or shown window. |
| Visual inspection | Reviewed What's new, both denoiser choices, Settings footer, actual open/closed lock buttons in standard/high contrast, recovery choices and the main workspace. Fixtures contain example devices only. |

Earlier combined desktop attempts exposed fixture setup/render sizing and an
outdated caption-button count; these were corrected without loosening the
existing caption hit-test or geometry requirements. Reviews also caught the
failed-update-health Quit edge case before packaging.

Raw reports, physical paths, process identities and recovery backups are retained
outside the checkout under the local build cache. Project documentation records
sanitized scope and results only.

## Initial local package and activation

The initial installed-mode candidate was built on 23 October 2026 and its source
identity was checked again before activation. The 28-file package was inspected
for unexpected settings, recovery, signing-key, log and verification artifacts;
none were present. Required third-party notices and licences are retained.

| Identity | SHA-256 |
| --- | --- |
| Build source snapshot | `e553fc503163fccbe2afdba2afed985a90ac101a8a345cef781bc7898edc1652` |
| VineMix.exe | `873fc2a645318f66fd5754dd4c84f83485b21e9723c884caeff693c25246fa60` |
| VineMix.Engine.exe | `654ff6ef2185e0a4ee12540d5612f62a730371ca354bbe2cd4553c384ee34ddb` |

The source snapshot covers the build inputs, including uncommitted code; it
excludes documentation and generated output. It is not a published release tag.

Local activation **passed**. Existing binaries and all discovered ordinary and
redirected settings views were backed up outside the checkout before process
changes. Process, version, hash, physical file-handle and file-stability guards
passed. Those checks establish persisted-state stability, not an independent
inspection of the previous controller's in-memory edit queue.

The controlled engine restart retained the identical configuration and revision.
Every previously running path recovered, and the engine frame counter advanced.
The settings file and workspace preferences remained byte-identical. The
candidate opened the same physical settings file verified through an open handle;
no settings backup was restored.

The controller was reopened through its actual existing Windows shortcut via
Explorer, after the engine was launched through the trusted Windows shell.
The visible WPF workspace was verified responsive. A further check about
49 seconds later confirmed the same candidate processes and binary hashes,
unchanged settings/preferences and a responsive workspace. VineMix was left open
for maintainer testing. No GitHub assets or repository visibility were changed.

## Layout lock refinement and interface-only activation

A later maintainer refinement on 23 October 2026 reduced the padlock drawing to
16 pixels while retaining its existing click target. Unlocked uses the same
muted grey as Saved; locked remains red. The shackle opens or closes over
180 milliseconds. A refused edit briefly outlines and gently enlarges the icon,
without moving focus or opening a dialog. Ordinary node/cable selection and
minor pointer jitter remain quiet; a locked drag signals once after the movement
threshold. Sound controls and graph/history protection are unchanged.

Windows reduced motion changes the state immediately and shows a static brief
outline. High contrast retains distinct open/closed shapes with system colours.
Emphasis expires automatically and clears on unlock or unload.

The final combined detached desktop pass passed **12,318 checks, 222 renders and
zero binding errors**. It covers the real blocked-edit event, node/cable gestures,
selection silence, structural history refusals, animation targets, reduced motion,
feedback expiry and caption bounds. Rendered normal/high-contrast states and the
feedback outline were inspected. The interface-only activation path passed
**54 isolated checks** covering transaction failures, rollback, unchanged engine
bytes and preservation of a newer workspace.

This follow-up retains version **0.3.0-beta.3.1-local** and supersedes only the
desktop build above:

| Identity | SHA-256 |
| --- | --- |
| Refined build source snapshot | `ed4f0d8fee8277de13363c789690676162c93c03f81ff05cdbe72f1ea72017bd` |
| Refined VineMix.exe | `fc385bc43d8fe3aa9b0161b3f391562a3319249e5893d0418df041312ca6e3f7` |
| Unchanged VineMix.Engine.exe | `654ff6ef2185e0a4ee12540d5612f62a730371ca354bbe2cd4553c384ee34ddb` |

The final packaged executable passed its installed-runtime smoke check. The
source identity matched and the package privacy audit passed before activation.
Interface-only activation **passed** through the trusted Windows shell and actual
existing Explorer shortcut. The original engine retained its process identity,
start time, path and hash; it was never stopped, replaced or restarted. Read-only
snapshots before reopening the interface confirmed an identical configuration,
advancing frames and preservation of every previously running path. The same
engine remained active throughout the eight-second observation after reopening.
Settings and workspace preferences remained byte-identical, with physical file
identity verified through an open handle. Both ordinary and redirected data views
were backed up outside the checkout; no settings were restored from backup.
The responsive VineMix workspace was left open. Nothing was published.

## Interface polish and outer-glow refinement

The next maintainer refinement supersedes the lock feedback described above.
The resting locked padlock is now warning yellow; unlocked remains muted grey.
A blocked edit smoothly turns the padlock red and enlarges it, holds briefly,
then returns it to normal over 1.4 seconds. There is no shake or surrounding card.
Affected nodes use the same outer-glow shape as a missing input/output, in yellow
with a stronger peak (0.85 versus the existing red glow's 0.35 strength). It fades
in and out without changing node geometry, selection or hit targets. Repeated
attempts continue from the current glow brightness. Cable edits identify their
endpoints; a group move identifies its selected nodes. Reduced motion uses
temporary static emphasis, and unlock/unload clear feedback and animation clocks.

Quit, layout-lock and denoiser hover help are shorter. Both algorithm choices keep
their delay values. Settings now contains **What's new?** immediately above
**About VineMix**. Button templates no longer leave a white underline after a
click; keyboard navigation retains an inset focus cue.

The final detached desktop pass passed **12,782 checks, 226 renders and zero
binding errors**. Coverage includes affected nodes, fade timing, repeated-attempt
continuity, expiry, reduced motion, zoom, actual button press/release templates,
normal/high-contrast focus cues, Settings ordering and concise tooltip content.
Rendered padlock states, yellow node glows, button styles, Settings and denoiser
choices were inspected. Verification remains detached and uses example fixtures.
Test setup corrections fixed compilation, detached theme-resource ownership and
preview framing; no production check was relaxed.

The 28-file installed-mode package passed source-identity, hash and contents
checks. The packaged executable passed its inert installed-runtime smoke check.
This retains **0.3.0-beta.3.1-local** and supersedes only the prior desktop binary:

| Identity | SHA-256 |
| --- | --- |
| Build source snapshot | `6d1cf5dfbac7dc5c3386be99df452658ac2e5c4c840b3f4e834736a1b0d3f398` |
| VineMix.exe | `b188435562962067240809e84d49b426c6fd217340eded9fc0da5ab055c386c5` |
| Unchanged VineMix.Engine.exe | `654ff6ef2185e0a4ee12540d5612f62a730371ca354bbe2cd4553c384ee34ddb` |

Interface-only activation **passed** through the trusted Windows shell and the
existing Explorer shortcut. The original engine kept its process identity,
start time, path and hash throughout; it was never stopped or replaced. Read-only
snapshots confirmed unchanged configuration, advancing frames and preservation
of all previously running paths before reopening. The same engine remained
active through the subsequent eight-second observation. Settings and workspace
preferences remained byte-identical, with settings identity checked through an
open file handle. Ordinary and redirected data views were backed up outside the
checkout, and no settings were restored. The responsive workspace was left open.
No release assets or repository visibility were changed.

## Quiet selection while locked

The maintainer rejected the yellow glow after trying it. This revision removes
that effect, its timers and affected-node tracking. Normal node selection outlines
are hidden whenever the layout is locked, including existing selections and
nodes selected while locked. Unlocking restores outlines for the current
selection. Selection state, Copy, inspection and sound controls remain available.
The padlock feedback and red missing-device warnings are unchanged.

The final detached pass passed **12,767 checks, 226 renders and zero binding
errors**. It verifies immediate outline hiding/restoration, selection changes,
blocked edits, refresh, zoom, sound controls and workspace reopening. The locked
and restored-selection previews were visually inspected. The obsolete glow tests
were replaced with selection-visibility checks; existing warning and padlock
coverage still passed.

| Identity | SHA-256 |
| --- | --- |
| Build source snapshot | `93d9783b3de42ecf2a9f6f95450a18faac09758332ddec4d0d3cd4c0995fec8b` |
| VineMix.exe | `f3c80d803465ce6ccf00021764cb9f68c4a8c0df6ca41a79220895d32aa1f70b` |
| Unchanged VineMix.Engine.exe | `654ff6ef2185e0a4ee12540d5612f62a730371ca354bbe2cd4553c384ee34ddb` |

The same-version 28-file package passed source, hash, contents and inert smoke
checks. Interface-only activation **passed** via the trusted Windows shell and
existing Explorer shortcut. The original engine retained its identity and hash,
with advancing frames and unchanged configuration. Settings and preferences
remained byte-identical; physical settings identity was checked through an open
handle and both data views were backed up outside the checkout. No settings were
restored. The reopened workspace was responsive, and the engine stayed unchanged
through the eight-second observation. Nothing was published.

## Faster red padlock pulse

The latest refinement keeps the locked padlock red throughout feedback. Only
size changes on a blocked edit: it grows to 1.4× over 120 ms, holds until 180 ms,
and shrinks to normal by 400 ms. This replaces the 1.25×, 1.4-second emphasis.
Unlocked remains muted grey; high contrast and reduced motion retain their
accessible treatments. Hidden selection outlines and other behavior are unchanged.

Detached verification passed **12,767 checks, 226 renders and zero binding
errors**, including constant colour, size-only animation, cleanup and enlarged
icon clearance at three display scales. Resting and enlarged previews were
inspected. The 28-file package passed source, contents, hashes and inert smoke
checks. Interface-only activation **passed** through the trusted Windows shell
and existing Explorer shortcut. The same engine stayed running; configuration,
settings and preferences were unchanged, with advancing frames and all previous
running paths retained. Backups and physical file-handle checks were preserved.
The responsive workspace was left open; nothing was published.

| Identity | SHA-256 |
| --- | --- |
| Build source snapshot | `50707454f129d6cc051f26952a46e83a8dcfbcfe08e3fda0fdb5db833046aeb4` |
| VineMix.exe | `82a3af60f82d672cd860c2b00fe5898dd2e2b3c11e6fdd3f7dc34acd09007a52` |
| Unchanged VineMix.Engine.exe | `654ff6ef2185e0a4ee12540d5612f62a730371ca354bbe2cd4553c384ee34ddb` |

## Slower padlock return

The padlock still grows over 120 ms to 1.4×, but now returns over 480 ms after
the brief hold: four times as long as the enlargement. The animation ends at
660 ms and cleanup runs at 710 ms. Its colour stays red throughout.

Detached verification again passed **12,767 checks, 226 renders and zero binding
errors**. The verified same-version package passed source, contents, hash and
inert smoke checks. Interface-only activation **passed** through the trusted
Windows shell and existing shortcut. Engine identity, configuration, settings
and preferences remained unchanged, frames advanced and previous running paths
were preserved. Physical file-handle checks and external backups were retained;
the responsive workspace was left open. Nothing was published.

| Identity | SHA-256 |
| --- | --- |
| Build source snapshot | `f695cde70cc826dce6dbec6ed131a90228609b6607b950fefcd4f2adcb54b46f` |
| VineMix.exe | `072e382c6fb93388303bbe18b8f0fffb13d3184e2beb02108d0d1bb552041385` |
| Unchanged VineMix.Engine.exe | `654ff6ef2185e0a4ee12540d5612f62a730371ca354bbe2cd4553c384ee34ddb` |

## Footer countdown and centred save status

Save status now sits in the centre of the footer. Action messages temporarily
replace it, with a two-DIP green bar along the footer's top edge that depletes
from right to left over five seconds. Expiry or **Dismiss** restores save status;
every new message, including repeated text, restarts the countdown. The old
controls hint is removed. Full save-state tooltips and accessibility labels,
wrapped errors and the latest message in Activity are retained. Windows reduced
motion uses a static bar with the same dismissal time. Unloading stops the timer
and animation; returning resumes the remaining time or dismisses an expired message.

Detached verification passed **12,826 checks, 229 renders and zero binding
errors**. Coverage includes actual timer expiry, repeated-message deadlines,
manual dismissal, load/unload cleanup, reduced motion, centred save-state
bindings and header hit geometry. Idle, half-depleted, wrapped-error and
high-contrast previews were visually inspected. The same-version 28-file
package passed source, contents, hash and inert smoke checks.

Interface-only activation **passed** through the trusted Windows shell and
existing Explorer shortcut. The same engine remained running with unchanged
identity and hash, advancing frames and all previous running paths preserved.
Configuration, settings and preferences were unchanged. Physical settings
identity was checked through an open handle and both data views were backed up
outside the checkout; no settings were restored. The responsive workspace was
left open after the eight-second observation. Nothing was published.

| Identity | SHA-256 |
| --- | --- |
| Build source snapshot | `646f99d1a577aa3b37eca0e838f1de1bd623ab4cdd95baf2362c4321b6e206b2` |
| VineMix.exe | `1fac2b61f8837635012123f519ee69fa18642ae102dfc66f9467801371967439` |
| Unchanged VineMix.Engine.exe | `654ff6ef2185e0a4ee12540d5612f62a730371ca354bbe2cd4553c384ee34ddb` |

## Remaining qualification

Actual Virtual Desktop headset sound, provider-specific reconnect and sustained
listening require the maintainer's later session. No hardware/format/latency
guarantee follows from the synthetic route test. Desktop coverage uses detached
controls and state fixtures, not live pointer/keyboard interaction with the
personal workspace. Delivered GitHub update/download/install/rollback and
clean-machine qualification were not performed for this local-only candidate.

The layout lock is intentionally session-only. A recovery copy is a complete
workspace JSON file, not an automatic replacement of the current workspace;
see [the user guide](USER_GUIDE.md#run-and-build). Future releases must curate
their bundled highlights alongside their new release identity.
