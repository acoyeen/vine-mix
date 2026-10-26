# Open-source Windows virtual audio driver survey

Researched **20 October 2026 (UTC+10)**. Research and offline checks
only; no driver or application implementation selected or installed.

## Recommendation

**Deep-review update, 20 October 2026:** the subsequent
[MicDeck and AudioRouter comparison](VIRTUAL_DRIVER_FOUNDATION_COMPARISON.md)
supersedes the initial ranking below. The maintainer accepts four clearly labelled
endpoints and requires both cables to work with VineMix closed. Prototype
**AudioRouter's Windows driver framework with two new internal loopback paths**
first. Its unchanged private bridge cannot satisfy that requirement. MicDeck's
portable transport passed stronger stress tests, but its actual clock and cyclic
position code contain reproduced API-contract faults, with further unfinished
power/format integration. AudioRouter has newer upstream VM records missed by
its stale README; the inspected driver subtree matches that tested RC3 source.

The initial survey preferred MicDeck for conventional cables and AudioRouter for
private transport. That was a prototype priority, not a reliability conclusion.
The updated preference retains AudioRouter's Windows framework while replacing
transport; all changed transport behavior still requires fresh qualification.
Keep **Virtual Cables with usbip-win2** as a distinct architecture alternative.

No inspected project is established here as a ready, commercially distributable,
reliable replacement for both existing drivers. A clear licence and passing host
tests are useful gates, but neither establishes loaded-driver safety or quality.

## Scope and immutable source references

Eight additional repositories were shallow-cloned into a temporary research
directory. Relevant source, licences, driver declarations, build/test definitions
and documentation were inspected. The prior
[AudioMirror assessment](AUDIOMIRROR_ASSESSMENT.md) and Microsoft's sample
foundation provide two further comparison points. Repository timestamps are not
treated as proof of recent substantive driver development.

| Repository | Inspected commit | Initial assessment |
|---|---|---|
| [MicDeck](https://github.com/3godzinyL/Virtual-Soundboard-Audio-Windows-Mixer/tree/4ec7e53c0ac37291187d92f3d75bf88af469895c/drivers/micdeck-vad) | `4ec7e53c0ac37291187d92f3d75bf88af469895c` | First conventional-cable prototype candidate |
| [AudioRouter](https://github.com/MrDesjardins/audiorouter/tree/f63f491f6c12e5c7325e9a9dcd5666163caed371/drivers/audiorouter-virtual) | `f63f491f6c12e5c7325e9a9dcd5666163caed371` | First private-transport comparison candidate |
| [Virtual Cables](https://github.com/tarekwasfy01/Virtual-Cables/tree/8a819c29971d30a73955178ac8a3aac00b1a5230) | `8a819c29971d30a73955178ac8a3aac00b1a5230` | Alternative architecture; timing/dependency gate |
| [DeckX](https://github.com/Nactro-Development/DeckX-Virtual-Audio-Driver/tree/22d6a01efb2071e635c781d7a751d41e6da87696) | `22d6a01efb2071e635c781d7a751d41e6da87696` | Real transport, basic implementation; reserve |
| [Synchronous Audio Router](https://github.com/eiz/SynchronousAudioRouter/tree/494ed9a810a0a5bfbf25b62a34605628f635fe6a) | `494ed9a810a0a5bfbf25b62a34605628f635fe6a` | Real multi-endpoint design; ASIO integration burden |
| [OpenAudioCable](https://github.com/amanison/OpenAudioCable/tree/107b2ae7fc1377c6b67cdb1910e3d4fe1d34e189) | `107b2ae7fc1377c6b67cdb1910e3d4fe1d34e189` | Older experimental singleton; provenance review needed |
| [VirtualDrivers](https://github.com/VirtualDrivers/Virtual-Audio-Driver/tree/bb34fba15faf569a6ae9bdea360bc1cf4821354e) | `bb34fba15faf569a6ae9bdea360bc1cf4821354e` | Dummy devices, not an implemented cable at this revision |
| [Scream](https://github.com/duncanthrax/scream/tree/d789743c248b11d1df7e5ecc546b1bc60b90cd91) | `d789743c248b11d1df7e5ecc546b1bc60b90cd91` | Playback/network transport; different purpose |

The commits above were checked against the local Git checkouts. GitHub repository
metadata/tree responses are retained as local evidence. This pass did not audit
every code path, contribution, dependency or release artifact.

## 1. MicDeck VAD

**Fit:** actual render-to-capture WaveRT/PortCls cable code, using the same general
WASAPI relationship as our current cables. Its portable core fixes the internal
rate at 48 kHz and implements frame-based buffering, sample conversion and
producer/consumer state. This is substantially more useful for our transport
requirements than AudioMirror's byte buffer.

Evidence is in
[`virtual_cable.cpp`](https://github.com/3godzinyL/Virtual-Soundboard-Audio-Windows-Mixer/blob/4ec7e53c0ac37291187d92f3d75bf88af469895c/drivers/micdeck-vad/driver/src/virtual_cable.cpp),
[`miniport_wave_rt_stream.cpp`](https://github.com/3godzinyL/Virtual-Soundboard-Audio-Windows-Mixer/blob/4ec7e53c0ac37291187d92f3d75bf88af469895c/drivers/micdeck-vad/driver/src/miniport_wave_rt_stream.cpp),
and the [portable core](https://github.com/3godzinyL/Virtual-Soundboard-Audio-Windows-Mixer/tree/4ec7e53c0ac37291187d92f3d75bf88af469895c/drivers/micdeck-vad/shared).
I compiled and ran its two existing portable test executables, with assertions
enabled: **both passed**. This exercises buffer/conversion behavior, a
producer/consumer stress case, priming, stale-data trimming, fades and flushes.

**Readiness limit:** the checked-in
[`VALIDATION.json`](https://github.com/3godzinyL/Virtual-Soundboard-Audio-Windows-Mixer/blob/4ec7e53c0ac37291187d92f3d75bf88af469895c/drivers/micdeck-vad/VALIDATION.json)
and `VALIDATION_V3.json` explicitly report no WDK driver build, Windows audio
runtime test or production signing in that validation environment. Included
verification scripts are not completed verification. Our host tests add no
loaded-driver evidence.

**Licence:** driver-specific
[MIT](https://github.com/3godzinyL/Virtual-Soundboard-Audio-Windows-Mixer/blob/4ec7e53c0ac37291187d92f3d75bf88af469895c/drivers/micdeck-vad/LICENSE.md).
Its notices describe an original implementation using Microsoft's documented
interfaces; source ancestry still needs checking before adoption. The overall
application's other dependencies need not be imported to use just this driver.

**VineMix work:** currently one static cable/clock in `adapter.cpp`. Refactor
ownership into two independent cable instances, create unique endpoint identities,
and verify teardown/failure handling, supported formats and shared-mode clients.
Then integrate role-aware device discovery in VineMix. Successful host-side
atomic code does not validate the separate kernel atomic implementation.

## 2. AudioRouter's virtual driver component

**Fit:** an actively developed sample derivative with explicit source provenance
and a private, bounded shared-memory bridge. Its current
[`minipairs.h`](https://github.com/MrDesjardins/audiorouter/blob/f63f491f6c12e5c7325e9a9dcd5666163caed371/drivers/audiorouter-virtual/Source/Filters/minipairs.h)
defines **eight render/capture pairs, with two enabled by default**. Registration
and configuration code use that count. This is implemented source structure,
not proof those endpoints have been qualified on Windows. Some introductory
README/roadmap text still describes an earlier prototype; source was used to
resolve the discrepancy.

The callbacks publish rendered audio to the engine and consume engine-supplied
capture audio through the
[bridge](https://github.com/MrDesjardins/audiorouter/blob/f63f491f6c12e5c7325e9a9dcd5666163caed371/drivers/audiorouter-virtual/Source/Inc/bridgeio.h).
This is not a drop-in conventional cable. Using that unchanged architecture
would require VineMix's own matching user-mode bridge implementation. A
deliberately reduced device layout could support the two-public-endpoint
architecture, subject to a separate design. The deeper review instead recommends
internal driver loopback for the confirmed autonomous-cable requirement.

**Offline evidence:** its existing bridge suite passed **88,167 checks** here.
The first LLVM build failed on two missing SAL annotation macros. Rebuilding
with no-op definitions for `_In_reads_bytes_(n)` and `_Inout_` passed, without
editing upstream source. This is an alternative host compiler invocation, not
proof that the documented MSVC/WDK build works. Test count and host copy timings
are not measures of kernel stability or audio latency.

**Licence boundary:** the application root is GPL-3.0-only, but the driver
directory is explicitly a separate
[MS-PL component](https://github.com/MrDesjardins/audiorouter/blob/f63f491f6c12e5c7325e9a9dcd5666163caed371/drivers/audiorouter-virtual/LICENSE-MS-PL.txt).
Its README identifies the Microsoft sample revision. Limit reuse to files whose
licensing has been checked; importing application/broker code from elsewhere
requires its own review. Commercial use is allowed under MS-PL's conditions.

**Readiness correction from the deeper review:** its driver README is stale.
The same pinned source contains
[newer VM execution records](https://github.com/MrDesjardins/audiorouter/blob/f63f491f6c12e5c7325e9a9dcd5666163caed371/docs/plans/active/virtual-cable.md)
covering streaming, installation/removal, Verifier, fuzzing and isolation. The
entire driver subtree matches the claimed RC3 tested source. These are upstream
self-reported results without accessible raw archives; continuity/latency,
physical hardware, real applications and production release gates remain open.
The detailed comparison separates these records from tests reproduced here.

## 3. Virtual Cables using usbip-win2

**Fit:** a genuinely different approach. The Go application emulates USB Audio
Class devices through a local USB/IP server; Windows' USB audio driver exposes
the playback/recording endpoints. Public source supports up to 32 independent
cables at stereo 48 kHz, 16-bit PCM. Two cables match our functional scope.

**Commercial licensing:** the application is
[BSD-2-Clause](https://github.com/tarekwasfy01/Virtual-Cables/blob/8a819c29971d30a73955178ac8a3aac00b1a5230/LICENSE.txt),
and the declared transport is also BSD-2-Clause, with additional Go/rsrc notices.
The author selling a newer Store version does not remove the inspected public
source's BSD permissions. Reconstruct and verify the complete dependency/package
notice set before distribution.

**Why investigate it:** it may avoid maintaining our own audio kernel driver
if a suitable unchanged signed USB/IP package can be used. It is still dependent
on third-party kernel software and an always-running user-mode device server.
The source, not just an installer binary, is present under `src/`.

**Limits:** upstream labels it experimental and reports memory-error/clock
improvements in its paid newer version. Those claims were not reproduced or
verified against the public version. The source includes buffer/descriptor/
protocol tests, but no Go toolchain was available here, so they were not run.
Its 250 ms default ring capacity is a capacity setting, not measured latency.

The independent [usbip-win2 project](https://github.com/vadimgrn/usbip-win2)
advertises WHLK/attestation-signed packages and documents its UDE/filter transport.
That is upstream information, not signature validation of a selected installer
here. It also documents that installation restarts USB hubs. Evaluate package
identity, supported USB/IP protocol version, Windows security compatibility,
audio timing, reconnects and USB-device disruption before selecting it. A local
server does not require sending users' audio over their external network.

## 4. DeckX Virtual Audio Driver

**Fit:** it does implement real render-to-capture transport, despite initial
screening by sample-like filenames. Source calls `DeckX_WriteToLoopback` from
render and `DeckX_ReadFromLoopback` from capture. It is not the silence-only
VirtualDrivers implementation.

However,
[`minwavertstream.cpp`](https://github.com/Nactro-Development/DeckX-Virtual-Audio-Driver/blob/22d6a01efb2071e635c781d7a751d41e6da87696/Source/Main/minwavertstream.cpp)
uses one global 64 KiB ring, lazy shared lock initialization and an internal
16-bit representation even for wider input samples. Sample precision, rate/
channel consistency, independent instances and stream reset need review; no
kernel failures were reproduced. No dedicated driver test suite was found in
the tracked-file inventory.

The repository has an MIT licence plus explicit Microsoft MS-PL notices. Included
`.sys` and `.cat` files returned **Valid ordinary Authenticode signatures**, with
SignPath Foundation as signer, in a read-only check. This does not prove Microsoft
kernel-signing acceptance, catalog membership, source-to-binary correspondence,
or acceptance of any modified build. The build workflow can fall back to test
signing. Do not repeat its README's production-readiness/latency claims as verified
facts. Reserve as a source reference, below MicDeck/AudioRouter for a first spike.

## 5. Synchronous Audio Router (SAR)

**Fit:** real driver code with dynamic playback/recording endpoints, shared
transport and synchronization to a physical ASIO interface. This is useful
architecture, but VineMix currently uses WASAPI. Its normal topology would add
ASIO-host/physical-interface assumptions; extracting and replacing that clock and
transport contract is substantial work.

**Licence:** GPL-3.0-or-later headers in core driver sources. Selling GPL-covered
software is allowed, with corresponding-source and other obligations when
distributing it. Do not treat GPL as a commercial-use ban. The effect on closely
integrated user-mode code needs a specific review, not an assumption that the
whole of VineMix automatically changes licence.

Its repository has a newer unsigned build/package pipeline, while the README
still describes Secure Boot limitations. Pipeline existence and proposed signing
services do not establish signed Windows 11 readiness. Useful reference; lower
priority than a design aligned to our existing engine.

## 6. Other candidates screened

- **OpenAudioCable:** actual SysVAD-based loopback implementation, including a
  static quasi-singleton and kernel ring allocation. Default branch dates to
  September 2022. Root GPL-3.0 and inherited Microsoft source need a provenance
  and compatibility review before reuse. Less complete documentation and test
  evidence than the shortlist; not selected for a deep repair exercise.
- **VirtualDrivers / MikeTheTech:** MIT additions and explicit MS-PL notices are
  helpful, but the pinned public
  [`WriteBytes`](https://github.com/VirtualDrivers/Virtual-Audio-Driver/blob/bb34fba15faf569a6ae9bdea360bc1cf4821354e/Source/Main/minwavertstream.cpp#L1392)
  capture path fills silence; render follows a sample diagnostic save path.
  This revision does not implement the cable/injection path we need. A paid
  custom-build offering is separate from the published implementation.
- **Scream:** MS-PL, real playback-to-network transport. It does not provide our
  required recording-device return path. Adding that and replacing network
  transport would be substantial repurposing. Upstream's Windows 11/test-mode
  guidance is not an installation approach we adopted.
- **AudioMirror:** real cable code, but the prior assessment reproduced buffer
  defects and identified cleanup/removal issues. Retain as a reference, with a
  lower prototype priority after this broader survey.
- **Microsoft SimpleAudioSample/SysVAD:** the official MS-PL foundation remains
  a fallback if third-party prototype repair exceeds the cost of building a
  narrowly scoped transport on a current sample. Device enumeration alone is not
  an implemented cable. See the Microsoft references in the earlier assessment.

Applications that only route into VB-CABLE, noncommercial-only source, macOS-only
drivers, and DirectShow-only inputs were not treated as replacements for a normal
Windows recording/playback device pair.

## What this changes for VineMix

| Architecture | Best candidate to prototype | Application work | Principal unknown |
|---|---|---|---|
| Two autonomous conventional cables in one package | AudioRouter framework plus new internal loopback, following the deeper review | Retain WASAPI; generalize transport roles; add two internal transport contexts | New transport concurrency/reset behavior and complete Windows qualification |
| Public devices with private engine transport | AudioRouter driver | Implement bounded shared-memory protocol, ownership and recovery in our native engine | Loaded bridge behavior and complete source/licence boundaries |
| Emulated USB audio with existing transport | Virtual Cables | Supervise local server and device attach/reconnect; retain WASAPI | Clock behavior, latency and exact USB/IP package qualification |

The common requirements in the
[AudioMirror development assessment](AUDIOMIRROR_ASSESSMENT.md#development-work-and-acceptance-gates)
still apply: stable role identities, no saved-binding reset, both directions
simultaneously, mixed shared-mode clients, correct silence and stale-data policy,
sleep/reboot/removal recovery, measured latency/glitches, clean install/upgrade/
rollback, and a maintainable signed package. None of these projects eliminates
that verification work.

**Next useful investigation:** follow the bounded two-cable feasibility gate in
the [deeper comparison](VIRTUAL_DRIVER_FOUNDATION_COMPARISON.md), including its
native compilation findings and the remaining loaded-driver tests. Keep Virtual
Cables as a separate architecture experiment if reducing audio-specific kernel
ownership is a priority. The months-scale release estimate remains uncertain.

## Verification and local evidence

Host checks used the existing LLVM-MinGW compiler, with no installation of new
toolchains. Two MicDeck executables passed with runtime assertions enabled;
AudioRouter's bridge suite passed using the two annotation definitions described
above. Ordinary Authenticode inspection of DeckX artifacts was read-only.

No full kernel driver was built in this **initial survey pass**; the deeper
comparison records subsequent direct MSVC/WDK compilation separately. No driver
was loaded, no installer ran, and no Windows audio or security setting changed.
Go and live audio tests were not performed. No upstream maintainer was messaged. No complete
security audit, commercial legal clearance or binary provenance audit is claimed.

Selected metadata, pinned revisions, build/run logs and result summaries are
retained under `artifacts/driver-survey-20261020/` as ignored local evidence.
This report updates IDEA-014 as research; it does not select or vendor a driver.
