# Distribution and attribution

Reviewed 19 October 2026 against the dependencies and assets in this repository.
This records Vine Mix's distribution setup and its limits. The full upstream
licences, rather than this summary, govern each component.

## Source repository

Keep the root `LICENSE`, `THIRD_PARTY_NOTICES.md`, the complete `licenses/`
directory and `desktop/AudioMixer.Desktop/Assets/Fonts/LICENSE.txt` in source
copies. The root MIT licence covers Vine Mix's original material; it does
not relicense the embedded fonts, dependencies, runtime components or cited
third-party material. Preserve upstream copyright and licence headers if
vendoring dependency source in future.

Vine Mix's original code and documentation are released under the MIT licence,
copyright (c) 2026 acoyeen. It permits reuse, modification and redistribution,
including commercial distribution, subject to its terms. Keep its copyright
and permission notice in copies or substantial portions of that material.
Third-party components retain their own terms and notices alongside it.

The repository excludes local build tools, downloaded archives, compiled
packages, user settings, activation backups and historical design captures.
Those folders can contain private endpoint identifiers, process names, machine
paths and separately licensed downloads. Only deliberately selected screenshots
of the application belong in public documentation. In particular, the archived
Blender Manual reference image is excluded; its attribution is retained in the
third-party notices for anyone later sharing that local reference.

## Application packages

Build into a staged directory using `scripts/build.ps1`. Before publishing a ZIP
or installer, verify the final archive contains:

- `AudioMixer.exe` and `AudioMixer.Engine.exe`, and the native runtime DLLs
  required by that build;
- the root `LICENSE` and `THIRD_PARTY_NOTICES.md`;
- every checked-in file under `licenses/`, including vendor source-file
  acknowledgements, Microsoft sample attribution, W3C EQ acknowledgement,
  Inter OFL and runtime notices;
- `Assets/Fonts/LICENSE.txt` beside the self-contained desktop publish output;
- the user guides, including this document.

Do not trim the notices to a short list of project names. BSD source and binary
redistribution conditions, MIT permission notices, the OFL and the runtime's
component terms require preserving the applicable text and copyrights. Some
compiled sources have additional notices beyond their project's root licence.
The package should keep them together even though dependencies are statically
linked or embedded inside a single executable.

The checked-in notices correspond to libsamplerate 0.2.2, SpeexDSP 1.2.1,
RNNoise v0.2/default model `0b50c45`, nlohmann/json 3.12.0, Inter 4.1,
LLVM-MinGW 20261006 and the .NET 10.0.13 runtime packages. The .NET runtime
licences are taken from `Microsoft.NETCore.App.Runtime.win-x64` and
`Microsoft.WindowsDesktop.App.Runtime.win-x64`; their licence is MIT. The .NET
SDK's separate `LICENSE.txt` is a build-tool licence and is not a substitute for
those runtime licences. Matching runtime, WPF and Windows Forms third-party
notices are included as well.

The compiler, CMake, .NET SDK, RNNoise training checkpoint and datasets are not
part of the application distribution. The current compiled audio libraries use
permissive licences; the Inter font remains under OFL. The archived Blender
image has separate CC BY-SA terms and must keep those terms and attribution if
later published. Do not apply the application licence to that image.

## Drivers and commercial use

Distribute Vine Mix without driver installers or driver binaries. Users
obtain their chosen virtual audio driver directly from the vendor. The
application's source licence grants no extra rights to a driver or another
vendor's branding.

[VB-Audio](https://vb-audio.com/Services/licensing.htm) publishes specific
donationware, professional-use and distribution conditions. Its A+B/C+D driver
packages cannot be bundled with another product under the published terms.
[VAC Lite](https://vac.muzychenko.net/en/license.txt) is restricted to private
home use without income generation. A source release of Vine Mix does not
require distributing either driver; a commercial user must choose a driver
licence suitable for that user's activity.

## Dependency changes and release checks

The current development executables are unsigned: Authenticode inspection of
both `AudioMixer.exe` and `AudioMixer.Engine.exe` reports `NotSigned`. A publisher
who wants a verified signing identity must sign and timestamp the final
executables using their own certificate before making the distribution archive.
[Microsoft's SignTool documentation](https://learn.microsoft.com/en-us/windows/win32/seccrypto/signtool)
describes signing and signature verification. Recompute archive checksums after
signing or changing package contents. Signing is separate from dependency
licensing and from the still-incomplete hardware qualification.

When changing a dependency version, model, runtime or font, refresh its full
licence, per-file copyright notices and upstream provenance at the same time.
Rebuild the final package and inspect its notice files. For native dependencies,
the existing supplemental notice collections came from the actual source and
header dependencies listed by the compiled Windows x64 targets. Recheck that
list when compilation options or vendor source lists change. For .NET, compare
the restored runtime package version with the notice version above; do not
assume an SDK update selects the same runtime.

Do not describe a release as endorsed by Microsoft, Xiph, Blender, VB-Audio or
other upstream authors. Do not claim that dependency permission notices are a
patent clearance, a guarantee of original authorship, or a legal guarantee for
every jurisdiction. This review checked the visible source, component licence
files, build configuration and documented references; it cannot establish the
provenance of undocumented external material.
