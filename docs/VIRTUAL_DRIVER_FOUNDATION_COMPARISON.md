# MicDeck and AudioRouter foundations for Vine Mix virtual cables

Research date: **20 October 2026, UTC+10**. Target: two independent
Windows virtual audio cables that also work between other applications while
Vine Mix is closed. Four clearly labelled Windows audio entries are acceptable.

## Recommendation

**Prototype AudioRouter's separately licensed Windows driver framework, replacing
its private application bridge with two internal playback-to-recording paths.**
Retain its endpoint registration, stream state, packet/position handling and
device lifecycle code. Keep Vine Mix on ordinary WASAPI for these devices.

This is a choice of development foundation. Neither inspected revision is ready
to ship as our two autonomous cables. AudioRouter needs a new internal transport;
MicDeck already has loopback transport but has reproduced faults in its Windows
clock and position handling, plus unfinished power and format integration. The
judgment is that a small, carefully tested transport added to the stronger Windows
framework is a better first investment than repairing MicDeck's unproven kernel
framework around its good portable buffer.

This changes the earlier [survey](OPEN_SOURCE_DRIVER_SURVEY.md) preference for
MicDeck as the first conventional-cable prototype. The deeper comparison found
both the MicDeck faults and newer AudioRouter VM records missed when relying on
its stale driver README. A bounded feasibility test should precede a dependency
decision. Implementation and installation remain future work.

## Comparison

| Consideration | MicDeck VAD | AudioRouter driver |
| --- | --- | --- |
| Existing audio path | Actual internal playback-to-recording loopback | Separate playback and recording bridges to its application; no automatic loopback |
| Two isolated cables | One global cable today; must allocate separate contexts and endpoints | Two pairs enabled by default; eight pairs defined |
| Reliability evidence | Good portable transport tests; two reproduced Windows API faults; no upstream loaded-driver validation in its validation records | More developed Windows sample framework; upstream VM streaming, lifecycle and Verifier records, with continuity and release gates still open |
| Work for our requirement | Fix clock, cyclic positions, format negotiation and power integration; add cable isolation; qualify the whole kernel path | Add concurrent per-pair internal queues and reset policy; remove private bridge dependency; qualify the changed transport and retained Windows paths |
| Commercial licence | Driver-specific MIT; simple notice requirement | Driver-specific MS-PL; preserve notices and covered-source licence |
| Application integration | Generalize existing WASAPI device roles | Same WASAPI approach after internal-loopback adaptation; no GPL application/client import needed |
| Preferred role | Portable transport reference and alternative prototype | First framework to prototype for the autonomous two-cable product |

The preferred framework has stronger evidence, not proven production reliability.
Test counts do not rank audio quality or predict the time needed to qualify a fork.

## Source versions

Only the relevant driver components were assessed for adoption:

- [MicDeck VAD](https://github.com/3godzinyL/Virtual-Soundboard-Audio-Windows-Mixer/tree/4ec7e53c0ac37291187d92f3d75bf88af469895c/drivers/micdeck-vad),
  commit `4ec7e53c0ac37291187d92f3d75bf88af469895c`.
- [AudioRouter virtual driver](https://github.com/MrDesjardins/audiorouter/tree/f63f491f6c12e5c7325e9a9dcd5666163caed371/drivers/audiorouter-virtual),
  commit `f63f491f6c12e5c7325e9a9dcd5666163caed371`.

AudioRouter's driver subtree is byte-identical to the source of its upstream
RC3 0.2.4 VM tests, commit `d2560715810242496a8a1652b9247a2369a296b5`.
Both Git subtree IDs are `18a4947f110ff8cc46517ba3d94ebdc1cf9bd0d0`.
This connects the reported tests to the inspected source; it does not authenticate
the upstream measurements or qualify our proposed transport change.

## Tests performed here

The portable tests used the existing LLVM-MinGW compiler with assertions enabled.
New harnesses exercise actual copied source or unchanged extracted functions,
with deterministic substitutes for Windows kernel services where necessary.

| Test | Observed result | Scope |
| --- | --- | --- |
| MicDeck upstream core and pipeline suites | Both pass, including strict compiler warnings | Portable queue, conversions and pipeline policies |
| MicDeck concurrent buffer stress | Two independent rings preserve 2,000,000 exact ordered frames in total | Four host audio threads, randomized chunk sizes |
| MicDeck pipeline stress | 20,000 randomized blocks pass isolation/amplitude checks, including flush/restart | Two portable pipeline objects, not its single global kernel context |
| Same new MicDeck transport tests under AddressSanitizer | Pass, no findings | Instrumented host memory paths; not a race detector or kernel test |
| MicDeck actual clock source with controlled counters | Reproduces incorrect elapsed time and frame progress; correction-only positive control passes | Concrete API-contract defect |
| MicDeck extracted unchanged GetPosition | Returns offsets beyond its cyclic buffer | Concrete API-contract defect |
| AudioRouter upstream bridge suite | 88,167 checks pass | Header helpers, queues, packet/timing models and guarded copies |
| AudioRouter additional actual transport functions | 180,287 assertions pass over 20,000 blocks per cable | Payload/isolation, backpressure, ownership generations, invalid data, seqlocks and simulated rundown |

AudioRouter's alternative host build supplied two missing SAL annotation macros
on the command line without editing upstream source. Repeated assertions mostly
check samples and state transitions, not thousands of distinct scenarios. Host
tests do not exercise real IRQL, pinned pages, PnP, notification timing or teardown.

The additional AudioRouter test also confirms that publishing playback data does
**not** feed recording without a user-mode relay. A small conversion defect was
reproduced: its float32-to-double-to-float32 round trip turns the maximum finite
float into zero because of an overly narrow upper bound. This is a correctness
repair for the fork, with no practical effect on ordinary audio around ±1.

### Native Windows driver compilation

A temporary subset of Microsoft's official EWDK supplied its signed MSVC 19.44
compiler, linker and genuine WDK 26100 headers/libraries without running an
installer. All **16 MicDeck** and **12 AudioRouter** driver translation units
compiled using kernel mode, and both linked to native x64 `.sys` binaries, with
no upstream source edits. A minimal kernel-header/driver baseline also passed.

These were direct compiler/linker checks, not the complete upstream MSBuild,
resource, INF, catalog, signing or install pipeline. AudioRouter's initial link
failure was corrected by matching the project's static-library semantics and
kernel CRT include order; it was a test-invocation issue, not an identified
upstream source defect. Compiling and linking does not establish driver loading
or working audio. Detailed commands and results are retained with the evidence.

## MicDeck reliability findings

The portable buffer is its strongest component. The kernel integration has
separate problems that portable tests cannot expose:

1. **Wrong clock frequency.** In
   [audio_clock.cpp](https://github.com/3godzinyL/Virtual-Soundboard-Audio-Windows-Mixer/blob/4ec7e53c0ac37291187d92f3d75bf88af469895c/drivers/micdeck-vad/driver/src/audio_clock.cpp#L10),
   the constructor stores the return from `KeQueryPerformanceCounter` as the
   frequency. That return is the counter; the output argument receives frequency,
   as [Microsoft documents](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/wdm/nf-wdm-kequeryperformancecounter).
   With a simulated counter of 3.6 billion and frequency of 10 million Hz, the
   unchanged source reports 1,064 bytes after one second instead of 384,000 for
   48 kHz stereo float32. Correcting only initialization produces 384,000.
   These are controlled test values, not a performance measurement on this PC.
2. **Invalid playback/recording positions.**
   [GetPosition](https://github.com/3godzinyL/Virtual-Soundboard-Audio-Windows-Mixer/blob/4ec7e53c0ac37291187d92f3d75bf88af469895c/drivers/micdeck-vad/driver/src/miniport_wave_rt_stream.cpp#L273)
   returns continuously increasing byte counts instead of offsets within the
   cyclic buffer. The exact method returns offsets 3,848 and 3,864 for a
   3,840-byte buffer. This violates
   [Microsoft's contract](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/portcls/nf-portcls-iminiportwavertstream-getposition).
   Capture positions must also track available data correctly after repair.
3. **Incomplete integration.** The power-management class is never registered,
   its master clock does not drive stream timing, float format intersection
   skips request bounds, and some mono PCM requests receive stereo answers.
   The adapter owns one static global cable, so duplicate device installation
   would not create independent paths. Diagnostics also need a safe concurrent
   snapshot policy.

The local clock correction is small. Establishing correct notifications,
capture availability, format negotiation, sleep/resume and lifecycle behavior is
the substantial work. MicDeck's
[validation records](https://github.com/3godzinyL/Virtual-Soundboard-Audio-Windows-Mixer/blob/4ec7e53c0ac37291187d92f3d75bf88af469895c/drivers/micdeck-vad/VALIDATION.json)
do not provide a loaded-driver baseline against which to assess those repairs.

## AudioRouter reliability evidence

Its newer
[active development records](https://github.com/MrDesjardins/audiorouter/blob/f63f491f6c12e5c7325e9a9dcd5666163caed371/docs/plans/active/virtual-cable.md)
report VM tests for install/remove, endpoint defaults and counts, both audio
directions, bit-exact transfers, crash recovery, reboot identity, upgrade/repair,
one-hour pool observations, Driver Verifier, IOCTL fuzzing and 56 directed
cross-talk pairs. The driver source matches the reported RC3 source exactly.

These remain **upstream self-reported results**. Their raw measurement archives
and measured binaries were not available in the inspected repository. Some runs
contain dropouts or inserted silent blocks; upstream attributes them to VM
scheduling and relay behavior, which does not prove that physical hardware will
be clean. Discord/OBS compatibility, sustained continuity/latency and a production
release package remain open. The new internal cable transport must repeat the
relevant tests even if the Windows framework is retained.

## Required adaptation

The target is one package, two autonomous cables and four endpoints. Each cable
connects its playback end to its recording end inside the driver, regardless of
whether Vine Mix is running. Missing playback produces silence. Missing recording
discards audio, so starting a recorder cannot replay old private audio.

For the first prototype:

1. Retain AudioRouter's two indexed endpoint pairs, stream bus mapping and
   Windows stream/position framework. Assign Vine Mix its own hardware/service
   identity, GUIDs and stable endpoint roles.
2. Add one bounded, concurrent transport context per cable. Replace its mapped
   section publication/copy and lease-shape queries with that cable's internal
   enqueue/dequeue and fixed format. The main seams are `ReadBytes`, `WriteBytes`
   and the bridge helpers in `adapter.cpp`.
3. Start with stereo 48 kHz and float32 shared-mode endpoints. Windows shared-mode
   conversion handles other application formats. Keep existing internal float64
   staging initially if that reduces change; do not also rewrite representation
   and packet timing without a measured reason.
4. Define stop/start, capture reconnect, sleep and removal resets. Invalidate
   the cross-stream ring and private staging together. The existing capture and
   render staging queues are **not** a concurrent cross-stream ring: their
   ordinary fields are protected only by their separate owning stream locks.
5. Remove the obsolete private control/lease bridge after internal audio works.
   Re-run lifecycle and Verifier checks on the reduced fork.

Vine Mix currently recognizes `vbCable` and `vacLite` by vendor-specific rules in
`native/src/windows_audio.cpp`, `native/src/engine.cpp` and
`desktop/AudioMixer.Desktop/VirtualDeviceBindings.cs`. Replace those with explicit
cable roles and deliberate saved-binding migration. The current VAC Lite output
uses exclusive WASAPI; the new cable needs a qualified shared-mode path so other
applications can use its normal ends as intended. Installation should bind Vine
Mix's two engine ends automatically and clearly label the two usual external
selections. This remains compatible with the
[installer proposal](INSTALLATION_AND_UPDATES.md).

A private Vine Mix bridge would require less alteration of AudioRouter's
transport, but would fail the confirmed requirement that cables work with Vine
Mix closed. A permanent relay service could provide that behavior, at the cost
of an additional required process and audio hop. It is not the recommended design.

## Commercial licensing

Both driver components permit paid distribution and donations under their visible
grants, without per-copy royalties:

- **MicDeck:** retain its
  [driver-specific MIT notice](https://github.com/3godzinyL/Virtual-Soundboard-Audio-Windows-Mixer/blob/4ec7e53c0ac37291187d92f3d75bf88af469895c/drivers/micdeck-vad/LICENSE.md).
  Its notices claim an original implementation using documented Microsoft
  interfaces. Take only the driver component; the surrounding application also
  contains proprietary VB-CABLE resources that are unnecessary for our fork.
- **AudioRouter:** its
  [root licence section](https://github.com/MrDesjardins/audiorouter/blob/f63f491f6c12e5c7325e9a9dcd5666163caed371/README.md#license)
  explicitly declares the driver a separate
  [MS-PL program](https://github.com/MrDesjardins/audiorouter/blob/f63f491f6c12e5c7325e9a9dcd5666163caed371/drivers/audiorouter-virtual/LICENSE-MS-PL.txt).
  Preserve copyright, patent, trademark and attribution notices. Published covered
  driver source stays under MS-PL; binary distribution does not by itself require
  publishing Vine Mix's source. Use our own branding without implying endorsement.

AudioRouter's existing Rust `windows-audio` and `driver-helper` crates are outside
the driver licence boundary and inherit **GPL-3.0-only**. They should not be
transplanted under an assumed MS-PL grant. Our autonomous kernel design avoids
needing their bridge client; independently implement the installer helper and
normal Windows device discovery. Review external tools/templates before reuse.

No paid cable licence is required by either source option. A normal public
Windows release still needs our own accepted signing and qualification route.
[Microsoft requires an EV certificate associated with the hardware dashboard](https://learn.microsoft.com/en-us/windows-hardware/drivers/dashboard/code-signing-reqs).
Its current guidance recommends
[HLK-tested dashboard signing for release](https://learn.microsoft.com/en-us/windows-hardware/drivers/dashboard/driver-signing-offerings)
and describes attestation for testing. Do not budget a production fork as entirely
cost-free or assume an upstream test certificate covers our modified binary.

## Development gates and estimated effort

Estimates assume one experienced Windows audio-driver engineer, a disposable
Windows test target and a narrow initial stereo 48 kHz scope. They are engineering
estimates, not measured delivery commitments.

| Stage | Expected effort | Evidence required |
| --- | --- | --- |
| Feasibility gate | 1–2 weeks after the lab is ready | Reproducible driver/package build; two simultaneous internal cables work with Vine Mix closed; isolation, clean stop/restart and bounded queue behavior |
| More complete prototype | Roughly 2–5 weeks total for driver adaptation | Recovery/reset policy, explicit endpoint roles, native binding and basic setup integration |
| Public release | Several months, with substantial uncertainty | Supported Windows builds, real applications/hardware, prolonged load, Verifier, leak/teardown checks, signing and installer/upgrade/rollback qualification |

Stop or change foundation if the feasibility gate fails; do not extend it
indefinitely because a framework has already been chosen for investigation.

The acceptance run should use distinct deterministic signals on both cables,
measure frame integrity and cross-talk, capture underruns/overruns and latency
distribution, and compare continuity/latency with the current setup. Exercise
apps opening/closing in either order, multiple shared-mode clients, silence and
stale-audio resets, sleep/resume, disable/remove, reboot, upgrade and uninstall.
Run Driver Verifier and load/teardown stress in a disposable target, then qualify
the exact signed package on real hardware with Secure Boot and Memory Integrity.

## Evidence and project state

Detailed source findings, reproducible harnesses, stdout, compiler identities and
hashes are retained under `artifacts/driver-deep-review-20261020/`, including
`micdeck/REVIEW.md`, `audiorouter/REVIEW.md` and
`licensing/LICENSING_REVIEW.md`; `build/BUILD_REPORT.md` and
`build/build-summary.json` record the native compiler/linker checks. Those are
ignored local assessment artifacts;
the key conclusions and immutable source links are preserved in this document.

No driver was installed or loaded, no certificate or security policy changed,
and the running audio engine and personal settings were preserved. Loaded-driver
qualification on our own disposable Windows target remains the decisive next
test. The proposal and accepted requirements are recorded in
[IDEA-014](IDEAS.md#idea-014-dedicated-vine-mix-virtual-audio-driver).
