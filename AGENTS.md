# Naming

Use `vine-mix` for the repository, Codex project and checkout folder. Use
**VineMix** for the product name in interfaces, documentation and releases.
Keep language-appropriate identifiers such as `VineMix.Desktop` and `VINEMIX_*`.
Preserve older identifiers only for compatibility and dated historical evidence;
see `docs/REBRAND_COMPATIBILITY.md`.

# Project idea notes

Use `docs/IDEAS.md` as the durable log for future development ideas.

When the maintainer shares a rough idea or explicitly asks to note something for later:

- Read the existing log and relevant project documentation or code first.
- Turn the idea into a clear description of the user need and proposed behavior,
  preserving the original intent. Label assumptions and unresolved questions.
- Research feasibility and relevant alternatives when useful. Verify external
  technical claims with primary sources and record links and the research date.
  Distinguish evidence from inference; say when research has not been done.
- Add or update an entry with a stable ID, date, status, rationale, likely scope,
  tradeoffs, dependencies, and a practical next step. Keep simple ideas concise.
- Merge related notes into existing entries and retain meaningful changes in
  direction. Do not silently import every older planning document into the log.
- Treat logged ideas as proposals for future development. Recording or researching
  an idea does not authorize implementing it, adding dependencies, or changing
  the running application. Follow explicit implementation requests separately.
- Briefly confirm what was recorded and link to the entry. Ask for clarification
  only when the missing information materially changes the proposal; otherwise
  record a reasonable assumption and continue.

Do not treat unrelated questions or current bug-fix requests as future ideas
unless the conversation makes that intent clear.

# Known issue notes

Use `docs/KNOWN_ISSUES.md` as the durable log for reported bugs and interface
defects. Keep these separate from future development ideas. Read relevant code
or documentation, retain stable issue IDs, and distinguish reported behavior
from verified reproduction and suspected causes. Record the expected behavior,
status, date and practical next step. Logging an issue does not itself authorize
implementation; follow explicit fix requests separately. Confirm new entries
with a link.

# Tweak notes

Use `docs/TWEAKS.md` for small refinements to existing UI, controls and behaviour.
Keep new capabilities in the ideas log and broken behaviour in known issues,
regardless of how small its fix is. Read the relevant code, retain stable tweak
IDs, record the intended change, rationale, date, status and next step, and
preserve corrections to the original request. Recording a tweak does not itself
authorize implementation. Distinguish local verification from a published release
and link newly recorded entries when confirming them.

# Local interface updates

The maintainer has requested that verified local interface updates proceed without
repeated Saved/idle questions. Keep process/hash checks, backups and ordinary
file-stability checks, and preserve the running audio engine and personal
settings. Do not describe this preference as confirmation that pending edits
were independently checked.

# Project privacy

Keep the maintainer's real name, personal account paths, geographical details,
hardware inventories and personal workspace data out of project documentation,
fixtures, screenshots, Git history and distributed packages. Use neutral
maintainer wording, portable paths and clearly labelled example devices.
Preserve required third-party attribution and valid pseudonymous project links.
Store raw personal diagnostic evidence and recovery backups outside the source
checkout; retain only sanitized summaries in the project. Audit archive contents
and metadata before exporting or uploading them.

# Windows launch and settings context

The Codex execution environment can redirect `%LOCALAPPDATA%\AudioMixer` to
Codex's private `LocalCache` even when a process reports no package identity.
An identical printed path can therefore name different settings files. VineMix
normalizes ordinary desktop startup through the Windows shell before loading
preferences or starting its engine. Keep this behavior intact.

For live verification, compare the physical settings path from an open file
handle, not just the printed path or a settings hash from this tool environment.
Verify an actual Explorer/shortcut launch. Do not start the personal engine
directly from a redirected tool process. The activation scripts reject that
context before changing application processes. Preserve both copies before any
recovery, and never replace a newer workspace with an older recovery snapshot.

# Product releases and repository visibility

Treat VineMix as a released, MIT-licensed open-source product in beta. Prepare
and publish normal downloadable GitHub prereleases, release notes, checksums,
matching source and signed update metadata as versions are released. Keep the
update and release paths ready before the maintainer changes repository visibility.

Repository visibility is a separate maintainer decision: keep it private until
explicitly instructed to change it. This does not mean releases should remain
drafts or downloads should be replaced with manual maintainer-only distribution.
Do not repeatedly explain access restrictions or frame product documentation as
a private test handoff. Use normal product, download and update guidance. State
actual beta limitations and verification results accurately, without treating
private visibility itself as a blocker to release preparation or publication.

This release direction supersedes older plans that kept release material
unpublished solely because the repository was private. Do not publish signing
keys, personal diagnostics, recovery backups or other private workspace data.

## Beta 2 finalisation hold — 22 October 2026

The current uploaded `v0.3.0-beta.2` is provisional. The maintainer has explicitly
said it is not final and will give the word when it is finalised. Wait for that
explicit go-ahead before replacing its uploaded files or treating a later build
as the final Beta 2. Do not infer approval from completed tests or elapsed time.
On approval, replace the Beta 2 downloads, signed update metadata, checksums and
release notes as one verified release update. Preserve Beta 1's version history.

## Beta 3 cleanup and release — 23 October 2026

The maintainer authorized a clean Beta 3 release, removal of all experimental
window-resize patches and labs from active source, and a codebase audit. Keep
normal Windows/WPF resizing and all unrelated Beta 2 features. Preserve the
earlier version tags and downloads; this new release does not finalize or
replace Beta 2. Do not use computer-use tools for this work.

## Live development target — 28 October 2026

The maintainer's installed live app is the ongoing testing target for implemented
Beta 6 changes. Keep the canonical app and engine release identity at
`0.3.0-beta.6` (`0.3.0.7`) during this development cycle, with matching source,
build receipts and installed package inventory. Include verified branding and
interface changes from concurrent work without overwriting them.

Continue verified local interface updates without repeated Saved/idle questions,
using the existing process, hash, backup, physical-settings and Windows-shell
launch safeguards. Preserve the running engine for interface-only updates. A
release transition that requires restarting the engine needs explicit permission
for the brief audio interruption; do not weaken release-identity checks.

Beta 6 remains in development until the maintainer explicitly finalizes it.
Local testing does not authorize publication, replacing earlier downloads or
changing repository visibility. On finalization, qualify and package the final
source and binaries together, with normal release notes, checksums and signed
update metadata. Record local integration evidence in
`docs/BETA6_DEVELOPMENT.md` separately from published release qualification.

## 0.4.0 Beta release authorization — 28 October 2026

The maintainer authorized an end-to-end cleanup, qualification and publication
of **VineMix 0.4.0 Beta**, superseding the Beta 6 development/finalization hold.
Use `0.4.0-beta`, Windows version `0.4.0.0`, and display `0.4.0 · Beta`.
Future beta releases increment the actual version and keep the unnumbered Beta
label. Preserve dated Beta 6 evidence and existing compatibility identifiers.

After the new release and website downloads are verified, remove only the
manually uploaded attachments from Beta 1–5, retaining recovery copies outside
the checkout, release pages, tags and historical notes. Add accurate removal
notices. This supersedes earlier instructions to preserve those attachments;
it does not finalize or replace Beta 2. Retained tags still expose GitHub's
automatic source archives, and existing licence permissions remain intact.

Preserve the current repository visibility. The full release and attribution
plan is in `docs/RELEASE_0.4.0_BETA.md`. Live installation still requires explicit
permission for its brief engine/audio restart, with the physical-settings,
Windows-shell, backup and matching-release safeguards intact.
