# Update transaction and recovery worker

The desktop coordinator stages a copy of its current single-file executable as
the worker. The worker rechecks the signed release envelope, exact payload size
and SHA-256, distribution, version, private directory ownership and original
application identity before accepting a handoff. It runs after normal Windows
shell startup normalization and before workspace preferences or audio startup.

Transactions live in `%LOCALAPPDATA%\VineMix\updates\<transaction-id>`. Only
the current user has access through the staging directory ACL. Links, junctions,
path traversal and inventory entries for settings or logs are rejected. These
ACLs protect other-account access; another program running as the same user is
not a separate security boundary. Signed release information binds executable
bytes independently of editable IPC markers.

The original UI remains running while the worker acquires the per-user global
update mutex, shared across Windows logon sessions, and
publishes its ready marker. The coordinator must then strictly flush edits,
cooperatively stop its exact engine and checkpoint the settled workspace before
committing. The worker waits for the original UI and engine PID/start-time
identities to exit. It never terminates either process. Abandoned, uncommitted
handoffs can be cancelled without changing application or workspace files.

For an installed copy, both the exact AppId ownership marker and its existing
per-user uninstall registration must identify the original application's
directory. The prior `SetupSupport/package-manifest.json` supplies the explicit
payload inventory. The backup adds only the exact manifest, `installation.ini`
and owned `unins000.exe`, `.dat` and optional `.msg` bookkeeping. Older copies
without that inventory require manual Setup repair; ownership is not guessed
from a folder name. Setup receives fixed silent, no-restart, accepted-terms and
no-driver flags and the existing directory. Startup and shortcut preferences
remain the installer's responsibility to preserve.

Beta 5 also passes a `setup.log` destination inside the
private transaction folder and retains a nonzero Setup exit code in the journal.
This distinguishes installer failures from later package or health failures;
older delivered workers do not gain this logging retroactively. Keep raw logs
private because installer output can contain personal paths. Inno Setup's
[logging option](https://jrsoftware.org/ishelp/topic_setupcmdline.htm) and
[exit codes](https://jrsoftware.org/ishelp/topic_setupexitcodes.htm) were checked
on 24 October 2026. Logging does not change rollback or process ownership rules.

For a standalone copy, the verified new executable is copied beside the exact
original launcher and installed using `File.Replace`, retaining the old
launcher. Private recovery storage can be on another volume. Replacement and
rollback recheck file hashes; a newer or unfamiliar payload file is preserved.
Only explicitly inventoried application files and owned uninstall registration
participate in rollback. Settings checkpoint copies are retained privately and
are never restored over user data, including when health fails.

Candidate checks include expected desktop/engine file versions, installed
inventory integrity and an inert `--release-smoke` invocation. Successful inert
smoke is insufficient to commit. The candidate must register its exact process
identity, load the same saved workspace, confirm the compatible engine protocol
and observe advancing audio frames before reporting health. Editing remains
disabled until the worker durably commits that report. Failed recovery waits for
both the candidate UI and its engine to exit normally; a live engine prevents
rollback rather than being killed.
After a known successful rollback, the worker records the terminal checkpoint,
clears its recovery registration and releases the global update mutex before
reopening the verified previous launcher with the same physical workspace path.
An unsafe or incomplete recovery never reopens it. If reopening fails, rollback
remains complete and its private journal asks for manual reopening.

Journal files are flushed and replaced at phase boundaries. Safe wait, backup,
candidate-health and rollback checkpoints can resume. A copied worker is also
registered for bounded per-user RunOnce recovery, with at most three attempts;
normal desktop startup inspects unfinished transactions as an additional path.
RunOnce execution is subject to Windows logon behavior and is not a guarantee of
immediate recovery. Committed backups and failed transaction evidence remain
available for inspection. More than 4096 transaction directories produces an
actionable history-archiving error, rather than silently ignoring later entries.
Protected terminal journals are checked without rehashing retired payloads on
every launch. Unfinished entries still undergo full signed-byte verification.
Ordinary desktop/engine ownership remains scoped to its Windows session; the
global update mutex serializes workers and does not claim cross-session engine
ownership. Installed updates also depend on Setup's existing process guard.

There is no atomic transaction spanning Setup's files, uninstall database and
registry. If installation is interrupted before a trustworthy postimage can be
recorded, installed-copy recovery retains the preimage and asks for manual
repair. It does not overwrite unknown changes. Standalone replacement can resume
when the launcher still matches either its recorded original bytes or its
signed candidate bytes. These are recoverable checkpoints, not a promise of
atomic rollback through every power loss.

Research checked 21 October 2026: Microsoft documents
[File.Replace](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.replace)
and the [Run/RunOnce lifecycle](https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys).
Inno Setup documents its [fixed command-line options](https://jrsoftware.org/ishelp/topic_setupcmdline.htm).
Microsoft also documents [global kernel object namespaces](https://learn.microsoft.com/en-us/windows/win32/termserv/kernel-object-namespaces).
Meaningful fixture tests cover signed-byte tampering, malformed metadata, ACLs,
process-exit gates, standalone success, installed Setup failure, health failure,
hash-conflict preservation, resumable waits and idempotent rollback. Real Setup
and delivered executable testing in an isolated environment remain separate
qualification steps; fixture success does not claim those steps were run.
