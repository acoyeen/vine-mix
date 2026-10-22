# AudioMirror assessment and Vine Mix driver requirements

Research date: **20 October 2026 (UTC+10)**.
Status: feasibility assessment; no driver implementation or installation approved.

## Decision

AudioMirror is a credible, inspectable experimental project, but **not a reliable
driver we can ship after minor edits**. Its cable mechanism is relevant, while
its buffering, object lifetime and removal handling require substantial work.
The best next investigation is a bounded comparison between repairing this fork
and implementing the cable transport on a current Microsoft sample foundation.
Do not select AudioMirror as a production dependency yet.

Commercial distribution appears feasible under the relevant open-source licences,
but the root MIT file is not sufficient evidence for the entire inherited codebase.
Establish the Microsoft sample lineage and preserve its applicable terms first.

## Scope and evidence

Inspected [JannesP/AudioMirror](https://github.com/JannesP/AudioMirror) at commit
[`a9618d1ab4114e3e35e8e50ae804a4205315d1f2`](https://github.com/JannesP/AudioMirror/commit/a9618d1ab4114e3e35e8e50ae804a4205315d1f2),
including its complete tracked-file inventory, history, project/INF definitions,
device registration, pairing, stream lifecycle, formats and ring buffer. Read
GitHub issue/PR metadata through the public GitHub API and relevant discussions.
Dates, issue states and release counts below are observations on the research date.

Reviewed the current Vine Mix virtual-device bindings, engine transport checks,
Windows audio workers, distribution guidance and proposed installer architecture.
Existing unrelated local edits were retained.

Ran two single-threaded portable behavior checks against the upstream
`RingBuffer.cpp` and `RingBuffer.h`, copied unchanged into a temporary directory.
A replacement `Globals.h` provided allocation, memory-copy and no-op lock shims;
objects were zero-initialized to match the upstream placement allocator. These
checks execute the buffer algorithms without loading kernel code. They do not
validate Windows locking, scheduling, driver installation or audible quality.
Local results, matching upstream/probe source hashes, the probe and shim are
retained privately outside the repository with the 20 October research evidence;
they are not part of an application package. Both upstream source copies were
hash-identical, and both defects reproduced on the verification rerun.

No upstream build scripts were executed. The complete WDK driver was **not built
or loaded**. No Driver Verifier, HLK, Secure Boot/HVCI or real audio qualification
was performed. The running mixer, installed drivers and personal settings were
not changed. This is targeted reliability/feasibility work, not a complete security
audit, provenance clearance or certification.

## Legitimacy, maintenance and licensing

- The project publishes source, an explicit MIT licence and its SysVAD origin.
  The owner explicitly added the licence in response to
  [issue #2](https://github.com/JannesP/AudioMirror/issues/2).
- The checked-out default branch has **nine commits**; the latest is dated
  **15 October 2022**. The repository was not archived, but there were **no
  published GitHub releases** in the API response. Later issue activity is not
  evidence of later code updates or Windows 11 qualification.
- The README calls the driver unfinished and says it is not ready to build and
  ship. Its stated testing is Windows 10 x64. This supports treating it as an
  engineering reference, rather than a maintained commercial product.
- The [MIT licence](https://github.com/JannesP/AudioMirror/blob/a9618d1ab4114e3e35e8e50ae804a4205315d1f2/LICENSE.md)
  permits modification and sale subject to its notices. However, Microsoft
  copyright headers remain in files such as `NewDelete.cpp` and `Driver.cpp`.
  The tracked tree contains no separate Microsoft licence collection.
- Microsoft's current sample repository uses
  [MS-PL](https://github.com/microsoft/Windows-driver-samples/blob/main/LICENSE).
  It permits commercial derivatives, requires retained notices, and has specific
  conditions for source redistribution. It does not require publishing source
  solely because compiled binaries are distributed. Identify the exact original
  sample revision and per-file terms before selecting a distribution licence;
  do not assume the fork's MIT label replaces upstream rights.

Assessment: there is credible evidence of an actual open-source development
project. That does not establish malware-free behavior, complete authorship,
production fitness or rights beyond the published licences. No maintainer or
vendor was contacted, and no special permission was obtained.

## Reliability findings

### Buffer defects reproduced outside the kernel

Source: pinned
[`RingBuffer.cpp`](https://github.com/JannesP/AudioMirror/blob/a9618d1ab4114e3e35e8e50ae804a4205315d1f2/AudioMirror/RingBuffer.cpp#L109).

1. **Wrapped writes repeat the wrong bytes.** `Put` advances the destination
   offset but not the input pointer across a wrap. With a 16-byte buffer, write
   bytes 1–12, consume eight, then write 101–108. The next read should end in
   105–108, but actually ends in 101–104. The isolated source check reproduced
   this corruption.
2. **Overflow breaks whole-frame alignment.** The overflow read-position formula
   includes an extra byte. Two frame-aligned writes of 12 and eight bytes into
   a 16-byte buffer with four-byte frames produced a 15-byte available read in
   the second check. Audio must be dropped in complete frames.

Additional static concerns: producers do not take the consumer's buffer lock;
the object stores the saved interrupt level as shared state; and refill waits for
more than half the buffer. Synchronization needs redesign, and refill policy
needs latency measurement. These are source observations, not reproduced kernel
race or listening results. Remove unused experimental alignment code rather than
assuming it protects the active path.

### Cleanup, failures and pairing

Sources: pinned
[`MiniportWaveRTStream.cpp`](https://github.com/JannesP/AudioMirror/blob/a9618d1ab4114e3e35e8e50ae804a4205315d1f2/AudioMirror/MiniportWaveRTStream.cpp#L14)
and
[`MiniportWaveRT.cpp`](https://github.com/JannesP/AudioMirror/blob/a9618d1ab4114e3e35e8e50ae804a4205315d1f2/AudioMirror/MiniportWaveRT.cpp#L629).

- Stream cleanup frees the ring-buffer object as raw pool memory, bypassing its
  destructor. This matches the open
  [memory-leak report #23](https://github.com/JannesP/AudioMirror/issues/23).
- Several stream resources are freed before the notification timer is cancelled
  and drained. The callback's access to those resources makes teardown order a
  concrete review priority; a crash was not reproduced locally.
- `SetPairedMiniport(NULL)` clears the paired pointer and subsequently dereferences
  it without a null guard. The destructor calls this operation. This is an
  observable removal-path defect, not merely a hypothetical feature concern.
- Buffer allocation calls `new RingBuffer` and `Init` without checking either
  failure. Registration and interface-query failures also need complete handling.

[PR #24](https://github.com/JannesP/AudioMirror/pull/24) reports leak and uninstall
crash fixes, but was still **open and unmerged**. Its author also commented that
the patch did not build in their Visual Studio 2019 environment and should not
be merged yet. It is evidence and review material, not a validated fix to adopt
blindly. The earlier merged 2022 change addressed a blue-screen issue; it does
not establish that removal and other crash paths are solved.

### Capability gaps

- The format tables in
  [`SpeakerWaveProperties.h`](https://github.com/JannesP/AudioMirror/blob/a9618d1ab4114e3e35e8e50ae804a4205315d1f2/AudioMirror/SpeakerWaveProperties.h)
  and
  [`MicrophoneWaveProperties.h`](https://github.com/JannesP/AudioMirror/blob/a9618d1ab4114e3e35e8e50ae804a4205315d1f2/AudioMirror/MicrophoneWaveProperties.h)
  expose stereo 16-bit PCM at **44.1 kHz**, while Vine Mix processes at 48 kHz.
- `AdapterCommon::Create` enforces one adapter instance. Installation creates one
  microphone/speaker pair; the descriptor factory's name-indexing function is a
  TODO. Two independently installed copies are not an implemented capability.
  Sources:
  [adapter](https://github.com/JannesP/AudioMirror/blob/a9618d1ab4114e3e35e8e50ae804a4205315d1f2/AudioMirror/AdapterCommon.cpp),
  [descriptor factory](https://github.com/JannesP/AudioMirror/blob/a9618d1ab4114e3e35e8e50ae804a4205315d1f2/AudioMirror/MinipairDescriptorFactory.cpp).
- The one render/capture system-pin pairing is not a proven multi-client design.
  One kernel stream can still serve several shared-mode applications through
  Windows' audio engine: the pin limit alone does not prove an application limit.
  Test Discord plus OBS, multiple producers, mixed client formats and exclusive
  access explicitly.
- No automated test suite, CI workflow or production package was present in the
  inspected tracked tree. No Windows 11 reliability claim is established by this
  assessment. [Issue #26](https://github.com/JannesP/AudioMirror/issues/26) includes
  an installation/device-visibility report mentioning Windows 11; its cause is
  not established and it is not proof of universal incompatibility.

## Proposed driver design for Vine Mix

Assumption: Windows 11 x64 initially; two independent stereo paths, no mixer,
effects, network audio, surround or arbitrary user-created cable counts in the
driver. Users may use Vine Mix commercially and pay for it. Exact supported
Windows releases and PCM precision remain decisions.

The smallest change to the existing engine is **one driver package containing
two isolated loopback cables**:

| Cable | Playback endpoint | Recording endpoint |
|---|---|---|
| Receive | Vine Mix Input — other apps play here | Vine Mix Receive — engine captures here |
| Send | Vine Mix Send — engine plays the finished mix here | Vine Mix Output — Discord/OBS record here |

This still creates four Windows audio endpoints. The two engine-facing partners
are necessary for ordinary cable transport, even when first-run guidance only
emphasizes Input and Output. Do not disable partners to make the device list look
shorter. Unique names, hardware/interface identities and pairing must keep the
two paths independent and avoid feedback or cross-cable leakage.

Showing only two public endpoints instead would require a different design:
virtual playback/capture devices with a private engine-to-driver transport.
That adds access control, protocol/versioning, buffer mapping, process-lifetime
and crash-recovery work. It is possible but not a small AudioMirror adaptation.
Choose between these architectures before committing to endpoint identities.

## Development work and acceptance gates

| Workstream | Required result before release |
|---|---|
| Provenance and ownership | Pin all source revisions; identify inherited files and licences; preserve notices; use Vine Mix branding and unique IDs; establish a maintained release repository. |
| Reproducible build | Use a supported matching Visual Studio/MSVC, SDK and WDK combination. Produce clean x64 Release and diagnostic builds, valid INF/catalog and build evidence. The existing user-mode LLVM/.NET pipeline is not a driver build pipeline. |
| Transport core | Replace or comprehensively repair the buffer implementation. Use bounded, frame-aligned operations, explicit overflow/underflow behavior, bounded callback work and a reviewed synchronization model. No stale audio replay after reconnect. |
| Object lifetime | Define ownership of adapters, endpoints, streams, timers and notifications. Cancel/drain callbacks before freeing state; synchronize unpairing; handle allocation and partial-registration failures; pass repeated removal tests. |
| Two cables | Register two unique pairs inside one adapter, with independent transport state and persistent endpoint identities. Handle either end opening first, missing peers and simultaneous active paths. |
| Audio contract | Define stereo 48 kHz kernel transport and chosen PCM precision. Validate all format sizes and rates. Use Windows shared-mode conversion where appropriate; reject unsupported exclusive requests predictably. Keep DSP/resampling out of kernel code unless demonstrably necessary. |
| Vine Mix integration | Add an explicit transport family/role mapping in native discovery, graph validation and desktop assignment. Remove the assumption that virtual input means VB-CABLE and virtual output means VAC Lite. Preserve saved assignments and offer deliberate migration. |
| Installation and serviceability | Detect by package/device identity; prompt for elevation; support update, cancellation, rollback and restart-required outcomes; preserve Windows defaults and app data. Driver updates need their own tested compatibility policy. |
| Windows trust | Set up the required publisher account/certificate process and Microsoft signing route. Qualify with Secure Boot and Memory Integrity enabled on normal target systems. Test signing is for isolated development, not the end-user experience. |
| Ongoing maintenance | Own crash triage, symbols, versioned packages, Windows compatibility checks and driver/app rollback compatibility. Upstream inactivity means we should plan to maintain the fork ourselves. |

### Vine Mix changes identified in the current source

- `native/src/windows_audio.cpp`: transport classification is vendor-specific;
  the VAC path currently uses exclusive rendering. Do not copy that exception
  automatically to the new driver; qualify the new shared-mode path explicitly.
- `native/src/engine.cpp`: worker construction and binding validation hard-code
  `vbCable` for virtual input and `vacLite` for virtual output.
- `desktop/AudioMixer.Desktop/VirtualDeviceBindings.cs`: automatic assignment
  uses those same families. A new family with two recording and two playback
  endpoints also needs explicit cable-role identification to avoid ambiguity.
- Availability messages, setup guidance and detached verification fixtures need
  the new roles. Existing saved IDs must remain authoritative until the user
  chooses migration; reinstalling or renaming must not silently select a peer.
- Integrate through the optional-component model already proposed in
  [installation and updates](INSTALLATION_AND_UPDATES.md). Driver development
  need not block work on consistent app/save locations.

### Verification needed

1. Host-side buffer tests: wrap boundaries, whole frames, exact capacity,
   overflow/underflow, reset, randomized sequences, supported precisions and
   concurrent producers/consumers under the chosen ownership contract.
2. Disposable kernel-test machines: open/close races, either peer absent,
   repeated disable/enable and uninstall, low-memory injection, timer shutdown,
   sleep/resume, reboot and Windows audio-service restart.
3. Simultaneous signal tests: distinguishable signals in both cables, channel
   mapping, no cross-talk, silence without a producer, no replay of old buffers,
   mono and 44.1/48 kHz shared-mode clients, multiple players and Discord + OBS.
4. Measure transport latency, scheduling load, pool allocations and glitch
   counters. Proposed minimum soak gate: 24 hours on each supported baseline,
   no driver-attributable crashes or leaks and no unexplained discontinuities.
   That gate is a proposal, not evidence of reliability; repeat on multiple
   machines, under load, and across power/device transitions.
5. Run Driver Verifier and applicable HLK audio, installation and integrity tests
   in the lab. Install the final signed package on clean ordinary Windows 11
   systems with security features enabled. Test upgrade/uninstall/rollback with
   Vine Mix active, stopped, crashed and previously installed.

Use a test VM with kernel debugging for early failure investigation, then real
test hardware for timing and security-feature qualification. Do not enable Driver
Verifier or development signing on a daily-use audio setup as part of this
research.

Microsoft references, checked 2026-10-20:
[WDK toolchain](https://learn.microsoft.com/en-us/windows-hardware/drivers/download-the-wdk),
[Driver Verifier](https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/driver-verifier),
[Memory Integrity compatibility](https://learn.microsoft.com/en-us/windows-hardware/test/hlk/testref/driver-compatibility-with-device-guard),
[signing policy](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/kernel-mode-code-signing-policy--windows-vista-and-later-),
[signing options](https://learn.microsoft.com/en-us/windows-hardware/drivers/dashboard/driver-signing-offerings).
Microsoft describes attestation as a testing route and recommends HLK-tested
dashboard signing. Plan production qualification accordingly; a test certificate
or the upstream project's signature cannot be reused for a modified driver.

## Practical sequence, effort and alternatives

1. **Feasibility gate:** resolve source/notice provenance, establish a clean WDK
   build, and compare the repair burden with current SimpleAudioSample/SysVAD.
   Inventory required sample features; do not retain unimplemented offload or
   processing features merely because they exist in a sample.
2. **One-cable gate:** implement a tested transport and lifecycle, then demonstrate
   correct 48 kHz audio and clean stop/removal in an isolated environment.
3. **Two-cable gate:** demonstrate independent paths in one package; integrate a
   staged Vine Mix build and deliberate settings migration using test profiles.
4. **Release gate:** complete app compatibility, endurance, installation recovery,
   Memory Integrity/Verifier/HLK work and production signing. Release only after
   those results, not just successful compilation.

Rough planning estimate, assuming one experienced Windows audio-driver engineer
with testing support: **one to two weeks for the initial feasibility comparison;
several further weeks for an isolated prototype; roughly three to six months
overall for a qualified first release**. These are uncertain engineering estimates,
not measured delivery dates; certification administration, source reconstruction,
unexpected kernel defects and broader platform support can extend them. Staffing,
test hardware, certificate costs and publisher eligibility have not been quoted.

A fork would remove third-party cable royalties under the checked licences,
subject to provenance and notice compliance; it would create our own maintenance
and signing obligations. Starting from a current Microsoft sample may be more
predictable than repairing AudioMirror's old stream implementation. That is an
engineering recommendation, not a comparative prototype result. The existing
drivers remain the fallback while this choice is researched, with their current
licensing restrictions recorded in [distribution](DISTRIBUTION.md).

**Next step:** authorize a separate, isolated feasibility prototype if this
direction is selected. This document and IDEA-014 record research only; no
dependencies, driver packages, Windows settings or running-app behavior changed.
