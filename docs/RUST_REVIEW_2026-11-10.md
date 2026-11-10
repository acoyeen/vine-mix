# VineMix Rust review — 10 November 2026

## Implementation handoff — start here

This document contains **41 stable findings**, followed through a series of dated investigations. Use the tracker below as the implementation index; the detailed findings remain the evidence record. The original review and follow-up source checks refer to commit `55e77c761f66e1c0270dda5fb8bff75e18d6141c`. Revalidate locations and behavior against the current checkout before changing code, and preserve unrelated or concurrent work. If an accompanying earlier prompt refers to 40 findings, include the subsequently added RUST-REV-041 as well.

**Authority and scope:** Statements below such as “no implementation authorized”, “no code changed” and “implementation pending” describe the review stage. They do not override a subsequent explicit maintainer instruction to implement fixes. When this report accompanies an implementation prompt, follow that prompt and the current repository instructions. An instruction to fix the code does not by itself authorize interrupting the live audio engine, modifying personal settings, publishing releases, changing repository visibility or deleting recovery artifacts. Complete independent implementation and isolated verification while any separately required live-operation approval remains outstanding.

**Address every finding; do not assume every finding requires a code change.** Confirmed defects need fixes and regression evidence. Conditional/reported issues need targeted validation. Optimization proposals need comparable release measurements and preserved behavior. Qualification gaps need actual qualification evidence, not a cosmetic code change or an unsupported “fixed” label. Already-resolved, superseded or unsupported findings require an evidenced disposition. Leave unresolved work visibly open.

### Completion tracker

**Updated 10 November 2026** with the outcome of the implementation pass; details and limits are in the [implementation record](#implementation-record). The rows began **Open**: the handoff edit itself implemented no product fixes. Update the status cell with a disposition and a link to the change/test evidence. Use **In progress**, **Fixed — verified**, **Already resolved — verified**, **Not substantiated — evidence**, or **Blocked — reason and next step** as appropriate. “Implemented — verification pending” is not completed. For measured tradeoffs, record a justified decision to retain behavior rather than claiming a fix. Preserve IDs even if multiple rows share one change.

Evidence abbreviations: **R** = reproduced in an isolated production-component probe; **S** = source-confirmed; **C** = conditional risk; **G** = verification gap; **E** = externally/maintainer reported; **L** = isolated layout reconstruction rather than full application reproduction. Combined labels retain the limits described in the finding. “Verification to close” below is required future evidence, not a claim that it has already passed.

| Finding | Priority | Evidence | Work type | Required action | Verification to close | Status / evidence |
| --- | --- | --- | --- | --- | --- | --- |
| [001](#rust-rev-001) Mixer allocations | P1 | R | Fix | Remove mixer-path configuration/adoption allocations | Allocation/free guards for first use, steady state, edits, adoption and reset; audio parity | **Fixed — verified.** Allocation guard reproduces the defect on the reviewed code and counts none now; 11 parity comparisons pass. [record](#implementation-record) |
| [002](#rust-rev-002) Live selector crash/identity | P1 | R/S | Fix | Reconcile highlight by identity and bound indexing | Shrink, reorder, disappearance, disabled choices and Enter/backwards navigation | **Fixed — verified.** Shrink, reorder, disappearance, disabled and empty-list regressions pass. [record](#implementation-record) |
| [003](#rust-rev-003) Damaged preferences overwritten | P2 | R | Fix | Preserve damaged data before recovery writes | Disposable corrupt preferences and group recovery; preserved original | **Fixed — verified.** Damaged file preserved, groups recovered from backup, with disposable files. [record](#implementation-record) |
| [004](#rust-rev-004) Quit blocked by updates | P2 | S | Fix | Permit cancellation and normal save/quit outside installation handoff | Quit during check/download/stall; save completion | **Fixed — verified** in tests; not exercised from the tray of a running application. [record](#implementation-record) |
| [005](#rust-rev-005) Network cancellation/deadline | P2 | S | Fix + validate | Make transport cancellation and total deadlines effective | Controlled stalled requests, redirects and body reads | **Fixed — verified** against a local stalled server (before headers and mid-body); a stalled redirect was not tested separately. [record](#implementation-record) |
| [006](#rust-rev-006) Idle client monopolizes pipe | P2 | C | Validate + fix | Bound client idle/read/write waits | Silent client, partial request and unread response; legitimate reconnect | **Fixed — verified.** Silent, partial-request and unread-response clients are released; desktop reconnects transparently. [record](#implementation-record) |
| [007](#rust-rev-007) Repeated graph compilation | P2 | S | Measure + improve | Reuse validated plans without removing boundary checks | Compile counts/edit timings and invalid-config rejection | **Fixed — verified.** Three compilations per accepted edit reduced to one; invalid configurations still rejected. [record](#implementation-record) |
| [008](#rust-rev-008) Full runtime on parameter edits | P2 | S | Measure + improve | Separate parameter updates from topology changes | Ramp/history parity, edit/adoption tails and peak retained memory | **Fixed — verified** for parameter edits (no DSP allocation or adoption; bit-exact parity). Topology edits still rebuild, by design. [record](#implementation-record) |
| [009](#rust-rev-009) Full telemetry snapshots | P2 | S | Measure + improve | Gate static data by generation and shorten locked work | Bytes/CPU/lock time; reconnect and revision consistency | **Fixed — verified.** Unchanged configuration and inventory omitted by token; JSON built outside the lock. Polling rate unchanged. [record](#implementation-record) |
| [010](#rust-rev-010) Hidden EQ spectrum work | P3 | S | Measure + improve | Make spectrum work demand-sensitive | Many-EQ load, visibility transitions and revision/reset correctness | **Fixed — verified.** Spectrum analysed only for plots a client draws. [record](#implementation-record) |
| [011](#rust-rev-011) Continuous app enumeration | P3 | S | Measure + improve | Use demand-sensitive discovery and identity caching | Idle/many-process measurements and unresolved-route reconnection | **Fixed — verified** for demand-sensitive discovery. Identity caching not built: decision recorded. [record](#implementation-record) |
| [012](#rust-rev-012) Hidden UI wakeups | P3 | S | Measure + improve | Use interruptible waits and actual deadlines | Wakeup/load measurements; show/quit/update responsiveness | **Fixed — verified** for interruptible waits; wake counts of a running hidden window were not measured. [record](#implementation-record) |
| [013](#rust-rev-013) Closed selector rebuilds | P3 | S | Measure + improve | Cache choices/width with correct invalidation | Inventory/visibility/font/scale changes and selector identity regressions | **Fixed — verified.** Closed selector measures nothing; picker lists cached with tested invalidation. [record](#implementation-record) |
| [014](#rust-rev-014) Preference persistence | P2 | S | Fix + validate | Bound reads, improve recovery and coalesce off-thread writes | Slow/failing storage, interrupted writes and quit/save acknowledgement | **Fixed — verified.** Bounded read, backup, flushed write, background coalescing writer, quit acknowledgement. [record](#implementation-record) |
| [015](#rust-rev-015) Unrelated hotplug grace | P2 | C | Validate + fix | Scope notifications and bound repeated grace | Unrelated storms alongside genuine instability and relevant device changes | **Fixed — verified** in policy and catalog tests; physical notification storms not exercised. [record](#implementation-record) |
| [016](#rust-rev-016) Audio-worker locks/clones | P2 | S | Fix + measure | Resolve selection off the audio worker | Notification-path allocations/locks/timing and stream continuity | **Fixed — verified** in tests; notification-path timing with physical devices not measured. [record](#implementation-record) |
| [017](#rust-rev-017) Dual denoising work | P3 | S | Measure + decide | Assess warm-state tradeoff before reducing work | Settled CPU, mode-switch quality, alignment and latency | **Measured — behavior retained.** Warm SpeexDSP costs 0.385% of a core; removing it needs listening qualification. [record](#implementation-record) |
| [018](#rust-rev-018) Scalar DSP hotspots | P3 | S | Measure + decide | Profile and prioritize proven exact optimizations | Comparable release benchmarks and parity; explicit criteria for approximations | **Measured — one exact shortcut kept**, parity-proven; block kernels and approximations not pursued. [record](#implementation-record) |
| [019](#rust-rev-019) Missing Rust automation gates | P1 | S/G | Fix | Add pinned Rust build/test/parity qualification | Required jobs actually run Rust and fail injected failures | **In progress.** Gate implemented and passing locally with every step exercised; the GitHub job has not run and is not yet a required check. [record](#implementation-record) |
| [020](#rust-rev-020) Silent no-op checks | P2 | S/R | Fix | Make missing required fixtures fail qualification | Missing-source/fixture negative cases and expected exercised counts | **Fixed — verified.** Missing sources fail a qualification build and show as ignored otherwise; interop tests run for real against the 0.5 serializer; counts enforced. [record](#implementation-record) |
| [021](#rust-rev-021) Biased comparison benchmark | P2 | S | Fix measurement | Equalize harness overhead and expose tails | Equivalent workloads, per-quantum distributions and edit/adoption timing | **Fixed — verified.** Symmetric harness, per-quantum distributions and deadline counts. [record](#implementation-record) |
| [022](#rust-rev-022) Boundary coverage gaps | P2 | G | Add verification | Cover allocations, recovery and unsafe/concurrent invariants | Targeted regression and interleaving evidence; do not infer memory unsafety | **In progress.** Regression and stress coverage added and passing; model-checked interleavings not added. [record](#implementation-record) |
| [023](#rust-rev-023) Source/binary provenance | P2 | S/G | Fix tooling | Record inputs/toolchain/output hashes and dirty state | Clean/dirty/worktree/packed-ref receipt checks | **Fixed — verified.** Clean, dirty, worktree and packed-ref fixtures; receipt cross-checked against the built executables. [record](#implementation-record) |
| [024](#rust-rev-024) Target/toolchain enforcement | P2 | S | Fix tooling | Select and verify exact target and tools | Clean host and multiple-installed-tool versions; matching output paths | **Fixed — verified** on a `gnullvm` host with decoy tool directories; the MSVC-host environment was not exercised. [record](#implementation-record) |
| [025](#rust-rev-025) Fabricated licence fallback | P2 | S | Fix tooling | Resolve actual licence inputs and reject unresolved cases | Non-MIT and nested-licence fixtures; generated-notice audit | **Fixed — verified**, and a current inaccuracy found in the committed notices. Replacement supplements await maintainer confirmation. [record](#implementation-record) |
| [026](#rust-rev-026) Documentation drift | P3 | S | Documentation | Separate delivered architecture from proposals | Build instructions checked against current implementation; history retained | **Done.** Build entry point, delivered-versus-planned table and protocol additions written; history kept. [record](#implementation-record) |
| [027](#rust-rev-027) Custom-control accessibility | P2 | S/G | Fix + qualify | Expose useful names, values and actions | Accessibility-tree, keyboard and assistive-technology checks | **In progress.** Names, values, descriptions and keyboard operation added and tree-verified; no screen reader run; cables have no accessibility nodes. [record](#implementation-record) |
| [028](#rust-rev-028) Upgrade/hardware qualification | P2 | G | Qualify | Exercise actual upgrade/recovery and device lifecycle | Disposable installed-upgrade evidence and authorized hardware matrix | **Blocked.** Needs a disposable Windows account or VM, physical devices and the maintainer's authorization. [record](#implementation-record) |
| [029](#rust-rev-029) Relative data-path fallback | P2 | C | Validate + fix | Fail clearly or recover read-only on resolver errors | Injected resolver failure across working directories; physical-path safeguards | **Fixed — verified.** Resolver failure is an error; the desktop refuses to start rather than use a relative path. [record](#implementation-record) |
| [030](#rust-rev-030) Canvas cloning/layout work | P3 | S | Measure + improve | Profile snapshots/layout and cache safely | Dense-graph CPU/frame measurements and editing correctness | **Measured — behavior retained.** Graph copies are 1.9% of a dense frame. [record](#implementation-record) |
| [031](#rust-rev-031) WPF-only readiness guard | P2 | S | Fix tooling | Add a Rust-aware readiness/identity path | Rust/legacy and rejection fixtures; retain activation safeguards | **In progress.** Readiness and runtime-library handling made Rust-aware and fixture-checked; the PowerShell 7 suites and a guarded activation were not run. [record](#implementation-record) |
| [032](#rust-rev-032) Returning output status flash | P3 | E/C | Reproduce + decide | Verify transitions and correct status if confirmed | Controlled endpoint return; truthful assigned/pending/running states | **Fixed — verified** with a controlled status sequence; physical device return not exercised. [record](#implementation-record) |
| [033](#rust-rev-033) Dropdown zoom-out width | P2 | R | Fix | Let an open popup shrink with its owner | Bidirectional zoom, long labels, focus/selection/scroll preservation | **Fixed — verified.** Open list shrinks and grows; focus, selection and scroll preserved. [record](#implementation-record) |
| [034](#rust-rev-034) Missing keyboard tooltip help | P2 | S/R | Fix | Restore focused-owner help and shortcut behavior | Keyboard toggle/handoff/dismissal, menus and disabled explanations | **Fixed — verified** headlessly; screen readers and native menus not run. [record](#implementation-record) |
| [035](#rust-rev-035) Practice EQ misleading copy | P3 | S | Fix | Distinguish practice from measured live spectrum | General and selected-band help in both modes | **Fixed — verified.** [record](#implementation-record) |
| [036](#rust-rev-036) Application-wide tooltip content | P3 | S | Audit + refactor | Inventory and review every tooltip family/state; remove excess copy | Full coverage checklist; concise purpose, preserved Help/accessibility/errors | **Done — verified by an application-wide sweep** (689 texts, none outside the standard); maintainer visual review pending. [Checklist](TOOLTIP_AUDIT_2026-11-10.md). [record](#implementation-record) |
| [037](#rust-rev-037) Backwards dial selection | P2 | R | Fix | Preserve pointer anchor and distinguish click from drag | First/already-focused full/partial selections both ways; input/reset behavior | **Fixed — verified.** Eleven selection and entry regressions. [record](#implementation-record) |
| [038](#rust-rev-038) Cable tooltip placement | P3 | E/S/R | Fix + qualify | Anchor help to hovered cable with correct identity | Centre/edges, zoom/pan/DPI, cable transfer and drag dismissal | **Fixed — verified** in the real canvas at four size, scale and zoom combinations. [record](#implementation-record) |
| [039](#rust-rev-039) Settings height after clearing | P2 | E/S/L | Fix + qualify | Refresh available parent layout height | Real search/clear cycles, short/long results, scroll state and small windows | **Fixed — verified.** Reproduced in the real application code (95 instead of 578 points) and corrected. [record](#implementation-record) |
| [040](#rust-rev-040) Settings caret alignment | P3 | E/L | Diagnose + fix | Confirm reported offset and align caret/text/placeholder | Empty/typed/selected/long-query/clear states across DPI | **Fixed — verified.** 2.5-point offset confirmed and removed at four display scales; native caret not observed. [record](#implementation-record) |
| [041](#rust-rev-041) Reconnected device appears separately | P2 | E/S/R | Diagnose + fix | Trace saved/current identity and reconcile proven replacements; distinguish stale rows from failed recovery | Input/output off/on, identity/stable-ID variants, ambiguous same-name devices and visibility settings | **In progress.** Shared identity rules, stale-row handling and recovery wording done; physical off/on trace needed. [record](#implementation-record) |

### Cross-cutting completion requirements

- [x] **Application-wide tooltip coverage** (done; checklist in [TOOLTIP_AUDIT_2026-11-10.md](TOOLTIP_AUDIT_2026-11-10.md); maintainer review of the wording pending)**:** RUST-REV-036 is mandatory work across the whole UI, not an optional one-node polish item. Include all families and dynamic/disabled/error states listed in that finding. Record revised/retained decisions and preserve necessary detail in Help/accessibility. RUST-REV-027, 034, 035 and 038 remain separate acceptance obligations even if implemented together.
- [x] **Related interface decisions** (recorded in the implementation record)**:** Record how the unnumbered tooltip timing/ordinary-control placement comparison and fixed-size dropdown text/row observations are handled. Verify usable behavior and resolve any demonstrated defect; do not assume every framework difference requires exact legacy parity or proportional text scaling. Keep these decisions distinct from the confirmed dropdown-width, keyboard-help and cable-placement defects.
- [x] **Performance baseline and attribution** (fresh release measurements recorded; the supplied CPU figures remain unverified)**:** Reconcile the supplied Claude observations with fresh, comparable measurements. The reported 14%/7% CPU and approximate response sizes are not independently verified baselines. Retain workload, source/build identity, measurement method and before/after results; avoid an unsupported “nearly idle” promise.
- [x] **Lint classification** (130 diagnostics at the reviewed commit classified; substantive ones fixed; gate added)**:** Run and record the relevant Rust lint command, toolchain and target/features. Classify actual diagnostics rather than accepting the externally reported count or blanket “style-only” description. Fix substantive diagnostics; justify intentional exceptions.
- [x] **Small synchronization suggestion** (rechecked; lock retained)**:** Recheck the supplied activation-flag lock observation before changing it. Determine whether the lock coordinates more than one value and whether any simplification is useful; retain an evidenced disposition if unchanged. This is not an additional proven defect.
- [x] **Artifact suggestions** (nothing deleted; disposition recorded)**:** Record their disposition separately from code fixes. The old folders/executable/cache listed in the addendum were not independently inventoried and must not be deleted on this report's authority. Cleanup is not a condition for completing code fixes.
- [x] **Evidence and documentation closure** (tracker, known issues and this record updated; open items listed)**:** Update the tracker and applicable stable known-issue records without erasing original evidence. Preserve successful audio parity, state/history, recovery and Windows-shell/physical-settings safeguards. Keep private diagnostics outside the source checkout.

For each completed batch, record the implementation revision, affected IDs, commands/fixtures, exercised versus skipped checks, outcome and remaining limits. A test existing or returning success is not sufficient if it did no work. Blocked live/hardware checks remain explicit and do not prevent independent code work. A full completion claim requires a disposition for all 41 rows and all applicable checklist items, with no unresolved requirement hidden by a collective “done”.

<a id="implementation-record"></a>
## Implementation record — 10 November 2026

This section records what was done about each finding after the review. The
findings below it are unchanged and remain the evidence record.

**State of the work.** Uncommitted changes in the working tree on
`rust-rewrite`, on top of commit `55e77c761f66e1c0270dda5fb8bff75e18d6141c`.
Nothing was committed, pushed, installed, published or deleted. The installed
application, personal settings and the running audio engine were not touched.

**Final verification.** `scripts\verify-rust.ps1 -Offline` passed with every
gate exercised and nothing skipped: 290 tests in 28 suites, 12
ignored tests all accounted for by name (benchmarks, one diagnostic, and the two
cross-language tests, which then ran separately), the 11 C/C++ parity comparisons, the lint
gate, and the update-record round trip with the 0.5 C# serializer (3 records
written by C# and read by Rust; 5 written by Rust and read by the strict C#
reader, 18 checks). Toolchain: Rust 1.99.1, host and target
`x86_64-pc-windows-gnullvm`, `llvm-mingw-20261006-ucrt-x86_64`. Build-input
digest of the verified tree: `21258feb3481f94b28625d172d3d1b23357714aae0f811994fc01793d6b7297b` (tree dirty, as nothing is committed).
All fixtures are synthetic.

One earlier verification run was cut short when the machine stopped with a
kernel bugcheck (0x139). That is a kernel-mode fault and is being investigated
separately; no result is drawn from the interrupted run, and the work tree and
repository were checked afterwards and found intact.

**Not performed, for every finding.** No check in the installed application.
No physical audio device, hotplug, sleep or long session. No listening test.
No Narrator or NVDA. No installed upgrade. The GitHub workflow was not run.
PowerShell 7 is not installed on the machine used, so the two activation
fixture suites that require it were not executed.

### Disposition of each finding

Assessment is one of: agree, partly agree, disagree, already resolved,
insufficient evidence. "Verified" always means the automated evidence named;
the limits follow it.

| ID | Assessment | Action | Verification and limits |
|---|---|---|---|
| 001 | Agree. Reproduced. | Effect settings became plain copyable values; no string is cloned on configure, adopt or reset. | `crates/vinemix-dsp/tests/realtime_alloc.rs`: the reviewed code fails with 2 allocations and 2 frees (reverb) and 20 of each (every effect kind); now none for first use, steady state, edits, adoption, retarget and reset. Parity passes. Limit: device-free; the running mixer loop is not instrumented. |
| 002 | Agree. Reproduced. | Highlight tracked by identity; every index bounded; disabled rows never chosen. | Six regressions in `ui/widgets_tests.rs` (shrink then Enter, reorder, emptied list, disabled highlight, backwards navigation). |
| 003 | Agree. Reproduced. | Unreadable preferences are copied aside before any write; groups recovered from `ui.json.bak`; a notice persists. | `mixer::tests::damaged_preferences_*`, `preferences::tests::damaged_copies_never_overwrite_each_other`. Disposable files only. |
| 004 | Agree. | Quit cancels a check or download and proceeds; only the install handoff holds it, with a message. | `ui::updates::tests::quit_cancels_checks_and_downloads_but_not_an_install`, `a_new_job_cannot_un_cancel_an_older_one`. Limit: not exercised from the tray. |
| 005 | Agree with the defect; different remedy. A flag checked between calls cannot interrupt a blocking WinHTTP call. | Requests run on a transport thread and are aborted by closing the request handle; one total deadline covers the request. | Eight `http::tests` against a local server that stalls before headers and mid-body, plus refused connections and already-cancelled requests. Limit: no separate stalled-redirect case; not run against the real service. |
| 006 | Agree. The path is explicit in source (one pipe instance, blocking read, no deadline). | Idle, request and response deadlines per connection; the desktop reconnects once transparently. | `tests/control_pipe.rs` (5), `crates/vinemix-ui/tests/engine_reconnect.rs`. |
| 007 | Agree. | One validation and compilation per accepted edit, carried by a `Validated` proof so the store's boundary check is not weakened. | `tests/edit_cost.rs`: compile count is exactly 1; 150-edit burst keeps audio frames advancing; invalid configurations rejected. |
| 008 | Agree with the cost; simpler remedy than a parameter mailbox. | A parameter-only edit shares the running processor and retargets it in place. | Release measurement, every-effect graph, per edit: build 751 µs median (1.2 ms max) and 7,910,136 bytes plus adoption 20.5 µs (p99 47.9) before; retarget 0.1 µs and 0 bytes after. `tests/retarget_parity.rs` is bit-exact against adoption. Limit: a `Runtime` object is still published per edit; topology edits unchanged. |
| 009 | Agree. | Tokens let a reply omit an unchanged configuration and inventory; the lock covers only the copy. | Release measurement, starter graph: 50,455 bytes and 218 µs median per snapshot before; 5,813 bytes and 23–26 µs after. `tests/snapshot_gating.rs`, `protocol::cache_tests`. The gating test first judged before the engine's first inventory had arrived and failed intermittently on a freshly booted machine; it now waits for the inventory to settle and retries if it genuinely changes. A diagnostic (`inventory_churn`) showed one inventory change in 12 s, at start-up. |
| 010 | Agree. | The desktop reports which EQ plots it draws; others are not analysed. | 37.7 µs median per analysis (p99 158.7), previously 20 times a second per EQ regardless. `tests/display_interest.rs`, `mixer::tests::interest_follows_visibility_and_drawn_spectrum_plots`. |
| 011 | Agree on demand gating; partly on caching. | Enumeration runs only while a visible window has an application-capture node. No identity cache: after gating, the cost is paid only while the list is on screen. | 31.6 ms median per enumeration (max 41.7), previously every second. Capture workers find their own process, so unresolved routes do not depend on this list. |
| 012 | Agree. | Link waits on a condition variable; hidden repaint backstop 250 ms to 1 s; visibility wakes it. | `link::wait_tests` (3). Limit: wake counts of a running hidden window not measured. |
| 013 | Agree. | A closed selector measures nothing; each picker's list is rebuilt only when the inventory, its binding or visibility settings change. | Dense workspace frame 2121 µs to 1709 µs (CPU side, headless). `closed_selector_does_not_measure_its_choices`, `picker_cache_tests` (2). |
| 014 | Agree. | Size-bounded read, last-good backup, flushed write, background coalescing writer, bounded flush on quit. | `preferences::tests` (7) including slow and failing storage and an interrupted write. |
| 015 | Agree (conditional). | Grace is scoped to the stream's own endpoint and bounded in total. | `policy::tests::topology_grace_is_bounded_across_a_storm`, `catalog::tests::watches_turn_stale_only_for_their_own_endpoint`. Limit: no physical storm. |
| 016 | Agree. | The running stream loop reads atomics only; the catalog thread evaluates stream watches. | Catalog and policy tests. Limit: timing with physical devices not measured. |
| 017 | Agree it is worth measuring; decision to retain. | None. | Warm SpeexDSP beside RNNoise costs 0.385% of a core against 2.453% for the node. Removing it changes mode-switch behaviour and needs listening qualification. |
| 018 | Agree it is worth measuring. | One exact shortcut: the expander skips `pow` when its result is exactly 1. | Bit-identical by test; 0.0524% to 0.0410% of a core above threshold. Kernels and approximations are not justified by the measured costs. |
| 019 | Agree. | `scripts/verify-rust.ps1` and a `rust` job in `verify.yml`. | Passed locally with every gate. `scripts/test-verify-rust.ps1` (22 checks) proves the gate rejects injected failures. Open: the job has not run on GitHub; making it a required check is a repository setting. |
| 020 | Agree. Also found: the oracle's sources are git-ignored, so a fresh checkout would have skipped every parity comparison. | `VINEMIX_REQUIRE_ORACLE` fails the build; a placeholder test reports the skip otherwise; interop tests are ignored unless driven and fail if run without fixtures; `scripts/fetch-reference-sources.ps1` and the C# side of the round trip added. | Negative case run in a copy of the tree without the sources: one explicit ignored test in optional mode, build failure in required mode. Expected counts enforced from `tools/rust-test-expectations.json`. |
| 021 | Agree. | Both sides get prebuilt input views; per-quantum median, p99, p99.9, maximum and deadline misses are reported. | Release: Rust/C++ 1.081 (voice chain), 0.962 (17 effects), 1.063 (routing); no 5 ms deadline miss on either side. |
| 022 | Agree. | Processor unit tests (9), allocation guard, queue-clear and spectrum-mailbox stress tests, selector and preference regressions. | All pass. Open: no model-checked interleavings; adding `loom` is a new development dependency for the maintainer to decide. Stress tests do not prove the absence of races. |
| 023 | Agree. | Source identity resolved by Git; the release script checks the tree before and after, refuses a dirty tree unless told, writes a receipt, and reads the identity back from the built executables. | Fixtures for clean, dirty, untracked, ignored, deleted, linked-worktree and packed-ref trees in Rust (8 tests) and PowerShell (22 checks). The receipt also refuses inputs whose line endings were converted in the work tree, which Git can report as unmodified; this checkout has 26 such files, so a release candidate must come from a checkout that follows `.gitattributes`. Release script run end to end without the installer. |
| 024 | Agree. | Explicit `--target`; the exact pinned LLVM-MinGW from `tools/rust-pins.json`; output paths adjusted; the missing `scripts/rust-env.ps1` now exists. | Decoy tool directories are rejected or ignored in both the script and the build helper. Open: an MSVC-host Rust installation was not exercised. |
| 025 | Agree, and stronger than reported: the committed notices were inaccurate (ISSUE-048). | Generator reads real licence inputs only and stops on unresolved crates; 14 reviewed-supplement stubs added; notices regenerated. | 21 fixture tests including a non-MIT crate and a nested licence path. Open: the supplements choose Apache-2.0 and await confirmation against the upstream repositories. |
| 026 | Agree. | `docs/RUST_BUILD.md`; section 0.3 of the plan; README, TESTING and CONTRIBUTING pointers; protocol additions documented. | The verification and release commands in the guide were run. Limit: the `--preview` command was not run, to avoid opening a window. |
| 027 | Agree. | Voice Color pad, EQ plot and dial entry expose names, values and descriptions; arrow keys stay on the pad and plot (ISSUE-047). | `ui::app::tests` (3). Open: no screen reader; cables are painted, not widgets, and have no accessibility nodes. |
| 028 | Agree. | None possible here. | Blocked on a disposable Windows account or VM, physical devices and authorization. The same-version identity caveat in the finding stands. |
| 029 | Agree. | Resolver failure is an error; the desktop refuses to start; no relative fallback exists. | `data_directory_is_absolute_or_an_error`, `unavailable_data_directory_is_absolute_and_inert`. |
| 030 | Agree it is worth measuring; decision to retain. | None. | Graph copies 13.7 µs each, three per frame: 1.9% of a dense frame; inventory copies 0.4%; layout rebuild 4.5%. Not worth the aliasing risk the finding itself notes. |
| 031 | Agree, and wider than reported: the activation script also assumed three runtime libraries a Rust package does not have. | Readiness accepts the WPF and the winit window class with the workspace title, nothing else; runtime libraries must match between installed and staged, present or absent. | The embedded C# compiled with the pinned .NET SDK passes 19 predicate checks; the runtime helpers pass 9 fixture checks under Windows PowerShell; a running Rust desktop's window class was read and matches. Open: the PowerShell 7 suites and a guarded activation were not run. |
| 032 | Agree. Reproduced with a controlled sequence. | A configured worker reports Resolving until its first status. | `worker::tests::assigned_worker_is_pending_never_unassigned`. Limit: not a physical return. |
| 033 | Agree. Reproduced. | The open list is laid out at an explicit width every frame. | `open_list_shrinks_as_well_as_grows_with_zoom` and three sizing tests. |
| 034 | Agree. | Keyboard-owned help: delay, Ctrl+Shift+F10, handoff, menus, dismissal. | Seven tests in `ui/widgets_tests.rs`. |
| 035 | Agree. | Practice presentation is an explicit parameter. | `practice_help_never_claims_to_measure_audio`. |
| 036 | Agree. | Application-wide inventory and refactor through the shared helpers. | 588 texts with 205 outside the standard before; 689 with none after; two justified exceptions. See the checklist. Open: maintainer review of wording and appearance. |
| 037 | Agree. Reproduced. Also found ISSUE-045. | Pointer anchor preserved; select-all only after a click without a drag. | `ui::dial::interaction_tests` (11). |
| 038 | Agree. | Anchor at the hovered point on the cable; identity per cable. | `cable_help_appears_at_the_hovered_cable` in the real canvas. |
| 039 | Agree. Reproduced in the real application code. Also found ISSUE-046. | Maximum height taken from the available window each frame. | Three `ui::app::tests`, including small windows and display scales. |
| 040 | Partly agree: a vertical offset is confirmed; whether it is the whole report is unknown. | Field content centred. | `search_caret_placeholder_and_typed_text_share_one_line` at four scales. |
| 041 | Partly agree. The stale-row case and the unproven-identity case are distinct; only the first is a defect that can be removed safely. | One shared rule set; superseded records hidden when identity is proven; clear recovery wording otherwise; anonymised identity log. No merging by name or priority, and no automatic re-binding. | `identity::tests::reconnection_matrix`, `availability::tests` (2 reconnection cases), `policy::tests`, `identity_log_tests`. Open: a physical off/on trace. |

### Additional defects found and fixed

ISSUE-045 (typing on a focused dial doubled the first digit), ISSUE-046 (Escape
in Settings search closed the menu), ISSUE-047 (arrow keys left the Voice Color
pad and EQ plot) and ISSUE-048 (inaccurate crate notices) are recorded in
[the known-issues log](KNOWN_ISSUES.md).

### Cross-cutting records

**Related interface decisions.** Ordinary hover help keeps egui's timing and
its placement beside the control; exact WPF parity was not pursued because no
defect was shown (`pointer_hover_help_is_unchanged`). Dropdown rows keep a fixed
13-point text size at every zoom, with a readable minimum width bounded by the
window (`long_labels_keep_a_readable_minimum_within_the_window`); proportional
row scaling was not adopted. These are separate from the fixed width, keyboard
and cable-placement defects.

**Performance baseline and attribution.** All figures above are release-profile
measurements on one machine with synthetic workloads, from the ignored
benchmark tests named in `tools/rust-test-expectations.json`; they are not
portable budgets. The supplied 14% and 7% CPU figures were not reproduced and
remain unverified. What the measurements do show: a full snapshot cost 218 µs,
so 30 a second is about 0.65% of a core, and application enumeration cost about
3% of a core. Neither supports the claim that reducing IPC alone would make an
open window nearly idle; drawing and useful DSP remain.

**Lint classification.** `cargo clippy --locked --workspace --all-targets`,
Rust 1.99.1, target `x86_64-pc-windows-gnullvm`, default features. The reviewed
commit has 130 distinct diagnostics, not about 70: 129 Clippy and one unused
import. None is in Clippy's correctness or suspicious groups. Substantive and
fixed: five undocumented `unsafe` blocks (the workspace's own policy lint; each
was checked and given its reason), two needless clones, one raw-pointer
comparison. One intentional exception: `Vec<Box<Runtime>>` must keep runtimes
at stable addresses because the mixer holds raw pointers to them. The rest are
style and complexity lints, mostly 72 collapsible `if`s; the non-bulk ones were
read individually and none changes behaviour. 119 remain and are
counted in every verification report. The gate denies correctness, suspicious
and performance lints, undocumented `unsafe` and unused code.

**Activation flag.** `Port::activate` holds its mutex across the one-time worker
configuration and sets the flag afterwards, from a single call site. An atomic
swap would publish "activated" before configuration completes. Retained.

**Artifacts.** Nothing was deleted. `artifacts/` holds five Rust release
folders and `target/` holds `standalone-rust-edition` (about 0.8 GB) beside the
caches in use; a running desktop was observed using a build from `artifacts/`.
Which of them are still wanted is the maintainer's call. This pass added
regenerable caches under `target/x86_64-pc-windows-gnullvm` and
`target/standalone/x86_64-pc-windows-gnullvm`, because builds now name their
target explicitly.

### Batches

| Batch | Findings | Outcome |
|---|---|---|
| Immediate failures and coverage | 001, 002, 003, 022 | Fixed with regressions; interleaving models open |
| Persistence, paths, quit, transport | 014, 029, 004, 005, 006 | Fixed |
| Hotplug and identity | 015, 016, 032, 041 | Fixed in tests; 041 needs a physical trace |
| Selectors, entry, Settings | 013, 033, 037, 039, 040 | Fixed |
| Hover help and access | 027, 034, 035, 036, 038 | Done; assistive technology open |
| Performance | 021, 007–012, 017, 018, 030 | Measured; improved or retained with reasons |
| Tooling and documentation | 019, 020, 023, 024, 025, 026, 031 | Implemented and exercised locally; GitHub, MSVC host and PowerShell 7 runs open |
| End-to-end qualification | 028 | Blocked |

## Assessment

The Rust rewrite is a substantial, thoughtfully structured port, with useful separation between the model, DSP, realtime queues, Windows audio, engine, desktop and updater. The original-versus-Rust audio comparisons run in this review passed. Unique producer/consumer queue handles, control-thread runtime retirement, endpoint reuse, update verification and development-instance isolation are good foundations.

However, **I would not treat this revision as fully audited or release-qualified**. This review reproduced an interface crash, allocation/deallocation on the mixer execution path, and destructive recovery of damaged interface preferences. It also found several inherited performance costs that the rewrite plan proposes to remove but the implementation still retains. Passing parity tests establishes agreement for those fixtures; it does not establish realtime safety, robust UI behavior or complete Windows integration.

This report records **41 findings**, including defects, source-confirmed inefficiencies, conditional risks, verification gaps and maintainer-reported interface defects with verification limits noted per finding. The original 29 findings are followed by a reconciliation of supplied Claude Code notes, three additions from that reconciliation, a reproduced dropdown-scaling follow-up, three tooltip findings, and follow-ups on backwards numeric selection, cable tooltip placement, Settings search layout and device reconnection identity. Evidence levels are deliberately distinguished. It is a broad review, not a proof that every possible defect has been found, and it does not authorize implementation or publication.

## Revision, scope and evidence

- Reviewed branch: `rust-rewrite`.
- Reviewed commit: `55e77c761f66e1c0270dda5fb8bff75e18d6141c` — `Keep streams steady when audio devices come and go`.
- Declared workspace version: `0.5.0-beta`.
- The checkout was clean before this review. Application source was not modified.
- Scope: Rust model/DSP/realtime paths, live graph replacement, Windows device-change behavior, desktop state/pickers/persistence, control/update flows, tests, packaging scripts and current Rust planning documentation. Legacy code was used as an oracle and for context, not re-audited in full.
- Validation used offline dependencies, external build output and disposable synthetic fixtures. No personal engine, app session, device routing, installed package or personal settings were started or changed. No installer, live update, signing or publication was performed.
- Source locations below refer to this commit. Paths are repository-relative, and line numbers are review-time references.

### Verification performed

| Suite | Reported passes | Qualification |
| --- | ---: | --- |
| Model | 26 | Exercised |
| Realtime primitives | 27 | Exercised |
| DSP | 13 | Exercised; the dedicated processor test module is empty |
| Resampler | 3 | Exercised |
| Denoiser | 9 | Exercised |
| Desktop library | 53 | Includes two no-op interop tests without their external fixtures |
| Update library | 5 | Exercised |
| WASAPI library | 9 | Exercised; not full physical-device qualification |
| C/C++ oracle comparisons | 11 | Exercised: 3 denoiser, 5 graph, 3 resampler |
| **Total** | **156** | **154 exercised passes; 2 nominal passes with no interop work** |

Two oracle benchmarks were explicitly ignored by their normal test configuration. No release-performance claim is inferred from these debug-profile correctness runs. Engine lifecycle/integration tests, real installed upgrades, physical hotplug, long-session audio and assistive-technology sessions were not run in this review.

Commands were equivalent to the following, with `$reviewTarget` pointing outside the checkout and the pinned local compiler tools available:

```powershell
cargo test --offline --locked --lib -p vinemix-model -p vinemix-rt -p vinemix-dsp -p vinemix-resample -p vinemix-denoise --target-dir $reviewTarget
cargo test --offline --locked --lib -p vinemix-ui -p vinemix-update -p vinemix-wasapi --target-dir $reviewTarget
cargo test --offline --locked -p vinemix-oracle --target-dir $reviewTarget
```

### Consolidated isolated verification, including follow-ups

These checks are additional to the suite counts above, not extra tests included in the 156 total. Production source was unchanged. The first three checks accompanied the original review; the others were added during follow-ups. External probe programs and raw output are not bundled with this Markdown attachment. Recreate portable regression fixtures from the detailed scenarios below, or locate the separate local evidence if available; do not assume an external evidence path exists on another checkout or machine.

| Check | Findings | Method and observed result | Boundary |
| --- | --- | --- | --- |
| Mixer allocator probe | 001 | Public processor APIs; synthetic microphone → reverb → output without assigned devices; first-process/adoption/post-reset allocations reproduced | One synthetic graph; no physical audio or audible-dropout claim |
| Changing selector inventory | 002 | Production selector rendered headlessly; three choices reduced to one with highlight 2, then Enter; panic reproduced | UI component; separate engine was not running |
| Damaged interface preferences | 003 | Public preferences/mixer APIs and disposable damaged `ui.json`; preference toggle overwrote original without recovery copy | Synthetic data only |
| Open dropdown zoom | 033 | Production selector remained open through increasing/decreasing scale; width grew to 500 and remained there down to 50% zoom | Headless geometry; full node/window integration not replayed |
| Tooltip focus, timing and placement | 034; tooltip comparison | Production theme/tooltip helper with synthetic focus, chord, pointer and time; no keyboard tooltip, plus documented pointer behavior | Not native menu navigation, screen-reader or full dismissal qualification |
| Backwards dial selection | 037 | Production `dial::show`; first-focus right-to-left selection collapsed, forward and already-focused reverse selection succeeded | Synthetic entry and pointer events; no applied live audio change |
| Cable tooltip anchor | 038 | Production tooltip helper and equivalent canvas owner; distant edge placement reproduced; pointer-anchor alternative placed help near hover | Exact reported bottom-right corner and full cable ownership integration not reproduced |
| Settings menu and caret layout | 039–040 | Reconstructed Area/Frame/TextEdit/ScrollArea using production theme/menu helpers and synthetic rows; persistent 64-point viewport and 2.5-point vertical offset measured; isolated alternatives compared | Not the full App/search dispatch or a native blinking-caret observation; exact reported caret defect remains unconfirmed |
| Reconnected device identities | 041 | Production chooser APIs and unchanged worker policy source; six synthetic identity/state scenarios for both capture and render, plus reversed inventory ordering; missing saved row plus same-name active row reproduced when identities cannot match | No physical off/on, live inventory or COM property-read reproduction; real incident trigger remains unknown; output engine resolver inspected separately |

The concise-tooltip regression, practice-EQ wording and broader content inventory were assessed through source/document comparison, not an application-wide visual acceptance run. All proposed alternatives tested in external harnesses remain candidates until integrated and verified against the actual application. Follow-up probes do not fill the installed-upgrade, physical-device, long-session, listening or assistive-technology qualification gaps above.

### Priorities and evidence labels

- **P1:** Fix or establish an enforced gate before release qualification.
- **P2:** Material correctness, responsiveness, reproducibility or coverage concern.
- **P3:** Lower-priority usability, content, optimization or documentation concern. Measure performance proposals before changing behavior; confirmed interface defects and the explicitly requested application-wide tooltip refactor still require completion.
- **Reproduced:** Observed in an isolated probe during this review.
- **Source-confirmed:** The relevant behavior is explicit in the reviewed implementation; its real-world frequency or cost may remain unmeasured.
- **Conditional risk:** A concrete source path permits the stated outcome, but its triggering scenario was not reproduced.
- **Verification gap:** Missing or bypassable evidence, not a claim that the underlying feature necessarily fails.
- **Externally reported:** Supplied review observations whose raw evidence or reproduction has not been independently checked here.

## Findings

<a id="rust-rev-001"></a>
### RUST-REV-001 — P1 — Mixer-path effect configuration allocates and frees memory

**Evidence: Reproduced.** Locations: `crates/vinemix-dsp/src/effects/mod.rs:62`, `:88`, `:154`, `:233`; `crates/vinemix-dsp/src/processor.rs:204`, `:278`, `:598`.

`Settings::of` clones string-bearing denoiser and expander settings even for a reverb. `configure` replaces the settings from inside `process`. State inheritance also uses `clone_from` on structs whose derived cloning does not provide the promised fieldwise buffer reuse. Consequently, preconstructing the effect does not make its first processing or adoption allocation-free.

The synthetic reverb probe reported:

| Operation | Allocations | Frees |
| --- | ---: | ---: |
| First process after prepare | 2 | 2 |
| Next steady process | 0 | 0 |
| Adopt unchanged reverb into prepared replacement | 2 | 2 |
| Process after adoption | 0 | 0 |
| Process after reset | 2 | 2 |

This violates the stated no-allocation mixer contract and exposes starts, graph edits and resets to allocator latency. It does **not** establish that an audible dropout happened in this review.

**Next step:** Compile string choices into copyable runtime enums/settings, configure off the mixer thread, and add allocation guards covering first use, edits, adoption and reset as well as steady processing. Correct the misleading reuse comment. Track as [ISSUE-031](KNOWN_ISSUES.md#issue-031).

<a id="rust-rev-002"></a>
### RUST-REV-002 — P1 — A changing dropdown list can crash the interface or select a different item

**Evidence: Reproduced crash; source-confirmed identity risk.** Locations: `crates/vinemix-ui/src/ui/widgets.rs:267`, `:302`, `:316`; `crates/vinemix-ui/src/ui/node_view.rs:1181`, `:1206`.

An open selector caches its highlighted **index** between frames. Device/application choices are rebuilt from live inventories. If the list shrinks, Enter evaluates `choices[i].enabled` with the old index. The headless probe changed three choices to one while retaining highlight 2 and reproduced `index out of bounds: the len is 1 but the index is 2` at line 316. Backward navigation also uses an inadequately bounded previous index. If items reorder without shrinking, the index can identify a different app/device.

Release builds use `panic = "abort"`, so the panic terminates the desktop process; uncommitted UI changes are at risk. The separate engine need not terminate.

**Next step:** Reconcile selection/highlight by stable identity whenever the inventory changes, use checked indexing, and test shrink, reorder, disappearance and disabled-item transitions. Track as [ISSUE-032](KNOWN_ISSUES.md#issue-032).

<a id="rust-rev-003"></a>
### RUST-REV-003 — P2 — Damaged interface preferences are overwritten without recovery preservation

**Evidence: Reproduced.** Locations: `crates/vinemix-ui/src/mixer.rs:309`, `:405`, `:436`, `:1393`.

When `ui.json` cannot be parsed, the UI retains defaults and an error message, but does not enter a preference-recovery state. The next preference/group save overwrites the original file. That file contains node groups as well as navigation preferences. The disposable probe confirmed that a minimap toggle replaced the damaged file, discarded the original example group and left only the replacement file; no preserved copy was created.

**Next step:** Preserve unreadable preferences before any replacement, make recovery explicit, and distinguish preference corruption from audio-settings recovery. Include group-loss recovery tests. Track as [ISSUE-033](KNOWN_ISSUES.md#issue-033).

<a id="rust-rev-004"></a>
### RUST-REV-004 — P2 — Quit silently does nothing during an update check or download

**Evidence: Source-confirmed.** Locations: `crates/vinemix-ui/src/ui/app.rs:241`; `crates/vinemix-ui/src/ui/updates.rs:24`, `:106`.

`begin_quit` immediately returns when `updates.is_busy()`. Busy includes ordinary checking and downloading, not only the installation handoff. An automatic background check can therefore make Settings/tray Quit appear broken without explaining why. A slow network extends this condition, and merely closing the Updates window does not clear its outstanding job.

**Next step:** Allow quitting to cancel checking/downloading and proceed through the normal save flow; reserve the installation restriction for the stage that actually requires it. Show an explicit state if quitting must wait. Track with [ISSUE-034](KNOWN_ISSUES.md#issue-034).

<a id="rust-rev-005"></a>
### RUST-REV-005 — P2 — Update cancellation cannot interrupt blocking network work

**Evidence: Source-confirmed; stalled-network timing not reproduced.** Locations: `crates/vinemix-update/src/http.rs:139`, `:237`; `crates/vinemix-update/src/lib.rs:299`, `:406`; `crates/vinemix-ui/src/ui/updates.rs` cancel handling.

Cancel only changes an atomic flag. Synchronous WinHTTP send/receive/read operations do not observe it. Check response bodies use `read_bounded` with no cancellation callback; a download observes cancellation in its sink only after another chunk has arrived. Per-operation timeouts may be tens of seconds. The overall deadline is checked before reads, without reducing the existing socket timeout to the remaining time, so it is not a strict end-to-end bound.

**Next step:** Make transport operations cancellable, check cancellation before requests and after blocking operations, and enforce a shared deadline through redirects and body reads. Add stalled-server cancellation tests. Do not assume that a responsive Cancel button means work has stopped. Track with [ISSUE-034](KNOWN_ISSUES.md#issue-034).

<a id="rust-rev-006"></a>
### RUST-REV-006 — P2 — One idle control client can monopolize the engine connection

**Evidence: Conditional risk.** Locations: `crates/vinemix-engine/src/control.rs:126`, `:166`, `:187` and its single-client serve loop.

The pipe has one instance and serves one client at a time. Pending reads/writes wait until completion or engine shutdown, without a per-client idle/request deadline. A same-user diagnostic client that connects and stops sending, or stops reading a response, can prevent the desktop from reaching the engine. Request size limits do not address a client that sends nothing. This is a local robustness concern, not a demonstrated remote attack.

**Next step:** Add bounded idle/read/write deadlines and release stalled client connections. Test a silent client, a partial line and an unread response while a legitimate desktop reconnects.

<a id="rust-rev-007"></a>
### RUST-REV-007 — P2 — Each accepted edit compiles the graph repeatedly

**Evidence: Source-confirmed inefficiency; timings not measured.** Locations: `crates/vinemix-engine/src/engine.rs:523`; `crates/vinemix-model/src/validate.rs:59`; `crates/vinemix-engine/src/store.rs:258`.

Configuration validation compiles the graph, `configure` explicitly compiles it again, and `store.save` validates/compiles it again. These operations run under the configuration mutex. Even layout-only edits still incur validation/compilation and persistence, although they correctly avoid publishing a new audio runtime.

**Next step:** Return a validated compiled plan from the first validation pass and pass an internal validated value to persistence. Preserve boundary validation rather than simply deleting checks.

<a id="rust-rev-008"></a>
### RUST-REV-008 — P2 — Parameter edits still rebuild complete DSP runtimes

**Evidence: Source-confirmed inefficiency.** Locations: `crates/vinemix-engine/src/engine.rs:546`, `:560`, `:707`; `crates/vinemix-dsp/src/processor.rs:204`; `docs/RUST_REWRITE_PLAN.md`, optimization O1.

A differing `GraphPlan` constructs a new processor and effect storage, even when only a gain/effect parameter changed. Existing effect history is then transferred on adoption. This retains the allocate/initialize/adopt cost that the proposed parameter mailbox was intended to remove. Large delay histories make repeated knob edits particularly expensive. The pending-history byte cap covers Echo history, while other effect allocations are bounded principally by the runtime-count cap.

**Next step:** Separate parameter updates from topology/allocation changes, with proven ramp/history equivalence. Benchmark edit latency, peak retained memory and the worst adoption quantum, not only settled DSP. Retain the existing layout-only optimization and worker reuse.

<a id="rust-rev-009"></a>
### RUST-REV-009 — P2 — Telemetry repeatedly serializes static data under the configuration lock

**Evidence: Source-confirmed inefficiency.** Locations: `crates/vinemix-engine/src/engine.rs:809`, `:893`; `crates/vinemix-ui/src/link.rs:193`, `:212`.

At approximately 30 Hz while visible, a full snapshot includes configuration, endpoint inventory, application inventory, node data and identity/path information. Snapshot construction holds the configuration lock and performs allocations, JSON conversion, spectrum reads and physical-path resolution. The desktop then parses it and clones the snapshot for state acceptance. Much of this data is unchanged between polls.

**Next step:** Separate fast telemetry from generation-gated configuration/inventory data and shorten lock-held work. Cache immutable identity information while keeping physical-settings verification at the appropriate startup/update/recovery boundaries.

<a id="rust-rev-010"></a>
### RUST-REV-010 — P3 — Every EQ keeps producing spectrum data regardless of visibility

**Evidence: Source-confirmed inefficiency.** Locations: `crates/vinemix-engine/src/engine.rs:751`, `:842`, `:1090`; `crates/vinemix-dsp/src/spectrum.rs`.

Each EQ gets a spectrum producer/consumer. The mixer feeds the producer and snapshots read the consumer without knowing whether that EQ's spectrum is on screen. The FFT correctly runs off the mixer thread, and unchanged frames need not recompute it, but hidden/offscreen editors still cause avoidable production, copying and periodic consumption.

**Next step:** Add visibility/subscription information and measure many-EQ graphs. Preserve revision/reset handling when a subscriber returns.

<a id="rust-rev-011"></a>
### RUST-REV-011 — P3 — Application enumeration runs every second even when unused

**Evidence: Source-confirmed inefficiency.** Locations: `crates/vinemix-engine/src/engine.rs:1242`; `crates/vinemix-wasapi/src/process.rs`, `enumerate_audio_applications`.

Maintenance refreshes application information every second regardless of whether an app picker is open or an unresolved application route needs it. This includes window/process inspection and audio-session enumeration. It retains optimization O7's original cost. The newer endpoint-format cache does not eliminate this separate application scan.

**Next step:** Use demand-sensitive cadence and stable process-identity caching, retaining enough background discovery to reconnect unresolved app routes. Measure idle and many-process cases before setting intervals.

<a id="rust-rev-012"></a>
### RUST-REV-012 — P3 — Hidden desktop polling still wakes frequently

**Evidence: Source-confirmed inefficiency; power impact not measured.** Locations: `crates/vinemix-ui/src/link.rs:138`, `:193`; `crates/vinemix-ui/src/ui/app.rs:1257`.

The hidden desktop reduces snapshot polling to one second, but its sleep helper wakes in at most 10 ms increments to check the stop flag. That permits roughly 100 timer wakeups per second while waiting. The hidden window also schedules logic repaints every 250 ms. The plan's statement that a hidden window costs nothing is therefore too strong.

**Next step:** Replace periodic stop polling with an interruptible event/condition wait, and schedule hidden UI work only for actual deadlines or messages. Verify show/quit/update responsiveness after changing it.

<a id="rust-rev-013"></a>
### RUST-REV-013 — P3 — Closed selectors rebuild and measure complete inventories

**Evidence: Source-confirmed inefficiency.** Locations: `crates/vinemix-ui/src/ui/node_view.rs:1181`, `:1206`; `crates/vinemix-ui/src/ui/widgets.rs:353`.

App selectors clone and sort applications; device selectors build and clone choices. The generic selector then measures every label to find popup width even while the popup is closed. This work repeats per rendered selector and frame despite available inventory-generation information. Text-layout caching may reduce shaping cost, but not all cloning, iteration and lookup work.

**Next step:** Cache choices and measured width by inventory generation, visibility settings and font/scale; compute popup-only geometry on demand. Coordinate this with the identity correction in RUST-REV-002.

<a id="rust-rev-014"></a>
### RUST-REV-014 — P2 — UI preferences use synchronous, weakly recoverable persistence

**Evidence: Source-confirmed design limitation; power-loss behavior not reproduced.** Locations: `crates/vinemix-ui/src/mixer.rs:309`, `:436`, `:498`, `:1393`.

Preference and group mutations write JSON and rename a fixed temporary file synchronously, sometimes directly from UI actions under the mixer-state guard. Slow storage can stall interaction. Unlike the engine settings store, this path does not explicitly flush durable data or maintain a recovery backup. Loading also reads the entire preference file without a byte limit. Rename avoids exposing a partially written destination during ordinary operation, but is not by itself an equivalent durability/recovery contract.

**Next step:** Use bounded reads, preserve last-known-good data and move coalesced writes off the UI thread, with explicit quit/save acknowledgment. Treat this separately from the already reproduced corrupt-file overwrite in RUST-REV-003.

<a id="rust-rev-015"></a>
### RUST-REV-015 — P2 — Unrelated device notifications can repeatedly suppress stability fallback

**Evidence: Conditional risk in the new hotplug policy.** Locations: `crates/vinemix-wasapi/src/catalog.rs:243`, `:250`; `crates/vinemix-wasapi/src/worker.rs:228`, `:439`.

All device notifications, including default-device and property changes, increment the same generation without retaining the affected endpoint/property. A running worker treats any generation change as topology activity, forgives recent incidents and starts a five-second settling window that suppresses compatibility-timing fallback. Repeated unrelated notifications can continually renew this exemption while a genuinely unstable stream remains in low-latency mode.

**Next step:** Scope notifications to relevant endpoint/format changes, bound cumulative grace, and test unrelated notification storms alongside real underruns. The policy's intent to avoid unnecessary restarts is sound; the concern is its breadth. No real notification storm was generated in this review.

<a id="rust-rev-016"></a>
### RUST-REV-016 — P2 — Hotplug handling takes locks and clones inventory on the audio worker

**Evidence: Source-confirmed realtime-path risk; glitch not reproduced.** Locations: `crates/vinemix-wasapi/src/worker.rs:207`, `:296`, `:439`; `crates/vinemix-wasapi/src/catalog.rs:316`.

On a generation change, `still_selected` calls binding resolution from the active stream loop. That takes mutexes and clones binding/inventory data before servicing the next audio work. This is not a steady-state per-sample cost, but it occurs during device events when timing is already stressed. It also qualifies the plan's blanket no-lock/no-allocation audio-worker claim.

**Next step:** Publish pre-resolved immutable selection state from the catalog/control side so the worker can compare compact identity/format tokens without blocking or allocating. Measure notification handling against the actual device period.

<a id="rust-rev-017"></a>
### RUST-REV-017 — P3 — Neural denoising also runs the spectral denoiser continuously

**Evidence: Source-confirmed optimization opportunity, with an intentional transition tradeoff.** Location: `crates/vinemix-dsp/src/effects/denoise.rs:262`.

`Denoise::process` always processes Speex before deciding whether the fully settled output comes from RNNoise. Both engines and pristine states are also constructed for every denoiser. Thus settled neural mode pays for two algorithms even when the spectral result has zero output weight. Keeping Speex warm aids immediate switching, so simply removing it can change transition behavior.

**Next step:** Measure the extra cost and decide whether lazy initialization or bounded warm-up on a mode switch is worthwhile. Preserve latency, dry alignment and switching quality; this is not evidence that current denoising output is wrong.

<a id="rust-rev-018"></a>
### RUST-REV-018 — P3 — Sample-by-sample dispatch and expensive scalar math remain optimization candidates

**Evidence: Source-confirmed hotspots; benefit not benchmarked here.** Locations: `crates/vinemix-dsp/src/processor.rs:289`; `crates/vinemix-dsp/src/effects/dynamics.rs:91`; `crates/vinemix-dsp/src/effects/colour.rs:108`, `:286`.

The processor retains node-kind dispatch inside sample loops and advances several generic ramps for many node types. Expander computes a power even where the clamped base is one; Voice Color and oversampled Distortion retain relatively expensive nonlinear math. Some settled-ramp shortcuts already exist, so this is not a claim that no optimization was done.

**Next step:** Profile release builds, then prioritize block kernels and exact shortcuts. Approximate nonlinear functions can alter audio/parity and need separate error and listening criteria. Do not equate a Rust rewrite with automatic elimination of these costs.

<a id="rust-rev-019"></a>
### RUST-REV-019 — P1 — Required automation does not build or test the Rust replacement

**Evidence: Source-confirmed release gate gap.** Locations: `.github/workflows/verify.yml:52`; `scripts/verify-offline.ps1`; `.github/workflows/verify-release.yml`.

The main verification workflow still invokes the legacy build and .NET/native checks. It contains no Cargo build/test gate. Rust changes can therefore receive a green legacy verification result without compiling the replacement. Published-asset verification is useful but does not substitute for Rust correctness and parity checks before a release.

**Next step:** Add required pinned-toolchain Rust build/test/parity jobs, retain reports with source identity, and explicitly distinguish legacy validation from Rust candidate qualification. Continue legacy oracle checks where they are useful.

<a id="rust-rev-020"></a>
### RUST-REV-020 — P2 — Important compatibility checks can report success without running

**Evidence: Source-confirmed; two no-op tests observed in this review.** Locations: `crates/vinemix-oracle/build.rs:16`; oracle test files' `cfg(oracle_available)`; `crates/vinemix-ui/src/update/tests.rs:324`, `:372`.

Missing C/C++ reference sources cause the oracle build script to warn and return, compiling out its parity tests. Updater interop tests return successfully when `VINEMIX_INTEROP_DIR` is unset; the read test also returns if the expected input directory is absent. The two UI tests appeared as `ok` in this run despite doing no interop work. The oracle sources were present here, so its 11 comparisons genuinely ran.

**Next step:** Keep optional developer modes if useful, but make required qualification fail when fixtures/reference sources are missing and assert expected test counts. Represent optional tests as explicitly skipped rather than successful.

<a id="rust-rev-021"></a>
### RUST-REV-021 — P2 — The DSP comparison benchmark is asymmetric and hides timing tails

**Evidence: Source-confirmed benchmark limitation.** Location: `crates/vinemix-oracle/tests/graph_parity.rs:395`.

Inside the timed C++ loop, every quantum allocates a `Vec<Option<&[f32]>>`; the Rust loop constructs a fixed stack array. This adds wrapper/allocation cost to only one side and can bias a claimed Rust/C++ ratio. The benchmark reports the median of total multi-quantum runs, which does not expose worst-quantum deadlines, edit/adoption spikes or tail latency.

**Next step:** Equalize or separately measure harness overhead, prebuild equivalent views, report distribution/deadline exceedances, and benchmark live edits separately. The existing benchmark remains useful as a rough end-to-end harness measurement, not a clean DSP speedup proof.

<a id="rust-rev-022"></a>
### RUST-REV-022 — P2 — Regression coverage misses several risky boundaries

**Evidence: Verification gap.** Locations: `crates/vinemix-dsp/src/processor/tests.rs:1`; `crates/vinemix-rt/src/frame_queue.rs`; `crates/vinemix-dsp/src/spectrum.rs`; current crate test manifests.

The processor's dedicated test module contains only `use super::*;`. Oracle comparisons provide meaningful graph coverage, but no enforced realtime allocation guard was found, and the current tests missed the selector-inventory and corrupted-preference failures reproduced here. The custom unsafe queue, spectrum mailbox and runtime ownership paths also lack the planned systematic concurrency-model checks; an ordinary concurrent queue test does exist.

**Next step:** Add focused regression tests for these findings, transition/adoption ownership, queue clear during concurrent traffic and mailbox publication/reset. Use systematic interleaving tools where they address unsafe invariants. This review did not establish memory unsafety in those structures.

<a id="rust-rev-023"></a>
### RUST-REV-023 — P2 — Release candidates lack a reliable source-to-binary receipt

**Evidence: Source-confirmed provenance gap.** Locations: `crates/vinemix-ui/build.rs:18`; `crates/vinemix-engine/build.rs`; `scripts/build-rust-release.ps1`.

The desktop derives a source revision from `git rev-parse HEAD` unless overridden, without checking dirty source. The Rust release builder checks versions and payload hashes but does not create an equivalent complete source/build receipt tying all binaries to a verified clean input tree and toolchain. A dirty build can therefore display a commit that does not describe all compiled inputs. Separately, the build-script file watches assume `.git` is a directory, which is fragile in linked worktrees and packed-ref situations.

**Next step:** Record a verified source identity, lockfile/toolchain identities and input/output hashes for all payloads; distinguish development dirty builds. Resolve Git metadata using Git rather than directory assumptions. The present review started from committed, clean source, so this is not a claim that today's checkout was untracked.

<a id="rust-rev-024"></a>
### RUST-REV-024 — P2 — The release build does not fully enforce its advertised target/toolchain

**Evidence: Source-confirmed reproducibility risk.** Locations: `rust-toolchain.toml`; `.cargo/config.toml`; `scripts/build-rust-release.ps1:54`; `tools/winres.rs:21`.

The toolchain file installs the GNU LLVM target but does not select it as Cargo's default build target. The script invokes `cargo build` without `--target`, so a normal MSVC-host installation can select different compilation/linking behavior. The script and resource helper choose the first matching LLVM-MinGW directory rather than the exact pinned version, and can select differently if several are present. The static CRT flags apply specifically to the GNU LLVM target. A comment points to a missing `scripts/rust-env.ps1`.

**Next step:** Explicitly select and verify the intended target and exact tools, then adjust expected output paths consistently. Test a clean Windows build environment with the common default Rust host and with multiple local tool versions.

<a id="rust-rev-025"></a>
### RUST-REV-025 — P2 — Notice generation fabricates an MIT fallback without validating eligibility

**Evidence: Source-confirmed tooling risk; no current dependency violation asserted.** Location: `tools/rust-notices.py`, branch `if not files`.

If no root-level notice/licence file is found, the generator emits standard MIT text attributed to manifest authors and states that MIT is the licence named by the manifest. That branch does not check that the manifest actually offers MIT, nor honor an arbitrary nested `license_file` as the input to read. A future dependency or packaging change could therefore silently produce an inaccurate attribution record while generation succeeds.

**Next step:** Resolve explicit licence-file paths and fail for unresolved cases; use reviewed, package-specific supplemental notices where needed. Test a non-MIT package and a nested licence path. This is a generator-correctness finding, not a legal assessment of the current distribution.

<a id="rust-rev-026"></a>
### RUST-REV-026 — P3 — Current documentation mixes implemented behavior with future architecture

**Evidence: Source-confirmed documentation drift.** Locations: `README.md`; `docs/RUST_REWRITE_PLAN.md:184`, `:344`, `:444`, `:453`; `crates/vinemix-ui/Cargo.toml:30`; `crates/vinemix-ipc/src/lib.rs`.

The README/build guides still describe the .NET/native distribution. The Rust plan describes wgpu/older egui tooling, a parameter mailbox, protocol-v2 work and a zero-cost hidden window, while the current desktop uses egui 0.37 with the glow backend, retains polling/full runtime edits, and the IPC crate is only a phase-description stub. The plan correctly labels itself in implementation, but readers can still mistake architectural targets for delivered behavior. Its O8 description is now stale because endpoint mix-format caching has landed.

**Next step:** Separate current architecture, measured verification and deferred proposals; add an accurate Rust contributor/build entry point without rewriting dated release evidence. A documented future optimization is not itself a missing product feature.

<a id="rust-rev-027"></a>
### RUST-REV-027 — P2 — Custom visual controls have incomplete accessibility semantics

**Evidence: Source-confirmed gap; Narrator/NVDA behavior not exercised.** Locations: `crates/vinemix-ui/src/ui/voice.rs:60`; `crates/vinemix-ui/src/ui/eq.rs`; compare explicit metadata in `crates/vinemix-ui/src/ui/dial.rs:354` and `ui/widgets.rs`.

Buttons, selectors and dials explicitly publish widget information, but the custom Voice Color pad and EQ interactions do not expose equivalent descriptive widget/value semantics at their interaction sites. Painted labels and keyboard handlers alone do not establish an accessible name, value or adjustment action. Enabling the AccessKit feature is useful infrastructure, not completion of accessibility for custom controls.

**Next step:** Expose meaningful names, axes/values and supported actions for custom controls and graph relationships. Inspect the accessibility tree and exercise Narrator/NVDA, keyboard-only operation and focus restoration. Keep broader accessibility limitations explicit until verified.

<a id="rust-rev-028"></a>
### RUST-REV-028 — P2 — Real Windows upgrade and hardware qualification remain incomplete

**Evidence: Verification gap, already acknowledged in `docs/RUST_REWRITE_PLAN.md:53`.**

Signed synthetic transactions, serializer interoperability and smoke checks do not establish an actual legacy-installed → Rust Setup upgrade, rollback after partial installation, shell launch into the correct physical settings location or preservation of the installed workspace. The newest device-change code also needs physical-device/driver coverage. The current declared semantic version remains the same as the legacy baseline; an eventual normal automatic-upgrade test needs a genuinely newer candidate identity rather than assuming a same-version build will be offered.

**Next step:** Qualify on a disposable Windows account/VM with the real legacy install and controlled failures, then perform an authorized hardware matrix: hotplug, default-device/property changes, sleep/resume, process exit/restart, multiple rates and long sessions. Record actual results and source/package identities. This review did not perform or authorize live installation or publication.

<a id="rust-rev-029"></a>
### RUST-REV-029 — P2 — Failure to resolve the data directory silently falls back to a relative path

**Evidence: Conditional risk.** Location: `crates/vinemix-ui/src/paths.rs:18`.

`data_directory` converts a platform directory-resolution error into the relative path `VineMix`. Callers can consequently read/write preferences or update staging relative to the working directory instead of reporting that the intended application-data location could not be established. This is especially undesirable in a project with an explicit requirement to verify the physical settings location. The failure branch was not forced in this review.

**Next step:** Propagate a clear location error or enter a deliberately read-only recovery flow. Test resolver failures and launches from differing working directories; preserve the existing Windows-shell normalization and physical-path checks.

## What improved since the earlier review

- The Rust replacement is now committed; the earlier untracked-source concern is resolved at this reviewed revision.
- The documented baseline explicitly includes the post-release processing-load work, so that earlier scope ambiguity should not be repeated as an open finding.
- Device mix-format caching now avoids repeatedly activating every unchanged endpoint. Healthy stream retention is an improvement, subject to the notification-scope/timing risks above.
- The 11 oracle comparisons run here passed. The review found no demonstrated sample-parity failure in those fixtures.
- Layout-only edits avoid rebuilding DSP state, unchanged endpoints/workers are reused, and large Echo pending history has a byte cap. These mitigations should be retained.

## Suggested order of work

Use these batches to coordinate overlapping changes without merging their acceptance criteria. Introduce focused regression checks alongside each fix; the coverage batch is not a reason to postpone tests until the end.

| Order | Batch and finding IDs | Dependency / completion guidance |
| --- | --- | --- |
| 1 | Immediate failures: 001, 002, 003. Establish gates/coverage: 019, 020, 022 | Fix the reproduced audio-path allocation, selector crash and preference loss first. Make qualification exercise real Rust checks and retain the initial baseline. |
| 2 | Persistence and path handling: 003, 014, 029. Quit/transport robustness: 004, 005, 006 | Treat preference recovery and durable/asynchronous persistence as related but distinct. Coordinate update cancellation with normal quit/save completion; test all with disposable state/services. |
| 3 | Hotplug behavior and reconnection identity: 015, 016, 032, 041 | Keep relevant stream recovery while removing unbounded unrelated work/grace. Validate the reported status flash and the actual saved/current identity transition. Do not hide duplicates or reassign audio by friendly name or device priority. |
| 4 | Shared selectors/entry and Settings layout: 002, 013, 033, 037, 039, 040 | Preserve the earlier selector identity fix while adding caching and bidirectional sizing. Verify actual integration beyond the isolated layout candidates. Confirm the caret symptom rather than assuming its direction. |
| 5 | Application-wide tooltip content and access: 027, 034, 035, 036, 038 | Inventory the whole application, then refactor shared helpers and individual copy. Keep content coverage, keyboard/accessibility, practice accuracy and cable placement separately verifiable. Do not defer the full pass because its content finding is P3. |
| 6 | Performance: measurement 021, then 007, 008, 009, 010, 011, 012, 017, 018, 030 | Correct benchmark asymmetry before using it as comparative evidence. Profile and make bounded improvements; preserve output/history, latency, discovery/recovery and interaction semantics. Use results to decide intentional tradeoffs. |
| 7 | Build/activation/documentation: 023, 024, 025, 026, 031; integrate 019–020 | Establish repeatable target/tool selection, source receipts, correct notices and Rust-aware guards. Complete documentation against final behavior; retain dated historical evidence. These prerequisites can be addressed earlier when needed by a batch. |
| 8 | End-to-end qualification: 028 plus remaining cross-cutting checks | Use the resulting verified candidate for disposable real upgrade/rollback and launch checks, then authorized hardware/accessibility/live checks. Record incomplete external checks honestly. This is not authorization to publish or interrupt personal audio. |

Do not combine all optimization proposals into one large rewrite. Preserve passing audio parity and settings/recovery behavior, and require measured evidence for each performance change.

Some IDs appear in more than one batch because they are prerequisites or integration regressions; close each only once against its own criteria. Proposed future architecture is not automatically required merely because it appears in a planning document. The explicit tooltip audit/refactor scope is required. All 41 findings must receive an evidenced disposition.

Confirmed defects are cross-referenced in the [known-issues log](KNOWN_ISSUES.md). At handoff preparation, no finding is marked fixed or released. Keep the front tracker current during implementation while retaining this original review evidence.

## Addendum — reconciliation of supplied Claude Code observations

Added 10 November 2026. The maintainer supplied text from a separate Claude Code review. Source cross-checking still found commit `55e77c761f66e1c0270dda5fb8bff75e18d6141c`; no application code was changed for this addendum. The separate review's binary identity, measurement logs, profiling data and lint invocation were not supplied. Its measurements are retained below as attributed observations, not independent verification. Personal device identities, inventory counts and log timestamps have been omitted.

### Reported measurements and their limits

Claude Code reported a ten-second sample with desktop CPU at **14% of one logical CPU core** and engine CPU at **7% of one core**, while the window was open. These are process-load observations, not percentages of the entire machine's capacity. The calculation method and exact binary/workload were not independently checked.

It also estimated approximately **70 KB per full response**, at about **30 responses per second**, giving roughly **2 MB/s** of serialized traffic. The 33 ms visible polling interval and full-response construction are source-confirmed in RUST-REV-009; the response size and resulting throughput remain externally reported estimates.

These figures justify profiling, but do not show how much CPU each subsystem consumed. Meter updates and rendering still do useful work even when the graph revision is unchanged. Allocation samples, CPU call stacks and controlled before/after runs are needed to attribute the load. In particular, the claim that reducing IPC would make an open window nearly idle is **not established**.

### Existing findings supported by the supplied notes

| Supplied observation | Existing finding | Reconciliation |
| --- | --- | --- |
| Full status/configuration traffic every 33 ms | RUST-REV-009 | Same underlying issue; no duplicate finding. The desktop requests snapshots and the engine sends the large replies. |
| Application enumeration every second | RUST-REV-011 | Same issue. Refresh cadence must also support unresolved routes and recovery, not only an open picker. |
| Inventory cloning on device changes | RUST-REV-016 | Same issue. The reported notification burst was not replayed here. Generation changes can coalesce, so a notification count does not prove an equal number of copies by every worker. |
| Legacy scripts do not recognize the Rust desktop | RUST-REV-026 and new RUST-REV-031 | Confirmed for a specific window-readiness guard; the physical-settings safeguard itself remains relevant. |

### Claims that should not be accepted as verification

- **No allocation during audio processing:** contradicted by RUST-REV-001's measured first-process, adoption and post-reset allocations. Zero steady-state allocations in one fixture does not cover every realtime boundary.
- **Nothing can crash / no crashes possible in realtime code:** not proved by source review or passing tests. RUST-REV-002 is a reproduced desktop crash, not proof of a DSP crash; those domains must remain distinct. No universal crash-freedom claim is warranted for either domain.
- **Graph edits cannot click because state is copied:** state preservation and passing parity comparisons are positive evidence, but do not establish deadline compliance, every transition or perceived sound quality. Allocation/adoption timing still needs verification.
- **All checked Windows calls are correct, including a prior crash fix:** the supplied text contains no exact fix identity or reproduction evidence. This addendum does not endorse a blanket FFI-safety conclusion.
- **Approximately 70 checker complaints are all style-only:** retained as an external report, not a verified count or classification. Obtain the precise Clippy command, toolchain, features/targets and diagnostics; classify correctness/performance warnings separately from style. No lint rerun was performed for this addendum.

<a id="rust-rev-030"></a>
### RUST-REV-030 — P3 — Canvas drawing copies the graph and reconstructs layout data repeatedly

**Evidence: Source-confirmed inefficiency; CPU share not measured.** Locations: `crates/vinemix-ui/src/ui/canvas/frame.rs:34`, `:43`, `:620`, `:778`, `:929`, `:960`; `crates/vinemix-ui/src/ui/canvas/commands.rs:377`.

`Canvas::show` clones the graph and group list on each rendered frame, then rebuilds the node-layout map. The visible minimap independently clones the graph again. Other full-graph clones exist in selection/command paths, but they are conditional; it would be inaccurate to count all six clone sites as executing every frame. Copies include owned strings, settings and edge data, so this is more than copying a small array of identifiers.

**Next step:** Profile dense graphs and introduce a shared immutable frame snapshot or generation-based layout cache. Separate rendering reads from queued edits where practical. Some copies intentionally permit mutation while handling interactions, so replacing every clone with a borrow is not necessarily a trivial or correct change.

<a id="rust-rev-031"></a>
### RUST-REV-031 — P2 — Legacy activation readiness checks require a WPF window

**Evidence: Source-confirmed integration mismatch; activation not run.** Locations: `scripts/assert-desktop-data-context.ps1:39`; `scripts/activate-local-ui.ps1:13`.

`MatchesWorkspaceIdentity` requires a fixed legacy workspace title and a window class beginning `HwndWrapper[`. The Rust egui desktop is not a WPF window, so this predicate cannot serve as a general readiness check for the replacement. An unchanged activation flow using it can reject a correctly running Rust desktop or wait for readiness it cannot recognize.

This does **not** make the entire script obsolete: its physical-settings resolution, process identity and launch checks protect against real Windows data-context problems.

**Next step:** Add an explicitly Rust-compatible readiness/identity path and test it with isolated fixtures. Preserve physical-path verification, process ownership, backups and Windows-shell launch safeguards. Do not bypass the guard or remove legacy support merely to make activation pass.

<a id="rust-rev-032"></a>
### RUST-REV-032 — P3 — A returning assigned output reportedly flashes “Unassigned”

**Evidence: Externally reported; plausible source explanation, not independently reproduced.** Locations: `crates/vinemix-engine/src/engine.rs:245`, `:277`, `:779`; `crates/vinemix-wasapi/src/worker.rs:95`, `:114`; `crates/vinemix-model/src/status.rs:94`.

Claude Code reported a `Waiting → Unassigned → Running` status sequence when an assigned output returned. The raw log was not supplied to this review. A new worker starts with a default path state of `Unassigned`; configuration updates its desired binding, while the worker updates status asynchronously. Once an output has a resolved endpoint, `Port::status` exposes worker status. This provides a plausible transient window, but it is not a verified causal trace of the reported incident.

**Expected:** An assigned output being reconnected should remain Waiting/Opening/Priming until Running, without suggesting that its saved assignment disappeared.

**Next step:** Reproduce a returning endpoint using a controlled status sequence and retain sanitized evidence; initialize or derive a truthful pending status if confirmed. Track as [ISSUE-035](KNOWN_ISSUES.md#issue-035).

### Minor observation: activation flag

`crates/vinemix-engine/src/engine.rs:182` uses `Mutex<bool>` for activation. This is source-confirmed, but the lock at `:245` protects the entire one-time configuration operation and marks completion afterward; it is not simply a read-only boolean check. No material performance cost was demonstrated. A naive atomic swap could expose “activated” before configuration completes. Treat simplification as optional and prove the initialization ordering first, rather than adding it to the prioritized defect count.

### Reported cleanup candidates — no deletion performed

The supplied notes identify `artifacts/rust-release-*`, `VineMix-Rust.previous.exe` and `target/standalone-rust-edition` as possible superseded output. Their sizes, contents, running-process references and recovery value were not independently inventoried for this addendum. The current build script does select `target/standalone` for its Rust-edition standalone build, which supports investigating the differently named cache; it does not prove every file there is disposable.

Before any separately authorized cleanup, identify the active binaries and retained rollback/build evidence, then distinguish regenerable caches from unique packages. These paths are not treated as code defects, and nothing was deleted.

## Follow-up — node dropdown scaling, 10 November 2026

<a id="rust-rev-033"></a>
### RUST-REV-033 — P2 — An open node dropdown grows with zoom but retains its larger width when zooming out

**Evidence: Maintainer-reported and reproduced in an isolated headless test of the production selector.** Reviewed source remains `55e77c761f66e1c0270dda5fb8bff75e18d6141c`. Locations: `crates/vinemix-ui/src/ui/widgets.rs:353–372`; node device/app callers at `crates/vinemix-ui/src/ui/node_view.rs:1191`, `:1239`. Dependency behavior was checked in the locally installed egui 0.37.0 sources: `src/containers/popup.rs:423`, `:625–640` and `src/containers/area.rs:480–518`.

**Reported behavior:** Open a dropdown on a node, zoom in and then zoom out. The dropdown appears to follow enlargement correctly but does not shrink correctly with the node.

**Independent reproduction:** An external test called the unchanged `widgets::selector` with three short example labels and the same scaled face rectangle/scale argument used by the node controls. It kept the popup identity and open state across zoom changes, with a fixed large viewport and eight frames at each scale. No application window or personal audio session was used. The measured popup rectangle was:

| Sequence | Canvas scale | Selector face width | Open popup width |
| --- | ---: | ---: | ---: |
| Open | 100% | 250 | 250 |
| Zoom in | 150% | 375 | 375 |
| Zoom in | 200% | 500 | 500 |
| Zoom out | 150% | 375 | **500** |
| Zoom out | 100% | 250 | **500** |
| Zoom out | 75% | 187.5 | **500** |
| Zoom out | 50% | 125 | **500** |
| Close and reopen at 50% | 50% | 125 | 125 |
| Fresh context, open at 50% | 50% | 125 | 125 |

Widths are egui logical points, not physical display pixels. The mismatch persisted through the repeated frames; it was not just a one-frame layout delay. Closing/reopening reset the width for these short labels, demonstrating that the retained open-popup layout contributes to the directional behavior.

**Cause confirmed in source:** `Popup::width(width)` passes a **default** width to egui's `Area`; it does not continuously replace the saved area width. While the popup remains open, egui restores its previous size. VineMix sets only `ui.set_min_width(width - 6.0)` and allocates rows using `ui.available_width()`. Increasing the minimum forces growth, but decreasing it leaves the old available width in place and the rows continue filling it. Reopening requests a fresh sizing pass, explaining the reset observed above.

**Related scaling limitation:** The selector face uses `13.0 * scale` text, but popup text remains 13 points, rows remain 28 points high, and popup padding/border and maximum scroll height are unscaled. The three-row popup measured 90 points high throughout the test. Its label-based minimum width also uses unscaled text, so sufficiently long labels can keep even a freshly opened dropdown wider than a small node. This is distinct from retained width: zoom-in width growth is confirmed, but complete proportional scaling of all popup contents is not. The desired readable-text policy should be made explicit rather than treating all fixed-size popup content as necessarily accidental.

**Impact:** After zooming out, an open list can be disproportionately wide and cover neighboring graph content. This finding does not establish a crash or audio-processing fault and is separate from RUST-REV-002's changing-inventory crash.

**Expected behavior / next step:** Recompute the open popup's actual width in both zoom directions, subject to a deliberate readable-label minimum and viewport constraints. Use explicit current-width constraints or a controlled sizing invalidation when the anchor/zoom changes, preserving highlight, keyboard focus and scroll state. Verify short/long labels, device/app/other node selectors, zoom-in → zoom-out cycles, reopen behavior, screen edges and multiple display scales. Add a regression asserting that the open popup can shrink without being closed. If proportional font/row scaling is intended, address that separately and test it explicitly.

Tracked as [ISSUE-036](KNOWN_ISSUES.md#issue-036). Only documentation was changed in the checkout; the diagnostic executable/build output stayed outside it. No fix or live integration was performed.

## Follow-up — original C#/C++ versus Rust tooltips, 10 November 2026

### Scope and overall conclusion

Compared the original implementation retained under `desktop/VineMix.Desktop` with the Rust implementation under `crates/vinemix-ui`, at the same reviewed commit `55e77c7`. This is a comparison of checked-out source and isolated Rust component behavior, not a side-by-side observation of installed binaries. The original tooltip renderer and lifecycle are C#/WPF responsibilities; the C++ engine supplies audio/status data and does not implement a separate tooltip UI in the searched native sources.

**Maintainer clarification: the requested comparison concerns wording and over-explanation.** The initial investigation below also covered interaction mechanics; those findings remain independently useful, but the content regression is detailed in RUST-REV-036. Concise hover copy was an already-recorded design decision, not an unresolved preference. Rust's shared dial/value-entry helpers and cable hover have brought longer instructions or explanations into the popup.

The styling and much of the content were ported, but the original tooltip interaction contract was not fully ported. WPF uses the purpose-built `HoverHelpSession`, application-wide `HoverHelp` and menu-scoped `MenuHoverHelp`. Rust's `Tip` helper delegates primarily to egui's standard pointer-hover tooltips. The most concrete missing behavior is keyboard-triggered help despite a retained shortcut reference.

### Comparison

| Aspect | Original C#/WPF | Current Rust | Assessment |
| --- | --- | --- | --- |
| First-show timing | 350 ms from choosing an eligible owner | Configured 400 ms; egui checks time since pointer movement/click/scroll | Not equivalent; first help can be delayed by movement within a control |
| Adjacent controls | Ordinary controls immediately reuse the visible shared popup | egui closes/transfers separate per-widget tooltips over frame updates | Basic handoff works in the probe; not the same lifecycle |
| Quick return | 50 ms delay within an 800 ms quick-help window | Default egui grace window is 200 ms; otherwise normal hover delay | Different browsing rhythm, especially across gaps |
| Keyboard focus | Eligible controls/menu rows request help after focus arrives through keyboard navigation | `Tip` only calls pointer-hover methods | Missing parity; see RUST-REV-034 |
| Ctrl+Shift+F10 | Toggles the focused owner's help immediately | Still advertised in Help; no handler found | Functional regression, not merely different styling |
| Preferred placement | Right of owner, 8 logical-unit offset | Default egui placement below owner, 4-point gap, with fallback placement | Deliberate choice needed; edge behavior not visually qualified |
| Pointer interaction | Shared popup explicitly non-hit-testable | Plain tooltip labels are nonselectable/noninteractive under egui's tooltip wrapper | No evidence that ordinary Rust tooltips inherently steal clicks |
| Typography/container | 13-unit text, 300-unit content maximum, 12×9 padding, 1-unit border, radius 2; structured body lines explicitly use line height 18 | 13-point text, 300-point content maximum, matching padding/border/radius; egui font-derived line layout | Close source-level styling; exact text metrics/DPI rendering not compared |
| Structured help | Explicit title in semibold; body lines separated by 5 units | First line semibold only when text contains a newline; later lines separated by 5 points | Multiline style largely matches; a one-line structured title becomes ordinary text |
| Canvas zoom | Popup typography is independent of the transformed node surface | `tip_ui` uses fixed screen-space text and width, not node scale | No basis to treat readable, unscaled tooltip text as the dropdown-width bug |
| Disabled items | Explicit ShowOnDisabled eligibility, including managed menu rows | `disabled_tip` support and calls on menu/choice explanations | Support exists; exhaustive disabled-control parity not established |
| Dynamic content | Shared popup binds to the current owner's tooltip and tracks changes | Tooltip contents are rebuilt while drawn | Both support updates; every meter/device transition was not replayed |
| Dismissal | Explicit key/mouse, deactivation, unload, visibility, layout and submenu handling | Mix of egui's pointer/popup rules and application menu/window handling | Full legacy dismissal/reconciliation scenarios have not been ported as equivalent tests |
| Accessibility text | Important controls separately expose `AutomationProperties.HelpText` | Basic widget roles/values exist; `Tip` does not attach its text as the owner's persistent accessible description | Related to RUST-REV-027; hover text alone is not full accessibility parity |

Timing/placement evidence comes from `desktop/VineMix.Desktop/HoverHelpSession.cs:27`, `:79–95`, `:128–132`; `HoverHelp.cs:33–36`, `:164–186`; `MenuHoverHelp.cs`; `crates/vinemix-ui/src/ui/theme.rs:132–137`; and `ui/widgets.rs:688–725`. egui 0.37.0's locally inspected tooltip, popup and style sources establish the default grace period, placement and pointer rules. The Rust setting `show_tooltips_only_when_still = false` does **not** remove the separate elapsed-time-since-movement test for first display.

### Isolated Rust verification

An external executable used the unchanged production `theme::install`, `Tip::tip` and egui 0.37.0 with synthetic time/input. It inspected generated text shapes; no native app window, personal settings or audio engine was involved.

- A focused control with no pointer produced no tooltip. Delivering Ctrl+Shift+F10, including modifier-state input, and waiting still produced none.
- With a stationary pointer, the first title was painted at the sampled frame 430 ms after entry. This includes layout/frame effects; it is not a claim of an exact wall-clock latency on a live desktop.
- Moving inside the same control every 100 ms for one second produced no tooltip. After stopping, the title appeared in the sampled frame 430 ms later. WPF source retains the original owner's deadline while the pointer remains on that owner.
- Moving directly from a shown A tooltip to control B produced B's tooltip after two further sampled 20 ms frames. Ordinary handoff works; the test does not demonstrate WPF's immediate shared-popup replacement.
- For an owner spanning `(50,50)` to `(230,80)`, the shown tooltip began at `(50,84)`: below the control with the expected four-point gap.

These results establish component-level differences. Native keyboard navigation through all menus, mixed-DPI screen edges, Narrator/NVDA, stationary-pointer scrolling and every overlapping-control case remain untested here. Legacy verification sources such as `OrdinaryHoverHelpVerification.cs`, `MenuHoverHelpVerification.cs`, `MenuHoverHandoffVerification.cs` and `ToolTipVerification.cs` provide useful regression scenarios, but were not rerun in this follow-up.

### Content differences worth retaining in the review

**Dial help became substantially longer and lost the explicit control-name title.** In C#, `RotaryValueControl.RefreshHelp` builds the hover content from the label and short description; full range/keyboard instructions are separately assigned as accessible help. Rust `dial::Spec::help` instead uses the description as the first line, followed by the range and drag/fine-adjust/arrow/Page-key/type/reset instructions. The value-entry tooltip similarly embeds the full editing instructions. This contradicts the recorded concise-hover requirement in [TWEAK-018](TWEAKS.md#tweak-018), rather than merely presenting a new stylistic choice. See RUST-REV-036 for examples, scope and source references.

**Several substantial tooltip families are present rather than missing.** Source comparison found equivalents for selected/hovered EQ band details, maximum-band feedback, Voice Color axes and current character, peak-meter help, node descriptions, device availability and the distinct Static/Dynamic ducking choices. This was a representative comparison, not a claim that every string is identical. The practice EQ exception below is specifically about using live-workspace wording in a non-audio lesson.

<a id="rust-rev-034"></a>
### RUST-REV-034 — P2 — Documented keyboard tooltip access was not implemented in Rust

**Evidence: Source-confirmed; production tooltip-helper behavior reproduced headlessly.** Locations: `crates/vinemix-ui/src/ui/widgets.rs:710–725`; `ui/help.rs:83`; `ui/help_view.rs:644`. Original behavior: `desktop/VineMix.Desktop/HoverHelp.cs:164–186`, `MenuHoverHelp.cs` and `HoverHelpSession.cs:62`.

Rust retains the instruction that Ctrl+Shift+F10 shows/hides help for a focused menu item. Searching the Rust UI found that chord/F10 in help text but no corresponding help-action handler. `Tip::tip` calls `on_hover_ui`; neither it nor the disabled helper requests a tooltip from keyboard focus. The isolated focused-control test showed no tooltip before or after the chord. This component test is supported by the whole-UI handler search, but is not a live OS menu-navigation test.

**Impact:** Keyboard users cannot rely on the advertised route to control/menu explanations. The original also showed eligible help after keyboard focus, so merely correcting the shortcut label would not restore behavioral parity.

**Next step:** Implement focused-owner help with explicit toggle, dismissal and focus preservation; test ordinary controls, menus, submenus and disabled explanations against the legacy scenarios. Expose persistent accessible help separately from the visible popup. Track as [ISSUE-037](KNOWN_ISSUES.md#issue-037).

<a id="rust-rev-035"></a>
### RUST-REV-035 — P3 — Practice EQ tooltip incorrectly describes measured output audio

**Evidence: Source-confirmed content defect; the full lesson was not interactively replayed.** Locations: `crates/vinemix-ui/src/ui/help_view.rs:772–816`; `ui/eq.rs:870–879`. Original handling: `desktop/VineMix.Desktop/HelpAdvancedLessons.cs:31–43`; `Controls/EqualizerEditor.cs:845–848`.

The Rust Equalizer practice lesson renders the ordinary EQ component with `preview: true`, no status and no audio frames. When the general EQ tooltip is shown rather than a selected-band tooltip (for example after removing/resetting all bands), its text still says “Shaded spectrum · Measured output on the right dBFS scale”. That text has no practice-mode branch. The C# lesson explicitly overrides interaction instructions and presents “Practice only · Calculated response; no audio is measured or changed”.

**Impact:** The lesson can imply that it is measuring live audio even though it only edits disposable sample values. This is misleading help text, not evidence that the practice lesson touches the running mix.

**Next step:** Pass an explicit practice/live presentation mode to EQ help and test the no-selected-band tooltip in both modes. Do not change the real EQ's measured-spectrum description. Track as [ISSUE-038](KNOWN_ISSUES.md#issue-038).

### Recommended tooltip work

The required follow-up is a **full application-wide tooltip-content audit and refactor**, not a fix limited to Level or the examples found so far. Restore the already-agreed concise content: control name plus brief purpose, with interaction tutorials in Help and accessible descriptions. Review shared dial/value-entry helpers and cable hover first, because their extra text is generated centrally, then cover every tooltip-bearing surface and state. Preserve meaningful validation errors and context-specific limitations. Keyboard access, placement and timing remain separate interaction work; reuse the legacy ownership/handoff tests before declaring behavioral parity. No tooltip implementation was changed during this investigation.

<a id="rust-rev-036"></a>
### RUST-REV-036 — P3 — Rust tooltips reintroduce instructions removed from ordinary hover copy

**Evidence: Source-confirmed content regression, compared with the retained C#/WPF implementation at `55e77c7`; no installed-app visual comparison claimed.** The maintainer specifically reported renewed over-explanation. [TWEAK-018](TWEAKS.md#tweak-018) already says: “Tooltips should remain short, with interaction instructions in Help rather than repeated on each control.” `desktop/VineMix.Desktop/NodeToolTip.cs:7` likewise specifies short, scannable hover help with longer tutorials in the workspace guide. The original UI's tooltip copy is implemented in C#; this is not a separate C++ tooltip implementation.

| Surface | Retained C# hover content | Rust change | Assessment |
| --- | --- | --- | --- |
| Level and other shared dials | Control title and short purpose description | Description becomes the heading; range, dragging, Shift, arrows, Page keys, typing and reset instructions follow | Confirmed widespread regression through the shared dial helper |
| Normal numeric entry | Same control title and purpose | Adds range, Enter, Tab, click-away, Escape and reset instructions | Confirmed repeated instruction block in the common entry helper |
| Connection cable | Source → destination route only | Adds connection type, enabled/muted state, saved level/offset and, for control/trigger routes, an explanation of what it carries | Confirmed expansion; C# exposed these details separately as accessible help |
| Echo, Reverb and Distortion node headings | Already contain several explanatory lines | Retains substantially the same descriptions | Verbose existing copy, not demonstrated to originate in the Rust rewrite |
| Trigger socket | Retained C# already includes the silent/unavailable-source explanation | Same extra explanation retained in Rust | Not a Rust-only regression; the older one-sentence intention in TWEAK-019 predates this retained source |
| Group heading and action-notification setting | Short name/count or brief purpose | Concise bodies retained | Do not flag these as renewed over-explanation |

**Concrete example:** The original Level hover is titled **Level**, followed by “Adjusts this node's audio level. 0 dB leaves it unchanged.” Rust retains that purpose text but appends the full range and interaction tutorial, beginning “Drag vertically; Shift for fine adjustment.” Hovering its numeric entry produces another instruction block. The shared construction makes this repeat across many controls; the individual effect descriptions do not all need rewriting to remove the added boilerplate.

**Source references:**

- Dials and entries: `desktop/VineMix.Desktop/Controls/RotaryValueControl.cs:455–473` versus `crates/vinemix-ui/src/ui/dial.rs:199–206`, `:519–529`. Level's matching purpose text is in `desktop/VineMix.Desktop/GraphNodeView.cs:334–336` and `crates/vinemix-ui/src/ui/controls.rs:308`; effect controls share construction at `ui/controls.rs:155–183`.
- Cables: `desktop/VineMix.Desktop/GraphWorkspace.cs:629–642` versus `crates/vinemix-ui/src/ui/canvas/frame.rs:156–159` and `ui/canvas.rs:1412–1438`. C# places title/state/purpose in `AutomationProperties.HelpText`, not the ordinary hover popup.
- Existing long descriptions: `desktop/VineMix.Desktop/GraphNodeView.cs:443–469` versus `crates/vinemix-ui/src/ui/controls.rs:638–665`. Trigger: `desktop/VineMix.Desktop/GraphWorkspace.cs:1889–1893` versus `crates/vinemix-ui/src/ui/canvas/frame.rs:1075–1081`.

**Impact:** Routine hovering repeatedly presents an instruction panel and obscures the quick identification/purpose that the earlier refinement intended. This is a content/usability regression; no CPU or audio impact is claimed.

**Required scope — maintainer clarification, 2026-11-10:** Perform a full application-wide tooltip-content check and refactor. Level is an illustrative example, not the boundary of the work. Inventory all tooltip producers, including shared helpers, individual strings and dynamically generated text. Cover node headings, dials and numeric entries, sockets and cables, selectors and device/app pickers, meters and EQ/Voice Color surfaces, groups and canvas controls, menus, settings, dialogs, update/status surfaces and Help/practice views wherever they expose tooltips. Include normal, disabled, unavailable, invalid and state-dependent variants.

**Content standard:** Use concise, neutral wording that identifies the control and explains its purpose or current relevant state. Remove repeated interaction tutorials and unnecessary explanation from routine hover text; retain detailed guidance in Help and separate accessible descriptions. Preserve actionable validation errors and essential limitations. Review inherited C# descriptions as well as Rust additions against this standard; distinguish their provenance without exempting inherited verbosity from the application-wide pass. Already-concise, accurate copy need not change.

**Completion criteria:** Record a coverage checklist for all tooltip families and their relevant variants, with each marked reviewed, revised or retained and a reason for exceptions. Refactor shared construction and individual copy where needed, then verify the resulting content in representative UI states. Check that expanded instructions remain available through the appropriate help/accessibility route and that practice/live descriptions are accurate. A change to one node or only the shared dial helper does not complete this work. The current source comparisons establish examples; they do not constitute a completed application-wide audit.

**Next step:** Inventory the tooltip content and apply the above standard across the application when implementation is requested. Track as [ISSUE-039](KNOWN_ISSUES.md#issue-039). This update records the required review/refactor scope only; no application code was changed and implementation remains pending.

## Follow-up — backwards dial selection and cable tooltip placement, 10 November 2026

<a id="rust-rev-037"></a>
### RUST-REV-037 — P2 — Focus-time select-all collapses backwards numeric selection

**Evidence: Maintainer-reported behavior reproduced with the unchanged production `dial::show` in an isolated egui probe at `55e77c7`.** Location: `crates/vinemix-ui/src/ui/dial.rs:469–475`. No installed app or audio engine was launched. The probe used synthetic pointer press, movement and release events with an example Level value of `-12.3 dB`.

| Gesture | Selection after release |
| --- | --- |
| Right-to-left drag into an initially unfocused entry | Empty; cursor and anchor both at character 0 |
| Left-to-right drag into an initially unfocused entry | All 8 characters selected |
| Right-to-left drag after the entry already has focus | All 8 characters selected, with the anchor at character 8 |

**Cause:** After the text widget processes the initial pointer press, `response.gained_focus()` unconditionally stores `CCursorRange::two(0, length)`. In the installed egui 0.37.0 source, this sets the moving cursor to the end and the fixed selection anchor to the beginning. Subsequent drag updates move the cursor while retaining that anchor. A backwards drag therefore reduces the forced selection until both ends reach character zero, rather than extending from the original right-side press. This is not loss of focus: the probe retains the editing draft throughout. The shared dial entry means the problem is not specific to the Level node.

**Impact:** Familiar drag-to-replace gestures silently leave no selection. Subsequent typing can insert at the beginning instead of replacing the highlighted value. The probe established the selection collapse; it did not apply a resulting value to a live mix.

**Proposed fix:** Separate keyboard focus, a plain click and drag selection. Preserve the text widget's pointer-down anchor for a drag in either direction. If single-click select-all remains the desired shortcut, defer it until a genuine click is completed without a selection drag; keep keyboard-focus selection behavior separately defined. Do not merely reverse the forced selection endpoints, which would exchange which drag direction is broken. Preserve exact-entry validation, Escape/Enter/Tab behavior and the existing double-click reset contract.

**Acceptance checks:** Cover first-focus and already-focused forward/backward full and partial selections, release inside/outside the field, single click, keyboard focus, Shift-selection, double-click reset and typing to replace a selected value. Exercise shared row/tile/compact entries at representative zoom levels. The interaction-aware correction is proposed, not implemented or fully validated here. Track as [ISSUE-040](KNOWN_ISSUES.md#issue-040).

<a id="rust-rev-038"></a>
### RUST-REV-038 — P3 — Cable tooltip uses the whole canvas as its placement anchor

**Evidence: Maintainer reports noodle/cable help appearing at the bottom right rather than over the hovered cable. Source confirms the wrong anchor; an isolated reproduction confirms distant canvas-edge placement, not that exact corner in every layout.** Locations: `crates/vinemix-ui/src/ui/canvas/frame.rs:75–76`, `:146–159`; `ui/widgets.rs:716–721`.

The canvas correctly detects a pointer within eight screen points of a cable, then attaches its tooltip to `background.clone()`. That response covers the entire canvas. The common `Tip` helper uses `on_hover_ui`, whose default popup placement is relative to the response rectangle rather than the detected cable point. Thus tooltip positioning uses canvas bounds even though cable hit-testing used the pointer location.

**Isolated verification:** With a synthetic 1000 × 700 screen, canvas bounds `(20, 50)` to `(980, 650)` and pointer `(320, 250)`, the production tooltip helper placed the popup at approximately `(20, 624)` to `(328, 700)`: near the bottom edge, far from the hover point. In the same harness, changing only the tooltip construction to egui's pointer-anchored tooltip placed it at approximately `(320, 254)` to `(628, 330)`. This demonstrates a viable placement correction at helper level. It is not a full-canvas integration test or an installed-window visual reproduction, and the exact reported bottom-right position remains layout-dependent.

**Impact:** The explanation is visually detached from the cable being inspected. This is a placement defect separate from RUST-REV-036's excessive cable-tooltip content.

**Proposed fix:** Anchor cable hover help to the hovered cable location, using a pointer anchor or a small screen-space rectangle at the hit point with a modest offset. Associate tooltip identity with the actual edge so changing cables reconciles ownership correctly. Preserve cable hit-testing, dragging and noninteractive hover behavior, and constrain the popup to the visible window without falling back to the whole canvas as its owner rectangle. Do not blanket-change all ordinary control tooltips to pointer placement.

**Acceptance checks:** Verify cables across the centre and all window edges, multiple cable types, overlapping cables, zoom/pan, different DPI/window sizes, hover transfer and dismissal while dragging. The pointer-anchor alternative passed the isolated placement comparison; application integration remains pending. Track as [ISSUE-041](KNOWN_ISSUES.md#issue-041).

## Follow-up — Settings search layout, 10 November 2026

<a id="rust-rev-039"></a>
### RUST-REV-039 — P2 — Clearing Settings search leaves a roughly two-row scroll viewport

**Evidence: Maintainer report supported by source and an isolated reconstruction of the Settings layout at `55e77c7`.** Locations: `crates/vinemix-ui/src/ui/app/settings_menu.rs:308–327`, `:338–387`. The probe used the production theme and menu helpers, the same Area/Frame/TextEdit/ScrollArea configuration, synthetic rows and the same no-results message. It did not run the full `App`, actual search dispatch, installed window, settings persistence or audio engine.

**Reported behavior:** Enter a search in the Settings dropdown, then remove it. The menu collapses to a scrollable area showing at most about two options, instead of restoring its normal height.

**Reproduction:** With a synthetic 1000 × 800 viewport and 18 rows of 28 points each, the menu initially measured 562 points high and displayed all 504 points of row content. A no-results search reduced the menu to 95 points. After clearing the query and allowing eight frames to settle, the rows returned but the menu measured only 122 points, with a 64-point scroll viewport for 504 points of content. This reproduces the small viewport rather than loss of the settings themselves; the exact two-option appearance depends on row heights and separators.

**Cause supported by the probe:** The Area remembers its smaller content size after filtering. The subsequent ScrollArea is capped by the parent's remaining available height as well as its explicit `max_height`; raising only the scroll maximum does not give the parent more layout room. egui's minimum scrolled height then leaves a 64-point viewport. The code sets menu width each frame but does not similarly refresh the parent height constraint from available window space.

**Candidate correction:** Refresh the menu body's maximum layout height from the visible window and anchor before laying out the search and results. Keep actual menu height content-driven so short results can still shrink. In the isolated comparison, setting that parent maximum restored the original 562-point menu after clearing while preserving the 95-point no-results menu. A forced fixed height is unnecessary and would leave excessive blank space. Production integration must account for frame/header margins and actual constrained placement, especially near window edges.

**Acceptance checks:** Empty → short/no results → cleared, deletion and Escape clear paths, repeated searches, long results, retained scroll offsets, submenus, small windows and different DPI. Verify that all settings return without reopening the menu and scrollbars appear only when the available window space requires them. Related historical WPF issue: ISSUE-025; its previous resolution is not evidence that this Rust path is correct. Track as [ISSUE-042](KNOWN_ISSUES.md#issue-042). No application implementation was changed.

<a id="rust-rev-040"></a>
### RUST-REV-040 — P3 — Settings search insertion caret is reported misaligned

**Evidence: Maintainer-reported visual defect; source and isolated layout measurements establish a vertical alignment candidate, not the exact installed appearance.** Location: `crates/vinemix-ui/src/ui/app/settings_menu.rs:319–327`. “Typing line” is recorded as the text insertion caret. The report did not specify whether the offset is horizontal, vertical or relative to the placeholder, so the measured vertical issue must not be treated as the complete diagnosis.

The search TextEdit has a 30-point minimum height and 8 × 4 padding but no explicit vertical centring. In the isolated layout used above, its rectangle spanned y=49–79, while the 17-point text/caret layout began at y=53: its centre was 2.5 logical points above the field centre. The same top-aligned geometry occurred for empty and populated text. An explicit left/centre alignment moved the layout origin to y=55.5, vertically centring it while retaining its horizontal inset. This verifies a potential correction to the measured offset, not a live blinking-caret or screenshot comparison.

**Expected and next step:** Caret, placeholder and typed text should share the intended insertion origin and vertical alignment. Check empty focus, typed text, selection, a horizontally scrolled long query and clearing at representative DPI scales. Apply explicit shared alignment if it resolves the reported defect; investigate horizontal offsets separately if they remain. Preserve the 30-point field and existing input behavior. The earlier WPF caret/placeholder mismatch was tracked under ISSUE-001; do not assume its different renderer-specific cause applies here. Track this Rust report as [ISSUE-043](KNOWN_ISSUES.md#issue-043). Implementation and installed visual verification remain pending.

## Follow-up — device reconnection identity, 10 November 2026

<a id="rust-rev-041"></a>
### RUST-REV-041 — P2 — Reconnected input/output can appear as a new choice while the saved device remains Missing

**Evidence: Maintainer-reported physical off/on symptom; identity-dependent behavior reproduced with synthetic data and production selection code. The actual device's identity transition has not been captured.** No personal device was switched, enumerated or reassigned for this investigation, and no app/engine was launched.

**Report:** An explicitly selected input or output is turned off and back on. Occasionally the dropdown presents a second version while the original remains marked Missing. The maintainer asks whether Windows device priority is responsible. Establish whether the saved selection itself remains Missing and audio fails to recover, or whether a stale Missing row merely remains listed after successful recovery: the code can produce both presentations under different conditions.

**What the implementation does:**

- `crates/vinemix-wasapi/src/catalog.rs:130–166` enumerates Windows endpoints in all states. It obtains the ordinary endpoint ID and attempts to read `System.AudioEndpoint.StableId`; previous metadata is reused only for the same ordinary ID and direction. A failed/unavailable stable-property lookup can leave a new endpoint without a stable ID. Whether that occurred in the reported incident is unknown.
- `crates/vinemix-wasapi/src/policy.rs:65–87` resolves an assignment by active exact ID, then a unique active stable-ID match in the correct direction. An ambiguous fallback is not guessed. Input workers use this policy. The separate output resolver at `crates/vinemix-engine/src/engine.rs:117–169` uses the same active-exact/unique-stable order; it was source-inspected, not invoked by this probe.
- `crates/vinemix-ui/src/availability.rs:78–126` mirrors identity resolution for the chooser, retaining the old exact record or synthesizing a Missing row from the saved binding when it cannot resolve. `:131–142` gives equal-name entries “Device 1”, “Device 2”, etc. Thus a second visible row need not mean VineMix created a second Windows device: it can be an active Windows endpoint beside a retained saved placeholder.
- `availability.rs:41–43` copies the available identity into a new binding on assignment. Investigate whether stable IDs unavailable at assignment are later safely learned for the same exact endpoint; do not assume a later inventory update automatically enriches an existing saved binding.

**Synthetic verification:** The probe called production `availability::build`/`selected` and compiled the unchanged platform-free WASAPI policy source into the external harness. All six cases were exercised for both capture and render, then checked with reversed inventory order. Example names and IDs were synthetic.

| Inventory/binding condition | Chooser result | Worker policy result |
| --- | --- | --- |
| Same endpoint ID returns Active, even without a stable ID | Original choice is current and Active | Resolves the original endpoint |
| Endpoint ID changes; one active endpoint matches the saved stable ID | New endpoint becomes current and Active | Resolves the replacement |
| Endpoint ID changes; saved stable ID is empty | Active same-name choice plus a current saved Missing row | No active match |
| Endpoint ID and stable ID both differ | Active same-name choice plus a current saved Missing row | No active match |
| Old Missing endpoint and new Active endpoint share the saved stable ID, with disconnected devices visible | Both rows shown, but the new Active row is current | Resolves the replacement; duplicate-looking rows alone do not prove failed reconnection |
| Two active endpoints share the fallback stable ID and no exact active match exists | Active candidates plus saved Missing row | Rejects ambiguous fallback |

Reversing enumeration order did not alter the selected identity or worker result in these fixtures. The synthetic cases establish how the reported presentation can arise; they do not establish that Windows changed this particular device's ID during a simple power cycle.

**Windows priority versus identity:** For explicitly assigned nodes, the inspected selection paths do not choose a Windows default-role device or the first/highest-priority same-name device. Default-device notifications can cause a catalog refresh (`catalog.rs:249–263`), but the subsequent assignment still resolves by identity. The evidence therefore points toward identity availability/change, stale inventory/presentation or ambiguous fallback rather than default-device priority. A real before/during/after trace is needed to distinguish them.

**Primary-source context, checked 2026-11-10:** Microsoft documents endpoint IDs as unique and opaque; identical devices can have the same friendly name. Ordinary USB unplug/replug is documented to preserve the endpoint ID, whereas driver/device reinstall or update can change it. A power-cycle report should not simply be dismissed as expected ID churn. [Microsoft: Endpoint ID Strings](https://learn.microsoft.com/en-us/windows/win32/coreaudio/endpoint-id-strings). Windows 11 24H2 introduced the more durable stable-ID property, but it is not guaranteed present or immutable; property reads can fail. Its value must be compared as an opaque, case-sensitive string. [Microsoft: PKEY_AudioEndpoint_StableId](https://learn.microsoft.com/en-us/windows/win32/coreaudio/pkey-audioendpoint-stableid). Windows default roles govern role-based device choice, a different concept from saved endpoint identity. [Microsoft: Device Roles](https://learn.microsoft.com/en-us/windows/win32/coreaudio/device-roles).

**Required investigation and correction:** Capture the saved binding, catalog generations, endpoint/stable-ID equality, direction/state, property-read availability, current choice and resolved worker/output identity before/off/after a controlled reproduction. Keep raw identifiers and hardware evidence outside the checkout; use consistent anonymous tokens in shared summaries so equality and changes remain visible. Recheck stable-property retrieval and delayed property availability. Compare an unchanged-ID return, changed-ID return, explicit Windows disable/enable and any default-role change separately.

When a unique trusted identity proves the returning endpoint is the saved device, reconcile chooser, availability and engine resolution consistently; investigate safe binding enrichment/recovery where identity metadata was previously unavailable. If only names match or identity is ambiguous, retain the assignment and provide a clear recovery/reselection path rather than silently routing audio elsewhere. Do not merge distinct same-name devices, strip/normalize opaque IDs, erase recovery entries indiscriminately or change Windows priority as a speculative fix. Missing rows exposed by the disconnected-device setting need truthful presentation, not automatic deletion.

**Acceptance checks:** Input and output off/on with unchanged IDs; changed endpoint ID with matching, missing, changed or delayed stable ID; old Missing record retained versus absent; disconnected visibility on/off; identical-name distinct devices; ambiguous fallback; reversed inventory order; default-role changes; and rapid notification bursts. Verify the actual selected row, recovered audio path and saved workspace behavior, not merely the number of dropdown rows. Keep this separate from RUST-REV-002's stale highlighted index, RUST-REV-015/016's notification handling and RUST-REV-032's transient status wording. Track as [ISSUE-044](KNOWN_ISSUES.md#issue-044). Diagnosis of the physical incident and implementation remain pending.
