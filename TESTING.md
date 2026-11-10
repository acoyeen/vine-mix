# VineMix testing

Use the [current beta qualification guide](docs/RELEASE_0.5.0_QUALIFICATION.md) for
commands, test scope and manual release gates. The
[0.5.0 Beta release notes](docs/releases/0.5.0-beta.md) identify the current
version and separate passed, failed and unperformed checks.

The current native suites and detached desktop checks never substitute for
clean-machine, actual Explorer/shortcut launch or sustained real-device testing.
The running personal audio engine must be preserved during ordinary development.

`scripts/verify-offline.ps1` uses strict performance budgets by default. Variable
hosted CI explicitly uses `-HostedBenchmarkReport`: creative benchmarks still
run, but measured budget overruns are reported separately from functional and
allocation failures. Review the benchmark JSON and CI summary; a green hosted
functional run is not a promise of real-time capacity on every computer.

The Rust rewrite on the `rust-rewrite` branch is verified separately by
`scripts/verify-rust.ps1`, described in
[Building and verifying the Rust candidate](docs/RUST_BUILD.md).
`scripts/verify-offline.ps1` compiles no Rust, so its result says nothing about
the Rust candidate, and the Rust script says nothing about the 0.5 application.

The previous testing document, including the settings-redirection
investigation, is retained as a sanitized historical record in
[the historical testing log](docs/history/TESTING_PRE_BETA1.md). Its older names,
paths and results describe their original source snapshots.
