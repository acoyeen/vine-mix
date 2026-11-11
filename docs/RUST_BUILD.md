# Building and verifying the Rust candidate

This is the contributor entry point for the Rust rewrite of VineMix on the
`rust-rewrite` branch. It describes what the code does today. Design intent
and proposals live in [the rewrite plan](RUST_REWRITE_PLAN.md); open defects
and review findings live in [the 10 November 2026 review](RUST_REVIEW_2026-11-10.md)
and [the known-issues log](KNOWN_ISSUES.md).

The Rust candidate has not been released. The root `README.md`, `TESTING.md`
and the distribution guides describe the released 0.5 application, which is
.NET (WPF) and C++. Both trees live in this repository; the 0.5 sources remain
the behavioural reference.

## What is in the workspace

| Crate | Role |
|---|---|
| `vinemix-model` | Configuration schema, defaults, validation, graph compilation, device identity rules |
| `vinemix-rt` | Realtime primitives: frames, queues, timing policy |
| `vinemix-dsp` | Graph processor and effects |
| `vinemix-denoise`, `vinemix-resample` | Rust translations of the pinned SpeexDSP, RNNoise and libsamplerate sources |
| `vinemix-wasapi` | Windows audio devices, process capture, device catalog |
| `vinemix-engine` | `VineMix.Engine.exe`: mixer thread, configuration store, control pipe |
| `vinemix-ui` | `VineMix.exe`: the egui desktop, update client and worker |
| `vinemix-update` | Update manifest verification and HTTP transport |
| `vinemix-platform` | Release metadata and Windows paths |
| `vinemix-setup-helper` | Setup detection helper |
| `vinemix-oracle` | Test only: the unmodified 0.5 C/C++ DSP, compiled as a parity reference |
| `vinemix-ipc` | Placeholder; contains no code yet |

Facts that differ from older planning text:

- The desktop uses egui and eframe 0.37 with the **glow** (OpenGL) backend and
  AccessKit. It does not use wgpu.
- The desktop and engine are two processes that speak control protocol
  **version 1** by request and reply over a named pipe. The desktop polls about
  30 times a second while its window is visible and once a second otherwise.
  There is no protocol version 2, no pushed telemetry and no edit patches. Two
  optional request fields reduce repeated work; see
  [the control protocol](CONTROL_PROTOCOL.md).
- Shipped executables contain Rust code only. C and C++ are compiled solely
  for the parity tests.

## Prerequisites

- Windows 10 or 11, x64.
- [rustup](https://rustup.rs). `rust-toolchain.toml` pins the exact Rust
  release and installs the build target, Clippy and rustfmt.
- The pinned LLVM-MinGW under `.tools`, which provides the linker, the C
  compiler and the resource compiler:

  ```powershell
  .\scripts\bootstrap-tools.ps1
  ```

- For verification: Git, Python 3 (the `py` launcher), the pinned .NET SDK
  (also installed by `bootstrap-tools.ps1`) and the parity reference sources:

  ```powershell
  .\scripts\fetch-reference-sources.ps1
  ```

The build target and the LLVM-MinGW directory name are pinned in
`tools\rust-pins.json`. `scripts\rust-env.ps1` checks that the Rust release,
the target and those exact tools are the ones in use, and puts them first on
`PATH`. Another `llvm-mingw-*` directory under `.tools` is never used instead.

The target is `x86_64-pc-windows-gnullvm` whatever your Rust host is. With a
`gnullvm` host the pinned LLVM-MinGW is all you need. With the common
`x86_64-pc-windows-msvc` host, Cargo compiles build scripts and procedural
macros for that host, so the Microsoft C++ build tools must also be installed.
The MSVC-host path has not been exercised in this repository; continuous
integration sets the `gnullvm` host.

## Build

```powershell
. .\scripts\rust-env.ps1
$rust = Initialize-RustEnvironment -Project (Get-Location)
cargo build --locked --target $rust.target
```

Output goes to `target\x86_64-pc-windows-gnullvm\debug` (or `release`). A bare
`cargo build` uses your host's default target and writes to `target\debug`;
that is convenient on a `gnullvm` host but is not what releases are built with.

To look at the interface without an engine or any saved data:

```powershell
cargo run --locked --target $rust.target -p vinemix-ui -- --preview
```

To run a complete desktop and engine beside an installed copy, set
`VINEMIX_DEV_INSTANCE` to a name first. That gives the instance its own pipe,
mutexes and data folder (`%LOCALAPPDATA%\VineMix.dev-<name>`), and it never
installs updates or registers itself to start at login. Follow the launch and
settings rules in `AGENTS.md` before starting anything against personal data.

## Verify

```powershell
.\scripts\verify-rust.ps1
```

This is the qualification gate for the Rust candidate. It:

1. checks the pinned toolchain, target and tools;
2. tests its own helpers, including that injected failures are rejected;
3. checks that `licenses\Rust-crates.txt` matches the locked dependencies;
4. runs Clippy, denying correctness, suspicious and performance lints,
   undocumented `unsafe` blocks and unused code (style and complexity lints
   are counted in the report but not denied);
5. builds and runs every test target on its own and compares each with
   `tools\rust-test-expectations.json`: a suite that is missing, unlisted,
   failing, below its expected count, or that skips a test not named there,
   fails the run;
6. requires the C/C++ parity oracle (`VINEMIX_REQUIRE_ORACLE=1` turns missing
   reference sources into a build failure);
7. cross-checks the update transaction records in both directions against the
   0.5 C# serializer.

Reports go to a new directory under `%LOCALAPPDATA%\VineMix.BuildCache`. Add
`-Offline` when every crate is already in Cargo's cache.

`-SkipLint`, `-SkipSerializerInterop` and `-AllowMissingReferenceSources` are
for machines without those tools. A run that uses one can pass, but it is
reported as a developer run and is not a qualification.

When you add, remove or rename tests, update
`tools\rust-test-expectations.json`. `.\scripts\verify-rust.ps1 -PrintObserved`
prints what the current tree runs as a starting point. Benchmarks are
`#[ignore]`d tests listed there by name; run one with, for example:

```powershell
cargo test --release -p vinemix-dsp --test realtime_alloc parameter_edit_cost -- --ignored --nocapture
```

Voice Color and Distortion are the two nodes that are not compared with the
0.5 engine: they are Rust designs ([VOICE_COLOR.md](VOICE_COLOR.md),
[DISTORTION.md](DISTORTION.md)). Their behaviour is held by
`crates\vinemix-dsp\tests\voice_color.rs` and `distortion.rs`. To listen to
either, or to measure it on a recording, run `.\scripts\render-voice-color.ps1`
or `.\scripts\render-distortion.ps1`, which write files under the ignored
`artifacts` folder and play nothing.

The verification script uses synthetic data only. It does not open an audio
device, read personal settings or start an application session. It does not
cover physical devices, an installed upgrade, listening tests or assistive
technology; those remain open in the review tracker.

Continuous integration runs the same script in the `rust` job of
`.github\workflows\verify.yml`. The `verify` job in that workflow builds and
tests the 0.5 application only.

## Release candidates

```powershell
.\scripts\build-rust-release.ps1 -SkipInstaller
```

The script builds the installed payload, the standalone executable and, without
`-SkipInstaller`, Setup, into a new staging directory outside the checkout.
Nothing is installed, published or run against personal data.

A release candidate must come from a source tree that matches its commit. The
script checks the tree before compiling and again afterwards, and stops if
they differ. `-AllowDirty` builds anyway; every identity then says `dirty` and
the receipt records `releaseCandidate: false`.

"Matches" means byte for byte. Git can call a file unmodified when only its
line endings were converted in the work tree, but the compiler reads the bytes
on disk and some files are embedded verbatim. The script therefore also
refuses a tree in which any build input has converted line endings, and names
one of the files. Build candidates from a checkout that follows
`.gitattributes` (LF).

Each run writes `VineMix-<version>-build-receipt.json` beside the candidates:
the commit, a SHA-256 digest of every build input, the Rust, Cargo and
LLVM-MinGW identities, the hashes of the lock file and pins, and the SHA-256
of every produced file. The desktop executables carry the same commit, input
digest and build time; the script reads them back through the inert
`--release-smoke` check and stops if they do not match.

What **About** shows as the source revision:

| Revision | Meaning |
|---|---|
| `<commit>` | Built by the release script from a tree identical to that commit |
| `<commit>-dirty` | Built by the release script with `-AllowDirty` |
| `<commit>-local` | Built by Cargo directly; the tree looked clean when the build script last ran |
| `<commit>-local-dirty` | Built by Cargo directly from a tree with changes |
| `unavailable` | Built outside a Git checkout |

## Third-party notices

`tools\rust-notices.py` writes `licenses\Rust-crates.txt` from the licence
files each shipped crate publishes; `--check` verifies the file without
changing it. The generator never writes a licence text of its own. A crate
that publishes none needs a reviewed supplement in
`licenses\rust-supplements\`, described in the `README.md` there; without one
the generator stops. Its fixture tests run with
`py -3 tools\test_rust_notices.py`.
