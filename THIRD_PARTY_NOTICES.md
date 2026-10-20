# Third-party notices

Vine Mix's original source is released under the MIT licence in the root
[LICENSE](LICENSE), copyright (c) 2026 acoyeen.
Third-party components retain the licences and copyright notices below; the
root licence does not replace them. Preserve this file and the complete
[`licenses/`](licenses/) directory when sharing the application. Release
packaging and update guidance is in [docs/DISTRIBUTION.md](docs/DISTRIBUTION.md).

## Components included in the application

| Component | Use | Licence and included notices |
|---|---|---|
| [libsamplerate 0.2.2](https://github.com/libsndfile/libsamplerate/tree/0.2.2) | Native streaming resampling | BSD-2-Clause; `licenses/libsamplerate-LICENSE` and `libsamplerate-source-notices.txt` |
| [SpeexDSP 1.2.1](https://downloads.xiph.org/releases/speex/) | Spectral noise suppression | BSD-3-Clause and source-file BSD notices; `licenses/SpeexDSP-COPYING.txt` and `SpeexDSP-source-notices.txt` |
| [RNNoise v0.2](https://github.com/xiph/rnnoise/tree/v0.2) and matching default model `0b50c45` | Neural noise suppression | BSD-3-Clause and source-file BSD notices; `licenses/RNNoise-COPYING.txt` and `RNNoise-source-notices.txt` |
| [nlohmann/json 3.12.0](https://github.com/nlohmann/json/tree/v3.12.0) | Configuration and control messages | MIT; `licenses/nlohmann-json-LICENSE` and `nlohmann-json-third-party-notices.txt`, including Florian Loitsch's Grisu2 and Evan Nemerson's Hedley credits |
| [IBM Plex](https://github.com/IBM/plex/tree/71f558992315a12ca1f06a2215d57962a1992162): Sans 3.005 Regular, Medium and SemiBold; Mono 2.005 Regular | Embedded interface typography and numeric readouts | SIL Open Font License 1.1; `licenses/IBM-Plex-OFL.txt` and `Assets/Fonts/IBM-Plex-LICENSE.txt`; [asset provenance](docs/IBM_PLEX_FONTS.md) |
| [Inter 4.1](https://github.com/rsms/inter/releases/tag/v4.1) Regular and SemiBold | Retained embedded legacy font assets; the current interface uses IBM Plex | SIL Open Font License 1.1; `licenses/Inter-OFL.txt` and `Assets/Fonts/LICENSE.txt` |
| .NET 10.0.13, WPF and Windows Forms | Self-contained desktop runtime | MIT and component terms; `licenses/dotnet-runtime-LICENSE.txt`, `dotnet-windowsdesktop-LICENSE.txt`, and the `dotnet-*-ThirdPartyNotices.txt` files |
| [LLVM-MinGW 20261006 UCRT x86_64](https://github.com/mstorsjo/llvm-mingw/releases/tag/20261006) | Native C++/unwind runtime, compiler support and Windows runtime support | Apache-2.0 with LLVM exceptions, legacy LLVM terms, MinGW-w64 and component terms; `licenses/LLVM-runtime-LICENSE.txt`, `MinGW-w64-COPYING.txt`, `MinGW-runtime-COPYING.txt`, and `winpthreads-COPYING.txt` |

The source-file notice collections preserve additional authors and licence
variants from the actual compiled vendor sources and headers. In particular,
upstream root `COPYING` files alone do not list every RNNoise or SpeexDSP author.
The checked-in .NET notices come from the runtime packages and the matching WPF
and Windows Forms releases, rather than the SDK licence. SDKs, compilers, CMake,
training datasets, training checkpoints and virtual audio drivers are not
included in the application package.

IBM Plex is unmodified; only the local filenames differ from upstream.
Copyright © 2017 IBM Corp., with Reserved Font Name "Plex". Sans is used for
interface text and Mono for numerical values and shortcuts. Inter is also
unmodified. Copyright (c) 2016 The Inter Project Authors. Both families are
embedded in the desktop application; they are not installed system-wide. Their
OFL terms remain attached to the fonts and do not change the licence of the
application source. The forest-green split VM symbol is Vine Mix's application
branding; the use of IBM Plex does not imply IBM's endorsement.

### Pinned denoiser provenance

SpeexDSP source:
`https://downloads.xiph.org/releases/speex/speexdsp-1.2.1.tar.gz`.
SHA-256: `8c777343e4a6399569c72abc38a95b24db56882c83dbdb6c6424a5f4aeb54d3d`.
The build compiles upstream preprocessor, FFT, filter-bank and echo-support
sources; echo cancellation is not enabled. The local
`native/vendor/speex_preprocess_bridge.c` includes the unmodified preprocessor
and adds bounded state copying. Local allocation hooks instrument heap operations
for offline verification. These additions are separate from the upstream files.

RNNoise source: `https://codeload.github.com/xiph/rnnoise/tar.gz/refs/tags/v0.2`.
SHA-256: `e4a17eb1493b7551f9f7781d2acc67b8847d6ed7c4b64db0fd62c017021ae1ec`.
The matching default model is the archive selected by upstream v0.2's
`model_version` and `download_model.sh`:
`https://media.xiph.org/rnnoise/models/rnnoise_data-0b50c45.tar.gz`.
SHA-256: `4ac81c5c0884ec4bd5907026aaae16209b7b76cd9d7f71af582094a2f98f4b43`.
Only the generated `rnnoise_data.c` and header are compiled into the application;
the training checkpoint and datasets are not distributed. This is the upstream
default model distributed for RNNoise, used with the project's BSD notices; no
third-party replacement model is added. Model and code are fetched at build
time. No runtime model download is required. State copying is audited against
this exact source/model pair and uses the public state-size API with the default
immutable model only.

## Windows application capture reference

The process-loopback activation sequence is informed by Microsoft's
[ApplicationLoopback sample](https://github.com/microsoft/Windows-classic-samples/blob/434f6002bdf9cf9829406c3ff2b33387982d6168/Samples/ApplicationLoopback/cpp/LoopbackCapture.cpp),
reviewed at repository commit `434f6002bdf9cf9829406c3ff2b33387982d6168`.
Copyright (c) Microsoft Corporation. The sample repository's complete MIT notice
is preserved in `licenses/Microsoft-Windows-classic-samples-LICENSE.txt` as an
acknowledgement of this implementation reference.

Vine Mix's `native/src/process_capture.cpp` implements its own discovery,
callback lifetime, interruptible capture worker, queues and recovery. The local
`ProcessActivationParams` declaration represents the documented Windows ABI's
values and layout because the build's MinGW headers lack that declaration. The
Microsoft SDK header itself is not redistributed. The sample's MIT licence does
not purport to relicense Microsoft's SDK headers; the
[win32metadata licence disclaimer](https://github.com/microsoft/win32metadata/blob/main/LICENSE)
retains those headers' original terms.

## Algorithm acknowledgements

The EQ calculations and response display implement the formulae described in
Robert Bristow-Johnson's **Audio EQ Cookbook**, adapted for W3C by Raymond Toy.
Source: [W3C Working Group Note, 8 June 2021](https://www.w3.org/TR/2021/NOTE-audio-eq-cookbook-20210608/).
Copyright (c) 2021 W3C (MIT, ERCIM, Keio, Beihang).
The implementation adapts these formulae to C++/C#, parameter bounds,
normalization and the application's response display. The W3C Software and
Document License 2015 notice is in
`licenses/W3C-Audio-EQ-Cookbook-NOTICE.txt`.

The de-esser and plosive processor use dynamic shelving attenuators with separate
band-limited detectors. Their topology-preserving state-variable filters follow
the mathematical derivation and shelf mappings published by
[Andrew Simper, Cytomic, 2013](https://www.cytomic.com/files/dsp/SvfLinearTrapOptimised2.pdf).
The de-essing use case is informed by
[MathWorks' split-band de-essing explanation](https://www.mathworks.com/help/audio/ug/SplitBandDeEssing.html).
The reverb uses passive scattering delay paths and allpass diffusion, informed
by Julius O. Smith's descriptions of
[Schroeder allpass sections](https://www.dsprelated.com/freebooks/pasp/Schroeder_Allpass_Sections.html)
and [their transfer function](https://www.dsprelated.com/freebooks/pasp/Allpass_Two_Combs.html).
These publications are linked as references; their PDFs, manual text and example
programs are not bundled. No MathWorks or Freeverb implementation source is
included in these effects.

## Historical design reference

The local development archive contains an unchanged Blender Manual reference
image at `docs/design/blender-reference/node-parts-overview.png`. Historical
design captures and this reference image are excluded from the published
repository and application package. If redistributing the reference image
separately, retain this attribution:

> Blender Documentation Team, Blender Manual, "Node Parts" overview image.
> [Original page](https://docs.blender.org/manual/en/latest/interface/controls/nodes/parts.html),
> [original image](https://docs.blender.org/manual/en/latest/_images/interface_controls_nodes_parts_overview.png).
> Unchanged. [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/),
> subject to the [manual's stated exceptions](https://docs.blender.org/manual/en/latest/copyright.html).

Blender implementation code and branding are not embedded in Vine Mix.

## Separately obtained virtual audio drivers

Vine Mix does not redistribute or install VB-CABLE, VB-CABLE A+B/C+D, or
Virtual Audio Cable (VAC). Obtain any driver separately from its publisher and
follow that publisher's licence for the intended use.

- [VB-Audio licensing](https://vb-audio.com/Services/licensing.htm): VB-CABLE is
  donationware; professional use and bundling have additional conditions.
  VB-CABLE A+B and C+D may not be bundled with another product under the
  publisher's published terms.
- [VAC licence](https://vac.muzychenko.net/en/license.txt): the Lite/freeware
  version is restricted to private home use without income generation. Other
  versions have their own terms. Vine Mix's licence does not expand those
  permissions.

The names of upstream projects and publishers identify components and sources;
they do not imply endorsement.
