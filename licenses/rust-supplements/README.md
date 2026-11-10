# Reviewed licence supplements for Rust crates

`tools/rust-notices.py` builds `licenses/Rust-crates.txt` from the licence
files each published crate contains. A few crates publish none. For those the
generator stops unless a supplement here says which licence applies and
supplies its text. It never writes a licence text of its own.

Each `<crate>.txt` is a header:

| Field | Meaning |
|---|---|
| `Crate` | The crate the supplement is for. |
| `Manifest-License` | The crate's licence expression when the supplement was written. If the crate's manifest changes, the generator stops until the supplement is reviewed again. |
| `Used-Under` | The licence VineMix uses the crate under. It must be one the manifest offers. |
| `Text` | A file in this directory holding that licence's text. Without it, the text follows the header after a blank line. |
| `Basis` | Why this text is the right one. It is printed in the crate's entry. |
| `Review` | Who confirmed the supplement against the crate's source repository. `Pending ...` means nobody has yet; the generator lists those on every run. |

`Apache-2.0.txt` is the Apache License 2.0 as published at
<https://www.apache.org/licenses/LICENSE-2.0.txt> (SHA-256
`cfc7749b96f63bd31c3c42b5c471bf756814053e847c10f3eb003417bc523d30`), copied
unchanged from a dependency that ships it.

The supplements written on 10 November 2026 all choose Apache-2.0, because its
text is the same for every work and could be reproduced without access to the
upstream repositories. Their `Review` fields are still `Pending`: confirm each
against its repository, and replace the choice with the upstream MIT text
where that is preferred.

A supplement is removed when its crate is no longer shipped or starts
publishing its own licence file; the generator reports both.
