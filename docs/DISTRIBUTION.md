# Distribution and attribution

Reviewed 19 October 2026 against the dependencies and assets in this repository.
Font, branding and installer notices updated 20 October 2026; beta package naming
and driver information reviewed 21 October 2026.
This records VineMix's distribution setup and its limits. The full upstream
licences, rather than this summary, govern each component.

## Source repository

Keep the root `LICENSE`, `THIRD_PARTY_NOTICES.md`, the complete `licenses/`
directory and `IBM-Plex-LICENSE.txt` under
`desktop/VineMix.Desktop/Assets/Fonts/` in source copies. The root MIT licence
covers VineMix's original material; it does not relicense the embedded fonts,
dependencies, runtime components or cited
third-party material. Preserve upstream copyright and licence headers if
vendoring dependency source in future.

VineMix's original code and documentation are released under the MIT licence,
copyright (c) 2026 acoyeen. It permits reuse, modification and redistribution,
including commercial distribution, subject to its terms. Keep its copyright
and permission notice in copies or substantial portions of that material.
Third-party components retain their own terms and notices alongside it.

The copyright notice was expanded on 28 October 2026 to identify VineMix and
`https://vinemix.app` alongside acoyeen. Preserve the complete notice supplied
with the source you use. [Crediting VineMix](../ATTRIBUTION.md) explains the MIT
requirement and requests a short acknowledgement in modified programs. The
requested interface placement is not an additional licence condition. Earlier
releases keep their original notices and permissions.

The repository excludes local build tools, downloaded archives, compiled
packages, user settings, activation backups and historical design captures.
Those folders can contain private endpoint identifiers, process names, machine
paths and separately licensed downloads. Only deliberately selected screenshots
of the application belong in public documentation. In particular, the archived
Blender Manual reference image is excluded; its attribution is retained in the
third-party notices for anyone later sharing that local reference.

## Application packages

Build into a staged directory using `scripts/build.ps1`. Before distributing a standalone EXE
or installer, verify the final archive contains:

- `VineMix.exe` and `VineMix.Engine.exe`, and the native runtime DLLs
  required by that build;
- the root `LICENSE` and `THIRD_PARTY_NOTICES.md`;
- every checked-in file under `licenses/`, including vendor source-file
  acknowledgements, Microsoft sample attribution, W3C EQ acknowledgement,
  IBM Plex and Inter OFL and runtime notices;
- `Assets/Fonts/LICENSE.txt` and `Assets/Fonts/IBM-Plex-LICENSE.txt` beside the
  self-contained desktop publish output;
- the user guides, including this document.

Do not trim the notices to a short list of project names. BSD source and binary
redistribution conditions, MIT permission notices, the OFL and the runtime's
component terms require preserving the applicable text and copyrights. Some
compiled sources have additional notices beyond their project's root licence.
The package should keep them together even though dependencies are statically
linked or embedded inside a single executable.

The checked-in notices correspond to libsamplerate 0.2.2, SpeexDSP 1.2.1,
RNNoise v0.2/default model `0b50c45`, nlohmann/json 3.12.0, IBM Plex Sans 3.005
and Mono 2.005, the retained Inter 4.1 assets,
LLVM-MinGW 20261006 and the .NET 10.0.13 runtime packages. The .NET runtime
licences are taken from `Microsoft.NETCore.App.Runtime.win-x64` and
`Microsoft.WindowsDesktop.App.Runtime.win-x64`; their licence is MIT. The .NET
SDK's separate `LICENSE.txt` is a build-tool licence and is not a substitute for
those runtime licences. Matching runtime, WPF and Windows Forms third-party
notices are included as well.

The compiler, CMake, .NET SDK, RNNoise training checkpoint and datasets are not
part of the application distribution. The current compiled audio libraries use
permissive licences; the IBM Plex and Inter fonts remain under OFL. The archived
Blender image has separate CC BY-SA terms and must keep those terms and attribution if
later published. Do not apply the application licence to that image.

The current interface uses IBM Plex Sans for labels and IBM Plex Mono for
numerical values and shortcuts, with VineMix's forest-green split VM symbol
for application and tray identity. Font files are embedded, so users do not
need to install a system font. Keep the font notices even though the files are
inside the executable. The source/version/hash record is in
[IBM_PLEX_FONTS.md](IBM_PLEX_FONTS.md). Inter assets remain embedded and retain
their existing notices. Beta 1 renames the executable and internal product
identity to VineMix and copies legacy AudioMixer settings into
`%LOCALAPPDATA%\VineMix` with a restartable migration. Original data is preserved.
The standalone EXE embeds a complete application payload; the verification
executable and test/render implementation are excluded from release packages.

## Drivers and commercial use

The standalone application package contains no driver installers. The Windows
setup built by `scripts/build-installer.ps1` includes the complete original base
VB-CABLE Pack45 and VAC Lite 4.71 packages, restricted to the current **free,
non-commercial distribution**. `installer/drivers.json` pins official URLs,
archive/file hashes, hardware identities and reviewed vendor signatures. The
preparation script verifies these before building; Setup retains their original
packages and runs only missing, selected drivers through their interactive
vendor installers. Existing drivers, including disabled/staged packages, are
preserved. Driver installation is never part of unattended application setup.

The application's MIT licence grants no extra rights to a driver or another
vendor's branding. Keep the vendor credits, donation links and usage conditions
visible in Setup, its required terms and installed driver notices. The driver
packages are not published as source-code dependencies in this repository.

[VB-Audio](https://vb-audio.com/Services/licensing.htm) publishes specific
donationware, professional-use and distribution conditions. Its A+B/C+D driver
packages have separate published bundling restrictions. The maintainer has obtained
VB-Audio permission for this project's current integrated base-driver flow;
preserve that permission record when distributing it.
[VAC Lite](https://vac.muzychenko.net/en/license.txt) is restricted to private
home use without income generation, and its
[distribution policy](https://vac.muzychenko.net/en/manual/distrib.htm) permits
unchanged non-commercial redistribution. A source release of VineMix does not
require distributing either driver; a commercial user must choose a driver
licence suitable for that user's activity. This installer is not cleared for
paid, sponsored or donation-supported redistribution. Reassess the exact vendor
conditions before changing that model; free/open-source status alone does not
resolve commercial purpose or a customer's income-generating use.

Inno Setup 7.1.0 builds the installer. Its
[licence](https://jrsoftware.org/files/is/license.txt) permits use for any purpose,
including commercial applications, with its distribution conditions. The tool's
commercial purchase request is distinct from that published permission grant.
The pinned signed compiler is prepared in project-local portable mode; it is
not part of the application's runtime. Its licence is retained in SetupSupport.
The self-contained setup helper uses the same .NET runtime licensing already
included with the application package.

## Dependency changes and release checks

The current development executables are unsigned: Authenticode inspection of
the development desktop and engine reports `NotSigned`. The beta app, helper,
standalone launcher and Setup remain unsigned until exact-artifact inspection
records otherwise. A publisher
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
