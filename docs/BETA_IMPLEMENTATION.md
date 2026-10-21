# VineMix beta qualification record

Candidate: **0.3.0-beta.1**, displayed as **0.3.0 · Beta 1**.
Updated **21 October 2026**. The maintainer supplies this Windows 11 x64 closed
beta directly. The repository remains private and release material unpublished.
The application and Setup are unsigned. Qualification is incomplete.

This record describes the separately built beta candidate. The committed GitHub
source still predates the beta application integration; see
[source status](../README.md#source-status). The checks below were recorded for
that candidate, not rerun against this documentation update. Raw diagnostics and
recovery backups remain private, outside the source repository.

## Candidate behavior

- A fresh workspace has Physical input connected to Virtual output and Listening
  output. Physical devices are unassigned. Both outputs start muted; Listening
  output starts at −24 dB. An existing workspace is retained.
- Startup resolves the ordinary desktop settings location before loading data.
  Migration copies legacy AudioMixer data while retaining originals. Conflicting
  stores require a deliberate choice. Recovery offers Retry, Restore backup or
  Start fresh with preserved originals instead of silently saving defaults.
- Installed and standalone copies coordinate one session. Standalone validates
  and prepares its bundled runtime automatically; neither format requires a
  separate .NET installation. Optional audio drivers have their own terms.
- Beta version/build information is available in Settings → About VineMix.
  Updates to these supplied files are manual through the maintainer.

## Exact candidate files

These are file identities, not public download links. Compare with the checksums
supplied through the maintainer's private distribution channel. A later build
needs its own checksums and qualification; do not reuse these for another file.

| File | Bytes | SHA-256 |
| --- | ---: | --- |
| `VineMix-0.3.0-beta.1-Standalone-x64.exe` | 182,727,709 | `DC86A5373EB8D79269A40F7B92CD47B14A85D2B1FC3EB77B9E0C4EE0AF962CDE` |
| `VineMix-0.3.0-beta.1-Setup-x64.exe` | 102,790,362 | `6B81D71420EC59AB9FDD837F681380C2041E3661B1F387AC1BA46297F73AF96E` |

The files were independently rehashed during this documentation refresh and match
the recorded candidate. Both packages share the recorded source snapshot hash
`cbc3c540cc03c58d3ec4209afb84706de94c200d09d74036a9764e945b4aeb7a`.
That is a build-receipt snapshot hash, not a Git commit or a claim that this
checkout reproduces the beta. The recorded engine SHA-256 is
`49FCE3820C9B92C9B0D9ED68380DA593712F58919ED02AEBD0163F51617A6F17`.

## Recorded passed checks

| Area | Evidence and limits |
| --- | --- |
| Native offline | All 17 suites passed. Final layout follow-up passed graph, lifecycle and qualification checks, including 278,760 graph and 7,922 qualification assertions. Synthetic workers did not open audio hardware. |
| Desktop | 10,860 detached checks, 215 renders and zero binding errors. Covered recovery, migration, session ownership, assignment persistence, history, restart and cancellation. |
| Documentation | 58 checks and six inspected images at 96/192 DPI, with zero binding errors. Fixtures used generic devices without accessing a live engine or personal settings. |
| Standalone runtime | 17 checks against the final embedded payload, covering extraction, reuse, corruption, locking, path validation and concurrency. Both delivered EXEs also passed inert release smoke checks. |
| Installer | 175 checks against final Setup, covering isolated installation, repair, downgrade refusal, running-target protection, payload and uninstall preservation. Vendor installation and normal app startup were disabled during these checks. |
| Live migration and reopening | 68 scripted checks covered desktop-shell and tool-origin launches, migration, engine recovery, legacy/current launch orders and installed/standalone switching. Workspace contents were preserved. This does not establish hardware endurance or a clean-machine pass. |

Required notices remain in the packages. Vendor driver signatures do not sign
or certify VineMix. See the [tester guide](BETA_TESTER_GUIDE.md) for driver terms
and how to review a supplied file before running it.

## Known limitations and pending checks

- **Sustained engine use:** an approximately 72-minute interval was interrupted
  when the desktop opened and monitoring yielded its connection. Frames advanced
  and workspace bytes stayed unchanged during that interval. It is not an
  eight-hour pass; a fresh continuous run is pending.
- **Interactive desktop endurance:** an eight-hour session with the interface
  open and memory use observed remains pending. Engine-only monitoring does not
  cover this.
- **Clean-machine installation:** each distribution format still needs a clean
  Windows 11 x64 exercise without a preinstalled .NET runtime. Isolated installer
  checks do not replace it.
- **Device lifecycle:** physical unplug/reconnect, Windows sleep/resume and vendor
  driver installation, cancellation and restart paths remain pending.
- **Unexplained closure:** an earlier unexpected application closure was not
  reproduced or explained by the available crash records. It remains unresolved;
  the maintainer must accept this uncertainty before broader distribution if it
  cannot be diagnosed. Any new unexplained exit or workspace reset blocks readiness.
- **Reported tester issues:** Virtual Desktop's virtual microphone was reported
  missing from the input picker, header/footer ghosting was reported during
  resizing, and some Add Node explanations were missing. Follow-up work is in
  progress separately; no correction is claimed for the exact files above.
- **Updates and signing:** these supplied files use manual replacement and are
  unsigned. No automatic update service or public download is enabled.

Missing hardware or an unavailable test environment leaves a check **pending**.
Neither package creation nor this documentation refresh establishes a qualified
release. Follow the [beta test guide](BETA_TESTER_GUIDE.md) to report observations
through the private distribution channel.
