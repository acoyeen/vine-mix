# VineMix known issues

A durable log of reported bugs, interface defects and existing usability limitations. Future feature proposals
belong in [development ideas](IDEAS.md). Recording an issue does not authorize
implementation or imply that it has been independently reproduced.
Optional refinements to working controls and layout belong in [tweaks](TWEAKS.md).
The 2026-10-25 classification review moved website layout/copy polish (ISSUE-006)
and update-window styling (ISSUE-009) there, retaining redirects below. Optional
fine-adjust/reset affordances were split from ISSUE-004. Broken behavior remains
here regardless of how small a correction might be.

Use stable IDs such as `ISSUE-001`; never reuse them. Update related entries
instead of adding duplicates. Track status as **Reported**, **Confirmed**,
**In progress**, or **Resolved**, and record verification and the fixing version
when an issue is resolved. Keep reports distinct from suspected causes.
A documented limitation may be confirmed in source while its proposed remedy
remains undecided; record that distinction explicitly.

| ID | Issue | Status |
| --- | --- | --- |
| [ISSUE-001](#issue-001) | Settings search caret overlaps placeholder | Resolved; live Beta 6 testing build, not published |
| [ISSUE-002](#issue-002) | Duplicate application monitoring | Reported limitation |
| [ISSUE-003](#issue-003) | Graph keyboard/accessibility gaps | Audit pending |
| [ISSUE-004](#issue-004) | Precise edits discarded on focus loss | Resolved; live Beta 6 testing build, not published |
| [ISSUE-005](#issue-005) | Dense-graph navigation and Fit limits | Partly confirmed in source |
| [ISSUE-006](#issue-006) | Website node route is hard to follow | Moved to [TWEAK-009](TWEAKS.md#tweak-009) |
| [ISSUE-007](#issue-007) | Automatic update checks can appear inactive and suppress failed retries | Resolved in Beta 5 |
| [ISSUE-008](#issue-008) | Installed Beta 2 reopens after an attempted Beta 4 update | Metadata defect resolved in Beta 5; tester incident not independently confirmed |
| [ISSUE-009](#issue-009) | Update window does not match the minimal application interface | Moved into [TWEAK-004](TWEAKS.md#tweak-004) |
| [ISSUE-010](#issue-010) | Word-reader prototype logo jitters at collapse finish | Local correction; visual review pending |
| [ISSUE-011](#issue-011) | Local UI activation helper rejects a newly opened controller | Confirmed tooling failure; exact cause unconfirmed |
| [ISSUE-012](#issue-012) | Scroll-story main heading is partly clipped | Local correction verified; maintainer review pending |
| [ISSUE-013](#issue-013) | Low / Mid / High and Ducking website meters do not animate | Resolved locally; maintainer review pending |
| [ISSUE-014](#issue-014) | Landing action contents and metadata drift from the selected design | Earlier layout superseded locally; visual review pending |
| [ISSUE-015](#issue-015) | Scroll for more remains visible after scrolling | Resolved locally; maintainer review pending |
| [ISSUE-016](#issue-016) | Open-source button GitHub icon does not invert on hover | Resolved locally; maintainer review pending |
| [ISSUE-017](#issue-017) | Header logo jitters when expanding | Resolved locally; maintainer review pending |
| [ISSUE-018](#issue-018) | Website release refresh stalls after reconnecting | Resolved and verified locally |
| [ISSUE-019](#issue-019) | Pipeline loading can outlive a disconnected component | Resolved and verified locally |
| [ISSUE-020](#issue-020) | Website Download and keyboard skip miss destination focus | Resolved and verified locally |
| [ISSUE-021](#issue-021) | Refresh flashes the expanded logo and resizes the scrollbar | Published and verified live |
| [ISSUE-022](#issue-022) | Selected nodes fall behind overlapping nodes | Resolved locally; live Beta 6 testing build, not published |

<a id="issue-022"></a>
## ISSUE-022: Selected nodes fall behind overlapping nodes

- **Reported:** 2026-10-28. **Target:** Beta 6. **Status:** Resolved locally and
  integrated into the installed testing app on 2026-10-28; not published.
- **Reported behavior:** The most recently selected node does not reliably
  remain above overlapping nodes, making its controls harder to see or reach.
- **Expected behavior:** Selecting a node brings it above other nodes and keeps
  it there after a click or drag. Selecting another node then brings that node
  to the front. Groups remain behind node and cable controls; selection order
  must not alter audio, node positions or routing.
- **Source-confirmed cause (2026-10-28):** `GraphWorkspace.BeginNodeMove` raised
  dragged members to Z-index 2, while `FinishNodeMove` reset them to 0. Ordinary
  selection did not retain a front order, so after release overlapping nodes
  returned to their canvas child order. This identifies the implementation cause
  separately from live pointer reproduction.
- **Implemented correction:** Retain a consistent session selection order for node visuals,
  including click, movement and selection changes, without changing saved audio
  configuration or automatic frame bounds. This is separate from the requested
  group appearance/background refinement in [TWEAK-017](TWEAKS.md#tweak-017).
- **Verification:** The final detached suite passed 13,526 checks with 273 renders
  and zero binding errors. Focused checks retain recent selection and drag/drop
  order through telemetry, view recreation, zoom, cancellation, multi-selection
  and group movement. Real-pointer checks of this correction were not completed.
- **Local integration:** Production build, inert controller smoke, 97 UI-only
  and 40 shared safeguard checks passed. Trusted-shell activation and the actual
  Explorer shortcut launch passed without rollback, preserving the same engine
  process and binary, advancing frames and running paths. The additional
  eight-second check passed. Saved settings/preferences were unchanged; physical
  settings identity and installed inventory were verified. Evidence is in
  [Beta 6 development](BETA6_DEVELOPMENT.md#group-refinements-and-node-stacking--28-october-2026).
- **Next step:** Maintainer review with overlapping nodes in the running app,
  including real pointer/VR use, then final Beta 6 release qualification when
  authorized. Local resolution does not imply publication or finalization.

## ISSUE-021: Refresh flashes the expanded logo and resizes the scrollbar

- **Date:** 2026-10-27.
- **Reported and reproduced:** Refresh briefly displayed the expanded hero logo
  before the compact-symbol/RSVP sequence. The scrollbar thumb changed as the
  desktop story and pipeline initialized.
- **Expected:** The first rendered logo matches the animation's starting pose;
  initialization does not change the sections' reserved space.
- **Cause verified:** Deferred reader setup changed the SVG pose, the story
  acquired its 480svh desktop height after load, and the pipeline replaced a
  short fallback with an asynchronously styled and populated shadow tree.
- **Resolution:** Early CSS supplies the compact pose for normal-motion script
  users and reserves the eligible desktop story height. The build includes the
  styled pipeline shell in the initial HTML; the controller adopts it and fills
  its existing stage. No-script and reduced-motion views retain static content.
- **Verification:** All nine check suites pass, including a new shell-adoption
  regression. At 1280×720, refresh samples kept document height at 7732px,
  story height at 3456px and pipeline height at 770.1875px across initialization
  and node loading. Mobile, reduced-motion and no-script views were checked.
- **Status:** Published to `vinemix.app` on 2026-10-27, Cloudflare deployment
  `7768eb10`. All 84 public files match the reviewed local build; the previous
  deployment remains available. Live refresh samples confirm stable styled
  geometry across reader setup and pipeline loading, and the compact initial
  logo was visually verified. Section navigation, pipeline rendering and the
  `vinemix.dev` redirect passed, with no captured browser warnings/errors.
- **Next step:** Maintainer visual review on the live website.

### Shader startup follow-up — 2026-10-27

- **Reported:** The background effect also appears large and then shrinks on
  refresh. The static preview uses `background-size: cover`, whereas the live
  shader scales its orb by the smaller canvas dimension; their handoff changes
  the apparent size at different screen proportions.
- **Correction:** Script-enabled startup keeps the plain background until the
  measured first GPU frame is drawn, then reveals it with a 180ms opacity fade.
  The shader geometry, speed and palette are unchanged. No-script and WebGL
  failure retain the poster; reduced motion disables the reveal transition.
- **Verification:** Ten check suites pass, including first-frame readiness,
  matching viewport/uniforms and missing/lost WebGL context regressions. Browser
  refresh samples show a constant 1257×715 drawing surface throughout the
  desktop fade. Mobile, reduced motion and no-script fallback checks pass.
- **Status:** Published and verified at `vinemix.app`, deployment `278ccbdd`.
  Both changed public assets match the local build. The live shader reports
  readiness at its measured size and fades in without displaying the poster.

### Header startup follow-up — 2026-10-27

- **Reported:** The top header logo briefly appears compact and centered before
  expanding and moving left on refresh.
- **Evidence:** Chrome refresh sampling exposed an intermediate 26px scroll
  offset before restoring 0px. The floating controller initialized immediately
  during module evaluation and could treat that offset as real scrolling.
  The exact visible race varies with resource timing; a regression fixture
  reproduces the transient offset and subsequent top restoration explicitly.
- **Correction:** Preserve the normal left-aligned header during loading. Mount
  the floating enhancement on the first animation frame after `pageshow`, when
  native scroll restoration has completed. Start at the settled pose without
  changing the user's scroll position. Navigation initializes immediately.
- **Verification:** All ten suites pass, including transient startup scroll,
  restored deep positions and cancellation before initialization. Three Chrome
  cold/cached refresh checks retain the expanded pose at the top. Refreshing at
  Downloads restores the compact pose; return-to-top still works. No captured
  browser warnings/errors.
- **Status:** Published at `vinemix.app`, deployment `2aed1065`. All 84 public
  files match the verified build. Live Chrome sampling reproduced the temporary
  26px offset, then 0px; the SVG retained its original expanded pose until the
  floating controller initialized expanded at the left after load. No captured
  browser warnings/errors. Mobile startup was also checked at 390px width.

### Page movement on refresh — 2026-10-27

- **Reported:** The page still moves up and down during refresh after the header
  pose correction.
- **Verified cause:** Before external styles apply, the browser's default body
  margin and the in-flow skip link place `main` 26px below the top. Chrome samples
  show a temporary 26px scroll position and then `main` at -26px before native
  restoration returns to 0. The header-only fix did not remove that page offset.
- **Correction:** Inline the small body-margin/skip-link positioning reset in
  the head. Disable scroll anchoring only during startup so initial layout
  changes do not compete with native history restoration; restore ordinary
  anchoring on the first animation frame after `pageshow`. No forced scroll or
  change to history-restoration settings is used.
- **Verification:** Four cold/cached Chrome refresh runs keep both scrollY and
  the main top at 0 throughout. A refresh at Downloads restores the same
  9823.333px position, mobile starts at 0 without horizontal overflow, and the
  no-script keyboard skip link reaches its native destination. Ordinary scroll
  anchoring returns to `auto`. All ten existing check suites pass.
- **Status:** Published at `vinemix.app`, deployment `035c051a`. All 84 public
  files match the verified build. Three live Chrome refresh runs, including a
  cache-bypassing refresh, keep both scrollY and the main top at 0 throughout.
  Startup anchoring protection clears correctly; no browser warnings/errors
  were captured. Maintainer visual review remains the next step.

<a id="issue-018"></a>
## ISSUE-018: Website release refresh stalls after reconnecting

- **Found:** 2026-10-27 during the authorized website audit. **Status:** Resolved
  in local website source/output; no published release or deployment.
- **Expected:** Restoring connectivity should promptly retry failed release
  discovery while retaining functional saved or previously checked downloads.
- **Verified:** Regression fixtures reproduced the attempt throttle discarding
  a reconnect, and a reconnect being lost during a still-pending failed request.
- **Correction:** Bypass the throttle on reconnect and retain one pending retry
  after failure. Successful pending requests avoid duplicate discovery; hidden
  pages defer the retry until visible. A failed retry does not loop.
- **Verification/next:** Release regression and browser fallback checks pass.
  Keep these checks in future changes; live service availability remains separate.
  See the [audit](../website-redesign/AUDIT.md).

<a id="issue-019"></a>
## ISSUE-019: Pipeline loading can outlive a disconnected component

- **Found:** 2026-10-27 during the authorized website audit. **Status:** Resolved
  and verified locally; no public deployment.
- **Expected:** Removing or reconnecting the pipeline must cancel obsolete work
  and keep only the current instance's observers and animation clock.
- **Verified:** A fixture reproduced an already-resolved fetch finishing JSON
  parsing after disconnect and mounting observers. Old queued observer callbacks
  could also act after cleanup. Hidden width could produce infinite SVG sizing.
- **Correction:** Check connection-specific cancellation at asynchronous and
  observer boundaries; defer sizing until the scale is finite and positive.
- **Verification/next:** Lifecycle, stale success/error, reconnect, observer and
  hidden-layout regressions pass; the normal pipeline loads in browser checks.
  Preserve these checks with future embedding changes. See the
  [audit](../website-redesign/AUDIT.md).

<a id="issue-020"></a>
## ISSUE-020: Website Download and keyboard skip miss destination focus

- **Found:** 2026-10-27 during the authorized website audit. **Status:** Resolved
  and verified locally; no public deployment.
- **Expected:** Explicit section shortcuts should use the shared navigation
  policy, including destination focus and the pinned-story bypass.
- **Verified:** The Download and keyboard skip anchors lacked the navigation
  marker. A browser Download activation reached the section but left active
  focus on the document body rather than the destination.
- **Correction:** Connect both anchors to the existing handler. Their real
  fragment destinations remain available without JavaScript.
- **Verification/next:** Browser checks cover both destination focus paths,
  history, other section shortcuts and JavaScript-disabled navigation. Keep
  equivalent checks when adding shortcuts. See the [audit](../website-redesign/AUDIT.md).

<a id="issue-017"></a>
## ISSUE-017: Header logo jitters when expanding

- **Reported and investigated:** 2026-10-27. **Status:** Corrected and verified
  locally on port 4175; no public deployment.
- **Report and expected behavior:** Collapse looks correct, but the header logo
  jitters slightly when expanding. Expansion should return smoothly to the
  header without changing the approved collapse motion or timing.
- **Verified evidence:** A local browser frame trace of a direct return from
  y=400 to y=0 showed a roughly 19 px vertical jump in the symbol at the first
  animated paint. Horizontal anchor/artwork positions remained monotonic. The
  anchor's vertical position followed raw scroll while its horizontal movement
  and retraction were eased. Small reverse scrolls also lifted the compact
  symbol before expansion began. This is a confirmed position discontinuity;
  it does not establish the cause of every possible rendering artifact.
- **Correction:** Keep the compact symbol docked until expansion starts, then
  ease from its last painted height to the header using the existing animation
  clock. Resizing preserves an expansion already underway; reduced motion and
  restored pages still resolve directly to their intended position.
- **Verification:** A comparable browser trace reduced the first visible
  vertical step from about 19 px to 4.6 px, followed by a monotonic return.
  Desktop/mobile checks confirm that a pause at y=12 keeps the symbol docked
  and y=0 restores the complete wordmark. Regression tests preserve the exact
  collapse and horizontal expansion samples, including their settling time;
  interrupted movement, resize and lifecycle checks pass. Build and the full
  website check suite pass.
- **Expansion-delay follow-up, 2026-10-27:** The maintainer then reported that
  expansion started too late. The previous return trigger waited until the
  page was within 1 px of the top. It now starts 2 px below the existing
  collapse threshold, preserving a small stable gap while responding as soon
  as the header trigger is crossed upward. This supersedes the earlier
  requirement to remain compact at y=12. The smooth vertical return and
  animation timing are retained.
- **Follow-up verification:** Desktop and mobile browser checks show expansion
  beginning on the first animation frame before reaching the top, then
  completing without further scrolling. Regression checks cover both sides of
  the trigger gap and retain the exact collapse/expansion timing samples.
- **Next:** Maintainer visual review of the [earlier expansion](../website-redesign/review/earlier-logo-expansion.jpg).

<a id="issue-016"></a>
## ISSUE-016: Open-source button GitHub icon does not invert on hover

- **Reported and verified:** 2026-10-27. **Status:** Resolved locally on port
  4175; no public deployment.
- **Expected:** The GitHub icon should invert with the button label during the
  white hover fill, retaining its existing size and scale.
- **Confirmed cause:** The icon kept a fixed dark backing and white filter,
  while only the label used difference blending. Browser hover reproduced the
  unchanged white icon on its dark disc.
- **Correction and verification:** The icon wrapper now uses a transparent
  backing and the same difference blending as the label. Browser checks confirm
  inversion on hover and keyboard focus. The 20 px artwork, 26 px wrapper and
  194.15625 × 54 px desktop button dimensions are unchanged. Build passed.
- **Next:** Maintainer visual review of the [local capture](../website-redesign/review/github-icon-hover.jpg).

<a id="issue-015"></a>
## ISSUE-015: Scroll for more remains visible after scrolling

- **Reported and verified:** 2026-10-27. **Status:** Resolved in the local
  website preview on port 4175; no public deployment or desktop release.
- **Report and expected behavior:** The landing cue remained visible while
  scrolling. It should disappear when leaving the top and return at the top.
- **Reproduction and cause:** Before correction, browser scrolling to y=108
  left the cue visible. Its visibility depended on the hero remaining more
  than 20% visible, so it persisted during the beginning of the scroll.
- **Correction:** Visibility now follows actual page position, allowing 1 px
  for fractional settling at the top. The scroll handler updates it immediately;
  page restoration and visibility changes also refresh it. Native anchors,
  click/keyboard navigation and destination focus are retained.
- **Verification:** Desktop and mobile browser scrolling hides the cue, and
  returning to y=0 restores it. The landing control still reaches How It Works;
  Skip reaches the pipeline. Build and existing navigation checks pass.
- **Next:** Maintainer review of the locally hosted result. The associated
  visual refinement is recorded in [TWEAK-009](TWEAKS.md#tweak-009).
- **Fade refinement, 2026-10-27:** The maintainer subsequently found the
  immediate disappearance too abrupt. The landing cue now eases to zero
  opacity over the first 120 px, restoring on reverse scrolling. Skip fades
  with the progress bar at the walkthrough's end. Both retain layout and
  become inert when fully faded. Local browser checks verified intermediate
  opacity, restoration and focus removal; reduced motion/high contrast retain
  immediate visibility changes. This supersedes the 1 px visibility threshold
  above for ordinary motion settings.

<a id="issue-014"></a>
## ISSUE-014: Landing action contents and metadata drift from the selected design

- **Reported and reviewed:** 2026-10-26. **Status:** Earlier two-action layout
  superseded locally; current visual review pending. No public deployment.
- **Latest direction:** The maintainer removed hero Documentation and selected
  one centred **Download** action, now using the selected Flow Hover treatment:
  transparent at rest with a sweeping white fill and stationary download icon/
  label. This supersedes the earlier sliding-label variants. Docs
  remains in the header, now a compact 36 px white pill with black text and arrow.
  The platform/beta note beneath Download remains removed. The original
  two-button mismatch and its corrections below are historical; the new layout
  has no post-change visual verification yet. See [TWEAK-012](TWEAKS.md#tweak-012).
- **Report:** The maintainer still found the landing buttons uneven after the
  equal-size change in [TWEAK-012](TWEAKS.md#tweak-012), and supplied a Chrome
  screenshot of the implemented page.
- **Verified pre-correction evidence:** The screenshot shows equal 216 × 54 px
  outer action boxes at the same vertical position. Their contents differ:
  Download includes a GitHub icon, while Documentation has no icon. The Windows/
  beta note has a filled pill treatment that can read as a third, smaller
  button; the selected concept uses plain text. Source confirms those omissions
  from the selected composition. The current header is visible, so a stale-page
  explanation is not established. The user's broader uneven-appearance report
  is not dismissed by equal outer dimensions.
- **Earlier expected behavior and follow-up:** Both actions use a matching icon-and-label
  arrangement inside equal boxes, with plain platform/beta text beneath them.
  The maintainer subsequently said Documentation still looked thinner and
  supplied a reference requesting a download arrow ending at a line, an open
  book, and rounder edges. This supersedes the initial rectangular/GitHub-icon
  treatment; perceived thinness is not proof of unequal outer dimensions.
- **Earlier local corrections:** An interim shared 18 px icon/154 px content grid was
  followed by the requested 20 px download-arrow and book icons in matching
  160 px content grids. Both buttons retain equal outer sizing and use 2 px
  border geometry with pill rounding. An interim stronger Documentation outline
  was superseded by the maintainer's explicit correction: remove that outline,
  because it made the action look smaller. Its default and hover borders are now
  transparent, retaining equal dimensions and the keyboard-focus indicator.
  The platform/beta note is plain text.
- **Earlier verification:** Build and source checks passed for equal icon/content
  tracks, button dimensions, borders and absence of a pause control. Homepage,
  CSS and both icons returned HTTP 200 and match built files on both local host
  addresses at port 4175. Earlier CSS/font checks
  established calculated fit only; they did not establish browser visual
  balance. The supplied screenshot documents the pre-fix state, not the
  corrected result. Keep only this sanitized evidence summary in the repository;
  private screenshots and reference crops are not copied or linked here.
- **Outline correction checks:** Rebuild passed; CSS served through both local
  host addresses matches the build. Default/hover border transparency, unchanged
  sizing and retained focus-visible styling were checked. No new visual check.
- **Next step:** Capture and compare the corrected desktop and phone views
  when browser access returns. Track the outstanding visual review in
  [website QA](../website-redesign/QA.md) and
  [design QA](../website-redesign/design-qa.md).

<a id="issue-013"></a>
## ISSUE-013: Low / Mid / High and Ducking website meters do not animate

- **Reported, confirmed and locally resolved:** 2026-10-25. **Status:** Resolved
  locally in `website-redesign`; maintainer review remains. The maintainer
  reported that the Low / Mid / High and Ducking nodes lacked the animated
  meters shown on the other active nodes.
- **Confirmed cause before correction:** The website markup included meter
  overlays only for Physical input, Running app and Virtual output. Its meter
  controller likewise produced and painted only those three signals. Low / Mid /
  High and Ducking remained static native images, so the missing motion was an
  implementation omission rather than evidence of missing audio.
- **Expected:** Low / Mid / High follows the illustrated voice signal. Ducking
  shows the processed music level, falling under speech and recovering in the
  gaps. Both use silent visual simulation while visible. Listening output stays
  muted and still; preserve the approved voice rhythm and music pulse.
- **Local correction:** Added native meter overlays to the two effect nodes
  and connected them to the existing visual meter clock and routing signals.
  The Low / Mid / High meter follows the microphone; the Ducking meter shows
  music after the simulated reduction. Native WPF meter strips were rendered
  without opening application windows, starting the engine or loading settings;
  all 33 existing assets remained byte-identical.
- **Verification:** Build and full checks pass, including the unchanged voice
  and 120 BPM music samples, five overlays, and pause/offscreen/hidden-effect
  behavior. In 100 browser samples over five seconds, all five active meters
  moved. Tone differed from the microphone by at most 0.0056 dB; Ducking was
  4.22–12.07 dB below the music meter during speech and recovered to within
  0.0002 dB in a sampled quiet interval. These are simulated display values,
  not live audio measurements. Both new images loaded, with alignment error
  below 0.12 CSS px; all five meters held unchanged for 450 ms when paused.
  Existing tone-frame changes remained intact. Listening has no meter overlay,
  and the page has no audio/video elements.
- **Next step:** Maintainer review in the local preview. This correction has
  not been published as a website or desktop release. Current evidence and
  limits are in [scroll QA](../website-redesign/SCROLL_QA.md).

<a id="issue-012"></a>
## ISSUE-012: Scroll-story main heading is partly clipped

- **Reported and locally corrected:** 2026-10-25. **Status:** Local correction
  verified in `website-redesign`; maintainer review remains.
  During local review, the maintainer reported that “See where your sound goes”
  was partially cut off as the second section appeared.
- **Expected:** Each main heading and its description remain fully readable
  throughout entry, pinned scrolling and responsive resizing, including after
  fonts load.
- **Evidence before correction:** The source reserved a fixed 130 px heading
  region and positioned all chapter copies absolutely inside it. The initial 974×873 local
  browser view did not reproduce the reported clipping. The fixed allocation
  is a plausible sizing weakness, not a confirmed cause of the reported case.
- **Local correction:** Size the shared heading region from its chapter
  content and use that measured height when fitting the node scene. Remove the
  three small chapter headings as separately requested in
  [TWEAK-009](TWEAKS.md#tweak-009).
- **Verification:** All three headings remained fully inside their heading
  region and clear of the diagram at 1440×900, 1280×720 and 900×680. The region
  now measured 115, 102 and 98 px respectively, fitting its actual text instead
  of reserving a fixed height. The actual 974×873 desktop screenshot showed
  full headings; 390×844 and 1280×560 fallback checks found no horizontal
  overflow. Forward/reverse scrolling and build checks passed. These results
  verify the revised local layout, not the cause of the original clipping.
- **Next step:** Maintainer review at the originally affected viewport. No
  published website or desktop release has been changed. See
  [scroll QA](../website-redesign/SCROLL_QA.md) for current evidence and limits.

<a id="issue-011"></a>
## ISSUE-011: Local UI activation helper rejects a newly opened controller

- **Observed:** 2026-10-24. **Status:** Confirmed local tooling failure; exact
  startup race remains unconfirmed. This helper is separate from the released
  in-app updater.
- **Verified behavior:** The helper replaced the UI and installed inventory,
  then reported an unexpected controller while reopening through the actual
  Windows shortcut. It correctly refused to stop an unrecorded process during
  recovery. Independent read-only checks confirmed the expected new UI was
  responsive, its files matched the candidate, and the original audio engine,
  physical settings location, saved workspace copies and preferences survived.
- **Expected:** Recognize the intended new controller during startup while
  continuing to reject another executable, session or pre-existing process.
- **Suspected cause:** The helper reads PowerShell's `Process.Path`, which
  depends on `MainModule.FileName`. Image information may be unavailable during
  early startup or launcher exit. The rejected PID and mismatching field were
  not captured, so this explanation is not a confirmed reproduction.
- **Next step:** Capture sanitized field-level diagnostics and exercise early
  startup/launcher exit. Evaluate querying the process image by its handle,
  preserving all session, start-time, live-process and rollback guards. Keep raw
  evidence outside the checkout; no additional live restart is required now.

<a id="issue-010"></a>
## ISSUE-010: Word-reader prototype logo jitters at collapse finish

- **Reported:** 2026-10-24. After synchronizing the lettering and symbol, the
  maintainer reported jitter at the end of the standalone homepage prototype's
  collapse. Expected: both finish together with a clean arrival.
- **Source evidence:** The shared `progress ** 6` curve ended at maximum speed
  before abruptly stopping. At the largest preview size, the final nominal
  60 Hz frame covered about 62 px. This explains a likely abrupt stop; the
  subjective jitter was not independently reproduced.
- **Local correction:** Use shared `smootherstep(progress²)` over the same 390 ms.
  It retains a slow start and acceleration but reaches zero velocity and
  acceleration at the hold. Final nominal frame travel is about 1.4 px.
- **Verification:** Syntax and served-source checks passed. Browser samples
  confirmed synchronized, monotonic movement to the correct endpoints with no
  console errors. **Next:** maintainer visual review. Scope is only
  `prototypes/word-reader`, not the main website or desktop application.

<a id="issue-007"></a>
## ISSUE-007: Automatic update checks can appear inactive and suppress failed retries

- **Reported and resolved:** 2026-10-24. **Status:** Resolved in published
  Beta 5; source and final-package checks passed.
- **Reported:** A remote tester found the automatic-check control ineffective,
  while a manual check found Beta 4.
- **Verified in released source:** The checkbox only saves a preference. A
  single startup check waits 30 seconds, records an attempt before contacting
  GitHub, and suppresses subsequent startup checks for a day even after failure.
  There is no periodic check while the application remains open.
- **Expected:** Clear preference feedback, prompt checking when enabled, daily
  checks while running, and bounded retries after failure without downloading
  or installing automatically.
- **Local correction:** Record only successful checks, retry failures after 15
  minutes, defer to active manual operations, and cancel background work when
  disabled or quitting. Ignore the old attempt timestamp because it cannot prove
  success.
- **Verification:** Deterministic scheduler checks cover failure/retry, daily
  cadence, stored timestamps, busy manual operations and cancellation. The full
  detached desktop suite passes 12,905 checks with zero binding errors.
- **Next step:** Qualify the delivered
  correction on a separate test installation.
  The tester's exact automatic-check timing and network conditions remain unknown.
- **Evidence:** [Update investigation](UPDATE_INVESTIGATION_2026-10-24.md).

<a id="issue-008"></a>
## ISSUE-008: Installed Beta 2 reopens after an attempted Beta 4 update

- **Reported and locally corrected:** 2026-10-24. **Status:** Confirmed package
  metadata defect; isolated rollback reproduced. Corrected in published Beta 5
  source and packages. The tester's own journal has not been
  inspected.
- **Reported:** A remote tester using the installed distribution manually found
  Beta 4, downloaded and installed it, then saw Beta 2 after the application
  restarted.
- **Expected:** A verified successful update reopens Beta 4 in the existing
  installation and saved workspace; a failed update clearly reports recovery.
- **Verified:** The exact Beta 2 discovery code and signing trust accept the live
  Beta 4 release anonymously. Direct Beta 2-to-4 updates are allowed; Beta 3 is
  not a prerequisite. The displayed version comes from the running executable.
- **Confirmed cause:** The delivered engine's textual Windows `FileVersion`
  contains `0.3.0-beta.4`; the worker requires numeric `0.3.0.5`. All 35 local
  installed files match the package manifest. The exact original Beta 2 checker
  rejects that engine, despite its correct fixed numeric fields. The installed
  worker then rolls back before smoke/health. A synthetic transaction reproduces
  rollback and reopening; the same defect also affects standalone extraction.
- **Correction / verification:** Use numeric `FileVersion` in the native resource
  while retaining semantic `ProductVersion`. The unchanged old checker accepts
  the rebuilt engine. Real metadata and isolated rollback tests pass: 111 worker
  checks, five native metadata fixtures and nine release-preparation checks.
  New build and packaging gates verify actual delivered version metadata.
  The exact original Beta 2 core/trust accepts the final signed Beta 5 envelope
  and both package hashes; its unchanged version checker accepts the installed
  and standalone-extracted engine at numeric version `0.3.0.6`. Both engines
  have identical bytes. Final-package checks pass: both inert smoke checks,
  18 standalone-runtime checks and 167 isolated Setup checks.
- **Diagnostic improvement:** Local source preserves the Setup exit code and
  writes its log in the private transaction folder. This does not retrofit the
  tester's already installed Beta 2 worker; the native metadata correction is
  what enables old workers to accept a future package.
- **Release decision (2026-10-24):** Prepare Beta 5 with corrected engine
  metadata, preserve the Beta 2/3/4 tags and downloadable bytes, and add notices
  to their release notes. Beta 2 remains provisional. Rebuilding older versions
  would create different bytes under the same version and erase useful evidence.
- **Recovery:** Save, **Quit VineMix**, then run Beta 5 Setup against the existing
  installation without uninstalling; standalone users replace the old EXE after
  quitting. Keep settings and recovery evidence. Intermediate betas are not
  required. Manual Setup bypasses the older worker's metadata check.
- **Publication:** [Beta 5](https://github.com/acoyeen/vine-mix/releases/tag/v0.3.0-beta.5)
  was published on 2026-10-24. All five uploaded assets were downloaded and
  rehashed, and the pinned-key signature passed. The original tags and packages
  remain unchanged. Beta 2/3/4's live release notes now include update notices
  and recovery guidance, preserving their original bodies.
- **Next step:** Qualify an
  actual delivered update on a separate test machine/account. Existing Beta 3/4
  packages remain affected. A sanitized tester journal can confirm the exact
  incident. Retain raw evidence outside the repository.
- **Evidence and workaround:** [Update investigation](UPDATE_INVESTIGATION_2026-10-24.md).

<a id="issue-009"></a>
## ISSUE-009: Update window does not match the minimal application interface

Reclassified on 2026-10-25 into [TWEAK-004](TWEAKS.md#tweak-004): the Beta 5
update-window redesign is the released foundation for the later information-window
refinements. Its original report, scope and verification are retained there.
Update-check and installation failures remain ISSUE-007 and ISSUE-008. This ID
and heading remain reserved for existing links.

<a id="issue-006"></a>
## ISSUE-006: Website node route is hard to follow

Reclassified on 2026-10-25 as [TWEAK-009](TWEAKS.md#tweak-009): website diagram
layout and copy polish. The original report and local verification are retained
there; no public deployment is implied. This ID and heading remain reserved for
existing links.

<a id="issue-001"></a>
## ISSUE-001: Settings search caret overlaps the placeholder word

- **Reported:** 2026-10-24. **Resolved locally:** 2026-10-27. **Status:** Resolved;
  active in the live Beta 6 testing build from 2026-10-28. Not yet published.
- **Area:** Settings dropdown search field.
- **Observed:** The maintainer reports that the blinking text insertion caret
  appears in the middle of the first placeholder word within the search box.
- **Expected:** Adjust positioning so the empty field's caret aligns with the
  start of the placeholder and typed text, rather than sitting inside the word.
- **Impact:** Confusing visual alignment when beginning a settings search.
- **Original reproduction steps:** Open Settings, leave the search empty, and focus
  the search box. Inspect the caret against “Search settings and help”.
  Empty-field focus was inferred from the report; the original affected build
  was not confirmed. The layout defect was subsequently reproduced below.
- **Investigation:** Source inspected on 2026-10-24. The search box and its
  separate placeholder are built in
  [SettingsSearchView.cs](../desktop/VineMix.Desktop/SettingsSearchView.cs);
  the shared text-box template is in
  [App.xaml](../desktop/VineMix.Desktop/App.xaml). Their padding, margins and
  content alignment are investigation points, not a confirmed root cause.
  No live reproduction or external research has been performed.
- **Confirmed cause, 2026-10-27:** Detached WPF layout reproduced a 9-DIP
  horizontal mismatch: the actual insertion rectangle began at x=18 while the
  placeholder began at x=9. The shared template applies content-host margin
  and padding; the separately positioned placeholder did not account for both.
- **Correction:** Position the placeholder from WPF's actual insertion rectangle
  after layout and match the search field's font metrics. Preserve the shared
  text-box template, field dimensions and existing search/keyboard behavior.
- **Verification:** Actual Settings-menu checks compare insertion and placeholder
  text rectangles, typing, and clearing after a horizontally scrolled query at
  100/125/150/175/200% scaling. Normal and high-contrast resource checks include
  the actual displayed brushes; representative rendered results were reviewed.
  The combined desktop suite passed **13,352 checks, 268 renders and zero binding
  errors**. Production and verification builds passed; production reported no
  compiler warnings or errors.
- **Verification boundary / next step (2026-10-27):** These are detached WPF layout and input
  route checks, not a native blinking-caret observation in the installed app.
  No personal app, engine or settings were changed, and no release was published.
  Retain these regressions and include the fix in the next qualified release;
  confirm native focus/caret behavior during its desktop qualification.
- **Beta 6 preparation, 2026-10-28:** Reverified under the canonical
  `0.3.0-beta.6` / `0.3.0.7` identity: **13,356 desktop checks, 268 renders and
  zero binding errors**. The live development transition is tracked in
  [the Beta 6 record](BETA6_DEVELOPMENT.md); this result does not claim a native
  caret observation or a published release.
- **Live integration, 2026-10-28:** Applied to the installed Beta 6 testing app
  after the maintainer approved the required engine restart. Matching binaries
  and inventory, preserved workspace/preferences and recovered running audio
  paths were verified. The native blinking-caret observation remains a testing
  step; see [the integration record](BETA6_DEVELOPMENT.md#live-activation--28-october-2026).

<a id="issue-002"></a>
## ISSUE-002: Duplicate audio when monitoring an application

- **Originally captured:** 2026-10-20 as IDEA-012.
  **Moved to this log:** 2026-10-24. Original ID remains reserved.
- **Status:** Reported limitation. **Previous priority:** P3.
- **Problem:** Original application playback and the captured, processed signal
  can reach the same headphones together. Users wanting processed-only listening
  can hear duplicate audio.
- **Expected outcome:** Provide a deliberate, understandable way to hear only the
  processed path while preserving capture and the user's Windows audio choices.
- **Evidence review (2026-10-24):** APP_AUDIO.md documents this routing limitation
  and the virtual-input workaround. This is expected loopback behavior with a
  usability consequence, not a newly confirmed capture-engine fault. No live
  audio check was performed. Earlier external research below was not refreshed.
- **Workaround:** Explicitly route the source app through the installed virtual
  input and use one capture path, as described in [application audio](APP_AUDIO.md).
- **Next step:** Qualify the proposed original-playback suppression in isolation;
  retain guided virtual-cable routing as an alternative if capture is also muted.

### Retained proposal and research from IDEA-012

These earlier notes preserve alternatives, tradeoffs and unresolved decisions.
They do not establish a chosen fix or implementation approval.

- **User need:** Apply EQ or other effects to a running application's audio and
  hear only the processed result. Currently the app's original playback and Vine
  Mix's listening output can both reach the same headphones, producing duplicate
  audio.
- **Requested direction:** Add a button on running-app input nodes to silence
  the application's direct local playback while retaining capture and the
  processed listening route. This differs from the node's existing Mute, which
  silences its signal inside VineMix. Suggested label: **Mute original playback**,
  with a tooltip explaining the distinction; wording is not yet chosen.
- **Project fit:** [Application audio](APP_AUDIO.md) documents that process
  loopback copies the selected process tree's audio without suppressing original
  playback. `native/src/process_capture.cpp` implements that capture path, and
  application discovery already inspects Windows render sessions. Existing code
  deliberately does not alter application mute, volume, or endpoint assignments.
  This proposal would introduce an explicit user-controlled exception.
- **Primary-source research (2026-10-20):**
  [Microsoft ISimpleAudioVolume::SetMute](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-isimpleaudiovolume-setmute)
  can mute a Windows audio session, and
  [Audio Sessions](https://learn.microsoft.com/en-us/windows/win32/coreaudio/audio-sessions)
  explains that a session groups streams and render-session mute can persist
  across restarts. The
  [application loopback sample](https://learn.microsoft.com/en-us/samples/microsoft/windows-classic-samples/applicationloopbackaudio-sample/)
  documents endpoint-independent process-tree capture, but does not guarantee
  that muting the target's render session leaves this capture audible. Do not
  infer that guarantee from endpoint-master-volume or AEC loopback behavior.
- **Feasibility conclusion:** A session-mute control is technically available;
  whether it achieves original-playback suppression while retaining usable
  process-loopback audio must be tested with this engine and representative
  applications. No application was muted, Windows routing changed, or live audio
  test performed for this research. Do not present a simple mute API call as a
  proven solution to duplicate monitoring.
- **Alternative:** Explicitly route the source application to an installed virtual
  cable, then capture one path and listen through VineMix. This is already
  described in APP_AUDIO.md and in the
  [OBS application capture guide](https://obsproject.com/kb/application-audio-capture-guide).
  A guided routing action could be explored if session mute also silences capture,
  but introduces device/driver requirements and a separate Windows routing change.
  Do not enable Windows "Listen to this device" or another simultaneous capture
  route that restores a duplicate path.
- **Scope and tradeoffs:** Match all relevant render sessions to the selected
  process tree, including child processes and new sessions after app restarts or
  endpoint changes. A session may affect more than one stream; a browser target
  may include multiple tabs. Original playback mute affects the Windows session,
  not just the VineMix node, and might affect other consumers. Account for
  multiple nodes targeting the same app, external mute changes, and preserving a
  pre-existing user mute. Keep control and session discovery off the audio thread.
- **Open questions:** Reliable capture-after-session-mute behavior; intended scope
  when the app plays on several devices; ownership and restoration on unselection,
  node removal, shutdown, crash, or engine failure; and whether the button should
  require a healthy listening route. Avoid leaving the app unexpectedly muted or
  silently unmuting it over a later user change. Recovery policy remains undecided.
- **Next step:** Define an isolated, user-authorized local-audio qualification:
  compare capture signal presence and direct playback before/after Windows session
  mute using known non-protected material. Then check child sessions, relaunch,
  multiple nodes, and restoration. If capture is silenced, investigate guided
  virtual-cable routing. This entry authorizes research notes only.

<a id="issue-003"></a>
## ISSUE-003: Incomplete graph keyboard and accessibility workflows

- **Originally captured:** 2026-10-23 as IDEA-026.
  **Moved to this log:** 2026-10-24. Original ID remains reserved.
- **Status:** Reported; workflow audit pending. **Previous priority:** P2.
- **Problem:** Earlier UI review identified gaps in operating and understanding
  a complete graph without a mouse, including traversal and accessible state.
- **Expected outcome:** Users can navigate, edit and inspect the graph with the
  keyboard and assistive technology, with current names and clear focus/state.
- **Evidence review (2026-10-24):** GraphWorkspace.cs and GraphNodeView.cs provide
  existing shortcuts, focus and automation labels. Their presence does not prove
  complete workflow coverage. Missing actions and rename/state announcements
  still need a focused audit; no live accessibility verification was performed.
- **Scope and next step:** Walk through create/select/connect/rename/inspect,
  document the exact failures, then choose the smallest corrections. The optional
  outline and traversal designs below remain candidate remedies.

### Retained proposal and research from IDEA-026

These earlier notes preserve alternatives, tradeoffs and unresolved decisions.
They do not establish a chosen fix or implementation approval.

- **Origin and need:** Prior UI-review suggestion. Existing keyboard controls
  should extend to navigating and understanding a complete graph without a mouse.
- **Proposal:** Next/previous and connected-node traversal, keyboard positioning,
  F2 rename, automatic reveal of focused nodes, and an optional node/connection
  outline. Keep accessible node names current after rename; describe controls and
  real toggle states in context.
- **Project fit / research:** `GraphWorkspace.cs` and `GraphNodeView.cs` already
  provide shortcuts, focusable controls and sockets; [the guide](USER_GUIDE.md)
  records Find node, dial editing and high contrast. This completes remaining
  interactions rather than replacing existing support. Local review only.
- **Scope, tradeoffs and dependencies:** Medium; avoid shortcut conflicts and
  unexpected parameter edits during navigation. Coordinate lock (IDEA-017),
  groups and presets. Define traversal for branched graphs and editing fields.
- **Next step:** Walk through create/select/connect/rename/inspect using only a
  keyboard, then specify missing actions and assistive-technology expectations.

<a id="issue-004"></a>
## ISSUE-004: Precise value edits can be discarded on focus loss

- **Originally captured:** 2026-10-23 as IDEA-027.
  **Moved to this log:** 2026-10-24. Original ID remains reserved.
- **Status:** Resolved in local development source on 2026-10-27; active in the
  live Beta 6 testing build from 2026-10-28. Not yet published.
  **Previous priority:** P2.
- **Problem:** Pointer/VR users lack a convenient commit/cancel flow for precise
  values; clicking away can discard a pending edit without applying it.
- **Expected outcome:** Make pending values and their apply/cancel behavior clear
  and usable with a pointer, while preserving keyboard editing and validation.
- **Evidence review (2026-10-24):** RotaryValueControl.cs redraws the current value
  on LostKeyboardFocus. HandleEntryKey supports Enter and Tab to commit, and
  Escape to cancel; validation feedback already exists. This narrows the earlier
  proposal to remaining pointer usability and focus-loss behavior, rather than
  treating keyboard commit or validation as missing. No live check was run.
- **Earlier workaround:** Commit a valid value with Enter or Tab before clicking away.
- **Confirmed reproduction, 2026-10-27:** A regression using the real preview
  node and its routed focus-loss event failed against the earlier editor: a
  pending exact value did not reach the graph or create its expected Undo entry.
- **Correction:** Clicking away applies a valid pending value once, retaining
  its full precision. Invalid text stays visible with a validation outline and
  accessible "Not applied" feedback, without pulling focus back. Right-click
  offers **Apply value** and **Cancel edit**, alongside standard text commands.
  Enter/Tab/Shift+Tab and Escape retain their existing apply/cancel behavior.
  Opening the menu preserves the draft until its action or genuine departure;
  Cancel cannot first apply the value through menu focus loss.
- **Related safeguards:** Unchanged model/theme refreshes preserve rejected
  drafts. Changed values, EQ target changes, explicit dial adjustments, disabling
  and unloading cancel obsolete drafts. Switching effect modes disables hidden
  editors so rejected text cannot become an invisible blocker. Quit and update
  preparation check pending editors throughout the live workspace. Help examples
  retain their separate temporary state. Help text and the user guide describe
  the new behavior. Optional fine-adjustment/reset affordances remain separate
  in [TWEAK-007](TWEAKS.md#tweak-007).
- **Verification:** Regression checks cover exact and localized values, invalid
  and whole-sample input, preserved text/selection, menu Apply/Cancel and deferred
  closure, unchanged layout, theme feedback, disabled/read-only/unloaded controls,
  EQ target/removal and mode changes, preparation, and one-step graph Undo/Redo.
  The combined desktop suite passed **13,352 checks, 268 renders and zero binding
  errors**; production and verification builds passed. The original focus-loss
  regression now passes, including protection against a second blur rounding or
  applying the displayed value again.
- **Verification boundary / next step (2026-10-27):** Tests raise real WPF input routes in
  detached synthetic controls; they do not establish native pointer/VR or popup
  focus timing in the installed app. No personal app, engine or settings were
  changed, and no release was published. Include the correction in the next
  qualified release and confirm those native interactions during desktop checks.
- **Beta 6 preparation, 2026-10-28:** The canonical Beta 6 detached suite passed
  **13,356 checks, 268 renders and zero binding errors**, including exact-value
  focus loss, invalid drafts and menu Apply/Cancel. See
  [the Beta 6 record](BETA6_DEVELOPMENT.md) for live integration status. Native
  pointer/VR interaction remains a separate testing boundary.
- **Live integration, 2026-10-28:** The verified exact-entry implementation is
  active in the installed Beta 6 testing app. The authorized transition preserved
  saved settings/preferences and recovered previously running audio paths with
  matching app/engine versions and inventory. Native pointer/VR and popup-focus
  testing remains separate; see
  [the integration record](BETA6_DEVELOPMENT.md#live-activation--28-october-2026).

### Retained proposal and research from IDEA-027

These earlier notes preserve alternatives, tradeoffs and unresolved decisions.
The local resolution above supersedes their proposed next step; optional
affordances outside that resolution remain proposals.

- **Origin and need:** Prior UI-review suggestion for mouse/VR users who cannot
  conveniently use keyboard commit gestures for precise edits. The original
  Shift/double-click discoverability suggestions are retained in TWEAK-007.
- **Proposal:** Explore compact pointer Apply/Cancel, visible pending text and
  inline validation. Preserve today's keyboard commit/cancel and deliberate
  wheel behavior. This issue concerns completion of an edit, not adjustment/reset
  presentation.
- **Project fit / research:** `Controls/RotaryValueControl.cs` supports exact
  fields and keyboard gestures; pending text can be discarded on focus loss.
  Local review only. Controls and accepted compact styling already exist.
- **Scope, tradeoffs and dependencies:** Small to medium; the extra affordances
  must not overwhelm every node. Decide whether they appear only during editing
  or in a contextual editor. Keep one undoable edit per completed gesture.
- **Next step:** Compare one minimal contextual-edit design with the current
  control, using pointer-only entry, invalid text, cancellation and focus changes.

<a id="issue-005"></a>
## ISSUE-005: Dense-graph navigation and misleading Fit expectations

- **Originally captured:** 2026-10-23 as IDEA-028.
  **Moved to this log:** 2026-10-24. Original ID remains reserved.
- **Status:** Fit limitation confirmed in source; pointer workflow needs verification.
  **Previous priority:** P2.
- **Problem:** Fit promises to bring every node into view, but sufficiently large
  layouts exceed the minimum zoom. Earlier review also identified difficulty
  panning a full window of nodes using only the available pointer controls.
- **Expected outcome:** Explain when the graph cannot fully fit and provide a
  discoverable way to reach off-screen nodes in dense layouts.
- **Evidence review (2026-10-24):** GraphWorkspace.cs clamps FitAll to a minimum
  zoom of 0.3 while its tooltip says it brings every node into view. Middle-button
  and Space+drag panning already exist, so the earlier empty-canvas/right-drag
  note must not be read as the only panning route. Pointer-only accessibility
  and event handling over controls still need reproduction; no live check ran.
- **Workaround:** Use existing middle-button or Space+drag panning, Find node,
  and Frame selection as appropriate to the input device.
- **Scope and next step:** Check a dense graph beyond Fit's zoom limit and a
  pointer-only workflow; correct feedback and choose the smallest remaining
  navigation remedy. A hand mode or overview is a proposal, not a decided fix.

### Retained proposal and research from IDEA-028

These earlier notes preserve alternatives, tradeoffs and unresolved decisions.
They do not establish a chosen fix or implementation approval.

- **Origin and need:** Prior UI-review suggestion. Users need to pan even when
  nodes fill the window, and understand a Fit action that cannot show everything.
- **Proposal:** Explore a hand/pan mode or reserved navigation area, with an
  optional overview navigator. Make the minimum-zoom fit limit understandable
  rather than implying every graph will fit. Existing Find and Frame selection
  remain available.
- **Project fit / research:** `GraphWorkspace.cs` clamps zoom and initiates
  right-drag panning on empty space; [workspace controls](NODE_WORKSPACE.md)
  document existing navigation. Local inspection only; no external research.
- **Scope and tradeoffs:** Medium UI work; avoid intercepting dials, cable edits
  or selection. No decision to change supported zoom bounds or renderer. A hand
  mode may solve the main problem without an always-visible overview panel.
- **Next step:** Review a dense example at minimum zoom and select the smallest
  navigation addition that works with lock mode and pointer-only use.
