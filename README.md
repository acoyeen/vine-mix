# Audio Mixer

A Windows 11 audio mixer with a node workspace: connect microphones, applications,
effects and listening or virtual microphone outputs using visible audio cables.

![Audio Mixer workspace](docs/images/workspace.png)

## Features

- Microphone, virtual cable and per-application loopback inputs.
- Separate listening outputs and a virtual microphone output, with independent mutes.
- 23 creatable node types, including EQ, RNNoise / SpeexDSP denoising, dynamics,
  sidechain ducking, reverb, Voice Color and alignment delay.
- Up to 32 nodes and 96 connections; draggable controls, searchable nodes,
  copy/paste, Undo/Redo and automatic settings backups.
- Local processing: a C++20 engine handles 48 kHz stereo audio; a .NET 10 WPF
  desktop edits the graph and displays meters over a current-user named pipe.

Version **0.2.0** is experimental. Offline verification covers synthetic processing
and detached desktop controls. Audible routing, device recovery and long-duration
hardware qualification remain incomplete; see [testing](TESTING.md).

## Build and run

Requires **Windows 11 x64**, **PowerShell 7** and an internet connection for the
first build. From a checkout, run:

```powershell
pwsh -File .\scripts\build.ps1
.\artifacts\AudioMixer\AudioMixer.exe
```

The build downloads pinned project-local .NET, LLVM-MinGW and CMake tools, plus
the audio libraries and RNNoise model. It builds a self-contained desktop package;
users do not need to install .NET. No audio drivers are installed by the build.
Keep the entire package together, including its `licenses` directory.

Later builds can use `-NoBootstrap`. If the app is running, stage a separate package:

```powershell
.\scripts\build.ps1 -NoBootstrap -PackageDirectory "$PWD\artifacts\AudioMixer-Candidate"
```

Launch with `--preview` to explore a silent example graph without starting the
engine or saving settings. Closing the normal window hides it to the tray; use
**Quit Audio Mixer** from its tray menu to stop audio. New outputs start muted
and require device assignment.

Physical devices and application capture can be used without virtual drivers.
Virtual nodes require separately installed [VB-CABLE](https://vb-audio.com/Cable/)
and [Virtual Audio Cable](https://vac.muzychenko.net/en/download.htm).
Their licences are separate; VAC Lite has private-home/non-income restrictions.
Read the [distribution guide](docs/DISTRIBUTION.md) before sharing a package.

## Documentation

- [User guide and shortcuts](docs/USER_GUIDE.md)
- [Node workspace and architecture](docs/NODE_WORKSPACE.md)
- [Application capture and routing](docs/APP_AUDIO.md)
- [Device visibility and recovery](docs/DEVICE_VISIBILITY.md)
- [Effects and limitations](docs/EFFECTS_RESEARCH.md)
- [Dynamics, ducking and delay](docs/CORE_DYNAMICS.md)
- [Control protocol](docs/CONTROL_PROTOCOL.md)
- [Testing](TESTING.md), [contributing](CONTRIBUTING.md) and [changelog](CHANGELOG.md)
- [Third-party credits](THIRD_PARTY_NOTICES.md) and [distribution requirements](docs/DISTRIBUTION.md)

The original application currently has [all rights reserved](LICENSE), pending
the owner's choice of a distribution licence. Third-party components keep their
own licences and required credits.

Settings live at `%LOCALAPPDATA%\AudioMixer\settings.json`. Application capture
copies existing playback; it does not silence the original application. Parallel
paths have no automatic latency compensation. The limiter limits sample peaks
without lookahead or true-peak detection.

## Verification

```powershell
.\scripts\verify-offline.ps1
```

The suite builds and runs synthetic native tests and detached WPF checks, using
temporary settings and simulated devices. It does not access personal audio
devices or connect to the running mixer. Results and renders go under `artifacts/`.
The GitHub Actions workflow runs the same suite on Windows.

Tool downloads, builds, packages, settings and accumulated design renders are
excluded from Git. Older review documents describe local evidence paths; those
files can be reproduced with the verification tools.
