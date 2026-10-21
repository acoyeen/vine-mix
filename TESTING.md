# VineMix testing

For the supplied **0.3.0-beta.1** files, start with the
[beta qualification record](docs/BETA_IMPLEMENTATION.md) and
[tester guide](docs/BETA_TESTER_GUIDE.md#useful-beta-checks). The record identifies
the exact files, recorded checks, known issues and unperformed qualification.
Its passed checks describe the separately built candidate; this documentation
refresh does not rerun them or make an unfinished release gate pass.

The source currently committed here predates the beta application integration.
See [source status](README.md#source-status). To verify that committed snapshot:

```powershell
pwsh -File .\scripts\verify-offline.ps1
```

The source suite uses synthetic audio, simulated devices and temporary data.
It does not qualify the supplied beta, establish audible reliability or replace
real-device, clean-machine, desktop-shell launch or sustained-use checks.
Ordinary development must preserve the running audio engine and personal data.

The [pre-beta testing log](docs/history/TESTING_PRE_BETA1.md) preserves the testing
document from the earlier committed source. Its commands, names and results
belong to those historical snapshots; they are not a current beta procedure.
