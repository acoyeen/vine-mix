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
An identical printed path can therefore name different settings files. Vine Mix
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
